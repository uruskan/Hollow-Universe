using System;
using System.Numerics;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>Continuous ascent and descent within one planet's current local flight volume (DESIGN.md TRAVEL-01).</summary>
public static class LocalFlightTests
{
    // The live Earth's radius, gravity and air; level terrain isolates the flight/transition rules from terrain art.
    private static ShipRig EarthRig(bool content = false) => new(content: content, worldSpec: ZoneSpec.StartingWorld(10, 20));

    [Test]
    public static void SurfaceToOrbitalAltitudeAndBack()
    {
        using var rig = EarthRig();
        Ship ship = rig.Add();
        rig.Step(40);
        float orbitalAltitude = ship.Def.CeilingOn(rig.Zone.Surface.Radius);
        int crossings = 0;
        rig.Zone.Ships().OrbitTransfer = _ => { crossings++; return false; };
        float greatestStep = 0;
        int ticks = 0;

        void Step(float vertical)
        {
            Vector3 before = ship.State.Position;
            rig.Fly(ship, new ShipInput { Vertical = vertical }, 1);
            greatestStep = Math.Max(greatestStep, Vector3.Distance(before, ship.State.Position));
            Assert.True(!ship.InOrbit && !ship.Destroyed, "flight stays active throughout the journey");
            ticks++;
        }

        while (rig.Height(ship) < orbitalAltitude + 40 && ticks < 6000)
        {
            Step(1);
        }
        float highest = rig.Height(ship);
        Assert.True(highest >= orbitalAltitude + 40, "flies beyond the former orbit parking height");
        Assert.Equal(1, crossings, "the optional transfer hook runs once on the upward crossing");
        Assert.True(Vector3.Dot(ship.State.Velocity, rig.Up(ship)) > 10, "keeps upward momentum past orbital altitude");

        while ((!ship.State.Has(ShipFlags.Landed) || !ship.State.Asleep) && ticks < 12000)
        {
            Step(-1);
        }
        Assert.True(ship.State.Has(ShipFlags.Landed) && rig.Height(ship) < 1, "normal descent returns to the surface");
        Assert.Equal(1, crossings, "descent does not repeat the upward transfer hook");
        Assert.True(greatestStep < 2, "no frame teleports or jumps at either crossing");
        Assert.Near(ship.Def.Health, ship.State.Health, 0.01, "controlled return preserves the hull");
        Fixtures.Report($"surface to {highest:0} m and back in {ticks / 30.0:0.0} s, largest step {greatestStep:0.000} m, {crossings} upward crossing");
    }

    [Test]
    public static void PredictionContinuesAcrossOrbitalAltitude()
    {
        using var rig = EarthRig();
        float orbitalAltitude = ShipRig.Finch.CeilingOn(rig.Zone.Surface.Radius);
        Ship ship = rig.Add(height: orbitalAltitude - 20);
        using var inputs = new Predictor(rig.Zone.Spec, rig.Zone.Surface, null);
        var prediction = new ShipPredictor(inputs, () => rig.Zone.Ships().ShipContext);
        prediction.Reset(ship.Def, 1, (uint)rig.Zone.Tick, ship.State, (uint)rig.Zone.Tick);
        int crossings = 0;
        float highest = 0;

        for (uint tick = 1; tick <= 1200; tick++)
        {
            var flight = new ShipInput { Vertical = tick <= 450 ? 1 : -1 };
            InputCommand command = NetConvert.ToCommand(new MoveInput { View = Vector3.UnitZ }, new ActionInput { HeldSlot = ActionInput.NoSlot });
            command.Ship = new[] { ShipConvert.ToControls(flight) };
            inputs.RecordInput(tick, command);
            prediction.Predict(tick);
            rig.Fly(ship, ShipConvert.ToShipInput(command), 1);

            Assert.True(prediction.Latest.Equals(ship.State), $"authority and prediction agree at flight tick {tick}");
            Assert.True(!ship.InOrbit, "neither side parks at orbital altitude");
            if (ship.Events.ReachedCeiling)
            {
                crossings++;
            }
            highest = Math.Max(highest, rig.Height(ship));
        }
        Assert.True(highest > orbitalAltitude + 250, "prediction covers continued ascent above the marker");
        Assert.True(rig.Height(ship) < orbitalAltitude - 50, "prediction covers commanded descent below the marker");
        Assert.Equal(1, crossings, "upward crossing is reported once");
        Assert.Equal(0L, prediction.Corrections, "no correction hides a prediction mismatch");
    }

    [Test]
    public static void LocalFlightLimitAllowsReturn()
    {
        using var rig = EarthRig();
        float edge = (float)(rig.Zone.Surface.Radius * ShipMotor.EdgeRadii);
        Ship ship = rig.Add(height: edge - (float)rig.Zone.Surface.Radius - 3);
        rig.Fly(ship, new ShipInput { Vertical = 1 }, 90);
        Assert.True(ship.State.Has(ShipFlags.AtEdge), "the current outer local boundary is reported");
        Assert.True(ship.State.Position.Length() <= edge + 0.01f, "flight stays inside its local volume");
        Vector3 atLimit = ship.State.Position;
        rig.Fly(ship, new ShipInput { Vertical = -1 }, 150);
        Assert.True(ship.State.Position.Length() < atLimit.Length() - 50, "ordinary descent leaves the boundary");
        Assert.True(!ship.State.Has(ShipFlags.AtEdge) && !ship.InOrbit, "the boundary does not latch or park the ship");
    }

    [Test]
    public static void LegacyOrbitReleaseDoesNotTeleport()
    {
        using var rig = EarthRig(content: true);
        Zone zone = rig.Zone;
        Ship ship = rig.Add();
        rig.Step(40);
        Vector3 hatch = ship.PointOf(ship.Def.HatchPoint());
        Vector3 side = Vector3.Normalize(Vector3.Transform(-Vector3.UnitX, ship.State.Rotation));
        Vector3 standAt = zone.Settle(hatch + side * 1.6f);
        Entity pilot = zone.Players().AddPlayer(zone.NextId(), "Pilot", zone.NewCharacter(standAt + zone.Frame.UpAt(standAt) * 0.02f));
        zone.Ships().SetDoorAtOnce(ship, 0, 1);
        Assert.Equal(OutcomeCode.Done, zone.Interaction().Interact(pilot,
            new TargetRef(TargetFamily.Ship, ship.Id.Value, ship.Def.HelmPart()), "board"), "boards before the legacy state is restored");

        ship.State.Position += rig.Up(ship) * (ship.Def.CeilingOn(zone.Surface.Radius) + 20);
        ShipMotor.ParkInOrbit(ref ship.State);
        ShipState before = ship.State;
        ship.OrbitAnchor = before.Position - Vector3.UnitY * 100;
        Assert.Equal(OutcomeCode.Done, zone.Ships().Descend(pilot), "legacy parked state can resume flight");
        Assert.Equal(before.Position, ship.State.Position, "release preserves the exact position");
        Assert.Equal(before.Rotation, ship.State.Rotation, "release preserves orientation");
        Assert.Equal(before.Velocity, ship.State.Velocity, "release does not impose a falling velocity");
        Assert.True(!ship.InOrbit, "the legacy parking flag is cleared");
        Assert.Equal(OutcomeCode.NotAllowed, zone.Ships().Descend(pilot), "normal flight has no teleport-descent action");
    }
}
