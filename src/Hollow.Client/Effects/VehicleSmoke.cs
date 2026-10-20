using System;

namespace Hollow.Client.Effects;

/// <summary>
/// What a ground vehicle's diesel shows at its stacks (VEHICLES.md §9.9), from plain inputs every client knows: whether it
/// runs, its throttle, the load the engine works against and a gear change. A thin haze at idle; black soot that comes in
/// quickly with the load and goes slowly; a puff on starting, at a gear change and on a stab of the throttle. The view
/// (ShipsView.Vehicles.cs) turns these into fx.json's <c>exhaust.*</c> effects. Until the shared revs model gives a load and
/// its gear changes, the load is the throttle and there are no shifts.
/// </summary>
public sealed class VehicleSmoke
{
    /// <summary>Seconds for the soot to come in with the load, and to clear after it.</summary>
    public const float SootAttack = 0.12f, SootRelease = 0.7f;

    /// <summary>A rise of the throttle this far over its recent level is a stab (a puff).</summary>
    public const float StabRise = 0.45f;

    /// <summary>The throttle's recent level follows it over this many seconds.</summary>
    public const float ThrottleLag = 0.3f;

    /// <summary>No second stab's puff sooner than this.</summary>
    public const float StabCooldown = 0.8f;

    private bool _running;
    private float _lagged, _sinceStab = StabCooldown;

    /// <summary>0–1: the idle haze (thinner as the soot takes over).</summary>
    public float Haze { get; private set; }

    /// <summary>0–1: the black soot under load.</summary>
    public float Soot { get; private set; }

    /// <summary>How hard a puff this step is (0: none; 1.4 a start, about 1 a stab, less a gear change).</summary>
    public float Puff { get; private set; }

    /// <summary>One frame: <paramref name="throttle"/> −1 to 1 (backing up negative), <paramref name="load"/> 0–1.</summary>
    public void Update(bool running, float throttle, float load, bool shifted, float dt)
    {
        Puff = 0;
        dt = Math.Max(dt, 0);
        if (!running)
        {
            _running = false;
            _lagged = 0;
            Haze = 0;
            Soot = Approach(Soot, 0, dt, SootRelease);
            return;
        }
        float gas = Math.Clamp(Math.Abs(throttle), 0, 1);
        load = Math.Clamp(load, 0, 1);
        if (!_running)
        {
            Puff = 1.4f;
        }
        _running = true;
        _sinceStab += dt;
        if (gas - _lagged > StabRise && _sinceStab >= StabCooldown)
        {
            Puff = Math.Max(Puff, 0.7f + 0.5f * gas);
            _sinceStab = 0;
        }
        if (shifted)
        {
            Puff = Math.Max(Puff, 0.4f + 0.5f * load);
        }
        _lagged = Approach(_lagged, gas, dt, ThrottleLag);
        Soot = Approach(Soot, load, dt, load > Soot ? SootAttack : SootRelease);
        Haze = 1f - 0.7f * Soot;
    }

    // Toward a target over a time constant (exponential, steady whatever the frame rate).
    private static float Approach(float from, float to, float dt, float seconds) =>
        to + (from - to) * MathF.Exp(-dt / Math.Max(seconds, 1e-3f));
}
