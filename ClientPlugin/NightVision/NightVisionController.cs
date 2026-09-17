using System;
using System.Diagnostics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.GUI;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.Graphics;
using VRage;
using VRage.Audio;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using IMyControllableEntity = Sandbox.Game.Entities.IMyControllableEntity;

namespace ClientPlugin.NightVision;

public enum NightVisionMode
{
    None,
    Passive,
    Active,
}

/// <summary>
/// Game-thread state machine: the activation toggle, visor mode and animation.
/// </summary>
public static class NightVisionController
{
    public static bool Activated { get; private set; }

    public static NightVisionMode Mode { get; private set; }
    public static bool Rendering => blend > 0f;

    private static float blend;
    private static float flash;
    private static float activeBlend;
    private static bool sliding;
    private static bool wasOn;
    private static bool? wasHelmetClosed;
    private static bool loggedActivated;
    private static bool loggedLight;
    private static long lastTimestamp;

    public static void Toggle()
    {
        Activated = !Activated;
        MyGuiAudio.PlaySound(MyGuiSounds.HudClick);
    }

    public static void Reset()
    {
        Activated = false;
        blend = 0f;
        flash = 0f;
        activeBlend = 0f;
        sliding = false;
        wasOn = false;
        wasHelmetClosed = null;
        Mode = NightVisionMode.None;
        NightVisionRenderer.Publish(null);
    }

    public static void Update()
    {
        long now = Stopwatch.GetTimestamp();
        float dt =
            lastTimestamp == 0 ? 0f : (float)((now - lastTimestamp) / (double)Stopwatch.Frequency);
        lastTimestamp = now;
        dt = Math.Min(dt, 0.1f);

        var previousMode = Mode;
        var mode = EvaluateMode();
        var character = MySession.Static?.LocalCharacter;
        bool? helmetClosed = character == null ? null : IsHelmetClosed(character);
        bool helmetChanged = helmetClosed.HasValue
            && wasHelmetClosed.HasValue
            && helmetClosed.Value != wasHelmetClosed.Value;
        wasHelmetClosed = helmetClosed;
        bool light = IsLightOn(MySession.Static?.ControlledEntity);
        if (mode != Mode || Activated != loggedActivated || light != loggedLight)
        {
            loggedActivated = Activated;
            loggedLight = light;
            MyLog.Default.WriteLine(
                $"{Plugin.Name}: activated={Activated} mode={mode} light={light} controlled={MySession.Static?.ControlledEntity?.GetType().Name} helmetEnabled={MySession.Static?.LocalCharacter?.OxygenComponent?.HelmetEnabled}"
            );
        }

        Mode = mode;

        var config = Config.Current;
        bool on = Activated && mode != NightVisionMode.None;
        float duration = Math.Max(config.FadeSeconds, 0.001f);
        float step = dt / duration;

        if (on && wasOn && mode != previousMode)
            sliding = helmetChanged;

        if (mode != NightVisionMode.None && ((!wasOn && on) || blend <= 0f))
        {
            activeBlend = mode == NightVisionMode.Active ? 1f : 0f;
            sliding = false;
        }
        else if (on)
            activeBlend = MathHelper.Clamp(
                activeBlend + (mode == NightVisionMode.Active ? step : -step),
                0f,
                1f
            );
        if (on && !wasOn)
            flash = 1f;

        blend = MathHelper.Clamp(blend + (on ? step : -step), 0f, 1f);
        flash = Math.Max(flash - step / 1.5f, 0f);
        wasOn = on;

        if (blend <= 0f)
        {
            NightVisionRenderer.Publish(null);
            return;
        }

        var snapshot = new NightVisionSnapshot
        {
            Blend = blend,
            Flash = flash * flash,
            ActiveBlend = activeBlend,
            FlashActive = !sliding && mode == NightVisionMode.Active,
            Sliding = sliding,
        };

        NightVisionRenderer.Publish(snapshot);
    }

    public static NightVisionMode EvaluateMode()
    {
        var session = MySession.Static;
        if (session == null)
            return NightVisionMode.None;

        if (session.GetCameraControllerEnum() != MyCameraControllerEnum.Entity)
            return Config.Current.SpectatorMode == VisionMode.Active
                ? NightVisionMode.Active
                : NightVisionMode.Passive;

        var camera = session.CameraController;
        switch (camera)
        {
            case MyCharacter character:
                return EvaluateHelmetMode(character, false);
            case MyCockpit cockpit:
                return EvaluateHelmetMode(cockpit.Pilot ?? session.LocalCharacter, true);
            default:
                if (camera == null)
                    return NightVisionMode.None;
                return Config.Current.CameraMode == VisionMode.Active
                    ? NightVisionMode.Active
                    : NightVisionMode.Passive;
        }
    }

    private static NightVisionMode EvaluateHelmetMode(MyCharacter character, bool inVehicle)
    {
        if (!IsHelmetClosed(character))
            return NightVisionMode.Passive;

        return EvaluateHelmetSetting(inVehicle);
    }

    private static NightVisionMode EvaluateHelmetSetting(bool inVehicle)
    {
        switch (Config.Current.HelmetMode)
        {
            case HelmetVisionMode.Passive:
                return NightVisionMode.Passive;
            case HelmetVisionMode.Active:
                return NightVisionMode.Active;
            default:
                return inVehicle ? NightVisionMode.Passive : NightVisionMode.Active;
        }
    }

    private static bool IsHelmetClosed(MyCharacter character)
    {
        return character?.OxygenComponent?.HelmetEnabled ?? false;
    }

    public static bool IsLightOn(IMyControllableEntity entity)
    {
        switch (entity)
        {
            case MyCharacter character:
                return character.LightEnabled;
            case MyShipController controller:
                // Vanilla's SwitchLights treats Mixed as on and switches everything off.
                // A grid without reflectors reports NoObjects, which is not a light.
                var state =
                    controller.GridReflectorLights?.ReflectorsEnabled
                    ?? MyMultipleEnabledEnum.NoObjects;
                return state == MyMultipleEnabledEnum.AllEnabled
                    || state == MyMultipleEnabledEnum.Mixed;
            default:
                return false;
        }
    }

}
