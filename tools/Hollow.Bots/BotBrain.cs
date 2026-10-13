using System;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;

namespace Hollow.Bots;

public enum BotScript
{
    /// <summary>Walks, now and then sprinting, turning to a new heading every few seconds.</summary>
    Wander,

    /// <summary>Walks round and round.</summary>
    Circle,

    /// <summary>Wanders and jumps every couple of seconds.</summary>
    Jump,

    /// <summary>A third of each.</summary>
    Mixed,

    /// <summary>Plays the survival game: gathers, picks, crafts, eats, fights now and then (<see cref="SurvivalBrain"/>).</summary>
    Survive,

    /// <summary>Fights with guns: shoots at whoever is in range, reloads, helps the downed up (<see cref="FightBrain"/>, G4).</summary>
    Fight,

    /// <summary>Raids the next bot's hut with charges, grenades, rockets and explosive rounds (<see cref="RaidBrain"/>, G5).</summary>
    Raid,
}

/// <summary>
/// A bot's scripted controls. Deterministic per bot (seeded by its number), and kept within
/// <see cref="HomeRadius"/> of the zone's origin, so a crowd of bots stays in each other's view (the bandwidth worst
/// case).
/// </summary>
public sealed class BotBrain
{
    public const float HomeRadius = 120f;

    private readonly BotScript _script;
    private Rng _rng;
    private float _yaw;
    private float _targetYaw;
    private double _nextTurn;
    private double _nextJump;
    private bool _sprint;
    private bool _walking = true;

    public BotBrain(int index, BotScript script)
    {
        _rng = new Rng(Hash64.Of((ulong)index, 0xB07));
        _script = script == BotScript.Mixed ? (BotScript)(index % 3) : script;
        _yaw = _targetYaw = (float)_rng.Range(-Math.PI, Math.PI);
        _nextJump = _rng.Range(0, 2);
    }

    public InputFrame Think(double time, double delta, Vector3 position)
    {
        var input = new InputFrame();
        switch (_script)
        {
            case BotScript.Circle:
                _yaw += (float)(delta * 0.6);
                input.Forward = 1;
                break;
            default:
                if (time >= _nextTurn)
                {
                    _nextTurn = time + _rng.Range(2, 6);
                    _targetYaw = (float)_rng.Range(-Math.PI, Math.PI);
                    _sprint = _rng.Chance(0.3);
                    _walking = _rng.Chance(0.85);
                }
                // Far from home: head back.
                var flat = new Vector2(position.X, position.Z);
                if (flat.Length() > HomeRadius)
                {
                    _targetYaw = MathF.Atan2(flat.X, flat.Y);
                    _walking = true;
                }
                float turn = WrapAngle(_targetYaw - _yaw);
                _yaw += Math.Clamp(turn, -(float)delta * 2f, (float)delta * 2f);
                input.Forward = _walking ? 1 : 0;
                input.Sprint = _sprint;
                break;
        }
        if (_script == BotScript.Jump && time >= _nextJump)
        {
            _nextJump = time + _rng.Range(1.5, 3);
            input.Jump = true;
            input.JumpPressed = true;
        }
        _yaw = WrapAngle(_yaw);
        input.Yaw = _yaw;
        return input;
    }

    private static float WrapAngle(float a)
    {
        while (a > MathF.PI)
        {
            a -= 2 * MathF.PI;
        }
        while (a < -MathF.PI)
        {
            a += 2 * MathF.PI;
        }
        return a;
    }
}
