using System;
using System.Globalization;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// The map's grid on a whole world (docs/WHOLE_WORLD.md §11, decision 6): the six faces of the cube-sphere, named A to F,
/// each cut into <c>cells</c> × <c>cells</c> squares of its face coordinates and named as Rust names its cells: columns
/// lettered A, B … Z, AA … across the face (along +u), rows numbered from 0 down it (from v = +1), so a place reads
/// "C K12". The one naming function: the map, the console's <c>whereami</c> and <c>teleport &lt;cell&gt;</c> all use it.
/// </summary>
/// <remarks>
/// Faces run east round the equator, then the poles: A is the +X face (0°), B −Z (90° E), C −X (180°), D +Z (90° W), E the
/// north pole's (+Y), F the south pole's (−Y). On A to D the face's u runs east and v north (<see cref="CubeSphere"/>), so
/// the letters grow eastward and the numbers southward, as on Rust's map; E and F share D's columns, so the cube unfolds
/// with E above D and F below it. Cells are squares of the warped face coordinates, so they are nearly the same size
/// everywhere (151 m on the first world at 2.41 km, 25 a face). Deterministic: + − × ÷ and <see cref="CubeSphere"/>.
/// </remarks>
public static class WorldGrid
{
    /// <summary>The faces' names, in naming order.</summary>
    public const string Letters = "ABCDEF";

    /// <summary>The most cells a face side may have (two letters: up to AZ).</summary>
    public const int MostCells = 52;

    // The cube-sphere face of each name (A … F), and the name of each face (0 … 5).
    private static readonly int[] FaceOfName = { 0, 5, 1, 4, 2, 3 };
    private static readonly int[] NameOfFace = { 0, 2, 4, 5, 3, 1 };

    /// <summary>A cell of the grid: a cube-sphere face (0 … 5, <see cref="CubeSphere"/>'s order), a column and a row.</summary>
    public readonly record struct Cell(int Face, int Column, int Row);

    /// <summary>Cells along a face's side for cells of about <paramref name="cellMetres"/> on a world of a radius (1 … <see cref="MostCells"/>).</summary>
    public static int CellsPerFace(double radius, double cellMetres) =>
        cellMetres > 0 && radius > 0 ? Math.Clamp((int)Math.Round(TileKey.EdgeLength(0, radius) / cellMetres), 1, MostCells) : 1;

    /// <summary>A face's letter (A … F) from its cube-sphere index.</summary>
    public static char FaceLetter(int face) => Letters[NameOfFace[Math.Clamp(face, 0, CubeSphere.FaceCount - 1)]];

    /// <summary>The cube-sphere face a letter names (A … F, either case), or −1.</summary>
    public static int FaceOf(char letter)
    {
        int name = Letters.IndexOf(char.ToUpperInvariant(letter), StringComparison.Ordinal);
        return name < 0 ? -1 : FaceOfName[name];
    }

    /// <summary>The cell a direction (need not be unit length; not zero) lies in.</summary>
    public static Cell CellAt(Vector3D direction, int cells)
    {
        (int face, double u, double v) = CubeSphere.FromDirection(direction);
        int column = Math.Clamp((int)Math.Floor((u + 1) / 2 * cells), 0, cells - 1);
        int row = Math.Clamp((int)Math.Floor((1 - v) / 2 * cells), 0, cells - 1);
        return new Cell(face, column, row);
    }

    /// <summary>The name of the cell a direction lies in: "C K12".</summary>
    public static string Name(Vector3D direction, int cells) => Name(CellAt(direction, cells));

    /// <summary>A cell's name: its face's letter, a space, its column's letters and its row's number.</summary>
    public static string Name(Cell cell) => FaceLetter(cell.Face) + " " + CellName(cell.Column, cell.Row);

    /// <summary>A cell's name on its face, as Rust's: "K12".</summary>
    public static string CellName(int column, int row) => Column(column) + row.ToString(CultureInfo.InvariantCulture);

    /// <summary>A column's letters: A … Z, then AA, AB …</summary>
    public static string Column(int index) =>
        index < 26 ? ((char)('A' + Math.Max(0, index))).ToString() : Column(index / 26 - 1) + (char)('A' + index % 26);

    /// <summary>The face coordinate u of column line <paramref name="i"/> (0 … cells: the face's west edge to its east).</summary>
    public static double LineU(int i, int cells) => -1 + 2.0 * i / cells;

    /// <summary>The face coordinate v of row line <paramref name="j"/> (0 … cells: the face's north edge to its south).</summary>
    public static double LineV(int j, int cells) => 1 - 2.0 * j / cells;

    /// <summary>The unit direction of a point of a face given in grid units (column and row lines, fractions between).</summary>
    public static Vector3D At(int face, double column, double row, int cells) =>
        CubeSphere.Direction(face, -1 + 2 * column / cells, 1 - 2 * row / cells);

    /// <summary>The unit direction of a cell's centre.</summary>
    public static Vector3D Centre(Cell cell, int cells) => At(cell.Face, cell.Column + 0.5, cell.Row + 0.5, cells);

    /// <summary>
    /// Reads a cell's name: "C K12", "CK12", "c-k12" (the face's letter, then the column's letters and the row's number).
    /// False for anything else, or a cell off the grid: an untrusted word never throws.
    /// </summary>
    public static bool TryParse(string text, int cells, out Cell cell)
    {
        cell = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 16)
        {
            return false;
        }
        Span<char> chars = stackalloc char[text.Length];
        int n = 0;
        foreach (char ch in text)
        {
            if (ch is ' ' or '-' or '_' or ':' or '/')
            {
                continue;
            }
            chars[n++] = char.ToUpperInvariant(ch);
        }
        // The face, then one or two letters, then digits.
        if (n < 3 || FaceOf(chars[0]) is var face && face < 0)
        {
            return false;
        }
        int k = 1, column = -1;
        while (k < n && chars[k] is >= 'A' and <= 'Z')
        {
            column = column < 0 ? chars[k] - 'A' : (column + 1) * 26 + chars[k] - 'A';
            k++;
            if (k > 3)
            {
                return false;
            }
        }
        if (column < 0 || k == n || n - k > 3)
        {
            return false;
        }
        int row = 0;
        for (; k < n; k++)
        {
            if (chars[k] is < '0' or > '9')
            {
                return false;
            }
            row = row * 10 + chars[k] - '0';
        }
        if (column >= cells || row >= cells)
        {
            return false;
        }
        cell = new Cell(face, column, row);
        return true;
    }
}
