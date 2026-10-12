using System;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Simulation.Movement;

/// <summary>
/// The physics spike of GROUND.md §4.5, kept as a check: a fixed script of inputs walks a character over ground from
/// <see cref="ISurface.GroundCollision"/> on a stand-in planet of the first world's size. Every process that runs it
/// must print the same hash; the tests run it, and so does the Godot client (<c>--physics-check</c>), which is how
/// we know prediction in the game matches the server.
/// </summary>
public static class MovementCheck
{
    public const int Ticks = 600;

    public readonly record struct Result(Vector3 Position, Vector3 Velocity, byte Health, ulong Hash, double Milliseconds, int Tiles)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture,
            $"physics check: {Ticks} ticks, final position ({Position.X:R}, {Position.Y:R}, {Position.Z:R}), health {Health}, " +
            $"hash 0x{Hash:X16} ({Milliseconds:0} ms, {Tiles} ground tiles)");
    }

    /// <summary>Runs the script in a fresh physics world and hashes every state it passes through.</summary>
    public static Result Run()
    {
        var surface = new SphereSurface(6_435_000, 0xF1257);
        ZoneFrame frame = ZoneFrame.At(surface, 0.62, -1.1);
        MovementTuning tuning = MovementTuning.Default;
        var context = new MoveContext(frame, surface.SeaRadius, MoveContext.CharacterGravity(0.92, tuning), tuning);
        long start = Stopwatch.GetTimestamp();
        using var world = new PhysicsWorld(surface, frame);
        var state = new CharacterState { Position = new Vector3(0, 3, 0), Health = tuning.MaxHealth };
        world.EnsureGround(state.Position);
        using var body = new CharacterBody(world, tuning, state.Position, frame.UpAt(state.Position));
        ulong hash = 0;
        lock (JoltRuntime.Gate)
        {
            for (int tick = 0; tick < Ticks; tick++)
            {
                world.EnsureGround(state.Position);
                MoveInput input = Script(tick);
                CharacterMotor.Step(ref state, input, body, context);
                hash = Hash64.Combine(hash, Bits(state.Position));
                hash = Hash64.Combine(hash, Bits(state.Velocity));
                hash = Hash64.Combine(hash, (ulong)state.Stance << 16 | (ulong)state.Health << 8 | (ulong)state.Flags);
            }
        }
        return new Result(state.Position, state.Velocity, state.Health, hash,
            Stopwatch.GetElapsedTime(start).TotalMilliseconds, world.TileCount);
    }

    /// <summary>
    /// Twenty seconds of input: walk, sprint while turning, jump now and then, crouch, strafe, walk backward, look up
    /// and down. Covers every branch of the ground movement and crosses several ground tiles.
    /// </summary>
    public static MoveInput Script(int tick)
    {
        var input = new MoveInput { Yaw = tick * 0.004f, Pitch = 0.3f * MathF.Sin(tick * 0.05f) };
        int phase = tick / 60;
        switch (phase)
        {
            case 0:
            case 1:
                input.Forward = 1;
                break;
            case 2:
            case 3:
            case 4:
                input.Forward = 1;
                input.Buttons |= MoveButtons.Sprint;
                break;
            case 5:
                input.Forward = 1;
                input.Buttons |= MoveButtons.Crouch;
                break;
            case 6:
                input.Right = 1;
                break;
            case 7:
                input.Forward = -1;
                input.Right = -0.5f;
                break;
            case 8:
                input.Forward = 0.7f;
                input.Right = 0.7f;
                break;
            default:
                break;
        }
        if (tick % 45 == 20)
        {
            input.Buttons |= MoveButtons.Jump | MoveButtons.JumpPressed;
        }
        return input;
    }

    private static ulong Bits(Vector3 v) =>
        Hash64.Of(BitConverter.SingleToUInt32Bits(v.X), BitConverter.SingleToUInt32Bits(v.Y), BitConverter.SingleToUInt32Bits(v.Z));
}
