using System;
using Hollow.Core;

namespace HollowUniverse.GalaxyMap;

/// <summary>
/// Orbit camera for the galaxy map, in double precision (galactic frame, light-years). Inputs set targets;
/// <see cref="Update"/> eases the current state towards them.
/// </summary>
public sealed class MapCamera
{
    public const double MinDistance = 12;
    public const double MaxDistance = 2400;
    private const double MaxPitch = 1.53;

    private Vector3D _targetFocus;
    private double _targetDistance;
    private double _targetYaw;
    private double _targetPitch;

    public MapCamera(Vector3D focus, double distance, double yaw, double pitch)
    {
        Focus = _targetFocus = focus;
        Distance = _targetDistance = distance;
        Yaw = _targetYaw = yaw;
        Pitch = _targetPitch = pitch;
    }

    public Vector3D Focus { get; private set; }
    public double Distance { get; private set; }
    public double Yaw { get; private set; }
    public double Pitch { get; private set; }

    public Vector3D TargetFocus => _targetFocus;
    public double TargetDistance => _targetDistance;

    /// <summary>Direction from the focus to the camera.</summary>
    public Vector3D Back => new(Math.Cos(Pitch) * Math.Sin(Yaw), Math.Sin(Pitch), Math.Cos(Pitch) * Math.Cos(Yaw));

    public Vector3D Position => Focus + Back * Distance;

    /// <summary>Screen-right direction projected on the galactic plane.</summary>
    public Vector3D Right => new(Math.Cos(Yaw), 0, -Math.Sin(Yaw));

    /// <summary>Into-the-screen direction projected on the galactic plane.</summary>
    public Vector3D Forward => new(-Math.Sin(Yaw), 0, -Math.Cos(Yaw));

    public void Orbit(double deltaYaw, double deltaPitch)
    {
        _targetYaw += deltaYaw;
        _targetPitch = Math.Clamp(_targetPitch + deltaPitch, -MaxPitch, MaxPitch);
    }

    public void Zoom(double factor) => _targetDistance = Math.Clamp(_targetDistance * factor, MinDistance, MaxDistance);

    /// <summary>Moves the focus by amounts relative to the current distance.</summary>
    public void Pan(double right, double forward, double up)
    {
        double scale = Math.Max(_targetDistance, 0.5);
        _targetFocus += (Right * right + Forward * forward + new Vector3D(0, up, 0)) * scale;
    }

    public void FlyTo(Vector3D focus, double? distance = null)
    {
        _targetFocus = focus;
        if (distance.HasValue)
        {
            _targetDistance = Math.Clamp(distance.Value, MinDistance, MaxDistance);
        }
    }

    public void Set(Vector3D focus, double distance, double yaw, double pitch)
    {
        Focus = _targetFocus = focus;
        Distance = _targetDistance = Math.Clamp(distance, MinDistance, MaxDistance);
        Yaw = _targetYaw = yaw;
        Pitch = _targetPitch = Math.Clamp(pitch, -MaxPitch, MaxPitch);
    }

    public void Update(double delta)
    {
        double k = 1 - Math.Exp(-delta * 7.0);
        Focus = Vector3D.Lerp(Focus, _targetFocus, k);
        // Zoom in log space so it feels the same at every scale.
        Distance = Math.Exp(Math.Log(Distance) + (Math.Log(_targetDistance) - Math.Log(Distance)) * k);
        Yaw += (_targetYaw - Yaw) * Math.Min(1, k * 1.6);
        Pitch += (_targetPitch - Pitch) * Math.Min(1, k * 1.6);
    }

    /// <summary>True while easing towards a target.</summary>
    public bool IsMoving =>
        Vector3D.Distance(Focus, _targetFocus) > Distance * 1e-4
        || Math.Abs(Math.Log(Distance / _targetDistance)) > 1e-3
        || Math.Abs(Yaw - _targetYaw) > 1e-4
        || Math.Abs(Pitch - _targetPitch) > 1e-4;
}
