using System;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Surface;

public partial class SurfaceView
{
    /// <summary>Read-only capture metadata from the same camera and sky that rendered the surface.</summary>
    public string LightingDiagnosticJson()
    {
        if (_sky == null || _frame == null || _surface == null)
            return JsonSerializer.Serialize(new { ready = false });
        var camera = _frame.ToBody(_camera.GlobalPosition);
        var size = GetViewport().GetVisibleRect().Size;
        object Light(DirectionalLight3D light) => new
        {
            enabled = light.Visible,
            energy = light.LightEnergy,
            colourSrgb = new[] { light.LightColor.R, light.LightColor.G, light.LightColor.B },
            directionToLightWorld = new[] { light.GlobalBasis.Z.X, light.GlobalBasis.Z.Y, light.GlobalBasis.Z.Z },
        };
        return JsonSerializer.Serialize(new
        {
            ready = true,
            shownUniverseSeconds = _time,
            timeRuns = _timeRuns,
            rotationTimeScale = RotationTimeScale,
            bodyPositionMetres = new[] { camera.X, camera.Y, camera.Z },
            bodyRadiusMetres = _surface.Radius,
            altitudeMetres = camera.Length - _surface.Radius,
            localSolarHours = _sky.LocalHours,
            sunElevationDegrees = _sky.SunElevation,
            moonElevationDegrees = _sky.Moon == null ? (float?)null : _sky.MoonElevation,
            moonPhase = _sky.Moon == null ? (float?)null : _sky.MoonPhase,
            skyExposure = _sky.Exposure,
            viewportExposure = _environment.TonemapExposure,
            cloudCoverage = CloudCoverage,
            cloudWindMetres = -(_cloudDrift % CloudPeriod),
            cameraFov = _camera.Fov,
            viewportPixels = new[] { size.X, size.Y },
            sun = Light(_sunLight),
            moon = Light(_moonLight),
        });
    }
}
