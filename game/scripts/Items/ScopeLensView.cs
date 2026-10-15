using System;
using System.Linq;
using Godot;
using HollowUniverse.Characters;

namespace HollowUniverse.Items;

/// <summary>A local magnified optic images the world onto its rear aperture, rather than looking down a tube.
/// One bounded HDR render is active only during ADS. It excludes the first-person meshes and shares the world.</summary>
public partial class ScopeLensView : Node
{
    public const int Resolution = 512;
    public Node Lens { get; set; } = null!;
    public bool Rendering => _viewport?.RenderTargetUpdateMode == SubViewport.UpdateMode.Always;
    public Camera3D OpticalCamera => _camera;
    public SubViewport OpticalViewport => _viewport;
    private MeshInstance3D _lens = null!;
    private ShaderMaterial _material = null!;
    private ViewmodelView? _viewmodel;
    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private Godot.Environment? _sourceEnvironment;
    private Aabb _aperture;
    private float _lensFov;

    public override void _Ready()
    {
        ProcessPriority = 100; // After the camera and hand pose, before rendering.
        _lens = ModelCatalog.Meshes(Lens).First(mesh => ModelCatalog.LodOf(mesh) == 0);
        _aperture = _lens.Mesh!.GetAabb();
        // Held() runs after ForArms() conversion: this instance never enters the static material caches.
        _material = (ShaderMaterial)_lens.GetActiveMaterial(0).Duplicate();
        for (int i = 0; i < _lens.GetSurfaceOverrideMaterialCount(); i++)
            _lens.SetSurfaceOverrideMaterial(i, _material);
        _material.SetShaderParameter("optical_centre", _aperture.GetCenter());
        _material.SetShaderParameter("optical_size", new Vector2(_aperture.Size.Z, _aperture.Size.Y));
        for (Node? parent = GetParent(); parent != null; parent = parent.GetParent())
            if (parent is ViewmodelView viewmodel) { _viewmodel = viewmodel; break; }
        _viewport = new SubViewport
        {
            Name = "OpticalWorld", Size = new Vector2I(Resolution, Resolution), UseHdr2D = true,
            World3D = GetViewport().FindWorld3D(), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(_viewport);
        _camera = new Camera3D { Name = "OpticalCamera", Current = true };
        _viewport.AddChild(_camera);
        _material.SetShaderParameter("optical_image", _viewport.GetTexture());
    }

    public override void _Process(double delta)
    {
        // This instance is deliberately outside the global conversion cache. Keep its projection in step
        // with the registered weapon materials even at the hip, where the optical render is disabled.
        float viewmodelFov = CharacterMaterials.ViewmodelFov;
        if (_lensFov != viewmodelFov)
        {
            _lensFov = viewmodelFov;
            _material.SetShaderParameter("viewmodel_fov", viewmodelFov);
        }
        Camera3D? eye = GetViewport().GetCamera3D();
        bool active = eye != null && _viewmodel is { AimAmount: > .98f } &&
            _viewmodel.ActiveAction != "reload" && _lens.IsVisibleInTree();
        if (!active) { SetActive(false); return; }
        Vector3 centre = eye!.GlobalTransform.AffineInverse() * (_lens.GlobalTransform * _aperture.GetCenter());
        float depth = -centre.Z;
        if (depth <= eye.Near * .25f) { SetActive(false); return; }
        float modelTangent = MathF.Tan(Mathf.DegToRad(viewmodelFov) * .5f);
        float worldTangent = MathF.Tan(Mathf.DegToRad(eye.Fov) * .5f);
        float radius = _aperture.Size.Y * .5f * _lens.GlobalBasis.Y.Length();
        // The main camera already applies the attachment's zoom. Match its angular image scale at the
        // projected aperture size instead of multiplying that zoom a second time inside the scope.
        float projectedDiameter = radius / depth / modelTangent;
        _camera.Fov = Mathf.RadToDeg(2 * MathF.Atan(worldTangent * projectedDiameter));
        Vector3 ray = new(centre.X * worldTangent / modelTangent, centre.Y * worldTangent / modelTangent, centre.Z);
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(eye.GlobalBasis * ray.Normalized(), eye.GlobalBasis.Y), eye.GlobalPosition);
        _camera.Near = eye.Near;
        _camera.Far = eye.Far;
        _camera.CullMask = eye.CullMask & ~ViewmodelView.RenderLayer;
        if (_sourceEnvironment != eye.Environment)
        {
            _sourceEnvironment = eye.Environment;
            if (_sourceEnvironment != null)
            {
                // Retain the shared sky/lighting but output linear HDR. The main viewport then applies
                // exposure and tone mapping once to both the lens image and the surrounding world.
                var raw = (Godot.Environment)_sourceEnvironment.Duplicate();
                raw.TonemapMode = Godot.Environment.ToneMapper.Linear;
                raw.TonemapExposure = 1;
                raw.GlowEnabled = false;
                raw.AdjustmentEnabled = false;
                _camera.Environment = raw;
            }
            else _camera.Environment = null;
        }
        // Immersion updates these scalars on the same Environment resource. Sharing the live sky alone
        // is insufficient: the optical world must enter and leave water fog with the player's camera.
        if (_sourceEnvironment is { } source && _camera.Environment is { } environment)
        {
            environment.FogEnabled = source.FogEnabled;
            environment.FogDensity = source.FogDensity;
            environment.FogLightColor = source.FogLightColor;
            environment.FogLightEnergy = source.FogLightEnergy;
            environment.FogSkyAffect = source.FogSkyAffect;
        }
        SetActive(true);
    }

    private void SetActive(bool active)
    {
        _viewport.RenderTargetUpdateMode = active ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        _material.SetShaderParameter("optical_image_enabled", active);
    }

    public override void _ExitTree()
    {
        // Break the material/viewport texture reference; the attachment owns every node/resource here.
        if (_material != null) _material.SetShaderParameter("optical_image", default(Variant));
    }
}
