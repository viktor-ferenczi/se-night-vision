using System;
using ClientPlugin.NightVision;
using HarmonyLib;
using Sandbox.Game.Gui;
using VRage.Utils;

namespace ClientPlugin.Patches;

/// <summary>
/// InitHudStatControls builds the HUD's stat controls from MyHud.HudDefinition, on every HUD
/// recreation. The night vision icon is added to the definition right before that.
/// </summary>
[HarmonyPatch(typeof(MyGuiScreenHudSpace), "InitHudStatControls")]
public static class HudStatControlsPatch
{
    private static bool failed;

    public static void Prefix()
    {
        if (failed)
            return;

        try
        {
            NightVisionHud.Apply();
        }
        catch (Exception e)
        {
            failed = true;
            NightVisionHud.Restore();
            MyLog.Default.Error(
                $"{Plugin.Name}: Failed to add the HUD indicator, disabling it for this session: {e}"
            );
        }
    }
}
