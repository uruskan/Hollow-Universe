using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>A scanned formation embedded in a steep slope; body-frame axes are shared by rendering and collision.</summary>
public readonly record struct CliffPlacement(TileKey Tile, int Model, Vector3D Position, Vector3D Right,
    Vector3D Up, Vector3D Forward, double Scale, int Part = 0)
{
    public (TileKey Tile, int Part) Key => (Tile, Part);
    /// <summary>The outward slope direction; authored scans can face the opposite local Z axis.</summary>
    public Vector3D Downslope => Forward * CliffGeometry.All[Model].Facing;
    public Vector3D Transform(Vector3D point) => Position + (Right * point.X + Up * point.Y + Forward * point.Z) * Scale;
}

/// <summary>
/// A surveyed face sample and what dresses it (T2): the ground, its grade, whether it is a wall, whether its face meant
/// it to carry rock (walls always; gentler ground by the recipe's share), whether rock is drawn there (inside a placed
/// scan's box, this cell's or a neighbour's) and the recipe of the face it belongs to (null when the cell dresses no
/// face there). A wall also reports its rise from foot to crest through the sample and the metres of that rise with
/// rock in front of them, since a wall's foot can be dressed while its face above stays bare. Intended samples left
/// uncovered are reported, never hidden.
/// </summary>
public readonly record struct CliffFaceSample(Vector3D Ground, double Gradient, bool Wall, bool Intended, bool Covered,
    string? Recipe, double Rise = 0, double RiseCovered = 0);

/// <summary>
/// The formation cache's retained entries, including empty results and pending Lazy values. Payload bytes count only
/// completed placement-array elements, excluding array/container overhead and arrays still held by outside readers.
/// Builds counts factory invocations, including failures and duplicates after an in-flight entry was evicted.
/// </summary>
public readonly record struct CliffFormationCacheDiagnostics(int Capacity, int Entries, int CompletedEntries,
    int EmptyEntries, int RetainedParts, long RetainedPayloadBytes, long Hits, long Misses, long Evictions, long Builds);

/// <summary>
/// Stable geological formations on the walking world's steep dry faces (docs/TERRAIN_SCENERY_PLAN.md T2). Each cell
/// surveys its own ground on a fixed grid, traces its faces along their contour and dresses each from crest to toe,
/// column by column, with the recipe of what made it (<see cref="CliffRecipe"/>). Candidate streams are separate from
/// harvestable scatter; no natural-object or loot ID changes when this presentation/collision layer changes.
/// </summary>
public sealed partial class CliffPlacements
{
    /// <summary>
    /// Shared collision-generation revision, for save/network compatibility (3: faces traced and dressed, T2's first
    /// iteration; 4: sea-cliff toes in the surf and coastal scans, its second; 5: the recipe chooses the family, and
    /// eight columns a face, its third; 6: the walls' rock skins, <see cref="CliffWall"/>, solid too).
    /// </summary>
    public const int GenerationRevision = 6;
    public const double CellMetres = 48;
    public const double MaximumExtent = 40;
    /// <summary>A member stands at most this far from its cell's centre across the ground (not up a face), so neighbour discovery finds it.</summary>
    public const double MaximumOffset = 64;
    public const int MaximumTiers = 4;
    public const int MaximumColumns = 8;
    /// <summary>Faces a cell dresses, largest first.</summary>
    public const int MaximumFaces = 2;
    public const int MaximumParts = MaximumFaces * MaximumColumns * MaximumTiers;
    /// <summary>Survey samples along a cell's edge: about five metres apart on Earth.</summary>
    public const int SurveySide = 12;
    /// <summary>Grades over the survey's ten-metre baseline: a face (the old candidate rule), a wall, and the toe or crest where a face ends.</summary>
    public const double FaceGrade = 0.62, WallGrade = 2, EdgeGrade = 0.45;
    /// <summary>A wall's section ends where a metre of it rises less than this: the wall's own face, not the slopes it stands in.</summary>
    public const double WallEdge = 1;
    /// <summary>
    /// Per-world retention budget: at most this many entries, including empty formations and pending builds, and at
    /// most Capacity × MaximumParts completed placement elements. This bounds cache retention, not consumer jobs.
    /// </summary>
    public const int CacheCapacity = 384;
    private static readonly Lazy<Vector3D[][]> FitPoints = new(BuildFitPoints);
    private readonly ISurface _surface;
    private readonly ulong _seed;
    private readonly object _cacheGate = new();
    private readonly Dictionary<TileKey, CacheEntry> _cache = new();
    private readonly LinkedList<TileKey> _recency = new();
    private long _cacheHits, _cacheMisses, _cacheEvictions, _cacheBuilds;
    private sealed class CacheEntry(TileKey tile, Func<CliffPlacement[]> build)
    {
        public readonly LinkedListNode<TileKey> Node = new(tile);
        public readonly Lazy<CliffPlacement[]> Value = new(build, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public CliffFormationCacheDiagnostics CacheDiagnostics
    {
        get
        {
            lock (_cacheGate)
            {
                int completed = 0, empty = 0, parts = 0;
                foreach (CacheEntry entry in _cache.Values)
                {
                    if (!entry.Value.IsValueCreated) continue;
                    CliffPlacement[] formation = entry.Value.Value;
                    completed++;
                    if (formation.Length == 0) empty++;
                    parts += formation.Length;
                }
                return new(CacheCapacity, _cache.Count, completed, empty, parts,
                    (long)parts * Unsafe.SizeOf<CliffPlacement>(), _cacheHits, _cacheMisses, _cacheEvictions,
                    Volatile.Read(ref _cacheBuilds));
            }
        }
    }
    public int Level { get; }
    /// <summary>The ground the formations stand on.</summary>
    public ISurface Surface => _surface;

    public CliffPlacements(ISurface surface, ulong seed)
    {
        _surface = surface;
        _seed = seed;
        Level = TileKey.LevelFor(surface.Radius, CellMetres);
    }

    public CliffPlacement? At(TileKey tile)
    {
        IReadOnlyList<CliffPlacement> parts = FormationAt(tile);
        return parts.Count == 0 ? null : parts[0];
    }

    /// <summary>
    /// LRU affects retention only; revisits use the unchanged address-derived generator. A resident entry coalesces
    /// readers, and generation never runs under the cache lock. Evicted entries/arrays remain valid for their current
    /// readers. Eviction of an in-flight entry permits a duplicate on revisit; there is no unbounded side table or
    /// queue retaining those builds. The renderer/navigation/physics owners bound and drain their own outstanding work.
    /// </summary>
    public IReadOnlyList<CliffPlacement> FormationAt(TileKey tile)
    {
        if (tile.Level != Level) return Array.Empty<CliffPlacement>();
        CacheEntry entry;
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(tile, out CacheEntry? found))
            {
                entry = found;
                _cacheHits++;
                _recency.Remove(entry.Node);
                _recency.AddLast(entry.Node);
            }
            else
            {
                _cacheMisses++;
                if (_cache.Count == CacheCapacity)
                {
                    LinkedListNode<TileKey> oldest = _recency.First!;
                    _cache.Remove(oldest.Value);
                    _recency.RemoveFirst();
                    _cacheEvictions++;
                }
                entry = new CacheEntry(tile, () =>
                {
                    Interlocked.Increment(ref _cacheBuilds);
                    return Build(tile);
                });
                _cache.Add(tile, entry);
                _recency.AddLast(entry.Node);
            }
        }
        try { return entry.Value.Value; }
        catch
        {
            // Failed work is retryable. An older evicted failure must not erase a successful replacement entry.
            lock (_cacheGate)
            {
                if (_cache.TryGetValue(tile, out CacheEntry? current) && ReferenceEquals(current, entry))
                {
                    _cache.Remove(tile);
                    _recency.Remove(entry.Node);
                }
            }
            throw;
        }
    }

    /// <summary>
    /// A cell's formation built afresh, outside the shared cache: the same placements <see cref="FormationAt"/> returns,
    /// for a reader that sweeps the whole world once and keeps what it needs itself (the client's far layer), so the
    /// sweep never evicts the cells that collision and the near layer read.
    /// </summary>
    public CliffPlacement[] Generate(TileKey tile) => tile.Level == Level ? Build(tile) : Array.Empty<CliffPlacement>();

    private CliffPlacement[] Build(TileKey tile)
    {
        if (!MayHoldFace(tile)) return Array.Empty<CliffPlacement>();
        List<List<FaceSample>> faces = Faces(Survey(tile));
        if (faces.Count == 0) return Array.Empty<CliffPlacement>();
        Vector3D centre = tile.Centre;
        Vector3D cellGround = centre * (_surface.Radius + _surface.HeightAt(centre));
        var result = new List<CliffPlacement>(MaximumParts);
        for (int face = 0; face < faces.Count; face++) Compose(tile, face, faces[face], cellGround, result);
        return result.ToArray();
    }

    private readonly record struct FaceSample(int Index, Vector3D Up, Vector3D Forward, Vector3D Right, double Gradient);

    // A cheap gate before the survey: a face as steep as the face rule over the survey's ten-metre baseline rises at
    // least 6.2 m, and a 5 x 5 grid of the cell (samples either side of it) spans most of that. Most cells are gentle.
    private bool MayHoldFace(TileKey tile) => MayHold(tile, FaceGrade * 8);

    // Whether a 5 x 5 grid of the cell spans at least a rise, metres.
    private bool MayHold(TileKey tile, double rise)
    {
        const int Side = 5;
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        for (int j = 0; j < Side; j++)
        for (int i = 0; i < Side; i++)
        {
            double h = _surface.HeightAt(CubeSphere.Direction(tile.Face, tile.U0 + tile.Span * i / (Side - 1),
                tile.V0 + tile.Span * j / (Side - 1)));
            low = Math.Min(low, h);
            high = Math.Max(high, h);
        }
        return high - low >= rise;
    }

    // The cell's steep dry ground on a fixed grid, in index order.
    private List<FaceSample> Survey(TileKey tile)
    {
        var steep = new List<FaceSample>();
        for (int j = 0; j < SurveySide; j++)
        for (int i = 0; i < SurveySide; i++)
        {
            Vector3D up = CubeSphere.Direction(tile.Face, tile.U0 + tile.Span * (i + 0.5) / SurveySide,
                tile.V0 + tile.Span * (j + 0.5) / SurveySide);
            if (Slope(up, out Vector3D forward, out Vector3D right, out double gradient) && gradient >= FaceGrade)
                steep.Add(new FaceSample(j * SurveySide + i, up, forward, right, gradient));
        }
        return steep;
    }

    // Connected steep samples (eight neighbours) are one face; the largest first, ties to the lowest sample. A lone
    // sample is a face only when it is a wall.
    private static List<List<FaceSample>> Faces(List<FaceSample> steep)
    {
        var byIndex = new Dictionary<int, FaceSample>(steep.Count);
        foreach (FaceSample sample in steep) byIndex[sample.Index] = sample;
        var seen = new HashSet<int>();
        var faces = new List<List<FaceSample>>();
        var stack = new Stack<FaceSample>();
        foreach (FaceSample start in steep)
        {
            if (!seen.Add(start.Index)) continue;
            var face = new List<FaceSample>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                FaceSample sample = stack.Pop();
                face.Add(sample);
                int i = sample.Index % SurveySide, j = sample.Index / SurveySide;
                for (int dj = -1; dj <= 1; dj++)
                for (int di = -1; di <= 1; di++)
                {
                    int ni = i + di, nj = j + dj;
                    if ((uint)ni >= SurveySide || (uint)nj >= SurveySide) continue;
                    int next = nj * SurveySide + ni;
                    if (byIndex.TryGetValue(next, out FaceSample neighbour) && seen.Add(next)) stack.Push(neighbour);
                }
            }
            face.Sort(static (a, b) => a.Index.CompareTo(b.Index));
            if (face.Count >= 2 || face[0].Gradient >= WallGrade) faces.Add(face);
        }
        faces.Sort(static (a, b) => a.Count != b.Count ? b.Count.CompareTo(a.Count) : a[0].Index.CompareTo(b[0].Index));
        if (faces.Count > MaximumFaces) faces.RemoveRange(MaximumFaces, faces.Count - MaximumFaces);
        return faces;
    }

    // A face's run: its samples' places along their mean contour direction, metres, and the sample nearest the middle.
    private (double[] Along, double Low, double High, int Middle) Run(List<FaceSample> face)
    {
        Vector3D axis = Vector3D.Zero;
        foreach (FaceSample sample in face) axis += Vector3D.Dot(sample.Right, face[0].Right) >= 0 ? sample.Right : -sample.Right;
        axis = axis.LengthSquared > 1e-12 ? axis.Normalized : face[0].Right;
        var along = new double[face.Count];
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        for (int k = 0; k < face.Count; k++)
        {
            along[k] = Vector3D.Dot(face[k].Up - face[0].Up, axis) * _surface.Radius;
            low = Math.Min(low, along[k]);
            high = Math.Max(high, along[k]);
        }
        return (along, low, high, Nearest(along, (low + high) / 2));
    }

    // The distance between two points across the ground: along the sphere at the mean radius, whatever their heights.
    private double Across(Vector3D a, Vector3D b) => (a.Normalized - b.Normalized).Length * _surface.Radius;

    private static int Nearest(double[] along, double target)
    {
        int best = 0;
        for (int k = 1; k < along.Length; k++)
            if (Math.Abs(along[k] - target) < Math.Abs(along[best] - target)) best = k;
        return best;
    }

    // The recipe of what made a face: the coast within the recipes' distance, otherwise its dominant relief (a
    // surface without relief fields takes the mountain recipe).
    private CliffRecipe RecipeFor(Vector3D direction) =>
        CliffRecipe.For(NearSea(direction), _surface is WalkingWorld walking ? walking.Fields(direction).Relief : Relief.Mountains);

    // Eight bearings at half and the whole of the coast distance: the sea is ground below its level under water at it.
    private bool NearSea(Vector3D direction)
    {
        if (_surface.SeaRadius <= 0) return false;
        double sea = _surface.SeaRadius - _surface.Radius;
        Vector3D up = direction.Normalized;
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, up);
        east = east.LengthSquared > 1e-16 ? east.Normalized : Vector3D.UnitZ;
        Vector3D north = Vector3D.Cross(up, east);
        Vector3D ground = up * (_surface.Radius + _surface.HeightAt(up));
        for (int k = 0; k < 8; k++)
        {
            double angle = k * DMath.TwoPi / 8;
            Vector3D bearing = east * DMath.Cos(angle) + north * DMath.Sin(angle);
            for (int r = 1; r <= 2; r++)
            {
                Vector3D d = (ground + bearing * (CliffRecipe.CoastMetres * r / 2)).Normalized;
                if (_surface.HeightAt(d) < sea && _surface.WaterLevelAt(d) == sea) return true;
            }
        }
        return false;
    }

    // One face, dressed as a whole: the samples it intends to carry rock, then column after column from the highest
    // sample still bare, each from its crest down to its toe, until every intended sample lies inside a piece or the
    // face's columns are spent. One material family.
    private void Compose(TileKey tile, int faceIndex, List<FaceSample> face, Vector3D cellGround, List<CliffPlacement> result)
    {
        CliffRecipe recipe = RecipeFor(face[Run(face).Middle].Up);
        var composition = new Rng(Hash64.Of(_seed, tile.Packed, 0x464F524D, (ulong)faceIndex)); // "FORM"
        double groupScale = composition.Range(recipe.MinimumScale, recipe.MaximumScale);
        var ground = new Vector3D[face.Count];
        var heights = new double[face.Count];
        var open = new List<int>(face.Count);
        for (int k = 0; k < face.Count; k++)
        {
            heights[k] = _surface.HeightAt(face[k].Up);
            ground[k] = face[k].Up * (_surface.Radius + heights[k]);
            if (Intends(face[k], recipe)) open.Add(k);
        }
        string? family = null;
        for (int column = 0; column < MaximumColumns && open.Count > 0; column++)
        {
            int top = open[0];
            foreach (int k in open)
                if (heights[k] > heights[top]) top = k;
            open.Remove(top);
            int before = result.Count;
            Column(tile, faceIndex * MaximumColumns + column, face[top], recipe, groupScale, cellGround, ref family, result);
            for (int i = before; i < result.Count; i++)
            {
                CliffPlacement piece = result[i];
                open.RemoveAll(k => Holds(piece, ground[k], 1));
            }
        }
    }

    // Whether a face sample is meant to carry rock: every wall, and of gentler ground the recipe's share, coherent in
    // patches tens of metres across (outcrops and soil, not salt and pepper).
    private bool Intends(FaceSample sample, CliffRecipe recipe)
    {
        if (sample.Gradient >= WallGrade) return true;
        double patch = DMath.SmoothStep(-0.35, 0.35,
            Noise.Fbm(sample.Up, _surface.Radius, 30, 2, 0.5, _seed + 0x534F494C, NoiseBasis.Corners)); // "SOIL"
        return patch < recipe.RockShareAt(sample.Gradient, FaceGrade);
    }

    // From a face sample uphill to where the ground eases (the crest), within the cell's discovery reach and at most
    // one and a half pieces above the sample (a tall flank is dressed in bands, by its own cells), then down again by
    // about half a piece's height, so the first tier's top reaches the crest.
    private Vector3D BelowCrest(FaceSample start, double pieceHeight, Vector3D cellGround)
    {
        Vector3D at = start.Up, uphill = -start.Forward;
        double gradient = start.Gradient, floor = _surface.HeightAt(start.Up);
        for (int step = 0; step < 30; step++)
        {
            Vector3D next = (at * (_surface.Radius + _surface.HeightAt(at)) + uphill * 1.5).Normalized;
            if (Across(next, cellGround) > MaximumOffset - 4 || _surface.HeightAt(next) - floor > 1.5 * pieceHeight) break;
            if (!Slope(next, out Vector3D forward, out _, out double g) || g < EdgeGrade) break;
            (at, uphill, gradient) = (next, -forward, g);
        }
        double down = Math.Clamp(pieceHeight * 0.45 / Math.Max(gradient, 0.35), 1.5, 12);
        return (at * (_surface.Radius + _surface.HeightAt(at)) - uphill * down).Normalized;
    }

    // One column: tiers stepping down the face from below its crest until its toe (the grade eases) or the tier limit.
    private void Column(TileKey tile, int slot, FaceSample start, CliffRecipe recipe, double groupScale, Vector3D cellGround,
        ref string? family, List<CliffPlacement> result)
    {
        Vector3D direction = BelowCrest(start, recipe.TypicalHeight * groupScale, cellGround);
        if (!Slope(direction, out Vector3D face, out Vector3D right, out _))
        {
            (direction, face, right) = (start.Up, start.Forward, start.Right);
        }
        double previousRadius = double.PositiveInfinity;
        Vector3D previousCentre = direction * (_surface.Radius + _surface.HeightAt(direction)), previousFace = face, previousAlong = right;
        double nextStep = 3, nextStagger = 0;
        for (int tier = 0; tier < MaximumTiers; tier++)
        {
            int part = slot * MaximumTiers + tier;
            var memberRandom = new Rng(Hash64.Of(_seed, tile.Packed, (ulong)(0x200 + part)));
            double scale = groupScale * memberRandom.Range(0.92, 1.08);
            double yaw = memberRandom.Range(-0.10, 0.10);
            CliffPlacement? fitted = null;
            Vector3D rowFace = face, rowAlong = right;
            double localGradient = 0;
            // The first tier tries below the crest, then a little further down; later tiers step down the face and
            // search back along that same slope before giving up. Every probe keeps all fit and clearance checks.
            for (int probe = 0; probe < 3; probe++)
            {
                Vector3D trial = tier == 0
                    ? (previousCentre + previousFace * (3.0 * probe)).Normalized
                    : (previousCentre + previousFace * (nextStep * (1 - probe * 0.3)) + previousAlong * (nextStagger * (1 - probe * 0.3))).Normalized;
                if (!Slope(trial, out Vector3D trialFace, out Vector3D trialAlong, out double trialGradient)) continue;
                if (tier > 0 && trialGradient < EdgeGrade) continue;
                if (Vector3D.Dot(trialFace, face) < 0.5) continue;
                Vector3D meshFace = trialFace * DMath.Cos(yaw) + trialAlong * DMath.Sin(yaw);
                Vector3D meshAlong = Vector3D.Cross(trial, meshFace).Normalized;
                var probeRandom = memberRandom;
                CliffPlacement? candidate = FitMember(tile, part, trial, meshFace, meshAlong, trialGradient, scale, recipe,
                    family, previousRadius - 0.4, ref probeRandom);
                if (candidate is not { } accepted || Across(accepted.Position, cellGround) > MaximumOffset) continue;
                fitted = accepted;
                direction = trial;
                rowFace = trialFace;
                rowAlong = trialAlong;
                localGradient = trialGradient;
                memberRandom = probeRandom;
                break;
            }
            if (fitted is not { } piece) break;
            previousRadius = piece.Position.Length;
            result.Add(piece);
            family ??= CliffGeometry.All[piece.Model].MaterialFamily;
            CliffGeometry geometry = CliffGeometry.All[piece.Model];
            // Member-sized overlap and staggered starts replace the conspicuous rectangular grid.
            // Follow the physical slope, never the scan's potentially reversed local Z axis.
            nextStep = Math.Clamp(geometry.Height * piece.Scale * memberRandom.Range(0.56, 0.68) / localGradient, 2.5, 17);
            previousCentre = direction * (_surface.Radius + _surface.HeightAt(direction));
            previousFace = rowFace;
            previousAlong = rowAlong;
            nextStagger = memberRandom.Range(-1.4, 1.4);
        }
    }

    /// <summary>
    /// A cell's surveyed face samples and whether rock is drawn on each (T2): the intended regions fitting failed to
    /// dress are the uncovered ones, the recipe's and the scan library's repair list.
    /// </summary>
    public IReadOnlyList<CliffFaceSample> Report(TileKey tile)
    {
        if (tile.Level != Level || !MayHoldFace(tile)) return Array.Empty<CliffFaceSample>();
        List<FaceSample> steep = Survey(tile);
        var recipes = new Dictionary<int, CliffRecipe>();
        foreach (List<FaceSample> face in Faces(steep))
        {
            CliffRecipe recipe = RecipeFor(face[Run(face).Middle].Up);
            foreach (FaceSample sample in face) recipes[sample.Index] = recipe;
        }
        var report = new List<CliffFaceSample>(steep.Count);
        foreach (FaceSample sample in steep)
        {
            Vector3D ground = sample.Up * (_surface.Radius + _surface.HeightAt(sample.Up));
            bool covered = false;
            foreach (CliffPlacement cliff in Near(ground, 0))
                if (Holds(cliff, ground, 1)) { covered = true; break; }
            CliffRecipe? recipe = recipes.TryGetValue(sample.Index, out CliffRecipe? found) ? found : null;
            double rise = 0, riseCovered = 0;
            if (sample.Gradient >= WallGrade)
            {
                WallSection section = Section(sample);
                rise = section.Rise;
                List<CliffPlacement> nearby = Near(ground, section.Rise);
                List<CliffWall> skins = WallsNear(ground, section.Rise + 10);
                // Every metre and a half up the face, a level sight line from 4 m out to just behind it: rock where
                // it meets a scan's mesh before the ground.
                for (double y = section.Foot + 0.75; y < section.Crest; y += 1.5)
                {
                    Vector3D from = section.At(y, _surface.Radius) + section.Downslope * 4;
                    bool hit = false;
                    foreach (CliffPlacement cliff in nearby)
                        if (Hits(cliff, from, -section.Downslope, 4.5)) { hit = true; break; }
                    if (!hit)
                        foreach (CliffWall skin in skins)
                            if (Hits(skin, from, -section.Downslope, 4.5)) { hit = true; break; }
                    if (hit) riseCovered += Math.Min(1.5, section.Crest - (y - 0.75));
                }
            }
            report.Add(new CliffFaceSample(ground, sample.Gradient, sample.Gradient >= WallGrade,
                recipe != null && Intends(sample, recipe), covered, recipe?.Key, rise, Math.Min(rise, riseCovered)));
        }
        return report;
    }

    /// <summary>
    /// Whether the segment from a point along a unit direction for a length meets a placed scan's collision mesh (its
    /// box first, then each triangle).
    /// </summary>
    public static bool Hits(CliffPlacement cliff, Vector3D from, Vector3D direction, double length)
    {
        CliffGeometry scan = CliffGeometry.All[cliff.Model];
        double inverse = 1 / cliff.Scale;
        Vector3D offset = from - cliff.Position;
        // The scan's own frame, where the segment is o + d t for t in [0, length].
        var o = new Vector3D(Vector3D.Dot(offset, cliff.Right), Vector3D.Dot(offset, cliff.Up), Vector3D.Dot(offset, cliff.Forward)) * inverse;
        var d = new Vector3D(Vector3D.Dot(direction, cliff.Right), Vector3D.Dot(direction, cliff.Up), Vector3D.Dot(direction, cliff.Forward)) * inverse;
        double near = 0, far = length;
        if (!Slab(o.X, d.X, scan.BoundsMin.X, scan.BoundsMax.X, ref near, ref far) ||
            !Slab(o.Y, d.Y, scan.BoundsMin.Y, scan.BoundsMax.Y, ref near, ref far) ||
            !Slab(o.Z, d.Z, scan.BoundsMin.Z, scan.BoundsMax.Z, ref near, ref far)) return false;
        Vector3D[] p = scan.Positions;
        int[] index = scan.Indices;
        for (int i = 0; i + 2 < index.Length; i += 3)
        {
            Vector3D a = p[index[i]], e1 = p[index[i + 1]] - a, e2 = p[index[i + 2]] - a;
            Vector3D q = Vector3D.Cross(d, e2);
            double det = Vector3D.Dot(e1, q);
            if (Math.Abs(det) < 1e-12) continue;
            double f = 1 / det;
            Vector3D s = o - a;
            double u = Vector3D.Dot(s, q) * f;
            if (u < 0 || u > 1) continue;
            Vector3D r = Vector3D.Cross(s, e1);
            double v = Vector3D.Dot(d, r) * f;
            if (v < 0 || u + v > 1) continue;
            double t = Vector3D.Dot(e2, r) * f;
            if (t >= 0 && t <= length) return true;
        }
        return false;

        static bool Slab(double origin, double step, double min, double max, ref double near, ref double far)
        {
            if (Math.Abs(step) < 1e-12) return origin >= min && origin <= max;
            double t0 = (min - origin) / step, t1 = (max - origin) / step;
            if (t0 > t1) (t0, t1) = (t1, t0);
            near = Math.Max(near, t0);
            far = Math.Min(far, t1);
            return near <= far;
        }
    }

    /// <summary>
    /// Whether a placed scan is a shelf (T2's survey): a third of its lowest fit points both more than 2.5 m over the
    /// ground (or a metre below the water) under them and standing more than 2.25 m out of the ground behind them, so its
    /// underside shows over a drop. A foot a metre or two out of a steep slope is an ordinary outcrop.
    /// </summary>
    public bool IsShelf(CliffPlacement piece)
    {
        CliffGeometry geometry = CliffGeometry.All[piece.Model];
        int low = 0, hanging = 0;
        foreach (Vector3D local in FitPoints.Value[piece.Model])
        {
            if (local.Y >= geometry.Height * 0.18) continue;
            low++;
            Vector3D point = piece.Transform(local), direction = point.Normalized;
            double ground = Math.Max(_surface.HeightAt(direction), _surface.WaterLevelAt(direction) - 1);
            if (point.Length - _surface.Radius - ground > 2.5 && Standoff(point, piece.Downslope) > 2.25) hanging++;
        }
        return low > 0 && hanging * 3 > low;
    }

    // How far a point in the air stands out of the ground behind it: back along a level line into the slope, in
    // three-quarter metres up to six, to where the ground rises above it.
    private double Standoff(Vector3D point, Vector3D downslope)
    {
        Vector3D level = (downslope - point.Normalized * Vector3D.Dot(downslope, point.Normalized)).Normalized;
        for (int step = 1; step <= 8; step++)
        {
            Vector3D behind = point - level * (0.75 * step);
            if (_surface.HeightAt(behind.Normalized) >= behind.Length - _surface.Radius) return 0.75 * step;
        }
        return 6.75;
    }

    /// <summary>Whether a point lies inside a placed scan's collision box grown by <paramref name="margin"/> metres.</summary>
    public static bool Holds(CliffPlacement cliff, Vector3D point, double margin)
    {
        CliffGeometry scan = CliffGeometry.All[cliff.Model];
        Vector3D d = point - cliff.Position;
        double grow = margin / cliff.Scale;
        double x = Vector3D.Dot(d, cliff.Right) / cliff.Scale, y = Vector3D.Dot(d, cliff.Up) / cliff.Scale,
            z = Vector3D.Dot(d, cliff.Forward) / cliff.Scale;
        return x >= scan.BoundsMin.X - grow && x <= scan.BoundsMax.X + grow && y >= scan.BoundsMin.Y - grow &&
            y <= scan.BoundsMax.Y + grow && z >= scan.BoundsMin.Z - grow && z <= scan.BoundsMax.Z + grow;
    }

    private CliffPlacement? FitMember(TileKey tile, int part, Vector3D up, Vector3D forward, Vector3D right,
        double gradient, double scale, CliffRecipe recipe, string? materialFamily, double maximumRadius, ref Rng random)
    {
        IReadOnlyList<CliffGeometry> models = CliffGeometry.All;
        Span<double> weights = stackalloc double[models.Count];
        weights.Clear();
        // The recipe chooses the family by its own weights; within a family the scans share that weight by theirs, so a
        // family of large scans cannot outvote the recipe.
        var families = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < models.Count; i++)
        {
            CliffGeometry model = models[i];
            if ((materialFamily == null || model.MaterialFamily == materialFamily) &&
                gradient >= model.MinimumGradient && gradient <= model.MaximumGradient)
            {
                weights[i] = model.SelectionWeight * Math.Clamp(model.Height / 10, 0.25, 1.4);
                families[model.MaterialFamily] = families.GetValueOrDefault(model.MaterialFamily) + weights[i];
            }
        }
        for (int i = 0; i < models.Count; i++)
            if (weights[i] > 0) weights[i] *= recipe.FamilyWeight(models[i].MaterialFamily) / families[models[i].MaterialFamily];
        // A rejected fit falls back to another suitable scan, without another column of hidden meshes.
        for (int attempt = 0; attempt < models.Count; attempt++)
        {
            int model = random.PickWeighted(weights);
            if (model < 0) break;
            weights[model] = 0;
            CliffGeometry geometry = models[model];
            double fittedScale = Math.Min(scale, (MaximumExtent - 1) / geometry.BoundingRadius);
            var piece = new CliffPlacement(tile, model, up * (_surface.Radius + _surface.HeightAt(up)),
                right * geometry.Facing, up, forward * geometry.Facing, fittedScale, part);
            if (Fit(piece, gradient, recipe.WetToe, out double offset))
            {
                CliffPlacement fitted = piece with { Position = piece.Position + up * offset };
                // Try the remaining compatible scans if this one's burial puts it above the prior row.
                if (fitted.Position.Length < maximumRadius) return fitted;
            }
        }
        return null;
    }

    // A sea cliff's toe may stand in the surf (wetToe): up to 40% of the fit points over water, measured from a metre
    // below the water line rather than the seabed. Elsewhere any point over water refuses the piece.
    private bool Fit(CliffPlacement piece, double gradient, bool wetToe, out double offset)
    {
        CliffGeometry geometry = CliffGeometry.All[piece.Model];
        Vector3D[] points = FitPoints.Value[piece.Model];
        Span<double> gaps = stackalloc double[points.Length];
        double lowContact = double.PositiveInfinity, top = double.NegativeInfinity;
        int wet = 0;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3D point = piece.Transform(points[i]);
            Vector3D direction = point.Normalized;
            double groundHeight = _surface.HeightAt(direction);
            double water = _surface.WaterLevelAt(direction);
            if (groundHeight <= water + 0.3)
            {
                if (!wetToe || ++wet > points.Length * 0.4)
                {
                    offset = 0;
                    return false;
                }
                groundHeight = Math.Max(groundHeight, water - 1);
            }
            gaps[i] = point.Length - _surface.Radius - groundHeight;
            if (points[i].Y < geometry.Height * 0.18) lowContact = Math.Min(lowContact, gaps[i]);
            if (points[i].Y > geometry.Height * 0.75) top = Math.Max(top, gaps[i]);
        }
        gaps.Sort();
        double height = geometry.Height * piece.Scale;
        double embed = Math.Clamp(height * 0.035, 0.2, 0.65);
        // Fit the actual silhouette, not a bounding-box toe: deep scans were buried almost completely
        // by that shortcut, while reversed scans left only a tall detached-looking spike exposed.
        offset = -gaps[(int)((gaps.Length - 1) * 0.28)] - embed;
        offset = Math.Min(offset, -lowContact - embed);
        offset = Math.Min(offset, height * 0.38 - top);
        double normal = Math.Sqrt(1 + gradient * gradient);
        double maximumRelief = Math.Min(6, height * 0.42);
        offset = Math.Min(offset, maximumRelief * normal - gaps[^1]);
        int visible = 0;
        foreach (double gap in gaps) if (gap + offset > embed) visible++;
        // Avoid spending render/collision work on a few remaining tips or a wholly unsupported sheet.
        return visible >= points.Length * 0.3 && visible <= points.Length * 0.85 &&
            Math.Abs(offset) < MaximumExtent && double.IsFinite(offset);
    }

    private static Vector3D[][] BuildFitPoints()
    {
        var result = new Vector3D[CliffGeometry.All.Count][];
        for (int model = 0; model < result.Length; model++)
        {
            CliffGeometry geometry = CliffGeometry.All[model];
            // Extrema in 5 horizontal × 4 vertical bands capture the front, back, toe and crest.
            // This fixed work budget is independent of the render mesh's tessellation.
            var low = new Vector3D?[20];
            var high = new Vector3D?[20];
            foreach (Vector3D p in geometry.Positions)
            {
                int x = Math.Clamp((int)((p.X - geometry.BoundsMin.X) / geometry.Width * 5), 0, 4);
                int y = Math.Clamp((int)((p.Y - geometry.BoundsMin.Y) / (geometry.BoundsMax.Y - geometry.BoundsMin.Y) * 4), 0, 3);
                int cell = y * 5 + x;
                if (low[cell] is not { } a || p.Z < a.Z) low[cell] = p;
                if (high[cell] is not { } b || p.Z > b.Z) high[cell] = p;
            }
            var points = new List<Vector3D>(42);
            for (int i = 0; i < low.Length; i++)
            {
                if (low[i] is { } a) points.Add(a);
                if (high[i] is { } b && low[i] != b) points.Add(b);
            }
            Vector3D bottom = geometry.Positions[0], crest = bottom;
            foreach (Vector3D p in geometry.Positions)
            {
                if (p.Y < bottom.Y) bottom = p;
                if (p.Y > crest.Y) crest = p;
            }
            if (!points.Contains(bottom)) points.Add(bottom);
            if (!points.Contains(crest)) points.Add(crest);
            result[model] = points.ToArray();
        }
        return result;
    }

    private bool Slope(Vector3D up, out Vector3D forward, out Vector3D right, out double gradient)
    {
        forward = right = Vector3D.Zero;
        gradient = 0;
        if (_surface is WalkingWorld walking && walking.Monuments.At(up, MaximumExtent) != null) return false;
        double height = _surface.HeightAt(up);
        if (height < _surface.WaterLevelAt(up) + 0.3) return false;
        Vector3D east = Vector3D.Cross(Vector3D.UnitY, up);
        east = east.LengthSquared > 1e-16 ? east.Normalized : Vector3D.UnitZ;
        Vector3D south = Vector3D.Cross(east, up);
        Vector3D ground = up * (_surface.Radius + height);
        double Rise(Vector3D axis) => (_surface.HeightAt((ground + axis * 5).Normalized)
            - _surface.HeightAt((ground - axis * 5).Normalized)) / 10;
        double x = Rise(east), z = Rise(south);
        gradient = Math.Sqrt(x * x + z * z);
        if (gradient < 0.35 || !double.IsFinite(gradient)) return false;
        forward = -(east * x + south * z).Normalized;
        right = Vector3D.Cross(up, forward).Normalized;
        return true;
    }

    /// <summary>
    /// A wall's section through a face sample, along its downslope a metre at a time: the heights from the crest to the
    /// foot of the steep face that made the sample a wall (each metre rising at least <see cref="WallEdge"/>), in order
    /// downhill. A sample with no such metre within its baseline has a section of one point, and no rise.
    /// </summary>
    private readonly record struct WallSection(Vector3D Origin, Vector3D Downslope, double[] Along, double[] Heights)
    {
        public double Crest => Heights[0];
        public double Foot => Heights[^1];
        public double Rise => Crest - Foot;

        /// <summary>The face at a height between foot and crest: where the section first falls to it.</summary>
        public Vector3D At(double height, double radius)
        {
            double along = Along[^1];
            for (int i = 0; i + 1 < Heights.Length; i++)
                if (Heights[i + 1] <= height)
                {
                    double span = Heights[i] - Heights[i + 1];
                    along = span > 1e-9 ? Along[i] + (Along[i + 1] - Along[i]) * Math.Clamp((Heights[i] - height) / span, 0, 1) : Along[i];
                    break;
                }
            return (Origin + Downslope * along).Normalized * (radius + height);
        }
    }

    private WallSection Section(FaceSample sample)
    {
        // The ground along the sample's downslope a metre apart, 30 m each way; the wall is the steepest metre within the
        // survey's five-metre half-baseline, grown both ways while each metre still rises WallEdge.
        const int Reach = 30;
        Vector3D origin = sample.Up * (_surface.Radius + _surface.HeightAt(sample.Up));
        var heights = new double[2 * Reach + 1];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = _surface.HeightAt((origin + sample.Forward * (i - Reach)).Normalized);
        int steepest = Reach - 5;
        for (int i = Reach - 5; i < Reach + 5; i++)
            if (heights[i] - heights[i + 1] > heights[steepest] - heights[steepest + 1]) steepest = i;
        int first = steepest, last = steepest + 1;
        while (first > 0 && heights[first - 1] - heights[first] >= WallEdge) first--;
        while (last < heights.Length - 1 && heights[last] - heights[last + 1] >= WallEdge) last++;
        if (heights[steepest] - heights[steepest + 1] < WallEdge) first = last = steepest;
        var along = new double[last - first + 1];
        var section = new double[along.Length];
        for (int i = first; i <= last; i++) (along[i - first], section[i - first]) = (i - Reach, heights[i]);
        return new WallSection(origin, sample.Forward, along, section);
    }

    public List<CliffPlacement> Near(Vector3D position, double reach)
    {
        var tiles = new List<TileKey>();
        TileKey.Around(position, reach + MaximumExtent + MaximumOffset, _surface.Radius, Level, tiles);
        var result = new List<CliffPlacement>();
        foreach (TileKey tile in tiles)
        {
            foreach (CliffPlacement cliff in FormationAt(tile))
                if (Vector3D.Distance(position, cliff.Position) <= reach + MaximumExtent) result.Add(cliff);
        }
        return result;
    }

    /// <summary>
    /// The ordinary heightfield plus nearby scan hulls and the walls' rock skins, identical on server, prediction and
    /// ragdoll worlds.
    /// </summary>
    public TriangleMesh Collision(in ZoneFrame frame, TileKey tile, double margin)
    {
        TriangleMesh terrain = SurfaceGrid.Collision(_surface, frame, tile, margin, SurfaceGrid.DefaultSpacing);
        double reach = TileKey.EdgeLength(tile.Level, _surface.Radius) * 0.95 + margin;
        Vector3D centre = tile.Centre * (_surface.Radius + _surface.HeightAt(tile.Centre));
        List<CliffPlacement> nearby = Near(centre, reach);
        List<CliffWall> walls = WallsNear(centre, reach);
        if (nearby.Count == 0 && walls.Count == 0) return terrain;
        var positions = new List<float>(terrain.Positions);
        var indices = new List<int>(terrain.Indices);
        // A skin's first coarser level (every second station and row, about two metres), its triangles turned so their
        // normals point out of the face as the ground's point up.
        ZoneFrame zone = frame;
        foreach (CliffWall wall in walls)
        {
            int[] triangles = wall.Lods[0].Indices;
            var map = new int[wall.Positions.Length / 3];
            Array.Fill(map, -1);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                indices.Add(Vertex(triangles[i]));
                indices.Add(Vertex(triangles[i + 2]));
                indices.Add(Vertex(triangles[i + 1]));
            }

            int Vertex(int v)
            {
                if (map[v] >= 0) return map[v];
                map[v] = positions.Count / 3;
                System.Numerics.Vector3 local = zone.ToLocal(wall.Anchor +
                    new Vector3D(wall.Positions[v * 3], wall.Positions[v * 3 + 1], wall.Positions[v * 3 + 2]));
                positions.Add(local.X); positions.Add(local.Y); positions.Add(local.Z);
                return map[v];
            }
        }
        foreach (CliffPlacement cliff in nearby)
        {
            CliffGeometry mesh = CliffGeometry.All[cliff.Model];
            int offset = positions.Count / 3;
            foreach (Vector3D point in mesh.Positions)
            {
                System.Numerics.Vector3 local = frame.ToLocal(cliff.Transform(point));
                positions.Add(local.X); positions.Add(local.Y); positions.Add(local.Z);
            }
            foreach (int index in mesh.Indices) indices.Add(offset + index);
        }
        return new TriangleMesh(positions.ToArray(), indices.ToArray());
    }
}
