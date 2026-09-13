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

public enum NightVisionSource
{
    None,
    Helmet,
    Cockpit,
}

/// <summary>
/// Game-thread state machine: the activation toggle, which source applies to the current view
/// and the fade animation. Runs once per drawn frame and publishes a snapshot for the renderer.
/// </summary>
public static class NightVisionController
{
    // One shared toggle for the suit and the cockpit, so the state follows the player in and out of seats
    public static bool Activated { get; private set; }

    public static NightVisionSource Source { get; private set; }
    public static bool Available { get; private set; }
    public static bool Rendering => blend > 0f;

    private static float blend;
    private static float flash;
    private static bool wasOn;
    private static bool loggedActivated;
    private static bool loggedLight;
    private static long lastTimestamp;
    private static NightVisionSnapshot lastSnapshot;

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
        wasOn = false;
        lastSnapshot = null;
        NightVisionRenderer.Publish(null);
    }

    /// <summary>
    /// One step of the Cycle activation mode for a tap of the light key.
    /// Returns whether vanilla should toggle the light.
    /// </summary>
    public static bool CycleStep()
    {
        bool lightOn = IsLightOn(MySession.Static?.ControlledEntity);

        if (Activated)
        {
            // Night vision -> Off. Also switch the light off if something turned it back on.
            Activated = false;
            if (lightOn)
                return true;

            MyGuiAudio.PlaySound(MyGuiSounds.HudClick);
            return false;
        }

        if (lightOn)
        {
            // Light -> Night vision with the light off, or straight to Off without a source
            EvaluateSource(out _, out bool available);
            if (available)
                Activated = true;
            return true;
        }

        // Off -> Light. A seat on a grid without spotlights has no light to cycle through:
        // vanilla's toggle does nothing there, so go straight to night vision.
        if (!HasLight(MySession.Static?.ControlledEntity))
        {
            EvaluateSource(out _, out bool hasSource);
            if (hasSource)
            {
                Activated = true;
                MyGuiAudio.PlaySound(MyGuiSounds.HudClick);
                return false;
            }
        }

        return true;
    }

    public static void Update()
    {
        long now = Stopwatch.GetTimestamp();
        float dt =
            lastTimestamp == 0 ? 0f : (float)((now - lastTimestamp) / (double)Stopwatch.Frequency);
        lastTimestamp = now;
        dt = Math.Min(dt, 0.1f);

        var cockpit = EvaluateSource(out var source, out bool available);
        bool light = IsLightOn(MySession.Static?.ControlledEntity);
        if (
            source != Source
            || available != Available
            || Activated != loggedActivated
            || light != loggedLight
        )
        {
            loggedActivated = Activated;
            loggedLight = light;
            MyLog.Default.WriteLine(
                $"{Plugin.Name}: activated={Activated} source={source} available={available} light={light} controlled={MySession.Static?.ControlledEntity?.GetType().Name} cockpit={cockpit?.BlockDefinition?.Id.SubtypeName} helmetEnabled={MySession.Static?.LocalCharacter?.OxygenComponent?.HelmetEnabled}"
            );
        }

        Source = source;
        Available = available;

        var config = Config.Current;
        bool on = Activated && available;
        float fade = Math.Max(config.FadeSeconds, 0.001f);

        if (source == NightVisionSource.None)
        {
            // Third person, remote control, turrets and spectator are not seen through a visor or
            // glass: off from the first frame, no fade. Coming back is not a switch-on, so no flash.
            blend = 0f;
            flash = 0f;
            wasOn = Activated;
        }

        if (on && !wasOn)
            flash = 1f;
        wasOn = on;

        blend = MathHelper.Clamp(blend + (on ? dt : -dt) / fade, 0f, 1f);
        flash = Math.Max(flash - dt / (fade * 1.5f), 0f);

        if (blend <= 0f)
        {
            lastSnapshot = null;
            NightVisionRenderer.Publish(null);
            return;
        }

        // While fading out after the source went away, keep the last known masking
        var snapshot = new NightVisionSnapshot { Blend = blend, Flash = flash * flash };

        if (on)
        {
            if (source == NightVisionSource.Cockpit && cockpit != null)
            {
                var glass = GlassDetector.Get(cockpit.BlockDefinition);
                var world = cockpit.WorldMatrix;
                snapshot.MaskInterior = true;
                snapshot.BoxCenter = Vector3D.Transform(glass.InteriorBox.Center, world);
                snapshot.BoxAxisX = world.Right;
                snapshot.BoxAxisY = world.Up;
                snapshot.BoxAxisZ = world.Backward;
                snapshot.BoxHalfExtents = glass.InteriorBox.HalfExtents;
            }
        }
        else if (lastSnapshot != null)
        {
            snapshot.MaskInterior = lastSnapshot.MaskInterior;
            snapshot.BoxCenter = lastSnapshot.BoxCenter;
            snapshot.BoxAxisX = lastSnapshot.BoxAxisX;
            snapshot.BoxAxisY = lastSnapshot.BoxAxisY;
            snapshot.BoxAxisZ = lastSnapshot.BoxAxisZ;
            snapshot.BoxHalfExtents = lastSnapshot.BoxHalfExtents;
        }

        lastSnapshot = snapshot;
        NightVisionRenderer.Publish(snapshot);
    }

    /// <summary>
    /// Decides which source provides night vision for the current view. Returns the cockpit the
    /// local player looks out of when the source is its glass.
    /// </summary>
    public static MyCockpit EvaluateSource(out NightVisionSource source, out bool available)
    {
        source = NightVisionSource.None;
        available = false;

        var session = MySession.Static;
        var controlled = session?.ControlledEntity;
        if (controlled == null || !ReferenceEquals(session.CameraController, controlled))
            return null;

        // First person only; this also rules out every spectator mode
        if (session.GetCameraControllerEnum() != MyCameraControllerEnum.Entity)
            return null;

        switch (controlled)
        {
            case MyCharacter character:
                source = NightVisionSource.Helmet;
                available = IsHelmetClosed(character);
                return null;

            // Includes cryo chambers, beds and passenger seats
            case MyCockpit cockpit:
                if (GlassDetector.Get(cockpit.BlockDefinition).HasGlass)
                {
                    source = NightVisionSource.Cockpit;
                    available = true;
                    return cockpit;
                }

                source = NightVisionSource.Helmet;
                available = IsHelmetClosed(cockpit.Pilot ?? session.LocalCharacter);
                return null;

            // Remote controlled ships, turrets and anything else
            default:
                return null;
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

    private static bool HasLight(IMyControllableEntity entity)
    {
        switch (entity)
        {
            case MyShipController controller:
                return controller.GridReflectorLights != null
                    && controller.GridReflectorLights.ReflectorsEnabled
                        != MyMultipleEnabledEnum.NoObjects;
            default:
                return entity != null;
        }
    }
}
