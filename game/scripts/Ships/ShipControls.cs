using System;
using Godot;
using Hollow.Simulation.Ships;

namespace HollowUniverse.Ships;

/// <summary>
/// The pilot's keyboard and mouse (G6, GROUND.md §6.7), read into a tick of <see cref="ShipInput"/>. The mouse is a
/// persistent steering cursor: right keeps turning right until moved back or explicitly centred with MMB.
/// W and S move the throttle lever, which stays where it is put (X pulls it to zero);
/// A and D slide sideways, Shift and Ctrl rise and sink, Q and E roll, the arrow keys are a keyboard stick, B boosts,
/// Z switches flight assist (the keys are <c>SeatKeys</c>', KNOWN_PROBLEMS.md finding 28; Space leaves the seat). With
/// flight assist on the lever sets the speed to hold and the cursor sets the rates to turn at.
/// </summary>
public sealed class ShipControls
{
    /// <summary>The throttle lever, −1 (full reverse) to 1 (full ahead).</summary>
    public float Throttle { get; set; }

    /// <summary>Flight assist switched off (Z).</summary>
    public bool AssistOff { get; set; }

    /// <summary>The steering cursor: x turns right, y raises the nose; each axis is −1 to 1.</summary>
    private readonly Hollow.Client.PilotSteering _steering = new();
    public Vector2 Stick => new(_steering.Cursor.X, _steering.Cursor.Y);
    public const float SteeringReach = 220;

    /// <summary>Stick per pixel of mouse travel (at interface scale 1).</summary>
    public float MouseSensitivity { get; set; } = 1 / SteeringReach;

    /// <summary>Explicitly centre steering; resting the mouse does not change it.</summary>
    public void CentreSteering() => _steering.Centre();

    /// <summary>How fast W and S move the lever (a full stroke in this many seconds).</summary>
    public float ThrottleStroke { get; set; } = 1.6f;

    /// <summary>Feeds the mouse's movement (pixels).</summary>
    public void Mouse(Vector2 relative)
    {
        _steering.Move(new System.Numerics.Vector2(relative.X, -relative.Y) * MouseSensitivity);
    }

    /// <summary>Handles a key press; true when it was a pilot's key (Z flight assist, X throttle to zero).</summary>
    public bool Key(InputEventKey key)
    {
        if (!key.Pressed || key.Echo)
        {
            return false;
        }
        if (key.PhysicalKeycode == Ground.SeatKeys.Assist)
        {
            AssistOff = !AssistOff;
            return true;
        }
        if (key.PhysicalKeycode == Ground.SeatKeys.ZeroThrottle)
        {
            Throttle = 0;
            return true;
        }
        return false;
    }

    /// <summary>Lets go of everything: lever to zero, stick to the middle, assist on (boarding, leaving).</summary>
    public void Reset()
    {
        Throttle = 0;
        _steering.Centre();
        AssistOff = false;
    }

    /// <summary>Reads this frame's controls and smoothly follows the persistent steering cursor.</summary>
    public ShipInput Read(double delta)
    {
        static float Held(Key key) => Input.IsPhysicalKeyPressed(key) ? 1 : 0;
        float lever = Held(Godot.Key.W) - Held(Godot.Key.S);
        Throttle = Hollow.Client.PilotSteering.AdjustThrottle(Throttle, lever * (float)delta * 2 / ThrottleStroke);
        Vector2 keys = new(Held(Godot.Key.Right) - Held(Godot.Key.Left), Held(Godot.Key.Up) - Held(Godot.Key.Down));
        System.Numerics.Vector2 smooth = _steering.Step((float)delta);
        Vector2 stick = new Vector2(smooth.X, smooth.Y) + keys;
        static float Shape(float v) => Math.Clamp(v, -1, 1);
        var input = new ShipInput
        {
            Pitch = Shape(stick.Y),
            Yaw = Shape(stick.X),
            Roll = Held(Godot.Key.E) - Held(Godot.Key.Q),
            Throttle = Throttle,
            Strafe = Held(Godot.Key.D) - Held(Godot.Key.A),
            Vertical = Held(Ground.SeatKeys.Rise) - Held(Ground.SeatKeys.Sink),
        };
        if (Input.IsPhysicalKeyPressed(Ground.SeatKeys.Boost))
        {
            input.Buttons |= ShipButtons.Boost;
        }
        if (AssistOff)
        {
            input.Buttons |= ShipButtons.AssistOff;
        }
        return input;
    }
}
