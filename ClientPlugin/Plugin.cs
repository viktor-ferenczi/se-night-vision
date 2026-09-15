using System;
using System.IO;
using System.Reflection;
using ClientPlugin.NightVision;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Layouts;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.FileSystem;
using VRage.Plugins;
using VRage.Utils;

// Define assembly version when compiled by Pulsar
#if !LOCAL_BUILD
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

#endif

namespace ClientPlugin;

// ReSharper disable once UnusedType.Global
public class Plugin : IPlugin
{
    public const string Name = "NightVision";
    public const string ShaderFileName = "NightVision.hlsl";
    public const string HudIconFileName = "NightVisionHud.png";
    public static Plugin Instance { get; private set; }
    private SettingsGenerator settingsGenerator;

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.NoInlining
    )]
    public void Init(object gameInstance)
    {
        Instance = this;
        Instance.settingsGenerator = new SettingsGenerator();

        // Pulsar calls LoadAssets with the copied shader folder before Init when it builds
        // from source. An msbuild/IDE build has the shader embedded instead.
        if (NightVisionRenderer.ShaderFilePath == null)
            ExtractEmbeddedShader();
        if (NightVisionHud.IconPath == null)
            ExtractEmbeddedIcon();
        GlassMaskRenderer.Initialize();
        NightVisionRenderer.Initialize();

        var harmony = new Harmony(Name);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    public void Dispose()
    {
        // IMPORTANT: Do NOT call harmony.UnpatchAll() here! It may break other plugins.
        NightVisionRenderer.Publish(null);
        Instance = null;
    }

    public void Update() { }

    // ReSharper disable once UnusedMember.Global
    public void LoadAssets(string folder)
    {
        try
        {
            var iconPath = Path.Combine(folder, HudIconFileName);
            if (File.Exists(iconPath))
                NightVisionHud.IconPath = iconPath;

            var path = Path.Combine(folder, ShaderFileName);
            if (File.Exists(path))
                NightVisionRenderer.ShaderFilePath = path;
            else
                MyLog.Default.Warning($"{Name}: Shader not found in the asset folder: {path}");
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"{Name}: Failed to load assets from {folder}: {e}");
        }
    }

    private static void ExtractEmbeddedIcon()
    {
        try
        {
            using var resource = Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream($"ClientPlugin.Assets.{HudIconFileName}");
            if (resource == null)
                return;

            var directory = Path.Combine(MyFileSystem.UserDataPath, "Storage", Name);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, HudIconFileName);

            using (var file = File.Create(path))
                resource.CopyTo(file);

            NightVisionHud.IconPath = path;
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"{Name}: Failed to extract the embedded HUD icon: {e}");
        }
    }

    private static void ExtractEmbeddedShader()
    {
        try
        {
            using var resource = Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream($"ClientPlugin.Assets.{ShaderFileName}");
            if (resource == null)
                return;

            var directory = Path.Combine(MyFileSystem.UserDataPath, "Storage", Name);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, ShaderFileName);

            using (var file = File.Create(path))
                resource.CopyTo(file);

            NightVisionRenderer.ShaderFilePath = path;
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"{Name}: Failed to extract the embedded shader: {e}");
        }
    }

    // ReSharper disable once UnusedMember.Global
    public void OpenConfigDialog()
    {
        Instance.settingsGenerator.SetLayout<Simple>();
        MyGuiSandbox.AddScreen(Instance.settingsGenerator.Dialog);
    }
}
