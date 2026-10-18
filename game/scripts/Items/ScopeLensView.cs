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
    /// <summary>Square camera-centred crop width/height, measured against the main viewport's height.</summary>
    public float OpticalImageScale { get; private set; }
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
        for (Node? parent = GetParent(); parent != null; parent = parent.GetParent())
            if (parent is ViewmodelView viewmodel) { _viewmodel = viewmodel; break; }
        _viewport = new SubViewport
        {
            Name = "OpticalWorld", Size = new Vector2I(Resolution, Resolution), UseHdr2D = true,
            World3D = GetViewport().FindWorld3D(), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(_viewport);
        _camera = new Camera3D { Name = "OpticalCamera", Current = true, KeepAspect = Camera3D.KeepAspectEnum.Height };
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
        Transform3D lensToEye = eye!.GlobalTransform.AffineInverse() * _lens.GlobalTransform;
        float modelTangent = MathF.Tan(Mathf.DegToRad(viewmodelFov) * .5f);
        float worldTangent = MathF.Tan(Mathf.DegToRad(eye.Fov) * .5f);
        // Keep the optical camera on the player's aim ray. Rotating it toward the recoiling lens and
        // stretching its image across lens-local UVs made the scene slide/shear independently of the gun.
        // Instead render a square crop of the main camera, large enough to cover every projected lens
        // corner. The shader samples it in screen space, so lens translation/rotation only changes the
        // aperture; the scene and reticle keep the exact same camera aim and existing attachment zoom.
        float imageScale = .01f;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = lensToEye * _aperture.GetEndpoint(corner);
            float depth = -point.Z;
            if (depth <= eye.Near * .25f) { SetActive(false); return; }
            imageScale = Math.Max(imageScale, Math.Max(MathF.Abs(point.X), MathF.Abs(point.Y)) / (depth * modelTangent));
        }
        _camera.Fov = Math.Clamp(Mathf.RadToDeg(2 * MathF.Atan(worldTangent * imageScale * 1.04f)), 1, 179);
        OpticalImageScale = MathF.Tan(Mathf.DegToRad(_camera.Fov) * .5f) / worldTangent;
        _material.SetShaderParameter("optical_image_scale", OpticalImageScale);
        _camera.GlobalTransform = eye.GlobalTransform;
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
