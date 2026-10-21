using System;

namespace Hollow.Client.Effects;

/// <summary>
/// What a ground vehicle's diesel shows at its stacks (VEHICLES.md §9.9), from plain inputs every client knows: whether it
/// runs, its throttle, the load the engine works against and a gear change. A thin haze at idle; black soot that comes in
/// quickly with the load and goes slowly; a puff on starting, at a gear change and on a stab of the throttle. The view
/// (ShipsView.Vehicles.cs) turns these into fx.json's <c>exhaust.*</c> effects, the load and the gear changes its revs'
/// (<c>Hollow.Client.Vehicles.VehicleRevs</c>).
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
    private float _lagged, _sinceStab = StabCooldown, _sinceBillow;

    /// <summary>0–1: the idle haze (thinner as the soot takes over).</summary>
    public float Haze { get; private set; }

    /// <summary>0–1: the black soot under load.</summary>
    public float Soot { get; private set; }

    /// <summary>How hard a puff this step is (0: none; 1.4 a start, about 1 a stab, less a gear change).</summary>
    public float Puff { get; private set; }

    /// <summary>
    /// 0–1: the heavy black smoke of an overboost (VEHICLES.md §9.10), by the overboost and the load: in as the soot comes,
    /// gone more slowly than it.
    /// </summary>
    public float Black { get; private set; }

    /// <summary>Seconds for the overboost's black smoke to clear.</summary>
    public const float BlackRelease = 1.1f;

    /// <summary>
    /// How big a billow this step is (0: none): a locomotive's heavy round puff of black smoke thrown up in the engine's
    /// rhythm (VEHICLES.md §9.10), about one a second under a hard pull and several a second overboosted, quicker with the
    /// revs; the view throws fx.json's <c>exhaust.billow</c> with a rumble felt near it.
    /// </summary>
    public float Billow { get; private set; }

    /// <summary>A billow every this many seconds at <see cref="BillowRpm"/>, overboosted (sooner at higher revs); a hard pull's
    /// come <see cref="PullBillowSlower"/> times less often.</summary>
    public const float BillowEvery = 0.42f, BillowRpm = 1500f, PullBillowSlower = 2.2f;

    /// <summary>
    /// One frame: <paramref name="throttle"/> −1 to 1 (backing up negative), <paramref name="load"/> 0–1,
    /// <paramref name="overboost"/> 0–1 (the revs' <c>Overboost</c>), <paramref name="rpm"/> the revs (0: unknown).
    /// </summary>
    public void Update(bool running, float throttle, float load, bool shifted, float dt, float overboost = 0, float rpm = 0)
    {
        Puff = Billow = 0;
        dt = Math.Max(dt, 0);
        if (!running)
        {
            _running = false;
            _lagged = 0;
            Haze = 0;
            Soot = Approach(Soot, 0, dt, SootRelease);
            Black = Approach(Black, 0, dt, BlackRelease);
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
        overboost = Math.Clamp(overboost, 0, 1);
        if (shifted)
        {
            // Overboosted, a gear change throws a bigger puff (the turbo's boost dumped and the fuel back on).
            Puff = Math.Max(Puff, (0.4f + 0.5f * load) * (1 + 0.6f * overboost));
        }
        _lagged = Approach(_lagged, gas, dt, ThrottleLag);
        Soot = Approach(Soot, load, dt, load > Soot ? SootAttack : SootRelease);
        float black = overboost * (0.35f + 0.65f * load);
        Black = Approach(Black, black, dt, black > Black ? SootAttack : BlackRelease);
        Haze = 1f - 0.7f * Math.Max(Soot, Black);

        // Billows: overboosted, or pulling hard, a heavy puff in the engine's rhythm.
        _sinceBillow += dt;
        bool boosted = Black > 0.3f, pulling = Soot > 0.8f;
        if (boosted || pulling)
        {
            float every = BillowEvery * BillowRpm / Math.Max(rpm > 0 ? rpm : BillowRpm, 600f) * (boosted ? 1 : PullBillowSlower);
            if (_sinceBillow >= Math.Clamp(every, 0.2f, 1.4f))
            {
                Billow = boosted ? 0.7f + 0.7f * Black : 0.5f;
                _sinceBillow = 0;
            }
        }
    }

    // Toward a target over a time constant (exponential, steady whatever the frame rate).
    private static float Approach(float from, float to, float dt, float seconds) =>
        to + (from - to) * MathF.Exp(-dt / Math.Max(seconds, 1e-3f));
}
