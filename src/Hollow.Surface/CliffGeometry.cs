using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Low-complexity collision derived from the same owned cliff scans the client draws.</summary>
public sealed class CliffGeometry
{
    private static readonly Lazy<CliffGeometry[]> Loaded = new(Read);
    public static IReadOnlyList<CliffGeometry> All => Loaded.Value;
    public string Key { get; }
    public double Height { get; }
    public double Width { get; }
    public double Depth { get; }
    internal Vector3D[] Positions { get; }
    internal int[] Indices { get; }

    private CliffGeometry(string key, double height, Vector3D[] positions, int[] indices)
    {
        (Key, Height, Positions, Indices) = (key, height, positions, indices);
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
        foreach (Vector3D p in positions)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
        }
        Width = maxX - minX;
        Depth = maxZ - minZ;
    }

    private static CliffGeometry[] Read()
    {
        using Stream data = typeof(CliffGeometry).Assembly.GetManifestResourceStream("Hollow.Surface.Data.cliffs.json")
            ?? throw new InvalidOperationException("Shared cliff collision data is missing.");
        using JsonDocument doc = JsonDocument.Parse(data);
        var result = new List<CliffGeometry>();
        foreach (JsonElement row in doc.RootElement.EnumerateArray())
        {
            JsonElement points = row.GetProperty("positions"), faces = row.GetProperty("indices");
            var vertices = new Vector3D[points.GetArrayLength()];
            for (int i = 0; i < vertices.Length; i++)
            {
                JsonElement p = points[i];
                vertices[i] = new Vector3D(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble());
            }
            var indices = new int[faces.GetArrayLength()];
            for (int i = 0; i < indices.Length; i++) indices[i] = faces[i].GetInt32();
            result.Add(new CliffGeometry(row.GetProperty("key").GetString()!, row.GetProperty("height").GetDouble(), vertices, indices));
        }
        return result.ToArray();
    }
}
