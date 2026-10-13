using System;
using System.Globalization;
using System.Numerics;

namespace Hollow.Simulation.Ships;

/// <summary>What a ship is doing, besides its motion. Some are found by the last step (outputs), some set by the zone.</summary>
[Flags]
public enum ShipFlags : byte
{
    None = 0,

    /// <summary>Resting on its skids on ground it may land on (the last step found it).</summary>
    Landed = 1,

    /// <summary>The hull's health ran out: a wreck (the zone replaces it with its loot).</summary>
    Destroyed = 2,

    /// <summary>Climbed above the ceiling: parked at its orbit marker over the zone, out of the physics (GROUND.md §6.6).</summary>
    InOrbit = 4,

    /// <summary>Boosting (the last step).</summary>
    Boosting = 8,

    /// <summary>The engines ran this step (a pilot in the seat, fuel in the tank).</summary>
    EnginesOn = 16,

    /// <summary>Flight assist was off in the last step's input.</summary>
    AssistOff = 32,

    /// <summary>Pushed back from the edge of the zone's ground this step.</summary>
    AtEdge = 64,

    /// <summary>In the sea.</summary>
    Wet = 128,
}

/// <summary>Buttons of a pilot's tick of input.</summary>
[Flags]
public enum ShipButtons : byte
{
    None = 0,

    /// <summary>Boost is held: a boost starts whenever one is ready.</summary>
    Boost = 1,

    /// <summary>Flight assist off (the pilot's toggle, sent every tick).</summary>
    AssistOff = 2,
}

/// <summary>
/// One tick of a pilot's controls (GAME_DESIGN.md §6): the stick (mouse pitch and yaw, Q/E roll), the throttle lever
/// (W/S), strafe (A/D), up and down (Space/Ctrl), boost and flight assist. Values are −1 … 1; the client builds it from
/// the same quantised command the server receives, so both step identically.
/// </summary>
public struct ShipInput
{
    /// <summary>Stick: + nose up, + nose right, + roll right (clockwise seen from behind).</summary>
    public float Pitch;
    public float Yaw;
    public float Roll;

    /// <summary>The throttle lever: + forward, − reverse.</summary>
    public float Throttle;

    /// <summary>+ right; + up.</summary>
    public float Strafe;
    public float Vertical;

    public ShipButtons Buttons;

    /// <summary>The engines run: someone is in the seat (set by the zone, never from the network).</summary>
    public bool Engines;

    public readonly bool Has(ShipButtons button) => (Buttons & button) != 0;

    /// <summary>Anything that asks the ship to move (wakes a resting ship, lifts a landed one).</summary>
    public readonly bool WantsMotion => Engines && (Throttle != 0 || Strafe != 0 || Vertical > 0 || Has(ShipButtons.Boost) ||
        (Has(ShipButtons.AssistOff) && (Pitch != 0 || Yaw != 0 || Roll != 0 || Vertical != 0)));
}

/// <summary>
/// Everything a ship's next step depends on besides its input and the static world (GROUND.md §6.6). The step is a
/// pure function of this, so a pilot's client rewinds to a state the server sent and replays its inputs exactly, as it
/// does for its character.
/// </summary>
public struct ShipState : IEquatable<ShipState>
{
    /// <summary>The centre of mass, in the zone frame (metres).</summary>
    public Vector3 Position;

    /// <summary>From the ship's frame (+Z nose, +Y up, +X left) to the zone frame.</summary>
    public Quaternion Rotation;

    /// <summary>Metres per second, in the zone frame.</summary>
    public Vector3 Velocity;

    /// <summary>Radians per second about the ship's own x (+ nose down), y (+ nose left) and z (+ roll right) axes.</summary>
    public Vector3 AngularVelocity;

    public float Health;

    /// <summary>Units of fuel in the tank.</summary>
    public float Fuel;

    /// <summary>Ticks of boost left, and of cooling down after it.</summary>
    public ushort BoostTicks;
    public ushort CooldownTicks;

    /// <summary>Ticks in a row it has rested; from <see cref="ShipMotor.SleepTicks"/> on it sleeps and costs nothing.</summary>
    public ushort RestTicks;

    public ShipFlags Flags;

    // Outputs of the last step, for the views and the HUD (never read by the step).

    /// <summary>Thrust the engines gave last step, as fractions of each axis's: x + left, y + up, z + forward (boost above 1).</summary>
    public Vector3 Thrust;

    public readonly bool Has(ShipFlags flag) => (Flags & flag) != 0;

    public readonly bool Asleep => RestTicks >= ShipMotor.SleepTicks;

    /// <summary>The model's pivot (on the ground between the skids) for a centre of mass offset in the ship's frame.</summary>
    public readonly Vector3 Origin(Vector3 centreOfMass) => Position - Vector3.Transform(centreOfMass, Rotation);

    /// <summary>A point of the ship's frame (relative to the pivot) in the zone frame.</summary>
    public readonly Vector3 PointOf(Vector3 local, Vector3 centreOfMass) => Position + Vector3.Transform(local - centreOfMass, Rotation);

    public readonly Vector3 Forward => Vector3.Transform(Vector3.UnitZ, Rotation);

    public readonly Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);

    /// <summary>The ship's right (its −X).</summary>
    public readonly Vector3 Right => Vector3.Transform(-Vector3.UnitX, Rotation);

    // What the next step reads (outputs aside): the client's prediction compares these.
    public readonly bool Equals(ShipState o) =>
        Position.Equals(o.Position) && Rotation.Equals(o.Rotation) && Velocity.Equals(o.Velocity) && AngularVelocity.Equals(o.AngularVelocity) &&
        Health.Equals(o.Health) && Fuel.Equals(o.Fuel) && BoostTicks == o.BoostTicks && CooldownTicks == o.CooldownTicks &&
        RestTicks == o.RestTicks && Flags == o.Flags;

    public override readonly bool Equals(object? obj) => obj is ShipState s && Equals(s);

    public override readonly int GetHashCode() => HashCode.Combine(Position, Rotation, Velocity, AngularVelocity, Health, Fuel, RestTicks, Flags);

    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"at ({Position.X:0.00}, {Position.Y:0.00}, {Position.Z:0.00}) moving ({Velocity.X:0.00}, {Velocity.Y:0.00}, {Velocity.Z:0.00}) " +
        $"{Health:0} hull {Fuel:0.0} fuel rest {RestTicks} {Flags}");
}

/// <summary>What a ship's step did, besides the new state.</summary>
public struct ShipEvents
{
    /// <summary>Hull damage from impacts this step, and the hardest impact's speed into what it hit.</summary>
    public float CrashDamage;
    public float ImpactSpeed;

    /// <summary>The skids touched down this step (were off the ground before), at this speed.</summary>
    public bool Touchdown;
    public float TouchdownSpeed;

    /// <summary>Crossed the ceiling this step (the zone moves it to orbit).</summary>
    public bool ReachedCeiling;

    /// <summary>A boost started.</summary>
    public bool BoostStarted;

    /// <summary>The tank ran dry this step.</summary>
    public bool OutOfFuel;
}
