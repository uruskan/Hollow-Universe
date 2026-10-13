using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Hollow.Client.Audio;
using Hollow.Surface;
using HollowUniverse.Common;

namespace HollowUniverse.Audio;

/// <summary>
/// The audition viewer (<c>--view sounds</c>, GROUND.md §4.9): every event of the catalogue in a list (a click plays
/// it), its settings and the loudness of each variant as the pipeline measured it and as the engine decodes it; a 3D
/// test where the source walks away from the listener (distance layers crossfade, far shots arrive late); and the
/// ambience by biome, hour, wind, sea, forest, height and storm, with the level of each bed.
/// Keys: Space play, W walk away, S stop, ↑/↓ the event, A ambience on/off, B the next biome.
/// Options: <c>--sound key</c>, <c>--play</c>, <c>--walk</c>, <c>--distance m</c>, <c>--ambience</c>,
/// <c>--biome name</c>, <c>--hour h</c>, <c>--wind 0–1</c>, <c>--sea m</c>, <c>--forest 0–1</c>, <c>--altitude m</c>,
/// <c>--storm 0–1</c>; <c>--sound-check</c> loads and measures every file in the engine, compares it with the
/// pipeline's measurements and quits with exit code 0 when all agree.
/// </summary>
public partial class SoundViewer : Node3D
{
    public Dictionary<string, string> Options { get; set; } = new();

    public bool ReadyForShot => _frames > 40;

    private static readonly (string Name, Biome Biome)[] Biomes =
    {
        ("Meadow", Biome.Grassland), ("Forest", Biome.TemperateForest), ("Taiga", Biome.BorealForest),
        ("Beach", Biome.Beach), ("Tundra", Biome.Tundra), ("Alpine", Biome.Alpine), ("Desert", Biome.Desert),
    };

    private AudioDirector _director = null!;
    private AmbienceDirector _ambience = null!;
    private Node3D _emitter = null!;
    private readonly List<(SoundEventData Event, Button Button)> _rows = new();
    private SoundEventData? _selected;
    private int _frames;

    private bool _walking;
    private float _distance = 4f;
    private float _speed;
    private double _replay;
    private SoundHandle _loop = SoundHandle.None;

    private bool _ambienceOn;
    private int _biome = 1;
    private double _hour = 9;
    private double _wind = 0.3;
    private double _sea = 300;
    private double _forest = 0.5;
    private double _altitude = 40;
    private double _storm;

    private Label _title = null!;
    private Label _stats = null!;
    private GridContainer _info = null!;
    private GridContainer _loudness = null!;
    private Label _target = null!;
    private Label _where = null!;
    private Label _layers = null!;
    private Label _ambienceState = null!;
    private GridContainer _beds = null!;
    private readonly Dictionary<string, Label> _bedLabels = new(StringComparer.Ordinal);
    private Button _ambienceButton = null!;
    private readonly List<(string Key, ButtonGroup Group, Button[] Buttons)> _segments = new();

    public override void _Ready()
    {
        _director = new AudioDirector { Name = "Audio" };
        AddChild(_director);
        _director.Seed(7);
        // The viewer's ground has no collision worth occluding by.
        _director.OcclusionTest = (_, _) => false;
        _ambience = new AmbienceDirector { Name = "Ambience", Director = _director };
        AddChild(_ambience);
        if (Options.ContainsKey("sound-check"))
        {
            CallDeferred(MethodName.SoundCheck);
            return;
        }
        BuildWorld();
        BuildUi();
        ReadOptions();
    }

    // ---------------------------------------------------------------- world

    private void BuildWorld()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color(0.28f, 0.42f, 0.62f),
                    SkyHorizonColor = new Color(0.66f, 0.71f, 0.76f),
                    GroundHorizonColor = new Color(0.4f, 0.42f, 0.38f),
                    GroundBottomColor = new Color(0.2f, 0.21f, 0.19f),
                },
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.7f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color(0.62f, 0.67f, 0.72f),
            FogDensity = 0.0012f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        var sun = new DirectionalLight3D { LightEnergy = 1.1f, ShadowEnabled = true };
        AddChild(sun);
        sun.LookAtFromPosition(new Vector3(-0.5f, 1f, 0.6f), Vector3.Zero, Vector3.Up);
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(6000, 6000) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.27f, 0.31f, 0.24f), Roughness = 1 },
        });
        // Posts every so far along the line the source walks: 10 m to 1.5 km.
        var post = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.82f, 0.74f), Roughness = 0.8f };
        foreach (float d in new[] { 10f, 25f, 50f, 100f, 250f, 500f, 1000f, 1500f })
        {
            float h = 0.8f + MathF.Log10(d) * 1.4f;
            var m = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.25f * MathF.Max(1, d / 60f), h, 0.25f * MathF.Max(1, d / 60f)) },
                MaterialOverride = post,
                Position = new Vector3(2.5f + d * 0.02f, h / 2, -d),
            };
            AddChild(m);
        }
        _emitter = new Node3D { Name = "Emitter", Position = new Vector3(0, 1, -_distance) };
        AddChild(_emitter);
        _emitter.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.35f, Height = 0.7f },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.6f, 0.25f),
                EmissionEnabled = true,
                Emission = new Color(1f, 0.55f, 0.2f),
                EmissionEnergyMultiplier = 0.6f,
            },
        });
        var camera = new Camera3D { Name = "Camera", Fov = 60, Current = true, Far = 4000, Position = new Vector3(0, 1.7f, 0) };
        AddChild(camera);
        camera.RotationDegrees = new Vector3(-6, 0, 0);
    }

    // ---------------------------------------------------------------- interface

    private void BuildUi()
    {
        var layer = new CanvasLayer { Name = "Ui" };
        AddChild(layer);
        var root = new Control { Theme = Ui.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);

        // Left: the events.
        (PanelContainer left, VBoxContainer list) = Ui.Card(6);
        Ui.Place(left, Control.LayoutPreset.LeftWide);
        Ui.Size(left, 330);
        root.AddChild(left);
        list.AddChild(Ui.Label("Sounds", TextStyle.Title));
        SoundCatalogData data = SoundLibrary.Data;
        int files = data.Events.Sum(e => e.AllFiles.Count());
        _stats = Ui.Label($"{data.Events.Count} events · {files} files", TextStyle.Caption);
        list.AddChild(_stats);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        list.AddChild(scroll);
        var rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        Ui.Track(rows, r => r.AddThemeConstantOverride("separation", Ui.Px(1)));
        scroll.AddChild(rows);
        var group = new ButtonGroup();
        string last = "";
        foreach (SoundEventData e in data.Events)
        {
            string head = e.Key.Split('.')[0];
            if (head != last)
            {
                last = head;
                Label h = Ui.Label(GroupName(head), TextStyle.Overline);
                rows.AddChild(h);
            }
            var button = new Button
            {
                Text = e.Key,
                ThemeTypeVariation = "ListButton",
                ToggleMode = true,
                ButtonGroup = group,
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
            };
            SoundEventData captured = e;
            button.Pressed += () =>
            {
                Select(captured);
                PlaySelected();
            };
            rows.AddChild(button);
            _rows.Add((e, button));
        }

        // Right: the selected event.
        (PanelContainer right, VBoxContainer info) = Ui.Card(6);
        Ui.Place(right, Control.LayoutPreset.TopRight);
        Ui.Size(right, 420);
        root.AddChild(right);
        _title = Ui.Label("", TextStyle.Heading);
        info.AddChild(_title);
        _info = Ui.Rows();
        info.AddChild(_info);
        _layers = Ui.Label("", TextStyle.Secondary);
        _layers.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        info.AddChild(_layers);
        info.AddChild(Ui.Separator());
        info.AddChild(Ui.Label("Loudness", TextStyle.Overline));
        _target = Ui.Label("", TextStyle.Secondary);
        _target.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        info.AddChild(_target);
        _loudness = new GridContainer { Columns = 4 };
        Ui.Track(_loudness, g =>
        {
            g.AddThemeConstantOverride("h_separation", Ui.Px(14));
            g.AddThemeConstantOverride("v_separation", Ui.Px(3));
        });
        info.AddChild(_loudness);
        info.AddChild(Ui.Separator());
        _where = Ui.Label("", TextStyle.Secondary);
        info.AddChild(_where);
        var buttons = new HBoxContainer();
        Ui.Track(buttons, b => b.AddThemeConstantOverride("separation", Ui.Px(8)));
        buttons.AddChild(MakeButton("Play", "PrimaryButton", PlaySelected));
        buttons.AddChild(MakeButton("Walk away", "Button", StartWalk));
        buttons.AddChild(MakeButton("Stop", "Button", StopAll));
        info.AddChild(buttons);
        info.AddChild(Ui.KeyHints(("Space", "play"), ("W", "walk away"), ("S", "stop"), ("↑|↓", "event"),
            ("A", "ambience"), ("B", "biome")));

        // Bottom: the ambience.
        (PanelContainer amb, VBoxContainer ambience) = Ui.Card(5);
        Ui.Place(amb, Control.LayoutPreset.CenterBottom);
        Ui.Size(amb, 640);
        root.AddChild(amb);
        var head2 = new HBoxContainer();
        Ui.Track(head2, h => h.AddThemeConstantOverride("separation", Ui.Px(12)));
        Label ambTitle = Ui.Label("Ambience", TextStyle.Overline);
        ambTitle.VerticalAlignment = VerticalAlignment.Center;
        head2.AddChild(ambTitle);
        _ambienceButton = MakeButton("Off", "Button", ToggleAmbience);
        head2.AddChild(_ambienceButton);
        _ambienceState = Ui.Label("", TextStyle.Caption);
        _ambienceState.VerticalAlignment = VerticalAlignment.Center;
        head2.AddChild(_ambienceState);
        ambience.AddChild(head2);
        GridContainer controls = Ui.Rows();
        ambience.AddChild(controls);
        Segments(controls, "biome", "Biome", Biomes.Select(b => b.Name).ToArray(), _biome, i => _biome = i);
        Segments(controls, "hour", "Hour", new[] { "0", "3", "6", "9", "12", "15", "18", "21" }, 3, i => _hour = i * 3);
        Segments(controls, "wind", "Wind", new[] { "0", "0.25", "0.5", "0.75", "1" }, 1, i => _wind = i * 0.25);
        Segments(controls, "sea", "Sea", new[] { "10 m", "80 m", "300 m", "1 km", "5 km" }, 2,
            i => _sea = new[] { 10.0, 80, 300, 1000, 5000 }[i]);
        Segments(controls, "forest", "Forest", new[] { "0", "0.3", "0.6", "0.9" }, 1, i => _forest = new[] { 0, 0.3, 0.6, 0.9 }[i]);
        Segments(controls, "altitude", "Height", new[] { "20 m", "400 m", "900 m", "1.8 km" }, 0,
            i => _altitude = new[] { 20.0, 400, 900, 1800 }[i]);
        Segments(controls, "storm", "Storm", new[] { "0", "0.5", "1" }, 0, i => _storm = i * 0.5);
        _beds = new GridContainer { Columns = 6 };
        Ui.Track(_beds, g =>
        {
            g.AddThemeConstantOverride("h_separation", Ui.Px(10));
            g.AddThemeConstantOverride("v_separation", Ui.Px(2));
        });
        ambience.AddChild(_beds);
        foreach ((string key, double _) in new AmbienceLevels().Beds())
        {
            _beds.AddChild(Ui.Label(key.Replace("amb.", ""), TextStyle.Caption));
            Label value = Ui.Label("", TextStyle.Value);
            _bedLabels[key] = value;
            _beds.AddChild(value);
        }
    }

    private static string GroupName(string head) => head switch
    {
        "footstep" => "Footsteps",
        "swim" => "Swimming",
        "gather" => "Gathering",
        "survival" => "Survival",
        "campfire" or "furnace" or "fire" or "box" or "deploy" => "Deployables",
        "build" or "door" or "codelock" => "Building",
        "melee" or "bow" or "arrow" or "pistol" or "rifle" or "reload" or "gun" or "impact" or "hit" => "Weapons",
        "explosive" or "explosion" => "Explosives",
        "ui" => "Interface",
        "amb" => "Ambience",
        _ => head,
    };

    private static Button MakeButton(string text, string variation, Action action)
    {
        var b = new Button { Text = text, ThemeTypeVariation = variation, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += action;
        return b;
    }

    private void Segments(GridContainer grid, string key, string label, string[] names, int selected, Action<int> set)
    {
        Label k = Ui.Label(label, TextStyle.Key);
        k.VerticalAlignment = VerticalAlignment.Center;
        grid.AddChild(k);
        var row = new HBoxContainer();
        Ui.Track(row, r => r.AddThemeConstantOverride("separation", Ui.Px(2)));
        var group = new ButtonGroup();
        var buttons = new Button[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            var b = new Button
            {
                Text = names[i],
                ThemeTypeVariation = "SegmentButton",
                ToggleMode = true,
                ButtonGroup = group,
                FocusMode = Control.FocusModeEnum.None,
                ButtonPressed = i == selected,
            };
            b.Pressed += () => set(index);
            row.AddChild(b);
            buttons[i] = b;
        }
        grid.AddChild(row);
        _segments.Add((key, group, buttons));
    }

    private void SelectSegment(string key, int index)
    {
        foreach ((string k, ButtonGroup _, Button[] buttons) in _segments)
        {
            if (k == key && index >= 0 && index < buttons.Length)
            {
                buttons[index].ButtonPressed = true;
            }
        }
    }

    // ---------------------------------------------------------------- options

    private void ReadOptions()
    {
        string key = Options.TryGetValue("sound", out string? k) ? k : "rifle.shot";
        SoundEventData? e = SoundLibrary.Find(key) ?? SoundLibrary.Data.Events.FirstOrDefault();
        if (e != null)
        {
            Select(e);
        }
        if (Options.TryGetValue("distance", out string? d))
        {
            _distance = (float)Math.Clamp(Number(d, 4), 1, 2500);
            _emitter.Position = new Vector3(0, 1, -_distance);
        }
        if (Options.TryGetValue("biome", out string? biome))
        {
            int i = Array.FindIndex(Biomes, b => string.Equals(b.Name, biome, StringComparison.OrdinalIgnoreCase)
                                                || string.Equals(b.Biome.ToString(), biome, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
            {
                _biome = i;
                SelectSegment("biome", i);
            }
        }
        _hour = Number(Options.GetValueOrDefault("hour"), _hour);
        SelectSegment("hour", (int)Math.Round(_hour / 3) % 8);
        _wind = Math.Clamp(Number(Options.GetValueOrDefault("wind"), _wind), 0, 1);
        SelectSegment("wind", (int)Math.Round(_wind * 4));
        _sea = Number(Options.GetValueOrDefault("sea"), _sea);
        SelectSegment("sea", Nearest(new[] { 10.0, 80, 300, 1000, 5000 }, _sea));
        _forest = Math.Clamp(Number(Options.GetValueOrDefault("forest"), _forest), 0, 1);
        SelectSegment("forest", Nearest(new[] { 0, 0.3, 0.6, 0.9 }, _forest));
        _altitude = Number(Options.GetValueOrDefault("altitude"), _altitude);
        SelectSegment("altitude", Nearest(new[] { 20.0, 400, 900, 1800 }, _altitude));
        _storm = Math.Clamp(Number(Options.GetValueOrDefault("storm"), _storm), 0, 1);
        SelectSegment("storm", (int)Math.Round(_storm * 2));
        if (Options.ContainsKey("ambience"))
        {
            ToggleAmbience();
        }
        if (Options.ContainsKey("walk"))
        {
            StartWalk();
        }
        else if (Options.ContainsKey("play"))
        {
            PlaySelected();
        }
    }

    private static int Nearest(double[] values, double v)
    {
        int best = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (Math.Abs(values[i] - v) < Math.Abs(values[best] - v))
            {
                best = i;
            }
        }
        return best;
    }

    private static double Number(string? s, double fallback) =>
        s != null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    // ---------------------------------------------------------------- the selected event

    private void Select(SoundEventData e)
    {
        _selected = e;
        foreach ((SoundEventData ev, Button b) in _rows)
        {
            if (ev == e && !b.ButtonPressed)
            {
                b.SetPressedNoSignal(true);
            }
        }
        _title.Text = e.Key;
        foreach (Node c in _info.GetChildren())
        {
            c.QueueFree();
        }
        Ui.AddRow(_info, "Category", e.Category, numeric: false);
        Ui.AddRow(_info, "Bus", e.Bus + (e.Positional ? "" : " · flat"), numeric: false);
        Ui.AddRow(_info, "Variants", e.Layered ? string.Join(" + ", e.Layers.Select(l => l.Files.Count)) : e.Files.Count.ToString(CultureInfo.InvariantCulture));
        Ui.AddRow(_info, "Volume", Db(e.Volume) + (e.Jitter > 0 ? $" ± {e.Jitter:0.#}" : ""));
        Ui.AddRow(_info, "Pitch", e.PitchMin == e.PitchMax ? $"{e.PitchMin:0.00}" : $"{e.PitchMin:0.00} … {e.PitchMax:0.00}");
        if (e.Positional && !e.Layered)
        {
            Ui.AddRow(_info, "Falloff", $"from {e.UnitSize:0.#} m, to {e.MaxDistance:0} m");
        }
        Ui.AddRow(_info, "Priority", $"{e.Priority} · {e.Limit} at once" + (e.Loop ? " · loop" : "") + (e.Delay ? " · delayed" : ""));
        _layers.Text = e.Layered
            ? string.Join("\n", e.Layers.Select(l => $"{l.Name}: {l.From:0}–{l.To:0} m (fade {l.Fade:0} m), {Db(l.Volume)}, falloff from {l.UnitSize:0} m"))
            : "";
        _layers.Visible = e.Layered;
        if (SoundLibrary.Data.Categories.TryGetValue(e.Category, out SoundCategory? cat))
        {
            _target.Text = $"{cat.Key}: {(cat.Metric == "integrated" ? "integrated" : "event")} loudness {Lufs(cat.Target)} ({Lufs(cat.Min)} … {Lufs(cat.Max)}), peaks under −1 dBTP";
        }
        FillLoudness(e);
    }

    private void FillLoudness(SoundEventData e)
    {
        foreach (Node c in _loudness.GetChildren())
        {
            c.QueueFree();
        }
        foreach (string h in new[] { "File", "Pipeline", "Engine", "Peak" })
        {
            _loudness.AddChild(Ui.Label(h, TextStyle.Caption));
        }
        bool integrated = SoundLibrary.Data.Categories.TryGetValue(e.Category, out SoundCategory? cat) && cat.Metric == "integrated";
        foreach (string file in e.AllFiles)
        {
            _loudness.AddChild(Ui.Label(file.Split('/')[^1], TextStyle.Secondary));
            var pipe = SoundMeter.Pipeline(file);
            AudioStream? stream = SoundLibrary.Stream(file, e.Loop);
            LoudnessResult? engine = stream == null ? null : SoundMeter.Measure(stream);
            _loudness.AddChild(Ui.Label(pipe is { } p ? Lufs(integrated ? p.Integrated : p.Event) : "–", TextStyle.Value));
            _loudness.AddChild(Ui.Label(engine is { } r ? Lufs(integrated ? r.Integrated : r.Event) : "missing", TextStyle.Value));
            _loudness.AddChild(Ui.Label(engine is { } r2 ? $"{Ui.Number(r2.TruePeak, "0.0")} dBTP" : "–", TextStyle.Value));
        }
    }

    private static string Db(double db) => (db >= 0 ? "+" : "") + Ui.Number(db, "0.#") + " dB";

    private static string Lufs(double v) => Ui.Number(v, "0.0") + " LUFS";

    private void PlaySelected()
    {
        if (_selected == null)
        {
            return;
        }
        if (_selected.Loop)
        {
            if (_director.IsPlaying(_loop))
            {
                _director.Stop(_loop, 0.2f);
                _loop = SoundHandle.None;
                return;
            }
            _loop = _selected.Positional ? _director.Loop(_selected.Key, _emitter) : _director.Loop2D(_selected.Key);
            return;
        }
        if (_selected.Positional)
        {
            _director.Play(_selected.Key, _emitter.GlobalPosition);
        }
        else
        {
            _director.Play2D(_selected.Key);
        }
    }

    private void StartWalk()
    {
        if (_selected == null)
        {
            return;
        }
        _walking = true;
        _distance = 2f;
        float reach = _selected.Layered ? (float)_selected.Layers.Max(l => l.MaxDistance) : (float)_selected.MaxDistance * 1.1f;
        _speed = Math.Clamp(reach / 22f, 1.4f, 90f);
        _replay = 0;
        if (_selected.Loop && !_director.IsPlaying(_loop))
        {
            PlaySelected();
        }
    }

    private void StopAll()
    {
        _walking = false;
        _director.StopAll();
        _loop = SoundHandle.None;
    }

    private void ToggleAmbience()
    {
        _ambienceOn = !_ambienceOn;
        _ambienceButton.Text = _ambienceOn ? "On" : "Off";
        if (!_ambienceOn)
        {
            _ambience.StopAll();
        }
    }

    // ---------------------------------------------------------------- every frame

    public override void _Process(double delta)
    {
        _frames++;
        if (_selected == null)
        {
            return;
        }
        if (_walking)
        {
            float reach = _selected.Layered ? (float)_selected.Layers.Max(l => l.MaxDistance) : (float)_selected.MaxDistance * 1.1f;
            _distance += _speed * (float)delta;
            if (_distance > reach)
            {
                _walking = false;
            }
            _emitter.Position = new Vector3(0, 1, -_distance);
            if (!_selected.Loop)
            {
                _replay -= delta;
                if (_replay <= 0)
                {
                    _replay = _selected.Key.StartsWith("footstep", StringComparison.Ordinal) ? 0.55
                        : _selected.Layered ? 2.2 : 1.4;
                    PlaySelected();
                }
            }
        }
        float distance = _emitter.GlobalPosition.DistanceTo(_director.ListenerPosition());
        string layers = "";
        if (_selected.Layered)
        {
            layers = " · " + string.Join(", ", _selected.Layers.Select(l =>
                $"{l.Name} {DistanceLayers.Weight(distance, l.From, l.To, l.Fade) * 100:0}%"));
            layers += $" · arrives after {DistanceLayers.Delay(distance) * 1000:0} ms";
        }
        (int placed, int flat) = _director.Busy;
        _where.Text = $"Source {Ui.Number(distance, "0")} m away{layers} · voices {placed} placed, {flat} flat";
        if (_ambienceOn)
        {
            var input = new AmbienceInput
            {
                Altitude = _altitude,
                Exposure = Math.Clamp(0.35 + _altitude / 2000.0 - 0.3 * _forest, 0, 1),
                Biome = Biomes[_biome].Biome,
                Forest = _forest,
                SeaDistance = _sea,
                SunElevation = AmbienceInput.SunAt(_hour, 45),
                Wind = _wind,
                Storm = _storm,
            };
            _ambience.Update(delta, input, _director.ListenerPosition());
            AmbienceLevels l = _ambience.Levels;
            foreach ((string key, double level) in l.Beds())
            {
                _bedLabels[key].Text = level < 0.005 ? "–" : $"{20 * Math.Log10(level):0} dB";
            }
            _ambienceState.Text = $"{Biomes[_biome].Name}, sun {input.SunElevation:0}°, accents a minute: owl {l.OwlRate:0.0}, crow {l.CrowRate:0.0}, thunder {l.ThunderRate:0.0}";
        }
        else
        {
            _ambienceState.Text = "A turns it on";
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        switch (key.Keycode)
        {
            case Key.Space:
                PlaySelected();
                break;
            case Key.W:
                StartWalk();
                break;
            case Key.S:
                StopAll();
                break;
            case Key.A:
                ToggleAmbience();
                break;
            case Key.B:
                _biome = (_biome + 1) % Biomes.Length;
                SelectSegment("biome", _biome);
                break;
            case Key.Up:
            case Key.Down:
                int i = _rows.FindIndex(r => r.Event == _selected);
                i = Math.Clamp(i + (key.Keycode == Key.Up ? -1 : 1), 0, _rows.Count - 1);
                Select(_rows[i].Event);
                break;
        }
    }

    // ---------------------------------------------------------------- the check

    /// <summary>Loads every file of every event and measures it in the engine; quits 0 when all agree with the pipeline.</summary>
    private void SoundCheck()
    {
        try
        {
            RunSoundCheck();
        }
        catch (Exception e)
        {
            GD.PrintErr("[sound-check] failed: " + e);
            GetTree().Quit(1);
        }
    }

    private void RunSoundCheck()
    {
        SoundCatalogData data = SoundLibrary.Data;
        int files = 0, failures = 0;
        double worstLoud = 0, worstPeak = -100;
        foreach (SoundEventData e in data.Events)
        {
            bool integrated = data.Categories.TryGetValue(e.Category, out SoundCategory? cat) && cat.Metric == "integrated";
            foreach (string file in e.AllFiles)
            {
                files++;
                AudioStream? stream = SoundLibrary.Stream(file, e.Loop);
                if (stream == null)
                {
                    GD.PrintErr($"[sound-check] {e.Key}: {file} doesn't load");
                    failures++;
                    continue;
                }
                LoudnessResult r = SoundMeter.Measure(stream);
                var p = SoundMeter.Pipeline(file);
                if (p == null)
                {
                    GD.PrintErr($"[sound-check] {e.Key}: {file} has no pipeline measurement");
                    failures++;
                    continue;
                }
                double engine = integrated ? r.Integrated : r.Event;
                double pipeline = integrated ? p.Value.Integrated : p.Value.Event;
                double diff = Math.Abs(engine - pipeline);
                worstLoud = Math.Max(worstLoud, diff);
                worstPeak = Math.Max(worstPeak, r.TruePeak);
                if (diff > 1.5 || r.TruePeak > -0.3)
                {
                    GD.PrintErr($"[sound-check] {e.Key}: {file} engine {engine:0.0} LUFS / {r.TruePeak:0.00} dBTP, pipeline {pipeline:0.0} / {p.Value.TruePeak:0.00}");
                    failures++;
                }
            }
        }
        GD.Print($"[sound-check] {data.Events.Count} events, {files} files at a mix rate of {AudioServer.GetMixRate():0} Hz: " +
                 $"largest loudness difference {worstLoud:0.0} LU, highest true peak {worstPeak:0.00} dBTP, {failures} problems.");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
