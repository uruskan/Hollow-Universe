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
/// Converts the HYG v4.1 star database (CSV) into Hollow Universe's compact catalogue (GALAXY.md §8).
/// Usage: dotnet run --project tools/Hollow.CatalogBuilder -- path/to/hygdata_v41.csv game/data/catalog/hyg41.bin.gz
/// The CSV is not stored in the repository; get it from https://github.com/astronexus/HYG-Database (CC BY-SA 4.0).
/// <c>--update in.bin.gz out.bin.gz</c> re-applies the rules that don't need the CSV (which stars to keep) to a
/// catalogue built earlier, so a rule change doesn't need the source data.
/// </summary>
public static class Program
{
    private const int CatalogueNumber = 0; // HYG 4.1

    public static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--update")
        {
            StarCatalogue built = StarCatalogue.Load(args[1]);
            List<CatalogueStar> kept = Finish(built.Stars);
            Write(args[2], built.Number, kept);
            Console.WriteLine($"Kept {kept.Count} of {built.Stars.Count} stars.");
            return 0;
        }
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: Hollow.CatalogBuilder <hygdata_v41.csv> <output.bin.gz>");
            Console.Error.WriteLine("       Hollow.CatalogBuilder --update <input.bin.gz> <output.bin.gz>");
            return 2;
        }
        List<Dictionary<string, string>> rows = ReadCsv(args[0]);
        Console.WriteLine($"Read {rows.Count} rows.");
        List<CatalogueStar> stars = Finish(Build(rows));
        Write(args[1], CatalogueNumber, stars);
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

    /// <summary>
    /// The rules applied to every built catalogue, from the CSV or from an earlier file: keep exactly the systems
    /// the generator leaves to the catalogue, i.e. where <see cref="CatalogueCompleteness"/> is above zero for the
    /// primary's level and distance (GALAXY.md §8.3). Companions follow their primary.
    /// </summary>
    public static List<CatalogueStar> Finish(IReadOnlyList<CatalogueStar> stars)
    {
        var keptPrimaries = new HashSet<int>();
        foreach (CatalogueStar s in stars)
        {
            if (s.PrimaryId < 0 && Catalogued(s))
            {
                keptPrimaries.Add(s.Id);
            }
        }
        var kept = new List<CatalogueStar>();
        foreach (CatalogueStar s in stars)
        {
            if (keptPrimaries.Contains(s.PrimaryId < 0 ? s.Id : s.PrimaryId))
            {
                kept.Add(s);
            }
        }
        return kept;
    }

    private static bool Catalogued(CatalogueStar primary) =>
        primary.Id == 0 || CatalogueCompleteness.Of(StarClassTable.LevelForMagnitude(primary.Physics.AbsoluteMagnitude), primary.Position.Length) > 0;

    private sealed class Row
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

    // Known errors in the source data, keyed by HYG ID.
    private static readonly Dictionary<int, string> SpectrumOverrides = new()
    {
        [24549] = "G3III", // Capella (source lists "M1: comp")
    };

    public static List<CatalogueStar> Build(List<Dictionary<string, string>> csv)
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
            if (row.Id != 0 && CatalogueCompleteness.Of(level, distance) <= 0)
            {
                continue;
            }
            List<Row> members = companions.TryGetValue(row.Id, out List<Row>? c) ? c : new List<Row>();
            string systemName = SystemName(row, members.Count > 0);
            result.Add(MakeStar(row, row.Position, -1, StarName(row), systemName));
            foreach (Row member in members.OrderBy(m => m.Component).ThenBy(m => m.Id))
            {
                // Companions with no usable distance (or an inconsistent one) sit next to their primary.
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
