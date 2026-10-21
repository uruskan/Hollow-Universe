using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Ships;

/// <summary>
/// A ship's whole step (ships.json <c>motor</c>, VEHICLES.md §9.2): how it moves through a tick. <c>flight</c>, the default,
/// is <see cref="ShipMotor"/> with its drive (thrusters, a propeller, a rotor); <c>ground</c> is <see cref="GroundMotor"/>,
/// a vehicle on wheels. Each is a pure function of state and input, so the server and a driver's prediction run it alike.
/// </summary>
public interface IShipMotor
{
    /// <summary>Its key in ships.json.</summary>
    string Key { get; }

    /// <summary>It flies: its entry gives the flight blocks (thrust, speed, rates, landing, skids) and its drive is judged.</summary>
    bool Flies { get; }

    /// <summary>Advances a ship one tick (<paramref name="ignoreBody"/>: its own parked body, if it has one).</summary>
    ShipEvents Step(ref ShipState s, in ShipInput input, ShipDef def, in ShipContext ctx, uint ignoreBody);

    /// <summary>What is wrong with a ship's numbers for this motor (the content check), each a sentence.</summary>
    IEnumerable<string> Problems(ShipDef ship);
}

/// <summary>The motors by key (GROUND_SYSTEMS.md: one system per concern, found in a registry).</summary>
public static class ShipMotors
{
    public const string Default = FlightMotor.Key;

    private static readonly Dictionary<string, IShipMotor> ByKey = new(StringComparer.Ordinal)
    {
        [FlightMotor.Key] = FlightMotor.Instance,
        [GroundMotor.Key] = GroundMotor.Instance,
    };

    public static IEnumerable<string> Keys => ByKey.Keys;

    /// <summary>The motor a ship names, or null: no such motor.</summary>
    public static IShipMotor? Find(string key) => ByKey.GetValueOrDefault(key);
}

/// <summary>Flight (GROUND.md §6.6): <see cref="ShipMotor.Step"/>, its drive deciding what the engines give.</summary>
public sealed class FlightMotor : IShipMotor
{
    public const string Key = "flight";

    public static readonly FlightMotor Instance = new();

    string IShipMotor.Key => Key;

    public bool Flies => true;

    public ShipEvents Step(ref ShipState s, in ShipInput input, ShipDef def, in ShipContext ctx, uint ignoreBody) =>
        ShipMotor.Step(ref s, input, def, ctx, ignoreBody);

    public IEnumerable<string> Problems(ShipDef ship)
    {
        // Thrust, speeds, boost and rates are its drive's to judge (thrusters want every axis).
        foreach (string problem in ship.Drive.Problems(ship))
        {
            yield return problem;
        }
        if (ship.SkidPoints.Count < 3 || ship.SkidStiffness <= 0 || ship.SkidDamping <= 0 || ship.SkidTravel is <= 0 or > 1 ||
            ship.SkidFriction is < 0 or > 2 || ship.MaxLandingSlope is <= 0 or > 0.8f)
        {
            yield return "at least three skid points, positive springs, travel 0 … 1 m, friction 0 … 2, a landing slope up to 45°";
        }
    }
}
