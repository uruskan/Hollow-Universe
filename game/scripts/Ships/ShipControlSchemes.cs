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

    /// <summary>It flies: the HUD shows altitude, climb, flight assist and the orbit's lines (a boat's shows none).</summary>
    bool Flies { get; }

    /// <summary>The helm's keys for the HUD's corner, in the cockpit or the orbit camera.</summary>
    string Hints(bool cockpit);

    /// <summary>What the HUD's left gauge and status show: the throttle lever and flight assist, or a rotor's spin.</summary>
    HudGauge Gauge => HudGauge.Throttle;

    /// <summary>The tick's input; <paramref name="mouseSteering"/> false: a camera-only view, the cursor let go.</summary>
    ShipInput Read(ShipControls controls, double delta, bool mouseSteering);
}

/// <summary>The control schemes by key (one system per concern, found in a registry); flight when a ship names none.</summary>
public static class ShipControlSchemes
{
    private static readonly Dictionary<string, IShipControlScheme> Schemes = new(StringComparer.Ordinal)
    {
        [FlightControls.SchemeKey] = new FlightControls(),
        [BoatControls.SchemeKey] = new BoatControls(),
        [RotorControls.SchemeKey] = new RotorControls(),
        [CarControls.SchemeKey] = new CarControls(),
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

    public bool Flies => true;

    public string Hints(bool cockpit) => Ground.SeatKeys.FlightHints(cockpit);

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

/// <summary>
/// A boat's helm (docs/MOTORBOAT.md §5; ships.json <c>"controls": "boat"</c>): W ahead and S astern while held (letting go
/// idles the motor and the boat coasts), A and D turn the wheel, and the mouse looks round the boat rather than steering.
/// Nothing climbs, rolls or boosts; flight assist stays on.
/// </summary>
public sealed class BoatControls : IShipControlScheme
{
    public const string SchemeKey = "boat";

    public string Key => SchemeKey;

    public bool MouseSteers => false;

    public bool Flies => false;

    public string Hints(bool cockpit) =>
        "W ahead · S astern · A/D steer · " + (cockpit ? "Mouse look · V orbit" : "RMB drag orbit · Wheel zoom · V seat view") + " · SPACE leave";

    public ShipInput Read(ShipControls controls, double delta, bool mouseSteering)
    {
        controls.CentreSteering();
        static float Held(Godot.Key key) => Input.IsPhysicalKeyPressed(key) ? 1 : 0;
        controls.Throttle = Held(Godot.Key.W) - Held(Godot.Key.S);
        controls.AssistOff = false;
        return new ShipInput
        {
            Throttle = controls.Throttle,
            Yaw = Math.Clamp(Held(Godot.Key.D) - Held(Godot.Key.A) + Held(Godot.Key.Right) - Held(Godot.Key.Left), -1, 1),
        };
    }
}

/// <summary>
/// A rotor (the KA-FA 1500, docs/MINICOPTER.md §5.2), on Rust's minicopter's keys: W climbs and S (or Ctrl) sinks, neither
/// holding the height; A and D yaw; the mouse tilts it (forward noses it down, as a cyclic is pushed; left and right roll)
/// through a stick that stays where it is put, so a cruise needs no fight with the mouse. A small tilt left alone for half a
/// second springs back to level (Rust's self-levelling), and the middle button levels it at once. The arrow keys are a
/// keyboard stick, which is what tilts it in the orbit camera.
/// </summary>
public sealed class RotorControls : IShipControlScheme
{
    public const string SchemeKey = "rotor";

    /// <summary>A stick nearer the middle than this, left still for <see cref="SettleSeconds"/>, springs back to level.</summary>
    public const float Detent = 0.2f;

    public const double SettleSeconds = 0.5;

    private System.Numerics.Vector2 _last;
    private double _still;

    public string Key => SchemeKey;

    public bool MouseSteers => true;

    public bool Flies => true;

    public HudGauge Gauge => HudGauge.Rotor;

    public string Hints(bool cockpit) =>
        "W climb · S sink · A/D yaw · " + (cockpit ? "Mouse tilt · MMB level · ALT look · V orbit" : "Arrows tilt · RMB drag orbit · Wheel zoom · V cockpit") +
        " · SPACE leave";

    public ShipInput Read(ShipControls controls, double delta, bool mouseSteering)
    {
        if (!mouseSteering) controls.CentreSteering();
        static float Held(Godot.Key key) => Input.IsPhysicalKeyPressed(key) ? 1 : 0;
        System.Numerics.Vector2 cursor = controls.Steering.Cursor;
        _still = cursor == _last ? _still + delta : 0;
        if (_still >= SettleSeconds && cursor != System.Numerics.Vector2.Zero && cursor.Length() < Detent)
        {
            // Rust's self-levelling: a small tilt left alone eases back to the middle over a quarter of a second.
            float ease = (float)Math.Min(1, delta * 4);
            controls.Steering.Move(-cursor * ease);
            if (controls.Steering.Cursor.Length() < 0.01f) controls.CentreSteering();
        }
        _last = controls.Steering.Cursor;
        System.Numerics.Vector2 smooth = controls.Steering.Step((float)delta);
        Vector2 keys = new(Held(Godot.Key.Right) - Held(Godot.Key.Left), Held(Godot.Key.Up) - Held(Godot.Key.Down));
        Vector2 stick = new Vector2(smooth.X, smooth.Y) + keys;
        controls.Throttle = 0;
        controls.AssistOff = false;
        return new ShipInput
        {
            // The stick forward (the mouse pushed away, or Up) noses it down: + pitch is nose up.
            Pitch = -Math.Clamp(stick.Y, -1, 1),
            Roll = Math.Clamp(stick.X, -1, 1),
            Yaw = Held(Godot.Key.D) - Held(Godot.Key.A),
            Vertical = Held(Godot.Key.W) - Math.Max(Held(Godot.Key.S), Held(Ground.SeatKeys.Sink)),
        };
    }
}

/// <summary>
/// Driving (VEHICLES.md §6.5), the TOGG's: W drives on, S brakes and then reverses (the ground motor knows which), A and D
/// steer, the wheel turning toward full lock at a rate and back to centre faster when let go (the ramp is in the input, so
/// the server and the prediction steer alike); Left Ctrl is the handbrake; Left Shift held overboosts an engine that can
/// (VEHICLES.md §9.10); the mouse looks round; Space gets out (<c>SeatKeys</c>, the owner's R17).
/// </summary>
public sealed class CarControls : IShipControlScheme
{
    public const string SchemeKey = "car";

    /// <summary>How fast the wheel turns toward a held lock, and back to centre when let go (full locks a second).</summary>
    private const float SteerRate = 2.5f, CentreRate = 4f;

    private float _steer;

    public string Key => SchemeKey;

    public bool MouseSteers => false;

    public bool Flies => false;

    public string Hints(bool cockpit) =>
        "W drive · S brake, reverse · A/D steer · SHIFT boost · CTRL handbrake · " + (cockpit ? "Mouse look · V orbit" : "RMB drag orbit · Wheel zoom · V seat view") +
        " · SPACE get out";

    public ShipInput Read(ShipControls controls, double delta, bool mouseSteering)
    {
        controls.CentreSteering();
        static float Held(Godot.Key key) => Input.IsPhysicalKeyPressed(key) ? 1 : 0;
        controls.Throttle = Held(Godot.Key.W) - Held(Godot.Key.S);
        controls.AssistOff = false;
        float target = Math.Clamp(Held(Godot.Key.D) - Held(Godot.Key.A) + Held(Godot.Key.Right) - Held(Godot.Key.Left), -1, 1);
        float rate = target == 0 || MathF.Sign(target) != MathF.Sign(_steer) ? CentreRate : SteerRate;
        float step = rate * (float)delta;
        _steer = MathF.Abs(target - _steer) <= step ? target : _steer + MathF.Sign(target - _steer) * step;
        return new ShipInput
        {
            Throttle = controls.Throttle,
            Yaw = _steer,
            Vertical = Held(Godot.Key.Ctrl),
            Buttons = Input.IsPhysicalKeyPressed(Godot.Key.Shift) ? ShipButtons.Boost : ShipButtons.None,
        };
    }
}
