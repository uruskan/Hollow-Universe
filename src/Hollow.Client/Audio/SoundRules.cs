using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Movement;
using Hollow.Surface;

namespace Hollow.Client.Audio;

/// <summary>Distance layering of loud sounds (GROUND.md §4.9): which layers play at a distance, and when they arrive.</summary>
public static class DistanceLayers
{
    /// <summary>Metres per second in the air, at about 20 °C.</summary>
    public const double SpeedOfSound = 343.0;

    /// <summary>
    /// How much of a layer plays at <paramref name="distance"/> (0–1): fully inside [from, to], crossfading over
    /// <paramref name="fade"/> metres centred on each edge, with equal power so neighbouring layers keep the level.
    /// A layer starting at 0 has no inner fade.
    /// </summary>
    public static double Weight(double distance, double from, double to, double fade)
    {
        double half = Math.Max(0.001, fade) / 2;
        double rise = from <= 0 ? 1 : Ramp((distance - (from - half)) / (2 * half));
        double fall = double.IsPositiveInfinity(to) ? 1 : Ramp(((to + half) - distance) / (2 * half));
        return rise * fall;
    }

    private static double Ramp(double x) => x <= 0 ? 0 : x >= 1 ? 1 : Math.Sin(x * Math.PI / 2);

    /// <summary>Seconds until a sound made <paramref name="distance"/> metres away is heard.</summary>
    public static double Delay(double distance) => Math.Max(0, distance) / SpeedOfSound;

    /// <summary>A linear weight as decibels (−80 for nothing).</summary>
    public static double Db(double weight) => weight <= 1e-4 ? -80 : 20 * Math.Log10(weight);
}

/// <summary>What a foot lands on (the footstep sets of sounds.json).</summary>
public enum FootSurface : byte
{
    Grass,
    Dirt,
    Sand,
    Rock,
    Snow,
    Wood,
    Metal,
    Water,
}

/// <summary>How a step is taken.</summary>
public enum FootGait : byte
{
    Walk,
    Run,
    Crouch,
}

/// <summary>A character's feet this frame, as the footstep rules need them.</summary>
public struct FootInput
{
    /// <summary>Horizontal speed, m/s.</summary>
    public double Speed;

    /// <summary>Vertical speed, m/s (negative falling).</summary>
    public double VerticalSpeed;

    public bool Grounded;
    public bool Crouching;
    public bool Swimming;
    public bool Sprinting;

    /// <summary>Seats, noclip and absent/dead characters stop and clear the cadence, including pending landings.</summary>
    public bool Suppressed;

    /// <summary>The ground under the feet (the shared physical surface/response tables).</summary>
    public FootSurface Surface;

    /// <summary>Worn foot-contact profile; null uses the audio registry's default (bare feet).</summary>
    public string? FootwearProfile;

    /// <summary>Water above the feet, metres; shallow water (≥ 8 cm, not swimming) splashes.</summary>
    public double WaterDepth;
}

/// <summary>A footstep sound to play: its event key and extra volume in dB.</summary>
public readonly record struct FootEvent(string Key, double VolumeDb);

/// <summary>
/// The footstep rules (GROUND.md §4.9): what each ground material and floor sounds like, and how often feet fall.
/// Steps follow the distance walked (a stride that grows with speed), so they keep pace with the animation at any
/// frame rate; a landing after a fall plays louder the harder it was.
/// </summary>
public static class Footsteps
{
    public const string SwimKey = "swim.stroke";

    /// <summary>Relative movement in the character's frame: a ship's +Y, or radial planet up.</summary>
    public static FootInput Input(in CharacterState state, in ZoneFrame frame, bool sprinting, FootSurface surface, double waterDepth)
    {
        Vector3 up = state.Frame == 0 ? frame.UpAt(state.Position) : Vector3.UnitY;
        double vertical = Vector3.Dot(state.Velocity, up);
        double speed = (state.Velocity - up * (float)vertical).Length();
        return new FootInput
        {
            Speed = speed, VerticalSpeed = vertical,
            Grounded = (state.Flags & MoveFlags.OnGround) != 0,
            Swimming = (state.Flags & MoveFlags.Swimming) != 0,
            Crouching = state.Stance is Stance.Crouching or Stance.Crawling,
            Sprinting = sprinting && speed > 3,
            Suppressed = state.Stance == Stance.Seated || (state.Flags & MoveFlags.Dead) != 0 || (state.Cheats & MoveCheats.Noclip) != 0,
            Surface = surface, WaterDepth = state.Frame == 0 ? waterDepth : 0,
        };
    }

    /// <summary>Metres between footfalls: a longer stride the faster one goes, shorter crouched.</summary>
    public static double Stride(double speed, bool crouching) =>
        crouching ? Math.Clamp(0.45 + 0.2 * speed, 0.45, 0.9) : Math.Clamp(0.55 + 0.22 * speed, 0.6, 1.75);

    public static FootGait Gait(double speed, bool crouching, bool sprinting) =>
        crouching ? FootGait.Crouch : sprinting || speed > 3.6 ? FootGait.Run : FootGait.Walk;

    public static string StepKey(FootGait gait, FootSurface surface) =>
        "footstep." + gait switch { FootGait.Run => "run", FootGait.Crouch => "crouch", _ => "walk" } + "." + Name(surface);

    public static string LandKey(FootSurface surface) => "footstep.land." + Name(surface);

    public static string Name(FootSurface surface) => surface switch
    {
        FootSurface.Grass => "grass",
        FootSurface.Dirt => "dirt",
        FootSurface.Sand => "sand",
        FootSurface.Rock => "rock",
        FootSurface.Snow => "snow",
        FootSurface.Wood => "wood",
        FootSurface.Metal => "metal",
        _ => "water",
    };

    /// <summary>Every footstep event key the rules can ask for.</summary>
    public static IEnumerable<string> AllKeys()
    {
        foreach (FootSurface s in Enum.GetValues<FootSurface>())
        {
            yield return StepKey(FootGait.Walk, s);
            yield return StepKey(FootGait.Run, s);
            yield return StepKey(FootGait.Crouch, s);
            yield return LandKey(s);
        }
        yield return SwimKey;
    }

    /// <summary>Extra dB for a landing at <paramref name="impactSpeed"/> m/s: −8 at 2.2 m/s up to +3 at 10 m/s.</summary>
    public static double LandingVolume(double impactSpeed) => -8 + 11 * Math.Clamp((impactSpeed - 2.2) / 7.8, 0, 1);
}

/// <summary>Turns a character's movement into footstep events, frame by frame (<see cref="Footsteps"/>).</summary>
public sealed class FootstepCadence
{
    private double _travelled;
    private double _airborne;
    private double _fallSpeed;
    private double _swim;
    private bool _wasGrounded = true;

    /// <summary>The next foot falls after this fraction of a stride when one starts moving.</summary>
    public const double FirstStep = 0.55;

    public void Reset()
    {
        _travelled = _airborne = _fallSpeed = _swim = 0;
        _wasGrounded = true;
    }

    /// <summary>Advances by <paramref name="delta"/> seconds; returns the sound to play now, if any.</summary>
    public FootEvent? Update(double delta, in FootInput input)
    {
        if (input.Suppressed)
        {
            Reset();
            return null;
        }
        FootEvent? result = null;
        FootSurface surface = input.WaterDepth >= 0.08 && !input.Swimming ? FootSurface.Water : input.Surface;
        if (input.Swimming)
        {
            _airborne = 0;
            _fallSpeed = 0;
            _wasGrounded = false;
            _travelled = 0;
            if (input.Speed > 0.3)
            {
                _swim += delta;
                double every = input.Sprinting ? 0.85 : 1.15;
                if (_swim >= every)
                {
                    _swim -= every;
                    result = new FootEvent(Footsteps.SwimKey, input.Sprinting ? 2 : 0);
                }
            }
            else
            {
                _swim = 0.8;
            }
            return result;
        }
        if (!input.Grounded)
        {
            _airborne += delta;
            _fallSpeed = Math.Max(_fallSpeed, -input.VerticalSpeed);
            _wasGrounded = false;
            return null;
        }
        _swim = 0;
        if (!_wasGrounded)
        {
            // Touching down: a landing after a real fall; either way the next step comes a stride later.
            if (_airborne >= 0.12 && _fallSpeed >= 2.2)
            {
                result = new FootEvent(Footsteps.LandKey(surface), Footsteps.LandingVolume(_fallSpeed));
            }
            _travelled = 0;
            _airborne = 0;
            _fallSpeed = 0;
            _wasGrounded = true;
            if (result != null)
            {
                return result;
            }
        }
        if (input.Speed < 0.25)
        {
            _travelled = Footsteps.Stride(input.Speed, input.Crouching) * FirstStep;
            return null;
        }
        _travelled += input.Speed * delta;
        double stride = Footsteps.Stride(input.Speed, input.Crouching);
        if (_travelled >= stride)
        {
            _travelled -= stride;
            if (_travelled > stride)
            {
                _travelled = 0;
            }
            FootGait gait = Footsteps.Gait(input.Speed, input.Crouching, input.Sprinting);
            result = new FootEvent(Footsteps.StepKey(gait, surface), 0);
        }
        return result;
    }
}
