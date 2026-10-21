using System;
using System.Numerics;
using Hollow.Core;

namespace Hollow.Client.Map;

/// <summary>
/// Where a map of the whole world looks (docs/WHOLE_WORLD.md §11): its centre, which way is up on the screen there, and
/// how many metres its area spans, from the whole world to about 150 m. Following keeps a point (the player) in the
/// middle with local north up; dragging turns the globe so the ground under the pointer stays under it, over the poles
/// too; the wheel zooms about the pointer. Left alone, the map turns itself back to north up, as a compass settles, except
/// near the poles, where north turns quickly and the turn of a drag is kept. The G screen and the death screen each keep
/// one. Engine-free and deterministic (DMath).
/// </summary>
public sealed class GlobeCamera
{
    /// <summary>The closest view, in metres across the area's shorter side.</summary>
    public const double ClosestAcross = 150;

    /// <summary>The widest view, in world radii across: the whole world with a margin.</summary>
    public const double WidestRadii = 2.25;

    /// <summary>How fast the map turns back to north up (per second), and within how many degrees of a pole it doesn't.</summary>
    private const double SettleRate = 5, PoleCalm = 10, PoleFull = 25;

    public GlobeCamera(double radius, Vector3D centre, double across)
    {
        Radius = radius;
        Centre = centre.Normalized;
        Up = NorthOr(Centre, Vector3D.UnitY);
        Across = across;
    }

    /// <summary>The world's radius in metres.</summary>
    public double Radius { get; }

    /// <summary>The direction at the middle of the map (unit, body frame).</summary>
    public Vector3D Centre { get; private set; }

    /// <summary>The screen's up on the ground at the centre (unit, at right angles to it).</summary>
    public Vector3D Up { get; private set; }

    private double _across;

    /// <summary>Metres across the area's shorter side at the centre (<see cref="ClosestAcross"/> … <see cref="WidestAcross"/>).</summary>
    public double Across
    {
        get => _across;
        set => _across = Math.Clamp(value, ClosestAcross, WidestAcross);
    }

    public double WidestAcross => Math.Max(ClosestAcross, WidestRadii * Radius);

    /// <summary>Whether the map keeps <see cref="Follow"/>'s point in the middle (a drag or a zoom away stops it).</summary>
    public bool Following { get; set; } = true;

    /// <summary>The projection for an area whose shorter side is <paramref name="shortSidePixels"/> pixels.</summary>
    public GlobeProjection Projection(double shortSidePixels) =>
        new(Centre, Up, Radius, Math.Max(1, shortSidePixels) / Across, GlobeProjection.BendFor(Across, Radius));

    /// <summary>While following: the map's middle on a point (a body-frame point or direction), local north up.</summary>
    public void Follow(Vector3D point)
    {
        if (!Following || point.LengthSquared < 1e-12)
        {
            return;
        }
        Centre = point.Normalized;
        Up = NorthOr(Centre, Up);
    }

    /// <summary>Puts the map's middle on a point and north up, without following (the death screen's body).</summary>
    public void LookAt(Vector3D point)
    {
        if (point.LengthSquared < 1e-12)
        {
            return;
        }
        Centre = point.Normalized;
        Up = NorthOr(Centre, Up);
    }

    /// <summary>North up at once, the centre kept.</summary>
    public void NorthUp() => Up = NorthOr(Centre, Up);

    /// <summary>
    /// A drag from one screen offset to another (pixels from the area's centre, y down): the globe turns so the ground
    /// that was under the first is under the second. Off the world (beyond a globe's edge), it turns by the distance.
    /// </summary>
    public void Drag(Vector2 from, Vector2 to, double shortSidePixels)
    {
        if ((to - from).LengthSquared() < 1e-6f)
        {
            return;
        }
        Following = false;
        GlobeProjection p = Projection(shortSidePixels);
        if (p.ToDirection(from, out Vector3D grabbed) && p.ToDirection(to, out Vector3D under))
        {
            // Turn the view so the screen point now showing 'under' shows 'grabbed'.
            Turn(under, grabbed);
            return;
        }
        // The pointer is off the globe: turn by the drag's length at the centre's scale.
        Vector2 d = to - from;
        double angle = d.Length() / p.PixelsPerMetre / Radius;
        Vector3D towards = (p.Right * -d.X + p.Up * d.Y).Normalized;
        Turn(Centre, Centre * DMath.Cos(angle) + towards * DMath.Sin(angle));
    }

    /// <summary>
    /// Zooms by a factor of the metres across (below 1 closer), keeping the ground under a screen offset where it is; while
    /// following, about the middle, which goes on following.
    /// </summary>
    public void Zoom(double factor, Vector2 pointer, double shortSidePixels)
    {
        if (Following)
        {
            Across *= factor;
            return;
        }
        bool under = Projection(shortSidePixels).ToDirection(pointer, out Vector3D before);
        Across *= factor;
        if (under && Projection(shortSidePixels).ToDirection(pointer, out Vector3D after))
        {
            Turn(after, before);
        }
    }

    /// <summary>
    /// Time passing: away from the poles the screen's up turns back towards local north, as a compass settles (nothing
    /// while following, which holds north up).
    /// </summary>
    public void Settle(double seconds, bool dragging = false)
    {
        // A held point must stay under the pointer. North-up settling resumes only after release.
        if (dragging || Following || seconds <= 0 || GlobeProjection.Compass(Centre) is not { } compass)
        {
            return;
        }
        double fromPole = DMath.Acos(Math.Abs(Centre.Y)) * DMath.RadToDeg;
        double weight = DMath.SmoothStep(PoleCalm, PoleFull, fromPole);
        if (weight <= 0)
        {
            return;
        }
        double t = weight * seconds * SettleRate / (1 + seconds * SettleRate);
        // The signed angle from up to north about the centre, turned by its share.
        double angle = DMath.Atan2(Vector3D.Dot(Vector3D.Cross(Up, compass.North), Centre), Vector3D.Dot(Up, compass.North));
        Up = Rotate(Up, Centre, angle * t);
    }

    // Turns the view (its centre and up together) by the shortest rotation that takes 'from' to 'to'.
    private void Turn(Vector3D from, Vector3D to)
    {
        Vector3D a = from.Normalized, b = to.Normalized;
        Vector3D axis = Vector3D.Cross(a, b);
        double sine = axis.Length, cosine = Vector3D.Dot(a, b);
        if (sine < 1e-15)
        {
            return;
        }
        axis /= sine;
        double angle = DMath.Atan2(sine, cosine);
        Centre = Rotate(Centre, axis, angle).Normalized;
        Vector3D up = Rotate(Up, axis, angle);
        up -= Centre * Vector3D.Dot(up, Centre);
        Up = up.LengthSquared > 1e-24 ? up.Normalized : NorthOr(Centre, Vector3D.UnitY);
    }

    // Rodrigues' rotation of v about a unit axis.
    private static Vector3D Rotate(Vector3D v, Vector3D axis, double angle)
    {
        double c = DMath.Cos(angle), s = DMath.Sin(angle);
        return v * c + Vector3D.Cross(axis, v) * s + axis * (Vector3D.Dot(axis, v) * (1 - c));
    }

    // Local north at a direction; at a pole, the given up laid on the ground there (or any direction along it).
    private static Vector3D NorthOr(Vector3D centre, Vector3D up)
    {
        if (GlobeProjection.Compass(centre) is { } compass)
        {
            return compass.North;
        }
        Vector3D u = up - centre * Vector3D.Dot(up, centre);
        return u.LengthSquared > 1e-24 ? u.Normalized : Vector3D.Cross(centre, Vector3D.UnitX).Normalized;
    }
}
