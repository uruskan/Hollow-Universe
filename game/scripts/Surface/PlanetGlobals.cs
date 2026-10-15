using System;
using Godot;
using Hollow.Core;

namespace HollowUniverse.Surface;

/// <summary>
/// The planet under everything drawn (docs/WHOLE_WORLD.md §10.1–10.4): its centre in render space, its mean radius, and
/// what turns render axes into the body's, both as shader globals (<c>surface_planet.gdshaderinc</c>: up, heights, the
/// cube faces textures are measured on) and for the code that moves things along up (effects, gibs, ragdolls). The view
/// that draws a planet sets it (<see cref="Set"/>); until then, and in views without one, the centre lies 6,371 km below
/// the origin, so up is +Y and a height is Y, as a flat stage expects.
/// </summary>
public static class PlanetGlobals
{
    /// <summary>The radius assumed when no planet is drawn.</summary>
    public const double NoPlanetRadius = 6_371_000;

    private static bool _registered;

    /// <summary>The planet's centre in render space.</summary>
    public static Vector3 Centre { get; private set; } = new(0, -(float)NoPlanetRadius, 0);

    /// <summary>The mean radius heights are measured from, metres.</summary>
    public static double Radius { get; private set; } = NoPlanetRadius;

    /// <summary>|centre|² − radius², in double precision (see <see cref="Height"/>).</summary>
    public static double K { get; private set; }

    /// <summary>Render axes to body axes.</summary>
    public static Basis RenderToBody { get; private set; } = Basis.Identity;

    /// <summary>Up at a render-space point: away from the planet's centre.</summary>
    public static Vector3 Up(Vector3 p)
    {
        Vector3 d = p - Centre;
        return d.LengthSquared() > 1e-12f ? d.Normalized() : Vector3.Up;
    }

    /// <summary>
    /// The height of a render-space point above the mean radius, the same as the shaders' <c>hu_height</c>: exact to a
    /// fraction of a millimetre whatever the planet's size (no large number is subtracted from another).
    /// </summary>
    public static float Height(Vector3 p)
    {
        double px = p.X, py = p.Y, pz = p.Z, cx = Centre.X, cy = Centre.Y, cz = Centre.Z;
        double dx = px - cx, dy = py - cy, dz = pz - cz;
        double numerator = px * px + py * py + pz * pz - 2 * (px * cx + py * cy + pz * cz) + K;
        return (float)(numerator / (Math.Sqrt(dx * dx + dy * dy + dz * dz) + Radius));
    }

    /// <summary>A point moved along up so that it stands at a height above the mean radius.</summary>
    public static Vector3 AtHeight(Vector3 p, float height) => p + Up(p) * (height - Height(p));

    /// <summary>The planet under a render frame: <paramref name="radius"/> is the mean radius heights are measured from.</summary>
    public static void Set(RenderFrame frame, double radius)
    {
        Centre = frame.Centre;
        Radius = radius;
        double o = frame.Origin.Length;
        K = (o - radius) * (o + radius);
        RenderToBody = frame.Basis.Transposed();
        Register();
    }

    /// <summary>Back to no planet (a view that drew one closed).</summary>
    public static void Clear()
    {
        Centre = new Vector3(0, -(float)NoPlanetRadius, 0);
        Radius = NoPlanetRadius;
        K = 0;
        RenderToBody = Basis.Identity;
        Register();
    }

    /// <summary>Makes sure the globals exist (before any shader that reads them is loaded) and hold the current values.</summary>
    public static void Register()
    {
        void Put(string name, RenderingServer.GlobalShaderParameterType type, Variant value)
        {
            if (_registered)
            {
                RenderingServer.GlobalShaderParameterSet(name, value);
            }
            else
            {
                RenderingServer.GlobalShaderParameterAdd(name, type, value);
            }
        }
        Put("hu_planet_centre", RenderingServer.GlobalShaderParameterType.Vec3, Centre);
        Put("hu_ground_radius", RenderingServer.GlobalShaderParameterType.Float, (float)Radius);
        Put("hu_planet_k", RenderingServer.GlobalShaderParameterType.Float, (float)K);
        Put("hu_render_to_body", RenderingServer.GlobalShaderParameterType.Mat3, RenderToBody);
        _registered = true;
    }
}
