using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;
using HollowUniverse.Ground;

namespace HollowUniverse.Items;

/// <summary>
/// Measures the drawn surfaces of the natural objects the ground game draws from catalogue models (the outcrops at
/// each depletion stage) for the rules (GROUND.md §6.1): the height of the model's top, straight down, at rings ×
/// directions about its base, in the model's own frame at size 1, as the game places it (<see cref="CatalogModels"/>).
/// The server puts the weak spot on these heights, so it sits on the rock players see at every stage. Written to
/// <c>data/ground/surfaces.json</c>; run again whenever those models change:
/// <c>--view models --survey-surfaces res://data/ground/surfaces.json</c>.
/// </summary>
public static class SurfaceSurvey
{
    /// <summary>Ring radii in metres at size 1 (the outcrops are about 2 m across).</summary>
    public static readonly float[] Rings = { 0.12f, 0.3f, 0.48f, 0.66f };

    /// <summary>Directions round each ring: the first along the model's +X, turning toward +Z.</summary>
    public const int Directions = 32;

    public static void Write(string path)
    {
        var models = new CatalogModels(new PlaceholderModels());
        var text = new StringBuilder();
        text.Append("{\n  \"source\": \"The drawn surfaces of natural objects drawn from catalogue models (GROUND.md §6.1), measured in the game by ")
            .Append("`--view models --survey-surfaces res://data/ground/surfaces.json` (game/scripts/Items/SurfaceSurvey.cs); do not edit by hand. ")
            .Append("For each kind and depletion stage: the height of the model's top (metres above its base at size 1) straight down at each ring ")
            .Append("(radius in metres) and direction (0 along the model's +X, turning toward +Z: the zone's right of the object's yaw, then its back); ")
            .Append("null where the ray finds nothing. The rules place the weak spot on these heights (gather.json spot surface \\\"measured\\\").\",\n");
        text.Append("  \"rings\": [").Append(string.Join(", ", Array.ConvertAll(Rings, r => r.ToString("0.###", CultureInfo.InvariantCulture)))).Append("],\n");
        text.Append("  \"directions\": ").Append(Directions).Append(",\n");
        text.Append("  \"kinds\": {");
        bool firstKind = true;
        foreach (ModelEntry entry in ModelCatalog.Entries)
        {
            if (entry.Class != "node" || entry.Stages < 2)
            {
                continue;
            }
            text.Append(firstKind ? "\n" : ",\n").Append("    \"").Append(entry.Key).Append("\": [");
            firstKind = false;
            for (int stage = 0; stage < entry.Stages; stage++)
            {
                Node3D? root = models.Outcrop(entry.Key, stage, 0);
                if (root == null)
                {
                    GD.PushError($"[survey] {entry.Key}: no model for stage {stage}");
                    continue;
                }
                List<Vector3> triangles = Triangles(root);
                root.Free();
                text.Append(stage == 0 ? "\n      [" : ",\n      [");
                float top = 0;
                for (int ring = 0; ring < Rings.Length; ring++)
                {
                    text.Append(ring == 0 ? "\n        [" : ",\n        [");
                    for (int d = 0; d < Directions; d++)
                    {
                        float angle = d * MathF.Tau / Directions;
                        float x = MathF.Cos(angle) * Rings[ring], z = MathF.Sin(angle) * Rings[ring];
                        float? height = TopAt(triangles, x, z);
                        text.Append(d == 0 ? "" : ", ").Append(height is { } h ? h.ToString("0.000", CultureInfo.InvariantCulture) : "null");
                        top = Math.Max(top, height ?? 0);
                    }
                    text.Append(']');
                }
                text.Append("\n      ]");
                GD.Print($"[survey] {entry.Key} stage {stage}: {triangles.Count / 3} triangles, top {top:0.00} m");
            }
            text.Append("\n    ]");
        }
        text.Append("\n  }\n}\n");
        string file = path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : path;
        File.WriteAllText(file, text.ToString());
        GD.Print($"[survey] wrote {file}");
    }

    // Every LOD-0 triangle of a model in its root's space (three points each).
    private static List<Vector3> Triangles(Node3D root)
    {
        var triangles = new List<Vector3>();
        foreach (MeshInstance3D mesh in ModelCatalog.Meshes(root))
        {
            if (ModelCatalog.LodOf(mesh) != 0 || mesh.Mesh == null)
            {
                continue;
            }
            Transform3D toRoot = ModelCatalog.RelativeTransform(root, mesh);
            foreach (Vector3 v in mesh.Mesh.GetFaces())
            {
                triangles.Add(toRoot * v);
            }
        }
        return triangles;
    }

    // The highest point of the triangles straight above (x, z), or null when none covers it.
    private static float? TopAt(List<Vector3> triangles, float x, float z)
    {
        float? best = null;
        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            Vector3 a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            if (x < Math.Min(a.X, Math.Min(b.X, c.X)) || x > Math.Max(a.X, Math.Max(b.X, c.X)) ||
                z < Math.Min(a.Z, Math.Min(b.Z, c.Z)) || z > Math.Max(a.Z, Math.Max(b.Z, c.Z)))
            {
                continue;
            }
            // Barycentric coordinates in the horizontal plane.
            float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
            if (MathF.Abs(d) < 1e-9f)
            {
                continue;
            }
            float u = ((b.Z - c.Z) * (x - c.X) + (c.X - b.X) * (z - c.Z)) / d;
            float v = ((c.Z - a.Z) * (x - c.X) + (a.X - c.X) * (z - c.Z)) / d;
            float w = 1 - u - v;
            if (u < -1e-5f || v < -1e-5f || w < -1e-5f)
            {
                continue;
            }
            float y = u * a.Y + v * b.Y + w * c.Y;
            if (best == null || y > best)
            {
                best = y;
            }
        }
        return best;
    }
}
