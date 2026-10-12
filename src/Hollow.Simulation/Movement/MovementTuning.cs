namespace Hollow.Simulation.Movement;

/// <summary>
/// The numbers of on-foot movement (GROUND.md §4.5, §6). Rust's movement, through RustMVP's legacy spec
/// (<c>docs/architecture/legacy/weapons_character_ui.md</c> §2.1–2.4, the values of its character movement
/// component); where RustMVP had nothing (fall damage, swimming) the source is given on the value. Distances in
/// metres, speeds in m/s. The server sends nothing of this: both sides run the same build.
/// </summary>
public sealed record MovementTuning
{
    public static readonly MovementTuning Default = new();

    // ---------------------------------------------------------------- body (RustMVP §2.1, §2.2)

    /// <summary>Capsule radius (RustMVP 32 cm).</summary>
    public float Radius { get; init; } = 0.32f;

    /// <summary>Standing capsule height from the feet (RustMVP half-height 88 cm).</summary>
    public float StandingHeight { get; init; } = 1.76f;

    /// <summary>Crouching capsule height (RustMVP crouched half-height 55 cm).</summary>
    public float CrouchingHeight { get; init; } = 1.10f;

    /// <summary>Eye height above the feet, standing and crouching (RustMVP 170 cm and 102 cm).</summary>
    public float StandingEyeHeight { get; init; } = 1.70f;

    public float CrouchingEyeHeight { get; init; } = 1.02f;

    /// <summary>How fast the eye follows a change of stance, per second (RustMVP interpolation rate 18).</summary>
    public float EyeHeightRate { get; init; } = 18f;

    /// <summary>The highest step walked up without jumping (RustMVP 40 cm).</summary>
    public float StepHeight { get; init; } = 0.40f;

    /// <summary>The steepest ground that can be walked on, in degrees (RustMVP 46°).</summary>
    public float MaxSlopeDegrees { get; init; } = 46f;

    // ---------------------------------------------------------------- ground (RustMVP §2.2)

    public float WalkSpeed { get; init; } = 2.8f;

    /// <summary>Only when moving straight forward and not crouching.</summary>
    public float SprintSpeed { get; init; } = 5.5f;

    public float CrouchSpeed { get; init; } = 1.3f;

    /// <summary>Acceleration toward the wanted speed (RustMVP 6000 cm/s²).</summary>
    public float Acceleration { get; init; } = 60f;

    /// <summary>Deceleration when there is no input (RustMVP 6000 cm/s²).</summary>
    public float BrakingDeceleration { get; init; } = 60f;

    /// <summary>Ground friction: how quickly a change of direction takes (RustMVP 8).</summary>
    public float GroundFriction { get; init; } = 8f;

    /// <summary>Friction is this much stronger while braking (the movement component's default braking factor).</summary>
    public float BrakingFrictionFactor { get; init; } = 2f;

    /// <summary>Fraction of <see cref="Acceleration"/> available in the air (RustMVP 0.25).</summary>
    public float AirControl { get; init; } = 0.25f;

    // ---------------------------------------------------------------- jumping and falling

    /// <summary>Upward speed of a jump, standing and crouching (RustMVP 700 and 760 cm/s).</summary>
    public float JumpSpeed { get; init; } = 7.0f;

    public float CrouchJumpSpeed { get; init; } = 7.6f;

    /// <summary>
    /// Characters fall with the planet's surface gravity times this (RustMVP's gravity scale 1.8). On a 1 g world that
    /// is RustMVP's 17.6 m/s²; on the first world (0.92 g) 16.2 m/s², so a standing jump rises 1.51 m.
    /// </summary>
    public float GravityScale { get; init; } = 1.8f;

    /// <summary>
    /// Fall damage (RustMVP has none; this is Rust's rule): landing faster than <see cref="SafeLandingSpeed"/> along
    /// "up" costs <see cref="FallDamageScale"/> × (speed − safe) / (<see cref="LethalScaleSpeed"/> − safe) health,
    /// rounded up. 100 health is gone at 32 m/s, a fall of about 31 m on the first world.
    /// </summary>
    public float SafeLandingSpeed { get; init; } = 15f;

    public float LethalScaleSpeed { get; init; } = 100f;

    public float FallDamageScale { get; init; } = 500f;

    // ---------------------------------------------------------------- swimming (our own until the owner tunes them)

    /// <summary>A character swims once the water is this deep, as a fraction of its standing height (Rust: 0.65).</summary>
    public float SwimDepth { get; init; } = 0.65f;

    /// <summary>Depth, as a fraction of standing height, a swimmer floats at when not steering up or down.</summary>
    public float FloatDepth { get; init; } = 0.75f;

    public float SwimSpeed { get; init; } = 2.0f;

    /// <summary>Swimming speed with sprint held.</summary>
    public float FastSwimSpeed { get; init; } = 3.0f;

    /// <summary>How quickly a swimmer reaches the wanted velocity (water drag).</summary>
    public float SwimAcceleration { get; init; } = 8f;

    /// <summary>Strongest vertical speed buoyancy gives.</summary>
    public float BuoyancySpeed { get; init; } = 1.0f;

    // ---------------------------------------------------------------- health

    public byte MaxHealth { get; init; } = 100;
}
