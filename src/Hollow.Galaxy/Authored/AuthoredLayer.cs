using System.Collections.Generic;
using Hollow.Galaxy.Systems;

namespace Hollow.Galaxy.Authored;

/// <summary>A real planet: what has been measured. Everything else (spin, moons, physical state) is generated.</summary>
/// <param name="Designation">Its letter, e.g. "b".</param>
/// <param name="MassEarth">Mass in Earth masses (the minimum mass where only that is known, or an estimate from its radius).</param>
/// <param name="SemiMajorAu">Semi-major axis, AU.</param>
/// <param name="Eccentricity">Orbital eccentricity (0 where only an upper limit is known).</param>
/// <param name="Formation">Solid planet, ice giant or gas giant.</param>
internal sealed record AuthoredPlanet(string Designation, double MassEarth, double SemiMajorAu, double Eccentricity, Formation Formation);

/// <summary>The known planets of one star (or pair) of an authored system, named "&lt;Prefix&gt; &lt;designation&gt;".</summary>
internal sealed record AuthoredHost(int Node, string Prefix, AuthoredPlanet[] Planets);

/// <summary>
/// One real system's authored content (GALAXY.md §15), keyed by its catalogue ID: names for its stars and barycentres
/// by node number, and the known planets of its hosts, which replace the generated ones there.
/// </summary>
internal sealed record AuthoredSystem(int CatalogueId, string Name, IReadOnlyDictionary<int, string> StarNames, AuthoredHost[] Hosts, string Source)
{
    public AuthoredHost? HostFor(int node)
    {
        foreach (AuthoredHost host in Hosts)
        {
            if (host.Node == node)
            {
                return host;
            }
        }
        return null;
    }
}

/// <summary>
/// The authored layer (GALAXY.md §15): hand-written content from public facts, compiled into the engine (so it can
/// never be a missing data file) and versioned like a generator stage. Real systems are keyed by catalogue ID and
/// their bodies by body path (§10.1): famous exoplanet systems with their measured planets, and real names for
/// stars of multiple systems. Named regions (real clusters and nebulae) are in <see cref="Regions"/>. Sol is
/// hand-written separately (<see cref="SolSystem"/>).
/// </summary>
public static class AuthoredLayer
{
    /// <summary>Version of the authored content. Frozen with a shard like the generator versions (GALAXY.md §14).</summary>
    public const int Version = 1;

    private const double JupiterMass = 317.83; // Earth masses

    /// <summary>
    /// The authored content of a system, or null. Addresses are untrusted: anything that isn't a known catalogue ID
    /// of HYG (0) or AT-HYG (1), which share HYG's and the supplement's IDs (GALAXY.md §8.5), finds nothing.
    /// </summary>
    internal static AuthoredSystem? Find(SystemAddress address)
    {
        if (!address.IsCatalogue || address.CatalogueNumber > 1)
        {
            return null;
        }
        long id = address.CatalogueId;
        return id is >= 0 and <= int.MaxValue && Systems.TryGetValue((int)id, out AuthoredSystem? system) ? system : null;
    }

    internal static IReadOnlyCollection<AuthoredSystem> All => Systems.Values;

    private static readonly Dictionary<int, AuthoredSystem> Systems = Index(
        // Proxima Centauri (Alpha Centauri C): b in the habitable zone, d close in; minimum masses. Planet c
        // (1.5 AU) is disputed and left out.
        new AuthoredSystem(71456, "Alpha Centauri", new Dictionary<int, string> { [3] = "Proxima Centauri" },
            new[]
            {
                new AuthoredHost(3, "Proxima Centauri", new[]
                {
                    new AuthoredPlanet("d", 0.260, 0.02881, 0, Formation.Solid),
                    new AuthoredPlanet("b", 1.055, 0.04848, 0, Formation.Solid),
                }),
            },
            "Anglada-Escudé et al. 2016; Faria et al. 2022"),

        // Epsilon Indi: Ba and Bb, a pair of brown dwarfs (catalogue supplement, §8.4).
        new AuthoredSystem(108526, "Epsilon Indi",
            new Dictionary<int, string> { [2] = "Epsilon Indi Ba", [3] = "Epsilon Indi Bb", [9] = "Epsilon Indi B" },
            System.Array.Empty<AuthoredHost>(), "Dieterich et al. 2018"),

        // Gliese 229: Ba and Bb, brown dwarfs orbiting each other every 12 days.
        new AuthoredSystem(29222, "Gliese 229",
            new Dictionary<int, string> { [2] = "Gliese 229 Ba", [3] = "Gliese 229 Bb", [9] = "Gliese 229 B" },
            System.Array.Empty<AuthoredHost>(), "Xuan et al. 2024; Whitebook et al. 2024"),

        // Epsilon Eridani (Ran): one giant planet (AEgir), true mass from astrometry.
        new AuthoredSystem(16496, "Epsilon Eridani", new Dictionary<int, string>(),
            new[]
            {
                new AuthoredHost(1, "Epsilon Eridani", new[]
                {
                    new AuthoredPlanet("b", 1.00 * JupiterMass, 3.53, 0.06, Formation.GasGiant),
                }),
            },
            "Llop-Sayson et al. 2021"),

        // 51 Pegasi (Helvetios): the first planet found around a Sun-like star, a hot Jupiter (Dimidium).
        new AuthoredSystem(112997, "51 Pegasi", new Dictionary<int, string>(),
            new[]
            {
                new AuthoredHost(1, "51 Pegasi", new[]
                {
                    new AuthoredPlanet("b", 0.61 * JupiterMass, 0.05234, 0, Formation.GasGiant),
                }),
            },
            "Mayor & Queloz 1995; Birkby et al. 2017"),

        // Teegarden's Star (catalogue supplement): three temperate-to-cool rocky planets; minimum masses.
        new AuthoredSystem(1_000_028, "Teegarden's Star", new Dictionary<int, string>(),
            new[]
            {
                new AuthoredHost(1, "Teegarden's Star", new[]
                {
                    new AuthoredPlanet("b", 1.16, 0.0259, 0.03, Formation.Solid),
                    new AuthoredPlanet("c", 1.05, 0.0455, 0.04, Formation.Solid),
                    new AuthoredPlanet("d", 0.82, 0.0791, 0.07, Formation.Solid),
                }),
            },
            "Zechmeister et al. 2019; Dreizler et al. 2024"),

        // TRAPPIST-1 (catalogue supplement): seven Earth-sized planets, masses from transit timing.
        new AuthoredSystem(1_000_029, "TRAPPIST-1", new Dictionary<int, string>(),
            new[]
            {
                new AuthoredHost(1, "TRAPPIST-1", new[]
                {
                    new AuthoredPlanet("b", 1.374, 0.01154, 0.00622, Formation.Solid),
                    new AuthoredPlanet("c", 1.308, 0.01580, 0.00654, Formation.Solid),
                    new AuthoredPlanet("d", 0.388, 0.02227, 0.00837, Formation.Solid),
                    new AuthoredPlanet("e", 0.692, 0.02925, 0.00510, Formation.Solid),
                    new AuthoredPlanet("f", 1.039, 0.03849, 0.01007, Formation.Solid),
                    new AuthoredPlanet("g", 1.321, 0.04683, 0.00208, Formation.Solid),
                    new AuthoredPlanet("h", 0.326, 0.06189, 0.00567, Formation.Solid),
                }),
            },
            "Agol et al. 2021"),

        // Kepler-186 (catalogue supplement): five small planets, f in the habitable zone. Only radii are measured; the
        // masses are estimates for rocky bodies of those radii.
        new AuthoredSystem(1_000_030, "Kepler-186", new Dictionary<int, string>(),
            new[]
            {
                new AuthoredHost(1, "Kepler-186", new[]
                {
                    new AuthoredPlanet("b", 1.24, 0.0343, 0, Formation.Solid),
                    new AuthoredPlanet("c", 2.1, 0.0451, 0, Formation.Solid),
                    new AuthoredPlanet("d", 2.54, 0.0781, 0, Formation.Solid),
                    new AuthoredPlanet("e", 2.15, 0.11, 0, Formation.Solid),
                    new AuthoredPlanet("f", 1.44, 0.432, 0, Formation.Solid),
                }),
            },
            "Quintana et al. 2014; Torres et al. 2015"));

    private static Dictionary<int, AuthoredSystem> Index(params AuthoredSystem[] systems)
    {
        var index = new Dictionary<int, AuthoredSystem>(systems.Length);
        foreach (AuthoredSystem s in systems)
        {
            index.Add(s.CatalogueId, s);
        }
        return index;
    }
}
