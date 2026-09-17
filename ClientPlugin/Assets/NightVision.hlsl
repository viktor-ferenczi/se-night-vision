// Night vision post-process pass.
//
// Compiled by the game's shader registry (entry point __pixel_shader, profile ps_5_0).
// Runs right after eye adaptation, before bloom and tone mapping, and writes into the
// HDR light buffer. It works in exposed space (the same space the tone mapper sees), so
// the look does not depend on what the eye adaptation happens to settle on. Bright
// sources end up far above 1 there, which is what makes them bloom and wash out.

#include <Frame.hlsli>
#include <Postprocess/PostprocessBase.hlsli>
#include <VertexTransformations.hlsli>

cbuffer NightVisionConstants : register(b8)
{
    float4 TintGain;    // rgb = tint, w = luminance gain
    float4 Look;        // x = outline strength, y = noise, z = vignette
    float4 Anim;        // x = on/off blend, y = flash, z = time, w = active-mode progress
    float4 Transition;  // x = flash active-only pixels, y = sliding visor, z = foliage highlights
    float4 Extra;       // x = natural light threshold, y = crease lines, z = IR fallback
    float4 FogVision;   // rgb = fog-vision tint, w = remaining visibility where it begins
};

Texture2D<float4> SceneTex    : register(t20); // copy of the light buffer (HDR, not yet exposed)
Texture2D<float>  DepthTex    : register(t21); // resolved hardware depth (reversed projection)
Texture2D<float4> Gbuffer1Tex : register(t22); // xy = packed view-space normal
Texture2D<float2> ExposureTex : register(t23); // 1x1, g = log2 exposure (see Postprocess/Defines.hlsli)
Texture2D<float4> Gbuffer0Tex : register(t24); // rgb = linear base color, a = model LOD/tree marker
Texture2D<float4> Gbuffer2Tex : register(t25); // a = multisample coverage; foliage writes zero
Texture2D<float4> SensorTex   : register(t26); // opaque lighting rendered without fog or weather
Texture2D<float2> GlassMask   : register(t27); // nearest clear-side depth, nearest dark-side depth

static const float3 LuminanceWeights = float3(0.2126, 0.7152, 0.0722);
static const float SkyDepth = 1e6;
static const float InfraredRange = 50;
static const float AtmosphereDistanceMin = 1000;
static const float LodDepthTolerance = 0.05;
static const float GlassLayerTolerance = 0.05;

float Hash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float LinearDepth(int2 texel)
{
    float hw = DepthTex.Load(int3(texel, 0));
    // Reversed projection: 0 is the far plane (sky), which has no geometry to outline.
    return hw <= 0 ? SkyDepth : compute_depth(hw);
}

float FogAmount(float depth, bool sky)
{
    if (sky)
        return saturate(frame_.Fog.sky);

    return saturate(frame_.Fog.mult * (1 - exp(-clamp(depth, 0, 1000) * frame_.Fog.density)));
}

float3 Normal(int2 texel)
{
    return unpack_normals2(Gbuffer1Tex.Load(int3(texel, 0)).xy);
}

uint ModelLod(int2 texel)
{
    return (uint)(Gbuffer0Tex.Load(int3(texel, 0)).a * 255 + 0.5);
}

bool IsFoliage(int2 texel, float depth, uint lod)
{
    return depth < SkyDepth
        && (Gbuffer2Tex.Load(int3(texel, 0)).a == 0
            || lod == 254);
}

bool IsLodBlend(float centerDepth, float neighborDepth, uint centerLod, uint neighborLod)
{
    // Vanilla interleaves both meshes during an LOD fade. Ignore nearby cross-LOD samples,
    // but retain large depth changes as real silhouettes between separate surfaces.
    return centerLod != neighborLod
        && neighborDepth < SkyDepth
        && abs(neighborDepth - centerDepth)
            < max(centerDepth, neighborDepth) * LodDepthTolerance + 0.05;
}

float MaskedDepthEdge(
    float centerDepth,
    float2 depths,
    bool2 foliage,
    bool centerFoliage,
    bool2 lodBlend)
{
    if (any(lodBlend))
        return 0;

    if (centerFoliage || !any(foliage))
        return abs(depths.x + depths.y - 2 * centerDepth);

    // An edge involving foliage is valid only when the foliage is behind solid geometry.
    return max(0, max(foliage.x ? depths.x - centerDepth : 0,
                      foliage.y ? depths.y - centerDepth : 0));
}

float EdgeStrength(int2 texel, float centerDepth)
{
    if (centerDepth >= SkyDepth)
        return 0;

    float l = LinearDepth(texel + int2(-1, 0));
    float r = LinearDepth(texel + int2(1, 0));
    float u = LinearDepth(texel + int2(0, -1));
    float d = LinearDepth(texel + int2(0, 1));
    uint centerLod = ModelLod(texel);
    uint ll = ModelLod(texel + int2(-1, 0));
    uint rl = ModelLod(texel + int2(1, 0));
    uint ul = ModelLod(texel + int2(0, -1));
    uint dl = ModelLod(texel + int2(0, 1));
    bool centerFoliage = IsFoliage(texel, centerDepth, centerLod);
    bool lc = IsFoliage(texel + int2(-1, 0), l, ll);
    bool rc = IsFoliage(texel + int2(1, 0), r, rl);
    bool uc = IsFoliage(texel + int2(0, -1), u, ul);
    bool dc = IsFoliage(texel + int2(0, 1), d, dl);
    bool4 lodBlend = bool4(
        IsLodBlend(centerDepth, l, centerLod, ll),
        IsLodBlend(centerDepth, r, centerLod, rl),
        IsLodBlend(centerDepth, u, centerLod, ul),
        IsLodBlend(centerDepth, d, centerLod, dl));

    // Second derivative of depth, relative to the distance: flat and sloped surfaces
    // both give ~0, only depth discontinuities (silhouettes) stand out. The geometry
    // side of a silhouette against the sky gets the full line, so horizons are traced.
    float laplacian = MaskedDepthEdge(
        centerDepth, float2(l, r), bool2(lc, rc), centerFoliage, lodBlend.xy)
        + MaskedDepthEdge(
            centerDepth, float2(u, d), bool2(uc, dc), centerFoliage, lodBlend.zw);
    float depthEdge = saturate(laplacian / (centerDepth * 0.02 + 0.05) - 0.1);

    // Creases and panel lines: normals that change direction between neighbors,
    // checked on both sides so the line is centered and reads as a contour.
    float3 n = Normal(texel);
    float bend = (1 - dot(n, rc == centerFoliage && rl == centerLod ? Normal(texel + int2(1, 0)) : n))
               + (1 - dot(n, lc == centerFoliage && ll == centerLod ? Normal(texel + int2(-1, 0)) : n))
               + (1 - dot(n, dc == centerFoliage && dl == centerLod ? Normal(texel + int2(0, 1)) : n))
               + (1 - dot(n, uc == centerFoliage && ul == centerLod ? Normal(texel + int2(0, -1)) : n));
    float normalEdge = saturate(bend * 2 - 0.15);
    // Fade with distance, where the normals of rough terrain turn into noise.
    normalEdge *= saturate(1.5 - centerDepth / 1500);

    // Crease lines are weighted separately: at 0 only silhouettes and real steps between surfaces
    // are outlined, not the bevels every armor block has along its borders.
    return max(depthEdge, normalEdge * Extra.y) * (centerFoliage ? Transition.z : 1);
}

void __pixel_shader(PostprocessVertex input, out float4 output : SV_Target0)
{
    int2 texel = int2(input.position.xy);
    float2 uv = input.uv;

    float4 scene = SceneTex.Load(int3(texel, 0));
    float centerDepth = LinearDepth(texel);

    float activeCoverage = Anim.w;
    if (Transition.y > 0.5 && Anim.w > 0 && Anim.w < 1)
        activeCoverage = 1 - smoothstep(Anim.w - 0.015, Anim.w + 0.015, uv.y);

    float glassCoverage = 0;
    if (Anim.w < 1)
    {
        float2 glassDepth = GlassMask.Load(int3(texel, 0));
        // Sloped inside/outside meshes can cross slightly along a non-planar join. Compare in
        // linear space so their small separation does not make the mask alternate at close range.
        glassCoverage = all(glassDepth > 0)
            && compute_depth(glassDepth.x) <= compute_depth(glassDepth.y) + GlassLayerTolerance
            ? 1 : 0;
    }
    float activeOnlyCoverage = activeCoverage * (1 - glassCoverage);
    if (glassCoverage <= 0 && activeOnlyCoverage <= 0)
    {
        output = scene;
        return;
    }

    float exposure = exp2(ExposureTex.Load(int3(0, 0, 0)).g);
    float3 exposed = scene.rgb * exposure;
    float3 sensorExposed = SensorTex.Load(int3(texel, 0)).rgb * exposure;
    float luminance = dot(sensorExposed, LuminanceWeights);
    bool sky = centerDepth >= SkyDepth;

    // Remove the material's base color from the visible response to estimate lighting. This keeps
    // black paint in daylight from looking like an unlit surface. Sky has no G-buffer material.
    float baseLuminance = 1;
    float lightLevel = luminance;
    if (!sky)
    {
        baseLuminance = dot(Gbuffer0Tex.Load(int3(texel, 0)).rgb, LuminanceWeights);
        lightLevel /= max(baseLuminance, 1e-4);
    }

    // Apply the sensor only where the estimated lighting is dark. The final composite uses this
    // mask so outlines, grain, vignette and the IR illuminator leave light areas alone.
    float darkness = 1;
    if (Extra.x > 0)
        darkness = 1 - smoothstep(0, Extra.x, lightLevel);

    // A wide, camera-mounted IR flood supplies a signal when visible lighting has none. The base
    // color contributes texture but has a floor because visible-black paint need not absorb IR.
    float sensorLuminance = luminance;
    if (!sky)
    {
        float3 viewRay = compute_screen_ray(uv);
        float rangeFade = saturate(1 - length(centerDepth * viewRay) / InfraredRange);
        rangeFade *= rangeFade;
        float facing = saturate(dot(Normal(texel), -normalize(viewRay)));
        float irReflectance = lerp(0.1, 1, saturate(baseLuminance));
        sensorLuminance += Extra.z * rangeFade * irReflectance * lerp(0.25, 1, facing);
    }

    // Amplified luminance, colour dropped. The soft knee lifts the shadows and levels off
    // below 1, so moderately lit surfaces do not turn into a glaring band before the natural
    // color takes over. The sky is not amplified, so space stays black with the stars showing.
    float amplified = sky ? 0 : 1 - exp(-1.3 * pow(sensorLuminance * TintGain.w, 0.75));

    // Grain, stronger in the dark like a real intensifier tube, animated per frame.
    float grain = Hash(float2(texel) + frac(Anim.z * 7.13) * 1024) - 0.5;
    amplified = max(amplified + grain * Look.y * (0.06 + 0.25 * amplified), 0);

    float3 nightVision = TintGain.rgb * amplified;
    float3 weatherVision = FogVision.rgb * amplified;

    // Bright contour lines, drawn as a light cyan glow on top of the tinted image.
    // The right and lower neighbors' edges count at a third of their strength, which widens the
    // lines to about 1.3 pixels. Foliage scale applies again here to reduce that widening too.
    float rightDepth = LinearDepth(texel + int2(1, 0));
    float downDepth = LinearDepth(texel + int2(0, 1));
    float rightWidth = IsFoliage(
        texel + int2(1, 0),
        rightDepth,
        ModelLod(texel + int2(1, 0))) ? Transition.z : 1;
    float downWidth = IsFoliage(
        texel + int2(0, 1),
        downDepth,
        ModelLod(texel + int2(0, 1))) ? Transition.z : 1;
    float edgeRaw = max(EdgeStrength(texel, centerDepth),
                        0.33 * max(rightWidth * EdgeStrength(texel + int2(1, 0), rightDepth),
                                   downWidth * EdgeStrength(texel + int2(0, 1), downDepth)));
    float edge = edgeRaw * Look.x;
    float3 edgeColor = lerp(TintGain.rgb, 1, 0.35);
    float3 weatherEdgeColor = lerp(FogVision.rgb, 1, 0.35);
    nightVision += edgeColor * edge * 0.6;
    weatherVision += weatherEdgeColor * edge * 0.6;

    // Vignette.
    float2 fromCenter = (uv - 0.5) * float2(frame_.Screen.resolution.x / frame_.Screen.resolution.y, 1);
    float vignette = 1 - Look.z * smoothstep(0.35, 0.95, length(fromCenter));
    nightVision *= vignette;
    weatherVision *= vignette;

    // Keep the weathered scene intact. Darkness uses the existing screen overlay, while fog
    // independently adds a blue sensor signal once visibility falls below its configured limit.
    // Planetary atmospheres are a separate additive pass and do not contribute to frame_.Fog.
    // Past the pass's distance cutoff, treat veiling luminance absent from the clean sensor as
    // another loss of visibility. This includes the Alien planet's dense Rayleigh/Mie haze.
    float veilLuminance = dot(max(exposed - sensorExposed, 0), LuminanceWeights);
    float atmosphereObscuration = centerDepth >= AtmosphereDistanceMin
        ? veilLuminance / max(luminance + veilLuminance, 1e-4)
        : 0;
    float visibility = 1 - max(FogAmount(centerDepth, sky), atmosphereObscuration);
    float badWeather = FogVision.w > 0
        ? saturate((FogVision.w - visibility) / FogVision.w)
        : 0;
    float greenWeight = darkness * (1 - badWeather);
    float3 vision = nightVision * (1 - saturate(exposed)) * greenWeight
        + weatherVision * badWeather;
    float3 flashColor = TintGain.rgb * (1 - saturate(exposed)) * greenWeight
        + FogVision.rgb * badWeather;
    float coverage = glassCoverage + activeOnlyCoverage;
    float activeFlash = Transition.x > 0.5 ? (1 - Anim.w) * (1 - Anim.w) : 0;
    float flashCoverage = glassCoverage * Anim.y
        + activeOnlyCoverage * max(Anim.y, activeFlash);
    float3 result = exposed + saturate(Anim.x)
        * (coverage * vision + flashCoverage * (vision * 3 + flashColor * 0.6));

    output = float4(result / max(exposure, 1e-6), scene.a);
}
