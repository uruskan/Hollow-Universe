using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>Earth's ground under a sea 10 m deep (the KA-FA's ditching test).</summary>
public sealed class FloodedSurface : ISurface
{
    public FloodedSurface(double radius) => Radius = radius;

    public double Radius { get; }

    public double SeaRadius => Radius;

    public double HeightAt(Vector3D direction) => -10;
}

/// <summary>
/// The rotor drive (docs/MINICOPTER.md §5), flown as the KA-FA 1500 over Earth's air and gravity on level ground: its
/// spool, hover, climb, top speed, self-levelling, yaw, ceiling, landing, the engine that needs a pilot, fuel, Rust's crash
/// rule, a rotor strike, the sea, determinism and storage.
/// </summary>
public static class RotorTests
{
    private const int T = CharacterMotor.TickRate;

    public static ShipDef KaFa => ContentFixture.Content.Ship("ka-fa-1500")!;

    private static ShipRig Earth(ISurface? surface = null) => new(surface, worldSpec: ZoneSpec.StartingWorld(10, 20));

    /// <summary>A KA-FA on the ground (or <paramref name="height"/> over it), its rotor at <paramref name="spool"/>.</summary>
    private static Ship Add(ShipRig rig, float height = 0, float spool = 0, float yaw = 0, float fuel = 250)
    {
        Vector3 ground = rig.Zone.GroundAt(0, 0);
        Ship ship = rig.Zone.Ships().SpawnShip(KaFa, ground + rig.Zone.Frame.UpAt(ground) * height, yaw, EntityId.None, fuel);
        ship.State.Spool = spool;
        if (height > 0)
        {
            ship.State.Flags &= ~ShipFlags.Landed;
        }
        return ship;
    }

    private static float Climb(ShipRig rig, Ship ship) => Vector3.Dot(ship.State.Velocity, rig.Up(ship));

    private static float TiltDegrees(ShipRig rig, Ship ship) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(ship.State.Up, rig.Up(ship)), -1, 1)) * 180 / MathF.PI;

    private static float Horizontal(ShipRig rig, Ship ship)
    {
        Vector3 v = ship.State.Velocity, up = rig.Up(ship);
        return (v - up * Vector3.Dot(v, up)).Length();
    }

    [Test]
    public static void RotorKaFaLoadsAsARotorcraft()
    {
        ShipDef def = KaFa;
        Assert.True(def.Drive is ShipRotor, "the KA-FA flies on the rotor drive");
        Assert.True(def.NeedsPilot && def.HasFragile && def.StrikeShare > 0, "its engine needs a pilot; its rotor's disc is fragile");
        Assert.Equal("rotor", def.Controls, "its control scheme");
        Assert.Equal(2, def.Seats.Count, "a pilot and a passenger");
        Assert.True(def.Seats[1].Exposed && def.Seats[0].Exposed, "both seats are open");
        Assert.True(Vector3.Distance(ShipSeats.Forward(def, 1, ReadOnlySpan<float>.Empty), -Vector3.UnitX) < 1e-5f, "the rear seat looks out to the right");
        Assert.Equal(750f, def.Health, "Rust's minicopter's hull");
    }

    /// <summary>§5.1: the rotor spools up over 5 s; it can't lift off at once, then climbs at its 7 m/s.</summary>
    [Test]
    public static void RotorSpoolsUpThenLiftsOff()
    {
        using var rig = Earth();
        Ship ship = Add(rig);
        var climb = new ShipInput { Vertical = 1 };
        int liftOff = -1;
        for (int i = 0; i < 12 * T; i++)
        {
            rig.Fly(ship, climb, 1);
            if (liftOff < 0 && rig.Height(ship) > 0.3f)
            {
                liftOff = i;
            }
        }
        Fixtures.Report($"KA-FA: spool {ship.State.Spool:0.00} after 12 s, lifted off at {liftOff / (float)T:0.00} s, climbing {Climb(rig, ship):0.00} m/s at {rig.Height(ship):0.0} m");
        Assert.InRange(2.0, 4.5, liftOff / (float)T, "lifts off once the rotor turns fast enough (spool² × lift over its weight)");
        Assert.Near(1, ship.State.Spool, 1e-6, "at full spool");
        Assert.InRange(6.3, 7.3, Climb(rig, ship), "climbs at the collective's 7 m/s");
    }

    /// <summary>M2: no key held, it holds its height and stays where it is.</summary>
    [Test]
    public static void RotorHoversWithNoInput()
    {
        using var rig = Earth();
        Ship ship = Add(rig, height: 50, spool: 1);
        rig.Fly(ship, default, T);
        float start = rig.Height(ship);
        float fuel = ship.State.Fuel;
        rig.Fly(ship, default, 10 * T);
        float drift = MathF.Abs(rig.Height(ship) - start);
        float burnt = (fuel - ship.State.Fuel) / 10;
        Fixtures.Report($"KA-FA hover: drifted {drift * 1000:0} mm in 10 s at {ship.State.Velocity.Length():0.000} m/s, collective {ship.State.Thrust.Y:0.00}, {burnt:0.000} fuel/s");
        Assert.True(drift < 0.1f && ship.State.Velocity.Length() < 0.05f, "hovers in place");
        Assert.InRange(0.4, 0.6, ship.State.Thrust.Y, "the rotor carries its weight on about half its lift");
        Assert.InRange(0.3, 0.4, burnt, "burns between idle (0.25) and full (0.5)");
        Assert.True(TiltDegrees(rig, ship) < 0.5f, "level");
    }

    /// <summary>§5.3: the stick tilts it, the tilted rotor pulls it along, and the collective keeps its height.</summary>
    [Test]
    public static void RotorTopSpeedAtFullTilt()
    {
        using var rig = Earth();
        Ship ship = Add(rig, height: 60, spool: 1);
        float start = rig.Height(ship);
        var forward = new ShipInput { Pitch = -1 };
        rig.Fly(ship, forward, 2 * T);
        float tilt = TiltDegrees(rig, ship);
        rig.Fly(ship, forward, 40 * T);
        float speed = Horizontal(rig, ship);
        float lost = start - rig.Height(ship);
        Vector3 up = rig.Up(ship), nose = ship.State.Forward, v = ship.State.Velocity;
        float along = Vector3.Dot(Vector3.Normalize(v - up * Vector3.Dot(v, up)), Vector3.Normalize(nose - up * Vector3.Dot(nose, up)));
        Fixtures.Report($"KA-FA at full tilt: {tilt:0.0}° after 2 s, {speed:0.0} m/s ({speed * 3.6f:0} km/h) after 42 s, height changed {-lost:0.00} m, collective {ship.State.Thrust.Y:0.00}");
        Assert.InRange(27, 31, tilt, "the stick asks for 30° of tilt");
        Assert.InRange(26, 36, speed, "about 30 m/s at full tilt");
        Assert.True(MathF.Abs(lost) < 3, "the collective holds its height while it goes");
        Assert.True(along > 0.99f, "it goes the way its nose points");
    }

    /// <summary>§5.3: let go of the stick and it comes back to level by itself.</summary>
    [Test]
    public static void RotorLevelsItselfWhenLetGo()
    {
        using var rig = Earth();
        Ship ship = Add(rig, height: 60, spool: 1);
        rig.Fly(ship, new ShipInput { Roll = 1 }, 2 * T);
        float rolled = TiltDegrees(rig, ship);
        int ticks = 0;
        while (TiltDegrees(rig, ship) > 2 && ticks < 5 * T)
        {
            rig.Fly(ship, default, 1);
            ticks++;
        }
        Fixtures.Report($"KA-FA: rolled {rolled:0.0}°, level within 2° {ticks / (float)T:0.00} s after letting go");
        Assert.InRange(25, 29, rolled, "the stick asks for 28° of roll");
        Assert.True(ticks < 1.5f * T, "levels itself within a second and a half");
    }

    /// <summary>§5.3: the pedals turn it at up to 100°/s, and at speed a roll becomes a turn (weathercocking).</summary>
    [Test]
    public static void RotorYawsAndBankedTurns()
    {
        using var rig = Earth();
        Ship ship = Add(rig, height: 60, spool: 1);
        rig.Fly(ship, new ShipInput { Yaw = 1 }, T);
        float rate = -ship.State.AngularVelocity.Y * 180 / MathF.PI;
        Assert.InRange(90, 101, rate, "yaws right at its 100°/s");
        rig.Fly(ship, default, 2 * T);

        // Cruise, then roll right: it slips right and the tail turns the nose after it.
        rig.Fly(ship, new ShipInput { Pitch = -1 }, 20 * T);
        Vector3 up = rig.Up(ship);
        float before = ShipMotor.YawOf(ship.State.Rotation, up);
        rig.Fly(ship, new ShipInput { Pitch = -1, Roll = 1 }, 4 * T);
        float after = ShipMotor.YawOf(ship.State.Rotation, rig.Up(ship));
        float turned = (after - before) * 180 / MathF.PI;
        turned = (turned + 540) % 360 - 180;
        Fixtures.Report($"KA-FA: yaw {rate:0} °/s; a 4 s banked turn at speed turned the nose {turned:0}°");
        Assert.True(turned < -25, "a roll to the right at speed turns the nose right (the tail follows the air)");
    }

    /// <summary>M4: the rotor's lift falls with the air, so the climb dies out well below the leaving height.</summary>
    [Test]
    public static void RotorHasACeilingInThinAir()
    {
        using var rig = Earth();
        Ship ship = Add(rig, height: 1000, spool: 1);
        var climb = new ShipInput { Vertical = 1 };
        int ticks = 0;
        float last = rig.Height(ship);
        while (ticks < 600 * T)
        {
            rig.Fly(ship, climb, T);
            ticks += T;
            float now = rig.Height(ship);
            if (now - last < 0.05f)
            {
                break;
            }
            last = now;
        }
        float ceiling = rig.Height(ship);
        float leaving = KaFa.CeilingOn(rig.Zone.Surface.Radius);
        Fixtures.Report($"KA-FA ceiling on Earth: {ceiling:0} m over the ground (the leaving height {leaving:0} m), reached in {ticks / T} s from 1 km");
        Assert.InRange(1200, 2200, ceiling, "tops out over a kilometre up");
        Assert.True(ceiling < leaving - 150, "it can't reach the leaving height");
    }

    /// <summary>M12: its engine needs someone at the controls; left alone it winds down and falls, and the fall hurts.</summary>
    [Test]
    public static void RotorStopsWithoutAPilotAndFalls()
    {
        using var rig = Earth();
        Ship ship = Add(rig, height: 30, spool: 1);
        rig.Fly(ship, default, T);
        ship.Autopilot = false;
        ship.Input = default;
        rig.Step(T);
        Assert.True(!ship.State.Has(ShipFlags.EnginesOn) && ship.State.Spool < 0.9f, "the engine stopped and the rotor winds down");
        int ticks = 0;
        while (!ship.State.Has(ShipFlags.Destroyed) && rig.Height(ship) > 0.2f && ticks < 10 * T)
        {
            rig.Step();
            ticks++;
        }
        rig.Step(T);
        Fixtures.Report($"KA-FA left at 30 m: down in {ticks / (float)T:0.0} s, hull {ship.State.Health:0} of 750");
        Assert.True(ship.State.Health < 750 * 0.85f, "a fall from 30 m costs at least Rust's 15%");
    }

    /// <summary>§5.3: holding S near the ground eases it down; on its skids the stick does nothing.</summary>
    [Test]
    public static void RotorLandsGentlyAndSits()
    {
        using var rig = Earth();
        Ship ship = Add(rig, height: 25, spool: 1);
        var down = new ShipInput { Vertical = -1 };
        int ticks = 0;
        while (!ship.State.Has(ShipFlags.Touching) && ticks < 30 * T)
        {
            rig.Fly(ship, down, 1);
            ticks++;
        }
        rig.Fly(ship, down, 2 * T);
        Fixtures.Report($"KA-FA landing from 25 m: {ticks / (float)T:0.0} s, hull {ship.State.Health:0}");
        Assert.True(ship.State.Has(ShipFlags.Touching), "it came down onto its skids");
        Assert.Equal(750f, ship.State.Health, "a gentle landing costs nothing");
        rig.Fly(ship, new ShipInput { Pitch = -1, Roll = 1 }, 3 * T);
        Assert.True(TiltDegrees(rig, ship) < 3 && rig.Height(ship) < 0.3f, "sitting, the stick doesn't tip it over");
        Assert.True(ship.State.Spool > 0.99f, "the rotor still turns");
    }

    /// <summary>A dry tank stops the engine: the rotor's lift goes and it falls.</summary>
    [Test]
    public static void RotorOutOfFuelFalls()
    {
        using var rig = Earth();
        Ship ship = Add(rig, height: 40, spool: 1, fuel: 1);
        rig.Fly(ship, default, 5 * T);
        Assert.Equal(0f, ship.State.Fuel, "the tank ran dry");
        Assert.True(Climb(rig, ship) < -5, "falling");
    }

    /// <summary>M10: Rust's rule: nothing at 5 m/s, at least 15% above, 30 m/s fatal, five times as much on its side.</summary>
    [Test]
    public static void RotorCrashesHurtAsRusts()
    {
        ShipDef def = KaFa;
        Assert.Equal(0f, def.CrashDamage(4.9f, 1), "under 5 m/s: nothing");
        Assert.Near(0.15 * 750, def.CrashDamage(6, 1), 1e-3, "just over: Rust's least, 15%");
        Assert.Near(0.6 * 750, def.CrashDamage(20, 1), 1e-3, "20 m/s: three fifths of the way to fatal");
        Assert.Near(750, def.CrashDamage(30, 1), 1e-3, "30 m/s: the whole hull");
        Assert.Near(5 * 0.15 * 750, def.CrashDamage(6, 0.3f), 1e-3, "tipped past 60°: five times");
        Assert.True(ContentFixture.Content.Ship("tin-finch")!.CrashDamage(10, 1) == 16 * 2.5f, "the Tin Finch keeps its own rule");
    }

    /// <summary>§5.5: the turning rotor meeting the ground (tipped over) is a strike: the blades stop and the hull pays.</summary>
    [Test]
    public static void RotorStrikeStopsTheBlades()
    {
        using var rig = Earth();
        Ship ship = Add(rig, height: 1.0f, spool: 1);
        // Rolled 80° onto its side near the ground: the disc's edge is in the ground.
        ship.State.Rotation = Quaternion.Normalize(ship.State.Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 80 * MathF.PI / 180));
        ship.State.Position = rig.Zone.GroundAt(0, 0) + rig.Up(ship) * 1.2f;
        rig.Fly(ship, default, 1);
        Fixtures.Report($"KA-FA rotor strike: spool {ship.State.Spool:0.00}, hull {ship.State.Health:0}, strike {ship.Events.Strike}");
        Assert.True(ship.Events.Strike, "the disc met the ground while turning");
        Assert.Near(0, ship.State.Spool, 0.05, "the blades stopped");
        Assert.True(ship.State.Health <= 750 * (1 - KaFa.StrikeShare) + 1e-3f, "the hull takes the strike's share");

        // A rotor that isn't turning is no strike: a parked craft on its side.
        using var rig2 = Earth();
        Ship parked = Add(rig2, height: 1.0f, spool: 0);
        parked.State.Rotation = ship.State.Rotation;
        parked.State.Position = rig2.Zone.GroundAt(0, 0) + rig2.Up(parked) * 1.2f;
        parked.Autopilot = false;
        rig2.Step();
        Assert.True(!parked.Events.Strike, "a still rotor strikes nothing");
    }

    /// <summary>§5.1: in the sea the engine drowns: no lift, and it won't run while it is under.</summary>
    [Test]
    public static void RotorDrownsInTheSea()
    {
        using var rig = Earth(new FloodedSurface(ZoneSpec.StartingWorld(10, 20).Radius));
        Ship ship = Add(rig, height: 2, spool: 1);
        rig.Fly(ship, default, 6 * T);
        float spool = ship.State.Spool;
        rig.Fly(ship, new ShipInput { Vertical = 1 }, 4 * T);
        Fixtures.Report($"KA-FA in the sea: spool {spool:0.00} after 6 s, then {ship.State.Spool:0.00}; wet {ship.State.Has(ShipFlags.Wet)}, climb {Climb(rig, ship):0.00} m/s");
        Assert.True(ship.State.Has(ShipFlags.Wet), "afloat in the sea");
        Assert.True(ship.State.Spool < 0.3f, "the drowned engine let the rotor stop");
        Assert.True(Climb(rig, ship) < 0.5f, "it can't climb out");
    }

    /// <summary>The step is a pure function of the state and input: two runs give the same bits (the pilot's prediction).</summary>
    [Test]
    public static void RotorStepIsDeterministic()
    {
        ShipState Run()
        {
            using var rig = Earth();
            Ship ship = Add(rig);
            var inputs = new[] { new ShipInput { Vertical = 1 }, new ShipInput { Pitch = -1, Yaw = 0.5f }, new ShipInput { Roll = -0.7f, Vertical = -1 } };
            for (int i = 0; i < 18 * T; i++)
            {
                rig.Fly(ship, inputs[i / (6 * T)], 1);
            }
            return ship.State;
        }
        ShipState a = Run(), b = Run();
        Assert.True(a.Equals(b), "the same state and inputs give the same state");
        Assert.True(a.Spool == 1 && a.Position != Vector3.Zero, "it flew");
    }

    /// <summary>§9: the spool survives the ship's storage (codec 7), so a hovering craft still hovers after a restart.</summary>
    [Test]
    public static void RotorSpoolSurvivesStorage()
    {
        using var rig = Earth();
        Ship ship = Add(rig, height: 40, spool: 0.75f);
        byte[] bytes = ShipCodec.Encode(ship, rig.Zone.Frame);
        Ship back = ShipCodec.Decode(ship.Id, bytes, ContentFixture.Content, rig.Zone.Frame)!.Ship;
        Assert.Near(0.75, back.State.Spool, 0, "the spool comes back");
        bytes[^ShipCodec.SpoolBytes..].AsSpan().Clear();
        bytes[^1] = 0x7F;
        bytes[^2] = 0xC0; // NaN
        Ship sane = ShipCodec.Decode(ship.Id, bytes, ContentFixture.Content, rig.Zone.Frame)!.Ship;
        Assert.Equal(0f, sane.State.Spool, "a spool that isn't a number comes back still");
    }
}
