using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sandbox.Definitions;
using VRage.Game;
using VRage.Game.Models;
using VRage.Utils;
using VRageMath;
using VRageRender.Import;
using VRageRender.Models;

namespace ClientPlugin.NightVision;

public struct CockpitGlassInfo
{
    public bool HasGlass;

    // Block-local box enclosing the cockpit and its interior model
    public BoundingBox InteriorBox;
}

/// <summary>
/// Decides once per block definition whether a seat has a see-through glass surface, by looking
/// for GLASS draw technique meshes in its models. The config overrides win over the detection.
/// </summary>
public static class GlassDetector
{
    // Extra room around the interior box, so geometry sitting right on the block boundary
    // (the canopy frame, the pilot's helmet) is still counted as interior
    private const float BoxMargin = 0.05f;

    private static readonly Dictionary<MyDefinitionId, CockpitGlassInfo> Cache =
        new Dictionary<MyDefinitionId, CockpitGlassInfo>();

    // Vanilla and DLC seats the GLASS technique scan gets wrong. Checked against the detection
    // report (NIGHTVISION_GLASS_REPORT=1) and in-game screenshots. The config lists win over these.
    private static readonly HashSet<string> BuiltInGlass = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        // Closed cockpits whose windows are not GLASS meshes
        "RoverCockpit",
    };

    private static readonly HashSet<string> BuiltInNoGlass = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        // Open cockpits: the glass is a windscreen or a screen, the pilot sits in the open
        "OpenCockpitLarge",
        "LargeBlockOpenSlopedCockpit",
        "SmallBlockOpenSlopedCockpit",
        // Holographic screen, not a window
        "LargeBlockSuspendedControlSeat",
        "SmallBlockSuspendedControlSeat",
        // Furniture with a glass part the occupant does not look through
        "LargeBlockBathroomOpen",
        "LargeBlockBed",
        "LargeBlockInsetPlantCouch",
        "LargeBlockLabDeskSeat",
    };

    public static void ClearCache()
    {
        lock (Cache)
            Cache.Clear();
    }

    public static CockpitGlassInfo Get(MyCockpitDefinition definition)
    {
        if (definition == null)
            return default;

        lock (Cache)
        {
            if (Cache.TryGetValue(definition.Id, out var info))
                return info;

            info = Detect(definition, null);
            Cache[definition.Id] = info;
            return info;
        }
    }

    private static CockpitGlassInfo Detect(MyCockpitDefinition definition, StringBuilder report)
    {
        var info = new CockpitGlassInfo();

        float cubeSize = MyDefinitionManager.Static.GetCubeSize(definition.CubeSize);
        var halfCube = new Vector3(definition.Size) * cubeSize * 0.5f;
        var box = new BoundingBox(-halfCube, halfCube);

        bool detected = false;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (
            var model in new[] { definition.Model, definition.InteriorModel, definition.GlassModel }
        )
        {
            if (string.IsNullOrEmpty(model))
                continue;

            detected |= ScanModel(model, visited, ref box, report, 0);
        }

        var subtype = definition.Id.SubtypeName;
        if (ContainsSubtype(Config.Current.ForceNoGlass, subtype))
            info.HasGlass = false;
        else if (ContainsSubtype(Config.Current.ForceGlass, subtype))
            info.HasGlass = true;
        else if (BuiltInNoGlass.Contains(subtype))
            info.HasGlass = false;
        else if (BuiltInGlass.Contains(subtype))
            info.HasGlass = true;
        else
            info.HasGlass = detected;

        box.Inflate(BoxMargin);
        info.InteriorBox = box;

        report?.Append(
            $" => detected={detected} hasGlass={info.HasGlass} box={box.Min}..{box.Max}"
        );
        return info;
    }

    private static bool ScanModel(
        string modelPath,
        HashSet<string> visited,
        ref BoundingBox box,
        StringBuilder report,
        int depth
    )
    {
        if (depth > 4 || !visited.Add(modelPath))
            return false;

        MyModel model;
        try
        {
            model = MyModels.GetModelOnlyData(modelPath);
        }
        catch (Exception e)
        {
            MyLog.Default.Warning($"{Plugin.Name}: Failed to load model {modelPath}: {e.Message}");
            return false;
        }

        if (model == null)
            return false;

        // Subparts are positioned by their dummy, their own boxes are only used for detection
        if (depth == 0)
            box.Include(model.BoundingBox);

        bool found = false;
        var techniques = new SortedSet<string>();
        foreach (var mesh in model.GetMeshList() ?? new List<MyMesh>())
        {
            var material = mesh.Material;
            if (material == null)
                continue;

            techniques.Add($"{material.DrawTechnique}:{material.Name}");
            if (material.DrawTechnique == MyMeshDrawTechnique.GLASS)
                found = true;
        }

        report?.Append(
            $"\n    {new string(' ', depth * 2)}{modelPath}: {string.Join(", ", techniques)}"
        );

        if (model.Dummies != null)
        {
            var directory = Path.GetDirectoryName(modelPath.Replace('\\', '/')) ?? "";
            foreach (var (name, dummy) in model.Dummies)
            {
                if (
                    !name.Contains("subpart_")
                    || dummy.CustomData == null
                    || !dummy.CustomData.TryGetValue("file", out var file)
                )
                    continue;

                var subpartPath = Path.Combine(directory, (string)file) + ".mwm";
                found |= ScanModel(subpartPath, visited, ref box, report, depth + 1);
            }
        }

        return found;
    }

    private static bool ContainsSubtype(string list, string subtype)
    {
        if (string.IsNullOrWhiteSpace(list))
            return false;

        return list.Split(',')
            .Any(item => string.Equals(item.Trim(), subtype, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Logs the detection result for every seat definition, used to verify the heuristic.</summary>
    public static void LogReport()
    {
        var report = new StringBuilder();
        report.Append($"{Plugin.Name}: Cockpit glass detection report");
        foreach (
            var definition in MyDefinitionManager
                .Static.GetAllDefinitions()
                .OfType<MyCockpitDefinition>()
                .OrderBy(d => d.Id.SubtypeName)
        )
        {
            report.Append(
                $"\n  {definition.Id.TypeId}/{definition.Id.SubtypeName} dlc={string.Join("|", definition.DLCs ?? Array.Empty<string>())}"
            );
            Detect(definition, report);
        }

        MyLog.Default.WriteLine(report.ToString());
    }
}
