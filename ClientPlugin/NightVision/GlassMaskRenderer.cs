using System;
using System.Collections.Generic;
using System.IO;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using VRage.FileSystem;
using VRage.Render11.Common;
using VRage.Render11.GeometryStage2.Rendering;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.NightVision;

/// <summary>Records pixels drawn with SE's GLASS technique for passive night vision.</summary>
public static class GlassMaskRenderer
{
    private static readonly Dictionary<(int, bool, bool), MyPixelShaders.Id> OldShaders = new();
    private static readonly Dictionary<(int, bool), MyShaderBundle> NewBundles = new();
    private static readonly RenderTargetView[] OitTargets = new RenderTargetView[3];
    private static readonly RenderTargetView[] StandardTargets = new RenderTargetView[3];

    private static string shaderPath;
    private static IBlendState oitBlend;
    private static IBlendState standardBlend;
    private static IBorrowedRtvTexture mask;
    private static bool failed;

    [ThreadStatic]
    private static bool recording;

    [ThreadStatic]
    // GeometryStage2 consumes this when it chooses the rasterizer state for the same draw.
    public static bool CaptureBackface;

    public static bool Recording => recording && mask != null;

    public static void Initialize()
    {
        try
        {
            var directory = Path.Combine(MyFileSystem.UserDataPath, "Storage", Plugin.Name);
            Directory.CreateDirectory(directory);

            var sourcePath = Path.Combine(directory, "GlassMask.source.hlsl");
            shaderPath = Path.Combine(directory, "GlassMask.flat.hlsl");
            var source = File.ReadAllText(
                Path.Combine(MyShaderCompiler.ShadersPath, "Geometry", "Materials", "Glass", "Pixel.hlsl")
            );

            const string declarations = "#include \"Declarations.hlsli\"";
            const string pixelStage = "#include <Geometry/Passes/PixelStage.hlsli>";
            if (!source.Contains(declarations) || !source.Contains(pixelStage))
                throw new InvalidOperationException("The game's glass shader has changed");

            source = source
                .Replace(
                    declarations,
                    "#include <Geometry/Materials/Glass/Declarations.hlsli>"
                )
                .Replace(pixelStage, MaskPixelStage);
            File.WriteAllText(sourcePath, source);
            ShaderFlattener.Flatten(sourcePath, shaderPath);
        }
        catch (Exception e)
        {
            shaderPath = null;
            Disable(e);
        }
    }

    public static void BeginFrame(MyRenderContext rc)
    {
        recording = false;
        try
        {
            mask?.Release();
            mask = null;
            if (failed || !NightVisionRenderer.NeedsGlassMask || shaderPath == null)
                return;

            int samples = MyRender11.DebugOverrides.OIT ? 1 : MyGBuffer.Main.SamplesCount;
            int quality = MyRender11.DebugOverrides.OIT ? 0 : MyGBuffer.Main.SamplesQuality;
            mask = MyManagers.RwTexturesPool.BorrowRtv(
                "NightVision.GlassMask",
                MyRender11.ResolutionI.X,
                MyRender11.ResolutionI.Y,
                Format.R32G32_Float,
                samples,
                quality
            );
            rc.ClearRtv(mask, new RawColor4(0f, 0f, 0f, 0f));
            recording = true;
        }
        catch (Exception e)
        {
            Disable(e);
        }
    }

    public static void EndFrame() => recording = false;

    public static void BindOit(
        MyRenderContext rc,
        IDepthStencil depth,
        IUavTexture accumulation,
        IUavTexture coverage
    )
    {
        if (mask == null)
            return;

        try
        {
            EnsureBlendStates();
            OitTargets[0] = accumulation.Rtv;
            OitTargets[1] = coverage.Rtv;
            OitTargets[2] = mask.Rtv;
            rc.SetRtvs(depth.DsvRoDepth, OitTargets);
            rc.SetBlendState(oitBlend);
        }
        catch (Exception e)
        {
            Disable(e);
        }
    }

    public static void BindStandard(MyRenderContext rc, IDepthStencil depth)
    {
        if (mask == null)
            return;

        try
        {
            EnsureBlendStates();
            StandardTargets[0] = MyGBuffer.Main.LBuffer.Rtv;
            StandardTargets[1] = null;
            StandardTargets[2] = mask.Rtv;
            rc.SetRtvs(depth.DsvRoDepth, StandardTargets);
            rc.SetBlendState(standardBlend);
        }
        catch (Exception e)
        {
            Disable(e);
        }
    }

    public static IBorrowedRtvTexture TakeMask()
    {
        var result = mask;
        mask = null;
        return result;
    }

    public static MyPixelShaders.Id GetOldShader(
        MyMaterialShadersBundleId bundle,
        bool captureBackface,
        bool pairedGlass
    )
    {
        var key = (bundle.Index, captureBackface, pairedGlass);
        if (OldShaders.TryGetValue(key, out var shader))
            return shader;

        var info = bundle.BundleInfo;
        var macros = new List<ShaderMacro>
        {
            MyMaterialShaders.GetRenderingPassMacro(info.Pass.String),
        };
        MyMaterialShaders.AddMaterialShaderFlagMacrosTo(macros, info.Flags, info.TextureTypes);
        macros.AddRange(info.Layout.Info.Macros);
        if (captureBackface)
            macros.Add(new ShaderMacro("NV_CAPTURE_BACKFACE", null));
        if (pairedGlass)
            macros.Add(new ShaderMacro("NV_PAIRED_GLASS", null));
        shader = MyPixelShaders.Create(shaderPath, macros.ToArray());
        OldShaders[key] = shader;
        return shader;
    }

    public static MyShaderBundle GetNewBundle(MyShaderBundle bundle, bool captureBackface)
    {
        var key = (bundle.PixelShader.Index, captureBackface);
        if (NewBundles.TryGetValue(key, out var masked))
            return masked;

        var info = MyShaders.GetCompilationInfo(bundle.PixelShader.InfoId);
        var macros = new List<ShaderMacro>(info.Macros);
        if (captureBackface)
            macros.Add(new ShaderMacro("NV_CAPTURE_BACKFACE", null));
        masked = new MyShaderBundle();
        masked.Init(
            MyPixelShaders.Create(shaderPath, macros.ToArray()),
            bundle.VertexShader,
            bundle.InputLayout
        );
        NewBundles[key] = masked;
        return masked;
    }

    public static bool IsMainTransparentBundle(MyShaderBundle bundle)
    {
        foreach (var macro in MyShaders.GetCompilationInfo(bundle.PixelShader.InfoId).Macros)
            if (macro.Name == "RENDERING_PASS")
                return macro.Definition == "5";

        return false;
    }

    public static void Disable(Exception error)
    {
        if (failed)
            return;

        failed = true;
        recording = false;
        MyLog.Default.Error(
            $"{Plugin.Name}: Glass masking failed, passive night vision has been disabled: {error}"
        );
    }

    private static void EnsureBlendStates()
    {
        if (oitBlend != null)
            return;

        var oit = MyBlendStateManager.BlendWeightedTransparency.Description.Clone();
        EnableMaskTarget(ref oit);
        oitBlend = MyManagers.BlendStates.CreateResource("NightVision.GlassMaskOIT", ref oit);

        var standard = MyBlendStateManager.BlendAlphaPremult.Description.Clone();
        standard.IndependentBlendEnable = true;
        EnableMaskTarget(ref standard);
        standardBlend = MyManagers.BlendStates.CreateResource(
            "NightVision.GlassMaskStandard",
            ref standard
        );
    }

    private static void EnableMaskTarget(ref BlendStateDescription description)
    {
        description.IndependentBlendEnable = true;
        description.RenderTarget[2].IsBlendEnabled = true;
        description.RenderTarget[2].SourceBlend = BlendOption.One;
        description.RenderTarget[2].DestinationBlend = BlendOption.One;
        description.RenderTarget[2].BlendOperation = BlendOperation.Maximum;
        description.RenderTarget[2].SourceAlphaBlend = BlendOption.One;
        description.RenderTarget[2].DestinationAlphaBlend = BlendOption.One;
        description.RenderTarget[2].AlphaBlendOperation = BlendOperation.Maximum;
        description.RenderTarget[2].RenderTargetWriteMask =
            ColorWriteMaskFlags.Red | ColorWriteMaskFlags.Green;
    }

    private const string MaskPixelStage = """
        #include <Geometry/Passes/Transparent/Declarations.hlsli>

        #define OIT
        #include <Transparent/OIT/Globals.hlsli>

        void __pixel_shader(VertexStageOutput vertex, bool isFrontFace : SV_IsFrontFace, out float4 accumTarget : SV_TARGET0, out float4 coverageTarget : SV_TARGET1, out float2 glassDepth : SV_TARGET2)
        {
            PixelInterface pixel;
            pixel.screen_position = vertex.position.xyz;
            pixel.custom = vertex.custom;
            init_ps_interface(pixel);

        #ifdef PASS_OBJECT_VALUES_THROUGH_STAGES
            pixel.key_color = vertex.key_color;
            pixel.custom_alpha = vertex.custom_alpha;
        #endif

        #ifdef USE_SIMPLE_INSTANCING_COLORING
            pixel.key_color = vertex.instance_key_color_dithering.xyz;
            pixel.custom_alpha = float2(pixel.custom_alpha.x, vertex.instance_key_color_dithering.w);
        #endif

            MaterialOutputInterface material_output = make_mat_interface();
            pixel_program(pixel, material_output);

            float4 resultColor = float4(material_output.base_color, material_output.transparency);
        #ifdef CUSTOM_DEPTH
            TransparentColorOutput(resultColor, material_output.depth, vertex.position.z, 1.0f, accumTarget, coverageTarget);
        #else
            TransparentColorOutput(resultColor, 0, vertex.position.z, 1.0f, accumTarget, coverageTarget);
        #endif
        #ifdef NV_CAPTURE_BACKFACE
            // The extra back face is for the transmission mask, not normal glass rendering.
            if (!isFrontFace)
            {
                accumTarget = 0;
                coverageTarget = 0;
            }
        #endif
        #ifdef NV_PAIRED_GLASS
            glassDepth = vertex.position.zz;
        #else
            // The dark/outside side of SE's one-way glass uses a strong additive alpha.
            glassDepth = TransparentConstants.ColorAdd.w < 0.5
                ? float2(vertex.position.z, 0)
                : float2(0, vertex.position.z);
        #endif
        }
        """;
}
