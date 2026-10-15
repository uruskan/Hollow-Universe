using System;
using System.Numerics;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;

namespace Hollow.Tests;

public static class ShipRepairTests
{
    [Test]
    public static void SmallCraftExitInFlightInheritsMomentumWithoutTeleporting()
    {
        using var rig = new ShipRig(content: true, worldSpec: ZoneSpec.StartingWorld(10, 20));
        Ship ship = rig.Add(height: 350);
        Entity pilot = rig.Zone.Players().AddPlayer(rig.Zone.NextId(), "Pilot", rig.Zone.NewCharacter(ship.State.Position));
        Assert.True(rig.Zone.Ships().Reseat(pilot, ship, ship.Def.Helm.Index), "pilot seated");
        ship.Input = new ShipInput { Throttle = 0.4f, Vertical = 0.2f };
        rig.Step(60);
        Vector3 shipAt = ship.State.Position, velocity = ship.State.Velocity;
        Assert.Equal(OutcomeCode.Done, rig.Zone.Ships().LeaveSeat(pilot).Code, "Space can leave a small aircraft in flight");
        CharacterState person = rig.Zone.Entities.Get<Character>(pilot).State;
        Assert.True(person.Stance == Stance.Standing && person.Frame == 0, "unseated into the planet frame");
        Assert.True(Vector3.Distance(person.Position, shipAt) < 8, "leaves beside the craft instead of at the ground");
        Assert.True(Vector3.Distance(person.Velocity, velocity) < 1, "inherits the craft's motion");
        Assert.True((person.Flags & MoveFlags.OnGround) == 0, "the person falls normally");
        rig.Step(30);
        Assert.True(ship.State.Has(ShipFlags.EnginesOn), "the empty craft keeps cruising");
    }

    [Test]
    public static void LeavingTheHelmKeepsCruiseUntilEngineOffOrFuelRunsOut()
    {
        using var rig = new ShipRig(content: true, worldSpec: ZoneSpec.StartingWorld(10, 20));
        Zone zone = rig.Zone;
        Vector3 ground = zone.GroundAt(0, 0), up = zone.Frame.UpAt(ground);
        Entity pilot = zone.Players().AddPlayer(zone.NextId(), "Pilot", zone.NewCharacter(ground + up));
        var owner = zone.Players().PlayerOf(pilot)!;
        ShipDef def = ContentFixture.Content.Ship("iron-wake")!;
        Ship ship = zone.Ships().SpawnShip(def, ground + up * 350, 0, owner.Id, 100);
        Assert.True(zone.Ships().Reseat(pilot, ship, def.Helm.Index), "pilot at the helm");
        ship.Input = new ShipInput { Throttle = 0.4f, Strafe = 0.15f, Vertical = 0.2f, Yaw = 0.1f };
        rig.Step(90);
        Assert.Equal(OutcomeCode.Done, zone.Ships().LeaveSeat(pilot).Code, "can walk away onto the carrier's deck in flight");
        Assert.Near(0.4, ship.Input.Throttle, 1e-6, "the lever is retained");
        Assert.Near(0.15, ship.Input.Strafe, 1e-6, "the lateral command is retained");
        Assert.Near(0.2, ship.Input.Vertical, 1e-6, "the climb command is retained");
        Assert.Near(0, ship.Input.Yaw, 1e-6, "hands off releases turning");
        Vector3 before = ship.State.Position;
        float fuel = ship.State.Fuel;
        rig.Step(90);
        Assert.True(!ship.Piloted && ship.State.Has(ShipFlags.EnginesOn), "an empty helm does not cut engines");
        Assert.True(!ship.State.Has(ShipFlags.AssistOff), "assist remains on");
        Assert.True(Vector3.Distance(before, ship.State.Position) > 20 && ship.State.Fuel < fuel, "unattended cruise moves and consumes fuel");
        Assert.Equal(OutcomeCode.Done, zone.Ships().SetEngine(owner, ship, false).Code, "crew can switch off");
        fuel = ship.State.Fuel;
        rig.Step(15);
        Assert.True(!ship.State.Has(ShipFlags.EnginesOn) && ship.State.Has(ShipFlags.EngineStopped), "off is latched");
        Assert.Near(fuel, ship.State.Fuel, 1e-6, "off does not burn fuel");
        Assert.True(zone.Ships().Reseat(pilot, ship, def.Helm.Index), "return to the helm");
        rig.Step(2);
        Assert.True(!ship.State.Has(ShipFlags.EnginesOn), "sitting does not override the engine switch");
        Assert.Equal(OutcomeCode.Done, zone.Ships().SetEngine(owner, ship, true).Code, "explicit restart");
        ship.LoadFuel(0.001f);
        rig.Step(3);
        Assert.True(ship.State.Fuel <= 0 && !ship.State.Has(ShipFlags.EnginesOn), "the engine stops when the tank runs dry");
        Assert.Equal(OutcomeCode.NoFuel, zone.Ships().SetEngine(owner, ship, true).Code, "cannot start an empty tank");
    }

    [Test]
    public static void RetainedCruiseAndEngineSwitchSurviveStorage()
    {
        using var rig = new ShipRig();
        Ship ship = rig.Add(height: 100);
        ship.Input = new ShipInput { Throttle = 0.6f, Strafe = -0.2f, Vertical = 0.3f, Buttons = ShipButtons.AssistOff | ShipButtons.Boost };
        ship.State.Flags |= ShipFlags.EngineStopped;
        StoredShip saved = ShipCodec.Decode(ship.Id, ShipCodec.Encode(ship, rig.Zone.Frame), ContentFixture.Content, rig.Zone.Frame)!;
        Assert.Near(ship.Input.Throttle, saved.Ship.Input.Throttle, 1e-6, "stored throttle");
        Assert.Near(ship.Input.Strafe, saved.Ship.Input.Strafe, 1e-6, "stored strafe");
        Assert.Near(ship.Input.Vertical, saved.Ship.Input.Vertical, 1e-6, "stored climb");
        Assert.True(saved.Ship.Input.Has(ShipButtons.AssistOff) && !saved.Ship.Input.Has(ShipButtons.Boost), "assist survives, a held boost does not");
        Assert.True(saved.Ship.State.Has(ShipFlags.EngineStopped), "explicit engine switch survives");
    }

    [Test]
    public static void MouseSteeringPersistsSmoothlyAcrossFrameRatesAndReverses()
    {
        var slow = new PilotSteering();
        var fast = new PilotSteering();
        slow.Move(new Vector2(0.7f, 0.001f));
        fast.Move(new Vector2(0.7f, 0.001f));
        float previous = 0;
        for (int i = 0; i < 30; i++)
        {
            Vector2 value = slow.Step(1f / 30);
            Assert.True(value.X >= previous && value.X <= 0.70001f, "smoothly approaches the requested turn without overshoot");
            previous = value.X;
        }
        for (int i = 0; i < 144; i++) fast.Step(1f / 144);
        Assert.Near(slow.Output.X, fast.Output.X, 1e-5, "frame rate does not change steering response");
        Assert.True(slow.Output.Y > 0, "a small movement is not discarded by a hidden dead zone");
        for (int i = 0; i < 600; i++) slow.Step(1f / 60);
        Assert.Near(0.7, slow.Output.X, 1e-5, "resting the mouse keeps turning");
        slow.Move(new Vector2(-1.4f, 0));
        for (int i = 0; i < 60; i++) slow.Step(1f / 60);
        Assert.True(slow.Output.X < -0.69f, "moving left reverses the turn");
        slow.Centre();
        Assert.Equal(Vector2.Zero, slow.Step(1f / 60), "explicit centre stops the turn");
        float throttle = 0;
        for (int i = 0; i < 144; i++) throttle = PilotSteering.AdjustThrottle(throttle, 1f / 144);
        Assert.Near(1, throttle, 1e-5, "high-FPS throttle leaves zero instead of being snapped back each frame");
    }

    [Test]
    public static void IronWakeCanTurnNinetyDegreesInFourSeconds()
    {
        using var rig = new ShipRig(worldSpec: ZoneSpec.StartingWorld(10, 20));
        ShipDef def = ContentFixture.Content.Ship("iron-wake")!;
        Vector3 ground = rig.Zone.GroundAt(0, 0);
        ShipState state = ShipMotor.Standing(def, ground + rig.Zone.Frame.UpAt(ground) * 400, rig.Zone.Frame.UpAt(ground), 0);
        state.Fuel = 100;
        Vector3 initial = state.Forward;
        var context = new ShipContext(rig.Zone.Frame, rig.Zone.Surface, 0, 0, 1, null);
        for (int i = 0; i < 120; i++) ShipMotor.Step(ref state, new ShipInput { Engines = true, Yaw = 1 }, def, context);
        float degrees = MathF.Acos(Math.Clamp(Vector3.Dot(initial, state.Forward), -1, 1)) * 180 / MathF.PI;
        Assert.True(degrees > 95 && degrees < 140, $"a deliberate turn is agile and bounded, observed {degrees:0.0} degrees");
    }

    [Test]
    public static void PredictionRespectsTheExplicitEngineOffSwitch()
    {
        using var rig = new ShipRig(worldSpec: ZoneSpec.StartingWorld(10, 20));
        Ship ship = rig.Add(height: 300);
        ship.State.Flags |= ShipFlags.EngineStopped;
        using var inputs = new Predictor(rig.Zone.Spec, rig.Zone.Surface, null);
        var prediction = new ShipPredictor(inputs, () => rig.Zone.Ships().ShipContext);
        prediction.Reset(ship.Def, 1, 0, ship.State, 0);
        for (uint tick = 1; tick <= 60; tick++)
        {
            InputCommand command = NetConvert.ToCommand(new MoveInput { View = Vector3.UnitZ }, new ActionInput { HeldSlot = ActionInput.NoSlot });
            command.Ship = new[] { ShipConvert.ToControls(new ShipInput { Throttle = 1, Vertical = 1, Buttons = ShipButtons.Boost }) };
            inputs.RecordInput(tick, command);
            prediction.Predict(tick);
            rig.Fly(ship, ShipConvert.ToShipInput(command), 1);
            Assert.True(prediction.Latest.Equals(ship.State), "off prediction agrees with the server despite movement/boost input");
        }
        Assert.Near(500, ship.State.Fuel, 1e-6, "a seated pilot does not burn fuel with engine off");
    }

    [Test]
    public static void SeatOrbitStaysCentredOnThePlayerAtAnyPlanetLatitude()
    {
        var camera = new SeatOrbitCamera();
        Vector3 player = new(500, 800, -600), up = Vector3.Normalize(player);
        var before = camera.Follow(player, up, -Vector3.UnitZ);
        camera.Drag(1.5f, 0.4f);
        camera.Zoom(2);
        var after = camera.Follow(player, up, -Vector3.UnitZ);
        Assert.Near(camera.Distance, Vector3.Distance(player, after.Eye), 1e-4, "distance is measured from the player's eye, not ship centre");
        Assert.True(Vector3.Dot(after.Forward, Vector3.Normalize(player - after.Eye)) > 0.9999f, "the orbit looks at the player");
        Assert.True(Vector3.Distance(before.Eye, after.Eye) > 1, "drag and wheel change the view");
        Vector3 shift = new(40, -20, 10);
        var moved = camera.Follow(player + shift, up, -Vector3.UnitZ);
        Assert.True(Vector3.Distance(after.Eye + shift, moved.Eye) < 1e-4, "moving the player moves the camera equally without trailing");
        camera.Zoom(1000);
        Assert.Near(SeatOrbitCamera.MinimumDistance, camera.Distance, 1e-6, "minimum orbit distance");
        camera.Zoom(-1000);
        Assert.Near(SeatOrbitCamera.MaximumDistance, camera.Distance, 1e-6, "maximum orbit distance");
    }
}
