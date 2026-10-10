using System;
using Hollow.Core;

namespace Hollow.Galaxy;

/// <summary>
/// The galactic frame and the density model's frame (GALAXY.md §2).
/// Galactic frame: light-years, origin Sol, +Z toward the Galactic Centre, +Y toward the north galactic pole,
/// +X toward galactic longitude 90°. Right-handed, Y up.
/// </summary>
public static class GalaxyFrame
{
    /// <summary>Distance from the Sun to the Galactic Centre (8.178 kpc).</summary>
    public const double SunToCentre = 26673.0;

    /// <summary>Height of the Sun above the disk midplane (20.8 pc).</summary>
    public const double SunHeight = 67.8;

    /// <summary>Sgr A* in the galactic frame (l = 359.944°, b = −0.046°, 8.178 kpc).</summary>
    public static readonly Vector3D GalacticCentre = FromLongitudeLatitude(359.944, -0.046, SunToCentre);

    // Rows of the ICRS (J2000 equatorial) to galactic rotation, in our axis order.
    private static readonly Vector3D TowardCentre = new(-0.0548755604162154, -0.8734370902348850, -0.4838350155487132);
    private static readonly Vector3D TowardL90 = new(+0.4941094278755837, -0.4448296299600112, +0.7469822444972189);
    private static readonly Vector3D TowardNorthPole = new(-0.8676661490190047, -0.1980763734312015, +0.4559837761750669);

    // Tilt of the model frame about X, so the Sun sits SunHeight above the midplane that passes through Sgr A*.
    private static readonly double TiltSin;
    private static readonly double TiltCos;

    /// <summary>The Sun's position in the model frame.</summary>
    public static readonly Vector3D SunInModel;

    static GalaxyFrame()
    {
        Vector3D sunRelative = -GalacticCentre; // Sun minus centre
        // Solve y·cos φ − z·sin φ = SunHeight: A·cos φ + B·sin φ = H with A = y, B = −z.
        double a = sunRelative.Y;
        double b = -sunRelative.Z;
        double r = Math.Sqrt(a * a + b * b);
        double phi = DMath.Atan2(b, a) - DMath.Acos(SunHeight / r);
        TiltSin = DMath.Sin(phi);
        TiltCos = DMath.Cos(phi);
        SunInModel = ToModel(Vector3D.Zero);
    }

    /// <summary>Unit direction for galactic longitude and latitude in degrees, scaled by <paramref name="distance"/>.</summary>
    public static Vector3D FromLongitudeLatitude(double longitudeDeg, double latitudeDeg, double distance = 1.0)
    {
        double l = longitudeDeg * DMath.DegToRad;
        double b = latitudeDeg * DMath.DegToRad;
        double cb = DMath.Cos(b);
        return new Vector3D(cb * DMath.Sin(l), DMath.Sin(b), cb * DMath.Cos(l)) * distance;
    }

    /// <summary>Converts a J2000 equatorial vector (any unit) to the galactic frame (same unit).</summary>
    public static Vector3D FromEquatorial(Vector3D equatorial) => new(
        Vector3D.Dot(TowardL90, equatorial),
        Vector3D.Dot(TowardNorthPole, equatorial),
        Vector3D.Dot(TowardCentre, equatorial));

    /// <summary>Unit vector for right ascension and declination in degrees (J2000), in the galactic frame.</summary>
    public static Vector3D FromRaDec(double raDeg, double decDeg, double distance = 1.0)
    {
        double ra = raDeg * DMath.DegToRad;
        double dec = decDeg * DMath.DegToRad;
        double cd = DMath.Cos(dec);
        var eq = new Vector3D(cd * DMath.Cos(ra), cd * DMath.Sin(ra), DMath.Sin(dec));
        return FromEquatorial(eq) * distance;
    }

    /// <summary>Galactic frame → model frame (centred on Sgr A*, midplane horizontal).</summary>
    public static Vector3D ToModel(Vector3D galactic)
    {
        Vector3D p = galactic - GalacticCentre;
        return new Vector3D(p.X, p.Y * TiltCos - p.Z * TiltSin, p.Y * TiltSin + p.Z * TiltCos);
    }

    /// <summary>Model frame → galactic frame.</summary>
    public static Vector3D FromModel(Vector3D model)
    {
        var p = new Vector3D(model.X, model.Y * TiltCos + model.Z * TiltSin, -model.Y * TiltSin + model.Z * TiltCos);
        return p + GalacticCentre;
    }
}
