using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Simulation.Content;

/// <summary>A stable physical identity, shared by contact simulation and presentation response catalogues.</summary>
public sealed record SurfaceDef(byte Id, string Key);

/// <summary>Physical materials declared by content. Zero is explicitly unknown; it never silently means wood.</summary>
public sealed class PhysicalSurfaceData
{
    public const string FileName = "contact-surfaces.json";
    private readonly SurfaceDef?[] _ids = new SurfaceDef?[256];
    private readonly Dictionary<string, SurfaceDef> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, byte>> _maps = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Group, string Key, byte Part), byte> _parts = new();
    private readonly byte[] _ground = new byte[BiomeTable.MaterialCount];
    private IReadOnlyList<SurfaceDef> _definitions = Array.Empty<SurfaceDef>();
    private static readonly string[] Groups = { "terrain", "grades", "deployables", "doors", "natural", "ships", "shipParts", "deployableParts" };
    public static PhysicalSurfaceData Empty { get; } = new();
    public IReadOnlyList<SurfaceDef> Definitions => _definitions;
    public byte FleshId => Find("flesh")?.Id ?? 0;
    public byte WaterId => Find("water")?.Id ?? 0;
    public byte RockId => Find("rock")?.Id ?? 0;
    public string Key(byte id) => _ids[id]?.Key ?? "unknown";
    public SurfaceDef? Find(string key) => _keys.GetValueOrDefault(key);
    public byte FromGround(GroundMaterial material) => (uint)material < _ground.Length ? _ground[(int)material] : (byte)0;
    public byte FromGrade(string key) => Lookup("grades", key);
    public byte FromDeployable(string key, byte part = 0) => _parts.GetValueOrDefault(("deployableParts", key, part), Lookup("deployables", key));
    public byte FromDoor(string key) => Lookup("doors", key);
    public byte FromNatural(string key) => Lookup("natural", key);
    public byte FromShip(string key, byte part = 0) => _parts.GetValueOrDefault(("shipParts", key, part), Lookup("ships", key));
    private byte Lookup(string group, string key) => _maps.TryGetValue(group, out var map) ? map.GetValueOrDefault(key) : (byte)0;

    /// <summary>The actual terrain cover at a contact, including inland water. Callers may supply collision normals.</summary>
    public byte AtGround(ISurface surface, in ZoneFrame frame, Vector3 point, Vector3 normal = default)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) return 0;
        Vector3D body = frame.ToBody(point);
        if (!double.IsFinite(body.Length) || body.Length < 1e-6) return 0;
        Vector3D direction = body.Normalized;
        if (surface.Radius + surface.WaterLevelAt(direction) - body.Length > 0.05) return WaterId;
        Vector3 contactNormal = normal.LengthSquared() > 0.1f && float.IsFinite(normal.LengthSquared())
            ? Vector3.Normalize(normal) : new GroundProbe(surface, frame).Normal(point).Normal;
        double cosine = Math.Clamp(Vector3.Dot(contactNormal, frame.UpAt(point)), -1, 1);
        double slope = Math.Sqrt(Math.Max(0, 1 - cosine * cosine));
        Span<double> biomes = stackalloc double[BiomeTable.Count], materials = stackalloc double[BiomeTable.MaterialCount];
        surface.Cover(direction, surface.Sample(direction), slope, biomes, materials);
        Span<double> totals = stackalloc double[256];
        totals.Clear();
        for (int i = 0; i < materials.Length; i++)
            if (double.IsFinite(materials[i]) && materials[i] > 0) totals[FromGround((GroundMaterial)i)] += materials[i];
        int best = 0;
        for (int i = 1; i < totals.Length; i++) if (totals[i] > totals[best]) best = i;
        return (byte)best;
    }

    internal static PhysicalSurfaceData Parse(JsonElement root, JsonFields fields)
    {
        var data = new PhysicalSurfaceData();
        if (root.ValueKind != JsonValueKind.Object) return data;
        foreach (JsonElement item in fields.List(root, "definitions", FileName))
        {
            fields.OnlyKnown(item, FileName, "id", "key");
            int id = fields.Integer(item, "id", FileName);
            string key = fields.Text(item, "key", FileName);
            if (id is < 1 or > 255 || key.Length == 0 || key == "unknown")
            { fields.Problem(FileName, "surface IDs must be 1..255 and keys nonempty (unknown is reserved)"); continue; }
            var def = new SurfaceDef((byte)id, key);
            if (data._ids[id] != null || !data._keys.TryAdd(key, def))
            { fields.Problem(FileName, $"duplicate surface ID or key {id}/{key}"); continue; }
            data._ids[id] = def;
        }
        foreach (string group in Groups)
        {
            var map = new Dictionary<string, byte>(StringComparer.Ordinal);
            data._maps[group] = map;
            if (group.EndsWith("Parts", StringComparison.Ordinal) && !root.TryGetProperty(group, out _)) continue;
            if (!root.TryGetProperty(group, out JsonElement values) || values.ValueKind != JsonValueKind.Object)
            { fields.Problem(FileName, $"{group} must be an object"); continue; }
            foreach (JsonProperty property in values.EnumerateObject())
            {
                string? name = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                if (name == null || data.Find(name) is not { } surface)
                { fields.Problem(FileName, $"{group}.{property.Name}: unknown surface {name}"); continue; }
                if (!map.TryAdd(property.Name, surface.Id)) fields.Problem(FileName, $"{group}.{property.Name}: duplicate mapping");
                if (group.EndsWith("Parts", StringComparison.Ordinal))
                {
                    int slash = property.Name.LastIndexOf('/');
                    if (slash <= 0 || !byte.TryParse(property.Name.AsSpan(slash + 1), out byte part))
                        fields.Problem(FileName, $"{group}.{property.Name}: expected content-key/part-byte");
                    else if (!data._parts.TryAdd((group, property.Name[..slash], part), surface.Id))
                        fields.Problem(FileName, $"{group}.{property.Name}: duplicate part mapping");
                }
            }
        }
        foreach (string required in new[] { "flesh", "water", "rock" })
            if (data.Find(required) == null) fields.Problem(FileName, $"required surface {required} missing");
        for (int i = 0; i < data._ground.Length; i++) data._ground[i] = data.Lookup("terrain", ((GroundMaterial)i).ToString());
        data._definitions = data._keys.Values.OrderBy(s => s.Id).ToArray();
        return data;
    }

    internal IEnumerable<string> Missing(string group, IEnumerable<string> keys) => keys.Where(key => Lookup(group, key) == 0);
    internal IEnumerable<(string Group, string Key, byte Part)> PartMappings => _parts.Keys;
}
