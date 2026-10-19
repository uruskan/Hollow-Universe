using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.GeneratorReport;

/// <summary>
/// T1 of docs/TERRAIN_SCENERY_PLAN.md: a whole-Earth landform survey of the live walking world. Classifies land into
/// coastal scarps, plateau and mountain faces, inland outcrops, wooded edges, meadows and shores; measures how much of
/// the steep ground the generated cliff scans actually cover; finds each class's largest connected regions; and draws
/// a labelled map. Read-only: it samples the same <see cref="WalkingWorld"/> the server builds, and changes nothing.
/// </summary>
public static class EarthSurvey
{
    // The cliff generator's own candidate rule (CliffPlacements: gradient 0.62), so coverage compares like with like.
    // A wall is ground no heightfield face can carry: steeper than 2 (63°), where a top-down texture stretches and a
    // render cell's height jumps (TERRAIN_SCENERY_PLAN.md T1, the scalloped wall).
    private const double SteepGrade = 0.62, GentleGrade = 0.35, WallGrade = 2, CoastalMetres = 80, ScanMargin = 1;

    private enum Landform : byte { Sea, Lake, Land, Shore, Meadow, WoodedEdge, InlandOutcrop, PlateauFace, CoastalScarp }

    private static readonly (Landform Kind, string Name, byte R, byte G, byte B)[] Palette =
    {
        (Landform.Sea, "sea", 22, 44, 76), (Landform.Lake, "lake", 70, 120, 170), (Landform.Land, "other land", 0, 0, 0),
        (Landform.Shore, "shore", 222, 204, 150), (Landform.Meadow, "meadow", 150, 205, 95),
        (Landform.WoodedEdge, "wooded edge", 40, 110, 50), (Landform.InlandOutcrop, "inland outcrop", 235, 150, 40),
        (Landform.PlateauFace, "plateau / mountain face", 150, 80, 190), (Landform.CoastalScarp, "coastal scarp", 210, 50, 45),
    };

    public static int Run(string[] args)
    {
        string folder = args.Length > 2 ? args[2] : Path.Combine(Program.RepoRoot(), "docs", "images", "t1-survey");
        int width = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 1440;
        int height = width / 2;
        Directory.CreateDirectory(folder);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        WalkingWorld world = WalkingReport.Build(earth: true);
        double radius = world.Radius, seaLevel = world.SeaRadius > 0 ? world.SeaRadius - world.Radius : double.NegativeInfinity;
        double cell = DMath.Pi / height; // radians per pixel in latitude and longitude

        var water = new byte[width * height]; // 0 land, 1 sea, 2 lake
        var heights = new double[width * height];
        var grades = new double[width * height];
        var grass = new double[width * height];
        var woodland = new double[width * height];
        var biomes = new Biome[width * height];
        var reliefs = new Relief[width * height];
        Parallel.For(0, height, y =>
        {
            Span<double> biomeWeights = stackalloc double[BiomeTable.Count];
            Span<double> materials = stackalloc double[BiomeTable.MaterialCount];
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                Vector3D direction = Direction(x, y, width, height);
                double h = world.HeightAt(direction), level = world.WaterLevelAt(direction);
                heights[i] = h;
                if (h < level - 0.05)
                {
                    water[i] = (byte)(Math.Abs(level - seaLevel) < 0.05 ? 1 : 2);
                    continue;
                }
                double grade = Grade(world, direction);
                grades[i] = grade;
                biomes[i] = world.BiomeAt(direction);
                grass[i] = world.Cover(direction, world.Sample(direction), grade, biomeWeights, materials).Grass;
                WalkingSample fields = world.Fields(direction);
                woodland[i] = world.ForestCover(direction, fields);
                reliefs[i] = fields.Relief;
            }
        });

        double[] seaDistance = SeaDistance(water, width, height, cell * radius);
        double[] nearestOpen = MinimumWithin(woodland, water, width, height, 3);

        var kinds = new Landform[width * height];
        for (int i = 0; i < kinds.Length; i++)
        {
            if (water[i] != 0) { kinds[i] = water[i] == 1 ? Landform.Sea : Landform.Lake; continue; }
            // Steep ground by what made it: the coast, a tableland's escarpment or a mountain's flank, or an outcrop of
            // the plains and hills (kettle rims, valley sides).
            if (grades[i] >= SteepGrade)
                kinds[i] = seaDistance[i] <= CoastalMetres ? Landform.CoastalScarp
                    : reliefs[i] is Relief.Tableland or Relief.Mountains ? Landform.PlateauFace : Landform.InlandOutcrop;
            else if (biomes[i] == Biome.Beach) kinds[i] = Landform.Shore;
            else if (grades[i] < GentleGrade && woodland[i] >= 0.45 && nearestOpen[i] <= 0.15) kinds[i] = Landform.WoodedEdge;
            else if (grades[i] < GentleGrade && grass[i] >= 0.6 && woodland[i] < 0.2) kinds[i] = Landform.Meadow;
            else kinds[i] = Landform.Land;
        }

        // Cliff coverage of steep ground: a steep sample is covered when a generated scan stands within a few metres.
        var covered = new bool[width * height];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (kinds[i] is not (Landform.CoastalScarp or Landform.PlateauFace or Landform.InlandOutcrop)) continue;
                Vector3D direction = Direction(x, y, width, height);
                covered[i] = InsideScan(world, direction * (radius + heights[i]));
            }
        });

        double PixelArea(int y) => cell * radius * (cell * radius) * DMath.Cos(Latitude(y, height) * DMath.DegToRad);
        var classes = new List<object>();
        var regionsByClass = new Dictionary<string, object>();
        foreach (var entry in Palette.Where(p => p.Kind is not (Landform.Sea or Landform.Lake or Landform.Land)))
        {
            double area = 0, coveredArea = 0, wallArea = 0, wallCovered = 0;
            for (int i = 0; i < kinds.Length; i++)
                if (kinds[i] == entry.Kind)
                {
                    double a = PixelArea(i / width);
                    area += a;
                    if (covered[i]) coveredArea += a;
                    if (grades[i] >= WallGrade) { wallArea += a; if (covered[i]) wallCovered += a; }
                }
            bool steep = entry.Kind is Landform.CoastalScarp or Landform.PlateauFace or Landform.InlandOutcrop;
            classes.Add(new { landform = entry.Name, areaSquareMetres = Math.Round(area),
                cliffCovered = steep ? Math.Round(coveredArea / Math.Max(1, area), 3) : (double?)null,
                wallShare = steep ? Math.Round(wallArea / Math.Max(1, area), 3) : (double?)null,
                wallCliffCovered = steep ? Math.Round(wallCovered / Math.Max(1, wallArea), 3) : (double?)null });
            regionsByClass[entry.Name] = Regions(world, entry.Kind, kinds, covered, heights, grades, reliefs, width, height, PixelArea, 6);
        }

        double land = Enumerable.Range(0, kinds.Length).Where(i => water[i] == 0).Sum(i => PixelArea(i / width));
        double steepArea = Enumerable.Range(0, kinds.Length)
            .Where(i => kinds[i] is Landform.CoastalScarp or Landform.PlateauFace or Landform.InlandOutcrop).Sum(i => PixelArea(i / width));
        double steepCovered = Enumerable.Range(0, kinds.Length).Where(i => covered[i]).Sum(i => PixelArea(i / width));
        int[] walls = Enumerable.Range(0, kinds.Length).Where(i => water[i] == 0 && grades[i] >= WallGrade).ToArray();
        double allWalls = walls.Sum(i => PixelArea(i / width)), coveredWalls = walls.Where(i => covered[i]).Sum(i => PixelArea(i / width));
        int placements = CountPlacements(world);

        DrawMap(Path.Combine(folder, "earth-landforms.png"), kinds, covered, heights, seaLevel, width, height);
        var report = new
        {
            survey = "T1 whole-Earth landform survey (docs/TERRAIN_SCENERY_PLAN.md)",
            world = new { world.Radius, layout = "Earth (StartingWorld) as built by the server", seaLevel },
            raster = new { width, height, metresPerPixelAtEquator = Math.Round(cell * radius, 2) },
            rules = new { steepGrade = SteepGrade, gentleGrade = GentleGrade, wallGrade = WallGrade, coastalMetres = CoastalMetres,
                plateauOrMountainFace = "steep, inland, tableland or mountain relief dominant", coveredMeans = "the ground point lies inside a placed scan's bounding box, 1 m margin",
                woodedEdge = "woodland >= 0.45 within 3 pixels of woodland <= 0.15", meadow = "grass >= 0.6, woodland < 0.2" },
            landSquareMetres = Math.Round(land), steepSquareMetres = Math.Round(steepArea),
            steepCoveredByCliffScans = Math.Round(steepCovered / Math.Max(1, steepArea), 3),
            wallSquareMetres = Math.Round(allWalls), wallCoveredByCliffScans = Math.Round(coveredWalls / Math.Max(1, allWalls), 3),
            cliffPlacements = placements, recipes = RecipeCoverage(world), classes, regions = regionsByClass,
            lakeBanks = LakeBanks(world, 3),
        };
        // The file holds only generated facts, so a rerun on the same generator reproduces it byte for byte.
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(folder, "earth-landforms.json"), json);
        Console.WriteLine(json);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{watch.Elapsed.TotalSeconds:F1} s"));
        return 0;
    }

    /// <summary>
    /// `earth transect lat lon bearing [length] [step]`: the ground along a bearing (degrees from north) from a point,
    /// with the relief that made it, the water over it and the cliff scans beside it. For diagnosing a wall or a scarp
    /// at reproduced coordinates (T1).
    /// </summary>
    public static int Transect(string[] args)
    {
        double Arg(int i, double fallback) => args.Length > i ? double.Parse(args[i], CultureInfo.InvariantCulture) : fallback;
        double latitude = Arg(2, 0), longitude = Arg(3, 0), bearing = Arg(4, 0) * DMath.DegToRad;
        double length = Arg(5, 200), step = Arg(6, 1);
        WalkingWorld world = WalkingReport.Build(earth: true);
        Vector3D start = ZoneFrame.Direction(latitude * DMath.DegToRad, longitude * DMath.DegToRad);
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, start).Normalized, north = Vector3D.Cross(start, east);
        Vector3D heading = north * DMath.Cos(bearing) + east * DMath.Sin(bearing);
        // "uncarved" is the relief before the drainage cuts valleys and canyons and before monument pads (WalkingWorld.Parts).
        Console.WriteLine("metres  latitude    longitude    height uncarved  water   rise/m  plains hills table mount rocky cliffs");
        double previous = double.NaN, steepest = 0, steepestAt = 0;
        for (double d = 0; d <= length + 1e-9; d += step)
        {
            // A great circle from the start along the heading: d metres of arc on the reference sphere.
            double angle = d / world.Radius;
            Vector3D direction = (start * DMath.Cos(angle) + heading * DMath.Sin(angle)).Normalized;
            WalkingSample s = world.Fields(direction);
            double h = world.HeightAt(direction);
            double rise = double.IsNaN(previous) ? 0 : (h - previous) / step;
            if (Math.Abs(rise) > Math.Abs(steepest)) (steepest, steepestAt) = (rise, d);
            previous = h;
            var (lat, lon) = LatLon(direction);
            int cliffs = world.Cliffs.Near(direction * (world.Radius + h), 0).Count(c => Inside(c, direction * (world.Radius + h)));
            string water = s.Wetted ? s.Water.ToString("F2", CultureInfo.InvariantCulture) : "-";
            double uncarved = world.Parts(direction).Base;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{d,6:F1}  {lat,10:F6}  {lon,11:F6}  {h,6:F2}  {uncarved,7:F2}  {water,6}  {rise,6:F2}   {s.Plains,5:F2} {s.Hills,5:F2} {s.Tableland,5:F2} {s.Mountains,5:F2} {s.Rocky,5:F2} {cliffs,5}"));
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"steepest rise {steepest:F2} m/m at {steepestAt:F1} m"));
        return 0;
    }

    /// <summary>
    /// `earth wall lat lon [size] [folder]`: the ground round a point seen from above, as the generator makes it and as
    /// the terrain's render grid draws it at tile levels 6 to 3 (32 cells a tile: 0.9, 1.8, 3.7 and 7.4 m cells on
    /// Earth), left to right. Hill-shaded from the north-west; steep ground orange, walls (steeper than 2) red. It shows
    /// how a step narrower than a cell is drawn (T1, the scalloped wall). Without the renderer's morphing between levels.
    /// </summary>
    public static int Wall(string[] args)
    {
        double Arg(int i, double fallback) => args.Length > i ? double.Parse(args[i], CultureInfo.InvariantCulture) : fallback;
        double latitude = Arg(2, 0), longitude = Arg(3, 0), size = Arg(4, 100);
        string folder = args.Length > 5 ? args[5] : Path.Combine(Program.RepoRoot(), "docs", "images", "t1-survey");
        Directory.CreateDirectory(folder);
        WalkingWorld world = WalkingReport.Build(earth: true);
        Vector3D centre = ZoneFrame.Direction(latitude * DMath.DegToRad, longitude * DMath.DegToRad);
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, centre).Normalized, north = Vector3D.Cross(centre, east);
        const double PixelMetres = 0.25;
        const int Gap = 8;
        int side = (int)Math.Round(size / PixelMetres);
        int[] levels = { -1, 6, 5, 4, 3 };
        int width = levels.Length * side + (levels.Length - 1) * Gap;
        var rgb = Enumerable.Repeat((byte)255, width * side * 3).ToArray();
        for (int k = 0; k < levels.Length; k++)
        {
            int level = levels[k];
            var vertices = new System.Collections.Concurrent.ConcurrentDictionary<(int, long, long), double>();
            var heights = new double[side * side];
            Parallel.For(0, side, py =>
            {
                for (int px = 0; px < side; px++)
                {
                    Vector3D point = (centre * world.Radius + east * ((px + 0.5) * PixelMetres - size / 2)
                        + north * (size / 2 - (py + 0.5) * PixelMetres)).Normalized;
                    heights[py * side + px] = level < 0 ? world.HeightAt(point) : Drawn(world, point, level, vertices);
                }
            });
            double wall = 0, steepest = 0;
            for (int py = 0; py < side; py++)
            for (int px = 0; px < side; px++)
            {
                double H(int x, int y) => heights[Math.Clamp(y, 0, side - 1) * side + Math.Clamp(x, 0, side - 1)];
                double dx = (H(px + 1, py) - H(px - 1, py)) / (2 * PixelMetres);
                double dy = (H(px, py - 1) - H(px, py + 1)) / (2 * PixelMetres); // rows run north to south
                double grade = Math.Sqrt(dx * dx + dy * dy);
                steepest = Math.Max(steepest, grade);
                if (grade >= WallGrade) wall++;
                // Lambert light from the north-west, 45° up.
                double nx = -dx, ny = -dy, nz = 1, length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                double light = Math.Max(0, (-nx + ny + 1.4 * nz) / (length * Math.Sqrt(1 + 1 + 1.96)));
                (double r, double g, double b) = grade >= WallGrade ? (190, 60, 50) : grade >= SteepGrade ? (215, 140, 70) : (120, 160, 85);
                double shade = 0.3 + 0.7 * light;
                int o = (py * width + k * (side + Gap) + px) * 3;
                rgb[o] = (byte)Math.Clamp(r * shade, 0, 255);
                rgb[o + 1] = (byte)Math.Clamp(g * shade, 0, 255);
                rgb[o + 2] = (byte)Math.Clamp(b * shade, 0, 255);
            }
            string name = level < 0 ? "generator" : $"level {level} ({TileKey.EdgeLength(level + 5, world.Radius):F2} m cells)";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{name,-26} steepest {steepest,6:F2}  wall {wall * PixelMetres * PixelMetres,7:F1} m²"));
        }
        string path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"wall_{latitude:F4}_{longitude:F4}.png"));
        Png.Write(path, width, side, rgb);
        Console.WriteLine(path);
        return 0;
    }

    /// <summary>
    /// `earth formations lat lon [size] [folder]`: the cliff formations round a point from above (T2): the ground
    /// hill-shaded (steep orange, walls red), each scan's footprint tinted by its family (nordic slate, sandstone sand,
    /// forest olive, coastal pale), and every surveyed face sample as a dot: green covered, magenta meant for rock but
    /// bare, white left as soil, black outside any dressed face.
    /// </summary>
    public static int Formations(string[] args)
    {
        double Arg(int i, double fallback) => args.Length > i ? double.Parse(args[i], CultureInfo.InvariantCulture) : fallback;
        double latitude = Arg(2, 0), longitude = Arg(3, 0), size = Arg(4, 160);
        string folder = args.Length > 5 ? args[5] : Path.Combine(Program.RepoRoot(), "docs", "images", "t2-formations");
        Directory.CreateDirectory(folder);
        WalkingWorld world = WalkingReport.Build(earth: true);
        Vector3D centre = ZoneFrame.Direction(latitude * DMath.DegToRad, longitude * DMath.DegToRad);
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, centre).Normalized, north = Vector3D.Cross(centre, east);
        Vector3D origin = centre * world.Radius;
        const double PixelMetres = 0.25;
        int side = (int)Math.Round(size / PixelMetres);
        Vector3D Point(double x, double y) => (origin + east * x + north * y).Normalized;
        List<CliffPlacement> pieces = world.Cliffs.Near(origin, size);
        var heights = new double[side * side];
        var family = new int[side * side];
        string[] families = CliffGeometry.All.Select(g => g.MaterialFamily).Distinct().Order(StringComparer.Ordinal).ToArray();
        Parallel.For(0, side, py =>
        {
            for (int px = 0; px < side; px++)
            {
                Vector3D d = Point((px + 0.5) * PixelMetres - size / 2, size / 2 - (py + 0.5) * PixelMetres);
                double h = world.HeightAt(d);
                heights[py * side + px] = h;
                Vector3D ground = d * (world.Radius + h);
                family[py * side + px] = -1;
                foreach (CliffPlacement piece in pieces)
                    if (CliffPlacements.Holds(piece, ground, 0))
                    {
                        family[py * side + px] = Array.IndexOf(families, CliffGeometry.All[piece.Model].MaterialFamily);
                        break;
                    }
            }
        });
        var tints = new Dictionary<string, (double R, double G, double B)>
        {
            ["nordic"] = (120, 135, 150), ["sandstone"] = (215, 180, 120), ["forest"] = (120, 130, 70), ["coastal"] = (225, 225, 210),
        };
        var rgb = new byte[side * side * 3];
        for (int py = 0; py < side; py++)
        for (int px = 0; px < side; px++)
        {
            double H(int x, int y) => heights[Math.Clamp(y, 0, side - 1) * side + Math.Clamp(x, 0, side - 1)];
            double dx = (H(px + 1, py) - H(px - 1, py)) / (2 * PixelMetres), dy = (H(px, py - 1) - H(px, py + 1)) / (2 * PixelMetres);
            double grade = Math.Sqrt(dx * dx + dy * dy), length = Math.Sqrt(dx * dx + dy * dy + 1);
            double light = Math.Max(0, (dx + dy + 1.4) / (length * Math.Sqrt(3.96)));
            int f = family[py * side + px];
            (double r, double g, double b) = f >= 0 ? tints.GetValueOrDefault(families[f], (200, 200, 200))
                : grade >= WallGrade ? (190, 60, 50) : grade >= SteepGrade ? (215, 140, 70) : (120, 160, 85);
            double shade = 0.3 + 0.7 * light;
            int o = (py * side + px) * 3;
            rgb[o] = (byte)Math.Clamp(r * shade, 0, 255); rgb[o + 1] = (byte)Math.Clamp(g * shade, 0, 255); rgb[o + 2] = (byte)Math.Clamp(b * shade, 0, 255);
        }
        var cells = new List<TileKey>();
        TileKey.Around(origin, size * 0.75, world.Radius, world.Cliffs.Level, cells);
        int covered = 0, bare = 0, soil = 0, outside = 0;
        foreach (TileKey cell in cells)
            foreach (CliffFaceSample sample in world.Cliffs.Report(cell))
            {
                Vector3D p = sample.Ground - origin;
                int cx = (int)((Vector3D.Dot(p, east) + size / 2) / PixelMetres), cy = (int)((size / 2 - Vector3D.Dot(p, north)) / PixelMetres);
                if (cx < 0 || cy < 0 || cx >= side || cy >= side) continue;
                (byte r, byte g, byte b) = sample.Recipe == null ? ((byte)0, (byte)0, (byte)0)
                    : sample.Covered ? ((byte)40, (byte)220, (byte)60) : sample.Intended ? ((byte)230, (byte)30, (byte)200) : ((byte)255, (byte)255, (byte)255);
                if (sample.Recipe == null) outside++; else if (sample.Covered) covered++; else if (sample.Intended) bare++; else soil++;
                for (int y = cy - 3; y <= cy + 3; y++)
                for (int x = cx - 3; x <= cx + 3; x++)
                {
                    if (x < 0 || y < 0 || x >= side || y >= side) continue;
                    int o = (y * side + x) * 3;
                    rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = b;
                }
            }
        string path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"formations_{latitude:F4}_{longitude:F4}.png"));
        Png.Write(path, side, side, rgb);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{pieces.Count} pieces near; face samples: {covered} covered, {bare} meant for rock but bare, {soil} left as soil, {outside} outside a dressed face"));
        Console.WriteLine(path);
        return 0;
    }

    /// <summary>The height the render grid draws at a point: its level's 32-cell tile grid, linear over the lattice's triangles.</summary>
    private static double Drawn(WalkingWorld world, Vector3D direction, int level,
        System.Collections.Concurrent.ConcurrentDictionary<(int, long, long), double> vertices)
    {
        (int face, double u, double v) = CubeSphere.FromDirection(direction);
        long n = 1L << (level + 5);
        double gu = (u + 1) * 0.5 * n, gv = (v + 1) * 0.5 * n;
        long i = Math.Clamp((long)Math.Floor(gu), 0, n - 1), j = Math.Clamp((long)Math.Floor(gv), 0, n - 1);
        double fu = gu - i, fv = gv - j;
        double H(long a, long b) => vertices.GetOrAdd((face, a, b),
            key => world.HeightAt(CubeSphere.Direction(key.Item1, -1 + 2.0 * key.Item2 / n, -1 + 2.0 * key.Item3 / n)));
        // The lattice's diagonal runs from (i, j) to (i + 1, j + 1) (TerrainLattice.Triangle).
        double h00 = H(i, j), h11 = H(i + 1, j + 1);
        if (fu >= fv)
        {
            double h10 = H(i + 1, j);
            return h00 + (h10 - h00) * fu + (h11 - h10) * fv;
        }
        double h01 = H(i, j + 1);
        return h00 + (h01 - h00) * fv + (h11 - h01) * fu;
    }

    private static Vector3D Direction(int x, int y, int width, int height)
    {
        double latitude = Latitude(y, height) * DMath.DegToRad;
        double longitude = (-180 + (x + 0.5) * 360.0 / width) * DMath.DegToRad;
        // The route planner's convention: latitude = asin(y), longitude = atan2(-z, x).
        return new Vector3D(DMath.Cos(latitude) * DMath.Cos(longitude), DMath.Sin(latitude), -DMath.Cos(latitude) * DMath.Sin(longitude));
    }

    private static double Latitude(int y, int height) => 90 - (y + 0.5) * 180.0 / height;

    private static (double Latitude, double Longitude) LatLon(Vector3D p)
    {
        Vector3D n = p.Normalized;
        return (DMath.Asin(Math.Clamp(n.Y, -1, 1)) * DMath.RadToDeg, DMath.Atan2(-n.Z, n.X) * DMath.RadToDeg);
    }

    /// <summary>The route planner's grade: central differences two metres either side, east and north.</summary>
    private static double Grade(WalkingWorld world, Vector3D direction)
    {
        Vector3D up = direction.Normalized, east = Vector3D.Cross(Vector3D.UnitY, up).Normalized,
            north = Vector3D.Cross(up, east), at = up * world.Radius;
        double x = (world.HeightAt((at + east * 2).Normalized) - world.HeightAt((at - east * 2).Normalized)) / 4;
        double y = (world.HeightAt((at + north * 2).Normalized) - world.HeightAt((at - north * 2).Normalized)) / 4;
        return Math.Sqrt(x * x + y * y);
    }

    /// <summary>Metres to the nearest sea pixel over the raster, eight-connected, with each row's east-west scale.</summary>
    private static double[] SeaDistance(byte[] water, int width, int height, double metresPerCell)
    {
        var distance = Enumerable.Repeat(double.PositiveInfinity, water.Length).ToArray();
        var queue = new PriorityQueue<int, double>();
        for (int i = 0; i < water.Length; i++)
            if (water[i] == 1) { distance[i] = 0; queue.Enqueue(i, 0); }
        while (queue.TryDequeue(out int i, out double d))
        {
            if (d > distance[i] || d > 500) continue;
            int x = i % width, y = i / width;
            double ew = metresPerCell * Math.Max(0.05, DMath.Cos(Latitude(y, height) * DMath.DegToRad));
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int ny = y + dy;
                if (ny < 0 || ny >= height) continue;
                int nx = (x + dx + width) % width, j = ny * width + nx;
                double step = Math.Sqrt(dx * dx * ew * ew + dy * dy * metresPerCell * metresPerCell);
                if (d + step < distance[j]) { distance[j] = d + step; queue.Enqueue(j, d + step); }
            }
        }
        return distance;
    }

    /// <summary>The smallest land value within a square window, for edge detection.</summary>
    private static double[] MinimumWithin(double[] values, byte[] water, int width, int height, int reach)
    {
        var rows = new double[values.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double minimum = double.PositiveInfinity;
            for (int dx = -reach; dx <= reach; dx++)
            {
                int j = y * width + (x + dx + width) % width;
                if (water[j] == 0) minimum = Math.Min(minimum, values[j]);
            }
            rows[y * width + x] = minimum;
        }
        var result = new double[values.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            double minimum = double.PositiveInfinity;
            for (int dy = -reach; dy <= reach; dy++)
            {
                int ny = Math.Clamp(y + dy, 0, height - 1);
                minimum = Math.Min(minimum, rows[ny * width + x]);
            }
            result[y * width + x] = minimum;
        }
        return result;
    }

    private static List<object> Regions(WalkingWorld world, Landform kind, Landform[] kinds, bool[] covered, double[] heights,
        double[] grades, Relief[] reliefs, int width, int height, Func<int, double> pixelArea, int keep)
    {
        bool steep = kind is Landform.CoastalScarp or Landform.PlateauFace or Landform.InlandOutcrop;
        var seen = new bool[kinds.Length];
        var found = new List<(double Area, int Anchor, Func<int, object> Record)>();
        var stack = new Stack<int>();
        var members = new List<int>();
        for (int start = 0; start < kinds.Length; start++)
        {
            if (seen[start] || kinds[start] != kind) continue;
            double area = 0, coveredArea = 0, maxGrade = 0, heightSum = 0, low = double.PositiveInfinity, high = double.NegativeInfinity;
            var reliefArea = new double[4];
            Vector3D centre = default;
            members.Clear();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                members.Add(i);
                int x = i % width, y = i / width;
                double a = pixelArea(y);
                area += a;
                if (covered[i]) coveredArea += a;
                maxGrade = Math.Max(maxGrade, grades[i]);
                heightSum += heights[i] * a;
                low = Math.Min(low, heights[i]);
                high = Math.Max(high, heights[i]);
                reliefArea[(int)reliefs[i]] += a;
                centre += Direction(x, y, width, height) * a;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int ny = y + dy;
                    if (ny < 0 || ny >= height) continue;
                    int j = ny * width + (x + dx + width) % width;
                    if (!seen[j] && kinds[j] == kind) { seen[j] = true; stack.Push(j); }
                }
            }
            // The anchor is a pixel of the region itself (a centroid can fall outside a curved one): the steepest for
            // a face, where its wall is worst; otherwise the one nearest the centroid.
            Vector3D middle = centre.Normalized;
            int anchor = steep
                ? members.MaxBy(i => grades[i])
                : members.MaxBy(i => Vector3D.Dot(Direction(i % width, i / width, width, height), middle));
            var (lat, lon) = LatLon(centre);
            int pixels = members.Count;
            string relief = ((Relief)Array.IndexOf(reliefArea, reliefArea.Max())).ToString();
            double meanHeight = heightSum / Math.Max(1e-9, area), range = high - low, share = coveredArea / Math.Max(1e-9, area);
            found.Add((area, anchor, a => new
            {
                latitude = Math.Round(lat, 6), longitude = Math.Round(lon, 6), areaSquareMetres = Math.Round(area), pixels,
                meanHeight = Math.Round(meanHeight, 2), heightRange = Math.Round(range, 2),
                maxGrade = Math.Round(maxGrade, 3), relief, cliffCovered = Math.Round(share, 3),
                anchor = Anchor(world, Direction(a % width, a / width, width, height), steep),
            }));
        }
        return found.OrderByDescending(f => f.Area).Take(keep).Select(f => f.Record(f.Anchor)).ToList();
    }

    /// <summary>
    /// A region's anchor and a viewpoint for its before-image: for a face, gentle dry ground 30 to 150 m downhill of
    /// it (uphill, on its top, where the foot of a sea cliff is under water), looking at it; otherwise the anchor
    /// itself, looking downhill. Bearings in degrees from north, pitch from the eye (1.7 m) to the anchor's ground.
    /// </summary>
    private static object Anchor(WalkingWorld world, Vector3D anchor, bool face)
    {
        var (latitude, longitude) = LatLon(anchor);
        double anchorHeight = world.HeightAt(anchor);
        object? view = null;
        if (!face)
        {
            Vector3D downhill = (anchor * world.Radius - Gradient(world, anchor).Normalized * 10).Normalized;
            view = new { latitude = Math.Round(latitude, 6), longitude = Math.Round(longitude, 6),
                yaw = Math.Round(Bearing(anchor, downhill), 1), pitch = 0.0, metres = 0.0, side = "on it" };
        }
        foreach (double sense in new[] { -1.0, 1.0 })
        {
            if (view != null) break;
            Vector3D at = anchor;
            double walked = 0;
            for (int step = 0; step < 75; step++)
            {
                Vector3D slope = Gradient(world, at) * sense;
                if (slope.LengthSquared < 1e-12) break;
                at = (at * world.Radius + slope.Normalized * 2).Normalized;
                walked += 2;
                if (world.Fields(at).Wetted) break;
                if (walked < 30 || Grade(world, at) >= GentleGrade) continue;
                var (viewLatitude, viewLongitude) = LatLon(at);
                double across = world.Radius * DMath.Acos(Math.Clamp(Vector3D.Dot(at, anchor), -1, 1));
                double pitch = DMath.Atan2(anchorHeight - (world.HeightAt(at) + 1.7), across) * DMath.RadToDeg;
                view = new { latitude = Math.Round(viewLatitude, 6), longitude = Math.Round(viewLongitude, 6),
                    yaw = Math.Round(Bearing(at, anchor), 1), pitch = Math.Round(Math.Clamp(pitch, -25, 25), 1),
                    metres = Math.Round(walked, 1), side = sense < 0 ? "below" : "above" };
                break;
            }
        }
        return new
        {
            latitude = Math.Round(latitude, 6), longitude = Math.Round(longitude, 6),
            height = Math.Round(anchorHeight, 2), grade = Math.Round(Grade(world, anchor), 3), view,
        };
    }

    /// <summary>
    /// A bank viewpoint for each of the largest lakes: from its centre outward, the first bearing (from north, in
    /// 45° steps) whose shore is dry gentle ground six metres from the water, looking back across it.
    /// </summary>
    private static List<object> LakeBanks(WalkingWorld world, int count)
    {
        var banks = new List<object>();
        foreach (WalkingLake lake in world.Lakes.OrderByDescending(l => l.Area).Take(count))
        {
            Vector3D centre = lake.Centre.Normalized, east = Vector3D.Cross(Vector3D.UnitY, centre).Normalized,
                north = Vector3D.Cross(centre, east);
            for (int k = 0; k < 8; k++)
            {
                double bearing = k * 45 * DMath.DegToRad;
                Vector3D heading = north * DMath.Cos(bearing) + east * DMath.Sin(bearing);
                Vector3D At(double metres) => (centre * DMath.Cos(metres / world.Radius) + heading * DMath.Sin(metres / world.Radius)).Normalized;
                double edge = 0;
                for (double d = 2; d <= 400 && edge == 0; d += 2)
                    if (!world.Fields(At(d)).Wetted) edge = d;
                if (edge == 0) continue;
                Vector3D view = At(edge + 6);
                if (world.Fields(view).Wetted || Grade(world, view) >= GentleGrade) continue;
                var (latitude, longitude) = LatLon(view);
                banks.Add(new
                {
                    level = Math.Round(lake.Level, 2), areaSquareMetres = Math.Round(lake.Area), maxDepth = Math.Round(lake.MaxDepth, 2),
                    view = new { latitude = Math.Round(latitude, 6), longitude = Math.Round(longitude, 6),
                        yaw = Math.Round(Bearing(view, At(Math.Max(0, edge - 20))), 1), pitch = -5.0 },
                });
                break;
            }
        }
        return banks;
    }

    /// <summary>Whether a ground point lies inside any placed scan's box (with a metre's margin): rock is drawn there.</summary>
    private static bool InsideScan(WalkingWorld world, Vector3D point) => world.Cliffs.Near(point, 0).Any(c => Inside(c, point));

    private static bool Inside(CliffPlacement cliff, Vector3D point)
    {
        CliffGeometry scan = CliffGeometry.All[cliff.Model];
        Vector3D d = point - cliff.Position;
        double margin = ScanMargin / cliff.Scale;
        double x = Vector3D.Dot(d, cliff.Right) / cliff.Scale, y = Vector3D.Dot(d, cliff.Up) / cliff.Scale,
            z = Vector3D.Dot(d, cliff.Forward) / cliff.Scale;
        return x >= scan.BoundsMin.X - margin && x <= scan.BoundsMax.X + margin && y >= scan.BoundsMin.Y - margin &&
            y <= scan.BoundsMax.Y + margin && z >= scan.BoundsMin.Z - margin && z <= scan.BoundsMax.Z + margin;
    }

    /// <summary>The ground's gradient at a point as a tangent vector (metres of rise per metre, east and north).</summary>
    private static Vector3D Gradient(WalkingWorld world, Vector3D direction)
    {
        Vector3D up = direction.Normalized, east = Vector3D.Cross(Vector3D.UnitY, up).Normalized,
            north = Vector3D.Cross(up, east), at = up * world.Radius;
        double x = (world.HeightAt((at + east * 2).Normalized) - world.HeightAt((at - east * 2).Normalized)) / 4;
        double y = (world.HeightAt((at + north * 2).Normalized) - world.HeightAt((at - north * 2).Normalized)) / 4;
        return east * x + north * y;
    }

    /// <summary>The initial bearing from one point to another, degrees clockwise from north.</summary>
    private static double Bearing(Vector3D from, Vector3D to)
    {
        Vector3D up = from.Normalized, east = Vector3D.Cross(Vector3D.UnitY, up).Normalized, north = Vector3D.Cross(up, east);
        Vector3D along = to.Normalized - up * Vector3D.Dot(up, to.Normalized);
        double degrees = DMath.Atan2(Vector3D.Dot(along, east), Vector3D.Dot(along, north)) * DMath.RadToDeg;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    /// <summary>
    /// The formation generator's own record (CliffPlacements.Report): every cell's surveyed face samples by recipe,
    /// how many carry rock, and the walls among them. Uncovered samples are the intended regions fitting left bare.
    /// </summary>
    private static List<object> RecipeCoverage(WalkingWorld world)
    {
        var totals = new SortedDictionary<string, long[]>(StringComparer.Ordinal);
        int level = world.Cliffs.Level;
        var reports = new IReadOnlyList<CliffFaceSample>[TileKey.CountAt(level)];
        Parallel.For(0, reports.Length, i => reports[i] = world.Cliffs.Report(TileKey.FromIndex(level, i)!.Value));
        foreach (IReadOnlyList<CliffFaceSample> report in reports)
            foreach (CliffFaceSample sample in report)
            {
                string key = sample.Recipe ?? "(no face kept)";
                if (!totals.TryGetValue(key, out long[]? t)) totals[key] = t = new long[6];
                t[0]++;
                if (sample.Covered) t[1]++;
                if (sample.Wall) { t[2]++; if (sample.Covered) t[3]++; }
                if (sample.Intended) { t[4]++; if (sample.Covered) t[5]++; }
            }
        return totals.Select(p => (object)new
        {
            recipe = p.Key, faceSamples = p.Value[0], covered = Math.Round((double)p.Value[1] / Math.Max(1, p.Value[0]), 3),
            walls = p.Value[2], wallsCovered = Math.Round((double)p.Value[3] / Math.Max(1, p.Value[2]), 3),
            intended = Math.Round((double)p.Value[4] / Math.Max(1, p.Value[0]), 3),
            intendedCovered = Math.Round((double)p.Value[5] / Math.Max(1, p.Value[4]), 3),
        }).ToList();
    }

    private static int CountPlacements(WalkingWorld world)
    {
        var all = new HashSet<(TileKey, int)>();
        for (long i = 0; i < TileKey.CountAt(world.Cliffs.Level); i++)
            foreach (CliffPlacement placement in world.Cliffs.FormationAt(TileKey.FromIndex(world.Cliffs.Level, i)!.Value))
                all.Add(placement.Key);
        return all.Count;
    }

    private static void DrawMap(string path, Landform[] kinds, bool[] covered, double[] heights, double seaLevel, int width, int height)
    {
        int legend = 30, imageHeight = height + legend;
        var rgb = new byte[width * imageHeight * 3];
        for (int i = 0; i < kinds.Length; i++)
        {
            var entry = Palette.First(p => p.Kind == kinds[i]);
            byte r = entry.R, g = entry.G, b = entry.B;
            if (kinds[i] == Landform.Land)
            {
                byte shade = (byte)Math.Clamp(90 + (heights[i] - Math.Max(0, seaLevel)) * 2.2, 90, 215);
                r = g = b = shade;
            }
            if (covered[i]) { r = (byte)Math.Min(255, r + 70); g = (byte)Math.Min(255, g + 70); b = (byte)Math.Min(255, b + 70); }
            rgb[i * 3] = r; rgb[i * 3 + 1] = g; rgb[i * 3 + 2] = b;
        }
        // A legend strip: one swatch per class, in palette order (sea, lake, other land, then the landforms).
        int swatch = width / Palette.Length;
        for (int k = 0; k < Palette.Length; k++)
        for (int y = height + 6; y < imageHeight - 6; y++)
        for (int x = k * swatch + 6; x < (k + 1) * swatch - 6; x++)
        {
            int i = (y * width + x) * 3;
            var p = Palette[k];
            bool land = p.Kind == Landform.Land;
            rgb[i] = land ? (byte)150 : p.R; rgb[i + 1] = land ? (byte)150 : p.G; rgb[i + 2] = land ? (byte)150 : p.B;
        }
        Png.Write(path, width, imageHeight, rgb);
    }
}
