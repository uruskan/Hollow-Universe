using System;
using System.Numerics;
using Hollow.Simulation.Ships;

namespace Hollow.Tests;

/// <summary>A ship kind's parts and seats by what the tests call them (ships.json's data, IRON_WAKE.md §5.1).</summary>
internal static class ShipTestParts
{
    /// <summary>The Tin Finch's hatch swings open or shut in this many ticks (its door's seconds, 1).</summary>
    public const int HatchTicks = 30;

    /// <summary>The part its helm is taken at (the Tin Finch's hatch, the Iron Wake's helm).</summary>
    public static byte HelmPart(this ShipDef def) => (byte)def.PartOfSeat(def.Helm.Index);

    /// <summary>Where that part is, in the ship's frame (the hatch's point).</summary>
    public static Vector3 HatchPoint(this ShipDef def) => def.PartPoint(def.HelmPart());

    public static byte FuelPart(this ShipDef def) => (byte)Math.Max(0, def.PartIndex("fuel"));

    public static byte CargoPartIndex(this ShipDef def) => (byte)def.CargoPart;

    /// <summary>The helm's seat, in the ship's frame (at rest).</summary>
    public static Vector3 SeatPoint(this ShipDef def) => def.Seats[def.Helm.Index].At.Position;
}
