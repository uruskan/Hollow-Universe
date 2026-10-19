using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Placement;
using HollowUniverse.App;
using HollowUniverse.Ground;
using HollowUniverse.UI;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Build;

/// <summary>
/// The build sandbox (<c>--view build</c>, GROUND.md §6.3): a local zone on the first world's stand-in surface with one
/// walking character, the building rules and their art, and the ground interface's belt and radials. Everything the
/// player sees comes from the same rules code the server runs: the ghost is the placement pipeline's verdict
/// (GROUND_SYSTEMS.md §6, <see cref="Placer"/>), on a placement world of this building alone, drawn by the one
/// <see cref="PlacementGhost"/>; what a click places, the placeable's kind puts in through this sandbox's site.
/// <para>
/// Keys: 1 plan, 2 hammer, 3–5 doors, 6 claim device; hold the right mouse button for the plan's or the hammer's radial;
/// left button places, repairs, fits a door or places the device; R turns the piece (or flips the door); E opens and
/// shuts a door; F2 the stability overlay; F5 one hour of upkeep and decay. Options (after "--"): <c>--scene
/// house|stability|ghost-valid|ghost-invalid|stress|empty|joints|doors</c> (joints: <c>--tour folder</c>), <c>--overlay</c>,
/// <c>--tool</c> 1–6, <c>--piece key</c>.
/// </para>
/// </summary>
public partial class BuildSandbox : Node3D, IHammerActions, IPlacementSite
{
    private const ulong Player = 1;
    private const float MouseSensitivity = 0.002f;
    private const float MaxPitch = 85f * Mathf.Pi / 180f;
    private const int MeasureFrames = 300;

    private BuildingData _data = null!;
    private Texts _texts = null!;
    private Zone _zone = null!;
    private BuildingWorld _world = null!;
    private Func<EntityId> _nextBuildId = null!;
    private BuildingAssets _assets = null!;
    private BuildingRenderer _renderer = null!;
    private PlacementGhost _ghost = null!;
    private PlacementWorld _placement = null!;
    private GameContent _content = null!;
    private PlacementCandidate? _candidate;
    private BuildHud _hud = null!;
    private GroundUi _ui = null!;
    private BuildSandboxUi _model = null!;
    private GroundTerrain _terrain = null!;
    private Camera3D _camera = null!;
    private CanvasLayer _interface = null!;
    private Entity _player;
    private readonly Wallet _wallet = new() { ["wood"] = 20_000, ["stone"] = 20_000, ["metal-fragments"] = 10_000, ["refined-metal"] = 1_000 };

    private Num.Vector3 _feetBefore, _feetNow;
    private double _accumulator;
    private float _yaw, _pitch;
    private bool _jumpPressed;
    private double _clock;
    private int _turns, _tool;
    private object? _target;
    private RayHit _targetHit;
    private bool _shot;
    private int _frames;

    private Transform3D? _fixedCamera;
    private (Vector3 Eye, Vector3 At, Vector3 Up)? _view;
    private Num.Vector3 _site;
    private bool _measure;
    private int _measured;
    private double _frameSum, _frameWorst;
    private string? _scene;

    public Dictionary<string, string> Options { get; set; } = new();

    /// <summary>For automated screenshots: the ground drawn, the scene built and (for the stress scene) measured.</summary>
    public bool ReadyForShot => _frames > 40 && _terrain.IsSettled && (!_measure || _measured >= MeasureFrames);

    public override void _Ready()
    {
        _shot = Options.ContainsKey("shot") || Options.ContainsKey("tour");
        // The whole game data: the building's numbers, and the player's lines for its answers (texts.json).
        GameContent content = GameContent.Load(ProjectSettings.GlobalizePath("res://data/ground"));
        _content = content;
        _data = content.Building;
        _texts = content.Texts;
        ZoneSpec spec = ZoneSpec.FirstWorld(GalaxyService.Galaxy, 28.4, -61.7) ?? new ZoneSpec
        {
            Radius = 6_435_000,
            SurfaceGravity = 0.92,
            SurfaceSeed = 0x5EED,
            LatitudeDegrees = 28.4,
            LongitudeDegrees = -61.7,
        };
        // The first world's historical stand-in radius is a diagnostic one now (docs/WHOLE_WORLD.md): a viewer asks for it as such.
        _zone = new Zone(spec.CreateSurface(diagnostic: true), spec);
        ulong next = 0;
        _nextBuildId = () => new EntityId(++next);
        _world = new BuildingWorld(_data, _zone.Physics, _nextBuildId) { Characters = Capsules };
        _placement = new PlacementWorld(content, _zone.Frame, _zone.Surface, _zone.Physics, _world, new Surroundings(this));

        BuildEnvironment();
        _terrain = new GroundTerrain { Name = "Terrain" };
        AddChild(_terrain);
        _assets = BuildingAssets.Load(_data);
        _renderer = new BuildingRenderer
        {
            Name = "Buildings",
            FinestOnly = Options.ContainsKey("finest"),
            OnlyLod = Options.TryGetValue("lod", out string? lodOption) ? int.Parse(lodOption, CultureInfo.InvariantCulture) : -1,
        };
        AddChild(_renderer);
        _ghost = new PlacementGhost { Name = "Ghost", Assets = _assets };
        AddChild(_ghost);
        _camera = new Camera3D { Name = "Camera", Fov = 75, Near = 0.05f, Far = 4000, Current = true };
        AddChild(_camera);

        _model = new BuildSandboxUi(_data, _wallet, BuildingRadials.Hammer(() => _target as Piece, _data, _wallet, () => _clock, this));
        _model.Plan.Changed += () => _turns = 0;
        var layer = new CanvasLayer { Name = "Interface" };
        _interface = layer;
        AddChild(layer);
        _hud = new BuildHud { Name = "BuildHud" };
        layer.AddChild(_hud);
        _ui = new GroundUi { Name = "GroundUi", Model = _model };
        layer.AddChild(_ui);
        _ui.Greet();
        _ui.QuitRequested += () => GetTree().Quit();
        _ui.HostRequested += _ => _ui.Open(GroundScreen.Hud);
        if (_shot)
        {
            _ui.HoldPointer(new Vector2(-10000, -10000));
        }

        Num.Vector3 site = FlatSite();
        _site = site;
        _zone.Physics.EnsureGroundWithin(site, 96);
        BuildScene(site);
        _renderer.Attach(_world, _assets);
        // What goes breaks apart (GROUND.md §9.4), as the zone reports it in the game: brought down, demolished, decayed.
        HollowUniverse.Gibs.GibDirector.Install(this).Floor = Floor;
        _world.Changed += BreakApart;
        if (Options.ContainsKey("overlay"))
        {
            _renderer.StabilityOverlay = true;
        }
        if (Options.TryGetValue("tool", out string? tool) && int.TryParse(tool, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t))
        {
            _model.SelectBelt(t - 1);
        }
        if (Options.TryGetValue("piece", out string? key))
        {
            int index = _data.Wheel.ToList().FindIndex(p => p.Key == key);
            if (index >= 0)
            {
                _model.Plan.Piece = index;
            }
        }
        // --wheel plan|hammer: the held tool's radial open, the pointer on a sector (a screenshot of the hook-up).
        if (Options.TryGetValue("wheel", out string? wheel))
        {
            _model.SelectBelt(wheel == "hammer" ? 1 : 0);
            _ui.OpenRadial(_model.HeldRadial!, RadialInput.Aim);
            _ui.HoldPointer(wheel == "hammer" ? new Vector2(800, 190) : new Vector2(1025, 300));
        }
        Capture();
        if (Options.ContainsKey("build-check"))
        {
            RunCheck();
        }
        if (_scene == "joints" && Options.TryGetValue("tour", out string? tour))
        {
            JointTour(tour);
        }
        else if (_scene == "doors" && Options.TryGetValue("tour", out string? doorTour))
        {
            DoorTour(doorTour);
        }
    }


    private void BreakApart(BuildingEvent e)
    {
        if (e.Removal == Removal.Taken || BuildingWorld.Removed(e) is not { } gone)
        {
            return;
        }
        // The sandbox has no blows: what breaks here comes down, is demolished or decays (a break by damage counts as a blow).
        Hollow.Simulation.Combat.DestroyCause cause = BuildingWorld.CauseOf(e.Removal) ?? Hollow.Simulation.Combat.DestroyCause.Melee;
        Num.Vector3 up = _zone.Frame.UpAt(gone.Position);
        HollowUniverse.Gibs.GibBreak broke = HollowUniverse.Gibs.GibDirector.Break(gone.Key, gone.Grade, gone.Position, gone.Rotation, Num.Vector3.Zero,
            cause, up, (float)(_zone.Spec.SurfaceGravity * 9.80665), gone.Target.Id);
        if (broke.Shown)
        {
            Sounds.At(broke.CauseSound ?? Sounds.Item(gone.Key, "break") ?? broke.MaterialSound, broke.Centre);
        }
    }

    // The sandbox's own collision under a point (its ground and pieces), for the pieces of what broke.
    private bool Floor(Vector3 from, Vector3 down, float length, out Vector3 point, out Vector3 normal)
    {
        point = default;
        normal = Vector3.Up;
        if (_world.Physics is not { } physics || !physics.Ray(new Num.Vector3(from.X, from.Y, from.Z), new Num.Vector3(down.X, down.Y, down.Z), length, out RayHit hit))
        {
            return false;
        }
        point = new Vector3(hit.Point.X, hit.Point.Y, hit.Point.Z);
        normal = new Vector3(hit.Normal.X, hit.Normal.Y, hit.Normal.Z);
        return true;
    }

    // ---------------------------------------------------------------- scenes

    // The flattest 34 m square within 150 m of the landing site, by radial terrain height.
    private Num.Vector3 FlatSite()
    {
        float best = float.MaxValue;
        var site = Num.Vector3.Zero;
        for (int cx = -15; cx <= 15; cx++)
        {
            for (int cz = -15; cz <= 15; cz++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i <= 10; i++)
                {
                    for (int j = 0; j <= 10; j++)
                    {
                        Num.Vector3 point = _zone.GroundAt(cx * 10 + (i - 5) * 3.4f, cz * 10 + (j - 5) * 3.4f);
                        float height = (float)_zone.Frame.ToBody(point).Length;
                        lo = MathF.Min(lo, height);
                        hi = MathF.Max(hi, height);
                    }
                }
                if (hi - lo < best)
                {
                    best = hi - lo;
                    site = _zone.GroundAt(cx * 10, cz * 10);
                }
            }
        }
        return site;
    }

    private void BuildScene(Num.Vector3 site)
    {
        _scene = Options.TryGetValue("scene", out string? s) ? s : "house";
        PlacementPlan? first = _world.FoundationAt(_data.Piece("foundation")!, site, 0.35);
        Num.Vector3 siteUp = _zone.Frame.UpAt(site);
        (Num.Vector3 north, Num.Vector3 east) = Facing.Compass(siteUp);
        Num.Vector3 Offset(float x, float y, float z) => site + east * x + siteUp * y - north * z;
        Num.Vector3 spawn = Offset(2, 0.5f, 12);
        if (first == null)
        {
            AddPlayer(Offset(0, 0.5f, 6));
            return;
        }
        BuildingFrame frame = first.Frame;
        switch (_scene)
        {
            case "clan":
                ClanBase(frame);
                spawn = F(frame.ToZone(new Vector3D(0, 0.5, 11)));
                break;
            case "empty":
                // Nothing built: the first foundation's frame only measures --from and --at (a free foundation's ghost).
                spawn = Offset(0, 0.5f, 6);
                break;
            case "doors":
                // Every door in its opening along one wall (BuildSandbox.Doors.cs); --from and --at are in the first foundation's frame.
                Doors(first);
                spawn = F(frame.ToZone(new Vector3D(0, 0.5, 8)));
                break;
            case "joints":
                // Every grade's joints (BuildSandbox.Joints.cs); --from and --at are in the twig group's frame.
                Joints(frame);
                frame = _jointFrames[0];
                spawn = F(frame.ToZone(new Vector3D(-6, -0.4, -6)));
                break;
            case "stress":
            {
                int count = DemoBases.Block(_world, first);
                GD.Print($"[build] stress block: {count} pieces in {_world.Structures.Count} structure");
                Look(frame, new Vector3D(-11, 21, -11), new Vector3D(13.5, 3, 13.5));
                _measure = true;
                spawn = F(frame.ToZone(new Vector3D(-6, 0.5, -6)));
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
                break;
            }
            case "stability":
            {
                DemoBases.House house = DemoBases.TwoStorey(_world, first);
                DemoBases.Balcony(_world, house);
                _renderer.StabilityOverlay = true;
                _model.SelectBelt(1);
                Look(frame, new Vector3D(-6.6, 6.3, -6.8), new Vector3D(0, 2.95, -9.2));
                break;
            }
            case "ghost-valid":
            {
                DemoBases.TwoStorey(_world, first);
                _model.SelectBelt(0);
                _model.Plan.Piece = _data.Wheel.ToList().FindIndex(p => p.Key == "foundation");
                Look(frame, new Vector3D(-9.5, 1.8, 5.5), new Vector3D(-1.6, 0.05, 0.3));
                spawn = F(frame.ToZone(new Vector3D(-12, 0.5, 9)));
                break;
            }
            case "ghost-invalid":
            {
                DemoBases.House house = DemoBases.TwoStorey(_world, first);
                DemoBases.Balcony(_world, house);
                _model.SelectBelt(0);
                _model.Plan.Piece = _data.Wheel.ToList().FindIndex(p => p.Key == "floor");
                Look(frame, new Vector3D(5.2, 5.2, -20.5), new Vector3D(0, 2.9, -18.2));
                break;
            }
            default:
            {
                DemoBases.House house = DemoBases.TwoStorey(_world, first);
                // The double door stands open; the hammer is held and aimed at the doorway.
                if (house.F.Structure.Pieces.FirstOrDefault(p => p.Door is { Def.Opening: Opening.Double })?.Door is Door wide)
                {
                    _world.Toggle(Player, wide);
                }
                _model.SelectBelt(1);
                Look(frame, new Vector3D(-4.7, 2.9, 9.9), new Vector3D(0.75, 2.45, 4.5));
                spawn = F(frame.ToZone(new Vector3D(-12, 0.5, 6)));
                break;
            }
        }
        // --from x,y,z --at x,y,z: the camera in the scene's frame, for a chosen view.
        bool chosen = Options.TryGetValue("from", out string? from) & Options.TryGetValue("at", out string? at);
        if (chosen)
        {
            Look(frame, ParseVector(from!), ParseVector(at!));
        }
        if (_view is (Vector3 eye, Vector3 target, Vector3 up))
        {
            if (_shot || chosen)
            {
                // A screenshot holds the scene's view.
                _fixedCamera = new Transform3D(Basis.LookingAt(target - eye, up), eye);
            }
            else
            {
                // Playing: the builder stands under the scene's view, facing what it looks at.
                spawn = N(eye);
                Num.Vector3 ground = _zone.Settle(spawn);
                Num.Vector3 localUp = _zone.Frame.UpAt(ground);
                Num.Vector3 look = Num.Vector3.Normalize(N(target) - (ground + localUp * _zone.Tuning.StandingEyeHeight));
                _yaw = Facing.YawOf(look, localUp);
                _pitch = Facing.PitchOf(look, localUp);
            }
        }
        AddPlayer(spawn);
    }

    private static Vector3D ParseVector(string text)
    {
        double[] v = text.Split(',').Select(p => double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        return new Vector3D(v[0], v[1], v[2]);
    }

    private void Look(in BuildingFrame frame, Vector3D from, Vector3D at) =>
        _view = (G(frame.ToZone(from)), G(frame.ToZone(at)), G(frame.DirectionToZone(Vector3D.UnitY)));

    private void AddPlayer(Num.Vector3 near)
    {
        Num.Vector3 ground = _zone.Settle(near);
        _player = _zone.AddCharacter(new EntityId(1000), "Builder", _zone.NewCharacter(ground + _zone.Frame.UpAt(ground) * 0.05f));
        _feetBefore = _feetNow = _zone.Entities.Get<Character>(_player).State.Position;
        GD.Print($"[build] builder planet-frame position={_feetNow} up={_zone.Frame.UpAt(_feetNow)} " +
            $"groundClearance={_zone.HeightAboveGround(_feetNow):0.000}m siteDistance={Num.Vector3.Distance(_feetNow, _site):0.00}m");
    }

    private IEnumerable<Capsule> Capsules()
    {
        if (!_zone.Entities.IsAlive(_player))
        {
            yield break;
        }
        CharacterState s = _zone.Entities.Get<Character>(_player).State;
        float height = s.Stance == Stance.Crouching ? _zone.Tuning.CrouchingHeight : _zone.Tuning.StandingHeight;
        yield return new Capsule(s.Position, _zone.Frame.UpAt(s.Position), _zone.Tuning.Radius, height);
    }

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        _frames++;
        _clock += delta;
        _world.Tick(_clock);
        if (_frames % 15 == 0)
        {
            DoorLocks.Sync(_renderer);
        }
        Walk(delta);
        Num.Vector3 feet = Num.Vector3.Lerp(_feetBefore, _feetNow, (float)(_accumulator / CharacterMotor.DeltaTime));
        Num.Vector3 up = _zone.Frame.UpAt(feet);
        if (_fixedCamera is Transform3D fixedCamera)
        {
            _camera.GlobalTransform = fixedCamera;
        }
        else
        {
            CharacterState state = _zone.Entities.Get<Character>(_player).State;
            float eyeHeight = state.Stance == Stance.Crouching ? _zone.Tuning.CrouchingEyeHeight : _zone.Tuning.StandingEyeHeight;
            Num.Vector3 eye = feet + up * eyeHeight;
            Num.Vector3 forward = CharacterMotor.ViewDirection(_yaw, _pitch, up);
            _camera.GlobalTransform = new Transform3D(Basis.LookingAt(G(forward), G(up)), G(eye));
        }
        Num.Vector3 cameraPosition = N(_camera.GlobalPosition);
        _terrain.Follow(_zone.Physics, _fixedCamera.HasValue ? cameraPosition - _zone.Frame.UpAt(cameraPosition) * 1.7f : feet);
        Aim();
        Measure(delta);
        bool wantsCursor = _ui.WantsCursor;
        if (!_shot && !OS.HasFeature("headless"))
        {
            Input.MouseMode = wantsCursor ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        }
    }

    private void Walk(double delta)
    {
        _accumulator += delta;
        while (_accumulator >= CharacterMotor.DeltaTime)
        {
            ref Character character = ref _zone.Entities.Get<Character>(_player);
            character.Input = ReadInput();
            _jumpPressed = false;
            _feetBefore = character.State.Position;
            _zone.Step();
            _feetNow = _zone.Entities.Get<Character>(_player).State.Position;
            _accumulator -= CharacterMotor.DeltaTime;
        }
    }

    private MoveInput ReadInput()
    {
        var input = new MoveInput { View = Facing.View(_yaw, _pitch, _zone.Frame.UpAt(_feetNow)) };
        if (_fixedCamera.HasValue || _ui.WantsCursor || Input.MouseMode != Input.MouseModeEnum.Captured)
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

    // What the held tool would do where the player looks: the ghost (the placement pipeline's verdict for what the tool
    // places), the target and the lines under the crosshair.
    private void Aim()
    {
        Num.Vector3 eye = N(_camera.GlobalPosition);
        Num.Vector3 forward = N(-_camera.GlobalBasis.Z);
        _target = _world.Target(eye, forward, (float)_data.Reach, out _targetHit);
        _model.SetPrivilege(_world.PrivilegeAt(Player, _feetNow));
        _hud.Line = null;
        _hud.Reason = null;
        _hud.Legend = _renderer.StabilityOverlay;
        _model.Prompt = null;
        int tool = _model.ActiveBelt;
        if (tool != _tool)
        {
            (_tool, _turns) = (tool, 0);
        }
        Placeable? placing = (uint)tool < (uint)Tools.Length ? Tools[tool](this) : null;
        _candidate = placing != null
            ? Placer.Plan(_placement, new PlacementContext(Player, eye, placing, _wallet), new PlacementAim(forward, Facing.YawOf(forward, _zone.Frame.UpAt(eye)), _turns))
            : null;
        _ghost.Show(_candidate);
        if (placing != null)
        {
            _hud.Line = placing.Name.ToUpperInvariant();
            foreach (PlacementCost cost in placing.Costs)
            {
                _hud.Line += $"  ·  {cost.Amount} {_data.MaterialName(cost.Material).ToUpperInvariant()} ({_wallet.Count(cost.Material):N0})";
            }
            Outcome verdict = _candidate?.Outcome ?? Placer.Nothing(_placement, placing);
            _hud.Reason = verdict.Ok ? _candidate?.Detail is PlacementPlan plan ? $"{plan.Stability / 10.0:0.#}% STABLE" : null : _texts.Line(verdict);
            _hud.ReasonIsWarning = _candidate is { Valid: false };
        }
        _hud.Hints = Hints[Math.Clamp(tool, 0, Hints.Length - 1)];
        if (tool != 0 && _target != null)
        {
            _hud.Line ??= TargetLine(_target);
        }
        if (_target is Door aimedDoor && _targetHit.Distance < 3.5f)
        {
            _model.Prompt = new InteractionPrompt("E", aimedDoor.Open ? "CLOSE DOOR" : "OPEN DOOR");
        }
    }

    // What each tool on the belt places (none: the hammer): the plan's piece, three doors, the claim device.
    private static readonly Func<BuildSandbox, Placeable?>[] Tools =
    {
        s => s._content.Placement.Of(s._model.Plan.Chosen),
        _ => null,
        s => s.Door("wooden-door"),
        s => s.Door("armoured-door"),
        s => s.Door("sheet-metal-double-door"),
        s => s._content.Placement.Of(s._content.ClaimDeployable),
    };

    private static readonly string[] Hints =
    {
        "LMB BUILD   ·   HOLD RMB SELECT PIECE   ·   R ROTATE   ·   F2 STABILITY",
        "LMB REPAIR   ·   HOLD RMB UPGRADE / ROTATE / DEMOLISH   ·   F2 STABILITY",
        "LMB PLACE THE DOOR   ·   R TURN 180°   ·   E OPEN / SHUT",
        "LMB PLACE THE DOOR   ·   R TURN 180°   ·   E OPEN / SHUT",
        "LMB PLACE THE DOOR   ·   R TURN 180°   ·   E OPEN / SHUT",
        "LMB PLACE ON A FOUNDATION OR FLOOR   ·   R TURN   ·   F5 ONE HOUR OF UPKEEP",
    };

    private Placeable? Door(string key) => _data.Door(key) is { } def ? _content.Placement.Of(def) : null;

    private string TargetLine(object target) => target switch
    {
        Piece p => $"{_data.Grades[p.Grade].Name.ToUpperInvariant()} {p.Def.Name.ToUpperInvariant()}  ·  {Math.Ceiling(p.Health):0} / {p.MaxHealth:0}  ·  {p.StabilityPercent:0.#}% STABLE",
        Door d => $"{d.Def.Name.ToUpperInvariant()}  ·  {Math.Ceiling(d.Health):0} / {d.Def.Health:0}  ·  {(d.Open ? "OPEN" : "SHUT")}",
        ClaimDevice c => $"{_data.ClaimName.ToUpperInvariant()}  ·  PROTECTED {Protected(c)}",
        _ => "",
    };

    private string Protected(ClaimDevice claim)
    {
        double seconds = _world.ProtectedSeconds(claim.Structure);
        return double.IsInfinity(seconds) ? "FOREVER" : seconds < 3600 ? $"{seconds / 60:0} MIN" : $"{seconds / 3600:0.#} H";
    }

    // The hammer's radial acts on the piece through the rules here; the game sends intents instead (BuildView).
    void IHammerActions.Upgrade(Piece piece, int grade) => Tell(_world.Upgrade(Player, piece, grade, _wallet, N(_camera.GlobalPosition)));

    void IHammerActions.Rotate(Piece piece) => Tell(_world.Rotate(Player, piece, N(_camera.GlobalPosition), _clock));

    void IHammerActions.Demolish(Piece piece) => Tell(_world.Demolish(Player, piece, N(_camera.GlobalPosition), _clock));

    private void Tell(Outcome outcome)
    {
        if (!outcome.Ok)
        {
            _model.Say(_texts.Line(outcome));
        }
    }

    // The stress scene's frame cost: the average and worst of 300 frames once everything is drawn (vsync off).
    private void Measure(double delta)
    {
        _hud.Stats = _scene == "stress" || Options.ContainsKey("stats")
            ? $"{_renderer.Pieces:N0} PIECES  ·  {_renderer.MultiMeshes} MULTIMESHES  ·  " +
              $"{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} DRAW CALLS" +
              (_measured > 0 ? $"\nFRAME {_frameSum / _measured * 1000:0.0} MS AVERAGE ({_measured / _frameSum:0} FPS), {_frameWorst * 1000:0.0} MS WORST, {_measured} FRAMES" : "")
            : null;
        if (!_measure || _measured >= MeasureFrames || _frames < 60 || !_terrain.IsSettled)
        {
            return;
        }
        _measured++;
        _frameSum += delta;
        _frameWorst = Math.Max(_frameWorst, delta);
        if (_measured == MeasureFrames)
        {
            ulong primitives = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
            GD.Print($"[build] stress: {_renderer.Pieces} pieces ({_renderer.Corners} corner posts), {_renderer.MultiMeshes} multimeshes, " +
                $"{RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} draw calls, {primitives:N0} primitives; " +
                $"frame {_frameSum / _measured * 1000:0.00} ms average ({_measured / _frameSum:0} fps), {_frameWorst * 1000:0.00} ms worst over {_measured} frames " +
                $"at {GetViewport().GetVisibleRect().Size}");
        }
    }

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion when Input.MouseMode == Input.MouseModeEnum.Captured:
                _yaw = Mathf.Wrap(_yaw - motion.Relative.X * MouseSensitivity, -Mathf.Pi, Mathf.Pi);
                _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * MouseSensitivity, -MaxPitch, MaxPitch);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when !_ui.WantsCursor:
                Use();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                switch (key.PhysicalKeycode)
                {
                    case Key.Space:
                        _jumpPressed = true;
                        break;
                    case Key.R:
                        _turns++;
                        break;
                    case Key.E:
                        UseDoor();
                        break;
                    case Key.F2:
                        _renderer.StabilityOverlay = !_renderer.StabilityOverlay;
                        break;
                    case Key.F5:
                        _world.AdvanceTime(3600, _clock);
                        _model.Say("An hour of upkeep and decay");
                        break;
                    default:
                        return;
                }
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    // The left mouse button with the held tool: what it places goes where the ghost shows (its kind puts it in through
    // this sandbox's site, the materials from the sandbox's wallet); the hammer repairs.
    private void Use()
    {
        Num.Vector3 eye = N(_camera.GlobalPosition);
        if (_model.ActiveBelt == 1)
        {
            if (_target is Piece piece)
            {
                Tell(_world.Repair(Player, piece, _wallet, eye, _clock));
            }
            return;
        }
        if (_candidate is not { } c)
        {
            if ((uint)_model.ActiveBelt < (uint)Tools.Length && Tools[_model.ActiveBelt](this) is { } placing)
            {
                Tell(Placer.Nothing(_placement, placing));
            }
            return;
        }
        if (!c.Valid)
        {
            Tell(c.Outcome);
            return;
        }
        Outcome put = c.Placeable.Kind.Put(this, Player, c);
        if (put.Ok)
        {
            foreach (PlacementCost cost in c.Placeable.Costs)
            {
                _wallet.Take(cost.Material, cost.Amount);
            }
        }
        Tell(put);
    }

    private void UseDoor()
    {
        Num.Vector3 eye = N(_camera.GlobalPosition);
        Door? door = _target as Door ?? _world.OpeningAimed(eye, N(-_camera.GlobalBasis.Z), (float)_data.Reach)?.Door;
        if (door != null)
        {
            Tell(_world.Toggle(Player, door));
        }
    }

    // The sandbox as a placement site: its building alone (no zone: no deployables, charges or ships here).
    BuildingWorld? IPlacementSite.Building => _world;

    double IPlacementSite.BuildingNow => _clock;

    Outcome IPlacementSite.PutDeployable(ulong player, DeployableDef def, PlacementCandidate c) => OutcomeCode.NotAllowed;

    Outcome IPlacementSite.PutCharge(ulong player, ThrowableStats stats, PlacementCandidate c) => OutcomeCode.NotAllowed;

    Outcome IPlacementSite.PutShip(ulong player, Hollow.Simulation.Ships.ShipDef def, PlacementCandidate c) => OutcomeCode.NotAllowed;

    /// <summary>The sandbox's surroundings for placement: its builder, nothing else.</summary>
    private sealed class Surroundings(BuildSandbox sandbox) : IPlacementSurroundings
    {
        public void ThingsNear(Num.Vector3 point, float radius, List<PlacedThing> into)
        {
        }

        public void CharactersNear(Num.Vector3 point, float radius, List<Capsule> into) => into.AddRange(sandbox.Capsules());

        public void ShipsNear(Num.Vector3 point, float radius, List<PlacedShip> into)
        {
        }

        public int ThingsOn(in Hollow.Simulation.Targets.TargetRef support) => 0;

        public int ThingsOwned(ulong player, DeployableDef def) => 0;
    }

    private void Capture()
    {
        if (!_shot && !OS.HasFeature("headless"))
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    // ---------------------------------------------------------------- the world around

    private void BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.17f, 0.33f, 0.62f),
            SkyHorizonColor = new Color(0.64f, 0.73f, 0.82f),
            GroundBottomColor = new Color(0.20f, 0.22f, 0.20f),
            GroundHorizonColor = new Color(0.56f, 0.62f, 0.66f),
            SunAngleMax = 30,
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.6f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            SsaoEnabled = true,
            SsaoRadius = 1.2f,
            SsaoIntensity = 1.6f,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Depth,
            FogDepthBegin = 70,
            FogDepthEnd = 330,
            FogDepthCurve = 1.4f,
            FogLightColor = new Color(0.64f, 0.73f, 0.82f),
            FogAerialPerspective = 0.3f,
        };
        AddChild(new WorldEnvironment { Name = "Environment", Environment = environment });
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 1.55f,
            LightColor = new Color(1.0f, 0.95f, 0.88f),
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 120,
        };
        // From the south-west, so the house's door side is in the sun.
        sun.RotationDegrees = new Vector3(-38, -35, 0);
        AddChild(sun);
    }

    public override void _ExitTree()
    {
        _world.Dispose();
        _zone.Dispose();
    }

    private static Vector3 G(Num.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3 G(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private static Num.Vector3 N(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Num.Vector3 F(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private static Vector3D D(Num.Vector3 v) => new(v.X, v.Y, v.Z);
}
