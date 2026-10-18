using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    // A cell's identity stays (face, global X, global Y, layer). The compact array index is never a seed.
    // Bounds and buffers use captured render axes; this worker has no native object or Node references.
    private sealed record SpatialTileData(int FaceIndex, int CellX, int CellY, int MinX, int MinY, int MaxX, int MaxY,
        Vector3D Minimum, Vector3D Maximum, float[] Grass, float[] Litter, int CandidateCells)
    {
        public long BufferBytes => ((long)Grass.Length + Litter.Length) * sizeof(float);
    }

    private static void BuildSpatialTiles(BuildContext context, FieldData field, CancellationToken cancellation)
    {
        int n = context.LatticeSize, tileCells = context.SpatialCells;
        for (int f = 0; f < field.Faces.Count; f++)
        {
            FaceLattice face = field.Faces[f];
            long minX = Math.Max(0, face.CornerX), minY = Math.Max(0, face.CornerY);
            long maxX = Math.Min(context.FaceCells, face.CornerX + n - 1);
            long maxY = Math.Min(context.FaceCells, face.CornerY + n - 1);
            for (long ty = minY / tileCells * tileCells; ty < maxY; ty += tileCells)
            for (long tx = minX / tileCells * tileCells; tx < maxX; tx += tileCells)
            {
                cancellation.ThrowIfCancellationRequested();
                int x0 = checked((int)(Math.Max(tx, minX) - face.CornerX));
                int y0 = checked((int)(Math.Max(ty, minY) - face.CornerY));
                int x1 = checked((int)(Math.Min(tx + tileCells, maxX) - face.CornerX));
                int y1 = checked((int)(Math.Min(ty + tileCells, maxY) - face.CornerY));
                int tileX = checked((int)(tx - face.CornerX)), tileY = checked((int)(ty - face.CornerY));
                var grass = new List<float>();
                var litter = new List<float>();
                Vector3D minimum = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
                Vector3D maximum = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int a = (y * n + x) * 4, b = a + 4, c = a + n * 4, d = c + 4;
                    long gx = face.CornerX + x, gy = face.CornerY + y;
                    // Grass has one habitat/quality gate. Its unknown GPU patch noise is bounded above; keep
                    // every possible survivor, including float threshold boundaries. Litter retains its old gate.
                    float maximumDensity = Math.Max(Math.Max(face.Nodes[a + 3], face.Nodes[b + 3]),
                        Math.Max(face.Nodes[c + 3], face.Nodes[d + 3]));
                    bool blade = SpatialGrassMayGrow(face.Face, gx, gy, context.Density, maximumDensity);
                    bool debris = face.Cover[a + 2] > 0 && SpatialQualityKeeps(face.Face, gx, gy, 1, context.Density);
                    if (!blade && !debris) continue;
                    if (blade) AddSpatialCell(grass, x - tileX, y - tileY);
                    if (debris) AddSpatialCell(litter, x - tileX, y - tileY);
                    Include(a); Include(b); Include(c); Include(d);
                }
                if (grass.Count == 0 && litter.Count == 0) continue;
                field.SpatialTiles.Add(new(f, tileX, tileY, x0, y0, x1, y1, minimum, maximum,
                    grass.ToArray(), litter.ToArray(), (x1 - x0) * (y1 - y0)));

                void Include(int p)
                {
                    double x = face.Nodes[p], y = face.Nodes[p + 1], z = face.Nodes[p + 2];
                    if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
                        throw new InvalidOperationException("A spatial grass support bound is not finite.");
                    minimum = new(Math.Min(minimum.X, x), Math.Min(minimum.Y, y), Math.Min(minimum.Z, z));
                    maximum = new(Math.Max(maximum.X, x), Math.Max(maximum.Y, y), Math.Max(maximum.Z, z));
                }
            }
        }
    }

    private static void AddSpatialCell(List<float> buffer, int x, int y)
    {
        // Godot's row-major 3x4 identity transform followed by custom RGBA. Small cell integers are exact floats.
        buffer.Add(1); buffer.Add(0); buffer.Add(0); buffer.Add(0);
        buffer.Add(0); buffer.Add(1); buffer.Add(0); buffer.Add(0);
        buffer.Add(0); buffer.Add(0); buffer.Add(1); buffer.Add(0);
        buffer.Add(x); buffer.Add(y); buffer.Add(0); buffer.Add(0);
    }

    private static uint SpatialHash(uint x)
    {
        unchecked
        {
            x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
            return x;
        }
    }

    private static uint SpatialCoverageState(int face, long x, long y, int layer)
    {
        uint s;
        unchecked { s = SpatialHash((uint)x * 0x9e3779b9u ^ SpatialHash((uint)y + 0x632be5abu)
            ^ (uint)layer * 0x51ed27u ^ (uint)face * 0x27d4eb2fu); }
        return SpatialHash(SpatialHash(SpatialHash(s))); // two jitter draws, then the coverage roll
    }

    private static bool SpatialGrassMayGrow(int face, long x, long y, float quality, float maximumDensity)
    {
        // Bilinear density cannot exceed its largest corner. Patch modulation is a mix of [0.25,1.75] and 1,
        // so min(1, maximum * 1.75) bounds the exact shader gate without duplicating texture sampling on the CPU.
        if (maximumDensity <= 0) return false;
        float upper = Math.Clamp(maximumDensity * 1.75f, 0, 1) * Math.Clamp(quality, 0.1f, 1f);
        float roll = (SpatialCoverageState(face, x, y, 0) >> 8) / 16777216f;
        return roll <= upper + 0.0000002f;
    }

    private static bool SpatialQualityKeeps(int face, long x, long y, int layer, float density)
    {
        uint s = SpatialCoverageState(face, x, y, layer);
        float keep = (SpatialHash(s ^ 0xA511E9B3u) & 0xFFFFu) / 65535f;
        // Keep the boundary band for the GPU's own comparison rather than assume identical float division.
        return keep <= Math.Clamp(density, 0.1f, 1f) + 0.0000002f;
    }
}
