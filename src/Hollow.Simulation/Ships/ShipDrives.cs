using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Ships;

/// <summary>
/// What a drive sees when it decides a tick's thrust and turn (<see cref="IShipDrive"/>): the ship's attitude and motion
/// in its own frame, the air and the sea about it, and the step's switches. ShipMotor builds it once a tick, after the
/// boost and before the substeps.
/// </summary>
public readonly struct ShipDriveContext
{
    public ShipDriveContext(in ShipContext world, Quaternion rotation, Vector3 up, Vector3 velocityLocal, Vector3 gravityLocal,
        float density, float airRatio, float overGround, float submerged, bool engines, bool boosting, bool assist, double water = 0)
    {
        Water = water;
        World = world;
        Rotation = rotation;
        Inverse = Quaternion.Conjugate(rotation);
        Up = up;
        VelocityLocal = velocityLocal;
        GravityLocal = gravityLocal;
        Density = density;
        AirRatio = airRatio;
        OverGround = overGround;
        Submerged = submerged;
        Engines = engines;
        Boosting = boosting;
        Assist = assist;
    }

    /// <summary>Where the step happens: the frame, the ground, gravity, the air, the static world.</summary>
    public readonly ShipContext World;

    /// <summary>From the ship's frame to the zone's, and back.</summary>
    public readonly Quaternion Rotation;

    public readonly Quaternion Inverse;

    /// <summary>The zone's up at the ship.</summary>
    public readonly Vector3 Up;

    /// <summary>The ship's velocity and gravity's acceleration, in the ship's frame.</summary>
    public readonly Vector3 VelocityLocal;

    public readonly Vector3 GravityLocal;

    /// <summary>The air's density at the ship's height (kg/m³), and its share of the density at the mean radius.</summary>
    public readonly float Density;

    public readonly float AirRatio;

    /// <summary>The centre of mass's height over the ground under it (m).</summary>
    public readonly float OverGround;

    /// <summary>
    /// How much of the hull is in the water, 0 … 1: its columns' share for a hull that floats on its shape (ships.json
    /// <c>buoyancy</c>), else the band round its centre of mass under the sea (ships.json <c>water</c>).
    /// </summary>
    public readonly float Submerged;

    /// <summary>
    /// The water's surface under the ship (sea, lake or stream) as its distance from the world's centre (metres), or 0:
    /// none there, or a ship that doesn't float on its shape (only those sample it).
    /// </summary>
    public readonly double Water;

    /// <summary>The engines run this tick (someone at the helm, fuel in the tank, not switched off).</summary>
    public readonly bool Engines;

    /// <summary>Boosting this tick; flight assist on.</summary>
    public readonly bool Boosting;

    public readonly bool Assist;
}

/// <summary>
/// What a ship's engines do (ships.json <c>drive</c>): each tick it turns the pilot's input into an acceleration along the
/// ship's axes and an angular acceleration about them; ShipMotor's shared step does the rest (gravity, drag, the sea,
/// skids, sweeps against the world, crashes, sleep). One instance per kind of ship, made by its reader in
/// <see cref="ShipDrives"/>; it keeps no state of its own (what changes lives in <see cref="ShipState"/>, so prediction
/// replays it).
/// </summary>
public interface IShipDrive
{
    /// <summary>Its key in ships.json (<c>drive.kind</c>).</summary>
    string Kind { get; }

    /// <summary>
    /// On its skids with nothing asked of it, the engines idle and give nothing (true: today's thrusters). A drive that
    /// works while it stands (a rotor spooling up) says false.
    /// </summary>
    bool IdlesOnSkids { get; }

    /// <summary>Once a tick before <see cref="Command"/>, idle on its skids or not: whatever the drive keeps in the state.</summary>
    void Advance(ref ShipState s, in ShipDriveContext c, in ShipInput input, ShipDef def);

    /// <summary>
    /// The tick's thrust (m/s² along the ship's axes) and turn (rad/s² about them) for the input, before the fuel's say;
    /// called only while the engines run and the ship isn't idle on its skids.
    /// </summary>
    void Command(in ShipDriveContext c, ref ShipState s, in ShipInput input, ShipDef def, out Vector3 thrust, out Vector3 turn);

    /// <summary>How hard the engines work for a thrust, 0 … 1: the fuel burns from idle to full by it.</summary>
    float Effort(Vector3 thrust, ShipDef def);

    /// <summary>The thrust as each axis's share for the views and the HUD (<see cref="ShipState.Thrust"/>), finite always.</summary>
    Vector3 Output(Vector3 thrust, ShipDef def);

    /// <summary>What is wrong with a ship's numbers for this drive (the content check), each a sentence.</summary>
    IEnumerable<string> Problems(ShipDef ship);

    /// <summary>
    /// This drive for a build whose modules add to it (VEHICLES.md §9.1: modules.json <c>drive</c>, summed by name over the
    /// build: a rotor's lift, an engine's power). A drive no module changes is itself.
    /// </summary>
    IShipDrive Compose(IReadOnlyDictionary<string, float> sums) => this;
}

/// <summary>
/// The drives by kind (GROUND_SYSTEMS.md: one system per concern, found in a registry): ships.json's <c>drive</c> block
/// names one, and its reader makes the ship's instance from the block's own numbers. Without the block a ship flies on
/// thrusters.
/// </summary>
public static class ShipDrives
{
    /// <summary>The drive a ship has when ships.json names none.</summary>
    public const string Default = ShipThrusters.Key;

    internal delegate IShipDrive Reader(JsonFields f, JsonElement block, string where);

    private static readonly Dictionary<string, Reader> Readers = new(StringComparer.Ordinal)
    {
        [ShipThrusters.Key] = ShipThrusters.Read,
        [ShipPropeller.Key] = ShipPropeller.Read,
        [ShipRotor.Key] = ShipRotor.Read,
    };

    /// <summary>Every kind a ship may name.</summary>
    public static IEnumerable<string> Kinds => Readers.Keys;

    /// <summary>The drive a ship's <c>drive</c> block names (null: none, the default), its problems noted.</summary>
    internal static IShipDrive Read(JsonFields f, JsonElement? block, string where)
    {
        if (block is not { } b)
        {
            return ShipThrusters.Instance;
        }
        string kind = f.Text(b, "kind", where, Default);
        if (!Readers.TryGetValue(kind, out Reader? read))
        {
            f.Problem(where + ".kind", $"no drive \"{kind}\" (known: {string.Join(", ", Readers.Keys)})");
            return ShipThrusters.Instance;
        }
        return read(f, b, where);
    }
}
