using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using ClientPlugin.Settings.Tools;
using VRage.Input;
using VRageMath;

namespace ClientPlugin;

public enum ActivationMode
{
    // A key combination toggles night vision (Shift + the light key unless set explicitly)
    Hotkey,

    // Tapping the light key toggles the light, holding it toggles night vision
    LongPress,

    // Each tap of the light key: Off -> Light -> Night vision -> Off
    Cycle,
}

public enum HudIndicator
{
    // A 5th state icon right of the light icon, always shown and dimmed while off
    IconRow,

    // Above the light icon, only while night vision is on
    AboveLightIcon,

    Off,
}

public class Config : INotifyPropertyChanged
{
    #region Options

    private ActivationMode activationMode = ActivationMode.Hotkey;
    private bool hotkeyAlwaysActive = true;
    private Binding hotkey = new Binding(MyKeys.None);
    private float longPressSeconds = 0.5f;
    private HudIndicator hudIndicator = HudIndicator.IconRow;

    private Color tint = new Color(0.35f, 1f, 0.7f);
    private float gain = 6f;
    private float skyGain = 0.05f;
    private float naturalLightThreshold = 0.2f;
    private float outlineStrength = 1.5f;
    private float creaseLines = 0f;
    private float noise = 0.5f;
    private float vignette = 0.6f;
    private float washout = 1f;
    private float fadeSeconds = 0.3f;

    private string forceGlass = "";
    private string forceNoGlass = "";

    #endregion

    #region User interface

    public readonly string Title = "Night Vision";

    [Separator("Activation")]
    [Dropdown(
        description: "Hotkey: a key combination toggles night vision\nLong Press: holding the light key toggles night vision, a tap toggles the light\nCycle: each tap of the light key goes Off, Light, Night vision"
    )]
    public ActivationMode ActivationMode
    {
        get => activationMode;
        set => SetField(ref activationMode, value);
    }

    [Checkbox(description: "Keep the hotkey working in the Long Press and Cycle modes as well")]
    public bool HotkeyAlwaysActive
    {
        get => hotkeyAlwaysActive;
        set => SetField(ref hotkeyAlwaysActive, value);
    }

    [Keybind(
        description: "Night vision hotkey. Unbind it (right click) to use Shift plus whatever key the light is bound to."
    )]
    public Binding Hotkey
    {
        get => hotkey;
        set => SetField(ref hotkey, value);
    }

    [Slider(
        0.2f,
        1.5f,
        0.05f,
        SliderAttribute.SliderType.Float,
        description: "How long the light key has to be held in the Long Press mode (seconds)"
    )]
    public float LongPressSeconds
    {
        get => longPressSeconds;
        set => SetField(ref longPressSeconds, value);
    }

    [Dropdown(
        description: "Icon Row: a 5th icon next to the light icon, dimmed while off\nAbove Light Icon: above the light icon, only while night vision is on (avoids conflicts with HUD mods)\nOff: no indicator\nModded HUDs never get an indicator."
    )]
    public HudIndicator HudIndicator
    {
        get => hudIndicator;
        set => SetField(ref hudIndicator, value);
    }

    [Separator("Look")]
    [Color(description: "Monochrome tint")]
    public Color Tint
    {
        get => tint;
        set => SetField(ref tint, value);
    }

    [Slider(
        1f,
        64f,
        0.5f,
        SliderAttribute.SliderType.Float,
        description: "Luminance amplification"
    )]
    public float Gain
    {
        get => gain;
        set => SetField(ref gain, value);
    }

    [Slider(
        0f,
        1f,
        0.05f,
        SliderAttribute.SliderType.Float,
        description: "Amplification of the sky relative to the gain, low values keep space black with the stars showing"
    )]
    public float SkyGain
    {
        get => skyGain;
        set => SetField(ref skyGain, value);
    }

    [Slider(
        0f,
        2f,
        0.05f,
        SliderAttribute.SliderType.Float,
        description: "Surfaces lit brighter than this by nearby lights keep their natural color (0 turns it off)"
    )]
    public float NaturalLightThreshold
    {
        get => naturalLightThreshold;
        set => SetField(ref naturalLightThreshold, value);
    }

    [Slider(
        0f,
        3f,
        0.05f,
        SliderAttribute.SliderType.Float,
        description: "Brightness of the contour lines on silhouettes, creases and panel lines"
    )]
    public float OutlineStrength
    {
        get => outlineStrength;
        set => SetField(ref outlineStrength, value);
    }

    [Slider(
        0f,
        1f,
        0.05f,
        SliderAttribute.SliderType.Float,
        description: "Contour lines on creases and panel lines, such as the edges of every armor block (0 outlines only silhouettes)"
    )]
    public float CreaseLines
    {
        get => creaseLines;
        set => SetField(ref creaseLines, value);
    }

    [Slider(0f, 2f, 0.05f, SliderAttribute.SliderType.Float, description: "Sensor grain")]
    public float Noise
    {
        get => noise;
        set => SetField(ref noise, value);
    }

    [Slider(
        0f,
        1f,
        0.05f,
        SliderAttribute.SliderType.Float,
        description: "Darkening towards the edges of the screen"
    )]
    public float Vignette
    {
        get => vignette;
        set => SetField(ref vignette, value);
    }

    [Slider(
        0f,
        4f,
        0.1f,
        SliderAttribute.SliderType.Float,
        description: "How much bright light sources overload the sensor and bloom"
    )]
    public float Washout
    {
        get => washout;
        set => SetField(ref washout, value);
    }

    [Slider(
        0f,
        1f,
        0.05f,
        SliderAttribute.SliderType.Float,
        description: "Fade and flash duration when switching on and off (seconds)"
    )]
    public float FadeSeconds
    {
        get => fadeSeconds;
        set => SetField(ref fadeSeconds, value);
    }

    [Separator("Cockpit glass overrides")]
    [Textbox(
        description: "Comma separated block subtype IDs always treated as having see-through glass (for modded cockpits)"
    )]
    public string ForceGlass
    {
        get => forceGlass;
        set => SetField(ref forceGlass, value);
    }

    [Textbox(
        description: "Comma separated block subtype IDs always treated as having no glass, so the helmet provides night vision"
    )]
    public string ForceNoGlass
    {
        get => forceNoGlass;
        set => SetField(ref forceNoGlass, value);
    }

    #endregion

    #region Property change notification boilerplate

    public static readonly Config Default = new Config();
    public static readonly Config Current = ConfigStorage.Load();

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
