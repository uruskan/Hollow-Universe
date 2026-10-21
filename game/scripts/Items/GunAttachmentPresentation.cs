using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Fx;

namespace HollowUniverse.Items;

/// <summary>Presentation capabilities of an authored attachment, independent of its item key.</summary>
public sealed record OpticInfo(string LensNode, string EyeSocket, int Reticle, bool Magnified = false);
public sealed record AttachmentLightInfo(string BeamSocket, float Range, float Angle, float Energy);

/// <summary>The real attachment supplies its optical eye, lens and beam origin in both held views.</summary>
public static class GunAttachmentPresentation
{
    // First-person material conversion is cached for the process, so these immutable bases must be shared too.
    // Exposure belongs to each mesh instance and never mutates the shared material.
    private static readonly Dictionary<int, ShaderMaterial> LensMaterials = new();

    public static void Prepare(Node3D model, ModelEntry entry)
    {
        if (entry.Optic is not { } optic || model.FindChild(optic.LensNode, true, false) is not Node lens)
            return;
        if (!LensMaterials.TryGetValue(optic.Reticle, out ShaderMaterial? material))
        {
            material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/optic_lens.gdshader") };
            material.SetShaderParameter("reticle_style", optic.Reticle);
            LensMaterials.Add(optic.Reticle, material);
        }
        foreach (MeshInstance3D mesh in ModelCatalog.Meshes(lens))
        {
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            for (int surface = 0; surface < mesh.GetSurfaceOverrideMaterialCount(); surface++)
                mesh.SetSurfaceOverrideMaterial(surface, material);
        }
    }

    /// <summary>Explicitly resolves the fitted sight before the gun's own similarly named sockets.</summary>
    public static bool TryEye(Node3D weapon, string weaponKey, out Transform3D eye)
    {
        eye = Transform3D.Identity;
        if (ModelCatalog.Find(weaponKey) is not { } gun || !gun.Sockets.TryGetValue("sight", out string? mount) ||
            weapon.FindChild(mount, true, false) is not Node socket || socket.GetNodeOrNull<Node3D>("sight") is not { } part ||
            !part.HasMeta("attachment_key") || ModelCatalog.Find((string)part.GetMeta("attachment_key"))?.Optic is not { } optic ||
            part.FindChild("socket_" + optic.EyeSocket, true, false) is not Node3D nativeEye)
            return false;
        for (Node3D? node = nativeEye; node != null && node != weapon; node = node.GetParent() as Node3D)
            eye = node.Transform * eye;
        return true;
    }

    public static void Held(Node3D attachment, ModelEntry entry, bool lightAllowed, bool firstPerson)
    {
        if (entry.Optic is { } optic && attachment.FindChild(optic.LensNode, true, false) is { } lens)
        {
            attachment.AddChild(new OpticLensView { Name = "LensExposure", Lens = lens });
            if (firstPerson && optic.Magnified)
                attachment.AddChild(new ScopeLensView { Name = "ScopeImage", Lens = lens });
        }
        if (!lightAllowed || entry.AttachmentLight is not { } light ||
            attachment.FindChild("socket_" + light.BeamSocket, true, false) is not Node3D beam)
            return;
        attachment.AddChild(new WeaponAttachmentLight { Name = "WeaponBeam", Beam = beam, Definition = light, FirstPerson = firstPerson });
    }
}

/// <summary>Lives and dies with the held attachment. The existing held-mod snapshot is its complete authority.</summary>
public partial class WeaponAttachmentLight : Node3D
{
    public Node3D Beam { get; set; } = null!;
    public AttachmentLightInfo Definition { get; set; } = null!;
    public bool FirstPerson { get; set; }
    private SpotLight3D _light = null!;

    public override void _Ready()
    {
        _light = new SpotLight3D
        {
            Name = "Light", TopLevel = true,
            SpotRange = Definition.Range, SpotAngle = Definition.Angle, SpotAttenuation = 1.2f,
            LightColor = new Color(1f, 0.94f, 0.84f), LightCullMask = 1,
            // Only the local beam casts shadows. Remote beams fade and remain bounded by their short range.
            ShadowEnabled = FirstPerson, DistanceFadeEnabled = true, DistanceFadeBegin = 35, DistanceFadeLength = 20,
            DistanceFadeShadow = 20,
        };
        AddChild(_light);
        if (FirstPerson) HollowUniverse.Settings.LocalShadowBudget.Track(_light, Definition.Range, priority: 10);
        _Process(0);
    }

    public override void _Process(double delta)
    {
        if (_light == null || !IsInstanceValid(Beam)) return;
        _light.Visible = IsVisibleInTree();
        if (!_light.Visible) return;
        Vector3 origin = Beam.GlobalPosition, end = origin + Beam.GlobalBasis.X;
        if (FirstPerson)
        {
            origin = ViewmodelPoint(origin);
            end = ViewmodelPoint(end);
        }
        Vector3 direction = (end - origin).Normalized();
        Vector3 up = Beam.GlobalBasis.Y.Normalized();
        if (MathF.Abs(direction.Dot(up)) > 0.98f) up = Beam.GlobalBasis.Z.Normalized();
        _light.GlobalTransform = new Transform3D(Basis.LookingAt(direction, up), origin);
        _light.LightEnergy = Definition.Energy * FxDirector.Brightness;
    }

    private Vector3 ViewmodelPoint(Vector3 world)
    {
        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera == null) return world;
        Transform3D view = camera.GlobalTransform;
        Vector3 local = view.AffineInverse() * world;
        float factor = MathF.Tan(Mathf.DegToRad(camera.Fov) * 0.5f) /
            MathF.Tan(Mathf.DegToRad(Characters.CharacterMaterials.ViewmodelFov) * 0.5f);
        return view * new Vector3(local.X * factor, local.Y * factor, local.Z);
    }
}
