using System;
using System.Numerics;

namespace Hollow.Client;

/// <summary>Presentation-only orbit about the seated player, independent of ship steering and station aim.</summary>
public sealed class SeatOrbitCamera
{
    public const float MinimumDistance = 1.5f, MaximumDistance = 120f;
    public float Distance { get; private set; } = 6;
    public float Yaw { get; private set; }
    public float Pitch { get; private set; } = 0.25f;
    public bool Dragging { get; set; }

    public void Drag(float yaw, float pitch)
    {
        Yaw = MathF.IEEERemainder(Yaw + yaw, 2 * MathF.PI);
        Pitch = Math.Clamp(Pitch + pitch, -1.45f, 1.45f);
    }

    public void Zoom(float steps) => Distance = Math.Clamp(Distance * MathF.Pow(0.85f, steps), MinimumDistance, MaximumDistance);

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
