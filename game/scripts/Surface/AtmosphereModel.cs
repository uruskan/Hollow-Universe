using System;
using Godot;
using Hollow.Galaxy.Systems;

namespace HollowUniverse.Surface;

/// <summary>
/// The physically based air of a planet for drawing (GROUND.md §4.6): Rayleigh scattering from its molecules, Mie
/// scattering from haze, ozone absorption from its oxygen, all scaled from the planet's pressure, gravity and
/// temperature. Precomputes the transmittance and multiple-scattering tables the sky and aerial-perspective shaders
/// sample (after Bruneton 2017 and Hillaire 2020). Presentation only: ordinary maths is fine here.
/// </summary>
public sealed class AtmosphereModel
{
    public const int TransmittanceWidth = 256;
    public const int TransmittanceHeight = 64;
    public const int MultiScatteringSize = 32;

    // Earth's sea-level values (per metre) for the three render wavelengths (680, 550, 440 nm).
    private static readonly Vector3 EarthRayleigh = new(5.802e-6f, 13.558e-6f, 33.1e-6f);
    private static readonly Vector3 EarthOzone = new(0.650e-6f, 1.881e-6f, 0.085e-6f);

    public double BottomRadius { get; private init; }
    public double TopRadius { get; private init; }
    public Vector3 RayleighScattering { get; private init; }
    public double RayleighHeight { get; private init; }
    public double MieScattering { get; private init; }
    public double MieExtinction { get; private init; }
    public double MieHeight { get; private init; }
    public double MieG { get; private init; }
    public Vector3 OzoneAbsorption { get; private init; }
    public double OzoneCentre { get; private init; }
    public double OzoneHalfWidth { get; private init; }
    public Vector3 GroundAlbedo { get; private init; }

    public Image? Transmittance { get; private set; }
    public Image? MultiScattering { get; private set; }

    private float[] _transmittance = Array.Empty<float>();
    private float[] _multiScattering = Array.Empty<float>();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Body, System.Threading.Tasks.Task<AtmosphereModel>> Ahead = new();

    /// <summary>
    /// Starts computing a planet's air on a worker thread, for the next view that opens it (a game does it while its server
    /// starts, GROUND.md §4.3.1).
    /// </summary>
    public static void Prepare(Body planet) => Ahead.GetOrAdd(planet, p => System.Threading.Tasks.Task.Run(() => Built(p)));

    /// <summary>A planet's air with its tables computed: the one prepared ahead (waiting for it if need be), or a new one.</summary>
    public static AtmosphereModel Take(Body planet) =>
        Ahead.TryRemove(planet, out System.Threading.Tasks.Task<AtmosphereModel>? ahead) ? ahead.Result : Built(planet);

    private static AtmosphereModel Built(Body planet)
    {
        AtmosphereModel air = For(planet);
        air.Build();
        return air;
    }

    /// <summary>The air of a planet from its physical state: denser and taller with more pressure, lower gravity, more heat.</summary>
    public static AtmosphereModel For(Body planet)
    {
        PlanetState state = planet.State ?? throw new ArgumentException("No physical state.", nameof(planet));
        double pressure = Math.Max(planet.SurfacePressure, 0);
        double gravity = Math.Max(planet.SurfaceGravity, 0.05);
        double temperature = Math.Max(planet.Temperature, 50);
        double density = pressure * 288.0 / temperature; // molecules per volume, relative to Earth's
        // Scale height relative to Earth's 8 km: kT / (m g) with Earth-like air.
        double height = 8000.0 * (temperature / 288.0) / gravity;
        double oxygen = state.Gases.Oxygen * pressure / 0.21; // ozone follows oxygen
        double humidity = Math.Clamp(state.Gases.Water * pressure / 0.01, 0.3, 3); // haze follows water vapour
        return new AtmosphereModel
        {
            BottomRadius = planet.Radius,
            TopRadius = planet.Radius + 12 * height,
            RayleighScattering = EarthRayleigh * (float)density,
            RayleighHeight = height,
            MieScattering = 4.5e-6 * Math.Sqrt(density) * humidity,
            MieExtinction = 5.0e-6 * Math.Sqrt(density) * humidity,
            MieHeight = 1300 * height / 8000,
            MieG = 0.8,
            OzoneAbsorption = EarthOzone * (float)Math.Clamp(oxygen, 0, 3),
            OzoneCentre = 25000 * height / 8000,
            OzoneHalfWidth = 15000 * height / 8000,
            GroundAlbedo = new Vector3(0.18f, 0.2f, 0.19f),
        };
    }

    // ---------------------------------------------------------------- media

    private Vector3 Extinction(double h)
    {
        float r = (float)Math.Exp(-h / RayleighHeight);
        float m = (float)Math.Exp(-h / MieHeight);
        float o = (float)Math.Max(0, 1 - Math.Abs(h - OzoneCentre) / OzoneHalfWidth);
        return RayleighScattering * r + new Vector3(1, 1, 1) * (float)(MieExtinction * m) + OzoneAbsorption * o;
    }

    private Vector3 Scattering(double h) =>
        RayleighScattering * (float)Math.Exp(-h / RayleighHeight) + new Vector3(1, 1, 1) * (float)(MieScattering * Math.Exp(-h / MieHeight));

    // ---------------------------------------------------------------- transmittance table (Bruneton's mapping)

    private double HorizonLength => Math.Sqrt(TopRadius * TopRadius - BottomRadius * BottomRadius);

    private void TransmittanceUvToRMu(double u, double v, out double r, out double mu)
    {
        double hl = HorizonLength;
        double rho = hl * v;
        r = Math.Sqrt(rho * rho + BottomRadius * BottomRadius);
        double dMin = TopRadius - r, dMax = rho + hl;
        double d = dMin + u * (dMax - dMin);
        mu = d == 0 ? 1 : (hl * hl - rho * rho - d * d) / (2 * r * d);
        mu = Math.Clamp(mu, -1, 1);
    }

    private void RMuToTransmittanceUv(double r, double mu, out double u, out double v)
    {
        double hl = HorizonLength;
        double rho = Math.Sqrt(Math.Max(r * r - BottomRadius * BottomRadius, 0));
        double disc = r * r * (mu * mu - 1) + TopRadius * TopRadius;
        double d = Math.Max(-r * mu + Math.Sqrt(Math.Max(disc, 0)), 0);
        double dMin = TopRadius - r, dMax = rho + hl;
        u = (d - dMin) / (dMax - dMin);
        v = rho / hl;
    }

    private Vector3 IntegrateTransmittance(double r, double mu)
    {
        double disc = r * r * (mu * mu - 1) + TopRadius * TopRadius;
        double length = Math.Max(-r * mu + Math.Sqrt(Math.Max(disc, 0)), 0);
        const int Steps = 96;
        double dt = length / Steps;
        Vector3 depth = Vector3.Zero;
        for (int i = 0; i < Steps; i++)
        {
            double t = (i + 0.5) * dt;
            double ri = Math.Sqrt(r * r + 2 * r * mu * t + t * t);
            depth += Extinction(ri - BottomRadius) * (float)dt;
        }
        return new Vector3(MathF.Exp(-depth.X), MathF.Exp(-depth.Y), MathF.Exp(-depth.Z));
    }

    /// <summary>Transmittance from altitude <paramref name="h"/> towards the sky at cosine of zenith <paramref name="mu"/>; 0 when the planet is in the way.</summary>
    public Vector3 TransmittanceToSky(double h, double mu)
    {
        double r = BottomRadius + Math.Max(h, 1);
        double horizon = -Math.Sqrt(Math.Max(0, 1 - BottomRadius * BottomRadius / (r * r)));
        if (mu < horizon)
        {
            return Vector3.Zero;
        }
        RMuToTransmittanceUv(r, mu, out double u, out double v);
        return SampleTransmittance(u, v);
    }

    private Vector3 SampleTransmittance(double u, double v)
    {
        double x = Math.Clamp(u, 0, 1) * (TransmittanceWidth - 1), y = Math.Clamp(v, 0, 1) * (TransmittanceHeight - 1);
        int x0 = Math.Min((int)x, TransmittanceWidth - 2), y0 = Math.Min((int)y, TransmittanceHeight - 2);
        float fx = (float)(x - x0), fy = (float)(y - y0);
        Vector3 Get(int i, int j)
        {
            int k = (j * TransmittanceWidth + i) * 4;
            return new Vector3(_transmittance[k], _transmittance[k + 1], _transmittance[k + 2]);
        }
        Vector3 a = Get(x0, y0).Lerp(Get(x0 + 1, y0), fx), b = Get(x0, y0 + 1).Lerp(Get(x0 + 1, y0 + 1), fx);
        return a.Lerp(b, fy);
    }

    // ---------------------------------------------------------------- tables

    /// <summary>Computes both tables (about a tenth of a second); call once, off the main thread if you like.</summary>
    public void Build()
    {
        _transmittance = new float[TransmittanceWidth * TransmittanceHeight * 4];
        for (int j = 0; j < TransmittanceHeight; j++)
        {
            for (int i = 0; i < TransmittanceWidth; i++)
            {
                // Texel centres sit exactly on the table's ends, so the shader maps with the same half-texel inset.
                double u = (double)i / (TransmittanceWidth - 1), v = (double)j / (TransmittanceHeight - 1);
                TransmittanceUvToRMu(u, v, out double r, out double mu);
                Vector3 t = IntegrateTransmittance(r, mu);
                int k = (j * TransmittanceWidth + i) * 4;
                _transmittance[k] = t.X;
                _transmittance[k + 1] = t.Y;
                _transmittance[k + 2] = t.Z;
                _transmittance[k + 3] = 1;
            }
        }
        Transmittance = Image.CreateFromData(TransmittanceWidth, TransmittanceHeight, false, Image.Format.Rgbaf, ToBytes(_transmittance));

        int n = MultiScatteringSize;
        var ms = new float[n * n * 4];
        Godot.Vector3[] directions = SphereDirections(64);
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                double mus = (double)i / (n - 1) * 2 - 1;
                double h = Math.Max((double)j / (n - 1) * (TopRadius - BottomRadius), 1);
                Vector3 psi = MultipleScattering(h, mus, directions);
                int k = (j * n + i) * 4;
                ms[k] = psi.X;
                ms[k + 1] = psi.Y;
                ms[k + 2] = psi.Z;
                ms[k + 3] = 1;
            }
        }
        MultiScattering = Image.CreateFromData(n, n, false, Image.Format.Rgbaf, ToBytes(ms));
        _multiScattering = ms;
    }

    // Hillaire 2020: second-order isotropic scattering at a point, and the fraction re-scattered, summed as a series.
    private Vector3 MultipleScattering(double h, double mus, Godot.Vector3[] directions)
    {
        double r0 = BottomRadius + h;
        var sun = new Vector3((float)Math.Sqrt(Math.Max(0, 1 - mus * mus)), (float)mus, 0);
        Vector3 l2 = Vector3.Zero, fms = Vector3.Zero;
        const int Steps = 24;
        foreach (Vector3 dir in directions)
        {
            double mu = dir.Y;
            double discTop = r0 * r0 * (mu * mu - 1) + TopRadius * TopRadius;
            double tTop = -r0 * mu + Math.Sqrt(Math.Max(discTop, 0));
            double discGround = r0 * r0 * (mu * mu - 1) + BottomRadius * BottomRadius;
            bool ground = mu < 0 && discGround >= 0;
            double tMax = ground ? -r0 * mu - Math.Sqrt(discGround) : tTop;
            double dt = tMax / Steps;
            Vector3 throughput = Vector3.One, l = Vector3.Zero, f = Vector3.Zero;
            double nu = dir.Dot(sun);
            for (int s = 0; s < Steps; s++)
            {
                double t = (s + 0.5) * dt;
                double r = Math.Sqrt(r0 * r0 + 2 * r0 * mu * t + t * t);
                double hs = r - BottomRadius;
                double musAt = (r0 * mus + t * nu) / r;
                Vector3 scattering = Scattering(hs), extinction = Extinction(hs);
                Vector3 sunT = TransmittanceToSky(hs, musAt);
                Vector3 step = new(MathF.Exp(-extinction.X * (float)dt), MathF.Exp(-extinction.Y * (float)dt), MathF.Exp(-extinction.Z * (float)dt));
                Vector3 source = scattering * sunT * (float)(1 / (4 * Math.PI));
                l += throughput * Integrate(source, extinction, step);
                f += throughput * Integrate(scattering, extinction, step);
                throughput *= step;
            }
            if (ground)
            {
                double rg = BottomRadius;
                double musGround = (r0 * mus + tMax * nu) / rg;
                Vector3 sunT = TransmittanceToSky(1, musGround);
                l += throughput * sunT * (float)Math.Max(0, musGround) * GroundAlbedo / Mathf.Pi;
            }
            l2 += l;
            fms += f;
        }
        l2 /= directions.Length;
        fms /= directions.Length;
        return new Vector3(l2.X / (1 - fms.X), l2.Y / (1 - fms.Y), l2.Z / (1 - fms.Z));
    }


    // ---------------------------------------------------------------- aerial perspective table

    /// <summary>Size of the aerial-perspective table: azimuth, cosine of zenith, distance.</summary>
    public const int ApAzimuths = 32;
    public const int ApZeniths = 48;
    public const int ApDistances = 32;

    /// <summary>Distance the aerial-perspective table reaches (metres).</summary>
    public const double ApRange = 400_000;

    private Vector3 SampleMultiScattering(double h, double mus)
    {
        int n = MultiScatteringSize;
        double x = Math.Clamp((mus * 0.5 + 0.5) * (n - 1), 0, n - 1.001);
        double y = Math.Clamp(h / (TopRadius - BottomRadius) * (n - 1), 0, n - 1.001);
        int x0 = (int)x, y0 = (int)y;
        float fx = (float)(x - x0), fy = (float)(y - y0);
        Vector3 Get(int i, int j)
        {
            int k = (j * n + i) * 4;
            return new Vector3(_multiScattering[k], _multiScattering[k + 1], _multiScattering[k + 2]);
        }
        Vector3 a = Get(x0, y0).Lerp(Get(x0 + 1, y0), fx), b = Get(x0, y0 + 1).Lerp(Get(x0 + 1, y0 + 1), fx);
        return a.Lerp(b, fy);
    }

    private static float RayleighPhase(double nu) => (float)(3 / (16 * Math.PI) * (1 + nu * nu));

    private float MiePhase(double nu)
    {
        double g = MieG, g2 = g * g;
        return (float)(3 / (8 * Math.PI) * ((1 - g2) * (1 + nu * nu)) / ((2 + g2) * Math.Pow(Math.Max(1 + g2 - 2 * g * nu, 1e-4), 1.5)));
    }

    /// <summary>
    /// Light scattered toward a camera at <paramref name="altitude"/> along every direction, and the transmittance,
    /// out to <see cref="ApRange"/>: a table indexed by azimuth (around <paramref name="up"/>, from
    /// <paramref name="east"/>), cosine of zenith (square-root mapped, finer near the horizon) and distance
    /// (square-root mapped). Directions and lights are in render axes. About 20 ms; run it off the main thread.
    /// </summary>
    public void AerialPerspective(double altitude, Vector3 up, Vector3 east, Vector3 sunDirection, Vector3 sunIlluminance,
        Vector3 moonDirection, Vector3 moonIlluminance, float[] inscatter, float[] transmittance)
    {
        Vector3 north = up.Cross(east).Normalized();
        double r0 = BottomRadius + Math.Max(altitude, 0.5);
        double mus0 = sunDirection.Dot(up), mum0 = moonDirection.Dot(up);
        bool moon = moonIlluminance.LengthSquared() > 0;
        const int Sub = 4;
        System.Threading.Tasks.Parallel.For(0, ApZeniths, iz =>
        {
            double v = (double)iz / (ApZeniths - 1) * 2 - 1;
            double mu = Math.Sign(v) * v * v;
            double sinZ = Math.Sqrt(Math.Max(0, 1 - mu * mu));
            for (int ia = 0; ia < ApAzimuths; ia++)
            {
                double az = (double)ia / ApAzimuths * 2 * Math.PI;
                Vector3 d = (east * (float)(Math.Cos(az) * sinZ) + north * (float)(Math.Sin(az) * sinZ) + up * (float)mu).Normalized();
                double nus = d.Dot(sunDirection), num = d.Dot(moonDirection);
                float prs = RayleighPhase(nus), pms = MiePhase(nus), prm = RayleighPhase(num), pmm = MiePhase(num);
                Vector3 l = Vector3.Zero, t = Vector3.One;
                double previous = 0;
                for (int k = 0; k < ApDistances; k++)
                {
                    double w = (double)k / (ApDistances - 1);
                    double target = w * w * ApRange;
                    double dt = (target - previous) / Sub;
                    for (int s = 0; s < Sub && dt > 0; s++)
                    {
                        double tt = previous + (s + 0.5) * dt;
                        double r = Math.Sqrt(r0 * r0 + 2 * r0 * mu * tt + tt * tt);
                        // Past where a downward ray meets the sea the table is never read; keep it finite there.
                        double h = Math.Max(r - BottomRadius, 0);
                        float dr = (float)Math.Exp(-h / RayleighHeight), dm = (float)Math.Exp(-h / MieHeight);
                        float dz = (float)Math.Max(0, 1 - Math.Abs(h - OzoneCentre) / OzoneHalfWidth);
                        Vector3 sr = RayleighScattering * dr;
                        float sm = (float)MieScattering * dm;
                        Vector3 ext = sr + Vector3.One * (float)(MieExtinction * dm) + OzoneAbsorption * dz;
                        double mus = (r0 * mus0 + tt * nus) / r;
                        Vector3 src = sunIlluminance * (TransmittanceToSky(h, mus) * (sr * prs + Vector3.One * sm * pms)
                            + SampleMultiScattering(h, mus) * (sr + Vector3.One * sm));
                        if (moon)
                        {
                            double mum = (r0 * mum0 + tt * num) / r;
                            src += moonIlluminance * (TransmittanceToSky(h, mum) * (sr * prm + Vector3.One * sm * pmm)
                                + SampleMultiScattering(h, mum) * (sr + Vector3.One * sm));
                        }
                        Vector3 step = new(MathF.Exp(-ext.X * (float)dt), MathF.Exp(-ext.Y * (float)dt), MathF.Exp(-ext.Z * (float)dt));
                        l += t * Integrate(src, ext, step);
                        t *= step;
                    }
                    previous = target;
                    int index = ((k * ApZeniths + iz) * ApAzimuths + ia) * 4;
                    inscatter[index] = l.X;
                    inscatter[index + 1] = l.Y;
                    inscatter[index + 2] = l.Z;
                    inscatter[index + 3] = 1;
                    transmittance[index] = t.X;
                    transmittance[index + 1] = t.Y;
                    transmittance[index + 2] = t.Z;
                    transmittance[index + 3] = 1;
                }
            }
        });
    }

    // ∫ T · S over a step of constant medium, energy-conserving.
    private static Vector3 Integrate(Vector3 source, Vector3 extinction, Vector3 step) =>
        new(Safe(source.X, extinction.X, step.X), Safe(source.Y, extinction.Y, step.Y), Safe(source.Z, extinction.Z, step.Z));

    private static float Safe(float s, float e, float t) => e > 1e-12f ? (s - s * t) / e : 0;

    private static Godot.Vector3[] SphereDirections(int count)
    {
        var result = new Godot.Vector3[count];
        double golden = Math.PI * (3 - Math.Sqrt(5));
        for (int i = 0; i < count; i++)
        {
            double y = 1 - (i + 0.5) * 2.0 / count;
            double r = Math.Sqrt(1 - y * y);
            double a = golden * i;
            result[i] = new Godot.Vector3((float)(r * Math.Cos(a)), (float)y, (float)(r * Math.Sin(a)));
        }
        return result;
    }

    private static byte[] ToBytes(float[] data)
    {
        var bytes = new byte[data.Length * 4];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    // ---------------------------------------------------------------- star colours

    /// <summary>
    /// The colour of a blackbody of this temperature in linear sRGB, with luminance 1 (CIE 1931 observer, Wyman et
    /// al. 2013 fits). <paramref name="whiteBalance"/> 0–1 pulls it toward the colour a 6,500 K star would have,
    /// as eyes adapted to that sun would see it.
    /// </summary>
    public static Vector3 Blackbody(double kelvin, double whiteBalance = 0)
    {
        Vector3 c = BlackbodyRaw(kelvin);
        if (whiteBalance > 0)
        {
            Vector3 white = BlackbodyRaw(6500);
            Vector3 balanced = new(c.X / white.X, c.Y / white.Y, c.Z / white.Z);
            float lum = Luminance(balanced);
            balanced /= lum;
            c = c.Lerp(balanced, (float)whiteBalance);
        }
        return c / Luminance(c);
    }

    private static Vector3 BlackbodyRaw(double kelvin)
    {
        double x = 0, y = 0, z = 0;
        for (double lambda = 380; lambda <= 780; lambda += 5)
        {
            double l = lambda * 1e-9;
            double planck = 1 / (Math.Pow(l, 5) * (Math.Exp(1.4387769e-2 / (l * kelvin)) - 1));
            x += planck * (1.056 * G(lambda, 599.8, 37.9, 31.0) + 0.362 * G(lambda, 442.0, 16.0, 26.7) - 0.065 * G(lambda, 501.1, 20.4, 26.2));
            y += planck * (0.821 * G(lambda, 568.8, 46.9, 40.5) + 0.286 * G(lambda, 530.9, 16.3, 31.1));
            z += planck * (1.217 * G(lambda, 437.0, 11.8, 36.0) + 0.681 * G(lambda, 459.0, 26.0, 13.8));
        }
        double r = 3.2406 * x - 1.5372 * y - 0.4986 * z;
        double g = -0.9689 * x + 1.8758 * y + 0.0415 * z;
        double b = 0.0557 * x - 0.2040 * y + 1.0570 * z;
        double max = Math.Max(r, Math.Max(g, b));
        return new Vector3((float)Math.Max(r / max, 0), (float)Math.Max(g / max, 0), (float)Math.Max(b / max, 0));
    }

    private static double G(double x, double mu, double s1, double s2)
    {
        double s = x < mu ? s1 : s2;
        double t = (x - mu) / s;
        return Math.Exp(-0.5 * t * t);
    }

    public static float Luminance(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
}
