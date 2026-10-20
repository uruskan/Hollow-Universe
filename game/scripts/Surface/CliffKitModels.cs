using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Surface;
using HollowUniverse.Items;

namespace HollowUniverse.Surface;

/// <summary>
/// The cliff kit's pieces as models (docs/WORLD_TERRAIN_PLAN.md §4.3, art/cliffkit): read from <c>cliffkit.json</c> for
/// the model viewer (<c>--view models --lineup cliffs</c>, <c>--model desert-xl-a</c>) and instanced with their authored
/// levels of detail switching at the kit's distances. Kept out of <c>models.json</c>, so that the ground game's warm-up
/// doesn't load them.
/// </summary>
public static class CliffKitModels
{
    public const string DataPath = "res://assets/surface/cliffkit/cliffkit.json";
    public const string Kind = "cliff";

    private static CliffKit? _kit;
    private static bool _loaded;

    public static CliffKit? Kit
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                if (FileAccess.FileExists(DataPath))
                {
                    _kit = CliffKit.Parse(FileAccess.GetFileAsString(DataPath));
                }
                else
                {
                    GD.PushWarning($"CliffKitModels: {DataPath} is missing (art/cliffkit/build.py writes it).");
                }
            }
            return _kit;
        }
    }

    /// <summary>The pieces as catalogue entries (kind "cliff", class the family).</summary>
    public static IEnumerable<ModelEntry> Entries()
    {
        if (Kit == null)
        {
            yield break;
        }
        foreach (CliffPiece p in Kit.Pieces)
        {
            float[] distances = new float[p.LodDistances.Count];
            for (int i = 0; i < distances.Length; i++)
            {
                distances[i] = (float)p.LodDistances[i];
            }
            string size = string.Format(CultureInfo.InvariantCulture, "{0:0.#} m long, {1:0.#} m high", p.Size.Z, p.Size.Y);
            yield return new ModelEntry(p.Key, Kind, p.Family, new[] { p.File }, p.LodTriangles.Count, false, null, null, 1f, 1,
                null, distances, $"{p.Family} {p.Role.ToString().ToLowerInvariant()}, {size}, LOD0 {p.LodTriangles[0]} triangles",
                null, null, new Dictionary<string, string>(), null);
        }
    }

    /// <summary>A new instance of a piece: its pivot on the ground line, its levels switching by distance.</summary>
    public static Node3D? Instantiate(ModelEntry entry)
    {
        string file = entry.Files[0];
        PackedScene? scene = ResourceLoader.Exists(file) ? ResourceLoader.Load<PackedScene>(file) : null;
        if (scene == null)
        {
            GD.PushWarning($"CliffKitModels: {entry.Key}: cannot load {file}");
            return null;
        }
        Node3D root = scene.Instantiate<Node3D>();
        root.Name = entry.Key;
        ModelCatalog.SetLodRanges(root, entry.LodDistances ?? Array.Empty<float>());
        return root;
    }
}
