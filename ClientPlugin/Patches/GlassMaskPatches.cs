using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.NightVision;
using HarmonyLib;
using VRage.Render11.GeometryStage2.Common;
using VRage.Render11.GeometryStage2.Model.Preprocess;
using VRage.Render11.GeometryStage2.RenderPass;
using VRage.Render11.GeometryStage2.Rendering;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRageRender;
using VRageRender.Import;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyTransparentRendering), "Render")]
public static class TransparentRenderPatch
{
    public static void Prefix(MyRenderContext __0)
    {
        GlassMaskRenderer.BeginFrame(__0);
    }

    public static Exception Finalizer(Exception __exception)
    {
        GlassMaskRenderer.EndFrame();
        return __exception;
    }
}

[HarmonyPatch(typeof(MyTransparentRendering), "SetupOIT")]
public static class TransparentOitTargetsPatch
{
    public static void Postfix(
        MyRenderContext __0,
        IDepthStencil __1,
        IUavTexture __2,
        IUavTexture __3
    )
    {
        GlassMaskRenderer.BindOit(__0, __1, __2, __3);
    }
}

[HarmonyPatch(typeof(MyTransparentRendering), "SetupStandard")]
public static class TransparentStandardTargetsPatch
{
    public static void Postfix(MyRenderContext __0, IDepthStencil __1)
    {
        GlassMaskRenderer.BindStandard(__0, __1);
    }
}

[HarmonyPatch(
    typeof(MyTransparentModelPass),
    "RecordCommandsInternal",
    typeof(MyRenderableProxy)
)]
public static class OldGlassDrawPatch
{
    [Flags]
    private enum GlassFix
    {
        None = 0,
        CaptureBackface = 1,
        ImplicitPair = 2,
    }

    // These legacy materials need behavior their models do not provide to the mask pass.
    private static readonly Dictionary<string, GlassFix> Exceptions = new(StringComparer.Ordinal)
    {
        // Its interior model omits the dark pane and winds the clear pane away from the pilot.
        ["CockpitIndustrialGlassInside"] = GlassFix.CaptureBackface | GlassFix.ImplicitPair,
    };

    [ThreadStatic]
    private static bool pairedGlass;

    private static readonly MethodInfo Original = AccessTools.Method(
        typeof(MyRenderUtils),
        nameof(MyRenderUtils.BindShaderBundle)
    );
    private static readonly MethodInfo Replacement = AccessTools.Method(
        typeof(OldGlassDrawPatch),
        nameof(BindShaderBundle)
    );

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(Original))
            {
                instruction.operand = Replacement;
                replaced++;
            }
            yield return instruction;
        }

        if (replaced != 1)
            GlassMaskRenderer.Disable(
                new InvalidOperationException($"Expected one old glass shader binding, found {replaced}")
            );
    }

    public static void Prefix(
        MyTransparentModelPass __instance,
        MyRenderableProxy __0,
        out bool __state
    )
    {
        var fix = GlassFix.None;
        if (GlassMaskRenderer.Recording && __0.Material.Info.Technique == MyMeshDrawTechnique.GLASS)
            Exceptions.TryGetValue(__0.Material.Info.Name.ToString(), out fix);

        pairedGlass = (fix & GlassFix.ImplicitPair) != 0;
        GlassMaskRenderer.CaptureBackface =
            (fix & GlassFix.CaptureBackface) != 0
            || (
                GlassMaskRenderer.Recording
                && __0.Material.Info.Technique == MyMeshDrawTechnique.GLASS
                && MyTransparentMaterials.TryGetMaterial(__0.Material.Info.Name, out var material)
                && material.TriangleFaceCulling
                && material.ColorAdd.W >= 0.5f
            );

        __state = GlassMaskRenderer.CaptureBackface;
        if (__state)
            __instance.RC.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
    }

    public static void Postfix(MyTransparentModelPass __instance, bool __state)
    {
        GlassMaskRenderer.CaptureBackface = false;
        pairedGlass = false;
        if (!__state)
            return;

        __instance.RC.SetRasterizerState(
            MyRender11.Settings.Wireframe ? MyRasterizerStateManager.WireframeRasterizerState : null
        );
    }

    public static void BindShaderBundle(MyRenderContext rc, MyMaterialShadersBundleId id)
    {
        if (!GlassMaskRenderer.Recording)
        {
            MyRenderUtils.BindShaderBundle(rc, id);
            return;
        }

        var info = id.BundleInfo;
        if (
            info.Material != MyMaterialShaders.GLASS_MATERIAL_TAG
            || info.Pass != MyMaterialShaders.TRANSPARENT_MODEL_PASS_ID
        )
        {
            MyRenderUtils.BindShaderBundle(rc, id);
            return;
        }

        try
        {
            rc.SetInputLayout(id.IL);
            rc.VertexShader.Set(id.VS);
            rc.PixelShader.Set(
                GlassMaskRenderer.GetOldShader(
                    id,
                    GlassMaskRenderer.CaptureBackface,
                    pairedGlass
                )
            );
        }
        catch (Exception e)
        {
            GlassMaskRenderer.Disable(e);
            MyRenderUtils.BindShaderBundle(rc, id);
        }
    }
}

[HarmonyPatch(typeof(MyTransparentRenderPass), "DrawInstanceLodGroup")]
public static class NewGlassDrawPatch
{
    private static readonly MethodInfo Original = AccessTools.Method(
        typeof(MyPreprocessedPart),
        nameof(MyPreprocessedPart.GetShaderBundle)
    );
    private static readonly MethodInfo Replacement = AccessTools.Method(
        typeof(NewGlassDrawPatch),
        nameof(GetShaderBundle)
    );
    private static readonly FieldInfo OriginalCulling = AccessTools.Field(
        typeof(MyTransparentMaterial),
        nameof(MyTransparentMaterial.TriangleFaceCulling)
    );
    private static readonly MethodInfo ReplacementCulling = AccessTools.Method(
        typeof(NewGlassDrawPatch),
        nameof(UseFaceCulling)
    );

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int replaced = 0;
        int cullingReplaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(Original))
            {
                instruction.operand = Replacement;
                replaced++;
            }
            else if (instruction.LoadsField(OriginalCulling))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = ReplacementCulling;
                cullingReplaced++;
            }
            yield return instruction;
        }

        if (replaced != 1 || cullingReplaced != 1)
            GlassMaskRenderer.Disable(
                new InvalidOperationException(
                    $"Expected one new glass shader binding and culling check, found {replaced} and {cullingReplaced}"
                )
            );
    }

    public static bool UseFaceCulling(MyTransparentMaterial material)
    {
        bool captureBackface = GlassMaskRenderer.CaptureBackface;
        GlassMaskRenderer.CaptureBackface = false;
        // Dark back faces complete the mask pair but remain invisible in the color targets.
        return material.TriangleFaceCulling && !captureBackface;
    }

    public static MyShaderBundle GetShaderBundle(
        ref MyPreprocessedPart part,
        MyInstanceLodState state,
        bool metalnessColorable
    )
    {
        GlassMaskRenderer.CaptureBackface = false;
        var bundle = part.GetShaderBundle(state, metalnessColorable);
        if (
            GlassMaskRenderer.Recording
            && part.Material.Technique == MyMeshDrawTechnique.GLASS
            && GlassMaskRenderer.IsMainTransparentBundle(bundle)
        )
        {
            bool captureBackface =
                part.Material.Material.TriangleFaceCulling
                && part.Material.Material.ColorAdd.W >= 0.5f;
            try
            {
                var masked = GlassMaskRenderer.GetNewBundle(bundle, captureBackface);
                GlassMaskRenderer.CaptureBackface = captureBackface;
                return masked;
            }
            catch (Exception e)
            {
                GlassMaskRenderer.Disable(e);
            }
        }

        return bundle;
    }
}
