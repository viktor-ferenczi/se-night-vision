using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using VRageMath;

namespace ClientPlugin;

public class Config : INotifyPropertyChanged
{
    #region Options

    private float longPressSeconds = 0.5f;

    private Color tint = new Color(0.35f, 1f, 0.7f);
    private float gain = 6f;
    private float fallback = 0.01f;
    private float skyGain = 0.05f;
    private float naturalLightThreshold = 0.2f;
    private Color fogTint = new Color(0.35f, 0.7f, 1f);
    private float fogVisibility = 0.75f;
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
    [Slider(
        0.2f,
        1.5f,
        0.05f,
        SliderAttribute.SliderType.Float,
        description: "How long the light key has to be held to toggle night vision (seconds)"
    )]
    public float LongPressSeconds
    {
        get => longPressSeconds;
        set => SetField(ref longPressSeconds, value);
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
        0.1f,
        0.01f,
        SliderAttribute.SliderType.Float,
        description: "Strength of the short-range infrared flood used when visible light is absent"
    )]
    public float Fallback
    {
        get => fallback;
        set => SetField(ref fallback, value);
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
        description: "Surfaces with lighting above this level keep their natural color (0 turns it off)"
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

    [Separator("Fog vision")]
    [Color(description: "Monochrome tint used when fog obscures the scene")]
    public Color FogTint
    {
        get => fogTint;
        set => SetField(ref fogTint, value);
    }

    [Slider(
        0f,
        1f,
        0.05f,
        SliderAttribute.SliderType.Float,
        description: "Remaining visibility where fog vision begins (0 disables it)"
    )]
    public float FogVisibility
    {
        get => fogVisibility;
        set => SetField(ref fogVisibility, value);
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
