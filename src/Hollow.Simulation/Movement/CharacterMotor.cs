using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Simulation.Movement;

/// <summary>What a movement step needs to know about where it happens.</summary>
public readonly struct MoveContext
{
    public MoveContext(ZoneFrame frame, double seaRadius, float gravity, MovementTuning tuning)
    {
        Frame = frame;
        SeaRadius = seaRadius;
        Gravity = gravity;
        Tuning = tuning;
    }

    public readonly ZoneFrame Frame;

    /// <summary>Radius of the sea surface in metres, 0 without one (<see cref="ISurface.SeaRadius"/>).</summary>
    public readonly double SeaRadius;

    /// <summary>A character's gravity in m/s²: the planet's, times <see cref="MovementTuning.GravityScale"/>.</summary>
    public readonly float Gravity;

    public readonly MovementTuning Tuning;

    /// <summary>A character's gravity on a body with this surface gravity (in g, as the generator gives it).</summary>
    public static float CharacterGravity(double surfaceGravityG, MovementTuning tuning) =>
        (float)(surfaceGravityG * 9.80665 * tuning.GravityScale);
}

/// <summary>What happened in a step, besides the new state.</summary>
public struct StepEvents
{
    /// <summary>Health lost on landing this step.</summary>
    public int FallDamage;

    /// <summary>Speed toward the ground when landing this step (0 if it didn't land).</summary>
    public float LandingSpeed;

    public bool Jumped;
}

/// <summary>
/// On-foot movement (GROUND.md §4.5): walk, sprint, crouch, jump, fall damage and swimming. The server and the client's
/// prediction run exactly this, one fixed tick at a time. "Up" is always away from the body's centre at the character
/// (<see cref="ZoneFrame.UpAt"/>), never +Y.
/// </summary>
public static class CharacterMotor
{
    /// <summary>The fixed tick of every zone and of prediction.</summary>
    public const int TickRate = 30;

    public const float DeltaTime = 1f / TickRate;

    /// <summary>Advances a character by one tick. Callers hold <see cref="JoltRuntime.Gate"/>.</summary>
    public static StepEvents Step(ref CharacterState state, in MoveInput input, CharacterBody body, in MoveContext context)
    {
        var events = new StepEvents();
        MovementTuning tuning = context.Tuning;
        const float dt = DeltaTime;
        if (state.IsDead)
        {
            state.Flags = MoveFlags.Dead;
            return events;
        }

        Vector3 up = context.Frame.UpAt(state.Position);
        body.Restore(state.Position, state.Velocity, up, state.Stance);

        // Stance: crouch at once; stand up only where there is room.
        Stance wanted = input.Has(MoveButtons.Crouch) ? Stance.Crouching : Stance.Standing;
        if (wanted != state.Stance && body.TrySetStance(wanted, dt))
        {
            state.Stance = wanted;
        }
        bool crouching = state.Stance == Stance.Crouching;

        // The view's horizontal basis at the character, and the wanted direction of travel.
        (Vector3 forward, Vector3 right) = HorizontalBasis(input.Yaw, up);
        Vector3 wish = forward * Math.Clamp(input.Forward, -1f, 1f) + right * Math.Clamp(input.Right, -1f, 1f);
        float wishLength = wish.Length();
        if (wishLength > 1f)
        {
            wish /= wishLength;
            wishLength = 1f;
        }

        // Sprint only straight forward (RustMVP: W alone; W+A, W+D, strafing and backward don't sprint).
        bool sprinting = input.Has(MoveButtons.Sprint) && !crouching && input.Forward > 0f &&
            MathF.Abs(input.Right) <= MathF.Max(0.1f, input.Forward * 0.005f);
        float maxSpeed = crouching ? tuning.CrouchSpeed : sprinting ? tuning.SprintSpeed : tuning.WalkSpeed;

        float submersion = Submersion(state.Position, context);
        bool swimming = submersion >= tuning.SwimDepth;

        Vector3 velocity = state.Velocity;
        float verticalSpeed = Vector3.Dot(velocity, up);
        Vector3 horizontal = velocity - up * verticalSpeed;
        bool supportedBefore = body.OnWalkableGround && verticalSpeed < 0.1f;
        Vector3 requested, next;

        if (swimming)
        {
            requested = next = SwimVelocity(velocity, input, forward, right, up, submersion, input.Has(MoveButtons.Sprint), context);
        }
        else
        {
            if (supportedBefore)
            {
                horizontal = GroundVelocity(horizontal, wish, wishLength, maxSpeed, tuning);
                verticalSpeed = 0f;
                if (input.Has(MoveButtons.JumpPressed))
                {
                    verticalSpeed = crouching ? tuning.CrouchJumpSpeed : tuning.JumpSpeed;
                    events.Jumped = true;
                }
            }
            else
            {
                horizontal = AirVelocity(horizontal, wish, wishLength, maxSpeed, tuning);
            }
            // Gravity by the midpoint rule: move with the tick's mean vertical speed, so a jump peaks at exactly v²/2g.
            // On the ground this also presses into it, so contact holds over bumps.
            requested = horizontal + up * (verticalSpeed - 0.5f * context.Gravity * dt);
            next = horizontal + up * (verticalSpeed - context.Gravity * dt);
        }

        Vector3 before = state.Position;
        bool stick = !swimming && supportedBefore && !events.Jumped;
        body.Move(requested, dt, up, stick);
        Vector3 after = body.Position;
        bool supportedAfter = body.OnWalkableGround;

        // Velocity for the next step: as integrated, except that the ground ends a fall and a ceiling ends a rise.
        if (!swimming)
        {
            float requestedUp = Vector3.Dot(requested, up);
            float movedUp = Vector3.Dot(after - before, up) / dt;
            float nextUp = Vector3.Dot(next, up);
            if (supportedAfter && requestedUp <= 0.1f)
            {
                nextUp = 0f;
            }
            else if (requestedUp > 0f && movedUp < requestedUp - 0.01f)
            {
                nextUp = MathF.Max(0f, movedUp);
            }
            next = horizontal + up * nextUp;

            // Landing: fall damage from the speed toward the ground (Rust's rule, MovementTuning).
            if (supportedAfter && !supportedBefore && requestedUp < 0f)
            {
                float speed = -requestedUp;
                events.LandingSpeed = speed;
                if (speed > tuning.SafeLandingSpeed)
                {
                    float fraction = (speed - tuning.SafeLandingSpeed) / (tuning.LethalScaleSpeed - tuning.SafeLandingSpeed);
                    events.FallDamage = (int)MathF.Ceiling(fraction * tuning.FallDamageScale);
                    state.Health = (byte)Math.Max(0, state.Health - events.FallDamage);
                }
            }
        }

        state.Position = after;
        state.Velocity = next;
        state.Flags = (supportedAfter && !swimming ? MoveFlags.OnGround : MoveFlags.None)
            | (swimming ? MoveFlags.Swimming : MoveFlags.None)
            | (sprinting && wishLength > 0f ? MoveFlags.Sprinting : MoveFlags.None)
            | (state.IsDead ? MoveFlags.Dead : MoveFlags.None);
        return events;
    }

    /// <summary>
    /// The horizontal forward and right of a view yaw at a point with this <paramref name="up"/>: the zone-frame
    /// directions laid onto the local horizontal plane.
    /// </summary>
    public static (Vector3 Forward, Vector3 Right) HorizontalBasis(float yaw, Vector3 up)
    {
        float sin = (float)DMath.Sin(yaw), cos = (float)DMath.Cos(yaw);
        var flat = new Vector3(-sin, 0f, -cos);
        Vector3 forward = flat - up * Vector3.Dot(flat, up);
        float length = forward.Length();
        forward = length > 1e-6f ? forward / length : Vector3.Normalize(Vector3.Cross(up, Vector3.UnitX));
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, up));
        return (forward, right);
    }

    /// <summary>The view direction for a yaw and pitch at a point with this <paramref name="up"/>.</summary>
    public static Vector3 ViewDirection(float yaw, float pitch, Vector3 up)
    {
        (Vector3 forward, _) = HorizontalBasis(yaw, up);
        return forward * (float)DMath.Cos(pitch) + up * (float)DMath.Sin(pitch);
    }

    /// <summary>How deep the feet are below the sea surface, as a fraction of the standing height (0 when dry).</summary>
    public static float Submersion(Vector3 position, in MoveContext context)
    {
        if (context.SeaRadius <= 0)
        {
            return 0f;
        }
        double depth = context.SeaRadius - context.Frame.ToBody(position).Length;
        return depth <= 0 ? 0f : (float)Math.Min(1.0, depth / context.Tuning.StandingHeight);
    }

    // Walking (the movement component's model): friction turns the velocity toward the wanted direction, then
    // acceleration adds speed up to the maximum; with no input, friction and braking stop the character.
    private static Vector3 GroundVelocity(Vector3 velocity, Vector3 wish, float wishLength, float maxSpeed, MovementTuning tuning)
    {
        const float dt = DeltaTime;
        if (wishLength <= 1e-4f)
        {
            float speed = velocity.Length();
            if (speed <= 0f)
            {
                return Vector3.Zero;
            }
            float friction = tuning.GroundFriction * tuning.BrakingFrictionFactor;
            Vector3 braked = velocity + (-friction * velocity - tuning.BrakingDeceleration * (velocity / speed)) * dt;
            // Never reverse; stop below 10 cm/s.
            return Vector3.Dot(braked, velocity) <= 0f || braked.LengthSquared() < 0.01f ? Vector3.Zero : braked;
        }
        Vector3 direction = wish / wishLength;
        float target = maxSpeed * wishLength;
        float current = velocity.Length();
        if (current > target)
        {
            // Faster than allowed (sprint released, stance changed): brake down to the limit along the travel.
            float slowed = MathF.Max(target, current - tuning.BrakingDeceleration * dt);
            velocity = velocity / current * slowed;
            current = slowed;
        }
        velocity -= (velocity - direction * current) * MathF.Min(dt * tuning.GroundFriction, 1f);
        velocity += direction * (tuning.Acceleration * wishLength * dt);
        float length = velocity.Length();
        return length > target ? velocity / length * target : velocity;
    }

    // In the air: no friction or braking; air control adds a little acceleration, never past the walking limit (or
    // the speed already carried).
    private static Vector3 AirVelocity(Vector3 velocity, Vector3 wish, float wishLength, float maxSpeed, MovementTuning tuning)
    {
        if (wishLength <= 1e-4f)
        {
            return velocity;
        }
        float before = velocity.Length();
        velocity += wish / wishLength * (tuning.Acceleration * tuning.AirControl * wishLength * DeltaTime);
        float limit = MathF.Max(before, maxSpeed * wishLength);
        float length = velocity.Length();
        return length > limit ? velocity / length * limit : velocity;
    }

    // Swimming: steer in the view direction (look down to dive), jump and crouch swim up and down, and float at
    // FloatDepth otherwise. No gravity; water drag eases the velocity toward the target.
    private static Vector3 SwimVelocity(Vector3 velocity, in MoveInput input, Vector3 forward, Vector3 right, Vector3 up,
        float submersion, bool fast, in MoveContext context)
    {
        MovementTuning tuning = context.Tuning;
        Vector3 view = forward * (float)DMath.Cos(input.Pitch) + up * (float)DMath.Sin(input.Pitch);
        float vertical = input.Has(MoveButtons.Jump) ? 1f : input.Has(MoveButtons.Crouch) ? -1f : 0f;
        Vector3 wish = view * Math.Clamp(input.Forward, -1f, 1f) + right * Math.Clamp(input.Right, -1f, 1f) + up * vertical;
        float length = wish.Length();
        if (length > 1f)
        {
            wish /= length;
        }
        Vector3 target = wish * (fast ? tuning.FastSwimSpeed : tuning.SwimSpeed);
        if (vertical == 0f)
        {
            // Rise when deeper than the float depth, sink when higher.
            float error = (submersion - tuning.FloatDepth) * tuning.StandingHeight;
            target += up * Math.Clamp(error * 2f, -tuning.BuoyancySpeed, tuning.BuoyancySpeed);
        }
        Vector3 change = target - velocity;
        float step = tuning.SwimAcceleration * DeltaTime;
        float distance = change.Length();
        return distance <= step ? target : velocity + change / distance * step;
    }
}
