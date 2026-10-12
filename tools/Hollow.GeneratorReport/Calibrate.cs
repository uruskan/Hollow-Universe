using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hollow.Core;
using Hollow.Galaxy;

namespace Hollow.GeneratorReport;

/// <summary>
/// The class table against the real neighbourhood: per level, inside the distance where the catalogue is complete
/// for that level, the real stars by kind against the density model's expected stars per class entry.
/// </summary>
public static class Calibrate
{
    /// <summary>Per level: within this distance the catalogue holds (nearly) every star of the level.</summary>
    public static readonly double[] CompleteWithin = { 1000, 800, 400, 200, 160, 80, 20, 20 };

    public static void Report(StarCatalogue catalogue)
    {
        var density = new DensityModel();
        CultureInfo c = CultureInfo.InvariantCulture;
        Span<double> components = stackalloc double[DensityModel.ComponentCount];
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            double radius = CompleteWithin[level];
            // Model: Monte Carlo over the sphere, per component, then split by the class table's weights.
            var perEntry = new double[StarClassTable.Entries.Count];
            var rng = new Rng(77);
            const int samples = 400_000;
            double volume = 4.0 / 3.0 * Math.PI * radius * radius * radius;
            for (int i = 0; i < samples; i++)
            {
                Vector3D p;
                do
                {
                    p = new Vector3D(rng.Range(-1, 1), rng.Range(-1, 1), rng.Range(-1, 1));
                }
                while (p.LengthSquared > 1);
                density.Evaluate(p * radius, components);
                for (int k = 0; k < DensityModel.ComponentCount; k++)
                {
                    Population population = DensityModel.PopulationOf((GalaxyComponent)k);
                    double total = StarClassTable.Entries.Sum(e => e.Weight(population));
                    for (int e = 0; e < StarClassTable.Entries.Count; e++)
                    {
                        StarClassEntry entry = StarClassTable.Entries[e];
                        if (entry.Level == level)
                        {
                            perEntry[e] += components[k] * entry.Weight(population) / total;
                        }
                    }
                }
            }
            for (int e = 0; e < perEntry.Length; e++)
            {
                perEntry[e] *= volume / samples;
            }
            var real = new Dictionary<string, int>();
            foreach (CatalogueStar s in catalogue.Primaries)
            {
                if (s.Level == level && s.Position.Length < radius)
                {
                    string key = s.Class.Kind == StarKind.MainSequence ? $"MS {s.Class.Letter}" : s.Class.Kind.ToString();
                    real[key] = real.GetValueOrDefault(key) + 1;
                }
            }
            Console.WriteLine(string.Create(c, $"level {level}, within {radius} ly: model {perEntry.Sum():0.0}, real {real.Values.Sum()}"));
            for (int e = 0; e < perEntry.Length; e++)
            {
                if (StarClassTable.Entries[e].Level == level)
                {
                    Console.WriteLine(string.Create(c, $"    model {StarClassTable.Entries[e].Name,-22} {perEntry[e],9:0.0}"));
                }
            }
            Console.WriteLine("    real  " + string.Join(", ", real.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
        }
    }
}
