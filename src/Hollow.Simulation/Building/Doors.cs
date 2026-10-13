using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Entities;

namespace Hollow.Simulation.Building;

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
public sealed class Door
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

    public DoorDef Def { get; }

    /// <summary>The doorway or wall frame it's fitted into.</summary>
    public Piece Frame { get; }

    /// <summary>0, or 1 when turned 180° (the other hinge side and swing).</summary>
    public int Flip { get; }

    public ulong Owner { get; }

    public bool Open { get; internal set; }

    public double Health { get; internal set; }

    public double LastDamagedAt { get; internal set; } = double.NegativeInfinity;

    public DoorLock? Lock { get; internal set; }

    public bool Gone { get; internal set; }

    /// <summary>
    /// A leaf's pose in the zone, open or shut: the frame's transform, the flip, the hinge from the manifest, then the
    /// leaf's yaw about its hinge.
    /// </summary>
    public (Vector3 Position, Quaternion Rotation) Leaf(int index, bool open)
    {
        LeafDef leaf = Def.Leaves[index];
        (Vector3 position, Quaternion rotation) = Frame.ZoneTransform;
        Quaternion flip = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Flip * MathF.PI);
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

    public override string ToString() => $"{Def.Key} in {Frame.Def.Key} #{Frame.Handle} ({(Open ? "open" : "shut")})";
}

/// <summary>The owner of a door leaf's body.</summary>
internal sealed record DoorLeaf(Door Door, int Index);

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
    /// Fits a door into a doorway (single doors) or a wall frame (double doors, the garage door): one per opening, with
    /// building rights there, shut, its swing clear of players.
    /// </summary>
    public Outcome PlaceDoor(ulong player, DoorDef def, Piece frame, int flip, Vector3 eye, out Door? door)
    {
        door = null;
        if (frame.Gone || frame.Collapsing)
        {
            return Outcome.Refuse(Refusal.Gone);
        }
        if (frame.Def.Opening is not (Opening.Door or Opening.Double))
        {
            return Outcome.Refuse(Refusal.NotAnOpening);
        }
        if (frame.Def.Opening != def.Opening)
        {
            return Outcome.Refuse(Refusal.WrongOpening, def.Opening == Opening.Door ? "This door needs a doorway" : "This door needs a wall frame");
        }
        if (frame.Door != null)
        {
            return Outcome.Refuse(Refusal.HasDoor);
        }
        (Vector3 position, _) = frame.ZoneTransform;
        if (PrivilegeAt(player, position, 0) == Privilege.Blocked)
        {
            return Outcome.Refuse(Refusal.BuildingBlocked);
        }
        if (Vector3.Distance(eye, position + Up(position) * 1.2f) > Data.Reach)
        {
            return Outcome.Refuse(Refusal.TooFar);
        }
        var fitted = new Door(_newId(), def, frame, flip & 1, player);
        if (LeavesBlocked(fitted, open: false))
        {
            return Outcome.Refuse(Refusal.PlayerInTheWay);
        }
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
            parts = new[] { PieceShapes.Leaf(leaf) };
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
            return Outcome.Refuse(Refusal.Gone);
        }
        if (!CanUse(player, door, keys))
        {
            return Outcome.Refuse(Refusal.Locked);
        }
        if (LeavesBlocked(door, !door.Open))
        {
            return Outcome.Refuse(Refusal.SwingBlocked);
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
    public static bool CanUse(ulong player, Door door, IReadOnlyCollection<int>? keys = null) => door.Lock switch
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
            return Outcome.Refuse(Refusal.Gone);
        }
        if (door.Lock != null)
        {
            return Outcome.Refuse(Refusal.RemoveLockFirst);
        }
        if (!door.Open)
        {
            return Outcome.Refuse(Refusal.OpenItFirst);
        }
        RemoveDoor(door);
        return Outcome.Done;
    }

    internal void RemoveDoor(Door door)
    {
        if (door.Gone)
        {
            return;
        }
        door.Gone = true;
        _doorIds.Remove(door.Id);
        foreach (uint body in door.Bodies)
        {
            Physics?.Remove(body);
        }
        door.Frame.Door = null;
        door.Lock = null;
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorRemoved, door.Frame, door, Structure: door.Frame.Structure));
    }

    /// <summary>Fits a key lock (unlocked) to a door without one; needs building rights at the door.</summary>
    public Outcome AttachKeyLock(ulong player, Door door, out KeyLock? keyLock)
    {
        keyLock = null;
        Outcome ok = CanAttach(player, door);
        if (!ok.Ok)
        {
            return ok;
        }
        keyLock = new KeyLock(_newId(), player, _nextKeyCode);
        _nextKeyCode = _nextKeyCode % MaxKeyCode + 1;
        door.Lock = keyLock;
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorChanged, door.Frame, door, Structure: door.Frame.Structure));
        return Outcome.Done;
    }

    /// <summary>Fits a code lock (no code yet, unlocked, its owner authorised) to a door without one.</summary>
    public Outcome AttachCodeLock(ulong player, Door door, out CodeLock? codeLock)
    {
        codeLock = null;
        Outcome ok = CanAttach(player, door);
        if (!ok.Ok)
        {
            return ok;
        }
        codeLock = new CodeLock(_newId(), player);
        door.Lock = codeLock;
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorChanged, door.Frame, door, Structure: door.Frame.Structure));
        return Outcome.Done;
    }

    private Outcome CanAttach(ulong player, Door door)
    {
        if (door.Gone)
        {
            return Outcome.Refuse(Refusal.Gone);
        }
        if (door.Lock != null)
        {
            return Outcome.Refuse(Refusal.HasLock);
        }
        (Vector3 position, _) = door.Frame.ZoneTransform;
        if (PrivilegeAt(player, position, 0) == Privilege.Blocked)
        {
            return Outcome.Refuse(Refusal.BuildingBlocked);
        }
        return Outcome.Done;
    }

    /// <summary>
    /// Sets or changes a code lock's code: four digits, by an authorised player, while it's unlocked. It locks at once
    /// and only the setter stays authorised (RustMVP's rule).
    /// </summary>
    public Outcome SetCode(ulong player, Door door, string code)
    {
        if (door.Lock is not CodeLock codeLock)
        {
            return Outcome.Refuse(Refusal.NoLock);
        }
        if (!ValidCode(code))
        {
            return Outcome.Refuse(Refusal.BadCode);
        }
        if (!codeLock.IsAuthorised(player))
        {
            return Outcome.Refuse(Refusal.NotAuthorised, "You aren't authorised on this lock");
        }
        if (codeLock.Locked)
        {
            return Outcome.Refuse(Refusal.UnlockItFirst);
        }
        codeLock.CodeHash = codeLock.HashOf(code);
        codeLock.Locked = true;
        codeLock.AuthorisedList.Clear();
        codeLock.AuthorisedList.Add(player);
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorChanged, door.Frame, door, Structure: door.Frame.Structure));
        return Outcome.Done;
    }

    /// <summary>
    /// Enters a code: the right one authorises the player (up to the list's limit); a wrong one makes them wait 1, then
    /// 5, then 30 seconds before the next try (<see cref="BuildingData.WrongCodeWait"/>).
    /// </summary>
    public Outcome EnterCode(ulong player, Door door, string code, double now)
    {
        if (door.Lock is not CodeLock codeLock)
        {
            return Outcome.Refuse(Refusal.NoLock);
        }
        if (!codeLock.HasCode)
        {
            return Outcome.Refuse(Refusal.NoCode);
        }
        if (codeLock.Failures.TryGetValue(player, out (int Failures, double Until) failed) && now < failed.Until)
        {
            return Outcome.Refuse(Refusal.WaitForCode, $"Wait {Math.Ceiling(failed.Until - now):0} s before trying another code");
        }
        if (ValidCode(code) && codeLock.HashOf(code) == codeLock.CodeHash)
        {
            codeLock.Failures.Remove(player);
            if (!codeLock.IsAuthorised(player))
            {
                if (codeLock.AuthorisedList.Count >= Data.LockMaxAuthorised)
                {
                    return Outcome.Refuse(Refusal.ListFull);
                }
                codeLock.AuthorisedList.Add(player);
            }
            return Outcome.Done;
        }
        int failures = Math.Min(failed.Failures + 1, 100);
        double wait = Data.WrongCodeWait[Math.Min(failures, Data.WrongCodeWait.Count) - 1];
        codeLock.Failures[player] = (failures, now + wait);
        return Outcome.Refuse(Refusal.WrongCode);
    }

    /// <summary>Locks a lock: a code lock by an authorised player once it has a code; a key lock by its owner or a key holder.</summary>
    public Outcome SetLocked(ulong player, Door door, bool locked, IReadOnlyCollection<int>? keys = null)
    {
        switch (door.Lock)
        {
            case CodeLock codeLock:
                if (!codeLock.IsAuthorised(player))
                {
                    return Outcome.Refuse(Refusal.NotAuthorised, "You aren't authorised on this lock");
                }
                if (locked && !codeLock.HasCode)
                {
                    return Outcome.Refuse(Refusal.NoCode);
                }
                break;
            case KeyLock keyLock:
                if (player != keyLock.Owner && (keys == null || !HasKey(keys, keyLock.KeyCode)))
                {
                    return Outcome.Refuse(Refusal.NotAuthorised, "You need its key");
                }
                break;
            default:
                return Outcome.Refuse(Refusal.NoLock);
        }
        door.Lock.Locked = locked;
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorChanged, door.Frame, door, Structure: door.Frame.Structure));
        return Outcome.Done;
    }

    /// <summary>Takes a lock off: unlocked, by an authorised player (a code lock) or its owner (a key lock). It comes back as an item.</summary>
    public Outcome RemoveLock(ulong player, Door door)
    {
        switch (door.Lock)
        {
            case null:
                return Outcome.Refuse(Refusal.NoLock);
            case CodeLock codeLock when !codeLock.IsAuthorised(player):
            case KeyLock keyLock when keyLock.Owner != player:
                return Outcome.Refuse(Refusal.NotAuthorised, "Only someone it knows can take it off");
        }
        if (door.Lock.Locked)
        {
            return Outcome.Refuse(Refusal.UnlockItFirst);
        }
        door.Lock = null;
        Changed?.Invoke(new BuildingEvent(BuildingChange.DoorChanged, door.Frame, door, Structure: door.Frame.Structure));
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
            RemoveDoor(door);
        }
        else
        {
            Changed?.Invoke(new BuildingEvent(BuildingChange.DoorChanged, door.Frame, door, Structure: door.Frame.Structure));
        }
        return new DamageResult(loss, false, destroyed);
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
