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
using Hollow.Client;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Targets;
using System.Linq;
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

    private static Installed Tyres => new(5, 1, C.ItemByKey("togg-tyres").Id);

    private static ShipDef Togg() => C.Compose(C.Ship("togg")!, new ShipBuild(new[] { Cab, Engine, Tyres }));

    /// <summary>The yaw that faces east (the zone's +X), where a test slope rises.</summary>
    private const float East = -MathF.PI / 2;

    /// <summary>
    /// The game's data with nothing growing on the stand-in ground (every natural kind's stand-in density 0): its trees and
    /// rocks stand everywhere past 9 m of the landing site, and a car driven into them crashes, as it should.
    /// </summary>
    internal static readonly Lazy<GameContent> Bare = new(() =>
    {
        Dictionary<string, string> texts = GameContent.ReadTexts(ContentFixture.Folder);
        texts["gather.json"] = Regex.Replace(texts["gather.json"], "\"perTile\": [0-9.]+", "\"perTile\": 0");
        GameContent content = GameContent.FromTexts(texts);
        Assert.Equal("", string.Join("\n", content.Problems), "the bare data reads");
        return content;
    });

    internal sealed class Drive : IDisposable
    {
        public readonly Zone Zone;
        public readonly Ship Car;
        public readonly Entity Driver;

        public Drive(float slope = 0, float yaw = East, string kind = "togg")
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
            Car = z.Ships().SpawnShip(Bare.Value.Ship(kind)!, ground, yaw, z.Players().PlayerOf(Driver)!.Id, 100);
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
        Assert.True(kind.Motor is GroundMotor && kind.Ground != null && kind.Wheels.Count == 0 && kind.NeedsPilot, "a ground motor, a driver needed");
        Assert.True(kind.Seats.Count == 0 && kind.Sockets.Count == 6, "its seats, engines and running gear come with its modules (VEHICLES.md §9.11)");
        ShipDef d = Togg();
        Assert.True(kind.DefaultBuild.Count == 3 && kind.DefaultBuild.Equals(d.Build) && d.Wheels.Count == 4, "made with its cab, engine and four tyres by default");
        Assert.True(d.Helm.Key == "bay3.1.driver" && d.Helm.Flies, "the cab's seat drives");
        Assert.Near(90_000, d.EnginePower, 1e-3, "the engine's power");
        Assert.Near(530 + 220 + 280 + 120, d.Mass, 1e-3, "the chassis, the cab, the engine and the tyres");
        // A second cab behind the first: the front one drives, the one behind rides.
        ShipDef two = C.Compose(kind, new ShipBuild(new[] { Cab, Engine, Tyres, new Installed(1, 1, Cab.Module) }));
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

    /// <summary>
    /// VEHICLES.md §6.4: a person standing in the TOGG's way is unharmed while it stands, and hurt when it drives into them
    /// above a walk, by its driver, thrown along; the one in its seat isn't.
    /// </summary>
    [Test]
    public static void ItRunsPeopleOverAboveAWalk()
    {
        using var t = new Drive();
        Zone z = t.Zone;
        Vector3 up = z.Frame.UpAt(t.Car.State.Position);
        Entity bo = z.Players().AddPlayer(z.NextId(), "Bo", z.NewCharacter(z.Settle(t.Car.Origin + t.Car.State.Forward * 30) + up * 0.05f));
        Hollow.Simulation.Players.Player pb = z.Players().PlayerOf(bo)!;
        Hollow.Simulation.Players.Player ada = z.Players().PlayerOf(t.Driver)!;
        float health = pb.Vitals.Health, driver = ada.Vitals.Health;
        t.Go(default, 30);
        Fixtures.Report($"standing: {z.Ships().RunOvers} hit, Bo's health {health:0.00} then {pb.Vitals.Health:0.00}");
        Assert.True(z.Ships().RunOvers == 0, "a car standing 30 m off hurts nobody");
        health = pb.Vitals.Health;
        t.Go(new ShipInput { Throttle = 1 }, 150);
        Fixtures.Report($"driven at Bo: {z.Ships().RunOvers} hit, Bo's health {pb.Vitals.Health:0} of {health:0}, dead {pb.Dead}, killed by {pb.KilledBy}");
        Assert.True(z.Ships().RunOvers >= 1 && (pb.Vitals.Health < health || pb.Dead), "Bo is run over");
        Assert.True(!pb.Dead || pb.KilledBy == "Ada", "by Ada, if it killed him");
        Assert.True(ada.Vitals.Health >= driver, "the driver isn't run over by her own car");
    }

    /// <summary>
    /// VEHICLES.md §4, §6.3 (a rough first Zamazingo): on its two wheels it stands upright, goes, leans into a turn rather
    /// than falling, and stops upright.
    /// </summary>
    [Test]
    public static void TheZamazingoBalancesAndLeans()
    {
        using var t = new Drive(kind: "zamazingo");
        Ship bike = t.Car;
        t.Go(default, 90);
        Fixtures.Report($"standing: upright {t.Upright:0.0000}, {bike.Events.Contacts} wheels down, {t.Height:0.000} m");
        Assert.True(t.Upright > 0.99f && bike.State.Has(ShipFlags.Landed), "it stands on its two wheels, upright");
        t.Go(new ShipInput { Throttle = 1 }, 180);
        Fixtures.Report($"six seconds of full throttle: {t.Speed:0.0} m/s, upright {t.Upright:0.000}");
        Assert.True(t.Along > 15 && t.Upright > 0.98f, "quick, and upright going straight");
        float heading = t.Heading;
        float leanMost = 0;
        for (int i = 0; i < 60; i++)
        {
            t.Go(new ShipInput { Throttle = 0.6f, Yaw = 0.8f }, 1);
            leanMost = MathF.Max(leanMost, Vector3.Dot(bike.State.Up, -Vector3.Transform(Vector3.UnitX, Quaternion.Identity)) * 0
                + Vector3.Dot(Vector3.Transform(Vector3.UnitX, bike.State.Rotation), t.Zone.Frame.UpAt(bike.State.Position)));
        }
        float turned = MathF.IEEERemainder(heading - t.Heading, MathF.Tau);
        Fixtures.Report($"two seconds steering right: turned {turned * 180 / MathF.PI:0} degrees, leaned right up to {MathF.Asin(leanMost) * 180 / MathF.PI:0} degrees, upright {t.Upright:0.00}");
        Assert.True(turned > 0.3f && leanMost > 0.1f && t.Upright > 0.5f, "it turns right leaning into the turn, and doesn't fall");
        t.Go(new ShipInput { Throttle = -1 }, 90);
        t.Go(default, 90);
        Fixtures.Report($"braked and let go: {t.Speed:0.00} m/s, upright {t.Upright:0.000}, {bike.Events.Contacts} wheels down");
        Assert.True(t.Speed < 0.5f && t.Upright > 0.98f, "stopped, upright");
    }

    /// <summary>
    /// VEHICLES.md §9.1, §9.6 over the real network: the server gives Ada a TOGG (made with its cab and engine by default),
    /// her client and Bo's compose the same layout from its details, she gets in through the cab's door and drives; Bo sees
    /// it go, and on a clean line her prediction is never corrected.
    /// </summary>
    [Test]
    public static void TheToggDrivesOverTheNetwork()
    {
        using var rig = new LoopbackRig();
        Hollow.Client.GameClient ada = rig.Join("Ada");
        Hollow.Client.GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Server.Execute("ship Ada togg");
        rig.Run(1.0, (_, _) => default);
        Hollow.Client.RemoteShip car = ada.Ships().All.First();
        Assert.True(car.Def is { Kind.Key: "togg" } d && d.Build.Count == 3 && d.Seats.Count == 1, "Ada's client composes the TOGG with its cab, engine and tyres");
        Vector3 door = car.PointOf(car.Def!.HatchPoint());
        Vector3 side = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, car.Rotation));
        rig.Teleport("Ada", door + side * 1.2f);
        rig.Run(0.5, (_, _) => default);
        // The cab's door first (E opens it, as the Tin Finch's hatch), then the seat.
        ada.Local.Interact(new TargetRef(TargetFamily.Ship, car.Id.Value, car.Def!.HelmPart()), "hatch-open");
        rig.Run(1.0, (_, _) => default);
        ada.Local.Interact(new TargetRef(TargetFamily.Ship, car.Id.Value, car.Def!.HelmPart()), "drive");
        rig.Run(0.5, (_, _) => default);
        Assert.True(ada.Ships().Piloting, "Ada drives it (her client predicts it)");
        long corrections = ada.Stats.ShipCorrections;
        rig.Run(4.0, (client, time) => client == ada ? new InputFrame { Ship = new ShipInput { Throttle = time < 3 ? 1 : 0, Yaw = time > 1.5 ? 0.5f : 0 } } : default);
        Hollow.Client.RemoteShip? seen = bo.Ships().All.FirstOrDefault(s => s.Id == car.Id);
        Fixtures.Report(FormattableString.Invariant(
            $"Bo sees it at {seen?.Velocity.Length():0.0} m/s, its build {seen?.Def?.Build}; Ada's corrections {ada.Stats.ShipCorrections - corrections}"));
        Assert.True(seen?.Def is { } boDef && boDef.Build.Equals(car.Def!.Build) && boDef.Seats.Count == car.Def.Seats.Count && boDef.Mass == car.Def.Mass,
            "Bo's client composes the same layout (from its own copy of the data)");
        Assert.True(seen!.Velocity.Length() > 3 || Vector3.Distance(seen.Position, door) > 10, "Bo sees it go");
        Assert.Equal(0L, ada.Stats.ShipCorrections - corrections, "Ada's prediction held on a clean line");
    }
}
