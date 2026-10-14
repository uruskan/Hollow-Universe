using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

public static class PhysicsTests
{
    /// <summary>
    /// GROUND.md §4.5, the physics spike: Jolt runs here, and the same inputs give the same result in two fresh worlds.
    /// The Godot client runs the same check (<c>-- --physics-check</c>) and must print the same hash.
    /// </summary>
    [Test]
    public static void MovementCheckIsRepeatable()
    {
        MovementCheck.Result first = MovementCheck.Run();
        MovementCheck.Result second = MovementCheck.Run();
        Fixtures.Report(first.ToString());
        Assert.Equal(first.Hash, second.Hash, "hash of every state in two runs");
        Vector3 start = new GroundProbe(new SphereSurface(MovementCheck.Radius, 0xF1257), ZoneFrame.World).On(ZoneFrame.Direction(0.62, -1.1));
        Assert.True(Vector3.Distance(first.Position, start) > 40, "the character got somewhere");
        Assert.Equal(100, (int)first.Health, "nothing in the script hurts");
    }

    /// <summary>
    /// GROUND.md §5.11: a body taken out of the world is kept and put back for the next body under its ID. A character
    /// walking the movement check's script through a grove of trees and boulders meets exactly the same world, bit
    /// for bit, whether the ground and props are new or came back from their parking (after other props had them),
    /// and whatever the broadphase went through.
    /// </summary>
    [Test]
    public static void KeptBodiesCollideAsNewOnes()
    {
        ulong fresh = Walk(reuse: false, out int made, out _, out int contacts);
        ulong reused = Walk(reuse: true, out _, out int back, out _);
        Fixtures.Report($"through the grove: hash 0x{fresh:X16}, {contacts} ticks touching a prop; {made} bodies made fresh, " +
            $"{back} put back from parking in the second world");
        Assert.Equal(fresh, reused, "the same walk among new and kept bodies");
        Assert.True(back > 600, "the second world reused its bodies");
        Assert.True(contacts > 10, "the walk meets the grove");
    }

    private static ulong Walk(bool reuse, out int made, out int back, out int touching)
    {
        var surface = new SphereSurface(MovementCheck.Radius, 0xF1257);
        ZoneFrame frame = ZoneFrame.World;
        MovementTuning tuning = MovementTuning.Default;
        var context = new MoveContext(frame, surface.SeaRadius, MoveContext.CharacterGravity(0.92, tuning), tuning, surface.Radius);
        using var world = new PhysicsWorld(surface, frame) { MakePropBodiesAhead = reuse };
        var probe = new GroundProbe(surface, frame);
        Vector3 start = probe.On(ZoneFrame.Direction(0.62, -1.1));
        Vector3 Ground(float x, float z) => probe.Offset(start, x, z);
        void Grove(ulong salt)
        {
            for (int i = -14; i <= 14; i++)
            {
                for (int j = -14; j <= 14; j++)
                {
                    if (Math.Abs(i) + Math.Abs(j) < 2)
                    {
                        continue; // clear where the character starts
                    }
                    Vector3 at = Ground(i * 3.1f + (j & 1), j * 3.3f);
                    Vector3 up = frame.UpAt(at);
                    ulong key = (ulong)((i + 14) * 29 + j + 14) + salt;
                    world.AddProp(PropLayer.Nature, key, (i + j) % 3 == 0
                        ? PropShape.Sphere(at + up * 0.2f, 0.6f)
                        : PropShape.Capsule(at + up * 0.3f, at + up * 8f, 0.3f, up));
                }
            }
        }
        lock (JoltRuntime.Gate)
        {
            if (reuse)
            {
                // Other props and tiles first, then everything out: the walk's bodies come back from parking.
                world.EnsureGroundWithin(start, 160);
                Grove(0);
                Grove(1_000_000);
                world.OptimizeBroadPhase();
                for (ulong k = 0; k < 29 * 29; k++)
                {
                    world.RemoveProp(PropLayer.Nature, k);
                    world.RemoveProp(PropLayer.Nature, k + 1_000_000);
                }
                world.ReleaseUnusedGround(-1);
                world.OptimizeBroadPhase();
            }
            var state = new CharacterState { Position = start + frame.UpAt(start) * 3, Health = tuning.MaxHealth };
            world.EnsureGround(state.Position);
            Grove(0);
            world.OptimizeBroadPhase();
            using var body = new CharacterBody(world, tuning, state.Position, frame.UpAt(state.Position));
            ulong hash = 0;
            touching = 0;
            for (int tick = 0; tick < MovementCheck.Ticks; tick++)
            {
                world.EnsureGround(state.Position);
                world.OptimizeBroadPhase();
                Vector3 before = state.Position;
                MoveInput input = MovementCheck.Script(tick, frame.UpAt(state.Position));
                CharacterMotor.Step(ref state, input, body, context);
                hash = Hash64.Combine(hash, Hash64.Of(BitConverter.SingleToUInt32Bits(state.Position.X),
                    BitConverter.SingleToUInt32Bits(state.Position.Y), BitConverter.SingleToUInt32Bits(state.Position.Z)));
                hash = Hash64.Combine(hash, Hash64.Of(BitConverter.SingleToUInt32Bits(state.Velocity.X),
                    BitConverter.SingleToUInt32Bits(state.Velocity.Y), BitConverter.SingleToUInt32Bits(state.Velocity.Z)));
                // Held back by something: a prop (the ground is a gentle sphere).
                Vector3 moved = state.Position - before;
                Vector3 up = frame.UpAt(state.Position);
                Vector3 flat = moved - up * Vector3.Dot(moved, up);
                if ((input.Forward != 0 || input.Right != 0) && flat.Length() < 0.02f)
                {
                    touching++;
                }
            }
            made = world.BodiesMade;
            back = world.BodiesReused;
            return hash;
        }
    }
}
