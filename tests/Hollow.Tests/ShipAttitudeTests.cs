using System;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Surface;

namespace Hollow.Tests;

public static class ShipAttitudeTests
{
    internal static ShipDef Carrier => ContentFixture.Content.Ship("iron-wake")!;
    internal static readonly ShipInput Hover = new() { Engines = true };
    internal static readonly LevelSurface Surface = new(1200);
    internal static ShipContext Air => new(ZoneFrame.World, Surface, 0, 0, 1, null);

    internal static ShipState Airborne(ShipDef? def = null)
    {
        ShipState state = ShipMotor.Standing(def ?? Carrier, new Vector3(0, 1600, 0), Vector3.UnitY, 0.7f);
        state.Flags = ShipFlags.EnginesOn;
        state.Fuel = 500;
        state.Rotation = Quaternion.Normalize(state.Rotation * Quaternion.CreateFromYawPitchRoll(0.1f, 0.35f, -0.3f));
        return state;
    }

    internal static double Degrees(Quaternion a, Quaternion b)
    {
        Quaternion error = Quaternion.Normalize(Quaternion.Conjugate(a) * b);
        return 2 * Math.Atan2(new Vector3(error.X, error.Y, error.Z).Length(), Math.Abs(error.W)) * 180 / Math.PI;
    }

    [Test]
    public static void ShipAttitudeReturnsToReleaseAfterCoupledResidualSpin()
    {
        ShipDef def = Carrier;
        foreach (Vector3 spin in new[] { Vector3.Zero, new Vector3(def.Pitch.MaxRate, 0, 0),
            new Vector3(0, -def.Yaw.MaxRate, 0), new Vector3(0, 0, def.Roll.MaxRate),
            new Vector3(def.Pitch.MaxRate, -def.Yaw.MaxRate, def.Roll.MaxRate),
            new Vector3(-def.Pitch.MaxRate, def.Yaw.MaxRate, -def.Roll.MaxRate) })
        {
            ShipState state = Airborne();
            state.AngularVelocity = spin;
            ShipAttitudeHold.Capture(ref state);
            Quaternion release = state.AttitudeHoldRotation;
            float largestError = 0;
            for (int tick = 0; tick < 900; tick++)
            {
                ShipMotor.Step(ref state, Hover, def, Air);
                largestError = MathF.Max(largestError, (float)Degrees(release, state.Rotation));
                Assert.True(Degrees(release, state.AttitudeHoldRotation) < 0.001, "braking never recaptures the displaced hull");
            }
            Assert.True(state.AttitudeHoldActive && Degrees(release, state.Rotation) < 0.2, "returns to release, not the post-braking angle");
            Assert.True(state.AngularVelocity.Length() * 180 / Math.PI < 0.05, "settles angular speed");
            if (spin != Vector3.Zero) Assert.True(largestError > 0.2, "bounded braking permits a real transient, no pose snap");
            Assert.True(state.Fuel < 500, "powered hold consumes fuel");
        }
    }

    [Test]
    public static void ShipAttitudeShortestArcAndEquivalentQuaternionSigns()
    {
        foreach (float angle in new[] { 0f, 0.00001f, 2.9f, MathF.PI, -3.2f })
        {
            ShipState a = Airborne(), b = a;
            Quaternion target = Quaternion.Normalize(a.Rotation * Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1, -2, 3)), angle));
            a.AttitudeHoldActive = b.AttitudeHoldActive = true;
            a.AttitudeHoldRotation = target;
            b.AttitudeHoldRotation = Quaternion.Negate(target);
            Vector3 rates = ShipAttitudeHold.Rates(a.Rotation, target, Carrier);
            Assert.True(MathF.Abs(rates.X) <= Carrier.Pitch.MaxRate && MathF.Abs(rates.Y) <= Carrier.Yaw.MaxRate &&
                MathF.Abs(rates.Z) <= Carrier.Roll.MaxRate, "large coupled errors respect every authored desired-rate limit");
            for (int tick = 0; tick < 900; tick++)
            {
                ShipMotor.Step(ref a, Hover, Carrier, Air);
                ShipMotor.Step(ref b, Hover, Carrier, Air);
                Assert.Equal(a.Rotation, b.Rotation, "q/-q target takes exactly the same path");
                Assert.Equal(a.AngularVelocity, b.AngularVelocity, "q/-q torque is identical");
            }
            Assert.True(Degrees(a.Rotation, target) < 0.2, "coupled large-angle error converges");
        }
        Vector3 half = ShipAttitudeHold.Rates(Quaternion.Identity, new Quaternion(-1, 0, 0, 0), Carrier);
        Assert.Equal(half, ShipAttitudeHold.Rates(Quaternion.Identity, new Quaternion(1, 0, 0, 0), Carrier), "exact half-turn tie is deterministic");
    }

    [Test]
    public static void ShipAttitudeTorqueAndLastFuelRemainBounded()
    {
        ShipState state = Airborne();
        ShipAttitudeHold.Capture(ref state);
        state.Rotation = Quaternion.Normalize(state.Rotation * Quaternion.CreateFromYawPitchRoll(0.8f, -0.7f, 0.9f));
        ShipState full = state, partial = state;
        ShipEvents burn = ShipMotor.Step(ref full, Hover, Carrier, Air);
        Assert.True(burn.FuelUsed > 0, "hold pays the existing idle fuel cost");
        partial.Fuel = partial.FuelReserve = burn.FuelUsed * 0.5f;
        ShipEvents last = ShipMotor.Step(ref partial, Hover, Carrier, Air);
        Vector3 limit = new(Carrier.Pitch.Acceleration, Carrier.Yaw.Acceleration, Carrier.Roll.Acceleration);
        Vector3 acceleration = full.AngularVelocity / CharacterMotor.DeltaTime;
        Assert.True(MathF.Abs(acceleration.X) <= limit.X + 1e-5 && MathF.Abs(acceleration.Y) <= limit.Y + 1e-5 &&
            MathF.Abs(acceleration.Z) <= limit.Z + 1e-5, "authored torque bounds hold on all axes");
        Assert.True(Vector3.Distance(full.AngularVelocity * 0.5f, partial.AngularVelocity) < 1e-5, "last half fuel tick buys half torque");
        Assert.True(last.OutOfFuel && !partial.AttitudeHoldActive, "exhaustion clears target in the same step");
        Assert.True(Degrees(state.Rotation, partial.Rotation) > 0 && Degrees(partial.Rotation, state.AttitudeHoldRotation) > 1,
            "partial fuel moves through physics without snapping to target");
    }

    [Test]
    public static void ShipAttitudeTargetFollowsPlanetaryTransport()
    {
        foreach (Vector3 radial in new[] { Vector3.UnitY, Vector3.UnitX, Vector3.Normalize(new Vector3(2, -3, 4)) })
        {
            ShipState state = Airborne();
            Quaternion relocate = Facing.Turn(Vector3.UnitY, radial);
            state.Position = Vector3.Transform(state.Position, relocate);
            state.Rotation = Quaternion.Normalize(relocate * state.Rotation);
            state.Velocity = Vector3.Transform(new Vector3(130, 0, 80), relocate);
            ShipAttitudeHold.Capture(ref state);
            Quaternion reference = state.AttitudeHoldRotation, original = reference;
            for (int tick = 0; tick < 900; tick++)
            {
                Vector3 before = Air.Frame.UpAt(state.Position);
                ShipMotor.Step(ref state, Hover, Carrier, Air);
                reference = Quaternion.Normalize(Facing.Turn(before, Air.Frame.UpAt(state.Position)) * reference);
                Assert.True(Degrees(reference, state.AttitudeHoldRotation) < 0.02, "target follows every local-up change");
            }
            Assert.True(Degrees(original, reference) > 1, "residual travel actually crossed curvature");
            Assert.True(Degrees(reference, state.Rotation) < 0.2, "hull returns in the transported frame");
        }
    }

    [Test]
    public static void ShipAttitudePowerAndCommandGatesClearBeforeEarlyReturns()
    {
        foreach (ShipInput input in new[] { new ShipInput(), new ShipInput { Engines = true, Throttle = float.Epsilon },
            new ShipInput { Engines = true, Strafe = -float.Epsilon }, new ShipInput { Engines = true, Vertical = -float.Epsilon },
            new ShipInput { Engines = true, Buttons = ShipButtons.AssistOff } })
        {
            ShipState state = Airborne();
            ShipAttitudeHold.Capture(ref state);
            ShipMotor.Step(ref state, input, Carrier, Air);
            Assert.True(!state.AttitudeHoldActive, "every nonzero translation / power / assist gate ends hold");
        }
        foreach (ShipFlags flag in new[] { ShipFlags.EngineStopped, ShipFlags.Landed, ShipFlags.Destroyed, ShipFlags.InOrbit })
        {
            ShipState state = Airborne();
            ShipAttitudeHold.Capture(ref state);
            state.Flags |= flag;
            ShipMotor.Step(ref state, Hover, Carrier, Air);
            Assert.True(!state.AttitudeHoldActive, "terminal state clears, including motor early exits");
        }
        ShipState asleep = Airborne();
        ShipAttitudeHold.Capture(ref asleep);
        asleep.RestTicks = ShipMotor.SleepTicks;
        ShipMotor.Step(ref asleep, Hover, Carrier, Air);
        Assert.True(!asleep.AttitudeHoldActive, "sleep early return cannot keep a target");
    }

    [Test]
    public static void ShipAttitudeNearGroundSubstepsTransportBothPoseAndTarget()
    {
        using var rig = new ShipRig();
        Vector3 ground = rig.Zone.GroundAt(0, 0), up = rig.Zone.Frame.UpAt(ground);
        Ship ship = rig.Zone.Ships().SpawnShip(Carrier, ground + up * 30, 0, EntityId.None, 500);
        ship.State.Flags = ShipFlags.EnginesOn;
        ship.State.Rotation = Quaternion.Normalize(ship.State.Rotation * Quaternion.CreateFromYawPitchRoll(0, 0.12f, -0.1f));
        ship.State.Velocity = Vector3.Transform(new Vector3(20, 0, 15), ShipMotor.Orientation(up, 0));
        ShipAttitudeHold.Capture(ref ship.State);
        float pitch = Vector3.Dot(Vector3.Transform(Vector3.UnitZ, ship.State.AttitudeHoldRotation), rig.Up(ship));
        float roll = Vector3.Dot(Vector3.Transform(Vector3.UnitX, ship.State.AttitudeHoldRotation), rig.Up(ship));
        Vector3 start = ship.State.Position;
        for (int tick = 0; tick < 300; tick++)
        {
            rig.Step();
            Assert.True(ship.Events.Contacts == 0 && rig.Height(ship) < ShipMotor.NearGround, "near-ground two-substep flight without landing");
            Assert.Near(pitch, Vector3.Dot(Vector3.Transform(Vector3.UnitZ, ship.State.AttitudeHoldRotation), rig.Up(ship)), 0.0002,
                "pitch relative to local up survives both carries");
            Assert.Near(roll, Vector3.Dot(Vector3.Transform(Vector3.UnitX, ship.State.AttitudeHoldRotation), rig.Up(ship)), 0.0002,
                "roll relative to local up survives both carries");
        }
        Assert.True(Vector3.Distance(start, ship.State.Position) > 20, "residual motion actually traversed the curved ground");
        Assert.True(ship.State.AttitudeHoldActive && Degrees(ship.State.Rotation, ship.State.AttitudeHoldRotation) < 0.2,
            "target and hull remain aligned through near-ground substeps");
    }

    internal static (Ship Ship, Entity Pilot) Crewed(ShipRig rig)
    {
        Zone zone = rig.Zone;
        Vector3 ground = zone.GroundAt(0, 0), up = zone.Frame.UpAt(ground);
        Entity pilot = zone.Players().AddPlayer(zone.NextId(), "Attitude pilot", zone.NewCharacter(ground + up));
        Ship ship = zone.Ships().SpawnShip(Carrier, ground + up * 350, 0, zone.Entities.IdOf(pilot), 500);
        Assert.True(zone.Ships().Reseat(pilot, ship, ship.Def.Helm.Index), "real helm occupancy");
        ship.State.Flags = ShipFlags.EnginesOn;
        ship.State.Rotation = Quaternion.Normalize(ship.State.Rotation * Quaternion.CreateFromYawPitchRoll(0.1f, 0.25f, -0.2f));
        return (ship, pilot);
    }

    [Test]
    public static void ShipAttitudeRealReleaseGunnerAndTakeoverLifecycle()
    {
        using var rig = new ShipRig(content: true, worldSpec: ZoneSpec.StartingWorld(10, 20));
        (Ship ship, Entity pilot) = Crewed(rig);
        ship.State.AngularVelocity = new Vector3(0.3f, -0.25f, 0.2f);
        Quaternion release = ship.State.Rotation;
        Assert.Equal(OutcomeCode.Done, rig.Zone.Ships().LeaveSeat(pilot).Code, "real successful helm release");
        Assert.True(ship.State.AttitudeHoldActive && Degrees(release, ship.State.AttitudeHoldRotation) < 0.001, "captures synchronously before next tick");
        Assert.Equal(new Vector3(0.3f, -0.25f, 0.2f), ship.State.AngularVelocity, "release preserves angular momentum");
        int gun = ship.Def.Stations[0].Seat;
        Assert.True(rig.Zone.Ships().Reseat(pilot, ship, gun), "move to a crew station");
        rig.Step(30);
        Assert.True(ship.State.AttitudeHoldActive, "gunner occupancy does not take the helm");
        Quaternion target = ship.State.AttitudeHoldRotation;
        Assert.True(rig.Zone.Ships().Release(pilot, SeatRelease.Moved), "leave crew station");
        Assert.Equal(target, ship.State.AttitudeHoldRotation, "gunner release does not recapture");
        Assert.True(rig.Zone.Ships().Reseat(pilot, ship, ship.Def.Helm.Index), "awake takeover");
        Assert.True(!ship.State.AttitudeHoldActive, "takeover clears before snapshot or tick");
    }

    [Test]
    public static void ShipAttitudeEngineTransitionsAndAutopilotNeverReuseOldTarget()
    {
        using var rig = new ShipRig(content: true, worldSpec: ZoneSpec.StartingWorld(10, 20));
        (Ship ship, Entity pilot) = Crewed(rig);
        Player owner = rig.Zone.Players().PlayerOf(pilot)!;
        Assert.True(rig.Zone.Ships().Release(pilot, SeatRelease.Moved), "vacant helm");
        Quaternion old = ship.State.AttitudeHoldRotation;
        Assert.Equal(OutcomeCode.Done, rig.Zone.Ships().SetEngine(owner, ship, false).Code, "engine off");
        Assert.True(!ship.State.AttitudeHoldActive, "off clears immediately without a tick");
        ship.State.Rotation = Quaternion.Normalize(ship.State.Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.3f));
        Assert.Equal(OutcomeCode.Done, rig.Zone.Ships().SetEngine(owner, ship, true).Code, "engine on in same tick");
        Assert.True(ship.State.AttitudeHoldActive && Degrees(old, ship.State.AttitudeHoldRotation) > 10, "restart captures new pose");
        ship.Autopilot = true;
        rig.Step();
        Assert.True(!ship.State.AttitudeHoldActive, "scripted autopilot excluded");
        ship.Autopilot = false;
        rig.Step();
        Assert.True(ship.State.AttitudeHoldActive, "fresh vacant neutral hover can activate");
        ship.Input.Throttle = 0.01f;
        rig.Step();
        Assert.True(!ship.State.AttitudeHoldActive && ship.State.Has(ShipFlags.EnginesOn), "nonzero unattended cruise keeps engines but ends hold");
    }

    [Test]
    public static void ShipAttitudeSleepingRestoreAndMissingOccupants()
    {
        using var rig = new ShipRig(content: true, worldSpec: ZoneSpec.StartingWorld(10, 20));
        (Ship ship, Entity pilot) = Crewed(rig);
        Assert.True(rig.Zone.Ships().Release(pilot, SeatRelease.Moved), "capture on release");
        Quaternion saved = ship.State.AttitudeHoldRotation;
        Player player = rig.Zone.Players().PlayerOf(pilot)!;
        player.Sleeping = true;
        Assert.True(rig.Zone.Ships().Reseat(pilot, ship, ship.Def.Helm.Index), "restore sleeper");
        Assert.Equal(saved, ship.State.AttitudeHoldRotation, "Reseat preserves a saved target");
        rig.Step();
        Assert.True(ship.State.AttitudeHoldActive, "sleeping helm preserves existing hold");
        player.Sleeping = false;
        ship.State.Flags |= ShipFlags.InOrbit; // Motor early exit must not postpone awake takeover clearing.
        rig.Step();
        Assert.True(!ship.State.AttitudeHoldActive, "wake clears before an early-return motor step");
        ship.State.Flags = ShipFlags.EnginesOn;
        rig.Zone.Entities.Remove<ShipSeat>(pilot);
        rig.Step();
        Assert.True(ship.State.AttitudeHoldActive && !ship.Piloted, "mismatched occupant cleanup captures");
        ShipAttitudeHold.Clear(ref ship.State);
        ship.Occupants[ship.Def.Helm.Index] = new EntityId(ulong.MaxValue);
        rig.Step();
        Assert.True(ship.State.AttitudeHoldActive && !ship.Piloted, "missing entity cleanup captures");
    }

    [Test]
    public static void ShipAttitudeInactiveStateDoesNotChangePilotEquality()
    {
        ShipState a = Airborne(), b = a;
        b.AttitudeHoldRotation = Quaternion.Identity;
        Assert.True(a.Equals(b) && a.GetHashCode() == b.GetHashCode(), "unused default/identity target is not state");
        ShipAttitudeHold.Capture(ref a);
        Assert.True(!a.Equals(b), "activation is state");
        b = a;
        b.AttitudeHoldRotation = Quaternion.Normalize(b.AttitudeHoldRotation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.1f));
        Assert.True(!a.Equals(b), "active target participates in reconciliation");
    }

    [Test]
    public static void ShipAttitudeRefusedExitAndSuccessfulDisconnectDiffer()
    {
        using var rig = new ShipRig(content: true, worldSpec: ZoneSpec.StartingWorld(10, 20));
        (Ship ship, Entity pilot) = Crewed(rig);
        ship.State.Flags |= ShipFlags.InOrbit;
        Assert.Equal(OutcomeCode.NotLanded, rig.Zone.Ships().LeaveSeat(pilot).Code, "refused exit");
        Assert.True(ship.Piloted && !ship.State.AttitudeHoldActive, "no capture for a refused release");
        ship.State.Flags = ShipFlags.EnginesOn;
        rig.Zone.Players().PlayerOf(pilot)!.Sleeping = true;
        rig.Step();
        Assert.True(ship.Piloted && !ship.State.AttitudeHoldActive, "an occupied sleeper does not invent a new target");
        Quaternion release = ship.State.Rotation;
        Assert.True(rig.Zone.Ships().Release(pilot, SeatRelease.Disconnected), "successful disconnect releases to the deck");
        Assert.True(!ship.Piloted && ship.State.AttitudeHoldActive && Degrees(release, ship.State.AttitudeHoldRotation) < 0.001,
            "successful disconnect captures its exact release pose");
    }

    [Test]
    public static void ShipAttitudeActualLandingEndsHoldWithoutLockingTheHull()
    {
        using var rig = new ShipRig(slopeDegrees: 5);
        Ship ship = rig.Add();
        ship.State.Flags = ShipFlags.EnginesOn;
        ship.State.Velocity = -rig.Up(ship) * 3;
        ShipAttitudeHold.Capture(ref ship.State);
        bool contact = false, touchdown = false;
        for (int tick = 0; tick < 180; tick++)
        {
            rig.Step();
            contact |= ship.Events.Contacts > 0;
            touchdown |= ship.Events.Touchdown;
            if (ship.State.Has(ShipFlags.Landed))
            {
                Assert.True(!ship.State.AttitudeHoldActive, "actual skid landing clears on the detecting step");
                break;
            }
        }
        Assert.True(contact && touchdown && ship.State.Has(ShipFlags.Landed), "real sloped-ground contact, not an injected landed flag");
        Quaternion landed = ship.State.Rotation;
        ship.State.AngularVelocity = new Vector3(0, 2, 0);
        rig.Step();
        Assert.True(Degrees(landed, ship.State.Rotation) > 0.001, "contact motor still responds to angular disturbance; no pose lock");
    }
}
