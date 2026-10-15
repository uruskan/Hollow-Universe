using System;
using System.Collections.Generic;
using Godot;
using Hollow.Simulation.Ships;
using HollowUniverse.Fx;
using HollowUniverse.Items;

namespace HollowUniverse.Ships;

/// <summary>What a ship is doing this frame, as a <see cref="ShipView"/> draws it.</summary>
/// <param name="Pivot">The model's pivot (the ship frame's origin, on the ground between the skids) in render space, its axes the ship's.</param>
/// <param name="Hatch">How far its first door is open, 0 shut to 1 open: a model with a <c>Hatch</c> node swings it by its open clip (the Tin Finch's).</param>
/// <param name="Thrust"><see cref="ShipState.Thrust"/>: what the engines give on each axis (x + left, y + up, z + forward; above 1 boosting).</param>
public readonly record struct ShipLook(Transform3D Pivot, float Hatch, System.Numerics.Vector3 Thrust, bool Engines, bool Boosting,
    bool Piloted, bool Destroyed);

/// <summary>
/// One ship drawn (G6, GROUND.md §6.7): the catalogue model of its kind (ships.json's <c>model</c>), whose levels of detail
/// switch by distance; the hatch swinging by the model's own open clip; a ship people walk in (IRON_WAKE.md §4) its moving
/// parts posed from their states as its space has them (the ramp, the airlock, the turrets) and its collision hulls kept
/// under the parts they ride, for the feet drawn on its decks; a plume on every nozzle of ships.json that grows with the
/// thrust on its axis (main and side engines forward, lift nozzles up, the small thrusters for sideways, braking and down),
/// sized to the ship; the lamps, nav lights and a landing light while the engines run, and the glow of the lift nozzles on
/// the ground below. Presentation only: the host gives it a <see cref="ShipLook"/> each frame, from the zone, the client's
/// prediction or a snapshot, and the parts' states when they change.
/// </summary>
public partial class ShipView : Node3D
{
    private const float PlumeSmoothing = 0.07f;

    private static ShaderMaterial? _plumeMaterial;
    private static ShaderMaterial? _glowMaterial;
    private static QuadMesh? _quad;

    private readonly ShipDef _def;
    private Node3D _model = null!;
    private Node3D? _hatch;
    private Animation? _hatchClip;
    private int _hatchTrack = -1;
    private Quaternion _hatchRest = Quaternion.Identity;
    private readonly List<Plume> _plumes = new();
    private readonly List<(MeshInstance3D Glow, Color Colour, float Phase, float Period)> _navLights = new();
    private readonly List<(BaseMaterial3D Material, float Energy)> _lamps = new();
    private readonly List<(BaseMaterial3D Material, float Energy)> _displays = new();
    private readonly List<(Light3D Light, ShipLight Def)> _lights = new();
    private OmniLight3D _belly = null!;
    private float _lampEnergy = -1;
    private int _lightsState = -1;
    private float _lightsEnergy = -1;
    private int _probeFrames;
    private double _time;
    private PartNode[] _parts = Array.Empty<PartNode>();
    private float[]? _posedAt;
    private bool _loadersShown;
    private readonly Dictionary<string, Node3D?> _sockets = new();

    /// <summary>A socket of the model by its name (<c>socket_&lt;name&gt;</c>), found once; null when the model has none.</summary>
    public Node3D? Socket(string name)
    {
        if (!_sockets.TryGetValue(name, out Node3D? node))
        {
            node = _model?.FindChild("socket_" + name, true, false) as Node3D;
            _sockets[name] = node;
        }
        return node;
    }

    /// <summary>A node of the model by its name (a loader's piece, a holder), found once; null when the model has none.</summary>
    public Node3D? NodeNamed(string name)
    {
        string key = "#" + name;
        if (!_sockets.TryGetValue(key, out Node3D? node))
        {
            node = _model?.FindChild(name, true, false) as Node3D;
            _sockets[key] = node;
        }
        return node;
    }

    // A moving part's node in the model: its part, where it stands at rest in the model's space, and what carries it
    // there (another part's node, by its index here, or the model's still nodes: their rest in the model's space).
    private readonly record struct PartNode(int Part, Node3D Node, Transform3D Rest, int Carrier, Transform3D CarrierRest);

    private enum Drive : byte { Main, Aux, Lift, RcsLeft, RcsRight }

    private sealed class Plume
    {
        public required MeshInstance3D Gas;
        public required MeshInstance3D Glow;
        public required Drive Drive;
        public required Vector3 Nozzle;
        public required Vector3 Direction;
        public required float Width;
        public required float Length;
        public float Shown;
    }

    public ShipView(ShipDef def)
    {
        _def = def;
        Name = def.Key;
    }

    /// <summary>The model's root (its meshes are in the ship's frame: +Z the nose, +Y up, +X left).</summary>
    public Node3D Model => _model;

    /// <summary>
    /// The camera's exposure (the surface view's <c>Sky.Exposure</c>), so plumes and lamps read alike by day and night.
    /// Shared by every ship view.
    /// </summary>
    public static float Exposure { get; set; } = 1;

    /// <summary>True in the pilot's own cockpit view: the plumes nearest the eye would fill the screen, so they hide.</summary>
    public bool FromCockpit { get; set; }

    /// <summary>The catalogue key of its model: ships.json's <c>model</c>, else the ship's own key.</summary>
    private string ModelKey => string.IsNullOrEmpty(_def.Model) ? _def.Key : _def.Model;

    // Plumes and lights grow with the ship: the Tin Finch's sizes, scaled by the square root of how much bigger it is.
    private float Scale => MathF.Sqrt(MathF.Max(1, _def.BoundingRadius / 3.5f));

    public override void _Ready()
    {
        // A ship people walk in keeps its hulls (each under the part it moves with) on the feet's layer.
        _model = ModelCatalog.Instantiate(ModelKey, use: _def.Interior != null ? ModelUse.Parts : ModelUse.Display) ?? new Node3D { Name = _def.Key };
        AddChild(_model);
        FindHatch();
        FindParts();
        BuildPlumes();
        BuildLights();
        BuildLamps();
        FindLamps();
        BuildProbes();
    }

    // ---------------------------------------------------------------- moving parts (IRON_WAKE.md §4.3)

    private void FindParts()
    {
        if (_def.Interior is not { } interior)
        {
            return;
        }
        var nodes = new List<(int Part, Node3D Node)>();
        for (int part = 1; part < interior.Parts.Count; part++)
        {
            if (_model.FindChild(interior.Parts[part].Name, true, false) is Node3D node)
            {
                nodes.Add((part, node));
            }
        }
        // Carriers before what they carry: by depth in the model.
        nodes.Sort((a, b) => Depth(a.Node).CompareTo(Depth(b.Node)));
        var parts = new List<PartNode>();
        foreach ((int part, Node3D node) in nodes)
        {
            int carrier = -1;
            for (Node? up = node.GetParent(); up != null && up != _model && carrier < 0; up = up.GetParent())
            {
                carrier = parts.FindIndex(p => p.Node == up);
            }
            Node3D? parent = node.GetParent() as Node3D;
            parts.Add(new PartNode(part, node, InModel(node), carrier, parent == null || parent == _model ? Transform3D.Identity : InModel(parent)));
        }
        _parts = parts.ToArray();
        // The model's own clips (the ramp's, the airlock's) are the import's; the view poses the parts from their states.
        if (_parts.Length > 0 && _model.FindChild("AnimationPlayer", true, false) is AnimationPlayer player)
        {
            player.Active = false;
        }
        ShowParts(interior.Rest);
    }

    private int Depth(Node node)
    {
        int depth = 0;
        for (Node? up = node.GetParent(); up != null && up != _model; up = up.GetParent())
        {
            depth++;
        }
        return depth;
    }

    // A node's transform in the model's space (the ship's frame), from the model down to it.
    private Transform3D InModel(Node3D node)
    {
        Transform3D t = node.Transform;
        for (Node? up = node.GetParent(); up is Node3D parent && up != _model; up = up.GetParent())
        {
            t = parent.Transform * t;
        }
        return t;
    }

    /// <summary>
    /// Poses the moving parts at their states (a door's progress, a turret's angle), as the ship's space poses its bodies:
    /// each part's motion from rest, its carriers' included. Nothing happens when they are as last posed.
    /// </summary>
    public void ShowParts(ReadOnlySpan<float> states, IReadOnlyDictionary<int, (System.Numerics.Vector3 Offset, System.Numerics.Quaternion Turn)>? loaders = null)
    {
        bool moved = loaders is { Count: > 0 };
        if (_parts.Length == 0 || _def.Interior is not { } interior || (!moved && !_loadersShown && _posedAt != null && states.SequenceEqual(_posedAt)))
        {
            return;
        }
        _posedAt = states.ToArray();
        _loadersShown = moved;
        Span<Transform3D> inModel = stackalloc Transform3D[_parts.Length];
        for (int i = 0; i < _parts.Length; i++)
        {
            PartNode p = _parts[i];
            PartPose pose = interior.PartPose(p.Part, states);
            var motion = new Transform3D(new Basis(new Quaternion(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W)),
                new Vector3(pose.Translation.X, pose.Translation.Y, pose.Translation.Z));
            inModel[i] = motion * p.Rest;
            if (moved && loaders!.TryGetValue(p.Part, out var extra))
            {
                // A loading mechanism's piece (IRON_WAKE.md §9.5): moved along and turned about its own axes at its pivot,
                // which are the model's axes at rest carried by its parents.
                var local = new Transform3D(new Basis(new Quaternion(extra.Turn.X, extra.Turn.Y, extra.Turn.Z, extra.Turn.W)),
                    new Vector3(extra.Offset.X, extra.Offset.Y, extra.Offset.Z));
                inModel[i] = inModel[i] * local;
            }
            Transform3D carrier = p.Carrier >= 0 ? inModel[p.Carrier] * _parts[p.Carrier].Rest.AffineInverse() * p.CarrierRest : p.CarrierRest;
            p.Node.Transform = carrier.AffineInverse() * inModel[i];
        }
    }

    /// <summary>
    /// A socket of the ship's frame (a seat, a grip) where it is drawn now, in render space: carried by its part as the part
    /// is posed (a gunner's seat on a turning mount), its axes the socket's (+Z forward, +Y up).
    /// </summary>
    public Transform3D SocketWorld(Hollow.Simulation.Ships.ShipSocket socket)
    {
        var fwd = new Vector3(socket.Forward.X, socket.Forward.Y, socket.Forward.Z);
        var up = new Vector3(socket.Up.X, socket.Up.Y, socket.Up.Z);
        if (fwd.LengthSquared() < 1e-6f)
        {
            fwd = Vector3.Back;
        }
        if (up.LengthSquared() < 1e-6f || MathF.Abs(up.Normalized().Dot(fwd.Normalized())) > 0.99f)
        {
            up = Vector3.Up;
        }
        Vector3 z = fwd.Normalized(), x = up.Cross(z).Normalized(), y = z.Cross(x);
        var local = new Transform3D(new Basis(x, y, z), new Vector3(socket.Position.X, socket.Position.Y, socket.Position.Z));
        foreach (PartNode p in _parts)
        {
            if (p.Part == socket.Part && GodotObject.IsInstanceValid(p.Node))
            {
                return _model.GlobalTransform * (InModel(p.Node) * p.Rest.AffineInverse()) * local;
            }
        }
        return _model.GlobalTransform * local;
    }

    // ---------------------------------------------------------------- set-up

    private void FindHatch()
    {
        _hatch = _model.FindChild("Hatch", true, false) as Node3D;
        if (_hatch == null)
        {
            return;
        }
        _hatchRest = _hatch.Quaternion;
        if (_model.FindChild("AnimationPlayer", true, false) is AnimationPlayer player && player.HasAnimation("Hatch_Open"))
        {
            _hatchClip = player.GetAnimation("Hatch_Open");
            player.Active = false; // the view samples the clip itself, by the hatch's progress
            for (int t = 0; t < _hatchClip.GetTrackCount(); t++)
            {
                if (_hatchClip.TrackGetType(t) == Animation.TrackType.Rotation3D)
                {
                    _hatchTrack = t;
                    break;
                }
            }
        }
    }

    private static void EnsureMaterials()
    {
        if (_plumeMaterial != null)
        {
            return;
        }
        var shader = GD.Load<Shader>("res://shaders/ship_exhaust.gdshader");
        _plumeMaterial = new ShaderMaterial { Shader = shader };
        _plumeMaterial.SetShaderParameter("mode", 0);
        _glowMaterial = new ShaderMaterial { Shader = shader };
        _glowMaterial.SetShaderParameter("mode", 1);
        // The vertex shader turns the quad to the camera, outside the quad's own box: a box that holds every way it turns.
        _quad = new QuadMesh { Size = Vector2.One, CustomAabb = new Aabb(new Vector3(-1.5f, -1.5f, -1.5f), new Vector3(3, 3, 3)) };
    }

    private MeshInstance3D Quad(ShaderMaterial material, string name)
    {
        var mesh = new MeshInstance3D
        {
            Name = name,
            Mesh = _quad,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = 2500,
            Visible = false,
        };
        _model.AddChild(mesh);
        return mesh;
    }

    // A plume on every nozzle of ships.json (its drive, its width and length at full thrust): the main and side engines
    // blow aft, the lift nozzles down, the small thrusters outward from their side of the hull (+X is the ship's left).
    private void BuildPlumes()
    {
        EnsureMaterials();
        foreach (ShipNozzle n in _def.Nozzles)
        {
            (Drive drive, Vector3 dir) = n.Drive switch
            {
                NozzleDrive.Main => (Drive.Main, new Vector3(0, 0, -1)),
                NozzleDrive.Aux => (Drive.Aux, new Vector3(0, 0, -1)),
                NozzleDrive.Lift => (Drive.Lift, new Vector3(0, -1, 0)),
                _ => n.At.X >= 0 ? (Drive.RcsLeft, new Vector3(1, 0, 0)) : (Drive.RcsRight, new Vector3(-1, 0, 0)),
            };
            if (drive == Drive.Lift)
            {
                _liftNozzles++;
            }
            _plumes.Add(new Plume
            {
                Gas = Quad(_plumeMaterial!, $"Plume{_plumes.Count}"),
                Glow = Quad(_glowMaterial!, $"Nozzle{_plumes.Count}"),
                Drive = drive,
                Nozzle = new Vector3(n.At.X, n.At.Y, n.At.Z),
                Direction = dir,
                Width = n.Width,
                Length = n.Length,
            });
        }
    }

    private int _liftNozzles;

    /// <summary>How many lift nozzles it has (ships.json <c>nozzles.lift</c>).</summary>
    public int LiftNozzles => _liftNozzles;

    /// <summary>A lift nozzle where it is drawn (render space) and how hard its plume blows now (0 out, 1 full lift).</summary>
    public (Vector3 At, float Shown) LiftNozzle(int index)
    {
        foreach (Plume p in _plumes)
        {
            if (p.Drive == Drive.Lift && index-- == 0)
            {
                return (_model.GlobalTransform * p.Nozzle, p.Shown);
            }
        }
        return (GlobalPosition, 0);
    }

    private void BuildLights()
    {
        // The lift nozzles' light on the ground below: between them, reaching eight times their plumes' length (ships.json).
        float scale = Scale;
        Vector3 liftAt = Vector3.Zero;
        float liftLength = 0, liftWidth = 0;
        foreach (Plume p in _plumes)
        {
            if (p.Drive == Drive.Lift)
            {
                liftAt += p.Nozzle;
                liftLength += p.Length;
                liftWidth += p.Width;
            }
        }
        int lifts = Math.Max(1, _liftNozzles);
        _belly = new OmniLight3D
        {
            Name = "Belly",
            LightColor = new Color(1f, 0.62f, 0.32f),
            OmniRange = _liftNozzles > 0 ? 8 * liftLength / lifts : 14 * scale,
            OmniAttenuation = 1.6f,
            ShadowEnabled = false,
            Position = _liftNozzles > 0 ? liftAt / lifts - Vector3.Up * (liftWidth / lifts) : new Vector3(0, 0.2f, -0.5f) * scale,
            Visible = false,
        };
        _model.AddChild(_belly);
        // Nav lights at the tips of the outermost hull boxes (the Tin Finch's engine pods: red on the left, green on the
        // right), a white beacon over the top of the hull.
        float podX = 1.18f, podY = 0.97f, podZ = -2.95f, top = 1.845f;
        foreach (HullBox box in _def.Hull)
        {
            if (!box.Skid && MathF.Abs(box.Centre.X) > 0.5f)
            {
                podX = MathF.Abs(box.Centre.X) + box.HalfExtents.X;
                podY = box.Centre.Y;
                podZ = box.Centre.Z - box.HalfExtents.Z;
            }
            if (!box.Skid)
            {
                top = MathF.Max(top, box.Centre.Y + box.HalfExtents.Y);
            }
        }
        AddNav(new Vector3(podX, podY + 0.01f, podZ), new Color(1f, 0.08f, 0.04f), 0, 0, scale);
        AddNav(new Vector3(-podX, podY + 0.01f, podZ), new Color(0.1f, 1f, 0.25f), 0, 0, scale);
        AddNav(new Vector3(0, top + 0.075f, -1.4f * scale), new Color(1f, 1f, 1f), 0.15f, 1.6f, scale);
    }

    private void AddNav(Vector3 at, Color colour, float phase, float period, float scale)
    {
        MeshInstance3D glow = Quad(_glowMaterial!, $"Nav{_navLights.Count}");
        glow.Position = at;
        glow.Scale = Vector3.One * 0.32f * scale;
        glow.SetInstanceShaderParameter("tint", colour);
        _navLights.Add((glow, colour, phase, period));
    }

    // A light at every lamp of ships.json's lights (IRON_WAKE.md §9.4): the data file's fixtures, omni or spot, a few casting
    // shadows; the dim red emergency lamps at the same fixtures; a small ship's nav and landing lights. Interior lamps fade
    // out with distance (nobody sees a deck's lamps from 100 m but through its glow).
    private void BuildLamps()
    {
        foreach (ShipLight l in _def.Lights)
        {
            Light3D light;
            var at = new Vector3(l.At.X, l.At.Y, l.At.Z);
            if (l.Spot)
            {
                var dir = new Vector3(l.Direction.X, l.Direction.Y, l.Direction.Z);
                Vector3 up = MathF.Abs(dir.Normalized().Y) > 0.95f ? Vector3.Forward : Vector3.Up;
                light = new SpotLight3D
                {
                    SpotRange = l.Range,
                    SpotAngle = Math.Clamp(l.Angle, 1f, 89f),
                    SpotAttenuation = 1.2f,
                    Transform = new Transform3D(Basis.LookingAt(dir, up), at),
                };
            }
            else
            {
                light = new OmniLight3D { OmniRange = l.Range, OmniAttenuation = 1.4f, Position = at };
            }
            light.Name = "Lamp_" + l.Key.Replace(':', '_');
            light.LightColor = new Color(l.Colour.X, l.Colour.Y, l.Colour.Z);
            light.ShadowEnabled = l.Shadows;
            light.Visible = false;
            if (l.Range < 30)
            {
                light.DistanceFadeEnabled = true;
                light.DistanceFadeBegin = 70;
                light.DistanceFadeLength = 25;
                light.DistanceFadeShadow = 40;
            }
            _model.AddChild(light);
            _lights.Add((light, l));
        }
    }

    // The lamp and display surfaces get a material of their own, so this ship's lamps and screens light and dim by
    // themselves: the data file's lamps and displays materials (IRON_WAKE.md §9.2), else any lamps material of the model.
    private void FindLamps()
    {
        string? lamps = null, displays = null;
        if (_def.Interior is { } interior)
        {
            lamps = interior.Materials.TryGetValue("lamps", out var l) ? l.Material : null;
            displays = interior.Materials.TryGetValue("displays", out var d) ? d.Material : null;
        }
        foreach (MeshInstance3D mesh in ModelCatalog.Meshes(_model))
        {
            if (mesh.Mesh == null)
            {
                continue;
            }
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(s) is not BaseMaterial3D m || !m.EmissionEnabled)
                {
                    continue;
                }
                bool lamp = lamps != null ? m.ResourceName == lamps : m.ResourceName.EndsWith("_Lamps", StringComparison.Ordinal);
                bool display = displays != null && m.ResourceName == displays;
                if (lamp || display)
                {
                    var own = (BaseMaterial3D)m.Duplicate();
                    mesh.SetSurfaceOverrideMaterial(s, own);
                    (lamp ? _lamps : _displays).Add((own, m.EmissionEnergyMultiplier));
                }
            }
        }
    }

    // Reflection probes in the rooms' boxes (IRON_WAKE.md §9.4): interior mode with their own dark ambient, box projected,
    // captured once with the ship's lamps on (held on for the capture's frames), moving with the ship; so metal inside
    // reflects the inside rather than the sky.
    private void BuildProbes()
    {
        if (_def.Interior is not { Rooms.Count: > 0 } interior)
        {
            return;
        }
        foreach (ShipRoom room in interior.Rooms)
        {
            var min = new Vector3(room.Min.X, room.Min.Y, room.Min.Z);
            var max = new Vector3(room.Max.X, room.Max.Y, room.Max.Z);
            var probe = new ReflectionProbe
            {
                Name = "Probe_" + room.Id,
                Position = (min + max) * 0.5f,
                Size = (max - min) + Vector3.One * 0.2f,
                BoxProjection = true,
                Interior = true,
                AmbientMode = ReflectionProbe.AmbientModeEnum.Color,
                AmbientColor = new Color(0.035f, 0.033f, 0.03f),
                AmbientColorEnergy = 1f,
                UpdateMode = ReflectionProbe.UpdateModeEnum.Once,
                EnableShadows = false,
                MaxDistance = (max - min).Length(),
                CullMask = ~FxDirector.RenderLayer,
            };
            _model.AddChild(probe);
        }
        _probeFrames = 8;
    }

    // ---------------------------------------------------------------- each frame

    /// <summary>Shows the ship as it is this frame; <paramref name="delta"/> smooths the plumes.</summary>
    public void Show(in ShipLook look, double delta)
    {
        _time += delta;
        GlobalTransform = look.Pivot;
        ShowHatch(look.Hatch);
        float energy = 1f / MathF.Max(Exposure, 1e-4f);
        _plumeMaterial!.SetShaderParameter("energy", 5f * energy);
        _glowMaterial!.SetShaderParameter("energy", 4f * energy);

        bool running = look.Engines && !look.Destroyed;
        System.Numerics.Vector3 t = running ? look.Thrust : default;
        float retro = MathF.Max(0, -t.Z), down = MathF.Max(0, -t.Y);
        float lift = 0;
        float k = 1 - MathF.Exp(-(float)delta / PlumeSmoothing);
        foreach (Plume p in _plumes)
        {
            float want = p.Drive switch
            {
                Drive.Main => MathF.Max(0, t.Z),
                Drive.Aux => MathF.Max(0, t.Z) * 0.85f,
                Drive.Lift => MathF.Max(0, t.Y),
                Drive.RcsLeft => MathF.Max(0, -t.X) + 0.6f * retro + 0.6f * down,
                _ => MathF.Max(0, t.X) + 0.6f * retro + 0.6f * down,
            };
            // A running engine never quite goes out: a pilot light.
            if (running && p.Drive is Drive.Main or Drive.Aux or Drive.Lift)
            {
                want = MathF.Max(want, 0.06f);
            }
            p.Shown += (want - p.Shown) * k;
            ShowPlume(p, look.Boosting && p.Drive is Drive.Main or Drive.Aux);
            if (p.Drive == Drive.Lift)
            {
                lift += p.Shown;
            }
        }
        lift /= Math.Max(1, _liftNozzles);

        _belly.Visible = running && lift > 0.08f;
        _belly.LightEnergy = 2.2f * lift * energy;
        ShowLamps(look, running, energy);
        foreach ((MeshInstance3D glow, Color _, float phase, float period) in _navLights)
        {
            bool on = running && (period <= 0 || ((_time / period + phase) % 1.0) < 0.08);
            glow.Visible = on;
            if (on)
            {
                glow.SetInstanceShaderParameter("intensity", 1.2f);
            }
        }
    }

    // The lamps by the ship's power (IRON_WAKE.md §9.4): with the engines running or someone at the helm, the ship's lamps,
    // their surfaces and the instrument displays; without, the dim red emergency lamps and the lamps' surfaces barely
    // glowing; nothing on a wreck. A reflection probe's capture holds the ship's lamps on for its frames.
    private void ShowLamps(in ShipLook look, bool running, float energy)
    {
        bool capture = _probeFrames > 0;
        if (capture)
        {
            _probeFrames--;
        }
        bool powered = !look.Destroyed && (running || look.Piloted || capture);
        int state = look.Destroyed ? 0 : powered ? 2 : 1;
        if (state != _lightsState || MathF.Abs(energy - _lightsEnergy) > 0.01f * _lightsEnergy)
        {
            _lightsState = state;
            _lightsEnergy = energy;
            foreach ((Light3D light, ShipLight def) in _lights)
            {
                bool on = state == 2 ? def.Power == LightPower.Ship : state == 1 && def.Power == LightPower.Emergency;
                light.Visible = on;
                light.LightEnergy = def.Energy * energy;
            }
            foreach ((BaseMaterial3D m, float e) in _displays)
            {
                m.EmissionEnergyMultiplier = state == 2 ? e * energy : 0f;
            }
        }
        float lamps = state == 0 ? 0 : state == 2 ? 1f : 0.08f;
        if (MathF.Abs(lamps * energy - _lampEnergy) > 1e-3f * MathF.Max(1, _lampEnergy))
        {
            _lampEnergy = lamps * energy;
            foreach ((BaseMaterial3D m, float e) in _lamps)
            {
                m.EmissionEnergyMultiplier = e * _lampEnergy;
            }
        }
    }

    private void ShowPlume(Plume p, bool boosting)
    {
        float s = p.Shown;
        bool visible = s > 0.015f && !(FromCockpit && p.Drive is Drive.RcsLeft or Drive.RcsRight);
        p.Gas.Visible = visible;
        p.Glow.Visible = visible;
        if (!visible)
        {
            return;
        }
        // Longer with more thrust (a square root: a little thrust already shows), hotter and bluer boosting.
        float length = p.Length * MathF.Sqrt(MathF.Min(s, 2f));
        Vector3 dir = p.Direction;
        Vector3 across = MathF.Abs(dir.Y) < 0.9f ? dir.Cross(Vector3.Up).Normalized() : dir.Cross(Vector3.Right).Normalized();
        Vector3 third = across.Cross(dir).Normalized();
        Vector3 start = p.Nozzle + dir * 0.04f;
        p.Gas.Transform = new Transform3D(new Basis(across * p.Width, dir * length, third * p.Width), start);
        p.Gas.SetInstanceShaderParameter("intensity", MathF.Min(s, 2f));
        Color tint = boosting ? new Color(0.62f, 0.72f, 1f) : new Color(1f, 0.52f, 0.2f);
        p.Gas.SetInstanceShaderParameter("tint", tint);
        p.Glow.Transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * p.Width * 1.6f), start);
        p.Glow.SetInstanceShaderParameter("intensity", MathF.Min(s, 2f) * 0.9f);
        p.Glow.SetInstanceShaderParameter("tint", tint);
    }

    private void ShowHatch(float open)
    {
        if (_hatch == null)
        {
            return;
        }
        open = Math.Clamp(open, 0, 1);
        if (_hatchClip != null && _hatchTrack >= 0)
        {
            _hatch.Quaternion = _hatchClip.RotationTrackInterpolate(_hatchTrack, open * _hatchClip.Length);
        }
        else
        {
            // No clip: swing up about the hinge's fore-and-aft axis, as the clip does (78 degrees).
            _hatch.Quaternion = _hatchRest * new Quaternion(Vector3.Back, -open * 78f * MathF.PI / 180f);
        }
    }
}
