using System;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>
/// A test ground tilted about the north–south line through the landing site: it rises toward the east by
/// <c>tan(slope)</c> a metre, at the mean radius on that line (the body-frame vector <paramref name="east"/> is the local
/// east at the landing site).
/// </summary>
public sealed class SlopeSurface : ISurface
{
    private readonly Vector3D _east;
    private readonly double _rise;

    public SlopeSurface(double radius, Vector3D east, double slopeDegrees)
    {
        Radius = radius;
        _east = east;
        _rise = Math.Tan(slopeDegrees * Math.PI / 180);
    }

    public double Radius { get; }

    public double SeaRadius => 0;

    public double HeightAt(Vector3D direction) => _rise * Radius * Vector3D.Dot(direction, _east);

}

/// <summary>A zone with ships (and characters when asked), stepped by hand; the first world's gravity and air.</summary>
public sealed class ShipRig : IDisposable
{
    private long _ids = 1;

    public const double Earthlike = ZoneSpec.FirstWorldRadius;

    public ShipRig(ISurface? surface = null, bool content = false, double slopeDegrees = 0, ZoneSpec? worldSpec = null)
    {
        var spec = worldSpec ?? new ZoneSpec
        {
            Radius = Earthlike, SurfaceGravity = 0.92, LatitudeDegrees = 10, LongitudeDegrees = 20,
            AirDensity = ZoneSpec.Air(2.23, 291, 0.92).Density, AirScaleHeight = ZoneSpec.Air(2.23, 291, 0.92).ScaleHeight,
        };
        if (surface == null && slopeDegrees != 0)
        {
            ZoneFrame flat = new LevelSurface(spec.Radius).Pipe(s => ZoneFrame.At(s, spec.LatitudeDegrees * Math.PI / 180, spec.LongitudeDegrees * Math.PI / 180));
            surface = new SlopeSurface(spec.Radius, flat.East, slopeDegrees);
        }
        Zone = new Zone(surface ?? new LevelSurface(spec.Radius), spec, content: content ? ContentFixture.Content : null)
        {
            UnixMilliseconds = 1_800_000_000_000,
        };
        Zone.NewEntityId = () => new EntityId(0x200_0000 + (ulong)_ids++);
    }

    public Zone Zone { get; }

    public static ShipDef Finch => ContentFixture.Content.Ship("tin-finch")!;

    /// <summary>A Tin Finch standing on the ground at a point of the tangent plane (or <paramref name="height"/> above it).</summary>
    public Ship Add(float x = 0, float z = 0, float height = 0, float yaw = 0, float fuel = 500)
    {
        Vector3 ground = Zone.GroundAt(x, z);
        Ship ship = Zone.Ships().SpawnShip(Finch, ground + Zone.Frame.UpAt(ground) * height, yaw, EntityId.None, fuel);
        return ship;
    }

    /// <summary>Steps with a pilot's controls every tick (the engines run).</summary>
    public void Fly(Ship ship, ShipInput input, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            ship.Autopilot = true;
            ship.Input = input;
            Step();
        }
    }

    public void Step(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
        {
            Zone.UnixMilliseconds += 1000 / CharacterMotor.TickRate;
            Zone.Step();
        }
    }

    /// <summary>Height of the ship's pivot over the ground below it.</summary>
    public float Height(Ship ship) => Zone.HeightAboveGround(ship.Origin);

    public Vector3 Up(Ship ship) => Zone.Frame.UpAt(ship.State.Position);

    public void Dispose() => Zone.Dispose();
}

internal static class PipeExtensions
{
    public static TOut Pipe<TIn, TOut>(this TIn value, Func<TIn, TOut> f) => f(value);
}

public static class ShipTests
{
    // The rig flies ships on the zone's autopilot (engines on, the given controls, nobody in the seat).

    /// <summary>GROUND.md §6.7: flight assist hovers against gravity, with no input, and stops a sink.</summary>
    [Test]
    public static void HoversUnderFlightAssist()
    {
        using var rig = new ShipRig();
        Ship ship = rig.Add(height: 60);
        var hover = new ShipInput();
        rig.Fly(ship, hover, 30);
        float start = rig.Height(ship);
        rig.Fly(ship, hover, 10 * CharacterMotor.TickRate);
        float drift = MathF.Abs(rig.Height(ship) - start);
        float speed = ship.State.Velocity.Length();
        Fixtures.Report($"hover: drifted {drift * 1000:0.0} mm in 10 s, {speed * 1000:0.00} mm/s, fuel {500 - ship.State.Fuel:0.0} used, lift {ship.State.Thrust.Y:0.00} of the nozzles");
        Assert.True(drift < 0.05f && speed < 0.01f, $"hovers in place (drift {drift} m, speed {speed} m/s)");
        Assert.InRange(0.4, 0.6, ship.State.Thrust.Y, "the lift nozzles carry the weight at about half their thrust");

        // Falling at 15 m/s, flight assist catches it.
        ship.State.Velocity = -rig.Up(ship) * 15f;
        float before = rig.Height(ship);
        int ticks = 0;
        while (Vector3.Dot(ship.State.Velocity, rig.Up(ship)) < -0.05f && ticks < 300)
        {
            rig.Fly(ship, hover, 1);
            ticks++;
        }
        float lost = before - rig.Height(ship);
        Fixtures.Report($"a 15 m/s sink stopped in {ticks / 30.0:0.00} s and {lost:0.0} m");
        Assert.True(ticks < 60 && lost < 15, "flight assist stops a 15 m/s sink within 2 s and 15 m");
    }

    /// <summary>
    /// docs/WHOLE_WORLD.md §13, the walk round the world, flown: a Tin Finch 100 m up flies straight ahead at full throttle
    /// with the stick still, flight assist on. The ground turns its attitude and its way with it (§6.5), so it goes north
    /// over the pole, through the far side and over the other pole, and comes back over its start within a metre, at its
    /// height, facing the way it set off.
    /// </summary>
    [Test]
    public static void FlyRoundTheWorld()
    {
        using var rig = new ShipRig();
        Ship ship = rig.Add(height: 100, yaw: 0);
        Vector3 nose = Vector3.Transform(Vector3.UnitZ, ship.State.Rotation);
        var lap = new Lap(ship.State.Position, nose);
        float low = float.MaxValue, high = float.MinValue;
        int ticks = 0;
        while (!lap.Round && ticks++ < 30 * 300)
        {
            rig.Fly(ship, new ShipInput { Throttle = 1 }, 1);
            lap.Add(ship.State.Position);
            low = MathF.Min(low, rig.Height(ship));
            high = MathF.Max(high, rig.Height(ship));
        }
        Vector3 now = Vector3.Transform(Vector3.UnitZ, ship.State.Rotation);
        float facing = MathF.Acos(Math.Clamp(Vector3.Dot(now, nose), -1, 1)) * 180 / MathF.PI;
        Fixtures.Report($"round the world in {ticks / 30.0:0} s: back {lap.Back * 100:0.0} cm from the start, at most {lap.Astray * 100:0.0} cm off its " +
            $"circle, over the poles {lap.North * 100:0.0} and {lap.South * 100:0.0} cm from their axis, {low:0.0} to {high:0.0} m up; " +
            $"facing {facing:0.000} degrees from the way it set off, fuel {ship.State.Fuel:0.0}");
        Assert.True(lap.Round, "went round");
        Assert.True(lap.Back < 1, "back over its start within a metre");
        Assert.True(lap.North < 1 && lap.South < 1, "over both poles");
        Assert.True(low > 95 && high < 105, "at its height");
        Assert.True(facing < 0.5f, "facing the way it set off");
    }

    /// <summary>GROUND.md §6.7: flight assist brakes to the throttle's speed; with it off the ship coasts (the air drags).</summary>
    [Test]
    public static void FlightAssistBrakes()
    {
        using var rig = new ShipRig();
        Ship ship = rig.Add(height: 300, yaw: 0);
        var full = new ShipInput { Throttle = 1 };
        int ticks = 0;
        while (ship.State.Velocity.Length() < 150 && ticks < 60 * 30)
        {
            rig.Fly(ship, full, 1);
            ticks++;
        }
        float top = ship.State.Velocity.Length();
        Fixtures.Report($"full throttle: 150 m/s in {ticks / 30.0:0.0} s");
        Assert.True(top >= 150, "reaches 150 m/s at full throttle");
        rig.Fly(ship, full, 5 * 30);
        float cruise = ship.State.Velocity.Length();
        Assert.Near(ShipRig.Finch.ForwardSpeed, cruise, 2, "holds the throttle's speed");

        // Throttle to zero: flight assist brakes with the retro thrusters (and the air).
        Vector3 from = ship.State.Position;
        int braking = 0;
        while (ship.State.Velocity.Length() > 0.5f && braking < 60 * 30)
        {
            rig.Fly(ship, new ShipInput(), 1);
            braking++;
        }
        float distance = Vector3.Distance(from, ship.State.Position);
        Fixtures.Report($"flight assist on: from {cruise:0} m/s to a stop in {braking / 30.0:0.0} s over {distance:0} m");
        Assert.True(braking < 15 * 30, "stops within 15 s");
        Assert.True(MathF.Abs(rig.Height(ship) - 300) < 20, "holds its height while braking");

        // The same with flight assist off: nothing brakes but the air.
        rig.Fly(ship, full, 20 * 30);
        cruise = ship.State.Velocity.Length();
        rig.Fly(ship, new ShipInput { Buttons = ShipButtons.AssistOff }, 3 * 30);
        float coasting = ship.State.Velocity.Length();
        Fixtures.Report($"flight assist off: {cruise:0} m/s coasted to {coasting:0} m/s in 3 s (drag only), falling {-Vector3.Dot(ship.State.Velocity, rig.Up(ship)):0.0} m/s");
        Assert.True(coasting > cruise * 0.4f && coasting < cruise, "coasts, slowed by the air alone");
        Assert.True(Vector3.Dot(ship.State.Velocity, rig.Up(ship)) < -5, "and falls: nothing holds it up");
    }

    /// <summary>GROUND.md §6.7: a ship settles on its skids on a slope it may land on, and won't rest on a steeper one.</summary>
    [Test]
    public static void LandsOnASlope()
    {
        foreach ((double slope, bool lands) in new[] { (0.0, true), (15.0, true), (24.0, true), (40.0, false) })
        {
            using var rig = new ShipRig(slopeDegrees: slope);
            Ship ship = rig.Add(height: 12, yaw: 0.7f);
            var down = new ShipInput { Vertical = -1 };
            int ticks = 0;
            while (!ship.State.Asleep && ticks < 40 * 30)
            {
                rig.Fly(ship, down, 1);
                ticks++;
            }
            // Engines off: does it stay?
            ship.Autopilot = false;
            Vector3 rest = ship.State.Position;
            rig.Step(5 * 30);
            float moved = Vector3.Distance(rest, ship.State.Position);
            (Vector3 normal, float ground) = rig.Zone.GroundNormal(ship.Origin);
            float tilt = MathF.Acos(Math.Clamp(Vector3.Dot(ship.State.Up, normal), -1, 1)) * 180 / MathF.PI;
            Fixtures.Report(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{slope,4:0}° slope: {(ship.State.Has(ShipFlags.Landed) ? "landed" : "not landed")} after {ticks / 30.0:0.0} s, " +
                $"{(ship.State.Asleep ? "asleep" : "awake")}, moved {moved * 100:0.0} cm in 5 s with the engines off, up {tilt:0.0}° off the ground's normal, " +
                $"hull {ship.State.Health:0}"));
            if (lands)
            {
                Assert.True(ship.State.Has(ShipFlags.Landed) && ship.State.Asleep, $"rests on a {slope}° slope");
                Assert.True(moved < 0.05f, $"stays put on a {slope}° slope ({moved} m)");
                Assert.True(tilt < 3, $"stands on its skids along the ground ({tilt:0.0}°)");
                Assert.Equal(ShipRig.Finch.Health, ship.State.Health, "an easy landing costs nothing");
            }
            else
            {
                Assert.True(!ship.State.Has(ShipFlags.Landed) && moved > 1, $"slides off a {slope}° slope");
            }
        }
    }

    /// <summary>GROUND.md §6.7: the skids take a hard landing; a crash costs hull by the speed; a fall from high wrecks it.</summary>
    [Test]
    public static void CrashDamage()
    {
        foreach ((float height, float least, float most) in new[] { (1.5f, 0f, 0f), (6f, 0f, 60f), (40f, 750f, 750f) })
        {
            using var rig = new ShipRig();
            Ship ship = rig.Add(height: height);
            float worst = 0;
            for (int i = 0; i < 10 * 30 && !ship.State.Asleep && !ship.Destroyed; i++)
            {
                rig.Step();
                worst = MathF.Max(worst, ship.Events.ImpactSpeed);
            }
            float lost = ShipRig.Finch.Health - ship.State.Health;
            Fixtures.Report(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"dropped from {height,4:0.0} m, engines off: hit at {worst:0.0} m/s into the hull, lost {lost:0} hull{(ship.Destroyed ? " (destroyed)" : "")}"));
            Assert.InRange(least, most, lost, $"damage from {height} m");
        }

        // Flown into a wall of ground at speed: an impact along the nose.
        using var wall = new ShipRig(slopeDegrees: 60);
        Ship fast = wall.Add(x: -60, height: 40, yaw: -MathF.PI / 2);
        var forward = new ShipInput { Throttle = 0.25f };
        for (int i = 0; i < 8 * 30 && !fast.Destroyed && fast.Events.ImpactSpeed == 0; i++)
        {
            wall.Fly(fast, forward, 1);
        }
        Fixtures.Report(FormattableString.Invariant($"flown into a hillside at {forward.Throttle * ShipRig.Finch.ForwardSpeed:0} m/s: impact {fast.Events.ImpactSpeed:0.0} m/s, hull {fast.State.Health:0}"));
        Assert.True(fast.Events.ImpactSpeed > 20 && fast.State.Health < ShipRig.Finch.Health - 300, "a nose-first crash hurts");
    }
}
