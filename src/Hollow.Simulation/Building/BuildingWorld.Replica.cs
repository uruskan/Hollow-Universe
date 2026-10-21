using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Entities;

namespace Hollow.Simulation.Building;

// The building as a copy of someone else's (GROUND.md §6.3, phase 2): a world loaded from storage, and a client's
// mirror of what the server replicates. Both put pieces, doors, locks and claim devices back exactly as they were
// (their IDs, poses in their frames, grades, health and, for a mirror, the server's body IDs, so prediction meets the
// same contacts), in batches that solve stability once at the end.
public sealed partial class BuildingWorld
{
    private readonly Dictionary<EntityId, Piece> _pieceIds = new();
    private readonly Dictionary<EntityId, Door> _doorIds = new();
    private readonly Dictionary<EntityId, ClaimDevice> _claimIds = new();
    private readonly HashSet<Structure> _unsolved = new();
    private int _batch;

    /// <summary>
    /// A client's copy of the server's building: the same rules answer the ghost's questions, but nothing collapses,
    /// decays or pays upkeep by itself; the server says what changes.
    /// </summary>
    public bool Mirror
    {
        get => _mirror;
        init
        {
            _mirror = value;
            if (Physics != null)
            {
                Physics.OnlyRequested = value;
            }
        }
    }

    private readonly bool _mirror;

    public Piece? PieceOf(EntityId id) => _pieceIds.GetValueOrDefault(id);

    public Door? DoorOf(EntityId id) => _doorIds.GetValueOrDefault(id);

    public ClaimDevice? ClaimOf(EntityId id) => _claimIds.GetValueOrDefault(id);

    /// <summary>The piece, door or claim device with an ID, or null (IDs from the wire are untrusted).</summary>
    public object? Find(EntityId id) =>
        _pieceIds.TryGetValue(id, out Piece? piece) ? piece
        : _doorIds.TryGetValue(id, out Door? door) ? door
        : _claimIds.TryGetValue(id, out ClaimDevice? claim) ? claim
        : null;

    /// <summary>
    /// Starts a batch: pieces added until <see cref="EndBatch"/> link and join structures at once, but stability is
    /// solved once per structure at the end (a 4,000-piece base loads in one pass, not 4,000).
    /// </summary>
    public void BeginBatch() => _batch++;

    /// <summary>
    /// Ends a batch: every structure that gained pieces is solved; on the server (not a mirror) anything left without
    /// support comes down as it would after any change.
    /// </summary>
    public void EndBatch(double now = 0)
    {
        if (_batch == 0 || --_batch > 0)
        {
            return;
        }
        List<Structure> unsolved = _unsolved.Where(s => _structures.Contains(s)).ToList();
        _unsolved.Clear();
        foreach (Structure s in unsolved)
        {
            if (Mirror)
            {
                Solve(s);
            }
            else
            {
                Settle(s, now, immediate: false, removed: null);
            }
        }
    }

    // ---------------------------------------------------------------- putting things back

    /// <summary>
    /// Puts a piece back as it was (loaded, or replicated): in a structure of its frame, at its pose, with its ID,
    /// grade, health and the start of its edit window; a mirror passes the server's body ID. No checks.
    /// </summary>
    public Piece Restore(in BuildingFrame frame, PieceDef def, int grade, in Pose pose, ulong owner, double placedAt, double health,
        EntityId id, uint body = 0, double lastDamagedAt = double.NegativeInfinity)
    {
        grade = Math.Clamp(grade, 0, Data.Grades.Count - 1);
        Piece piece = Add(NewStructure(frame), def, grade, pose, owner, placedAt, Math.Max(0, health), id, body);
        piece.LastDamagedAt = lastDamagedAt;
        return piece;
    }

    /// <summary>Puts a door back into its frame (shut or open, with its health); a mirror passes the server's leaf bodies.</summary>
    public Door? RestoreDoor(EntityId id, DoorDef def, Piece frame, int layout, ulong owner, bool open, double health,
        IReadOnlyList<uint>? bodies = null)
    {
        if (frame.Gone || frame.Door != null || frame.Def.Opening != def.Opening)
        {
            return null;
        }
        var door = new Door(id, def, frame, layout, owner)
        {
            Health = Math.Clamp(health, 0, def.Health),
        };
        door.Rest(open);
        frame.Door = door;
        _doorIds[id] = door;
        for (int i = 0; i < def.Leaves.Count; i++)
        {
            if (Physics == null)
            {
                break;
            }
            (Vector3 position, Quaternion rotation) = door.LeafNow(i);
            uint requested = bodies != null && i < bodies.Count ? bodies[i] : 0;
            door.Bodies[i] = Physics.Add(new DoorLeaf(door, i), LeafShape(door, i), position, rotation, requested);
        }
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorAdded, frame, door, Structure: frame.Structure));
        return door;
    }

    /// <summary>Puts a key lock back on a door, with its number (later locks get higher ones).</summary>
    public KeyLock RestoreKeyLock(ILockHost door, EntityId id, ulong owner, int keyCode, bool locked)
    {
        var keyLock = LockState.RestoreKey(id, owner, keyCode, locked);
        door.Lock = keyLock;
        ObserveLock(keyLock);
        door.LockChanged();
        return keyLock;
    }

    /// <summary>Advances the shared key allocator past a restored lock on any host.</summary>
    public void ObserveLock(DoorLock? fitted)
    {
        if (fitted is KeyLock key && key.KeyCode >= _nextKeyCode)
            _nextKeyCode = key.KeyCode % MaxKeyCode + 1;
    }

    /// <summary>Puts a code lock back on its host without exposing its secrets to clients.</summary>
    public CodeLock RestoreCodeLock(ILockHost door, EntityId id, ulong owner, ulong codeHash, bool locked, IEnumerable<ulong> authorised)
    {
        var codeLock = LockState.RestoreCode(id, owner, codeHash, locked, authorised, Data.LockMaxAuthorised);
        door.Lock = codeLock;
        door.LockChanged();
        return codeLock;
    }
    /// <summary>
    /// Puts a claim device back on its piece: where it stood in the frame, its health and authorised list; a mirror
    /// passes the server's body. Null when the piece already has one or isn't a slab.
    /// </summary>
    public ClaimDevice? RestoreClaim(EntityId id, ulong owner, Piece support, Vector3D position, int yaw, double health,
        IEnumerable<ulong> authorised, uint body = 0)
    {
        if (support.Gone || !support.Def.IsSlab || support.Structure.Claim != null)
        {
            return null;
        }
        var device = new ClaimDevice(id, owner, support, position, PieceGeometry.Wrap(yaw), Math.Clamp(health, 0, Data.ClaimHealth));
        device.AuthorisedList.Clear();
        foreach (ulong player in authorised.Distinct().Take(Data.ClaimMaxAuthorised))
        {
            device.AuthorisedList.Add(player);
        }
        support.Structure.Claim = device;
        _claimIds[id] = device;
        if (Physics != null)
        {
            ConvexPart[] parts = { PieceShapes.Claim(Data) };
            (Vector3 zonePosition, Quaternion zoneRotation) = device.ZoneTransform;
            device.Body = Physics.Add(device, Physics.ShapeFor(Data.ClaimItem, parts), zonePosition, zoneRotation, body);
        }
        Changed?.Invoke(new BuildingEvent(BuildingChange.ClaimAdded, support, Claim: device, Structure: support.Structure));
        return device;
    }

    /// <summary>The piece of a kind standing at a pose in a frame, if any.</summary>
    public Piece? PieceAt(in BuildingFrame frame, PieceDef def, in Pose pose)
    {
        foreach (Structure s in Lattice(frame))
        {
            if (s.At(def, pose) is { } piece && piece.Def == def)
            {
                return piece;
            }
        }
        return null;
    }

    /// <summary>A structure's upkeep owed but not yet taken from its store (fractions of an item), by material.</summary>
    public IReadOnlyDictionary<string, double> DebtOf(Structure structure) => structure.UpkeepDebt;

    /// <summary>Puts back what a structure owed (it is saved with its claim device).</summary>
    public void RestoreDebt(Structure structure, string material, double debt)
    {
        if (double.IsFinite(debt) && debt > 0 && Data.Materials.Contains(material))
        {
            structure.UpkeepDebt[material] = Math.Min(debt, 1e6);
        }
    }

    // ---------------------------------------------------------------- a mirror following the server

    /// <summary>
    /// A mirror takes a piece's new state from the server: grade and health, a turn (it relinks and its body moves),
    /// and whether it is coming down.
    /// </summary>
    public void Apply(Piece piece, int grade, double health, int yaw, bool collapsing)
    {
        if (piece.Gone)
        {
            return;
        }
        piece.Grade = Math.Clamp(grade, 0, Data.Grades.Count - 1);
        piece.Health = Math.Clamp(health, 0, piece.MaxHealth);
        bool relink = false;
        yaw = PieceGeometry.Wrap(yaw);
        if (yaw != piece.Pose.Yaw)
        {
            Unlink(piece);
            piece.Pose = new Pose(piece.Pose.Position, yaw);
            Link(piece);
            (Vector3 position, Quaternion rotation) = piece.ZoneTransform;
            Physics?.Move(piece.Body, position, rotation);
            relink = true;
        }
        if (collapsing != piece.Collapsing)
        {
            piece.Collapsing = collapsing;
            relink = true;
        }
        if (relink)
        {
            if (_batch > 0)
            {
                _unsolved.Add(piece.Structure);
            }
            else
            {
                Solve(piece.Structure);
            }
        }
        Changed?.Invoke(new BuildingEvent(BuildingChange.PieceChanged, piece, Structure: piece.Structure));
    }

    /// <summary>A mirror takes a piece away (the server removed it, or it went out of range): nothing else comes down with it.</summary>
    public void Forget(Piece piece)
    {
        if (piece.Gone)
        {
            return;
        }
        Structure structure = piece.Structure;
        RemoveOne(piece);
        if (structure.Count > 0)
        {
            if (_batch > 0 && Mirror)
            {
                _unsolved.Add(structure);
                Split(structure);
            }
            else
            {
                Settle(structure, 0, immediate: false, removed: null);
            }
        }
    }

    /// <summary>
    /// A mirror takes a door's state from the server: its target, its motion record (the progress at a tick, which way it
    /// goes) and its health. The leaves' bodies go where the record puts them at the tick the doors are at.
    /// </summary>
    public void Apply(Door door, bool open, float from, int moving, long since, double health)
    {
        if (door.Gone)
        {
            return;
        }
        door.Health = Math.Clamp(health, 0, door.Def.Health);
        lock (_moving)
        {
            door.Open = open && door.Def.Opens;
            door.From = Math.Clamp(from, 0f, 1f);
            door.Moving = (sbyte)(door.Def.Opens ? Math.Clamp(moving, -1, 1) : 0);
            door.Since = since;
            if (door.Moving != 0 && !_moving.Contains(door))
            {
                _moving.Add(door);
            }
            PoseDoor(door, door.ProgressAt(DoorTick));
        }
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorChanged, door.Frame, door, Structure: door.Frame.Structure));
    }

    /// <summary>A mirror shows a door's lock as the server describes it: none, a key lock or a code lock, locked or not.</summary>
    public void ApplyLock(Door door, LockKind kind, EntityId id, bool locked)
    {
        DoorLock? current = door.Lock;
        bool same = kind switch
        {
            LockKind.None => current == null,
            LockKind.Key => current is KeyLock,
            LockKind.Code => current is CodeLock,
            _ => true,
        };
        if (!same)
        {
            door.Lock = kind switch
            {
                LockKind.Key => new KeyLock(id, 0, 0),
                LockKind.Code => new CodeLock(id, 0),
                _ => null,
            };
        }
        if (door.Lock != null)
        {
            door.Lock.Locked = locked;
        }
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorChanged, door.Frame, door, Structure: door.Frame.Structure));
    }

    /// <summary>A mirror takes a door away.</summary>
    public void Forget(Door door) => RemoveDoor(door);

    /// <summary>A mirror takes a claim device's state: its health and whether <paramref name="player"/> is on its list.</summary>
    public void Apply(ClaimDevice device, double health, ulong player, bool authorised)
    {
        if (device.Gone)
        {
            return;
        }
        device.Health = Math.Clamp(health, 0, Data.ClaimHealth);
        // Only this player's entry: a client's mirror lists no one else, and a mirror many clients share (bots) keeps
        // each one's as the server tells each of them (another's "not listed" mustn't take the owner off).
        device.AuthorisedList.Remove(player);
        if (authorised)
        {
            device.AuthorisedList.Add(player);
        }
        Changed?.Invoke(new BuildingEvent(BuildingChange.ClaimChanged, device.Support, Claim: device, Structure: device.Structure));
    }

    /// <summary>A mirror takes a claim device away.</summary>
    public void Forget(ClaimDevice device) => RemoveClaim(device);
}

/// <summary>What kind of lock a door has.</summary>
public enum LockKind : byte
{
    None,
    Key,
    Code,
}

/// <summary>Which kind of building part a replicated ID names.</summary>
public enum BuildPartKind : byte
{
    Piece,
    Door,
    Claim,
}
