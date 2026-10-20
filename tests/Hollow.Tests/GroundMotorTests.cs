using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Ships.Modules;
using Hollow.Simulation.Movement;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>
/// VEHICLES.md §6.2, V2: the ground motor on the TOGG (a one-seat cab in its front bay, an engine under its hood). It
/// stands on its four sprung wheels, drives, steers, brakes, holds still and sleeps; the same inputs give the same bits;
/// it climbs what its tyres grip and not a wall; it goes round the ball on the ground.
/// </summary>
public static class GroundMotorTests
{
    private static GameContent C => ContentFixture.Content;

    private static Installed Cab => new(2, 1, C.ItemByKey("togg-cab-one").Id);

    private static Installed Engine => new(3, 1, C.ItemByKey("togg-engine").Id);

    private static ShipDef Togg() => C.Compose(C.Ship("togg")!, new ShipBuild(new[] { Cab, Engine }));

    /// <summary>The yaw that faces east (the zone's +X), where a test slope rises.</summary>
    private const float East = -MathF.PI / 2;

    /// <summary>
    /// The game's data with nothing growing on the stand-in ground (every natural kind's stand-in density 0): its trees and
    /// rocks stand everywhere past 9 m of the landing site, and a car driven into them crashes, as it should.
    /// </summary>
    private static readonly Lazy<GameContent> Bare = new(() =>
    {
        Dictionary<string, string> texts = GameContent.ReadTexts(ContentFixture.Folder);
        texts["gather.json"] = Regex.Replace(texts["gather.json"], "\"perTile\": [0-9.]+", "\"perTile\": 0");
        GameContent content = GameContent.FromTexts(texts);
        Assert.Equal("", string.Join("\n", content.Problems), "the bare data reads");
        return content;
    });

    private sealed class Drive : IDisposable
    {
        public readonly Zone Zone;
        public readonly Ship Car;
        public readonly Entity Driver;

        public Drive(float slope = 0, float yaw = East)
        {
            // The ships' tests' world (10° N, 20° E, 0.92 g), flat or rising east.
            var spec = new ZoneSpec
            {
                Radius = ShipRig.Earthlike, SurfaceGravity = 0.92, LatitudeDegrees = 10, LongitudeDegrees = 20,
                AirDensity = ZoneSpec.Air(2.23, 291, 0.92).Density, AirScaleHeight = ZoneSpec.Air(2.23, 291, 0.92).ScaleHeight,
            };
            ISurface surface = slope == 0 ? new LevelSurface(spec.Radius)
                : new SlopeSurface(spec.Radius, ZoneFrame.At(new LevelSurface(spec.Radius), 10 * Math.PI / 180, 20 * Math.PI / 180).East, slope);
            Zone = new Zone(surface, spec, content: Bare.Value) { UnixMilliseconds = 1_800_000_000_000 };
            Zone z = Zone;
            Vector3 ground = z.GroundAt(0, 0);
            Driver = z.Players().AddPlayer(z.NextId(), "Ada", z.NewCharacter(ground + z.Frame.UpAt(ground) * 3 + new Vector3(4, 0, 0)));
            Car = z.Ships().SpawnShip(Bare.Value.Ship("togg")!, ground, yaw, z.Players().PlayerOf(Driver)!.Id, 100);
            Assert.True(z.Ships().Reseat(Driver, Car, Car.Def.Helm.Index), "Ada in the driver's seat");
        }

        public void Go(ShipInput input, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Car.Input = input;
                Zone.UnixMilliseconds += 1000 / CharacterMotor.TickRate;
                Zone.Step();
            }
        }

        public float Height => Zone.HeightAboveGround(Car.Origin);

        public float Speed => Car.State.Velocity.Length();

        public float Along => Vector3.Dot(Car.State.Velocity, Car.State.Forward);

        public float Upright => Vector3.Dot(Car.State.Up, Zone.Frame.UpAt(Car.State.Position));

        public float Heading => ShipMotor.YawOf(Car.State.Rotation, Zone.Frame.UpAt(Car.State.Position));

        public void Dispose() => Zone.Dispose();
    }

    [Test]
    public static void TheToggIsAKindOnWheels()
    {
        ShipDef kind = C.Ship("togg")!;
        Assert.True(kind.Motor is GroundMotor && kind.Ground != null && kind.Wheels.Count == 4 && kind.NeedsPilot, "a ground motor, four wheels, a driver needed");
        Assert.True(kind.Seats.Count == 0 && kind.Sockets.Count == 4, "its seats and engines come with its modules");
        ShipDef d = Togg();
        Assert.True(kind.DefaultBuild.Count == 2 && kind.DefaultBuild.Equals(d.Build), "made with its cab and engine by default");
        Assert.True(d.Helm.Key == "bay3.1.driver" && d.Helm.Flies, "the cab's seat drives");
        Assert.Near(90_000, d.EnginePower, 1e-3, "the engine's power");
        Assert.Near(650 + 220 + 280, d.Mass, 1e-3, "the chassis, the cab and the engine");
        // A second cab behind the first: the front one drives, the one behind rides.
        ShipDef two = C.Compose(kind, new ShipBuild(new[] { Cab, Engine, new Installed(1, 1, Cab.Module) }));
        Assert.True(two.Helm.Key == "bay3.1.driver" && two.Seats.Count == 2 && two.Seats[0].Role == SeatRole.Passenger, "the front cab drives");
    }

    [Test]
    public static void ItStandsDrivesTurnsBrakesAndRests()
    {
        using var t = new Drive();
        Ship car = t.Car;
        t.Go(default, 20);
        float sag = t.Height;
        Fixtures.Report($"standing: the origin {sag:0.000} m over the ground, upright {t.Upright:0.0000}, {car.Events.Contacts} wheels down");
        Assert.True(car.Events.Contacts == 4 && car.State.Has(ShipFlags.Landed), "on its four wheels");
        t.Go(default, 70);
        Assert.True(MathF.Abs(sag) < 0.15f && t.Upright > 0.999f && t.Speed < 0.05f, "level, at its ride height, still");
        Vector3 start = car.State.Position;
        t.Go(new ShipInput { Throttle = 1 }, 300);
        Fixtures.Report($"ten seconds of full throttle: {t.Speed:0.0} m/s, {Vector3.Distance(start, car.State.Position):0} m, upright {t.Upright:0.000}");
        Assert.True(t.Along > 14 && t.Speed < 20.5f, "up to its top speed (20 m/s) and not past it");
        Assert.True(t.Upright > 0.98f && car.State.Has(ShipFlags.Landed), "on its wheels");
        float heading = t.Heading;
        t.Go(new ShipInput { Throttle = 0.5f, Yaw = 1 }, 90);
        float turned = MathF.IEEERemainder(heading - t.Heading, MathF.Tau);
        Fixtures.Report($"three seconds steering right: turned {turned * 180 / MathF.PI:0} degrees at {t.Speed:0.0} m/s, upright {t.Upright:0.000}");
        Assert.True(turned > 0.5f && t.Upright > 0.9f, "it turns right and stays on its wheels");
        t.Go(new ShipInput { Throttle = 1 }, 60);
        Vector3 braking = car.State.Position;
        float from = t.Along;
        int ticks = 0;
        while (t.Along > 0.5f && ticks < 300)
        {
            t.Go(new ShipInput { Throttle = -1 }, 1);
            ticks++;
        }
        float distance = Vector3.Distance(braking, car.State.Position);
        Fixtures.Report($"braking from {from:0.0} m/s: stopped in {ticks / 30f:0.0} s and {distance:0.0} m");
        Assert.True(ticks < 150 && distance < 45, "the brakes stop it");
        t.Go(new ShipInput { Throttle = -1 }, 60);
        Assert.True(t.Along < -2, "held back, it reverses");
        t.Go(default, 150);
        Assert.True(car.State.Asleep && t.Speed == 0, "let go, it holds still and sleeps");
    }

    [Test]
    public static void TheSameInputsGiveTheSameBits()
    {
        ShipState Run()
        {
            using var t = new Drive();
            t.Go(new ShipInput { Throttle = 1 }, 120);
            t.Go(new ShipInput { Throttle = 0.6f, Yaw = -0.7f }, 60);
            t.Go(new ShipInput { Throttle = -1, Vertical = 1 }, 30);
            return t.Car.State;
        }
        ShipState a = Run(), b = Run();
        Assert.True(a.Equals(b), "two runs end in the same state, to the bit");
    }

    [Test]
    public static void ItClimbsWhatItsTyresGripAndNotAWall()
    {
        foreach ((float slope, bool climbs) in new[] { (20f, true), (42f, false) })
        {
            using var t = new Drive(slope);
            float low = t.Zone.HeightAboveGround(t.Car.Origin);
            Vector3D start = t.Zone.Frame.ToBody(t.Car.Origin);
            t.Go(default, 30);
            t.Go(new ShipInput { Throttle = 1 }, 240);
            double rise = t.Zone.Frame.ToBody(t.Car.Origin).Length - start.Length;
            Fixtures.Report($"{slope}°: rose {rise:0.0} m in eight seconds, {t.Along:0.0} m/s, upright {t.Upright:0.00}");
            Assert.True(climbs ? rise > 15 : rise < 6, climbs ? "it climbs 20°" : "it can't climb 42°");
            _ = low;
        }
    }

    [Test]
    public static void ItGoesRoundTheBallOnTheGround()
    {
        using var t = new Drive();
        Vector3D start = t.Zone.Frame.ToBody(t.Car.Origin);
        t.Go(new ShipInput { Throttle = 1 }, 1800);
        Vector3D end = t.Zone.Frame.ToBody(t.Car.Origin);
        double radius = t.Zone.Surface.Radius;
        double arc = Math.Acos(Math.Clamp(Vector3D.Dot(start, end) / (start.Length * end.Length), -1, 1)) * radius;
        Fixtures.Report($"a minute flat out: {arc:0} m round a world {radius:0} m in radius, {t.Height:0.00} m over the ground, upright {t.Upright:0.000}");
        Assert.True(arc > 900, "about a kilometre round the ball");
        Assert.True(MathF.Abs(t.Height) < 0.3f && t.Upright > 0.98f && t.Car.State.Has(ShipFlags.Landed), "still on its wheels, up the ball's way");
    }
}
