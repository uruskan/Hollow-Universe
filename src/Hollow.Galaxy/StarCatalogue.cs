using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Hollow.Core;

namespace Hollow.Galaxy;

/// <summary>One real star from the catalogue. Companions hang off their primary.</summary>
public sealed class CatalogueStar
{
    public int Id { get; init; }

    /// <summary>The primary's ID for a companion; −1 for a primary.</summary>
    public int PrimaryId { get; init; }

    public Vector3D Position { get; init; }
    public StarClass Class { get; init; }
    public StarPhysics Physics { get; init; }

    /// <summary>This star's own name, e.g. "Rigil Kentaurus".</summary>
    public string Name { get; init; } = "";

    /// <summary>The system's name if this is a primary, e.g. "Alpha Centauri"; empty for companions.</summary>
    public string SystemName { get; init; } = "";

    /// <summary>Other names and designations, for search.</summary>
    public string[] Aliases { get; init; } = Array.Empty<string>();

    public int Level { get; init; }

    public bool IsPrimary => PrimaryId < 0;

    public List<CatalogueStar> Companions { get; } = new();
}

/// <summary>
/// The real-star catalogue, loaded from the compact file written by tools/Hollow.CatalogBuilder
/// (GALAXY.md §8). Indexed by ID, by galaxy cell and by name.
/// </summary>
public sealed class StarCatalogue
{
    public const int FormatVersion = 1;
    private static readonly byte[] Magic = "HUSC"u8.ToArray();

    private readonly Dictionary<int, CatalogueStar> _byId = new();
    private readonly Dictionary<ulong, List<CatalogueStar>> _byCell = new();
    private readonly Dictionary<string, CatalogueStar> _byName = new(StringComparer.OrdinalIgnoreCase);
    private (string Key, string Display, CatalogueStar Star)[] _sortedNames = Array.Empty<(string, string, CatalogueStar)>();

    /// <summary>Catalogue number used in system addresses (0 = HYG 4.1).</summary>
    public int Number { get; private set; }

    public IReadOnlyList<CatalogueStar> Stars { get; private set; } = Array.Empty<CatalogueStar>();

    public IEnumerable<CatalogueStar> Primaries
    {
        get
        {
            foreach (CatalogueStar s in Stars)
            {
                if (s.IsPrimary)
                {
                    yield return s;
                }
            }
        }
    }

    public static StarCatalogue Load(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Load(file);
    }

    /// <summary>Loads a gzip-compressed catalogue.</summary>
    public static StarCatalogue Load(Stream compressed)
    {
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new BinaryReader(gzip, Encoding.UTF8);
        byte[] magic = reader.ReadBytes(4);
        if (magic.Length != 4 || !magic.AsSpan().SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a Hollow Universe star catalogue.");
        }
        int version = reader.ReadInt32();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported catalogue version {version}.");
        }
        var catalogue = new StarCatalogue { Number = reader.ReadInt32() };
        int count = reader.ReadInt32();
        var stars = new List<CatalogueStar>(count);
        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadInt32();
            int primary = reader.ReadInt32();
            var position = new Vector3D(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble());
            var kind = (StarKind)reader.ReadByte();
            float code = reader.ReadSingle();
            float absMag = reader.ReadSingle();
            float teff = reader.ReadSingle();
            float mass = reader.ReadSingle();
            float radius = reader.ReadSingle();
            float luminosity = reader.ReadSingle();
            string name = reader.ReadString();
            string systemName = reader.ReadString();
            string aliases = reader.ReadString();
            stars.Add(new CatalogueStar
            {
                Id = id,
                PrimaryId = primary,
                Position = position,
                Class = new StarClass(kind, code),
                Physics = new StarPhysics(teff, absMag, mass, radius, luminosity),
                Name = name,
                SystemName = systemName,
                Aliases = aliases.Length == 0 ? Array.Empty<string>() : aliases.Split('|'),
                Level = StarClassTable.LevelForMagnitude(absMag),
            });
        }
        catalogue.Index(stars);
        return catalogue;
    }

    /// <summary>Writes a catalogue (used by the builder tool and by tests).</summary>
    public static void Write(Stream output, int number, IReadOnlyList<CatalogueStar> stars)
    {
        using var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
        using var writer = new BinaryWriter(gzip, Encoding.UTF8);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(number);
        writer.Write(stars.Count);
        foreach (CatalogueStar s in stars)
        {
            writer.Write(s.Id);
            writer.Write(s.PrimaryId);
            writer.Write(s.Position.X);
            writer.Write(s.Position.Y);
            writer.Write(s.Position.Z);
            writer.Write((byte)s.Class.Kind);
            writer.Write(s.Class.Code);
            writer.Write(s.Physics.AbsoluteMagnitude);
            writer.Write(s.Physics.Temperature);
            writer.Write(s.Physics.Mass);
            writer.Write(s.Physics.Radius);
            writer.Write(s.Physics.Luminosity);
            writer.Write(s.Name);
            writer.Write(s.SystemName);
            writer.Write(string.Join('|', s.Aliases));
        }
    }

    public CatalogueStar? Find(int id) => _byId.TryGetValue(id, out CatalogueStar? s) ? s : null;

    /// <summary>The catalogue primaries whose position falls in this cell at their own level.</summary>
    public IReadOnlyList<CatalogueStar> InCell(CellKey cell) =>
        _byCell.TryGetValue(cell.Packed, out List<CatalogueStar>? list) ? list : Array.Empty<CatalogueStar>();

    /// <summary>Exact name lookup (any name or alias, case-insensitive). Returns the system's primary.</summary>
    public CatalogueStar? FindByName(string name) =>
        _byName.TryGetValue(name.Trim(), out CatalogueStar? s) ? s : null;

    /// <summary>Names starting with <paramref name="prefix"/> (case-insensitive), primaries only, deduplicated.</summary>
    public List<(string Name, CatalogueStar Star)> Search(string prefix, int max)
    {
        var results = new List<(string, CatalogueStar)>();
        string key = Normalise(prefix);
        if (key.Length == 0)
        {
            return results;
        }
        int lo = 0;
        int hi = _sortedNames.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (string.CompareOrdinal(_sortedNames[mid].Key, key) < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }
        var seen = new HashSet<int>();
        for (int i = lo; i < _sortedNames.Length && results.Count < max; i++)
        {
            if (!_sortedNames[i].Key.StartsWith(key, StringComparison.Ordinal))
            {
                break;
            }
            if (seen.Add(_sortedNames[i].Star.Id))
            {
                results.Add((_sortedNames[i].Display, _sortedNames[i].Star));
            }
        }
        return results;
    }

    /// <summary>The galaxy-level view of a catalogue primary.</summary>
    public GalaxyStar ToGalaxyStar(CatalogueStar star) => new(
        SystemAddress.Catalogue(Number, star.Id),
        star.Position,
        star.Class,
        star.Physics,
        0f,
        GalaxyComponent.Catalogue,
        star.Level);

    private void Index(List<CatalogueStar> stars)
    {
        Stars = stars;
        foreach (CatalogueStar s in stars)
        {
            _byId[s.Id] = s;
        }
        var names = new List<(string, string, CatalogueStar)>();
        foreach (CatalogueStar s in stars)
        {
            if (!s.IsPrimary)
            {
                if (_byId.TryGetValue(s.PrimaryId, out CatalogueStar? primary))
                {
                    primary.Companions.Add(s);
                }
                continue;
            }
            if (CellKey.TryFromPosition(s.Level, s.Position, out CellKey cell))
            {
                if (!_byCell.TryGetValue(cell.Packed, out List<CatalogueStar>? list))
                {
                    list = new List<CatalogueStar>();
                    _byCell[cell.Packed] = list;
                }
                list.Add(s);
            }
        }
        // Names: system name, own name and aliases of primaries; companions' names lead to their primary.
        foreach (CatalogueStar s in stars)
        {
            CatalogueStar target = s.IsPrimary ? s : (Find(s.PrimaryId) ?? s);
            AddName(names, s.SystemName, target);
            AddName(names, s.Name, target);
            foreach (string alias in s.Aliases)
            {
                AddName(names, alias, target);
            }
        }
        names.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        _sortedNames = names.ToArray();
    }

    private void AddName(List<(string, string, CatalogueStar)> names, string name, CatalogueStar target)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        string trimmed = name.Trim();
        _byName.TryAdd(trimmed, target);
        names.Add((Normalise(trimmed), trimmed, target));
    }

    private static string Normalise(string s) => s.Trim().ToLowerInvariant();
}
