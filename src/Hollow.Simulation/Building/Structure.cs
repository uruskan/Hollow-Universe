using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Building;

/// <summary>
/// A structure's frame (GROUND.md §6.3): its origin in the zone and its axes, with +Y the planet's up at its first
/// foundation. Pieces snap in this frame, so a structure stays square on a curved world; on a 2.41 km world the ground
/// falls away from it by about a metre 67 m out, where pieces stop finding support (docs/WHOLE_WORLD.md §6.4). Kept in
/// double precision; the zone gets floats.
/// </summary>
public readonly struct BuildingFrame : IEquatable<BuildingFrame>
{
    public readonly Vector3D Origin;
    public readonly Vector3D AxisX;
    public readonly Vector3D AxisY;
    public readonly Vector3D AxisZ;

    public BuildingFrame(Vector3D origin, Vector3D axisX, Vector3D axisY, Vector3D axisZ)
    {
        Origin = origin;
        AxisX = axisX;
        AxisY = axisY;
        AxisZ = axisZ;
    }

    /// <summary>
    /// A frame at a zone point with +Y along <paramref name="up"/>, turned about it by <paramref name="headingRadians"/>
    /// from local east (the pole axis crossed with up; at a pole the body's −Z, as <c>Facing.Compass</c> has it), the same
    /// way piece yaws turn: +X east and +Z south at heading 0 (docs/WHOLE_WORLD.md §5).
    /// </summary>
    public static BuildingFrame At(Vector3D origin, Vector3D up, double headingRadians)
    {
        Vector3D y = up.Normalized;
        Vector3D x0 = EastAt(y);
        Vector3D z0 = Vector3D.Cross(x0, y);
        double c = Math.Cos(headingRadians), s = Math.Sin(headingRadians);
        return new BuildingFrame(origin, x0 * c - z0 * s, y, x0 * s + z0 * c);
    }

    /// <summary>Local east at a point with this (unit) up: the pole axis crossed with up, or the body's −Z at a pole.</summary>
    public static Vector3D EastAt(Vector3D up)
    {
        var east = new Vector3D(up.Z, 0, -up.X);
        return east.Length > 1e-4 ? east.Normalized : (new Vector3D(0, 0, -1) + up * up.Z).Normalized;
    }

    /// <summary>
    /// The heading <see cref="At"/> takes to turn a frame's +Z along a direction lying on the ground at a point with this
    /// up (the inverse of <see cref="At"/>'s turn).
    /// </summary>
    public static double HeadingOf(Vector3D direction, Vector3D up)
    {
        Vector3D x0 = EastAt(up.Normalized);
        Vector3D z0 = Vector3D.Cross(x0, up.Normalized);
        return Math.Atan2(Vector3D.Dot(direction, x0), Vector3D.Dot(direction, z0));
    }

    public Vector3D ToZone(Vector3D local) => Origin + AxisX * local.X + AxisY * local.Y + AxisZ * local.Z;

    public Vector3D ToLocal(Vector3D zone)
    {
        Vector3D d = zone - Origin;
        return new Vector3D(Vector3D.Dot(d, AxisX), Vector3D.Dot(d, AxisY), Vector3D.Dot(d, AxisZ));
    }

    public Vector3D DirectionToZone(Vector3D local) => AxisX * local.X + AxisY * local.Y + AxisZ * local.Z;

    public Vector3D DirectionToLocal(Vector3D zone) =>
        new(Vector3D.Dot(zone, AxisX), Vector3D.Dot(zone, AxisY), Vector3D.Dot(zone, AxisZ));

    /// <summary>The frame's rotation (local to zone).</summary>
    public Quaternion Rotation => Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
        (float)AxisX.X, (float)AxisX.Y, (float)AxisX.Z, 0,
        (float)AxisY.X, (float)AxisY.Y, (float)AxisY.Z, 0,
        (float)AxisZ.X, (float)AxisZ.Y, (float)AxisZ.Z, 0,
        0, 0, 0, 1)));

    /// <summary>A piece pose's position and rotation in the zone.</summary>
    public (Vector3 Position, Quaternion Rotation) ToZone(in Pose pose)
    {
        Vector3D p = ToZone(pose.Position);
        Quaternion yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(pose.Yaw * Math.PI / 6));
        return (new Vector3((float)p.X, (float)p.Y, (float)p.Z), Quaternion.Normalize(Rotation * yaw));
    }

    public bool Equals(BuildingFrame other) =>
        Origin == other.Origin && AxisX == other.AxisX && AxisY == other.AxisY && AxisZ == other.AxisZ;

    public override bool Equals(object? obj) => obj is BuildingFrame f && Equals(f);

    public override int GetHashCode() => HashCode.Combine(Origin, AxisX, AxisY);
}

/// <summary>A support link in a structure: <see cref="Other"/> and the stability the link loses (thousandths).</summary>
public readonly record struct Link(Piece Other, int Loss);

/// <summary>
/// One building piece: what it is, its grade and health, where it sits in its structure's frame, when it was placed and
/// last damaged, and its stability. A static entity: nothing about it runs per tick (ARCHITECTURE.md §10.2).
/// </summary>
public sealed class Piece : IBuildingPart
{
    internal readonly List<Link> SupportedBy = new();
    internal readonly List<Link> Supports = new();

    internal Piece(int handle, EntityId id, PieceDef def, int grade, Pose pose, ulong owner, double placedAt)
    {
        Handle = handle;
        Id = id;
        Def = def;
        Grade = grade;
        Pose = pose;
        Owner = owner;
        PlacedAt = placedAt;
    }

    /// <summary>A number unique in this world for the piece's life (process-local; <see cref="Id"/> is permanent).</summary>
    public int Handle { get; }

    public EntityId Id { get; }

    /// <summary>How the world names it (GROUND_SYSTEMS.md §3.1).</summary>
    public TargetRef Ref => new(TargetFamily.Piece, Id.Value);

    public PieceDef Def { get; }

    public int Grade { get; internal set; }

    public Pose Pose { get; internal set; }

    public Structure Structure { get; internal set; } = null!;

    public double Health { get; internal set; }

    /// <summary>The player who placed it (it grants nothing: rights come from claim devices).</summary>
    public ulong Owner { get; }

    /// <summary>Server seconds when it was placed: rotate and demolish are open for the edit window after.</summary>
    public double PlacedAt { get; }

    /// <summary>Server seconds of the last damage (repair waits after it), or −∞.</summary>
    public double LastDamagedAt { get; internal set; } = double.NegativeInfinity;

    /// <summary>Stability in thousandths: foundations 1000; 0 means unsupported.</summary>
    public int Stability { get; internal set; }

    /// <summary>Unsupported and coming down: it supports nothing and can't be used.</summary>
    public bool Collapsing { get; internal set; }

    /// <summary>A door fitted into this doorway or wall frame.</summary>
    public Door? Door { get; internal set; }

    /// <summary>Removed from the world.</summary>
    public bool Gone { get; internal set; }

    /// <summary>The claim device of its structure, or null.</summary>
    public ClaimDevice? Base => Structure?.Claim;

    internal uint Body;

    /// <summary>Its Jolt body's ID (0: none), replicated so a client's prediction meets the same contacts.</summary>
    public uint BodyId => Body;

    public double MaxHealth => Structure.World.Data.Grades[Grade].Health;

    public double StabilityPercent => Stability / 10.0;

    /// <summary>The pieces holding this one up, and how much each link loses.</summary>
    public IReadOnlyList<Link> Supporters => SupportedBy;

    /// <summary>The pieces this one holds up.</summary>
    public IReadOnlyList<Link> Dependants => Supports;

    /// <summary>Position and rotation in the zone.</summary>
    public (Vector3 Position, Quaternion Rotation) ZoneTransform => Structure.Frame.ToZone(Pose);

    public override string ToString() => $"{Def.Key} #{Handle} ({Structure?.World.Data.Grades[Grade].Key}) at {Pose.Position}";
}

/// <summary>
/// A connected structure with its own frame (GROUND.md §6.3): the pieces linked to each other through sockets, from its
/// first foundation. Demolishing a connector splits it into structures that keep the frame; a piece that links two
/// structures of the same frame joins them. Stability is solved per structure.
/// </summary>
public sealed class Structure
{
    private readonly Dictionary<(int, int, int), List<Piece>> _cells = new();
    internal readonly HashSet<Piece> PieceSet = new();

    internal Structure(BuildingWorld world, int number, BuildingFrame frame)
    {
        World = world;
        Number = number;
        Frame = frame;
    }

    public BuildingWorld World { get; }

    /// <summary>Unique in the world for the structure's life.</summary>
    public int Number { get; }

    public BuildingFrame Frame { get; }

    public IReadOnlyCollection<Piece> Pieces => PieceSet;

    public int Count => PieceSet.Count;

    /// <summary>The claim device standing on this structure, if any.</summary>
    public ClaimDevice? Claim { get; internal set; }

    /// <summary>Fractions of upkeep paid but not yet taken as whole items, per material (see <see cref="Upkeep"/>).</summary>
    internal readonly Dictionary<string, double> UpkeepDebt = new(StringComparer.Ordinal);

    internal const double Cell = 3.0;

    private static (int, int, int) CellOf(Vector3D p) =>
        ((int)Math.Floor(p.X / Cell), (int)Math.Floor(p.Y / Cell), (int)Math.Floor(p.Z / Cell));

    internal void Index(Piece piece)
    {
        (int, int, int) key = CellOf(piece.Pose.Position);
        if (!_cells.TryGetValue(key, out List<Piece>? list))
        {
            list = new List<Piece>();
            _cells.Add(key, list);
        }
        list.Add(piece);
    }

    internal void Unindex(Piece piece, Pose pose)
    {
        (int, int, int) key = CellOf(pose.Position);
        if (_cells.TryGetValue(key, out List<Piece>? list))
        {
            list.Remove(piece);
            if (list.Count == 0)
            {
                _cells.Remove(key);
            }
        }
    }

    /// <summary>Pieces whose pivot lies within <paramref name="radius"/> of a frame point (by grid cells, then distance).</summary>
    public void Near(Vector3D point, double radius, List<Piece> into)
    {
        int r = (int)Math.Ceiling(radius / Cell);
        (int cx, int cy, int cz) = CellOf(point);
        double r2 = radius * radius;
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                for (int z = cz - r; z <= cz + r; z++)
                {
                    if (!_cells.TryGetValue((x, y, z), out List<Piece>? list))
                    {
                        continue;
                    }
                    foreach (Piece p in list)
                    {
                        if ((p.Pose.Position - point).LengthSquared <= r2)
                        {
                            into.Add(p);
                        }
                    }
                }
            }
        }
    }

    /// <summary>A live piece of a placement family at a pose (the spot is taken), or null.</summary>
    public Piece? At(PieceDef def, in Pose pose)
    {
        (int cx, int cy, int cz) = CellOf(pose.Position);
        int family = PieceGeometry.PlaceFamily(def);
        for (int x = cx - 1; x <= cx + 1; x++)
        {
            for (int y = cy - 1; y <= cy + 1; y++)
            {
                for (int z = cz - 1; z <= cz + 1; z++)
                {
                    if (!_cells.TryGetValue((x, y, z), out List<Piece>? list))
                    {
                        continue;
                    }
                    foreach (Piece p in list)
                    {
                        if (!p.Collapsing && PieceGeometry.PlaceFamily(p.Def) == family &&
                            (p.Pose.Position - pose.Position).LengthSquared <= 1e-4)
                        {
                            return p;
                        }
                    }
                }
            }
        }
        return null;
    }

    public override string ToString() => $"structure {Number} ({Count} pieces)";
}
