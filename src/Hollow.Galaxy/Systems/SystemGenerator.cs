using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Hollow.Core;

namespace Hollow.Galaxy.Systems;

/// <summary>
/// Generates a star system's stars, planets, moons, rings and belts from its primary star (GALAXY.md §10). A pure
/// function of (seed, address): the same system on every machine. Every object draws from its own random stream
/// (ARCHITECTURE.md §8.3): the layout, each companion, each star, each pair's orbit, each planet host's layout, each
/// orbit slot and each moon. Changing how one moon is generated never shifts a planet.
/// <para>
/// Multiple stars form a barycentric tree (§10.1): two stars, or a star and an inner pair, orbit their common
/// barycentre. Every star and every barycentre can host planets: around one star (S-type), or around a close pair
/// (P-type, circumbinary), each zone limited by what orbits outside it.
/// </para>
/// </summary>
public sealed class SystemGenerator
{
    private const double Au = Units.MetresPerAu;
    private const double AuPerLightYear = Units.MetresPerLightYear / Units.MetresPerAu;
    private const double AuPerSolarRadius = Units.SolarRadius / Units.MetresPerAu;
    private const int MaxPlanets = 14;
    private const int MaxSlots = 40;

    // Widest rolled companion orbit (semi-major axis), and the farthest any rolled companion may swing out
    // (0.63 ly; at most 0.85 eccentricity keeps the widest orbit inside it). Bound pairs much wider than that are
    // rare and fragile.
    private const double MaxCompanionSeparationAu = 20000;
    private const double MaxCompanionApoapsisAu = 40000;

    // Stream tags inside a system.
    private const ulong LayoutStream = 1;
    private const ulong PrimaryStream = 2;
    private const ulong CompanionStream = 0x100;
    private const ulong PairStream = 0x200;
    private const ulong StarStream = 0x300;
    private const ulong HostStream = 0x400;
    private const ulong SlotStream = 0x10000;
    private const ulong MoonStream = 0x1000000;

    private readonly ulong _seed;

    /// <summary>A system generator for a universe seed, at the current system-stage version.</summary>
    public SystemGenerator(ulong seed)
        : this(seed, GeneratorVersion.System)
    {
    }

    /// <summary>Test seam: <paramref name="version"/> stands in for another system-stage version.</summary>
    internal SystemGenerator(ulong seed, int version)
    {
        _seed = GeneratorVersion.SystemSeed(seed, version);
    }

    /// <summary>A companion before the tree is built: one star, or a close pair of stars that orbit together.</summary>
    private sealed record Companion(StarClass Class, StarPhysics Physics, double SeparationAu, double Eccentricity)
    {
        public StarClass TwinClass { get; init; }
        public StarPhysics TwinPhysics { get; init; }
        public double TwinSeparationAu { get; init; }
        public double TwinEccentricity { get; init; }
        public bool HasTwin => TwinSeparationAu > 0;
        public double Mass => Physics.Mass + (HasTwin ? TwinPhysics.Mass : 0);
    }

    /// <summary>A node of the stellar tree: a star, or a pair (two nodes orbiting their barycentre).</summary>
    private sealed class Node
    {
        public Node? Left;
        public Node? Right;
        public Node? Parent;
        public StarClass Class;
        public StarPhysics Physics;
        public double SeparationAu;
        public double Eccentricity;
        public int Number;
        public string Letters = "";
        public int BodyIndex;

        public bool IsStar => Left == null;

        public double Mass => IsStar ? Math.Max(Physics.Mass, 0.01) : Left!.Mass + Right!.Mass;

        public double Luminosity => IsStar ? Math.Max(Physics.Luminosity, 0) : Left!.Luminosity + Right!.Luminosity;

        public static Node Star(StarClass cls, StarPhysics physics) => new() { Class = cls, Physics = physics };

        public static Node Pair(Node left, Node right, double separationAu, double eccentricity)
        {
            var pair = new Node { Left = left, Right = right, SeparationAu = separationAu, Eccentricity = eccentricity };
            left.Parent = pair;
            right.Parent = pair;
            return pair;
        }

        // The heaviest star under this node.
        public Node Heaviest()
        {
            if (IsStar)
            {
                return this;
            }
            Node l = Left!.Heaviest();
            Node r = Right!.Heaviest();
            return r.Physics.Mass > l.Physics.Mass ? r : l;
        }
    }

    private sealed class Builder
    {
        public readonly StarSystem System;
        public Builder(StarSystem system) => System = system;

        public Body Add(Body body)
        {
            System.Bodies.Add(body);
            if (body.Parent >= 0)
            {
                System.Bodies[body.Parent].Children.Add(body.Index);
            }
            return body;
        }

        public int Next => System.Bodies.Count;
    }

    /// <summary>
    /// Generates the system of <paramref name="primary"/>. <paramref name="knownCompanions"/> are real companion
    /// stars from the catalogue (if any); procedural systems roll their own.
    /// </summary>
    public StarSystem Generate(GalaxyStar primary, string name, IReadOnlyList<GalaxyStar>? knownCompanions = null)
    {
        ulong key = Hash64.Of(_seed, Streams.System, primary.Address.Value);
        var layout = new Rng(Hash64.Combine(key, LayoutStream));
        var system = new StarSystem { Address = primary.Address, Name = name, Primary = primary };
        var b = new Builder(system);

        List<Companion> companions = knownCompanions != null
            ? CompanionsFromCatalogue(primary, knownCompanions, key)
            : RollCompanions(primary, key, ref layout);

        // The stellar tree, innermost companion first: (((A B) C) D), where a companion may itself be a close pair.
        Node a = Node.Star(primary.Class, primary.Physics);
        Node root = a;
        foreach (Companion c in companions)
        {
            Node unit = Node.Star(c.Class, c.Physics);
            if (c.HasTwin)
            {
                unit = Node.Pair(unit, Node.Star(c.TwinClass, c.TwinPhysics), c.TwinSeparationAu, c.TwinEccentricity);
            }
            root = Node.Pair(root, unit, c.SeparationAu, c.Eccentricity);
        }
        var stars = new List<Node>();
        var pairs = new List<Node>();
        Number(root, stars, pairs);
        bool multiple = stars.Count > 1;

        AddStellarBodies(b, root, -1, name, multiple, key);
        double metallicity = primary.Metallicity;
        foreach (Node host in stars)
        {
            GeneratePlanets(b, host, name, multiple, metallicity, key);
        }
        foreach (Node host in pairs)
        {
            GeneratePlanets(b, host, name, multiple, metallicity, key);
        }
        return system;
    }

    // Letters A, B, … for the stars in tree order (left first), node numbers 1–8 for stars and 9–15 for barycentres
    // (inner pairs first); a pair's letters are its stars' letters.
    private static void Number(Node root, List<Node> stars, List<Node> pairs)
    {
        Visit(root);

        void Visit(Node n)
        {
            if (n.IsStar)
            {
                n.Number = 1 + stars.Count;
                n.Letters = ((char)('A' + stars.Count)).ToString();
                stars.Add(n);
                return;
            }
            Visit(n.Left!);
            Visit(n.Right!);
            n.Number = BodyPath.FirstBarycentre + pairs.Count;
            n.Letters = n.Left!.Letters + n.Right!.Letters;
            pairs.Add(n);
        }
    }

    // Adds a node and everything under it, root first. The two members of a pair orbit the pair's barycentre on
    // opposite sides, each at its share of the separation (the lighter one farther out).
    private void AddStellarBodies(Builder b, Node node, int parentIndex, string name, bool multiple, ulong key)
    {
        Orbit orbit = default;
        if (node.Parent is Node pair)
        {
            var rng = new Rng(Hash64.Combine(key, PairStream + (ulong)pair.Number));
            double inclination = rng.Range(0, 0.6);
            double ascending = rng.Range(0, DMath.TwoPi);
            double periapsis = rng.Range(0, DMath.TwoPi);
            double meanAnomaly = rng.Range(0, DMath.TwoPi);
            double total = pair.Mass;
            double other = node == pair.Left ? pair.Right!.Mass : pair.Left!.Mass;
            double relative = pair.SeparationAu * Au;
            double period = Orbit.PeriodFor(relative, total * Units.SolarMass);
            double argument = node == pair.Left ? periapsis : periapsis + DMath.Pi;
            orbit = new Orbit(relative * other / total, pair.Eccentricity, inclination, ascending, argument, meanAnomaly, period);
        }

        node.BodyIndex = b.Next;
        if (node.IsStar)
        {
            var rng = new Rng(Hash64.Combine(key, node.Number == 1 ? PrimaryStream : StarStream + (ulong)node.Number));
            b.Add(new Body
            {
                Index = b.Next,
                Parent = parentIndex,
                Kind = BodyKind.Star,
                Name = multiple ? name + " " + node.Letters : name,
                Designation = node.Letters,
                Path = BodyPath.Node(node.Number),
                StarClass = node.Class,
                Mass = node.Mass * Units.SolarMass,
                Radius = Math.Max(node.Physics.Radius, 1e-6) * Units.SolarRadius,
                Temperature = node.Physics.Temperature,
                Luminosity = node.Physics.Luminosity,
                Orbit = orbit,
                RotationPeriod = rng.Range(5, 40) * Units.SecondsPerDay,
                AxialTilt = rng.Range(0, 0.3),
            });
            return;
        }
        b.Add(new Body
        {
            Index = b.Next,
            Parent = parentIndex,
            Kind = BodyKind.Barycentre,
            Name = name + " " + node.Letters,
            Designation = node.Letters,
            Path = BodyPath.Node(node.Number),
            Mass = node.Mass * Units.SolarMass,
            Luminosity = node.Luminosity,
            Orbit = orbit,
        });
        AddStellarBodies(b, node.Left!, node.BodyIndex, name, multiple, key);
        AddStellarBodies(b, node.Right!, node.BodyIndex, name, multiple, key);
    }

    // Separation floor: periapsis at least three times the sum of the two stars' radii.
    private static double MinimumSeparationAu(StarPhysics a, StarPhysics b, double eccentricity) =>
        3.0 * (Math.Max(a.Radius, 1e-6) + Math.Max(b.Radius, 1e-6)) * AuPerSolarRadius / (1.0 - eccentricity);

    private static List<Companion> CompanionsFromCatalogue(GalaxyStar primary, IReadOnlyList<GalaxyStar> known, ulong key)
    {
        var list = new List<Companion>(known.Count);
        for (int i = 0; i < known.Count; i++)
        {
            GalaxyStar c = known[i];
            var rng = new Rng(Hash64.Combine(key, CompanionStream + 0x80 + (ulong)i));
            double eccentricity = StellarTables.Clamp(rng.Rayleigh(0.3), 0, 0.8);
            double separation = Vector3D.Distance(c.Position, primary.Position) * AuPerLightYear;
            if (separation < 1)
            {
                separation = rng.LogNormal(40, 0.8);
            }
            separation = Math.Max(StellarTables.Clamp(separation, 0.5, 20000), MinimumSeparationAu(primary.Physics, c.Physics, eccentricity));
            list.Add(new Companion(c.Class, c.Physics, separation, eccentricity));
        }
        MakeHierarchical(list, dropTooWide: false);
        return list;
    }

    // Multiple stars are stable only as a hierarchy: sorts the companions outward and moves each one out until its
    // closest approach is at least three times the previous companion's farthest point. Rolled companions that
    // would end up swinging out beyond MaxCompanionApoapsisAu are dropped instead; real ones are always kept.
    private static void MakeHierarchical(List<Companion> list, bool dropTooWide)
    {
        list.Sort((x, y) => x.SeparationAu.CompareTo(y.SeparationAu));
        for (int i = 1; i < list.Count; i++)
        {
            Companion inner = list[i - 1];
            Companion c = list[i];
            double minimum = 3 * inner.SeparationAu * (1 + inner.Eccentricity) / (1 - c.Eccentricity);
            if (c.SeparationAu < minimum)
            {
                if (dropTooWide && minimum * (1 + c.Eccentricity) > MaxCompanionApoapsisAu)
                {
                    list.RemoveRange(i, list.Count - i);
                    return;
                }
                list[i] = c with { SeparationAu = minimum };
            }
        }
    }

    private static List<Companion> RollCompanions(GalaxyStar primary, ulong key, ref Rng layout)
    {
        var list = new List<Companion>();
        double mass = primary.Physics.Mass;
        StarKind kind = primary.Class.Kind;
        double chance = mass < 0.08 ? 0.15 : mass < 0.5 ? 0.27 : mass < 1.3 ? 0.45 : mass < 3 ? 0.55 : 0.75;
        if (kind is StarKind.BlackHole or StarKind.NeutronStar or StarKind.WolfRayet)
        {
            chance = 0.2;
        }
        if (!layout.Chance(chance))
        {
            return list;
        }
        int count = layout.Chance(0.15) ? 2 : 1;
        // Companions were born with the primary: the mass ratio applies to its initial mass, and the system's age
        // decides what each companion is now (GALAXY.md §4.1).
        double initialMass = StellarEvolution.InitialMass(kind, mass);
        // A giant companion would outshine anything but another giant.
        bool giantPrimary = kind is StarKind.Subgiant or StarKind.Giant or StarKind.BrightGiant or StarKind.Supergiant or StarKind.CarbonStar;
        for (int i = 0; i < count; i++)
        {
            var rng = new Rng(Hash64.Combine(key, CompanionStream + (ulong)i + 0x40));
            double companionMass = Math.Max(0.013, initialMass * rng.Range(0.1, 1.0));
            // Sometimes the companion was born the heavier star and has already died: a white dwarf (or neutron star)
            // fainter than the primary now, like Sirius B. If it would still shine, it would be the primary.
            double heavier = initialMass * rng.Range(1.0, 3.0);
            if (rng.Chance(0.1) && StellarEvolution.MainSequenceLifetime(heavier) * 1.08 < primary.Age)
            {
                companionMass = heavier;
            }
            (StarClass cls, StarPhysics phys) = EvolvedCompanion(companionMass, primary.Age, giantPrimary, ref rng);
            double eccentricity = StellarTables.Clamp(rng.Rayleigh(0.3), 0, 0.85);
            double separation = StellarTables.Clamp(rng.LogNormal(50 * DMath.Pow(Math.Max(mass, 0.05), 0.3), 1.5), 0.05, MaxCompanionSeparationAu);
            separation = Math.Max(separation, MinimumSeparationAu(primary.Physics, phys, eccentricity));
            var companion = new Companion(cls, phys, separation, eccentricity);

            // A wide companion is itself a close pair a quarter of the time (a hierarchical triple): its two stars
            // orbit each other well inside its orbit around the primary.
            if (separation > 50 && rng.Chance(0.25))
            {
                double twinMass = Math.Max(0.013, companionMass * rng.Range(0.2, 1.0));
                (StarClass twinClass, StarPhysics twinPhysics) = EvolvedCompanion(twinMass, primary.Age, giantPrimary, ref rng);
                double twinEccentricity = StellarTables.Clamp(rng.Rayleigh(0.15), 0, 0.5);
                double twinSeparation = Math.Max(rng.LogUniform(0.02, 0.05 * separation), MinimumSeparationAu(phys, twinPhysics, twinEccentricity));
                if (twinSeparation * (1 + twinEccentricity) <= separation * (1 - eccentricity) / 5)
                {
                    companion = companion with
                    {
                        TwinClass = twinClass,
                        TwinPhysics = twinPhysics,
                        TwinSeparationAu = twinSeparation,
                        TwinEccentricity = twinEccentricity,
                    };
                }
            }
            list.Add(companion);
        }
        MakeHierarchical(list, dropTooWide: true);
        return list;
    }

    // What a star born with this mass is at this age (Gyr): a brown dwarf cooling since birth, a main-sequence star, a
    // giant just past its main sequence, or the white dwarf, neutron star or black hole it left.
    private static (StarClass Class, StarPhysics Physics) EvolvedCompanion(double initialMass, double age, bool allowGiant, ref Rng rng)
    {
        if (initialMass < 0.075)
        {
            double teff = StellarEvolution.BrownDwarfTemperature(initialMass, age);
            double code = Math.Floor(StellarTables.MainSequenceCodeForTemperature(teff) * 2) / 2;
            (_, double mv, _, double r) = StellarTables.Typical(StarKind.MainSequence, code);
            return (new StarClass(StarKind.BrownDwarf, (float)code),
                new StarPhysics((float)teff, (float)mv, (float)initialMass, (float)r, (float)StellarTables.Luminosity(r, teff)));
        }
        double lifetime = StellarEvolution.MainSequenceLifetime(initialMass);
        if (age < lifetime)
        {
            double code = Math.Floor(CodeForMass(initialMass) * 2) / 2;
            (double teff, double mv, double m, double r) = StellarTables.Typical(StarKind.MainSequence, code);
            return (new StarClass(StarKind.MainSequence, (float)code),
                new StarPhysics((float)teff, (float)mv, (float)m, (float)r, (float)StellarTables.Luminosity(r, teff)));
        }
        if (initialMass < 8 && age < lifetime * 1.08 && allowGiant)
        {
            const double giantCode = 50; // K0 III: the red giant branch and clump
            (double teff, double mv, _, double r) = StellarTables.Typical(StarKind.Giant, giantCode);
            return (new StarClass(StarKind.Giant, (float)giantCode),
                new StarPhysics((float)teff, (float)mv, (float)initialMass, (float)r, (float)StellarTables.Luminosity(r, teff)));
        }
        if (initialMass < 8)
        {
            double wdMass = StellarEvolution.WhiteDwarfMass(initialMass);
            double teff = StellarEvolution.WhiteDwarfTemperature(age - lifetime, wdMass);
            double radius = 0.0126 * DMath.Pow(wdMass / 0.6, -1.0 / 3.0);
            return (new StarClass(StarKind.WhiteDwarf, (float)(Math.Floor(50400.0 / teff * 2) / 2)),
                new StarPhysics((float)teff, (float)StellarTables.WhiteDwarfMagnitude(teff), (float)wdMass,
                    (float)radius, (float)StellarTables.Luminosity(radius, teff)));
        }
        if (initialMass < 20)
        {
            double teff = StellarEvolution.NeutronStarTemperature(age - lifetime);
            double nsMass = StellarTables.Clamp(rng.Gaussian(1.4, 0.08), 1.1, 2.1);
            return (new StarClass(StarKind.NeutronStar, 0),
                new StarPhysics((float)teff, 30f, (float)nsMass, 1.725e-5f, (float)StellarTables.Luminosity(1.725e-5, teff)));
        }
        double bhMass = initialMass * rng.Range(0.2, 0.4);
        return (new StarClass(StarKind.BlackHole, 0), new StarPhysics(0f, 99f, (float)bhMass, (float)(4.24e-6 * bhMass), 0f));
    }

    // Main-sequence spectral code of a star of the given mass (solar masses).
    private static double CodeForMass(double mass)
    {
        for (double code = 3; code <= 92; code += 0.5)
        {
            if (StellarTables.Typical(StarKind.MainSequence, code).Mass <= mass)
            {
                return code;
            }
        }
        return 92;
    }

    private static double PlanetRate(StarClass star)
    {
        return star.Kind switch
        {
            StarKind.WhiteDwarf => 0.5,
            StarKind.BrownDwarf => 0.8,
            StarKind.NeutronStar => 0.2,
            StarKind.BlackHole => 0.1,
            StarKind.WolfRayet => 0.2,
            StarKind.Supergiant => 0.5,
            StarKind.Giant or StarKind.BrightGiant or StarKind.Subgiant => 1.5,
            StarKind.CarbonStar => 1.0,
            StarKind.TTauri => 3.0,
            StarKind.HerbigAeBe => 2.0,
            _ => star.Letter switch
            {
                'O' => 0.3,
                'B' => 1.0,
                'A' => 2.5,
                'F' => 4.0,
                'G' => 4.5,
                'K' => 3.5,
                'M' => 3.0,
                _ => 0.8,
            },
        };
    }

    // Stability limits for planets in binaries (Holman & Wiegert 1999), as multiples of the binary's separation.
    // S-type: a planet orbiting one star, perturbed by the other (mass fraction mu of the pair) stays within this.
    private static double StableAroundOne(double mu, double e) =>
        StellarTables.Clamp(0.464 - 0.380 * mu - 0.631 * e + 0.586 * mu * e + 0.150 * e * e - 0.198 * mu * e * e, 0.01, 0.5);

    // P-type: a planet orbiting both stars (mu = the lighter star's fraction of the pair) stays beyond this.
    private static double StableAroundBoth(double mu, double e) =>
        StellarTables.Clamp(1.60 + 5.10 * e - 2.22 * e * e + 4.12 * mu - 4.27 * e * mu - 5.09 * mu * mu + 4.61 * e * e * mu * mu, 1.5, 6.0);

    // Light reaching a host's planets from stars outside it (L☉ / AU²): each outer member at the orbit-averaged
    // distance of the pair that joins it to the host, ⟨1/r²⟩ = 1 / (a² √(1 − e²)).
    private static double OuterFlux(Node host)
    {
        double flux = 0;
        Node child = host;
        Node? parent = host.Parent;
        while (parent != null)
        {
            Node sibling = parent.Left == child ? parent.Right! : parent.Left!;
            double a = parent.SeparationAu;
            flux += sibling.Luminosity / (a * a * Math.Sqrt(1 - parent.Eccentricity * parent.Eccentricity));
            child = parent;
            parent = parent.Parent;
        }
        return flux;
    }

    // The planets, moons and belt of one host: a star (S-type) or a pair (P-type, circumbinary).
    private static void GeneratePlanets(Builder b, Node host, string name, bool multiple, double metallicity, ulong key)
    {
        var layout = new Rng(Hash64.Combine(key, HostStream + (ulong)host.Number));
        Node heaviest = host.Heaviest();
        double hostMass = host.Mass;
        double luminosity = Math.Max(host.Luminosity, 1e-7);
        double sqrtL = Math.Sqrt(luminosity);
        double metalFactor = DMath.Pow10(0.3 * metallicity);
        double giantFactor = DMath.Pow10(2.0 * metallicity);
        double massFactor = StellarTables.Clamp(hostMass, 0.3, 2.0);

        // The zone: outside the star (where equilibrium temperatures reach about 1,600 K, so close-in hot worlds
        // exist), or outside the pair's unstable region; out to 50 AU·√M, and inside the stable region around this
        // host left by everything that orbits outside it.
        double inner;
        if (host.IsStar)
        {
            inner = Math.Max(Math.Max(0.01, 0.03 * sqrtL), 3 * Math.Max(host.Physics.Radius, 1e-6) * AuPerSolarRadius);
            if (host.Class.Kind is StarKind.NeutronStar or StarKind.BlackHole)
            {
                inner = 0.1;
            }
        }
        else
        {
            double mu = Math.Min(host.Left!.Mass, host.Right!.Mass) / hostMass;
            inner = Math.Max(0.03 * sqrtL, host.SeparationAu * StableAroundBoth(mu, host.Eccentricity));
        }
        double outer = Math.Max(2.0, 50.0 * Math.Sqrt(hostMass));
        Node child = host;
        Node? parent = host.Parent;
        while (parent != null)
        {
            Node sibling = parent.Left == child ? parent.Right! : parent.Left!;
            double mu = sibling.Mass / (sibling.Mass + child.Mass);
            outer = Math.Min(outer, parent.SeparationAu * StableAroundOne(mu, parent.Eccentricity));
            child = parent;
            parent = parent.Parent;
        }
        if (outer <= inner * 1.2)
        {
            return;
        }

        var slots = new List<double>();
        double a = inner * layout.Range(1.0, 1.6);
        while (a < outer && slots.Count < MaxSlots)
        {
            slots.Add(a);
            a *= layout.Range(1.4, 2.4);
        }
        // Circumbinary planets are rarer than planets around single stars, and a close companion stunts the disc a
        // planet forms in: planets around one star of a pair get rarer as the pair's closest approach shrinks below
        // 50 AU and are gone below 3 AU (as surveys of real binaries find).
        double rate = PlanetRate(heaviest.Class) * (host.IsStar ? 1.0 : 0.5);
        if (host.Parent is Node pair)
        {
            double closest = pair.SeparationAu * (1 - pair.Eccentricity);
            rate *= DMath.SmoothStep(DMath.Log10(3), DMath.Log10(50), DMath.Log10(closest));
        }
        int count = (int)Math.Min(Math.Min(layout.Poisson(rate * metalFactor), slots.Count), MaxPlanets);

        // Choose which slots hold planets; slot indices (not planet numbers) identify bodies permanently. Slots are
        // equally likely beyond about ten-day orbits (0.1 AU·√L); closer in, real planets get rarer fast (occurrence
        // per log period falls roughly as P²), so those slots are weighted down by (a / 0.1 AU·√L)³. A weighted random
        // subset: each slot gets the key ln(u) / weight and the largest keys win.
        double hotEdge = 0.1 * sqrtL;
        var keys = new double[slots.Count];
        var order = new int[slots.Count];
        for (int i = 0; i < slots.Count; i++)
        {
            double ratio = Math.Min(1.0, slots[i] / hotEdge);
            double weight = Math.Max(ratio * ratio * ratio, 1e-6);
            keys[i] = DMath.Log(layout.NextDoubleNonZero()) / weight;
            order[i] = i;
        }
        Array.Sort(order, (x, y) => keys[x] != keys[y] ? keys[y].CompareTo(keys[x]) : x.CompareTo(y));
        var chosen = new List<int>(order[..count]);
        chosen.Sort();
        bool wantBelt = layout.Chance(0.3);
        double trailingBelt = layout.Range(1.5, 2.5);

        double snowLine = 2.7 * sqrtL;
        bool earthLikeHost = heaviest.Class.Kind == StarKind.MainSequence && heaviest.Class.Letter is 'F' or 'G' or 'K';
        bool mDwarf = heaviest.Class.Kind == StarKind.MainSequence && heaviest.Class.Letter == 'M';
        double hostMassKg = hostMass * Units.SolarMass;
        double outerFlux = OuterFlux(host);
        string prefix = multiple ? name + " " + host.Letters : name;
        int planetNumber = 0;
        bool beltPlaced = !wantBelt;
        double previousApoapsis = inner;
        var context = new HostContext(host.Number, host.BodyIndex, prefix, hostMassKg, luminosity, outerFlux, key);

        for (int k = 0; k < chosen.Count; k++)
        {
            int slot = chosen[k];
            double semiMajor = slots[slot];
            // Stay well inside the next body's orbit (or the edge of the zone).
            double apoapsisLimit = Math.Min(outer, k + 1 < chosen.Count ? slots[chosen[k + 1]] / 1.3 : outer);
            var rng = new Rng(Hash64.Combine(key, SlotStream + (ulong)host.Number * 256 + (ulong)slot));
            double teq = context.EquilibriumTemperature(semiMajor);

            // A belt takes the place of one planet, preferably near the snow line, if there's room for it.
            if (!beltPlaced && semiMajor > 0.5 * snowLine && semiMajor > 1.2 * previousApoapsis)
            {
                beltPlaced = true;
                previousApoapsis = AddBelt(b, context, slot, semiMajor, 1.05 * previousApoapsis, apoapsisLimit, ref rng);
                continue;
            }

            planetNumber++;
            PlanetType type = ChoosePlanetType(teq, semiMajor, snowLine, giantFactor, massFactor, earthLikeHost, mDwarf, ref rng);
            previousApoapsis = AddPlanet(b, context, planetNumber, slot, type, semiMajor, teq, previousApoapsis, apoapsisLimit, ref rng);
        }
        if (!beltPlaced && chosen.Count > 0)
        {
            double beltA = Math.Min(slots[chosen[^1]] * trailingBelt, outer * 0.9);
            if (beltA > previousApoapsis * 1.3)
            {
                var rng = new Rng(Hash64.Combine(key, SlotStream + (ulong)host.Number * 256 + MaxSlots));
                AddBelt(b, context, MaxSlots, beltA, 1.05 * previousApoapsis, outer, ref rng);
            }
        }
    }

    /// <summary>What a host's bodies need to know about it.</summary>
    private sealed record HostContext(int NodeNumber, int BodyIndex, string Prefix, double MassKg, double Luminosity, double OuterFlux, ulong Key)
    {
        // Zero-albedo equilibrium temperature at a distance (AU) from the host: its own light plus its companions'.
        public double EquilibriumTemperature(double au) =>
            278.6 * Math.Sqrt(Math.Sqrt(Luminosity / (au * au) + OuterFlux));
    }

    private static PlanetType ChoosePlanetType(double teq, double a, double snowLine, double giantFactor,
        double massFactor, bool earthLikeHost, bool mDwarf, ref Rng rng)
    {
        if (teq > 1000)
        {
            if (rng.Chance(StellarTables.Clamp(0.05 * giantFactor, 0, 0.3)))
            {
                return PlanetType.GasGiant;
            }
            return teq > 1300 || rng.Chance(0.4) ? PlanetType.MetalRich : PlanetType.Rocky;
        }
        if (a < snowLine)
        {
            if (rng.Chance(StellarTables.Clamp(0.08 * giantFactor * massFactor, 0, 0.5)))
            {
                return PlanetType.GasGiant;
            }
            if (teq > 230 && teq < 310)
            {
                double earthChance = earthLikeHost ? 0.04 : mDwarf ? 0.006 : 0.0;
                if (rng.Chance(earthChance))
                {
                    return PlanetType.EarthLike;
                }
            }
            if (teq > 200 && teq < 400 && rng.Chance(0.08))
            {
                return PlanetType.WaterWorld;
            }
            return teq > 150 ? PlanetType.Rocky : PlanetType.RockyIce;
        }
        double roll = rng.NextDouble();
        double pGas = StellarTables.Clamp(0.3 * giantFactor * massFactor, 0.05, 0.7);
        if (roll < pGas)
        {
            return PlanetType.GasGiant;
        }
        roll -= pGas;
        if (roll < 0.25)
        {
            return PlanetType.IceGiant;
        }
        roll -= 0.25;
        if (roll < 0.03)
        {
            return PlanetType.AmmoniaWorld;
        }
        return rng.Chance(0.5) ? PlanetType.Icy : PlanetType.RockyIce;
    }

    private static double MassForType(PlanetType type, ref Rng rng) => type switch
    {
        PlanetType.EarthLike => rng.LogUniform(0.5, 3),
        PlanetType.WaterWorld => rng.LogUniform(0.8, 8),
        PlanetType.Icy => rng.LogUniform(0.01, 4),
        PlanetType.AmmoniaWorld => rng.LogUniform(0.5, 6),
        PlanetType.IceGiant => rng.LogUniform(8, 40),
        PlanetType.GasGiant => rng.LogUniform(40, 4000),
        _ => rng.LogUniform(0.05, 8),
    };

    // Radius in Earth radii for a mass in Earth masses.
    private static double RadiusFor(PlanetType type, double massEarth, double teq)
    {
        switch (type)
        {
            case PlanetType.GasGiant:
            {
                double r = massEarth >= 95 ? 11.2 * DMath.Pow(massEarth / 318.0, -0.04) : 9.45 * DMath.Pow(massEarth / 95.0, 0.5);
                return teq > 1000 ? r * 1.2 : r;
            }
            case PlanetType.IceGiant:
                return 3.9 * DMath.Pow(massEarth / 17.0, 0.3);
            case PlanetType.Icy:
            case PlanetType.RockyIce:
            case PlanetType.WaterWorld:
            case PlanetType.AmmoniaWorld:
                return 1.15 * DMath.Pow(massEarth, 0.28);
            case PlanetType.MetalRich:
                return 0.85 * DMath.Pow(massEarth, 0.28);
            default:
                return DMath.Pow(massEarth, 0.28);
        }
    }

    private static double Greenhouse(AtmosphereType atmosphere, double pressure) => atmosphere switch
    {
        AtmosphereType.Thin => 1.05,
        AtmosphereType.Thick => 1.0 + 0.5 * Math.Min(1.0, pressure / 10.0),
        AtmosphereType.Breathable => 1.06,
        _ => 1.0,
    };

    // Adds a planet and its moons; returns the planet's apoapsis in AU.
    private static double AddPlanet(Builder b, HostContext host, int number, int slot, PlanetType type, double semiMajorAu,
        double teq, double previousApoapsisAu, double apoapsisLimitAu, ref Rng rng)
    {
        double massEarth = MassForType(type, ref rng);
        double radiusEarth = RadiusFor(type, massEarth, teq);
        double mass = massEarth * Units.EarthMass;
        double radius = radiusEarth * Units.EarthRadius;
        bool giant = type is PlanetType.GasGiant or PlanetType.IceGiant;

        AtmosphereType atmosphere;
        double pressure;
        switch (type)
        {
            case PlanetType.GasGiant:
            case PlanetType.IceGiant:
                atmosphere = AtmosphereType.HydrogenHelium;
                pressure = 1000;
                break;
            case PlanetType.EarthLike:
                atmosphere = AtmosphereType.Breathable;
                pressure = rng.Range(0.5, 2.0);
                break;
            case PlanetType.WaterWorld:
            case PlanetType.AmmoniaWorld:
                atmosphere = AtmosphereType.Thick;
                pressure = rng.Range(1, 10);
                break;
            default:
            {
                double roll = rng.NextDouble();
                if (massEarth < 0.2 || roll < 0.5)
                {
                    atmosphere = AtmosphereType.None;
                    pressure = 0;
                }
                else if (roll < 0.8)
                {
                    atmosphere = AtmosphereType.Thin;
                    pressure = rng.LogUniform(0.001, 0.5);
                }
                else
                {
                    atmosphere = AtmosphereType.Thick;
                    pressure = rng.LogUniform(1, 90);
                }
                break;
            }
        }

        // Orbits never cross: periapsis stays beyond the previous body's apoapsis, apoapsis inside the next orbit.
        double eccentricityLimit = Math.Min(Math.Min(0.5, 1 - 1.15 * previousApoapsisAu / semiMajorAu), apoapsisLimitAu / semiMajorAu - 1);
        double eccentricity = StellarTables.Clamp(rng.Rayleigh(giant ? 0.08 : 0.05), 0, Math.Max(0, eccentricityLimit));
        double a = semiMajorAu * Au;
        var orbit = new Orbit(a, eccentricity, StellarTables.Clamp(rng.Rayleigh(2 * DMath.DegToRad), 0, 15 * DMath.DegToRad),
            rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi),
            Orbit.PeriodFor(a, host.MassKg + mass));

        double lockDistance = 0.1 * DMath.Pow(host.MassKg / Units.SolarMass, 1.0 / 3.0);
        bool locked = !giant && semiMajorAu < lockDistance;
        double rotation = giant
            ? rng.Range(8, 20) * 3600
            : locked ? orbit.Period : rng.LogUniform(10 * 3600, 100 * Units.SecondsPerDay);
        if (!locked && rng.Chance(0.1))
        {
            rotation = -rotation;
        }

        double ringInner = 0;
        double ringOuter = 0;
        double ringChance = type == PlanetType.GasGiant ? 0.4 : type == PlanetType.IceGiant ? 0.3 : 0.02;
        if (rng.Chance(ringChance))
        {
            ringInner = radius * rng.Range(1.2, 1.6);
            ringOuter = ringInner * rng.Range(1.3, 2.2);
        }

        uint path = BodyPath.Planet(host.NodeNumber, slot);
        string designation = number.ToString(CultureInfo.InvariantCulture);
        Body planet = b.Add(new Body
        {
            Index = b.Next,
            Parent = host.BodyIndex,
            Kind = BodyKind.Planet,
            Name = host.Prefix + " " + designation,
            Designation = designation,
            Path = path,
            PlanetType = type,
            Mass = mass,
            Radius = radius,
            Temperature = teq * Greenhouse(atmosphere, pressure),
            Atmosphere = atmosphere,
            SurfacePressure = pressure,
            Orbit = orbit,
            RotationPeriod = rotation,
            AxialTilt = StellarTables.Clamp(Math.Abs(rng.Gaussian(0, 25 * DMath.DegToRad)), 0, DMath.Pi),
            TidallyLocked = locked,
            RingInnerRadius = ringInner,
            RingOuterRadius = ringOuter,
            Landable = !giant,
        });

        double moonRate = type switch
        {
            PlanetType.GasGiant => 4.0,
            PlanetType.IceGiant => 2.5,
            _ => massEarth > 0.5 ? 0.4 : 0.0,
        };
        int moons = (int)Math.Min(rng.Poisson(moonRate), 12);
        double hill = orbit.Periapsis * DMath.Pow(mass / (3 * host.MassKg), 1.0 / 3.0);
        double moonLimit = 0.4 * hill;
        double moonA = Math.Max(2.5 * radius, ringOuter * 1.15) * rng.Range(1.0, 1.5);
        for (int m = 0; m < moons && moonA < moonLimit; m++)
        {
            var moonRng = new Rng(Hash64.Combine(host.Key, MoonStream + ((ulong)host.NodeNumber * 256 + (ulong)slot) * 256 + (ulong)m));
            AddMoon(b, planet, designation, path, m, moonA, teq, mass, massEarth, ref moonRng);
            moonA *= rng.Range(1.3, 2.2);
        }
        return semiMajorAu * (1 + eccentricity);
    }

    private static void AddMoon(Builder b, Body planet, string designation, uint planetPath, int m, double moonA, double teq,
        double planetMass, double planetMassEarth, ref Rng rng)
    {
        bool icy = teq < 170;
        double maxMoonMass = Math.Min(0.03, 3e-4 * planetMassEarth);
        double moonMassEarth = rng.LogUniform(1e-5, Math.Max(2e-5, maxMoonMass));
        PlanetType moonType = icy ? (rng.Chance(0.6) ? PlanetType.Icy : PlanetType.RockyIce) : PlanetType.Rocky;
        double moonRadius = RadiusFor(moonType, moonMassEarth, teq) * Units.EarthRadius;
        double moonMass = moonMassEarth * Units.EarthMass;
        bool retrograde = rng.Chance(0.1);
        double inclination = StellarTables.Clamp(rng.Rayleigh(1 * DMath.DegToRad), 0, 20 * DMath.DegToRad);
        var moonOrbit = new Orbit(moonA, StellarTables.Clamp(rng.Rayleigh(0.01), 0, 0.2),
            retrograde ? DMath.Pi - inclination : inclination,
            rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi),
            Orbit.PeriodFor(moonA, planetMass + moonMass));

        // Big moons can hold atmospheres too: cold ones keep thick nitrogen and methane (like Titan), others at most a
        // thin veil from outgassing.
        AtmosphereType atmosphere = AtmosphereType.None;
        double pressure = 0;
        if (moonMassEarth > 0.005)
        {
            double roll = rng.NextDouble();
            if (teq < 130 && roll < 0.35)
            {
                atmosphere = AtmosphereType.Thick;
                pressure = rng.LogUniform(0.5, 5);
            }
            else if (roll < 0.55)
            {
                atmosphere = AtmosphereType.Thin;
                pressure = rng.LogUniform(1e-5, 0.01);
            }
        }

        string letter = ((char)('a' + m)).ToString();
        b.Add(new Body
        {
            Index = b.Next,
            Parent = planet.Index,
            Kind = BodyKind.Moon,
            Name = planet.Name + " " + letter,
            Designation = designation + " " + letter,
            Path = BodyPath.Moon(planetPath, m),
            PlanetType = moonType,
            Mass = moonMass,
            Radius = moonRadius,
            Temperature = teq * Greenhouse(atmosphere, pressure),
            Atmosphere = atmosphere,
            SurfacePressure = pressure,
            Orbit = moonOrbit,
            RotationPeriod = moonOrbit.Period,
            TidallyLocked = true,
            Landable = true,
        });
    }

    // Adds a belt between innerLimitAu and outerLimitAu; returns its outer edge in AU.
    private static double AddBelt(Builder b, HostContext host, int slot, double semiMajorAu,
        double innerLimitAu, double outerLimitAu, ref Rng rng)
    {
        double widthAu = semiMajorAu * rng.Range(0.2, 0.4);
        widthAu = Math.Max(0, Math.Min(widthAu, 2 * Math.Min(semiMajorAu - innerLimitAu, outerLimitAu - semiMajorAu)));
        double a = semiMajorAu * Au;
        b.Add(new Body
        {
            Index = b.Next,
            Parent = host.BodyIndex,
            Kind = BodyKind.Belt,
            Name = host.Prefix + " Belt",
            Designation = "Belt",
            Path = BodyPath.Belt(host.NodeNumber, slot),
            PlanetType = PlanetType.AsteroidBelt,
            Orbit = new Orbit(a, 0, 0, 0, 0, 0, Orbit.PeriodFor(a, host.MassKg)),
            BeltWidth = widthAu * Au,
        });
        return semiMajorAu + widthAu / 2;
    }
}
