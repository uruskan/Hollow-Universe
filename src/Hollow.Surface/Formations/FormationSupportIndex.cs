using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

public sealed record FormationSupportIndexLimits
{
    public int MaxPieces { get; init; } = 4096;
    /// <summary>Logical triangles across all instances, even when their local geometry is shared.</summary>
    public int MaxTriangles { get; init; } = 65536;
    /// <summary>Actual world nodes plus local nodes counted once per distinct geometry key/revision.</summary>
    public int MaxNodes { get; init; } = 131071;
    public int MaxUniqueGeometries { get; init; } = 4096;
    public int MaxUniqueTriangles { get; init; } = 65536;
    public long MaxEstimatedResidentBytes { get; init; } = 128L * 1024 * 1024;
    public long MaxEstimatedScratchBytes { get; init; } = 8L * 1024 * 1024;
}

/// <summary>Triangles are instanced totals; local storage is reported separately and is never silently substituted.</summary>
public readonly record struct FormationSupportIndexDiagnostics(int Pieces, int Triangles, int Nodes,
    long EstimatedResidentBytes, long ReservedResidentBytes, long ReservedScratchBytes)
{
    public int UniqueGeometries { get; init; }
    public int LocalTriangles { get; init; }
    public int WorldNodes { get; init; }
    public int LocalNodes { get; init; }
    public long WorldEstimatedResidentBytes { get; init; }
    /// <summary>Unique immutable catalog geometry and local BVHs retained by this world; may be shared with others.</summary>
    public long SharedGeometryEstimatedResidentBytes { get; init; }
}

/// <summary>
/// Immutable world BVH over piece instances, each referring to one shared local geometry BVH. There are no
/// expanded world-space triangle arrays, lazy callbacks, tile discovery or static caches. A caller supplying
/// prebuilt local indexes can share them across worlds; accounting still reports everything retained by each
/// world, with the shareable portion separated. Reservations are estimates, not managed-heap measurements.
/// </summary>
public sealed class FormationSupportIndex
{
    private readonly Instance[] _instances;
    private readonly Node[] _nodes;
    private readonly FormationGeometryIndex[] _geometryIndexes;
    public FormationSupportIndexDiagnostics Diagnostics { get; }
    public ReadOnlySpan<FormationGeometryIndex> GeometryIndexes => _geometryIndexes;

    public FormationSupportIndex(IEnumerable<FormationPiece> pieces, FormationSupportIndexLimits? limits = null,
        CancellationToken cancellation = default)
        : this(pieces, Array.Empty<FormationGeometryIndex>(), limits, cancellation) { }

    public FormationSupportIndex(IEnumerable<FormationPiece> pieces, IReadOnlyList<FormationGeometryIndex> sharedGeometryIndexes,
        FormationSupportIndexLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(pieces); ArgumentNullException.ThrowIfNull(sharedGeometryIndexes);
        limits ??= new FormationSupportIndexLimits();
        if (limits.MaxPieces < 0 || limits.MaxPieces > (int.MaxValue - 1) / 2 || limits.MaxTriangles < 0 ||
            limits.MaxNodes < 0 || limits.MaxUniqueGeometries < 0 || limits.MaxUniqueTriangles < 0 ||
            limits.MaxEstimatedResidentBytes < 0 || limits.MaxEstimatedScratchBytes < 0)
            throw new FormationValidationException("Invalid support index limits.", nameof(limits));
        long residentReservation = EstimateWorld(limits.MaxPieces,
            Math.Min(limits.MaxNodes, FormationGeometryIndex.NodeCountFor(limits.MaxPieces)));
        long scratchReservation = 8192L + 256L * limits.MaxPieces + 256L * limits.MaxUniqueGeometries;
        if (residentReservation > limits.MaxEstimatedResidentBytes || scratchReservation > limits.MaxEstimatedScratchBytes)
            throw new FormationCapacityException("World support index reservation exceeds its resident or scratch budget.");
        cancellation.ThrowIfCancellationRequested();
        int suppliedCount = sharedGeometryIndexes.Count;
        if (suppliedCount < 0 || suppliedCount > limits.MaxUniqueGeometries)
            throw new FormationCapacityException("Shared geometry catalog exceeds its entry budget.");
        var available = new Dictionary<(string Key, int Revision), FormationGeometryIndex>();
        for (int i = 0; i < suppliedCount; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            FormationGeometryIndex local = sharedGeometryIndexes[i];
            if (local == null) throw new FormationValidationException("Shared geometry indexes cannot be null.");
            var key = (local.Geometry.Key, local.Geometry.Revision);
            if (available.TryGetValue(key, out FormationGeometryIndex? prior) && !prior.Geometry.SameDefinition(local.Geometry))
                throw new FormationValidationException("Shared geometry catalog contains conflicting definitions.");
            available.TryAdd(key, local);
        }

        var snapshot = new List<FormationPiece>();
        int instancedTriangles = 0;
        foreach (FormationPiece piece in pieces)
        {
            cancellation.ThrowIfCancellationRequested();
            if (piece == null) throw new FormationValidationException("Support pieces cannot be null.", nameof(pieces));
            if (snapshot.Count == limits.MaxPieces || piece.Geometry.TriangleCount > limits.MaxTriangles - instancedTriangles)
                throw new FormationCapacityException("Support index piece or instanced triangle limit exceeded.");
            instancedTriangles += piece.Geometry.TriangleCount;
            snapshot.Add(piece);
        }
        snapshot.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        int worldNodes = FormationGeometryIndex.NodeCountFor(snapshot.Count);
        int localTriangles = 0, localNodes = 0;
        long sharedBytes = 0, peakLocalScratch = 0;
        var definitions = new Dictionary<(string Key, int Revision), FormationGeometry>();
        for (int i = 0; i < snapshot.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            FormationPiece piece = snapshot[i];
            if (i > 0 && snapshot[i - 1].Id == piece.Id)
                throw new FormationValidationException("Duplicate support piece identity.", nameof(pieces));
            FormationGeometry geometry = piece.Geometry;
            var key = (geometry.Key, geometry.Revision);
            if (definitions.TryGetValue(key, out FormationGeometry? prior))
            {
                if (!ReferenceEquals(prior, geometry) && !prior.SameDefinition(geometry))
                    throw new FormationValidationException("One geometry key/revision has conflicting physical definitions.", nameof(pieces));
                continue;
            }
            if (definitions.Count == limits.MaxUniqueGeometries || geometry.TriangleCount > limits.MaxUniqueTriangles - localTriangles)
                throw new FormationCapacityException("Support index unique geometry or local triangle limit exceeded.");
            int requiredNodes = FormationGeometryIndex.NodeCountFor(geometry.TriangleCount);
            if ((long)worldNodes + localNodes + requiredNodes > limits.MaxNodes)
                throw new FormationCapacityException("Support index world and local node limit exceeded.");
            if (available.TryGetValue(key, out FormationGeometryIndex? existing))
            {
                if (!ReferenceEquals(existing.Geometry, geometry) && !existing.Geometry.SameDefinition(geometry))
                    throw new FormationValidationException("A supplied geometry index conflicts with its requested piece.", nameof(sharedGeometryIndexes));
                geometry = existing.Geometry;
            }
            else peakLocalScratch = Math.Max(peakLocalScratch, FormationGeometryIndex.EstimateScratch(geometry.TriangleCount));
            long bytes = FormationGeometryIndex.EstimateResident(geometry);
            if (bytes > limits.MaxEstimatedResidentBytes - residentReservation - sharedBytes)
                throw new FormationCapacityException("Retained shared geometry exceeds the world resident reservation.");
            sharedBytes += bytes;
            if (peakLocalScratch > limits.MaxEstimatedScratchBytes - scratchReservation)
                throw new FormationCapacityException("Local index construction exceeds the world scratch reservation.");
            localTriangles += geometry.TriangleCount; localNodes += requiredNodes;
            definitions.Add(key, geometry);
        }
        if ((long)worldNodes + localNodes > limits.MaxNodes)
            throw new FormationCapacityException("Support index world and local node limit exceeded.");
        residentReservation += sharedBytes;
        scratchReservation += peakLocalScratch;

        // All count/byte admission checks precede local BVH construction and world storage allocation.
        var catalog = new List<FormationGeometry>(definitions.Values);
        catalog.Sort(static (a, b) => { int c = StringComparer.Ordinal.Compare(a.Key, b.Key); return c != 0 ? c : a.Revision.CompareTo(b.Revision); });
        _geometryIndexes = new FormationGeometryIndex[catalog.Count];
        for (int i = 0; i < catalog.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            FormationGeometry geometry = catalog[i];
            var key = (geometry.Key, geometry.Revision);
            if (!available.TryGetValue(key, out FormationGeometryIndex? local))
            {
                local = new FormationGeometryIndex(geometry, new FormationGeometryIndexLimits
                {
                    MaxTriangles = geometry.TriangleCount,
                    MaxNodes = FormationGeometryIndex.NodeCountFor(geometry.TriangleCount),
                    MaxEstimatedResidentBytes = FormationGeometryIndex.EstimateResident(geometry),
                    MaxEstimatedScratchBytes = FormationGeometryIndex.EstimateScratch(geometry.TriangleCount),
                }, cancellation);
                available.Add(key, local);
            }
            _geometryIndexes[i] = local;
        }
        _instances = new Instance[snapshot.Count];
        _nodes = new Node[worldNodes];
        for (int i = 0; i < snapshot.Count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            FormationPiece source = snapshot[i];
            FormationGeometryIndex local = available[(source.Geometry.Key, source.Geometry.Revision)];
            // Equal separate geometry allocations must not remain secretly retained through the original piece.
            FormationPiece canonical = ReferenceEquals(source.Geometry, local.Geometry) ? source
                : new FormationPiece(source.Id, source.Role, local.Geometry, source.Transform);
            _instances[i] = new Instance(canonical, local);
        }
        int nextNode = 0;
        if (_instances.Length > 0) Build(0, _instances.Length, ref nextNode, cancellation);
        cancellation.ThrowIfCancellationRequested();
        long worldBytes = EstimateWorld(_instances.Length, worldNodes);
        Diagnostics = new FormationSupportIndexDiagnostics(snapshot.Count, instancedTriangles, worldNodes + localNodes,
            worldBytes + sharedBytes, residentReservation, scratchReservation)
        {
            UniqueGeometries = catalog.Count, LocalTriangles = localTriangles, WorldNodes = worldNodes, LocalNodes = localNodes,
            WorldEstimatedResidentBytes = worldBytes, SharedGeometryEstimatedResidentBytes = sharedBytes,
        };
    }

    private static long EstimateWorld(int pieces, int nodes) => 1024L + 1024L * pieces + 96L * nodes;
    private int Build(int start, int count, ref int next, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        int node = next++;
        FormationBounds bounds = _instances[start].Piece.Bounds;
        for (int i = start + 1; i < start + count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            bounds = bounds.Union(_instances[i].Piece.Bounds);
        }
        if (count == 1) { _nodes[node] = new Node(bounds, -1, -1, start); return node; }
        Array.Sort(_instances, start, count, new AxisComparer(FormationGeometryIndex.LongestAxis(bounds.Extent)));
        int half = count / 2;
        int left = Build(start, half, ref next, cancellation);
        int right = Build(start + half, count - half, ref next, cancellation);
        _nodes[node] = new Node(bounds, left, right, -1);
        return node;
    }

    internal ReadOnlySpan<Instance> Instances => _instances;
    internal ReadOnlySpan<Node> Nodes => _nodes;
    internal readonly record struct Instance(FormationPiece Piece, FormationGeometryIndex Geometry);
    internal readonly record struct Node(FormationBounds Bounds, int Left, int Right, int Instance);
    private sealed class AxisComparer(int axis) : IComparer<Instance>
    {
        public int Compare(Instance a, Instance b)
        {
            int order = FormationGeometryIndex.Component(a.Piece.Bounds.Centre, axis)
                .CompareTo(FormationGeometryIndex.Component(b.Piece.Bounds.Centre, axis));
            return order != 0 ? order : a.Piece.Id.CompareTo(b.Piece.Id);
        }
    }
}
