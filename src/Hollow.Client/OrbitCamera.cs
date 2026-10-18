using System;
using System.Numerics;

namespace Hollow.Client;

/// <summary>Presentation-only orbit about a player, independent of movement and aim, on any planetary up axis.</summary>
public sealed class OrbitCamera
{
    public const float MinimumDistance = 1.5f, MaximumDistance = 120f;
    private readonly float _minimumDistance, _maximumDistance;
    public float Distance { get; private set; }
    public float Yaw { get; private set; }
    public float Pitch { get; private set; } = 0.25f;
    public bool Dragging { get; set; }

    public OrbitCamera(float distance = 6, float minimumDistance = MinimumDistance, float maximumDistance = MaximumDistance)
    {
        if (!float.IsFinite(minimumDistance) || !float.IsFinite(maximumDistance) || minimumDistance <= 0 || maximumDistance < minimumDistance)
            throw new ArgumentOutOfRangeException(nameof(minimumDistance));
        if (!float.IsFinite(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
        _minimumDistance = minimumDistance;
        _maximumDistance = maximumDistance;
        Distance = Math.Clamp(distance, minimumDistance, maximumDistance);
    }

    public void Drag(float yaw, float pitch)
    {
        Yaw = MathF.IEEERemainder(Yaw + yaw, 2 * MathF.PI);
        Pitch = Math.Clamp(Pitch + pitch, -1.45f, 1.45f);
    }

    public void Zoom(float steps) => Distance = Math.Clamp(Distance * MathF.Pow(0.85f, steps), _minimumDistance, _maximumDistance);

    public (Vector3 Eye, Vector3 Forward, Vector3 Up) Follow(Vector3 playerEye, Vector3 up, Vector3 facing)
    {
        up = Vector3.Normalize(up);
        Vector3 heading = facing - up * Vector3.Dot(facing, up);
        if (heading.LengthSquared() < 1e-6f)
            heading = Vector3.Cross(up, MathF.Abs(up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ);
        heading = Vector3.Normalize(heading);
        Vector3 behind = Vector3.Transform(-heading, Quaternion.CreateFromAxisAngle(up, Yaw));
        Vector3 offset = (behind * MathF.Cos(Pitch) + up * MathF.Sin(Pitch)) * Distance;
        return (playerEye + offset, Vector3.Normalize(-offset), up);
    }
}
