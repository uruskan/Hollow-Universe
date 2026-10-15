using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Ships;

/// <summary>One box of a ship's hull, in the ship's frame: its centre and half extents.</summary>
public readonly record struct HullBox(Vector3 Centre, Vector3 HalfExtents, bool Skid);

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

    public float Mass { get; init; }

    /// <summary>Moments of inertia about the ship's x (pitch), y (yaw) and z (roll) axes through the centre of mass.</summary>
    public Vector3 Inertia { get; init; }

    public Vector3 CentreOfMass { get; init; }

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

    /// <summary>The fuel (an item), the tank, and units burnt a second at idle and at full thrust; boost burns this many times more.</summary>
    public ItemDef Fuel { get; init; } = null!;

    public float FuelCapacity { get; init; }

    public float FuelIdle { get; init; }

    public float FuelFull { get; init; }

    public float FuelBoost { get; init; } = 1;

    /// <summary>The fuel a ship placed from its item carries (ships.json fuel.placed: its recipe's first tank).</summary>
    public float PlacedFuel { get; init; }

    public int CargoSlots { get; init; }

    public IReadOnlyList<HullBox> Hull { get; init; } = Array.Empty<HullBox>();

    // ---------------------------------------------------------------- seats, doors and parts (IRON_WAKE.md §5.1)

    /// <summary>Its seats (ships.json <c>seats</c>, §5.2): by index, the wire's and the saves' number.</summary>
    public IReadOnlyList<ShipSeatDef> Seats { get; init; } = Array.Empty<ShipSeatDef>();

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
    public IReadOnlyList<ShipDoorDef> Doors { get; init; } = Array.Empty<ShipDoorDef>();

    /// <summary>
    /// Its target parts (ships.json <c>parts</c>, GROUND_SYSTEMS.md §3.1): <c>TargetRef.Part</c> is an index here, 0 the hull.
    /// </summary>
    public IReadOnlyList<ShipTargetPart> Parts { get; init; } = Array.Empty<ShipTargetPart>();

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
    /// The leaving height in world radii over the ground (docs/WHOLE_WORLD.md decision 4: 2, 4.8 km on the first world):
    /// above it the ship reaches orbit (GROUND.md §6.6).
    /// </summary>
    public float Ceiling { get; init; }

    /// <summary>The leaving height in metres over the mean radius on a world of this radius.</summary>
    public float CeilingOn(double radius) => (float)(Ceiling * radius);

    /// <summary>What a destroyed ship leaves in its wreck besides its cargo and fuel.</summary>
    public IReadOnlyList<ItemAmount> Wreck { get; init; } = Array.Empty<ItemAmount>();

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

/// <summary>The hull's number among a ship's target parts (GROUND_SYSTEMS.md §3.1): 0, the rest of the ship.</summary>
public static class ShipParts
{
    public const byte Hull = 0;
}
