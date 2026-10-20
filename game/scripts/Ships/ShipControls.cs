using System;
using Godot;
using Hollow.Simulation.Ships;

namespace HollowUniverse.Ships;

/// <summary>
/// The pilot's keyboard and mouse (G6, GROUND.md §6.7), read into a tick of <see cref="ShipInput"/> by the ship's control
/// scheme (<see cref="Scheme"/>, ships.json <c>controls</c>; flight by default, <see cref="FlightControls"/>). This keeps
/// what the schemes share between frames: the throttle lever, which stays where it is put (X pulls it to zero), the
/// persistent steering cursor (the mouse in the cockpit; MMB centres it) and flight assist's switch (Z).
/// </summary>
public sealed class ShipControls
{
    /// <summary>How the keys fly the ship at the helm (set on taking the helm from its kind's data).</summary>
    public IShipControlScheme Scheme { get; set; } = ShipControlSchemes.For(null);

    /// <summary>The throttle lever, −1 (full reverse) to 1 (full ahead).</summary>
    public float Throttle { get; set; }

    /// <summary>Flight assist switched off (Z).</summary>
    public bool AssistOff { get; set; }

    /// <summary>The steering cursor: x turns right, y raises the nose; each axis is −1 to 1.</summary>
    internal Hollow.Client.PilotSteering Steering { get; } = new();
    public Vector2 Stick => new(Steering.Cursor.X, Steering.Cursor.Y);
    public const float SteeringReach = 220;

    /// <summary>Stick per pixel of mouse travel (at interface scale 1).</summary>
    public float MouseSensitivity { get; set; } = 1 / SteeringReach;

    /// <summary>Explicitly centre steering; resting the mouse does not change it.</summary>
    public void CentreSteering() => Steering.Centre();

    /// <summary>How fast W and S move the lever (a full stroke in this many seconds).</summary>
    public float ThrottleStroke { get; set; } = 1.6f;

    /// <summary>Feeds the mouse's movement (pixels).</summary>
    public void Mouse(Vector2 relative)
    {
        Steering.Move(new System.Numerics.Vector2(relative.X, -relative.Y) * MouseSensitivity);
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
        Steering.Centre();
        AssistOff = false;
    }

    /// <summary>Reads the helm's keys by the scheme; only the cockpit uses the persistent mouse cursor. Camera-only views release it.</summary>
    public ShipInput Read(double delta, bool mouseSteering = true) => Scheme.Read(this, delta, mouseSteering);
}
