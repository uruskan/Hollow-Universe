using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Surface;

/// <summary>
/// Neutral shader inputs for isolated render diagnostics. Never used alongside a live SurfaceView;
/// PlanetGlobals owns the separate planet-frame parameters. Supplied textures remain caller-owned.
/// </summary>
internal sealed class SurfaceDiagnosticGlobals : IDisposable
{
    private readonly HashSet<string> _added = new(StringComparer.Ordinal);

    public void Set(string name, RenderingServer.GlobalShaderParameterType type, Variant value)
    {
        if (_added.Add(name)) RenderingServer.GlobalShaderParameterAdd(name, type, value);
        else RenderingServer.GlobalShaderParameterSet(name, value);
    }

    public void Setup(double radius, Texture2D zero, Texture2D one, Texture2D noise,
        Texture3D darkAir, Texture3D clearAir)
    {
        var f = RenderingServer.GlobalShaderParameterType.Float;
        var v2 = RenderingServer.GlobalShaderParameterType.Vec2;
        var v3 = RenderingServer.GlobalShaderParameterType.Vec3;
        var v4 = RenderingServer.GlobalShaderParameterType.Vec4;
        var s2 = RenderingServer.GlobalShaderParameterType.Sampler2D;
        var s3 = RenderingServer.GlobalShaderParameterType.Sampler3D;
        Set("hu_planet_radius", f, (float)radius); Set("hu_atmosphere_top", f, (float)radius + 10);
        Set("hu_camera_altitude", f, 2f); Set("hu_camera_up", v3, Vector3.Up);
        Set("hu_camera_position", v3, Vector3.Zero);
        Set("hu_sun_direction", v3, new Vector3(-0.5f, 0.8f, 0.3f).Normalized());
        Set("hu_sun_illuminance", v3, Vector3.One); Set("hu_moon_direction", v3, Vector3.Up);
        Set("hu_moon_illuminance", v3, Vector3.Zero);
        Set("hu_rayleigh", v4, new Vector4(0, 0, 0, 10)); Set("hu_mie", v4, new Vector4(0, 0, 10, 0));
        Set("hu_ozone", v4, new Vector4(0, 0, 0, 5)); Set("hu_ozone_half_width", f, 5f);
        Set("hu_transmittance_lut", s2, one); Set("hu_multiscatter_lut", s2, zero);
        Set("hu_ap_inscatter", s3, darkAir); Set("hu_ap_transmittance", s3, clearAir);
        Set("hu_ap_up", v3, Vector3.Up); Set("hu_ap_east", v3, Vector3.Right); Set("hu_ap_range", f, 100f);
        Set("hu_cloud_noise", s2, noise); Set("hu_cloud_wind", v2, Vector2.Zero);
        Set("hu_cloud_coverage", f, 0f); Set("hu_cloud_layer", v2, new Vector2(10, 20));
        Set("hu_wind", v4, new Vector4(0.8f, 0, 0.6f, 0));
        Set("hu_phase_wave_main", v3, SurfaceWorldPhases.MainWave);
        Set("hu_phase_wave_foliage", v3, SurfaceWorldPhases.FoliageDetailWave - new Vector3(.8f, 0, .6f) * .035f);
        Set("hu_phase_wave_tree", v3, new Vector3(.8f, 0, .6f) * -.035f);
        Set("hu_phase_wave_angles", v3, Vector3.Zero);
        Set("hu_lod_clock", f, 0f);
        Set("hu_shake_at", v4, Vector4.Zero); Set("hu_shake_dir", v4, Vector4.Zero);
    }

    public void Dispose()
    {
        foreach (string name in _added) RenderingServer.GlobalShaderParameterRemove(name);
        _added.Clear();
    }
}
