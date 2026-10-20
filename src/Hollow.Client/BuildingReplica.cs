using System;
using System.Collections.Generic;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Targets;

namespace Hollow.Client;

/// <summary>
/// What a client knows of a door beyond its geometry: its lock, and what the server tells this player of it
/// (GROUND_SYSTEMS.md §3.3): whether they may pass it, whether it is locked, whether its code lock has a code and lists them,
/// whether it is theirs (they fitted its lock, or the door when it has none).
/// </summary>
public readonly record struct DoorState(LockKind Lock, ViewerFlags Flags)
{
    public bool Locked => (Flags & ViewerFlags.Locked) != 0;

    public bool CanUse => (Flags & ViewerFlags.CanUse) != 0;

    public bool HasCode => (Flags & ViewerFlags.HasCode) != 0;

    public bool Authorised => (Flags & ViewerFlags.Authorised) != 0;

    public bool Mine => (Flags & ViewerFlags.Mine) != 0;
}

/// <summary>What a client knows of a claim device: what the server tells this player of it (on its list, theirs), and how long its store keeps the base.</summary>
public readonly record struct ClaimState(ViewerFlags Flags, double ProtectedHours)
{
    public bool Authorised => (Flags & ViewerFlags.Authorised) != 0;
}

/// <summary>
/// A client's copy of the server's building (GROUND.md §6.3, phase 2): a <see cref="BuildingWorld"/> in mirror mode
/// holding the pieces, doors and claim devices in range exactly as the server has them (same frames, poses, grades and
/// body IDs, so the player's prediction collides with the same walls), and answering the ghost's questions with the
/// same rules. It changes only when the server says so. Bots share one between many clients (<see cref="Shared"/>):
/// then everything is applied idempotently and nothing is forgotten for leaving one bot's range.
/// </summary>
public sealed class BuildingReplica : IDisposable
{
    private readonly Dictionary<uint, BuildingFrame> _frames = new();
    private readonly Dictionary<EntityId, DoorState> _doors = new();
    private readonly Dictionary<EntityId, ClaimState> _claims = new();
    private readonly List<uint> _bodies = new();

    public BuildingReplica(BuildingData data, PhysicsWorld? physics, bool shared = false)
    {
        World = new BuildingWorld(data, physics, () => EntityId.None) { Mirror = true };
        Shared = shared;
    }

    /// <summary>The mirrored building: draw its <see cref="BuildingWorld.Structures"/>, listen to <see cref="BuildingWorld.Changed"/>.</summary>
    public BuildingWorld World { get; }

    public bool Shared { get; }

    /// <summary>Pieces received so far (for statistics and tests).</summary>
    public long PiecesReceived { get; private set; }

    public DoorState StateOf(Door door) => _doors.GetValueOrDefault(door.Id);

    public ClaimState StateOf(ClaimDevice device) => _claims.GetValueOrDefault(device.Id);

    internal void Apply(in BuildFrames m)
    {
        foreach (BuildFrameNet f in m.Frames ?? Array.Empty<BuildFrameNet>())
        {
            _frames[f.Id] = new BuildingFrame(new Vector3D(f.OriginX, f.OriginY, f.OriginZ), new Vector3D(f.XX, f.XY, f.XZ),
                new Vector3D(f.YX, f.YY, f.YZ), new Vector3D(f.ZX, f.ZY, f.ZZ));
        }
    }

    /// <summary>New and changed pieces; <paramref name="now"/> is the client's clock (edit windows count to it).</summary>
    internal void Apply(in BuildPieces m, double now)
    {
        BuildingData data = World.Data;
        World.BeginBatch();
        try
        {
            foreach (PieceNet p in m.Pieces ?? Array.Empty<PieceNet>())
            {
                if (data.Piece(p.Piece) is not { } def || !_frames.TryGetValue(p.Frame, out BuildingFrame frame) ||
                    !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z))
                {
                    continue;
                }
                int grade = Math.Clamp((int)p.Grade, 0, data.Grades.Count - 1);
                double health = p.Health / 65535.0 * data.Grades[grade].Health;
                var id = new EntityId(p.Id);
                if (World.PieceOf(id) is { } piece)
                {
                    World.Apply(piece, grade, health, p.Yaw, p.Collapsing);
                    continue;
                }
                double placedAt = now - (data.EditWindow - p.EditLeft);
                Piece added = World.Restore(frame, def, grade, new Pose(new Vector3D(p.X, p.Y, p.Z), p.Yaw), 0, placedAt, health, id, p.Body);
                if (p.Collapsing)
                {
                    World.Apply(added, grade, health, p.Yaw, collapsing: true);
                }
                PiecesReceived++;
            }
        }
        finally
        {
            World.EndBatch();
        }
    }

    internal void Apply(in BuildRemoved m)
    {
        if (!m.Destroyed && Shared)
        {
            return;
        }
        World.BeginBatch();
        try
        {
            foreach (BuildIdNet n in m.Ids ?? Array.Empty<BuildIdNet>())
            {
                var id = new EntityId(n.Id);
                switch (m.Kind)
                {
                    case BuildPartKind.Piece when World.PieceOf(id) is { } piece:
                        if (piece.Door != null)
                        {
                            _doors.Remove(piece.Door.Id);
                        }
                        World.Forget(piece);
                        break;
                    case BuildPartKind.Door when World.DoorOf(id) is { } door:
                        _doors.Remove(id);
                        World.Forget(door);
                        break;
                    case BuildPartKind.Claim when World.ClaimOf(id) is { } claim:
                        _claims.Remove(id);
                        World.Forget(claim);
                        break;
                }
            }
        }
        finally
        {
            World.EndBatch();
        }
    }

    internal void Apply(in BuildDoors m)
    {
        BuildingData data = World.Data;
        foreach (DoorNet d in m.Doors ?? Array.Empty<DoorNet>())
        {
            if (data.Door(d.Door) is not { } def)
            {
                continue;
            }
            var id = new EntityId(d.Id);
            double health = d.Health / 65535.0 * def.Health;
            Door? door = World.DoorOf(id);
            if (door != null)
            {
                World.Apply(door, d.Open, health);
            }
            else if (World.PieceOf(new EntityId(d.Frame)) is { } frame)
            {
                _bodies.Clear();
                _bodies.Add(d.Body0);
                _bodies.Add(d.Body1);
                door = World.RestoreDoor(id, def, frame, d.Layout, 0, d.Open, health, _bodies);
            }
            if (door == null)
            {
                continue;
            }
            var state = new DoorState(d.Lock, d.Flags);
            World.ApplyLock(door, d.Lock, new EntityId(d.LockId), state.Locked);
            _doors[id] = state;
        }
    }

    /// <summary>Claim devices; <paramref name="me"/> is this player (whether they're on a device's list).</summary>
    internal void Apply(in BuildClaims m, ulong me)
    {
        foreach (ClaimNet c in m.Claims ?? Array.Empty<ClaimNet>())
        {
            var id = new EntityId(c.Id);
            double health = c.Health / 65535.0 * World.Data.ClaimHealth;
            ClaimDevice? device = World.ClaimOf(id);
            var state = new ClaimState(c.Flags, c.ProtectedMinutes == uint.MaxValue ? double.PositiveInfinity : c.ProtectedMinutes / 60.0);
            if (device != null)
            {
                World.Apply(device, health, me, state.Authorised);
            }
            else if (World.PieceOf(new EntityId(c.Support)) is { } support && double.IsFinite(c.X) && double.IsFinite(c.Y) && double.IsFinite(c.Z))
            {
                device = World.RestoreClaim(id, 0, support, new Vector3D(c.X, c.Y, c.Z), c.Yaw, health,
                    state.Authorised ? new[] { me } : Array.Empty<ulong>(), c.Body);
            }
            if (device != null)
            {
                _claims[id] = state;
            }
        }
    }

    public void Dispose() => World.Dispose();
}
