using System;
using System.Linq;
using Godot;
using Hollow.Simulation;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Num = System.Numerics;

namespace HollowUniverse.Ships;

// --tour for a rotorcraft (the KA-FA 1500, docs/MINICOPTER.md §5): the sandbox played through the keys a pilot presses
// (Rust's: W climb, S sink, A/D yaw, the arrows a keyboard stick), to check the whole flight end to end and measure it: walk
// to the seat, sit, hold W while the rotor spools and lifts it, climb, push the stick forward and cruise, turn on the
// pedals, pull the stick back to stop, fly back over the open pad it started from (a 7 m rotor lands where there are no
// trees), sink to a landing, step out. Prints what happened and quits with 0 when every check holds. The mouse is never
// captured.
public partial class ShipSandbox
{
    private enum RotorLeg { Walk, Board, Spool, Climb, Cruise, Turn, Flare, Return, Descend, Settle, Exit, Done }

    private RotorLeg _rotorLeg = RotorLeg.Walk;
    private float _liftOffAt = -1, _headingBefore, _rotorTurned, _cruiseSpeed, _flareFrom;
    private bool _rotorClimbing;
    private string _strikes = "";

    private void NextRotor(RotorLeg leg)
    {
        _log += FormattableString.Invariant($"  {_rotorLeg} {_clock - _legStarted:0.0} s\n");
        _rotorLeg = leg;
        _legStarted = _clock;
    }

    private float Over(in ShipState s) => _zone.HeightAboveGround(s.Origin(_ship.Def.CentreOfMass));

    private float FlatSpeed(in ShipState s)
    {
        Num.Vector3 up = _zone.Frame.UpAt(s.Position);
        return (s.Velocity - up * Num.Vector3.Dot(s.Velocity, up)).Length();
    }

    private void RotorTour()
    {
        if (_tourStarted < 0)
        {
            _tourStarted = _clock;
            _legStarted = _clock;
        }
        if (_ship.Destroyed || !_zone.Ships().All.Contains(_ship))
        {
            GD.PrintErr($"[tour] the KA-FA was destroyed in {_rotorLeg}");
            FinishRotor(false);
            return;
        }
        double t = _clock - _legStarted;
        ShipState s = _ship.State;
        Num.Vector3 up = _zone.Frame.UpAt(s.Position);
        if (Seated)
        {
            _maxHeight = MathF.Max(_maxHeight, Over(s));
            _maxSpeed = MathF.Max(_maxSpeed, FlatSpeed(s));
        }
        if (_clock - _tourStarted > 180 && _rotorLeg != RotorLeg.Done)
        {
            float tilt = MathF.Acos(Math.Clamp(Num.Vector3.Dot(s.Up, up), -1, 1)) * 180 / MathF.PI;
            GD.PrintErr(FormattableString.Invariant(
                $"[tour] stuck in {_rotorLeg} after 180 s: {Over(s):0.00} m over the ground, tilted {tilt:0.0}°, {_ship.Events.Contacts} skid contacts, {Num.Vector3.Distance(s.Position, _pad):0.0} m from the pad's point; {s}"));
            FinishRotor(false);
            return;
        }
        // Like a pilot watching the ground: the collective up over the hills and the trees (a 7 m rotor) while cruising,
        // turning and flaring.
        if (_rotorLeg is RotorLeg.Cruise or RotorLeg.Turn or RotorLeg.Flare or RotorLeg.Return)
        {
            bool low = Over(s) < 70;
            if (low != _rotorClimbing)
            {
                _rotorClimbing = low;
                Hold(Godot.Key.W, low);
            }
        }
        switch (_rotorLeg)
        {
            case RotorLeg.Walk:
            {
                Num.Vector3 seat = _ship.PointOf(_ship.Def.PartPoint(Hatch.Part));
                Num.Vector3 outward = Num.Vector3.Transform(-Num.Vector3.UnitX, s.Rotation);
                Num.Vector3 feet = _zone.Entities.Get<Character>(_player).State.Position;
                Num.Vector3 to = seat + outward * 1.1f - feet;
                Num.Vector3 flat = to - up * Num.Vector3.Dot(to, up);
                if (flat.Length() > 0.5f && t < 25)
                {
                    _yaw = Facing.YawOf(flat, up);
                    Hold(Godot.Key.W, true);
                    break;
                }
                Hold(Godot.Key.W, false);
                LookFrom(_zone.EyeOf(_zone.Entities.Get<Character>(_player).State), seat);
                NextRotor(RotorLeg.Board);
                Hold(Godot.Key.E, true);
                break;
            }
            case RotorLeg.Board:
                Hold(Godot.Key.E, false);
                if (Seated)
                {
                    _fuelAtBoard = s.Fuel;
                    NextRotor(RotorLeg.Spool);
                    Hold(Godot.Key.W, true);
                }
                else if (t > 3)
                {
                    GD.PrintErr("[tour] couldn't sit at the pilot's seat");
                    FinishRotor(false);
                }
                break;
            case RotorLeg.Spool:
                if (Over(s) > 1.0f)
                {
                    _liftOffAt = (float)t;
                    NextRotor(RotorLeg.Climb);
                }
                break;
            case RotorLeg.Climb:
                if (Over(s) > 100)
                {
                    Hold(Godot.Key.W, false);
                    NextRotor(RotorLeg.Cruise);
                    Hold(Godot.Key.Up, true);
                }
                break;
            case RotorLeg.Cruise:
                if (t > 12)
                {
                    _cruiseSpeed = FlatSpeed(s);
                    _headingBefore = ShipMotor.YawOf(s.Rotation, up);
                    NextRotor(RotorLeg.Turn);
                    Hold(Godot.Key.D, true);
                }
                break;
            case RotorLeg.Turn:
                if (t > 3)
                {
                    Hold(Godot.Key.D, false);
                    Hold(Godot.Key.Up, false);
                    float turned = (ShipMotor.YawOf(s.Rotation, up) - _headingBefore) * 180 / MathF.PI;
                    _rotorTurned = MathF.Abs(((turned % 360) + 540) % 360 - 180);
                    _flareFrom = FlatSpeed(s);
                    NextRotor(RotorLeg.Flare);
                    Hold(Godot.Key.Down, true);
                }
                break;
            case RotorLeg.Flare:
                if (FlatSpeed(s) < 2.5f || t > 15)
                {
                    Hold(Godot.Key.Down, false);
                    if (_rotorClimbing)
                    {
                        _rotorClimbing = false;
                        Hold(Godot.Key.W, false);
                    }
                    _log += FormattableString.Invariant($"  flared from {_flareFrom:0} m/s to {FlatSpeed(s):0.0} m/s in {t:0.0} s\n");
                    NextRotor(RotorLeg.Return);
                }
                break;
            case RotorLeg.Return:
                if (FlyTo(s, _pad, t))
                {
                    if (_rotorClimbing)
                    {
                        _rotorClimbing = false;
                        Hold(Godot.Key.W, false);
                    }
                    NextRotor(RotorLeg.Descend);
                    Hold(Godot.Key.S, true);
                }
                break;
            case RotorLeg.Descend:
                if (s.Has(ShipFlags.Touching) || s.Has(ShipFlags.Landed))
                {
                    Hold(Godot.Key.S, false);
                    NextRotor(RotorLeg.Settle);
                }
                break;
            case RotorLeg.Settle:
                if (t > 2)
                {
                    NextRotor(RotorLeg.Exit);
                    Hold(Godot.Key.Space, true);
                }
                break;
            case RotorLeg.Exit:
                Hold(Godot.Key.Space, false);
                if (!Seated)
                {
                    NextRotor(RotorLeg.Done);
                    FinishRotor(true);
                }
                break;
        }
    }

    // Flies over a point of the ground through the keys, as a pilot on a keyboard would: the stick (the arrows) tilts it
    // toward the velocity it wants (toward the point, slower as it nears), the pedals face it there while it is far. True once
    // it hovers within 3 m of the point.
    private bool FlyTo(in ShipState s, Num.Vector3 point, double t)
    {
        Num.Vector3 up = _zone.Frame.UpAt(s.Position);
        Num.Vector3 to = point - s.Position;
        to -= up * Num.Vector3.Dot(to, up);
        Num.Vector3 v = s.Velocity - up * Num.Vector3.Dot(s.Velocity, up);
        float distance = to.Length(), speed = v.Length();
        if ((distance < 1.5f && speed < 0.8f) || t > 90)
        {
            foreach (Godot.Key key in new[] { Godot.Key.A, Godot.Key.D, Godot.Key.Up, Godot.Key.Down, Godot.Key.Left, Godot.Key.Right })
            {
                Hold(key, false);
            }
            _log += FormattableString.Invariant($"  back over the pad: {distance:0.0} m off at {speed:0.0} m/s\n");
            return true;
        }
        Num.Vector3 forward = s.Forward - up * Num.Vector3.Dot(s.Forward, up);
        forward = forward.LengthSquared() > 1e-6f ? Num.Vector3.Normalize(forward) : forward;
        Num.Vector3 right = Num.Vector3.Cross(forward, up);
        Num.Vector3 want = distance > 0.1f ? to / distance * MathF.Min(18, 0.3f * distance) : Num.Vector3.Zero;
        Num.Vector3 miss = want - v;
        float ahead = Num.Vector3.Dot(miss, forward), aside = Num.Vector3.Dot(miss, right);
        // Finer near the point: a dead band of 1.2 m/s far off, 0.4 m/s within 10 m.
        float band = distance < 10 ? 0.4f : 1.2f;
        Hold(Godot.Key.Up, ahead > band);
        Hold(Godot.Key.Down, ahead < -band);
        Hold(Godot.Key.Right, aside > band);
        Hold(Godot.Key.Left, aside < -band);
        // Far off, the nose toward it (+ yaw is to the left: A); near, the pedals let go.
        float error = 0;
        if (distance > 40)
        {
            error = (ShipMotor.YawOf(ShipMotor.Basis(to, up), up) - ShipMotor.YawOf(s.Rotation, up)) * 180 / MathF.PI;
            error = ((error % 360) + 540) % 360 - 180;
        }
        Hold(Godot.Key.A, error > 8);
        Hold(Godot.Key.D, error < -8);
        return false;
    }

    // After each zone step while touring a rotorcraft: the touchdown and any crash.
    private void RotorTourStep()
    {
        if (_ship.Events.Touchdown && _rotorLeg >= RotorLeg.Descend)
        {
            _touchdown = MathF.Max(_touchdown, _ship.Events.TouchdownSpeed);
        }
        _crash += _ship.Events.CrashDamage;
        if (_ship.Events.Strike)
        {
            _strikes += FormattableString.Invariant($"  rotor strike in {_rotorLeg}, {Over(_ship.State):0.0} m over the ground\n");
        }
    }

    private void FinishRotor(bool done)
    {
        ShipState s = _ship.State;
        bool held = done && _liftOffAt > 0 && _maxSpeed >= 20 && _rotorTurned >= 60 && _crash <= 0 && _touchdown < 4;
        GD.Print("[tour] legs:\n" + _log + _strikes
            + FormattableString.Invariant($"[tour] {(held ? "done" : "FAILED")} in {_clock - _tourStarted:0.0} s: lifted off {_liftOffAt:0.0} s after sitting, ")
            + FormattableString.Invariant($"highest {_maxHeight:0} m over the ground, fastest {_maxSpeed:0.0} m/s (cruising {_cruiseSpeed:0.0}), turned {_rotorTurned:0}° in 3 s; ")
            + FormattableString.Invariant($"touched down at {_touchdown:0.0} m/s, {_crash:0} hull lost to impacts, {s.Health:0} hull left; {_fuelAtBoard - s.Fuel:0.0} fuel burnt; ")
            + FormattableString.Invariant($"spool {s.Spool:0.00} when stepping out"));
        if (done && !held)
        {
            GD.PrintErr("[tour] a check failed: lift-off, 20 m/s, a 60° turn, no crash damage, a touchdown under 4 m/s");
        }
        GetTree().Quit(held ? 0 : 1);
    }
}
