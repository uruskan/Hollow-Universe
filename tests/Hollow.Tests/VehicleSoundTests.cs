using System;
using System.IO;
using System.Linq;
using Hollow.Client.Audio;
using Hollow.Client.Vehicles;
using Hollow.Simulation.Ships;

namespace Hollow.Tests;

/// <summary>
/// Ground vehicles' engine sounds (VEHICLES.md §7.1): the gearbox the sounds, the HUD and the exhaust read
/// (<see cref="VehicleRevs"/>), and sounds.json's <c>engines</c> entry of every ground vehicle kind.
/// </summary>
public static class VehicleSoundTests
{
    private static SoundCatalogData Catalogue() =>
        SoundCatalogData.Load(Path.Combine(Fixtures.Root, "game", "data", "ground", SoundCatalogData.FileName));

    private static EngineSoundData Bike => new()
    {
        Kind = "bike", IdleRpm = 1450, Redline = 8800, Limiter = 9100, Gears = new[] { 7f, 11f, 15f, 19.5f, 25.6f }, Reverse = 4,
        Response = 0.06f, Upshift = 0.86f, Downshift = 0.42f, ShiftTime = 0.12f, Launch = 0.42f,
    };

    private static EngineSoundData Diesel => new()
    {
        Kind = "truck", IdleRpm = 750, Redline = 3000, Limiter = 3100, Gears = new[] { 4.2f, 7f, 10.5f, 15f, 20.6f }, Reverse = 5,
        Response = 0.22f, Upshift = 0.85f, Downshift = 0.45f, ShiftTime = 0.45f, Launch = 0.42f, TurboSpool = 0.8f,
    };

    private const float Dt = 1f / 60f;

    [Test]
    public static void VehicleRevsChangeUpAsItGathersSpeedAndDownAsItSlows()
    {
        var revs = new VehicleRevs(Bike);
        revs.Step(0, 0, true, true, Dt);
        Assert.True(revs.Running && revs.Gear == 0, "started: idling in neutral");
        Assert.Near(1450, revs.Rpm, 1, "at idle");
        int ups = 0, highest = 0;
        float speed = 0;
        for (int i = 0; i < 60 * 12; i++)
        {
            speed = Math.Min(25f, speed + 2.5f * Dt);
            revs.Step(speed, 1, true, true, Dt);
            ups += revs.Shifted ? 1 : 0;
            Assert.True(revs.Gear >= highest, "no change down on a steady pull");
            highest = Math.Max(highest, revs.Gear);
            Assert.True(revs.Rpm >= 1450 * 0.85f - 1 && revs.Rpm <= 9100 + 1, $"revs {revs.Rpm} in the band");
        }
        Assert.Equal(5, revs.Gear, "top gear at top speed");
        Assert.True(ups == 4, $"four changes up on the way (saw {ups})");
        Assert.True(revs.Rpm > 0.85f * 8800, $"near the redline at 25 m/s in fifth ({revs.Rpm})");
        Assert.True(revs.Load > 0.9f, "on the throttle");

        // Off the throttle: engine braking, down through the box as it slows, neutral when it stops.
        bool dragged = false;
        for (int i = 0; i < 60 * 20 && speed > 0; i++)
        {
            speed = Math.Max(0, speed - 2f * Dt);
            revs.Step(speed, 0, true, true, Dt);
            dragged |= revs.Overrun > 0.3f;
            Assert.True(revs.Load < 0.1f || i < 10, "no load off the throttle");
        }
        Assert.True(dragged, "engine braking rolling off the throttle in gear");
        for (int i = 0; i < 60; i++)
        {
            revs.Step(0, 0, true, true, Dt);
        }
        Assert.Equal(0, revs.Gear, "neutral once stopped");
        Assert.Near(1450, revs.Rpm, 30, "back to idle");
    }

    [Test]
    public static void VehicleRevsSlipTheClutchFreeRevAndRunDown()
    {
        var revs = new VehicleRevs(Bike);
        for (int i = 0; i < 60; i++)
        {
            revs.Step(0, 1, true, true, Dt);
        }
        Assert.Equal(1, revs.Gear, "first to pull away");
        Assert.Near(0.42 * 8800, revs.Rpm, 150, "the clutch slipping at its launch revs");
        for (int i = 0; i < 60; i++)
        {
            revs.Step(3, 1, true, false, Dt);
        }
        Assert.True(revs.Rpm > 0.9f * 8800, $"wheels in the air spin up to the limiter ({revs.Rpm})");
        for (int i = 0; i < 30; i++)
        {
            revs.Step(-1.5f, -1, true, true, Dt);
        }
        Assert.Equal(-1, revs.Gear, "reverse backing up");
        for (int i = 0; i < 120; i++)
        {
            revs.Step(0, 0, false, true, Dt);
        }
        Assert.True(!revs.Running && revs.Rpm == 0 && revs.Gear == 0, "stopped: the revs run down to nothing");
    }

    [Test]
    public static void VehicleRevsSpoolTheTurboAndReleaseIt()
    {
        var revs = new VehicleRevs(Diesel);
        float speed = 6;
        for (int i = 0; i < 60 * 3; i++)
        {
            speed = Math.Min(14f, speed + 1.5f * Dt);
            revs.Step(speed, 1, true, true, Dt);
        }
        Assert.True(revs.Boost > 0.4f, $"spooled on a hard pull ({revs.Boost})");
        int released = 0;
        for (int i = 0; i < 60; i++)
        {
            revs.Step(speed, 0, true, true, Dt);
            released += revs.Released ? 1 : 0;
        }
        Assert.Equal(1, released, "one release letting go");
        Assert.True(revs.Boost < 0.2f, "the boost gone off the throttle");
        var bike = new VehicleRevs(Bike);
        for (int i = 0; i < 120; i++)
        {
            bike.Step(10, 1, true, true, Dt);
        }
        Assert.True(bike.Boost == 0, "no turbo, no boost");
    }

    [Test]
    public static void EngineSoundsCoverEveryGroundVehicle()
    {
        SoundCatalogData c = Catalogue();
        var problems = new System.Collections.Generic.List<string>();
        foreach (ShipDef ship in ContentFixture.Content.Ships.Where(s => s.Ground != null))
        {
            if (c.Engine(ship.Key) is not { } e)
            {
                problems.Add($"{ship.Key}: no engine sounds");
                continue;
            }
            if (e.Gears.Count == 0 || e.Gears.Zip(e.Gears.Skip(1)).Any(p => p.Second <= p.First))
            {
                problems.Add($"{ship.Key}: gears not rising");
            }
            else if (e.Gears[^1] < ship.Ground!.TopSpeed)
            {
                problems.Add($"{ship.Key}: top gear {e.Gears[^1]} m/s short of its top speed {ship.Ground.TopSpeed}");
            }
            if (e.Loops.Count < 3 || e.Loops[0].Rpm > e.IdleRpm * 1.05f || e.Loops[^1].Rpm < e.Redline * 0.9f)
            {
                problems.Add($"{ship.Key}: loops from idle to the redline");
            }
            foreach (string key in e.Loops.SelectMany(l => new[] { l.On, l.Off }).Concat(e.Tyres.Values)
                         .Concat(new[] { e.Turbo, e.SkidLoose, e.SkidHard }.OfType<string>()))
            {
                if (c.Find(key) is not { Loop: true })
                {
                    problems.Add($"{ship.Key}: {key} isn't a loop");
                }
            }
            foreach (string surface in new[] { "grass", "dirt", "gravel", "sand", "rock", "snow", "water", "metal", "wood" })
            {
                if (e.TyreFor(surface) == null)
                {
                    problems.Add($"{ship.Key}: no tyres on {surface}");
                }
            }
            foreach (string role in new[] { "start", "shutdown" })
            {
                if (c.ItemEvent(ship.Key, role) is not { } key || c.Find(key) == null)
                {
                    problems.Add($"{ship.Key}: no {role} sound");
                }
            }
            if (c.ItemEvent(ship.Key, "touchdown") != null)
            {
                problems.Add($"{ship.Key}: a touchdown role (a vehicle lands by VehicleSounds, after a jump)");
            }
            if (e.Knock == null || e.Land == null || e.Creak == null)
            {
                problems.Add($"{ship.Key}: its suspension's knock, creak and landing");
            }
        }
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }
}
