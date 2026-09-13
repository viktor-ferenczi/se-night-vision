using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Sandbox.Definitions.GUI;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI;
using VRage.Game;
using VRage.Game.Definitions;
using VRage.Game.GUI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.NightVision;

/// <summary>1 while night vision is activated and renders, 0 otherwise (off or on standby).</summary>
public class NightVisionHudStat : MyStatBase
{
    public static readonly MyStringHash StatName = MyStringHash.GetOrCompute("night_vision");

    public NightVisionHudStat()
    {
        Id = StatName;
    }

    public override void Update()
    {
        CurrentValue = NightVisionController.Activated && NightVisionController.Available ? 1f : 0f;
    }
}

/// <summary>
/// Adds the night vision icon to the vanilla HUD by cloning the light icon's styles, so size, color,
/// fading and HUD scaling match the other state icons. Only the base game's Default HUD with the
/// light icon at its vanilla slot is changed; any other HUD gets no indicator at all.
/// </summary>
public static class NightVisionHud
{
    // Set at plugin init (asset folder or extracted embedded copy)
    public static volatile string IconPath;

    private static readonly MyStringHash IconTexture = MyStringHash.GetOrCompute(
        "NightVisionGoggles"
    );
    private static readonly MyStringHash BackgroundTexture = MyStringHash.GetOrCompute(
        "IconsBackground"
    );
    private static readonly MyStringHash LightStat = MyStringHash.GetOrCompute("player_flashlight");
    private static readonly MyStringHash DefaultHud = MyStringHash.GetOrCompute("Default");

    // The light icon slot in Data/Hud/Default.sbc; the state icons are 62 px apart
    private static readonly Vector2 LightSlot = new Vector2(206f, -227f);
    private const float SlotStep = 62f;

    private static MyHudDefinition patchedDefinition;
    private static MyObjectBuilder_StatControls[] originalControls;

    /// <summary>Called right before the HUD screen builds its stat controls from the definition.</summary>
    public static void Apply()
    {
        var definition = MyHud.HudDefinition;
        if (definition == null)
            return;

        if (!ReferenceEquals(definition, patchedDefinition))
        {
            patchedDefinition = definition;
            originalControls = definition.m_statControlses;
        }

        // Always rebuild from the untouched layout, so a settings change never stacks icons
        definition.m_statControlses = originalControls;

        var mode = Config.Current.HudIndicator;
        if (
            mode == HudIndicator.Off
            || IconPath == null
            || originalControls == null
            || !IsBaseGameDefault(definition)
        )
            return;

        int groupIndex = FindLightIcon(
            out var background,
            out var iconOn,
            out var iconOff,
            out var keybind
        );
        if (groupIndex < 0)
            return;

        EnsureStat();
        EnsureTexture();

        bool iconRow = mode == HudIndicator.IconRow;
        var slot = iconRow
            ? LightSlot + new Vector2(SlotStep, 0f)
            : LightSlot - new Vector2(0f, SlotStep);
        var styles = new List<MyObjectBuilder_StatVisualStyle>();

        // Next to the light icon it behaves like its neighbors: always there, dimmed while off.
        // Above the light icon it only appears while night vision is on, to stay out of HUD mods' way.
        var shownBackground = Clone(background, slot);
        if (!iconRow)
            shownBackground.VisibleCondition = Active();
        styles.Add(shownBackground);

        var shownOn = (MyObjectBuilder_ImageStatVisualStyle)Clone(iconOn, slot);
        shownOn.StatId = NightVisionHudStat.StatName;
        shownOn.Texture = IconTexture;
        shownOn.VisibleCondition = Active();
        styles.Add(shownOn);

        if (iconRow)
        {
            var shownOff = (MyObjectBuilder_ImageStatVisualStyle)Clone(iconOff, slot);
            shownOff.StatId = NightVisionHudStat.StatName;
            shownOff.Texture = IconTexture;
            shownOff.VisibleCondition = Stat(StatConditionOperator.Below);
            styles.Add(shownOff);
        }

        if (keybind != null)
        {
            // The key hint shows the light key: every activation mode is built on it
            var shownKey = Clone(keybind, slot + (keybind.OffsetPx - LightSlot));
            if (!iconRow)
            {
                shownKey.VisibleCondition = new Condition
                {
                    Operator = StatLogicOperator.And,
                    Terms = new[] { shownKey.VisibleCondition, Active() },
                };
            }
            styles.Add(shownKey);
        }

        var group = ShallowCopy(originalControls[groupIndex]);
        group.StatStyles = originalControls[groupIndex].StatStyles.Concat(styles).ToArray();
        var controls = (MyObjectBuilder_StatControls[])originalControls.Clone();
        controls[groupIndex] = group;
        definition.m_statControlses = controls;
    }

    /// <summary>Puts the vanilla layout back, used when the injection fails.</summary>
    public static void Restore()
    {
        if (patchedDefinition != null)
            patchedDefinition.m_statControlses = originalControls;
    }

    /// <summary>Rebuilds the HUD controls after the indicator setting changed.</summary>
    public static void Refresh()
    {
        MyGuiScreenHudSpace.Static?.RecreateControls(false);
    }

    private static bool IsBaseGameDefault(MyHudDefinition definition)
    {
        return definition.Id.SubtypeId == DefaultHud
            && (definition.Context == null || definition.Context.IsBaseGame);
    }

    private static int FindLightIcon(
        out MyObjectBuilder_ImageStatVisualStyle background,
        out MyObjectBuilder_ImageStatVisualStyle iconOn,
        out MyObjectBuilder_ImageStatVisualStyle iconOff,
        out MyObjectBuilder_TextStatVisualStyle keybind
    )
    {
        for (int index = 0; index < originalControls.Length; index++)
        {
            var styles = originalControls[index]?.StatStyles;
            if (styles == null)
                continue;

            var images = styles.OfType<MyObjectBuilder_ImageStatVisualStyle>().ToList();
            background = images.FirstOrDefault(s =>
                s.Texture == BackgroundTexture && s.OffsetPx == LightSlot
            );
            iconOn = images.FirstOrDefault(s =>
                s.StatId == LightStat && s.OffsetPx == LightSlot && s.ColorMask == null
            );
            iconOff = images.FirstOrDefault(s =>
                s.StatId == LightStat && s.OffsetPx == LightSlot && s.ColorMask != null
            );
            keybind = styles
                .OfType<MyObjectBuilder_TextStatVisualStyle>()
                .FirstOrDefault(s =>
                    s.Text != null && s.Text.Contains("HEADLIGHTS") && s.OffsetPx.Y == LightSlot.Y
                );

            if (background != null && iconOn != null && iconOff != null)
                return index;
        }

        background = iconOn = iconOff = null;
        keybind = null;
        return -1;
    }

    // MyObjectBuilder_Base.Clone round-trips through the serializer, which drops the abstract
    // StatStyles array and the visibility conditions. A member-wise copy keeps them; every field
    // changed on a copy is assigned a new value, so the shared references stay untouched.
    private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod(
        "MemberwiseClone",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    private static T ShallowCopy<T>(T source)
        where T : class => (T)MemberwiseCloneMethod.Invoke(source, null);

    private static T Clone<T>(T style, Vector2 offset)
        where T : MyObjectBuilder_StatVisualStyle
    {
        var copy = ShallowCopy(style);
        copy.OffsetPx = offset;
        return copy;
    }

    private static ConditionBase Active() => Stat(StatConditionOperator.Above);

    private static ConditionBase Stat(StatConditionOperator op) =>
        new StatCondition
        {
            StatId = NightVisionHudStat.StatName,
            Operator = op,
            Value = 0.5f,
        };

    private static void EnsureStat()
    {
        if (MyHud.Stats.GetStat<NightVisionHudStat>() == null)
            MyHud.Stats.Register(new NightVisionHudStat());
    }

    private static void EnsureTexture()
    {
        // The texture list is reloaded with the definitions, so this runs whenever it went missing
        if (MyGuiTextures.Static.TryGetTexture(IconTexture, out _))
            return;

        var atlas = new MyGuiTextureAtlasDefinition();
        atlas.Textures[IconTexture] = new MyObjectBuilder_GuiTexture
        {
            Path = IconPath,
            SizePx = new Vector2I(190, 190),
        };
        MyGuiTextures.Static.AddTextures(atlas);
    }
}
