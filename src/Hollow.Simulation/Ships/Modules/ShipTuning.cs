using System;
using System.Collections.Generic;

namespace Hollow.Simulation.Ships.Modules;

/// <summary>
/// The kind's numbers a module may replace while it is fitted (modules.json <c>ground</c>, <c>drag</c>, <c>crash</c> and
/// <c>fuelUse</c> blocks, VEHICLES.md §9.11): the blown six's top speed and traction, the monster tyres' brakes and steering,
/// the lift kit's grip height and righting. Each name is the kind's block and its key in ships.json, in the file's units
/// (steering in degrees); the composed layout takes the value, the later module in the build's order winning. This table is
/// the one place that names them.
/// </summary>
public static class ShipTuning
{
    private const float Degrees = MathF.PI / 180;

    private static readonly Dictionary<string, Action<ShipDef, float>> Setters = new(StringComparer.Ordinal)
    {
        ["ground.topSpeed"] = (d, v) => d.Ground = d.Ground! with { TopSpeed = v },
        ["ground.reverseSpeed"] = (d, v) => d.Ground = d.Ground! with { ReverseSpeed = v },
        ["ground.brake"] = (d, v) => d.Ground = d.Ground! with { Brake = v },
        ["ground.traction"] = (d, v) => d.Ground = d.Ground! with { Traction = v },
        ["ground.steer"] = (d, v) => d.Ground = d.Ground! with { Steer = v * Degrees },
        ["ground.steerFast"] = (d, v) => d.Ground = d.Ground! with { SteerFast = v * Degrees },
        ["ground.rolling"] = (d, v) => d.Ground = d.Ground! with { Rolling = v },
        ["ground.handbrakeGrip"] = (d, v) => d.Ground = d.Ground! with { HandbrakeGrip = v },
        ["ground.gripHeight"] = (d, v) => d.Ground = d.Ground! with { GripHeight = v },
        ["ground.sideGripHeight"] = (d, v) => d.Ground = d.Ground! with { SideGripHeight = v },
        ["ground.coast"] = (d, v) => d.Ground = d.Ground! with { Coast = v },
        ["ground.righting"] = (d, v) => d.Ground = d.Ground! with { Righting = v },
        ["ground.rightingDamping"] = (d, v) => d.Ground = d.Ground! with { RightingDamping = v },
        ["drag.forward"] = (d, v) => d.DragForward = v,
        ["drag.side"] = (d, v) => d.DragSide = v,
        ["drag.top"] = (d, v) => d.DragTop = v,
        ["drag.turn"] = (d, v) => d.TurnDrag = v,
        ["crash.safe"] = (d, v) => d.CrashSafeSpeed = v,
        ["crash.scale"] = (d, v) => d.CrashScale = v,
        ["crash.pilot"] = (d, v) => d.CrashPilotShare = v,
        ["fuelUse.idle"] = (d, v) => d.FuelIdle = v,
        ["fuelUse.full"] = (d, v) => d.FuelFull = v,
    };

    /// <summary>The blocks a module's numbers come in (modules.json).</summary>
    public static readonly string[] Blocks = { "ground", "drag", "crash", "fuelUse" };

    /// <summary>A name the table knows ("ground.topSpeed").</summary>
    public static bool Knows(string name) => Setters.ContainsKey(name);

    /// <summary>A name that changes the ground motor's numbers, which only a kind with a <c>ground</c> block has.</summary>
    public static bool IsGround(string name) => name.StartsWith("ground.", StringComparison.Ordinal);

    /// <summary>Sets a module's numbers on a composed layout (a copy: never on a kind as read).</summary>
    internal static void Apply(ShipDef composed, IReadOnlyList<(string Name, float Value)> sets)
    {
        foreach ((string name, float value) in sets)
        {
            if ((!IsGround(name) || composed.Ground != null) && Setters.TryGetValue(name, out Action<ShipDef, float>? set))
            {
                set(composed, value);
            }
        }
    }
}
