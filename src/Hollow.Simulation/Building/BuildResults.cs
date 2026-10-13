using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hollow.Simulation.Building;

/// <summary>Why the building rules refused something. Every refusal has a line for the player (<see cref="Outcome.Text"/>).</summary>
public enum Refusal : byte
{
    None,
    NoSocket,
    NeedsFoundation,
    TooFar,
    OutOfSight,
    BuildingBlocked,
    Occupied,
    Floating,
    Buried,
    TooSteep,
    HitsPiece,
    HitsGround,
    HitsObject,
    PlayerInTheWay,
    Unstable,
    NotEnough,
    TooManyPieces,
    JoinsClaims,
    NotAuthorised,
    EditWindowOver,
    NotHigherGrade,
    RecentlyDamaged,
    FullHealth,
    CannotRotate,
    DoorInTheWay,
    NotAnOpening,
    WrongOpening,
    HasDoor,
    Locked,
    HasLock,
    NoLock,
    WrongCode,
    WaitForCode,
    BadCode,
    NoCode,
    OpenItFirst,
    RemoveLockFirst,
    UnlockItFirst,
    SwingBlocked,
    HasClaim,
    NotOnStructure,
    ListFull,
    Gone,

    // Phase 2: refusals of a player's request over the network.

    /// <summary>The player doesn't carry what the action needs (a building plan, a hammer).</summary>
    NoTool,

    /// <summary>Malformed: a piece, socket, slot, grade or code that can't be.</summary>
    Invalid,
    Dead,

    /// <summary>Over the player's action budget.</summary>
    RateLimited,

    /// <summary>The slot no longer holds the item the client saw there.</summary>
    Stale,

    /// <summary>Only the lock's owner may do that.</summary>
    NotOwner,
}

/// <summary>The answer to a building action: done, or refused with a reason and a line for the player.</summary>
public readonly record struct Outcome(Refusal Reason, string Text)
{
    public static readonly Outcome Done = new(Refusal.None, "");

    public bool Ok => Reason == Refusal.None;

    public static Outcome Refuse(Refusal reason, string? text = null) => new(reason, text ?? DefaultText(reason));

    /// <summary>The player's line for a refusal without particulars.</summary>
    public static string DefaultText(Refusal reason) => reason switch
    {
        Refusal.None => "",
        Refusal.NoSocket => "Aim at the ground or at a building socket",
        Refusal.NeedsFoundation => "Start with a foundation",
        Refusal.TooFar => "Too far away",
        Refusal.OutOfSight => "Something is in the way",
        Refusal.BuildingBlocked => "Building blocked: someone else's claim",
        Refusal.Occupied => "Something is already built there",
        Refusal.Floating => "The foundation would float: the ground is too far below",
        Refusal.Buried => "The foundation would be buried: the ground is too high",
        Refusal.TooSteep => "The ground is too steep",
        Refusal.HitsPiece => "It would cut into another piece",
        Refusal.HitsGround => "It would cut into the ground",
        Refusal.HitsObject => "Something is in the way",
        Refusal.PlayerInTheWay => "A player is in the way",
        Refusal.Unstable => "Not stable enough",
        Refusal.NotEnough => "Not enough materials",
        Refusal.TooManyPieces => "This building can't grow any bigger",
        Refusal.JoinsClaims => "That would join two claimed buildings",
        Refusal.NotAuthorised => "You have no building rights here",
        Refusal.EditWindowOver => "Too late: pieces can be rotated or demolished only for 10 minutes after placing",
        Refusal.NotHigherGrade => "Already at this grade or stronger",
        Refusal.RecentlyDamaged => "Recently damaged",
        Refusal.FullHealth => "Already at full health",
        Refusal.CannotRotate => "This piece can't be rotated",
        Refusal.DoorInTheWay => "Take the door out first",
        Refusal.NotAnOpening => "Aim at a doorway or a wall frame",
        Refusal.WrongOpening => "This door doesn't fit that opening",
        Refusal.HasDoor => "There's a door here already",
        Refusal.Locked => "It's locked",
        Refusal.HasLock => "There's a lock on it already",
        Refusal.NoLock => "There's no lock on it",
        Refusal.WrongCode => "Wrong code",
        Refusal.WaitForCode => "Wait before trying another code",
        Refusal.BadCode => "A code is four digits",
        Refusal.NoCode => "Set a code first",
        Refusal.OpenItFirst => "Open the door first",
        Refusal.RemoveLockFirst => "Take the lock off first",
        Refusal.UnlockItFirst => "Unlock it first",
        Refusal.SwingBlocked => "Something is blocking the door",
        Refusal.HasClaim => "This building has a claim device already",
        Refusal.NotOnStructure => "Place it on a foundation or a floor",
        Refusal.ListFull => "The list is full",
        Refusal.Gone => "It's gone",
        Refusal.NoTool => "You need the right tool",
        Refusal.Invalid => "That can't be done",
        Refusal.Dead => "Not while dead",
        Refusal.RateLimited => "Too fast: wait a moment",
        Refusal.Stale => "That item has moved",
        Refusal.NotOwner => "Only its owner can do that",
        _ => reason.ToString(),
    };

    public override string ToString() => Ok ? "done" : $"{Reason}: {Text}";
}

/// <summary>Building rights at a place for a player (GROUND.md §8's privilege status).</summary>
public enum Privilege : byte
{
    /// <summary>No claim device covers the place: anyone may build.</summary>
    None,

    /// <summary>Covered, and the player is authorised on every device that covers it.</summary>
    Allowed,

    /// <summary>Covered by a device the player isn't authorised on.</summary>
    Blocked,
}

/// <summary>
/// Where the building rules take materials from: the player's inventory on the server (phase 2), a dictionary in
/// tests and the sandbox.
/// </summary>
public interface IBuildingWallet
{
    int Count(string material);

    /// <summary>Takes the amount, or nothing and false when there isn't enough.</summary>
    bool Take(string material, int amount);
}

/// <summary>A wallet of materials in a dictionary; <see cref="Endless"/> never runs out.</summary>
public sealed class Wallet : IBuildingWallet
{
    private readonly Dictionary<string, int> _amounts = new(StringComparer.Ordinal);

    public bool Endless { get; init; }

    public int this[string material]
    {
        get => _amounts.TryGetValue(material, out int n) ? n : 0;
        set => _amounts[material] = value;
    }

    public int Count(string material) => Endless ? 1_000_000 : this[material];

    public bool Take(string material, int amount)
    {
        if (Endless)
        {
            return true;
        }
        if (this[material] < amount)
        {
            return false;
        }
        this[material] -= amount;
        return true;
    }
}

/// <summary>A character's capsule in the zone, for "a player is in the way": feet, up, radius and height.</summary>
public readonly record struct Capsule(Vector3 Feet, Vector3 Up, float Radius, float Height);

/// <summary>What changed in the building world, for views and replication.</summary>
public enum BuildingChange : byte
{
    PieceAdded,
    PieceRemoved,

    /// <summary>Grade, health, yaw or the collapsing flag.</summary>
    PieceChanged,

    /// <summary>A structure's stability values were solved again.</summary>
    Stability,
    DoorAdded,
    DoorRemoved,

    /// <summary>Opened, closed, locked, damaged.</summary>
    DoorChanged,
    ClaimAdded,
    ClaimRemoved,
    ClaimChanged,
}

public readonly record struct BuildingEvent(BuildingChange Change, Piece? Piece = null, Door? Door = null, ClaimDevice? Claim = null,
    Structure? Structure = null, Removal Removal = Removal.Taken);

/// <summary>
/// Why the building rules took a piece, door or claim device away (on the removed events): destroyed, collapsed, decayed
/// and demolished are the zone's Destroyed event (GROUND.md §6.4.2); taken is not (picked up, or a client's copy
/// following the server).
/// </summary>
public enum Removal : byte
{
    /// <summary>Taken away whole: picked up, removed by a test or an admin, or forgotten by a client's copy.</summary>
    Taken,

    /// <summary>Broken by damage, with what went with it (the door in its frame, the claim device on its piece).</summary>
    Destroyed,

    /// <summary>Came down when what held it up went.</summary>
    Collapsed,

    /// <summary>Decayed away, its upkeep unpaid.</summary>
    Decayed,

    /// <summary>Demolished with the hammer.</summary>
    Demolished,
}
