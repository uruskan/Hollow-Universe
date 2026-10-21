using System;
using System.Numerics;
using Hollow.Simulation.Movement;
using JoltPhysicsSharp;

namespace Hollow.Simulation.Physics;

/// <summary>
/// A character's collision in a space (GROUND.md §4.5): Jolt's virtual character, a kinematic capsule outside the
/// rigid-body solver, bound to the physics it steps in: a zone's <see cref="PhysicsWorld"/> or a ship's
/// <see cref="ShipSpace"/> (IRON_WAKE.md §4.3). It holds no state between steps that matters: <see cref="Restore"/> puts
/// it exactly where a <see cref="CharacterState"/> says before every step, so the server and a rewinding client get the
/// same result, and one body serves every character stepping in its space in turn. Callers hold
/// <see cref="JoltRuntime.Gate"/>.
/// </summary>
public sealed class CharacterBody : IDisposable
{
    private static Shape? _standing;
    private static Shape? _crouching;
    private static MovementTuning? _shapesFor;

    private readonly PhysicsSystem _system;
    private readonly CharacterVirtual _character;
    private readonly MovementTuning _tuning;
    private Stance _stance;

    public CharacterBody(PhysicsWorld world, MovementTuning tuning, Vector3 position, Vector3 up)
        : this(world.Jolt, tuning, position, up)
    {
    }

    public CharacterBody(ShipSpace space, MovementTuning tuning, Vector3 position, Vector3 up)
        : this(space.Jolt, tuning, position, up)
    {
    }

    private CharacterBody(PhysicsSystem system, MovementTuning tuning, Vector3 position, Vector3 up)
    {
        _system = system;
        _tuning = tuning;
        lock (JoltRuntime.Gate)
        {
            EnsureShapes(tuning);
            var settings = new CharacterVirtualSettings
            {
                Shape = _standing!,
                Up = up,
                MaxSlopeAngle = tuning.MaxSlopeDegrees * MathF.PI / 180f,
                // Contacts below the centre of the capsule's bottom sphere hold the character up.
                SupportingVolume = new Plane(Vector3.UnitY, -tuning.Radius),
                Mass = 80f,
                MaxStrength = 100f,
                CharacterPadding = 0.02f,
                PenetrationRecoverySpeed = 1f,
                PredictiveContactDistance = 0.1f,
                EnhancedInternalEdgeRemoval = true,
            };
            _character = new CharacterVirtual(settings, position, UpRotation(up), 0, system);
        }
    }

    public Vector3 Position => _character.Position;

    public GroundState GroundState => _character.GroundState;

    public Vector3 GroundNormal => _character.GroundNormal;

    /// <summary>On walkable ground (not a slope too steep to stand on).</summary>
    public bool OnWalkableGround => _character.GroundState == GroundState.OnGround;

    /// <summary>The body under the feet after the last step (a parked ship's, IRON_WAKE.md §4.5), when there is ground.</summary>
    public uint GroundBody => _character.GroundBodyId;

    /// <summary>
    /// Puts the character exactly at a state: up and orientation from <paramref name="up"/>, position, velocity and
    /// capsule, then finds its contacts there afresh. Whatever happened to this object before doesn't matter.
    /// </summary>
    public void Restore(Vector3 position, Vector3 velocity, Vector3 up, Stance stance)
    {
        _character.Up = up;
        _character.Rotation = UpRotation(up);
        _character.Position = position;
        _character.LinearVelocity = velocity;
        if (stance != _stance)
        {
            // Forced: the state says this is the capsule (no room check).
            // A crawling character keeps the crouched capsule (its hit shapes lie down; its body needn't).
            _character.SetShape(0f, stance == Stance.Standing ? _standing! : _crouching!, float.MaxValue, PhysicsWorld.MovingLayer, _system);
            _stance = stance;
        }
        _character.RefreshContacts(PhysicsWorld.MovingLayer, _system);
    }

    /// <summary>Changes the capsule if there is room; false (and unchanged) if standing up is blocked.</summary>
    public bool TrySetStance(Stance stance, float deltaTime)
    {
        if (stance == _stance)
        {
            return true;
        }
        Shape shape = stance == Stance.Standing ? _standing! : _crouching!;
        // Allow the padding's worth of overlap, so standing up on uneven ground isn't refused for nothing.
        if (!_character.SetShape(deltaTime, shape, 1.5f * _character.CharacterPadding, PhysicsWorld.MovingLayer, _system))
        {
            return false;
        }
        _stance = stance;
        return true;
    }

    /// <summary>
    /// Moves with <paramref name="velocity"/> for one step, sliding along what it hits, walking up steps up to the step
    /// height and, when <paramref name="stickToFloor"/>, staying on the ground going down slopes and over small drops.
    /// </summary>
    public void Move(Vector3 velocity, float deltaTime, Vector3 up, bool stickToFloor)
    {
        _character.LinearVelocity = velocity;
        var settings = new ExtendedUpdateSettings
        {
            StickToFloorStepDown = stickToFloor ? up * -0.5f : Vector3.Zero,
            WalkStairsStepUp = up * _tuning.StepHeight,
            WalkStairsMinStepForward = 0.02f,
            WalkStairsStepForwardTest = 0.15f,
            WalkStairsCosAngleForwardContact = MathF.Cos(75f * MathF.PI / 180f),
            WalkStairsStepDownExtra = Vector3.Zero,
        };
        _character.ExtendedUpdate(deltaTime, settings, PhysicsWorld.MovingLayer, _system);
    }

    /// <summary>The rotation that turns +Y into <paramref name="up"/> by the shortest arc.</summary>
    public static Quaternion UpRotation(Vector3 up)
    {
        float d = up.Y;
        if (d > 0.999999f)
        {
            return Quaternion.Identity;
        }
        if (d < -0.999999f)
        {
            return new Quaternion(1, 0, 0, 0);
        }
        // q = (Y × up, 1 + Y·up), normalised.
        return Quaternion.Normalize(new Quaternion(up.Z, 0, -up.X, 1 + d));
    }

    private static void EnsureShapes(MovementTuning tuning)
    {
        if (_shapesFor == tuning && _standing != null)
        {
            return;
        }
        _standing = Capsule(tuning.Radius, tuning.StandingHeight);
        _crouching = Capsule(tuning.Radius, tuning.CrouchingHeight);
        _shapesFor = tuning;
    }

    // A capsule standing on the origin: the character's position is its feet.
    private static Shape Capsule(float radius, float height)
    {
        float half = height / 2;
        using var capsule = new CapsuleShapeSettings(half - radius, radius).Owned();
        using var placed = new RotatedTranslatedShapeSettings(new Vector3(0, half, 0), Quaternion.Identity, capsule).Owned();
        return placed.Create();
    }

    public void Dispose()
    {
        lock (JoltRuntime.Gate)
        {
            _character.Dispose();
        }
    }
}
