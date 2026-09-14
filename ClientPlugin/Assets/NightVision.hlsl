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
    float4 Look;        // x = outline strength, y = noise, z = vignette, w = highlight washout
    float4 Anim;        // x = blend (fade), y = flash, z = time in seconds, w = 1 when masking the cockpit interior
    float4 BoxCenter;   // xyz = camera-relative center of the cockpit interior box
    float4 BoxAxisX;    // xyz = unit axis, w = half extent along it
    float4 BoxAxisY;
    float4 BoxAxisZ;
    float4 Extra;       // x = natural light threshold, y = sky gain, z = crease lines, w = IR fallback
};

Texture2D<float4> SceneTex    : register(t20); // copy of the light buffer (HDR, not yet exposed)
Texture2D<float>  DepthTex    : register(t21); // resolved hardware depth (reversed projection)
Texture2D<float4> Gbuffer1Tex : register(t22); // xy = packed view-space normal
Texture2D<float2> ExposureTex : register(t23); // 1x1, g = log2 exposure (see Postprocess/Defines.hlsli)
Texture2D<float4> Gbuffer0Tex : register(t24); // rgb = linear base color

static const float3 LuminanceWeights = float3(0.2126, 0.7152, 0.0722);
static const float SkyDepth = 1e6;
static const float InfraredRange = 50;

float Hash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

bool InsideInteriorBox(float3 position)
{
    float3 d = position - BoxCenter.xyz;
    return abs(dot(d, BoxAxisX.xyz)) <= BoxAxisX.w
        && abs(dot(d, BoxAxisY.xyz)) <= BoxAxisY.w
        && abs(dot(d, BoxAxisZ.xyz)) <= BoxAxisZ.w;
}

float LinearDepth(int2 texel)
{
    float hw = DepthTex.Load(int3(texel, 0));
    // Reversed projection: 0 is the far plane (sky), which has no geometry to outline.
    return hw <= 0 ? SkyDepth : compute_depth(hw);
}

float3 Normal(int2 texel)
{
    return unpack_normals2(Gbuffer1Tex.Load(int3(texel, 0)).xy);
}

float EdgeStrength(int2 texel, float centerDepth)
{
    if (centerDepth >= SkyDepth)
        return 0;

    float l = LinearDepth(texel + int2(-1, 0));
    float r = LinearDepth(texel + int2(1, 0));
    float u = LinearDepth(texel + int2(0, -1));
    float d = LinearDepth(texel + int2(0, 1));

    // Second derivative of depth, relative to the distance: flat and sloped surfaces
    // both give ~0, only depth discontinuities (silhouettes) stand out. The geometry
    // side of a silhouette against the sky gets the full line, so horizons are traced.
    float laplacian = abs(l + r - 2 * centerDepth) + abs(u + d - 2 * centerDepth);
    float depthEdge = saturate(laplacian / (centerDepth * 0.02 + 0.05) - 0.1);

    // Creases and panel lines: normals that change direction between neighbors,
    // checked on both sides so the line is centered and reads as a contour.
    float3 n = Normal(texel);
    float bend = (1 - dot(n, Normal(texel + int2(1, 0))))
               + (1 - dot(n, Normal(texel + int2(-1, 0))))
               + (1 - dot(n, Normal(texel + int2(0, 1))))
               + (1 - dot(n, Normal(texel + int2(0, -1))));
    float normalEdge = saturate(bend * 2 - 0.15);
    // Fade with distance, where the normals of rough terrain turn into noise.
    normalEdge *= saturate(1.5 - centerDepth / 1500);

    // Crease lines are weighted separately: at 0 only silhouettes and real steps between surfaces
    // are outlined, not the bevels every armor block has along its borders.
    return max(depthEdge, normalEdge * Extra.z);
}

void __pixel_shader(PostprocessVertex input, out float4 output : SV_Target0)
{
    int2 texel = int2(input.position.xy);
    float2 uv = input.uv;

    float4 scene = SceneTex.Load(int3(texel, 0));
    float centerDepth = LinearDepth(texel);

    if (Anim.w > 0.5)
    {
        // Cockpit source: the interior, the pilot and anything else inside the block's
        // box are not seen through the glass, so they keep their normal look.
        float hw = DepthTex.Load(int3(texel, 0));
        if (hw > 0 && InsideInteriorBox(ReconstructWorldPosition(hw, uv)))
        {
            output = scene;
            return;
        }
    }

    float exposure = exp2(ExposureTex.Load(int3(0, 0, 0)).g);
    float3 exposed = scene.rgb * exposure;
    float luminance = dot(exposed, LuminanceWeights);
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
    // mask so outlines, grain, vignette, flash and the IR illuminator leave light areas alone.
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
        sensorLuminance += Extra.w * rangeFade * irReflectance * lerp(0.25, 1, facing);
    }

    // Amplified luminance, colour dropped. The soft knee lifts the shadows and levels off
    // below 1, so moderately lit surfaces do not turn into a glaring band before the natural
    // color takes over. The sky gets much less gain: space stays black with the stars showing.
    float amplified = sensorLuminance * TintGain.w;
    if (sky)
        amplified *= Extra.y;
    else
        amplified = 1 - exp(-1.3 * pow(amplified, 0.75));

    // Sensor overload: only real light sources (already brighter than white before any gain)
    // are pushed further, so they bloom and wash out.
    amplified += Look.w * max(luminance - 1, 0);

    // Grain, stronger in the dark like a real intensifier tube, animated per frame.
    float grain = Hash(float2(texel) + frac(Anim.z * 7.13) * 1024) - 0.5;
    amplified = max(amplified + grain * Look.y * (0.06 + 0.25 * amplified), 0);

    float3 nightVision = TintGain.rgb * amplified;

    // Bright contour lines, drawn as a light cyan glow on top of the tinted image.
    // The right and lower neighbors' edges count at a third of their strength, which widens the
    // lines to about 1.3 pixels: one full pixel with a faint second one.
    float edgeRaw = max(EdgeStrength(texel, centerDepth),
                        0.33 * max(EdgeStrength(texel + int2(1, 0), LinearDepth(texel + int2(1, 0))),
                                   EdgeStrength(texel + int2(0, 1), LinearDepth(texel + int2(0, 1)))));
    float edge = edgeRaw * Look.x;
    float3 edgeColor = lerp(TintGain.rgb, 1, 0.35);
    nightVision += edgeColor * edge * 0.6;

    // Vignette.
    float2 fromCenter = (uv - 0.5) * float2(frame_.Screen.resolution.x / frame_.Screen.resolution.y, 1);
    nightVision *= 1 - Look.z * smoothstep(0.35, 0.95, length(fromCenter));

    // Switch-on flash.
    nightVision = nightVision * (1 + Anim.y * 3) + TintGain.rgb * Anim.y * 0.6;

    float3 result = lerp(exposed, nightVision, saturate(Anim.x * darkness));

    output = float4(result / max(exposure, 1e-6), scene.a);
}
