using System;
using System.IO;
using System.Runtime.InteropServices;
using VRage.FileSystem;
using VRage.Render11.Common;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.NightVision;

/// <summary>
/// Render-thread side: runs the night vision pixel shader over the HDR light buffer right after
/// eye adaptation, so bloom and tone mapping see the amplified image. The HUD and GUI sprites are
/// drawn after the whole scene and are never touched.
/// </summary>
public static class NightVisionRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Constants
    {
        public Vector4 TintGain;
        public Vector4 Look;
        public Vector4 Anim;
        public Vector4 BoxCenter;
        public Vector4 BoxAxisX;
        public Vector4 BoxAxisY;
        public Vector4 BoxAxisZ;
        public Vector4 Extra;
    }

    // Matches register(b8) in the shader. The game owns b0-b7 (see MyCommon).
    private const int ConstantsSlot = 8;

    private static readonly int ConstantsSize = Marshal.SizeOf(typeof(Constants));
    private static readonly object InitLock = new object();

    // Set once at plugin init (game thread), read on the render thread
    public static volatile string ShaderFilePath;

    private static volatile NightVisionSnapshot snapshot;
    private static bool failed;
    private static bool shaderInitialized;
    private static MyPixelShaders.Id pixelShader;

    public static void Publish(NightVisionSnapshot value)
    {
        snapshot = value;
    }

    /// <summary>Entry point from the eye adaptation patches. Never throws: disables itself on the first error.</summary>
    public static void Apply()
    {
        if (failed)
            return;

        var snap = snapshot;
        if (snap == null || snap.Blend <= 0f)
            return;

        try
        {
            ApplyInternal(MyRender11.RC, snap);
        }
        catch (Exception e)
        {
            failed = true;
            MyLog.Default.Error(
                $"{Plugin.Name}: Night vision renderer failed, disabling it for this session: {e}"
            );
        }
    }

    private static void ApplyInternal(MyRenderContext rc, NightVisionSnapshot snap)
    {
        if (!EnsureShader())
            return;

        var lbuffer = MyGBuffer.Main.LBuffer;
        var scene = MyManagers.RwTexturesPool.BorrowRtv(
            "NightVision.Scene",
            lbuffer.Size.X,
            lbuffer.Size.Y,
            lbuffer.Format
        );
        try
        {
            rc.CopyResource(lbuffer, scene);

            var constants = FillConstants(snap, Config.Current);

            rc.SetBlendState(null);
            rc.SetRasterizerState(null);
            rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
            rc.SetRtv(lbuffer);

            // Bound straight on the device context: MyCommonStage only accepts the game's slots
            IConstantBuffer cb = rc.GetObjectCB(ConstantsSize);
            var mapping = MyMapping.MapDiscard(rc, cb);
            mapping.WriteAndPosition(ref constants);
            mapping.Unmap();
            rc.DeviceContext.PixelShader.SetConstantBuffer(ConstantsSlot, cb.Buffer);

            rc.PixelShader.SetConstantBuffer(0, MyCommon.FrameConstants);
            rc.PixelShader.Set(pixelShader);
            rc.PixelShader.SetSrv(20, scene);
            rc.PixelShader.SetSrv(21, MyGBuffer.Main.ResolvedDepthStencil.SrvDepth);
            rc.PixelShader.SetSrv(22, MyGBuffer.Main.GBuffer1);
            rc.PixelShader.SetSrv(23, MyEyeAdaptation.GetExposure());
            rc.PixelShader.SetSrv(24, MyGBuffer.Main.GBuffer0);

            MyScreenPass.DrawFullscreenQuad(rc);

            rc.PixelShader.SetSrv(20, null);
            rc.PixelShader.SetSrv(21, null);
            rc.PixelShader.SetSrv(22, null);
            rc.PixelShader.SetSrv(23, null);
            rc.PixelShader.SetSrv(24, null);
            rc.DeviceContext.PixelShader.SetConstantBuffer(ConstantsSlot, null);
            rc.SetDepthStencilState(null);
            rc.SetRtvNull();
        }
        finally
        {
            scene.Release();
        }
    }

    private static Constants FillConstants(NightVisionSnapshot snap, Config config)
    {
        var constants = new Constants
        {
            TintGain = new Vector4(config.Tint.ToVector3(), config.Gain),
            Look = new Vector4(
                config.OutlineStrength,
                config.Noise,
                config.Vignette,
                config.Washout
            ),
            Anim = new Vector4(
                snap.Blend,
                snap.Flash,
                (float)(MyCommon.FrameTime.Seconds % 3600.0),
                snap.MaskInterior ? 1f : 0f
            ),
            Extra = new Vector4(
                config.NaturalLightThreshold,
                config.SkyGain,
                config.CreaseLines,
                config.Fallback
            ),
        };

        if (snap.MaskInterior)
        {
            var center = (Vector3)(snap.BoxCenter - MyRender11.Environment.Matrices.CameraPosition);
            constants.BoxCenter = new Vector4(center, 0f);
            constants.BoxAxisX = new Vector4(
                Vector3.Normalize(snap.BoxAxisX),
                snap.BoxHalfExtents.X
            );
            constants.BoxAxisY = new Vector4(
                Vector3.Normalize(snap.BoxAxisY),
                snap.BoxHalfExtents.Y
            );
            constants.BoxAxisZ = new Vector4(
                Vector3.Normalize(snap.BoxAxisZ),
                snap.BoxHalfExtents.Z
            );
        }

        return constants;
    }

    private static bool EnsureShader()
    {
        if (shaderInitialized)
            return pixelShader != MyPixelShaders.Id.NULL;

        lock (InitLock)
        {
            if (shaderInitialized)
                return pixelShader != MyPixelShaders.Id.NULL;

            var path = ShaderFilePath;
            if (path == null)
            {
                shaderInitialized = true;
                MyLog.Default.Error(
                    $"{Plugin.Name}: No shader file available, night vision will not render"
                );
                return false;
            }

            try
            {
                // The include handler callback is broken in the Linux build of the D3D compiler,
                // so compile a copy with all game includes inlined
                var directory = Path.Combine(MyFileSystem.UserDataPath, "Storage", Plugin.Name);
                Directory.CreateDirectory(directory);
                var flattenedPath = Path.Combine(directory, "NightVision.flat.hlsl");
                ShaderFlattener.Flatten(path, flattenedPath);

                // The registry compiles at ps_5_0 with entry point __pixel_shader and restores it after device resets
                pixelShader = MyPixelShaders.Create(flattenedPath);
            }
            catch (Exception e)
            {
                pixelShader = MyPixelShaders.Id.NULL;
                MyLog.Default.Error($"{Plugin.Name}: Failed to compile {path}: {e.Message}");
            }

            shaderInitialized = true;
            return pixelShader != MyPixelShaders.Id.NULL;
        }
    }
}
