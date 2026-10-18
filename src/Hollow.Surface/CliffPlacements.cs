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
/// The formation cache's retained entries, including empty results and pending Lazy values. Payload bytes count only
/// completed placement-array elements, excluding array/container overhead and arrays still held by outside readers.
/// Builds counts factory invocations, including failures and duplicates after an in-flight entry was evicted.
/// </summary>
public readonly record struct CliffFormationCacheDiagnostics(int Capacity, int Entries, int CompletedEntries,
    int EmptyEntries, int RetainedParts, long RetainedPayloadBytes, long Hits, long Misses, long Evictions, long Builds);

/// <summary>
/// Sparse, stable geological formations on the walking world's steep dry faces. Candidate streams are separate
/// from harvestable scatter; no natural-object or loot ID changes when this presentation/collision layer is added.
/// </summary>
public sealed class CliffPlacements
{
    /// <summary>Shared collision-generation revision, for save/network compatibility; candidate seeds stay stable.</summary>
    public const int GenerationRevision = 2;
    public const double CellMetres = 48;
    public const double MaximumExtent = 40;
    public const double MaximumOffset = 64;
    public const int MaximumTiers = 3;
    public const int MaximumColumns = 3;
    public const int MaximumParts = MaximumColumns * MaximumTiers;
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

    private CliffPlacement[] Build(TileKey tile)
    {
        var random = new Rng(Hash64.Of(_seed, 0x434C4946, tile.Packed)); // "CLIF"
        if (!random.Chance(0.8)) return Array.Empty<CliffPlacement>();
        Vector3D up = CubeSphere.Direction(tile.Face, tile.U0 + tile.Span * random.Range(0.15, 0.85),
            tile.V0 + tile.Span * random.Range(0.15, 0.85));
        if (!Slope(up, out Vector3D forward, out Vector3D right, out double gradient) || gradient < 0.62)
            return Array.Empty<CliffPlacement>();
        Vector3D ground = up * (_surface.Radius + _surface.HeightAt(up));
        // The candidate stream above is unchanged. Composition has its own address-derived stream,
        // so changing a model, rejecting a member or ending a column never moves another candidate.
        var composition = new Rng(Hash64.Of(_seed, tile.Packed, 0x464F524D)); // "FORM"
        double groupScale = composition.Range(1.35, 1.72);
        var result = new List<CliffPlacement>(MaximumParts);
        CliffPlacement? anchor = null;
        for (int column = 0; column < MaximumColumns; column++)
        {
            var columnRandom = new Rng(Hash64.Of(_seed, tile.Packed, (ulong)(0x100 + column)));
            int side = column == 0 ? 0 : column == 1 ? -1 : 1;
            double width = anchor is { } first ? CliffGeometry.All[first.Model].Width * first.Scale : 0;
            double along = side * width * columnRandom.Range(0.60, 0.72);
            double stagger = side == 0 ? 0 : columnRandom.Range(-2.5, 2.5) / Math.Max(0.65, gradient);
            Vector3D direction = (ground + right * along + forward * stagger).Normalized;
            if (!Slope(direction, out Vector3D face, out _, out _)) continue;
            double previousRadius = double.PositiveInfinity;
            Vector3D previousCentre = Vector3D.Zero, previousFace = face, previousAlong = right;
            double nextStep = 0, nextStagger = 0;
            for (int tier = 0; tier < MaximumTiers; tier++)
            {
                int part = column * MaximumTiers + tier;
                var memberRandom = new Rng(Hash64.Of(_seed, tile.Packed, (ulong)(0x200 + part)));
                double scale = groupScale * memberRandom.Range(0.92, 1.08);
                double yaw = memberRandom.Range(-0.10, 0.10);
                CliffPlacement? fitted = null;
                Vector3D rowFace = face, rowAlong = right;
                double localGradient = 0;
                // A full-mesh stride can overshoot the remaining exposed face or change burial enough
                // that its first fit is not descending. Search back along that same slope before giving
                // up; each probe keeps all fit/clearance checks and creates no additional instance.
                for (int probe = 0; probe < (tier == 0 ? 1 : 3); probe++)
                {
                    double fraction = 1 - probe * 0.3;
                    Vector3D trial = tier == 0 ? direction : (previousCentre +
                        previousFace * (nextStep * fraction) + previousAlong * (nextStagger * fraction)).Normalized;
                    if (!Slope(trial, out Vector3D trialFace, out Vector3D trialAlong, out double trialGradient)) continue;
                    if (Vector3D.Dot(trialFace, face) < 0.5) continue;
                    Vector3D meshFace = trialFace * DMath.Cos(yaw) + trialAlong * DMath.Sin(yaw);
                    Vector3D meshAlong = Vector3D.Cross(trial, meshFace).Normalized;
                    var probeRandom = memberRandom;
                    CliffPlacement? candidate = FitMember(tile, part, trial, meshFace, meshAlong,
                        trialGradient, scale, anchor is { } chosen ? CliffGeometry.All[chosen.Model].MaterialFamily : null,
                        previousRadius - 0.4, ref probeRandom);
                    if (candidate is not { } accepted || Vector3D.Distance(accepted.Position, ground) > MaximumOffset) continue;
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
                anchor ??= piece;
                CliffGeometry geometry = CliffGeometry.All[piece.Model];
                // Member-sized overlap and staggered starts replace the conspicuous rectangular grid.
                // Follow the physical slope, never the scan's potentially reversed local Z axis.
                nextStep = Math.Clamp(geometry.Height * piece.Scale * memberRandom.Range(0.56, 0.68)
                    / localGradient, 2.5, 17);
                previousCentre = direction * (_surface.Radius + _surface.HeightAt(direction));
                previousFace = rowFace;
                previousAlong = rowAlong;
                nextStagger = memberRandom.Range(-1.4, 1.4);
            }
            if (column == 0 && anchor == null) break;
        }
        return result.ToArray();
    }

    private CliffPlacement? FitMember(TileKey tile, int part, Vector3D up, Vector3D forward,
        Vector3D right, double gradient, double scale, string? materialFamily, double maximumRadius, ref Rng random)
    {
        IReadOnlyList<CliffGeometry> models = CliffGeometry.All;
        Span<double> weights = stackalloc double[models.Count];
        weights.Clear();
        for (int i = 0; i < models.Count; i++)
        {
            CliffGeometry model = models[i];
            if ((materialFamily == null || model.MaterialFamily == materialFamily) &&
                gradient >= model.MinimumGradient && gradient <= model.MaximumGradient)
                weights[i] = model.SelectionWeight * Math.Clamp(model.Height / 10, 0.25, 1.4);
        }
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
            if (Fit(piece, gradient, out double offset))
            {
                CliffPlacement fitted = piece with { Position = piece.Position + up * offset };
                // Try the remaining compatible scans if this one's burial puts it above the prior row.
                if (fitted.Position.Length < maximumRadius) return fitted;
            }
        }
        return null;
    }

    private bool Fit(CliffPlacement piece, double gradient, out double offset)
    {
        CliffGeometry geometry = CliffGeometry.All[piece.Model];
        Vector3D[] points = FitPoints.Value[piece.Model];
        Span<double> gaps = stackalloc double[points.Length];
        double lowContact = double.PositiveInfinity, top = double.NegativeInfinity;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3D point = piece.Transform(points[i]);
            Vector3D direction = point.Normalized;
            double groundHeight = _surface.HeightAt(direction);
            if (groundHeight <= _surface.WaterLevelAt(direction) + 0.3)
            {
                offset = 0;
                return false;
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

    /// <summary>The ordinary heightfield plus nearby scan hulls, identical on server, prediction and ragdoll worlds.</summary>
    public TriangleMesh Collision(in ZoneFrame frame, TileKey tile, double margin)
    {
        TriangleMesh terrain = SurfaceGrid.Collision(_surface, frame, tile, margin, SurfaceGrid.DefaultSpacing);
        double reach = TileKey.EdgeLength(tile.Level, _surface.Radius) * 0.95 + margin;
        Vector3D centre = tile.Centre * (_surface.Radius + _surface.HeightAt(tile.Centre));
        List<CliffPlacement> nearby = Near(centre, reach);
        if (nearby.Count == 0) return terrain;
        var positions = new List<float>(terrain.Positions);
        var indices = new List<int>(terrain.Indices);
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
