using System;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Ships.Modules;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>
/// VEHICLES.md §9.10: the TOGG's overboost. Left Shift held gives its engine 1.4 times its power for as long as it is held,
/// at twice the fuel; it pulls harder where the power, not the grip, limits it; it is a flag on the wire, and a client's
/// prediction of it holds.
/// </summary>
public static class VehicleBoostTests
{
    private static readonly ShipInput Flat = new() { Throttle = 1 };
    private static readonly ShipInput Boosted = new() { Throttle = 1, Buttons = ShipButtons.Boost };

    [Test]
    public static void TheEnginesOverboostByTheirData()
    {
        var c = ContentFixture.Content;
        ShipDef kind = c.Ship("togg")!;
        // The TOGG with its own engine, and with the Monster TOGG's blown six in its nose mount.
        foreach (string engine in new[] { "togg-engine", "monster-togg-engine" })
        {
            var build = new ShipBuild(kind.DefaultBuild.Entries.Select(e =>
                c.Module(e.Module) is { Power: > 0 } ? e with { Module = (ushort)c.ItemByKey(engine).Id } : e).ToArray());
            ShipDef d = c.Compose(kind, build);
            Fixtures.Report($"the TOGG with {engine}: {d.EnginePower / 1000:0} kW, {d.BoostPower / 1000:0} kW overboosted, fuel ×{d.FuelBoost:0.0}");
            Assert.True(d.Overboosts, $"{engine} overboosts");
            Assert.Near(1.4, d.BoostPower / d.EnginePower, 1e-4, "1.4 times its engine's power (modules.json boost)");
            Assert.Near(2.0, d.FuelBoost, 1e-4, "twice the fuel (ships.json fuel.boost)");
        }
        ShipDef bike = c.Ship("zamazingo")!;
        Assert.True(!bike.Overboosts, "an engine without a boost doesn't overboost");
        ShipDef togg = c.Compose(kind, kind.DefaultBuild);
        Assert.Near(1.4, GroundMotor.DriveForce(togg, 1, 15, boosting: true) / GroundMotor.DriveForce(togg, 1, 15), 1e-4,
            "at 15 m/s the power limits the drive: 1.4 times the force");
        Assert.Near(1, GroundMotor.DriveForce(togg, 1, 15, boosting: true) / GroundMotor.DriveForce(togg, 1, 15, boosting: true), 1e-6, "steady");
    }

    [Test]
    public static void ItPullsHarderWhileShiftIsHeld()
    {
        (float speed, float distance, int to15) Run(ShipInput input)
        {
            using var t = new GroundMotorTests.Drive();
            t.Go(default, 30);
            Vector3 start = t.Car.State.Position;
            int to15 = -1;
            for (int i = 0; i < 240; i++)
            {
                t.Go(input, 1);
                if (to15 < 0 && t.Along >= 15)
                {
                    to15 = i;
                }
                if (input.Has(ShipButtons.Boost))
                {
                    Assert.True(t.Car.State.Has(ShipFlags.Boosting), "the boosting flag is set while Shift is held on the throttle");
                }
            }
            return (t.Along, Vector3.Distance(start, t.Car.State.Position), to15);
        }
        var flat = Run(Flat);
        var boosted = Run(Boosted);
        Fixtures.Report(FormattableString.Invariant(
            $"eight seconds flat out: {flat.speed:0.0} m/s, {flat.distance:0} m, 15 m/s at {flat.to15 / 30f:0.00} s; overboosted {boosted.speed:0.0} m/s, {boosted.distance:0} m, 15 m/s at {boosted.to15 / 30f:0.00} s"));
        Assert.True(boosted.to15 > 0 && (flat.to15 < 0 || boosted.to15 < flat.to15 * 0.92f), "overboosted it reaches 15 m/s sooner");
        Assert.True(boosted.distance > flat.distance + 3, "and goes further");
        Assert.True(boosted.speed < 20.5f, "its top speed is the same");

        using var idle = new GroundMotorTests.Drive();
        idle.Go(new ShipInput { Buttons = ShipButtons.Boost }, 30);
        Assert.True(!idle.Car.State.Has(ShipFlags.Boosting), "Shift with no throttle doesn't overboost");
        idle.Go(new ShipInput { Throttle = -1, Buttons = ShipButtons.Boost }, 30);
        Assert.True(!idle.Car.State.Has(ShipFlags.Boosting), "nor backing up");
    }

    [Test]
    public static void ItBurnsTwiceTheFuelWithNoTimeLimit()
    {
        float Burnt(ShipInput input, int ticks, out bool boostingAtEnd)
        {
            using var t = new GroundMotorTests.Drive();
            t.Go(default, 30);
            float before = t.Car.State.Fuel;
            t.Go(input, ticks);
            boostingAtEnd = t.Car.State.Has(ShipFlags.Boosting);
            return before - t.Car.State.Fuel;
        }
        float flat = Burnt(Flat, 300, out _);
        float boosted = Burnt(Boosted, 300, out _);
        Fixtures.Report($"ten seconds flat out: {flat:0.000} fuel; overboosted {boosted:0.000}");
        Assert.Near(2.0, boosted / flat, 0.02, "twice the fuel");
        Burnt(Boosted, 1800, out bool still);
        Assert.True(still, "still overboosting after a minute held (no time limit)");
    }

    [Test]
    public static void TheSameBoostedInputsGiveTheSameBits()
    {
        ShipState Run()
        {
            using var t = new GroundMotorTests.Drive();
            t.Go(Boosted, 90);
            t.Go(new ShipInput { Throttle = 0.7f, Yaw = 0.5f, Buttons = ShipButtons.Boost }, 45);
            t.Go(Flat, 30);
            t.Go(Boosted, 30);
            return t.Car.State;
        }
        Assert.True(Run().Equals(Run()), "two overboosted runs end in the same state, to the bit");
    }

    /// <summary>Over the real network: Ada overboosts, her prediction is never corrected, and Bo sees the flag (black smoke, the turbo).</summary>
    [Test]
    public static void OverboostIsPredictedAndSeen()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Server.Execute("ship Ada togg");
        rig.Run(1.0, (_, _) => default);
        RemoteShip car = ada.Ships().All.First();
        Vector3 door = car.PointOf(car.Def!.HatchPoint());
        Vector3 side = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, car.Rotation));
        rig.Teleport("Ada", door + side * 1.2f);
        rig.Run(0.5, (_, _) => default);
        ada.Local.Interact(new TargetRef(TargetFamily.Ship, car.Id.Value, car.Def!.HelmPart()), "hatch-open");
        rig.Run(1.0, (_, _) => default);
        ada.Local.Interact(new TargetRef(TargetFamily.Ship, car.Id.Value, car.Def!.HelmPart()), "drive");
        rig.Run(0.5, (_, _) => default);
        Assert.True(ada.Ships().Piloting, "Ada drives it");
        long corrections = ada.Stats.ShipCorrections;
        bool boSaw = false, adaBoosted = false;
        rig.Run(4.0, (client, time) =>
        {
            if (client == bo && bo.Ships().All.FirstOrDefault(s => s.Id == car.Id) is { } s && s.Has(Hollow.Net.Protocol.ShipNetFlags.Boosting))
            {
                boSaw = true;
            }
            if (client == ada && ada.Ships().OwnShip.Has(ShipFlags.Boosting))
            {
                adaBoosted = true;
            }
            return client == ada
                ? new InputFrame { Ship = new ShipInput { Throttle = 1, Yaw = time > 2 ? 0.4f : 0, Buttons = time > 1 ? ShipButtons.Boost : ShipButtons.None } }
                : default;
        });
        Fixtures.Report($"Ada's prediction overboosted {adaBoosted}, Bo saw it {boSaw}, corrections {ada.Stats.ShipCorrections - corrections}");
        Assert.True(adaBoosted && boSaw, "Ada's client predicts the overboost and Bo sees its flag");
        Assert.Equal(0L, ada.Stats.ShipCorrections - corrections, "Ada's prediction held overboosted");
    }
}
