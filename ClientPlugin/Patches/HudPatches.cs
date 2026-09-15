using System;
using ClientPlugin.NightVision;
using HarmonyLib;
using Sandbox.Game.Gui;
using VRage.Utils;

namespace ClientPlugin.Patches;

/// <summary>
/// InitHudStatControls builds controls from the active HUD definition. Replace its light styles
/// immediately beforehand so vanilla and modded layouts keep ownership of the icon geometry.
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
                $"{Plugin.Name}: Failed to replace the HUD light icon, disabling it for this session: {e}"
            );
        }
    }
}
