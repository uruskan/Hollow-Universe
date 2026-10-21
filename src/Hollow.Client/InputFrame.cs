using System.Numerics;
using Hollow.Simulation.Movement;

namespace Hollow.Client;

/// <summary>One frame of the player's controls, as the game (or a bot) reads them.</summary>
public struct InputFrame
{
    /// <summary>−1 … 1: back to forward, left to right.</summary>
    public float Forward;
    public float Right;

    /// <summary>
    /// Where the player looks: a unit direction in <see cref="ViewFrame"/>, which the camera carries along the ground
    /// (docs/WHOLE_WORLD.md §5, <see cref="Facing.Carry"/>). When it is zero, <see cref="Yaw"/> and
    /// <see cref="Pitch"/> are read instead.
    /// </summary>
    public Vector3 View;

    /// <summary>The frame the view is in (IRON_WAKE.md §4.11): a ship's net ID, 0 the zone's; null (bots) the one the character is in.</summary>
    public uint? ViewFrame;

    /// <summary>
    /// The view as a heading from local north and a pitch, in radians, where the player stands (bots and tests), read
    /// when <see cref="View"/> is zero.
    /// </summary>
    public float Yaw;
    public float Pitch;

    /// <summary>
    /// Free look (Alt, <see cref="FreeLook"/>): the camera's and the head's turn from the view, in radians. Others see the
    /// head turn; the view above stays the aim and the movement's.
    /// </summary>
    public float LookYaw;
    public float LookPitch;

    public bool Jump;
    public bool Crouch;
    public bool Sprint;

    /// <summary>Requested admin noclip speed, quantized before prediction; grants no movement permission.</summary>
    public float NoclipSpeed;

    /// <summary>Jump went down this frame; kept until the next tick uses it, so a quick tap is never lost.</summary>
    public bool JumpPressed;

    /// <summary>Attack (left mouse) and aim or draw (right mouse) are held (G2).</summary>
    public bool Primary;
    public bool Secondary;

    /// <summary>Attack went down this frame (kept until a tick uses it, as jump is).</summary>
    public bool PrimaryPressed;

    /// <summary>A pilot's controls (G6): sent with every tick's input while the player flies a ship.</summary>
    public Simulation.Ships.ShipInput Ship;

    /// <summary>
    /// A gunner's trigger and zoom (IW3, IRON_WAKE.md §6.8), sent with every tick's input while the player sits at a
    /// station; its ammo choice and cycle tap are the stations' module's, its asked direction the view.
    /// </summary>
    public Simulation.Ships.StationInput Station;
}
