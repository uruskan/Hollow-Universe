using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Galaxy;

namespace Hollow.GeneratorReport;

/// <summary>
/// Galaxy-wide maps of what the generator makes, one tile per level (GALAXY.md §7.1): generated stars seen face-on
/// (brightness from surface density, colour from the mix of star classes), the ratio of generated stars to a fine
/// integral of the density model (to show steps between cells or a biased sampler), and an edge-on slice. Meant to
/// be looked at for artefacts before the generator is frozen.
/// </summary>
public static class Maps
{
    private const int Tile = 512; // face-on tiles: 512 × 512 pixels of 256 ly over the whole sector grid
    private const double Pixel = SectorGrid.SectorSize * SectorGrid.SectorsPerAxis / Tile;
    private const int EdgeWidth = 1024; // edge-on tiles: 1024 × 128 pixels of 128 ly
    private const int EdgeHeight = 128;
    private const double EdgePixel = SectorGrid.SectorSize * SectorGrid.SectorsPerAxis / EdgeWidth;
    private const int Block = 8; // ratio map: 8 × 8 face-on pixels (2,048 ly) per block, to beat Poisson noise

    private sealed class Layer
    {
        public readonly double[] Count;
        public readonly double[] R;
        public readonly double[] G;
        public readonly double[] B;
        public readonly double[] Expected;

        /// <summary>How many stars each generated star stands for (more than 1 where only a sample of cells is made).</summary>
        public double Weight = 1;

        public Layer(int pixels)
        {
            Count = new double[pixels];
            R = new double[pixels];
            G = new double[pixels];
            B = new double[pixels];
            Expected = new double[pixels];
        }

        public void Add(int pixel, double weight, (double R, double G, double B) colour)
        {
            Count[pixel] += weight;
            R[pixel] += weight * colour.R;
            G[pixel] += weight * colour.G;
            B[pixel] += weight * colour.B;
        }
    }

    /// <summary>Half-thickness of the face-on slab per level: thick enough to hold most of each level's stars.</summary>
    private static double SlabHalf(int level) => level <= 2 ? 1024 : level <= 4 ? 256 : 64;

    /// <summary>Half-thickness of the edge-on slice per level (levels 0–3 only).</summary>
    private static double SliceHalf(int level) => SectorGrid.CellSize(level) / 2;

    public static void Render(string outDir, string prefix, int maxLevel = StarClassTable.LevelCount - 1)
    {
        Directory.CreateDirectory(outDir);
        var galaxy = new Galaxy.Galaxy(null); // the catalogue never changes procedural stars
        var faceOn = new byte[Tile * 4 * Tile * 2 * 3];
        var ratio = new byte[Tile * 4 * Tile * 2 * 3];
        var edgeOn = new byte[EdgeWidth * EdgeHeight * 4 * 3];
        var total = Stopwatch.StartNew();
        for (int level = 0; level <= maxLevel; level++)
        {
            var watch = Stopwatch.StartNew();
            Layer layer = FaceOn(galaxy, level);
            ExpectedFaceOn(galaxy.Density, level, layer);
            Blit(faceOn, Tile * 4, Paint(layer.Count, layer.R, layer.G, layer.B, Tile, Tile), Tile, Tile, (level % 4) * Tile, (level / 4) * Tile);
            Blit(ratio, Tile * 4, PaintRatio(layer), Tile, Tile, (level % 4) * Tile, (level / 4) * Tile);
            double stars = 0;
            double expected = 0;
            foreach (double c in layer.Count)
            {
                stars += c;
            }
            foreach (double e in layer.Expected)
            {
                expected += e;
            }
            (double chi2, int blocks) = ChiSquare(layer);
            Console.WriteLine($"level {level}: face-on {stars:E3} stars (expected {expected:E3}, ratio {stars / expected:0.000}); " +
                $"χ²/block {chi2:0.00} over {blocks} blocks (1 = Poisson noise only); {watch.Elapsed.TotalSeconds:0.0} s");
            if (level < 4)
            {
                Layer edge = EdgeOn(galaxy, level);
                Blit(edgeOn, EdgeWidth, Paint(edge.Count, edge.R, edge.G, edge.B, EdgeWidth, EdgeHeight), EdgeWidth, EdgeHeight, 0, level * EdgeHeight);
            }
        }
        Png.Write(Path.Combine(outDir, prefix + "face-on.png"), Tile * 4, Tile * 2, faceOn);
        Png.Write(Path.Combine(outDir, prefix + "ratio.png"), Tile * 4, Tile * 2, ratio);
        Png.Write(Path.Combine(outDir, prefix + "edge-on.png"), EdgeWidth, EdgeHeight * 4, edgeOn);
        Console.WriteLine($"maps written to {outDir} in {total.Elapsed.TotalSeconds:0} s");
    }

    // ---------------------------------------------------------------- face-on

    private static Layer FaceOn(Galaxy.Galaxy galaxy, int level)
    {
        var layer = new Layer(Tile * Tile);
        double size = SectorGrid.CellSize(level);
        double half = SlabHalf(level);
        long y0 = (long)Math.Floor((-half - SectorGrid.Origin.Y) / size);
        long y1 = (long)Math.Floor((half - SectorGrid.Origin.Y) / size);
        if (size >= Pixel)
        {
            // Big cells: generate every cell in the slab; each column of cells covers its own columns of pixels.
            long perAxis = (long)SectorGrid.SectorsPerAxis << level;
            Parallel.For(0, (int)perAxis, gx =>
            {
                for (long gz = 0; gz < perAxis; gz++)
                {
                    for (long gy = y0; gy <= y1; gy++)
                    {
                        if (!CellKey.TryFromGlobal(level, gx, gy, gz, out CellKey cell))
                        {
                            continue;
                        }
                        foreach (GalaxyStar s in galaxy.GenerateProcedural(cell))
                        {
                            if (Math.Abs(s.Position.Y) <= half)
                            {
                                layer.Add(FacePixel(s.Position), 1, StarColour(s));
                            }
                        }
                    }
                }
            });
        }
        else
        {
            // Small cells: one column of cells at each pixel's centre stands for the whole pixel.
            double weight = Pixel / size * (Pixel / size);
            layer.Weight = weight;
            Parallel.For(0, Tile, px =>
            {
                for (int pz = 0; pz < Tile; pz++)
                {
                    double x = SectorGrid.Origin.X + (px + 0.5) * Pixel;
                    double z = SectorGrid.Origin.Z + (pz + 0.5) * Pixel;
                    for (long gy = y0; gy <= y1; gy++)
                    {
                        double y = SectorGrid.Origin.Y + (gy + 0.5) * size;
                        if (!CellKey.TryFromPosition(level, new Vector3D(x, y, z), out CellKey cell))
                        {
                            continue;
                        }
                        foreach (GalaxyStar s in galaxy.GenerateProcedural(cell))
                        {
                            if (Math.Abs(s.Position.Y) <= half)
                            {
                                layer.Add(Index(px, pz), weight, StarColour(s));
                            }
                        }
                    }
                }
            });
        }
        return layer;
    }

    // The density model's expected count of this level's stars in each pixel's slab, from a fine integral over the
    // area the stars came from: the whole pixel, or for sampled levels the one column of cells at its centre
    // (scaled up by the same weight as the stars).
    private static void ExpectedFaceOn(DensityModel density, int level, Layer layer)
    {
        double half = SlabHalf(level);
        int steps = level <= 2 ? 64 : 16;
        double dy = 2 * half / steps;
        double size = SectorGrid.CellSize(level);
        bool sampled = size < Pixel;
        double width = sampled ? size : Pixel;
        Parallel.For(0, Tile, px =>
        {
            Span<double> components = stackalloc double[DensityModel.ComponentCount];
            for (int pz = 0; pz < Tile; pz++)
            {
                double x0 = SectorGrid.Origin.X + px * Pixel;
                double z0 = SectorGrid.Origin.Z + pz * Pixel;
                if (sampled)
                {
                    // The footprint of the cell column that contains the pixel's centre.
                    x0 = SectorGrid.Origin.X + Math.Floor((px + 0.5) * Pixel / size) * size;
                    z0 = SectorGrid.Origin.Z + Math.Floor((pz + 0.5) * Pixel / size) * size;
                }
                double sum = 0;
                for (int sx = 0; sx < 2; sx++)
                {
                    for (int sz = 0; sz < 2; sz++)
                    {
                        double x = x0 + (0.25 + 0.5 * sx) * width;
                        double z = z0 + (0.25 + 0.5 * sz) * width;
                        for (int k = 0; k < steps; k++)
                        {
                            density.Evaluate(new Vector3D(x, -half + (k + 0.5) * dy, z), components);
                            for (int c = 0; c < DensityModel.ComponentCount; c++)
                            {
                                sum += components[c] * StarClassTable.Share(DensityModel.PopulationOf((GalaxyComponent)c), level);
                            }
                        }
                    }
                }
                layer.Expected[Index(px, pz)] = sum / 4 * dy * width * width * layer.Weight;
            }
        });
    }

    // Image row 0 is the far side of the galaxy (high z), so the Galactic Centre is above Sol, as on the map.
    private static int FacePixel(Vector3D p)
    {
        int px = Math.Clamp((int)Math.Floor((p.X - SectorGrid.Origin.X) / Pixel), 0, Tile - 1);
        int pz = Math.Clamp((int)Math.Floor((p.Z - SectorGrid.Origin.Z) / Pixel), 0, Tile - 1);
        return Index(px, pz);
    }

    private static int Index(int px, int pz) => (Tile - 1 - pz) * Tile + px;

    // ---------------------------------------------------------------- edge-on

    // The y–z plane through Sol and the Galactic Centre (|x| within half a cell), Galactic Centre to the right.
    private static Layer EdgeOn(Galaxy.Galaxy galaxy, int level)
    {
        var layer = new Layer(EdgeWidth * EdgeHeight);
        double size = SectorGrid.CellSize(level);
        double half = SliceHalf(level);
        double top = EdgeHeight * EdgePixel / 2;
        long x0 = (long)Math.Floor((-half - SectorGrid.Origin.X) / size);
        long x1 = (long)Math.Floor((half - SectorGrid.Origin.X) / size);
        long y0 = (long)Math.Floor((-top - SectorGrid.Origin.Y) / size);
        long y1 = (long)Math.Floor((top - SectorGrid.Origin.Y) / size);
        long perAxis = (long)SectorGrid.SectorsPerAxis << level;
        Parallel.For(0, (int)perAxis, gz =>
        {
            for (long gx = x0; gx <= x1; gx++)
            {
                for (long gy = y0; gy <= y1; gy++)
                {
                    if (!CellKey.TryFromGlobal(level, gx, gy, gz, out CellKey cell))
                    {
                        continue;
                    }
                    foreach (GalaxyStar s in galaxy.GenerateProcedural(cell))
                    {
                        if (Math.Abs(s.Position.X) > half || Math.Abs(s.Position.Y) >= top)
                        {
                            continue;
                        }
                        int pz = Math.Clamp((int)Math.Floor((s.Position.Z - SectorGrid.Origin.Z) / EdgePixel), 0, EdgeWidth - 1);
                        int py = Math.Clamp((int)Math.Floor((top - s.Position.Y) / EdgePixel), 0, EdgeHeight - 1);
                        lock (layer)
                        {
                            layer.Add(py * EdgeWidth + pz, 1, StarColour(s));
                        }
                    }
                }
            }
        });
        return layer;
    }

    // ---------------------------------------------------------------- painting

    // Brightness from log surface density (the densest 0.2% saturate), colour from the stars' mean colour.
    private static byte[] Paint(double[] count, double[] r, double[] g, double[] b, int width, int height)
    {
        var image = new byte[width * height * 3];
        var nonZero = new List<double>();
        foreach (double c in count)
        {
            if (c > 0)
            {
                nonZero.Add(c);
            }
        }
        if (nonZero.Count == 0)
        {
            return image;
        }
        nonZero.Sort();
        double top = Math.Log10(nonZero[(int)(nonZero.Count * 0.998)] + 1);
        double floor = Math.Log10(nonZero[0] + 1);
        for (int i = 0; i < count.Length; i++)
        {
            if (count[i] <= 0)
            {
                continue;
            }
            double v = Math.Clamp((Math.Log10(count[i] + 1) - floor) / Math.Max(top - floor, 1e-9), 0, 1);
            double brightness = 0.12 + 0.88 * v;
            double cr = r[i] / count[i];
            double cg = g[i] / count[i];
            double cb = b[i] / count[i];
            double max = Math.Max(cr, Math.Max(cg, cb));
            image[i * 3] = (byte)Math.Clamp(255 * brightness * cr / max, 0, 255);
            image[i * 3 + 1] = (byte)Math.Clamp(255 * brightness * cg / max, 0, 255);
            image[i * 3 + 2] = (byte)Math.Clamp(255 * brightness * cb / max, 0, 255);
        }
        return image;
    }

    // Generated / expected per 8 × 8-pixel block: grey = 1, red = more stars than the model, blue = fewer (full
    // colour at a factor of 1.5); black where fewer than 30 generated stars are expected in the block's sample.
    private static byte[] PaintRatio(Layer layer)
    {
        var image = new byte[Tile * Tile * 3];
        for (int by = 0; by < Tile / Block; by++)
        {
            for (int bx = 0; bx < Tile / Block; bx++)
            {
                double n = 0;
                double e = 0;
                for (int y = by * Block; y < (by + 1) * Block; y++)
                {
                    for (int x = bx * Block; x < (bx + 1) * Block; x++)
                    {
                        n += layer.Count[y * Tile + x];
                        e += layer.Expected[y * Tile + x];
                    }
                }
                (byte R, byte G, byte B) colour = (0, 0, 0);
                if (e / layer.Weight >= 30)
                {
                    double t = Math.Clamp(Math.Log(n / e + 1e-9) / Math.Log(1.5), -1, 1);
                    colour = t >= 0
                        ? ((byte)(128 + 127 * t), (byte)(128 - 100 * t), (byte)(128 - 100 * t))
                        : ((byte)(128 + 100 * t), (byte)(128 + 100 * t), (byte)(128 - 127 * t));
                }
                for (int y = by * Block; y < (by + 1) * Block; y++)
                {
                    for (int x = bx * Block; x < (bx + 1) * Block; x++)
                    {
                        int i = (y * Tile + x) * 3;
                        image[i] = colour.R;
                        image[i + 1] = colour.G;
                        image[i + 2] = colour.B;
                    }
                }
            }
        }
        return image;
    }

    // Mean of (N − E)² / Var over the blocks with at least 30 expected generated stars, away from Sol's catalogue
    // region (2 blocks around Sol are skipped). Var = weight × E for a sampled level. Pure Poisson noise gives 1.
    private static (double ChiSquare, int Blocks) ChiSquare(Layer layer)
    {
        double sum = 0;
        int blocks = 0;
        int solX = (int)((0 - SectorGrid.Origin.X) / Pixel) / Block;
        int solY = (Tile - 1 - (int)((0 - SectorGrid.Origin.Z) / Pixel)) / Block;
        for (int by = 0; by < Tile / Block; by++)
        {
            for (int bx = 0; bx < Tile / Block; bx++)
            {
                if (Math.Abs(bx - solX) <= 2 && Math.Abs(by - solY) <= 2)
                {
                    continue;
                }
                double n = 0;
                double e = 0;
                for (int y = by * Block; y < (by + 1) * Block; y++)
                {
                    for (int x = bx * Block; x < (bx + 1) * Block; x++)
                    {
                        n += layer.Count[y * Tile + x];
                        e += layer.Expected[y * Tile + x];
                    }
                }
                if (e / layer.Weight < 30)
                {
                    continue;
                }
                sum += (n - e) * (n - e) / (layer.Weight * e);
                blocks++;
            }
        }
        return (blocks > 0 ? sum / blocks : 0, blocks);
    }

    private static void Blit(byte[] target, int targetWidth, byte[] source, int width, int height, int left, int top)
    {
        for (int y = 0; y < height; y++)
        {
            Array.Copy(source, y * width * 3, target, ((top + y) * targetWidth + left) * 3, width * 3);
        }
    }

    // Approximate colour of a blackbody (Tanner Helland's fit), 0–1 per channel. Remnants without light are violet.
    private static (double R, double G, double B) StarColour(in GalaxyStar star)
    {
        double t = star.Physics.Temperature;
        if (star.Class.Kind is StarKind.BlackHole || t <= 0)
        {
            return (0.6, 0.4, 1.0);
        }
        t = Math.Clamp(t, 1000, 40000) / 100;
        double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        return (Math.Clamp(r, 0, 255) / 255, Math.Clamp(g, 0, 255) / 255, Math.Clamp(b, 0, 255) / 255);
    }
}
