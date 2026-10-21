using System;
using System.Linq;
using Godot;
using HollowUniverse.Ground;

namespace HollowUniverse.Items;

/// <summary>A held lens follows the ground camera's exposure without changing shared material resources.</summary>
public partial class OpticLensView : Node
{
    public Node Lens { get; set; } = null!;
    private MeshInstance3D[] _meshes = Array.Empty<MeshInstance3D>();
    private float _lastExposure;

    public override void _Ready()
    {
        _meshes = ModelCatalog.Meshes(Lens).ToArray();
        _Process(0);
    }

    public override void _Process(double delta)
    {
        float exposure = Glow.Exposure;
        if (MathF.Abs(exposure - _lastExposure) <= Math.Max(1e-5f, exposure * .005f)) return;
        _lastExposure = exposure;
        foreach (MeshInstance3D mesh in _meshes)
            mesh.SetInstanceShaderParameter("optic_exposure", exposure);
    }
}
