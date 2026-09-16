using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using VRageMath;

namespace ClientPlugin;

public enum HelmetVisionMode
{
    Passive,
    Active,
    Auto,
}

public enum VisionMode
{
    Passive,
    Active,
}

public class Config : INotifyPropertyChanged
{
    #region Options

    private HelmetVisionMode helmetMode = HelmetVisionMode.Auto;
    private float longPressSeconds = 0.25f;
    private VisionMode cameraMode = VisionMode.Active;
    private VisionMode spectatorMode = VisionMode.Passive;

    private float gain = 1f;
    private Color tint = new Color(0.35f, 1f, 0.7f);
    private float naturalLightThreshold = 0.075f;
    private float fallback = 0.1f;
    private float outlineStrength = 1f;
    private float creaseLines = 0.5f;
    private float foliageHighlightScale = 0.25f;
    private float fogVisibility = 0.5f;
    private Color fogTint = new Color(0.35f, 0.7f, 1f);
    private float noise = 1f;
    private float vignette = 0.6f;
    private float fadeSeconds = 0.3f;

    #endregion

    #region User interface

    public readonly string Title = "Night Vision";

    [Separator("Modes and controls")]
    [Dropdown(description: "Choose how night vision works with the helmet closed.\nAuto switches to passive mode while you are in a vehicle.")]
    public HelmetVisionMode HelmetMode
    {
        get => helmetMode;
        set => SetField(ref helmetMode, value);
    }

    [Slider(
        0.2f,
        1.5f,
        0.05f,
        SliderAttribute.SliderType.Float,
        label: "Activation delay",
        description: "How long you must hold the light key to toggle night vision."
    )]
    public float LongPressSeconds
    {
        get => longPressSeconds;
        set => SetField(ref longPressSeconds, value);
    }

    [Dropdown(description: "Choose the mode used for ship cameras, remote views, and turrets.")]
    public VisionMode CameraMode
    {
        get => cameraMode;
        set => SetField(ref cameraMode, value);
    }

    [Dropdown(description: "Choose the mode used in third person and spectator cameras.")]
    public VisionMode SpectatorMode
    {
        get => spectatorMode;
        set => SetField(ref spectatorMode, value);
    }

    [Separator("Image")]
    [Slider(
        0f,
        4f,
        0.1f,
        SliderAttribute.SliderType.Float,
        description: "Brightens the night vision image."
    )]
    public float Gain
    {
        get => gain;
        set => SetField(ref gain, value);
    }

    [Color(description: "Sets the color of the night vision image.")]
    public Color Tint
    {
        get => tint;
        set => SetField(ref tint, value);
    }

    [Slider(
        0f,
        0.5f,
        0.005f,
        SliderAttribute.SliderType.Float,
        label: "Natural color threshold",
        description: "Keeps well-lit surfaces in their natural color.\nSet this to 0 to apply night vision everywhere."
    )]
    public float NaturalLightThreshold
    {
        get => naturalLightThreshold;
        set => SetField(ref naturalLightThreshold, value);
    }

    [Slider(
        0f,
        0.5f,
        0.01f,
        SliderAttribute.SliderType.Float,
        label: "Infrared fallback",
        description: "Controls the short-range infrared view used when there is no visible light."
    )]
    public float Fallback
    {
        get => fallback;
        set => SetField(ref fallback, value);
    }

    [Separator("Outlines")]
    [Slider(
        0f,
        2f,
        0.05f,
        SliderAttribute.SliderType.Float,
        description: "Controls the brightness of all outlines."
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
        description: "Adds outlines to creases and panel lines, including armor block edges.\nSet this to 0 to show only silhouettes."
    )]
    public float CreaseLines
    {
        get => creaseLines;
        set => SetField(ref creaseLines, value);
    }

    [Slider(
        0f,
        1f,
        0.05f,
        SliderAttribute.SliderType.Float,
        label: "Foliage highlights",
        description: "Controls the size and opacity of outlines on grass, bushes, and trees.\nSet this to 0 to hide them."
    )]
    public float FoliageHighlightScale
    {
        get => foliageHighlightScale;
        set => SetField(ref foliageHighlightScale, value);
    }

    [Separator("Fog vision")]
    [Slider(
        0f,
        1f,
        0.05f,
        SliderAttribute.SliderType.Float,
        label: "Minimum visibility",
        description: "Sets how much visibility remains when fog vision starts.\nSet this to 0 to turn fog vision off."
    )]
    public float FogVisibility
    {
        get => fogVisibility;
        set => SetField(ref fogVisibility, value);
    }

    [Color(description: "Sets the color used for fog vision.")]
    public Color FogTint
    {
        get => fogTint;
        set => SetField(ref fogTint, value);
    }

    [Separator("Effects")]
    [Slider(
        0f,
        5f,
        0.1f,
        SliderAttribute.SliderType.Float,
        label: "Sensor grain",
        description: "Controls the amount of sensor grain."
    )]
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
        description: "Controls how much the image darkens near the edges of the screen."
    )]
    public float Vignette
    {
        get => vignette;
        set => SetField(ref vignette, value);
    }

    [Slider(
        0f,
        1f,
        0.05f,
        SliderAttribute.SliderType.Float,
        label: "Animation duration",
        description: "Sets the duration of the flash, fade, and visor animations."
    )]
    public float FadeSeconds
    {
        get => fadeSeconds;
        set => SetField(ref fadeSeconds, value);
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
