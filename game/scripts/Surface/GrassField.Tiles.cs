using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    /// <summary>The finest terrain tiles have 2^5 cells along an edge (TileMesh, TerrainLod.ContactCells).</summary>
    private const int TerrainCellsLog2 = 5;
    private const int TerrainCells = 1 << TerrainCellsLog2;

    /// <summary>Floats per instance: a 3 × 4 transform, a colour and custom data (Godot's MultiMesh layout).</summary>
    private const int Stride = 20;

    private const ulong JitterSalt = 0x4752415353434C55UL; // "GRASSCLU"

    // Captured once on the main thread. Workers never read the Node, the frame, the settings or native resources.
    private sealed record BuildContext(ISurface Surface, GroundCoverHabitats Habitats, TintNoise Tint, SpeciesShape[] Shapes,
        int Litter, int TerrainLevel, int TileShift, float Density);

    /// <summary>One species' instances in a tile: the buffer (padded to its capacity), ranks ascending, and bounds less
    /// the tile's centre (body axes).</summary>
    private sealed record SpeciesBuild(float[] Buffer, float[] Ranks, int Count, int Capacity, Vector3D Minimum, Vector3D Maximum);

    private sealed class TileBuild
    {
        public required TileKey Key;
        public required TileGrid Grid;
        public required Vector3D Centre;
        public required SpeciesBuild[] Species;
        public required List<CoverPlant> Accents;
        public required Vector3D Minimum, Maximum;
        public required int Cells, Clumps;
        public long Bytes
        {
            get
            {
                long total = 0;
                foreach (SpeciesBuild s in Species) total += (long)s.Count * Stride * sizeof(float);
                return total;
            }
        }
    }

    private BuildContext CaptureContext(ISurface surface, SpeciesCatalog catalog, TintNoise tint, float density) =>
        new(surface, GroundCoverHabitats.Default, tint, catalog.Shapes, catalog.Litter, _terrainLevel, _tileShift, density);

    // A clump in the making: its sort key, and its twenty floats.
    private struct Instance
    {
        public float Rank;
        public int Order;
        public Vector3D Root;     // less the tile's centre
        public double Margin;     // how far its cards and their bending can reach from the root
        public float[] Data;
    }

    /// <summary>
    /// Builds one tile on a worker: for every placement cell of its block of the terrain tile, the habitat rule's plan at
    /// the cell's jittered point (the drawn grid's climate and materials there, the grass colour's dryness), its root on
    /// the drawn triangle with the morph target under it, the ground's slope over the clump's footprint as a shear, and
    /// the clump's colour; then each species' instances sorted by rank (the stable thinning order).
    /// </summary>
    private static TileBuild BuildTile(BuildContext context, TileJob job, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        TileGrid grid = job.Grid ?? TileMesh.Build(context.Surface, job.Terrain, TerrainCells);
        if (grid.Cells != TerrainCells) throw new InvalidOperationException($"Grass needs {TerrainCells}-cell terrain grids, not {grid.Cells}.");
        int n = TerrainCells + 1;
        int block = TerrainCells >> context.TileShift;
        int split = 1 << context.TileShift;
        int bx = (job.Key.X - job.Terrain.X * split) * block, by = (job.Key.Y - job.Terrain.Y * split) * block;
        int face = job.Key.Face;
        WalkingWorld? walking = context.Surface as WalkingWorld;

        // The block's vertices: what grass finds there (the shared presentation cover from the grid's own climate and
        // materials), and whether water or a monument's pad leaves them bare.
        int side = block + 1;
        var sites = new GrassSite[side * side];
        var bare = new bool[side * side];
        Span<double> materials = stackalloc double[BiomeTable.MaterialCount];
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        biomes.Clear();
        for (int j = 0; j < side; j++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int i = 0; i < side; i++)
            {
                int v = (by + j) * n + bx + i;
                Vector3D position = grid.Vertex(v);
                Vector3D up = position.Normalized;
                var normal = new Vector3D(grid.Normals[3 * v], grid.Normals[3 * v + 1], grid.Normals[3 * v + 2]);
                double cos = Math.Abs(Vector3D.Dot(normal, up));
                double slope = Math.Sqrt(Math.Max(0, 1 - cos * cos));
                double temperature = grid.Climate[4 * v] / 255.0 * 70 - 30, moisture = grid.Climate[4 * v + 1] / 255.0;
                double grass = grid.Climate[4 * v + 2] / 255.0, canopy = grid.Climate[4 * v + 3] / 255.0;
                for (int m = 0; m < BiomeTable.MaterialCount; m++) materials[m] = grid.Materials[v * BiomeTable.MaterialCount + m] / 255.0;
                BiomeTable.FoldFamilies(materials);
                double aboveWater = grid.HasWater ? grid.Heights[v] - grid.WaterLevels[v] : 1e6;
                bare[j * side + i] = aboveWater < 0.12 || walking?.Monuments.At(up, 2) != null;
                GroundDetailCover detail = GroundDetailCover.At(new GroundCover(temperature, moisture, grass, canopy * 0.022),
                    slope, aboveWater, biomes, materials);
                sites[j * side + i] = new GrassSite(detail.Grass, detail.Litter, moisture, temperature, canopy, 0);
            }
        }

        // The tile's centre: the middle of its block on the drawn ground.
        Vector3D centre = Vector3D.Zero;
        foreach (int v in new[] { by * n + bx, by * n + bx + block, (by + block) * n + bx, (by + block) * n + bx + block })
            centre += grid.Vertex(v);
        centre *= 0.25;

        var lists = new List<Instance>[context.Shapes.Length];
        for (int s = 0; s < lists.Length; s++) lists[s] = new List<Instance>();
        var accents = new List<CoverPlant>();
        int cells = 0, clumps = 0;
        long cellBaseX = (long)job.Terrain.X * TerrainCells, cellBaseY = (long)job.Terrain.Y * TerrainCells;
        for (int cj = 0; cj < block; cj++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int ci = 0; ci < block; ci++)
            {
                int s00 = cj * side + ci, s10 = s00 + 1, s01 = s00 + side, s11 = s01 + 1;
                if (bare[s00] || bare[s10] || bare[s01] || bare[s11]) continue;
                int gi = bx + ci, gj = by + cj;
                for (int sub = 0; sub < 4; sub++)
                {
                    int a = sub & 1, b = sub >> 1;
                    long gx = (cellBaseX + gi) * 2 + a, gy = (cellBaseY + gj) * 2 + b;
                    var r = new Rng(Hash64.Of(JitterSalt, (ulong)face, unchecked((ulong)gx), unchecked((ulong)gy)));
                    double fx = (a + r.NextDouble()) * 0.5, fy = (b + r.NextDouble()) * 0.5;
                    double yaw = r.NextDouble() * DMath.TwoPi, phase = r.NextDouble(), palette = r.NextDouble();
                    double tintJitter = (r.NextDouble() - 0.5) * 0.16;
                    cells++;
                    GrassSite site = Lerp(sites[s00], sites[s10], sites[s01], sites[s11], fx, fy);
                    (double wu, double wv) = FaceMetres(grid, gi, gj, fx, fy);
                    double dryness = Math.Clamp(context.Tint.Dryness(wu, wv, site.Moisture) + tintJitter, 0, 1);
                    site = site with { Dryness = dryness };
                    GrassPlan plan = context.Habitats.Plan(site, face, gx, gy, wu, wv);
                    if (!plan.Grows && plan.Accent < 0 && !plan.Litter) continue;
                    Contact contact = Touch(grid, gi, gj, fx, fy);
                    Vector3D up = contact.Root.Normalized;
                    double brightness = context.Tint.Brightness(wu, wv);
                    if (plan.Accent >= 0)
                    {
                        accents.Add(new CoverPlant(contact.Root - up * 0.015, plan.Accent, (int)(palette * 3) % 3, (float)yaw,
                            (float)plan.AccentScale, (float)(0.8 + 0.2 * brightness), (float)plan.Rank));
                    }
                    if (plan.Litter && context.Litter >= 0 && plan.LitterRank < context.Density)
                        AddLitter(lists[context.Litter], context.Shapes[context.Litter], contact, centre, plan, yaw, palette, site.Canopy,
                            context.Density);
                    if (!plan.Grows || plan.Rank >= context.Density) continue;
                    clumps++;
                    AddClump(lists[plan.Species], context.Shapes[plan.Species], grid, gi, gj, fx, fy, contact, centre, plan,
                        context.Density, yaw, phase, palette, brightness, dryness, site.Canopy);
                }
            }
        }

        // Each species' instances by rank, so a distance's share is a prefix; buffers padded to their capacity.
        var species = new SpeciesBuild[lists.Length];
        Vector3D minimum = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        Vector3D maximum = -minimum;
        for (int s = 0; s < lists.Length; s++)
        {
            List<Instance> list = lists[s];
            list.Sort(static (x, y) => x.Rank != y.Rank ? x.Rank.CompareTo(y.Rank) : x.Order.CompareTo(y.Order));
            int capacity = list.Count == 0 ? 0 : CapacityFor(list.Count);
            var buffer = new float[capacity * Stride];
            var ranks = new float[list.Count];
            Vector3D low = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity), high = -low;
            for (int i = 0; i < list.Count; i++)
            {
                Instance instance = list[i];
                Array.Copy(instance.Data, 0, buffer, i * Stride, Stride);
                ranks[i] = instance.Rank;
                Vector3D m = new(instance.Margin, instance.Margin, instance.Margin);
                low = Min(low, instance.Root - m);
                high = Max(high, instance.Root + m);
            }
            if (list.Count == 0) low = high = Vector3D.Zero;
            else
            {
                minimum = Min(minimum, low);
                maximum = Max(maximum, high);
            }
            species[s] = new SpeciesBuild(buffer, ranks, list.Count, capacity, low, high);
        }
        if (double.IsInfinity(minimum.X))
        {
            double half = TileKey.EdgeLength(job.Key.Level, context.Surface.Radius) * 0.75;
            minimum = new Vector3D(-half, -half, -half);
            maximum = new Vector3D(half, half, half);
        }
        cancellation.ThrowIfCancellationRequested();
        return new TileBuild
        {
            Key = job.Key, Grid = grid, Centre = centre, Species = species, Accents = accents, Minimum = minimum,
            Maximum = maximum, Cells = cells, Clumps = clumps,
        };
    }

    private static Vector3D Min(Vector3D a, Vector3D b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    private static Vector3D Max(Vector3D a, Vector3D b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    private static GrassSite Lerp(in GrassSite a, in GrassSite b, in GrassSite c, in GrassSite d, double x, double y)
    {
        double L(double p, double q, double r, double s) => (p * (1 - x) + q * x) * (1 - y) + (r * (1 - x) + s * x) * y;
        return new GrassSite(L(a.Grass, b.Grass, c.Grass, d.Grass), L(a.Litter, b.Litter, c.Litter, d.Litter),
            L(a.Moisture, b.Moisture, c.Moisture, d.Moisture), L(a.Temperature, b.Temperature, c.Temperature, d.Temperature),
            L(a.Canopy, b.Canopy, c.Canopy, d.Canopy), 0);
    }

    // Metres along the face at a point of a grid cell (the terrain's own texture coordinates, so the colour's noise
    // matches the ground's under the clump).
    private static (double U, double V) FaceMetres(TileGrid grid, int i, int j, double x, double y)
    {
        int n = grid.Cells + 1, v = j * n + i;
        double U(int k) => grid.FaceMetres[2 * k];
        double W(int k) => grid.FaceMetres[2 * k + 1];
        double u = (U(v) * (1 - x) + U(v + 1) * x) * (1 - y) + (U(v + n) * (1 - x) + U(v + n + 1) * x) * y;
        double w = (W(v) * (1 - x) + W(v + 1) * x) * (1 - y) + (W(v + n) * (1 - x) + W(v + n + 1) * x) * y;
        return (u, w);
    }

    // A point on the drawn ground: the fine triangle's (TileMesh's diagonal, TerrainLod.TrySampleContact's convention),
    // the same point on the morph targets' triangle, and the fine triangle's normal.
    private readonly record struct Contact(Vector3D Root, Vector3D Morph, Vector3D Normal);

    private static Contact Touch(TileGrid grid, int i, int j, double x, double y)
    {
        i = Math.Clamp(i, 0, grid.Cells - 1);
        j = Math.Clamp(j, 0, grid.Cells - 1);
        x = Math.Clamp(x, 0, 1);
        y = Math.Clamp(y, 0, 1);
        int n = grid.Cells + 1, a = j * n + i;
        int b, c;
        double wa, wb, wc;
        if (y <= x)
        {
            b = a + 1; c = a + n + 1;
            wa = 1 - x; wb = x - y; wc = y;
        }
        else
        {
            b = a + n + 1; c = a + n;
            wa = 1 - y; wb = x; wc = y - x;
        }
        Vector3D pa = Fine(grid, a), pb = Fine(grid, b), pc = Fine(grid, c);
        Vector3D root = pa * wa + pb * wb + pc * wc;
        Vector3D morph = Morph(grid, a) * wa + Morph(grid, b) * wb + Morph(grid, c) * wc;
        Vector3D normal = Vector3D.Cross(pb - pa, pc - pa).Normalized;
        if (Vector3D.Dot(normal, root) < 0) normal = -normal;
        return new Contact(root, morph, normal);
    }

    private static Vector3D Fine(TileGrid grid, int v) =>
        grid.Origin + new Vector3D(grid.Positions[3 * v], grid.Positions[3 * v + 1], grid.Positions[3 * v + 2]);

    private static Vector3D Morph(TileGrid grid, int v) =>
        grid.Origin + new Vector3D(grid.MorphPositions[3 * v], grid.MorphPositions[3 * v + 1], grid.MorphPositions[3 * v + 2]);

    // The drawn ground's height over the root's tangent plane at a horizontal offset from it: the offset turned into the
    // cell's grid coordinates (the cell's own edges as axes), that point's fine triangle measured along up.
    private static double HeightAt(TileGrid grid, int i, int j, double x, double y, Vector3D root, Vector3D up,
        Vector3D eu, Vector3D ev, Vector3D offset)
    {
        // Solve offset = du eu + dv ev in the tangent plane (eu, ev without their parts along up).
        double a = Vector3D.Dot(eu, eu), b = Vector3D.Dot(eu, ev), c = Vector3D.Dot(ev, ev);
        double p = Vector3D.Dot(offset, eu), q = Vector3D.Dot(offset, ev);
        double det = a * c - b * b;
        if (Math.Abs(det) < 1e-12) return 0;
        double du = (p * c - q * b) / det, dv = (q * a - p * b) / det;
        double gx = i + x + du, gy = j + y + dv;
        int ii = (int)Math.Floor(gx), jj = (int)Math.Floor(gy);
        ii = Math.Clamp(ii, 0, grid.Cells - 1);
        jj = Math.Clamp(jj, 0, grid.Cells - 1);
        Contact c2 = Touch(grid, ii, jj, gx - ii, gy - jj);
        return Vector3D.Dot(c2.Root - root, up);
    }

    private static void AddClump(List<Instance> list, SpeciesShape shape, TileGrid grid, int i, int j, double x, double y,
        Contact contact, Vector3D centre, GrassPlan plan, float density, double yaw, double phase, double palette,
        double brightness, double dryness, double canopy)
    {
        Vector3D root = contact.Root;
        Vector3D up = root.Normalized;
        // The cell's own axes on the ground, then the clump's turn.
        int n = grid.Cells + 1, v = Math.Clamp(j, 0, grid.Cells - 1) * n + Math.Clamp(i, 0, grid.Cells - 1);
        Vector3D eu = Fine(grid, v + 1) - Fine(grid, v), ev = Fine(grid, v + n) - Fine(grid, v);
        eu -= up * Vector3D.Dot(eu, up);
        ev -= up * Vector3D.Dot(ev, up);
        Vector3D tu = eu.Normalized, tv = Vector3D.Cross(up, tu);
        double cs = DMath.Cos(yaw), sn = DMath.Sin(yaw);
        Vector3D ax = tu * cs + tv * sn;
        Vector3D az = Vector3D.Cross(ax, up);
        double width = plan.Width, height = plan.Height;

        // The footprint: the ground's slope over the clump's cards (four points at its reach), the plane through the root
        // that fits them, and how much its sides would float over a crest. A crest more than a few centimetres down at the
        // sides narrows the clump (the float falls with the square of the width) and sinks it by what is left.
        double reach = shape.Reach * width * 0.6;
        double hE = HeightAt(grid, i, j, x, y, root, up, eu, ev, ax * reach), hW = HeightAt(grid, i, j, x, y, root, up, eu, ev, -ax * reach);
        double hN = HeightAt(grid, i, j, x, y, root, up, eu, ev, az * reach), hS = HeightAt(grid, i, j, x, y, root, up, eu, ev, -az * reach);
        double gX = (hE - hW) / (2 * reach), gZ = (hN - hS) / (2 * reach);
        double crest = -Math.Min(Math.Min((hE + hW) * 0.5, (hN + hS) * 0.5), 0);
        double narrow = 1;
        if (crest > 0.05)
        {
            narrow = Math.Max(0.55, Math.Sqrt(0.05 / crest));
            crest *= narrow * narrow;
        }
        width *= narrow;
        // Two centimetres in, so a card's foot never shows as a straight line where the ground undulates under it.
        double sink = Math.Min(crest, 0.06) + 0.02;
        // Slopes too steep for a clump to stand on keep it upright and let the foot sink into the uphill side.
        gX = Math.Clamp(gX, -1.2, 1.2);
        gZ = Math.Clamp(gZ, -1.2, 1.2);

        Vector3D bx = (ax + up * gX) * width, by = up * height, bz = (az + up * gZ) * width;
        Vector3D origin = root - up * sink - centre;
        double morph = Vector3D.Dot(contact.Morph - contact.Root, up);
        float rank = (float)(plan.Rank / Math.Max(density, 1e-3));
        var data = new float[Stride];
        Write(data, bx, by, bz, origin);
        data[12] = (float)brightness;
        data[13] = (float)dryness;
        data[14] = (float)canopy;
        data[15] = 1;
        data[16] = (float)morph;
        data[17] = (float)phase;
        data[18] = rank;
        data[19] = (float)((plan.Variant + palette * 0.999) / 4);
        double margin = shape.Reach * width * 2.2 + shape.Height * height * 0.7 + Math.Abs(morph) + 0.1;
        margin = Math.Max(margin, shape.Height * height + Math.Abs(morph) + 0.2);
        list.Add(new Instance { Rank = rank, Order = list.Count, Root = origin, Margin = margin, Data = data });
    }

    // Litter lies flat on the ground's triangle, turned and sized by its own draws.
    private static void AddLitter(List<Instance> list, SpeciesShape shape, Contact contact, Vector3D centre, GrassPlan plan,
        double yaw, double palette, double canopy, float density)
    {
        float rank = (float)(plan.LitterRank / Math.Max(density, 1e-3));
        Vector3D up = contact.Normal;
        Vector3D radial = contact.Root.Normalized;
        Vector3D t = Vector3D.Cross(radial, new Vector3D(0.267, 0.534, 0.802));
        if (t.LengthSquared < 1e-9) t = Vector3D.Cross(radial, Vector3D.UnitX);
        t = (t - up * Vector3D.Dot(t, up)).Normalized;
        Vector3D s = Vector3D.Cross(up, t);
        double cs = DMath.Cos(yaw + 1.7), sn = DMath.Sin(yaw + 1.7);
        Vector3D ax = t * cs + s * sn;
        Vector3D az = Vector3D.Cross(ax, up);
        double size = 0.75 + 0.5 * palette;
        Vector3D origin = contact.Root + up * 0.004 - centre;
        var data = new float[Stride];
        Write(data, ax * size, up * size, az * size, origin);
        data[12] = 1;
        data[13] = 0;
        data[14] = (float)canopy;
        data[15] = 1;
        data[16] = (float)Vector3D.Dot(contact.Morph - contact.Root, radial);
        data[17] = (float)palette;
        data[18] = rank;
        data[19] = (float)((plan.LitterVariant + palette * 0.999) / 4);
        list.Add(new Instance { Rank = rank, Order = list.Count, Root = origin, Margin = shape.Reach * size + 0.2, Data = data });
    }

    // Godot's row-major 3 × 4 transform: each row holds the basis columns' components, then the origin's.
    private static void Write(float[] data, Vector3D x, Vector3D y, Vector3D z, Vector3D origin)
    {
        data[0] = (float)x.X; data[1] = (float)y.X; data[2] = (float)z.X; data[3] = (float)origin.X;
        data[4] = (float)x.Y; data[5] = (float)y.Y; data[6] = (float)z.Y; data[7] = (float)origin.Y;
        data[8] = (float)x.Z; data[9] = (float)y.Z; data[10] = (float)z.Z; data[11] = (float)origin.Z;
    }
}
