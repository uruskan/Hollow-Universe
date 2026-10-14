using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Hollow.Core;
using Hollow.Galaxy.Systems;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using Hollow.Surface;
using HollowUniverse.App;
using HollowUniverse.Common;
using HollowUniverse.Items;
using HollowUniverse.Surface;
using Num = System.Numerics;

namespace HollowUniverse.Ships;

/// <summary>
/// The ship sandbox (<c>--view ship</c>, GROUND.md §6.7): the Tin Finch on the first world's real surface. A local zone
/// on the planet's own ground and nature (its trees stand where they are drawn) runs the same rules the server does; the
/// planet is drawn by a <see cref="SurfaceView"/> driven in the zone's frame, as the ground game drives it. You start on
/// foot beside your ship, parked on the bay of a vehicle workbench at the landing site: open the hatch and board (E),
/// lift off, fly, land anywhere, step out (F).
/// <para>
/// In the seat: the mouse is the stick; W/S the throttle lever, X lever to zero; A/D sideways; Space/C rise and sink;
/// Q/E roll; arrows a keyboard stick; Shift boost; Z flight assist; V cockpit or chase camera; H the hatch; L the lock;
/// J descend from orbit; F step out. Esc frees the mouse. Options (after "--"): <c>--scene
/// pad|hover|forest|cockpit|slope|ceiling</c> (screenshots hold the scene's camera), <c>--time</c>, <c>--trace</c>.
/// </para>
/// </summary>
public partial class ShipSandbox : Node3D
{
    private const string LookDevTime = "2026-10-12T03:00:00Z";
    private const float MouseLook = 0.0022f;
    private const float MaxPitch = 85f * Mathf.Pi / 180f;
    private const int MaxTicksPerFrame = 6;

    private GameContent _content = null!;
    private PlanetSurface _planet = null!;
    private Zone _zone = null!;
    private SurfaceView _surface = null!;
    private Camera3D _camera = null!;
    private Ship _ship = null!;
    private ShipView _shipView = null!;
    private Deployable _bench = null!;
    private Node3D? _benchModel;
    private Entity _player;
    private CanvasLayer _layer = null!;
    private ShipHud _hud = null!;
    private readonly ShipControls _controls = new();
    private readonly ShipCamera _shipCamera = new();
    private readonly List<InteractOption> _options = new();

    // The ship's parts as the interact key names them (GROUND_SYSTEMS.md §5).
    private TargetRef Hatch => new(TargetFamily.Ship, _ship.Id.Value, ShipParts.Hatch);

    private TargetRef FuelCap => new(TargetFamily.Ship, _ship.Id.Value, ShipParts.FuelCap);

    private Num.Vector3 _pad;
    private float _padYaw;
    private ShipState _shipBefore, _shipNow;
    private Num.Vector3 _feetBefore, _feetNow;
    private double _accumulator;
    private float _yaw, _pitch;
    private bool _jumpPressed;
    private bool _shot;
    private bool _paused;
    private string _scene = "pad";
    private (Num.Vector3 Eye, Num.Vector3 At)? _observer;
    private int _frames;
    private string? _notice;
    private bool _touring;
    private double _noticeUntil;
    private double _clock;
    private double _traceTimer;
    private double _respawnAt = -1;
    private double _tickMs;

    public Dictionary<string, string> Options { get; set; } = new();

    /// <summary>For automated screenshots: the scene set up, the ground, plants and grass around the camera streamed.</summary>
    public bool ReadyForShot => _frames > 30 && _surface.IsReady;

    private bool Seated => _zone.Ships().SeatOf(_player) == _ship;

    public override void _Ready()
    {
        _shot = Options.ContainsKey("shot");
        _touring = Options.ContainsKey("tour");
        _scene = Options.TryGetValue("scene", out string? scene) ? scene : "pad";
        ZoneSpec? spec = ZoneSpec.FirstWorld(GalaxyService.Galaxy, FirstWorld.LandingLatitude, FirstWorld.LandingLongitude);
        if (spec == null || PlanetSurfaces.Find(spec.SystemAddress, spec.BodyPath) is not { } found)
        {
            GD.PushError("--view ship: the first world is not in this universe.");
            return;
        }
        _content = GameContent.Load(ProjectSettings.GlobalizePath("res://data/ground"));
        _zone = new Zone(spec, content: _content);
        _planet = found.Surface;

        // The planet: the surface view driven in the zone's frame (render coordinates are the zone's), as the ground game
        // drives it: it draws around the camera this sandbox places.
        _surface = new SurfaceView { Name = "Surface", Driven = true };
        AddChild(_surface);
        // The play clock (the planet turning 24 times faster, as the server keeps it): --time, or the moment after the
        // look-dev date when it is --hour o'clock at the landing site (by default half past nine in the morning).
        double time = Options.TryGetValue("time", out string? t) ? ParseTime(t)
            : PlayTime(found, spec.RotationTimeScale, ParseTime(LookDevTime), Options.TryGetValue("hour", out string? h) ? Parse(h) : 9.5);
        _surface.OpenZone(found.System, found.Body, found.Surface, _zone.Frame.Origin, time, spec.RotationTimeScale);
        if (_shot)
        {
            _surface.SetTime(time, paused: true); // screenshots never wait for the sun
        }
        _camera = _surface.Camera;
        _camera.Fov = 70;
        _camera.MakeCurrent();

        _layer = new CanvasLayer { Name = "Interface" };
        AddChild(_layer);
        _hud = new ShipHud { Name = "ShipHud" };
        _layer.AddChild(_hud);

        BuildPad();
        SetScene(_scene);
        if (!_shot && !_touring && !OS.HasFeature("headless"))
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    // ---------------------------------------------------------------- set-up

    // The flattest open spot near the landing site: the workbench, its bay and the ship parked on it, and the pilot.
    private void BuildPad()
    {
        _padYaw = -MathF.PI / 2; // the nose east: in the morning the sun lights the hatch side
        ShipDef finch = _content.Ship("tin-finch") ?? _content.Ships[0];
        _pad = FindSite(Num.Vector3.Zero, 90, 6, s => s.Spread < 0.6f && s.Trees < 0.004 ? -s.Spread - (float)s.Trees * 200 - s.Distance * 0.002f : float.MinValue,
            fallback: Num.Vector3.Zero, accept: g => _zone.Ships().ShipSpaceClear(finch, g, _padYaw));
        Num.Vector3 up = _zone.Frame.UpAt(_pad);
        (Num.Vector3 forward, Num.Vector3 right) = CharacterMotor.HorizontalBasis(_padYaw, up);
        DeployableDef benchDef = _content.Deployable("vehicle-workbench")!;
        float bay = benchDef.Bay?.At.Z ?? 4.6f;
        Num.Vector3 benchAt = _zone.Settle(_pad - forward * bay);
        Entity player = AddPlayer(_pad + right * 4.5f - forward * 0.5f);
        EntityId owner = _zone.Players().PlayerOf(player)!.Id;
        _bench = new Deployable(_zone.NextId(), benchDef, benchAt, _zone.Frame.UpAt(benchAt), _padYaw, owner);
        _zone.Things().AddStatic(_bench);
        ShipDef def = _content.Ship("tin-finch") ?? _content.Ships[0];
        _ship = _zone.Ships().SpawnShip(def, _pad, _padYaw, owner, def.FuelCapacity);
        _shipView = new ShipView(def);
        AddChild(_shipView);
        _benchModel = ModelCatalog.Instantiate("vehicle-workbench");
        if (_benchModel != null)
        {
            AddChild(_benchModel);
        }
        Step(45); // settle on the skids and sleep
    }

    private Entity AddPlayer(Num.Vector3 near)
    {
        Num.Vector3 ground = _zone.Settle(near);
        _player = _zone.Players().AddPlayer(_zone.NextId(), "Pilot", _zone.NewCharacter(ground + _zone.Frame.UpAt(ground) * 0.05f));
        Player p = _zone.Players().PlayerOf(_player)!;
        p.Inventory.Give(_content, _content.ItemByKey("lamp-oil"), 200);
        _feetBefore = _feetNow = _zone.Entities.Get<Character>(_player).State.Position;
        return _player;
    }

    /// <summary>A candidate spot: its ground, the height spread over a ship's footprint, the slope there and over the footprint (degrees), the planet's tree density, the distance from the search's centre.</summary>
    private readonly record struct Site(Num.Vector3 Ground, float Spread, float Slope, float Footprint, double Trees, float Distance);

    /// <summary>
    /// The best-scoring point on a grid around <paramref name="around"/> (radius and step in metres): each is scored by the
    /// height spread over a ship's footprint (8 m across), the slope there (degrees) and the planet's tree density.
    /// </summary>
    private Num.Vector3 FindSite(Num.Vector3 around, float radius, float step, Func<Site, float> score, Num.Vector3 fallback,
        Func<Num.Vector3, bool>? accept = null)
    {
        int n = (int)(radius / step);
        var candidates = new List<(float Score, Num.Vector3 Ground)>();
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        for (int i = -n; i <= n; i++)
        {
            for (int j = -n; j <= n; j++)
            {
                float x = around.X + i * step, zc = around.Z + j * step;
                Num.Vector3 g = _zone.GroundAt(x, zc);
                float lo = g.Y, hi = g.Y;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * MathF.PI / 4;
                    float y = _zone.GroundAt(x + 4 * MathF.Cos(a), zc + 4 * MathF.Sin(a)).Y;
                    lo = MathF.Min(lo, y);
                    hi = MathF.Max(hi, y);
                }
                (_, float slope) = _zone.GroundNormal(g);
                Vector3D dir = _zone.Frame.ToBody(g).Normalized;
                SurfaceSample s = _planet.Sample(dir);
                if (_planet.SeaRadius > 0 && s.Height < 1)
                {
                    continue;
                }
                ClimateSample climate = _planet.Climate(dir, s);
                _planet.BiomeWeights(dir, s, climate, biomes);
                double trees = _planet.Vegetation(dir, s, climate, biomes, Math.Sin(slope)).Trees;
                float footprint = MathF.Atan((hi - lo) / 8) * 180 / MathF.PI;
                float value = score(new Site(g, hi - lo, slope * 180 / MathF.PI, footprint, trees, new Num.Vector2(i * step, j * step).Length()));
                if (value > float.MinValue)
                {
                    candidates.Add((value, g));
                }
            }
        }
        // The best that is also acceptable (nothing of the planet's own standing there, say).
        foreach ((float _, Num.Vector3 ground) in candidates.OrderByDescending(c => c.Score).Take(60))
        {
            if (accept == null || accept(ground))
            {
                return ground;
            }
        }
        return fallback;
    }

    // ---------------------------------------------------------------- scenes

    private void SetScene(string scene)
    {
        Num.Vector3 up = _zone.Frame.UpAt(_pad);
        (Num.Vector3 forward, Num.Vector3 right) = CharacterMotor.HorizontalBasis(_padYaw, up);
        ShipDef def = _ship.Def;
        switch (scene)
        {
            case "hover":
            {
                // Hovering 8 m over the pad under flight assist: the lift nozzles carry it.
                Board();
                Teleport(_pad + up * (8 + def.CentreOfMass.Y), _padYaw, Num.Vector3.Zero);
                Step(75);
                Num.Vector3 centre = _ship.State.Position;
                _observer = (_zone.Settle(_pad + right * 13 + forward * 10) + up * 1.6f, centre - up * 1.2f);
                break;
            }
            case "forest":
            case "cockpit":
            {
                // Over the forest: the densest trees within 3 km, flown toward at 60–70 m.
                Num.Vector3 forest = FindSite(Num.Vector3.Zero, 3000, 150,
                    s => (float)s.Trees - (s.Slope > 30 ? 1 : 0) - s.Distance * 2e-6f, fallback: new Num.Vector3(0, 0, -1500));
                bool cockpit = scene == "cockpit";
                Num.Vector3 heading = Num.Vector3.Normalize(new Num.Vector3(forest.X, 0, forest.Z) + new Num.Vector3(1e-3f, 0, 0));
                Num.Vector3 start = forest - heading * (cockpit ? 260 : 330);
                float yaw = MathF.Atan2(-heading.X, -heading.Z);
                float height = cockpit ? 42 : 65;
                float speed = cockpit ? 55 : 95;
                Board();
                Num.Vector3 ground = _zone.Settle(start);
                Teleport(ground + _zone.Frame.UpAt(ground) * height, yaw, heading * speed);
                _controls.Throttle = speed / def.ForwardSpeed;
                Fly(new ShipInput { Throttle = _controls.Throttle }, 45);
                _shipCamera.Mode = cockpit ? ShipCameraMode.Cockpit : ShipCameraMode.Chase;
                break;
            }
            case "slope":
            {
                // Landed on a hillside: an even slope near 19° (the Tin Finch lands on up to 25°), open if it can be,
                // within 2 km; set down on it under flight assist (down held until the skids touch, then let go).
                Num.Vector3 hill = FindSite(Num.Vector3.Zero, 2000, 40,
                    s => s.Slope is > 14 and < 24 && MathF.Abs(s.Footprint - s.Slope) < 4
                        ? -MathF.Abs(s.Slope - 19) - (float)s.Trees * 300 - s.Distance * 0.001f : float.MinValue,
                    fallback: _pad, accept: g => _zone.Ships().ShipSpaceClear(def, g, 0));
                Num.Vector3 hillUp = _zone.Frame.UpAt(hill);
                (Num.Vector3 normal, float slope) = _zone.GroundNormal(hill);
                Num.Vector3 downhill = normal - hillUp * Num.Vector3.Dot(normal, hillUp);
                downhill = downhill.LengthSquared() > 1e-6f ? Num.Vector3.Normalize(downhill) : forward;
                // Nose across the slope, so the tilt shows from the side.
                Num.Vector3 across = Num.Vector3.Cross(hillUp, downhill);
                float yaw = MathF.Atan2(-across.X, -across.Z);
                Board();
                Teleport(hill + hillUp * (6 + def.CentreOfMass.Y), yaw, Num.Vector3.Zero);
                Fly(new ShipInput { Vertical = -1 }, 30 * 15, untilLanded: true);
                Num.Vector3 centre = _ship.State.Position;
                // From ahead of the nose and a little downhill: the slope falls across the picture and the ship leans with it.
                Num.Vector3 eyeGround = _zone.Settle(centre + across * 8 + downhill * 3.5f);
                _observer = (eyeGround + hillUp * 2.4f, centre - hillUp * 0.5f);
                float tilt = MathF.Acos(Math.Clamp(Num.Vector3.Dot(_ship.State.Up, hillUp), -1, 1)) * 180 / MathF.PI;
                GD.Print(FormattableString.Invariant(
                    $"[ship] slope scene: ground {slope * 180 / MathF.PI:0.0}° at {hill.X:0},{hill.Z:0}; the ship leans {tilt:0.0}°, {_ship.State.Velocity.Length():0.000} m/s, {_ship.State.Flags}"));
                break;
            }
            case "ceiling":
            {
                // High up, climbing for the ceiling: 600 m short of orbit.
                Board();
                float height = def.Ceiling - 600;
                Num.Vector3 at = up * (float)(height - (_zone.Frame.ToBody(_pad).Length - _planet.Radius)) + _pad;
                float yaw = _padYaw + 0.6f;
                (Num.Vector3 ahead, _) = CharacterMotor.HorizontalBasis(yaw, up);
                Teleport(at, yaw, up * 20 + ahead * 60);
                Fly(new ShipInput { Throttle = 0.4f, Vertical = 0.8f }, 45);
                _controls.Throttle = 0.4f;
                // From above and behind, looking down past the ship at the land 14 km below.
                _shipCamera.Height = 9;
                _shipCamera.Distance = 13;
                _shipCamera.LookAhead = -2;
                break;
            }
            default:
            {
                // On foot by the ship on its pad, the hatch open, the workbench behind it.
                _ship.HatchOpen = true;
                Step(Ship.HatchTicks + 2);
                Num.Vector3 eye = _zone.Settle(_pad + forward * 6.5f + right * 6f) + up * 2.3f;
                _observer = (eye, _pad + up * 1.0f - forward * 1.6f);
                LookFrom(eye, _observer.Value.At);
                break;
            }
        }
        _paused = _shot;
        _shipBefore = _shipNow = _ship.State;
        _feetBefore = _feetNow = _zone.Entities.Get<Character>(_player).State.Position;
        _shipCamera.Reset();
    }

    private void Board()
    {
        Ship ship = _ship;
        Num.Vector3 hatch = ship.PointOf(ship.Def.Hatch);
        ref Character c = ref _zone.Entities.Get<Character>(_player);
        Num.Vector3 outward = Num.Vector3.Transform(-Num.Vector3.UnitX, ship.State.Rotation);
        c.State.Position = _zone.Settle(hatch + outward * 1.2f) + _zone.Frame.UpAt(hatch) * 0.05f;
        ship.HatchOpen = true;
        ship.HatchProgress = Ship.HatchTicks;
        Outcome result = _zone.Interaction().Interact(_player, new TargetRef(TargetFamily.Ship, ship.Id.Value, ShipParts.Hatch), "board");
        if (result != OutcomeCode.Done)
        {
            GD.PushWarning($"[ship] could not board for the scene: {result}");
        }
        _controls.Reset();
    }

    private void Teleport(Num.Vector3 centre, float yaw, Num.Vector3 velocity)
    {
        Num.Vector3 up = _zone.Frame.UpAt(centre);
        ref ShipState s = ref _ship.State;
        s.Position = centre;
        s.Rotation = ShipMotor.Orientation(up, yaw);
        s.Velocity = velocity;
        s.AngularVelocity = Num.Vector3.Zero;
        s.RestTicks = 0;
        s.Flags &= ~(ShipFlags.Landed | ShipFlags.Touching);
        _zone.Physics.EnsureGround(_zone.Settle(centre), 1);
    }

    // Steps the zone with the pilot holding an input for some ticks, or (landing) until the skids touch, letting go then,
    // and on until the ship has landed and settled.
    private void Fly(ShipInput input, int ticks, bool untilLanded = false)
    {
        for (int i = 0; i < ticks; i++)
        {
            ShipInput tick = untilLanded && _ship.State.Has(ShipFlags.Touching) ? default : input;
            _ship.Input = tick;
            Step(1);
            if (untilLanded && _ship.State.Has(ShipFlags.Landed) && _ship.State.Velocity.Length() < 0.05f)
            {
                Step(10);
                break;
            }
        }
    }

    private void Step(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            _zone.Step();
            _zone.Ships().ShipEvents.Clear();
            _zone.GameEvents.Clear();
        }
    }

    private void LookFrom(Num.Vector3 eye, Num.Vector3 at)
    {
        Num.Vector3 up = _zone.Frame.UpAt(eye);
        Num.Vector3 d = at - eye;
        Num.Vector3 flat = d - up * Num.Vector3.Dot(d, up);
        _yaw = MathF.Atan2(-flat.X, -flat.Z);
        _pitch = MathF.Atan2(Num.Vector3.Dot(d, up), flat.Length());
    }

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        if (_zone == null)
        {
            return;
        }
        _frames++;
        _clock += delta;
        if (_touring)
        {
            Tour();
        }
        if (!_paused)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            Tick(delta);
            _tickMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        float alpha = _paused ? 1 : (float)(_accumulator / CharacterMotor.DeltaTime);
        ShipState shown = Interpolate(_shipBefore, _shipNow, alpha);

        // The camera (the surface view streams around it in its own process step), then what stands in the world.
        PlaceCamera(shown, (float)delta, alpha);
        ShipView.Exposure = _surface.Sky?.Exposure ?? 1;
        if (_zone.Ships().All.Contains(_ship))
        {
            _shipView.Visible = true;
            _shipView.FromCockpit = Seated && _shipCamera.Mode == ShipCameraMode.Cockpit && _observer == null;
            _shipView.Show(new ShipLook(Pivot(shown), _ship.HatchProgress / (float)Ship.HatchTicks, shown.Thrust,
                shown.Has(ShipFlags.EnginesOn), shown.Has(ShipFlags.Boosting), _ship.Piloted, _ship.Destroyed), delta);
        }
        else
        {
            _shipView.Visible = false;
        }
        if (_benchModel != null)
        {
            (Num.Vector3 f, _) = CharacterMotor.HorizontalBasis(_bench.Yaw, _bench.Up);
            _benchModel.GlobalTransform = new Transform3D(Basis.LookingAt(Dir(-f), Dir(_bench.Up)), R(_bench.Position));
        }
        FillHud(shown);
        Trace(delta, shown);
    }

    private void Tick(double delta)
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
                _ship.Input = Input.MouseMode == Input.MouseModeEnum.Captured || _shot || _touring ? _controls.Read(CharacterMotor.DeltaTime) : default;
            }
            else
            {
                _zone.Entities.Get<Character>(_player).Input = ReadWalk();
                _jumpPressed = false;
            }
            _shipBefore = _ship.State;
            _feetBefore = _zone.Entities.Get<Character>(_player).State.Position;
            _zone.Step();
            if (_touring)
            {
                TourStep();
            }
            Events();
            _shipNow = _ship.State;
            _feetNow = _zone.Entities.Get<Character>(_player).State.Position;
        }
        Revive();
    }

    // What the zone said this tick: crashes, orbit, the ship's end.
    private void Events()
    {
        foreach (ShipEvent e in _zone.Ships().ShipEvents)
        {
            if (e.Ship != _ship)
            {
                continue;
            }
            switch (e.Kind)
            {
                case ShipEventKind.Crashed:
                    Notice(FormattableString.Invariant($"Hit the ground at {_ship.Events.ImpactSpeed:0} m/s: {e.Amount:0} hull lost"));
                    break;
                case ShipEventKind.Orbit when e.Amount > 0:
                    Notice("Orbit reached: parked over this spot. J to descend.");
                    break;
                case ShipEventKind.Destroyed:
                    Notice("The Tin Finch is destroyed.");
                    break;
            }
        }
        _zone.Ships().ShipEvents.Clear();
        _zone.GameEvents.Clear();
    }

    // A ship destroyed or a pilot dead: a new ship on the pad and the pilot beside it, after a moment.
    private void Revive()
    {
        bool gone = !_zone.Ships().All.Contains(_ship);
        bool dead = _zone.Players().PlayerOf(_player)?.Dead ?? true;
        if (!gone && !dead)
        {
            return;
        }
        if (_respawnAt < 0)
        {
            _respawnAt = _clock + 3;
            return;
        }
        if (_clock < _respawnAt)
        {
            return;
        }
        _respawnAt = -1;
        if (dead)
        {
            AddPlayer(_pad + new Num.Vector3(0, 0, 6));
        }
        if (gone)
        {
            ShipDef def = _ship.Def;
            _ship = _zone.Ships().SpawnShip(def, _pad, _padYaw, _zone.Players().PlayerOf(_player)!.Id, def.FuelCapacity);
        }
        _shipBefore = _shipNow = _ship.State;
        _feetBefore = _feetNow = _zone.Entities.Get<Character>(_player).State.Position;
    }

    private MoveInput ReadWalk()
    {
        var input = new MoveInput { Yaw = _yaw, Pitch = _pitch };
        if (_shot || (!_touring && Input.MouseMode != Input.MouseModeEnum.Captured))
        {
            return input;
        }
        input.Forward = (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0);
        input.Right = (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0);
        if (Input.IsPhysicalKeyPressed(Key.Shift))
        {
            input.Buttons |= MoveButtons.Sprint;
        }
        if (Input.IsPhysicalKeyPressed(Key.Ctrl))
        {
            input.Buttons |= MoveButtons.Crouch;
        }
        if (Input.IsPhysicalKeyPressed(Key.Space))
        {
            input.Buttons |= MoveButtons.Jump;
        }
        if (_jumpPressed)
        {
            input.Buttons |= MoveButtons.JumpPressed;
        }
        return input;
    }

    private static ShipState Interpolate(in ShipState a, in ShipState b, float t)
    {
        ShipState s = b;
        if (Num.Vector3.DistanceSquared(a.Position, b.Position) < 400 * 400)
        {
            s.Position = Num.Vector3.Lerp(a.Position, b.Position, t);
            s.Rotation = Num.Quaternion.Slerp(a.Rotation, b.Rotation, t);
        }
        return s;
    }

    private Transform3D Pivot(in ShipState s)
    {
        Num.Quaternion q = s.Rotation;
        return new Transform3D(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)), R(s.Origin(_ship.Def.CentreOfMass)));
    }

    private void PlaceCamera(in ShipState shown, float delta, float alpha)
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
            Num.Vector3 worldUp = _zone.Frame.UpAt(shown.Position);
            (eye, forward, up) = _shipCamera.Follow(shown, _ship.Def, worldUp, delta, p => _zone.HeightAboveGround(p));
            _camera.Fov = _shipCamera.Mode == ShipCameraMode.Cockpit ? 74 : 68;
        }
        else
        {
            Num.Vector3 feet = Num.Vector3.Lerp(_feetBefore, _feetNow, alpha);
            up = _zone.Frame.UpAt(feet);
            CharacterState state = _zone.Entities.Get<Character>(_player).State;
            eye = feet + up * _zone.Tuning.EyeHeight(state.Stance);
            forward = CharacterMotor.ViewDirection(_yaw, _pitch, up);
            _camera.Fov = 70;
        }
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(Dir(forward), Dir(up)), R(eye));
    }

    // ---------------------------------------------------------------- the HUD

    private void FillHud(in ShipState shown)
    {
        ShipHudData d = _hud.Data;
        bool flying = Seated && !(_shot && _observer != null);
        _hud.Visible = !(_shot && _observer != null);
        d.Notice = _clock < _noticeUntil ? _notice : null;
        if (!flying)
        {
            d.Def = null;
            d.Prompt = Seated ? null : FootPrompt();
            d.Hints = "WASD walk · E hatch and board · L lock · R refuel · Esc mouse";
            _hud.Refresh();
            return;
        }
        ShipDef def = _ship.Def;
        ShipState s = _ship.State;
        s.Position = shown.Position;
        s.Rotation = shown.Rotation;
        d.Def = def;
        d.State = s;
        Num.Vector3 up = _zone.Frame.UpAt(s.Position);
        d.Speed = s.Velocity.Length();
        d.VerticalSpeed = Num.Vector3.Dot(s.Velocity, up);
        Vector3D body = _zone.Frame.ToBody(s.Origin(def.CentreOfMass));
        double r = body.Length;
        double ground = _planet.HeightAt(body / r);
        if (_planet.SeaRadius > 0)
        {
            ground = Math.Max(ground, 0);
        }
        d.Height = (float)(_zone.Frame.ToBody(s.Position).Length - _planet.Radius);
        d.OverGround = (float)(r - _planet.Radius - ground);
        Num.Vector3 f = s.Forward;
        d.Heading = MathF.Atan2(f.X, -f.Z) * 180 / MathF.PI;
        d.Throttle = _controls.Throttle;
        d.Stick = _controls.Stick;
        d.AssistOff = _controls.AssistOff;
        d.TimeToGround = d.VerticalSpeed < -0.5f ? d.OverGround / -d.VerticalSpeed : float.PositiveInfinity;
        d.Prompt = s.Has(ShipFlags.InOrbit) ? "J  DESCEND OVER THE SAME SPOT"
            : s.Has(ShipFlags.Landed) || s.Asleep ? "F  STEP OUT     H  HATCH" : null;
        d.Hints = $"V {(_shipCamera.Mode == ShipCameraMode.Cockpit ? "chase" : "cockpit")} camera · Z flight assist · X throttle 0 · Shift boost";
        _hud.Refresh();
    }

    // What a tap of E does at the hatch, within its reach (the interaction's own rules, the zone being local here).
    private string? FootPrompt()
    {
        if (!_zone.Ships().All.Contains(_ship) || _ship.Destroyed || !_zone.Interaction().Options(_player, Hatch, _options))
        {
            return null;
        }
        Num.Vector3 eye = _zone.EyeOf(_zone.Entities.Get<Character>(_player).State);
        if (!_zone.Targets().Reach(eye, Hatch, _zone.Content!.Survival.InteractReach).Ok || InteractionRules.Primary(_options) is not (>= 0 and var primary))
        {
            return null;
        }
        InteractOption tap = _options[primary];
        return tap.Enabled ? $"E  {_zone.Content.Interactions.Verb(tap.Verb)!.Label.ToUpperInvariant()}" : _zone.Content.Texts.Line(tap.Reason).ToUpperInvariant();
    }

    // A tap of E at the hatch: its primary option (open the hatch, or board).
    private Outcome TapHatch() =>
        _zone.Interaction().Options(_player, Hatch, _options) && InteractionRules.Primary(_options) is >= 0 and var primary
            ? _zone.Interaction().Interact(_player, Hatch, _options[primary].Verb)
            : OutcomeCode.NotUsable;

    private void Notice(string text)
    {
        _notice = text;
        _noticeUntil = _clock + 3;
    }

    private void Trace(double delta, in ShipState s)
    {
        if (!Options.ContainsKey("trace"))
        {
            return;
        }
        _traceTimer += delta;
        if (_traceTimer < 1)
        {
            return;
        }
        _traceTimer = 0;
        float over = _zone.HeightAboveGround(s.Origin(_ship.Def.CentreOfMass));
        Num.Vector3 up = _zone.Frame.UpAt(s.Position);
        float pitch = MathF.Asin(Math.Clamp(Num.Vector3.Dot(s.Forward, up), -1, 1)) * 180 / MathF.PI;
        float roll = MathF.Asin(Math.Clamp(Num.Vector3.Dot(s.Right, up), -1, 1)) * 180 / MathF.PI;
        GD.Print(FormattableString.Invariant(
            $"[trace] t={_clock:0.0}s frame={delta * 1000:0.0}ms fps={Engine.GetFramesPerSecond()} ticks={_tickMs:0.00}ms ship {s.Velocity.Length():0.0} m/s climb {Num.Vector3.Dot(s.Velocity, up):0.0} {over:0.0} m over ground pitch {pitch:0.0}° roll {roll:0.0}° thrust ({s.Thrust.X:0.00},{s.Thrust.Y:0.00},{s.Thrust.Z:0.00}) fuel {s.Fuel:0} hull {s.Health:0} {s.Flags} tiles={_surface.Terrain.DrawnTiles} pending={_surface.Terrain.Pending} ready={_surface.IsReady} exposure={ShipView.Exposure:0.###}"));
    }

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent e)
    {
        if (_zone == null || _shot)
        {
            return;
        }
        switch (e)
        {
            case InputEventMouseButton { Pressed: true } when Input.MouseMode != Input.MouseModeEnum.Captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                return;
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                if (Seated)
                {
                    _controls.Mouse(motion.Relative / Math.Max(Ui.Scale, 0.5f));
                }
                else
                {
                    _yaw = Mathf.Wrap(_yaw - motion.Relative.X * MouseLook, -Mathf.Pi, Mathf.Pi);
                    _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseLook, -MaxPitch, MaxPitch);
                }
                return;
            case InputEventKey { Pressed: true, Echo: false } key:
                Press(key);
                return;
        }
    }

    private void Press(InputEventKey key)
    {
        if (key.PhysicalKeycode == Godot.Key.Escape)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            return;
        }
        if (Seated)
        {
            if (_controls.Key(key))
            {
                return;
            }
            switch (key.PhysicalKeycode)
            {
                case Godot.Key.V:
                    _shipCamera.Mode = _shipCamera.Mode == ShipCameraMode.Chase ? ShipCameraMode.Cockpit : ShipCameraMode.Chase;
                    _shipCamera.Reset();
                    break;
                case Godot.Key.F:
                {
                    Outcome r = _zone.Ships().ExitShip(_player);
                    if (r == OutcomeCode.Done)
                    {
                        _controls.Reset();
                        LookFrom(_zone.EyeOf(_zone.Entities.Get<Character>(_player).State), _ship.State.Position);
                        _feetBefore = _feetNow = _zone.Entities.Get<Character>(_player).State.Position;
                    }
                    else
                    {
                        Notice(r == OutcomeCode.NotLanded ? "Land first: you can only step out of a ship at rest." : $"Can't step out: {r}");
                    }
                    break;
                }
                case Godot.Key.H:
                    Refused(_zone.Ships().ShipHatch(_player, _ship.Id), "The hatch opens only when the ship is at rest.");
                    break;
                case Godot.Key.L:
                    Refused(_zone.Ships().LockShip(_player, _ship.Id), "Only the owner locks it.");
                    Notice(_ship.Locked ? "Locked" : "Unlocked");
                    break;
                case Godot.Key.J:
                    Refused(_zone.Ships().Descend(_player), "Only from orbit.");
                    _shipBefore = _shipNow = _ship.State;
                    break;
            }
            return;
        }
        switch (key.PhysicalKeycode)
        {
            case Godot.Key.Space:
                _jumpPressed = true;
                break;
            case Godot.Key.E:
            {
                Outcome r = TapHatch();
                if (r == OutcomeCode.Done && _zone.Ships().SeatOf(_player) == _ship)
                {
                    _controls.Reset();
                    _shipCamera.Reset();
                }
                Refused(r, "");
                break;
            }
            case Godot.Key.G:
                Refused(_zone.Interaction().Interact(_player, Hatch, "hatch-close"), "");
                break;
            case Godot.Key.L:
                Refused(_zone.Interaction().Interact(_player, Hatch, _ship.Locked ? "ship-unlock" : "ship-lock"), "");
                Notice(_ship.Locked ? "Locked" : "Unlocked");
                break;
            case Godot.Key.R:
                Refused(_zone.Interaction().Interact(_player, FuelCap, "refuel"), "");
                break;
        }
    }

    private void Refused(Outcome result, string why)
    {
        if (result == OutcomeCode.Done)
        {
            return;
        }
        Notice(why.Length > 0 && (result == OutcomeCode.NotLanded || result == OutcomeCode.NotAllowed) ? why : _content.Texts.Line(result));
    }

    // ---------------------------------------------------------------- frames

    /// <summary>A zone-frame point in render space (the driven surface view draws in the zone's frame).</summary>
    private static Vector3 R(Num.Vector3 zone) => new(zone.X, zone.Y, zone.Z);

    /// <summary>A zone-frame direction in render space.</summary>
    private static Vector3 Dir(Num.Vector3 v) => new(v.X, v.Y, v.Z);

    private static double ParseTime(string text) => UniverseTime.SecondsSinceJ2000(
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));

    private static double Parse(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>The first moment from <paramref name="from"/> when it is <paramref name="hour"/> o'clock, local solar time, at the landing site on the play clock.</summary>
    private static double PlayTime(PlanetSurfaces.Entry planet, double rotationTimeScale, double from, double hour)
    {
        var sky = new SkyEphemeris(planet.System, planet.Body);
        sky.SetRotationTimeScale(rotationTimeScale, 0);
        Vector3D site = ZoneFrame.Direction(FirstWorld.LandingLatitude * Math.PI / 180, FirstWorld.LandingLongitude * Math.PI / 180);
        double best = from, error = double.MaxValue;
        for (double s = 0; s < 6 * 3600; s += 20)
        {
            double h = sky.LocalSolarHours(site, sky.Sun.Index, from + s);
            double e = Math.Abs(((h - hour) % 24 + 36) % 24 - 12);
            if (e < error)
            {
                error = e;
                best = from + s;
            }
        }
        return best;
    }
}
