using System;
using ClientPlugin.NightVision;
using HarmonyLib;
using Sandbox.Game;
using Sandbox.Game.Gui;
using VRage.Input;
using VRage.Utils;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyGuiScreenGamePlay), nameof(MyGuiScreenGamePlay.HandleUnhandledInput))]
public static class HandleUnhandledInputPatch
{
    public static void Prefix()
    {
        InputHandler.BeginFrame(MyGuiScreenGamePlay.DisableInput);
    }

    public static Exception Finalizer(Exception __exception)
    {
        InputHandler.EndFrame();
        return __exception;
    }
}

[HarmonyPatch(typeof(MyControllerHelper), nameof(MyControllerHelper.IsControl))]
public static class IsControlPatch
{
    public static void Postfix(
        MyStringId controlId,
        MyControlStateType type,
        bool joystickOnly,
        ref bool __result
    )
    {
        if (
            !InputHandler.InGameplayInput
            || controlId != MyControlsSpace.HEADLIGHTS
            || type != MyControlStateType.NEW_PRESSED
            || joystickOnly
        )
            return;

        __result = InputHandler.FilterLight(__result);
    }
}

[HarmonyPatch(typeof(MyGuiScreenGamePlay), nameof(MyGuiScreenGamePlay.UnloadData))]
public static class GamePlayUnloadPatch
{
    public static void Postfix()
    {
        NightVisionController.Reset();
    }
}
