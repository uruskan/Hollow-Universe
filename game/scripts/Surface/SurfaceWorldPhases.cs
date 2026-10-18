using System;
using Godot;
using Hollow.Core;

namespace HollowUniverse.Surface;

/// <summary>
/// One view's canonical body-space texture/wave phases. Only fractional phases cross the double/float boundary;
/// positions in shaders remain relative to the current render origin. Materials are borrowed, never disposed here.
/// Face-anchor UV streams are deliberately unchanged (the admitted playable radii have a zero face anchor).
/// </summary>
internal sealed class SurfaceWorldPhases : IDisposable
{
    internal const int MaximumBindings = 64;
    internal const double MaximumAnchorMetres = 1_000_000_000;
    internal static readonly Vector3 MainWave = new(.061f, .047f, .053f);
    internal static readonly Vector3 FoliageDetailWave = new(.066f, .042f, .054f);
    private const double Tau = Math.PI * 2;
    private readonly long _planetOwner;
    private readonly float[] _cycles = new float[GroundLayers.Count];
    private readonly Vector3[] _phases = new Vector3[GroundLayers.Count];
    private readonly Binding?[] _bindings = new Binding?[MaximumBindings];
    private Vector3D _origin;
    private Vector3 _windBody;
    private bool _initialized, _closed;

    internal int BindingCount { get; private set; }
    internal long Revision { get; private set; }
    internal long MaterialUpdates { get; private set; }

    internal SurfaceWorldPhases(long planetOwner, ReadOnlySpan<float> layerMetres)
    {
        if (!PlanetGlobals.IsOwner(planetOwner)) throw new InvalidOperationException("Surface phases require the current planet owner.");
        if (layerMetres.Length != GroundLayers.Count) throw new ArgumentException("Sixteen ground layer sizes are required.", nameof(layerMetres));
        _planetOwner = planetOwner;
        for (int i = 0; i < _cycles.Length; i++)
        {
            float metres = layerMetres[i];
            if (!float.IsFinite(metres) || metres < .25f || metres > 1_000_000f)
                throw new ArgumentOutOfRangeException(nameof(layerMetres), "Ground phase sizes must be finite and between .25 m and 1000 km.");
            // The same rounded coefficient is used by the CPU phase and the GPU local-coordinate multiply.
            _cycles[i] = .5f / metres;
        }
    }

    // bodyWind is rounded once from the canonical body-space wind, not round-tripped through a render basis.
    internal void Update(RenderFrame frame, Vector3 bodyWind)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(frame);
        Vector3D origin = frame.Origin;
        if (!Finite(origin.X) || !Finite(origin.Y) || !Finite(origin.Z) || !Finite(bodyWind))
            throw new ArgumentOutOfRangeException(nameof(frame), "Surface phase anchors exceed the explicit finite body-coordinate domain.");
        bool moved = !_initialized || origin != _origin;
        if (!moved && bodyWind == _windBody) return;
        if (moved)
        {
            // Preflight borrowed owners before changing any material. A registration must be released before its
            // caller disposes the material; silently pruning it could leave a dependent draw at an old phase.
            foreach (Binding? binding in _bindings)
                if (binding?.Material is { } material && !GodotObject.IsInstanceValid(material))
                    throw new InvalidOperationException("A surface phase material was disposed before its binding.");
            for (int i = 0; i < _phases.Length; i++)
                _phases[i] = new Vector3(Fraction(origin.X * _cycles[i]), Fraction(origin.Y * _cycles[i]), Fraction(origin.Z * _cycles[i]));
            foreach (Binding? binding in _bindings)
                if (binding?.Material is { } material) Apply(material);
        }
        Vector3 tree = bodyWind * -.035f;
        Vector3 foliage = FoliageDetailWave + tree;
        Vector3 angles = new(Angle(origin, MainWave), Angle(origin, foliage), Angle(origin, tree));
        RenderingServer.GlobalShaderParameterSet("hu_phase_wave_main", MainWave);
        RenderingServer.GlobalShaderParameterSet("hu_phase_wave_foliage", foliage);
        RenderingServer.GlobalShaderParameterSet("hu_phase_wave_tree", tree);
        RenderingServer.GlobalShaderParameterSet("hu_phase_wave_angles", angles);
        _origin = origin; _windBody = bodyWind; _initialized = true; Revision++;
    }

    /// <summary>Bind before publishing a material; release this registration before disposing that material.</summary>
    internal IDisposable BindGround(ShaderMaterial material)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(material);
        if (!_initialized) throw new InvalidOperationException("Initialize surface phases before binding a ground material.");
        if (!GodotObject.IsInstanceValid(material)) throw new ObjectDisposedException(nameof(material));
        int slot = -1;
        for (int i = 0; i < _bindings.Length; i++)
        {
            if (ReferenceEquals(_bindings[i]?.Material, material)) throw new InvalidOperationException("Ground material already has a phase binding.");
            if (_bindings[i] == null && slot < 0) slot = i;
        }
        if (slot < 0) throw new InvalidOperationException("Surface phase material binding capacity (64) is exhausted.");
        Apply(material);
        var binding = new Binding(this, material, slot);
        _bindings[slot] = binding; BindingCount++; return binding;
    }

    private void Apply(ShaderMaterial material)
    {
        material.SetShaderParameter("ground_body_cycles", _cycles);
        material.SetShaderParameter("ground_body_phase", _phases);
        MaterialUpdates++;
    }
    private void RequireOpen()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (!PlanetGlobals.IsOwner(_planetOwner)) throw new InvalidOperationException("A retired planet owner cannot publish surface phases.");
    }
    private static bool Finite(double value) => double.IsFinite(value) && Math.Abs(value) <= MaximumAnchorMetres;
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static float Fraction(double value) => (float)(value - Math.Floor(value));
    private static float Angle(Vector3D point, Vector3 wave)
    {
        double radians = point.X * wave.X + point.Y * wave.Y + point.Z * wave.Z;
        return (float)(radians - Math.Floor(radians / Tau) * Tau);
    }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        for (int i = 0; i < _bindings.Length; i++) _bindings[i]?.Dispose();
        // No shader-global writes: a successor may already own those values. No native resource is owned here.
    }
    private sealed class Binding(SurfaceWorldPhases owner, ShaderMaterial material, int slot) : IDisposable
    {
        private SurfaceWorldPhases? _owner = owner;
        internal ShaderMaterial? Material { get; private set; } = material;
        public void Dispose()
        {
            if (_owner == null) return;
            _owner._bindings[slot] = null; _owner.BindingCount--;
            Material = null; _owner = null;
        }
    }
}
