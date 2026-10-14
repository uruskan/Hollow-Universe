using System;
using System.Linq;
using Godot;
using Hollow.Simulation;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Num = System.Numerics;

namespace HollowUniverse.Ships;

// --tour: the sandbox played by a script through the same keys a player presses (Input.ParseInputEvent), to check the
// whole loop end to end and measure it: walk to the hatch, open it, board, lift off, throttle up, fly, turn with a boost,
// brake with flight assist, sink to a landing, step out. Prints what happened and quits. The mouse is never captured.
public partial class ShipSandbox
{
    private enum Leg { Walk, Open, Board, Lift, Throttle, Cruise, Turn, Brake, Descend, Settle, Exit, Done }

    private Leg _leg = Leg.Walk;
    private double _legStarted;
    private double _tourStarted = -1;
    private float _maxHeight, _maxSpeed, _touchdown = -1, _crash;
    private double _brakeFrom;
    private float _brakeSpeed;
    private Num.Vector3 _brakeAt;
    private float _fuelAtBoard;
    private string _log = "";
    private bool _climbing;

    private static void Hold(Godot.Key key, bool down) =>
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = down });

    private void Next(Leg leg)
    {
        _log += FormattableString.Invariant($"  {_leg} {_clock - _legStarted:0.0} s\n");
        _leg = leg;
        _legStarted = _clock;
    }

    private void Tour()
    {
        if (_tourStarted < 0)
        {
            // From the start: the hatch shut, the pilot a few steps from it.
            _tourStarted = _clock;
            _legStarted = _clock;
            _ship.HatchOpen = false;
            _ship.HatchProgress = 0;
        }
        if (_ship.Destroyed || !_zone.Ships().All.Contains(_ship))
        {
            GD.PrintErr($"[tour] the ship was destroyed in {_leg}");
            Finish(false);
            return;
        }
        double t = _clock - _legStarted;
        ShipState s = _ship.State;
        Num.Vector3 up = _zone.Frame.UpAt(s.Position);
        if (Seated)
        {
            _maxHeight = MathF.Max(_maxHeight, _zone.HeightAboveGround(s.Origin(_ship.Def.CentreOfMass)));
            _maxSpeed = MathF.Max(_maxSpeed, s.Velocity.Length());
        }
        if (_clock - _tourStarted > 150 && _leg != Leg.Done)
        {
            GD.PrintErr($"[tour] stuck in {_leg} after 150 s: {s}");
            Finish(false);
            return;
        }
        // Like a pilot watching the ground: climb over the hills while cruising, turning and braking.
        if (_leg is Leg.Cruise or Leg.Turn or Leg.Brake)
        {
            bool low = _zone.HeightAboveGround(s.Origin(_ship.Def.CentreOfMass)) < 45;
            if (low != _climbing)
            {
                _climbing = low;
                Hold(Godot.Key.Space, low);
            }
        }
        else if (_climbing)
        {
            _climbing = false;
            Hold(Godot.Key.Space, false);
        }
        switch (_leg)
        {
            case Leg.Walk:
            {
                Num.Vector3 hatch = _ship.PointOf(_ship.Def.Hatch);
                Num.Vector3 outward = Num.Vector3.Transform(-Num.Vector3.UnitX, s.Rotation);
                Num.Vector3 feet = _zone.Entities.Get<Character>(_player).State.Position;
                Num.Vector3 to = hatch + outward * 1.3f - feet;
                Num.Vector3 flat = to - up * Num.Vector3.Dot(to, up);
                if (flat.Length() > 0.5f && t < 20)
                {
                    _yaw = MathF.Atan2(-flat.X, -flat.Z);
                    Hold(Godot.Key.W, true);
                    break;
                }
                Hold(Godot.Key.W, false);
                LookFrom(_zone.EyeOf(_zone.Entities.Get<Character>(_player).State), hatch);
                Next(Leg.Open);
                Hold(Godot.Key.E, true);
                break;
            }
            case Leg.Open:
                Hold(Godot.Key.E, false);
                if (_ship.HatchProgress >= Ship.HatchTicks)
                {
                    Next(Leg.Board);
                    Hold(Godot.Key.E, true);
                }
                break;
            case Leg.Board:
                Hold(Godot.Key.E, false);
                if (Seated)
                {
                    _fuelAtBoard = s.Fuel;
                    Next(Leg.Lift);
                    Hold(Godot.Key.Space, true);
                }
                break;
            case Leg.Lift:
                if (t > 3)
                {
                    Hold(Godot.Key.Space, false);
                    Next(Leg.Throttle);
                    Hold(Godot.Key.W, true);
                }
                break;
            case Leg.Throttle:
                if (t > 0.6)
                {
                    Hold(Godot.Key.W, false);
                    Next(Leg.Cruise);
                }
                break;
            case Leg.Cruise:
                if (t > 6)
                {
                    Next(Leg.Turn);
                    Hold(Godot.Key.Right, true);
                    Hold(Godot.Key.Shift, true);
                }
                break;
            case Leg.Turn:
                if (t > 0.3)
                {
                    Hold(Godot.Key.Shift, false);
                }
                if (t > 2.5)
                {
                    Hold(Godot.Key.Right, false);
                    Next(Leg.Brake);
                    _brakeFrom = _clock;
                    _brakeSpeed = s.Velocity.Length();
                    _brakeAt = s.Position;
                    Hold(Godot.Key.X, true);
                }
                break;
            case Leg.Brake:
                Hold(Godot.Key.X, false);
                if (s.Velocity.Length() < 2)
                {
                    _log += FormattableString.Invariant(
                        $"  flight assist stopped it from {_brakeSpeed:0} m/s in {_clock - _brakeFrom:0.0} s over {Num.Vector3.Distance(_brakeAt, s.Position):0} m\n");
                    Next(Leg.Descend);
                    Hold(Godot.Key.C, true);
                }
                break;
            case Leg.Descend:
                if (s.Has(ShipFlags.Touching) || s.Has(ShipFlags.Landed))
                {
                    Hold(Godot.Key.C, false);
                    Next(Leg.Settle);
                }
                break;
            case Leg.Settle:
                if (t > 1.5)
                {
                    Next(Leg.Exit);
                    Hold(Godot.Key.F, true);
                }
                break;
            case Leg.Exit:
                Hold(Godot.Key.F, false);
                if (!Seated)
                {
                    Next(Leg.Done);
                    Finish(true);
                }
                break;
        }
    }

    // After each zone step while touring: the touchdown and any crash.
    private void TourStep()
    {
        if (_ship.Events.Touchdown && _touchdown < 0 && _leg >= Leg.Lift)
        {
            _touchdown = _ship.Events.TouchdownSpeed;
        }
        _crash += _ship.Events.CrashDamage;
    }

    private void Finish(bool done)
    {
        ShipState s = _ship.State;
        Num.Vector3 feet = _zone.Entities.Get<Character>(_player).State.Position;
        GD.Print("[tour] legs:\n" + _log
            + FormattableString.Invariant($"[tour] {(done ? "done" : "FAILED")} in {_clock - _tourStarted:0.0} s: highest {_maxHeight:0} m over the ground, fastest {_maxSpeed:0} m/s; ")
            + FormattableString.Invariant($"touched down at {_touchdown:0.0} m/s, {_crash:0} hull lost to impacts, {s.Health:0} hull left; {_fuelAtBoard - s.Fuel:0.0} fuel burnt; ")
            + FormattableString.Invariant($"landed {s.Has(ShipFlags.Landed)}; the pilot stepped out {Num.Vector3.Distance(feet, _ship.Origin):0.0} m from the ship; ")
            + FormattableString.Invariant($"{_zone.Physics.TilesBuiltOnDemand} ground tiles built on demand"));
        GetTree().Quit(done ? 0 : 1);
    }
}
