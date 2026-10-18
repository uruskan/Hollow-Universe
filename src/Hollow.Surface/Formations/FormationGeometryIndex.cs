using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

public sealed record FormationGeometryIndexLimits
{
    public int MaxTriangles { get; init; } = 16384;
    public int MaxNodes { get; init; } = 32767;
    public long MaxEstimatedResidentBytes { get; init; } = 16L * 1024 * 1024;
    public long MaxEstimatedScratchBytes { get; init; } = 4L * 1024 * 1024;
}

/// <summary>
/// A shareable immutable local-space BVH. One instance can serve many pieces and many world indexes. The caller
/// owns catalog sharing explicitly; there is no static cache. Triangle ordinals continue to name source geometry.
/// Byte estimates include the geometry retained by this index, not only newly allocated nodes.
/// </summary>
public sealed class FormationGeometryIndex
{
    private readonly Node[] _nodes;

    public FormationGeometryIndex(FormationGeometry geometry, FormationGeometryIndexLimits? limits = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        limits ??= new FormationGeometryIndexLimits();
        if (limits.MaxTriangles < 0 || limits.MaxNodes < 0 || limits.MaxEstimatedResidentBytes < 0 ||
            limits.MaxEstimatedScratchBytes < 0)
            throw new FormationValidationException("Invalid local geometry index limits.", nameof(limits));
        int nodes = NodeCountFor(geometry.TriangleCount);
        long resident = EstimateResident(geometry), scratch = EstimateScratch(geometry.TriangleCount);
        if (geometry.TriangleCount > limits.MaxTriangles || nodes > limits.MaxNodes ||
            resident > limits.MaxEstimatedResidentBytes || scratch > limits.MaxEstimatedScratchBytes)
            throw new FormationCapacityException("Local geometry index exceeds its triangle, node or byte budget.");
        cancellation.ThrowIfCancellationRequested();
        Geometry = geometry;
        EstimatedResidentBytes = resident;
        ReservedScratchBytes = scratch;
        _nodes = new Node[nodes];
        var triangles = new BuildTriangle[geometry.TriangleCount];
        for (int i = 0; i < triangles.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            GeometryTriangle triangle = geometry.Triangles[i];
            Vector3D a = geometry.Vertices[triangle.A], b = geometry.Vertices[triangle.B], c = geometry.Vertices[triangle.C];
            triangles[i] = new BuildTriangle(i, new FormationBounds(FormationBounds.Lower(a, FormationBounds.Lower(b, c)),
                FormationBounds.Upper(a, FormationBounds.Upper(b, c))));
        }
        int next = 0;
        Build(triangles, 0, triangles.Length, ref next, cancellation);
        cancellation.ThrowIfCancellationRequested();
    }

    public FormationGeometry Geometry { get; }
    public int TriangleCount => Geometry.TriangleCount;
    public int NodeCount => _nodes.Length;
    public long EstimatedResidentBytes { get; }
    public long ReservedScratchBytes { get; }
    internal ReadOnlySpan<Node> Nodes => _nodes;
    internal readonly record struct Node(FormationBounds Bounds, int Left, int Right, int Triangle);
    private readonly record struct BuildTriangle(int Ordinal, FormationBounds Bounds);

    internal static int NodeCountFor(int count)
    {
        if (count < 0 || count > (int.MaxValue - 1) / 2)
            throw new FormationCapacityException("A BVH exceeds the representable node count.");
        return count == 0 ? 0 : 2 * count - 1;
    }

    internal static long EstimateResident(FormationGeometry geometry) =>
        1024L + geometry.EstimatedResidentBytes + 96L * NodeCountFor(geometry.TriangleCount);
    internal static long EstimateScratch(int triangles) => 8192L + 128L * triangles;

    internal static Vector3D WindingNormal(Vector3D a, Vector3D b, Vector3D c)
    {
        Vector3D cross = Vector3D.Cross(b - a, c - a);
        double largest = Math.Max(Math.Abs(cross.X), Math.Max(Math.Abs(cross.Y), Math.Abs(cross.Z)));
        Vector3D normal = (cross / largest).Normalized;
        FormationInput.Unit(normal, nameof(normal));
        return normal;
    }

    private int Build(BuildTriangle[] triangles, int start, int count, ref int next, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        int node = next++;
        FormationBounds bounds = triangles[start].Bounds;
        for (int i = start + 1; i < start + count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            bounds = bounds.Union(triangles[i].Bounds);
        }
        if (count == 1) { _nodes[node] = new Node(bounds, -1, -1, triangles[start].Ordinal); return node; }
        int axis = LongestAxis(bounds.Extent);
        Array.Sort(triangles, start, count, new AxisComparer(axis));
        int half = count / 2;
        int left = Build(triangles, start, half, ref next, cancellation);
        int right = Build(triangles, start + half, count - half, ref next, cancellation);
        _nodes[node] = new Node(bounds, left, right, -1);
        return node;
    }

    internal static int LongestAxis(Vector3D extent) => extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
    internal static double Component(Vector3D value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
    private sealed class AxisComparer(int axis) : IComparer<BuildTriangle>
    {
        public int Compare(BuildTriangle a, BuildTriangle b)
        {
            int order = Component(a.Bounds.Centre, axis).CompareTo(Component(b.Bounds.Centre, axis));
            return order != 0 ? order : a.Ordinal.CompareTo(b.Ordinal);
        }
    }
}
