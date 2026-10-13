using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hollow.Simulation.Building;

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
/// A piece, a door or a claim device: what blows, blasts and raid alerts take alike (the zone's building receiver,
/// GROUND.md §6.4.2).
/// </summary>
public interface IBuildingPart
{
    Entities.EntityId Id { get; }

    /// <summary>Taken out of the building (broken, collapsed, decayed, demolished or picked up).</summary>
    bool Gone { get; }

    /// <summary>The claim device of the base it belongs to (a claim device is its own), or null when unclaimed.</summary>
    ClaimDevice? Base { get; }
}

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
