using System;
using Hollow.Client.Audio;

namespace Hollow.Client.Vehicles;

/// <summary>
/// A ground vehicle's engine as the player hears and reads it (VEHICLES.md §7.1): its revs, gear, load, engine braking and
/// turbo, from what every client knows of it (its speed along its nose, its throttle, its engine running, its wheels on
/// the ground). The motor drives the wheels by power alone (GroundMotor); this gearbox is presentation: it keeps the revs
/// in a band, changes up as the speed grows and down as it falls, slips the clutch to pull away, free-revs in neutral and
/// with its wheels in the air, bounces off the limiter, and spools a turbo by revs and load. One per vehicle drawn
/// (ShipsView), read by its sounds, its HUD and its exhaust. Its numbers are sounds.json's <c>engines</c> entry.
/// </summary>
public sealed class VehicleRevs
{
    private readonly EngineSoundData _e;
    private float _shiftLeft, _cutLeft, _neutralFor, _gasWas, _releaseCooldown;

    public VehicleRevs(EngineSoundData engine)
    {
        _e = engine;
    }

    public EngineSoundData Engine => _e;

    /// <summary>The crank's revs a minute (0 stopped).</summary>
    public float Rpm { get; private set; }

    /// <summary>-1 reverse, 0 neutral, 1 … <see cref="Gears"/> forward.</summary>
    public int Gear { get; private set; }

    public int Gears => _e.Gears.Count;
    public float IdleRpm => _e.IdleRpm;
    public float Redline => _e.Redline;

    /// <summary>0–1: the throttle the engine works against (0 with the clutch out or the limiter cutting).</summary>
    public float Load { get; private set; }

    /// <summary>0–1: the engine dragged along off the throttle (engine braking).</summary>
    public float Overrun { get; private set; }

    /// <summary>0–1: the turbo's boost (0 without one).</summary>
    public float Boost { get; private set; }

    /// <summary>
    /// 0–1: how far into its overboost it is (VEHICLES.md §9.10: Shift held, the ship's Boosting flag), eased in and out: the
    /// turbo pushed to its most, louder and higher, black smoke and the engine rocking harder.
    /// </summary>
    public float Overboost { get; private set; }

    /// <summary>True while it runs.</summary>
    public bool Running { get; private set; }

    /// <summary>True while the limiter cuts.</summary>
    public bool Limiting => _cutLeft > 0;

    /// <summary>True on the step a gear changed (its sound, a puff at the exhaust).</summary>
    public bool Shifted { get; private set; }

    /// <summary>True on the step the throttle let go with the turbo spooled (its release).</summary>
    public bool Released { get; private set; }

    /// <summary>Seconds for the overboost to come in, and to go when let go.</summary>
    public const float OverboostIn = 0.25f, OverboostOut = 0.35f;

    /// <summary>
    /// One frame: <paramref name="speed"/> along the nose (m/s, negative backing up), <paramref name="throttle"/> as the
    /// motor has it (-1 … 1; against the motion it brakes), whether the engine runs and the wheels touch the ground, and
    /// whether it is overboosted (the ship's Boosting flag).
    /// </summary>
    public void Step(float speed, float throttle, bool engines, bool grounded, float dt, bool boosting = false)
    {
        Shifted = Released = false;
        dt = Math.Clamp(dt, 1e-4f, 0.1f);
        _releaseCooldown = Math.Max(0, _releaseCooldown - dt);
        float overboostWas = Overboost;
        Overboost = Approach(Overboost, engines && boosting ? 1 : 0, dt, boosting ? OverboostIn : OverboostOut);
        if (!engines)
        {
            // Stopped: the revs run down, the turbo with them.
            Running = false;
            Gear = 0;
            Load = Overrun = 0;
            _shiftLeft = _cutLeft = 0;
            Rpm = Approach(Rpm, 0, dt, 0.3f);
            Rpm = Rpm < 60 ? 0 : Rpm;
            Boost = Approach(Boost, 0, dt, 0.8f);
            _gasWas = 0;
            return;
        }
        if (!Running)
        {
            Running = true;
            Rpm = Math.Max(Rpm, _e.IdleRpm);
            Gear = 0;
        }
        float s = MathF.Abs(speed);
        bool braking = throttle < -0.05f && speed > 0.5f || throttle > 0.05f && speed < -0.5f;
        float gas = braking ? 0 : MathF.Abs(throttle);
        bool backing = !braking && (throttle < -0.05f || speed < -0.5f && throttle <= 0.05f);
        ChooseGear(s, gas, backing, grounded, dt);

        // Where the revs go: free in neutral, else the wheels' (the clutch slipping to pull away, the wheels spinning up in
        // the air), the limiter cutting at the top.
        float target;
        bool engaged = false;
        if (Gear == 0)
        {
            target = _e.IdleRpm + gas * (_e.Redline * 0.9f - _e.IdleRpm);
        }
        else
        {
            float wheels = WheelRpm(Gear, s);
            if (!grounded)
            {
                wheels = MathF.Max(wheels, _e.IdleRpm + gas * (_e.Redline - _e.IdleRpm));
            }
            float slip = Gear is 1 or -1 ? _e.IdleRpm + gas * (_e.Launch * _e.Redline - _e.IdleRpm) : _e.IdleRpm;
            engaged = grounded && wheels >= slip && _shiftLeft <= 0;
            target = MathF.Max(wheels, slip);
        }
        if (_cutLeft > 0)
        {
            _cutLeft -= dt;
        }
        else if (gas > 0.05f && Rpm >= _e.Limiter - 1)
        {
            _cutLeft = 0.07f;
        }
        target = Math.Min(target, _cutLeft > 0 ? _e.Redline * 0.95f : _e.Limiter);
        float tau = engaged ? 0.05f : target > Rpm ? _e.Response : _e.Response * 1.8f;
        Rpm = Math.Clamp(Approach(Rpm, target, dt, tau), _e.IdleRpm * 0.85f, _e.Limiter);

        // The load the engine works against; engine braking off the throttle in gear; the turbo by revs and load.
        float load = _shiftLeft > 0 || _cutLeft > 0 ? 0 : Gear == 0 ? gas * 0.6f : gas;
        Load = Approach(Load, load, dt, 0.06f);
        float drag = gas < 0.05f && Gear != 0
            ? Math.Clamp((Rpm - _e.IdleRpm * 1.25f) / MathF.Max(_e.Redline * 0.55f - _e.IdleRpm, 1), 0, 1) * (braking ? 1f : 0.8f)
            : 0;
        Overrun = Approach(Overrun, drag, dt, 0.12f);
        if (_e.TurboSpool > 0)
        {
            // Overboosted the wastegate is held shut: the turbo goes to its most from lower revs, and spools faster.
            float from = _e.Redline * (0.3f - 0.15f * Overboost);
            float want = Load * Math.Clamp((Rpm - from) / (_e.Redline * 0.45f), 0, 1);
            want = MathF.Max(want, Load * Overboost);
            float spool = _e.TurboSpool * (1 - 0.45f * Overboost);
            Boost = Approach(Boost, want, dt, want > Boost ? spool : _e.TurboSpool * 0.5f);
            // Its release: the throttle let go with the turbo spooled, or the overboost let go under load.
            bool lifted = _gasWas > 0.6f && gas < 0.2f || overboostWas > 0.6f && !boosting && gas > 0.2f;
            if (lifted && Boost > 0.35f && _releaseCooldown <= 0)
            {
                Released = true;
                _releaseCooldown = 1.5f;
            }
        }
        _gasWas = gas;
    }

    // Reverse while backing; neutral after a moment still and off the throttle; first to pull away; then up past the
    // change point on the throttle (or past the redline rolling), down under the low one, and down a gear for a hard pull.
    private void ChooseGear(float s, float gas, bool backing, bool grounded, float dt)
    {
        if (backing)
        {
            Gear = -1;
            _neutralFor = 0;
            return;
        }
        if (Gear <= 0 && (gas > 0.05f || s > 1f))
        {
            Gear = 1;
        }
        if (Gear != 0 && gas < 0.05f && s < 0.4f)
        {
            _neutralFor += dt;
            if (_neutralFor > 0.6f)
            {
                Gear = 0;
            }
        }
        else
        {
            _neutralFor = 0;
        }
        if (_shiftLeft > 0)
        {
            _shiftLeft -= dt;
            return;
        }
        if (Gear < 1 || !grounded)
        {
            return;
        }
        float revs = WheelRpm(Gear, s);
        if (Gear < Gears && (gas > 0.05f && revs > _e.Upshift * _e.Redline || revs > _e.Redline * 0.98f))
        {
            Change(Gear + 1);
        }
        else if (Gear > 1 && (revs < _e.Downshift * _e.Redline
                              || gas > 0.7f && revs < 0.5f * _e.Redline && WheelRpm(Gear - 1, s) < 0.8f * _e.Redline))
        {
            Change(Gear - 1);
        }
    }

    private void Change(int gear)
    {
        Gear = gear;
        _shiftLeft = _e.ShiftTime;
        Shifted = true;
    }

    /// <summary>The revs the wheels turn the engine at in a gear at a speed.</summary>
    public float WheelRpm(int gear, float speed)
    {
        float top = gear < 0 ? _e.Reverse : gear >= 1 && gear <= Gears ? _e.Gears[gear - 1] : 0;
        return top > 0 ? MathF.Abs(speed) / top * _e.Redline : 0;
    }

    private static float Approach(float value, float target, float dt, float tau) =>
        value + (target - value) * (1 - MathF.Exp(-dt / MathF.Max(tau, 1e-3f)));
}
