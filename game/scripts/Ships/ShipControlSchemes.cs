using System;
using System.Collections.Generic;
using Godot;
using Hollow.Simulation.Ships;

namespace HollowUniverse.Ships;

/// <summary>
/// How the helm's keys and mouse fly one kind of ship (ships.json <c>controls</c>, <see cref="ShipDef.Controls"/>): each
/// frame it reads the keyboard into a tick of <see cref="ShipInput"/>, keeping what it needs (the throttle lever, the
/// steering cursor) in <see cref="ShipControls"/>.
/// </summary>
public interface IShipControlScheme
{
    /// <summary>Its key in ships.json.</summary>
    string Key { get; }

    /// <summary>In the cockpit the mouse steers (a persistent cursor) rather than looking round the seat.</summary>
    bool MouseSteers { get; }

    /// <summary>The tick's input; <paramref name="mouseSteering"/> false: a camera-only view, the cursor let go.</summary>
    ShipInput Read(ShipControls controls, double delta, bool mouseSteering);
}

/// <summary>The control schemes by key (one system per concern, found in a registry); flight when a ship names none.</summary>
public static class ShipControlSchemes
{
    private static readonly Dictionary<string, IShipControlScheme> Schemes = new(StringComparer.Ordinal)
    {
        [FlightControls.SchemeKey] = new FlightControls(),
    };

    /// <summary>The scheme a ship's data names, or flight (with a warning for a key nobody knows).</summary>
    public static IShipControlScheme For(string? key)
    {
        if (string.IsNullOrEmpty(key) || key == ShipDef.DefaultControls)
        {
            return Schemes[FlightControls.SchemeKey];
        }
        if (Schemes.TryGetValue(key, out IShipControlScheme? scheme))
        {
            return scheme;
        }
        GD.PushWarning($"ships.json controls \"{key}\": no such control scheme; flying it as \"{FlightControls.SchemeKey}\"");
        return Schemes[FlightControls.SchemeKey];
    }
}

/// <summary>
/// Flight (G6, GROUND.md §6.7), the Tin Finch's and the Iron Wake's: the mouse is a persistent steering cursor (right keeps
/// turning right until moved back or centred with the middle button); W and S move the throttle lever, which stays where
/// it is put (X pulls it to zero); A and D slide sideways, Shift and Ctrl rise and sink, Q and E roll, the arrow keys are
/// a keyboard stick, B boosts, Z switches flight assist (the keys are <c>SeatKeys</c>', KNOWN_PROBLEMS.md finding 28).
/// </summary>
public sealed class FlightControls : IShipControlScheme
{
    public const string SchemeKey = ShipDef.DefaultControls;

    public string Key => SchemeKey;

    public bool MouseSteers => true;

    public ShipInput Read(ShipControls controls, double delta, bool mouseSteering)
    {
        if (!mouseSteering) controls.CentreSteering();
        static float Held(Godot.Key key) => Input.IsPhysicalKeyPressed(key) ? 1 : 0;
        float lever = Held(Godot.Key.W) - Held(Godot.Key.S);
        controls.Throttle = Hollow.Client.PilotSteering.AdjustThrottle(controls.Throttle, lever * (float)delta * 2 / controls.ThrottleStroke);
        Vector2 keys = new(Held(Godot.Key.Right) - Held(Godot.Key.Left), Held(Godot.Key.Up) - Held(Godot.Key.Down));
        System.Numerics.Vector2 smooth = controls.Steering.Step((float)delta);
        Vector2 stick = new Vector2(smooth.X, smooth.Y) + keys;
        static float Shape(float v) => Math.Clamp(v, -1, 1);
        var input = new ShipInput
        {
            Pitch = Shape(stick.Y),
            Yaw = Shape(stick.X),
            Roll = Held(Godot.Key.E) - Held(Godot.Key.Q),
            Throttle = controls.Throttle,
            Strafe = Held(Godot.Key.D) - Held(Godot.Key.A),
            Vertical = Held(Ground.SeatKeys.Rise) - Held(Ground.SeatKeys.Sink),
        };
        if (Input.IsPhysicalKeyPressed(Ground.SeatKeys.Boost))
        {
            input.Buttons |= ShipButtons.Boost;
        }
        if (controls.AssistOff)
        {
            input.Buttons |= ShipButtons.AssistOff;
        }
        return input;
    }
}
