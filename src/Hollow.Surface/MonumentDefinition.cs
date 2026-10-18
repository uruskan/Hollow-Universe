using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Hollow.Core;

namespace Hollow.Surface;

public readonly record struct MonumentPiece(string Key, Vector3D At, double X, double Y, double Z, double W);
public readonly record struct MonumentLoot(Vector3D At, float Yaw, string Table);

/// <summary>Original Hollow assemblies: presentation, usable loot anchors and authored solid collision together.</summary>
public sealed class MonumentDefinition
{
    private static readonly Lazy<IReadOnlyList<MonumentDefinition>> Loaded = new(() => Array.AsReadOnly(Read()));
    public static IReadOnlyList<MonumentDefinition> All => Loaded.Value;
    public string Key { get; private init; } = "";
    public string Title { get; private init; } = "";
    public string Model { get; private init; } = "";
    public double Radius { get; private init; }
    public string Ground { get; private init; } = "";
    public IReadOnlyList<MonumentPiece> Pieces { get; private init; } = Array.Empty<MonumentPiece>();
    public IReadOnlyList<MonumentLoot> Loot { get; private init; } = Array.Empty<MonumentLoot>();
    internal Vector3D[] Positions { get; private init; } = Array.Empty<Vector3D>();
    internal int[] Indices { get; private init; } = Array.Empty<int>();

    private static Vector3D Vec(JsonElement p) => new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble());
    private static MonumentDefinition[] Read()
    {
        using Stream data = typeof(MonumentDefinition).Assembly.GetManifestResourceStream("Hollow.Surface.Data.monuments.json")
            ?? throw new InvalidOperationException("Shared monument definitions are missing.");
        using JsonDocument doc = JsonDocument.Parse(data);
        var result = new List<MonumentDefinition>();
        // The archive's proposed specialist tables are mapped onto shipped loot tables until that content exists.
        var tables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["barrel"] = "barrel", ["crate"] = "crate", ["food"] = "crate", ["cloth"] = "crate",
            ["tech-scrap"] = "military-crate", ["ship-parts"] = "military-crate", ["chip-amber"] = "military-crate",
        };
        foreach (JsonElement row in doc.RootElement.EnumerateArray())
        {
            Vector3D min = Vec(row.GetProperty("bounds").GetProperty("min"));
            Vector3D max = Vec(row.GetProperty("bounds").GetProperty("max"));
            double x = Math.Max(Math.Abs(min.X), Math.Abs(max.X)), z = Math.Max(Math.Abs(min.Z), Math.Abs(max.Z));
            var pieces = new List<MonumentPiece>();
            foreach (JsonElement p in row.GetProperty("pieces").EnumerateArray())
            {
                JsonElement q = p.GetProperty("rot");
                pieces.Add(new MonumentPiece(p.GetProperty("piece").GetString()!, Vec(p.GetProperty("at")),
                    q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble(), q[0].GetDouble()));
            }
            var loot = new List<MonumentLoot>();
            string key = row.GetProperty("key").GetString()!;
            foreach (JsonElement marker in row.GetProperty("markers").EnumerateArray())
                if (marker.GetProperty("kind").GetString() == "loot")
                {
                    string table = marker.GetProperty("table").GetString()!;
                    loot.Add(new MonumentLoot(Vec(marker.GetProperty("at")), marker.GetProperty("yaw").GetSingle(),
                        key == "wellhead" && loot.Count == 0 ? "fuel-barrel" : tables.GetValueOrDefault(table, "crate")));
                }
            result.Add(new MonumentDefinition
            {
                Key = key, Title = row.GetProperty("title").GetString()!, Model = row.GetProperty("model").GetString()!,
                Radius = Math.Sqrt(x * x + z * z) + 2, Ground = row.GetProperty("pad").GetProperty("ground").GetString()!,
                Pieces = pieces, Loot = loot, Positions = row.GetProperty("positions").EnumerateArray().Select(Vec).ToArray(),
                Indices = row.GetProperty("indices").EnumerateArray().Select(e => e.GetInt32()).ToArray(),
            });
        }
        return result.ToArray();
    }
}
