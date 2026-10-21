using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Ships.Modules;

/// <summary>
/// A place on a kind of ship where a module goes (ships.json <c>sockets</c>, VEHICLES.md §9.1): a car's bay or nose mount, a
/// bike's mount point, a rotorcraft's rear bay. A module set here has its frame's origin at <see cref="At"/> (the ship's
/// frame; sockets don't turn modules), on level 2 raised by the height of the module under it. Sockets of one
/// <see cref="Row"/>, in the list's order, are neighbours: a module two long takes two of them.
/// </summary>
public sealed class ShipSocketDef
{
    /// <summary>Its number on the wire and in saves (the list's order, append only).</summary>
    public int Index { get; init; }

    public string Key { get; init; } = "";

    /// <summary>What the vehicle screen calls it (ships.json <c>name</c>; its key by default).</summary>
    public string Name { get; init; } = "";

    /// <summary>Where a level-1 module's frame starts, in the ship's frame.</summary>
    public Vector3 At { get; init; }

    /// <summary>The tags a module must fit one of (a bay, an engine mount, a sidecar).</summary>
    public IReadOnlyList<string> Fits { get; init; } = Array.Empty<string>();

    /// <summary>The row whose neighbours a long module also takes ("" stands alone).</summary>
    public string Row { get; init; } = "";

    /// <summary>How many levels it offers: 1, or 2 (a module on the roof of the one below, VEHICLES.md §3.3).</summary>
    public int Levels { get; init; } = 1;

    /// <summary>
    /// Its modules stand on the ground (ships.json <c>"ground": true</c>, VEHICLES.md §9.11): running gear and the arms that
    /// carry it, left where they are when a lift kit raises the rest of the vehicle.
    /// </summary>
    public bool Ground { get; init; }

    public bool Takes(ModuleDef module) => module.Fits.Any(tag => Fits.Contains(tag, StringComparer.Ordinal));

    public override string ToString() => Key;
}

/// <summary>
/// A kind of module (modules.json, VEHICLES.md §3.2, §9.1): an item that goes into a socket and adds to the ship what its
/// data says: hull boxes, seats, doors and target parts (as a ship's own are read, in the module's frame: its origin at its
/// socket, +Z forward, +Y up), mass, fuel, a hold. Its first target part is its body: its hull boxes are that part's, so a
/// shot meets it and the hammer mends it. A cab, an engine, a deck, a locker, a rotorcraft's seat or box.
/// </summary>
public sealed class ModuleDef
{
    public string Key { get; init; } = "";

    /// <summary>The item it is (its ID names it on the wire and in saves).</summary>
    public ItemDef Item { get; init; } = null!;

    public ushort Id => Item.Id;

    public string Name => Item.Name;

    /// <summary>The socket tags it fits (any one).</summary>
    public IReadOnlyList<string> Fits { get; init; } = Array.Empty<string>();

    /// <summary>How many neighbouring sockets of a row it takes (a camper shell two bays).</summary>
    public int Span { get; init; } = 1;

    /// <summary>It may stand on the chassis (level 1) and on a roof that carries load (level 2).</summary>
    public bool Lower { get; init; } = true;

    public bool Upper { get; init; }

    /// <summary>Its height over its origin: where the roof a level-2 module stands on is, and the build's height.</summary>
    public float Height { get; init; }

    /// <summary>Its roof carries a level-2 module.</summary>
    public bool Carries { get; init; }

    public float Mass { get; init; }

    /// <summary>Its centre of mass in its own frame.</summary>
    public Vector3 CentreOfMass { get; init; }

    public IReadOnlyList<HullBox> Hull { get; init; } = Array.Empty<HullBox>();

    public IReadOnlyList<ShipSeatDef> Seats { get; init; } = Array.Empty<ShipSeatDef>();

    public IReadOnlyList<ShipDoorDef> Doors { get; init; } = Array.Empty<ShipDoorDef>();

    public IReadOnlyList<ShipTargetPart> Parts { get; init; } = Array.Empty<ShipTargetPart>();

    /// <summary>Its wheels, in its own frame (a sidecar's, a module of running gear).</summary>
    public IReadOnlyList<WheelDef> Wheels { get; init; } = Array.Empty<WheelDef>();

    /// <summary>The power it adds to the ship's engines, in watts (an engine's): also the drive's "power".</summary>
    public float Power { get; init; }

    /// <summary>Its power's factor overboosted (modules.json <c>boost</c>, 1 … 3; VEHICLES.md §9.10): Shift held, an engine gives this much more.</summary>
    public float Boost { get; init; } = 1;

    /// <summary>What it adds to the ship's drive, by name (modules.json <c>drive</c>: a rotor's lift, its spool).</summary>
    public IReadOnlyDictionary<string, float> Drive { get; init; } = new Dictionary<string, float>();

    /// <summary>Fuel it adds to the ship's tank.</summary>
    public float FuelCapacity { get; init; }

    /// <summary>A hold's slots (0: none), opened at its part with the container capability.</summary>
    public int Hold { get; init; }

    /// <summary>Its part (its own index) where the hold opens, or −1.</summary>
    public int HoldPart { get; init; } = -1;

    /// <summary>The model catalogue's key the views draw it with (by default its own key).</summary>
    public string Model { get; init; } = "";

    /// <summary>
    /// How far it lifts the vehicle's body over its running gear (modules.json <c>raise</c>, metres; VEHICLES.md §9.11: a lift
    /// kit): the kind's own hull, seats, parts, lamps and centre of mass and every module not on a ground socket go up by the
    /// build's sum.
    /// </summary>
    public float Raise { get; init; }

    /// <summary>Tags one of the build's other modules must fit for this one to be fitted (monster tyres need the lift kit).</summary>
    public IReadOnlyList<string> Needs { get; init; } = Array.Empty<string>();

    /// <summary>Tags none of the build's other modules may fit while this one is (the stock tyres and the lift kit).</summary>
    public IReadOnlyList<string> Excludes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The model the whole vehicle is drawn with while it is fitted (modules.json <c>frame</c>; "": the kind's): the frame
    /// whose arms and wheels this running gear is (the lift kit's is the Monster TOGG's).
    /// </summary>
    public string Frame { get; init; } = "";

    /// <summary>The kind the vehicle sounds like while it is fitted (modules.json <c>sound</c>, a key of sounds.json's ships; "": no change).</summary>
    public string Sound { get; init; } = "";

    /// <summary>Its own moments of inertia about its centre of mass, or null: a solid box of its hull's bounds.</summary>
    public Vector3? Inertia { get; init; }

    /// <summary>
    /// The kind's numbers it replaces while fitted (modules.json <c>ground</c>, <c>drag</c>, <c>crash</c> and <c>fuelUse</c>
    /// blocks, by name: "ground.topSpeed"), applied in the build's order (<see cref="ShipTuning"/>).
    /// </summary>
    public IReadOnlyList<(string Name, float Value)> Sets { get; init; } = Array.Empty<(string, float)>();

    /// <summary>It fits one of these tags.</summary>
    public bool FitsAny(IReadOnlyList<string> tags)
    {
        foreach (string tag in tags)
        {
            if (Fits.Contains(tag, StringComparer.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public override string ToString() => Key;
}

/// <summary>One module installed: the socket it starts at, its level (1 or 2) and its item's ID.</summary>
public readonly record struct Installed(byte Socket, byte Level, ushort Module);

/// <summary>
/// What a ship has installed (VEHICLES.md §9.1): its entries in the order of their sockets and levels, so two builds with the
/// same modules in the same places are equal whatever order they were installed in. With its kind, it names a composed
/// layout (<see cref="ShipAssembler"/>), and it is all a save or a client needs to make that layout again.
/// </summary>
public sealed class ShipBuild : IEquatable<ShipBuild>
{
    /// <summary>The most entries a build holds (a byte on the wire and in saves).</summary>
    public const int MostEntries = 32;

    public static readonly ShipBuild Empty = new(Array.Empty<Installed>());

    private readonly Installed[] _entries;
    private readonly int _hash;

    public ShipBuild(IEnumerable<Installed> entries)
    {
        _entries = entries.OrderBy(e => e.Socket).ThenBy(e => e.Level).ToArray();
        int hash = 17;
        foreach (Installed e in _entries)
        {
            hash = unchecked(hash * 31 + (e.Socket | e.Level << 8 | e.Module << 16));
        }
        _hash = hash;
    }

    public IReadOnlyList<Installed> Entries => _entries;

    public int Count => _entries.Length;

    public bool IsEmpty => _entries.Length == 0;

    /// <summary>This build with one more module.</summary>
    public ShipBuild With(Installed entry) => new(_entries.Append(entry));

    /// <summary>This build without whatever starts at a socket and level.</summary>
    public ShipBuild Without(int socket, int level) => new(_entries.Where(e => e.Socket != socket || e.Level != level));

    /// <summary>The entry starting at a socket and level, if any.</summary>
    public Installed? At(int socket, int level)
    {
        foreach (Installed e in _entries)
        {
            if (e.Socket == socket && e.Level == level)
            {
                return e;
            }
        }
        return null;
    }

    public bool Equals(ShipBuild? other) => other is not null && _hash == other._hash && _entries.AsSpan().SequenceEqual(other._entries);

    public override bool Equals(object? obj) => Equals(obj as ShipBuild);

    public override int GetHashCode() => _hash;

    public override string ToString() => string.Join(" ", _entries.Select(e => $"{e.Socket}.{e.Level}:{e.Module}"));
}

/// <summary>
/// A module where it stands in a composed layout: its entry, its kind, its frame's origin in the ship's frame, and where its
/// seats, doors, parts and hull boxes start among the ship's (its own index plus these).
/// </summary>
public sealed record PlacedModule(Installed Entry, ModuleDef Module, Vector3 Origin, int FirstSeat, int FirstDoor, int FirstPart, int FirstBox);

/// <summary>A module's hold in a composed layout: the target part it opens at, its slots and its module (the index in <see cref="ShipDef.Placed"/>).</summary>
public readonly record struct ShipHold(int Part, int Slots, int Module);
