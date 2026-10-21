using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Ships;

/// <summary>What a seat's occupant does (ships.json <c>seats</c>' role, IRON_WAKE.md §5.2).</summary>
public enum SeatRole : byte
{
    /// <summary>Flies the ship: the flight controls ride in the occupant's input (<c>helm</c>).</summary>
    Helm,

    /// <summary>Rides (<c>passenger</c>): nothing more, unless the seat works the doors or the lights.</summary>
    Passenger,

    /// <summary>Works a station (<c>station:&lt;id&gt;</c>, a battery of the data file, IRON_WAKE.md §6): its controls are IW3's.</summary>
    Station,
}

/// <summary>Who may sit in a seat (ships.json <c>seats</c>' <c>sitters</c>).</summary>
public enum SeatSitters : byte
{
    /// <summary>Anyone who reaches it.</summary>
    Anyone,

    /// <summary>As the ship's doors: anyone while the ship is unlocked, its crew (those on its list) while it is locked.</summary>
    Unlocked,

    /// <summary>The ship's crew alone: those on its list, authorised at its helm (IRON_WAKE.md §5.2).</summary>
    Crew,
}

/// <summary>What a seat's occupant works besides its role's controls (ships.json <c>seats</c>' <c>works</c>).</summary>
[Flags]
public enum SeatWorks : byte
{
    None = 0,

    /// <summary>The ship's doors, from the seat (the keys' doors radial).</summary>
    Doors = 1,

    /// <summary>Its lights (IW6).</summary>
    Lights = 2,
}

/// <summary>When a ship's door may open (ships.json <c>doors</c>' <c>opens</c>); it shuts at any time.</summary>
public enum DoorOpens : byte
{
    /// <summary>Any time (the Iron Wake's airlock).</summary>
    Always,

    /// <summary>The ship at rest (<c>ShipSystem.ShipStill</c>: the Tin Finch's hatch).</summary>
    Still,

    /// <summary>
    /// The ship at rest, or slower than <see cref="ShipDoorDef.SlowSpeed"/> within <see cref="ShipDoorDef.SlowHeight"/> of the
    /// ground (IRON_WAKE.md §5.3: the Iron Wake's ramp).
    /// </summary>
    Slow,
}

/// <summary>How a door's parts move with its progress (ships.json <c>doors</c>' <c>ease</c>).</summary>
public enum DoorEase : byte
{
    Linear,

    /// <summary>Slow out of the stops and into them (smoothstep): a hydraulic ramp's release and lock.</summary>
    InOut,
}

/// <summary>
/// What a ship's part with health takes and costs to mend (ships.json <c>health</c> on a door or a part, IRON_WAKE.md §5.5):
/// its most health, the building grade whose protection it has (building.json's grades: what reaches it of each kind of
/// damage), which of an explosive's tables a blast takes it by (its doors' by that grade, as an armoured door; or its
/// pieces'), and the hammer's repair: the material and what mending it from nothing costs (a hit of the hammer restores
/// building.json's <c>repairPerHit</c>, for its share of that).
/// </summary>
public sealed class ShipPartHealth
{
    public float Max { get; init; }

    /// <summary>The building grade's key whose protection it has (resolved once the building's data is read).</summary>
    public string ProtectionKey { get; init; } = "";

    public GradeDef Protection { get; internal set; } = new();

    /// <summary>A blast takes it by the explosive's doors' table (an armoured door), else by its pieces'.</summary>
    public bool DoorTable { get; init; }

    public ItemDef RepairMaterial { get; init; } = null!;

    public int RepairFull { get; init; }
}

/// <summary>
/// One seat of a ship (ships.json <c>seats</c>, IRON_WAKE.md §5.2): where its occupant sits (a socket of the data file,
/// carried by its part: a gunner's seat turns with the turret), their eye, where they stand up (in order), whether they
/// can be shot there, who may sit, what they do there, the verb that sits (its words: board, take the helm, man, sit in),
/// and the door it is reached through (the Tin Finch's hatch: it shuts behind the one who sits, and opens to let them
/// out). Its index is its number on the wire and in saves.
/// </summary>
public sealed class ShipSeatDef
{
    /// <summary>The verbs that sit (the seat capability's, interactions.json): each seat names one, its words.</summary>
    public static readonly string[] Verbs = { "board", "take-helm", "man", "sit", "fly", "drive" };

    public int Index { get; init; }

    public string Key { get; init; } = "";

    /// <summary>What the prompt calls it ("Forward battery"; the ship's own name when the data gives none).</summary>
    public string Name { get; init; } = "";

    public SeatRole Role { get; init; }

    /// <summary>The data file's station a station's seat works (its id), or empty.</summary>
    public string Station { get; init; } = "";

    /// <summary>Where the seated character is (its <c>Position</c>), in the ship's frame at rest, and the part that carries it.</summary>
    public ShipSocket At { get; init; }

    /// <summary>The seated eye: the camera there (IRON_WAKE.md §9.7), carried as the seat is.</summary>
    public ShipSocket Eye { get; init; }

    /// <summary>
    /// Where the occupant stands up, in order of preference: aboard a ship people walk in, points on its decks (the feet,
    /// tested in its space); on one they only sit in, points on the ground below (GROUND.md §6.7).
    /// </summary>
    public IReadOnlyList<ShipSocket> Exits { get; init; } = Array.Empty<ShipSocket>();

    /// <summary>The occupant can be shot and caught in blasts there (IRON_WAKE.md IW4: gunners); else the cabin shelters them.</summary>
    public bool Exposed { get; init; }

    public SeatSitters Sitters { get; init; }

    public SeatWorks Works { get; init; }

    /// <summary>The interaction verb that sits here (interactions.json's index).</summary>
    public int Verb { get; init; } = -1;

    /// <summary>That verb's key (one of <see cref="Verbs"/>).</summary>
    public string VerbKey { get; init; } = "";

    /// <summary>The door it is reached through (an index in <see cref="ShipDef.Doors"/>), or −1.</summary>
    public int Door { get; init; } = -1;

    public bool Flies => Role == SeatRole.Helm;

    public override string ToString() => Key;
}

/// <summary>
/// One door of a ship (ships.json <c>doors</c>, IRON_WAKE.md §5.3): a part with a progress, 0 shut to 1 open, moved over
/// <see cref="Seconds"/> by the ship's rules. It drives its parts of the data file (their states follow its progress, by
/// its ease), which its space and its view pose; a ship without an interior has none, and its view samples the model's
/// <see cref="Clip"/> at the progress instead. It opens when <see cref="Opens"/> allows, to anyone the ship's lock lets
/// through; it shuts any time. With health it breaks as an armoured door does and jams open until mended.
/// </summary>
public sealed class ShipDoorDef
{
    /// <summary>The ramp's speed and height limit for <see cref="DoorOpens.Slow"/> (IRON_WAKE.md §5.3).</summary>
    public const float SlowSpeed = 5f, SlowHeight = 30f;

    public int Index { get; init; }

    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>The interior's parts it drives (each from its closed state at 0 to its open one at 1).</summary>
    public IReadOnlyList<int> Parts { get; init; } = Array.Empty<int>();

    /// <summary>The model's clip the view samples by the progress (a ship without an interior), or empty.</summary>
    public string Clip { get; init; } = "";

    public float Seconds { get; init; } = 1;

    public DoorOpens Opens { get; init; }

    public DoorEase Ease { get; init; }

    public ShipPartHealth? Health { get; init; }

    /// <summary>How far it goes in a tick (its progress a second over the tick rate).</summary>
    public float Rate => 1f / MathF.Max(Seconds * Movement.CharacterMotor.TickRate, 1);

    /// <summary>The state of each of its parts at a progress (its ease).</summary>
    public float PartState(float progress) => Ease == DoorEase.InOut ? progress * progress * (3 - 2 * progress) : progress;

    /// <summary>
    /// Whether it may open now, by its rule (the server's and every client's): always; the ship at rest; at rest, or slow and
    /// low (<paramref name="slow"/>, the server's word).
    /// </summary>
    public Outcome Rule(bool still, bool slow) => Opens switch
    {
        DoorOpens.Always => Outcome.Done,
        DoorOpens.Still => still ? Outcome.Done : OutcomeCode.ShipMoving,
        _ => slow || still ? Outcome.Done : OutcomeCode.TooFast,
    };

    public override string ToString() => Key;
}

/// <summary>
/// A target part of a ship (ships.json <c>parts</c>, GROUND_SYSTEMS.md §3.1: <c>TargetRef.Part</c>): what the interact
/// key, a shot or a blast meets, with its own name, capabilities and verbs. Part 0 is the hull, the rest of the ship (a
/// backdrop to the interact key on a ship people walk in, the hull's health to a shot). A part is aimed at by a ball round
/// a point of the ship's frame (<see cref="At"/>, carried by its part of the data file: a gunner's grip turns with its
/// turret) and by the convex pieces of some of the data file's parts (<see cref="Pieces"/>: the ramp's leaves, a turret).
/// It may name the seat it sits in, the door it works, the door it is reached through, and its own health (a battery's).
/// The list's order is the wire's number (a byte), append only.
/// </summary>
public sealed class ShipTargetPart
{
    public int Index { get; init; }

    public string Key { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>A ball round a point: its centre (carried by its part) and radius (0: none).</summary>
    public ShipSocket At { get; init; }

    public float Radius { get; init; }

    /// <summary>The data file's parts whose pieces are this part (none: only the ball).</summary>
    public IReadOnlyList<int> Pieces { get; init; } = Array.Empty<int>();

    /// <summary>Its capabilities (capabilities.json), beside the ship's own (owned, ship).</summary>
    public CapabilitySet Capabilities { get; init; }

    /// <summary>The seat it sits in, the door it works, the door it is reached through (−1: none).</summary>
    public int Seat { get; init; } = -1;

    public int Door { get; init; } = -1;

    public int Through { get; init; } = -1;

    /// <summary>Its own health (a battery's); a door's part takes its door's.</summary>
    public ShipPartHealth? Health { get; init; }

    /// <summary>The station whose seat it sits in (its magazine opens here, IRON_WAKE.md §6.5), or −1.</summary>
    public int Station { get; internal set; } = -1;

    public bool HasBall => Radius > 0;

    public override string ToString() => Key;
}

/// <summary>Which of a ship's drives a nozzle is (its plume grows with that axis's thrust).</summary>
public enum NozzleDrive : byte
{
    Main,
    Aux,
    Lift,
    Rcs,
}

/// <summary>A nozzle of a ship (ships.json <c>nozzles</c>): where it is, which drive, its plume's width and length at full thrust.</summary>
public readonly record struct ShipNozzle(Vector3 At, NozzleDrive Drive, float Width, float Length);

/// <summary>Which power a lamp burns with (ships.json <c>lights</c>, IRON_WAKE.md §9.4).</summary>
public enum LightPower : byte
{
    /// <summary>While the engines run, or the helm's switch is on.</summary>
    Ship,

    /// <summary>Always: the dim red emergency lamps.</summary>
    Emergency,
}

/// <summary>
/// A lamp of a ship (ships.json <c>lights</c>, IRON_WAKE.md §9.4): the data file's fixtures, or a point; how it shines, its
/// colour, energy and range, whether it casts shadows, the power it needs, and a spot's cone (degrees, its half angle as
/// Godot's <c>spot_angle</c>). The ship views draw them (IW6).
/// </summary>
public sealed record ShipLight(string Key, Vector3 At, Vector3 Direction, bool Spot, Vector3 Colour, float Energy, float Range, bool Shadows, LightPower Power,
    float Angle = 50f);

/// <summary>
/// A rotor as the views draw it (ships.json <c>rotors</c>, docs/MINICOPTER.md §8): the model's node that turns (its blades
/// under it), the axis it turns about in the model's frame, its turns a minute at full spool, the node of its blurred disc
/// shown in place of the blades as it speeds up (or empty), and whether its downwash raises dust from the ground below.
/// Presentation only: the step never reads it.
/// </summary>
public sealed record ShipRotorLook(string Node, Vector3 Axis, float Rpm, string Blur, bool Wash);

