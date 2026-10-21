using System;

namespace Hollow.Client.Vehicles;

/// <summary>
/// A heavy engine rocking on its mounts (VEHICLES.md §9.10), from its revs (<see cref="VehicleRevs"/>): presentation only.
/// The engine leans about its crank against its torque, on a spring and damper, so a stab of the throttle lurches it over
/// and back, a gear change (the load let go and taken up again) kicks it, and steady load holds it leaned; it shakes at idle
/// (a diesel's lope, roughest at its lowest revs) and harder overboosted. Standing still, a free rev twists the chassis a
/// little against the flywheel's pull. The view (ShipView.Rock.cs) turns <see cref="Lean"/> about the engine's crank and
/// <see cref="Twist"/> about the chassis's length.
/// </summary>
public sealed class EngineRock
{
    /// <summary>The most the engine leans under full load (radians); overboosted, this many times more.</summary>
    public const float MostLean = 0.045f, OverboostLean = 1.7f;

    /// <summary>The mounts' spring and damper (rad/s² per rad, per rad/s): about 2.5 Hz, so a stab overshoots and settles.</summary>
    public const float Stiffness = 240, Damping = 6;

    /// <summary>A gear change's kick (rad/s, against the lean): the load let go, the engine swinging back.</summary>
    public const float ShiftKick = 0.55f;

    /// <summary>The idle shake (radians) at idle revs, gone by the redline; overboosted, this many times more.</summary>
    public const float IdleShake = 0.006f, OverboostShake = 2.2f;

    /// <summary>The chassis's twist: radians a 1,000 rpm-a-second rise in revs gives standing still, and its spring and damper.</summary>
    public const float TwistPerRise = 0.0035f, MostTwist = 0.014f, TwistStiffness = 90, TwistDamping = 7;

    /// <summary>Below this speed (m/s) it stands still enough for a free rev to twist the chassis.</summary>
    public const float StillBelow = 1.5f;

    private float _spin, _twistSpin, _rpmWas, _time;

    /// <summary>Radians the engine leans about its crank (+ against the crank's turning, as its torque pushes it).</summary>
    public float Lean { get; private set; }

    /// <summary>The idle's shake this frame (radians, about the crank as the lean).</summary>
    public float Shake { get; private set; }

    /// <summary>Radians the chassis twists about its length.</summary>
    public float Twist { get; private set; }

    /// <summary>One frame of the revs (stepped first) at a speed (m/s).</summary>
    public void Step(VehicleRevs revs, float speed, float dt)
    {
        dt = Math.Clamp(dt, 1e-4f, 0.05f);
        _time += dt;
        if (!revs.Running && revs.Rpm <= 0)
        {
            Lean = Approach(Lean, 0, dt, 0.2f);
            Twist = Approach(Twist, 0, dt, 0.3f);
            _spin = _twistSpin = Shake = 0;
            _rpmWas = 0;
            return;
        }
        float over = revs.Overboost;
        float target = MostLean * revs.Load * (1 + (OverboostLean - 1) * over);
        if (revs.Shifted)
        {
            _spin -= ShiftKick * (0.4f + revs.Load) * (1 + over);
        }
        // The idle's lope: two beats off the revs (the six's half order and its firing), roughest low and overboosted.
        float low = 1 - Math.Clamp((revs.Rpm - revs.IdleRpm) / MathF.Max(revs.Redline - revs.IdleRpm, 1), 0, 1);
        float amplitude = IdleShake * (0.25f + 0.75f * low) * (1 + (OverboostShake - 1) * over);
        float beat = revs.Rpm / 60f;
        float shake = amplitude * (MathF.Sin(_time * MathF.Tau * beat * 0.5f) * 0.7f + MathF.Sin(_time * MathF.Tau * beat * 1.5f + 1.3f) * 0.3f);
        // The spring stepped in small pieces (steady at any frame rate).
        int pieces = (int)MathF.Ceiling(dt / 0.004f);
        float h = dt / pieces;
        for (int i = 0; i < pieces; i++)
        {
            _spin += (Stiffness * (target - Lean) - Damping * _spin) * h;
            Lean += _spin * h;
        }
        Lean = Math.Clamp(Lean, -MostLean * 3, MostLean * 3);

        // The chassis against the flywheel: a free rev standing still twists it, the revs falling twist it back.
        float rise = _rpmWas > 0 ? (revs.Rpm - _rpmWas) / dt : 0;
        _rpmWas = revs.Rpm;
        float still = 1 - Math.Clamp(MathF.Abs(speed) / StillBelow, 0, 1);
        float twistTarget = Math.Clamp(TwistPerRise * rise / 1000f, -MostTwist, MostTwist) * still + Lean * 0.08f;
        for (int i = 0; i < pieces; i++)
        {
            _twistSpin += (TwistStiffness * (twistTarget - Twist) - TwistDamping * _twistSpin) * h;
            Twist += _twistSpin * h;
        }
        Twist = Math.Clamp(Twist, -MostTwist * 2, MostTwist * 2);
        Shake = shake;
    }

    private static float Approach(float from, float to, float dt, float seconds) =>
        to + (from - to) * MathF.Exp(-dt / Math.Max(seconds, 1e-3f));
}
