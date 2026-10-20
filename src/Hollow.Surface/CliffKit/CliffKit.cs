using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>What a piece is for when cliffs are placed (docs/WORLD_TERRAIN_PLAN.md §4.3).</summary>
public enum CliffRole
{
    /// <summary>A length of cliff face, joined to others at its sockets.</summary>
    Straight,
    /// <summary>A convex corner: its sockets look out along its two wings.</summary>
    Corner,
    /// <summary>A slope end: one end falls to the ground and has no socket (<see cref="CliffPiece.CapLeft"/>, ...).</summary>
    End,
    /// <summary>A low step of rock.</summary>
    Ledge,
    /// <summary>A formation jutting from hills and mountains.</summary>
    Jut,
    /// <summary>A filler for feet and joints, without sockets.</summary>
    Rubble,
}

/// <summary>A family's shared tiling detail and where the family belongs.</summary>
public sealed record CliffFamily(string Key, string DetailAlbedo, string DetailNormal, double DetailMetres,
    IReadOnlyList<string> Places);

/// <summary>
/// A place where a chain joins: the next piece's opposite socket goes on this one, its direction reversed. Directions
/// point out of the piece along the chain.
/// </summary>
public sealed record CliffSocket(string Name, Vector3D Position, Vector3D Direction);

/// <summary>
/// A point the ground is checked against: the ground's height at (x, z) must lie between Y − <see cref="Below"/> and
/// Y + <see cref="Above"/>, or the piece is dropped rather than patched. "foot": along the face's foot (floating or
/// buried); "top": just behind the lip (the open back would show).
/// </summary>
public sealed record CliffCheck(string Kind, Vector3D Position, double Below, double Above);

/// <summary>The rock's top over a grid on the ground plane (x, z), for the stamp that fits the ground to the piece.</summary>
public sealed record CliffGrid(double Cell, double OriginX, double OriginZ, int Columns, int Rows, double?[] Top)
{
    /// <summary>The top at a point of the piece's ground plane, or null where the piece doesn't stand.</summary>
    public double? TopAt(double x, double z)
    {
        int i = (int)Math.Floor((x - OriginX) / Cell), j = (int)Math.Floor((z - OriginZ) / Cell);
        return i < 0 || j < 0 || i >= Columns || j >= Rows ? null : Top[j * Columns + i];
    }
}

/// <summary>Where a piece's collision shell is and what it holds (<see cref="CliffCollision"/>).</summary>
public sealed record CliffCollisionFile(string File, int Vertices, int Triangles, string Sha256);

/// <summary>
/// One piece of the kit, in its own frame: metres, +Y up, its face toward +X, its pivot on the ground line at the middle
/// of the face's foot, its left end (as you face it) toward +Z. A placement scales it uniformly within
/// [<see cref="ScaleMin"/>, <see cref="ScaleMax"/>].
/// </summary>
public sealed record CliffPiece(
    string Key,
    string Family,
    CliffRole Role,
    string File,
    string Material,
    Vector3D Size,
    Vector3D BoundsMin,
    Vector3D BoundsMax,
    double Top,
    double Skirt,
    IReadOnlyList<CliffSocket> Sockets,
    bool CapLeft,
    bool CapRight,
    double ScaleMin,
    double ScaleMax,
    IReadOnlyList<Vector3D> Anchors,
    IReadOnlyList<CliffCheck> Checks,
    IReadOnlyList<(double X, double Z)> Footprint,
    IReadOnlyList<(double X, double Z)> Outline,
    CliffGrid Grid,
    IReadOnlyList<int> LodTriangles,
    IReadOnlyList<double> LodDistances,
    int ShadowLod,
    CliffCollisionFile Collision)
{
    public CliffSocket? Socket(string name)
    {
        foreach (CliffSocket s in Sockets)
        {
            if (s.Name == name)
            {
                return s;
            }
        }
        return null;
    }
}

/// <summary>
/// The cliff kit's placement data, <c>game/assets/surface/cliffkit/cliffkit.json</c>, written by
/// <c>art/cliffkit/build.py</c> (docs/WORLD_TERRAIN_PLAN.md §4.3, iteration 4). Paths in it are Godot's (res:// is the
/// game folder); <see cref="Resolve"/> turns one into a file path.
/// </summary>
public sealed class CliffKit
{
    public const string DefaultPath = "assets/surface/cliffkit/cliffkit.json";

    public int Version { get; }
    /// <summary>The deepest any piece reaches below its ground line, metres (each piece's own: <see cref="CliffPiece.Skirt"/>).</summary>
    public double SkirtMax { get; }
    public IReadOnlyDictionary<string, CliffFamily> Families { get; }
    public IReadOnlyList<CliffPiece> Pieces { get; }

    private CliffKit(int version, double skirtMax, IReadOnlyDictionary<string, CliffFamily> families, IReadOnlyList<CliffPiece> pieces)
    {
        (Version, SkirtMax, Families, Pieces) = (version, skirtMax, families, pieces);
    }

    public CliffPiece? Find(string key)
    {
        foreach (CliffPiece p in Pieces)
        {
            if (p.Key == key)
            {
                return p;
            }
        }
        return null;
    }

    /// <summary>A res:// path of the kit as a file path under the game folder.</summary>
    public static string Resolve(string gameFolder, string resPath) =>
        Path.Combine(gameFolder, resPath.StartsWith("res://", StringComparison.Ordinal) ? resPath[6..] : resPath);

    /// <summary>Reads the kit from the game folder (<see cref="DefaultPath"/> under it).</summary>
    public static CliffKit Load(string gameFolder) => Parse(File.ReadAllText(Path.Combine(gameFolder, DefaultPath)));

    public static CliffKit Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        var families = new Dictionary<string, CliffFamily>(StringComparer.Ordinal);
        foreach (JsonProperty f in root.GetProperty("families").EnumerateObject())
        {
            JsonElement v = f.Value;
            families[f.Name] = new CliffFamily(f.Name, v.GetProperty("albedo").GetString()!, v.GetProperty("normal").GetString()!,
                v.GetProperty("metres").GetDouble(), Strings(v.GetProperty("places")));
        }
        var pieces = new List<CliffPiece>();
        foreach (JsonElement p in root.GetProperty("pieces").EnumerateArray())
        {
            pieces.Add(Piece(p));
        }
        return new CliffKit(root.GetProperty("version").GetInt32(), root.GetProperty("skirtMax").GetDouble(), families, pieces);
    }

    private static CliffPiece Piece(JsonElement p)
    {
        string key = p.GetProperty("key").GetString()!;
        var sockets = new List<CliffSocket>();
        foreach (JsonElement s in p.GetProperty("sockets").EnumerateArray())
        {
            Vector3D d = Vec(s.GetProperty("direction"));
            if (d.LengthSquared < 0.5)
            {
                throw new InvalidDataException($"cliff kit: {key}: socket {s.GetProperty("name").GetString()} has no direction");
            }
            sockets.Add(new CliffSocket(s.GetProperty("name").GetString()!, Vec(s.GetProperty("position")), d.Normalized));
        }
        var checks = new List<CliffCheck>();
        foreach (JsonElement c in p.GetProperty("checks").EnumerateArray())
        {
            checks.Add(new CliffCheck(c.GetProperty("kind").GetString()!, Vec(c.GetProperty("position")),
                c.GetProperty("below").GetDouble(), c.GetProperty("above").GetDouble()));
        }
        var anchors = new List<Vector3D>();
        foreach (JsonElement a in p.GetProperty("anchors").EnumerateArray())
        {
            anchors.Add(Vec(a));
        }
        JsonElement g = p.GetProperty("grid");
        var top = new List<double?>();
        foreach (JsonElement h in g.GetProperty("top").EnumerateArray())
        {
            top.Add(h.ValueKind == JsonValueKind.Null ? null : h.GetDouble());
        }
        int columns = g.GetProperty("columns").GetInt32(), rows = g.GetProperty("rows").GetInt32();
        if (top.Count != columns * rows)
        {
            throw new InvalidDataException($"cliff kit: {key}: its grid has {top.Count} heights for {columns} × {rows} cells");
        }
        JsonElement origin = g.GetProperty("origin");
        var lods = new List<int>();
        foreach (JsonElement l in p.GetProperty("lods").EnumerateArray())
        {
            lods.Add(l.GetProperty("triangles").GetInt32());
        }
        var distances = new List<double>();
        foreach (JsonElement d in p.GetProperty("lodDistances").EnumerateArray())
        {
            distances.Add(d.GetDouble());
        }
        JsonElement scale = p.GetProperty("scale");
        JsonElement caps = p.GetProperty("caps");
        JsonElement col = p.GetProperty("collision");
        JsonElement bounds = p.GetProperty("bounds");
        return new CliffPiece(
            key,
            p.GetProperty("family").GetString()!,
            Enum.Parse<CliffRole>(p.GetProperty("role").GetString()!, ignoreCase: true),
            p.GetProperty("file").GetString()!,
            p.GetProperty("material").GetString()!,
            Vec(p.GetProperty("size")),
            Vec(bounds.GetProperty("min")),
            Vec(bounds.GetProperty("max")),
            p.GetProperty("top").GetDouble(),
            p.GetProperty("skirt").GetDouble(),
            sockets,
            caps.GetProperty("left").GetBoolean(),
            caps.GetProperty("right").GetBoolean(),
            scale[0].GetDouble(),
            scale[1].GetDouble(),
            anchors,
            checks,
            Plan(p.GetProperty("footprint")),
            Plan(p.GetProperty("outline")),
            new CliffGrid(g.GetProperty("cell").GetDouble(), origin[0].GetDouble(), origin[1].GetDouble(), columns, rows, top.ToArray()),
            lods,
            distances,
            p.GetProperty("shadowLod").GetInt32(),
            new CliffCollisionFile(col.GetProperty("file").GetString()!, col.GetProperty("vertices").GetInt32(),
                col.GetProperty("triangles").GetInt32(), col.GetProperty("sha256").GetString()!));
    }

    private static Vector3D Vec(JsonElement a) => new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble());

    private static List<(double X, double Z)> Plan(JsonElement a)
    {
        var points = new List<(double X, double Z)>();
        foreach (JsonElement p in a.EnumerateArray())
        {
            points.Add((p[0].GetDouble(), p[1].GetDouble()));
        }
        return points;
    }

    private static List<string> Strings(JsonElement a)
    {
        var list = new List<string>();
        foreach (JsonElement s in a.EnumerateArray())
        {
            list.Add(s.GetString()!);
        }
        return list;
    }
}

/// <summary>
/// A piece's closed collision shell (<c>&lt;key&gt;.collision.bin</c>, little-endian): "HCOL", version 1, vertex and
/// triangle counts, the vertices (float x, y, z, the piece's frame), the triangles (three unsigned 16-bit indices each,
/// counter-clockwise seen from outside).
/// </summary>
public sealed class CliffCollision
{
    public Vector3D[] Vertices { get; }
    public int[] Indices { get; }

    private CliffCollision(Vector3D[] vertices, int[] indices) => (Vertices, Indices) = (vertices, indices);

    public static CliffCollision Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Read(stream);
    }

    public static CliffCollision Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "HCOL" || reader.ReadUInt32() != 1)
        {
            throw new InvalidDataException("cliff collision: not an HCOL version 1 file");
        }
        int vertexCount = (int)reader.ReadUInt32(), triangleCount = (int)reader.ReadUInt32();
        var vertices = new Vector3D[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = new Vector3D(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
        var indices = new int[triangleCount * 3];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = reader.ReadUInt16();
            if (indices[i] >= vertexCount)
            {
                throw new InvalidDataException($"cliff collision: index {indices[i]} past {vertexCount} vertices");
            }
        }
        return new CliffCollision(vertices, indices);
    }
}
