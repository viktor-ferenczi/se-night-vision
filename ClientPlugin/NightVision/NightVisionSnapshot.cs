namespace ClientPlugin.NightVision;

/// <summary>
/// Immutable per-frame state handed from the game thread to the render thread.
/// </summary>
public sealed class NightVisionSnapshot
{
    public float Blend;
    public float Flash;
    public float ActiveBlend;
    public bool FlashActive;
    public bool Sliding;
}
