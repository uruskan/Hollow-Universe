using System;
using Godot;
using Hollow.Simulation.Ships;

namespace HollowUniverse.Ships;

/// <summary>
/// The pilot's keyboard and mouse (G6, GROUND.md §6.7), read into a tick of <see cref="ShipInput"/>. The mouse is a
/// virtual stick: moving it pushes the stick (up the nose rises, right it turns right), and the stick springs back to the
/// middle when the mouse rests. W and S move the throttle lever, which stays where it is put (X pulls it to zero);
/// A and D slide sideways, Space and C (or Ctrl) rise and sink, Q and E roll, the arrow keys are a keyboard stick,
/// Shift (or Tab) boosts, Z switches flight assist. With flight assist on the lever sets the speed to hold, the stick the
/// rates to turn at, and letting go of everything hovers.
/// </summary>
public sealed class ShipControls
{
    /// <summary>The throttle lever, −1 (full reverse) to 1 (full ahead).</summary>
    public float Throttle { get; set; }

    /// <summary>Flight assist switched off (Z).</summary>
    public bool AssistOff { get; set; }

    /// <summary>The virtual stick: x turns right, y raises the nose; within the unit circle.</summary>
    public Vector2 Stick { get; private set; }

    /// <summary>Stick per pixel of mouse travel (at interface scale 1).</summary>
    public float MouseSensitivity { get; set; } = 0.006f;

    /// <summary>Seconds for a resting stick to come halfway back to the middle.</summary>
    public float StickReturn { get; set; } = 0.16f;

    /// <summary>How fast W and S move the lever (a full stroke in this many seconds).</summary>
    public float ThrottleStroke { get; set; } = 1.6f;

    /// <summary>Feeds the mouse's movement (pixels).</summary>
    public void Mouse(Vector2 relative)
    {
        Vector2 stick = Stick + new Vector2(relative.X, -relative.Y) * MouseSensitivity;
        Stick = stick.Length() > 1 ? stick.Normalized() : stick;
    }

    /// <summary>Handles a key press; true when it was a pilot's key (Z flight assist, X throttle to zero).</summary>
    public bool Key(InputEventKey key)
    {
        if (!key.Pressed || key.Echo)
        {
            return false;
        }
        switch (key.PhysicalKeycode)
        {
            case Godot.Key.Z:
                AssistOff = !AssistOff;
                return true;
            case Godot.Key.X:
                Throttle = 0;
                return true;
        }
        return false;
    }

    /// <summary>Lets go of everything: lever to zero, stick to the middle, assist on (boarding, leaving).</summary>
    public void Reset()
    {
        Throttle = 0;
        Stick = Vector2.Zero;
        AssistOff = false;
    }

    /// <summary>Reads this frame's controls (the keys held now) and lets the stick spring back by <paramref name="delta"/>.</summary>
    public ShipInput Read(double delta)
    {
        static float Held(Key key) => Input.IsPhysicalKeyPressed(key) ? 1 : 0;
        float lever = Held(Godot.Key.W) - Held(Godot.Key.S);
        Throttle = Math.Clamp(Throttle + lever * (float)delta * 2 / ThrottleStroke, -1, 1);
        // Snap to zero when the lever crosses it, so stopping is easy.
        if (lever != 0 && MathF.Abs(Throttle) < 0.02f)
        {
            Throttle = 0;
        }
        Vector2 keys = new(Held(Godot.Key.Right) - Held(Godot.Key.Left), Held(Godot.Key.Up) - Held(Godot.Key.Down));
        Vector2 stick = Stick + keys;
        static float Shape(float v) => MathF.Abs(v) < 0.04f ? 0 : Math.Clamp(v, -1, 1);
        var input = new ShipInput
        {
            Pitch = Shape(stick.Y),
            Yaw = Shape(stick.X),
            Roll = Held(Godot.Key.E) - Held(Godot.Key.Q),
            Throttle = Throttle,
            Strafe = Held(Godot.Key.D) - Held(Godot.Key.A),
            Vertical = Held(Godot.Key.Space) - Math.Max(Held(Godot.Key.C), Held(Godot.Key.Ctrl)),
        };
        if (Input.IsPhysicalKeyPressed(Godot.Key.Shift) || Input.IsPhysicalKeyPressed(Godot.Key.Tab))
        {
            input.Buttons |= ShipButtons.Boost;
        }
        if (AssistOff)
        {
            input.Buttons |= ShipButtons.AssistOff;
        }
        Stick *= MathF.Exp(-(float)delta * MathF.Log(2) / StickReturn);
        return input;
    }
}
