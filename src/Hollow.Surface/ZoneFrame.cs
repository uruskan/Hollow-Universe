using System;
using System.Numerics;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// The local frame of a zone on a body's surface (ARCHITECTURE.md §6): float metres, origin on the ground,
/// +X east, +Y up (away from the body's centre at the origin), +Z south, so −Z faces north. Right-handed with Y up,
/// like Godot. "Up" changes across a zone because the ground is curved: use <see cref="UpAt"/>, never +Y, for
/// gravity and for characters.
/// </summary>
public readonly struct ZoneFrame
{
    /// <summary>The origin in the body frame (metres from the body's centre).</summary>
    public readonly Vector3D Origin;

    /// <summary>Body-frame unit vectors of the local axes: +X, +Y and +Z (south).</summary>
    public readonly Vector3D East, Up, South;

    public ZoneFrame(Vector3D origin)
    {
        Origin = origin;
        Up = origin.Normalized;
        // East is the pole axis crossed with up; at a pole, where that vanishes, use −Z (east at the prime meridian).
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, Up);
        East = east.LengthSquared > 1e-18 ? east.Normalized : new Vector3D(0, 0, -1);
        South = Vector3D.Cross(East, Up);
    }

    /// <summary>A frame on the ground at a latitude and longitude (radians; longitude grows eastward from +X).</summary>
    public static ZoneFrame At(ISurface surface, double latitude, double longitude)
    {
        Vector3D direction = Direction(latitude, longitude);
        return new ZoneFrame(direction * (surface.Radius + surface.HeightAt(direction)));
    }

    /// <summary>Unit body-frame direction of a latitude and longitude in radians (deterministic: DMath).</summary>
    public static Vector3D Direction(double latitude, double longitude)
    {
        double cosLat = DMath.Cos(latitude);
        return new Vector3D(cosLat * DMath.Cos(longitude), DMath.Sin(latitude), -cosLat * DMath.Sin(longitude));
    }

    /// <summary>A body-frame point in this frame.</summary>
    public Vector3 ToLocal(Vector3D bodyPoint)
    {
        Vector3D d = bodyPoint - Origin;
        return new Vector3((float)Vector3D.Dot(d, East), (float)Vector3D.Dot(d, Up), (float)Vector3D.Dot(d, South));
    }

    /// <summary>A local point in the body frame.</summary>
    public Vector3D ToBody(Vector3 local) => ToBody(local.X, local.Y, local.Z);

    public Vector3D ToBody(double x, double y, double z) => Origin + East * x + Up * y + South * z;

    /// <summary>A body-frame direction (no translation) in this frame's axes.</summary>
    public Vector3 DirectionToLocal(Vector3D direction) =>
        new((float)Vector3D.Dot(direction, East), (float)Vector3D.Dot(direction, Up), (float)Vector3D.Dot(direction, South));

    /// <summary>Unit "up" (away from the body's centre) at a local point, in this frame's axes.</summary>
    public Vector3 UpAt(Vector3 local) => DirectionToLocal(ToBody(local).Normalized);

    public override string ToString() => FormattableString.Invariant($"ZoneFrame at {Origin}");
}
