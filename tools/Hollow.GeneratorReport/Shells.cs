using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Galaxy;

namespace Hollow.GeneratorReport;

/// <summary>
/// Real plus generated stars per level in shells around Sol, against what the density model expects
/// (GALAXY.md §8.3): shows where the catalogue and the procedural stars double up, or leave a gap.
/// </summary>
public static class Shells
{
    private static readonly double[] Edges =
    {
        0, 10, 15, 20, 25, 30, 40, 50, 65, 80, 100, 130, 160, 200, 250, 320, 400, 500, 650, 800, 1000, 1250, 1500,
        2000, 2500, 3000, 4000,
    };

    /// <summary>How far each level's procedural stars are actually generated (beyond: the model's count is used).</summary>
    private static readonly double[] GenerateWithin = { 4000, 4000, 4000, 3000, 2000, 1500, 800, 400 };

    public static void Report(StarCatalogue catalogue)
    {
        var galaxy = new Galaxy.Galaxy(catalogue);
        var profile = new StarCountProfile();
        profile.Compute(galaxy.Density, Vector3D.Zero);
        int bins = Edges.Length - 1;
        CultureInfo c = CultureInfo.InvariantCulture;
        double overModel = 0;
        double overReal = 0;
        double totalModel = 0;
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            var model = new double[bins];
            var real = new double[bins];
            var generated = new long[bins];
            for (int b = 0; b < bins; b++)
            {
                model[b] = profile.CountWithin(level, Edges[b + 1]) - profile.CountWithin(level, Edges[b]);
            }
            foreach (CatalogueStar s in catalogue.Primaries)
            {
                if (s.Level == level)
                {
                    int b = Bin(s.Position.Length);
                    if (b >= 0)
                    {
                        real[b]++;
                    }
                }
            }
            double limit = GenerateWithin[level];
            List<CellKey> cells = Program.CellsAround(level, Vector3D.Zero, limit).ToList();
            Parallel.ForEach(cells, cell =>
            {
                foreach (GalaxyStar s in galaxy.GenerateProcedural(cell))
                {
                    double d = s.Position.Length;
                    int b = Bin(d);
                    if (b >= 0 && d < limit)
                    {
                        Interlocked.Increment(ref generated[b]);
                    }
                }
            });

            Console.WriteLine();
            Console.WriteLine(string.Create(c, $"level {level}: shell (ly) | model | real | generated | (real + generated) / model | real / model"));
            for (int b = 0; b < bins; b++)
            {
                if (model[b] < 0.5 && real[b] == 0)
                {
                    continue;
                }
                bool measured = Edges[b + 1] <= limit;
                string gen = measured ? generated[b].ToString(c) : "(model)";
                double proc = measured ? generated[b] : model[b];
                double sum = (real[b] + proc) / model[b];
                Console.WriteLine(string.Create(c,
                    $"  {Edges[b],5:0}–{Edges[b + 1],-5:0} | {model[b],10:0.0} | {real[b],6:0} | {gen,9} | {sum,6:0.00} | {real[b] / model[b],6:0.000}"));
                if (Edges[b] >= 0)
                {
                    overModel += model[b];
                    overReal += real[b] + proc - model[b];
                }
            }
            totalModel += model.Sum();
            Fit(level, model, real);
        }
        Console.WriteLine();
        Console.WriteLine(string.Create(c, $"all levels within {Edges[^1]} ly: model {totalModel:E3}, real + generated − model = {overReal:E3} ({100 * overReal / overModel:0.0}%)"));
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            Console.WriteLine(string.Create(c, $"level {level}: catalogue only within {CatalogueCompleteness.CompleteWithin(level):0.0} ly, " +
                $"procedural only beyond {CatalogueCompleteness.EmptyBeyond(level):0.0} ly"));
        }
    }

    // Least-squares fit of the catalogue's completeness to real / model per shell, inside the catalogue's import
    // limit, weighting each shell by its Poisson variance. The form has a complete core that ends at d0 (the
    // catalogue of nearby stars) and the bulk p, which falls at d50 (the magnitude limit):
    // c(d) = (1 − p) / (1 + (d / d0)⁸) + p / (1 + (d / d50)^k).
    private static void Fit(int level, double[] model, double[] real)
    {
        double cut = level <= 1 ? 3000 : 1500;
        var radii = new double[model.Length];
        for (int b = 0; b < model.Length; b++)
        {
            radii[b] = Math.Cbrt((Edges[b] * Edges[b] * Edges[b] + Edges[b + 1] * Edges[b + 1] * Edges[b + 1]) / 2);
        }
        (double P, double D0, double D50, double K, double Chi2) best = (1, 0, 0, 0, double.MaxValue);
        for (double p = 0.4; p <= 1.0001; p += 0.02)
        {
            for (double logD0 = 0.8; logD0 <= 3.3; logD0 += 0.025)
            {
                double d0 = Math.Pow(10, logD0);
                if (p > 0.999 && logD0 > 0.8)
                {
                    break; // without a core, d0 doesn't matter
                }
                for (double logD = logD0; logD <= 3.5; logD += 0.01)
                {
                    double d50 = Math.Pow(10, logD);
                    for (double k = 1; k <= 12; k += 0.1)
                    {
                        double chi2 = 0;
                        for (int b = 0; b < model.Length; b++)
                        {
                            if (Edges[b + 1] > cut || model[b] < 2)
                            {
                                continue;
                            }
                            double c = (1 - p) / (1 + Math.Pow(radii[b] / d0, 8)) + p / (1 + Math.Pow(radii[b] / d50, k));
                            double expected = model[b] * c;
                            chi2 += (real[b] - expected) * (real[b] - expected) / Math.Max(expected, 1);
                            if (chi2 > best.Chi2)
                            {
                                break;
                            }
                        }
                        if (chi2 < best.Chi2)
                        {
                            best = (p, d0, d50, k, chi2);
                        }
                    }
                }
            }
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  fit: p = {best.P:0.00}, d0 = {best.D0:0.0} ly, d50 = {best.D50:0.0} ly, k = {best.K:0.0} (χ² {best.Chi2:0.0})"));
    }

    private static int Bin(double d)
    {
        for (int b = 0; b < Edges.Length - 1; b++)
        {
            if (d >= Edges[b] && d < Edges[b + 1])
            {
                return b;
            }
        }
        return -1;
    }
}
