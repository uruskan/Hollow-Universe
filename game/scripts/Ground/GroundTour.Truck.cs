using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using HollowUniverse.Fx;
using HollowUniverse.Ships;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The TOGG as a heavy truck (VEHICLES.md §9.10; <c>--tour-steps truck</c>, or <c>HOLLOW_TOUR_KIND=monster-togg</c> for the
/// monster): set down, its engine started and idling (shaking on its mounts), revved standing still on the handbrake (the
/// chassis twisting), pulling away (soot, a puff at each gear change), then overboosted with Shift held (black smoke, the
/// turbo screaming, the engine leaning harder) and let go (the turbo's release). Prints its speed, revs, gear, turbo,
/// overboost and the engine's lean as it goes, the fuel it burns, and the frame times with the effects
/// (<c>HOLLOW_TOUR_NO_FX=1</c>: without them).
/// </summary>
public partial class GroundTour
{
    private void TruckTrace(string label, RemoteShip car)
    {
        var revs = View.Ships.RevsOf(car.NetId);
        var rock = View.Ships.RockOf(car.NetId);
        ShipState s = Client.Ships().OwnShip;
        float lean = (rock?.Lean ?? 0) * 180 / MathF.PI, twist = (rock?.Twist ?? 0) * 180 / MathF.PI;
        GD.Print("[tour] truck: " + label + ": " + FormattableString.Invariant(
            $"{s.Velocity.Length() * 3.6f:0} km/h, {revs?.Rpm:0} rpm, gear {revs?.Gear}, load {revs?.Load:0.00}, turbo {revs?.Boost:0.00}, overboost {revs?.Overboost:0.00}, boosting {s.Has(ShipFlags.Boosting)}, lean {lean:0.0}°, twist {twist:0.00}°, fuel {s.Fuel:0.00}"));
    }

    private async Task TruckTour()
    {
        if (System.Environment.GetEnvironmentVariable("HOLLOW_TOUR_NO_FX") == "1")
        {
            FxDirector.Instance.OnlyBatch = "(none)";
        }
        string kind = System.Environment.GetEnvironmentVariable("HOLLOW_TOUR_KIND") is { Length: > 0 } k ? k : "togg";
        var old = Client.Ships().All.Select(s => s.Id).ToHashSet();
        Admin("ship", kind);
        await Until(() => Client.Ships().All.Any(s => !old.Contains(s.Id) && s.Def != null), 8);
        RemoteShip car = Client.Ships().All.FirstOrDefault(s => !old.Contains(s.Id) && s.Def != null)
            ?? throw new InvalidOperationException($"truck: the {kind} was not set down");
        ShipDef def = car.Def!;
        await Seconds(2);

        byte seat = (byte)def.PartOfSeat(def.Helm.Index);
        await TeleportTo(car.PointOf(def.PartPoint(seat) + new Num.Vector3(1.6f, 0, 0)));
        Face(car.PointOf(def.PartPoint(seat)));
        await Seconds(0.5);
        Me.Interact(new TargetRef(TargetFamily.Ship, car.Id.Value, seat), "hatch-open");
        await Seconds(1);
        Me.Interact(new TargetRef(TargetFamily.Ship, car.Id.Value, seat), def.Helm.VerbKey);
        await Until(() => Client.Ships().Piloting, 5);
        if (!Client.Ships().Piloting)
        {
            throw new InvalidOperationException("truck: the driver's seat wasn't taken");
        }
        View.ShipCamera.Mode = ShipCameraMode.Chase;
        View.ShipCamera.Reset();
        await Seconds(1);
        await Pilot(new ShipInput { Engines = true }, 3);
        TruckTrace("idling", car);
        await Shot("truck-idle", frames: 0);

        // Revved standing still on the handbrake: the chassis twists against the flywheel.
        float twistMost = 0;
        await Pilot(new ShipInput { Engines = true, Throttle = 1, Vertical = 1 }, 0.6, () =>
        {
            twistMost = MathF.Max(twistMost, MathF.Abs(View.Ships.RockOf(car.NetId)?.Twist ?? 0));
            return false;
        });
        TruckTrace($"revved on the handbrake (twisted up to {twistMost * 180 / MathF.PI:0.00}°)", car);
        await Shot("truck-rev", frames: 0);
        await Pilot(new ShipInput { Engines = true }, 1.5);

        // Pulling away: soot, a puff at each gear change.
        float fuel = Client.Ships().OwnShip.Fuel;
        await Pilot(new ShipInput { Engines = true, Throttle = 1 }, 0.5);
        TruckTrace("pulling away", car);
        await Shot("truck-launch", frames: 0);
        await DriveTimed(FxDirector.Instance.OnlyBatch == null ? "truck flat out, with effects" : "truck flat out, without effects", 4);
        TruckTrace("flat out", car);
        float plainFuel = fuel - Client.Ships().OwnShip.Fuel;
        await Pilot(new ShipInput { Engines = true, Throttle = -1 }, 3);
        await Pilot(new ShipInput { Engines = true }, 1);

        // Overboosted from standing: black smoke, the turbo screaming, the engine leaning harder.
        fuel = Client.Ships().OwnShip.Fuel;
        var boost = new ShipInput { Engines = true, Throttle = 1, Buttons = ShipButtons.Boost };
        await Pilot(boost, 0.8);
        TruckTrace("overboosted, pulling away", car);
        await Shot("truck-overboost", frames: 0);
        await DriveTimed(FxDirector.Instance.OnlyBatch == null ? "truck overboosted, with effects" : "truck overboosted, without effects", 3.7, boost);
        TruckTrace("overboosted", car);
        await Shot("truck-overboost-speed", frames: 0);
        GD.Print(FormattableString.Invariant(
            $"[tour] truck: fuel over 4.5 s flat out {plainFuel:0.000}, overboosted {fuel - Client.Ships().OwnShip.Fuel:0.000}"));

        // Shift let go on the throttle: the turbo's release.
        bool released = false;
        await Pilot(new ShipInput { Engines = true, Throttle = 1 }, 0.8, () =>
        {
            released |= View.Ships.RevsOf(car.NetId)?.Released ?? false;
            return false;
        });
        TruckTrace($"Shift let go (the turbo's release heard: {released})", car);
        await Pilot(new ShipInput { Engines = true, Throttle = -1 }, 3);

        View.Scripted = null;
        Me.LeaveSeat();
        await Until(() => !Client.Ships().Seated, 5);
    }
}
