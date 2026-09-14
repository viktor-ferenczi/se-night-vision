using ClientPlugin.NightVision;
using HarmonyLib;
using VRage.Render11.LightingStage;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRageRender;

namespace ClientPlugin.Patches;

// DrawGameScene calls exactly one of these per frame, right after the HDR scene is complete and
// before bloom and tone mapping read it. The exposure they write is what the shader normalizes by.

[HarmonyPatch(typeof(MyEyeAdaptation), nameof(MyEyeAdaptation.Run))]
public static class EyeAdaptationRunPatch
{
    public static void Prefix(ref ISrvTexture __1)
    {
        NightVisionRenderer.UseSensorForExposure(ref __1);
    }

    public static void Postfix()
    {
        NightVisionRenderer.Apply();
    }
}

[HarmonyPatch(typeof(MyLightsRendering), "RenderDirectionalEnvironmentLight")]
public static class DirectionalLightPatch
{
    public static void Prefix(MyRenderContext __0, out bool __state)
    {
        __state = NightVisionRenderer.BeginDirectionalLight(__0);
    }

    public static void Postfix(MyRenderContext __0, ISrvTexture __1, bool __state)
    {
        if (__state)
            NightVisionRenderer.EndDirectionalLight(__0, __1);
    }
}

[HarmonyPatch(typeof(MyEyeAdaptation), nameof(MyEyeAdaptation.ConstantExposure))]
public static class EyeAdaptationConstantExposurePatch
{
    public static void Postfix()
    {
        NightVisionRenderer.Apply();
    }
}
