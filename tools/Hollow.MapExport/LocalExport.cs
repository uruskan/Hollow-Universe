using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;
using HollowGalaxy = Hollow.Galaxy.Galaxy;

namespace Hollow.MapExport;

/// <summary>One system of the local bubble, with its generated star system.</summary>
internal sealed class LocalSystem
{
    public GalaxyStar Star;
    public string Name = "";
    public double Distance;
    public StarSystem System = null!;

    /// <summary>Counts, in the order of <see cref="LocalExport.SummaryFields"/>.</summary>
    public int[] Summary = new int[LocalExport.SummaryFields.Length];
}

internal sealed class LocalData
{
    public List<LocalSystem> Systems = new();
    public int BodyListCount;
}

/// <summary>local.js: every system within 150 ly of Sol, catalogue and procedural.</summary>
internal static class LocalExport
{
    public const double Radius = 150;
    public const double BodyListRadius = 40;

    public static readonly string[] SummaryFields =
    {
        "stars", "planets", "moons", "landable", "gasGiants", "iceGiants", "earthLike", "waterWorlds", "ringed", "belts",
    };

    public static LocalData Build(HollowGalaxy galaxy)
    {
        var found = new ConcurrentBag<GalaxyStar>();
        for (int level = 0; level < StarClassTable.LevelCount; level++)
        {
            var cells = new List<CellKey>();
            CellKey.TryFromPosition(level, new Vector3D(-Radius, -Radius, -Radius), out CellKey lo);
            CellKey.TryFromPosition(level, new Vector3D(Radius, Radius, Radius), out CellKey hi);
            for (long gx = lo.GlobalX; gx <= hi.GlobalX; gx++)
            {
                for (long gy = lo.GlobalY; gy <= hi.GlobalY; gy++)
                {
                    for (long gz = lo.GlobalZ; gz <= hi.GlobalZ; gz++)
                    {
                        if (CellKey.TryFromGlobal(level, gx, gy, gz, out CellKey cell) && NearestDistance(cell) <= Radius)
                        {
                            cells.Add(cell);
                        }
                    }
                }
            }
            Parallel.ForEach(cells, cell =>
            {
                foreach (GalaxyStar s in galaxy.GetCellStars(cell))
                {
                    if (s.Position.Length <= Radius)
                    {
                        found.Add(s);
                    }
                }
            });
        }

        var data = new LocalData();
        data.Systems = found
            .Select(s => new LocalSystem { Star = s, Distance = s.Position.Length })
            .OrderBy(s => s.Distance)
            .ThenBy(s => s.Star.Address.Value)
            .ToList();

        Parallel.For(0, data.Systems.Count, i =>
        {
            LocalSystem ls = data.Systems[i];
            ls.Name = galaxy.NameOf(ls.Star.Address);
            ls.System = galaxy.GetSystem(ls.Star.Address)
                ?? throw new InvalidOperationException($"No system for {ls.Star.Address}");
            Summarise(ls);
        });
        data.BodyListCount = data.Systems.Count(s => s.Distance <= BodyListRadius);
        return data;
    }

    private static void Summarise(LocalSystem ls)
    {
        int[] n = ls.Summary;
        foreach (Body b in ls.System.Bodies)
        {
            switch (b.Kind)
            {
                case BodyKind.Star:
                    n[0]++;
                    break;
                case BodyKind.Planet:
                    n[1]++;
                    if (b.HasRings)
                    {
                        n[8]++;
                    }
                    switch (b.PlanetType)
                    {
                        case PlanetType.GasGiant:
                            n[4]++;
                            break;
                        case PlanetType.IceGiant:
                            n[5]++;
                            break;
                        case PlanetType.EarthLike:
                            n[6]++;
                            break;
                        case PlanetType.WaterWorld:
                            n[7]++;
                            break;
                    }
                    break;
                case BodyKind.Moon:
                    n[2]++;
                    break;
                case BodyKind.Belt:
                    n[9]++;
                    break;
            }
            if (b.Landable)
            {
                n[3]++;
            }
        }
    }

    private static double NearestDistance(CellKey cell)
    {
        Vector3D min = cell.Min;
        double size = cell.Size;
        double dx = min.X > 0 ? min.X : (min.X + size < 0 ? min.X + size : 0);
        double dy = min.Y > 0 ? min.Y : (min.Y + size < 0 ? min.Y + size : 0);
        double dz = min.Z > 0 ? min.Z : (min.Z + size < 0 ? min.Z + size : 0);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public static void Write(string path, HollowGalaxy galaxy, LocalData data)
    {
        List<LocalSystem> list = data.Systems;
        Js.WriteGlobal(path, "HOLLOW_LOCAL",
            $"Hollow Universe: the {list.Count} systems within {Radius} ly of Sol (catalogue + procedural).", w =>
        {
            w.WriteStartObject();
            w.WritePropertyName("meta");
            w.WriteStartObject();
            w.WriteString("what", "Every star system within 150 ly of Sol, from the real generator (HYG 4.1 catalogue + procedural).");
            w.WriteString("seed", Js.Hex(galaxy.Seed));
            w.WriteNumber("generatorVersion", HollowGalaxy.GeneratorVersion);
            w.WriteString("frame", "Galactic frame, light-years, origin Sol. +X toward galactic longitude 90 deg, +Y toward the north galactic pole (up, out of the plane), +Z toward the Galactic Centre. Right-handed, Y up.");
            w.WriteNumber("radiusLy", Radius);
            w.WriteNumber("bodyListRadiusLy", BodyListRadius);
            w.WriteNumber("count", list.Count);
            w.WriteNumber("catalogueCount", list.Count(s => s.Star.IsCatalogue));
            w.WriteNumber("bodyListCount", data.BodyListCount);
            w.WritePropertyName("summaryFields");
            w.WriteStartArray();
            foreach (string f in SummaryFields)
            {
                w.WriteStringValue(f);
            }
            w.WriteEndArray();
            w.WritePropertyName("bodyFields");
            w.WriteStartArray();
            foreach (string f in new[] { "name", "kind", "type", "parent", "aAU", "radiusKm", "gravityG", "tempK", "atmosphere", "landable", "rings" })
            {
                w.WriteStringValue(f);
            }
            w.WriteEndArray();
            w.WritePropertyName("bodyKinds");
            w.WriteStartArray();
            foreach (string f in new[] { "star", "planet", "moon", "belt" })
            {
                w.WriteStringValue(f);
            }
            w.WriteEndArray();
            w.WriteEndObject();

            // Column-wise system table: index i of every array is system i (sorted by distance from Sol).
            w.WritePropertyName("systems");
            w.WriteStartObject();
            Column(w, "addr", list, s => w.WriteStringValue(Js.Hex(s.Star.Address.Value)));
            Column(w, "name", list, s => w.WriteStringValue(s.Name));
            Column(w, "x", list, s => w.WriteNumberValue(Js.Round(s.Star.Position.X, 2)));
            Column(w, "y", list, s => w.WriteNumberValue(Js.Round(s.Star.Position.Y, 2)));
            Column(w, "z", list, s => w.WriteNumberValue(Js.Round(s.Star.Position.Z, 2)));
            Column(w, "cls", list, s => w.WriteStringValue(s.Star.Class.ToString()));
            Column(w, "col", list, s => w.WriteStringValue(StarColours.Hex(s.Star.Class, s.Star.Physics.Temperature)));
            Column(w, "mag", list, s => w.WriteNumberValue(Js.Round(s.Star.Physics.AbsoluteMagnitude, 2)));
            Column(w, "teff", list, s => w.WriteNumberValue(Math.Round(s.Star.Physics.Temperature)));
            Column(w, "lvl", list, s => w.WriteNumberValue(s.Star.Level));
            Column(w, "cat", list, s => w.WriteNumberValue(s.Star.IsCatalogue ? 1 : 0));
            Column(w, "scoop", list, s => w.WriteNumberValue(StarColours.Scoopable(s.Star.Class) ? 1 : 0));
            // Planet summary: one small array per system, in the order of meta.summaryFields.
            Column(w, "sum", list, s =>
            {
                w.WriteStartArray();
                foreach (int v in s.Summary)
                {
                    w.WriteNumberValue(v);
                }
                w.WriteEndArray();
            });
            w.WriteEndObject();

            // Body lists for systems within 40 ly, keyed by system index.
            w.WritePropertyName("bodies");
            w.WriteStartObject();
            for (int i = 0; i < list.Count; i++)
            {
                LocalSystem s = list[i];
                if (s.Distance > BodyListRadius)
                {
                    continue;
                }
                w.WritePropertyName(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                w.WriteStartArray();
                foreach (Body b in s.System.Bodies)
                {
                    w.WriteStartArray();
                    w.WriteStringValue(b.Name);
                    w.WriteNumberValue((int)b.Kind);
                    w.WriteStringValue(b.TypeName);
                    w.WriteNumberValue(b.Parent);
                    w.WriteNumberValue(Js.Sig(b.Orbit.SemiMajorAxis / Units.MetresPerAu, 5));
                    w.WriteNumberValue(Math.Round(b.Radius / 1000.0));
                    w.WriteNumberValue(Js.Sig(b.SurfaceGravity, 3));
                    w.WriteNumberValue(Math.Round(b.Temperature));
                    w.WriteStringValue(AtmosphereName(b.Atmosphere));
                    w.WriteNumberValue(b.Landable ? 1 : 0);
                    w.WriteNumberValue(b.HasRings ? 1 : 0);
                    w.WriteEndArray();
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        });
    }

    public static string AtmosphereName(AtmosphereType type) => type switch
    {
        AtmosphereType.None => "none",
        AtmosphereType.Thin => "thin",
        AtmosphereType.Thick => "thick",
        AtmosphereType.Breathable => "breathable",
        AtmosphereType.HydrogenHelium => "hydrogen-helium",
        _ => "unknown",
    };

    private static void Column(Utf8JsonWriter w, string name, List<LocalSystem> list, Action<LocalSystem> write)
    {
        w.WritePropertyName(name);
        w.WriteStartArray();
        foreach (LocalSystem s in list)
        {
            write(s);
        }
        w.WriteEndArray();
    }
}
