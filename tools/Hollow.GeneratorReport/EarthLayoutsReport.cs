using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Surface;

namespace Hollow.GeneratorReport;

/// <summary>
/// Earth's candidate layouts (docs/WORLD_TERRAIN_PLAN.md §7a, step 1): each layout variant baked, the cliff kit placed on
/// it and its flat sites found, drawn as an equal-area relief map with the kit's pieces as dark red dots, the flat sites
/// as circles (magenta: a flat circle of 150 m or more; yellow: 75 m or more) and the landing site as a white cross; a
/// contact sheet of them all, numbered; and each one's numbers. Production Earth is not changed.
/// <c>earth layouts &lt;folder&gt; [--variants 0-11 | 1,4,5] [--width 1600]</c>
/// </summary>
internal static class EarthLayoutsReport
{
    private const double LargeSite = 150, MediumSite = 75, SmallSite = 40; // flat circles' radii, metres
    private const double FlatSlope = 0.09, FlatAbove = 2;                   // TerrainBake.FlatSites' settings

    private static readonly (double R, double G, double B) CliffColour = (128, 12, 20);
    private static readonly (double R, double G, double B) LargeColour = (236, 40, 210);
    private static readonly (double R, double G, double B) MediumColour = (255, 222, 40);

    private sealed record Layout(
        int Variant, double LandShare, int Lakes, double LakeHectares, double Plains, double Hills, double Tableland,
        double Mountains, double Highest, double ConnectedLand, int CliffPieces, int Nordic, int Desert, int Tundra,
        int LargeSites, int MediumSites, int SmallSites, double[] LargestSites, string Landing, double BakeSeconds,
        bool BakeCached);

    // What a map's pixels say about the world: each pixel inside the ellipse is the same area.
    private struct Tally
    {
        public int Pixels, Land, Lake;
        public double Plains, Hills, Tableland, Mountains, Highest;
    }

    private readonly record struct Piece(Vector3D Position, string Family);

    public static int Run(string[] args)
    {
        if (args.Length < 3 || args[2].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("usage: earth layouts <folder> [--variants 0-11 | 1,4,5] [--width 1600]");
            return 2;
        }
        string folder = Path.GetFullPath(args[2]);
        int[] variants = Enumerable.Range(0, 12).ToArray();
        int width = 1600;
        for (int i = 3; i < args.Length - 1; i++)
        {
            if (args[i] == "--variants")
            {
                variants = ParseVariants(args[i + 1]);
            }
            else if (args[i] == "--width")
            {
                width = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
            }
        }
        if (width is < 400 or > 4096 || width % 4 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(args), "The map's width: 400 to 4096 pixels, a multiple of 4.");
        }
        Directory.CreateDirectory(folder);
        Console.WriteLine($"Earth's layouts {string.Join(", ", variants)} (production: {WalkingParameters.Earth.LayoutVariant}), maps {width} × {width / 2}, into {folder}");

        var watch = Stopwatch.StartNew();
        var results = new Layout[variants.Length];
        var thumbs = new byte[variants.Length][];
        int thumbWidth = width / 2;
        // Three at a time: a bake's flood is one thread, its other passes all of them.
        Parallel.For(0, variants.Length, new ParallelOptions { MaxDegreeOfParallelism = 3 }, k =>
        {
            (results[k], thumbs[k]) = One(variants[k], folder, width, thumbWidth);
            Console.WriteLine($"  layout {variants[k]} done at {watch.Elapsed.TotalSeconds:0} s");
        });

        WriteSheet(Path.Combine(folder, "contact-sheet.png"), results, thumbs, thumbWidth);
        File.WriteAllText(Path.Combine(folder, "layouts.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        string table = Table(results);
        File.WriteAllText(Path.Combine(folder, "layouts.txt"), table);
        Console.WriteLine(table);
        Console.WriteLine($"Done in {watch.Elapsed.TotalSeconds:0} s: contact-sheet.png, layout-NN.png, layouts.txt and layouts.json in {folder}");
        return 0;
    }

    private static int[] ParseVariants(string text)
    {
        var list = new List<int>();
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] range = part.Split('-');
            int from = int.Parse(range[0], CultureInfo.InvariantCulture);
            int to = range.Length > 1 ? int.Parse(range[1], CultureInfo.InvariantCulture) : from;
            for (int v = from; v <= to; v++)
            {
                list.Add(v);
            }
        }
        return list.Distinct().ToArray();
    }

    // One layout: baked (or loaded), its kit and flat sites, its map and its thumbnail for the sheet.
    private static (Layout, byte[]) One(int variant, string folder, int width, int thumbWidth)
    {
        var (system, body) = StartingWorld.Find();
        WalkingParameters parameters = WalkingReport.LookDev(WalkingParameters.Earth with { LayoutVariant = variant });
        WalkingWorld world = WalkingWorld.For(system, body, GameScale.PlanetRadius(body), parameters);
        TerrainBake bake = world.Bake ?? throw new InvalidOperationException("Earth is not baked (WalkingParameters.Baked).");

        var pieces = new List<Piece>();
        if (world.KitCliffs is { } kit)
        {
            long cells = TileKey.CountAt(kit.Level);
            for (long i = 0; i < cells; i++)
            {
                foreach (KitPlacement k in kit.At(TileKey.FromIndex(kit.Level, i)!.Value))
                {
                    pieces.Add(new Piece(k.Position, kit.Kit.Pieces[k.Piece].Family));
                }
            }
        }
        List<FlatSite> sites = bake.FlatSites(FlatSlope, FlatAbove).Where(s => s.Radius >= SmallSite).ToList();

        int height = width / 2;
        (byte[] map, Tally tally) = Relief(world, width);
        byte[] thumb = Downsample(map, width, height, width / thumbWidth);
        Overlay(world, map, width, pieces, sites, 1.0);
        Overlay(world, thumb, thumbWidth, pieces, sites, (double)thumbWidth / width);
        Png.Write(Path.Combine(folder, $"layout-{variant:00}.png"), width, height, map);

        double lakeHectares = bake.Lakes.Sum(l => l.Area) / 10_000;
        double weights = tally.Plains + tally.Hills + tally.Tableland + tally.Mountains;
        Vector3D landing = world.LandingSite;
        var layout = new Layout(variant,
            LandShare: (double)tally.Land / tally.Pixels,
            Lakes: bake.Lakes.Count,
            LakeHectares: Math.Round(lakeHectares, 1),
            Plains: tally.Plains / weights,
            Hills: tally.Hills / weights,
            Tableland: tally.Tableland / weights,
            Mountains: tally.Mountains / weights,
            Highest: Math.Round(tally.Highest, 1),
            ConnectedLand: EarthCandidatesReport.Connectivity(world),
            CliffPieces: pieces.Count,
            Nordic: pieces.Count(p => p.Family == "nordic"),
            Desert: pieces.Count(p => p.Family == "desert"),
            Tundra: pieces.Count(p => p.Family == "tundra"),
            LargeSites: sites.Count(s => s.Radius >= LargeSite),
            MediumSites: sites.Count(s => s.Radius >= MediumSite && s.Radius < LargeSite),
            SmallSites: sites.Count(s => s.Radius >= SmallSite && s.Radius < MediumSite),
            LargestSites: sites.Take(6).Select(s => Math.Round(s.Radius)).ToArray(),
            Landing: At(landing),
            BakeSeconds: Math.Round(bake.Time.TotalSeconds, 1),
            BakeCached: bake.FromCache);
        return (layout, thumb);
    }

    // ---------------------------------------------------------------- the map

    // The relief, coloured by height and lit from the north-west, on an equal-area (Mollweide) map, and what its pixels
    // count: land, lakes, the relief types' weights on land, the highest ground.
    private static (byte[] Rgb, Tally Tally) Relief(WalkingWorld world, int width)
    {
        int height = width / 2;
        var rgb = new byte[width * height * 3];
        var rows = new Tally[height];
        double step = 2 * Math.PI * world.Radius / width * 0.8;
        Parallel.For(0, height, y =>
        {
            Tally t = default;
            for (int x = 0; x < width; x++)
            {
                int k = (y * width + x) * 3;
                if (!WalkingReport.MollweideInverse(x, y, width, height, 0, out double lat, out double lon))
                {
                    rgb[k] = rgb[k + 1] = rgb[k + 2] = 250;
                    continue;
                }
                Vector3D d = ZoneFrame.Direction(lat, lon);
                WalkingSample s = world.Fields(d);
                t.Pixels++;
                (double r, double g, double b) colour;
                if (s.Height < 0 && !(s.Water > 0))
                {
                    colour = WalkingReport.Water(s.Water - s.Height, plain: false);
                }
                else if (s.Ice > 0 || s.Wetted)
                {
                    t.Lake++;
                    colour = s.Ice > 0 ? (226, 234, 242) : (52, 118, 196);
                }
                else
                {
                    t.Land++;
                    t.Plains += s.Plains;
                    t.Hills += s.Hills;
                    t.Tableland += s.Tableland;
                    t.Mountains += s.Mountains;
                    t.Highest = Math.Max(t.Highest, s.Height);
                    colour = WalkingReport.Hypsometric(s.Height);
                }
                double light = WalkingReport.Light(world, d, step);
                if (s.Height < 0)
                {
                    light = 1 + (light - 1) * 0.25;
                }
                rgb[k] = Byte(colour.r * light);
                rgb[k + 1] = Byte(colour.g * light);
                rgb[k + 2] = Byte(colour.b * light);
            }
            rows[y] = t;
        });
        Tally total = default;
        foreach (Tally t in rows)
        {
            total.Pixels += t.Pixels;
            total.Land += t.Land;
            total.Lake += t.Lake;
            total.Plains += t.Plains;
            total.Hills += t.Hills;
            total.Tableland += t.Tableland;
            total.Mountains += t.Mountains;
            total.Highest = Math.Max(total.Highest, t.Highest);
        }
        return (rgb, total);
    }

    // The kit's pieces as dark red dots, the flat sites as circles at their true size, the landing site as a white cross.
    // `scale` is the map's width over the full map's, so marks keep their size in metres and stay legible.
    private static void Overlay(WalkingWorld world, byte[] rgb, int width, List<Piece> pieces, List<FlatSite> sites, double scale)
    {
        int height = width / 2;
        (double X, double Y) P(Vector3D d)
        {
            (double lat, double lon) = LatLon(d);
            return WalkingReport.MollweideForward(lat, lon, width, height, 0);
        }
        // Lighter on the small map, where the chains crowd together and would hide the relief.
        double dot = Math.Max(0.75, 1.2 * scale), opacity = scale < 1 ? 0.6 : 0.9;
        foreach (Piece piece in pieces)
        {
            (double x, double y) = P(piece.Position.Normalized);
            Disc(rgb, width, height, x, y, dot, CliffColour, opacity);
        }
        double line = Math.Max(0.9, 1.5 * scale);
        foreach (FlatSite site in sites.Where(s => s.Radius >= MediumSite).OrderBy(s => s.Radius))
        {
            var colour = site.Radius >= LargeSite ? LargeColour : MediumColour;
            Ring(rgb, width, height, site.Centre, site.Radius / world.Radius, P, line + 0.9, (20, 20, 20), 0.55);
            Ring(rgb, width, height, site.Centre, site.Radius / world.Radius, P, line, colour, 1);
        }
        (double lx, double ly) = P(world.LandingSite);
        double arm = Math.Max(4, 9 * scale);
        Cross(rgb, width, height, lx, ly, arm, line + 1.2, (0, 0, 0));
        Cross(rgb, width, height, lx, ly, arm, line, (255, 255, 255));
    }

    // A circle on the sphere (angular radius `angle`) drawn through the projection, its segments skipping the map's seam.
    private static void Ring(byte[] rgb, int width, int height, Vector3D centre, double angle,
        Func<Vector3D, (double X, double Y)> project, double radius, (double, double, double) colour, double alpha)
    {
        Vector3D e1 = Vector3D.Cross(Math.Abs(centre.Y) < 0.9 ? Vector3D.UnitY : Vector3D.UnitX, centre).Normalized;
        Vector3D e2 = Vector3D.Cross(centre, e1);
        const int Steps = 120;
        (double X, double Y) previous = default;
        for (int i = 0; i <= Steps; i++)
        {
            double a = 2 * Math.PI * i / Steps;
            Vector3D p = (centre * Math.Cos(angle) + (e1 * Math.Cos(a) + e2 * Math.Sin(a)) * Math.Sin(angle)).Normalized;
            (double X, double Y) q = project(p);
            if (i > 0 && Math.Abs(q.X - previous.X) < width / 4.0)
            {
                Segment(rgb, width, height, previous, q, radius, colour, alpha);
            }
            previous = q;
        }
    }

    private static void Cross(byte[] rgb, int width, int height, double x, double y, double arm, double radius, (double, double, double) colour)
    {
        Segment(rgb, width, height, (x - arm, y - arm), (x + arm, y + arm), radius, colour, 1);
        Segment(rgb, width, height, (x - arm, y + arm), (x + arm, y - arm), radius, colour, 1);
    }

    private static void Segment(byte[] rgb, int width, int height, (double X, double Y) a, (double X, double Y) b,
        double radius, (double, double, double) colour, double alpha)
    {
        double length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        int steps = Math.Max(1, (int)Math.Ceiling(length * 2));
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            Disc(rgb, width, height, a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, radius, colour, alpha);
        }
    }

    // A soft-edged disc blended over the picture; overlapping discs along a line add no darker beads than one.
    private static void Disc(byte[] rgb, int width, int height, double cx, double cy, double radius, (double R, double G, double B) c, double alpha)
    {
        int x0 = (int)Math.Floor(cx - radius - 1), x1 = (int)Math.Ceiling(cx + radius + 1);
        int y0 = (int)Math.Floor(cy - radius - 1), y1 = (int)Math.Ceiling(cy + radius + 1);
        for (int y = Math.Max(0, y0); y <= Math.Min(height - 1, y1); y++)
        {
            for (int x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++)
            {
                double dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                double a = Math.Clamp(radius + 0.5 - dist, 0, 1) * alpha;
                if (a <= 0)
                {
                    continue;
                }
                int k = (y * width + x) * 3;
                rgb[k] = Byte(rgb[k] + (c.R - rgb[k]) * a);
                rgb[k + 1] = Byte(rgb[k + 1] + (c.G - rgb[k + 1]) * a);
                rgb[k + 2] = Byte(rgb[k + 2] + (c.B - rgb[k + 2]) * a);
            }
        }
    }

    private static byte[] Downsample(byte[] rgb, int width, int height, int factor)
    {
        int w = width / factor, h = height / factor;
        var result = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < 3; c++)
                {
                    int sum = 0;
                    for (int j = 0; j < factor; j++)
                    {
                        for (int i = 0; i < factor; i++)
                        {
                            sum += rgb[((y * factor + j) * width + x * factor + i) * 3 + c];
                        }
                    }
                    result[(y * w + x) * 3 + c] = (byte)(sum / (factor * factor));
                }
            }
        }
        return result;
    }

    // ---------------------------------------------------------------- the contact sheet

    private static void WriteSheet(string path, Layout[] layouts, byte[][] thumbs, int thumbWidth)
    {
        const int Columns = 3, Gutter = 16, Header = 44, Caption = 46;
        int thumbHeight = thumbWidth / 2;
        int rows = (layouts.Length + Columns - 1) / Columns;
        int width = Columns * thumbWidth + (Columns + 1) * Gutter;
        int height = Header + rows * (thumbHeight + Caption) + (rows + 1) * Gutter;
        var rgb = new byte[width * height * 3];
        Array.Fill(rgb, (byte)24);
        Text(rgb, width, Gutter, 14, 2, "EARTH LAYOUTS - DARK RED: CLIFF KIT PIECES - MAGENTA: FLAT CIRCLE 150 M+ - YELLOW: 75 M+ - WHITE X: LANDING SITE", (230, 230, 230));
        for (int n = 0; n < layouts.Length; n++)
        {
            int ox = Gutter + (n % Columns) * (thumbWidth + Gutter);
            int oy = Header + Gutter + (n / Columns) * (thumbHeight + Caption + Gutter);
            byte[] thumb = thumbs[n];
            for (int y = 0; y < thumbHeight; y++)
            {
                Buffer.BlockCopy(thumb, y * thumbWidth * 3, rgb, ((oy + y) * width + ox) * 3, thumbWidth * 3);
            }
            Layout l = layouts[n];
            string number = l.Variant.ToString(CultureInfo.InvariantCulture);
            bool production = l.Variant == WalkingParameters.Earth.LayoutVariant;
            Box(rgb, width, ox, oy, 12 + number.Length * 36 + (production ? 100 : 0), 56, (16, 16, 16));
            Text(rgb, width, ox + 8, oy + 7, 6, number, (255, 255, 255));
            if (production)
            {
                Text(rgb, width, ox + 12 + number.Length * 36, oy + 22, 2, "TODAY", (255, 222, 40));
            }
            string line1 = string.Create(CultureInfo.InvariantCulture,
                $"LAND {100 * l.LandShare:0}%  MOUNTAINS {100 * l.Mountains:0}%  TABLELAND {100 * l.Tableland:0}%  HIGHEST {l.Highest:0} M  LAKES {l.Lakes}");
            string line2 = string.Create(CultureInfo.InvariantCulture,
                $"CLIFFS {l.CliffPieces}  FLAT 150M+ {l.LargeSites}  75M+ {l.MediumSites}  40M+ {l.SmallSites}  ONE LANDMASS {100 * l.ConnectedLand:0}%");
            Text(rgb, width, ox, oy + thumbHeight + 8, 2, line1, (225, 225, 225));
            Text(rgb, width, ox, oy + thumbHeight + 26, 2, line2, (225, 225, 225));
        }
        Png.Write(path, width, height, rgb);
    }

    private static void Box(byte[] rgb, int width, int x0, int y0, int w, int h, (byte R, byte G, byte B) c)
    {
        int height = rgb.Length / 3 / width;
        for (int y = Math.Max(0, y0); y < Math.Min(height, y0 + h); y++)
        {
            for (int x = Math.Max(0, x0); x < Math.Min(width, x0 + w); x++)
            {
                int k = (y * width + x) * 3;
                rgb[k] = c.R;
                rgb[k + 1] = c.G;
                rgb[k + 2] = c.B;
            }
        }
    }

    // Text in a 5 × 7 pixel font, each font pixel `scale` pixels square; unknown characters are blank.
    private static void Text(byte[] rgb, int width, int x0, int y0, int scale, string text, (byte R, byte G, byte B) c)
    {
        int x = x0;
        foreach (char ch in text.ToUpperInvariant())
        {
            if (Font.TryGetValue(ch, out string[]? rows))
            {
                for (int j = 0; j < 7; j++)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        if (rows[j][i] == '#')
                        {
                            Box(rgb, width, x + i * scale, y0 + j * scale, scale, scale, c);
                        }
                    }
                }
            }
            x += 6 * scale;
        }
    }

    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['0'] = new[] { " ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### " },
        ['1'] = new[] { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
        ['2'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####" },
        ['3'] = new[] { "#####", "   # ", "  #  ", "   # ", "    #", "#   #", " ### " },
        ['4'] = new[] { "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # " },
        ['5'] = new[] { "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### " },
        ['6'] = new[] { "  ## ", " #   ", "#    ", "#### ", "#   #", "#   #", " ### " },
        ['7'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   " },
        ['8'] = new[] { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " },
        ['9'] = new[] { " ### ", "#   #", "#   #", " ####", "    #", "   # ", " ##  " },
        ['A'] = new[] { " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
        ['B'] = new[] { "#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### " },
        ['C'] = new[] { " ### ", "#   #", "#    ", "#    ", "#    ", "#   #", " ### " },
        ['D'] = new[] { "#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### " },
        ['E'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####" },
        ['F'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    " },
        ['G'] = new[] { " ### ", "#   #", "#    ", "# ###", "#   #", "#   #", " ####" },
        ['H'] = new[] { "#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
        ['I'] = new[] { " ### ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
        ['J'] = new[] { "  ###", "   # ", "   # ", "   # ", "   # ", "#  # ", " ##  " },
        ['K'] = new[] { "#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #" },
        ['L'] = new[] { "#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####" },
        ['M'] = new[] { "#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #" },
        ['N'] = new[] { "#   #", "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #" },
        ['O'] = new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
        ['P'] = new[] { "#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    " },
        ['Q'] = new[] { " ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #" },
        ['R'] = new[] { "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #" },
        ['S'] = new[] { " ####", "#    ", "#    ", " ### ", "    #", "    #", "#### " },
        ['T'] = new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  " },
        ['U'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
        ['V'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  " },
        ['W'] = new[] { "#   #", "#   #", "#   #", "# # #", "# # #", "# # #", " # # " },
        ['X'] = new[] { "#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #" },
        ['Y'] = new[] { "#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  " },
        ['Z'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####" },
        ['.'] = new[] { "     ", "     ", "     ", "     ", "     ", " ##  ", " ##  " },
        [','] = new[] { "     ", "     ", "     ", "     ", " ##  ", "  #  ", " #   " },
        ['%'] = new[] { "##   ", "##  #", "   # ", "  #  ", " #   ", "#  ##", "   ##" },
        [':'] = new[] { "     ", " ##  ", " ##  ", "     ", " ##  ", " ##  ", "     " },
        ['-'] = new[] { "     ", "     ", "     ", "#####", "     ", "     ", "     " },
        ['+'] = new[] { "     ", "  #  ", "  #  ", "#####", "  #  ", "  #  ", "     " },
        ['/'] = new[] { "     ", "    #", "   # ", "  #  ", " #   ", "#    ", "     " },
        ['('] = new[] { "   # ", "  #  ", " #   ", " #   ", " #   ", "  #  ", "   # " },
        [')'] = new[] { " #   ", "  #  ", "   # ", "   # ", "   # ", "  #  ", " #   " },
    };

    // ---------------------------------------------------------------- numbers

    private static string Table(Layout[] layouts)
    {
        var s = new StringBuilder();
        s.AppendLine("layout  land  plains hills table mount  highest  lakes(ha)  one-mass  cliffs (nordic/desert/tundra)  flat 150+/75+/40+  largest flat radii (m)  landing (lat lon)  bake");
        foreach (Layout l in layouts.OrderBy(l => l.Variant))
        {
            s.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{l.Variant,6}{(l.Variant == WalkingParameters.Earth.LayoutVariant ? "*" : " ")} {100 * l.LandShare,3:0}%  {100 * l.Plains,5:0}% {100 * l.Hills,4:0}% {100 * l.Tableland,4:0}% {100 * l.Mountains,4:0}%  {l.Highest,5:0} m  {l.Lakes,3} ({l.LakeHectares,5:0.0})  {100 * l.ConnectedLand,6:0}%   {l.CliffPieces,5} ({l.Nordic}/{l.Desert}/{l.Tundra})  {l.LargeSites,8}/{l.MediumSites}/{l.SmallSites}  {string.Join(" ", l.LargestSites.Select(r => r.ToString("0", CultureInfo.InvariantCulture))),-22}  {l.Landing,-17}  {l.BakeSeconds:0.0} s{(l.BakeCached ? " (cache)" : "")}"));
        }
        s.AppendLine("* today's Earth. Shares of land by relief type; one-mass: the largest landmass's share of the land.");
        return s.ToString();
    }

    private static (double Lat, double Lon) LatLon(Vector3D d) => (Math.Asin(Math.Clamp(d.Y, -1, 1)), Math.Atan2(-d.Z, d.X));

    private static string At(Vector3D d)
    {
        (double lat, double lon) = LatLon(d);
        return string.Create(CultureInfo.InvariantCulture, $"{lat * 180 / Math.PI:0.000} {lon * 180 / Math.PI:0.000}");
    }

    private static byte Byte(double v) => (byte)Math.Clamp(v, 0, 255);
}
