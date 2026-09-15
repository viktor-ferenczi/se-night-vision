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

[HarmonyPatch(typeof(MyGuiScreenGamePlay), nameof(MyGuiScreenGamePlay.Draw))]
public static class GamePlayDrawPatch
{
    private static bool failed;
    public static void Postfix()
    {
        if (failed)
            return;

        try
        {
            NightVisionController.Update();
        }
        catch (Exception e)
        {
            failed = true;
            NightVisionController.Reset();
            MyLog.Default.Error(
                $"{Plugin.Name}: Night vision update failed, disabling it for this session: {e}"
            );
        }
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
