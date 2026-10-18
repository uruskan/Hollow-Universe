using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

public static class ClimbingTests
{
    private sealed class Ladder : IClimbSource
    {
        public void Nearby(ulong frame, Vector3 position, List<ClimbSurface> into) =>
            into.Add(new ClimbSurface(1, new Vector3(0, 1000, 0), new Vector3(0, 1008, 0), 0.6f, 2));
    }

    [Test]
    public static void ClimbingMotorAscendsHoldsDescendsAndJumpsAway()
    {
        var tuning = MovementTuning.Default;
        using var world = new PhysicsWorld(new LevelSurface(1000), ZoneFrame.World);
        var state = new CharacterState { Position = new Vector3(0, 1002, 0.42f), Health = tuning.MaxHealth };
        var context = new MoveContext(new ZoneMoveFrame(ZoneFrame.World, 0, 9.81f, 1000), tuning, new Climbing(new Ladder()));
        lock (JoltRuntime.Gate)
        {
            using var body = new CharacterBody(world, tuning, state.Position, Vector3.UnitY);
            var input = new MoveInput { View = -Vector3.UnitZ, Forward = 1 };
            for (int tick = 0; tick < 30; tick++) CharacterMotor.Step(ref state, input, body, context);
            Assert.Near(1004, state.Position.Y, 0.01, "climb speed uses physical capsule movement");
            input.Forward = 0;
            float hold = state.Position.Y;
            for (int tick = 0; tick < 10; tick++) CharacterMotor.Step(ref state, input, body, context);
            Assert.Near(hold, state.Position.Y, 0.01, "no input holds on ladder without gravity drift");
            input.Forward = -1;
            for (int tick = 0; tick < 15; tick++) CharacterMotor.Step(ref state, input, body, context);
            Assert.Near(hold - 1, state.Position.Y, 0.01, "backward descends at the configured speed");
            input.Forward = 0;
            input.Buttons = MoveButtons.JumpPressed;
            StepEvents jumped = CharacterMotor.Step(ref state, input, body, context);
            Assert.True(jumped.Jumped && state.Velocity.Z > 1 && state.Velocity.Y > 0, "jump dismounts away from the ladder");
        }
    }
}
