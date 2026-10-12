using System;
using System.Globalization;
using System.Numerics;

namespace Hollow.Simulation.Movement;

public enum Stance : byte
{
    Standing,
    Crouching,
}

/// <summary>Buttons of one tick of input.</summary>
[Flags]
public enum MoveButtons : byte
{
    None = 0,

    /// <summary>Jump is held (swims up in water).</summary>
    Jump = 1,

    /// <summary>Jump went down since the previous tick: jumps when on the ground.</summary>
    JumpPressed = 2,

    /// <summary>Crouch is held (dives in water).</summary>
    Crouch = 4,

    /// <summary>Sprint is held.</summary>
    Sprint = 8,
}

/// <summary>What a character is doing, as the last step found it. Output only: the step never reads it.</summary>
[Flags]
public enum MoveFlags : byte
{
    None = 0,
    OnGround = 1,
    Swimming = 2,
    Sprinting = 4,
    Dead = 8,
}

/// <summary>
/// One tick of a player's movement intent. The client builds it from the same quantised input command the server
/// receives, so both sides step with identical values.
/// </summary>
public struct MoveInput
{
    /// <summary>−1 (back) … 1 (forward).</summary>
    public float Forward;

    /// <summary>−1 (left) … 1 (right).</summary>
    public float Right;

    /// <summary>View yaw in radians about the zone's +Y: 0 faces −Z (north), growing anticlockwise seen from above.</summary>
    public float Yaw;

    /// <summary>View pitch in radians: positive looks up.</summary>
    public float Pitch;

    public MoveButtons Buttons;

    public readonly bool Has(MoveButtons button) => (Buttons & button) != 0;
}

/// <summary>
/// Everything a character's next movement step depends on (with its input and the static world). The step is a pure
/// function of this, so the client can rewind to a state the server sent and replay its inputs exactly.
/// </summary>
public struct CharacterState : IEquatable<CharacterState>
{
    /// <summary>Feet, in the zone frame (metres).</summary>
    public Vector3 Position;

    /// <summary>Metres per second, in the zone frame.</summary>
    public Vector3 Velocity;

    public Stance Stance;

    public byte Health;

    /// <summary>What the last step found (not an input of the next).</summary>
    public MoveFlags Flags;

    public readonly bool IsDead => Health == 0;

    public readonly bool Equals(CharacterState other) =>
        Position.Equals(other.Position) && Velocity.Equals(other.Velocity) && Stance == other.Stance &&
        Health == other.Health && Flags == other.Flags;

    public override readonly bool Equals(object? obj) => obj is CharacterState s && Equals(s);

    public override readonly int GetHashCode() => HashCode.Combine(Position, Velocity, Stance, Health, Flags);

    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"at ({Position.X:0.000}, {Position.Y:0.000}, {Position.Z:0.000}) moving ({Velocity.X:0.00}, {Velocity.Y:0.00}, {Velocity.Z:0.00}) {Stance} {Health} hp {Flags}");
}
