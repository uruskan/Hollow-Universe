using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Capabilities;

namespace Hollow.Simulation.Building;

/// <summary>
/// A lock as something to place (building.json's locks, GROUND_SYSTEMS.md §6): its key (its item's), name, kind, and the
/// field of building.json's locks it is read from.
/// </summary>
public sealed record LockDef(string Key, string Name, LockKind Kind, string Field);

/// <summary>A lock on a door. Its secrets (a code, who is authorised) stay on the server.</summary>
public abstract class DoorLock
{
    internal DoorLock(EntityId id, ulong owner)
    {
        Id = id;
        Owner = owner;
    }

    public EntityId Id { get; }

    /// <summary>The player who fitted it.</summary>
    public ulong Owner { get; }

    public bool Locked { get; internal set; }

    /// <summary>What kind of lock it is (what replication and capabilities go by).</summary>
    public abstract LockKind Kind { get; }
}

/// <summary>
/// Rust's key lock: its owner opens it without a key; anyone holding a key cut for it (a key with its
/// <see cref="KeyCode"/>) opens it too, and it stays locked behind them.
/// </summary>
public sealed class KeyLock : DoorLock
{
    internal KeyLock(EntityId id, ulong owner, int keyCode) : base(id, owner)
    {
        KeyCode = keyCode;
    }

    /// <summary>The number its keys carry.</summary>
    public int KeyCode { get; }

    public override LockKind Kind => LockKind.Key;

    public bool CanUse(ulong player, IReadOnlyCollection<int>? keys) => !Locked || player == Owner || (keys != null && Contains(keys, KeyCode));

    private static bool Contains(IReadOnlyCollection<int> keys, int code)
    {
        foreach (int k in keys)
        {
            if (k == code)
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// A code lock (RustMVP's rules, one code and no guest codes): setting the code locks it and leaves only the setter
/// authorised; entering the right code authorises a player; wrong codes make the player wait longer and longer.
/// </summary>
public sealed class CodeLock : DoorLock
{
    internal readonly List<ulong> AuthorisedList = new();
    internal readonly Dictionary<ulong, (int Failures, double Until)> Failures = new();

    internal CodeLock(EntityId id, ulong owner) : base(id, owner)
    {
        AuthorisedList.Add(owner);
    }

    /// <summary>The code, kept only as a hash (salted with the lock's ID); 0 before one is set.</summary>
    public ulong CodeHash { get; internal set; }

    public bool HasCode => CodeHash != 0;

    public override LockKind Kind => LockKind.Code;

    /// <summary>The hash a code is kept as on this lock (never 0).</summary>
    public ulong HashOf(string code) => Math.Max(1, Hash64.Combine(Hash64.Of(Id.Value, 0xC0DE), Hash64.Of(code)));

    public IReadOnlyList<ulong> Authorised => AuthorisedList;

    public bool IsAuthorised(ulong player) => AuthorisedList.Contains(player);

    public bool CanUse(ulong player) => !Locked || IsAuthorised(player);
}

/// <summary>
/// A door fitted into a doorway (single) or a wall frame (double, garage): a deployable item with its own health, never
/// upgraded. Its leaves swing on the hinges from the kit's assembly manifest; open or shut they collide, so an open
/// door still blocks (RustMVP's rule).
/// </summary>
public sealed class Door : IBuildingPart, IOwned, ILockHost
{
    internal uint[] Bodies;

    internal Door(EntityId id, DoorDef def, Piece frame, int flip, ulong owner)
    {
        Id = id;
        Def = def;
        Frame = frame;
        Flip = flip;
        Owner = owner;
        Health = def.Health;
        Bodies = new uint[def.Leaves.Count];
    }

    public EntityId Id { get; }

    /// <summary>How the world names it (GROUND_SYSTEMS.md §3.1).</summary>
    public TargetRef Ref => new(TargetFamily.Door, Id.Value);

    public DoorDef Def { get; }

    /// <summary>The doorway or wall frame it's fitted into.</summary>
    public Piece Frame { get; }

    /// <summary>0, or 1 when turned 180° (the other hinge side and swing).</summary>
    public int Flip { get; }

    public ulong Owner { get; }

    /// <summary>Whose it is to a viewer (GROUND_SYSTEMS.md §3.3): its lock's fitter while it has a lock, else who fitted the door.</summary>
    ulong IOwned.OwnerId => Lock?.Owner ?? Owner;

    public bool Open { get; internal set; }

    public double Health { get; internal set; }

    public double LastDamagedAt { get; internal set; } = double.NegativeInfinity;

    public DoorLock? Lock { get; internal set; }

    DoorLock? ILockHost.Lock { get => Lock; set => Lock = value; }
    bool ILockHost.Lockable => Def.Lockable;
    bool ILockHost.LockGone => Gone;
    void ILockHost.LockChanged() => Frame.Structure.World.NotifyLockChanged(this);

    public bool Gone { get; internal set; }

    /// <summary>The claim device of its frame's structure, or null.</summary>
    public ClaimDevice? Base => Frame.Structure?.Claim;

    /// <summary>Its leaves' Jolt body IDs.</summary>
    public IReadOnlyList<uint> BodyIds => Bodies;

    /// <summary>
    /// A leaf's pose in the zone, open or shut: the frame's transform, the flip, the hinge from the manifest, then the
    /// leaf's yaw about its hinge.
    /// </summary>
    public (Vector3 Position, Quaternion Rotation) Leaf(int index, bool open)
    {
        LeafDef leaf = Def.Leaves[index];
        (Vector3 position, Quaternion rotation) = Frame.ZoneTransform;
        Quaternion flip = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Flip * MathF.PI);
        open &= Def.Opens;
        Quaternion swing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, open && !Def.Rolls ? (float)(leaf.OpenYaw * Math.PI / 180) : 0f);
        Vector3 hinge = leaf.Hinge;
        if (open && Def.Rolls)
        {
            // A garage door's curtain rolls up into its header.
            hinge += new Vector3(0, leaf.Size.Y, 0);
        }
        Quaternion frameFlip = Quaternion.Normalize(rotation * flip);
        return (position + Vector3.Transform(hinge, frameFlip), Quaternion.Normalize(frameFlip * swing));
    }

    /// <summary>
    /// Where a lock sits on a door's first leaf, in the leaf's frame (its origin the hinge on the leaf's middle plane): by
    /// the free edge at hand height, its back resting on the front of the handle plate on the +Z face (the outside of
    /// the frame), where the keypad or the key cylinder shows. The lock is drawn there and placed there (DOORS.md §6).
    /// </summary>
    public static Vector3 LockOffset(LeafDef leaf) => new(leaf.Size.X * 0.86f, 1.05f, leaf.LockDepth);

    /// <summary>
    /// Where the back plate of a lock sits, on the leaf's other face (−Z), at the same place seen from that side: a
    /// matching housing, so a door with a lock shows one from both sides. It is turned half round: its front faces −Z.
    /// </summary>
    public static Vector3 LockBackOffset(LeafDef leaf) => LockOffset(leaf) with { Z = -leaf.LockDepth };

    /// <summary>Where a lock on this door is now (it swings with the leaf), in the zone.</summary>
    public (Vector3 Position, Quaternion Rotation) LockSpot
    {
        get
        {
            (Vector3 position, Quaternion rotation) = Leaf(0, Open);
            return (position + Vector3.Transform(LockOffset(Def.Leaves[0]), rotation), rotation);
        }
    }

    /// <summary>Where the back plate of a lock on this door is now, in the zone (facing the way the leaf's −Z face does).</summary>
    public (Vector3 Position, Quaternion Rotation) LockBackSpot
    {
        get
        {
            (Vector3 position, Quaternion rotation) = Leaf(0, Open);
            return (position + Vector3.Transform(LockBackOffset(Def.Leaves[0]), rotation),
                Quaternion.Normalize(rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI)));
        }
    }

    public override string ToString() => $"{Def.Key} in {Frame.Def.Key} #{Frame.Handle} ({(Open ? "open" : "shut")})";
}

/// <summary>The owner of a door leaf's body: the door's leaf (its part, <see cref="TargetRef.Part"/>).</summary>
internal sealed record DoorLeaf(Door Door, int Index) : ITargetable
{
    public TargetRef Ref => Door.Ref with { Part = (byte)(Index + 1) };
}

public sealed partial class BuildingWorld
{
    /// <summary>Key numbers run 1 … this (a key's condition holds its lock's).</summary>
    public const int MaxKeyCode = ushort.MaxValue;

    private readonly Dictionary<object, ConvexPart[]> _leafParts = new();
    private int _nextKeyCode = 1;

    /// <summary>Every door in the world.</summary>
    public IEnumerable<Door> Doors
    {
        get
        {
            foreach (Structure s in _structures)
            {
                foreach (Piece p in s.PieceSet)
                {
                    if (p.Door != null)
                    {
                        yield return p.Door;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Whether a door fits into a frame piece, turned by <paramref name="flip"/> (the door's own rules: building rights,
    /// reach and sight are the placement pipeline's, GROUND_SYSTEMS.md §6): single doors into doorways, double doors and the
    /// garage door into wall frames, one per opening, shut, its leaves clear of players.
    /// </summary>
    public Outcome CanFitDoor(DoorDef def, Piece frame, int flip)
    {
        if (frame.Gone || frame.Collapsing)
        {
            return OutcomeCode.NotFound;
        }
        if (!Data.TryOpening(frame.Def.Opening, out _))
        {
            return OutcomeCode.NotAnOpening;
        }
        if (frame.Def.Opening != def.Opening)
        {
            return def.Opening == Opening.Door ? OutcomeCode.NeedsDoorway
                : def.Opening == Opening.Double ? OutcomeCode.NeedsWallFrame : OutcomeCode.NotAnOpening;
        }
        if (frame.Door != null)
        {
            return OutcomeCode.HasDoor;
        }
        return LeavesBlocked(new Door(EntityId.None, def, frame, flip & 1, 0), open: false) ? OutcomeCode.PlayerInTheWay : Outcome.Done;
    }

    /// <summary>Fits a door into a frame piece (the checks of <see cref="CanFitDoor"/> again): shut, unlocked, owned by its fitter.</summary>
    public Outcome PlaceDoor(ulong player, DoorDef def, Piece frame, int flip, out Door? door)
    {
        door = null;
        Outcome fits = CanFitDoor(def, frame, flip);
        if (!fits.Ok)
        {
            return fits;
        }
        var fitted = new Door(_newId(), def, frame, flip & 1, player);
        frame.Door = fitted;
        _doorIds[fitted.Id] = fitted;
        for (int i = 0; i < def.Leaves.Count; i++)
        {
            AddLeafBody(fitted, i);
        }
        door = fitted;
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorAdded, frame, fitted, Structure: frame.Structure));
        return Outcome.Done;
    }

    private ConvexPart[] LeafParts(LeafDef leaf)
    {
        if (!_leafParts.TryGetValue(leaf, out ConvexPart[]? parts))
        {
            parts = leaf.Parts;
            _leafParts.Add(leaf, parts);
        }
        return parts;
    }

    private void AddLeafBody(Door door, int index)
    {
        if (Physics == null)
        {
            return;
        }
        LeafDef leaf = door.Def.Leaves[index];
        (Vector3 position, Quaternion rotation) = door.Leaf(index, door.Open);
        door.Bodies[index] = Physics.Add(new DoorLeaf(door, index), Physics.ShapeFor(leaf, LeafParts(leaf)), position, rotation);
    }

    // Whether a player stands where the door's leaves would be.
    private bool LeavesBlocked(Door door, bool open)
    {
        for (int i = 0; i < door.Def.Leaves.Count; i++)
        {
            if (open && door.Def.Rolls)
            {
                continue;
            }
            (Vector3 position, Quaternion rotation) = door.Leaf(i, open);
            if (CharacterInside(LeafParts(door.Def.Leaves[i]), position, rotation))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Opens or shuts a door. A locked lock lets through only those it knows: a key lock its owner and anyone holding
    /// its key (<paramref name="keys"/>, the key codes the player carries), a code lock the players authorised on it.
    /// No building rights needed. The leaves' new pose must be clear of players.
    /// </summary>
    public Outcome Toggle(ulong player, Door door, IReadOnlyCollection<int>? keys = null)
    {
        if (door.Gone)
        {
            return OutcomeCode.NotFound;
        }
        if (!door.Def.Opens)
        {
            return OutcomeCode.NotUsable;
        }
        if (!CanUse(player, door, keys))
        {
            return OutcomeCode.Locked;
        }
        if (LeavesBlocked(door, !door.Open))
        {
            return OutcomeCode.SwingBlocked;
        }
        SetOpen(door, !door.Open);
        return Outcome.Done;
    }

    // Opens or shuts a door's leaves (their bodies move with them).
    private void SetOpen(Door door, bool open)
    {
        door.Open = open;
        for (int i = 0; i < door.Def.Leaves.Count; i++)
        {
            (Vector3 position, Quaternion rotation) = door.Leaf(i, door.Open);
            Physics?.Move(door.Bodies[i], position, rotation);
        }
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorChanged, door.Frame, door, Structure: door.Frame.Structure));
    }

    /// <summary>Whether a player can open and shut a door now.</summary>
    public static bool CanUse(ulong player, ILockHost door, IReadOnlyCollection<int>? keys = null) => door.Lock switch
    {
        null => true,
        KeyLock key => key.CanUse(player, keys),
        CodeLock code => code.CanUse(player),
        _ => false,
    };

    /// <summary>Takes a door back as an item: it must be open with no lock on it (Rust's rule). No building rights needed.</summary>
    public Outcome PickUpDoor(ulong player, Door door)
    {
        if (door.Gone)
        {
            return OutcomeCode.NotFound;
        }
        if (door.Lock != null)
        {
            return OutcomeCode.RemoveLockFirst;
        }
        if (door.Def.Opens && !door.Open)
        {
            return OutcomeCode.OpenItFirst;
        }
        if (!door.Def.Opens && PrivilegeAt(player, door.Frame.ZoneTransform.Position) == Privilege.Blocked)
        {
            return OutcomeCode.BuildingBlocked;
        }
        RemoveDoor(door);
        return Outcome.Done;
    }

    internal void RemoveDoor(Door door, Removal reason = Removal.Taken)
    {
        if (door.Gone)
        {
            return;
        }
        door.Gone = true;
        _doorIds.Remove(door.Id);
        for (int i = 0; i < door.Bodies.Length; i++)
        {
            Physics?.Remove(door.Bodies[i], new DoorLeaf(door, i));
        }
        door.Frame.Door = null;
        door.Lock = null;
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorRemoved, door.Frame, door, Structure: door.Frame.Structure, Removal: reason));
    }

    // A new lock of each kind, by the kind's number (a lookup, not a switch over kinds): a key lock takes the next key number.
    private static readonly Func<BuildingWorld, ulong, DoorLock>?[] NewLocks =
    {
        null,
        (world, owner) => new KeyLock(world._newId(), owner, world.NextKeyCode()),
        (world, owner) => new CodeLock(world._newId(), owner),
    };

    internal void NotifyLockChanged(Door door) =>
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorChanged, door.Frame, door, Structure: door.Frame.Structure));

    private int NextKeyCode()
    {
        int code = _nextKeyCode;
        _nextKeyCode = _nextKeyCode % MaxKeyCode + 1;
        return code;
    }

    /// <summary>
    /// Fits a lock of a kind to a door without one: a key lock unlocked, its owner the fitter; a code lock with no code yet,
    /// unlocked, its owner authorised. Building rights are the placement pipeline's (GROUND_SYSTEMS.md §6).
    /// </summary>
    public Outcome AttachLock(ulong player, ILockHost door, LockKind kind, out DoorLock? fitted)
    {
        fitted = null;
        if (door.LockGone)
        {
            return OutcomeCode.NotFound;
        }
        if (!door.Lockable)
        {
            return OutcomeCode.NotUsable;
        }
        if (door.Lock != null)
        {
            return OutcomeCode.HasLock;
        }
        if ((uint)kind >= (uint)NewLocks.Length || NewLocks[(int)kind] is not { } make)
        {
            return OutcomeCode.Invalid;
        }
        fitted = make(this, player);
        door.Lock = fitted;
        door.LockChanged();
        return Outcome.Done;
    }

    /// <summary>Fits a key lock (<see cref="AttachLock"/>).</summary>
    public Outcome AttachKeyLock(ulong player, ILockHost door, out KeyLock? keyLock)
    {
        Outcome done = AttachLock(player, door, LockKind.Key, out DoorLock? fitted);
        keyLock = fitted as KeyLock;
        return done;
    }

    /// <summary>Fits a code lock (<see cref="AttachLock"/>).</summary>
    public Outcome AttachCodeLock(ulong player, ILockHost door, out CodeLock? codeLock)
    {
        Outcome done = AttachLock(player, door, LockKind.Code, out DoorLock? fitted);
        codeLock = fitted as CodeLock;
        return done;
    }

    /// <summary>
    /// Sets or changes a code lock's code: four digits, by an authorised player, while it's unlocked. It locks at once
    /// and only the setter stays authorised (RustMVP's rule).
    /// </summary>
    public Outcome SetCode(ulong player, ILockHost door, string code)
    {
        if (door.Lock is not CodeLock codeLock)
        {
            return OutcomeCode.NoLock;
        }
        if (!ValidCode(code))
        {
            return OutcomeCode.BadCode;
        }
        if (!codeLock.IsAuthorised(player))
        {
            return OutcomeCode.NotOnLock;
        }
        if (codeLock.Locked)
        {
            return OutcomeCode.UnlockItFirst;
        }
        codeLock.CodeHash = codeLock.HashOf(code);
        codeLock.Locked = true;
        codeLock.AuthorisedList.Clear();
        codeLock.AuthorisedList.Add(player);
        door.LockChanged();
        return Outcome.Done;
    }

    /// <summary>
    /// Enters a code: the right one authorises the player (up to the list's limit); a wrong one makes them wait 1, then
    /// 5, then 30 seconds before the next try (<see cref="BuildingData.WrongCodeWait"/>).
    /// </summary>
    public Outcome EnterCode(ulong player, ILockHost door, string code, double now)
    {
        if (door.Lock is not CodeLock codeLock)
        {
            return OutcomeCode.NoLock;
        }
        if (!codeLock.HasCode)
        {
            return OutcomeCode.NoCode;
        }
        if (codeLock.Failures.TryGetValue(player, out (int Failures, double Until) failed) && now < failed.Until)
        {
            return OutcomeCode.WaitForCode.With((int)Math.Ceiling(failed.Until - now));
        }
        if (ValidCode(code) && codeLock.HashOf(code) == codeLock.CodeHash)
        {
            codeLock.Failures.Remove(player);
            if (!codeLock.IsAuthorised(player))
            {
                if (codeLock.AuthorisedList.Count >= Data.LockMaxAuthorised)
                {
                    return OutcomeCode.ListFull;
                }
                codeLock.AuthorisedList.Add(player);
                door.LockChanged();
            }
            return Outcome.Done;
        }
        int failures = Math.Min(failed.Failures + 1, 100);
        double wait = Data.WrongCodeWait[Math.Min(failures, Data.WrongCodeWait.Count) - 1];
        codeLock.Failures[player] = (failures, now + wait);
        return OutcomeCode.WrongCode;
    }

    /// <summary>
    /// "Authorise team" (G7, Rust's): a player on a code lock's list puts the members of their team on it too, up to the
    /// list's limit. Refused for a stranger (it would be a way round the code) or a key lock.
    /// </summary>
    public Outcome AuthoriseOnLock(ulong player, ILockHost door, IEnumerable<ulong> members)
    {
        if (door.Lock is not CodeLock codeLock)
        {
            return OutcomeCode.NoLock;
        }
        if (!codeLock.IsAuthorised(player))
        {
            return OutcomeCode.NotOnLock;
        }
        bool changed = false, full = false;
        foreach (ulong member in members)
        {
            if (codeLock.IsAuthorised(member))
            {
                continue;
            }
            if (codeLock.AuthorisedList.Count >= Data.LockMaxAuthorised)
            {
                full = true;
                break;
            }
            codeLock.AuthorisedList.Add(member);
            changed = true;
        }
        if (changed)
        {
            door.LockChanged();
        }
        return full && !changed ? OutcomeCode.ListFull : Outcome.Done;
    }

    /// <summary>Locks a lock: a code lock by an authorised player once it has a code; a key lock by its owner or a key holder.</summary>
    public Outcome SetLocked(ulong player, ILockHost door, bool locked, IReadOnlyCollection<int>? keys = null)
    {
        switch (door.Lock)
        {
            case CodeLock codeLock:
                if (!codeLock.IsAuthorised(player))
                {
                    return OutcomeCode.NotOnLock;
                }
                if (locked && !codeLock.HasCode)
                {
                    return OutcomeCode.NoCode;
                }
                break;
            case KeyLock keyLock:
                if (player != keyLock.Owner && (keys == null || !HasKey(keys, keyLock.KeyCode)))
                {
                    return OutcomeCode.NeedsKey;
                }
                break;
            default:
                return OutcomeCode.NoLock;
        }
        door.Lock.Locked = locked;
        door.LockChanged();
        return Outcome.Done;
    }

    /// <summary>Takes a lock off: unlocked, by an authorised player (a code lock) or its owner (a key lock). It comes back as an item.</summary>
    public Outcome RemoveLock(ulong player, ILockHost door)
    {
        switch (door.Lock)
        {
            case null:
                return OutcomeCode.NoLock;
            case CodeLock codeLock when !codeLock.IsAuthorised(player):
            case KeyLock keyLock when keyLock.Owner != player:
                return OutcomeCode.NotOnLock;
        }
        if (door.Lock.Locked)
        {
            return OutcomeCode.UnlockItFirst;
        }
        door.Lock = null;
        door.LockChanged();
        return Outcome.Done;
    }

    /// <summary>Damages a door through its protection (its material's grade, hard side); at zero it breaks and its lock goes with it.</summary>
    public DamageResult Damage(Door door, in DamageHit hit, double now)
    {
        if (door.Gone)
        {
            return default;
        }
        double loss = Resolve(door.Def.Protection.Hard, hit, door.Health);
        if (loss <= 0)
        {
            return default;
        }
        door.Health -= loss;
        door.LastDamagedAt = now;
        bool destroyed = door.Health <= 0;
        if (destroyed)
        {
            RemoveDoor(door, Removal.Destroyed);
        }
        else
        {
            Changed?.Invoke(new BuildingEvent(BuildingChange.DoorChanged, door.Frame, door, Structure: door.Frame.Structure));
        }
        return new DamageResult(loss, false, destroyed);
    }

    /// <summary>Repairs any fitted attachment through the shared hammer, with its data-defined material and full-repair cost.</summary>
    public Outcome Repair(ulong player, Door door, IBuildingWallet wallet, double now)
    {
        if (door.Gone || door.Frame.Gone || door.Frame.Collapsing) return OutcomeCode.NotFound;
        if (PrivilegeAt(player, door.Frame.ZoneTransform.Position, Data.ClaimPadding) == Privilege.Blocked)
            return OutcomeCode.BuildingBlocked;
        if (door.Health >= door.Def.Health) return OutcomeCode.FullHealth;
        double wait = door.LastDamagedAt + Data.RepairDelay - now;
        if (wait > 0) return OutcomeCode.RecentlyDamaged.With((int)Math.Ceiling(wait));
        double restored = Math.Min(Data.RepairPerHit, door.Def.Health - door.Health);
        int cost = (int)Math.Max(1, Math.Ceiling(door.Def.RepairFull * restored / door.Def.Health - 1e-9));
        string material = door.Def.RepairMaterial;
        if (wallet.Count(material) < cost || !wallet.Take(material, cost))
            return OutcomeCode.NotEnough.With(Data.MaterialItem(material));
        door.Health = Math.Min(door.Def.Health, door.Health + restored);
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorChanged, door.Frame, door, Structure: door.Frame.Structure));
        return Outcome.Done;
    }

    private bool ValidCode(string code)
    {
        if (code.Length != Data.CodeLength)
        {
            return false;
        }
        foreach (char c in code)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasKey(IReadOnlyCollection<int> keys, int code)
    {
        foreach (int k in keys)
        {
            if (k == code)
            {
                return true;
            }
        }
        return false;
    }
}
