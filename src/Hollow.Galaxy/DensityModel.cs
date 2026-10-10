using System;
using Hollow.Core;

namespace Hollow.Galaxy;

/// <summary>
/// Number density of star systems (systems per ly³) in the Milky Way, as the sum of six components
/// (GALAXY.md §3). Deterministic: every function used is from <see cref="DMath"/> or exactly rounded.
/// </summary>
public sealed class DensityModel
{
    public const int ComponentCount = 6;

    /// <summary>Local density of systems at the Sun (0.081 per pc³).</summary>
    public const double LocalDensity = 0.00233;

    /// <summary>The total density never exceeds this (only reached in the inner core).</summary>
    public const double DensityCap = 1.0;

    // Disks.
    private const double R0 = GalaxyFrame.SunToCentre;
    private const double ScaleLengthThin = 8480;
    private const double ScaleHeightYoung = 330;
    private const double ScaleHeightThin = 980;
    private const double ScaleLengthThick = 11740;
    private const double ScaleHeightThick = 2935;
    private const double HoleRadius = 8000;
    private const double HoleDepth = 0.8;
    private const double EdgeRadius = 49000;
    private const double EdgeScale = 1500;

    // Bulge (boxy "G2" bar) and nuclear disk.
    private const double BarA = 5150;
    private const double BarB = 2020;
    private const double BarC = 1400;
    private const double BarAngleDeg = 27;
    private const double BulgeShapeIntegral = 20.6482155; // ∫ exp(−½·√((u²+v²)² + w⁴)) du dv dw
    private const double BulgeTotal = 3.5e10;
    private const double NuclearScale = 300;
    private const double NuclearFlattening = 0.25;
    private const double NuclearTotal = 1e9;

    // Halo.
    private const double HaloFlattening = 0.65;
    private const double HaloCore = 3000;
    private const double HaloExponent = -1.4; // applied to (R² + (h/q)² + core²), i.e. r^−2.8

    // Spiral arms.
    private const double ArmSigmaYoung = 1300;
    private const double ArmSigmaWide = 2600;
    private const double ArmFadeInStart = 9000;
    private const double ArmFadeInEnd = 13000;
    private const double ArmFadeOutStart = 46000;
    private const double ArmFadeOutEnd = 59000;

    // Local shares at the Sun.
    private const double ShareYoung = 0.02;
    private const double ShareThin = 0.86;
    private const double ShareThick = 0.118;
    private const double ShareHalo = 0.002;

    private readonly record struct Arm(double Theta0, double Cot, double SinPitch, double Strength, bool IsSpur);

    private static readonly Arm[] Arms;
    private static readonly double BarDirX;
    private static readonly double BarDirZ;
    private static readonly double BarPerpX;
    private static readonly double BarPerpZ;
    private static readonly double SpurMinTheta = -40 * DMath.DegToRad;
    private static readonly double SpurMaxTheta = 35 * DMath.DegToRad;
    private static readonly double SpurEdge = 10 * DMath.DegToRad;

    private readonly double[] _norm = new double[ComponentCount];

    static DensityModel()
    {
        Arms = new[]
        {
            MakeArm(16140, 12, 1.0, false), // inner arm
            MakeArm(22500, 12, 1.0, false), // near arm, inside the Sun
            MakeArm(31300, 12, 1.0, false), // far arm, outside the Sun
            MakeArm(43700, 12, 0.8, false), // outer arm
            MakeArm(27070, 11, 0.6, true), // local spur
        };
        double bar = BarAngleDeg * DMath.DegToRad;
        BarDirX = DMath.Sin(bar);
        BarDirZ = -DMath.Cos(bar);
        BarPerpX = DMath.Cos(bar);
        BarPerpZ = DMath.Sin(bar);
    }

    public DensityModel()
    {
        Span<double> sun = stackalloc double[ComponentCount];
        Shapes(GalaxyFrame.SunInModel, sun);
        _norm[(int)GalaxyComponent.YoungDisk] = ShareYoung * LocalDensity / sun[(int)GalaxyComponent.YoungDisk];
        _norm[(int)GalaxyComponent.ThinDisk] = ShareThin * LocalDensity / sun[(int)GalaxyComponent.ThinDisk];
        _norm[(int)GalaxyComponent.ThickDisk] = ShareThick * LocalDensity / sun[(int)GalaxyComponent.ThickDisk];
        _norm[(int)GalaxyComponent.Halo] = ShareHalo * LocalDensity / sun[(int)GalaxyComponent.Halo];
        _norm[(int)GalaxyComponent.Bulge] = BulgeTotal / (BarA * BarB * BarC * BulgeShapeIntegral);
        _norm[(int)GalaxyComponent.NuclearDisk] =
            NuclearTotal / (8.0 * DMath.Pi * NuclearFlattening * NuclearScale * NuclearScale * NuclearScale);
    }

    /// <summary>The population each component's stars belong to.</summary>
    public static Population PopulationOf(GalaxyComponent component) => component switch
    {
        GalaxyComponent.YoungDisk => Population.Young,
        GalaxyComponent.ThinDisk => Population.Disk,
        _ => Population.Old,
    };

    /// <summary>Typical metallicity [Fe/H] (mean, spread) of a component's stars.</summary>
    public static (double Mean, double Sigma) MetallicityOf(GalaxyComponent component) => component switch
    {
        GalaxyComponent.YoungDisk => (0.05, 0.1),
        GalaxyComponent.ThinDisk => (-0.05, 0.2),
        GalaxyComponent.ThickDisk => (-0.55, 0.25),
        GalaxyComponent.Bulge => (0.0, 0.35),
        GalaxyComponent.NuclearDisk => (0.1, 0.25),
        _ => (-1.5, 0.5),
    };

    /// <summary>Density of each component at a galactic position. Returns the (capped) total.</summary>
    public double Evaluate(Vector3D galactic, Span<double> components) =>
        EvaluateModel(GalaxyFrame.ToModel(galactic), components);

    /// <summary>Density of each component at a model-frame position. Returns the (capped) total.</summary>
    public double EvaluateModel(Vector3D model, Span<double> components)
    {
        Shapes(model, components);
        double total = 0;
        for (int i = 0; i < ComponentCount; i++)
        {
            components[i] *= _norm[i];
            total += components[i];
        }
        if (total > DensityCap)
        {
            double scale = DensityCap / total;
            for (int i = 0; i < ComponentCount; i++)
            {
                components[i] *= scale;
            }
            total = DensityCap;
        }
        return total;
    }

    /// <summary>Total density at a galactic position.</summary>
    public double Total(Vector3D galactic)
    {
        Span<double> c = stackalloc double[ComponentCount];
        return Evaluate(galactic, c);
    }

    /// <summary>Spiral-arm strength at a model-frame position (0 between arms); used by visuals too.</summary>
    public static double ArmStrength(Vector3D model, bool wide)
    {
        double r = Math.Sqrt(model.X * model.X + model.Z * model.Z);
        ArmTerms(r, model.X, model.Z, out double young, out double wideTerm);
        return wide ? wideTerm : young;
    }

    // Unnormalised component shapes.
    private static void Shapes(Vector3D m, Span<double> c)
    {
        double x = m.X;
        double h = m.Y;
        double z = m.Z;
        double r2 = x * x + z * z;
        double r = Math.Sqrt(r2);

        double hole = 1.0 - HoleDepth * DMath.Exp(-r2 / (HoleRadius * HoleRadius));
        double edge = r > EdgeRadius ? DMath.Exp(-(r - EdgeRadius) / EdgeScale) : 1.0;
        double disk = hole * edge;
        double radialThin = DMath.Exp(-(r - R0) / ScaleLengthThin);
        double radialThick = DMath.Exp(-(r - R0) / ScaleLengthThick);
        ArmTerms(r, x, z, out double armsYoung, out double armsWide);

        c[(int)GalaxyComponent.YoungDisk] =
            radialThin * DMath.Sech2(h / (2 * ScaleHeightYoung)) * (0.15 + armsYoung) * disk;
        c[(int)GalaxyComponent.ThinDisk] =
            radialThin * DMath.Sech2(h / (2 * ScaleHeightThin)) * (1.0 + 0.3 * armsWide) * disk;
        c[(int)GalaxyComponent.ThickDisk] = radialThick * DMath.Sech2(h / (2 * ScaleHeightThick)) * disk;

        double along = x * BarDirX + z * BarDirZ;
        double across = x * BarPerpX + z * BarPerpZ;
        double u = along / BarA;
        double v = across / BarB;
        double w = h / BarC;
        double s = u * u + v * v;
        double w2 = w * w;
        double rs2 = Math.Sqrt(s * s + w2 * w2);
        c[(int)GalaxyComponent.Bulge] = rs2 < 90 ? DMath.Exp(-0.5 * rs2) : 0.0;

        double hn = h / NuclearFlattening;
        double rn = Math.Sqrt(r2 + hn * hn);
        c[(int)GalaxyComponent.NuclearDisk] = rn < 40 * NuclearScale ? DMath.Exp(-rn / NuclearScale) : 0.0;

        double hh = h / HaloFlattening;
        c[(int)GalaxyComponent.Halo] = DMath.Exp(HaloExponent * DMath.Log(r2 + hh * hh + HaloCore * HaloCore));
    }

    private static void ArmTerms(double r, double x, double z, out double young, out double wide)
    {
        young = 0;
        wide = 0;
        if (r < ArmFadeInStart || r > ArmFadeOutEnd)
        {
            return;
        }
        double envelope = DMath.SmoothStep(ArmFadeInStart, ArmFadeInEnd, r)
            * (1.0 - DMath.SmoothStep(ArmFadeOutStart, ArmFadeOutEnd, r));
        double theta = DMath.Atan2(x, -z);
        double lnR = DMath.Log(r / R0);
        foreach (Arm arm in Arms)
        {
            double window = 1.0;
            if (arm.IsSpur)
            {
                window = DMath.SmoothStep(SpurMinTheta, SpurMinTheta + SpurEdge, theta)
                    * (1.0 - DMath.SmoothStep(SpurMaxTheta - SpurEdge, SpurMaxTheta, theta));
                if (window <= 0)
                {
                    continue;
                }
            }
            double armTheta = arm.Theta0 - lnR * arm.Cot;
            double delta = DMath.WrapAngle(theta - armTheta);
            double d = r * Math.Abs(delta) * arm.SinPitch;
            double d2 = d * d;
            double weight = arm.Strength * window;
            young += weight * DMath.Exp(-d2 / (2 * ArmSigmaYoung * ArmSigmaYoung));
            wide += weight * DMath.Exp(-d2 / (2 * ArmSigmaWide * ArmSigmaWide));
        }
        young *= envelope;
        wide *= envelope;
    }

    private static Arm MakeArm(double radiusAtSunDirection, double pitchDeg, double strength, bool isSpur)
    {
        double pitch = pitchDeg * DMath.DegToRad;
        double tan = DMath.Sin(pitch) / DMath.Cos(pitch);
        double cot = 1.0 / tan;
        double theta0 = DMath.Log(radiusAtSunDirection / R0) * cot;
        return new Arm(theta0, cot, DMath.Sin(pitch), strength, isSpur);
    }
}
