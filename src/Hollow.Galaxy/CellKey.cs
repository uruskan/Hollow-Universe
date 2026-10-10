using System;
using Hollow.Core;

namespace Hollow.Galaxy;

/// <summary>The sector grid (GALAXY.md §5).</summary>
public static class SectorGrid
{
    /// <summary>Edge of a sector in light-years.</summary>
    public const double SectorSize = 1024.0;

    /// <summary>Sectors per axis.</summary>
    public const int SectorsPerAxis = 128;

    /// <summary>Galactic position of the grid's minimum corner. Sol sits at the centre of sector (64, 64, 38).</summary>
    public static readonly Vector3D Origin = new(-66048.0, -66048.0, -39424.0);

    /// <summary>Edge of a cell at a level, in light-years: 1024 / 2^level.</summary>
    public static double CellSize(int level) => SectorSize / (1 << level);

    /// <summary>The sector containing a galactic position, or false if outside the grid.</summary>
    public static bool TrySectorOf(Vector3D position, out int sx, out int sy, out int sz)
    {
        Vector3D rel = position - Origin;
        double fx = Math.Floor(rel.X / SectorSize);
        double fy = Math.Floor(rel.Y / SectorSize);
        double fz = Math.Floor(rel.Z / SectorSize);
        sx = (int)fx;
        sy = (int)fy;
        sz = (int)fz;
        return fx >= 0 && fy >= 0 && fz >= 0 && fx < SectorsPerAxis && fy < SectorsPerAxis && fz < SectorsPerAxis;
    }

    /// <summary>Minimum corner of a sector in the galactic frame.</summary>
    public static Vector3D SectorMin(int sx, int sy, int sz) =>
        Origin + new Vector3D(sx * SectorSize, sy * SectorSize, sz * SectorSize);

    /// <summary>Centre of a sector in the galactic frame.</summary>
    public static Vector3D SectorCentre(int sx, int sy, int sz) =>
        SectorMin(sx, sy, sz) + new Vector3D(SectorSize / 2, SectorSize / 2, SectorSize / 2);

    /// <summary>Sector index used for names: x | y·128 | z·128².</summary>
    public static int SectorIndex(int sx, int sy, int sz) => sx | (sy << 7) | (sz << 14);
}

/// <summary>
/// One cell of the galaxy's octree: a level (0–7) and integer coordinates inside the grid.
/// Packs into the same bits as a <see cref="SystemAddress"/> with star index 0.
/// </summary>
public readonly struct CellKey : IEquatable<CellKey>, IComparable<CellKey>
{
    public readonly int Level;
    public readonly int SectorX;
    public readonly int SectorY;
    public readonly int SectorZ;
    public readonly int CellX;
    public readonly int CellY;
    public readonly int CellZ;

    public CellKey(int level, int sectorX, int sectorY, int sectorZ, int cellX, int cellY, int cellZ)
    {
        int cellsPerAxis = 1 << level;
        if (level < 0 || level >= StarClassTable.LevelCount
            || (uint)sectorX >= SectorGrid.SectorsPerAxis || (uint)sectorY >= SectorGrid.SectorsPerAxis
            || (uint)sectorZ >= SectorGrid.SectorsPerAxis
            || (uint)cellX >= cellsPerAxis || (uint)cellY >= cellsPerAxis || (uint)cellZ >= cellsPerAxis)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Cell coordinates out of range.");
        }
        Level = level;
        SectorX = sectorX;
        SectorY = sectorY;
        SectorZ = sectorZ;
        CellX = cellX;
        CellY = cellY;
        CellZ = cellZ;
    }

    /// <summary>The cell of a level containing a galactic position, or false if outside the grid.</summary>
    public static bool TryFromPosition(int level, Vector3D position, out CellKey key)
    {
        key = default;
        if (!SectorGrid.TrySectorOf(position, out int sx, out int sy, out int sz))
        {
            return false;
        }
        Vector3D local = position - SectorGrid.SectorMin(sx, sy, sz);
        double size = SectorGrid.CellSize(level);
        int max = (1 << level) - 1;
        int cx = Math.Clamp((int)Math.Floor(local.X / size), 0, max);
        int cy = Math.Clamp((int)Math.Floor(local.Y / size), 0, max);
        int cz = Math.Clamp((int)Math.Floor(local.Z / size), 0, max);
        key = new CellKey(level, sx, sy, sz, cx, cy, cz);
        return true;
    }

    /// <summary>Global integer coordinates of the cell at its level (sector × 2^level + cell).</summary>
    public long GlobalX => ((long)SectorX << Level) + CellX;
    public long GlobalY => ((long)SectorY << Level) + CellY;
    public long GlobalZ => ((long)SectorZ << Level) + CellZ;

    /// <summary>Builds a key from global integer coordinates at a level; false if outside the grid.</summary>
    public static bool TryFromGlobal(int level, long gx, long gy, long gz, out CellKey key)
    {
        key = default;
        long limit = (long)SectorGrid.SectorsPerAxis << level;
        if (gx < 0 || gy < 0 || gz < 0 || gx >= limit || gy >= limit || gz >= limit)
        {
            return false;
        }
        int mask = (1 << level) - 1;
        key = new CellKey(level, (int)(gx >> level), (int)(gy >> level), (int)(gz >> level),
            (int)(gx & mask), (int)(gy & mask), (int)(gz & mask));
        return true;
    }

    public double Size => SectorGrid.CellSize(Level);

    /// <summary>Minimum corner of the cell in the galactic frame.</summary>
    public Vector3D Min => SectorGrid.SectorMin(SectorX, SectorY, SectorZ) + new Vector3D(CellX * Size, CellY * Size, CellZ * Size);

    public Vector3D Centre => Min + new Vector3D(Size / 2, Size / 2, Size / 2);

    /// <summary>Index of the cell inside its sector: x | y·2^L | z·4^L.</summary>
    public long IndexInSector => (long)CellX | ((long)CellY << Level) | ((long)CellZ << (2 * Level));

    /// <summary>Number of low bits left for the star index in an address at this level.</summary>
    public int StarIndexBits => 39 - 3 * Level;

    /// <summary>The packed form (an address with star index 0).</summary>
    public ulong Packed
    {
        get
        {
            ulong v = (ulong)Level << 60;
            v |= (ulong)SectorX << 53 | (ulong)SectorY << 46 | (ulong)SectorZ << 39;
            int shift = 39 - Level;
            v |= (ulong)CellX << shift;
            shift -= Level;
            v |= (ulong)CellY << shift;
            shift -= Level;
            v |= (ulong)CellZ << shift;
            return v;
        }
    }

    public static CellKey FromPacked(ulong v)
    {
        int level = (int)((v >> 60) & 7);
        int sx = (int)((v >> 53) & 127);
        int sy = (int)((v >> 46) & 127);
        int sz = (int)((v >> 39) & 127);
        ulong mask = (1UL << level) - 1;
        int shift = 39 - level;
        int cx = (int)((v >> shift) & mask);
        shift -= level;
        int cy = (int)((v >> shift) & mask);
        shift -= level;
        int cz = (int)((v >> shift) & mask);
        return new CellKey(level, sx, sy, sz, cx, cy, cz);
    }

    public bool Equals(CellKey other) => Packed == other.Packed;

    public override bool Equals(object? obj) => obj is CellKey other && Equals(other);

    public override int GetHashCode() => Packed.GetHashCode();

    public int CompareTo(CellKey other) => Packed.CompareTo(other.Packed);

    public static bool operator ==(CellKey a, CellKey b) => a.Equals(b);

    public static bool operator !=(CellKey a, CellKey b) => !a.Equals(b);

    public override string ToString() =>
        $"L{Level} S({SectorX},{SectorY},{SectorZ}) C({CellX},{CellY},{CellZ})";
}

/// <summary>
/// A star system's permanent 64-bit address (GALAXY.md §6).
/// Procedural: level, sector, cell and star index. Catalogue: flag, catalogue number and catalogue ID.
/// </summary>
public readonly struct SystemAddress : IEquatable<SystemAddress>, IComparable<SystemAddress>
{
    public readonly ulong Value;

    public SystemAddress(ulong value)
    {
        Value = value;
    }

    public static SystemAddress Procedural(CellKey cell, long starIndex)
    {
        if (starIndex < 0 || (ulong)starIndex >= 1UL << cell.StarIndexBits)
        {
            throw new ArgumentOutOfRangeException(nameof(starIndex));
        }
        return new SystemAddress(cell.Packed | (ulong)starIndex);
    }

    public static SystemAddress Catalogue(int catalogue, long id)
    {
        if ((uint)catalogue > 127 || id < 0 || id >= 1L << 56)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }
        return new SystemAddress(1UL << 63 | (ulong)catalogue << 56 | (ulong)id);
    }

    public bool IsCatalogue => (Value >> 63) != 0;

    public int Level => (int)((Value >> 60) & 7);

    /// <summary>The procedural cell (only for procedural addresses).</summary>
    public CellKey Cell => CellKey.FromPacked(Value & ~((1UL << (39 - 3 * Level)) - 1) & ~(1UL << 63));

    /// <summary>Star index inside the cell (only for procedural addresses).</summary>
    public long StarIndex => (long)(Value & ((1UL << (39 - 3 * Level)) - 1));

    public int CatalogueNumber => (int)((Value >> 56) & 0x7F);

    public long CatalogueId => (long)(Value & ((1UL << 56) - 1));

    public bool Equals(SystemAddress other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is SystemAddress other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public int CompareTo(SystemAddress other) => Value.CompareTo(other.Value);

    public static bool operator ==(SystemAddress a, SystemAddress b) => a.Value == b.Value;

    public static bool operator !=(SystemAddress a, SystemAddress b) => a.Value != b.Value;

    public override string ToString() => $"0x{Value:X16}";
}
