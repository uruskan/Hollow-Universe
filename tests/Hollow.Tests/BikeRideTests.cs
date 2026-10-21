using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>
/// VEHICLES.md §6.3: the Zamazingo rides as a dirt bike does (the owner, 20 October 2026: "not rideable at all"): quick
/// off the line, flat out at its top speed, up a steep hillside from a standstill, and over a kerb or a log in its way.
/// </summary>
public static class BikeRideTests
{
    /// <summary>The yaw that faces east (the zone's +X), where a test slope rises.</summary>
    private const float East = -MathF.PI / 2;

    private static readonly Lazy<GameContent> Bare = new(() =>
    {
        Dictionary<string, string> texts = GameContent.ReadTexts(ContentFixture.Folder);
        texts["gather.json"] = Regex.Replace(texts["gather.json"], "\"perTile\": [0-9.]+", "\"perTile\": 0");
        GameContent content = GameContent.FromTexts(texts);
        Assert.Equal("", string.Join("\n", content.Problems), "the bare data reads");
        return content;
    });

    private sealed class Ride : IDisposable
    {
        public readonly Zone Zone;
        public readonly Ship Bike;

        public Ride(float slope = 0, string kind = "zamazingo")
        {
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
            Entity rider = z.Players().AddPlayer(z.NextId(), "Ada", z.NewCharacter(ground + z.Frame.UpAt(ground) * 3 + new Vector3(4, 0, -4)));
            Bike = z.Ships().SpawnShip(Bare.Value.Ship(kind)!, ground, East, z.Players().PlayerOf(rider)!.Id, 100);
            Assert.True(z.Ships().Reseat(rider, Bike, Bike.Def.Helm.Index), "Ada on the saddle");
            if (slope != 0)
            {
                // Standing on the hillside as a rider stops on one: along the ground, not level with the sky.
                Vector3 ahead = Bike.State.Forward, side = Bike.State.Right;
                Vector3 normal = Vector3.Normalize(Vector3.Cross(z.Settle(ground + ahead) - z.Settle(ground - ahead), z.Settle(ground + side) - z.Settle(ground - side)));
                if (Vector3.Dot(normal, z.Frame.UpAt(ground)) < 0)
                {
                    normal = -normal;
                }
                ShipState standing = ShipMotor.Standing(Bike.Def, ground + normal * 0.02f, normal, ShipMotor.YawOf(Bike.State.Rotation, normal));
                Bike.State.Position = standing.Position;
                Bike.State.Rotation = Quaternion.Normalize(ShipMotor.Basis(ahead - normal * Vector3.Dot(ahead, normal), normal));
            }
            Go(default, 60);
            _start = Bike.State.Position;
            _ahead = Bike.State.Forward;
            _radius = Zone.Frame.ToBody(_start).Length;
        }

        private readonly Vector3 _start, _ahead;
        private readonly double _radius;

        /// <summary>A step across the way <paramref name="east"/> m east of the start, <paramref name="height"/> high and 3 m deep.</summary>
        public void Step(float ahead, float height)
        {
            Vector3 at = Zone.Settle(_start + _ahead * (ahead + 3));
            Vector3 up = Zone.Frame.UpAt(at);
            float half = (height + 0.3f) / 2;
            Zone.Physics.AddProp(PropLayer.Deployable, 0xB1CE, PropShape.Box(at + up * (height - half), new Vector3(3, half, 3), up, 0));
        }

        public void Go(ShipInput input, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Bike.Input = input;
                Zone.UnixMilliseconds += 1000 / CharacterMotor.TickRate;
                Zone.Step();
            }
        }

        public float Along => Vector3.Dot(Bike.State.Velocity, Bike.State.Forward);

        public float Upright => Vector3.Dot(Bike.State.Up, Zone.Frame.UpAt(Bike.State.Position));

        /// <summary>How far along its first way from where it stood.</summary>
        public float Gone => Vector3.Dot(Bike.State.Position - _start, _ahead);

        /// <summary>How much higher than where it stood.</summary>
        public float Rise => (float)(Zone.Frame.ToBody(Bike.State.Position).Length - _radius);

        public void Dispose() => Zone.Dispose();
    }

    private static readonly ShipInput Full = new() { Throttle = 1 };

    [Test]
    public static void TheZamazingoIsQuickOffTheLineAndFlatOut()
    {
        using var r = new Ride();
        int ticks = 0;
        while (r.Along < 50 / 3.6f && ticks < 600)
        {
            r.Go(Full, 1);
            ticks++;
        }
        float to50 = ticks / (float)CharacterMotor.TickRate;
        r.Go(Full, 600);
        Fixtures.Report($"0–50 km/h in {to50:0.00} s; top speed {r.Along * 3.6f:0} km/h, upright {r.Upright:0.000}");
        Assert.True(to50 < 3.5f, "0 to 50 km/h in under 3.5 s");
        Assert.True(r.Along * 3.6f > 80 && r.Upright > 0.98f, "flat out above 80 km/h, upright");
    }

    [Test]
    public static void TheZamazingoClimbsASteepHillsideFromAStandstill()
    {
        float most = 0;
        foreach (float slope in new[] { 15f, 20f, 25f, 30f, 35f, 40f })
        {
            using var r = new Ride(slope);
            r.Go(Full, 300);
            bool climbs = r.Rise > 8 && r.Along > 2 && r.Upright > MathF.Cos(slope * MathF.PI / 180) - 0.15f;
            Fixtures.Report($"{slope}°: {r.Rise:0.0} m up in ten seconds, {r.Along * 3.6f:0} km/h, upright {r.Upright:0.00}{(climbs ? "" : ": stalls")}");
            if (climbs)
            {
                most = slope;
            }
        }
        Fixtures.Report($"the steepest it climbs from a standstill: {most}°");
        Assert.True(most >= 30, "a 30° hillside from a standstill");
    }

    [Test]
    public static void TheZamazingoRidesOverAKerb()
    {
        foreach ((float height, float run) in new[] { (0.15f, 0f), (0.25f, 0f), (0.3f, 0f), (0.25f, 8f), (0.3f, 8f), (0.4f, 8f) })
        {
            // From a crawl (the step 3 m ahead) or with a run at it (8 m/s).
            using var r = new Ride();
            float start = 3;
            if (run > 0)
            {
                r.Go(Full, 60);
                start = r.Gone + 10;
            }
            r.Step(start + 0.8f, height);
            int ticks = 0;
            while (r.Gone < start + 3 && ticks < 300)
            {
                r.Go(run > 0 && r.Along > run ? default : Full, 1);
                ticks++;
            }
            bool over = r.Gone >= start + 3 && r.Upright > 0.8f;
            Fixtures.Report($"a {height * 100:0} cm step {(run > 0 ? $"at {run:0} m/s" : "from a standstill")}: " +
                (over ? $"over in {ticks / (float)CharacterMotor.TickRate:0.0} s" : $"stuck {r.Gone - start:0.0} m on, upright {r.Upright:0.00}"));
            if (height <= 0.25f || run > 0 && height <= 0.3f)
            {
                Assert.True(over, $"over a {height * 100:0} cm step");
            }
        }
    }
}
