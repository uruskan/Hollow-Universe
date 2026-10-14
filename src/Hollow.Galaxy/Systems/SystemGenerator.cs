using System;
using System.Collections.Generic;
using System.Globalization;
using Hollow.Core;
using Hollow.Galaxy.Authored;

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
    private const ulong WorldStream = 0x3000000;

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

        /// <summary>Light when its stars were young (on the main sequence), when the planets formed.</summary>
        public double ZamsLuminosity => IsStar
            ? StellarEvolution.MainSequenceLuminosity(StellarEvolution.InitialMass(Class.Kind, Math.Max(Physics.Mass, 0.01)))
            : Left!.ZamsLuminosity + Right!.ZamsLuminosity;

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
    public StarSystem Generate(GalaxyStar primary, string name, IReadOnlyList<GalaxyStar>? knownCompanions = null) =>
        Generate(primary, name, knownCompanions, null);

    /// <summary>
    /// As above, with a real system's authored content (GALAXY.md §15): names for its stars and barycentres, and the
    /// known planets of its hosts in place of generated ones.
    /// </summary>
    internal StarSystem Generate(GalaxyStar primary, string name, IReadOnlyList<GalaxyStar>? knownCompanions, AuthoredSystem? authored)
    {
        ulong key = Hash64.Of(_seed, Streams.System, primary.Address.Value);
        var layout = new Rng(Hash64.Combine(key, LayoutStream));
        var system = new StarSystem { Address = primary.Address, Name = name, Primary = primary };
        var b = new Builder(system);

        List<Companion> companions = knownCompanions == null
            ? RollCompanions(primary, key, ref layout)
            : knownCompanions.Count > 0
                ? CompanionsFromCatalogue(primary, knownCompanions, key)
                : UnseenCompanions(primary, key, ref layout);

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

        AddStellarBodies(b, root, -1, name, multiple, key, authored);
        double metallicity = primary.Metallicity;
        foreach (Node host in stars)
        {
            GeneratePlanets(b, host, name, multiple, metallicity, primary.Age, key, authored?.HostFor(host.Number));
        }
        foreach (Node host in pairs)
        {
            GeneratePlanets(b, host, name, multiple, metallicity, primary.Age, key, authored?.HostFor(host.Number));
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
    private void AddStellarBodies(Builder b, Node node, int parentIndex, string name, bool multiple, ulong key, AuthoredSystem? authored)
    {
        string? authoredName = authored != null && authored.StarNames.TryGetValue(node.Number, out string? real) ? real : null;
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
                Name = authoredName ?? (multiple ? name + " " + node.Letters : name),
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
            Name = authoredName ?? name + " " + node.Letters,
            Designation = node.Letters,
            Path = BodyPath.Node(node.Number),
            Mass = node.Mass * Units.SolarMass,
            Luminosity = node.Luminosity,
            Orbit = orbit,
        });
        AddStellarBodies(b, node.Left!, node.BodyIndex, name, multiple, key, authored);
        AddStellarBodies(b, node.Right!, node.BodyIndex, name, multiple, key, authored);
    }

    // Separation floor: periapsis at least three times the sum of the two stars' radii.
    private static double MinimumSeparationAu(StarPhysics a, StarPhysics b, double eccentricity) =>
        3.0 * (Math.Max(a.Radius, 1e-6) + Math.Max(b.Radius, 1e-6)) * AuPerSolarRadius / (1.0 - eccentricity);

    // Real companions from the catalogue (GALAXY.md §10.2). A companion's separation is its angle from the primary on
    // the sky times the primary's distance: catalogue distances of the stars of one system often differ by far more
    // than the stars are apart (parallax errors put Procyon B 3,458 AU from Procyon instead of 15). A companion at the
    // primary's very position has an unknown separation, which is rolled. Two companions much closer to each other than
    // to the primary orbit each other as a close pair (Epsilon Indi Ba and Bb, Gliese 229 Ba and Bb).
    private static List<Companion> CompanionsFromCatalogue(GalaxyStar primary, IReadOnlyList<GalaxyStar> known, ulong key)
    {
        int n = known.Count;
        var separations = new double[n];
        var eccentricities = new double[n];
        for (int i = 0; i < n; i++)
        {
            var rng = new Rng(Hash64.Combine(key, CompanionStream + 0x80 + (ulong)i));
            eccentricities[i] = StellarTables.Clamp(rng.Rayleigh(0.3), 0, 0.8);
            double separation = Projected(primary.Position, known[i].Position, primary.Position.Length);
            separations[i] = separation < 1 ? rng.LogNormal(40, 0.8) : separation;
        }
        var partner = new int[n];
        Array.Fill(partner, -1);
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n && partner[i] < 0; j++)
            {
                double apart = Projected(known[i].Position, known[j].Position, primary.Position.Length);
                if (partner[j] < 0 && apart > 0 && apart < 0.2 * Math.Min(separations[i], separations[j]))
                {
                    partner[i] = j;
                    partner[j] = i;
                }
            }
        }

        var list = new List<Companion>(n);
        for (int i = 0; i < n; i++)
        {
            if (partner[i] >= 0 && partner[i] < i)
            {
                continue; // added with its partner
            }
            GalaxyStar c = known[i];
            double eccentricity = eccentricities[i];
            double separation = Math.Max(StellarTables.Clamp(separations[i], 0.5, 20000), MinimumSeparationAu(primary.Physics, c.Physics, eccentricity));
            var companion = new Companion(c.Class, c.Physics, separation, eccentricity);
            if (partner[i] >= 0)
            {
                GalaxyStar twin = known[partner[i]];
                var rng = new Rng(Hash64.Combine(key, PairStream + 0x80 + (ulong)i));
                double twinEccentricity = StellarTables.Clamp(rng.Rayleigh(0.15), 0, 0.5);
                double twinSeparation = Math.Max(Projected(c.Position, twin.Position, primary.Position.Length),
                    MinimumSeparationAu(c.Physics, twin.Physics, twinEccentricity));
                // The pair's own orbit stays well inside its closest approach to the primary.
                eccentricity = Math.Min(eccentricity, Math.Max(0, 1 - 5 * twinSeparation * (1 + twinEccentricity) / separation));
                companion = new Companion(c.Class, c.Physics, separation, eccentricity)
                {
                    TwinClass = twin.Class,
                    TwinPhysics = twin.Physics,
                    TwinSeparationAu = twinSeparation,
                    TwinEccentricity = twinEccentricity,
                };
            }
            list.Add(companion);
        }
        MakeHierarchical(list, dropTooWide: false);
        return list;
    }

    // Separation (AU) of two stars as seen from Sol at a distance (ly): the chord between their directions, which is
    // their angle on the sky to within 0.03% for anything closer than 0.1 rad.
    private static double Projected(Vector3D a, Vector3D b, double distanceLy)
    {
        if (a.Length < 1e-12 || b.Length < 1e-12)
        {
            return Vector3D.Distance(a, b) * AuPerLightYear;
        }
        return Vector3D.Distance(a.Normalized, b.Normalized) * distanceLy * AuPerLightYear;
    }

    // Companions the catalogue can't have seen (GALAXY.md §10.2): a catalogue star listed alone beyond 25 ly rolls
    // companions like a generated star and keeps those too faint for the catalogue at its distance (where it holds
    // under half of their brightness level) or too close to resolve (under 1″ apart). Within 25 ly the neighbourhood's
    // multiple stars are all known.
    private static List<Companion> UnseenCompanions(GalaxyStar primary, ulong key, ref Rng layout)
    {
        double distanceLy = primary.Position.Length;
        var kept = new List<Companion>();
        if (distanceLy < UnseenCompanionsBeyondLy)
        {
            return kept;
        }
        double resolvedAu = distanceLy / Units.LightYearsPerParsec; // 1″ at this distance
        foreach (Companion c in RollCompanions(primary, key, ref layout))
        {
            float brightest = c.HasTwin ? Math.Min(c.Physics.AbsoluteMagnitude, c.TwinPhysics.AbsoluteMagnitude) : c.Physics.AbsoluteMagnitude;
            if (c.SeparationAu < resolvedAu || CatalogueCompleteness.Of(StarClassTable.LevelForMagnitude(brightest), distanceLy) < 0.5)
            {
                kept.Add(c);
            }
        }
        return kept;
    }

    /// <summary>Catalogue stars listed alone get rolled, unseen companions only beyond this distance from Sol (ly).</summary>
    internal const double UnseenCompanionsBeyondLy = 25;

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

    // The planets, moons and belt of one host: a star (S-type) or a pair (P-type, circumbinary). An authored host
    // (GALAXY.md §15) has exactly its known planets, in their slots 0, 1, 2, … and nothing invented around them.
    private static void GeneratePlanets(Builder b, Node host, string name, bool multiple, double metallicity, double age, ulong key,
        AuthoredHost? authored)
    {
        var layout = new Rng(Hash64.Combine(key, HostStream + (ulong)host.Number));
        Node heaviest = host.Heaviest();
        double hostMass = host.Mass;
        double luminosity = Math.Max(host.Luminosity, 1e-7);
        double sqrtL = Math.Sqrt(luminosity);
        double metalFactor = DMath.Pow10(0.3 * metallicity);
        double giantFactor = DMath.Pow10(2.0 * metallicity);
        double massFactor = StellarTables.Clamp(hostMass, 0.3, 2.0);
        if (authored != null)
        {
            var real = new HostContext(host.Number, host.BodyIndex, authored.Prefix, hostMass * Units.SolarMass, luminosity, OuterFlux(host), key)
            {
                FormationSnowLine = 2.7 * Math.Sqrt(Math.Max(host.ZamsLuminosity, 1e-7)),
                Activity = Activity(heaviest.Class, age),
                StableHost = heaviest.Class.Kind is StarKind.MainSequence or StarKind.Subgiant && heaviest.Class.Letter is 'F' or 'G' or 'K' or 'M',
                Age = age,
                MetalFactor = metalFactor,
            };
            double previous = 0;
            for (int i = 0; i < authored.Planets.Length; i++)
            {
                AuthoredPlanet planet = authored.Planets[i];
                var rng = new Rng(Hash64.Combine(key, SlotStream + (ulong)host.Number * 256 + (ulong)i));
                previous = AddPlanet(b, real, i + 1, i, planet.Formation, planet.SemiMajorAu, previous, double.MaxValue, ref rng, planet);
            }
            return;
        }

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

        // Planets formed when the star was young: for an evolved host, the snow line of its main-sequence days.
        double formationSnowLine = 2.7 * Math.Sqrt(Math.Max(host.ZamsLuminosity, 1e-7));
        double snowLine = 2.7 * sqrtL;
        double hostMassKg = hostMass * Units.SolarMass;
        double outerFlux = OuterFlux(host);
        string prefix = multiple ? name + " " + host.Letters : name;
        int planetNumber = 0;
        bool beltPlaced = !wantBelt;
        double previousApoapsis = inner;
        var context = new HostContext(host.Number, host.BodyIndex, prefix, hostMassKg, luminosity, outerFlux, key)
        {
            FormationSnowLine = formationSnowLine,
            Activity = Activity(heaviest.Class, age),
            StableHost = heaviest.Class.Kind is StarKind.MainSequence or StarKind.Subgiant && heaviest.Class.Letter is 'F' or 'G' or 'K' or 'M',
            Age = age,
            MetalFactor = metalFactor,
        };

        for (int k = 0; k < chosen.Count; k++)
        {
            int slot = chosen[k];
            double semiMajor = slots[slot];
            // Stay well inside the next body's orbit (or the edge of the zone).
            double apoapsisLimit = Math.Min(outer, k + 1 < chosen.Count ? slots[chosen[k + 1]] / 1.3 : outer);
            var rng = new Rng(Hash64.Combine(key, SlotStream + (ulong)host.Number * 256 + (ulong)slot));

            // A belt takes the place of one planet, preferably near the snow line, if there's room for it.
            if (!beltPlaced && semiMajor > 0.5 * snowLine && semiMajor > 1.2 * previousApoapsis)
            {
                beltPlaced = true;
                previousApoapsis = AddBelt(b, context, slot, semiMajor, 1.05 * previousApoapsis, apoapsisLimit, ref rng);
                continue;
            }

            planetNumber++;
            Formation formation = ChooseFormation(context.EquilibriumTemperature(semiMajor), semiMajor, formationSnowLine, giantFactor, massFactor, ref rng);
            previousApoapsis = AddPlanet(b, context, planetNumber, slot, formation, semiMajor, previousApoapsis, apoapsisLimit, ref rng);
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
        public double FormationSnowLine { get; init; }
        public double Activity { get; init; }
        public bool StableHost { get; init; }
        public double Age { get; init; }
        public double MetalFactor { get; init; }

        /// <summary>Starlight at a distance (AU) from the host, L☉/AU²: its own light plus its companions'.</summary>
        public double FluxAt(double au) => Luminosity / (au * au) + OuterFlux;

        /// <summary>Zero-albedo equilibrium temperature at a distance (AU) from the host.</summary>
        public double EquilibriumTemperature(double au) => 278.6 * Math.Sqrt(Math.Sqrt(FluxAt(au)));
    }

    /// <summary>
    /// High-energy output (ultraviolet, X-rays, flares and wind) of a star relative to the Sun's, which strips
    /// atmospheres (GALAXY.md §10.3): red dwarfs flare for billions of years, young stars are wild, white dwarfs quiet.
    /// </summary>
    private static double Activity(StarClass star, double age)
    {
        double type = star.Kind switch
        {
            StarKind.WhiteDwarf => 0.3,
            StarKind.NeutronStar => 5,
            StarKind.BlackHole => 0.5,
            StarKind.BrownDwarf => 1,
            StarKind.TTauri => 30,
            StarKind.HerbigAeBe or StarKind.WolfRayet => 10,
            StarKind.Supergiant => 2,
            StarKind.MainSequence => star.Letter switch
            {
                'O' or 'B' or 'A' => 1.5,
                'F' => 0.8,
                'G' => 1.0,
                'K' => 2.0,
                'M' => star.Code < 64.5 ? 4 : 16, // late red dwarfs stay saturated for billions of years
                _ => 1,
            },
            _ => 1,
        };
        return type * (1 + 0.3 / Math.Max(age, 0.05));
    }

    // How a planet in this slot formed: gas giants are likelier beyond the snow line of the young star and around
    // metal-rich and heavier stars; ice giants form far out; everything else is a solid body.
    private static Formation ChooseFormation(double teq, double a, double snowLine, double giantFactor, double massFactor, ref Rng rng)
    {
        if (teq > 1000)
        {
            return rng.Chance(StellarTables.Clamp(0.05 * giantFactor, 0, 0.3)) ? Formation.GasGiant : Formation.Solid;
        }
        if (a < snowLine)
        {
            return rng.Chance(StellarTables.Clamp(0.08 * giantFactor * massFactor, 0, 0.5)) ? Formation.GasGiant : Formation.Solid;
        }
        double roll = rng.NextDouble();
        double pGas = StellarTables.Clamp(0.3 * giantFactor * massFactor, 0.05, 0.7);
        if (roll < pGas)
        {
            return Formation.GasGiant;
        }
        return roll - pGas < 0.25 ? Formation.IceGiant : Formation.Solid;
    }

    // Adds a planet and its moons; returns the planet's apoapsis in AU.
    private static double AddPlanet(Builder b, HostContext host, int number, int slot, Formation formation, double semiMajorAu,
        double previousApoapsisAu, double apoapsisLimitAu, ref Rng rng, AuthoredPlanet? authored = null)
    {
        bool giant = formation != Formation.Solid;
        double massEarth = formation switch
        {
            Formation.GasGiant => rng.LogUniform(40, 4000),
            Formation.IceGiant => rng.LogUniform(8, 40),
            _ => rng.LogUniform(0.02, 10),
        };
        if (authored != null)
        {
            massEarth = authored.MassEarth;
        }
        double mass = massEarth * Units.EarthMass;

        // Orbits never cross: periapsis stays beyond the previous body's apoapsis, apoapsis inside the next orbit.
        double eccentricityLimit = Math.Min(Math.Min(0.5, 1 - 1.15 * previousApoapsisAu / semiMajorAu), apoapsisLimitAu / semiMajorAu - 1);
        double eccentricity = StellarTables.Clamp(rng.Rayleigh(giant ? 0.08 : 0.05), 0, Math.Max(0, eccentricityLimit));
        // Close in, tides lock a solid planet's spin and circularise its orbit down to what its neighbours force.
        double lockDistance = 0.1 * DMath.Pow(host.MassKg / Units.SolarMass, 1.0 / 3.0);
        bool locked = !giant && semiMajorAu < lockDistance;
        if (locked)
        {
            eccentricity *= 0.2;
        }
        if (authored != null)
        {
            eccentricity = authored.Eccentricity;
        }
        double a = semiMajorAu * Au;
        var orbit = new Orbit(a, eccentricity, StellarTables.Clamp(rng.Rayleigh(2 * DMath.DegToRad), 0, 15 * DMath.DegToRad),
            rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi),
            Orbit.PeriodFor(a, host.MassKg + mass));

        double rotation = giant
            ? rng.Range(8, 20) * 3600
            : locked ? orbit.Period : rng.LogUniform(10 * 3600, 100 * Units.SecondsPerDay);
        if (!locked && rng.Chance(0.1))
        {
            rotation = -rotation;
        }
        double tilt = StellarTables.Clamp(Math.Abs(rng.Gaussian(0, 25 * DMath.DegToRad)), 0, DMath.Pi);
        if (locked)
        {
            tilt *= 0.02; // tides also right a locked planet's spin axis
        }

        // Where it formed: a giant inside the snow line migrated in from beyond it; solid planets formed near where
        // they are, give or take the mixing of a young disc.
        double formedAu = semiMajorAu * rng.LogUniform(0.7, 1.4);
        if (giant && formedAu < host.FormationSnowLine)
        {
            formedAu = host.FormationSnowLine * rng.Range(1.0, 3.0);
        }
        var worldRng = new Rng(Hash64.Combine(host.Key, WorldStream + (ulong)host.NodeNumber * 256 + (ulong)slot));
        World world = WorldBuilder.Build(new WorldInput
        {
            MassEarth = massEarth,
            Formation = formation,
            FormationZone = formedAu / host.FormationSnowLine,
            Flux = host.FluxAt(semiMajorAu),
            Activity = host.Activity,
            Age = host.Age,
            HostMass = host.MassKg,
            OrbitRadius = a,
            OrbitPeriod = orbit.Period,
            Eccentricity = eccentricity,
            RotationPeriod = rotation,
            TidallyLocked = locked,
            StableHost = host.StableHost,
        }, ref worldRng);
        double radius = world.Radius;

        double ringInner = 0;
        double ringOuter = 0;
        double ringMass = 0;
        Material ringMaterial = Material.None;
        double ringChance = world.Type switch
        {
            PlanetType.GasGiant => 0.4,
            PlanetType.IceGiant => 0.3,
            PlanetType.GasDwarf => 0.05,
            _ => 0.02,
        };
        // A real planet has no rings unless they have been seen (none have).
        if (rng.Chance(ringChance) && authored == null)
        {
            ringInner = radius * rng.Range(1.2, 1.6);
            ringOuter = ringInner * rng.Range(1.3, 2.2);
            // Ice survives in rings only where it's cold; closer in they are dust and rock, near hot stars metal.
            double teq = world.State.EquilibriumTemperature;
            ringMaterial = teq < 170 ? Material.Icy : teq < 700 ? Material.Rocky : Material.Metallic;
            ringMass = mass * rng.LogUniform(1e-9, 1e-6);
        }

        uint path = BodyPath.Planet(host.NodeNumber, slot);
        string designation = authored?.Designation ?? number.ToString(CultureInfo.InvariantCulture);
        Body planet = b.Add(new Body
        {
            Index = b.Next,
            Parent = host.BodyIndex,
            Kind = BodyKind.Planet,
            Name = host.Prefix + " " + designation,
            Designation = designation,
            Path = path,
            PlanetType = world.Type,
            Mass = mass,
            Radius = radius,
            Temperature = world.Temperature,
            Atmosphere = world.Atmosphere,
            SurfacePressure = world.Pressure,
            Orbit = orbit,
            RotationPeriod = rotation,
            AxialTilt = tilt,
            TidallyLocked = locked,
            RingInnerRadius = ringInner,
            RingOuterRadius = ringOuter,
            RingMaterial = ringMaterial,
            RingMass = ringMass,
            State = world.State,
        });

        double moonRate = world.Type switch
        {
            PlanetType.GasGiant => 4.0,
            PlanetType.IceGiant => 2.5,
            _ => massEarth > 0.5 ? 0.4 : 0.0,
        };
        int moons = (int)Math.Min(rng.Poisson(moonRate), 12);
        double hill = orbit.Periapsis * DMath.Pow(mass / (3 * host.MassKg), 1.0 / 3.0);
        double moonLimit = 0.4 * hill;
        double moonA = Math.Max(2.5 * radius, ringOuter * 1.15) * rng.Range(1.2, 2.0);
        for (int m = 0; m < moons && moonA < moonLimit; m++)
        {
            var moonRng = new Rng(Hash64.Combine(host.Key, MoonStream + ((ulong)host.NodeNumber * 256 + (ulong)slot) * 256 + (ulong)m));
            AddMoon(b, host, planet, world, designation, path, m, moonA, massEarth, ref moonRng);
            moonA *= rng.Range(1.3, 2.2);
        }
        return semiMajorAu * (1 + eccentricity);
    }

    private static void AddMoon(Builder b, HostContext host, Body planet, World planetWorld, string designation, uint planetPath,
        int m, double moonA, double planetMassEarth, ref Rng rng)
    {
        double maxMoonMass = Math.Min(0.03, 3e-4 * planetMassEarth);
        double moonMassEarth = rng.LogUniform(1e-5, Math.Max(2e-5, maxMoonMass));
        double moonMass = moonMassEarth * Units.EarthMass;
        bool retrograde = rng.Chance(0.1);
        double inclination = StellarTables.Clamp(rng.Rayleigh(1 * DMath.DegToRad), 0, 20 * DMath.DegToRad);
        // Tides circularise a moon's orbit within a few hundred million years unless a resonance with another moon
        // keeps forcing it (Io 0.004, Europa 0.009, Enceladus 0.005); only that forced part heats it.
        bool resonant = rng.Chance(0.25);
        double eccentricity = resonant ? rng.LogUniform(0.002, 0.012) : rng.LogUniform(1e-6, 1e-4);
        var moonOrbit = new Orbit(moonA, eccentricity, retrograde ? DMath.Pi - inclination : inclination,
            rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi), rng.Range(0, DMath.TwoPi),
            Orbit.PeriodFor(moonA, planet.Mass + moonMass));

        // A moon forms in its planet's disc, so it shares the planet's formation zone, and gets the planet's starlight.
        double planetAu = planet.Orbit.SemiMajorAxis / Units.MetresPerAu;
        World world = WorldBuilder.Build(new WorldInput
        {
            MassEarth = moonMassEarth,
            Formation = Formation.Solid,
            FormationZone = planetWorld.State.FormationZone,
            Flux = host.FluxAt(planetAu),
            Activity = host.Activity,
            Age = host.Age,
            HostMass = planet.Mass,
            OrbitRadius = moonA,
            OrbitPeriod = moonOrbit.Period,
            Eccentricity = eccentricity,
            RotationPeriod = moonOrbit.Period,
            TidallyLocked = true,
            IsMoon = true,
            ParentRadius = planet.Radius,
            ParentHasBelts = planetWorld.Type == PlanetType.GasGiant && planetMassEarth > 150,
            StableHost = host.StableHost,
        }, ref rng);

        string letter = ((char)('a' + m)).ToString();
        b.Add(new Body
        {
            Index = b.Next,
            Parent = planet.Index,
            Kind = BodyKind.Moon,
            Name = planet.Name + " " + letter,
            Designation = designation + " " + letter,
            Path = BodyPath.Moon(planetPath, m),
            PlanetType = world.Type,
            Mass = moonMass,
            Radius = world.Radius,
            Temperature = world.Temperature,
            Atmosphere = world.Atmosphere,
            SurfacePressure = world.Pressure,
            Orbit = moonOrbit,
            RotationPeriod = moonOrbit.Period,
            TidallyLocked = true,
            State = world.State,
        });
    }

    // Adds a belt between innerLimitAu and outerLimitAu; returns its outer edge in AU. What it is made of follows where
    // it lies against the young star's snow line, like the planets.
    private static double AddBelt(Builder b, HostContext host, int slot, double semiMajorAu,
        double innerLimitAu, double outerLimitAu, ref Rng rng)
    {
        double widthAu = semiMajorAu * rng.Range(0.2, 0.4);
        widthAu = Math.Max(0, Math.Min(widthAu, 2 * Math.Min(semiMajorAu - innerLimitAu, outerLimitAu - semiMajorAu)));
        double a = semiMajorAu * Au;
        double zone = semiMajorAu / host.FormationSnowLine;
        Material material = zone < 0.15 ? Material.Metallic : zone < 0.5 ? Material.MetalRich : zone < 1.3 ? Material.Rocky : Material.Icy;
        double mass = rng.LogUniform(1e-4, 0.05) * host.MetalFactor * Units.EarthMass;
        b.Add(new Body
        {
            Index = b.Next,
            Parent = host.BodyIndex,
            Kind = BodyKind.Belt,
            Name = host.Prefix + " Belt",
            Designation = "Belt",
            Path = BodyPath.Belt(host.NodeNumber, slot),
            PlanetType = PlanetType.AsteroidBelt,
            Mass = mass,
            Orbit = new Orbit(a, 0, 0, 0, 0, 0, Orbit.PeriodFor(a, host.MassKg)),
            BeltWidth = widthAu * Au,
            BeltMaterial = material,
        });
        return semiMajorAu + widthAu / 2;
    }
}
