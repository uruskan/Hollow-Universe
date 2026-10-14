using System;
using System.Numerics;
using Hollow.Core;

namespace Hollow.Simulation.Movement;

/// <summary>
/// Which way things face on a whole world (docs/WHOLE_WORLD.md §5). A zone's frame is the body's own (+Y the north pole),
/// so a heading is measured from local north: 0 faces north, growing anticlockwise seen from above, so π/2 faces west.
/// Things that stand still keep such a yaw (their point never moves, so its north never changes); a moving character's
/// view is a direction instead (<see cref="MoveInput.View"/>), which has no pole. Within <see cref="PoleRadians"/> of a
/// pole, where north is undefined, east is the body's −Z (as <c>ZoneFrame</c> has it), the same on every machine.
/// </summary>
public static class Facing
{
    /// <summary>How close to a pole (radians from the axis) the fixed east takes over: 24 cm on a 2.41 km world.</summary>
    public const float PoleRadians = 1e-4f;

    /// <summary>Local north and east at a point with this <paramref name="up"/> (unit, in the zone's frame, the body's).</summary>
    public static (Vector3 North, Vector3 East) Compass(Vector3 up)
    {
        // East is the pole axis crossed with up; north is up crossed with east.
        var east = new Vector3(up.Z, 0f, -up.X);
        float length = east.Length();
        east = length > PoleRadians ? east / length : Vector3.Normalize(new Vector3(0f, 0f, -1f) - up * -up.Z);
        Vector3 north = Vector3.Cross(up, east);
        return (north, east);
    }

    /// <summary>
    /// The horizontal forward and right of a heading at a point with this <paramref name="up"/>: 0 faces local north,
    /// π/2 west.
    /// </summary>
    public static (Vector3 Forward, Vector3 Right) Basis(float yaw, Vector3 up)
    {
        (Vector3 north, Vector3 east) = Compass(up);
        float sin = (float)DMath.Sin(yaw), cos = (float)DMath.Cos(yaw);
        Vector3 forward = north * cos - east * sin;
        Vector3 right = Vector3.Cross(forward, up);
        return (forward, right);
    }

    /// <summary>The horizontal forward and right of a view direction at a point with this <paramref name="up"/>.</summary>
    public static (Vector3 Forward, Vector3 Right) Basis(Vector3 view, Vector3 up)
    {
        Vector3 flat = view - up * Vector3.Dot(view, up);
        float length = flat.Length();
        Vector3 forward = length > 1e-5f ? flat / length : Compass(up).North;
        return (forward, Vector3.Cross(forward, up));
    }

    /// <summary>The heading (from local north, anticlockwise seen from above) of a direction at a point with this up.</summary>
    public static float YawOf(Vector3 direction, Vector3 up)
    {
        (Vector3 north, Vector3 east) = Compass(up);
        return (float)DMath.Atan2(-Vector3.Dot(direction, east), Vector3.Dot(direction, north));
    }

    /// <summary>How far a view direction looks above the horizontal at a point with this up, in radians.</summary>
    public static float PitchOf(Vector3 view, Vector3 up) => (float)DMath.Asin(Math.Clamp(Vector3.Dot(view, up), -1f, 1f));

    /// <summary>A view direction from a heading and a pitch at a point with this up.</summary>
    public static Vector3 View(float yaw, float pitch, Vector3 up)
    {
        (Vector3 forward, _) = Basis(yaw, up);
        return forward * (float)DMath.Cos(pitch) + up * (float)DMath.Sin(pitch);
    }

    /// <summary>
    /// A direction carried from a point with up <paramref name="from"/> to one with up <paramref name="to"/>: turned by
    /// the shortest rotation between them, so walking straight ahead follows a great circle round the world and over the
    /// poles (the client's camera, §5).
    /// </summary>
    public static Vector3 Carry(Vector3 direction, Vector3 from, Vector3 to)
    {
        float cos = Vector3.Dot(from, to);
        if (cos > 0.9999999f)
        {
            return direction;
        }
        Vector3 axis = Vector3.Cross(from, to);
        float sin = axis.Length();
        if (sin < 1e-7f)
        {
            return direction; // opposite points: no one rotation (never between two ticks)
        }
        axis /= sin;
        // Rodrigues' rotation by the angle between the two ups.
        Vector3 turned = direction * cos + Vector3.Cross(axis, direction) * sin + axis * (Vector3.Dot(axis, direction) * (1f - cos));
        return Vector3.Normalize(turned);
    }

    /// <summary>The turn of a thing standing along <paramref name="up"/> and facing <paramref name="forward"/>: X right, Y up, −Z forward.</summary>
    public static Quaternion Upright(Vector3 up, Vector3 forward)
    {
        Vector3 right = Vector3.Cross(forward, up);
        var rows = new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, -forward.X, -forward.Y, -forward.Z, 0, 0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rows));
    }
}
