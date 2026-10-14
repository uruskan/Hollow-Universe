using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Hollow.Core;
using Hollow.Galaxy.Authored;
using Hollow.Galaxy.Systems;

namespace Hollow.Galaxy;

/// <summary>
/// A search hit: a system, or a place (a sector or a named region, <paramref name="IsSector"/>) when the text names
/// one; <paramref name="PlaceKind"/> says which ("sector", "open cluster", "emission nebula", …).
/// </summary>
public readonly record struct SearchResult(string Name, SystemAddress Address, Vector3D Position, bool IsSector, string PlaceKind = "sector");

/// <summary>
/// The galaxy: density model, procedural stars, real-star catalogue, names and star systems.
/// Thread-safe; every query is a pure function of the seed and the catalogue.
/// </summary>
public sealed class Galaxy
{
    /// <summary>
    /// The seed of the main universe. Each stage mixes its own generator version into it (<see cref="GeneratorVersion"/>).
    /// </summary>
    public static readonly ulong DefaultSeed = Hash64.Of("Hollow Universe galaxy, version 1");

    private const int CellCacheLimit = 4096;

    private readonly CellGenerator _cells;
    private readonly SystemGenerator _systems;
    private readonly ConcurrentDictionary<ulong, GalaxyStar[]> _cellCache = new();

    public Galaxy(StarCatalogue? catalogue, ulong? seed = null)
    {
        Seed = seed ?? DefaultSeed;
        Catalogue = catalogue;
        Density = new DensityModel();
        _cells = new CellGenerator(Seed, Density);
        _systems = new SystemGenerator(Seed);
    }

    public ulong Seed { get; }
    public DensityModel Density { get; }
    public StarCatalogue? Catalogue { get; }

    /// <summary>Procedural stars of a cell (no catalogue stars). Not cached.</summary>
    public GalaxyStar[] GenerateProcedural(CellKey cell) => _cells.Generate(cell);

    /// <summary>Expected number of procedural systems in a cell (cheap; used for budgets).</summary>
    public double ExpectedCount(CellKey cell) => _cells.ExpectedCount(cell);

    /// <summary>Every system whose primary lives in this cell: procedural first, then catalogue.</summary>
    public GalaxyStar[] GetCellStars(CellKey cell)
    {
        GalaxyStar[] procedural = _cells.Generate(cell);
        if (Catalogue == null)
        {
            return procedural;
        }
        IReadOnlyList<CatalogueStar> real = Catalogue.InCell(cell);
        if (real.Count == 0)
        {
            return procedural;
        }
        var all = new GalaxyStar[procedural.Length + real.Count];
        procedural.CopyTo(all, 0);
        for (int i = 0; i < real.Count; i++)
        {
            all[procedural.Length + i] = Catalogue.ToGalaxyStar(real[i]);
        }
        return all;
    }

    /// <summary>
    /// The catalogue star an address names, or null: wrong catalogue, an id out of range, or not loaded.
    /// Addresses arrive from the network, so nothing about them is trusted.
    /// </summary>
    public CatalogueStar? FindCatalogueStar(SystemAddress address)
    {
        if (!address.IsCatalogue || Catalogue == null || address.CatalogueNumber != Catalogue.Number)
        {
            return null;
        }
        long id = address.CatalogueId;
        return id is >= 0 and <= int.MaxValue ? Catalogue.Find((int)id) : null;
    }

    /// <summary>Looks up a system's primary star by address.</summary>
    public bool TryGetStar(SystemAddress address, out GalaxyStar star)
    {
        star = default;
        if (address.IsCatalogue)
        {
            CatalogueStar? real = FindCatalogueStar(address);
            if (real == null || !real.IsPrimary)
            {
                return false;
            }
            star = Catalogue!.ToGalaxyStar(real);
            return true;
        }
        CellKey cell = address.Cell;
        GalaxyStar[] stars = _cellCache.GetOrAdd(cell.Packed, _ => _cells.Generate(cell));
        if (_cellCache.Count > CellCacheLimit)
        {
            _cellCache.Clear();
        }
        int lo = 0;
        int hi = stars.Length - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            int cmp = stars[mid].Address.CompareTo(address);
            if (cmp == 0)
            {
                star = stars[mid];
                return true;
            }
            if (cmp < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return false;
    }

    /// <summary>
    /// The system's name: the real name for catalogue systems, the procedural name otherwise, which inside a named
    /// region (in the region's home sector) starts with the region's name instead of the sector's (GALAXY.md §9).
    /// </summary>
    public string NameOf(SystemAddress address)
    {
        if (!address.IsCatalogue)
        {
            // Only systems in a region's home sector can carry its name, so most never need their star looked up.
            if (SectorHasRegion(address) && TryGetStar(address, out GalaxyStar star))
            {
                return ProceduralName(address, star.Position);
            }
            return SystemNames.Format(address);
        }
        CatalogueStar? real = FindCatalogueStar(address);
        if (real == null)
        {
            return $"Catalogue {address.CatalogueNumber}:{address.CatalogueId}";
        }
        return real.SystemName.Length > 0 ? real.SystemName : real.Name;
    }

    public string NameOf(in GalaxyStar star) =>
        star.Address.IsCatalogue ? NameOf(star.Address) : ProceduralName(star.Address, star.Position);

    private static string ProceduralName(SystemAddress address, Vector3D position) =>
        SystemNames.Format(address, Regions.NamingRegion(address, position)?.Name);

    private static bool SectorHasRegion(SystemAddress address)
    {
        CellKey cell = address.Cell;
        foreach (Region r in Regions.All)
        {
            if (r.InGrid && r.SectorX == cell.SectorX && r.SectorY == cell.SectorY && r.SectorZ == cell.SectorZ)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The sector name at a galactic position, or an empty string outside the grid.</summary>
    public static string SectorNameAt(Vector3D position) =>
        SectorGrid.TrySectorOf(position, out int sx, out int sy, out int sz) ? SectorNames.Name(sx, sy, sz) : "";

    /// <summary>
    /// Finds systems and sectors by name: catalogue names by prefix, procedural system names and sector names
    /// exactly (case-insensitive).
    /// </summary>
    public List<SearchResult> Search(string text, int max)
    {
        var results = new List<SearchResult>();
        string query = text.Trim();
        if (query.Length == 0)
        {
            return results;
        }
        if (SystemNames.TryParse(query, out SystemAddress address) && TryGetStar(address, out GalaxyStar star))
        {
            results.Add(new SearchResult(NameOf(star), address, star.Position, false));
        }
        // A system of a named region: the region's name, then the rest of a procedural name.
        if (Regions.TrySplitName(query, out Region region, out string rest) && region.InGrid
            && SystemNames.TryParse(SectorNames.Name(region.SectorX, region.SectorY, region.SectorZ) + " " + rest, out SystemAddress inRegion)
            && TryGetStar(inRegion, out GalaxyStar regional) && Regions.NamingRegion(inRegion, regional.Position) == region)
        {
            results.Add(new SearchResult(NameOf(regional), inRegion, regional.Position, false));
        }
        // Named regions themselves, as places.
        foreach (Region r in Regions.All)
        {
            if (results.Count < max && ((query.Length >= 3 && r.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                || string.Equals(r.Designation, query, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new SearchResult($"{r.Name} ({r.Designation})", default, r.Centre, true, r.KindName));
            }
        }
        if (SectorNames.TryParse(query, out int sx, out int sy, out int sz))
        {
            results.Add(new SearchResult(SectorNames.Name(sx, sy, sz) + " (sector)", default,
                SectorGrid.SectorCentre(sx, sy, sz), true));
        }
        if (Catalogue != null)
        {
            foreach ((string name, CatalogueStar real) in Catalogue.Search(query, max))
            {
                if (results.Count >= max)
                {
                    break;
                }
                string label = real.SystemName.Length > 0 && !string.Equals(real.SystemName, name, StringComparison.OrdinalIgnoreCase)
                    ? $"{name} ({real.SystemName})"
                    : name;
                results.Add(new SearchResult(label, SystemAddress.Catalogue(Catalogue.Number, real.Id), real.Position, false));
            }
        }
        return results;
    }

    /// <summary>Generates (or builds, for Sol) the full star system of an address.</summary>
    public StarSystem? GetSystem(SystemAddress address)
    {
        if (!TryGetStar(address, out GalaxyStar primary))
        {
            return null;
        }
        string name = NameOf(address);
        if (address.IsCatalogue && Catalogue != null)
        {
            CatalogueStar real = FindCatalogueStar(address)!; // TryGetStar succeeded, so it exists
            if (real.Id == 0)
            {
                return SolSystem.Create(primary);
            }
            var companions = new List<GalaxyStar>(real.Companions.Count);
            foreach (CatalogueStar c in real.Companions)
            {
                companions.Add(new GalaxyStar(SystemAddress.Catalogue(Catalogue.Number, c.Id), c.Position, c.Class,
                    c.Physics, 0f, GalaxyComponent.Catalogue, c.Level, primary.Age));
            }
            return _systems.Generate(primary, name, companions, AuthoredLayer.Find(address));
        }
        return _systems.Generate(primary, name);
    }
}
