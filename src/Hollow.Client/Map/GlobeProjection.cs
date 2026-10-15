using System;
using System.Numerics;
using Hollow.Core;

namespace Hollow.Client.Map;

/// <summary>
/// How the map draws a whole world (docs/WHOLE_WORLD.md §11): a globe seen from afar that becomes a flat chart as it
/// zooms in. One family of azimuthal projections about the view's centre: a point θ radians from the centre lands
/// R·sin(kθ)/k from it, along its bearing. <see cref="Bend"/> k = 1 is the globe (orthographic: the near half, the far
/// side hidden); k = ½ is Lambert's equal-area disc of the whole world; k → 0 the azimuthal equidistant chart, every
/// distance from the centre true. Every member is true to scale at the centre, so the land under the view keeps its
/// size while the map bends. The interface's shader draws the picture with the same formula (<c>GlobeMap</c>), and the
/// overlay places the grid, markers and labels with this one, and reads clicks with its inverse.
/// </summary>
/// <remarks>
/// Screen offsets are in pixels from the centre of the map's area, x to the right and y down. The view is a frame of
/// three body-frame unit vectors: <see cref="Centre"/> (out of the screen), <see cref="Right"/> and <see cref="Up"/>.
/// Deterministic: DMath only.
/// </remarks>
public readonly struct GlobeProjection
{
    /// <summary>The direction at the middle of the map, and the screen's right and up there (unit, body frame).</summary>
    public readonly Vector3D Centre, Right, Up;

    /// <summary>The world's radius in metres (the map's sphere).</summary>
    public readonly double Radius;

    /// <summary>Screen pixels per metre of ground at the centre.</summary>
    public readonly double PixelsPerMetre;

    /// <summary>The bend k: 1 a globe, towards 0 a flat chart.</summary>
    public readonly double Bend;

    public GlobeProjection(Vector3D centre, Vector3D up, double radius, double pixelsPerMetre, double bend)
    {
        Centre = centre.Normalized;
        // Up laid on the ground at the centre; the screen's right is up crossed with the view's direction (out of it).
        Vector3D u = up - Centre * Vector3D.Dot(up, Centre);
        Up = u.LengthSquared > 1e-24 ? u.Normalized : Perpendicular(Centre);
        Right = Vector3D.Cross(Up, Centre);
        Radius = radius;
        PixelsPerMetre = pixelsPerMetre;
        Bend = Math.Clamp(bend, 0, 1);
    }

    /// <summary>
    /// The bend for a view <paramref name="metresAcross"/> wide on a world of a radius: a globe while the whole world
    /// fits (2 radii across and more), a chart from 1 radius across in, smooth between.
    /// </summary>
    public static double BendFor(double metresAcross, double radius) => DMath.SmoothStep(GlobeFrom * 0.5, GlobeFrom, metresAcross / radius);

    /// <summary>Views this many radii across and wider are a globe; half that, a chart.</summary>
    public const double GlobeFrom = 2;

    /// <summary>How far from the centre (radians) the map reaches: the near half for a globe, the whole world for k ≤ ½.</summary>
    public double Reach => Bend <= 0.5 ? DMath.Pi : DMath.HalfPi / Bend;

    /// <summary>A direction's place on the screen; false when it is hidden (the far side of a globe).</summary>
    public bool ToScreen(Vector3D direction, out Vector2 offset)
    {
        offset = default;
        Vector3D d = direction.Normalized;
        double along = Vector3D.Dot(d, Centre);
        double x = Vector3D.Dot(d, Right), y = Vector3D.Dot(d, Up);
        double sine = Math.Sqrt(x * x + y * y);
        double theta = DMath.Atan2(sine, along);
        if (theta > Reach + 1e-9)
        {
            return false;
        }
        double r = Radius * Arc(theta);
        if (sine > 1e-15)
        {
            double s = r * PixelsPerMetre / sine;
            offset = new Vector2((float)(x * s), (float)(-y * s));
        }
        return true;
    }

    /// <summary>The direction shown at a screen offset; false off the world (beyond the globe's edge).</summary>
    public bool ToDirection(Vector2 offset, out Vector3D direction)
    {
        direction = Centre;
        double mx = offset.X / PixelsPerMetre, my = -offset.Y / PixelsPerMetre;
        double r = Math.Sqrt(mx * mx + my * my) / Radius;
        if (r < 1e-15)
        {
            return true;
        }
        double theta;
        if (Bend > 1e-6)
        {
            double s = r * Bend;
            if (s > 1)
            {
                return false;
            }
            theta = DMath.Asin(s) / Bend;
        }
        else
        {
            theta = r;
        }
        if (theta > Reach + 1e-9)
        {
            return false;
        }
        Vector3D along = (Right * mx + Up * my) / (r * Radius);
        direction = (Centre * DMath.Cos(theta) + along * DMath.Sin(theta)).Normalized;
        return true;
    }

    /// <summary>The map's edge: how far from the centre the world reaches on screen, in pixels.</summary>
    public double EdgePixels => Radius * PixelsPerMetre * Arc(Reach);

    /// <summary>
    /// The screen direction (unit, y down) of local north at a direction shown on the map, or none within a metre of a
    /// pole, where north has no meaning, or where the point is hidden.
    /// </summary>
    public Vector2? NorthAt(Vector3D direction)
    {
        Vector3D d = direction.Normalized;
        if (Compass(d) is not { } north || !ToScreen(d, out Vector2 here))
        {
            return null;
        }
        // A step of a metre north, measured on screen (the projection's own bend included).
        double step = 1 / Radius;
        if (!ToScreen(d * DMath.Cos(step) + north.North * DMath.Sin(step), out Vector2 there) || (there - here).LengthSquared() < 1e-12f)
        {
            return null;
        }
        return Vector2.Normalize(there - here);
    }

    /// <summary>Local north and east at a unit direction (docs/WHOLE_WORLD.md §3.1), or none within a metre of a pole.</summary>
    public static (Vector3D North, Vector3D East)? Compass(Vector3D d)
    {
        var east = new Vector3D(d.Z, 0, -d.X);
        double length = east.Length;
        if (length < 1e-4)
        {
            return null;
        }
        east /= length;
        return (Vector3D.Cross(d, east), east);
    }

    // R·sin(kθ)/k over R: the distance on screen of a point θ from the centre, in radii.
    private double Arc(double theta) => Bend > 1e-6 ? DMath.Sin(Bend * theta) / Bend : theta;

    private static Vector3D Perpendicular(Vector3D d) =>
        Vector3D.Cross(Math.Abs(d.Y) < 0.9 ? Vector3D.UnitY : Vector3D.UnitX, d).Normalized;
}
