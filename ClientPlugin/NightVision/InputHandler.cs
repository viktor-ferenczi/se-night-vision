using System.Diagnostics;
using Sandbox;
using Sandbox.Game;
using Sandbox.Game.World;
using VRage.Input;

namespace ClientPlugin.NightVision;

/// <summary>
/// The three activation modes. Vanilla toggles the light where MyGuiScreenGamePlay.HandleUnhandledInput
/// asks MyControllerHelper.IsControl(context, HEADLIGHTS); FilterLight rewrites that answer, so the
/// click sound and the replay record vanilla attaches to it stay consistent with what happened.
/// </summary>
public static class InputHandler
{
    private static bool inGameplayInput;
    private static bool suppressLight;
    private static bool pendingTap;
    private static long pressStart = -1;
    private static bool holdFired;

    public static bool InGameplayInput => inGameplayInput;

    public static void BeginFrame(bool inputDisabled)
    {
        inGameplayInput = true;
        suppressLight = false;
        pendingTap = false;

        var session = MySession.Static;
        if (inputDisabled || session?.ControlledEntity == null || MySandboxGame.IsPaused)
        {
            pressStart = -1;
            return;
        }

        var config = Config.Current;
        var input = MyInput.Static;
        bool spectator = session.IsCameraUserControlledSpectator();

        if (
            (config.ActivationMode == ActivationMode.Hotkey || config.HotkeyAlwaysActive)
            && IsHotkeyNewPressed(config, input)
        )
        {
            NightVisionController.Toggle();

            // Shift+L also reads as the light key to vanilla. The spectator light is left alone.
            suppressLight = !spectator;
            pressStart = -1;
            return;
        }

        if (config.ActivationMode == ActivationMode.LongPress && !spectator)
            TrackLongPress(config, input);
        else
            pressStart = -1;
    }

    public static void EndFrame()
    {
        inGameplayInput = false;
        suppressLight = false;
        pendingTap = false;
    }

    private static bool IsHotkeyNewPressed(Config config, IMyInput input)
    {
        if (config.Hotkey.Key != MyKeys.None)
            return config.Hotkey.HasPressed(input);

        // Default: Shift plus whatever the light control is bound to, resolved at runtime
        if (
            !input.IsAnyShiftKeyPressed()
            || input.IsAnyCtrlKeyPressed()
            || input.IsAnyAltKeyPressed()
        )
            return false;

        var control = input.GetGameControl(MyControlsSpace.HEADLIGHTS);
        if (control == null)
            return false;

        return IsNewPressed(input, control.GetKeyboardControl())
            || IsNewPressed(input, control.GetSecondKeyboardControl());
    }

    private static bool IsNewPressed(IMyInput input, MyKeys key)
    {
        return key != MyKeys.None && input.IsNewKeyPressed(key);
    }

    private static void TrackLongPress(Config config, IMyInput input)
    {
        if (input.IsNewGameControlPressed(MyControlsSpace.HEADLIGHTS))
        {
            pressStart = Stopwatch.GetTimestamp();
            holdFired = false;
        }

        if (pressStart < 0)
            return;

        if (input.IsGameControlPressed(MyControlsSpace.HEADLIGHTS))
        {
            double held = (Stopwatch.GetTimestamp() - pressStart) / (double)Stopwatch.Frequency;
            if (!holdFired && held >= config.LongPressSeconds)
            {
                holdFired = true;
                NightVisionController.Toggle();
            }

            return;
        }

        // Released: a short tap is handed to vanilla on this frame
        if (!holdFired)
            pendingTap = true;
        pressStart = -1;
    }

    /// <summary>Rewrites vanilla's answer to "was the light key pressed" for this frame.</summary>
    public static bool FilterLight(bool vanilla)
    {
        var session = MySession.Static;
        if (session == null || session.IsCameraUserControlledSpectator())
            return vanilla;

        if (suppressLight)
            return false;

        var input = MyInput.Static;
        bool keyNewPressed = input.IsNewGameControlPressed(MyControlsSpace.HEADLIGHTS);

        // The gamepad binding is not remapped, it keeps working as in vanilla
        bool gamepad = vanilla && !keyNewPressed;

        switch (Config.Current.ActivationMode)
        {
            case ActivationMode.LongPress:
                if (pendingTap)
                {
                    pendingTap = false;
                    return true;
                }

                return gamepad;

            case ActivationMode.Cycle:
                if (!keyNewPressed)
                    return gamepad;

                return NightVisionController.CycleStep();

            default:
                return vanilla;
        }
    }
}
