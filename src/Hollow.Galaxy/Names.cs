using System;
using System.Globalization;
using System.Text;

namespace Hollow.Galaxy;

/// <summary>
/// Sector names (GALAXY.md §9): two words from original syllable tables, exactly one name per sector, and
/// every name parses back to its sector. No table entry is a prefix of another entry of the same table, so a
/// word splits into its syllables in only one way.
/// </summary>
public static class SectorNames
{
    private static readonly string[] Onset1 =
    {
        "Va", "Ke", "Lo", "Thi", "Mar", "Sel", "Dra", "Or", "Ul", "Ny", "Che", "Is", "Bra", "Fen", "Gal", "Hes",
        "Ja", "Kor", "Lin", "Mo", "Nar", "Os", "Pel", "Qua", "Rin", "Sa", "Tor", "Ume", "Vel", "Wy", "Xa", "Yr",
        "Zan", "Ae", "Bel", "Cor", "Dun", "Eth", "Fai", "Gro", "Hal", "Ith", "Jor", "Kae", "Lum", "Mir", "Nol", "Oth",
        "Pra", "Ros", "Shi", "Tal", "Una", "Vor", "Wen", "Xe", "Ya", "Zo", "Ar", "Bri", "Cy", "Dal", "Ere", "Fro",
    };

    private static readonly string[] Coda1 =
    {
        "ren", "vos", "dar", "mis", "thar", "lon", "vek", "sin", "nor", "cas", "dris", "mon", "lys", "ther", "gan", "rok",
        "tis", "vale", "dun", "nash", "ril", "bos", "kesh", "mar", "tov", "lin", "zor", "pha", "quil", "ros", "sek", "tan",
        "ver", "wyn", "xis", "yor", "zel", "bar", "cor", "dek", "fal", "gor", "hal", "jin", "kor", "lam", "mel", "nim",
        "pel", "rak", "sol", "tum", "ush", "vin", "wal", "xon", "yth", "zan", "ard", "esk", "ion", "orn", "uld", "ath",
    };

    private static readonly string[] Onset2 =
    {
        "Al", "Bes", "Cel", "Dor", "Em", "Far", "Gil", "Hon", "Ir", "Jas", "Kel", "Lor", "Mun", "Nem", "Ost", "Per",
        "Quin", "Ras", "Sor", "Tem", "Ur", "Vas", "Wes", "Xan", "Yel", "Zer", "Ash", "Bor", "Cad", "Del", "Evr", "Gar",
    };

    private static readonly string[] Coda2 =
    {
        "ia", "on", "ar", "is", "um", "eth", "al", "or", "en", "ys", "ax", "ire", "ova", "und", "esh", "ith",
    };

    private const int Bits = 21;
    private const uint Mask = (1u << Bits) - 1;

    // Odd multipliers: multiplication modulo 2^21 is a bijection. With the xor-shifts in between, every output
    // bit depends on every input bit, so neighbouring sectors get unrelated names.
    private const uint MultiplierA = 0x1A3C5B;
    private const uint MultiplierB = 0x0C6D39;
    private static readonly uint InverseA = ComputeInverse(MultiplierA);
    private static readonly uint InverseB = ComputeInverse(MultiplierB);

    public static int TableSize => Onset1.Length * Coda1.Length * Onset2.Length * Coda2.Length;

    /// <summary>The name of the sector at grid coordinates (sx, sy, sz).</summary>
    public static string Name(int sx, int sy, int sz) => NameOfIndex(SectorGrid.SectorIndex(sx, sy, sz));

    /// <summary>The name of a sector index (0 … 2^21 − 1).</summary>
    public static string NameOfIndex(int index)
    {
        uint j = Scramble((uint)index & Mask);
        int a = (int)(j & 63);
        int b = (int)((j >> 6) & 63);
        int c = (int)((j >> 12) & 31);
        int d = (int)((j >> 17) & 15);
        return Onset1[a] + Coda1[b] + " " + Onset2[c] + Coda2[d];
    }

    /// <summary>Parses a sector name (case-insensitive). Returns false if it isn't a valid sector name.</summary>
    public static bool TryParse(string name, out int sx, out int sy, out int sz)
    {
        sx = sy = sz = 0;
        string[] words = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 2)
        {
            return false;
        }
        if (!TrySplit(words[0], Onset1, Coda1, out int a, out int b) || !TrySplit(words[1], Onset2, Coda2, out int c, out int d))
        {
            return false;
        }
        uint j = (uint)(a | b << 6 | c << 12 | d << 17);
        uint index = Unscramble(j);
        sx = (int)(index & 127);
        sy = (int)((index >> 7) & 127);
        sz = (int)((index >> 14) & 127);
        return true;
    }

    private static bool TrySplit(string word, string[] onsets, string[] codas, out int onset, out int coda)
    {
        onset = coda = -1;
        for (int i = 0; i < onsets.Length; i++)
        {
            if (!word.StartsWith(onsets[i], StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string rest = word.Substring(onsets[i].Length);
            for (int k = 0; k < codas.Length; k++)
            {
                if (string.Equals(rest, codas[k], StringComparison.OrdinalIgnoreCase))
                {
                    onset = i;
                    coda = k;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>A bijection on 21-bit values (exposed for tests).</summary>
    public static uint Scramble(uint x)
    {
        x ^= x >> 10;
        x = (uint)(((ulong)x * MultiplierA) & Mask);
        x ^= x >> 7;
        x = (uint)(((ulong)x * MultiplierB) & Mask);
        x ^= x >> 11;
        return x;
    }

    /// <summary>Inverse of <see cref="Scramble"/>.</summary>
    public static uint Unscramble(uint x)
    {
        x = UndoXorShift(x, 11);
        x = (uint)(((ulong)x * InverseB) & Mask);
        x = UndoXorShift(x, 7);
        x = (uint)(((ulong)x * InverseA) & Mask);
        x = UndoXorShift(x, 10);
        return x;
    }

    private static uint UndoXorShift(uint y, int shift)
    {
        uint x = y;
        for (int s = shift; s < Bits; s += shift)
        {
            x = y ^ (x >> shift);
        }
        return x & Mask;
    }

    private static uint ComputeInverse(uint odd)
    {
        ulong inv = odd;
        for (int i = 0; i < 6; i++)
        {
            inv *= 2 - odd * inv;
        }
        return (uint)(inv & Mask);
    }
}

/// <summary>
/// Procedural system names: <c>&lt;Sector&gt; &lt;Cell&gt; &lt;Level&gt;-&lt;Index&gt;</c>, e.g.
/// "Lumtum Delis FF-WL 6-0" (GALAXY.md §9); in a named region, the region's name replaces the sector's.
/// </summary>
public static class SystemNames
{
    private static readonly int[] CellLetters = { 0, 1, 2, 2, 3, 4, 4, 5 };

    public static string Format(SystemAddress address) => Format(address, null);

    /// <summary>
    /// The name with <paramref name="prefix"/> in place of the sector's name: a named region's systems (GALAXY.md §9),
    /// e.g. "Pleiades LA-YS 6-0".
    /// </summary>
    public static string Format(SystemAddress address, string? prefix)
    {
        if (address.IsCatalogue)
        {
            throw new ArgumentException("Catalogue systems use their catalogue names.", nameof(address));
        }
        CellKey cell = address.Cell;
        var sb = new StringBuilder(40);
        sb.Append(prefix ?? SectorNames.Name(cell.SectorX, cell.SectorY, cell.SectorZ));
        sb.Append(' ');
        if (cell.Level > 0)
        {
            sb.Append(CellCode(cell.IndexInSector, cell.Level));
            sb.Append(' ');
        }
        sb.Append(cell.Level.ToString(CultureInfo.InvariantCulture));
        sb.Append('-');
        sb.Append(address.StarIndex.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    public static bool TryParse(string name, out SystemAddress address)
    {
        address = default;
        string[] tokens = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3)
        {
            return false;
        }
        string last = tokens[^1];
        int dash = last.IndexOf('-', StringComparison.Ordinal);
        if (dash != 1 || !char.IsDigit(last[0]))
        {
            return false;
        }
        int level = last[0] - '0';
        if (level >= StarClassTable.LevelCount
            || !long.TryParse(last.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out long index))
        {
            return false;
        }
        int sectorWords = level == 0 ? tokens.Length - 1 : tokens.Length - 2;
        if (sectorWords != 2)
        {
            return false;
        }
        if (!SectorNames.TryParse(tokens[0] + " " + tokens[1], out int sx, out int sy, out int sz))
        {
            return false;
        }
        long cellIndex = 0;
        if (level > 0 && !TryParseCellCode(tokens[2], level, out cellIndex))
        {
            return false;
        }
        int mask = (1 << level) - 1;
        int cx = (int)(cellIndex & mask);
        int cy = (int)((cellIndex >> level) & mask);
        int cz = (int)((cellIndex >> (2 * level)) & mask);
        var cell = new CellKey(level, sx, sy, sz, cx, cy, cz);
        if (index < 0 || (ulong)index >= 1UL << cell.StarIndexBits)
        {
            return false;
        }
        address = SystemAddress.Procedural(cell, index);
        return true;
    }

    private static string CellCode(long index, int level)
    {
        int width = CellLetters[level];
        Span<char> letters = stackalloc char[width];
        for (int i = width - 1; i >= 0; i--)
        {
            letters[i] = (char)('A' + (int)(index % 26));
            index /= 26;
        }
        string code = new string(letters);
        return width >= 3 ? code.Substring(0, 2) + "-" + code.Substring(2) : code;
    }

    private static bool TryParseCellCode(string code, int level, out long index)
    {
        index = 0;
        string letters = code.Replace("-", "", StringComparison.Ordinal).ToUpperInvariant();
        if (letters.Length != CellLetters[level])
        {
            return false;
        }
        foreach (char ch in letters)
        {
            if (ch < 'A' || ch > 'Z')
            {
                return false;
            }
            index = index * 26 + (ch - 'A');
        }
        return index < 1L << (3 * level);
    }
}
