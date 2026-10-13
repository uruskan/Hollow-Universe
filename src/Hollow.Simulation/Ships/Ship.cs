using System.Numerics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Ships;

/// <summary>
/// A ship in a zone (GROUND.md §6.7): a dynamic entity with its motion (<see cref="ShipState"/>), its pilot, hatch, lock,
/// owner, fuel and cargo. Owned by the zone's thread. A parked ship (asleep, nobody in the seat) is a static body
/// characters walk into; a flown one isn't.
/// </summary>
public sealed class Ship : ITargetable
{
    /// <summary>The hatch swings open or shut in this many ticks (the model's clip: a second).</summary>
    public const int HatchTicks = 30;

    /// <summary>A destroyed ship stays this many ticks (so everyone sees it go up), then only its wreck is left.</summary>
    public const int WreckTicks = 60;

    public Ship(EntityId id, ShipDef def, EntityId owner)
    {
        Id = id;
        Def = def;
        Owner = owner;
        Cargo = new Container(ContainerKind.Box, System.Math.Max(1, def.CargoSlots));
    }

    public EntityId Id { get; }

    /// <summary>How the world names it (GROUND_SYSTEMS.md §3.1).</summary>
    public TargetRef Ref => new(TargetFamily.Ship, Id.Value);

    public ShipDef Def { get; }

    /// <summary>Who built it (only they lock and unlock it).</summary>
    public EntityId Owner { get; set; }

    /// <summary>Locked: nobody but the owner opens the hatch, boards or reaches the cargo.</summary>
    public bool Locked { get; set; }

    public ShipState State;

    /// <summary>The pilot's controls for the coming tick (the server puts them here before each step).</summary>
    public ShipInput Input;

    /// <summary>What its last step did.</summary>
    public ShipEvents Events;

    /// <summary>The character in the seat, or none.</summary>
    public EntityId Pilot { get; set; }

    /// <summary>
    /// Flown by the zone itself on <see cref="Input"/> with nobody in the seat: tests, the sandbox's traffic, and later
    /// the built-in autopilot. Never set from the network.
    /// </summary>
    public bool Autopilot { get; set; }

    /// <summary>Where the hatch is going, and how far it has got (0 shut … <see cref="HatchTicks"/> open).</summary>
    public bool HatchOpen { get; set; }

    public int HatchProgress { get; set; }

    public Container Cargo { get; }

    /// <summary>The static body while parked (0: none).</summary>
    public uint ParkedBody { get; set; }

    /// <summary>Where it left the zone for orbit (the descent comes back over it), when <see cref="ShipFlags.InOrbit"/>.</summary>
    public Vector3 OrbitAnchor { get; set; }

    /// <summary>Who hurt it last, and with what (the pilot's death, if it goes, is theirs).</summary>
    public EntityId LastAttacker { get; set; }

    public string LastAttackerName { get; set; } = "";

    /// <summary>Ticks since it was destroyed (−1: not destroyed).</summary>
    public int WreckAge { get; set; } = -1;

    /// <summary>Raised on every change others see that isn't in its snapshot state (owner, lock).</summary>
    public uint Revision { get; set; }

    public bool Piloted => !Pilot.IsNone;

    public bool Destroyed => State.Has(ShipFlags.Destroyed);

    public bool InOrbit => State.Has(ShipFlags.InOrbit);

    /// <summary>A point of the ship's frame in the zone frame (its pivot is <see cref="ShipDef"/>'s origin).</summary>
    public Vector3 PointOf(Vector3 local) => State.PointOf(local, Def.CentreOfMass);

    /// <summary>The model's pivot in the zone frame (on the ground between the skids when it stands).</summary>
    public Vector3 Origin => State.Origin(Def.CentreOfMass);

    public override string ToString() => $"{Def.Key} {Id} {State}";
}

/// <summary>The entity component linking a ship's entity to its object.</summary>
public struct ShipLink
{
    public Ship Ship;
}

/// <summary>The component a character has while in a ship's seat.</summary>
public struct ShipSeat
{
    public Ship Ship;
}
