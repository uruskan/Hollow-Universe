using System;
using System.Collections.Generic;
using Godot;
using Hollow.Simulation.Ships;
using HollowUniverse.Items;

namespace HollowUniverse.Ships;

/// <summary>What a ship is doing this frame, as a <see cref="ShipView"/> draws it.</summary>
/// <param name="Pivot">The model's pivot (the ship frame's origin, on the ground between the skids) in render space, its axes the ship's.</param>
/// <param name="Hatch">How far the hatch has swung open, 0 shut to 1 open.</param>
/// <param name="Thrust"><see cref="ShipState.Thrust"/>: what the engines give on each axis (x + left, y + up, z + forward; above 1 boosting).</param>
public readonly record struct ShipLook(Transform3D Pivot, float Hatch, System.Numerics.Vector3 Thrust, bool Engines, bool Boosting,
    bool Piloted, bool Destroyed);

/// <summary>
/// One ship drawn (G6, GROUND.md §6.7): the catalogue model, whose levels of detail switch by distance; the hatch
/// swinging by the model's own open clip; a plume on every nozzle of ships.json that grows with the thrust on its axis
/// (main and side engines forward, lift nozzles up, the small thrusters for sideways, braking and down); the lamps, nav
/// lights and a landing light while the engines run, and the glow of the lift nozzles on the ground below. Presentation
/// only: the host gives it a <see cref="ShipLook"/> each frame, from the zone, the client's prediction or a snapshot.
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
    private readonly List<StandardMaterial3D> _lamps = new();
    private OmniLight3D _belly = null!;
    private SpotLight3D _landing = null!;
    private float _lampEnergy = -1;
    private double _time;

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

    public override void _Ready()
    {
        _model = ModelCatalog.Instantiate(_def.Key) ?? new Node3D { Name = _def.Key };
        AddChild(_model);
        FindHatch();
        BuildPlumes();
        BuildLights();
        FindLamps();
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

    private void BuildPlumes()
    {
        EnsureMaterials();
        void Add(IReadOnlyList<System.Numerics.Vector3> nozzles, Drive drive, Vector3 direction, float width, float length)
        {
            foreach (System.Numerics.Vector3 n in nozzles)
            {
                Drive d = drive;
                Vector3 dir = direction;
                if (drive == Drive.RcsLeft)
                {
                    // The small thrusters blow outward from their side of the hull (+X is the ship's left).
                    d = n.X >= 0 ? Drive.RcsLeft : Drive.RcsRight;
                    dir = new Vector3(n.X >= 0 ? 1 : -1, 0, 0);
                }
                _plumes.Add(new Plume
                {
                    Gas = Quad(_plumeMaterial!, $"Plume{_plumes.Count}"),
                    Glow = Quad(_glowMaterial!, $"Nozzle{_plumes.Count}"),
                    Drive = d,
                    Nozzle = new Vector3(n.X, n.Y, n.Z),
                    Direction = dir,
                    Width = width,
                    Length = length,
                });
            }
        }
        Add(_def.MainNozzles, Drive.Main, new Vector3(0, 0, -1), 0.5f, 3.4f);
        Add(_def.AuxNozzles, Drive.Aux, new Vector3(0, 0, -1), 0.24f, 1.9f);
        Add(_def.LiftNozzles, Drive.Lift, new Vector3(0, -1, 0), 0.3f, 1.7f);
        Add(_def.RcsNozzles, Drive.RcsLeft, Vector3.Zero, 0.12f, 0.8f);
    }

    private void BuildLights()
    {
        // The lift nozzles' light on the ground below, and a landing light under the nose looking ahead and down.
        _belly = new OmniLight3D
        {
            Name = "Belly",
            LightColor = new Color(1f, 0.62f, 0.32f),
            OmniRange = 14,
            OmniAttenuation = 1.6f,
            ShadowEnabled = false,
            Position = new Vector3(0, 0.2f, -0.5f),
            Visible = false,
        };
        _model.AddChild(_belly);
        _landing = new SpotLight3D
        {
            Name = "Landing",
            LightColor = new Color(1f, 0.95f, 0.86f),
            SpotRange = 90,
            SpotAngle = 26,
            SpotAttenuation = 1.2f,
            ShadowEnabled = false,
            Visible = false,
        };
        _landing.Transform = new Transform3D(Basis.LookingAt(new Vector3(0, -0.55f, 1), Vector3.Up), new Vector3(0, 0.75f, 1.9f));
        _model.AddChild(_landing);
        // Nav lights at the tips of the engine pods (red on the left, green on the right), a white beacon on the spine.
        float podX = 1.18f, podZ = -2.95f;
        foreach (HullBox box in _def.Hull)
        {
            if (!box.Skid && MathF.Abs(box.Centre.X) > 0.5f)
            {
                podX = MathF.Abs(box.Centre.X) + box.HalfExtents.X;
                podZ = box.Centre.Z - box.HalfExtents.Z;
            }
        }
        AddNav(new Vector3(podX, 0.98f, podZ), new Color(1f, 0.08f, 0.04f), 0, 0);
        AddNav(new Vector3(-podX, 0.98f, podZ), new Color(0.1f, 1f, 0.25f), 0, 0);
        AddNav(new Vector3(0, 1.92f, -1.4f), new Color(1f, 1f, 1f), 0.15f, 1.6f);
    }

    private void AddNav(Vector3 at, Color colour, float phase, float period)
    {
        MeshInstance3D glow = Quad(_glowMaterial!, $"Nav{_navLights.Count}");
        glow.Position = at;
        glow.Scale = Vector3.One * 0.32f;
        glow.SetInstanceShaderParameter("tint", colour);
        _navLights.Add((glow, colour, phase, period));
    }

    // The lamp surfaces get a material of their own, so this ship's lamps light and dim by themselves.
    private void FindLamps()
    {
        foreach (MeshInstance3D mesh in ModelCatalog.Meshes(_model))
        {
            if (mesh.Mesh == null)
            {
                continue;
            }
            for (int s = 0; s < mesh.Mesh.GetSurfaceCount(); s++)
            {
                if (mesh.Mesh.SurfaceGetMaterial(s) is StandardMaterial3D m && m.ResourceName == "M_TinFinch_Lamps")
                {
                    var own = (StandardMaterial3D)m.Duplicate();
                    mesh.SetSurfaceOverrideMaterial(s, own);
                    _lamps.Add(own);
                }
            }
        }
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
        lift /= Math.Max(1, _def.LiftNozzles.Count);

        _belly.Visible = running && lift > 0.08f;
        _belly.LightEnergy = 2.2f * lift * energy;
        _landing.Visible = running && look.Piloted;
        _landing.LightEnergy = 4f * energy;
        float lamps = look.Destroyed ? 0 : running ? 3f : look.Piloted ? 1f : 0.25f;
        if (MathF.Abs(lamps * energy - _lampEnergy) > 1e-3f * MathF.Max(1, _lampEnergy))
        {
            _lampEnergy = lamps * energy;
            foreach (StandardMaterial3D m in _lamps)
            {
                m.EmissionEnergyMultiplier = _lampEnergy;
            }
        }
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
