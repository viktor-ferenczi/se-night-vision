using ClientPlugin.NightVision;
using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches;

// DrawGameScene calls exactly one of these per frame, right after the HDR scene is complete and
// before bloom and tone mapping read it. The exposure they write is what the shader normalizes by.

[HarmonyPatch(typeof(MyEyeAdaptation), nameof(MyEyeAdaptation.Run))]
public static class EyeAdaptationRunPatch
{
    public static void Postfix()
    {
        NightVisionRenderer.Apply();
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
