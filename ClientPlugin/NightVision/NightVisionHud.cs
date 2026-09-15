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

public class NightVisionHudStat : MyStatBase
{
    public static readonly MyStringHash StatName = MyStringHash.GetOrCompute("night_vision");

    public NightVisionHudStat()
    {
        Id = StatName;
    }

    public override void Update()
    {
        CurrentValue =
            NightVisionController.Activated
            && NightVisionController.Mode == NightVisionMode.Active
            ? 1f
            : 0f;
    }
}

public class NightVisionPassiveHudStat : NightVisionHudStat
{
    public new static readonly MyStringHash StatName = MyStringHash.GetOrCompute(
        "night_vision_passive"
    );

    public NightVisionPassiveHudStat()
    {
        Id = StatName;
    }

    public override void Update()
    {
        CurrentValue =
            NightVisionController.Activated
            && NightVisionController.Mode == NightVisionMode.Passive
            ? 1f
            : 0f;
    }
}

/// <summary>Uses the current HUD's light styles and swaps their texture while night vision renders.</summary>
public static class NightVisionHud
{
    public static volatile string IconPath;

    private static readonly MyStringHash IconTexture = MyStringHash.GetOrCompute(
        "NightVisionGoggles"
    );
    private static readonly MyStringHash LightStat = MyStringHash.GetOrCompute("player_flashlight");
    private static readonly MethodInfo MemberwiseCloneMethod = typeof(object).GetMethod(
        "MemberwiseClone",
        BindingFlags.Instance | BindingFlags.NonPublic
    );

    private static MyHudDefinition patchedDefinition;
    private static MyObjectBuilder_StatControls[] originalControls;

    /// <summary>Called before the HUD builds its controls, including for modded HUD definitions.</summary>
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

        definition.m_statControlses = originalControls;
        if (IconPath == null || originalControls == null)
            return;

        var controls = (MyObjectBuilder_StatControls[])originalControls.Clone();
        bool replaced = false;

        for (int index = 0; index < controls.Length; index++)
        {
            var source = originalControls[index];
            if (source?.StatStyles == null)
                continue;

            var styles = new List<MyObjectBuilder_StatVisualStyle>(source.StatStyles.Length);
            var nightVisionSlots = new HashSet<Vector2>();
            bool groupChanged = false;
            foreach (var style in source.StatStyles)
            {
                if (
                    style is not MyObjectBuilder_ImageStatVisualStyle light
                    || light.StatId != LightStat
                    || light.SizePx.Y < light.SizePx.X * 0.5f
                )
                {
                    styles.Add(style);
                    continue;
                }

                var vanilla = ShallowCopy(light);
                vanilla.VisibleCondition = When(light.VisibleCondition, Inactive());
                styles.Add(vanilla);
                if (!nightVisionSlots.Add(light.OffsetPx))
                    continue;

                var full = ShallowCopy(light);
                full.StatId = NightVisionHudStat.StatName;
                full.Texture = IconTexture;
                full.ColorMask = null;
                full.VisibleCondition = Full();
                styles.Add(full);

                var passive = ShallowCopy(full);
                passive.StatId = NightVisionPassiveHudStat.StatName;
                passive.ColorMask = FindDimColor(source.StatStyles, light.OffsetPx);
                passive.VisibleCondition = Passive();
                styles.Add(passive);

                groupChanged = replaced = true;
            }

            if (groupChanged)
            {
                var group = ShallowCopy(source);
                group.StatStyles = styles.ToArray();
                controls[index] = group;
            }
        }

        if (!replaced)
            return;

        EnsureStat();
        EnsureTexture();
        definition.m_statControlses = controls;
    }

    public static void Restore()
    {
        if (patchedDefinition != null)
            patchedDefinition.m_statControlses = originalControls;
    }

    private static T ShallowCopy<T>(T source)
        where T : class => (T)MemberwiseCloneMethod.Invoke(source, null);

    private static ConditionBase When(ConditionBase original, ConditionBase state) =>
        original == null
            ? state
            : new Condition
            {
                Operator = StatLogicOperator.And,
                Terms = new[] { original, state },
            };

    private static ConditionBase Full() =>
        Stat(NightVisionHudStat.StatName, StatConditionOperator.Above);

    private static ConditionBase Passive() =>
        Stat(NightVisionPassiveHudStat.StatName, StatConditionOperator.Above);

    private static ConditionBase Inactive() =>
        new Condition
        {
            Operator = StatLogicOperator.And,
            Terms =
            [
                Stat(NightVisionPassiveHudStat.StatName, StatConditionOperator.Below),
                Stat(NightVisionHudStat.StatName, StatConditionOperator.Below),
            ],
        };

    private static ConditionBase Stat(MyStringHash id, StatConditionOperator op) =>
        new StatCondition
        {
            StatId = id,
            Operator = op,
            Value = 0.5f,
        };

    private static void EnsureStat()
    {
        if (MyHud.Stats.GetStat(NightVisionHudStat.StatName) == null)
            MyHud.Stats.Register(new NightVisionHudStat());
        if (MyHud.Stats.GetStat(NightVisionPassiveHudStat.StatName) == null)
            MyHud.Stats.Register(new NightVisionPassiveHudStat());
    }

    private static Vector4 FindDimColor(MyObjectBuilder_StatVisualStyle[] styles, Vector2 offset) =>
        styles
            .OfType<MyObjectBuilder_ImageStatVisualStyle>()
            .FirstOrDefault(image =>
                image.StatId == LightStat && image.OffsetPx == offset && image.ColorMask.HasValue
            )
            ?.ColorMask ?? new Vector4(1f, 1f, 1f, 0.2f);

    private static void EnsureTexture()
    {
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
