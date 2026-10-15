using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Fx;

/// <summary>
/// The Iron Wake in the effects viewer (IRON_WAKE.md §11): <c>--action ship</c>, its siege battery firing over its deck
/// with a defense mount rippling beside it and incendiary fires burning downrange; and <c>--stress battery --measure
/// &lt;s&gt;</c>, the cost of its guns in a fight: both siege batteries and all four defense mounts firing at their full
/// rate (every shot's muzzle, deck dust, rocket ignition, trail and breech smoke), incendiary rockets bursting downrange
/// every 3 s with six fires burning, a hit on the hull every tenth of a second, two hulks smoking and the four lift
/// engines' wash, with the effects off and on in turn as in the firefight.
/// </summary>
public partial class FxViewer
{
    // ---------------------------------------------------------------- the scene

    private void ActionShip()
    {
        // From the deck behind the dorsal battery, looking over its barrels downrange.
        FxStation siege = FxStage.Build(this, "siege", new Vector3(0, 0, -12), MathF.PI / 2 - 0.35f);
        FxStation defense = FxStage.Build(this, "defense", new Vector3(10.5f, 0, -15), MathF.PI / 2 - 0.7f);
        _camera.Fov = 62;
        _camera.Position = new Vector3(-8.5f, 4.6f, -4f);
        _camera.LookAt(new Vector3(4f, 2.2f, -26f), Vector3.Up);
        // Two incendiary fires burning downrange from earlier rockets.
        FxDirector.Start("fire.patch", new Vector3(14f, 0.02f, -50f), Vector3.Up);
        FxDirector.Start("fire.patch", new Vector3(-7f, 0.02f, -66f), Vector3.Up);
        At(1.0, () => SiegeShot(siege, 0));
        for (int i = 0; i < 3; i++)
        {
            Node3D barrel = defense.Sockets[$"muzzle{i}"];
            At(0.56 + 0.25 * i, () => DefenseShot(barrel));
        }
        _captureAt = 1.11;
    }

    /// <summary>One siege barrel's shot as the battery's view will play it: the muzzle, the deck under it, the rocket
    /// lighting 4 m out, and the breech opening 1.35 s later (IRON_WAKE.md §6.6: recover 0.9 s, breech open 0.45 s)
    /// with its smoke trailing until the rammer closes it 2.2 s on.</summary>
    private void SiegeShot(FxStation siege, int barrel)
    {
        Node3D muzzle = siege.Sockets[$"muzzle{barrel}"], breech = siege.Sockets[$"breech{barrel}"];
        Node3D deck = siege.Sockets["deck0"];
        _shotsThisPhase++;
        FxDirector.Muzzle("muzzle.siege", muzzle, false);
        if (barrel == 0)
        {
            FxDirector.Play("muzzle.siege.dust", deck.GlobalPosition, Vector3.Up, new FxParams { Direction = deck.GlobalBasis.X });
        }
        FxStage.LaunchRocket(this, muzzle, FxRocketLaunch.Siege, "ignite.rocket");
        At(1.35, () =>
        {
            if (!IsInstanceValid(breech) || (_batteryStress && !EffectsOn))
            {
                return;
            }
            Vector3 at = breech.GlobalPosition, back = breech.GlobalBasis.X;
            FxDirector.Play("breech.smoke", at, back, new FxParams { Follow = breech, Direction = back });
            FxDirector.Start("breech.smoke.wisps", at, Vector3.Up, new FxParams { Follow = breech, Duration = 2.2f });
        });
    }

    private void DefenseShot(Node3D muzzle)
    {
        _shotsThisPhase++;
        FxDirector.Muzzle("muzzle.defense", muzzle, false);
        FxStage.LaunchRocket(this, muzzle, FxRocketLaunch.Defense, "ignite.rocket");
    }

    // ---------------------------------------------------------------- the measurement

    private bool _batteryStress;
    private readonly List<FxStation> _sieges = new(), _defenses = new();
    private FxStation? _hullTarget;
    private readonly List<(Vector3 At, string Key, FxParams Params)> _batteryLoops = new();
    private double _nextHullHit, _nextIncendiary;
    private readonly double[] _nextSiege = new double[2], _nextRipple = new double[4];
    private readonly int[] _siegeBarrel = new int[2];
    private int _batteryLoopPhase = -1;
    private HashSet<string> _batteryParts = new(StringComparer.Ordinal);

    /// <summary>Whether a part of the fight plays (<c>--battery-parts siege,defense,fires,hull,wash,smoke</c>; all by default),
    /// to find what costs what.</summary>
    private bool BatteryPart(string part) => _batteryParts.Count == 0 || _batteryParts.Contains(part);

    private void StartBatteryStress()
    {
        _batteryStress = true;
        if (Options.TryGetValue("battery-parts", out string? parts))
        {
            _batteryParts = new HashSet<string>(parts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.Ordinal);
        }
        // Both siege batteries on the centreline, the four defense mounts out on the sides, all aimed downrange (-Z).
        _sieges.Add(FxStage.Build(this, "siege", new Vector3(-2, 0, -2), MathF.PI / 2 - 0.08f));
        _sieges.Add(FxStage.Build(this, "siege", new Vector3(3, 0, 10), MathF.PI / 2 + 0.06f));
        foreach ((float x, float z, float turn) in new[] { (-11f, 0f, 0.5f), (11f, 2f, -0.45f), (-12f, 14f, 0.65f), (12f, 15f, -0.6f) })
        {
            _defenses.Add(FxStage.Build(this, "defense", new Vector3(x, 0, z), MathF.PI / 2 + turn));
        }
        _hullTarget = FxStage.Build(this, "hull", new Vector3(7, 0, 24), 0.3f);
        // What burns and blows for the whole fight: the lift engines' wash under the ship, two hulks smoking.
        if (BatteryPart("wash"))
        {
            foreach (Vector3 at in new[] { new Vector3(-7, 0.02f, -8), new Vector3(7, 0.02f, -8), new Vector3(-7, 0.02f, 18),
                         new Vector3(7, 0.02f, 18) })
            {
                _batteryLoops.Add((at, "wash.lift", default));
            }
        }
        if (BatteryPart("smoke"))
        {
            _batteryLoops.Add((_sieges[1].Sockets["house"].GlobalPosition, "smoke.damaged", default));
            _batteryLoops.Add((new Vector3(-20, 1.5f, -30), "smoke.damaged", new FxParams { Scale = 1.4f }));
        }
        _camera.Fov = 70;
        _camera.Position = new Vector3(0, 13, 44);
        _camera.LookAt(new Vector3(0, 1, -40), Vector3.Up);
        _nextSiege[1] = 1.15;
        for (int i = 0; i < 4; i++)
        {
            _nextRipple[i] = 0.75 * i;
        }
        GD.Print("[fx-measure] battery: 2 siege batteries, 4 defense mounts, incendiary fires, hull hits, wash and smoke; "
            + $"warm-up 4 s, then {_measureSeconds:0} s each: effects off, on, off, on");
    }

    private void UpdateBatteryStress()
    {
        if (!EffectsOn)
        {
            return;
        }
        if (_batteryLoopPhase != _phase)
        {
            // The fight is already on: its loops, and six fires at every stage of their 18 s.
            _batteryLoopPhase = _phase;
            foreach ((Vector3 at, string key, FxParams p) in _batteryLoops)
            {
                FxDirector.Start(key, at, Vector3.Up, p);
            }
            for (int i = 0; i < 6 && BatteryPart("fires"); i++)
            {
                Incendiary(18f - 3f * i);
            }
        }
        double t = _clock - _phaseStart;
        for (int i = 0; i < 2 && BatteryPart("siege"); i++)
        {
            while (t >= _nextSiege[i])
            {
                _nextSiege[i] += 2.3;
                SiegeShot(_sieges[i], _siegeBarrel[i]);
                _siegeBarrel[i] ^= 1;
            }
        }
        for (int i = 0; i < 4 && BatteryPart("defense"); i++)
        {
            while (t >= _nextRipple[i])
            {
                _nextRipple[i] += 3.0;
                for (int b = 0; b < 3; b++)
                {
                    Node3D barrel = _defenses[i].Sockets[$"muzzle{b}"];
                    At(0.25 * b, () =>
                    {
                        if (EffectsOn)
                        {
                            DefenseShot(barrel);
                        }
                    });
                }
            }
        }
        while (t >= _nextIncendiary && BatteryPart("fires"))
        {
            _nextIncendiary += 3.0;
            Incendiary(18f, burst: true);
        }
        while (t >= _nextHullHit && _hullTarget != null && BatteryPart("hull"))
        {
            _nextHullHit += 0.1;
            FxDef? hit = FxCatalog.Find("impact.hull");
            if (hit != null && _hullTarget.Play != null)
            {
                _hullTarget.Play(hit, default, _fight, At);
            }
        }
    }

    /// <summary>An incendiary rocket's fire downrange, burning for <paramref name="seconds"/>; with its burst when it
    /// has just hit.</summary>
    private void Incendiary(float seconds, bool burst = false)
    {
        var at = new Vector3(_fight.Range(-35f, 35f), 0.02f, _fight.Range(-120f, -55f));
        if (burst)
        {
            FxDirector.Play("explosion.incendiary", at, Vector3.Up);
        }
        FxDirector.Start("fire.patch", at, Vector3.Up, new FxParams { Duration = seconds });
        At(seconds, () =>
        {
            if (EffectsOn)
            {
                FxDirector.Play("fire.patch.out", at, Vector3.Up);
            }
        });
    }

    /// <summary>The phase restarts the schedules: the measurement compares like with like.</summary>
    private void ResetBatterySchedules()
    {
        _nextSiege[0] = 0;
        _nextSiege[1] = 1.15;
        for (int i = 0; i < 4; i++)
        {
            _nextRipple[i] = 0.75 * i;
        }
        _nextIncendiary = 1.5;
        _nextHullHit = 0;
    }
}
