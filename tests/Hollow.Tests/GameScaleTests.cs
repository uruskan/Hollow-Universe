using System;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Surface;

namespace Hollow.Tests;

public static class GameScaleTests
{
    [Test]
    public static void EarthMovementPredictionUsesTheScaledWalkingTerrain()
    {
        ZoneSpec spec = ZoneSpec.StartingWorld(StartingWorld.LandingLatitude, StartingWorld.LandingLongitude)
            with { Ground = GroundKind.Walking };
        var store = new WorldStore(new WorldStoreOptions());
        try
        {
            // Predictor currently borrows the surface. Keep this pin until it and the zone have drained physics.
            using WorldLease lease = store.Acquire(spec.EnvironmentDescriptor());
            var surface = (WalkingWorld)lease.Surface;
            Vector3D site = surface.LandingSite;
            spec = spec with
            {
                LatitudeDegrees = DMath.Asin(site.Y) * DMath.RadToDeg,
                LongitudeDegrees = DMath.Atan2(-site.Z, site.X) * DMath.RadToDeg,
            };
            using var zone = new Zone(lease.Fork(), spec);
            Vector3 start = zone.Landing + zone.Frame.UpAt(zone.Landing) * 0.02f;
            Entity entity = zone.AddCharacter(new EntityId(7), "Earth walker", zone.NewCharacter(start));
            using var predictor = new Predictor(spec, surface, null);
            predictor.World.EnsureGround(start);
            predictor.Reset(0, zone.Entities.Get<Character>(entity).State, 0);
            for (uint tick = 1; tick <= 360; tick++)
            {
                ref Character character = ref zone.Entities.Get<Character>(entity);
                Vector3 up = zone.Frame.UpAt(character.State.Position);
                var move = new MoveInput { Forward = tick <= 240 ? 1 : 0, View = Facing.View(0.3f, 0, up) };
                InputCommand command = NetConvert.ToCommand(move);
                character.Input = NetConvert.ToMoveInput(command);
                zone.Step();
                predictor.RecordInput(tick, command);
                predictor.Predict(tick);
                Assert.True(predictor.Latest.Equals(zone.Entities.Get<Character>(entity).State), $"Earth movement predicted exactly at tick {tick}");
            }
            Vector3 end = zone.Entities.Get<Character>(entity).State.Position;
            Assert.Near(1200, zone.Surface.Radius, 1e-8, "live Earth radius, not the reference-world fixture");
            Assert.True(Vector3.Distance(start, end) > 5, "walked across scaled terrain");
            Assert.True(Vector3.Distance(zone.Frame.UpAt(start), zone.Frame.UpAt(end)) > 0.003f, "radial up follows the small world's curvature");
            Fixtures.Report($"Earth radius {zone.Surface.Radius:0} m; walked {Vector3.Distance(start, end):0.0} m; 360 exact prediction ticks");
        }
        finally { store.DisposeAsync().GetAwaiter().GetResult(); }
    }

    [Test]
    public static void EveryPlanetFitsTheEntireSurfaceAreaBudget()
    {
        Assert.True(GameScale.SurfaceArea(GameScale.MaximumPlanetRadius) <= GameScale.MaximumSurfaceArea,
            "maximum is the complete sphere, with no roundoff overflow");
        Assert.Near(23.04, GameScale.SurfaceArea(GameScale.MaximumPlanetRadius) / 1e6, 1e-12, "area in km²");
        double previous = 0;
        for (int i = 0; i <= 120; i++)
        {
            double realRadius = 100 * DMath.Pow(1.18, i);
            double radius = GameScale.PlanetRadius(realRadius);
            Assert.True(radius >= previous, "real size ordering is preserved");
            Assert.True(GameScale.SurfaceArea(radius) <= GameScale.MaximumSurfaceArea, "no real planet radius exceeds the cap");
            previous = radius;
        }
        Assert.Near(1200, GameScale.PlanetRadius(Units.EarthRadius), 1e-10, "default Earth radius");
        Assert.True(GameScale.PlanetRadius(1_737_000) < GameScale.PlanetRadius(Units.EarthRadius), "Moon is smaller than Earth");
        Assert.True(GameScale.PlanetRadius(Units.EarthRadius, 1000) < GameScale.PlanetRadius(Units.EarthRadius), "tuning can reduce Earth size");
    }

    [Test]
    public static void InvalidScaleInputsAreRejected()
    {
        foreach (double input in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            bool rejected = false;
            try { GameScale.PlanetRadius(input); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert.True(rejected, "invalid real radius is rejected");
        }
        bool invalidAnchor = false;
        try { GameScale.PlanetRadius(Units.EarthRadius, GameScale.MaximumPlanetRadius + 1); }
        catch (ArgumentOutOfRangeException) { invalidAnchor = true; }
        Assert.True(invalidAnchor, "tuning cannot escape the cap");
    }

    [Test]
    public static void EarthStartsWithoutAnOptionalCatalogue()
    {
        var (system, earth) = StartingWorld.Find();
        Assert.Equal("Sol", system.Name, "starting system");
        Assert.Equal("Earth", earth.Name, "starting planet");
        Assert.True(earth.Landable, "Earth has a landable surface");
        ZoneSpec spec = ZoneSpec.StartingWorld(StartingWorld.LandingLatitude, StartingWorld.LandingLongitude);
        Assert.Equal(StartingWorld.System.Value, spec.SystemAddress, "server sends Sol identity");
        Assert.Equal(StartingWorld.BodyPath, spec.BodyPath, "server sends Earth identity");
        Assert.Near(GameScale.PlanetRadius(earth), spec.Radius, 0, "server uses shared game scale");
        Assert.Near(earth.SurfaceGravity, spec.SurfaceGravity, 0, "real gravity remains independent of game radius");
        PlanetBodies.Entry? metadata = PlanetBodies.Find(spec.SystemAddress, spec.BodyPath);
        Assert.True(metadata != null && metadata.Body.Name == "Earth", "client and server resolve built-in Earth without HYG");
        Assert.True(spec.CreateSky() != null, "Earth's sky uses the existing real Solar System");
        Assert.True(PlanetBodies.Find(ulong.MaxValue, uint.MaxValue) == null, "untrusted identities remain rejected");
    }

    [Test]
    public static void ScalingLeavesGeneratedPhysicsUnchanged()
    {
        StarSystem solar = SolSystem.Create(SolSystem.Primary);
        foreach (Body body in solar.Bodies.Where(body => body.Kind is BodyKind.Planet or BodyKind.Moon))
        {
            double radius = body.Radius, mass = body.Mass, gravity = body.SurfaceGravity;
            ZoneSpec spec = ZoneSpec.ForBody(solar, body, 0, 0);
            Assert.True(spec.Radius <= GameScale.MaximumPlanetRadius, "every Solar System body respects cap");
            Assert.Equal(radius, body.Radius, "physical radius unchanged");
            Assert.Equal(mass, body.Mass, "physical mass unchanged");
            Assert.Equal(gravity, body.SurfaceGravity, "physical gravity unchanged");
        }
    }
}
