using System;

namespace Hollow.Simulation.Movement;

/// <summary>
/// Free look (Left Alt held, RustMVP's; GROUND.md §4.5): the mouse turns the head instead of the view, so the camera
/// looks around while the aim and the movement keep their direction. The view (<see cref="MoveInput.Yaw"/> and
/// <see cref="MoveInput.Pitch"/>) stays the aim of every shot, swing and placement, so a free look never disagrees with
/// them; letting go, or the cursor showing, turns the camera back to it at once. The turn rides the player's input to the
/// server and the character's state to everyone else, whose views turn the head and ease it back.
/// </summary>
public struct FreeLook
{
    /// <summary>How far the head turns from the view either way (radians): RustMVP's 120°.</summary>
    public const float MaxYaw = 120f * MathF.PI / 180f;

    /// <summary>The view's own pitch limit (radians): a free look stays within it too.</summary>
    public const float MaxViewPitch = 85f * MathF.PI / 180f;

    /// <summary>The most a pitch turn can be (radians): from one end of the view's pitch to the other.</summary>
    public const float MaxPitch = 2 * MaxViewPitch;

    /// <summary>Seconds without a player's input after which the server turns the head back (RustMVP's 0.8 s).</summary>
    public const double TimeoutSeconds = 0.8;

    /// <summary>The turn from the view (radians): yaw anticlockwise seen from above (to the left), pitch up.</summary>
    public float Yaw;

    public float Pitch;

    /// <summary>Alt is held while the game has the mouse.</summary>
    public bool Active { get; private set; }

    /// <summary>Holding or not: letting go turns the camera back to the view at once.</summary>
    public void Hold(bool held)
    {
        Active = held;
        if (!held)
        {
            Yaw = Pitch = 0;
        }
    }

    /// <summary>
    /// The mouse's turn while held (radians): the yaw within <see cref="MaxYaw"/> of the view, the pitch so that the view
    /// at <paramref name="viewPitch"/> and the turn together stay within <see cref="MaxViewPitch"/>.
    /// </summary>
    public void Turn(float yaw, float pitch, float viewPitch)
    {
        if (!Active || !float.IsFinite(yaw) || !float.IsFinite(pitch))
        {
            return;
        }
        Yaw = Math.Clamp(Yaw + yaw, -MaxYaw, MaxYaw);
        Pitch = Math.Clamp(viewPitch + Pitch + pitch, -MaxViewPitch, MaxViewPitch) - viewPitch;
    }
}
