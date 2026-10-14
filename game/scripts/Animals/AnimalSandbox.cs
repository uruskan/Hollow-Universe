using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Surface;
using HollowUniverse.App;
using HollowUniverse.Audio;
using HollowUniverse.Characters;
using HollowUniverse.Common;
using HollowUniverse.Fx;
using HollowUniverse.Surface;
using Num = System.Numerics;
using Hollow.Simulation.Targets;

namespace HollowUniverse.Animals;

/// <summary>
/// The animal sandbox (GROUND.md §6.9): <c>--view animals</c>. A zone of the game's rules runs in this process on the
/// first world's real surface, drawn by the surface view in the zone's frame, with its animals drawn by
/// <see cref="AnimalsView"/> exactly as the ground game draws replicated ones. Scenes, each at its own clearing found
/// near the landing site (<c>--scene</c>, or the keys 1-5):
/// <list type="bullet">
/// <item><c>herd</c>: deer grazing and wandering in a meadow;</item>
/// <item><c>wolves</c>: a hungry pack stalking a test dummy (a player that heals at once) and biting it;</item>
/// <item><c>bear</c>: a bear warning off the dummy (rearing, a roar), then charging;</item>
/// <item><c>harvest</c>: a player butchering a deer with a bone knife (the real swings and yields);</item>
/// <item><c>cooking</c>: meat on a campfire at dusk, raw, cooked and burnt, one piece cooking through.</item>
/// </list>
/// Other options: <c>--hour h</c> (local solar time), <c>--shot</c> (waits for the scene's moment), <c>--hide-ui</c>.
/// Keys: 1-5 scenes, [ and ] the hour, the wheel zooms, the right button drags the view round.
/// </summary>
public partial class AnimalSandbox : Node3D
{
    private const double TickSeconds = 1.0 / CharacterMotor.TickRate;

    public static readonly string[] SceneNames = { "herd", "wolves", "bear", "harvest", "cooking" };

    private sealed class Person
    {
        public required Entity Entity;
        public required CharacterView View;
        public required Player Player;
        public HeldKind Kind;
        public string? Held;
        public byte Actions;
        public Num.Vector3 Previous;
        public Num.Vector3 Current;
        public bool Dummy;
    }

    private sealed class Scene
    {
        public required string Name;
        public required Num.Vector3 Site;

        /// <summary>The way the camera looks across the clearing (towards the trees), flat.</summary>
        public Num.Vector3 Toward;

        public double Hour = 10;

        /// <summary>The camera's vertical field of view, degrees.</summary>
        public float Fov = 50;
        public Func<(Num.Vector3 Eye, Num.Vector3 At)> Camera = null!;
        public Func<bool> Moment = () => true;
        public Func<string>? Trace;
        public Action? Tick;
        public Action<double>? Frame;
        public bool Built;
        public Action Build = null!;
    }

    private readonly List<Scene> _scenes = new();
    private readonly List<Person> _people = new();
    private readonly Dictionary<EntityId, (Num.Vector3 Position, float Yaw)> _before = new();
    private readonly List<AnimalFrame> _frames = new();
    private GameContent _content = null!;
    private Zone _zone = null!;
    private SurfaceView _world = null!;
    private AnimalsView _animals = null!;
    private AudioDirector? _audio;
    private Label? _caption;
    private Scene? _scene;
    private double _accumulator;
    private double _sceneTime;
    private double _elapsed;
    private bool _shot;
    private float _orbit;
    private float _zoom = 1;
    private Vector3 _eye;
    private Vector3 _at;
    private bool _cameraPlaced;

    public Dictionary<string, string> Options { get; set; } = new();

    /// <summary>For automated screenshots: the ground drawn and the scene at its moment.</summary>
    public bool ReadyForShot => _scene != null && _world.IsReady && _sceneTime > 1 && _scene.Moment();

    public Zone Zone => _zone;

    public override void _Ready()
    {
        _shot = Options.ContainsKey("shot");
        _content = GameContent.Load(ProjectSettings.GlobalizePath("res://data/ground"));
        ZoneSpec spec = ZoneSpec.FirstWorld(GalaxyService.Galaxy, FirstWorld.LandingLatitude, FirstWorld.LandingLongitude)
            ?? throw new InvalidOperationException("The generator no longer makes the first world.");
        _zone = new Zone(spec, content: _content) { UnixMilliseconds = 1_800_000_000_000 };
        PlanetSurfaces.Entry found = PlanetSurfaces.Find(spec.SystemAddress, spec.BodyPath)
            ?? throw new InvalidOperationException("The first world has no surface.");

        _world = new SurfaceView { Name = "World", Driven = true };
        AddChild(_world);
        _world.OpenZone(found.System, found.Body, found.Surface, _zone.Frame.Origin, Hollow.Core.UniverseTime.Now, spec.RotationTimeScale);
        _world.Camera.Fov = 50;
        _world.Camera.Near = 0.05f;
        _world.Camera.Current = true;
        FxDirector.Install(this);
        // As in the ground game: the haze bends a screen copy this renderer setup doesn't keep.
        FxDirector.HeatHaze = false;
        if (!Options.ContainsKey("mute"))
        {
            _audio = new AudioDirector { Name = "Audio", Listener = _world.Camera };
            AddChild(_audio);
            if (Options.ContainsKey("sound-trace"))
            {
                _audio.Started += (key, layer, distance, db, pitch) => GD.Print($"[sound] {key} {db:0.0} dB at {distance:0} m");
            }
        }
        _animals = new AnimalsView { Name = "Animals", Source = () => _frames, Eye = () => _world.Camera.GlobalPosition };
        AddChild(_animals);

        FindClearings();
        string wanted = Options.TryGetValue("scene", out string? s) ? s : "herd";
        bool all = wanted == "all" || !_shot;
        foreach (Scene scene in _scenes)
        {
            if (all || scene.Name == wanted)
            {
                scene.Build();
                scene.Built = true;
            }
        }
        if (!Options.ContainsKey("hide-ui") && !_shot)
        {
            var layer = new CanvasLayer { Name = "Interface" };
            AddChild(layer);
            _caption = Ui.Label("", TextStyle.Caption);
            layer.AddChild(_caption);
            Ui.Place(_caption, Control.LayoutPreset.TopLeft, 14);
        }
        Open(_scenes.Find(x => x.Name == wanted) ?? _scenes[0]);
    }

    public override void _ExitTree() => _zone.Dispose();

    private void Open(Scene scene)
    {
        if (!scene.Built)
        {
            scene.Build();
            scene.Built = true;
        }
        _scene = scene;
        _sceneTime = 0;
        _world.Camera.Fov = scene.Fov;
        _cameraPlaced = false;
        _orbit = 0;
        _zoom = 1;
        double hour = Options.TryGetValue("hour", out string? h) ? double.Parse(h, CultureInfo.InvariantCulture) : scene.Hour;
        SetHour(scene.Site, hour);
        if (_caption != null)
        {
            _caption.Text = $"Animals: {scene.Name}   1 herd  2 wolves  3 bear  4 harvest  5 cooking   [ ] hour   wheel zoom   right-drag turn";
        }
    }

    // The sky at a local solar hour over a point (the client's clock only; the zone has no time of day of its own).
    private void SetHour(Num.Vector3 site, double hour)
    {
        if (_world.Sky is not { } sky)
        {
            return;
        }
        Hollow.Core.Vector3D up = _world.Frame!.ToBody(ToRender(site)).Normalized;
        double now = sky.Ephemeris.LocalSolarHours(up, sky.Sun.Index, _world.ShownTime);
        double ahead = ((hour - now) % 24 + 24) % 24;
        _world.JumpClock(ahead / 24 * Math.Abs(sky.Ephemeris.Planet.RotationPeriod));
        _world.SetTime(_world.ShownTime, paused: _shot);
    }

    // ---------------------------------------------------------------- the loop

    public override void _Process(double delta)
    {
        _elapsed += delta;
        // The world held still until the ground around is drawn, so a scene's moment isn't missed while it streams.
        bool waiting = !_world.IsReady && _elapsed < 30;
        _sceneTime += waiting ? 0 : delta;
        _accumulator += waiting ? 0 : Math.Min(delta, 0.25);
        int steps = 0;
        while (_accumulator >= TickSeconds && steps < 4)
        {
            _accumulator -= TickSeconds;
            steps++;
            Step();
        }
        // Effects and glowing things follow the camera's exposure, as in the ground game.
        HollowUniverse.Ground.Glow.Exposure = _world.Sky?.Exposure ?? 1;
        FxDirector.Brightness = 1.3f / HollowUniverse.Ground.Glow.Exposure;
        float alpha = (float)Math.Clamp(_accumulator / TickSeconds, 0, 1);
        Frames(alpha);
        _animals.Apply(_frames, delta);
        DrawPeople(alpha);
        foreach (Scene scene in _scenes)
        {
            if (scene.Built)
            {
                scene.Frame?.Invoke(delta);
            }
        }
        PlaceCamera(delta);
        if (Options.ContainsKey("trace") && (int)(_elapsed - delta) != (int)_elapsed)
        {
            Trace();
        }
    }

    // --trace: once a second, where the scene's animals are and what they do, as seen from the camera.
    private void Trace()
    {
        Vector3 eye = _world.Camera.GlobalPosition;
        var parts = new List<string>();
        foreach (Animal a in _zone.Animals().World!.All)
        {
            float d = eye.DistanceTo(ToRender(a.Position));
            if (d < 120 && !a.Removed)
            {
                AnimalView? v = _animals.ViewOf((uint)a.Id.Value);
                Vector3 at = ToRender(a.Position);
                string seen = _world.Camera.IsPositionBehind(at) ? "behind" : _world.Camera.UnprojectPosition(at).ToString("0");
                parts.Add($"{a.Species.Key} {a.State}{(a.Warning ? "!" : "")} {d:0}m v={a.Speed:0.0} {v?.Playing} {(v?.Visible == true ? "shown" : "hidden")} {seen}");
            }
        }
        GD.Print($"[animals] t={_elapsed:0} {_scene?.Name} moment={_scene?.Moment()} ready={_world.IsReady} {_scene?.Trace?.Invoke()} | " + string.Join("; ", parts));
    }

    private void Step()
    {
        AnimalWorld animals = _zone.Animals().World!;
        _before.Clear();
        foreach (Animal a in animals.All)
        {
            _before[a.Id] = (a.Position, a.Yaw);
        }
        foreach (Person p in _people)
        {
            p.Previous = _zone.Entities.Get<Character>(p.Entity).State.Position;
        }
        foreach (Scene scene in _scenes)
        {
            if (scene.Built)
            {
                scene.Tick?.Invoke();
            }
        }
        // The camera keeps the animals near it awake without being seen.
        animals.Watchers.Clear();
        animals.Watchers.Add(ToZone(_world.Camera.GlobalPosition));
        _zone.UnixMilliseconds += 1000 / CharacterMotor.TickRate;
        _zone.Step();
        foreach (ZoneEvent e in _zone.GameEvents)
        {
            if (e.Kind == ZoneEventKind.HitConfirmed && e.Target.Family == TargetFamily.Animal && _audio != null)
            {
                _audio.Play("melee.hit.flesh", ToRender(e.Position));
            }
        }
        _zone.GameEvents.Clear();
        foreach (Person p in _people)
        {
            p.Current = _zone.Entities.Get<Character>(p.Entity).State.Position;
            if (p.Dummy)
            {
                // A test dummy: whatever bites it, it stands.
                p.Player.Vitals.Health = 100;
                p.Player.Vitals.Bleeding = 0;
            }
        }
    }

    // Every animal between the last two ticks, so they move smoothly at any frame rate.
    private void Frames(float alpha)
    {
        _frames.Clear();
        foreach (Animal a in _zone.Animals().World!.All)
        {
            if (a.Removed)
            {
                continue;
            }
            Num.Vector3 position = a.Position;
            float yaw = a.Yaw;
            if (_before.TryGetValue(a.Id, out var was) && !a.IsDead)
            {
                position = Num.Vector3.Lerp(was.Position, a.Position, alpha);
                yaw = was.Yaw + AnimalWorldAngles.Wrap(a.Yaw - was.Yaw) * alpha;
            }
            _frames.Add(AnimalsView.Of(a, _zone.Frame, position, yaw));
        }
    }

    // ---------------------------------------------------------------- people

    private Person AddPerson(string name, Num.Vector3 at, float yaw, string? held = null, bool dummy = false)
    {
        Num.Vector3 ground = _zone.GroundAt(at.X, at.Z);
        Entity e = _zone.Players().AddPlayer(_zone.NextId(), name, _zone.NewCharacter(ground + _zone.Frame.UpAt(ground) * 0.02f));
        Player player = _zone.Players().PlayerOf(e)!;
        player.Inventory.Clear();
        player.Vitals.Health = 100;
        if (held != null)
        {
            player.Inventory.Give(_content, _content.ItemByKey(held), 1);
            _zone.Actions().MoveItem(e, Hollow.Simulation.Items.ContainerRef.Main, 0, Hollow.Simulation.Items.ContainerRef.Belt, 0, 0, 0);
        }
        ref Character c = ref _zone.Entities.Get<Character>(e);
        c.Input = new MoveInput { Yaw = yaw };
        c.Actions = new ActionInput { HeldSlot = held != null ? (byte)0 : ActionInput.NoSlot };
        var view = new CharacterView { Name = name, Body = BodyType.Male };
        AddChild(view);
        view.Wear(new[] { Wardrobe.WovenShirt, Wardrobe.WovenTrousers, Wardrobe.LacedBoots });
        HeldItemModel? model = held != null ? HeldItems.Find(held) : null;
        var person = new Person
        {
            Entity = e, View = view, Player = player, Dummy = dummy,
            Kind = model?.Kind ?? HeldKind.Unarmed, Held = model?.Id,
            Previous = ground, Current = ground,
        };
        _people.Add(person);
        return person;
    }

    /// <summary>Points a person's view (and so their swings) at a point.</summary>
    private void Look(Person p, Num.Vector3 at)
    {
        ref Character c = ref _zone.Entities.Get<Character>(p.Entity);
        Num.Vector3 eye = _zone.EyeOf(c.State);
        Num.Vector3 up = _zone.Frame.UpAt(c.State.Position);
        Num.Vector3 d = at - eye;
        Num.Vector3 flat = d - up * Num.Vector3.Dot(d, up);
        c.Input.Yaw = YawOf(flat);
        c.Input.Pitch = MathF.Atan2(Num.Vector3.Dot(d, up), flat.Length());
    }

    private void DrawPeople(float alpha)
    {
        foreach (Person p in _people)
        {
            ref Character c = ref _zone.Entities.Get<Character>(p.Entity);
            Num.Vector3 position = Num.Vector3.Lerp(p.Previous, p.Current, alpha);
            Num.Vector3 up = _zone.Frame.UpAt(position);
            p.View.Apply(new CharacterFrame
            {
                Position = ToRender(position),
                Up = ToRender(up),
                Yaw = c.Input.Yaw,
                Pitch = c.Input.Pitch,
                Velocity = ToRender(c.State.Velocity),
                Stance = c.State.Stance is Stance.Crouching or Stance.Crawling ? CharacterStance.Crouch : CharacterStance.Stand,
                Grounded = true,
                Held = p.Kind,
                HeldItem = p.Held,
            });
            if (p.Player.ActionCount != p.Actions)
            {
                p.Actions = p.Player.ActionCount;
                p.View.Trigger(CharacterEvent.Swing);
                _audio?.Play(SoundLibrary.Data.ItemEvent(p.Held ?? "", "swing") ?? "melee.swing", ToRender(position + up * 1.2f));
            }
        }
    }

    // ---------------------------------------------------------------- camera and keys

    private void PlaceCamera(double delta)
    {
        if (_scene == null)
        {
            return;
        }
        (Num.Vector3 eyeZone, Num.Vector3 atZone) = _scene.Camera();
        Vector3 at = ToRender(atZone);
        Vector3 eye = ToRender(eyeZone);
        Vector3 up = ToRender(_zone.Frame.UpAt(atZone));
        // The view turned round the point looked at and moved in or out.
        Vector3 offset = (eye - at).Rotated(up, _orbit) * _zoom;
        eye = at + offset;
        float k = _cameraPlaced ? 1 - MathF.Exp(-(float)delta * 3) : 1;
        _eye = _cameraPlaced ? _eye.Lerp(eye, k) : eye;
        _at = _cameraPlaced ? _at.Lerp(at, k) : at;
        _cameraPlaced = true;
        // Never under the ground.
        Num.Vector3 ground = _zone.GroundAt(_eye.X, _eye.Z);
        Vector3 floor = ToRender(ground) + up * 0.4f;
        if ((_eye - floor).Dot(up) < 0)
        {
            _eye += up * (floor - _eye).Dot(up);
        }
        _world.Camera.GlobalTransform = new Transform3D(Basis.LookingAt(_at - _eye, up), _eye);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true, Echo: false } key when key.Keycode is >= Key.Key1 and <= Key.Key5:
                Open(_scenes[(int)(key.Keycode - Key.Key1) % _scenes.Count]);
                break;
            case InputEventKey { Pressed: true } key when key.Keycode is Key.Bracketleft or Key.Bracketright && _scene != null:
                _world.JumpClock((key.Keycode == Key.Bracketright ? 1 : -1) / 24.0 * Math.Abs(_world.Sky?.Ephemeris.Planet.RotationPeriod ?? 0));
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _zoom = Math.Max(0.3f, _zoom * 0.9f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _zoom = Math.Min(4f, _zoom * 1.1f);
                break;
            case InputEventMouseMotion motion when (motion.ButtonMask & MouseButtonMask.Right) != 0:
                _orbit -= motion.Relative.X * 0.005f;
                break;
        }
    }

    // ---------------------------------------------------------------- helpers

    public static Vector3 ToRender(Num.Vector3 v) => new(v.X, v.Y, v.Z);

    public static Num.Vector3 ToZone(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>The characters' yaw of a flat direction: 0 faces −Z, anticlockwise from above.</summary>
    public static float YawOf(Num.Vector3 d) => MathF.Atan2(-d.X, -d.Z);
}

/// <summary>Angles for drawing: the shortest turn between two headings.</summary>
public static class AnimalWorldAngles
{
    public static float Wrap(float a)
    {
        a %= MathF.Tau;
        return a > MathF.PI ? a - MathF.Tau : a < -MathF.PI ? a + MathF.Tau : a;
    }
}
