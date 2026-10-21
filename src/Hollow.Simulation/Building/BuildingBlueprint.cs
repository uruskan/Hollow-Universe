using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;

namespace Hollow.Simulation.Building;

/// <summary>An authored, socket-linked building assembled through the ordinary building world.</summary>
public sealed class BuildingBlueprint
{
    public sealed class NodeSpec
    {
        public string Id { get; set; } = "";
        public string Piece { get; set; } = "";
        public string? Parent { get; set; }
        public int Socket { get; set; }
        public int Orientation { get; set; }
        public bool Door { get; set; }
    }

    public sealed class Spec
    {
        public string Name { get; set; } = "";
        public NodeSpec[] Nodes { get; set; } = Array.Empty<NodeSpec>();
        public string[] DoorsByGrade { get; set; } = Array.Empty<string>();
        public string ClaimSupport { get; set; } = "";
        public double[] ClaimOffset { get; set; } = Array.Empty<double>();
        public double[] Entrance { get; set; } = Array.Empty<double>();
    }

    public sealed record Node(string Id, PieceDef Def, Pose Pose, bool Door);

    private readonly BuildingData _data;
    private readonly DoorDef[] _doors;
    private readonly int _claimIndex;
    private readonly Vector3D _claimPosition;

    private BuildingBlueprint(BuildingData data, Spec spec)
    {
        _data = data;
        Name = spec.Name;
        if (string.IsNullOrWhiteSpace(Name) || spec.Nodes == null || spec.Nodes.Length is < 1 or > 1024)
            throw new ContentException("A building blueprint needs a name and 1–1024 nodes.");
        if (spec.DoorsByGrade == null || spec.DoorsByGrade.Length != data.Grades.Count
            || spec.DoorsByGrade.Any(string.IsNullOrWhiteSpace))
            throw new ContentException("A blueprint must name a door for each building grade.");
        _doors = spec.DoorsByGrade.Select(key => data.Door(key)
            ?? throw new ContentException($"Unknown blueprint door '{key}'.")).ToArray();
        var nodes = new List<Node>();
        var named = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (NodeSpec entry in spec.Nodes)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Piece))
                throw new ContentException("A blueprint node must name a piece.");
            PieceDef def = data.Piece(entry.Piece)
                ?? throw new ContentException($"Unknown blueprint piece '{entry.Piece}'.");
            Pose pose = new(Vector3D.Zero, 0);
            if (nodes.Count == 0)
            {
                if (entry.Parent != null || def.Family != PieceFamily.Foundation)
                    throw new ContentException("A blueprint starts with a foundation without a parent.");
            }
            else if (entry.Parent == null || !named.TryGetValue(entry.Parent, out Node? parent)
                || SocketId.FromCode(entry.Socket) is not { } socket
                || !PieceGeometry.Snap(data, parent.Def, parent.Pose, socket, def, entry.Orientation, out pose))
                throw new ContentException($"Blueprint node '{entry.Id}' has an invalid parent/socket/orientation.");
            var node = new Node(entry.Id, def, pose, entry.Door);
            if (string.IsNullOrWhiteSpace(entry.Id) || !named.TryAdd(entry.Id, node)
                || nodes.Any(other => other.Def.Family == def.Family && PieceGeometry.SamePose(other.Pose, pose)))
                throw new ContentException($"Duplicate blueprint node or occupied socket at '{entry.Id}'.");
            if (entry.Door && _doors.Any(door => door.Opening != def.Opening))
                throw new ContentException($"The door does not fit blueprint node '{entry.Id}'.");
            nodes.Add(node);
        }
        Nodes = nodes.AsReadOnly();
        _claimIndex = nodes.FindIndex(n => n.Id == spec.ClaimSupport);
        if (_claimIndex < 0 || !nodes[_claimIndex].Def.IsSlab)
            throw new ContentException("A blueprint cupboard needs a slab support.");
        Vector3D offset = ReadPoint(spec.ClaimOffset, "cupboard offset");
        if (Math.Abs(offset.X) > data.Width / 2 - data.ClaimSize.X / 2
            || Math.Abs(offset.Z) > data.Width / 2 - data.ClaimSize.Z / 2 || Math.Abs(offset.Y) > 0.001)
            throw new ContentException("The blueprint cupboard must fit on its support.");
        _claimPosition = PieceGeometry.ToFrame(nodes[_claimIndex].Pose, offset);
        Entrance = ReadPoint(spec.Entrance, "entrance");
        var points = nodes.SelectMany(n => PieceShapes.For(data, n.Def)
            .SelectMany(part => part.Points).Select(p => PieceGeometry.ToFrame(n.Pose, new Vector3D(p.X, p.Y, p.Z)))).ToArray();
        Min = new Vector3D(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z));
        Max = new Vector3D(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
        if ((Max - Min).Length > 80 || Entrance.X < Min.X || Entrance.X > Max.X
            || Entrance.Z < Min.Z || Entrance.Z > Max.Z)
            throw new ContentException("Blueprint bounds or entrance are invalid.");

        // Validate support once, before the live zone can receive any of its pieces or events.
        ulong next = 0;
        using var check = new BuildingWorld(data, null, () => new EntityId(++next));
        IReadOnlyList<Piece> built = InsertPieces(check, BuildingFrame.At(Vector3D.Zero, Vector3D.UnitY, 0), 0, 1, 0);
        if (check.Collapsing != 0 || built.Any(p => p.Stability <= 0) || check.Structures.Count != 1)
            throw new ContentException("Blueprint contains unsupported or disconnected pieces.");
    }

    public string Name { get; }
    public IReadOnlyList<Node> Nodes { get; }
    public Vector3D Min { get; }
    public Vector3D Max { get; }
    public Vector3D Entrance { get; }

    public static BuildingBlueprint Parse(BuildingData data, string json)
    {
        try
        {
            return new BuildingBlueprint(data, JsonSerializer.Deserialize<Spec>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new ContentException("Empty building blueprint."));
        }
        catch (JsonException e) { throw new ContentException("Invalid building blueprint JSON: " + e.Message); }
    }

    private static Vector3D ReadPoint(double[] values, string name) =>
        values is { Length: 3 } && values.All(double.IsFinite) ? new(values[0], values[1], values[2])
        : throw new ContentException("Invalid blueprint " + name + ".");

    public IEnumerable<Vector3D> GroundSamples => Nodes.Where(n => n.Def.Family == PieceFamily.Foundation)
        .SelectMany(n => PieceGeometry.Footprint(n.Def.Shape, _data.Width).Append(Vector3D.Zero)
            .Select(p => PieceGeometry.ToFrame(n.Pose, p)));

    private IReadOnlyList<Piece> InsertPieces(BuildingWorld world, BuildingFrame frame, int grade, ulong owner, double now)
    {
        var pieces = new List<Piece>(Nodes.Count);
        world.BeginBatch();
        try
        {
            foreach (Node node in Nodes)
                pieces.Add(pieces.Count == 0 ? world.AddFirst(frame, node.Def, grade, owner, now)
                    : world.Add(pieces[0].Structure, node.Def, grade, node.Pose, owner, now));
        }
        finally { world.EndBatch(now); }
        return pieces;
    }

    /// <summary>Caller checks the site first. IDs, events, physics and ownership use the normal building world.</summary>
    public IReadOnlyList<Piece> Place(BuildingWorld world, BuildingFrame frame, int grade, ulong owner,
        double now, Func<EntityId> nextId)
    {
        if (!ReferenceEquals(world.Data, _data) || grade < 0 || grade >= _data.Grades.Count)
            throw new ArgumentException("Blueprint data/grade does not match the building world.");
        IReadOnlyList<Piece> pieces = InsertPieces(world, frame, grade, owner, now);
        DoorDef door = _doors[grade];
        for (int i = 0; i < pieces.Count; i++)
            if (Nodes[i].Door)
                world.RestoreDoor(nextId(), door, pieces[i], 0, owner, false, door.Health);
        world.RestoreClaim(nextId(), owner, pieces[_claimIndex], _claimPosition, 0,
            _data.ClaimHealth, new[] { owner });
        return pieces;
    }
}
