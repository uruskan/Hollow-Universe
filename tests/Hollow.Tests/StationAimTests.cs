using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class StationAimTests
{
    [Test]
    public static void PredictedCentreRayMatchesAuthorityAtCorpseEdgesAndWorldProps()
    {
        using var rig = new GameRig();
        using var client = new GameClient(new ClientOptions { Content = rig.C });
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(TargetModule).GetMethod("Welcome", hidden)!.Invoke(client.Targets(), null);
        var mirrors = (Dictionary<uint, RemoteThing>)typeof(SurvivalModule).GetField("_things", hidden)!.GetValue(client.Survival())!;
        Vector3 ground = rig.Zone.GroundAt(20, 20), planetUp = rig.Zone.Frame.UpAt(ground);
        rig.Clear(ground, ground, 8);
        (Vector3 across, _) = Geometry.Axes(planetUp, 0);
        Vector3 up = Vector3.Normalize(planetUp + across * 0.35f), position = ground + planetUp * 3;
        const float yaw = 0.61f;
        (Vector3 right, Vector3 forward) = Geometry.Axes(up, yaw);
        var body = new Corpse(rig.Zone.NextId(), "Body", new EntityId(77), 37, position, up, yaw, rig.C.Survival.BodyLifetime, rig.Zone.Tick + 10000)
        {
            Life = new CorpseLifecycle(rig.C.Survival.Corpse, rig.Zone.Tick, 0, Array.Empty<ushort>()),
        };
        rig.Zone.Things().AddStatic(body);
        var mirror = new RemoteThing(501) { Id = body.Id, Kind = StaticKind.Corpse, Position = position, Up = up, Yaw = yaw, IsBody = true };
        mirrors.Add(mirror.NetId, mirror);
        foreach ((Vector3 offset, bool expected) in new[] { (right * 0.44f + forward * 0.85f, true), (right * 0.55f, false) })
        {
            Vector3 eye = position + offset + up * 2;
            AimHit predicted = client.Targets().PredictProjectileAim(eye, -up, 2.5f, 0);
            TraceHit authority = rig.Zone.Damage().Trace(eye, -up, 2.5f, 0, EntityId.None, rig.Zone.Tick);
            Assert.Equal(expected, authority.Hit && authority.Target == body.Ref, "the solid body's corner and outside-width edge");
            Assert.Equal(expected, predicted.Hit && predicted.Target == body.Ref, "prediction uses the same oriented body box, including aboard up");
            if (expected) Assert.Near(authority.Distance, predicted.Distance, 0.0001, "the same centre-ray contact distance");
        }
        mirror.IsBody = false;
        body.Life.Restore(CorpseStage.Bag, 0, 0, rig.Zone.Tick);
        Vector3 bodyEye = position + up * 2;
        Assert.True(!client.Targets().PredictProjectileAim(bodyEye, -up, 2.5f, 0).Hit,
            "a converted lootbag does not become a phantom projectile target");

        var prop = new WorldProp(rig.Zone.NextId(), rig.C.World.Prop("loot-barrel-1")!, position + right * 5, up, yaw);
        rig.Zone.Things().AddStatic(prop);
        mirrors.Add(502, new RemoteThing(502) { Id = prop.Id, Kind = StaticKind.Prop, Look = prop.Look, Position = prop.Position, Up = up, Yaw = yaw });
        Vector3 propEye = prop.Position + up * 3;
        AimHit predictedProp = client.Targets().PredictProjectileAim(propEye, -up, 3, 0);
        TraceHit authorityProp = rig.Zone.Damage().Trace(propEye, -up, 3, 0, EntityId.None, rig.Zone.Tick);
        Assert.True(authorityProp.Target == prop.Ref && predictedProp.Target == prop.Ref,
            "world props retain their shared deployable collision capability in the centre-ray query");
        Assert.Near(authorityProp.Distance, predictedProp.Distance, 0.0001, "world prop contact agrees too");
    }

    [Test]
    public static void EveryStationBarrelConvergesAtTheCameraTargetAcrossRangesAndHullRotations()
    {
        ShipDef def = ContentFixture.Content.Ship("iron-wake")!;
        ShipInterior interior = def.Interior!;
        foreach (ShipStationDef station in def.Stations)
        foreach (float distance in new[] { 25f, 100f, 500f, StationRules.AimDistance })
        foreach (Quaternion rotation in new[] { Quaternion.Identity, Quaternion.CreateFromYawPitchRoll(0.8f, 0.2f, -0.3f) })
        {
            var pose = new ShipPose(new Vector3(45, -30, 90), rotation, Vector3.Zero, Vector3.Zero, def.CentreOfMass);
            Vector3 eye = pose.ToWorld(ShipSeats.EyeInFrame(def, station.Seat, interior.Rest));
            Vector3 view = pose.DirectionToWorld(StationRules.BarrelDirection(interior, station, interior.Rest));
            StationAim aim = StationRules.AimAt(interior, station, interior.Rest, pose, eye, view, distance);
            Assert.True(!aim.Blocked, $"{station.Id}: target beyond the muzzles at {distance}m");
            foreach (StationBarrel barrel in station.Barrels)
            {
                Vector3 muzzle = pose.ToWorld(StationRules.BarrelLine(interior, station, barrel.Index, interior.Rest).Muzzle);
                Vector3 launch = StationRules.LaunchDirection(aim, muzzle, view);
                Vector3 at = muzzle + launch * Vector3.Dot(aim.Point - muzzle, launch);
                Assert.True(Vector3.Distance(at, eye + view * distance) < 0.002f,
                    $"{station.Id}/{barrel.Index}: launch line intersects the camera centre ray at its target");
                Assert.True(Vector3.Dot(launch, view) > 0, "a converged shot still goes forward");
            }
        }
    }

    [Test]
    public static void AStationaryCloseTargetStaysAlignedAfterConvergedTraverseSettles()
    {
        ShipDef def = ContentFixture.Content.Ship("iron-wake")!;
        ShipInterior interior = def.Interior!;
        ShipStationDef station = def.Stations[0];
        foreach (float distance in new[] { 20f, 30f, StationRules.AimDistance })
        foreach (Quaternion rotation in new[] { Quaternion.Identity, Quaternion.CreateFromYawPitchRoll(0.8f, 0.2f, -0.3f) })
        {
            var pose = new ShipPose(new Vector3(45, -30, 90), rotation, Vector3.Zero, Vector3.Zero, def.CentreOfMass);
            var parts = new float[interior.Parts.Count];
            interior.Rest.CopyTo(parts, 0);
            var state = new StationState(station);
            Vector3 eye = pose.ToWorld(ShipSeats.EyeInFrame(def, station.Seat, parts));
            Vector3 target = eye + pose.DirectionToWorld(Vector3.Normalize(new Vector3(0, 0.174f, 0.985f))) * distance;
            for (int tick = 0; tick < 240; tick++)
            {
                void Resolve()
                {
                    eye = pose.ToWorld(ShipSeats.EyeInFrame(def, station.Seat, parts));
                    Vector3 line = target - eye;
                    state.Input.View = Vector3.Normalize(line);
                    state.Aim = StationRules.AimAt(interior, station, parts, pose, eye, state.Input.View, line.Length());
                }
                Resolve();
                StationRules.Slew(interior, state, pose.Rotation);
                StationRules.Into(state, parts);
                Resolve();
                if (tick < 120) continue;
                Assert.True(!state.Limit && !state.Aim.Blocked, $"a clear stationary target at {distance}m remains reachable");
                Assert.True(StationRules.Misalignment(interior, state, parts, pose.Rotation) <= station.Battery.Mount.Alignment,
                    $"a stationary target at {distance}m stays aligned at tick {tick}, rather than alternating ready/traversing");
                Assert.True(MathF.Abs(state.PitchRate) < 0.001f && MathF.Abs(state.YawRate) < 0.001f,
                    "the stationary target does not manufacture target-motion feed-forward from the moving muzzle");
            }
        }
    }

    [Test]
    public static void ACameraSideSurfaceBeforeTheMuzzlesBlocksFiringAndSkyUsesTheFarRay()
    {
        ShipDef def = ContentFixture.Content.Ship("iron-wake")!;
        ShipInterior interior = def.Interior!;
        ShipStationDef station = def.Stations[0];
        var pose = new ShipPose(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero, def.CentreOfMass);
        Vector3 eye = pose.ToWorld(ShipSeats.EyeInFrame(def, station.Seat, interior.Rest));
        Vector3 view = StationRules.BarrelDirection(interior, station, interior.Rest);
        var state = new StationState(station)
        {
            Input = new StationInput { View = view },
            Aim = StationRules.AimAt(interior, station, interior.Rest, pose, eye, view, 0.5f),
        };
        Assert.True(state.Aim.Blocked, "a nearby surface cannot be skipped by spawning beyond it at a long barrel's muzzle");
        var loaded = new bool[station.Barrels.Count];
        Array.Fill(loaded, true);
        Assert.Equal(OutcomeCode.HullBlocked, StationRules.Check(state, false, loaded, 0, true, 0, out _).Code,
            "no ammunition is fired backwards or through the surface");
        StationAim sky = StationRules.AimAt(interior, station, interior.Rest, pose, eye, view, -1);
        Assert.True(Vector3.Distance(sky.Point, eye + view * StationRules.AimDistance) < 0.001f, "sky retains the centre ray at the display range");
        state.Aim = sky;
        state.Limit = true;
        Assert.Equal(OutcomeCode.Limit, StationRules.Check(state, false, loaded, 0, true, 0, out _).Code, "convergence cannot bypass mount arcs");
        state.Limit = false;
        Assert.Equal(OutcomeCode.Traversing, StationRules.Check(state, false, loaded, 0, true, station.Battery.Mount.Alignment * 2, out _).Code,
            "convergence cannot fire before the mount has traversed");
    }
}
