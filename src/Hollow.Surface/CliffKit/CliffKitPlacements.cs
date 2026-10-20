using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>What the kit's placement reads of the ground at a point besides its height: which family belongs there.</summary>
/// <param name="Tableland">The tableland relief's weight (0 to 1).</param>
/// <param name="Coast">1 at the sea's edge, 0 inland.</param>
/// <param name="Wet">Whether the point lies under a lake.</param>
public readonly record struct KitGround(double Tableland, double Coast, bool Wet);

/// <summary>
/// One piece of the cliff kit on the ground (docs/WORLD_TERRAIN_PLAN.md §4.3, iteration 5): the kit's piece, its pivot
/// (body frame, metres), its axes (face out, up, left along the chain) and its uniform scale.
/// </summary>
public readonly record struct KitPlacement(int Piece, Vector3D Position, Vector3D AxisX, Vector3D AxisY, Vector3D AxisZ,
    double Scale, double Reach)
{
    /// <summary>A point of the piece's frame in the body frame.</summary>
    public Vector3D Transform(Vector3D local) => Position + (AxisX * local.X + AxisY * local.Y + AxisZ * local.Z) * Scale;

    /// <summary>A body-frame point in the piece's frame (its units, before scale).</summary>
    public Vector3D Local(Vector3D world)
    {
        Vector3D d = (world - Position) / Scale;
        return new Vector3D(Vector3D.Dot(d, AxisX), Vector3D.Dot(d, AxisY), Vector3D.Dot(d, AxisZ));
    }

    /// <summary>A direction of the piece's frame in the body frame.</summary>
    public Vector3D Direction(Vector3D local) => (AxisX * local.X + AxisY * local.Y + AxisZ * local.Z).Normalized;
}

/// <summary>
/// The cliff kit on a baked world (docs/WORLD_TERRAIN_PLAN.md §4.3, iteration 5), after Rust's way: seeds on steep
/// ground, one try per few hundred square metres, each growing a chain of pieces both ways along the slope, joined at
/// their sockets and capped with slope ends where the slope gives out. Every piece faces downhill, leans back a little,
/// stands with its foot on the ground (its skirt below) and is dropped, never patched, when its foot checks fail. Each
/// piece raises the ground behind its face to just under its top (<see cref="Stamp"/>), so no back shows, and its closed
/// shell joins the ground tile's collision.
/// <para>Per 59 m cell, from the bake's ground alone (never the stamped ground), so the server and every client agree;
/// ties are broken by <see cref="Hash64"/>.</para>
/// </summary>
public sealed class CliffKitPlacements
{
    private const double CellMetres = 59;
    private const int SeedGrid = 7;           // seed candidates per cell edge (about 8.4 m apart)
    private const double GradientStep = 3;    // metres either side for a slope
    private const double Sink = 0.6;          // the pivot sits this far under the ground (the skirt reaches further)
    // The stamp stays inside the rock: well under its top, behind its face, and only a short steep skirt behind its back.
    // A wider one (40° over 25 m) raised ridges and teeth of bare ground between and behind the pieces.
    private const double Inset = 1.2;         // the raised ground stops this far under the rock's top
    private const double FaceClearance = 1.5; // and this far behind its face
    private const double BackFall = 2.0;      // behind the piece the raised ground falls back at 63°
    private const double BackReach = 4;       // metres at most
    private const double CellMargin = 18;     // a chain's pivots stay within this of their cell

    private readonly CliffKit _kit;
    private readonly string _gameFolder;
    private readonly double _radius;
    private readonly Func<Vector3D, double> _ground;
    private readonly Func<Vector3D, KitGround> _place;
    private readonly Func<Vector3D, bool> _blocked;
    private readonly WalkingParameters _p;
    private readonly ulong _seed;
    private readonly Shape[] _shapes;
    private readonly Lazy<CliffCollision>[] _collision;
    private readonly ConcurrentDictionary<TileKey, Lazy<KitPlacement[]>> _cells = new();
    private readonly ConcurrentDictionary<TileKey, Lazy<KitPlacement[]>> _stamps = new();
    private readonly double _maxReach;

    // A piece's stamp: per row of its top grid (along z), its face's x and its back's x and top there.
    private sealed record Shape(CliffGrid Grid, double[] Front, double[] Back, double[] BackTop, double Reach);

    public CliffKitPlacements(CliffKit kit, string gameFolder, double radius, Func<Vector3D, double> ground,
        Func<Vector3D, KitGround> place, Func<Vector3D, bool> blocked, WalkingParameters p, ulong seed)
    {
        _kit = kit;
        _gameFolder = gameFolder;
        _radius = radius;
        _ground = ground;
        _place = place;
        _blocked = blocked;
        _p = p;
        _seed = seed;
        Level = TileKey.LevelFor(radius, CellMetres);
        _shapes = new Shape[kit.Pieces.Count];
        _collision = new Lazy<CliffCollision>[kit.Pieces.Count];
        for (int i = 0; i < kit.Pieces.Count; i++)
        {
            CliffPiece piece = kit.Pieces[i];
            _shapes[i] = ShapeOf(piece);
            _maxReach = Math.Max(_maxReach, _shapes[i].Reach * piece.ScaleMax);
            string file = CliffKit.Resolve(gameFolder, piece.Collision.File);
            _collision[i] = new Lazy<CliffCollision>(() => CliffCollision.Load(file), LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }

    /// <summary>The cube-sphere level of the cells pieces are seeded by (5 on Earth: about 59 m).</summary>
    public int Level { get; }

    public CliffKit Kit => _kit;

    /// <summary>
    /// The kit's game folder for <paramref name="start"/> and above it: HOLLOW_GAME_FOLDER, else a folder holding
    /// <see cref="CliffKit.DefaultPath"/>, or a <c>game</c> folder beside one that does; null when there is none.
    /// </summary>
    public static string? FindGameFolder(string? start = null)
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("HOLLOW_GAME_FOLDER");
        if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(Path.Combine(fromEnvironment, CliffKit.DefaultPath)))
        {
            return fromEnvironment;
        }
        string? dir = start ?? AppContext.BaseDirectory;
        while (dir != null)
        {
            foreach (string candidate in new[] { dir, Path.Combine(dir, "game") })
            {
                if (File.Exists(Path.Combine(candidate, CliffKit.DefaultPath)))
                {
                    return candidate;
                }
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>The pieces a cell seeded (some reach past it).</summary>
    public KitPlacement[] At(TileKey cell) =>
        cell.Level != Level
            ? Array.Empty<KitPlacement>()
            : _cells.GetOrAdd(cell, c => new Lazy<KitPlacement[]>(() => Generate(c), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>The pieces within <paramref name="reach"/> metres of a body-frame position, by the cell that seeded them.</summary>
    public List<(TileKey Cell, KitPlacement[] Pieces)> Near(Vector3D position, double reach)
    {
        var cells = new List<TileKey>();
        TileKey.Around(position.Normalized, reach + CellMetres, _radius, Level, cells);
        var result = new List<(TileKey, KitPlacement[])>();
        foreach (TileKey cell in cells)
        {
            KitPlacement[] pieces = At(cell);
            if (pieces.Length > 0)
            {
                result.Add((cell, pieces));
            }
        }
        return result;
    }

    // ---------------------------------------------------------------- the stamp

    /// <summary>
    /// The ground at a unit direction with the pieces near it fitted in: behind each piece's face it rises to just under
    /// the rock's top, and behind its back it falls away at 40°; in front of a face nothing changes.
    /// </summary>
    public double Stamp(Vector3D direction, double height)
    {
        KitPlacement[] near = StampsFor(TileKey.At(direction, Level));
        if (near.Length == 0)
        {
            return height;
        }
        Vector3D w = direction * (_radius + height);
        double result = height;
        foreach (KitPlacement k in near)
        {
            if (!Stamps(_kit.Pieces[k.Piece].Role))
            {
                continue;
            }
            double reach = k.Reach + BackReach;
            if ((w - k.Position).LengthSquared > reach * reach)
            {
                continue;
            }
            Shape shape = _shapes[k.Piece];
            CliffGrid grid = shape.Grid;
            Vector3D local = k.Local(w);
            int j = (int)Math.Floor((local.Z - grid.OriginZ) / grid.Cell);
            if (j < 0 || j >= grid.Rows || local.X > shape.Front[j] - FaceClearance / k.Scale)
            {
                continue;
            }
            double top, behind = 0;
            if (grid.TopAt(local.X, local.Z) is double t)
            {
                top = t;
            }
            else if (local.X < shape.Back[j])
            {
                top = shape.BackTop[j];
                behind = (shape.Back[j] - local.X) * k.Scale;
                if (behind > BackReach)
                {
                    continue;
                }
            }
            else
            {
                continue;
            }
            double raised = k.Transform(new Vector3D(local.X, top, local.Z)).Length - _radius - Inset - behind * BackFall;
            if (raised > result)
            {
                result = raised;
            }
        }
        return result;
    }

    // Only walls fit the ground behind them: ledges and jutting pieces overhang their foot, and ground raised under their
    // top showed beneath them.
    private static bool Stamps(CliffRole role) => role is CliffRole.Straight or CliffRole.Corner or CliffRole.End;

    // The pieces whose stamp can reach a cell: those of the cells around it, near enough.
    private KitPlacement[] StampsFor(TileKey cell) =>
        _stamps.GetOrAdd(cell, c => new Lazy<KitPlacement[]>(() =>
        {
            Vector3D centre = c.Centre;
            double cellReach = TileKey.EdgeLength(Level, _radius) * 0.75;
            var cells = new List<TileKey>();
            TileKey.Around(centre, cellReach + CellMargin + _maxReach + BackReach, _radius, Level, cells);
            var result = new List<KitPlacement>();
            Vector3D middle = centre * (_radius + _ground(centre));
            foreach (TileKey other in cells)
            {
                foreach (KitPlacement k in At(other))
                {
                    if (Vector3D.Distance(k.Position, middle) < cellReach + k.Reach + BackReach + 60)
                    {
                        result.Add(k);
                    }
                }
            }
            return result.ToArray();
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static Shape ShapeOf(CliffPiece piece)
    {
        CliffGrid g = piece.Grid;
        var front = new double[g.Rows];
        var back = new double[g.Rows];
        var backTop = new double[g.Rows];
        for (int j = 0; j < g.Rows; j++)
        {
            front[j] = double.NegativeInfinity;
            back[j] = double.PositiveInfinity;
            for (int i = 0; i < g.Columns; i++)
            {
                if (g.Top[j * g.Columns + i] is not double t)
                {
                    continue;
                }
                double x0 = g.OriginX + i * g.Cell;
                front[j] = Math.Max(front[j], x0 + g.Cell);
                if (x0 < back[j])
                {
                    back[j] = x0;
                    backTop[j] = t;
                }
            }
        }
        double reach = 0;
        foreach (double x in new[] { piece.BoundsMin.X, piece.BoundsMax.X })
        foreach (double y in new[] { piece.BoundsMin.Y, piece.BoundsMax.Y })
        foreach (double z in new[] { piece.BoundsMin.Z, piece.BoundsMax.Z })
        {
            reach = Math.Max(reach, Math.Sqrt(x * x + y * y + z * z));
        }
        return new Shape(g, front, back, backTop, reach);
    }

    // ---------------------------------------------------------------- collision

    /// <summary>The ground tile's collision with the closed shells of the pieces that reach it.</summary>
    public TriangleMesh AddCollision(TriangleMesh ground, in ZoneFrame frame, TileKey tile, double margin)
    {
        Vector3D centre = tile.Centre;
        double tileReach = TileKey.EdgeLength(tile.Level, _radius) * 0.75 + margin;
        Vector3D middle = centre * (_radius + _ground(centre));
        var cells = new List<TileKey>();
        TileKey.Around(centre, tileReach + CellMargin + _maxReach, _radius, Level, cells);
        List<float>? positions = null;
        List<int>? indices = null;
        foreach (TileKey cell in cells)
        {
            foreach (KitPlacement k in At(cell))
            {
                if (Vector3D.Distance(k.Position, middle) > tileReach + k.Reach + 60)
                {
                    continue;
                }
                positions ??= new List<float>(ground.Positions);
                indices ??= new List<int>(ground.Indices);
                CliffCollision shell = _collision[k.Piece].Value;
                int offset = positions.Count / 3;
                foreach (Vector3D v in shell.Vertices)
                {
                    System.Numerics.Vector3 local = frame.ToLocal(k.Transform(v));
                    positions.Add(local.X);
                    positions.Add(local.Y);
                    positions.Add(local.Z);
                }
                foreach (int index in shell.Indices)
                {
                    indices.Add(offset + index);
                }
            }
        }
        return positions == null ? ground : new TriangleMesh(positions.ToArray(), indices!.ToArray());
    }

    // ---------------------------------------------------------------- placement

    private readonly record struct Slope(Vector3D Up, Vector3D Ground, Vector3D Downhill, double Grade);

    private KitPlacement[] Generate(TileKey cell)
    {
        var placed = new List<KitPlacement>();
        // A cheap gate: no ground in the cell as steep as a seed needs.
        var candidates = new List<(ulong Order, Slope Slope)>();
        for (int j = 0; j < SeedGrid; j++)
        {
            for (int i = 0; i < SeedGrid; i++)
            {
                ulong h = Hash64.Of(_seed, cell.Packed, (ulong)(j * SeedGrid + i));
                double u = cell.U0 + cell.Span * (i + 0.2 + 0.6 * Unit(h)) / SeedGrid;
                double v = cell.V0 + cell.Span * (j + 0.2 + 0.6 * Unit(Hash64.Of(h, 1))) / SeedGrid;
                Slope s = SlopeAt(CubeSphere.Direction(cell.Face, u, v));
                if (s.Grade >= _p.KitSeedSlope)
                {
                    candidates.Add((Hash64.Of(h, 2), s));
                }
            }
        }
        if (candidates.Count == 0)
        {
            return Array.Empty<KitPlacement>();
        }
        candidates.Sort((a, b) => a.Order.CompareTo(b.Order));
        Vector3D cellMiddle = cell.Centre;
        double cellReach = TileKey.EdgeLength(Level, _radius) * 0.71 + CellMargin;
        int chains = 0;
        foreach ((ulong order, Slope seed) in candidates)
        {
            if (chains >= _p.KitSeedsPerCell)
            {
                break;
            }
            KitGround g = _place(seed.Up);
            if (g.Wet || _blocked(seed.Up) || seed.Ground.Length - _radius < 0.5)
            {
                continue;
            }
            string family = g.Coast > 0.5 ? "nordic" : g.Tableland > 0.4 ? "desert" : "tundra";
            if (TryChain(seed, family, order, cellMiddle, cellReach, placed))
            {
                chains++;
            }
        }
        return placed.ToArray();
    }

    // A chain from a seed: its middle piece, then pieces both ways along the slope while it lasts, ends capped.
    private bool TryChain(Slope seed, string family, ulong order, Vector3D cellMiddle, double cellReach, List<KitPlacement> placed)
    {
        double rise = Rise(seed);
        if (rise < 2)
        {
            return false;
        }
        KitPlacement? middle = null;
        foreach (int piece in Candidates(family, Faces, null, rise, order))
        {
            double scale = ScaleFor(piece, rise, order);
            KitPlacement k = Make(piece, scale, seed, null);
            if (Valid(k, placed, -1, cellMiddle, cellReach))
            {
                middle = k;
                break;
            }
        }
        if (middle is not { } start)
        {
            return false;
        }
        int startIndex = placed.Count;
        placed.Add(start);
        foreach (string side in new[] { "left", "right" })
        {
            KitPlacement current = start;
            int currentIndex = startIndex;
            for (int n = 1; n < _p.KitMaxChain / 2 + 1; n++)
            {
                KitPlacement? next = Extend(current, currentIndex, side, family, Hash64.Of(order, (ulong)n, side == "left" ? 1UL : 2UL),
                    placed, cellMiddle, cellReach, last: n == _p.KitMaxChain / 2);
                if (next is not { } k)
                {
                    break;
                }
                currentIndex = placed.Count;
                placed.Add(k);
                current = k;
                if (_kit.Pieces[k.Piece].Role == CliffRole.End)
                {
                    break;
                }
            }
        }
        return true;
    }

    // The roles that make a chain's length of face (a family has some of them: desert straights and ledges, nordic
    // straights, tundra's jutting pieces), and the slope ends that close it.
    private static readonly CliffRole[] Faces = { CliffRole.Straight, CliffRole.Jut, CliffRole.Ledge };
    private static readonly CliffRole[] Ends = { CliffRole.End };

    // The next piece of a chain past one of a piece's sockets: its own opposite socket on that one, facing downhill
    // where it stands; a slope end when the slope gives out or the chain is long enough, else a straight piece.
    private KitPlacement? Extend(KitPlacement current, int currentIndex, string side, string family, ulong order,
        List<KitPlacement> placed, Vector3D cellMiddle, double cellReach, bool last)
    {
        CliffPiece piece = _kit.Pieces[current.Piece];
        if (piece.Socket(side) is not { } socket)
        {
            return null;
        }
        string opposite = side == "left" ? "right" : "left";
        Vector3D joint = current.Transform(socket.Position);
        Vector3D along = current.Direction(socket.Direction);
        Slope here = SlopeAt((joint + along * 8 * current.Scale).Normalized);
        bool cap = last || here.Grade < _p.KitChainSlope;
        double rise = Math.Max(2, Rise(here));
        foreach (CliffRole[] roles in cap ? new[] { Ends } : new[] { Faces, Ends })
        {
            foreach (int candidate in Candidates(family, roles, opposite, rise, order))
            {
                CliffPiece next = _kit.Pieces[candidate];
                if (next.Role == CliffRole.End && (side == "left" ? !next.CapLeft : !next.CapRight))
                {
                    continue; // an end must close the chain on this side
                }
                CliffSocket? theirs = next.Socket(opposite);
                if (theirs == null)
                {
                    continue;
                }
                double scale = Math.Clamp(current.Scale * (0.92 + 0.16 * Unit(Hash64.Of(order, (ulong)candidate))), next.ScaleMin, next.ScaleMax);
                KitPlacement k = Make(candidate, scale, here, null);
                // Join the sockets: move the piece so its socket lies on this one.
                Vector3D offset = joint - k.Transform(theirs.Position);
                k = k with { Position = k.Position + offset };
                if (Valid(k, placed, currentIndex, cellMiddle, cellReach))
                {
                    return k;
                }
            }
        }
        return null;
    }

    // The pieces of a family and role that suit a rise, best first, ties shuffled by the hash.
    private List<int> Candidates(string family, CliffRole[] roles, string? socket, double rise, ulong order)
    {
        var list = new List<(double Score, int Piece)>();
        for (int i = 0; i < _kit.Pieces.Count; i++)
        {
            CliffPiece p = _kit.Pieces[i];
            if (p.Family != family || Array.IndexOf(roles, p.Role) < 0 || (socket != null && p.Socket(socket) == null))
            {
                continue;
            }
            double best = Math.Clamp(rise / Math.Max(0.5, p.Top), p.ScaleMin, p.ScaleMax);
            double miss = Math.Abs(p.Top * best - rise) / Math.Max(3, rise);
            list.Add((miss + 0.35 * Unit(Hash64.Of(order, (ulong)i, 7)), i));
        }
        list.Sort((a, b) => a.Score.CompareTo(b.Score));
        var result = new List<int>(list.Count);
        foreach ((_, int piece) in list)
        {
            result.Add(piece);
        }
        return result;
    }

    private double ScaleFor(int piece, double rise, ulong order)
    {
        CliffPiece p = _kit.Pieces[piece];
        double want = rise / Math.Max(0.5, p.Top) * (0.9 + 0.2 * Unit(Hash64.Of(order, 11)));
        return Math.Clamp(want, p.ScaleMin, p.ScaleMax);
    }

    // A piece standing on a slope: facing downhill, leaning back a little, its pivot just under the ground.
    private KitPlacement Make(int piece, double scale, Slope s, Vector3D? at)
    {
        double lean = _p.KitLeanBack;
        double c = Math.Cos(lean), sn = Math.Sin(lean);
        Vector3D x = s.Downhill * c + s.Up * sn;
        Vector3D y = s.Up * c - s.Downhill * sn;
        Vector3D z = Vector3D.Cross(x, y);
        Vector3D pivot = (at ?? s.Ground) - s.Up * Sink;
        return new KitPlacement(piece, pivot, x, y, z, scale, _shapes[piece].Reach * scale);
    }

    // Whether a piece may stand: its foot on the ground at every foot check, nothing it stands on under water or in a
    // monument, near its cell, and clear of the pieces already placed (but the one it joins).
    private bool Valid(KitPlacement k, List<KitPlacement> placed, int joined, Vector3D cellMiddle, double cellReach)
    {
        if (Vector3D.Distance(k.Position.Normalized, cellMiddle) * _radius > cellReach)
        {
            return false;
        }
        CliffPiece piece = _kit.Pieces[k.Piece];
        foreach (CliffCheck check in piece.Checks)
        {
            if (check.Kind != "foot")
            {
                continue;
            }
            Vector3D w = k.Transform(check.Position);
            Vector3D up = w.Normalized;
            double at = w.Length - _radius;
            double ground = _ground(up);
            if (ground < at - check.Below * Math.Max(1, k.Scale) || ground > at + check.Above * Math.Max(1, k.Scale))
            {
                return false;
            }
            if (ground < 0.3 || _blocked(up) || _place(up).Wet)
            {
                return false;
            }
        }
        for (int i = 0; i < placed.Count; i++)
        {
            if (i == joined)
            {
                continue;
            }
            KitPlacement other = placed[i];
            double apart = Vector3D.Distance(k.Position, other.Position);
            if (apart < 0.55 * (k.Reach + other.Reach))
            {
                return false;
            }
        }
        return true;
    }

    // The slope at a direction: its ground point, the way downhill and its grade, from the bake's ground.
    private Slope SlopeAt(Vector3D up)
    {
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, up);
        east = east.LengthSquared > 1e-18 ? east.Normalized : new Vector3D(0, 0, -1);
        Vector3D north = Vector3D.Cross(up, east);
        double step = GradientStep / _radius;
        double he = _ground((up + east * step).Normalized), hw = _ground((up - east * step).Normalized);
        double hn = _ground((up + north * step).Normalized), hs = _ground((up - north * step).Normalized);
        Vector3D gradient = east * ((he - hw) / (2 * GradientStep)) + north * ((hn - hs) / (2 * GradientStep));
        double grade = gradient.Length;
        Vector3D downhill = grade > 1e-9 ? gradient * (-1 / grade) : east;
        return new Slope(up, up * (_radius + _ground(up)), downhill, grade);
    }

    // How far the ground rises behind a point within a dozen metres uphill: the height a piece there would face.
    private double Rise(Slope s)
    {
        Vector3D behind = (s.Up - s.Downhill * (12 / _radius)).Normalized;
        return _ground(behind) - (s.Ground.Length - _radius);
    }

    private static double Unit(ulong h) => (h >> 11) * (1.0 / (1UL << 53));
}
