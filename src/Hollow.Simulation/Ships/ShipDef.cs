using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Ships;

/// <summary>
/// One box of a ship's hull, in the ship's frame: its centre and half extents. A skid's box is raised to its travel's end
/// for sweeps. A fragile box (a rotor's disc, docs/MINICOPTER.md §5.5) meets the world only while the ship flies: a fast
/// contact on it is a strike, shots pass through it, and a parked ship's body leaves it out.
/// </summary>
public readonly record struct HullBox(Vector3 Centre, Vector3 HalfExtents, bool Skid, bool Fragile = false);

/// <summary>Per-axis turning: the most rate flight assist asks for, and the thrusters' angular acceleration (radians).</summary>
public readonly record struct TurnAxis(float MaxRate, float Acceleration);

/// <summary>
/// A kind of ship (ships.json, GROUND.md §6.6): its flight model, hull, skids, fuel, cargo and sockets. Every vector is
/// in the ship's own frame, the model's: +Z the nose, +Y up, +X the left side, metres from the pivot (on the ground,
/// between the skids). Forces in newtons, speeds in m/s, angles in radians (degrees in the file).
/// </summary>
public sealed class ShipDef
{
    public string Key { get; init; } = "";

    /// <summary>The item the ship is crafted as (its ID names the kind on the wire and in saves).</summary>
    public ItemDef Item { get; init; } = null!;

    public ushort Id => Item.Id;

    public string Name => Item.Name;

    public float Mass { get; internal set; }

    /// <summary>Moments of inertia about the ship's x (pitch), y (yaw) and z (roll) axes through the centre of mass.</summary>
    public Vector3 Inertia { get; internal set; }

    public Vector3 CentreOfMass { get; internal set; }

    public float Health { get; init; }

    /// <summary>Hull damage taken per point of a weapon's damage, by kind.</summary>
    public float MeleeDamage { get; init; }

    public float ArrowDamage { get; init; }

    public float BulletDamage { get; init; }

    public float ExplosiveDamage { get; init; }

    /// <summary>The hull's share of a blow by how it came (its defence in the damage pipeline, GROUND.md §6.4.2).</summary>
    public float Share(Combat.DamageDelivery delivery) => delivery switch
    {
        Combat.DamageDelivery.Arrow => ArrowDamage,
        Combat.DamageDelivery.Bullet => BulletDamage,
        Combat.DamageDelivery.Blast => ExplosiveDamage,
        _ => MeleeDamage,
    };

    /// <summary>
    /// How it moves through a tick (ships.json <c>motor</c>, <see cref="ShipMotors"/>, VEHICLES.md §9.2): flight by default, or
    /// on wheels (<see cref="GroundMotor"/>).
    /// </summary>
    public IShipMotor Motor { get; init; } = FlightMotor.Instance;

    /// <summary>How a vehicle on wheels drives (ships.json <c>ground</c>), or null: it doesn't.</summary>
    public GroundDef? Ground { get; init; }

    /// <summary>Its wheels (the kind's, then its modules', each where its socket puts it).</summary>
    public IReadOnlyList<WheelDef> Wheels { get; internal set; } = Array.Empty<WheelDef>();

    /// <summary>Its engines' power in watts (ships.json <c>ground.power</c> and its modules' <c>power</c>): a ground motor's drive.</summary>
    public float EnginePower { get; internal set; }

    /// <summary>What its engines do each tick (ships.json <c>drive</c>, <see cref="ShipDrives"/>; by default thrusters).</summary>
    public IShipDrive Drive { get; internal set; } = ShipThrusters.Instance;

    /// <summary>The control scheme a client reads its helm's keys by when none is named (the Tin Finch's flight).</summary>
    public const string DefaultControls = "flight";

    /// <summary>
    /// How a pilot's keys and mouse fly it (ships.json <c>controls</c>): a key of the client's control schemes, which turn
    /// the keyboard into the same <see cref="ShipInput"/> (presentation only; the step never reads it).
    /// </summary>
    public string Controls { get; init; } = DefaultControls;

    /// <summary>Thrust along each axis: forward (main engine), back (retro), sideways, up (lift nozzles), down.</summary>
    public float MainThrust { get; init; }

    public float RetroThrust { get; init; }

    public float LateralThrust { get; init; }

    public float LiftThrust { get; init; }

    public float DownThrust { get; init; }

    /// <summary>What flight assist holds at full stick or throttle.</summary>
    public float ForwardSpeed { get; init; }

    public float ReverseSpeed { get; init; }

    public float LateralSpeed { get; init; }

    public float UpSpeed { get; init; }

    public float DownSpeed { get; init; }

    /// <summary>Boost: the main engine's thrust times this, flight assist's cap raised to <see cref="BoostSpeed"/>.</summary>
    public float BoostThrust { get; init; } = 1;

    public float BoostSpeed { get; init; }

    public float BoostSeconds { get; init; }

    public float BoostCooldownSeconds { get; init; }

    public TurnAxis Pitch { get; init; }

    public TurnAxis Yaw { get; init; }

    public TurnAxis Roll { get; init; }

    /// <summary>Drag areas (Cd × m²) facing forward, sideways and from above; and the turning damping at sea-level air (1/s).</summary>
    public float DragForward { get; init; }

    public float DragSide { get; init; }

    public float DragTop { get; init; }

    public float TurnDrag { get; init; }

    /// <summary>
    /// Flight assist's level hold (ships.json <c>assist.level</c>, IRON_WAKE.md §5.4, IW3): the most pitch and roll from
    /// level it lets the ship take, in radians, and how fast it brings the ship back toward level with the stick centred
    /// (radians a second per radian). 0: none (the Tin Finch flies free).
    /// </summary>
    public float LevelPitch { get; init; }

    public float LevelRoll { get; init; }

    public float LevelReturn { get; init; } = 0.6f;

    /// <summary>
    /// The sea (IRON_WAKE.md §5.4): the band round the centre of mass the hull fills, below and above it (metres; the
    /// Tin Finch's 1.2 each way), and the water its hull displaces when wholly under (m³; by default its non-skid boxes').
    /// </summary>
    public float WaterBelow { get; init; } = 1.2f;

    public float WaterAbove { get; init; } = 1.2f;

    public float WaterVolume { get; internal set; }

    /// <summary>
    /// A hull that floats on its own shape (ships.json <c>buoyancy</c>: a boat's), or null: the band above, the flight
    /// assist holding it level.
    /// </summary>
    public ShipBuoyancy? Buoyancy { get; init; }

    /// <summary>The model catalogue's key the views draw it with (ships.json <c>model</c>; by default its own key).</summary>
    public string Model { get; init; } = "";

    /// <summary>The inside people walk in (ships.json <c>interior</c>, IRON_WAKE.md §4.3), or null: a ship people only sit in.</summary>
    public ShipInterior? Interior { get; init; }

    /// <summary>The steepest ground the ship rests on as landed.</summary>
    public float MaxLandingSlope { get; init; }

    /// <summary>Flight assist's landing descent near the ground: at least this many m/s, plus this much per metre of height.</summary>
    public float LandingDescent { get; init; } = 1.5f;

    public float LandingDescentPerMetre { get; init; } = 0.25f;

    /// <summary>The skids' contact points (the pads' undersides), springs and friction.</summary>
    public IReadOnlyList<Vector3> SkidPoints { get; init; } = Array.Empty<Vector3>();

    public float SkidStiffness { get; init; }

    public float SkidDamping { get; init; }

    public float SkidTravel { get; init; }

    public float SkidFriction { get; init; }

    /// <summary>Crash damage: impacts slower than this do none; above, (speed − safe)² × scale; the pilot takes this share.</summary>
    public float CrashSafeSpeed { get; init; }

    public float CrashScale { get; init; }

    public float CrashPilotShare { get; init; }

    /// <summary>
    /// Rust's crash rule instead (ships.json crash <c>fatal</c>, docs/MINICOPTER.md M10), when above 0: an impact faster
    /// than the safe speed costs the hull's whole times its place between safe and <c>fatal</c>, at least
    /// <see cref="CrashLeast"/> of it, and <see cref="CrashTipped"/> times that with the ship tipped past 60°.
    /// </summary>
    public float CrashFatalSpeed { get; init; }

    public float CrashLeast { get; init; }

    public float CrashTipped { get; init; } = 1;

    /// <summary>The share of the hull a strike on a fragile box costs (ships.json crash <c>strike</c>).</summary>
    public float StrikeShare { get; init; }

    /// <summary>
    /// The hull damage an impact at <paramref name="speed"/> does, <paramref name="upness"/> the cosine of the ship's tilt
    /// from level: the quadratic rule, or Rust's (<see cref="CrashFatalSpeed"/>).
    /// </summary>
    public float CrashDamage(float speed, float upness)
    {
        if (speed <= CrashSafeSpeed)
        {
            return 0;
        }
        if (CrashFatalSpeed > CrashSafeSpeed)
        {
            float share = MathF.Max(Math.Clamp((speed - CrashSafeSpeed) / (CrashFatalSpeed - CrashSafeSpeed), 0, 1), CrashLeast);
            return share * Health * (upness < 0.5f ? CrashTipped : 1);
        }
        float over = speed - CrashSafeSpeed;
        return over * over * CrashScale;
    }

    /// <summary>Whether a point of the ship's frame (from the pivot) lies on a fragile hull box (or within 10 cm of one).</summary>
    public bool FragileAt(Vector3 local)
    {
        foreach (HullBox box in Hull)
        {
            if (!box.Fragile)
            {
                continue;
            }
            Vector3 d = Vector3.Abs(local - box.Centre);
            if (d.X <= box.HalfExtents.X + 0.1f && d.Y <= box.HalfExtents.Y + 0.1f && d.Z <= box.HalfExtents.Z + 0.1f)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether the hull has a fragile box at all.</summary>
    public bool HasFragile { get; private set; }

    /// <summary>
    /// Its engines stop when nobody awake is at its helm (ships.json <c>needsPilot</c>, Rust's minicopter: docs/MINICOPTER.md
    /// M12); without it they run on as the pilot left them (the Tin Finch's, which holds itself where it was).
    /// </summary>
    public bool NeedsPilot { get; init; }

    /// <summary>The fuel (an item), the tank, and units burnt a second at idle and at full thrust; boost burns this many times more.</summary>
    public ItemDef Fuel { get; init; } = null!;

    public float FuelCapacity { get; internal set; }

    public float FuelIdle { get; init; }

    public float FuelFull { get; init; }

    public float FuelBoost { get; init; } = 1;

    /// <summary>The fuel a ship placed from its item carries (ships.json fuel.placed: its recipe's first tank).</summary>
    public float PlacedFuel { get; init; }

    public int CargoSlots { get; init; }

    public IReadOnlyList<HullBox> Hull { get; internal set; } = Array.Empty<HullBox>();

    // ---------------------------------------------------------------- seats, doors and parts (IRON_WAKE.md §5.1)

    /// <summary>Its seats (ships.json <c>seats</c>, §5.2): by index, the wire's and the saves' number.</summary>
    public IReadOnlyList<ShipSeatDef> Seats { get; internal set; } = Array.Empty<ShipSeatDef>();

    /// <summary>The seat that flies it (the one with the helm's role; the content check wants exactly one).</summary>
    public ShipSeatDef Helm => _helm ??= FirstHelm();

    private ShipSeatDef? _helm;

    private ShipSeatDef FirstHelm()
    {
        foreach (ShipSeatDef seat in Seats)
        {
            if (seat.Flies)
            {
                return seat;
            }
        }
        return Seats.Count > 0 ? Seats[0] : new ShipSeatDef();
    }

    /// <summary>Its doors (ships.json <c>doors</c>, §5.3): by index, the wire's and the saves' number.</summary>
    public IReadOnlyList<ShipDoorDef> Doors { get; internal set; } = Array.Empty<ShipDoorDef>();

    /// <summary>
    /// Its target parts (ships.json <c>parts</c>, GROUND_SYSTEMS.md §3.1): <c>TargetRef.Part</c> is an index here, 0 the hull.
    /// </summary>
    public IReadOnlyList<ShipTargetPart> Parts { get; internal set; } = Array.Empty<ShipTargetPart>();

    /// <summary>Its crewed batteries (IRON_WAKE.md §6, IW3): by index, the wire's and the saves' number.</summary>
    public IReadOnlyList<ShipStationDef> Stations { get; init; } = Array.Empty<ShipStationDef>();

    /// <summary>The station a seat works, or null.</summary>
    public ShipStationDef? StationOfSeat(int seat)
    {
        foreach (ShipStationDef s in Stations)
        {
            if (s.Seat == seat)
            {
                return s;
            }
        }
        return null;
    }

    /// <summary>The target part its cargo is opened at (the one with the container capability), or 0: none.</summary>
    public int CargoPart { get; init; }

    private int[]? _partOfPieces;

    /// <summary>
    /// The target part the pieces of the data file's part <paramref name="dataPart"/> belong to (a hit on the ramp's leaf is
    /// the ramp's, on a turret its battery's), else 0, the hull.
    /// </summary>
    public int PartOfPieces(int dataPart)
    {
        if (_partOfPieces == null)
        {
            var map = new int[Interior?.Parts.Count ?? 0];
            foreach (ShipTargetPart part in Parts)
            {
                foreach (int piece in part.Pieces)
                {
                    if (piece < map.Length)
                    {
                        map[piece] = part.Index;
                    }
                }
            }
            _partOfPieces = map;
        }
        return dataPart > 0 && dataPart < _partOfPieces.Length ? _partOfPieces[dataPart] : 0;
    }

    /// <summary>The target part a seat is sat in at (whose health is its station's), or 0.</summary>
    public int PartOfSeat(int seat)
    {
        foreach (ShipTargetPart part in Parts)
        {
            if (part.Seat == seat)
            {
                return part.Index;
            }
        }
        return 0;
    }

    /// <summary>The door that moves an interior part (the part's state follows the door's progress, §5.3), or −1.</summary>
    public int DoorOfPart(int part)
    {
        foreach (ShipDoorDef door in Doors)
        {
            if (Contains(door.Parts, part))
            {
                return door.Index;
            }
        }
        return -1;
    }

    /// <summary>The target part a door is worked at (the first naming it), or 0.</summary>
    public int PartOfDoor(int door)
    {
        foreach (ShipTargetPart part in Parts)
        {
            if (part.Door == door)
            {
                return part.Index;
            }
        }
        return 0;
    }

    /// <summary>A target part's index by its key, or −1.</summary>
    public int PartIndex(string key)
    {
        foreach (ShipTargetPart part in Parts)
        {
            if (part.Key == key)
            {
                return part.Index;
            }
        }
        return -1;
    }

    /// <summary>A door's index by its key, or −1.</summary>
    public int DoorIndex(string key)
    {
        foreach (ShipDoorDef door in Doors)
        {
            if (door.Key == key)
            {
                return door.Index;
            }
        }
        return -1;
    }

    /// <summary>A seat's index by its key, or −1.</summary>
    public int SeatIndex(string key)
    {
        foreach (ShipSeatDef seat in Seats)
        {
            if (seat.Key == key)
            {
                return seat.Index;
            }
        }
        return -1;
    }

    /// <summary>
    /// A target part's point in the ship's frame at its parts' states: its ball's centre (carried by its part), or the
    /// middle of its pieces' bounds where they are now; the centre of mass for the hull. Where a tool aims at it.
    /// </summary>
    public Vector3 PartPoint(int part, ReadOnlySpan<float> states)
    {
        if (part <= 0 || part >= Parts.Count)
        {
            return CentreOfMass;
        }
        ShipTargetPart p = Parts[part];
        if (p.HasBall)
        {
            return Interior != null ? Interior.SocketAt(p.At, states) : p.At.Position;
        }
        if (Interior == null || p.Pieces.Count == 0)
        {
            return CentreOfMass;
        }
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (ShipPiece piece in Interior.Pieces)
        {
            if (!Contains(p.Pieces, piece.Part))
            {
                continue;
            }
            PartPose pose = Interior.PartPose(piece.Part, states);
            foreach (Vector3 point in piece.Points)
            {
                Vector3 moved = pose.Apply(point);
                min = Vector3.Min(min, moved);
                max = Vector3.Max(max, moved);
            }
        }
        return min.X <= max.X ? (min + max) / 2 : CentreOfMass;
    }

    /// <summary>A target part's point at the interior's rest (or the data's, for a ship without one).</summary>
    public Vector3 PartPoint(int part) => PartPoint(part, Interior?.Rest ?? Array.Empty<float>());

    private static bool Contains(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The nozzles (ships.json <c>nozzles</c>): where each is, its drive and its plume's size (presentation only).</summary>
    public IReadOnlyList<ShipNozzle> Nozzles { get; init; } = Array.Empty<ShipNozzle>();

    /// <summary>The lamps (ships.json <c>lights</c>, IRON_WAKE.md §9.4): data until IW6 draws them.</summary>
    public IReadOnlyList<ShipLight> Lights { get; init; } = Array.Empty<ShipLight>();

    /// <summary>
    /// The views' red, green and white flight glows at the hull's tips (ships.json <c>lights.nav</c>, true unless false: a
    /// boat has none).
    /// </summary>
    public bool NavLights { get; init; } = true;

    /// <summary>
    /// The orbital-altitude marker, in world radii above the mean surface. Kept as <c>ceiling</c> in the data for
    /// compatibility; this height is a transfer hook and presentation marker, not a flight limit.
    /// </summary>
    public float Ceiling { get; init; }

    /// <summary>The leaving height in metres over the mean radius on a world of this radius.</summary>
    public float CeilingOn(double radius) => (float)(Ceiling * radius);

    /// <summary>What a destroyed ship leaves in its wreck besides its cargo and fuel.</summary>
    public IReadOnlyList<ItemAmount> Wreck { get; init; } = Array.Empty<ItemAmount>();

    /// <summary>
    /// What mending its hull with the hammer costs (ships.json <c>repair</c>: the material and a full repair's cost; VEHICLES.md
    /// §5.5), or null: the hull isn't mended.
    /// </summary>
    public ShipRepair? HullRepair { get; init; }

    // ---------------------------------------------------------------- modules (VEHICLES.md §9.1)

    /// <summary>Where modules go on this kind (ships.json <c>sockets</c>): none for a kind that takes none.</summary>
    public IReadOnlyList<Modules.ShipSocketDef> Sockets { get; init; } = Array.Empty<Modules.ShipSocketDef>();

    /// <summary>The tallest a build may stand over the ship's origin (ships.json <c>buildHeight</c>, metres; 0: no limit).</summary>
    public float BuildHeight { get; init; }

    /// <summary>The kind this layout is of: itself for a kind as read, the kind for one composed with modules.</summary>
    public ShipDef Kind => _kind ?? this;

    private ShipDef? _kind;

    /// <summary>
    /// What its modules add to its drive, by name (modules.json <c>drive</c> summed over the build, and each module's
    /// <c>power</c> as "power"); empty for a kind as read.
    /// </summary>
    public IReadOnlyDictionary<string, float> DriveSums { get; private set; } = new Dictionary<string, float>();

    /// <summary>
    /// What a ship of this kind is made with when it is made without a build (ships.json <c>build</c>: crafted, placed, the
    /// admin's <c>ship</c>): empty for none. Read once the modules are (ModulesReader).
    /// </summary>
    public Modules.ShipBuild DefaultBuild { get; internal set; } = Modules.ShipBuild.Empty;

    /// <summary>The default build as ships.json names it: socket keys, levels, module keys.</summary>
    internal IReadOnlyList<(string Socket, int Level, string Module)> DefaultBuildKeys { get; init; } = Array.Empty<(string, int, string)>();

    /// <summary>The modules installed (empty for a kind as read): with <see cref="Kind"/>, what this layout is.</summary>
    public Modules.ShipBuild Build { get; private set; } = Modules.ShipBuild.Empty;

    /// <summary>Each installed module where it stands, in the build's order (the views draw them; empty without modules).</summary>
    public IReadOnlyList<Modules.PlacedModule> Placed { get; private set; } = Array.Empty<Modules.PlacedModule>();

    /// <summary>
    /// The target part each hull box belongs to, by the hull's order (a module's boxes its body part, a shot's part), or empty:
    /// every box the hull's.
    /// </summary>
    public IReadOnlyList<byte> HullParts { get; private set; } = Array.Empty<byte>();

    /// <summary>The target part a hull box belongs to (0: the hull).</summary>
    public byte HullPart(int box) => box >= 0 && box < HullParts.Count ? HullParts[box] : ShipParts.Hull;

    /// <summary>The modules' holds (a locker's slots, opened at its part), by index; the kind's own cargo is apart.</summary>
    public IReadOnlyList<Modules.ShipHold> Holds { get; private set; } = Array.Empty<Modules.ShipHold>();

    /// <summary>The hold opened at a target part, or −1.</summary>
    public int HoldOfPart(int part)
    {
        for (int i = 0; i < Holds.Count; i++)
        {
            if (Holds[i].Part == part)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// A copy of this kind for a build (<see cref="Modules.ShipAssembler"/>): every field the kind has, the build's own set
    /// after. A copy, so whatever a kind gains later is carried over without the assembler naming it.
    /// </summary>
    internal ShipDef CopyFor(Modules.ShipBuild build, IReadOnlyList<Modules.PlacedModule> placed, IReadOnlyList<byte> hullParts,
        IReadOnlyList<Modules.ShipHold> holds, IReadOnlyDictionary<string, float> driveSums)
    {
        var copy = (ShipDef)MemberwiseClone();
        copy._kind = Kind;
        copy._helm = null;
        copy._partOfPieces = null;
        copy.Build = build;
        copy.DriveSums = driveSums;
        copy.Drive = Drive.Compose(driveSums);
        copy.Placed = placed;
        copy.HullParts = hullParts;
        copy.Holds = holds;
        copy.DefaultBuild = Modules.ShipBuild.Empty;
        return copy;
    }

    /// <summary>The hull's extent: the half size of the box holding every hull box (for quick tests).</summary>
    public Vector3 BoundsCentre { get; internal set; }

    public Vector3 BoundsHalf { get; internal set; }

    /// <summary>A sphere about the centre of mass holding the whole hull.</summary>
    public float BoundingRadius { get; internal set; }

    internal void ComputeBounds()
    {
        if (Hull.Count == 0)
        {
            return;
        }
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        float radius = 0, volume = 0;
        foreach (HullBox box in Hull)
        {
            min = Vector3.Min(min, box.Centre - box.HalfExtents);
            max = Vector3.Max(max, box.Centre + box.HalfExtents);
            radius = MathF.Max(radius, Vector3.Distance(box.Centre, CentreOfMass) + box.HalfExtents.Length());
            if (!box.Skid)
            {
                volume += 8 * box.HalfExtents.X * box.HalfExtents.Y * box.HalfExtents.Z;
            }
        }
        foreach (Vector3 p in SkidPoints)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
            radius = MathF.Max(radius, Vector3.Distance(p, CentreOfMass));
        }
        BoundsCentre = (min + max) / 2;
        BoundsHalf = (max - min) / 2;
        BoundingRadius = radius;
        if (WaterVolume <= 0)
        {
            WaterVolume = volume;
        }
        int boxes = 0;
        foreach (HullBox box in Hull)
        {
            boxes += box.Skid ? 0 : 1;
            HasFragile |= box.Fragile;
        }
        MostContacts = Math.Max(4, boxes);
    }

    /// <summary>
    /// The most hull contacts one sweep's overlaps keep (the deepest first): one for each of its boxes that isn't a skid,
    /// at least four (IRON_WAKE.md §5.4: a fixed four held the Tin Finch's four, not a carrier's ten).
    /// </summary>
    public int MostContacts { get; private set; } = 4;

    public override string ToString() => Key;
}

/// <summary>What one full mend costs (ships.json <c>repair</c>): the material, and how much of it from nothing to whole.</summary>
public sealed record ShipRepair(ItemDef Material, int Full);

/// <summary>The hull's number among a ship's target parts (GROUND_SYSTEMS.md §3.1): 0, the rest of the ship.</summary>
public static class ShipParts
{
    public const byte Hull = 0;
}
