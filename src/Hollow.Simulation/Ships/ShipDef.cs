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

    /// <summary>
    /// The pilot's seat (where the seated character's feet go), the pilot's eye, the hatch's middle, the fuel cap, the
    /// cargo (reached through the open hatch).
    /// </summary>
    public Vector3 Seat { get; init; }

    public Vector3 Eye { get; init; }

    public Vector3 Hatch { get; init; }

    public Vector3 FuelCap { get; init; }

    public Vector3 Cargo { get; init; }

    private Vector3[]? _parts;

    /// <summary>A part's point (<see cref="ShipParts"/>, by its number): the hatch, the fuel cap, the cargo; the centre for the hull.</summary>
    public Vector3 PartPoint(byte part)
    {
        _parts ??= new[] { CentreOfMass, Hatch, FuelCap, Cargo };
        return part < _parts.Length ? _parts[part] : CentreOfMass;
    }

    /// <summary>Where a pilot steps out, in order of preference (on the ground below each point).</summary>
    public IReadOnlyList<Vector3> Exits { get; init; } = Array.Empty<Vector3>();

    /// <summary>Exhausts and nozzles, for the client's glow (presentation only).</summary>
    public IReadOnlyList<Vector3> MainNozzles { get; init; } = Array.Empty<Vector3>();

    public IReadOnlyList<Vector3> AuxNozzles { get; init; } = Array.Empty<Vector3>();

    public IReadOnlyList<Vector3> LiftNozzles { get; init; } = Array.Empty<Vector3>();

    public IReadOnlyList<Vector3> RcsNozzles { get; init; } = Array.Empty<Vector3>();

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

/// <summary>
/// A ship's parts as targets (GROUND_SYSTEMS.md §3.1, §5): <see cref="Targets.TargetRef.Part"/> names one. The hull is the
/// whole ship; the hatch is where it is boarded, opened and locked; the fuel cap where it is refuelled; the cargo is reached
/// through the open hatch.
/// </summary>
public static class ShipParts
{
    public const byte Hull = 0, Hatch = 1, FuelCap = 2, Cargo = 3;

    /// <summary>The parts the interact key looks for, after the hull.</summary>
    public const byte Count = 4;

    // How near a part the aim must pass, by its number: the hatch's frame, the cap, the cargo's opening.
    private static readonly float[] Radii = { 0, 0.7f, 0.45f, 0.45f };

    public static float Radius(byte part) => part < Radii.Length ? Radii[part] : 0;
}
