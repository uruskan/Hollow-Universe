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
    /// <summary>Authored local Z relative to downslope; -1 rotates both horizontal axes by half a turn.</summary>
    public double Facing { get; }
    public double MinimumGradient { get; }
    public double MaximumGradient { get; }
    public double SelectionWeight { get; }
    /// <summary>Compatible source rock/material families; one formation never randomly mixes unrelated scans.</summary>
    public string MaterialFamily { get; }
    /// <summary>Shared collision bounds. Presentation can use its more detailed visual mesh bounds.</summary>
    public Vector3D BoundsMin { get; }
    public Vector3D BoundsMax { get; }
    public double BoundingRadius { get; }
    internal Vector3D[] Positions { get; }
    internal int[] Indices { get; }

    private CliffGeometry(string key, double height, Vector3D[] positions, int[] indices,
        double facing, double minimumGradient, double maximumGradient, double selectionWeight, string materialFamily)
    {
        (Key, Height, Positions, Indices) = (key, height, positions, indices);
        if ((facing != 1 && facing != -1) || !double.IsFinite(minimumGradient) ||
            !double.IsFinite(maximumGradient) || minimumGradient < 0 || maximumGradient <= minimumGradient ||
            !double.IsFinite(selectionWeight) || selectionWeight <= 0 || string.IsNullOrWhiteSpace(materialFamily))
            throw new InvalidOperationException("Invalid cliff placement metadata: " + key);
        (Facing, MinimumGradient, MaximumGradient, SelectionWeight) =
            (facing, minimumGradient, maximumGradient, selectionWeight);
        MaterialFamily = materialFamily;
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
        double radius = 0;
        foreach (Vector3D p in positions)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
            radius = Math.Max(radius, p.Length);
        }
        BoundsMin = new Vector3D(minX, minY, minZ);
        BoundsMax = new Vector3D(maxX, maxY, maxZ);
        BoundingRadius = radius;
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
            JsonElement placement = row.GetProperty("placement");
            result.Add(new CliffGeometry(row.GetProperty("key").GetString()!, row.GetProperty("height").GetDouble(), vertices, indices,
                placement.GetProperty("facing").GetDouble(), placement.GetProperty("minimumGradient").GetDouble(),
                placement.GetProperty("maximumGradient").GetDouble(), placement.GetProperty("selectionWeight").GetDouble(),
                placement.GetProperty("materialFamily").GetString()!));
        }
        return result.ToArray();
    }
}
