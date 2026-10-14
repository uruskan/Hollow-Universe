using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using JoltPhysicsSharp;

namespace Hollow.Simulation.Building;

/// <summary>
/// Building in one zone (GROUND.md §6.3): its structures and pieces, doors and claim devices, and every rule about them,
/// engine-free. Placement, the hammer, doors and claims are requests that answer with an <see cref="Outcome"/>; what
/// changed is announced through <see cref="Changed"/>. Pieces are static entities: nothing runs per tick except a
/// collapse in progress (ARCHITECTURE.md §10.2), and decay and upkeep are applied in closed form over elapsed time
/// (§10.4). One thread at a time (the zone's).
/// </summary>
public sealed partial class BuildingWorld : IDisposable
{
    /// <summary>Linked pivots are never further apart than this (a roof on a roof's high edge: 4.24 m).</summary>
    private const double LinkRadius = 4.5;

    /// <summary>The zone index's cell, in metres.</summary>
    private const float ZoneCell = 4f;

    private readonly Func<EntityId> _newId;
    private readonly HashSet<Structure> _structures = new();
    private readonly Dictionary<BuildingFrame, List<Structure>> _lattices = new();
    private readonly Dictionary<(int, int), List<Piece>> _zoneCells = new();
    private readonly Dictionary<PieceDef, ConvexPart[]> _parts = new();
    private readonly List<(double At, Piece Piece)> _collapse = new();
    private readonly List<SocketId> _sockets = new();
    private readonly List<Piece> _near = new();
    private int _nextHandle = 1;
    private int _nextStructure = 1;
    private int _pieceCount;

    /// <param name="physics">The zone's Jolt world, or null for rules without collision (pure tests).</param>
    /// <param name="newId">Hands out permanent IDs for new pieces, doors and devices.</param>
    public BuildingWorld(BuildingData data, PhysicsWorld? physics, Func<EntityId> newId)
    {
        Data = data;
        _newId = newId;
        Physics = physics != null ? new BuildingPhysics(physics) : null;
        foreach (PieceDef def in data.Pieces)
        {
            _parts[def] = PieceShapes.For(data, def);
        }
    }

    public BuildingData Data { get; }

    public BuildingPhysics? Physics { get; }

    /// <summary>The characters placement must not trap (feet, up, size), asked when a check needs them.</summary>
    public Func<IEnumerable<Capsule>>? Characters { get; set; }

    /// <summary>Everything that changes, as it changes (views, replication).</summary>
    public event Action<BuildingEvent>? Changed;

    public IReadOnlyCollection<Structure> Structures => _structures;

    public int PieceCount => _pieceCount;

    /// <summary>Pieces handled by <see cref="Tick"/> so far: stays 0 while nothing collapses (static entities cost nothing).</summary>
    public long TickWork { get; private set; }

    /// <summary>A piece kind's collision parts in its local metres.</summary>
    public ConvexPart[] PartsOf(PieceDef def) => _parts[def];

    public IEnumerable<Piece> AllPieces => _structures.SelectMany(s => s.PieceSet);

    // ---------------------------------------------------------------- time

    /// <summary>
    /// The building's share of a zone tick: takes down pieces whose collapse is due. Nothing at all happens while no
    /// collapse is under way, however many pieces stand.
    /// </summary>
    public void Tick(double now)
    {
        if (_collapse.Count == 0)
        {
            return;
        }
        var touched = new HashSet<Structure>();
        for (int i = 0; i < _collapse.Count;)
        {
            (double at, Piece piece) = _collapse[i];
            if (at > now)
            {
                i++;
                continue;
            }
            _collapse.RemoveAt(i);
            TickWork++;
            if (!piece.Gone)
            {
                touched.Add(piece.Structure);
                RemoveOne(piece, Removal.Collapsed);
            }
        }
        foreach (Structure s in touched)
        {
            if (s.Count > 0)
            {
                Settle(s, now, immediate: false, removed: null);
            }
        }
    }

    /// <summary>Pieces still coming down.</summary>
    public int Collapsing => _collapse.Count;

    // ---------------------------------------------------------------- pieces

    private Structure NewStructure(BuildingFrame frame)
    {
        var structure = new Structure(this, _nextStructure++, frame);
        _structures.Add(structure);
        if (!_lattices.TryGetValue(frame, out List<Structure>? list))
        {
            list = new List<Structure>();
            _lattices.Add(frame, list);
        }
        list.Add(structure);
        return structure;
    }

    private void DropStructure(Structure structure)
    {
        _structures.Remove(structure);
        if (_lattices.TryGetValue(structure.Frame, out List<Structure>? list))
        {
            list.Remove(structure);
            if (list.Count == 0)
            {
                _lattices.Remove(structure.Frame);
            }
        }
    }

    /// <summary>The structures that share a frame (pieces can link only within them).</summary>
    private IReadOnlyList<Structure> Lattice(in BuildingFrame frame) =>
        _lattices.TryGetValue(frame, out List<Structure>? list) ? list : Array.Empty<Structure>();

    /// <summary>
    /// Adds a piece to a structure without any checks (placement checks first; loading a saved base doesn't): links it
    /// to its neighbours, joins structures it connects, gives it a body and solves stability.
    /// </summary>
    public Piece Add(Structure structure, PieceDef def, int grade, Pose pose, ulong owner, double placedAt, double? health = null,
        EntityId id = default, uint body = 0)
    {
        var piece = new Piece(_nextHandle++, id.IsNone ? _newId() : id, def, grade, pose, owner, placedAt)
        {
            Structure = structure,
        };
        piece.Health = Math.Min(health ?? Data.Grades[grade].Health, Data.Grades[grade].Health);
        structure.PieceSet.Add(piece);
        structure.Index(piece);
        ZoneIndex(piece, add: true);
        _pieceCount++;
        _pieceIds[piece.Id] = piece;
        AddBody(piece, body);
        Link(piece);
        // Join every structure of the same frame it links to: the smaller moves into the bigger.
        foreach (Link link in piece.SupportedBy.Concat(piece.Supports).ToList())
        {
            Structure mine = piece.Structure, theirs = link.Other.Structure;
            if (theirs != mine)
            {
                if (theirs.Count > mine.Count)
                {
                    Merge(theirs, mine);
                }
                else
                {
                    Merge(mine, theirs);
                }
            }
        }
        structure = piece.Structure;
        if (_batch > 0)
        {
            _unsolved.Add(structure);
        }
        else
        {
            Solve(structure);
        }
        Changed?.Invoke(new BuildingEvent(BuildingChange.PieceAdded, piece, Structure: structure));
        return piece;
    }

    /// <summary>A new structure with its first piece at the frame's origin (a free foundation, or a loaded base).</summary>
    public Piece AddFirst(BuildingFrame frame, PieceDef def, int grade, ulong owner, double placedAt) =>
        Add(NewStructure(frame), def, grade, new Pose(Vector3D.Zero, 0), owner, placedAt);

    private void AddBody(Piece piece, uint body = 0)
    {
        if (Physics == null)
        {
            return;
        }
        (Vector3 position, Quaternion rotation) = piece.ZoneTransform;
        piece.Body = Physics.Add(piece, ShapeOf(piece.Def), position, rotation, body);
    }

    internal Shape ShapeOf(PieceDef def) => Physics!.ShapeFor(def, _parts[def]);

    // Links a new piece to its neighbours in every structure of its frame, both ways.
    private void Link(Piece piece)
    {
        _near.Clear();
        foreach (Structure s in Lattice(piece.Structure.Frame))
        {
            s.Near(piece.Pose.Position, LinkRadius, _near);
        }
        foreach (Piece other in _near)
        {
            if (other == piece || other.Gone || other.Collapsing)
            {
                continue;
            }
            if (Supports(other.Def, other.Pose, piece.Def, piece.Pose, out int loss))
            {
                other.Supports.Add(new Link(piece, loss));
                piece.SupportedBy.Add(new Link(other, loss));
            }
            if (Supports(piece.Def, piece.Pose, other.Def, other.Pose, out loss))
            {
                piece.Supports.Add(new Link(other, loss));
                other.SupportedBy.Add(new Link(piece, loss));
            }
        }
    }

    /// <summary>
    /// Whether a child sits exactly where one of the parent's sockets would put it (stability links come only from exact
    /// snaps, never from pieces merely touching), and the least loss of such a link.
    /// </summary>
    public bool Supports(PieceDef parent, in Pose parentPose, PieceDef child, in Pose childPose, out int loss)
    {
        loss = int.MaxValue;
        if ((parent.Height + LinkRadius) * (parent.Height + LinkRadius) < (parentPose.Position - childPose.Position).LengthSquared)
        {
            return false;
        }
        PieceGeometry.Sockets(parent, child, _sockets);
        foreach (SocketId socket in _sockets)
        {
            int count = PieceGeometry.Orientations(parent, socket, child);
            for (int o = 0; o < count; o++)
            {
                if (PieceGeometry.Snap(Data, parent, parentPose, socket, child, o, out Pose pose) && PieceGeometry.SamePose(pose, childPose))
                {
                    loss = Math.Min(loss, PieceGeometry.SupportLoss(Data, parent, child, socket));
                }
            }
        }
        return loss != int.MaxValue;
    }

    // Moves every piece (and the claim) of `other` into `into`; both have the same frame.
    private void Merge(Structure into, Structure other)
    {
        if (into == other)
        {
            return;
        }
        foreach (Piece p in other.PieceSet)
        {
            p.Structure = into;
            into.PieceSet.Add(p);
            into.Index(p);
        }
        if (other.Claim != null && into.Claim == null)
        {
            into.Claim = other.Claim;
        }
        foreach ((string material, double debt) in other.UpkeepDebt)
        {
            into.UpkeepDebt[material] = into.UpkeepDebt.GetValueOrDefault(material) + debt;
        }
        if (_unsolved.Remove(other))
        {
            _unsolved.Add(into);
        }
        other.PieceSet.Clear();
        DropStructure(other);
    }

    private void ZoneIndex(Piece piece, bool add)
    {
        (Vector3 position, _) = piece.ZoneTransform;
        (int, int) key = ((int)MathF.Floor(position.X / ZoneCell), (int)MathF.Floor(position.Z / ZoneCell));
        if (add)
        {
            if (!_zoneCells.TryGetValue(key, out List<Piece>? list))
            {
                list = new List<Piece>();
                _zoneCells.Add(key, list);
            }
            list.Add(piece);
        }
        else if (_zoneCells.TryGetValue(key, out List<Piece>? list))
        {
            list.Remove(piece);
            if (list.Count == 0)
            {
                _zoneCells.Remove(key);
            }
        }
    }

    /// <summary>Pieces whose pivot is within <paramref name="radius"/> of a zone segment.</summary>
    public void PiecesNear(Vector3 a, Vector3 b, float radius, List<Piece> into)
    {
        into.Clear();
        Vector3 min = Vector3.Min(a, b) - new Vector3(radius), max = Vector3.Max(a, b) + new Vector3(radius);
        int x0 = (int)MathF.Floor(min.X / ZoneCell), x1 = (int)MathF.Floor(max.X / ZoneCell);
        int z0 = (int)MathF.Floor(min.Z / ZoneCell), z1 = (int)MathF.Floor(max.Z / ZoneCell);
        var a3 = new Vector3D(a.X, a.Y, a.Z);
        var b3 = new Vector3D(b.X, b.Y, b.Z);
        for (int x = x0; x <= x1; x++)
        {
            for (int z = z0; z <= z1; z++)
            {
                if (!_zoneCells.TryGetValue((x, z), out List<Piece>? list))
                {
                    continue;
                }
                foreach (Piece p in list)
                {
                    (Vector3 position, _) = p.ZoneTransform;
                    var p3 = new Vector3D(position.X, position.Y, position.Z);
                    if (PieceGeometry.DistanceToSegmentSquared(p3, a3, b3) <= radius * radius)
                    {
                        into.Add(p);
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- stability

    /// <summary>
    /// Solves a structure's stability (RustMVP's <c>resolve_stability</c>): foundations hold 1000; along each support
    /// link the value drops by the link's loss; a piece takes the strongest path from the ground, so a floating loop
    /// never holds itself up. Collapsing pieces are left out.
    /// </summary>
    public void Solve(Structure structure)
    {
        var queue = new PriorityQueue<Piece, int>();
        foreach (Piece p in structure.PieceSet)
        {
            p.Stability = 0;
        }
        foreach (Piece p in structure.PieceSet)
        {
            if (p.Def.Family == PieceFamily.Foundation && !p.Collapsing)
            {
                p.Stability = Data.GroundStability;
                queue.Enqueue(p, -p.Stability);
            }
        }
        while (queue.TryDequeue(out Piece? p, out int priority))
        {
            if (-priority != p.Stability)
            {
                continue;
            }
            foreach (Link link in p.Supports)
            {
                Piece next = link.Other;
                if (next.Collapsing || next.Gone)
                {
                    continue;
                }
                int value = Math.Max(0, p.Stability - link.Loss);
                if (value > next.Stability)
                {
                    next.Stability = value;
                    queue.Enqueue(next, -value);
                }
            }
        }
        Changed?.Invoke(new BuildingEvent(BuildingChange.Stability, Structure: structure));
    }

    /// <summary>The stability a piece would have at a pose: the best of its would-be supports, less each link's loss.</summary>
    public int StabilityAt(PieceDef def, in BuildingFrame frame, in Pose pose, List<Piece>? supports = null)
    {
        if (def.Family == PieceFamily.Foundation)
        {
            return Data.GroundStability;
        }
        int best = 0;
        _near.Clear();
        foreach (Structure s in Lattice(frame))
        {
            s.Near(pose.Position, LinkRadius, _near);
        }
        foreach (Piece other in _near)
        {
            if (other.Gone || other.Collapsing)
            {
                continue;
            }
            if (Supports(other.Def, other.Pose, def, pose, out int loss))
            {
                best = Math.Max(best, other.Stability - loss);
                supports?.Add(other);
            }
        }
        return Math.Max(0, best);
    }

    // ---------------------------------------------------------------- removal and collapse

    /// <summary>
    /// Removes a piece (demolished, destroyed or decayed: <paramref name="reason"/>) and brings down what it held up:
    /// pieces left unsupported collapse in sequence, nearest first, one layer every <see cref="BuildingData.CollapseStep"/>
    /// seconds; with <paramref name="immediate"/> (catch-up over elapsed time) they go at once. A structure split in two
    /// becomes two.
    /// </summary>
    public void Remove(Piece piece, double now, bool immediate = false, Removal reason = Removal.Taken)
    {
        if (piece.Gone)
        {
            return;
        }
        Structure structure = piece.Structure;
        var neighbours = piece.SupportedBy.Concat(piece.Supports).Select(l => l.Other).ToList();
        RemoveOne(piece, reason);
        if (structure.Count > 0)
        {
            Settle(structure, now, immediate, neighbours);
        }
    }

    // Solves again after removals, takes down or schedules what lost its support, and splits what came apart.
    private void Settle(Structure structure, double now, bool immediate, List<Piece>? removed)
    {
        Solve(structure);
        // A mirror (a client's copy) takes nothing down by itself: the server says what goes.
        List<Piece> unsupported = Mirror ? new List<Piece>()
            : structure.PieceSet.Where(p => !p.Collapsing && p.Stability < Data.MinimumStability).ToList();
        if (unsupported.Count > 0)
        {
            if (immediate)
            {
                foreach (Piece p in unsupported)
                {
                    RemoveOne(p, Removal.Collapsed);
                }
                if (structure.Count > 0)
                {
                    Settle(structure, now, immediate, null);
                }
                return;
            }
            ScheduleCollapse(unsupported, removed, now);
        }
        Split(structure);
    }

    // Orders a collapse by layers from where support was lost, so it runs through the building like a wave.
    private void ScheduleCollapse(List<Piece> unsupported, List<Piece>? from, double now)
    {
        var pending = new HashSet<Piece>(unsupported);
        var layer = new List<Piece>();
        if (from != null)
        {
            layer.AddRange(from.Where(pending.Contains).Distinct());
        }
        if (layer.Count == 0)
        {
            layer.Add(unsupported.OrderByDescending(p => p.Pose.Position.Y).First());
        }
        int step = 0;
        while (pending.Count > 0)
        {
            if (layer.Count == 0)
            {
                // Whatever the links don't reach from here goes in one last layer.
                layer.AddRange(pending);
            }
            var next = new List<Piece>();
            foreach (Piece p in layer)
            {
                if (!pending.Remove(p))
                {
                    continue;
                }
                p.Collapsing = true;
                _collapse.Add((now + (step + 1) * Data.CollapseStep, p));
                Changed?.Invoke(new BuildingEvent(BuildingChange.PieceChanged, p, Structure: p.Structure));
                foreach (Link link in p.Supports.Concat(p.SupportedBy))
                {
                    if (pending.Contains(link.Other))
                    {
                        next.Add(link.Other);
                    }
                }
            }
            layer = next;
            step++;
        }
    }

    // Takes one piece out of everything: links, indexes, physics, its door and the claim standing on it (for the same reason).
    private void RemoveOne(Piece piece, Removal reason = Removal.Taken)
    {
        if (piece.Gone)
        {
            return;
        }
        Structure structure = piece.Structure;
        foreach (Link link in piece.SupportedBy)
        {
            link.Other.Supports.RemoveAll(l => l.Other == piece);
        }
        foreach (Link link in piece.Supports)
        {
            link.Other.SupportedBy.RemoveAll(l => l.Other == piece);
        }
        piece.SupportedBy.Clear();
        piece.Supports.Clear();
        if (piece.Door != null)
        {
            RemoveDoor(piece.Door, reason);
        }
        if (structure.Claim != null && structure.Claim.Support == piece)
        {
            RemoveClaim(structure.Claim, reason);
        }
        Physics?.Remove(piece.Body, piece);
        piece.Body = 0;
        ZoneIndex(piece, add: false);
        structure.Unindex(piece, piece.Pose);
        structure.PieceSet.Remove(piece);
        _pieceCount--;
        _pieceIds.Remove(piece.Id);
        piece.Gone = true;
        piece.Collapsing = false;
        Changed?.Invoke(new BuildingEvent(BuildingChange.PieceRemoved, piece, Structure: structure, Removal: reason));
        if (structure.Count == 0)
        {
            if (structure.Claim != null)
            {
                RemoveClaim(structure.Claim, reason);
            }
            DropStructure(structure);
        }
    }

    // Splits a structure whose pieces no longer all connect into one structure per connected part (same frame). The
    // part holding the claim device keeps the structure; otherwise the biggest does.
    private void Split(Structure structure)
    {
        if (structure.Count <= 1)
        {
            return;
        }
        var seen = new HashSet<Piece>();
        var parts = new List<List<Piece>>();
        foreach (Piece start in structure.PieceSet)
        {
            if (!seen.Add(start))
            {
                continue;
            }
            var part = new List<Piece> { start };
            for (int i = 0; i < part.Count; i++)
            {
                foreach (Link link in part[i].Supports.Concat(part[i].SupportedBy))
                {
                    if (link.Other.Structure == structure && seen.Add(link.Other))
                    {
                        part.Add(link.Other);
                    }
                }
            }
            parts.Add(part);
        }
        if (parts.Count == 1)
        {
            return;
        }
        Piece? claimed = structure.Claim?.Support;
        List<Piece> keep = claimed != null ? parts.First(p => p.Contains(claimed)) : parts.OrderByDescending(p => p.Count).First();
        foreach (List<Piece> part in parts)
        {
            if (part == keep)
            {
                continue;
            }
            Structure split = NewStructure(structure.Frame);
            foreach (Piece p in part)
            {
                structure.Unindex(p, p.Pose);
                structure.PieceSet.Remove(p);
                p.Structure = split;
                split.PieceSet.Add(p);
                split.Index(p);
            }
            Solve(split);
        }
    }

    // ---------------------------------------------------------------- targeting

    /// <summary>What the player aims at within reach: a piece, a door or a claim device (or null), and where.</summary>
    public object? Target(Vector3 eye, Vector3 direction, float reach, out RayHit hit)
    {
        hit = default;
        if (Physics == null || !Physics.Ray(eye, direction, reach, out hit))
        {
            return null;
        }
        object? owner = Physics.OwnerOf(hit.Body);
        return owner switch
        {
            DoorLeaf leaf => leaf.Door,
            _ => owner,
        };
    }

    public void Dispose()
    {
        Physics?.Dispose();
    }
}
