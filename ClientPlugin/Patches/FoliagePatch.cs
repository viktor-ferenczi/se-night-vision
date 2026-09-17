using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyGBufferPass), "RecordCommandsInternal", new[] { typeof(MyRenderableProxy) })]
internal static class FoliagePatch
{
    // The LOD is only exported to GBuffer0.a; 255 is already the new-pipeline marker.
    private const uint FoliageLodMarker = 254;

    [HarmonyPrefix]
    private static void Prefix(MyRenderableProxy proxy, out uint __state)
    {
        __state = proxy.CommonObjectData.LOD;
        var name = proxy.Parent?.Owner?.DebugName;
        if (
            name?.Contains(@"Models\Environment\Trees\") == true
            || name?.Contains(@"Models\Environment\Bushes\") == true
        )
            proxy.CommonObjectData.LOD = FoliageLodMarker;
    }

    [HarmonyPostfix]
    private static void Postfix(MyRenderableProxy proxy, uint __state)
    {
        proxy.CommonObjectData.LOD = __state;
    }
}
