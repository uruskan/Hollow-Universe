using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Capabilities;

namespace Hollow.Simulation.Ships;

/// <summary>
/// A ship's door as the rules have it (IRON_WAKE.md §5.3): its progress (0 shut … 1 open) at a tick and which way it moves
/// from there, so the server and every client work out where it is at any tick alike (a door's motion is sent once, not
/// every tick); which way it was going when someone in its way stopped it (it goes on once they are clear); its health
/// (a door without health has none to lose).
/// </summary>
public struct ShipDoorState
{
    /// <summary>The progress at <see cref="Since"/>.</summary>
    public float From;

    /// <summary>+1 opening, −1 shutting, 0 still.</summary>
    public sbyte Moving;

    /// <summary>Stopped by someone in its way: the way it goes on once clear (+1, −1); 0 none.</summary>
    public sbyte Waiting;

    /// <summary>The tick its motion, or its stop, began.</summary>
    public long Since;

    public float Health;

    /// <summary>Where it is at a tick, at its rate a tick (the same arithmetic on every machine; before its motion began, where it began).</summary>
    public readonly float ProgressAt(long tick, float rate) =>
        Moving == 0 || tick <= Since ? From : Math.Clamp(From + Moving * ((tick - Since) * rate), 0f, 1f);

    /// <summary>Where it is at a fractional tick (drawing).</summary>
    public readonly float ProgressAt(double tick, float rate) =>
        Moving == 0 || tick <= Since ? From : (float)Math.Clamp(From + Moving * ((tick - Since) * rate), 0.0, 1.0);

    /// <summary>Going open, or open (its prompt offers to shut it); else shut or shutting.</summary>
    public readonly bool Opening => Moving != 0 ? Moving > 0 : Waiting != 0 ? Waiting > 0 : From > 0;

    /// <summary>Open at least half way, and opening or open, at <paramref name="progress"/>: a seat reached through it can be got at.</summary>
    public readonly bool LetsIn(float progress) => Opening && progress >= 0.5f;

    /// <summary>The tick its motion ends (its progress at a stop), or <see cref="Since"/> when still.</summary>
    public readonly long EndsAt(float rate) =>
        Moving == 0 || rate <= 0 ? Since : Since + (long)MathF.Ceiling((Moving > 0 ? 1 - From : From) / rate);
}

/// <summary>
/// A ship in a zone (GROUND.md §6.7): a dynamic entity with its motion (<see cref="ShipState"/>), who sits in each of its
/// seats, its doors, its parts' states and health, its lock and its list of crew, its owner, fuel and cargo. Owned by the
/// zone's thread. A parked ship (asleep, nobody at its helm) is a static body characters walk into; a flown one isn't.
/// </summary>
public sealed class Ship : ITargetable, IOwned, IContainerHolder, IAuthorisations
{
    /// <summary>A destroyed ship stays this many ticks (so everyone sees it go up), then only its wreck is left.</summary>
    public const int WreckTicks = 60;

    /// <summary>The most players its list holds (as the tool cupboard's).</summary>
    public const int MostCrew = 64;

    private readonly List<ulong> _crew = new();
    private EntityId _owner;

    public Ship(EntityId id, ShipDef def, EntityId owner)
    {
        Id = id;
        Def = def;
        Owner = owner;
        Cargo = new Container(ContainerKind.Box, Math.Max(1, def.CargoSlots));
        Parts = def.Interior?.Rest.Clone() as float[] ?? Array.Empty<float>();
        Occupants = new EntityId[def.Seats.Count];
        Doors = new ShipDoorState[def.Doors.Count];
        for (int i = 0; i < Doors.Length; i++)
        {
            ShipDoorDef door = def.Doors[i];
            // As the interior's poses have its parts (the ramp open as a ship is made); a door with no parts starts shut.
            float at = door.Parts.Count > 0 && door.Parts[0] < Parts.Length ? Parts[door.Parts[0]] : 0;
            Doors[i] = new ShipDoorState { From = Math.Clamp(at, 0f, 1f), Health = door.Health?.Max ?? 0 };
        }
        PartHealth = new float[def.Parts.Count];
        for (int i = 0; i < PartHealth.Length; i++)
        {
            PartHealth[i] = def.Parts[i].Health?.Max ?? 0;
        }
        Owner = owner;
    }

    /// <summary>
    /// Its moving parts' states, by the interior's part index (IRON_WAKE.md §4.3): a door's part as its door's progress
    /// has it, a turret's angle. A new ship's are the interior's poses (the ramp open); the doors move theirs each tick
    /// (<c>ShipSystem</c>'s doors), anything else goes through <c>ShipSystem.MovePart</c>.
    /// </summary>
    public float[] Parts { get; }

    /// <summary>Who sits in each seat (<see cref="ShipDef.Seats"/>' order), or none.</summary>
    public EntityId[] Occupants { get; }

    /// <summary>Each door (<see cref="ShipDef.Doors"/>' order).</summary>
    public ShipDoorState[] Doors { get; }

    /// <summary>Each target part's health (<see cref="ShipDef.Parts"/>' order; 0 for one without health: a door's part takes its door's).</summary>
    public float[] PartHealth { get; }

    /// <summary>Its own physics, for a ship people walk in (null for one they only sit in): made with the ship, gone with it.</summary>
    public Physics.ShipSpace? Space { get; internal set; }

    /// <summary>The frame characters aboard move in (null without an interior).</summary>
    public ShipFrame? Frame { get; internal set; }

    public EntityId Id { get; }

    /// <summary>How the world names it (GROUND_SYSTEMS.md §3.1).</summary>
    public TargetRef Ref => new(TargetFamily.Ship, Id.Value);

    public ShipDef Def { get; }

    /// <summary>Who built it (or was given it): on its list from then on.</summary>
    public EntityId Owner
    {
        get => _owner;
        set
        {
            _owner = value;
            if (!value.IsNone)
            {
                Authorise(value.Value);
            }
        }
    }

    /// <summary>Locked: its doors stay shut to anyone not on its list, and its seats for "unlocked" sitters too.</summary>
    public bool Locked { get; set; }

    public ShipState State;

    /// <summary>The pilot's controls for the coming tick (the server puts them here before each step).</summary>
    public ShipInput Input;

    /// <summary>What its last step did.</summary>
    public ShipEvents Events;

    /// <summary>The character at its helm, or none.</summary>
    public EntityId Pilot => Occupants.Length > 0 ? Occupants[Def.Helm.Index] : EntityId.None;

    /// <summary>
    /// Flown by the zone itself on <see cref="Input"/> with nobody in the seat: tests, the sandbox's traffic, and later
    /// the built-in autopilot. Never set from the network.
    /// </summary>
    public bool Autopilot { get; set; }

    public Container Cargo { get; }

    ulong IOwned.OwnerId => Owner.Value;

    Container? IContainerHolder.Container => Cargo;

    /// <summary>The static body while parked (0: none).</summary>
    public uint ParkedBody { get; set; }

    /// <summary>Where it left the zone for orbit (the descent comes back over it), when <see cref="ShipFlags.InOrbit"/>.</summary>
    public Vector3 OrbitAnchor { get; set; }

    /// <summary>Who hurt it last, and with what (the pilot's death, if it goes, is theirs).</summary>
    public EntityId LastAttacker { get; set; }

    public string LastAttackerName { get; set; } = "";

    /// <summary>When a part's health was last taken (world seconds): the hammer waits after damage, as the building's does.</summary>
    public double LastDamagedAt { get; set; } = double.NegativeInfinity;

    /// <summary>Ticks since it was destroyed (−1: not destroyed).</summary>
    public int WreckAge { get; set; } = -1;

    /// <summary>Raised on every change others see that isn't in its snapshot state (owner, lock, its list).</summary>
    public uint Revision { get; set; }

    /// <summary>Raised whenever a part moves (<c>ShipSystem.MovePart</c>): the clients that know the ship are sent its parts' states.</summary>
    public uint PartsRevision { get; internal set; }

    /// <summary>
    /// Raised whenever its seats, its doors' motion or the health of its parts change: the clients that know the ship are
    /// sent them (<c>ShipDetails</c>).
    /// </summary>
    public uint DetailsRevision { get; internal set; }

    /// <summary>Someone is at its helm.</summary>
    public bool Piloted => !Pilot.IsNone;

    public bool Destroyed => State.Has(ShipFlags.Destroyed);

    public bool InOrbit => State.Has(ShipFlags.InOrbit);

    /// <summary>A point of the ship's frame in the zone frame (its pivot is <see cref="ShipDef"/>'s origin).</summary>
    public Vector3 PointOf(Vector3 local) => State.PointOf(local, Def.CentreOfMass);

    /// <summary>The model's pivot in the zone frame (on the ground between the skids when it stands).</summary>
    public Vector3 Origin => State.Origin(Def.CentreOfMass);

    /// <summary>The seat a character sits in here, or −1.</summary>
    public int SeatOf(EntityId character)
    {
        for (int i = 0; i < Occupants.Length; i++)
        {
            if (Occupants[i] == character && !character.IsNone)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>A door's progress at a tick.</summary>
    public float DoorAt(int door, long tick) => Doors[door].ProgressAt(tick, Def.Doors[door].Rate);

    // ---------------------------------------------------------------- its list (the cupboard capability's, IRON_WAKE.md §5.2)

    /// <summary>Its crew: those authorised at its helm (its owner from the start).</summary>
    public IReadOnlyList<ulong> Crew => _crew;

    public bool IsAuthorised(ulong player) => _crew.Contains(player);

    /// <summary>Puts a player on its list (false: full).</summary>
    internal bool Authorise(ulong player)
    {
        if (_crew.Contains(player))
        {
            return true;
        }
        if (_crew.Count >= MostCrew)
        {
            return false;
        }
        _crew.Add(player);
        Revision++;
        return true;
    }

    internal void Deauthorise(ulong player)
    {
        if (_crew.Remove(player))
        {
            Revision++;
        }
    }

    internal void ClearCrew()
    {
        _crew.Clear();
        Revision++;
    }

    /// <summary>Its list as stored (a save, IW2): replaces what it holds.</summary>
    public void RestoreCrew(IEnumerable<ulong> players)
    {
        _crew.Clear();
        foreach (ulong p in players)
        {
            if (p != 0 && !_crew.Contains(p) && _crew.Count < MostCrew)
            {
                _crew.Add(p);
            }
        }
    }

    public override string ToString() => $"{Def.Key} {Id} {State}";
}

/// <summary>The entity component linking a ship's entity to its object.</summary>
public struct ShipLink
{
    public Ship Ship;
}

/// <summary>The component a character has while in a ship's seat: the ship and the seat (its index in its kind's seats).</summary>
public struct ShipSeat
{
    public Ship Ship;

    public int Seat;
}
