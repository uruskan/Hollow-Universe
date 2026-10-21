using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Core;
using Hollow.Galaxy.Systems;

namespace Hollow.Galaxy;

/// <summary>
/// The finite playable universe. The legacy galaxy remains an unchanged source of deterministic systems;
/// map, search and travel resolve through this registry, never its unbounded cell population.
/// </summary>
public sealed class PlayableGalaxy
{
    public const int MaximumStars = 1000;
    public const int DefaultStarBudget = MaximumStars;
    public const int LayoutVersion = 1;

    public sealed record Entry(string Name, GalaxyStar SourceStar, GalaxyStar MapStar, int StarCount);

    private readonly Galaxy _source;
    private readonly Dictionary<SystemAddress, Entry> _byAddress = new();
    private readonly Entry[] _systems;

    public PlayableGalaxy(Galaxy source, int starBudget = DefaultStarBudget)
    {
        if (starBudget < 1 || starBudget > MaximumStars)
            throw new ArgumentOutOfRangeException(nameof(starBudget));
        _source = source;
        var entries = new List<Entry>();
        Add(SolSystem.Primary, "Sol", 1);

        // A fixed seeded sample outside the complete nearby-star catalogue. Optional catalogue files therefore
        // cannot change the playable population or its addresses. Every accepted system is generated unchanged.
        var random = new Rng(Hash64.Of(source.Seed, 0x504C415947414C, LayoutVersion));
        var cells = new HashSet<CellKey>();
        int attempts = 0;
        while (StarCount < starBudget && attempts++ < 8192)
        {
            var position = new Vector3D(random.Range(400, 1800), random.Range(-150, 150), random.Range(-1200, 1200));
            if (!CellKey.TryFromPosition(5, position, out CellKey cell) || !cells.Add(cell)) continue;
            foreach (GalaxyStar star in source.GenerateProcedural(cell))
            {
                if (_byAddress.ContainsKey(star.Address)) continue;
                StarSystem? system = source.GetSystem(star.Address);
                if (system == null) continue;
                int count = system.Bodies.Count(body => body.Kind == BodyKind.Star);
                if (count < 1 || StarCount + count > starBudget) continue;
                Add(star, system.Name, count);
                if (StarCount == starBudget) break;
            }
        }
        if (StarCount != starBudget)
            throw new InvalidOperationException($"Could not fill the requested {starBudget}-star playable galaxy.");
        _systems = entries.ToArray();
        Systems = Array.AsReadOnly(_systems);
        MapRadius = Math.Max(20, _systems.Max(entry => entry.MapStar.Position.Length));

        void Add(GalaxyStar sourceStar, string name, int count)
        {
            int index = entries.Count;
            Vector3D point = MapPosition(index, sourceStar.Address);
            // Map positions are diagram coordinates. They never overwrite physical source positions or orbits.
            var mapStar = new GalaxyStar(sourceStar.Address, point, sourceStar.Class, sourceStar.Physics,
                sourceStar.Metallicity, sourceStar.Component, 0, sourceStar.Age);
            var entry = new Entry(name, sourceStar, mapStar, count);
            entries.Add(entry);
            _byAddress.Add(sourceStar.Address, entry);
            StarCount += count;
        }
    }

    public IReadOnlyList<Entry> Systems { get; }
    public int StarCount { get; private set; }
    public double MapRadius { get; }
    public bool Contains(SystemAddress address) => _byAddress.ContainsKey(address);
    public Entry? Find(SystemAddress address) => _byAddress.GetValueOrDefault(address);
    public string NameOf(in GalaxyStar star) => Find(star.Address)?.Name ?? "Unknown system";

    public bool TryGetStar(SystemAddress address, out GalaxyStar star)
    {
        Entry? entry = Find(address);
        star = entry?.MapStar ?? default;
        return entry != null;
    }

    public StarSystem? GetSystem(SystemAddress address) => !Contains(address) ? null
        : address.Equals(SolSystem.Address) ? SolSystem.Create(SolSystem.Primary) : _source.GetSystem(address);

    public List<SearchResult> Search(string text, int maximum)
    {
        var results = new List<SearchResult>();
        string query = text.Trim();
        if (query.Length == 0 || maximum <= 0) return results;
        // Exact names first; searching Earth finds its parent system.
        foreach (Entry entry in _systems.OrderBy(e => string.Equals(e.Name, query, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (entry.SourceStar.Address.Equals(SolSystem.Address) && string.Equals(query, "Earth", StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new SearchResult(entry.Name, entry.SourceStar.Address, entry.MapStar.Position, false));
                if (results.Count == maximum) break;
            }
        }
        return results;
    }

    private static Vector3D MapPosition(int index, SystemAddress address)
    {
        if (index == 0) return Vector3D.Zero;
        var random = new Rng(Hash64.Of(address.Value, 0x4D4150, LayoutVersion));
        double radius = 15 * Math.Sqrt(index);
        double angle = index * DMath.Pi * (3 - Math.Sqrt(5)) + random.Range(-0.08, 0.08);
        return new Vector3D(radius * DMath.Cos(angle), 0, radius * DMath.Sin(angle));
    }
}
