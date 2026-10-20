using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>
/// VEHICLES.md §6.4 and §6.5, the owner's two findings of 20 October 2026 on the TOGG: "it doesn't even move when I keep
/// jumping in the back of it even though it has suspensions", and "hold E, select engine on but nothing happens". A
/// person who lands on its deck stands on it and rocks it on its springs, their weight sets it lower, a jump off pushes it,
/// and it rests again; ENGINE ON from the radial starts a vehicle's engine with nobody in it, idling hands off.
/// </summary>
public static class VehicleRiderTests
{
    // The game's data with nothing growing on the stand-in ground (GroundMotorTests' Bare: a car among trees crashes).
    private static readonly Lazy<GameContent> Bare = new(() =>
    {
        Dictionary<string, string> texts = GameContent.ReadTexts(ContentFixture.Folder);
        texts["gather.json"] = Regex.Replace(texts["gather.json"], "\"perTile\": [0-9.]+", "\"perTile\": 0");
        GameContent content = GameContent.FromTexts(texts);
        Assert.Equal("", string.Join("\n", content.Problems), "the bare data reads");
        return content;
    });

    private sealed class Yard : IDisposable
    {
        public readonly Zone Zone;
        public readonly Ship Car;
        public readonly Entity Ada;
        public readonly Player Owner;
        public readonly Vector3 Ground;

        public Yard(string kind = "togg")
        {
            var spec = new ZoneSpec
            {
                Radius = ShipRig.Earthlike, SurfaceGravity = 0.92, LatitudeDegrees = 10, LongitudeDegrees = 20,
                AirDensity = ZoneSpec.Air(2.23, 291, 0.92).Density, AirScaleHeight = ZoneSpec.Air(2.23, 291, 0.92).ScaleHeight,
            };
            Zone = new Zone(new LevelSurface(spec.Radius), spec, content: Bare.Value) { UnixMilliseconds = 1_800_000_000_000 };
            Ground = Zone.GroundAt(0, 0);
            Ada = Zone.Players().AddPlayer(Zone.NextId(), "Ada", Zone.NewCharacter(Zone.GroundAt(8, 0) + Zone.Frame.UpAt(Ground) * 0.5f));
            Owner = Zone.Players().PlayerOf(Ada)!;
            Car = Zone.Ships().SpawnShip(Bare.Value.Ship(kind)!, Ground, 0, Owner.Id, 100);
        }

        public void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Zone.UnixMilliseconds += 1000 / CharacterMotor.TickRate;
                Zone.Step();
            }
        }

        /// <summary>Steps until the car sleeps (or the ticks run out); the ticks it took.</summary>
        public int Settle(int most = 300)
        {
            for (int i = 0; i < most; i++)
            {
                if (Car.State.Asleep)
                {
                    return i;
                }
                Step(1);
            }
            return most;
        }

        public ref CharacterState Person => ref Zone.Entities.Get<Character>(Ada).State;

        /// <summary>A point of the car's own frame (from its pivot), in the zone's.</summary>
        public Vector3 Point(Vector3 local) => Car.State.PointOf(local, Car.Def.CentreOfMass);

        /// <summary>How high the deck's top stands over the ground at a point along the car (m from the rear axle).</summary>
        public float DeckHeight(float along) => Zone.HeightAboveGround(Point(new Vector3(0, 0.98f, along)));

        public void Dispose() => Zone.Dispose();
    }

    [Test]
    public static void APersonJumpingOnItsBackRocksItOnItsSprings()
    {
        using var y = new Yard();
        int took = y.Settle();
        Assert.True(y.Car.State.Asleep && y.Car.ParkedBody != 0, $"the empty TOGG settles and sleeps on a solid body ({took} ticks)");
        float empty = y.DeckHeight(-1.0f), front = y.DeckHeight(3.0f);
        Vector3 up = y.Zone.Frame.UpAt(y.Ground);

        // Ada drops onto the back of the deck from 1.2 m over it.
        y.Person.Position = y.Point(new Vector3(0.2f, 0.98f + 1.2f, -1.0f));
        y.Person.Velocity = Vector3.Zero;
        float lowest = empty, highest = empty;
        bool stood = false;
        for (int i = 0; i < 60; i++)
        {
            y.Step(1);
            lowest = MathF.Min(lowest, y.DeckHeight(-1.0f));
            highest = MathF.Max(highest, y.DeckHeight(-1.0f));
            stood |= (y.Person.Flags & MoveFlags.OnGround) != 0;
        }
        float feet = y.Zone.HeightAboveGround(y.Person.Position);
        Fixtures.Report($"empty deck {empty:0.000} m at the back; Ada lands: it dips to {lowest:0.000}, rebounds to {highest:0.000}; " +
            $"her feet {feet:0.000} m up, the deck there {y.DeckHeight(-1.0f):0.000}");
        Assert.True(stood && MathF.Abs(feet - y.DeckHeight(-1.0f)) < 0.06f, "she stands on the deck, not through it");
        Assert.True(empty - lowest > 0.05f && empty - lowest < 0.2f, $"the landing dips the back on its springs ({(empty - lowest) * 100:0.0} cm)");
        Assert.True(highest - (lowest + empty) / 2 > 0.005f, "and it rocks back up");
        Assert.True(y.Zone.Ships().RiderCount == 1, "one rider");

        took = y.Settle();
        float loaded = y.DeckHeight(-1.0f);
        Fixtures.Report($"rests again in {took} ticks with the back {(empty - loaded) * 100:0.0} cm lower under her; the front {(front - y.DeckHeight(3.0f)) * 100:0.0} cm");
        Assert.True(y.Car.State.Asleep && empty - loaded > 0.01f && empty - loaded < 0.08f, "it rests a little lower at the back under her weight");

        // She jumps: the push sends the back down, and her landing again.
        y.Person.Velocity = up * 4.5f;
        lowest = loaded;
        for (int i = 0; i < 75; i++)
        {
            y.Step(1);
            lowest = MathF.Min(lowest, y.DeckHeight(-1.0f));
        }
        Fixtures.Report($"a jump in place: the back down to {lowest:0.000} (from {loaded:0.000})");
        Assert.True(loaded - lowest > 0.03f, "a jump on its back rocks it again");

        // She steps off (put beside it): it rises back to where it stood empty.
        y.Person.Position = y.Zone.GroundAt(6, 0) + up * 0.3f;
        y.Person.Velocity = Vector3.Zero;
        y.Step(5);
        took = y.Settle();
        Fixtures.Report($"empty again: the back {y.DeckHeight(-1.0f):0.000} (was {empty:0.000}), asleep after {took} ticks");
        Assert.True(y.Car.State.Asleep && MathF.Abs(y.DeckHeight(-1.0f) - empty) < 0.01f, "it rises back and rests");
        Assert.True(y.Zone.Ships().RiderCount == 0, "nobody on it");
    }

    [Test]
    public static void ADrivenVehicleKeepsItsSolidBody()
    {
        using var y = new Yard();
        y.Settle();
        uint body = y.Car.ParkedBody;
        Assert.True(y.Zone.Ships().Reseat(y.Ada, y.Car, y.Car.Def.Helm.Index), "Ada drives");
        y.Car.Input = new ShipInput { Throttle = 1 };
        y.Step(60);
        Assert.True(y.Car.State.Velocity.Length() > 5 && y.Car.ParkedBody == body, $"driving at {y.Car.State.Velocity.Length():0.0} m/s, its body kept");
        Assert.True(y.Zone.Ships().ShipCollider.HasParked(body), "and in the world");
        Assert.True(Vector3.Distance(y.Car.BodyPosition, y.Car.State.Position) < 1e-4f, "where the car is");
    }

    [Test]
    public static void EngineOnFromTheRadialIdlesAVehicleWithNobodyInIt()
    {
        using var y = new Yard();
        y.Settle();
        ShipSystem ships = y.Zone.Ships();
        Vector3 at = y.Car.State.Position;
        float fuel = y.Car.State.Fuel;
        Assert.Equal(OutcomeCode.Done, ships.SetEngine(y.Owner, y.Car, true).Code, "ENGINE ON");
        y.Step(90);
        Assert.True(y.Car.State.Has(ShipFlags.EnginesOn) && y.Car.HandStarted, "it runs with nobody in it");
        Assert.True(y.Car.State.Fuel < fuel, $"burning its idle ({fuel - y.Car.State.Fuel:0.000} in 3 s)");
        Assert.True(Vector3.Distance(at, y.Car.State.Position) < 0.05f, "and stays where it is, the handbrake on");
        Assert.Equal(OutcomeCode.Done, ships.SetEngine(y.Owner, y.Car, false).Code, "ENGINE OFF");
        y.Step(5);
        Assert.True(!y.Car.State.Has(ShipFlags.EnginesOn), "it stops");

        // Switched off by hand, the driver's seat starts it all the same; leaving stops it.
        Assert.True(ships.Reseat(y.Ada, y.Car, y.Car.Def.Helm.Index), "Ada takes the wheel");
        y.Step(3);
        Assert.True(y.Car.State.Has(ShipFlags.EnginesOn), "taking the wheel starts it");
        Assert.Equal(OutcomeCode.Done, ships.LeaveSeat(y.Ada).Code, "Space: out");
        y.Step(5);
        Assert.True(!y.Car.State.Has(ShipFlags.EnginesOn), "leaving stops it");

        // Started by hand, then driven and left: it stops then too (the driver's engine).
        Assert.Equal(OutcomeCode.Done, ships.SetEngine(y.Owner, y.Car, true).Code, "ENGINE ON again");
        Assert.True(ships.Reseat(y.Ada, y.Car, y.Car.Def.Helm.Index), "in");
        y.Step(3);
        Assert.Equal(OutcomeCode.Done, ships.LeaveSeat(y.Ada).Code, "out");
        y.Step(5);
        Assert.True(!y.Car.State.Has(ShipFlags.EnginesOn) && !y.Car.HandStarted, "stopped");

        // A dry tank refuses, and runs a hand-started engine down.
        Assert.Equal(OutcomeCode.Done, ships.SetEngine(y.Owner, y.Car, true).Code, "on");
        y.Car.LoadFuel(0.002f);
        y.Step(30);
        Assert.True(!y.Car.State.Has(ShipFlags.EnginesOn) && !y.Car.HandStarted, "the tank ran dry");
        Assert.Equal(OutcomeCode.NoFuel, ships.SetEngine(y.Owner, y.Car, true).Code, "no fuel, no start");
    }

    [Test]
    public static void TheMonsterToggStandsTallAndGoes()
    {
        using var y = new Yard("monster-togg");
        ShipDef d = y.Car.Def;
        Assert.True(d.Kind?.Key == "monster-togg" && d.Helm.Key == "bay3.1.driver", "the monster with its cab's wheel");
        Assert.Near(450_000, d.EnginePower, 1e-3, "a blown six's power");
        int took = y.Settle();
        float deck = y.Zone.HeightAboveGround(y.Point(new Vector3(0, 1.73f, 1.0f)));
        float upright = Vector3.Dot(y.Car.State.Up, y.Zone.Frame.UpAt(y.Car.State.Position));
        Fixtures.Report($"settles in {took} ticks, its deck {deck:0.00} m up (the TOGG's 0.98), upright {upright:0.0000}");
        Assert.True(y.Car.State.Asleep && MathF.Abs(deck - 1.73f) < 0.08f && upright > 0.999f, "it stands level at its ride height");
        Assert.True(y.Zone.Ships().Reseat(y.Ada, y.Car, d.Helm.Index), "Ada drives");
        y.Car.Input = new ShipInput { Throttle = 1 };
        float at3 = 0;
        for (int i = 0; i < 300; i++)
        {
            y.Step(1);
            if (i == 89) at3 = y.Car.State.Velocity.Length();
        }
        float speed = y.Car.State.Velocity.Length();
        upright = Vector3.Dot(y.Car.State.Up, y.Zone.Frame.UpAt(y.Car.State.Position));
        Fixtures.Report($"flat out: {at3:0.0} m/s after 3 s, {speed:0.0} m/s ({speed * 3.6f:0} km/h) after 10 s, upright {upright:0.000}");
        Assert.True(at3 > 15 && speed > 30 && upright > 0.98f, "it pulls hard and runs past 100 km/h on its wheels");
    }

    [Test]
    public static void EveryKindsEngineStartsByHand()
    {
        foreach (ShipDef kind in Bare.Value.Ships)
        {
            using var y = new Yard(kind.Key);
            y.Settle();
            Vector3 at = y.Car.State.Position;
            Outcome on = y.Zone.Ships().SetEngine(y.Owner, y.Car, true);
            y.Step(60);
            float moved = Vector3.Distance(at, y.Car.State.Position);
            Fixtures.Report($"{kind.Key}: ENGINE ON {on.Code}; after 2 s engines {(y.Car.State.Has(ShipFlags.EnginesOn) ? "on" : "off")}, moved {moved:0.000} m, spool {y.Car.State.Spool:0.00}");
            Assert.Equal(OutcomeCode.Done, on.Code, $"{kind.Key}: ENGINE ON is taken");
            Assert.True(y.Car.State.Has(ShipFlags.EnginesOn), $"{kind.Key}: its engine runs with nobody aboard");
            Assert.True(moved < 0.5f, $"{kind.Key}: and it stays put");
        }
    }
}
