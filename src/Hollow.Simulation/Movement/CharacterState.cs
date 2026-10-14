using System;
using System.Globalization;
using System.Numerics;

namespace Hollow.Simulation.Movement;

public enum Stance : byte
{
    Standing,
    Crouching,

    /// <summary>Down and crawling (wounded, G4): slow, no jumping or sprinting; only the server sets and clears it.</summary>
    Crawling,

    /// <summary>In a ship's seat (G6): it doesn't move by itself; the ship carries it (only the server sets and clears it).</summary>
    Seated,
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
/// An admin's cheats (GROUND.md §8.2): no damage of any kind, and flying through everything. Only the server sets them; the
/// player's own state carries them to the client, whose prediction then flies (or doesn't take fall damage) exactly as the
/// server does.
/// </summary>
[Flags]
public enum MoveCheats : byte
{
    None = 0,

    /// <summary>No damage: the zone's <c>Hurt</c> takes nothing, and a landing costs no health.</summary>
    God = 1,

    /// <summary>Flies along the view through the ground and everything on it (<see cref="CharacterMotor"/>).</summary>
    Noclip = 2,
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

    /// <summary>
    /// Where the character looks: a unit direction in the zone's frame (the body's), its aim, and laid onto the ground
    /// its forward (docs/WHOLE_WORLD.md §5). A direction has no pole or seam, so it means the same on every side of the
    /// world and on the server and the client whatever their positions. The client carries it along the ground.
    /// </summary>
    public Vector3 View;

    public MoveButtons Buttons;

    public readonly bool Has(MoveButtons button) => (Buttons & button) != 0;

    /// <summary>The heading of the view at a point with this up (from local north, <see cref="Facing"/>).</summary>
    public readonly float YawAt(Vector3 up) => Facing.YawOf(View, up);

    /// <summary>How far the view looks above the horizontal at a point with this up.</summary>
    public readonly float PitchAt(Vector3 up) => Facing.PitchOf(View, up);

    /// <summary>An input looking along a heading and a pitch at a point with this up (bots, tests, a restored view).</summary>
    public static Vector3 Looking(float yaw, float pitch, Vector3 up) => Facing.View(yaw, pitch, up);
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

    /// <summary>An admin's god mode and noclip; only the server sets them.</summary>
    public MoveCheats Cheats;

    public readonly bool IsDead => Health == 0;

    public readonly bool Has(MoveCheats cheat) => (Cheats & cheat) != 0;

    public readonly bool Equals(CharacterState other) =>
        Position.Equals(other.Position) && Velocity.Equals(other.Velocity) && Stance == other.Stance &&
        Health == other.Health && Flags == other.Flags && Cheats == other.Cheats;

    public override readonly bool Equals(object? obj) => obj is CharacterState s && Equals(s);

    public override readonly int GetHashCode() => HashCode.Combine(Position, Velocity, Stance, Health, Flags, Cheats);

    public override readonly string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"at ({Position.X:0.000}, {Position.Y:0.000}, {Position.Z:0.000}) moving ({Velocity.X:0.00}, {Velocity.Y:0.00}, {Velocity.Z:0.00}) {Stance} {Health} hp {Flags}") +
        (Cheats != MoveCheats.None ? " " + Cheats : "");
}
