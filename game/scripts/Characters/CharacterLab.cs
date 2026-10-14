using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Core;
using HollowUniverse.Common;

namespace HollowUniverse.Characters;

/// <summary>
/// The character viewer, <c>--view character</c> (GROUND.md §4.8): one character on a patch of ground, with keys to
/// cycle outfits, held items and motions, fire the events, die, and look through the first-person view. A crowd mode
/// measures the cost of many characters. Command-line options are listed in AGENTS.md.
/// </summary>
public partial class CharacterLab : Node3D
{
    private static readonly string[] Motions =
    {
        "idle", "walk", "jog", "sprint", "crouch", "crouch walk", "strafe left", "strafe right", "back", "jump", "fall",
        "swim idle", "swim", "down", "crawl", "sleep",
    };

    private readonly Dictionary<string, string> _options;
    private CharacterView _character = null!;
    private Camera3D _camera = null!;
    private ViewmodelView _viewmodel = null!;
    private readonly List<(CharacterView View, float Radius, float Phase, float Speed, HeldItemModel? Held, string Motion)> _crowd = new();

    private int _outfit;
    private ulong _seed;
    private bool _seeded;
    private int _item = -1;
    private int _motion;
    private float _aimPitch;

    // --look yaw,pitch (degrees): the head's free look (Alt), as another player would see it.
    private Vector2 _look;
    private bool _firstPerson;
    private bool _aiming;
    private Vector3? _viewmodelOrbit;
    private float _time;
    private float _jumpClock;

    // Orbit camera.
    private float _yaw = 205f, _pitch = 6f, _distance = 3.4f, _height = 0.95f;
    private bool _dragging;

    // Interface.
    private Label _outfitLabel = null!, _lookLabel = null!, _itemLabel = null!, _motionLabel = null!, _lodLabel = null!, _frameLabel = null!;
    private Control _hud = null!;

    // Automation.
    private double _elapsed;
    private bool _capturing;
    private string? _pendingEvent;
    private double _eventAt;
    private readonly List<double> _frameTimes = new();

    public CharacterLab(Dictionary<string, string> options)
    {
        _options = options;
        Name = "CharacterLab";
    }

    // Everything the viewer shows stands on this: at the origin of its own little world, or on the first world's real
    // ground in front of the surface view's camera (--on-surface).
    private Node3D _stage = null!;
    private HollowUniverse.Surface.SurfaceView? _surface;
    private Camera3D? _surfaceCamera;

    public override void _Ready()
    {
        _stage = new Node3D { Name = "Stage" };
        AddChild(_stage);
        if (_options.ContainsKey("on-surface"))
        {
            OpenSurface();
        }
        else
        {
            BuildWorld();
        }
        BodyType body = Option("body", "male") == "female" ? BodyType.Female : BodyType.Male;
        _character = new CharacterView { Name = "Character", Body = body };
        _stage.AddChild(_character);
        _camera = new Camera3D { Name = "Camera", Fov = 50f, Near = 0.03f, Far = 800f, CullMask = 1 | ViewmodelView.RenderLayer };
        AddChild(_camera);
        // On the surface its own camera draws (its terrain, plants and sky are placed for it), set where the viewer's is.
        (_surfaceCamera ?? _camera).MakeCurrent();
        _viewmodel = new ViewmodelView { Name = "Viewmodel", Body = body };
        _camera.AddChild(_viewmodel);
        BuildInterface();

        _outfit = (int)Number("outfit", 0) % Wardrobe.Outfits.Count;
        if (_options.TryGetValue("player", out string? player))
        {
            _seed = Looks.Hash(player);
            _seeded = true;
        }
        else if (_options.TryGetValue("seed", out string? seed))
        {
            _seed = ulong.Parse(seed, CultureInfo.InvariantCulture);
            _seeded = true;
        }
        ApplyLook();
        _motion = Math.Max(0, Array.IndexOf(Motions, Option("motion", "idle").Replace('_', ' ')));
        string item = Option("item", "none");
        _item = item == "none" ? -1 : IndexOfItem(item);
        _aimPitch = Mathf.DegToRad((float)Number("aim", 0));
        if (_options.TryGetValue("look", out string? look))
        {
            string[] v = look.Split(',');
            _look = new Vector2(Mathf.DegToRad(float.Parse(v[0], CultureInfo.InvariantCulture)),
                v.Length > 1 ? Mathf.DegToRad(float.Parse(v[1], CultureInfo.InvariantCulture)) : 0f);
        }
        _yaw = (float)Number("yaw", _yaw);
        _pitch = (float)Number("pitch", _pitch);
        _distance = (float)Number("distance", _distance);
        _height = (float)Number("height", _height);
        _firstPerson = _options.ContainsKey("fp");
        if (_options.TryGetValue("vm-orbit", out string? orbit))
        {
            // Inspect the first-person hands from another side: yaw, pitch (degrees) and distance (metres) round them.
            string[] v = orbit.Split(',');
            _viewmodelOrbit = new Vector3(float.Parse(v[0], CultureInfo.InvariantCulture),
                float.Parse(v[1], CultureInfo.InvariantCulture), float.Parse(v[2], CultureInfo.InvariantCulture));
        }
        _aiming = _options.ContainsKey("ads");
        if (_options.TryGetValue("blink", out string? blink))
        {
            _character.BlinkOverride = float.Parse(blink, CultureInfo.InvariantCulture);
        }
        if (_options.TryGetValue("lod", out string? lod))
        {
            _character.ForcedLod = int.Parse(lod, CultureInfo.InvariantCulture);
        }
        if (_options.TryGetValue("event", out string? e))
        {
            _pendingEvent = e;
            _eventAt = Number("event-at", 1.0);
        }
        int crowd = (int)Number("crowd", 0);
        if (crowd > 0)
        {
            SpawnCrowd(crowd);
        }
        int lineup = (int)Number("lineup", 0);
        if (lineup > 0)
        {
            SpawnLineup(lineup);
        }
        if (_options.ContainsKey("partner"))
        {
            BodyType partnerBody = Option("partner", "male") == "female" && CharacterAssets.Has(BodyType.Female) ? BodyType.Female : BodyType.Male;
            _partner = new CharacterView { Name = "Partner", Body = partnerBody };
            _stage.AddChild(_partner);
            _partner.Look = Looks.ForPlayer(_seed + 101, partnerBody);
            _partner.Wear(Wardrobe.Outfits[(int)Number("partner-outfit", 1) % Wardrobe.Outfits.Count].Items);
        }
        if (_options.ContainsKey("measure"))
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        }
        ApplyOutfit();
        Refresh();
        if (_options.TryGetValue("fp-sheet", out string? sheet))
        {
            SheetOfHeldItems(sheet);
        }
    }

    /// <summary>
    /// --fp-sheet &lt;png&gt;: every held item in first person (or those <c>--only a,b</c> names), a tile each in the
    /// catalogue's order (six to a row), saved as one picture; then quits. The lower right of each frame, where the hands
    /// are (<c>--full</c>: the whole frame).
    /// </summary>
    private async void SheetOfHeldItems(string path)
    {
        _firstPerson = true;
        const int columns = 6;
        var tile = new Vector2I(480, 270);
        var items = new List<int>();
        string[]? only = _options.TryGetValue("only", out string? keys) ? keys.Split(',') : null;
        for (int i = 0; i < HeldItems.All.Count; i++)
        {
            if (only == null || Array.IndexOf(only, HeldItems.All[i].Id) >= 0)
            {
                items.Add(i);
            }
        }
        int count = items.Count, rows = Math.Max(1, (count + columns - 1) / columns);
        Image sheet = Image.CreateEmpty(Math.Min(count, columns) * tile.X, rows * tile.Y, false, Image.Format.Rgba8);
        await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        for (int n = 0; n < count; n++)
        {
            int i = items[n];
            _item = i;
            Refresh();
            await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Image frame = GetViewport().GetTexture().GetImage();
            Vector2I size = frame.GetSize();
            // The right two thirds and lower two thirds of the frame: the hands and what they hold (--full: all of it).
            Rect2I crop = _options.ContainsKey("full") ? new Rect2I(Vector2I.Zero, size) : new Rect2I(size.X / 3, size.Y / 3, size.X * 2 / 3, size.Y * 2 / 3);
            Image part = frame.GetRegion(crop);
            part.Resize(tile.X, tile.Y, Image.Interpolation.Lanczos);
            part.Convert(Image.Format.Rgba8);
            sheet.BlitRect(part, new Rect2I(Vector2I.Zero, tile), new Vector2I(n % columns * tile.X, n / columns * tile.Y));
            GD.Print($"[sheet] {n}: {HeldItems.All[i].Id}");
        }
        Error error = Save(sheet, path);
        GD.Print(error == Error.Ok ? $"Saved the sheet {path}" : $"Could not save {path}: {error}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private static int IndexOfItem(string id)
    {
        for (int i = 0; i < HeldItems.All.Count; i++)
        {
            if (HeldItems.All[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }

    // ---------------------------------------------------------------- world

    /// <summary>
    /// The first world's surface view (GROUND.md §4.6) at the landing site (or --lat, --lon) and --time, its clock held:
    /// its sky, sun, moon, exposure and ground light the characters, who stand on a stage on its terrain six metres in
    /// front of its camera. The viewer's own camera frames them and borrows the surface's environment.
    /// </summary>
    private void OpenSurface()
    {
        HollowUniverse.App.GalaxyService.Load();
        var found = Hollow.Surface.FirstWorld.Find(HollowUniverse.App.GalaxyService.Galaxy);
        if (found == null)
        {
            GD.PushError("--on-surface: the first world is not in this universe.");
            BuildWorld();
            return;
        }
        (Hollow.Galaxy.Systems.StarSystem system, Hollow.Galaxy.Systems.Body planet) = found.Value;
        var surface = Hollow.Surface.PlanetSurface.For(HollowUniverse.App.GalaxyService.Galaxy, system, planet);
        double lat = Number("lat", Hollow.Surface.FirstWorld.LandingLatitude);
        double lon = Number("lon", Hollow.Surface.FirstWorld.LandingLongitude);
        double time = _options.TryGetValue("time", out string? t)
            ? UniverseTime.SecondsSinceJ2000(DateTime.Parse(t, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal))
            : UniverseTime.Now;
        _surface = new HollowUniverse.Surface.SurfaceView { Name = "Surface" };
        AddChild(_surface);
        _surface.SetActive(true);
        // Flying (above eye height), so it leaves its camera where the viewer puts it.
        _surface.Open(system, planet, surface, lat, lon, 3, 0, 0, time);
        _surface.SetTime(time, paused: true);
        _surface.InterfaceVisible = false;
        _surfaceCamera = GetViewport().GetCamera3D();
        if (_surfaceCamera != null)
        {
            _surfaceCamera.CullMask |= ViewmodelView.RenderLayer;
        }
        // The stage: on the ground six metres north of the camera, upright on the planet.
        Vector3D origin = Hollow.Surface.ZoneFrame.Direction(lat * DMath.DegToRad, lon * DMath.DegToRad);
        var frame = new HollowUniverse.Surface.RenderFrame(origin * (surface.Radius + Math.Max(surface.HeightAt(origin), 0)));
        Vector3D spot = frame.ToBody(new Vector3(0f, 0f, -6f));
        Vector3D dir = spot.Normalized;
        double ground = Math.Max(surface.HeightAt(dir), surface.SeaRadius > 0 ? surface.SeaRadius - surface.Radius : double.MinValue);
        Vector3 at = frame.ToRender(dir * (surface.Radius + ground));
        Vector3 up = frame.DirectionToRender(dir).Normalized();
        // Turned so the characters face the sun, --stage-yaw degrees (default 30) to its left: lit from the front and side.
        Vector3 sun = _surface.Sky?.SunDirection ?? Vector3.Forward;
        Vector3 flat = sun - up * sun.Dot(up);
        float yaw = flat.LengthSquared() > 1e-6f ? MathF.Atan2(-flat.X, -flat.Z) : 0f;
        yaw += Mathf.DegToRad((float)Number("stage-yaw", 30));
        _stage.Transform = new Transform3D(CharacterFrame.BasisOf(up, yaw), at);
    }

    private void BuildWorld()
    {
        var sky = new Sky
        {
            SkyMaterial = new ProceduralSkyMaterial
            {
                SkyTopColor = new Color(0.30f, 0.45f, 0.66f),
                SkyHorizonColor = new Color(0.66f, 0.70f, 0.74f),
                GroundBottomColor = new Color(0.20f, 0.19f, 0.17f),
                GroundHorizonColor = new Color(0.52f, 0.53f, 0.52f),
                SunAngleMax = 20f,
            },
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.9f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.05f,
            SsaoEnabled = true,
            SsaoRadius = 0.6f,
            SsaoIntensity = 1.6f,
            GlowEnabled = false,
            FogEnabled = true,
            FogLightColor = new Color(0.62f, 0.67f, 0.74f),
            FogDensity = 0.0025f,
            FogSkyAffect = 0.0f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        var sun = new DirectionalLight3D
        {
            Name = "Sun",
            LightEnergy = 2.1f,
            LightColor = new Color(1.0f, 0.95f, 0.88f),
            ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
            DirectionalShadowMaxDistance = 60f,
            ShadowBlur = 1.2f,
            LightAngularDistance = 0.6f,
        };
        sun.RotationDegrees = new Vector3(-38f, -128f, 0f);
        AddChild(sun);

        // Ground: a wide field with a gentle noise, and a ramp to show the feet finding a slope.
        var noise = new FastNoiseLite { Frequency = 0.04f, FractalOctaves = 4 };
        var ground = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.36f, 0.37f, 0.30f),
            AlbedoTexture = new NoiseTexture2D { Noise = noise, Width = 512, Height = 512, Seamless = true, ColorRamp = GroundRamp() },
            Roughness = 0.95f,
            Uv1Scale = new Vector3(40, 40, 1),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        var floor = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(400, 400) }, MaterialOverride = ground };
        AddChild(floor);
        var body = new StaticBody3D { Name = "Ground", CollisionLayer = 1 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(400, 1, 400) }, Position = new Vector3(0, -0.5f, 0) });
        AddChild(body);
        var rampMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.43f, 0.40f), Roughness = 0.9f };
        var ramp = new StaticBody3D { Name = "Ramp", Position = new Vector3(4f, 0f, 0f), RotationDegrees = new Vector3(0, 0, 18f) };
        ramp.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(3f, 0.6f, 3f) }, MaterialOverride = rampMaterial });
        ramp.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3f, 0.6f, 3f) } });
        AddChild(ramp);
    }

    private static Gradient GroundRamp()
    {
        var g = new Gradient();
        g.SetColor(0, new Color(0.55f, 0.55f, 0.50f));
        g.SetColor(1, new Color(0.85f, 0.86f, 0.80f));
        return g;
    }

    private void SpawnCrowd(int count)
    {
        var rng = new RandomNumberGenerator { Seed = 7 };
        // Bare hands, the default tool, pistol and rifle, a bow (drawn while standing) and a spear.
        HeldItemModel?[] items =
        {
            null, HeldItems.For(HeldKind.Tool, null), HeldItems.For(HeldKind.Pistol, null), HeldItems.For(HeldKind.Rifle, null),
            ItemById("bow"), ItemById("wooden-spear"),
        };
        // Moving, wounded (lying and crawling) and asleep.
        string[] motions = { "idle", "walk", "jog", "sprint", "crouch walk", "jog", "walk", "crawl", "sleep", "down" };
        for (int i = 0; i < count; i++)
        {
            // Every other one a woman; each with its own look.
            BodyType body = i % 2 == 1 && CharacterAssets.Has(BodyType.Female) ? BodyType.Female : BodyType.Male;
            var view = new CharacterView { Name = "Crowd" + i, Body = body };
            view.Look = Looks.ForPlayer((ulong)(1000 + i * 7919), body);
            _stage.AddChild(view);
            if (_options.TryGetValue("lod", out string? lod))
            {
                view.ForcedLod = int.Parse(lod, CultureInfo.InvariantCulture);
            }
            view.Wear(Wardrobe.Outfits[1 + i % (Wardrobe.Outfits.Count - 1)].Items);
            _crowd.Add((view, 2.5f + (i % 10) * 2.6f, rng.Randf() * MathF.Tau, 0f, items[i % items.Length], motions[i % motions.Length]));
        }
    }

    private static HeldItemModel? ItemById(string id)
    {
        int index = IndexOfItem(id);
        return index >= 0 ? HeldItems.All[index] : null;
    }

    /// <summary>
    /// A row of players facing the camera (<c>--lineup n</c>): every other one a woman, each with the look of a seed
    /// from <c>--seed</c> on and an outfit of the list in turn. The viewer's own character steps aside.
    /// </summary>
    private void SpawnLineup(int count)
    {
        _character.Visible = false;
        _character.ProcessMode = ProcessModeEnum.Disabled;
        ulong first = _options.TryGetValue("seed", out string? seed) ? ulong.Parse(seed, CultureInfo.InvariantCulture) : 1;
        float spacing = (float)Number("spacing", 0.85);
        for (int i = 0; i < count; i++)
        {
            BodyType body = i % 2 == 1 && CharacterAssets.Has(BodyType.Female) ? BodyType.Female : BodyType.Male;
            var view = new CharacterView { Name = "Lineup" + i, Body = body };
            _stage.AddChild(view);
            view.Look = Looks.ForPlayer(first + (ulong)i, body);
            view.Wear(LineupOutfits[i % LineupOutfits.Length]);
            // A loose row: staggered a little, each turned a little its own way.
            float x = (i - (count - 1) * 0.5f) * spacing;
            float z = MathF.Abs(x) * 0.12f + (i % 2 == 0 ? 0f : 0.3f) + (i % 3 - 1) * 0.08f;
            float yaw = ((i * 5 + 2) % 7 - 3) * 0.07f;
            view.Apply(CharacterFrame.Standing(new Vector3(x, 0f, z), Vector3.Up, yaw));
            _lineup.Add(view);
        }
    }

    private readonly List<CharacterView> _lineup = new();

    /// <summary>Outfits the lineup goes through (what players wear in the first hours of a wipe and after).</summary>
    private static readonly Wearable[][] LineupOutfits =
    {
        new[] { Wardrobe.WovenShirt, Wardrobe.WovenTrousers },
        new[] { Wardrobe.WovenHood, Wardrobe.WovenShirt, Wardrobe.WovenTrousers, Wardrobe.LacedBoots },
        Array.Empty<Wearable>(),
        new[] { Wardrobe.KnitCap, Wardrobe.WovenShirt, Wardrobe.WovenTrousers, Wardrobe.LacedBoots },
        new[] { Wardrobe.WovenTrousers, Wardrobe.LacedBoots, Wardrobe.WovenGloves },
        new[] { Wardrobe.WovenShirt, Wardrobe.WovenTrousers, Wardrobe.LacedBoots, Wardrobe.RivetedBreastplate, Wardrobe.WovenGloves },
        new[] { Wardrobe.WovenHood, Wardrobe.MetalMask, Wardrobe.WovenShirt, Wardrobe.WovenTrousers, Wardrobe.LacedBoots, Wardrobe.WovenGloves },
        new[] { Wardrobe.MetalMask, Wardrobe.WovenShirt, Wardrobe.WovenTrousers, Wardrobe.LacedBoots, Wardrobe.RivetedBreastplate },
    };

    // ---------------------------------------------------------------- interface

    private void BuildInterface()
    {
        // --hide-ui: the picture alone (for screenshots).
        var layer = new CanvasLayer { Visible = !_options.ContainsKey("hide-ui") };
        AddChild(layer);
        var root = new Control { Theme = Ui.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);
        _hud = root;
        (PanelContainer card, VBoxContainer content) = Ui.Card(8);
        Ui.Place(card, Control.LayoutPreset.TopLeft);
        root.AddChild(card);
        content.AddChild(Ui.Label("Character", TextStyle.Title));
        content.AddChild(Ui.Label("Player body, clothing and animation", TextStyle.Secondary));
        content.AddChild(Ui.Separator());
        GridContainer rows = Ui.Rows();
        content.AddChild(rows);
        _outfitLabel = Ui.AddRow(rows, "Outfit", "", numeric: false);
        _lookLabel = Ui.AddRow(rows, "Look", "", numeric: false);
        _itemLabel = Ui.AddRow(rows, "Holding", "", numeric: false);
        _motionLabel = Ui.AddRow(rows, "Motion", "", numeric: false);
        _lodLabel = Ui.AddRow(rows, "Detail", "", numeric: false);
        _frameLabel = Ui.AddRow(rows, "Frame", "");
        Ui.Size(rows, 300);

        (PanelContainer keys, VBoxContainer keyContent) = Ui.Card(6);
        Ui.Place(keys, Control.LayoutPreset.BottomLeft);
        root.AddChild(keys);
        HFlowContainer hints = Ui.KeyHints(("O", "outfit"), ("N", "look"), ("I", "item"), ("M", "motion"), ("Q", "swing"), ("F", "fire"),
            ("R", "reload"), ("U", "use"), ("G", "help up"), ("H", "hit"), ("K", "die / revive"), ("V", "first person"),
            ("RMB", "aim / draw"), ("↑|↓", "look"), ("L", "detail"));
        keyContent.AddChild(hints);
        Ui.Size(hints, 560);
    }

    private void Refresh()
    {
        _outfitLabel.Text = Wardrobe.Outfits[_outfit].Name;
        Look look = _character.Look;
        _lookLabel.Text = (_seeded ? $"#{_seed} · " : "Default · ") +
            $"{look.SkinName} skin, {look.HairColourName} {(look.Hair == Looks.Buzz ? "buzz cut" : look.Hair)}, face {look.Face}";
        _itemLabel.Text = _item < 0 ? "Nothing" : HeldItems.All[_item].Name;
        _motionLabel.Text = char.ToUpperInvariant(Motions[_motion][0]) + Motions[_motion][1..] + (_firstPerson ? " · first person" : "");
    }

    private void ApplyLook()
    {
        Look look = _seeded ? Looks.ForPlayer(_seed, _character.Body) : Looks.Default(_character.Body);
        _character.Look = look;
        _viewmodel.Look = look;
    }

    private void ApplyOutfit()
    {
        _character.Wear(Wardrobe.Outfits[_outfit].Items);
        _viewmodel.Wear(Wardrobe.Outfits[_outfit].Items);
    }

    // ---------------------------------------------------------------- input

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _dragging = mb.Pressed;
            }
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                _aiming = mb.Pressed;
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                _distance = Math.Max(0.8f, _distance * 0.9f);
            }
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                _distance = Math.Min(60f, _distance * 1.1f);
            }
        }
        else if (e is InputEventMouseMotion mm && _dragging)
        {
            _yaw -= mm.Relative.X * 0.3f;
            _pitch = Math.Clamp(_pitch + mm.Relative.Y * 0.3f, -30f, 80f);
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.O:
                    _outfit = (_outfit + 1) % Wardrobe.Outfits.Count;
                    ApplyOutfit();
                    break;
                case Key.N:
                    _seed = key.ShiftPressed ? _seed - 1 : _seed + 1;
                    _seeded = true;
                    ApplyLook();
                    break;
                case Key.I:
                    _item = _item + 1 >= HeldItems.All.Count ? -1 : _item + 1;
                    break;
                case Key.M:
                    _motion = (_motion + 1) % Motions.Length;
                    break;
                case Key.Q:
                    Fire(CharacterEvent.Swing);
                    break;
                case Key.F:
                    Fire(CharacterEvent.Fire);
                    break;
                case Key.R:
                    Fire(CharacterEvent.Reload);
                    break;
                case Key.H:
                    Fire(CharacterEvent.Hit);
                    break;
                case Key.U:
                    Fire(CharacterEvent.Use);
                    break;
                case Key.G:
                    Fire(CharacterEvent.HelpUp);
                    break;
                case Key.K:
                    if (_character.IsDead)
                    {
                        _character.Revive();
                    }
                    else
                    {
                        Fire(CharacterEvent.Death);
                    }
                    break;
                case Key.V:
                    _firstPerson = !_firstPerson;
                    break;
                case Key.L:
                    _character.ForcedLod = _character.ForcedLod >= 2 ? -1 : _character.ForcedLod + 1;
                    break;
                case Key.Up:
                    _aimPitch = Math.Min(1.3f, _aimPitch + 0.15f);
                    break;
                case Key.Down:
                    _aimPitch = Math.Max(-1.3f, _aimPitch - 0.15f);
                    break;
                default:
                    return;
            }
            Refresh();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Fire(CharacterEvent e)
    {
        Vector3 back = -CharacterFrame.Facing(Vector3.Up, 0f);
        _character.Trigger(e, e == CharacterEvent.Death ? back : Vector3.Zero);
        if (_firstPerson)
        {
            _viewmodel.Trigger(e);
        }
        if (e == CharacterEvent.HelpUp && _partner != null)
        {
            // The one helped gets up as the helper takes their hand.
            _partnerUp = true;
        }
    }

    // A second player for the help-up (--partner): down in front of the character, getting up when helped.
    private CharacterView? _partner;
    private bool _partnerUp;
    private float _partnerClock;

    private void UpdatePartner(CharacterFrame main)
    {
        if (_partner == null)
        {
            return;
        }
        if (_partnerUp)
        {
            _partnerClock += (float)GetProcessDeltaTime();
        }
        // They lie facing the helper, their head towards the helper's feet; up, they stand where they lay.
        float yaw = main.Yaw + MathF.PI;
        Vector3 forward = CharacterFrame.Facing(Vector3.Up, main.Yaw);
        CharacterFrame f = CharacterFrame.Standing(main.Position + forward * (float)Number("partner-distance", 1.55), Vector3.Up, yaw);
        // Helped up: they start getting up half a second after the helper begins (when the hands meet).
        f.Stance = _partnerUp && _partnerClock > 0.45f ? CharacterStance.Stand : CharacterStance.Crawl;
        _partner.Apply(f);
    }

    // ---------------------------------------------------------------- frame

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        _elapsed += delta;
        CharacterFrame frame = MotionFrame(Motions[_motion], _time, dt);
        frame.Pitch = _aimPitch;
        frame.LookYaw = _look.X;
        frame.LookPitch = _look.Y;
        HeldItemModel? held = _item >= 0 ? HeldItems.All[_item] : null;
        frame.Held = held?.Kind ?? HeldKind.Unarmed;
        frame.HeldItem = held?.Id;
        _character.Apply(frame);
        UpdateCrowd(dt);

        if (_pendingEvent != null && _elapsed >= _eventAt)
        {
            if (_pendingEvent.StartsWith("motion:", StringComparison.Ordinal))
            {
                // A change of motion at a time (e.g. motion:idle from down, to get up).
                _motion = Math.Max(0, Array.IndexOf(Motions, _pendingEvent[7..].Replace('_', ' ')));
                if (_partner != null)
                {
                    _partnerUp = true;
                }
                Refresh();
            }
            else
            {
                Fire(_pendingEvent switch
                {
                    "swing" => CharacterEvent.Swing,
                    "fire" => CharacterEvent.Fire,
                    "reload" => CharacterEvent.Reload,
                    "hit" => CharacterEvent.Hit,
                    "helpup" => CharacterEvent.HelpUp,
                    "use" => CharacterEvent.Use,
                    "eat" => CharacterEvent.Eat,
                    "bandage" => CharacterEvent.Bandage,
                    "inject" => CharacterEvent.Inject,
                    _ => CharacterEvent.Death,
                });
            }
            _pendingEvent = null;
        }
        UpdatePartner(frame);

        if (_firstPerson)
        {
            Vector3 up = frame.Up;
            float eye = frame.Stance == CharacterStance.Crouch ? 1.02f : CharacterAssets.For(_character.Body).EyeHeight;
            Basis look = CharacterFrame.BasisOf(up, frame.Yaw) * new Basis(Vector3.Right, _aimPitch);
            var eyes = new Transform3D(look, frame.Position + up * eye);
            _camera.GlobalTransform = eyes;
            if (_viewmodelOrbit is Vector3 o)
            {
                _viewmodel.TopLevel = true;
                _viewmodel.GlobalTransform = eyes;
                Basis turn = look * Basis.FromEuler(new Vector3(Mathf.DegToRad(o.Y), Mathf.DegToRad(o.X), 0f));
                Vector3 hands = eyes * new Vector3(0.1f, -0.16f, -0.36f);
                _camera.GlobalTransform = new Transform3D(turn, hands + turn * new Vector3(0f, 0f, o.Z));
            }
            _viewmodel.Hold(frame.Held, frame.HeldItem);
            Vector3 local = look.Inverse() * frame.Velocity;
            _viewmodel.Apply(new ViewmodelFrame
            {
                LocalVelocity = local,
                Grounded = frame.Grounded,
                Sprinting = Motions[_motion] == "sprint",
                Crouching = frame.Stance == CharacterStance.Crouch,
                Aiming = _aiming,
            });
            _camera.Fov = Mathf.Lerp(70f, 54f, _viewmodel.AimAmount);
            _character.Visible = false;
        }
        else
        {
            _character.Visible = true;
            _viewmodel.Hold(HeldKind.Unarmed);
            Vector3 focus = _character.GlobalPosition + _stage.GlobalBasis.Y * _height;
            if (_character.IsDead)
            {
                // Follow the body, which the ragdoll carries away from where the character stood.
                if (_character.Skeleton.FindChild("Body_pelvis", true, false) is Node3D pelvis)
                {
                    focus = pelvis.GlobalPosition + Vector3.Up * (_height - 0.95f);
                }
            }
            float yaw = Mathf.DegToRad(_yaw), pitch = Mathf.DegToRad(_pitch);
            Vector3 offset = _stage.GlobalBasis * new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch)) * _distance;
            _camera.Fov = 50f;
            _camera.LookAtFromPosition(focus + offset, focus, _stage.GlobalBasis.Y);
        }
        if (_surfaceCamera != null)
        {
            _surfaceCamera.GlobalTransform = _camera.GlobalTransform;
            _surfaceCamera.Fov = _camera.Fov;
        }

        _lodLabel.Text = _character.ForcedLod >= 0 ? $"Level {_character.ForcedLod} (fixed)" : $"Level {_character.Lod}";
        _frameTimes.Add(delta);
        if (_frameTimes.Count % 20 == 0)
        {
            _frameLabel.Text = $"{Engine.GetFramesPerSecond():0} fps · {_crowd.Count + 1} characters";
        }
        Automate(delta);
        if (_options.ContainsKey("debug") && _frameTimes.Count == 30)
        {
            foreach (Node n in _character.Skeleton.GetChildren())
            {
                if (n is MeshInstance3D mi && mi.Visible)
                {
                    GD.Print($"[debug] {mi.Name} surfaces={mi.Mesh.GetSurfaceCount()} hidden={mi.GetInstanceShaderParameter("hidden_regions")} aabb={mi.GetAabb()}");
                }
            }
            GD.Print($"[debug] lod={_character.Lod} pelvis={_character.Skeleton.GetBoneGlobalPose(_character.Skeleton.FindBone("pelvis")).Origin}");
            int hand = _character.Skeleton.FindBone("hand_r");
            GD.Print($"[debug] hand target {_character.Poser.RightHand} weight {_character.Poser.RightHandWeight}");
            GD.Print($"[debug] hand actual {_character.Poser.LastRightHand}");
            if (_character.HeldModel != null)
            {
                Node3D? attach = _character.HeldModel.GetParent() as Node3D;
                GD.Print($"[debug] attachment {_character.Skeleton.GlobalTransform.AffineInverse() * attach!.GlobalTransform}");
                GD.Print($"[debug] item {_character.Skeleton.GlobalTransform.AffineInverse() * _character.HeldModel.GlobalTransform}");
            }
        }
    }

    /// <summary>The state the movement code would give for a motion, standing on the spot (a treadmill).</summary>
    private CharacterFrame MotionFrame(string motion, float t, float dt)
    {
        float face = Mathf.DegToRad((float)Number("face", 0));
        CharacterFrame f = CharacterFrame.Standing(Vector3.Zero, Vector3.Up, face);
        Vector3 forward = CharacterFrame.Facing(Vector3.Up, face);
        Vector3 right = forward.Cross(Vector3.Up);
        switch (motion)
        {
            case "walk":
                f.Velocity = forward * 1.25f;
                break;
            case "jog":
                f.Velocity = forward * 2.8f;
                break;
            case "sprint":
                f.Velocity = forward * 5.5f;
                break;
            case "crouch":
                f.Stance = CharacterStance.Crouch;
                break;
            case "crouch walk":
                f.Stance = CharacterStance.Crouch;
                f.Velocity = forward * 1.3f;
                break;
            case "strafe left":
                f.Velocity = -right * 2.8f;
                break;
            case "strafe right":
                f.Velocity = right * 2.8f;
                break;
            case "back":
                f.Velocity = -forward * 2.8f;
                break;
            case "jump":
                // A jump every 2.4 s: take-off, flight, landing.
                _jumpClock = (_jumpClock + dt) % 2.4f;
                f.Grounded = _jumpClock > 1.0f;
                f.Velocity = Vector3.Up * (f.Grounded ? 0f : 7f - 16f * _jumpClock);
                break;
            case "fall":
                f.Grounded = false;
                f.Velocity = Vector3.Down * 6f;
                break;
            case "swim idle":
                f.Stance = CharacterStance.Swim;
                break;
            case "swim":
                f.Stance = CharacterStance.Swim;
                f.Velocity = forward * 2f;
                break;
            case "down":
                f.Stance = CharacterStance.Crawl;
                break;
            case "crawl":
                f.Stance = CharacterStance.Crawl;
                f.Velocity = forward * 0.8f;
                break;
            case "sleep":
                f.Stance = CharacterStance.Sleep;
                break;
        }
        f.Aiming = _aiming;
        if (_options.ContainsKey("slope"))
        {
            f.Position = new Vector3(4f, 0.35f, 0f);
        }
        return f;
    }

    private void UpdateCrowd(float dt)
    {
        for (int i = 0; i < _crowd.Count; i++)
        {
            (CharacterView view, float radius, float phase, float _, HeldItemModel? held, string motion) = _crowd[i];
            float speed = motion switch { "walk" => 1.25f, "jog" => 2.8f, "sprint" => 5.5f, "crouch walk" => 1.3f, "crawl" => 0.8f, _ => 0f };
            phase += speed / radius * dt;
            _crowd[i] = (view, radius, phase, speed, held, motion);
            int row = i / 10, column = i % 10;
            Vector3 centre = new((column - 4.5f) * 3.2f, 0f, -6f - row * 4.5f);
            Vector3 position = speed > 0f ? centre + new Vector3(MathF.Cos(phase), 0, MathF.Sin(phase)) * 1.2f : centre;
            Vector3 tangent = new(-MathF.Sin(phase), 0, MathF.Cos(phase));
            float yaw = MathF.Atan2(-tangent.X, -tangent.Z);
            view.Apply(new CharacterFrame
            {
                Position = position,
                Up = Vector3.Up,
                Yaw = speed > 0f ? yaw : phase,
                Pitch = 0.1f * MathF.Sin(_time + i),
                Velocity = tangent * speed,
                Stance = motion switch
                {
                    "crouch walk" => CharacterStance.Crouch,
                    "crawl" or "down" => CharacterStance.Crawl,
                    "sleep" => CharacterStance.Sleep,
                    _ => CharacterStance.Stand,
                },
                Grounded = true,
                Held = held?.Kind ?? HeldKind.Unarmed,
                HeldItem = held?.Id,
                // Standing still with a bow or a gun: drawn, or raised.
                Aiming = speed == 0f && held != null && (held.Kind is HeldKind.Pistol or HeldKind.Rifle || held.Id == "bow"),
            });
        }
    }

    // ---------------------------------------------------------------- automation

    private void Automate(double delta)
    {
        double measure = Number("measure", 0);
        double wait = Number("wait", 2.5);
        if (measure > 0 && _elapsed > wait + measure && !_capturing)
        {
            Report(wait);
            if (!_options.ContainsKey("shot"))
            {
                GetTree().Quit();
                return;
            }
        }
        if (!_options.TryGetValue("shot", out string? path) || _capturing)
        {
            return;
        }
        if (_elapsed >= wait + Math.Max(0, measure))
        {
            _capturing = true;
            Capture(path);
        }
    }

    private void Report(double skip)
    {
        var times = new List<double>();
        double total = 0;
        double t = 0;
        foreach (double d in _frameTimes)
        {
            t += d;
            if (t > skip)
            {
                times.Add(d);
                total += d;
            }
        }
        if (times.Count == 0)
        {
            return;
        }
        times.Sort();
        double average = total / times.Count;
        double p99 = times[(int)(times.Count * 0.99)];
        GD.Print($"[measure] characters={_crowd.Count + 1} frames={times.Count} avg={average * 1000:0.00} ms ({1 / average:0} fps) " +
            $"p99={p99 * 1000:0.00} ms worst={times[^1] * 1000:0.00} ms window={Number("measure", 0):0}s " +
            $"size={GetViewport().GetVisibleRect().Size} gpu={RenderingServer.GetVideoAdapterName()}");
    }

    private async void Capture(string path)
    {
        for (int i = 0; i < 3; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        Error error = Save(image, path);
        GD.Print(error == Error.Ok ? $"Saved screenshot {path} ({image.GetWidth()}x{image.GetHeight()})." : $"Could not save {path}: {error}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    // A picture as PNG, or as JPEG when the path says .jpg (smaller, for the docs).
    private static Error Save(Image image, string path) =>
        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? image.SaveJpg(path, 0.88f) : image.SavePng(path);

    private string Option(string key, string fallback) => _options.TryGetValue(key, out string? v) ? v : fallback;

    private double Number(string key, double fallback) =>
        _options.TryGetValue(key, out string? v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;
}
