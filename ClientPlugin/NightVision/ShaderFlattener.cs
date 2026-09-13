using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using VRageRender;

namespace ClientPlugin.NightVision;

/// <summary>
/// Inlines the #include directives of a shader source file before compilation. The game's
/// shader compiler resolves includes through a SharpDX callback; that works on Windows, but
/// the Linux build's D3D compiler never receives the handler, so every #include fails there
/// with "No include handler specified". Flattening the source up front avoids the callback
/// entirely and compiles the same bytecode on both platforms.
/// </summary>
public static class ShaderFlattener
{
    private static readonly Regex IncludeRegex = new Regex(
        @"^\s*#\s*include\s+(?:<(?<angled>[^>]+)>|""(?<quoted>[^""]+)"")\s*$",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Writes a copy of sourcePath into targetPath with all resolvable #include directives
    /// inlined recursively, each included file only once (they all have include guards, so
    /// this matches what the preprocessor would produce). Mirrors the game's include handler:
    /// angled includes resolve against the game's Shaders content folder, quoted ones against
    /// the including file's folder first. Unresolvable includes are kept as-is, because they
    /// may sit in preprocessor branches never taken in HLSL (e.g. #ifdef __cplusplus).
    /// </summary>
    public static void Flatten(string sourcePath, string targetPath)
    {
        var output = new StringBuilder(64 * 1024);
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AppendFlattened(Path.GetFullPath(sourcePath), output, included);
        File.WriteAllText(targetPath, output.ToString());
    }

    private static void AppendFlattened(string path, StringBuilder output, HashSet<string> included)
    {
        var directory = Path.GetDirectoryName(path);
        foreach (var line in File.ReadLines(path))
        {
            var match = IncludeRegex.Match(line);
            if (!match.Success)
            {
                output.AppendLine(line);
                continue;
            }

            bool quoted = match.Groups["quoted"].Success;
            var name = quoted ? match.Groups["quoted"].Value : match.Groups["angled"].Value;
            var resolved = Resolve(name, quoted ? directory : null);
            if (resolved == null)
            {
                output.AppendLine(line);
                continue;
            }

            if (!included.Add(resolved))
            {
                output.AppendLine($"// [{Plugin.Name}] Skipped repeated include: {name}");
                continue;
            }

            output.AppendLine($"// [{Plugin.Name}] Begin include: {name}");
            AppendFlattened(resolved, output, included);
            output.AppendLine($"// [{Plugin.Name}] End include: {name}");
        }
    }

    private static string Resolve(string name, string localDirectory)
    {
        name = name.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (localDirectory != null)
        {
            var local = Path.Combine(localDirectory, name);
            if (File.Exists(local))
                return Path.GetFullPath(local);
        }

        var shared = Path.Combine(MyShaderCompiler.ShadersPath, name);
        return File.Exists(shared) ? Path.GetFullPath(shared) : null;
    }
}
