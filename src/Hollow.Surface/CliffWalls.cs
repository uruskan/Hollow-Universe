using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// A wall's rock skin in one cell (docs/TERRAIN_SCENERY_PLAN.md T2): a mesh that follows the heightfield's steep face
/// from foot to crest, standing a little out from it with the relief of rock, drawn in one of the ground's rock scans.
/// Earth's walls are tens of metres tall and curve in plan, and rigid scans cannot follow them, so the face itself is
/// traced a metre and a half at a time along its run. Vertices are body-frame offsets from <see cref="Anchor"/>, the
/// cell's ground; <see cref="Lods"/> are coarser triangle lists over the same vertices. <see cref="Recipe"/> is its
/// face's recipe (<see cref="CliffRecipe.Index"/>), whose wall textures it takes.
/// </summary>
public sealed record CliffWall(TileKey Cell, int Recipe, Vector3D Anchor, float[] Positions, float[] Normals,
    int[] Indices, CliffWallLod[] Lods, double Radius);

/// <summary>A coarser triangle list over a wall's vertices and the largest error it admits, metres.</summary>
public readonly record struct CliffWallLod(double Error, int[] Indices);

public sealed partial class CliffPlacements
{
    /// <summary>A half-metre of face rising at least this grade is wall the skin covers (56°).</summary>
    public const double SkinGrade = 1.5;
    /// <summary>A face rising less than this from foot to crest takes no skin, metres.</summary>
    public const double SkinRise = 3;
    /// <summary>The skin stands this far out of the face, and up to <see cref="SkinRelief"/> more where its rock bulges.</summary>
    public const double SkinOffset = 0.3, SkinRelief = 0.9;
    private const double StationSpacing = 1.5, ProfileStep = 0.5, RowHeight = 1, SkirtDepth = 0.8, LipDepth = 1.4;
    private const int ProfileHalf = 32, MaximumStations = 120, MaximumRows = 64, WallSeeds = 16;

    private readonly object _wallGate = new();
    private readonly Dictionary<TileKey, (Lazy<CliffWall[]> Value, LinkedListNode<TileKey> Node)> _walls = new();
    private readonly LinkedList<TileKey> _wallRecency = new();

    /// <summary>
    /// A face through a point: the ground's downslope there and its profile along it, half-metres from
    /// <see cref="Ground"/>, with the run of the face (each half-metre rising <see cref="SkinGrade"/> or more) from
    /// <see cref="Crest"/> to <see cref="Foot"/>; <see cref="Middle"/> is the face at its middle height.
    /// </summary>
    private readonly record struct WallStation(Vector3D Ground, Vector3D Downslope, Vector3D Right, double[] Heights,
        int Crest, int Foot, Vector3D Middle)
    {
        public double CrestHeight => Heights[Crest];
        public double FootHeight => Heights[Foot];

        /// <summary>Metres along the profile where the face stands at a height within its run.</summary>
        public double AlongAt(double height)
        {
            for (int i = Crest; i < Foot; i++)
                if (Heights[i + 1] <= height)
                {
                    double span = Heights[i] - Heights[i + 1];
                    double t = span > 1e-9 ? Math.Clamp((Heights[i] - height) / span, 0, 1) : 0;
                    return (i - ProfileHalf + t) * ProfileStep;
                }
            return (Foot - ProfileHalf) * ProfileStep;
        }
    }

    /// <summary>The wall skins a cell owns, cached as its formation is (<see cref="GenerateWalls"/> builds them afresh).</summary>
    public IReadOnlyList<CliffWall> WallsAt(TileKey cell)
    {
        if (cell.Level != Level) return Array.Empty<CliffWall>();
        Lazy<CliffWall[]> entry;
        lock (_wallGate)
        {
            if (_walls.TryGetValue(cell, out var found))
            {
                entry = found.Value;
                _wallRecency.Remove(found.Node);
                _wallRecency.AddLast(found.Node);
            }
            else
            {
                if (_walls.Count == CacheCapacity)
                {
                    LinkedListNode<TileKey> oldest = _wallRecency.First!;
                    _walls.Remove(oldest.Value);
                    _wallRecency.RemoveFirst();
                }
                entry = new Lazy<CliffWall[]>(() => GenerateWalls(cell), LazyThreadSafetyMode.ExecutionAndPublication);
                var node = new LinkedListNode<TileKey>(cell);
                _walls.Add(cell, (entry, node));
                _wallRecency.AddLast(node);
            }
        }
        return entry.Value;
    }

    /// <summary>
    /// A skin reaches at most this far past its cell's edge, metres: three stations along the wall and its stand out of
    /// the face.
    /// </summary>
    public const double SkinMargin = 10;

    /// <summary>The wall skins within a reach of a point (each skin's cell owns it).</summary>
    public List<CliffWall> WallsNear(Vector3D position, double reach)
    {
        var tiles = new List<TileKey>();
        TileKey.Around(position, reach + SkinMargin, _surface.Radius, Level, tiles);
        var result = new List<CliffWall>();
        foreach (TileKey tile in tiles)
            foreach (CliffWall wall in WallsAt(tile))
                if (Vector3D.Distance(position, wall.Anchor) <= reach + wall.Radius) result.Add(wall);
        return result;
    }

    /// <summary>
    /// A cell's wall skins built afresh, one a recipe: each wall of the cell traced along its face and dressed from foot
    /// to crest. The same for every caller; nothing is cached.
    /// </summary>
    public CliffWall[] GenerateWalls(TileKey cell)
    {
        if (cell.Level != Level || !MayHold(cell, SkinRise)) return Array.Empty<CliffWall>();
        List<List<WallStation>> chains = TraceWalls(cell);
        if (chains.Count == 0) return Array.Empty<CliffWall>();
        Vector3D centre = cell.Centre;
        Vector3D anchor = centre * (_surface.Radius + _surface.HeightAt(centre));
        // Each wall takes the recipe of what made it at its middle: the coast's, or its relief's.
        var byRecipe = new SortedDictionary<int, List<List<WallStation>>>();
        foreach (List<WallStation> chain in chains)
        {
            int recipe = RecipeFor(chain[chain.Count / 2].Middle.Normalized).Index;
            if (!byRecipe.TryGetValue(recipe, out var list)) byRecipe[recipe] = list = new List<List<WallStation>>();
            list.Add(chain);
        }
        var walls = new List<CliffWall>(byRecipe.Count);
        foreach ((int recipe, List<List<WallStation>> list) in byRecipe) walls.Add(Skin(cell, recipe, anchor, list));
        return walls.ToArray();
    }

    // The cell's walls, each a chain of stations a metre and a half apart along its face, from seeds on a 3.7 m grid. A
    // seed's wall is traced both ways while it lasts, three stations past the cell's edge (so neighbouring cells' skins
    // overlap); a seed anywhere on a face already traced, foot to crest, is skipped.
    private List<List<WallStation>> TraceWalls(TileKey cell)
    {
        var chains = new List<List<WallStation>>();
        var taken = new List<WallStation>();
        for (int j = 0; j < WallSeeds; j++)
        for (int i = 0; i < WallSeeds; i++)
        {
            Vector3D up = CubeSphere.Direction(cell.Face, cell.U0 + cell.Span * (i + 0.5) / WallSeeds,
                cell.V0 + cell.Span * (j + 0.5) / WallSeeds);
            Vector3D ground = up * (_surface.Radius + _surface.HeightAt(up));
            if (Taken(taken, ground) || !Steep(up, ground)) continue;
            if (StationNear(up) is not { } seed || !Owns(cell, seed.Middle) || Taken(taken, seed.Middle)) continue;
            List<WallStation> chain = Trace(cell, seed);
            if (chain.Count < 3) continue;
            chains.Add(chain);
            taken.AddRange(chain);
        }
        return chains;
    }

    // A cheap screen before a station's profile: the ground rises at least three metres across six here.
    private bool Steep(Vector3D up, Vector3D ground)
    {
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, up);
        east = east.LengthSquared > 1e-16 ? east.Normalized : Vector3D.UnitZ;
        Vector3D south = Vector3D.Cross(east, up);
        double x = _surface.HeightAt((ground + east * 3).Normalized) - _surface.HeightAt((ground - east * 3).Normalized);
        double z = _surface.HeightAt((ground + south * 3).Normalized) - _surface.HeightAt((ground - south * 3).Normalized);
        return x * x + z * z >= 9;
    }

    private bool Owns(TileKey cell, Vector3D point) => TileKey.At(point.Normalized, Level) == cell;

    // Whether a point lies on the face of a traced station: within its stretch along the wall, and between its crest
    // and its foot (two metres either side) across it.
    private static bool Taken(List<WallStation> taken, Vector3D point)
    {
        foreach (WallStation s in taken)
        {
            Vector3D offset = point - s.Ground;
            if (Math.Abs(Vector3D.Dot(offset, s.Right)) > StationSpacing * 0.75 + 0.5) continue;
            double along = Vector3D.Dot(offset, s.Downslope);
            if (along >= (s.Crest - ProfileHalf) * ProfileStep - 2 && along <= (s.Foot - ProfileHalf) * ProfileStep + 2) return true;
        }
        return false;
    }

    private List<WallStation> Trace(TileKey cell, WallStation seed)
    {
        List<WallStation> chain = Walk(cell, seed, -1);
        chain.Reverse();
        chain.Add(seed);
        chain.AddRange(Walk(cell, seed, 1));
        return chain;
    }

    // Stations a metre and a half apart one way along a face: while the wall lasts, turns gently and goes on along it (its
    // middle may wander up or down a leaning face), and stays in the cell; then three more.
    private List<WallStation> Walk(TileKey cell, WallStation from, int sign)
    {
        var result = new List<WallStation>();
        WallStation current = from;
        int outside = 0;
        for (int k = 0; k < MaximumStations / 2; k++)
        {
            if (StationNear(current.Middle + current.Right * (sign * StationSpacing)) is not { } station) break;
            if (Vector3D.Dot(station.Right, current.Right) < 0.5) break;
            Vector3D moved = station.Middle - current.Middle;
            double forward = Vector3D.Dot(moved, current.Right) * sign;
            if (forward < StationSpacing * 0.3 || forward > StationSpacing * 3 || Math.Abs(Vector3D.Dot(moved, current.Downslope)) > 6) break;
            result.Add(station);
            if (!Owns(cell, station.Middle) && ++outside >= 3) break;
            current = station;
        }
        return result;
    }

    // The face through a point (anywhere near it): the downslope over three metres, the profile along it, and the run of
    // half-metres each rising SkinGrade or more that holds the steepest one within four metres. None where the run rises
    // less than SkinRise, under water, or on a monument's ground.
    private WallStation? StationNear(Vector3D point)
    {
        Vector3D up = point.Normalized;
        if (_surface is WalkingWorld walking && walking.Monuments.At(up, MaximumExtent) != null) return null;
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, up);
        east = east.LengthSquared > 1e-16 ? east.Normalized : Vector3D.UnitZ;
        Vector3D south = Vector3D.Cross(east, up);
        Vector3D ground = up * (_surface.Radius + _surface.HeightAt(up));
        double x = (_surface.HeightAt((ground + east * 1.5).Normalized) - _surface.HeightAt((ground - east * 1.5).Normalized)) / 3;
        double z = (_surface.HeightAt((ground + south * 1.5).Normalized) - _surface.HeightAt((ground - south * 1.5).Normalized)) / 3;
        double gradient = Math.Sqrt(x * x + z * z);
        if (!(gradient >= 0.3)) return null;
        Vector3D downslope = -(east * x + south * z) / gradient;
        Vector3D right = Vector3D.Cross(up, downslope).Normalized;
        // The profile's heights are taken only as far as the face runs: within four metres of the point for its steepest
        // half-metre, then outward while each half-metre still rises.
        var heights = new double[2 * ProfileHalf + 1];
        Array.Fill(heights, double.NaN);
        double H(int i)
        {
            if (double.IsNaN(heights[i]))
                heights[i] = _surface.HeightAt((ground + downslope * ((i - ProfileHalf) * ProfileStep)).Normalized);
            return heights[i];
        }
        int steepest = -1;
        double best = 0, rise = SkinGrade * ProfileStep;
        for (int i = ProfileHalf - 8; i < ProfileHalf + 8; i++)
            if (H(i) - H(i + 1) > best) (best, steepest) = (H(i) - H(i + 1), i);
        if (steepest < 0 || best < rise) return null;
        int crest = steepest, foot = steepest + 1;
        while (crest > 0 && H(crest - 1) - H(crest) >= rise) crest--;
        while (foot < heights.Length - 1 && H(foot) - H(foot + 1) >= rise) foot++;
        if (heights[crest] - heights[foot] < SkinRise) return null;
        if (heights[foot] < _surface.WaterLevelAt((ground + downslope * ((foot - ProfileHalf) * ProfileStep)).Normalized) - 6)
            return null;
        var station = new WallStation(ground, downslope, right, heights, crest, foot, Vector3D.Zero);
        double middle = (station.CrestHeight + station.FootHeight) / 2;
        return station with { Middle = FacePoint(station, middle, station.AlongAt(middle)) };
    }

    private Vector3D FacePoint(in WallStation station, double height, double along) =>
        (station.Ground + station.Downslope * along).Normalized * (_surface.Radius + height);

    // One cell's skin of one recipe: each chain a grid of stations by rows, from a skirt buried below the foot,
    // up the face, to a lip folded back under the crest's ground; stood out of the face by the rock's relief, which
    // fades at the grid's edges and dives behind the face at a chain's ends (where a neighbouring cell's skin, or the
    // wall's end, takes over).
    private CliffWall Skin(TileKey cell, int recipe, Vector3D anchor, List<List<WallStation>> chains)
    {
        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<int>();
        var lods = new List<int>[3] { new(), new(), new() };
        double radius = 0;
        foreach (List<WallStation> chain in chains)
        {
            int n = chain.Count;
            double tallest = 0;
            foreach (WallStation s in chain) tallest = Math.Max(tallest, s.CrestHeight - s.FootHeight);
            int rows = Math.Clamp((int)Math.Ceiling(tallest / RowHeight), 2, MaximumRows);
            int column = rows + 3;
            var grid = new Vector3D[n * column];
            for (int i = 0; i < n; i++)
            {
                WallStation s = chain[i];
                double feather = Feather(cell, chain, i);
                for (int j = 0; j < column; j++)
                {
                    Vector3D point;
                    if (j == 0)
                        point = FacePoint(s, s.FootHeight - SkirtDepth, s.AlongAt(s.FootHeight) + SkinOffset + 0.4);
                    else if (j == column - 1)
                        point = FacePoint(s, s.CrestHeight - 0.5, s.AlongAt(s.CrestHeight) - LipDepth);
                    else
                    {
                        double v = (j - 1) / (double)rows;
                        double height = s.FootHeight + v * (s.CrestHeight - s.FootHeight);
                        Vector3D face = FacePoint(s, height, s.AlongAt(height));
                        // Full relief inside the grid, half on its first and last face rows, so the skin rolls into the
                        // skirt and the lip.
                        double edge = j == 1 || j == column - 2 ? 0.5 : 1;
                        double stand = feather >= 0 ? feather * edge * (SkinOffset + SkinRelief * RockRelief(face)) : feather;
                        point = face + s.Downslope * stand;
                    }
                    grid[i * column + j] = point;
                }
            }
            int start = positions.Count / 3;
            for (int i = 0; i < n; i++)
            for (int j = 0; j < column; j++)
            {
                Vector3D p = grid[i * column + j];
                Vector3D along = grid[Math.Min(n - 1, i + 1) * column + j] - grid[Math.Max(0, i - 1) * column + j];
                Vector3D upward = grid[i * column + Math.Min(column - 1, j + 1)] - grid[i * column + Math.Max(0, j - 1)];
                Vector3D normal = Vector3D.Cross(along, upward);
                normal = normal.LengthSquared > 1e-18 ? normal.Normalized : chain[i].Downslope;
                Vector3D offset = p - anchor;
                radius = Math.Max(radius, offset.Length);
                positions.Add((float)offset.X); positions.Add((float)offset.Y); positions.Add((float)offset.Z);
                normals.Add((float)normal.X); normals.Add((float)normal.Y); normals.Add((float)normal.Z);
            }
            Grid(indices, start, n, column, 1);
            for (int k = 0; k < lods.Length; k++) Grid(lods[k], start, n, column, 2 << k);
        }
        var levels = new CliffWallLod[lods.Length];
        for (int k = 0; k < lods.Length; k++) levels[k] = new CliffWallLod(LodErrors[k], lods[k].ToArray());
        return new CliffWall(cell, recipe, anchor, positions.ToArray(), normals.ToArray(), indices.ToArray(), levels, radius);
    }

    // What every second, fourth and eighth station and row leave out, about.
    private static readonly double[] LodErrors = { 0.6, 1.6, 4 };

    // How far a chain's station stands out, as a share of the rock's relief: all of it in the cell's own stretch, less
    // over the last two stations before an end (past the cell's edge, or where the wall ends), and the last a little
    // behind the face, so neighbouring cells' skins cross gently.
    private double Feather(TileKey cell, List<WallStation> chain, int i)
    {
        int n = chain.Count;
        int fromEnd = Math.Min(i, n - 1 - i);
        if (fromEnd == 0) return -0.15;
        double share = fromEnd == 1 ? 0.3 : fromEnd == 2 ? 0.7 : 1;
        return Owns(cell, chain[i].Middle) ? share : Math.Min(share, 0.7);
    }

    // Triangles over a chain's grid of stations by rows taking every stride-th of each (and always the last), wound
    // clockwise seen from outside the face (Godot's front).
    private static void Grid(List<int> into, int start, int stations, int column, int stride)
    {
        Span<int> columns = stackalloc int[stations + 1];
        Span<int> rows = stackalloc int[column + 1];
        int cs = Strided(columns, stations, stride), rs = Strided(rows, column, stride);
        for (int a = 0; a + 1 < cs; a++)
        for (int b = 0; b + 1 < rs; b++)
        {
            int p = start + columns[a] * column + rows[b], q = start + columns[a + 1] * column + rows[b];
            int r = start + columns[a] * column + rows[b + 1], s = start + columns[a + 1] * column + rows[b + 1];
            into.Add(p); into.Add(r); into.Add(q);
            into.Add(q); into.Add(r); into.Add(s);
        }

        static int Strided(Span<int> into, int count, int stride)
        {
            int k = 0;
            for (int i = 0; i < count; i += stride) into[k++] = i;
            if (into[k - 1] != count - 1) into[k++] = count - 1;
            return k;
        }
    }

    // Rock relief in [0, 1] at a body-frame point: blocks a few metres across standing out by turns, cut by joints that
    // run down the face, and a little grain (the beds are the shader's: their lines must stay sharp at any distance). In
    // the world's own space, so neighbouring cells' skins agree.
    private double RockRelief(Vector3D p)
    {
        double h = p.Length - _surface.Radius;
        Vector3D g = p.Normalized * _surface.Radius;
        ulong seed = _seed + 0x534B494E; // "SKIN"
        double blocks = Noise.Value(g.X / 6.1 + h / 13, g.Y / 6.1, g.Z / 6.1 - h / 17, seed, NoiseBasis.Corners);
        double swell = Noise.Value(g.X / 19, g.Y / 19 + h / 23, g.Z / 19, seed + 1, NoiseBasis.Corners);
        double joint = Math.Abs(Noise.Value(g.X / 4.7, g.Y / 4.7 + h / 41, g.Z / 4.7, seed + 2, NoiseBasis.Corners));
        double grain = Noise.Value(p.X / 1.1, p.Y / 1.1, p.Z / 1.1, seed + 3, NoiseBasis.Corners);
        double relief = 0.5 + 0.8 * blocks + 0.45 * swell + 0.12 * grain;
        // A joint is where its noise crosses zero: a crack the rock falls back into.
        relief *= DMath.SmoothStep(0.03, 0.12, joint);
        return Math.Clamp(relief, 0, 1);
    }

    /// <summary>Whether the segment from a point along a unit direction for a length meets a wall skin's triangles.</summary>
    public static bool Hits(CliffWall wall, Vector3D from, Vector3D direction, double length)
    {
        Vector3D o = from - wall.Anchor;
        // The segment's nearest approach to the anchor, against the skin's radius.
        double t = Math.Clamp(Vector3D.Dot(-o, direction), 0, length);
        if ((o + direction * t).Length > wall.Radius) return false;
        float[] p = wall.Positions;
        int[] index = wall.Indices;
        for (int i = 0; i + 2 < index.Length; i += 3)
        {
            int ia = index[i] * 3, ib = index[i + 1] * 3, ic = index[i + 2] * 3;
            var a = new Vector3D(p[ia], p[ia + 1], p[ia + 2]);
            Vector3D e1 = new Vector3D(p[ib], p[ib + 1], p[ib + 2]) - a, e2 = new Vector3D(p[ic], p[ic + 1], p[ic + 2]) - a;
            Vector3D q = Vector3D.Cross(direction, e2);
            double det = Vector3D.Dot(e1, q);
            if (Math.Abs(det) < 1e-12) continue;
            double f = 1 / det;
            Vector3D s = o - a;
            double u = Vector3D.Dot(s, q) * f;
            if (u < 0 || u > 1) continue;
            Vector3D r = Vector3D.Cross(s, e1);
            double v = Vector3D.Dot(direction, r) * f;
            if (v < 0 || u + v > 1) continue;
            double hit = Vector3D.Dot(e2, r) * f;
            if (hit >= 0 && hit <= length) return true;
        }
        return false;
    }
}
