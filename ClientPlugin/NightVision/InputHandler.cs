using System.Diagnostics;
using Sandbox;
using Sandbox.Game;
using VRage.Input;

namespace ClientPlugin.NightVision;

/// <summary>
/// Holds keyboard light presses until release. A short press is passed to vanilla; a long press
/// toggles night vision without toggling the light.
/// </summary>
public static class InputHandler
{
    private static bool inGameplayInput;
    private static bool pendingTap;
    private static long pressStart = -1;
    private static bool holdFired;

    public static bool InGameplayInput => inGameplayInput;

    public static void BeginFrame(bool inputDisabled)
    {
        inGameplayInput = true;
        pendingTap = false;

        if (
            inputDisabled
            || MySandboxGame.IsPaused
            || NightVisionController.EvaluateMode() == NightVisionMode.None
        )
        {
            pressStart = -1;
            return;
        }

        TrackLongPress(MyInput.Static);
    }

    public static void EndFrame()
    {
        inGameplayInput = false;
        pendingTap = false;
    }

    private static void TrackLongPress(IMyInput input)
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
            if (!holdFired && held >= Config.Current.LongPressSeconds)
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
        if (NightVisionController.EvaluateMode() == NightVisionMode.None)
            return vanilla;

        var input = MyInput.Static;
        bool keyNewPressed = input.IsNewGameControlPressed(MyControlsSpace.HEADLIGHTS);

        // The gamepad binding is not remapped, it keeps working as in vanilla
        bool gamepad = vanilla && !keyNewPressed;

        if (pendingTap)
        {
            pendingTap = false;
            return true;
        }

        return gamepad;
    }
}
