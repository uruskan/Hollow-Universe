using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Hollow.Core;
using Hollow.Galaxy;

namespace Hollow.CatalogBuilder;

/// <summary>
/// Converts a star catalogue (CSV) into Hollow Universe's compact catalogue (GALAXY.md §8).
/// <list type="bullet">
/// <item><c>hygdata_v41.csv out.bin.gz</c>: the HYG database v4.1 (catalogue number 0). The CSV is not stored in the
/// repository; get it from https://github.com/astronexus/HYG-Database (CC BY-SA 4.0).</item>
/// <item><c>athyg_v40.csv out.bin.gz [--hyg hygdata_v41.csv] [--keep-all]</c>: AT-HYG v4.0 (catalogue number 1), from
/// https://codeberg.org/astronexus/athyg (CC BY-SA 4.0); the HYG CSV, if given, supplies multiple-star systems.
/// <c>--keep-all</c> keeps every star, for refitting <see cref="CatalogueCompleteness"/> to the new catalogue.</item>
/// <item><c>--update in.bin.gz out.bin.gz</c>: re-applies the rules that don't need the source data (which stars to
/// keep, repairs, bindings, the hand-entered supplement) to a catalogue built earlier.</item>
/// </list>
/// The format is detected from the CSV header.
/// </summary>
public static class Program
{
    private const int HygCatalogue = 0; // HYG 4.1 (+ Hollow's supplement)
    private const int AtHygCatalogue = 1; // AT-HYG 4.0 (+ Hollow's supplement)

    public static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--update")
        {
            StarCatalogue built = StarCatalogue.Load(args[1]);
            List<CatalogueStar> kept = Finish(built.Stars, keepAll: false);
            Write(args[2], built.Number, kept);
            Console.WriteLine($"Kept {kept.Count} of {built.Stars.Count} stars.");
            return 0;
        }
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: Hollow.CatalogBuilder <hygdata_v41.csv> <output.bin.gz>");
            Console.Error.WriteLine("       Hollow.CatalogBuilder <athyg_v40.csv> <output.bin.gz> [--hyg <hygdata_v41.csv>] [--keep-all]");
            Console.Error.WriteLine("       Hollow.CatalogBuilder --update <input.bin.gz> <output.bin.gz>");
            return 2;
        }
        string? hygPath = null;
        bool keepAll = false;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--hyg" && i + 1 < args.Length)
            {
                hygPath = args[++i];
            }
            else if (args[i] == "--keep-all")
            {
                keepAll = true;
            }
        }
        List<Dictionary<string, string>> rows = ReadCsv(args[0]);
        Console.WriteLine($"Read {rows.Count} rows.");
        bool atHyg = rows.Count > 0 && rows[0].ContainsKey("x0") && !rows[0].ContainsKey("comp_primary");
        Dictionary<int, Row> parsed = atHyg
            ? ReadAtHyg(rows, hygPath != null ? ReadCsv(hygPath) : null)
            : ReadHyg(rows);
        List<CatalogueStar> stars = Finish(Assemble(parsed, keepAll), keepAll);
        Write(args[1], atHyg ? AtHygCatalogue : HygCatalogue, stars);
        return 0;
    }

    private static void Write(string path, int number, List<CatalogueStar> stars)
    {
        using (FileStream output = File.Create(path))
        {
            StarCatalogue.Write(output, number, stars);
        }
        int primaries = stars.Count(s => s.IsPrimary);
        Console.WriteLine($"Wrote {stars.Count} stars ({primaries} systems) to {path} ({new FileInfo(path).Length / 1024} KiB).");
    }

    // ---------------------------------------------------------------- rules for every catalogue

    /// <summary>IDs of Hollow's hand-entered supplement (GALAXY.md §8.4): above every HYG ID, below the AT-HYG ranges.</summary>
    public const int SupplementFirstId = 1_000_000;

    public const int SupplementLastId = 1_999_999;

    // Real companions the source lists as systems of their own: companion ID → (primary ID, extra alias).
    private static readonly Dictionary<int, (int Primary, string Alias)> Bindings = new()
    {
        [70666] = (71456, "Alpha Centauri C"), // Proxima Centauri is bound to Alpha Centauri A and B
    };

    // Spectral types the source gets wrong or leaves out for nearby stars, by HYG ID (applied to every build).
    private static readonly Dictionary<int, string> ClassOverrides = new()
    {
        [3820] = "DZ8", // van Maanen's Star, a white dwarf (no type in the source, so it had become a G5 dwarf)
        [119275] = "M8V", // VB 10, Gliese 752 B
        [118831] = "M7V", // Wolf 424 B, Gliese 473 B
    };

    /// <summary>
    /// The rules applied to every built catalogue, from a CSV or from an earlier file (GALAXY.md §8.2–8.4): repair
    /// classes the source got wrong, bind real companions listed as separate systems, add the hand-entered supplement,
    /// and keep exactly the systems the generator leaves to the catalogue, i.e. where <see cref="CatalogueCompleteness"/>
    /// is above zero for the primary's level and distance (supplement systems are always kept). Companions follow their
    /// primary. Applying it twice changes nothing.
    /// </summary>
    public static List<CatalogueStar> Finish(IReadOnlyList<CatalogueStar> stars, bool keepAll)
    {
        // Repairs and bindings; an earlier supplement is replaced by the current one.
        var repaired = new List<CatalogueStar>(stars.Count);
        int bound = 0;
        foreach (CatalogueStar s in stars)
        {
            if (s.Id is >= SupplementFirstId and <= SupplementLastId)
            {
                continue;
            }
            CatalogueStar star = Repair(s);
            if (Bindings.TryGetValue(star.Id, out (int Primary, string Alias) bind) && stars.Any(p => p.Id == bind.Primary))
            {
                star = With(star, primaryId: bind.Primary, systemName: "",
                    aliases: star.Aliases.Contains(bind.Alias) ? star.Aliases : star.Aliases.Append(bind.Alias).ToArray());
                bound++;
            }
            repaired.Add(star);
        }
        var byId = repaired.ToDictionary(s => s.Id);
        List<CatalogueStar> supplement = Supplement(byId, Path.Combine(AppContext.BaseDirectory, "supplement.csv"));
        repaired.AddRange(supplement);

        var keptPrimaries = new HashSet<int>();
        foreach (CatalogueStar s in repaired)
        {
            if (s.PrimaryId < 0 && (keepAll || s.Id is >= SupplementFirstId and <= SupplementLastId || Catalogued(s)))
            {
                keptPrimaries.Add(s.Id);
            }
        }
        var kept = new List<CatalogueStar>();
        foreach (CatalogueStar s in repaired)
        {
            if (keptPrimaries.Contains(s.PrimaryId < 0 ? s.Id : s.PrimaryId))
            {
                kept.Add(s);
            }
        }
        Console.WriteLine($"Supplement: {supplement.Count} stars; bound {bound} companion(s) to their systems.");
        return kept;
    }

    private static bool Catalogued(CatalogueStar primary) =>
        primary.Id == 0 || CatalogueCompleteness.Of(StarClassTable.LevelForMagnitude(primary.Physics.AbsoluteMagnitude), primary.Position.Length) > 0;

    // A star whose class the source got wrong: an explicit override, a star without a type that became a G5 dwarf
    // although it is far fainter than any G dwarf, or a "giant" fainter than any giant (a garbled type). The measured
    // absolute magnitude is kept; the class follows from it on the main sequence.
    private static CatalogueStar Repair(CatalogueStar s)
    {
        float absMag = s.Physics.AbsoluteMagnitude;
        if (ClassOverrides.TryGetValue(s.Id, out string? spectrum))
        {
            (StarKind kind, double code) = ParseSpectrum(spectrum, absMag, double.NaN);
            return With(s, cls: new StarClass(kind, (float)code), physics: PhysicsFor(kind, code, absMag, double.NaN, double.NaN));
        }
        bool untypedDwarf = s.Class.Kind == StarKind.MainSequence && s.Class.Code == 45f && absMag > 7.5f;
        bool faintGiant = s.Class.Kind is StarKind.Giant or StarKind.BrightGiant or StarKind.Supergiant && absMag > 6f;
        if (untypedDwarf || faintGiant)
        {
            double code = MainSequenceCodeForMagnitude(absMag);
            return With(s, cls: new StarClass(StarKind.MainSequence, (float)code),
                physics: PhysicsFor(StarKind.MainSequence, code, absMag, double.NaN, double.NaN));
        }
        return s;
    }

    // The main-sequence spectral code (half steps, O3 to M9.5) whose absolute magnitude is nearest.
    private static double MainSequenceCodeForMagnitude(double absMag)
    {
        double best = 45;
        double bestDifference = double.MaxValue;
        for (double code = 3; code <= 69.5; code += 0.5)
        {
            double difference = Math.Abs(StellarTables.MainSequenceMagnitude(code) - absMag);
            if (difference < bestDifference)
            {
                best = code;
                bestDifference = difference;
            }
        }
        return best;
    }

    // Physics for a class: typical values, with a measured magnitude, temperature or mass where known.
    private static StarPhysics PhysicsFor(StarKind kind, double code, double absMag, double teff, double mass)
    {
        double radius;
        double typicalMag;
        if (kind == StarKind.WhiteDwarf)
        {
            mass = double.IsNaN(mass) ? 0.6 : mass;
            teff = double.IsNaN(teff) ? 50400.0 / Math.Max(code, 0.5) : teff;
            radius = 0.0126 * Math.Pow(mass / 0.6, -1.0 / 3.0);
            typicalMag = StellarTables.WhiteDwarfMagnitude(teff);
        }
        else
        {
            (double t, double mv, double m, double r) = StellarTables.Typical(kind, code);
            teff = double.IsNaN(teff) ? t : teff;
            mass = double.IsNaN(mass) ? m : mass;
            radius = r;
            typicalMag = mv;
        }
        double magnitude = double.IsNaN(absMag) ? typicalMag : absMag;
        return new StarPhysics((float)teff, (float)magnitude, (float)mass, (float)radius, (float)StellarTables.Luminosity(radius, teff));
    }

    private static CatalogueStar With(CatalogueStar s, int? primaryId = null, string? systemName = null, string[]? aliases = null,
        StarClass? cls = null, StarPhysics? physics = null) => new()
    {
        Id = s.Id,
        PrimaryId = primaryId ?? s.PrimaryId,
        Position = s.Position,
        Class = cls ?? s.Class,
        Physics = physics ?? s.Physics,
        Name = s.Name,
        SystemName = systemName ?? s.SystemName,
        Aliases = aliases ?? s.Aliases,
        Level = s.Level,
    };

    // ---------------------------------------------------------------- the hand-entered supplement

    /// <summary>
    /// Hollow's supplement (GALAXY.md §8.4, <c>supplement.csv</c>): real stars and brown dwarfs that the source catalogue
    /// lacks, from public measurements. A new system has its J2000 right ascension, declination and parallax; a
    /// companion (of a source star or of a supplement star) its separation in AU from its primary, or from the star
    /// named in <c>near</c> (the partner of a close pair), placed across the line of sight at the primary's distance.
    /// A separation of 0 means unknown.
    /// </summary>
    public static List<CatalogueStar> Supplement(IReadOnlyDictionary<int, CatalogueStar> source, string path)
    {
        var result = new List<CatalogueStar>();
        if (!File.Exists(path))
        {
            Console.WriteLine($"No supplement at {path}.");
            return result;
        }
        List<Dictionary<string, string>> rows = ReadCsv(path);
        var placed = new Dictionary<int, CatalogueStar>();
        CatalogueStar? Lookup(int id) => placed.TryGetValue(id, out CatalogueStar? s) ? s : source.TryGetValue(id, out s) ? s : null;

        // New systems first, then companions in file order (a companion may sit next to an earlier one).
        foreach (bool companions in new[] { false, true })
        {
            foreach (Dictionary<string, string> r in rows)
            {
                bool isCompanion = r["primary"].Length > 0;
                if (isCompanion != companions)
                {
                    continue;
                }
                int id = Int(r["id"]);
                if (id is < SupplementFirstId or > SupplementLastId)
                {
                    throw new InvalidDataException($"Supplement ID {id} is outside {SupplementFirstId}–{SupplementLastId}.");
                }
                (StarKind kind, double code) = ParseSpectrum(r["spect"], double.NaN, double.NaN);
                StarPhysics physics = PhysicsFor(kind, code, double.NaN, Double(r["teff_k"]), Double(r["mass_msun"]));
                Vector3D position;
                int primaryId = -1;
                if (!isCompanion)
                {
                    double distanceLy = 1000.0 / Double(r["parallax_mas"]) * Units.LightYearsPerParsec;
                    position = GalaxyFrame.FromEquatorial(Equatorial(Hours(r["ra"]), Degrees(r["dec"]))) * distanceLy;
                }
                else
                {
                    primaryId = Int(r["primary"]);
                    CatalogueStar? primary = Lookup(primaryId);
                    if (primary == null)
                    {
                        Console.WriteLine($"Supplement {id} ({r["name"]}) skipped: its primary {primaryId} is not in this catalogue.");
                        continue;
                    }
                    CatalogueStar anchor = r["near"].Length > 0
                        ? Lookup(Int(r["near"])) ?? throw new InvalidDataException($"Supplement {id}: no star {r["near"]}.")
                        : primary;
                    double separationAu = r["separation_au"].Length > 0 ? Double(r["separation_au"]) : 0;
                    position = anchor.Position + Across(anchor.Position, id) * (separationAu / AuPerLightYear);
                }
                string name = r["name"].Trim();
                var star = new CatalogueStar
                {
                    Id = id,
                    PrimaryId = primaryId,
                    Position = position,
                    Class = new StarClass(kind, (float)code),
                    Physics = physics,
                    Name = name,
                    SystemName = isCompanion ? "" : (r["system"].Trim().Length > 0 ? r["system"].Trim() : name),
                    Aliases = r["aliases"].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    Level = StarClassTable.LevelForMagnitude(physics.AbsoluteMagnitude),
                };
                placed[id] = star;
                result.Add(star);
            }
        }
        return result;
    }

    private const double AuPerLightYear = 63241.077;

    // A unit vector across the line of sight to a position, turned about it by an angle from the ID.
    private static Vector3D Across(Vector3D position, int id)
    {
        Vector3D line = position.Length > 1e-9 ? position.Normalized : new Vector3D(0, 0, 1);
        Vector3D u = Vector3D.Cross(line, new Vector3D(0, 1, 0));
        if (u.Length < 1e-6)
        {
            u = Vector3D.Cross(line, new Vector3D(1, 0, 0));
        }
        u = u.Normalized;
        Vector3D v = Vector3D.Cross(line, u).Normalized;
        double angle = Hash64.Of((ulong)id) % 3600 / 3600.0 * 2 * Math.PI;
        return u * Math.Cos(angle) + v * Math.Sin(angle);
    }

    private static Vector3D Equatorial(double raHours, double decDegrees)
    {
        double ra = raHours * 15 * Math.PI / 180;
        double dec = decDegrees * Math.PI / 180;
        return new Vector3D(Math.Cos(dec) * Math.Cos(ra), Math.Cos(dec) * Math.Sin(ra), Math.Sin(dec));
    }

    // "10:49:18.9" → hours; "-53:19:10" → degrees.
    private static double Hours(string hms) => Sexagesimal(hms);

    private static double Degrees(string dms) => Sexagesimal(dms);

    private static double Sexagesimal(string text)
    {
        string t = text.Trim();
        double sign = t.StartsWith('-') ? -1 : 1;
        string[] parts = t.TrimStart('+', '-').Split(':');
        double value = 0;
        double scale = 1;
        foreach (string part in parts)
        {
            value += Double(part) * scale;
            scale /= 60;
        }
        return sign * value;
    }

    // ---------------------------------------------------------------- sources

    public sealed class Row
    {
        public int Id;
        public int Primary;
        public int Component;
        public double DistancePc;
        public Vector3D Position;
        public double AbsMag;
        public double ColourIndex;
        public string Spectrum = "";
        public string Proper = "";
        public string Bayer = "";
        public string Flamsteed = "";
        public string Constellation = "";
        public string Gliese = "";
        public string Hd = "";
        public string Hip = "";
        public string Hr = "";
        public double Luminosity;
        public bool ValidDistance => Id == 0 || (DistancePc > 0 && DistancePc < 100000);
    }

    // Known errors in the HYG source data, keyed by HYG ID (applied while reading it).
    private static readonly Dictionary<int, string> SpectrumOverrides = new()
    {
        [24549] = "G3III", // Capella (source lists "M1: comp")
    };

    /// <summary>Reads the HYG database (v4.1 CSV): positions, magnitudes, types, names and its multiple-star grouping.</summary>
    public static Dictionary<int, Row> ReadHyg(List<Dictionary<string, string>> csv)
    {
        var rows = new Dictionary<int, Row>();
        foreach (Dictionary<string, string> r in csv)
        {
            var row = new Row
            {
                Id = Int(r["id"]),
                Primary = r["comp_primary"].Length > 0 ? Int(r["comp_primary"]) : Int(r["id"]),
                Component = r["comp"].Length > 0 ? Int(r["comp"]) : 1,
                DistancePc = Double(r["dist"]),
                AbsMag = Double(r["absmag"]),
                ColourIndex = r["ci"].Length > 0 ? Double(r["ci"]) : double.NaN,
                Spectrum = r["spect"].Trim(),
                Proper = r["proper"].Trim(),
                Bayer = r["bayer"].Trim(),
                Flamsteed = r["flam"].Trim(),
                Constellation = r["con"].Trim(),
                Gliese = Regex.Replace(r["gl"].Trim(), @"\s+", " "),
                Hd = r["hd"].Trim(),
                Hip = r["hip"].Trim(),
                Hr = r["hr"].Trim(),
                Luminosity = r["lum"].Length > 0 ? Double(r["lum"]) : double.NaN,
            };
            var eq = new Vector3D(Double(r["x"]), Double(r["y"]), Double(r["z"]));
            row.Position = GalaxyFrame.FromEquatorial(eq) * Units.LightYearsPerParsec;
            if (SpectrumOverrides.TryGetValue(row.Id, out string? fixedSpectrum))
            {
                row.Spectrum = fixedSpectrum;
            }
            rows[row.Id] = row;
        }
        return rows;
    }

    /// <summary>First ID for Tycho-2 stars of AT-HYG that HYG lacks: 2²⁴ + (TYC1 · 2¹⁴ + TYC2) · 4 + TYC3.</summary>
    public const int TychoFirstId = 1 << 24;

    /// <summary>First ID for AT-HYG stars with neither a HYG nor a Tycho-2 number: 2³⁰ + AT-HYG's serial number.</summary>
    public const int SerialFirstId = 1 << 30;

    /// <summary>
    /// Reads AT-HYG (v4.0 CSV; GALAXY.md §8.1): Gaia DR3 distances for nearly all of its 2.5 million stars, positions
    /// <c>x0, y0, z0</c> (equatorial, parsecs), <c>absmag</c>, <c>ci</c>, <c>spect</c> and HYG's names and numbers. IDs
    /// stay stable where they can: a star in HYG keeps its HYG ID (Sol stays 0), a Tycho-2 star gets
    /// <see cref="TychoFirstId"/> + its packed TYC number, any other <see cref="SerialFirstId"/> + AT-HYG's serial number
    /// (which is only stable within one AT-HYG version). AT-HYG lists no multiple-star systems: they come from the HYG
    /// CSV through the <c>hyg</c> column if it is given, else from a shared Gliese number (Gl 559 A and B), else from a
    /// shared Tycho-2 field and number (TYC1-TYC2) within an arcminute. The brightest member is the primary.
    /// </summary>
    public static Dictionary<int, Row> ReadAtHyg(List<Dictionary<string, string>> csv, List<Dictionary<string, string>>? hyg)
    {
        var rows = new Dictionary<int, Row>();
        var hygPrimary = new Dictionary<int, int>();
        var hygComponent = new Dictionary<int, int>();
        if (hyg != null)
        {
            foreach (Dictionary<string, string> r in hyg)
            {
                int id = Int(r["id"]);
                hygPrimary[id] = r["comp_primary"].Length > 0 ? Int(r["comp_primary"]) : id;
                hygComponent[id] = r["comp"].Length > 0 ? Int(r["comp"]) : 1;
            }
        }
        var tycho = new Dictionary<int, (int Field, int Number)>();
        foreach (Dictionary<string, string> r in csv)
        {
            string hygId = Field(r, "hyg");
            string tyc = Field(r, "tyc");
            int id;
            (int, int)? tychoKey = null;
            if (hygId.Length > 0)
            {
                id = Int(hygId);
            }
            else if (TryTycho(tyc, out int t1, out int t2, out int t3))
            {
                id = TychoFirstId + ((t1 << 14) + t2) * 4 + t3;
                tychoKey = (t1, t2);
            }
            else
            {
                id = SerialFirstId + Int(r["id"]);
            }
            var row = new Row
            {
                Id = id,
                Primary = id,
                Component = 1,
                DistancePc = Double(r["dist"]),
                AbsMag = Double(r["absmag"]),
                ColourIndex = Field(r, "ci").Length > 0 ? Double(r["ci"]) : double.NaN,
                Spectrum = Field(r, "spect").Trim(),
                Proper = Field(r, "proper").Trim(),
                Bayer = Field(r, "bayer").Trim(),
                Flamsteed = Field(r, "flam").Trim(),
                Constellation = Field(r, "con").Trim(),
                Gliese = Regex.Replace(Field(r, "gl").Trim(), @"\s+", " "),
                Hd = Field(r, "hd").Trim(),
                Hip = Field(r, "hip").Trim(),
                Hr = Field(r, "hr").Trim(),
                Luminosity = double.NaN,
            };
            var eq = new Vector3D(Double(r["x0"]), Double(r["y0"]), Double(r["z0"]));
            row.Position = GalaxyFrame.FromEquatorial(eq) * Units.LightYearsPerParsec;
            if (SpectrumOverrides.TryGetValue(row.Id, out string? fixedSpectrum) && hygId.Length > 0)
            {
                row.Spectrum = fixedSpectrum;
            }
            rows[row.Id] = row;
            if (tychoKey.HasValue)
            {
                tycho[row.Id] = tychoKey.Value;
            }
        }
        if (!rows.ContainsKey(0))
        {
            rows[0] = new Row { Id = 0, Primary = 0, Component = 1, AbsMag = 4.83, Spectrum = "G2V", Proper = "Sol" };
        }

        // Multiple stars: HYG's grouping where both stars are in HYG, then Gliese numbers, then Tycho-2 components.
        var groups = new Dictionary<string, List<Row>>();
        foreach (Row row in rows.Values)
        {
            string? key = null;
            if (hygPrimary.TryGetValue(row.Id, out int primary) && primary != row.Id && rows.ContainsKey(primary))
            {
                row.Primary = primary;
                row.Component = hygComponent[row.Id];
                continue;
            }
            Match gl = Regex.Match(row.Gliese, @"^(?<base>(Gl|GJ|NN|Wo)\s*[\d.]+)\s*(?<comp>[A-Z])$");
            if (gl.Success && !hygPrimary.ContainsKey(row.Id))
            {
                key = "gl:" + gl.Groups["base"].Value.Replace(" ", "", StringComparison.Ordinal);
            }
            else if (tycho.TryGetValue(row.Id, out (int Field, int Number) t))
            {
                key = $"tyc:{t.Field}-{t.Number}";
            }
            if (key != null)
            {
                if (!groups.TryGetValue(key, out List<Row>? list))
                {
                    list = new List<Row>();
                    groups[key] = list;
                }
                list.Add(row);
            }
        }
        foreach (List<Row> group in groups.Values.Where(g => g.Count > 1))
        {
            group.Sort((a, b) => a.AbsMag.CompareTo(b.AbsMag));
            Row brightest = group[0];
            int component = 2;
            foreach (Row member in group.Skip(1))
            {
                double angle = Math.Acos(Math.Clamp(Vector3D.Dot(member.Position.Normalized, brightest.Position.Normalized), -1, 1));
                if (angle < Math.PI / 180 / 60 || member.Gliese.Length > 0)
                {
                    member.Primary = brightest.Id;
                    member.Component = component++;
                }
            }
        }
        return rows;
    }

    private static string Field(Dictionary<string, string> row, string column) =>
        row.TryGetValue(column, out string? value) ? value : "";

    // "1234-567-1" (leading zeros dropped in AT-HYG) → (1234, 567, 1).
    private static bool TryTycho(string tyc, out int field, out int number, out int component)
    {
        field = number = component = 0;
        string[] parts = tyc.Trim().Split('-');
        return parts.Length == 3 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out field)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out component)
            && field is > 0 and < 16384 && number is > 0 and < 16384 && component is >= 1 and <= 3;
    }

    /// <summary>
    /// Turns source rows into catalogue stars: primaries where the generator leaves their level to the catalogue (all
    /// with <paramref name="keepAll"/>), each followed by its companions. Companions with no usable distance (or an
    /// inconsistent one) sit next to their primary.
    /// </summary>
    public static List<CatalogueStar> Assemble(Dictionary<int, Row> rows, bool keepAll)
    {
        // Which rows are primaries, and which companions belong to each.
        var companions = new Dictionary<int, List<Row>>();
        foreach (Row row in rows.Values)
        {
            if (row.Primary != row.Id && rows.ContainsKey(row.Primary))
            {
                if (!companions.TryGetValue(row.Primary, out List<Row>? list))
                {
                    list = new List<Row>();
                    companions[row.Primary] = list;
                }
                list.Add(row);
            }
        }

        var result = new List<CatalogueStar>();
        foreach (Row row in rows.Values.OrderBy(r => r.Id))
        {
            bool isPrimary = row.Primary == row.Id || !rows.ContainsKey(row.Primary);
            if (!isPrimary || !row.ValidDistance)
            {
                continue;
            }
            // Where the catalogue counts for this level (Finish applies the same rule to every star).
            double distance = row.Position.Length;
            int level = StarClassTable.LevelForMagnitude((float)row.AbsMag); // as stored, so Finish agrees
            if (!keepAll && row.Id != 0 && CatalogueCompleteness.Of(level, distance) <= 0)
            {
                continue;
            }
            List<Row> members = companions.TryGetValue(row.Id, out List<Row>? c) ? c : new List<Row>();
            string systemName = SystemName(row, members.Count > 0);
            result.Add(MakeStar(row, row.Position, -1, StarName(row), systemName));
            foreach (Row member in members.OrderBy(m => m.Component).ThenBy(m => m.Id))
            {
                Vector3D position = member.ValidDistance && Vector3D.Distance(member.Position, row.Position) < 1.0
                    ? member.Position
                    : row.Position + new Vector3D(0.0005 * member.Component, 0, 0);
                string name = member.Proper.Length > 0 ? member.Proper : systemName + " " + ComponentLetter(member.Component);
                result.Add(MakeStar(member, position, row.Id, name, ""));
            }
        }
        return result;
    }

    private static CatalogueStar MakeStar(Row row, Vector3D position, int primaryId, string name, string systemName)
    {
        (StarKind kind, double code) = ParseSpectrum(row.Spectrum, row.AbsMag, row.ColourIndex);
        double teff;
        double mass;
        double radius;
        switch (kind)
        {
            case StarKind.WhiteDwarf:
                teff = 50400.0 / Math.Max(code, 0.5);
                mass = 0.6;
                radius = 0.0126;
                break;
            default:
            {
                (double t, double _, double m, double r) = StellarTables.Typical(kind, code);
                teff = t;
                mass = m;
                radius = r;
                break;
            }
        }
        if (row.Id == 0)
        {
            teff = 5772;
            mass = 1;
            radius = 1;
        }
        double luminosity = double.IsNaN(row.Luminosity) ? StellarTables.Luminosity(radius, teff) : row.Luminosity;
        var aliases = new List<string>();
        foreach (string alias in AllNames(row))
        {
            if (!alias.Equals(name, StringComparison.OrdinalIgnoreCase)
                && !alias.Equals(systemName, StringComparison.OrdinalIgnoreCase)
                && !aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                aliases.Add(alias);
            }
        }
        return new CatalogueStar
        {
            Id = row.Id,
            PrimaryId = primaryId,
            Position = position,
            Class = new StarClass(kind, (float)code),
            Physics = new StarPhysics((float)teff, (float)row.AbsMag, (float)mass, (float)radius, (float)luminosity),
            Name = name,
            SystemName = systemName,
            Aliases = aliases.ToArray(),
        };
    }

    private static readonly Regex SpectralPattern = new(@"(?<pre>esd|usd|sd|d|g|c)?(?<cls>[OBAFGKMLTY])(?<sub>\d+(?:\.\d+)?)?", RegexOptions.Compiled);
    private static readonly Regex LuminosityPattern = new(@"(Iab|Ia0|Ia|Ib|III|II|IV|VI|V|I)", RegexOptions.Compiled);

    /// <summary>Parses a catalogue spectral type into a kind and spectral code (see <see cref="StarClass"/>).</summary>
    public static (StarKind Kind, double Code) ParseSpectrum(string spectrum, double absMag, double colourIndex)
    {
        string s = spectrum.Trim();
        if (s.Length >= 2 && s[0] == 'D' && "ABOQZCX0123456789".Contains(s[1], StringComparison.Ordinal))
        {
            Match digits = Regex.Match(s, @"\d+(\.\d+)?");
            double index = digits.Success ? Double(digits.Value) : 5.0;
            return (StarKind.WhiteDwarf, Math.Clamp(index, 0.5, 15));
        }
        if (s.Length >= 2 && s[0] == 'W' && (s[1] == 'N' || s[1] == 'C' || s[1] == 'O' || s[1] == 'R'))
        {
            return (StarKind.WolfRayet, 3);
        }
        if (s.Length >= 1 && (s[0] == 'C' || s[0] == 'R' || s[0] == 'N') && (s.Length == 1 || !char.IsLower(s[1])))
        {
            return (StarKind.CarbonStar, 67);
        }
        if (s.Length >= 1 && s[0] == 'S' && (s.Length == 1 || char.IsDigit(s[1])))
        {
            return (StarKind.Giant, 65);
        }

        Match m = SpectralPattern.Match(s);
        double code;
        string? luminosityClass = null;
        if (m.Success)
        {
            int letter = StarClass.LetterIndex(m.Groups["cls"].Value[0]);
            double sub = m.Groups["sub"].Success ? Double(m.Groups["sub"].Value) : 5.0;
            code = letter * 10 + Math.Min(sub, 9.5);
            string prefix = m.Groups["pre"].Value;
            luminosityClass = prefix switch
            {
                "sd" or "esd" or "usd" => "VI",
                "d" => "V",
                "g" => "III",
                "c" => "I",
                _ => null,
            };
            if (luminosityClass == null)
            {
                Match lc = LuminosityPattern.Match(s, m.Index + m.Length);
                if (lc.Success)
                {
                    luminosityClass = lc.Value;
                }
            }
        }
        else if (!double.IsNaN(colourIndex))
        {
            double ci = Math.Clamp(colourIndex, -0.4, 2.0);
            double teff = 4600 * (1 / (0.92 * ci + 1.7) + 1 / (0.92 * ci + 0.62));
            code = StellarTables.MainSequenceCodeForTemperature(teff);
        }
        else
        {
            code = 45;
        }

        if (code >= 70)
        {
            return (StarKind.BrownDwarf, code);
        }
        StarKind kind = luminosityClass switch
        {
            "V" or "VI" => StarKind.MainSequence,
            "IV" => StarKind.Subgiant,
            "III" => StarKind.Giant,
            "II" => StarKind.BrightGiant,
            "I" or "Ia" or "Ib" or "Iab" or "Ia0" => StarKind.Supergiant,
            _ => InferKind(code, absMag),
        };
        return (kind, code);
    }

    private static StarKind InferKind(double code, double absMag)
    {
        double ms = StellarTables.MainSequenceMagnitude(code);
        if (absMag < ms - 2.0)
        {
            return absMag < -4.0 ? StarKind.Supergiant : StarKind.Giant;
        }
        return StarKind.MainSequence;
    }

    // ---------------------------------------------------------------- names

    private static string StarName(Row row) => AllNames(row).FirstOrDefault() ?? $"HYG {row.Id}";

    private static string SystemName(Row row, bool hasCompanions)
    {
        if (row.Id == 0)
        {
            return "Sol";
        }
        if (hasCompanions && row.Bayer.Contains('-', StringComparison.Ordinal) && Genitives.ContainsKey(row.Constellation))
        {
            string greek = row.Bayer.Split('-')[0];
            if (Greek.TryGetValue(greek, out string? full))
            {
                return full + " " + Genitives[row.Constellation];
            }
        }
        return StarName(row);
    }

    private static IEnumerable<string> AllNames(Row row)
    {
        if (row.Proper.Length > 0)
        {
            yield return row.Proper;
        }
        if (row.Bayer.Length > 0 && Genitives.TryGetValue(row.Constellation, out string? genitive))
        {
            string[] parts = row.Bayer.Split('-');
            if (Greek.TryGetValue(parts[0], out string? greek))
            {
                string suffix = parts.Length > 1 ? parts[1] : "";
                yield return greek + suffix + " " + genitive;
                yield return parts[0] + suffix + " " + row.Constellation;
            }
        }
        if (row.Flamsteed.Length > 0 && Genitives.TryGetValue(row.Constellation, out string? g2))
        {
            yield return row.Flamsteed + " " + g2;
        }
        if (row.Gliese.Length > 0)
        {
            yield return GlieseName(row.Gliese);
        }
        if (row.Hd.Length > 0)
        {
            yield return "HD " + row.Hd;
        }
        if (row.Hip.Length > 0)
        {
            yield return "HIP " + row.Hip;
        }
        if (row.Hr.Length > 0)
        {
            yield return "HR " + row.Hr;
        }
    }

    private static string GlieseName(string gl)
    {
        // "Gl 559A" → "Gliese 559 A"; "GJ 1002" and "NN 3618" stay as they are.
        Match m = Regex.Match(gl, @"^Gl\s*(?<num>[\d.]+)\s*(?<comp>[A-Z]*)$");
        if (!m.Success)
        {
            return gl;
        }
        string comp = m.Groups["comp"].Value;
        return "Gliese " + m.Groups["num"].Value + (comp.Length > 0 ? " " + comp : "");
    }

    private static string ComponentLetter(int component) => ((char)('A' + Math.Clamp(component - 1, 0, 25))).ToString();

    private static readonly Dictionary<string, string> Greek = new()
    {
        ["Alp"] = "Alpha", ["Bet"] = "Beta", ["Gam"] = "Gamma", ["Del"] = "Delta", ["Eps"] = "Epsilon", ["Zet"] = "Zeta",
        ["Eta"] = "Eta", ["The"] = "Theta", ["Iot"] = "Iota", ["Kap"] = "Kappa", ["Lam"] = "Lambda", ["Mu"] = "Mu",
        ["Nu"] = "Nu", ["Xi"] = "Xi", ["Omi"] = "Omicron", ["Pi"] = "Pi", ["Rho"] = "Rho", ["Sig"] = "Sigma",
        ["Tau"] = "Tau", ["Ups"] = "Upsilon", ["Phi"] = "Phi", ["Chi"] = "Chi", ["Psi"] = "Psi", ["Ome"] = "Omega",
    };

    private static readonly Dictionary<string, string> Genitives = new()
    {
        ["And"] = "Andromedae", ["Ant"] = "Antliae", ["Aps"] = "Apodis", ["Aqr"] = "Aquarii", ["Aql"] = "Aquilae",
        ["Ara"] = "Arae", ["Ari"] = "Arietis", ["Aur"] = "Aurigae", ["Boo"] = "Bootis", ["Cae"] = "Caeli",
        ["Cam"] = "Camelopardalis", ["Cnc"] = "Cancri", ["CVn"] = "Canum Venaticorum", ["CMa"] = "Canis Majoris",
        ["CMi"] = "Canis Minoris", ["Cap"] = "Capricorni", ["Car"] = "Carinae", ["Cas"] = "Cassiopeiae", ["Cen"] = "Centauri",
        ["Cep"] = "Cephei", ["Cet"] = "Ceti", ["Cha"] = "Chamaeleontis", ["Cir"] = "Circini", ["Col"] = "Columbae",
        ["Com"] = "Comae Berenices", ["CrA"] = "Coronae Australis", ["CrB"] = "Coronae Borealis", ["Crv"] = "Corvi",
        ["Crt"] = "Crateris", ["Cru"] = "Crucis", ["Cyg"] = "Cygni", ["Del"] = "Delphini", ["Dor"] = "Doradus",
        ["Dra"] = "Draconis", ["Equ"] = "Equulei", ["Eri"] = "Eridani", ["For"] = "Fornacis", ["Gem"] = "Geminorum",
        ["Gru"] = "Gruis", ["Her"] = "Herculis", ["Hor"] = "Horologii", ["Hya"] = "Hydrae", ["Hyi"] = "Hydri",
        ["Ind"] = "Indi", ["Lac"] = "Lacertae", ["Leo"] = "Leonis", ["LMi"] = "Leonis Minoris", ["Lep"] = "Leporis",
        ["Lib"] = "Librae", ["Lup"] = "Lupi", ["Lyn"] = "Lyncis", ["Lyr"] = "Lyrae", ["Men"] = "Mensae",
        ["Mic"] = "Microscopii", ["Mon"] = "Monocerotis", ["Mus"] = "Muscae", ["Nor"] = "Normae", ["Oct"] = "Octantis",
        ["Oph"] = "Ophiuchi", ["Ori"] = "Orionis", ["Pav"] = "Pavonis", ["Peg"] = "Pegasi", ["Per"] = "Persei",
        ["Phe"] = "Phoenicis", ["Pic"] = "Pictoris", ["Psc"] = "Piscium", ["PsA"] = "Piscis Austrini", ["Pup"] = "Puppis",
        ["Pyx"] = "Pyxidis", ["Ret"] = "Reticuli", ["Sge"] = "Sagittae", ["Sgr"] = "Sagittarii", ["Sco"] = "Scorpii",
        ["Scl"] = "Sculptoris", ["Sct"] = "Scuti", ["Ser"] = "Serpentis", ["Sex"] = "Sextantis", ["Tau"] = "Tauri",
        ["Tel"] = "Telescopii", ["Tri"] = "Trianguli", ["TrA"] = "Trianguli Australis", ["Tuc"] = "Tucanae",
        ["UMa"] = "Ursae Majoris", ["UMi"] = "Ursae Minoris", ["Vel"] = "Velorum", ["Vir"] = "Virginis",
        ["Vol"] = "Volantis", ["Vul"] = "Vulpeculae",
    };

    // ---------------------------------------------------------------- CSV

    public static List<Dictionary<string, string>> ReadCsv(string path)
    {
        var rows = new List<Dictionary<string, string>>();
        using var reader = new StreamReader(path, Encoding.UTF8);
        string? header = reader.ReadLine();
        if (header == null)
        {
            return rows;
        }
        List<string> columns = SplitCsvLine(header);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0)
            {
                continue;
            }
            List<string> fields = SplitCsvLine(line);
            var row = new Dictionary<string, string>(columns.Count);
            for (int i = 0; i < columns.Count; i++)
            {
                row[columns[i]] = i < fields.Count ? fields[i] : "";
            }
            rows.Add(row);
        }
        return rows;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch == ',')
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static int Int(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static double Double(string s) =>
        s.Length == 0 ? double.NaN : double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
