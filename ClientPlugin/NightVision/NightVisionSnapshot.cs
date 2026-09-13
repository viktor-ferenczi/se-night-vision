using VRageMath;

namespace ClientPlugin.NightVision;

/// <summary>
/// Immutable per-frame state handed from the game thread to the render thread.
/// </summary>
public sealed class NightVisionSnapshot
{
    public float Blend;
    public float Flash;

    // Cockpit source: pixels inside this oriented box are the interior and stay unprocessed
    public bool MaskInterior;
    public Vector3D BoxCenter;
    public Vector3 BoxAxisX;
    public Vector3 BoxAxisY;
    public Vector3 BoxAxisZ;
    public Vector3 BoxHalfExtents;
}
