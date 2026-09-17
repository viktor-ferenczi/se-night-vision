using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpDX.Direct3D;
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
        public Vector4 Transition;
        public Vector4 Extra;
        public Vector4 FogVision;
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
    private static MyPixelShaders.Id noFogLightPixel = MyPixelShaders.Id.NULL;
    private static MyPixelShaders.Id noFogLightSample = MyPixelShaders.Id.NULL;
    private static MyPixelShaders.Id noFogLightNoShadow = MyPixelShaders.Id.NULL;
    private static IBorrowedRtvTexture sensorScene;

    internal static bool NeedsGlassMask => snapshot?.ActiveBlend < 1f;

    public static void Initialize() => MyRender11.EnqueueUpdate(() => EnsureShader());

    public static void Publish(NightVisionSnapshot value)
    {
        snapshot = value;
    }

    /// <summary>Entry point from the eye adaptation patches. Never throws: disables itself on the first error.</summary>
    public static void Apply()
    {
        var sensor = sensorScene;
        sensorScene = null;
        var glassMask = GlassMaskRenderer.TakeMask();

        if (failed || !EnsureShader())
        {
            sensor?.Release();
            glassMask?.Release();
            return;
        }

        var snap = snapshot;
        if (snap == null || snap.Blend <= 0f)
        {
            sensor?.Release();
            glassMask?.Release();
            return;
        }

        try
        {
            ApplyInternal(MyRender11.RC, snap, sensor, glassMask);
        }
        catch (Exception e)
        {
            Fail(e);
        }
        finally
        {
            sensor?.Release();
            glassMask?.Release();
        }
    }

    public static bool BeginDirectionalLight(MyRenderContext rc)
    {
        var snap = snapshot;
        if (
            failed
            || !shaderInitialized
            || noFogLightPixel == MyPixelShaders.Id.NULL
            || snap == null
            || snap.Blend <= 0f
        )
            return false;

        try
        {
            var lbuffer = MyGBuffer.Main.LBuffer;
            sensorScene?.Release();
            sensorScene = MyManagers.RwTexturesPool.BorrowRtv(
                "NightVision.Sensor",
                lbuffer.Size.X,
                lbuffer.Size.Y,
                lbuffer.Format,
                MyGBuffer.Main.SamplesCount,
                MyGBuffer.Main.SamplesQuality
            );
            rc.SetRtvNull();
            rc.CopyResource(lbuffer, sensorScene);
            return true;
        }
        catch (Exception e)
        {
            sensorScene?.Release();
            sensorScene = null;
            Fail(e);
            return false;
        }
    }

    public static void EndDirectionalLight(MyRenderContext rc, ISrvTexture shadows)
    {
        if (sensorScene == null)
            return;

        try
        {
            bool useShadows =
                MyRender11.Settings.EnableShadows
                && MyRender11.DebugOverrides.Shadows
                && MyRender11.Settings.User.ShadowQuality != MyShadowsQuality.DISABLED;
            rc.PixelShader.Set(useShadows ? noFogLightPixel : noFogLightNoShadow);
            rc.PixelShader.SetSrv(19, shadows);
            MyScreenPass.RunFullscreenPixelFreq(rc, sensorScene);
            if (MyRender11.MultisamplingEnabled)
            {
                rc.PixelShader.Set(noFogLightSample);
                MyScreenPass.RunFullscreenSampleFreq(rc, sensorScene);
            }
            rc.PixelShader.SetSrv(19, null);
        }
        catch (Exception e)
        {
            sensorScene.Release();
            sensorScene = null;
            Fail(e);
        }
    }

    private static void ApplyInternal(
        MyRenderContext rc,
        NightVisionSnapshot snap,
        ISrvTexture sensor,
        ISrvTexture glassMask
    )
    {
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
            rc.PixelShader.SetSrv(25, MyGBuffer.Main.GBuffer2);
            rc.PixelShader.SetSrv(26, sensor ?? scene);
            rc.PixelShader.SetSrv(27, glassMask);

            MyScreenPass.DrawFullscreenQuad(rc);

            rc.PixelShader.SetSrv(20, null);
            rc.PixelShader.SetSrv(21, null);
            rc.PixelShader.SetSrv(22, null);
            rc.PixelShader.SetSrv(23, null);
            rc.PixelShader.SetSrv(24, null);
            rc.PixelShader.SetSrv(25, null);
            rc.PixelShader.SetSrv(26, null);
            rc.PixelShader.SetSrv(27, null);
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
                0f
            ),
            Anim = new Vector4(
                snap.Blend,
                snap.Flash,
                (float)(MyCommon.FrameTime.Seconds % 3600.0),
                snap.ActiveBlend
            ),
            Transition = new Vector4(
                snap.FlashActive ? 1f : 0f,
                snap.Sliding ? 1f : 0f,
                config.FoliageHighlightScale,
                0f
            ),
            Extra = new Vector4(
                config.NaturalLightThreshold,
                config.CreaseLines,
                config.Fallback,
                0f
            ),
            FogVision = new Vector4(config.FogTint.ToVector3(), config.FogVisibility),
        };

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

                var noFogPath = Path.Combine(directory, "LightDirNoFog.flat.hlsl");
                ShaderFlattener.Flatten(
                    Path.Combine(MyShaderCompiler.ShadersPath, "Lighting", "LightDir.hlsl"),
                    noFogPath
                );
                const string foregroundFog = "output = Fog(shaded, input.depth);";
                const string skyFog = "output = lerp(skyColor, frame_.Fog.color, frame_.Fog.sky);";
                var source = File.ReadAllText(noFogPath);
                if (!source.Contains(foregroundFog) || !source.Contains(skyFog))
                    throw new InvalidOperationException("The game's directional light shader has changed");
                File.WriteAllText(
                    noFogPath,
                    source.Replace(foregroundFog, "output = shaded;")
                        .Replace(skyFog, "output = skyColor;")
                );

                noFogLightPixel = MyPixelShaders.Create(noFogPath);
                noFogLightSample = MyPixelShaders.Create(
                    noFogPath,
                    MyRender11.ShaderSampleFrequencyDefine()
                );
                noFogLightNoShadow = MyPixelShaders.Create(
                    noFogPath,
                    new[] { new ShaderMacro("NO_SHADOWS", null) }
                );
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

    private static void Fail(Exception e)
    {
        failed = true;
        MyLog.Default.Error(
            $"{Plugin.Name}: Night vision renderer failed, disabling it for this session: {e}"
        );
    }
}
