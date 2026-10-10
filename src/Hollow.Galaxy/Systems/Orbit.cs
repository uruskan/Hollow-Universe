using System;
using Hollow.Core;

namespace Hollow.Galaxy.Systems;

/// <summary>
/// Keplerian orbit around a parent body, in the system frame (reference plane x–z, +Y north). Angles in
/// radians, distances in metres, time in seconds since J2000. Deterministic: uses <see cref="DMath"/>.
/// </summary>
public readonly struct Orbit
{
    public readonly double SemiMajorAxis;
    public readonly double Eccentricity;
    public readonly double Inclination;
    public readonly double AscendingNode;
    public readonly double ArgumentOfPeriapsis;
    public readonly double MeanAnomalyAtEpoch;

    /// <summary>Orbital period in seconds.</summary>
    public readonly double Period;

    // Rotation from the perifocal frame, precomputed.
    private readonly double _px;
    private readonly double _py;
    private readonly double _pz;
    private readonly double _qx;
    private readonly double _qy;
    private readonly double _qz;

    public Orbit(double semiMajorAxis, double eccentricity, double inclination, double ascendingNode,
        double argumentOfPeriapsis, double meanAnomalyAtEpoch, double period)
    {
        SemiMajorAxis = semiMajorAxis;
        Eccentricity = eccentricity;
        Inclination = inclination;
        AscendingNode = ascendingNode;
        ArgumentOfPeriapsis = argumentOfPeriapsis;
        MeanAnomalyAtEpoch = meanAnomalyAtEpoch;
        Period = period;

        double co = DMath.Cos(ascendingNode);
        double so = DMath.Sin(ascendingNode);
        double cw = DMath.Cos(argumentOfPeriapsis);
        double sw = DMath.Sin(argumentOfPeriapsis);
        double ci = DMath.Cos(inclination);
        double si = DMath.Sin(inclination);

        // Ecliptic-style axes (X, Y in the reference plane, Z north) mapped to ours: x = X, y = Z, z = −Y.
        double pX = cw * co - sw * so * ci;
        double pY = cw * so + sw * co * ci;
        double pZ = sw * si;
        double qX = -sw * co - cw * so * ci;
        double qY = -sw * so + cw * co * ci;
        double qZ = cw * si;
        _px = pX;
        _py = pZ;
        _pz = -pY;
        _qx = qX;
        _qy = qZ;
        _qz = -qY;
    }

    /// <summary>Period of a two-body orbit, from the semi-major axis and the total mass (kg).</summary>
    public static double PeriodFor(double semiMajorAxis, double totalMass) =>
        DMath.TwoPi * Math.Sqrt(semiMajorAxis * semiMajorAxis * semiMajorAxis / (Units.GravitationalConstant * totalMass));

    public bool IsValid => SemiMajorAxis > 0 && Period > 0;

    public double Periapsis => SemiMajorAxis * (1 - Eccentricity);

    public double Apoapsis => SemiMajorAxis * (1 + Eccentricity);

    /// <summary>Position relative to the parent at a time (seconds since J2000).</summary>
    public Vector3D PositionAt(double secondsSinceJ2000)
    {
        if (!IsValid)
        {
            return Vector3D.Zero;
        }
        double meanAnomaly = MeanAnomalyAtEpoch + DMath.TwoPi * FractionalOrbits(secondsSinceJ2000);
        return PositionAtMeanAnomaly(meanAnomaly);
    }

    /// <summary>Position for a given mean anomaly (radians).</summary>
    public Vector3D PositionAtMeanAnomaly(double meanAnomaly)
    {
        double m = DMath.WrapAngle(meanAnomaly);
        double e = Eccentricity;
        double ecc = e < 0.8 ? m : DMath.Pi * Math.Sign(m == 0 ? 1 : m);
        for (int i = 0; i < 12; i++)
        {
            double f = ecc - e * DMath.Sin(ecc) - m;
            double fp = 1 - e * DMath.Cos(ecc);
            double step = f / fp;
            ecc -= step;
            if (Math.Abs(step) < 1e-13)
            {
                break;
            }
        }
        double p = SemiMajorAxis * (DMath.Cos(ecc) - e);
        double q = SemiMajorAxis * Math.Sqrt(Math.Max(0, 1 - e * e)) * DMath.Sin(ecc);
        return new Vector3D(_px * p + _qx * q, _py * p + _qy * q, _pz * p + _qz * q);
    }

    // Orbits since epoch, reduced to [0, 1) before converting to an angle, so precision holds for any date.
    private double FractionalOrbits(double t)
    {
        double orbits = t / Period;
        return orbits - Math.Floor(orbits);
    }
}
