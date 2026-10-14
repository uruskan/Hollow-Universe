using System;
using System.Collections.Generic;
using System.Globalization;
using Hollow.Core;

namespace Hollow.Galaxy;

/// <summary>What a named region of the galaxy is.</summary>
public enum RegionKind : byte
{
    OpenCluster = 0,
    GlobularCluster = 1,
    EmissionNebula = 2,
    ReflectionNebula = 3,
    DarkNebula = 4,
    PlanetaryNebula = 5,
    SupernovaRemnant = 6,
}

/// <summary>
/// A real cluster or nebula as an object of the galaxy (GALAXY.md §15.2): a sphere around its centre. Procedural
/// systems inside it, in the sector that holds its centre, carry its name (§9).
/// </summary>
public sealed class Region
{
    internal Region(string name, string designation, RegionKind kind, Vector3D centre, double radius)
    {
        Name = name;
        Designation = designation;
        Kind = kind;
        Centre = centre;
        Radius = radius;
        InGrid = SectorGrid.TrySectorOf(centre, out int sx, out int sy, out int sz);
        SectorX = sx;
        SectorY = sy;
        SectorZ = sz;
    }

    /// <summary>The common name, e.g. "Pleiades"; also the prefix of its procedural systems' names.</summary>
    public string Name { get; }

    /// <summary>The catalogue designation, e.g. "M45".</summary>
    public string Designation { get; }

    public RegionKind Kind { get; }

    /// <summary>Centre in the galactic frame, light-years from Sol.</summary>
    public Vector3D Centre { get; }

    /// <summary>Radius in light-years: the cluster's or nebula's extent, rounded up to take in its halo.</summary>
    public double Radius { get; }

    /// <summary>The sector that holds the centre: systems of the region in it are named after the region.</summary>
    public int SectorX { get; }

    public int SectorY { get; }
    public int SectorZ { get; }
    public bool InGrid { get; }

    public bool Contains(Vector3D position) => Vector3D.Distance(position, Centre) <= Radius;

    public string KindName => Kind switch
    {
        RegionKind.OpenCluster => "open cluster",
        RegionKind.GlobularCluster => "globular cluster",
        RegionKind.EmissionNebula => "emission nebula",
        RegionKind.ReflectionNebula => "reflection nebula",
        RegionKind.DarkNebula => "dark nebula",
        RegionKind.PlanetaryNebula => "planetary nebula",
        RegionKind.SupernovaRemnant => "supernova remnant",
        _ => "region",
    };

    public override string ToString() => Name;
}

/// <summary>
/// Named regions of the authored layer (GALAXY.md §15.2): well-known real clusters and nebulae, positioned from their
/// J2000 coordinates and distances (public measurements), sized from their apparent extent. Compiled in, like the
/// rest of the authored layer.
/// </summary>
public static class Regions
{
    private static readonly Region[] AllRegions =
    {
        // Open clusters
        R("Hyades", "Melotte 25", RegionKind.OpenCluster, "04:26:54", "+15:52:00", 153, 15),
        R("Pleiades", "M45", RegionKind.OpenCluster, "03:47:24", "+24:07:00", 444, 15),
        R("Beehive Cluster", "M44", RegionKind.OpenCluster, "08:40:24", "+19:59:00", 577, 12),
        R("Coma Star Cluster", "Melotte 111", RegionKind.OpenCluster, "12:22:30", "+25:51:00", 280, 10),
        R("Alpha Persei Cluster", "Melotte 20", RegionKind.OpenCluster, "03:27:00", "+49:07:00", 570, 15),
        R("Southern Pleiades", "IC 2602", RegionKind.OpenCluster, "10:42:54", "-64:24:00", 479, 8),
        R("Omicron Velorum Cluster", "IC 2391", RegionKind.OpenCluster, "08:40:12", "-53:04:00", 500, 6),
        R("Ptolemy's Cluster", "M7", RegionKind.OpenCluster, "17:53:51.2", "-34:47:34", 980, 12),
        R("Butterfly Cluster", "M6", RegionKind.OpenCluster, "17:40:06", "-32:13:00", 1600, 6),
        R("Shoe-Buckle Cluster", "M35", RegionKind.OpenCluster, "06:09:06", "+24:21:00", 2800, 12),
        R("Golden Eye Cluster", "M67", RegionKind.OpenCluster, "08:51:18", "+11:49:00", 2770, 12),
        R("Wild Duck Cluster", "M11", RegionKind.OpenCluster, "18:51:06", "-06:16:00", 6200, 20),
        R("Double Cluster", "NGC 869 and NGC 884", RegionKind.OpenCluster, "02:21:30", "+57:08:00", 7500, 40),
        // Globular clusters
        R("Hercules Cluster", "M13", RegionKind.GlobularCluster, "16:41:41.2", "+36:27:35.5", 22200, 75),
        R("Sagittarius Cluster", "M22", RegionKind.GlobularCluster, "18:36:23.9", "-23:54:17.1", 10600, 50),
        R("Spider Cluster", "M4", RegionKind.GlobularCluster, "16:23:35.2", "-26:31:32.7", 7200, 35),
        R("Omega Centauri", "NGC 5139", RegionKind.GlobularCluster, "13:26:47.28", "-47:28:46.1", 17090, 75),
        R("47 Tucanae", "NGC 104", RegionKind.GlobularCluster, "00:24:05.67", "-72:04:52.6", 14700, 60),
        // Emission nebulae (star-forming regions)
        R("Orion Nebula", "M42", RegionKind.EmissionNebula, "05:35:17.3", "-05:23:28", 1344, 12),
        R("Lagoon Nebula", "M8", RegionKind.EmissionNebula, "18:03:37", "-24:23:12", 4100, 40),
        R("Eagle Nebula", "M16", RegionKind.EmissionNebula, "18:18:48", "-13:49:00", 5700, 35),
        R("Omega Nebula", "M17", RegionKind.EmissionNebula, "18:20:26", "-16:10:36", 5500, 9),
        R("Trifid Nebula", "M20", RegionKind.EmissionNebula, "18:02:23", "-23:01:48", 5200, 21),
        R("Carina Nebula", "NGC 3372", RegionKind.EmissionNebula, "10:45:08.5", "-59:52:04", 8500, 115),
        R("Rosette Nebula", "NGC 2237", RegionKind.EmissionNebula, "06:33:45", "+04:59:54", 5200, 65),
        R("North America Nebula", "NGC 7000", RegionKind.EmissionNebula, "20:59:17.1", "+44:31:44", 2590, 45),
        R("California Nebula", "NGC 1499", RegionKind.EmissionNebula, "04:03:18", "+36:25:18", 1000, 25),
        R("Heart Nebula", "IC 1805", RegionKind.EmissionNebula, "02:33:22", "+61:26:36", 7500, 80),
        // Reflection and dark nebulae
        R("M78 Nebula", "M78", RegionKind.ReflectionNebula, "05:46:46.7", "+00:00:50", 1600, 2),
        R("Horsehead Nebula", "Barnard 33", RegionKind.DarkNebula, "05:40:59", "-02:27:30", 1375, 2),
        R("Coalsack", "Caldwell 99", RegionKind.DarkNebula, "12:50:00", "-62:30:00", 600, 30),
        R("Rho Ophiuchi Cloud", "Rho Ophiuchi cloud complex", RegionKind.DarkNebula, "16:25:35", "-23:26:50", 460, 18),
        // Planetary nebulae
        R("Dumbbell Nebula", "M27", RegionKind.PlanetaryNebula, "19:59:36.3", "+22:43:16.1", 1360, 1.5),
        R("Ring Nebula", "M57", RegionKind.PlanetaryNebula, "18:53:35.1", "+33:01:45", 2570, 1.3),
        R("Owl Nebula", "M97", RegionKind.PlanetaryNebula, "11:14:47.7", "+55:01:08.5", 2030, 1),
        R("Helix Nebula", "NGC 7293", RegionKind.PlanetaryNebula, "22:29:38.55", "-20:50:13.6", 655, 2),
        R("Cat's Eye Nebula", "NGC 6543", RegionKind.PlanetaryNebula, "17:58:33.4", "+66:37:59", 3300, 0.5),
        // Supernova remnants
        R("Crab Nebula", "M1", RegionKind.SupernovaRemnant, "05:34:31.94", "+22:00:52.2", 6500, 5.5),
        R("Veil Nebula", "Cygnus Loop", RegionKind.SupernovaRemnant, "20:45:38", "+30:42:30", 2400, 55),
    };

    // Longest names first, so a name that starts another one (none do today) can't shadow it when parsing.
    private static readonly Region[] ByNameLength = SortByNameLength(AllRegions);

    public static IReadOnlyList<Region> All => AllRegions;

    /// <summary>The smallest region that holds a position, or null.</summary>
    public static Region? At(Vector3D position)
    {
        Region? best = null;
        foreach (Region r in AllRegions)
        {
            if (r.Contains(position) && (best == null || r.Radius < best.Radius))
            {
                best = r;
            }
        }
        return best;
    }

    /// <summary>A region by its name or designation (case-insensitive), or null.</summary>
    public static Region? Named(string text)
    {
        string t = text.Trim();
        foreach (Region r in AllRegions)
        {
            if (string.Equals(r.Name, t, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Designation, t, StringComparison.OrdinalIgnoreCase))
            {
                return r;
            }
        }
        return null;
    }

    /// <summary>
    /// The region a procedural system is named after: the smallest one that holds its position, if the system lies in
    /// that region's home sector (GALAXY.md §9). Null for every other system.
    /// </summary>
    public static Region? NamingRegion(SystemAddress address, Vector3D position)
    {
        if (address.IsCatalogue)
        {
            return null;
        }
        Region? r = At(position);
        CellKey cell = address.Cell;
        return r != null && r.InGrid && r.SectorX == cell.SectorX && r.SectorY == cell.SectorY && r.SectorZ == cell.SectorZ ? r : null;
    }

    /// <summary>
    /// Splits a system name that starts with a region's name into the region and the rest ("LA-YS 6-0"), or returns
    /// false. Only the name is matched here; the caller checks that the system really lies in the region.
    /// </summary>
    public static bool TrySplitName(string name, out Region region, out string rest)
    {
        string t = name.Trim();
        foreach (Region r in ByNameLength)
        {
            if (t.Length > r.Name.Length + 1 && t.StartsWith(r.Name, StringComparison.OrdinalIgnoreCase) && t[r.Name.Length] == ' ')
            {
                region = r;
                rest = t[(r.Name.Length + 1)..].Trim();
                return true;
            }
        }
        region = null!;
        rest = "";
        return false;
    }

    private static Region R(string name, string designation, RegionKind kind, string ra, string dec, double distanceLy, double radiusLy) =>
        new(name, designation, kind, GalaxyFrame.FromRaDec(Sexagesimal(ra) * 15, Sexagesimal(dec), distanceLy), radiusLy);

    // "05:35:17.3" → 5.588 (hours or degrees, with a sign).
    private static double Sexagesimal(string text)
    {
        double sign = text.StartsWith('-') ? -1 : 1;
        string[] parts = text.TrimStart('+', '-').Split(':');
        double value = 0;
        double scale = 1;
        foreach (string part in parts)
        {
            value += double.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture) * scale;
            scale /= 60;
        }
        return sign * value;
    }

    private static Region[] SortByNameLength(Region[] regions)
    {
        var sorted = (Region[])regions.Clone();
        Array.Sort(sorted, (a, b) => b.Name.Length != a.Name.Length ? b.Name.Length.CompareTo(a.Name.Length) : string.CompareOrdinal(a.Name, b.Name));
        return sorted;
    }
}
