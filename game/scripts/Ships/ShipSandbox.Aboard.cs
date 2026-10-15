using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using HollowUniverse.Characters;
using Num = System.Numerics;

namespace HollowUniverse.Ships;

// The ship sandbox for a ship people walk in (IRON_WAKE.md §4, IW1): `--view ship --ship iron-wake`. The ship parked in a
// clearing near the landing site, its ramp open; you on foot behind it: walk up the ramp onto Deck A, about the decks and
// down again (E at the helm takes it, as the Tin Finch's seat). Two of the crew stand aboard, drawn on the ship as it is
// drawn. Scenes (--scene): parked (the default), ramp (the parked ship's open ramp from the ground behind it), deck (in
// flight, on Deck A looking out of the open ramp at the land), outside (hovering low, seen from the ground, the crew
// aboard). --tour walks up the ramp, has a pilot lift off, climb, turn and brake while you stand on the deck looking out,
// land, and walks you down the ramp; it prints what it measured and quits with 0 when every check holds.
public partial class ShipSandbox
{
    private bool _aboard;
    private Entity _helm = Entity.None;
    private readonly List<(Entity Who, CharacterView View)> _crew = new();
    private Num.Vector3 _localBefore, _localNow;
    private ulong _frameBefore, _frameNow;
    private ulong _viewFrame;
    private double _stepSeconds;
    private long _steps;

    // ---------------------------------------------------------------- set-up

    // The zone's own whole world, drawn by the surface view as the ground game draws it (docs/WHOLE_WORLD.md §10, S3c):
    // its ground, sea, sky and sun at the play clock's hour, and its natural objects as the plants (the view is opened
    // before this, for both kinds of ship).
    private void OpenGround()
    {
        _aboard = true;
    }

    // The flattest ground near the landing site a ship of this size finds (rings out to 30 m from its centre), what grows
    // there cleared; the ship parked on it, nose north, its ramp open; you on foot 12 m behind the ramp; the crew aboard.
    private void BuildAboard(ShipDef def)
    {
        _aboard = true;
        _padYaw = 0;
        Num.Vector3 best = _zone.GroundAt(0, 0);
        float spread = float.MaxValue;
        for (int i = -10; i <= 10; i++)
        {
            for (int j = -10; j <= 10; j++)
            {
                Num.Vector3 g = _zone.GroundAt(i * 30, j * 30);
                if (_zone.Surface.SeaRadius > 0 && _zone.Frame.ToBody(g).Length < _zone.Surface.SeaRadius + 3)
                {
                    continue;
                }
                float lo = float.MaxValue, hi = float.MinValue;
                Num.Vector3 up = _zone.Frame.UpAt(g);
                for (int k = 0; k < 12; k++)
                {
                    float a = k * MathF.PI / 6;
                    foreach (float r in new[] { 10f, 20f, 30f })
                    {
                        float height = Num.Vector3.Dot(_zone.GroundAt(i * 30 + r * MathF.Cos(a), j * 30 + r * MathF.Sin(a)) - g, up);
                        lo = MathF.Min(lo, height);
                        hi = MathF.Max(hi, height);
                    }
                }
                float score = hi - lo + new Num.Vector2(i, j).Length() * 0.03f;
                if (score < spread)
                {
                    spread = score;
                    best = g;
                }
            }
        }
        _pad = best;
        int cleared = _zone.Ships().ClearNatureFor(def, _pad, _padYaw);
        Entity player = AddPlayer(Local(def, new Num.Vector3(0, 0, -36)));
        _ship = _zone.Ships().SpawnShip(def, _pad, _padYaw, _zone.Players().PlayerOf(player)!.Id, def.FuelCapacity);
        _ship.HatchOpen = true;
        _ship.HatchProgress = Ship.HatchTicks;
        _shipView = new ShipView(def);
        AddChild(_shipView);
        for (int i = 0; i < 30 * 20 && _ship.ParkedBody == 0; i++)
        {
            Step(1);
        }
        LookAlong(Num.Vector3.UnitZ);
        _localBefore = _localNow = _zone.Entities.Get<Character>(_player).State.Position;
        GD.Print(FormattableString.Invariant(
            $"[ship] the {def.Name} parked {(_ship.ParkedBody != 0 ? "" : "NOT ")}on the flattest ground near the landing site ({spread:0.00} m of rise over its footprint, {cleared} trees and rocks cleared)"));
    }

    // A point on the ground under a point of the ship's frame where it stands (before it is made: as it will stand).
    private Num.Vector3 Local(ShipDef def, Num.Vector3 local)
    {
        Num.Vector3 up = _zone.Frame.UpAt(_pad);
        ShipState standing = ShipMotor.Standing(def, _pad, up, _padYaw);
        Num.Vector3 world = standing.PointOf(local, def.CentreOfMass);
        return _zone.Settle(world);
    }

    private ShipPose PoseOf(in ShipState s) => ShipFrame.PoseOf(s, _ship.Def.CentreOfMass);

    // One of the crew, standing aboard at a point of the ship's frame and looking along a direction of it, drawn.
    private Entity AddCrew(string name, Num.Vector3 local, Num.Vector3 looking, BodyType body)
    {
        CharacterState state = _zone.NewCharacter(local + Num.Vector3.UnitY * 0.02f);
        state.Frame = _ship.Id.Value;
        Entity e = _zone.Players().AddPlayer(_zone.NextId(), name, state);
        _zone.Entities.Get<Character>(e).Input = new MoveInput { View = Num.Vector3.Normalize(looking), Frame = _ship.Id.Value };
        var view = new CharacterView { Name = name, Body = body, Look = Looks.ForPlayer(_zone.Players().PlayerOf(e)!.Id.Value, body) };
        AddChild(view);
        _crew.Add((e, view));
        return e;
    }

    // The pilot, onto the bridge's dais and into the helm's seat (the board verb, until IW2's seats).
    private void TakeTheHelm()
    {
        if (_helm != Entity.None && _zone.Ships().SeatOf(_helm) == _ship)
        {
            return;
        }
        CharacterState state = _zone.NewCharacter(new Num.Vector3(-1.75f, 9.12f, 13.9f));
        state.Frame = _ship.Id.Value;
        _helm = _zone.Players().AddPlayer(_zone.NextId(), "Helm", state);
        _zone.Entities.Get<Character>(_helm).Input = new MoveInput { View = Num.Vector3.UnitZ, Frame = _ship.Id.Value };
        Step(2);
        Outcome boarded = _zone.Interaction().Interact(_helm, new TargetRef(TargetFamily.Ship, _ship.Id.Value, ShipParts.Hatch), "board");
        if (boarded != OutcomeCode.Done)
        {
            GD.PushWarning($"[ship] the pilot couldn't take the helm: {boarded}");
        }
    }

    // The pilot's lift toward a height of the pads over the ground, slowing as it nears.
    private ShipInput HoldHeight(float height)
    {
        float climbing = Num.Vector3.Dot(_ship.State.Velocity, _zone.Frame.UpAt(_ship.State.Position));
        float over = _zone.HeightAboveGround(_ship.Origin);
        return new ShipInput { Vertical = Math.Clamp((height - over) * 0.12f - climbing * 0.05f, -0.6f, 0.6f) };
    }

    // Steps the zone with the scripted pilot's controls for some ticks.
    private void FlyAboard(Func<int, ShipInput> input, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            _ship.Input = input(i);
            Step(1);
        }
    }

    // Puts you aboard at a point of the ship's frame, looking along a direction of it.
    private void StandAboard(Num.Vector3 local, Num.Vector3 looking, float pitch = 0)
    {
        ref Character c = ref _zone.Entities.Get<Character>(_player);
        c.State = _zone.NewCharacter(local + Num.Vector3.UnitY * 0.02f);
        c.State.Frame = _ship.Id.Value;
        c.Input = new MoveInput { View = Num.Vector3.UnitZ, Frame = _ship.Id.Value };
        _viewFrame = _ship.Id.Value;
        LookAlong(looking);
        _pitch = pitch;
    }

    // Turns your view along a direction of the frame you are in (the ship's aboard, else the zone's along the ship).
    private void LookAlong(Num.Vector3 local)
    {
        CharacterState me = _zone.Entities.Get<Character>(_player).State;
        _viewFrame = me.Frame;
        Num.Vector3 up = me.Frame != 0 ? Num.Vector3.UnitY : _zone.Frame.UpAt(me.Position);
        Num.Vector3 direction = me.Frame != 0 ? local : PoseOf(_ship.State).DirectionToWorld(local);
        _yaw = Facing.YawOf(direction, up);
        _pitch = Facing.PitchOf(direction, up);
    }

    // ---------------------------------------------------------------- scenes

    private void SetSceneAboard(string scene)
    {
        ShipPose pose = PoseOf(_ship.State);
        Num.Vector3 up = _zone.Frame.UpAt(_pad);
        switch (scene)
        {
            case "ramp":
            {
                // The open ramp from the ground behind and to the side, someone on it and someone at its top.
                AddCrew("Ada", RampPoint(-21.5f, 0.6f), Num.Vector3.UnitZ, BodyType.Female);
                AddCrew("Cy", new Num.Vector3(-1.6f, 3.2f, -14.5f), -Num.Vector3.UnitZ, BodyType.Male);
                Step(10);
                Num.Vector3 eye = _zone.Settle(pose.ToWorld(new Num.Vector3(9.5f, 0, -41))) + up * 1.75f;
                _observer = (eye, pose.ToWorld(new Num.Vector3(0, 3.6f, -18)));
                break;
            }
            case "deck":
            {
                // In flight over the land: on Deck A at the top of the open ramp, looking out aft and down.
                AddCrew("Ada", new Num.Vector3(-2.2f, 3.2f, -16.4f), new Num.Vector3(-0.3f, 0, -1), BodyType.Female);
                TakeTheHelm();
                StandAboard(new Num.Vector3(0.9f, 3.2f, -12.6f), new Num.Vector3(-0.1f, 0, -1), -0.16f);
                FlyAboard(_ => HoldHeight(30) with { Throttle = 0.1f }, 30 * 9);
                FlyAboard(_ => HoldHeight(34) with { Throttle = 0.14f, Yaw = 0.35f }, 30 * 5);
                _observer = null;
                break;
            }
            case "outside":
            {
                // Hovering low over the clearing, the crew on the ramp and in the hold, from the ground beside the stern.
                AddCrew("Ada", RampPoint(-20.2f, -0.5f), new Num.Vector3(0.2f, 0, -1), BodyType.Female);
                AddCrew("Cy", new Num.Vector3(1.4f, 3.2f, -15.2f), new Num.Vector3(0.3f, 0, -1), BodyType.Male);
                AddCrew("Dee", new Num.Vector3(-2.4f, 3.2f, -12.5f), -Num.Vector3.UnitZ, BodyType.Male);
                TakeTheHelm();
                FlyAboard(_ => HoldHeight(7), 30 * 12);
                pose = PoseOf(_ship.State);
                Num.Vector3 eye = _zone.Settle(pose.ToWorld(new Num.Vector3(-8, 0, -36))) + up * 1.7f;
                _observer = (eye, pose.ToWorld(new Num.Vector3(0, 3.5f, -15)));
                break;
            }
            default:
                break;
        }
        _paused = _shot;
        _shipBefore = _shipNow = _ship.State;
        CharacterState me = _zone.Entities.Get<Character>(_player).State;
        (_localBefore, _localNow, _frameBefore, _frameNow) = (me.Position, me.Position, me.Frame, me.Frame);
        _shipCamera.Reset();
    }

    // A point on the open ramp, along the ship's frame's Z and across it (the ramp's surface under it, from its space).
    private Num.Vector3 RampPoint(float z, float x)
    {
        lock (Hollow.Simulation.Physics.JoltRuntime.Gate)
        {
            return _ship.Space!.Ray(new Num.Vector3(x, 8, z), -Num.Vector3.UnitY, 10, out Hollow.Simulation.Physics.SpaceHit hit)
                ? hit.Point : new Num.Vector3(x, 1.5f, z);
        }
    }

    // ---------------------------------------------------------------- each frame

    private void ProcessAboard(double delta)
    {
        if (_touring)
        {
            AboardTour();
        }
        if (!_paused)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            TickAboard(delta);
            _tickMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        float alpha = _paused ? 1 : (float)(_accumulator / CharacterMotor.DeltaTime);
        ShipState shown = Interpolate(_shipBefore, _shipNow, alpha);
        ShipPose drawn = PoseOf(shown);
        PlaceCameraAboard(shown, drawn, (float)delta, alpha);
        ShipView.Exposure = _surface.Sky?.Exposure ?? 1;
        _shipView.Visible = _zone.Ships().All.Contains(_ship);
        _shipView.FromCockpit = Seated && _shipCamera.Mode == ShipCameraMode.Cockpit && _observer == null;
        _shipView.Show(new ShipLook(Pivot(shown), _ship.HatchProgress / (float)Ship.HatchTicks, shown.Thrust,
            shown.Has(ShipFlags.EnginesOn), shown.Has(ShipFlags.Boosting), _ship.Piloted, _ship.Destroyed), delta);
        _shipView.ShowParts(_ship.Parts);
        foreach ((Entity who, CharacterView view) in _crew)
        {
            CharacterState s = _zone.Entities.Get<Character>(who).State;
            Num.Vector3 feet = s.Frame != 0 ? drawn.ToWorld(s.Position) : s.Position;
            Num.Vector3 up = s.Frame != 0 ? drawn.Up : _zone.Frame.UpAt(s.Position);
            Num.Vector3 look = _zone.Frames.DirectionToWorld(_zone.Entities.Get<Character>(who).Input.View, s.Frame, _zone.Tick);
            view.Apply(new CharacterFrame
            {
                Position = R(feet),
                Up = Dir(up),
                Yaw = CharacterFrame.YawToward(Dir(up), Dir(look)),
                Grounded = (s.Flags & MoveFlags.OnGround) != 0,
                Stance = CharacterStance.Stand,
            });
        }
        FillHud(shown);
        _hud.Visible &= !_shot;
    }

    private static Num.Vector3 R2(Vector3 v) => new(v.X, v.Y, v.Z);

    private void TickAboard(double delta)
    {
        _accumulator += delta;
        int ticks = 0;
        while (_accumulator >= CharacterMotor.DeltaTime)
        {
            _accumulator -= CharacterMotor.DeltaTime;
            if (++ticks > MaxTicksPerFrame)
            {
                _accumulator = 0;
                break;
            }
            if (Seated)
            {
                _ship.Input = Input.MouseMode == Input.MouseModeEnum.Captured || _shot ? _controls.Read(CharacterMotor.DeltaTime) : default;
            }
            else
            {
                _zone.Entities.Get<Character>(_player).Input = ReadWalkAboard();
                _jumpPressed = false;
            }
            CharacterState before = _zone.Entities.Get<Character>(_player).State;
            _shipBefore = _ship.State;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            _zone.Step();
            _stepSeconds += System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
            _steps++;
            if (_touring)
            {
                AboardTourStep();
            }
            Events();
            CharacterState now = _zone.Entities.Get<Character>(_player).State;
            (_localBefore, _frameBefore, _localNow, _frameNow) = (before.Position, before.Frame, now.Position, now.Frame);
            _shipNow = _ship.State;
            FollowFrameAboard();
        }
    }

    // Your walk, its view read in the frame it is given in (the server's and the prediction's rule: turned on the tick of a
    // hand-over).
    private MoveInput ReadWalkAboard()
    {
        CharacterState me = _zone.Entities.Get<Character>(_player).State;
        Num.Vector3 up = _viewFrame != 0 ? Num.Vector3.UnitY : _zone.Frame.UpAt(_zone.WorldOf(me));
        MoveInput input = ReadWalk();
        input.View = Facing.View(_yaw, _pitch, up);
        input.Frame = _viewFrame;
        if (_tourWalk != 0)
        {
            input.Forward = _tourWalk;
            input.Right = 0;
        }
        return input;
    }

    // Boarded or left: the view's heading and pitch turned into the new frame, looking the same way in the world.
    private void FollowFrameAboard()
    {
        CharacterState me = _zone.Entities.Get<Character>(_player).State;
        if (me.Frame == _viewFrame)
        {
            return;
        }
        ShipPose pose = PoseOf(_ship.State);
        Num.Vector3 world = _viewFrame != 0 ? pose.DirectionToWorld(Facing.View(_yaw, _pitch, Num.Vector3.UnitY))
            : Facing.View(_yaw, _pitch, _zone.Frame.UpAt(_zone.WorldOf(me)));
        Num.Vector3 up = me.Frame != 0 ? Num.Vector3.UnitY : _zone.Frame.UpAt(me.Position);
        Num.Vector3 local = me.Frame != 0 ? pose.DirectionToLocal(world) : world;
        _yaw = Facing.YawOf(local, up);
        _pitch = Math.Clamp(Facing.PitchOf(local, up), -MaxPitch, MaxPitch);
        _viewFrame = me.Frame;
    }

    // The camera: an observer's for a picture; the helm's (cockpit or chase) at the controls; else your eye, aboard on the
    // ship as it is drawn (IRON_WAKE.md §4.8).
    private void PlaceCameraAboard(in ShipState shown, in ShipPose drawn, float delta, float alpha)
    {
        Num.Vector3 eye, forward, up;
        if (_shot && _observer is (Num.Vector3 from, Num.Vector3 at))
        {
            eye = from;
            up = _zone.Frame.UpAt(from);
            forward = Num.Vector3.Normalize(at - from);
            _camera.Fov = 60;
        }
        else if (Seated)
        {
            (eye, forward, up) = _shipCamera.Follow(shown, _ship.Def, _zone.Frame.UpAt(shown.Position), delta, p => _zone.HeightAboveGround(p));
            _camera.Fov = _shipCamera.Mode == ShipCameraMode.Cockpit ? 74 : 68;
        }
        else
        {
            CharacterState me = _zone.Entities.Get<Character>(_player).State;
            Num.Vector3 local = _frameBefore == _frameNow ? Num.Vector3.Lerp(_localBefore, _localNow, alpha) : _localNow;
            float height = _zone.Tuning.EyeHeight(me.Stance);
            if (_frameNow != 0)
            {
                eye = drawn.ToWorld(local + Num.Vector3.UnitY * height);
                up = drawn.Up;
                forward = Num.Vector3.Normalize(drawn.DirectionToWorld(Facing.View(_yaw, _pitch, Num.Vector3.UnitY)));
            }
            else
            {
                up = _zone.Frame.UpAt(local);
                eye = local + up * height;
                forward = Facing.View(_yaw, _pitch, up);
            }
            _camera.Fov = 70;
        }
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(Dir(forward), Dir(up)), R(eye));
    }

    // ---------------------------------------------------------------- --tour

    private enum Walk { Settle, Up, Turn, Lift, Climb, Brake, Return, Land, Park, Down, Done }

    private Walk _walk = Walk.Settle;
    private int _walkTicks;
    private float _tourWalk;
    private double _walkStarted = -1;
    private Num.Vector3 _deckSpot;
    private float _drift, _climbed, _turned, _fastest, _startHeight, _startYaw;
    private long _boardedAt = -1, _leftAt = -1;
    private int _landedTicks;

    private void AboardTour()
    {
        if (_walkStarted < 0)
        {
            _walkStarted = _clock;
        }
        if (_clock - _walkStarted > 240 && _walk != Walk.Done)
        {
            GD.PrintErr($"[tour] stuck walking {_walk} after 240 s");
            FinishAboard(false);
        }
    }

    // Each tick of the tour: what you do and what the pilot does, leg by leg, and what is measured.
    private void AboardTourStep()
    {
        _walkTicks++;
        CharacterState me = _zone.Entities.Get<Character>(_player).State;
        ShipState s = _ship.State;
        Num.Vector3 up = _zone.Frame.UpAt(s.Position);
        float over = _zone.HeightAboveGround(_ship.Origin);
        float yaw = Facing.YawOf(s.Forward, up);
        switch (_walk)
        {
            case Walk.Settle when _walkTicks > 30:
                NextWalk(Walk.Up);
                _tourWalk = 1;
                LookAlong(Num.Vector3.UnitZ);
                break;
            case Walk.Up:
                if (me.Frame == _ship.Id.Value && _boardedAt < 0)
                {
                    _boardedAt = _zone.Tick;
                }
                if (me.Frame == _ship.Id.Value && me.Position.Z > -11)
                {
                    _tourWalk = 0;
                    NextWalk(Walk.Turn);
                }
                else if (_walkTicks > 30 * 15)
                {
                    FinishAboard(false);
                }
                break;
            case Walk.Turn when _walkTicks > 30:
                // Facing aft, looking out of the open ramp; the pilot takes the helm.
                LookAlong(new Num.Vector3(0, -0.2f, -1));
                TakeTheHelm();
                _deckSpot = _zone.Entities.Get<Character>(_player).State.Position;
                _startHeight = over;
                _startYaw = yaw;
                NextWalk(Walk.Lift);
                break;
            case Walk.Lift:
            case Walk.Climb:
            case Walk.Brake:
                _drift = MathF.Max(_drift, Num.Vector3.Distance(me.Position, _deckSpot));
                _climbed = MathF.Max(_climbed, over - _startHeight);
                _fastest = MathF.Max(_fastest, s.Velocity.Length());
                _turned = MathF.Max(_turned, MathF.Abs(Wrap(yaw - _startYaw)) * 180 / MathF.PI);
                _ship.Input = _walk == Walk.Lift ? new ShipInput { Vertical = 1 }
                    : _walk == Walk.Climb ? new ShipInput { Vertical = 0.3f, Throttle = 0.45f, Yaw = 0.6f } : default;
                if (_walk == Walk.Lift && _walkTicks > 30 * 6)
                {
                    NextWalk(Walk.Climb);
                }
                else if (_walk == Walk.Climb && _walkTicks > 30 * 8)
                {
                    NextWalk(Walk.Brake);
                }
                else if (_walk == Walk.Brake && (s.Velocity.Length() < 0.3f || _walkTicks > 30 * 25))
                {
                    NextWalk(Walk.Return);
                }
                break;
            case Walk.Return:
            {
                // Back over the clearing it rose from, 25 m up and nose north again (its footprint on what was cleared), then
                // down onto it.
                (ShipInput back, float off) = BackToPad();
                _ship.Input = HoldHeight(25) with { Throttle = back.Throttle, Strafe = back.Strafe, Yaw = back.Yaw };
                if (off < 2.5f && s.Velocity.Length() < 0.6f && MathF.Abs(Wrap(_startYaw - yaw)) < 0.05f)
                {
                    NextWalk(Walk.Land);
                }
                else if (_walkTicks > 30 * 90)
                {
                    FinishAboard(false);
                }
                break;
            }
            case Walk.Land:
            {
                // Down under flight assist until the pads hold it, then the pilot lets go and steps up from the helm.
                (ShipInput back, _) = BackToPad();
                _ship.Input = s.Has(ShipFlags.Touching) ? default : back with { Vertical = over > 12 ? -0.8f : -0.3f };
                _landedTicks = s.Has(ShipFlags.Landed) && s.Velocity.Length() < 0.1f ? _landedTicks + 1 : 0;
                if (_landedTicks > 30)
                {
                    Outcome exit = _zone.Ships().ExitShip(_helm);
                    GD.Print($"[tour] landed {over:0.00} m over the ground; the pilot steps up from the helm: {exit}");
                    NextWalk(Walk.Park);
                }
                else if (_walkTicks > 30 * 60)
                {
                    FinishAboard(false);
                }
                break;
            }
            case Walk.Park:
                _ship.Input = default;
                if (_ship.ParkedBody != 0)
                {
                    LookAlong(-Num.Vector3.UnitZ);
                    _tourWalk = 1;
                    NextWalk(Walk.Down);
                }
                else if (_walkTicks > 30 * 20)
                {
                    GD.PrintErr("[tour] the ship never parked");
                    FinishAboard(false);
                }
                break;
            case Walk.Down:
                if (me.Frame == 0 && _leftAt < 0)
                {
                    _leftAt = _zone.Tick;
                }
                if (me.Frame == 0 && Num.Vector3.Distance(me.Position, _ship.State.Position) > 34)
                {
                    _tourWalk = 0;
                    FinishAboard(true);
                }
                else if (_walkTicks > 30 * 20)
                {
                    FinishAboard(false);
                }
                break;
        }
    }

    // Flight assist's speeds toward the clearing the ship rose from, slowing as it nears (its throttle and strafe), and how
    // far off it is across the ground.
    private (ShipInput Input, float Off) BackToPad()
    {
        ShipPose pose = PoseOf(_ship.State);
        Num.Vector3 up = _zone.Frame.UpAt(_pad);
        Num.Vector3 to = _pad - _ship.Origin;
        to -= up * Num.Vector3.Dot(to, up);
        Num.Vector3 local = pose.DirectionToLocal(to);
        Num.Vector3 want = local * 0.3f;
        if (want.Length() > 12)
        {
            want *= 12 / want.Length();
        }
        ShipDef def = _ship.Def;
        // The nose back to the heading it rose with (the stick's yaw turns the nose right, the heading anticlockwise).
        float turn = Wrap(_startYaw - Facing.YawOf(_ship.State.Forward, up));
        var input = new ShipInput
        {
            Throttle = Math.Clamp(want.Z / (want.Z >= 0 ? def.ForwardSpeed : def.ReverseSpeed), -1, 1),
            Strafe = Math.Clamp(-want.X / def.LateralSpeed, -1, 1),
            Yaw = Math.Clamp(-turn * 3, -0.6f, 0.6f),
        };
        return (input, to.Length());
    }

    private void NextWalk(Walk next)
    {
        GD.Print(FormattableString.Invariant($"[tour] {_walk} {_walkTicks / 30.0:0.0} s"));
        _walk = next;
        _walkTicks = 0;
    }

    private static float Wrap(float a) => MathF.IEEERemainder(a, MathF.Tau);

    private void FinishAboard(bool done)
    {
        if (_walk == Walk.Done)
        {
            return;
        }
        _walk = Walk.Done;
        CharacterState me = _zone.Entities.Get<Character>(_player).State;
        float ground = _zone.HeightAboveGround(_zone.WorldOf(me));
        bool ok = done && _boardedAt > 0 && _leftAt > 0 && _drift <= 1f / 256 && _climbed > 30 && _turned > 20 && _fastest > 8 && MathF.Abs(ground) < 0.1f;
        GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"[tour] {(ok ? "done" : "FAILED")} in {_clock - _walkStarted:0.0} s: aboard at tick {_boardedAt}; on the deck while the ship climbed {_climbed:0.0} m, " +
            $"turned {_turned:0.0}° and went {_fastest:0.0} m/s at the most, your place on the deck moved {_drift * 1000:0.000} mm; landed and parked " +
            $"{_ship.ParkedBody != 0}; off the ramp at tick {_leftAt}, {ground:0.00} m over the ground; {_zone.Frames.Boarded} hand-overs on, {_zone.Frames.Left} off; " +
            $"the zone's step {_stepSeconds * 1000 / Math.Max(1, _steps):0.000} ms on average"));
        GetTree().Quit(ok ? 0 : 1);
    }
}
