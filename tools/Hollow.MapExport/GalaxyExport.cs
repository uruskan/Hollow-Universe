using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Galaxy;
using HollowGalaxy = Hollow.Galaxy.Galaxy;

namespace Hollow.MapExport;

/// <summary>
/// galaxy.js: a galaxy-wide star sample drawn from the density model and the class table, a face-on glow
/// image, label anchors for the big structures, and the sectors around Sol.
/// </summary>
internal static class GalaxyExport
{
    // Sampling grid in the density model's frame (centred on Sgr A*, midplane horizontal).
    private const double Half = 62000;
    private const double Step = 500;
    private static readonly double[] HeightEdges = { 0, 80, 200, 400, 700, 1100, 1600, 2300, 3200, 4500, 6500, 9000, 12000 };

    /// <summary>Sample size per level (0 = brightest). About 250,000 in total.</summary>
    public static readonly int[] PerLevel = { 70000, 60000, 50000, 20000, 15000, 12000, 12000, 11000 };

    private const int PositionTries = 16;
    private const double SectorRadius = 3000;

    // The arms of DensityModel (private constants there; keep in step), with the real arms they stand for.
    private static readonly (string Id, string Name, double RadiusAtSun, double PitchDeg, double Strength, bool Spur)[] Arms =
    {
        ("scutum-centaurus", "Scutum–Centaurus Arm", 16140, 12, 1.0, false),
        ("sagittarius-carina", "Sagittarius–Carina Arm", 22500, 12, 1.0, false),
        ("perseus", "Perseus Arm", 31300, 12, 1.0, false),
        ("norma-outer", "Norma–Outer Arm", 43700, 12, 0.8, false),
        ("orion-spur", "Orion Spur", 27070, 11, 0.6, true),
    };

    public static void Write(string path, HollowGalaxy galaxy)
    {
        DensityModel density = galaxy.Density;
        int nx = (int)(2 * Half / Step);
        int nz = nx;
        int nh = 2 * (HeightEdges.Length - 1);
        var hLo = new double[nh];
        var hHi = new double[nh];
        int halfBins = HeightEdges.Length - 1;
        for (int k = 0; k < halfBins; k++)
        {
            hLo[k] = -HeightEdges[halfBins - k];
            hHi[k] = -HeightEdges[halfBins - k - 1];
            hLo[halfBins + k] = HeightEdges[k];
            hHi[halfBins + k] = HeightEdges[k + 1];
        }
        int boxes = nx * nz * nh;
        const int nc = DensityModel.ComponentCount;
        var lambda = new double[boxes * nc];
        Parallel.For(0, nx, ix =>
        {
            Span<double> comps = stackalloc double[nc];
            for (int iz = 0; iz < nz; iz++)
            {
                for (int ih = 0; ih < nh; ih++)
                {
                    int b = (ix * nz + iz) * nh + ih;
                    var centre = new Vector3D(-Half + (ix + 0.5) * Step, 0.5 * (hLo[ih] + hHi[ih]), -Half + (iz + 0.5) * Step);
                    density.EvaluateModel(centre, comps);
                    double volume = Step * Step * (hHi[ih] - hLo[ih]);
                    for (int c = 0; c < nc; c++)
                    {
                        lambda[b * nc + c] = comps[c] * volume;
                    }
                }
            }
        });

        // share[c, level]: fraction of component c's systems whose primary sits at that level.
        var share = new double[nc, StarClassTable.LevelCount];
        for (int c = 0; c < nc; c++)
        {
            for (int l = 0; l < StarClassTable.LevelCount; l++)
            {
                share[c, l] = StarClassTable.Share(DensityModel.PopulationOf((GalaxyComponent)c), l);
            }
        }

        int total = PerLevel.Sum();
        var pos = new float[total * 3];
        var col = new byte[total * 3];
        var mag = new byte[total];
        var lvl = new byte[total];
        var comp = new byte[total];
        var logLight = new double[total];
        var levelTotals = new double[StarClassTable.LevelCount];
        ulong sampleSeed = Hash64.Of("Hollow map-ux galaxy sample, version 1");
        var cdf = new double[boxes];
        Span<double> at = stackalloc double[nc];
        Span<double> weights = stackalloc double[nc];
        int n = 0;
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            double run = 0;
            for (int b = 0; b < boxes; b++)
            {
                double w = 0;
                for (int c = 0; c < nc; c++)
                {
                    w += lambda[b * nc + c] * share[c, level];
                }
                run += w;
                cdf[b] = run;
            }
            levelTotals[level] = run;
            double represented = run / PerLevel[level];
            var rng = new Rng(Hash64.Of(sampleSeed, (ulong)level));
            for (int i = 0; i < PerLevel[level]; i++)
            {
                int b = UpperBound(cdf, rng.NextDouble() * run);
                int ih = b % nh;
                int iz = b / nh % nz;
                int ix = b / nh / nz;
                double boxWeight = cdf[b] - (b > 0 ? cdf[b - 1] : 0);
                double volume = Step * Step * (hHi[ih] - hLo[ih]);
                double bound = 1.5 * boxWeight / volume;
                Vector3D p = default;
                for (int t = 0; t < PositionTries; t++)
                {
                    p = new Vector3D(-Half + (ix + rng.NextDouble()) * Step,
                        hLo[ih] + rng.NextDouble() * (hHi[ih] - hLo[ih]),
                        -Half + (iz + rng.NextDouble()) * Step);
                    density.EvaluateModel(p, at);
                    double d = 0;
                    for (int c = 0; c < nc; c++)
                    {
                        d += at[c] * share[c, level];
                    }
                    if (rng.NextDouble() * bound <= d)
                    {
                        break;
                    }
                }
                for (int c = 0; c < nc; c++)
                {
                    weights[c] = at[c] * share[c, level];
                }
                int component = rng.PickWeighted(weights);
                if (component < 0)
                {
                    // The last candidate fell where this level has no stars: use the box's own mix.
                    for (int c = 0; c < nc; c++)
                    {
                        weights[c] = lambda[b * nc + c] * share[c, level];
                    }
                    component = rng.PickWeighted(weights);
                }
                int entry = StarClassTable.Pick(DensityModel.PopulationOf((GalaxyComponent)component), level, ref rng);
                (StarClass cls, StarPhysics phys, _) = StarClassTable.Roll(entry, (GalaxyComponent)component, ref rng);
                Vector3D g = GalaxyFrame.FromModel(p);
                pos[3 * n] = (float)g.X;
                pos[3 * n + 1] = (float)g.Y;
                pos[3 * n + 2] = (float)g.Z;
                (float r, float gg, float bb) = StarColours.For(cls, phys.Temperature);
                col[3 * n] = StarColours.Byte(r);
                col[3 * n + 1] = StarColours.Byte(gg);
                col[3 * n + 2] = StarColours.Byte(bb);
                mag[n] = (byte)Math.Clamp(Math.Round((phys.AbsoluteMagnitude + 12.0) * 8.0), 0, 255);
                lvl[n] = (byte)level;
                comp[n] = (byte)component;
                double dm = StarColours.DisplayMagnitude(cls, phys.AbsoluteMagnitude);
                logLight[n] = Math.Log10(represented) - 0.4 * (dm - Units.SolarAbsoluteMagnitude);
                n++;
            }
            Console.WriteLine($"  level {level}: {PerLevel[level]} samples, {run:E3} systems in the sampled volume");
        }

        double[] sorted = logLight.OrderBy(v => v).ToArray();
        double lightLo = sorted[(int)(0.01 * (sorted.Length - 1))];
        double lightHi = sorted[(int)(0.999 * (sorted.Length - 1))];
        var bright = new byte[total];
        for (int i = 0; i < total; i++)
        {
            double t = Math.Clamp((logLight[i] - lightLo) / (lightHi - lightLo), 0, 1);
            bright[i] = (byte)Math.Round(255 * t);
        }

        // Face-on glow: systems per 500 × 500 ly column (all components, and the young disk alone).
        var glowAll = new double[nx * nz];
        var glowYoung = new double[nx * nz];
        for (int ix = 0; ix < nx; ix++)
        {
            for (int iz = 0; iz < nz; iz++)
            {
                double all = 0;
                double young = 0;
                for (int ih = 0; ih < nh; ih++)
                {
                    int b = (ix * nz + iz) * nh + ih;
                    for (int c = 0; c < nc; c++)
                    {
                        all += lambda[b * nc + c];
                    }
                    young += lambda[b * nc + (int)GalaxyComponent.YoungDisk];
                }
                glowAll[iz * nx + ix] = all;
                glowYoung[iz * nx + ix] = young;
            }
        }
        (byte[] glowAllBytes, double allLo, double allHi) = LogBytes(glowAll, 5);
        (byte[] glowYoungBytes, double youngLo, double youngHi) = LogBytes(glowYoung, 4);

        Vector3D gc = GalaxyFrame.GalacticCentre;
        Js.WriteGlobal(path, "HOLLOW_GALAXY",
            $"Hollow Universe: a {total}-star galaxy-wide sample, glow image, structure labels and sectors near Sol.", w =>
        {
            w.WriteStartObject();
            w.WritePropertyName("meta");
            w.WriteStartObject();
            w.WriteString("what", "Galaxy-wide sample drawn from Hollow's density model and star-class table (not individual generated stars), plus label anchors and sectors.");
            w.WriteString("seed", Js.Hex(galaxy.Seed));
            w.WriteNumber("generatorVersion", GeneratorVersion.Galaxy);
            w.WriteString("frame", "Galactic frame, light-years, origin Sol. +X toward galactic longitude 90 deg, +Y toward the north galactic pole (up), +Z toward the Galactic Centre. Right-handed, Y up.");
            w.WritePropertyName("galacticCentre");
            WriteVector(w, gc, 1);
            w.WriteEndObject();

            w.WritePropertyName("stars");
            w.WriteStartObject();
            w.WriteNumber("count", total);
            w.WriteString("encoding", "base64 of little-endian typed arrays: pos Float32Array (x,y,z per star, ly), col Uint8Array (r,g,b per star), mag/lvl/comp/bright Uint8Array (one per star)");
            w.WriteString("magDecode", "absolute magnitude M_V = mag / 8 - 12 (clamped to -12 ... 19.9; black holes and neutron stars read 19.9)");
            w.WriteString("brightDecode", "bright 0-255 is log10 of the light this sample point stands for (its level's weight x its V-band luminosity), scaled from brightLog10Min to brightLog10Max; light = 10^(min + bright/255*(max-min)) solar V luminosities");
            w.Number("brightLog10Min", Js.Round(lightLo, 4));
            w.Number("brightLog10Max", Js.Round(lightHi, 4));
            w.WritePropertyName("perLevel");
            WriteInts(w, PerLevel);
            w.WritePropertyName("levelSystems");
            w.WriteStartArray();
            foreach (double v in levelTotals)
            {
                w.WriteNumberValue(Js.Sig(v, 4));
            }
            w.WriteEndArray();
            w.WritePropertyName("components");
            w.WriteStartArray();
            foreach (string c in new[] { "young disk", "thin disk", "thick disk", "bulge/bar", "nuclear disk", "halo" })
            {
                w.WriteStringValue(c);
            }
            w.WriteEndArray();
            w.WriteString("coverage", "a square of +/-62,000 ly around Sgr A* in the plane (the disk ends near 49,000 ly), |height| <= 12,000 ly from the midplane");
            w.WriteString("pos", Convert.ToBase64String(MemoryMarshal.AsBytes(pos.AsSpan())));
            w.WriteString("col", Convert.ToBase64String(col));
            w.WriteString("mag", Convert.ToBase64String(mag));
            w.WriteString("lvl", Convert.ToBase64String(lvl));
            w.WriteString("comp", Convert.ToBase64String(comp));
            w.WriteString("bright", Convert.ToBase64String(bright));
            w.WriteEndObject();

            w.WritePropertyName("glow");
            w.WriteStartObject();
            w.WriteString("what", "Face-on image of the density model: systems per 500 x 500 ly column, projected along Y. Row-major: index = iz * size + ix.");
            w.WriteNumber("size", nx);
            w.WriteNumber("cellLy", Step);
            w.Number("x0", Js.Round(gc.X - Half, 1));
            w.Number("z0", Js.Round(gc.Z - Half, 1));
            w.WriteString("cellCentre", "X = x0 + (ix + 0.5) * cellLy, Z = z0 + (iz + 0.5) * cellLy (galactic frame)");
            w.WriteString("decode", "byte 0 = none; otherwise log10(systems per column) = log10Min + (byte - 1) / 254 * (log10Max - log10Min)");
            w.WritePropertyName("all");
            w.WriteStartObject();
            w.Number("log10Min", Js.Round(allLo, 4));
            w.Number("log10Max", Js.Round(allHi, 4));
            w.WriteString("data", Convert.ToBase64String(glowAllBytes));
            w.WriteEndObject();
            w.WritePropertyName("young");
            w.WriteStartObject();
            w.Number("log10Min", Js.Round(youngLo, 4));
            w.Number("log10Max", Js.Round(youngHi, 4));
            w.WriteString("data", Convert.ToBase64String(glowYoungBytes));
            w.WriteEndObject();
            w.WriteEndObject();

            WriteStructures(w);
            WriteSectors(w);
            w.WriteEndObject();
        });
    }

    private static void WriteStructures(Utf8JsonWriter w)
    {
        double r0 = GalaxyFrame.SunToCentre;
        w.WritePropertyName("structures");
        w.WriteStartArray();

        // Galactic Centre and the bar.
        w.WriteStartObject();
        w.WriteString("id", "galactic-centre");
        w.WriteString("name", "Galactic Centre");
        w.WriteString("kind", "centre");
        w.WriteString("note", "Sgr A*, l = 359.944 deg, b = -0.046 deg, 26,673 ly from Sol");
        w.WritePropertyName("label");
        WriteVector(w, GalaxyFrame.GalacticCentre, 0);
        w.WriteEndObject();

        double bar = 27 * Math.PI / 180;
        var barDir = new Vector3D(Math.Sin(bar), 0, -Math.Cos(bar));
        w.WriteStartObject();
        w.WriteString("id", "bar");
        w.WriteString("name", "Galactic Bar");
        w.WriteString("kind", "bar");
        w.WriteString("note", "Boxy bar/bulge: semi-axes 5,150 x 2,020 x 1,400 ly, long axis 27 deg from the Sun-centre line, near end toward positive longitudes. 'ends' are where the density falls to e^-2 of the centre. Label sits on the near half.");
        w.WritePropertyName("label");
        WriteVector(w, GalaxyFrame.FromModel(barDir * 5150), 0);
        w.WritePropertyName("ends");
        w.WriteStartArray();
        WriteVector(w, GalaxyFrame.FromModel(barDir * 10300), 0);
        WriteVector(w, GalaxyFrame.FromModel(barDir * -10300), 0);
        w.WriteEndArray();
        w.WriteEndObject();

        // Arms: the centre line of each, and one label anchor where the arm is strong.
        var labels = new Dictionary<string, (string Name, double? R, double? Theta)[]>
        {
            ["scutum-centaurus"] = new (string, double?, double?)[] { ("Scutum–Centaurus Arm", 20000, null) },
            ["sagittarius-carina"] = new (string, double?, double?)[] { ("Sagittarius–Carina Arm", 19000, null) },
            ["perseus"] = new (string, double?, double?)[] { ("Perseus Arm", 33000, null) },
            ["norma-outer"] = new (string, double?, double?)[] { ("Norma Arm", 14000, null), ("Outer Arm", 44000, null) },
            ["orion-spur"] = new (string, double?, double?)[] { ("Orion Spur", null, 12 * Math.PI / 180) },
        };
        foreach (var arm in Arms)
        {
            double pitch = arm.PitchDeg * Math.PI / 180;
            double cot = Math.Cos(pitch) / Math.Sin(pitch);
            double theta0 = Math.Log(arm.RadiusAtSun / r0) * cot;
            double ThetaAt(double r) => theta0 - Math.Log(r / r0) * cot;
            double RadiusAt(double theta) => r0 * Math.Exp((theta0 - theta) / cot);
            Vector3D ModelAt(double r)
            {
                double th = ThetaAt(r);
                return new Vector3D(r * Math.Sin(th), 0, -r * Math.Cos(th));
            }
            foreach ((string name, double? labelR, double? labelTheta) in labels[arm.Id])
            {
                double r = labelR ?? RadiusAt(labelTheta!.Value);
                Vector3D model = ModelAt(r);
                double strength = DensityModel.ArmStrength(model, wide: false);
                Vector3D g = GalaxyFrame.FromModel(model);
                Console.WriteLine($"  {name}: R = {r:F0} ly, theta = {ThetaAt(r) * 180 / Math.PI:F1} deg, " +
                    $"arm strength {strength:F2}, at ({g.X:F0}, {g.Y:F0}, {g.Z:F0}), {g.Length:F0} ly from Sol");
                w.WriteStartObject();
                w.WriteString("id", name.ToLowerInvariant().Replace(' ', '-').Replace("–", "-"));
                w.WriteString("name", name);
                w.WriteString("kind", arm.Spur ? "spur" : "arm");
                w.WriteString("path", arm.Id);
                w.Number("radiusFromCentreLy", Math.Round(r));
                w.Number("armStrength", Js.Round(strength, 2));
                w.WritePropertyName("label");
                WriteVector(w, g, 0);
                w.WriteEndObject();
            }
        }
        w.WriteEndArray();

        w.WritePropertyName("armPaths");
        w.WriteStartArray();
        foreach (var arm in Arms)
        {
            double pitch = arm.PitchDeg * Math.PI / 180;
            double cot = Math.Cos(pitch) / Math.Sin(pitch);
            double theta0 = Math.Log(arm.RadiusAtSun / r0) * cot;
            w.WriteStartObject();
            w.WriteString("id", arm.Id);
            w.WriteString("name", arm.Name);
            w.Number("pitchDeg", arm.PitchDeg);
            w.Number("radiusOnSunLineLy", arm.RadiusAtSun);
            w.WriteString("points", "[x, y, z, weight] in the galactic frame along the arm's centre line on the midplane; weight 0-1 is the arm's own strength there (fade-in, fade-out, spur window)");
            w.WritePropertyName("line");
            w.WriteStartArray();
            for (double lnR = Math.Log(9000); lnR <= Math.Log(59000) + 1e-9; lnR += 0.0125)
            {
                double r = Math.Exp(lnR);
                double th = theta0 - Math.Log(r / r0) * cot;
                double env = SmoothStep(9000, 13000, r) * (1 - SmoothStep(46000, 59000, r));
                if (arm.Spur)
                {
                    double lo = -40 * Math.PI / 180;
                    double hi = 35 * Math.PI / 180;
                    double edge = 10 * Math.PI / 180;
                    env *= SmoothStep(lo, lo + edge, th) * (1 - SmoothStep(hi - edge, hi, th));
                }
                double weight = env * arm.Strength;
                if (weight < 0.05)
                {
                    continue;
                }
                Vector3D g = GalaxyFrame.FromModel(new Vector3D(r * Math.Sin(th), 0, -r * Math.Cos(th)));
                w.WriteStartArray();
                w.WriteNumberValue(Math.Round(g.X));
                w.WriteNumberValue(Math.Round(g.Y));
                w.WriteNumberValue(Math.Round(g.Z));
                w.WriteNumberValue(Js.Round(weight, 2));
                w.WriteEndArray();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private static void WriteSectors(Utf8JsonWriter w)
    {
        SectorGrid.TrySectorOf(Vector3D.Zero, out int sx0, out int sy0, out int sz0);
        var sectors = new List<(int X, int Y, int Z, double D)>();
        for (int sx = sx0 - 4; sx <= sx0 + 4; sx++)
        {
            for (int sy = sy0 - 4; sy <= sy0 + 4; sy++)
            {
                for (int sz = sz0 - 4; sz <= sz0 + 4; sz++)
                {
                    Vector3D min = SectorGrid.SectorMin(sx, sy, sz);
                    double s = SectorGrid.SectorSize;
                    double dx = min.X > 0 ? min.X : (min.X + s < 0 ? min.X + s : 0);
                    double dy = min.Y > 0 ? min.Y : (min.Y + s < 0 ? min.Y + s : 0);
                    double dz = min.Z > 0 ? min.Z : (min.Z + s < 0 ? min.Z + s : 0);
                    double nearest = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (nearest <= SectorRadius)
                    {
                        sectors.Add((sx, sy, sz, SectorGrid.SectorCentre(sx, sy, sz).Length));
                    }
                }
            }
        }
        sectors.Sort((a, b) => a.D.CompareTo(b.D));
        w.WritePropertyName("sectors");
        w.WriteStartObject();
        w.WriteString("what", "Every 1,024-ly sector whose cube comes within 3,000 ly of Sol (sorted by centre distance). Sol is at the centre of its own sector.");
        w.WriteNumber("sizeLy", SectorGrid.SectorSize);
        w.WriteNumber("count", sectors.Count);
        w.WriteString("fields", "[name, sx, sy, sz, centreX, centreY, centreZ] (centre in the galactic frame, ly; the cube spans centre +/- 512 ly)");
        w.WritePropertyName("list");
        w.WriteStartArray();
        foreach ((int x, int y, int z, double _) in sectors)
        {
            Vector3D c = SectorGrid.SectorCentre(x, y, z);
            w.WriteStartArray();
            w.WriteStringValue(SectorNames.Name(x, y, z));
            w.WriteNumberValue(x);
            w.WriteNumberValue(y);
            w.WriteNumberValue(z);
            w.WriteNumberValue(c.X);
            w.WriteNumberValue(c.Y);
            w.WriteNumberValue(c.Z);
            w.WriteEndArray();
        }
        w.WriteEndArray();
        w.WriteEndObject();
        Console.WriteLine($"  sectors within {SectorRadius} ly: {sectors.Count}");
    }

    private static (byte[] Bytes, double Lo, double Hi) LogBytes(double[] values, double decades)
    {
        double max = values.Max();
        double hi = Math.Log10(max);
        double lo = hi - decades;
        var bytes = new byte[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] <= 0)
            {
                continue;
            }
            double t = Math.Clamp((Math.Log10(values[i]) - lo) / (hi - lo), 0, 1);
            bytes[i] = (byte)(1 + Math.Round(254 * t));
        }
        return (bytes, lo, hi);
    }

    private static int UpperBound(double[] cdf, double value)
    {
        int lo = 0;
        int hi = cdf.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (cdf[mid] > value)
            {
                hi = mid;
            }
            else
            {
                lo = mid + 1;
            }
        }
        return lo;
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static void WriteVector(Utf8JsonWriter w, Vector3D v, int decimals)
    {
        w.WriteStartArray();
        w.WriteNumberValue(Js.Round(v.X, decimals));
        w.WriteNumberValue(Js.Round(v.Y, decimals));
        w.WriteNumberValue(Js.Round(v.Z, decimals));
        w.WriteEndArray();
    }

    private static void WriteInts(Utf8JsonWriter w, int[] values)
    {
        w.WriteStartArray();
        foreach (int v in values)
        {
            w.WriteNumberValue(v);
        }
        w.WriteEndArray();
    }
}
