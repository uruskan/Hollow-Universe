using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Simulation.Movement;

/// <summary>
/// What a movement step needs to know about where it happens: the frame it runs in (<see cref="IMoveFrame"/>: the
/// zone's, or a ship's, IRON_WAKE.md §4.3) and the movement's numbers.
/// </summary>
public readonly struct MoveContext
{
    public MoveContext(IMoveFrame frame, MovementTuning tuning)
    {
        Frame = frame;
        Tuning = tuning;
    }

    /// <summary>The zone's frame, as before ship spaces (the movement check, tests).</summary>
    public MoveContext(ZoneFrame frame, double seaRadius, float gravity, MovementTuning tuning, double radius = 0)
        : this(new ZoneMoveFrame(frame, seaRadius, gravity, radius), tuning)
    {
    }

    /// <summary>The frame: up, gravity, water and the space (IRON_WAKE.md §4.3).</summary>
    public readonly IMoveFrame Frame;

    /// <summary>The world's mean radius in metres (noclip's bounds); 0 when not known.</summary>
    public double Radius => Frame.Radius;

    /// <summary>A character's gravity in m/s²: the planet's, times <see cref="MovementTuning.GravityScale"/> (a ship's decks: theirs).</summary>
    public float Gravity => Frame.Gravity;

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
/// prediction run exactly this, one fixed tick at a time, in whichever frame the character is (IRON_WAKE.md §4.3): in
/// the zone's, "up" is away from the body's centre at the character (<see cref="ZoneFrame.UpAt"/>), never +Y; aboard a
/// ship, its +Y, with the decks' gravity.
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
        if (state.Stance == Stance.Seated)
        {
            // In a ship's seat (G6): the ship moves it, not its legs.
            state.Flags = MoveFlags.None;
            return events;
        }
        if (state.Has(MoveCheats.Noclip))
        {
            Fly(ref state, input, context);
            return events;
        }

        Vector3 up = context.Frame.UpAt(state.Position);
        body.Restore(state.Position, state.Velocity, up, state.Stance);

        // Stance: crouch at once; stand up only where there is room. A crawling (wounded) character stays down until the
        // server lets it up.
        bool crawling = state.Stance == Stance.Crawling;
        Stance wanted = crawling ? Stance.Crawling : input.Has(MoveButtons.Crouch) ? Stance.Crouching : Stance.Standing;
        if (wanted != state.Stance && body.TrySetStance(wanted, dt))
        {
            state.Stance = wanted;
        }
        bool crouching = state.Stance == Stance.Crouching;

        // The view's horizontal basis at the character, and the wanted direction of travel.
        (Vector3 forward, Vector3 right) = Facing.Basis(input.View, up);
        Vector3 wish = forward * Math.Clamp(input.Forward, -1f, 1f) + right * Math.Clamp(input.Right, -1f, 1f);
        float wishLength = wish.Length();
        if (wishLength > 1f)
        {
            wish /= wishLength;
            wishLength = 1f;
        }

        // Sprint only straight forward (RustMVP: W alone; W+A, W+D, strafing and backward don't sprint).
        bool sprinting = input.Has(MoveButtons.Sprint) && !crouching && !crawling && input.Forward > 0f &&
            MathF.Abs(input.Right) <= MathF.Max(0.1f, input.Forward * 0.005f);
        float maxSpeed = crawling ? tuning.CrawlSpeed : crouching ? tuning.CrouchSpeed : sprinting ? tuning.SprintSpeed : tuning.WalkSpeed;

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
                if (input.Has(MoveButtons.JumpPressed) && !crawling)
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
            requested = horizontal + up * (verticalSpeed - 0.5f * context.Gravity * dt);
            next = horizontal + up * (verticalSpeed - context.Gravity * dt);
            if (supportedBefore && !events.Jumped)
            {
                // Standing or walking: press into the ground along its normal, so contact holds over bumps but nothing
                // pushes the character down a slope it can stand on.
                requested = horizontal - body.GroundNormal * (0.5f * context.Gravity * dt);
            }
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
                if (speed > tuning.SafeLandingSpeed && !state.Has(MoveCheats.God))
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

    /// <summary>Noclip's speed (m/s), and with sprint held (GROUND.md §8.2).</summary>
    public const float NoclipSpeed = 10f;

    public const float NoclipFastSpeed = 40f;

    /// <summary>How far below the mean radius noclip may go, and how many radii from the centre it may reach.</summary>
    public const float NoclipDepth = 500f;

    public const float NoclipRadii = 3f;

    // Noclip (an admin's, GROUND.md §8.2): flies along the view through the ground and everything on it, W and S along
    // the view, A and D sideways, Space up and Ctrl down along the local up; no gravity, no collision, no fall damage.
    // It stays between NoclipDepth under the ground and NoclipRadii from the centre (docs/WHOLE_WORLD.md §6.2), never
    // near the centre, where "up" has no meaning.
    private static void Fly(ref CharacterState state, in MoveInput input, in MoveContext context)
    {
        Vector3 up = context.Frame.UpAt(state.Position);
        (_, Vector3 right) = Facing.Basis(input.View, up);
        Vector3 view = SafeView(input.View, up);
        float vertical = (input.Has(MoveButtons.Jump) ? 1f : 0f) - (input.Has(MoveButtons.Crouch) ? 1f : 0f);
        Vector3 wish = view * Math.Clamp(input.Forward, -1f, 1f) + right * Math.Clamp(input.Right, -1f, 1f) + up * vertical;
        float length = wish.Length();
        if (length > 1f)
        {
            wish /= length;
        }
        float speed = float.IsFinite(input.NoclipSpeed) && input.NoclipSpeed > 0 ? Math.Clamp(input.NoclipSpeed, 1, 200) : NoclipSpeed;
        state.Velocity = wish * speed * (input.Has(MoveButtons.Sprint) ? NoclipFastSpeed / NoclipSpeed : 1);
        Vector3 next = state.Position + state.Velocity * DeltaTime;
        if (context.Radius > 0)
        {
            float r = next.Length();
            float least = (float)Math.Max(context.Radius - NoclipDepth, context.Radius * 0.5), most = (float)(context.Radius * NoclipRadii);
            if (r < least || r > most)
            {
                next = (r > 1e-3f ? next / r : up) * Math.Clamp(r, least, most);
            }
        }
        state.Position = next;
        if (state.Stance == Stance.Crouching)
        {
            // Ctrl sinks rather than crouches.
            state.Stance = Stance.Standing;
        }
        state.Flags = MoveFlags.None;
    }

    /// <summary>
    /// The horizontal forward and right of a heading at a point with this <paramref name="up"/>: 0 faces local north,
    /// growing anticlockwise seen from above (<see cref="Facing.Basis(float, Vector3)"/>).
    /// </summary>
    public static (Vector3 Forward, Vector3 Right) HorizontalBasis(float yaw, Vector3 up) => Facing.Basis(yaw, up);

    /// <summary>The view direction for a heading and pitch at a point with this <paramref name="up"/>.</summary>
    public static Vector3 ViewDirection(float yaw, float pitch, Vector3 up) => Facing.View(yaw, pitch, up);

    /// <summary>
    /// An input's view as a unit direction (untrusted: a zero or broken view looks along local north), held at least a
    /// degree off straight up and down, as the client holds it.
    /// </summary>
    public static Vector3 SafeView(Vector3 view, Vector3 up)
    {
        float length = view.Length();
        if (!(length > 1e-6f) || !float.IsFinite(length))
        {
            return Facing.Compass(up).North;
        }
        view /= length;
        float sin = Vector3.Dot(view, up);
        const float limit = 0.99985f; // sin 89°
        if (MathF.Abs(sin) <= limit)
        {
            return view;
        }
        (Vector3 forward, _) = Facing.Basis(view, up);
        float s = MathF.CopySign(limit, sin);
        return forward * MathF.Sqrt(1 - s * s) + up * s;
    }

    /// <summary>How deep the feet are below the sea surface, as a fraction of the standing height (0 when dry).</summary>
    public static float Submersion(Vector3 position, in MoveContext context) => context.Frame.Submersion(position, context.Tuning);

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
        Vector3 view = SafeView(input.View, up);
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
