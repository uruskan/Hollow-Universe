using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Malformed required physical data. No partially validated geometry is published.</summary>
public sealed class FormationValidationException(string message, string? parameter = null) : ArgumentException(message, parameter);

/// <summary>An explicit construction limit was exceeded; this is not an empty physical result.</summary>
public sealed class FormationCapacityException(string message) : InvalidOperationException(message);

[Flags]
public enum SupportCapabilities : byte { None = 0, Walkable = 1, Plantable = 2, Soil = 4 }

/// <summary>Intrinsic patch permissions. Final soil/habitat must not participate in geometric support selection.</summary>
public sealed class SupportPatch
{
    public SupportPatch(string key, string substrateProfileKey, SupportCapabilities capabilities)
    {
        Key = FormationInput.Key(key, nameof(key));
        SubstrateProfileKey = FormationInput.Key(substrateProfileKey, nameof(substrateProfileKey));
        FormationInput.Capabilities(capabilities, nameof(capabilities));
        Capabilities = capabilities;
    }
    public string Key { get; }
    public string SubstrateProfileKey { get; }
    public SupportCapabilities Capabilities { get; }
}

/// <summary>Winding and patch membership are physical data. Ordinal is stable within a geometry revision.</summary>
public readonly record struct GeometryTriangle(int A, int B, int C, int Patch);

public sealed record FormationGeometryLimits
{
    public int MaxVertices { get; init; } = 8192;
    public int MaxTriangles { get; init; } = 16384;
    public int MaxPatches { get; init; } = 256;
    public long MaxEstimatedBytes { get; init; } = 4L * 1024 * 1024;
}

/// <summary>
/// A copied, validated physical mesh. Neither source collections nor returned views can mutate its arrays.
/// Catalog/recipe integration is deliberately absent from this synthetic core.
/// </summary>
public sealed class FormationGeometry
{
    private readonly Vector3D[] _vertices;
    private readonly GeometryTriangle[] _triangles;
    private readonly SupportPatch[] _patches;

    public FormationGeometry(string key, int revision, string familyKey, IEnumerable<Vector3D> vertices,
        IEnumerable<GeometryTriangle> triangles, IEnumerable<SupportPatch> patches, FormationGeometryLimits? limits = null)
    {
        Key = FormationInput.Key(key, nameof(key));
        FamilyKey = FormationInput.Key(familyKey, nameof(familyKey));
        if (revision < 0) throw new FormationValidationException("Geometry revision must be nonnegative.", nameof(revision));
        Revision = revision;
        limits ??= new FormationGeometryLimits();
        if (limits.MaxVertices < 3 || limits.MaxTriangles < 1 || limits.MaxPatches < 1 || limits.MaxEstimatedBytes < 0)
            throw new FormationValidationException("Invalid physical geometry limits.", nameof(limits));
        // Check a conservative upper-bound reservation before reading user enumerators or copying their output.
        long reserved = 512L + 72L * limits.MaxVertices + 48L * limits.MaxTriangles + 2048L * limits.MaxPatches;
        if (reserved > limits.MaxEstimatedBytes)
            throw new FormationCapacityException("Geometry copy/validation reservation exceeds its byte budget.");
        _vertices = FormationInput.Copy(vertices, limits.MaxVertices, nameof(vertices));
        _triangles = FormationInput.Copy(triangles, limits.MaxTriangles, nameof(triangles));
        _patches = FormationInput.Copy(patches, limits.MaxPatches, nameof(patches));
        if (_vertices.Length < 3 || _triangles.Length == 0 || _patches.Length == 0)
            throw new FormationValidationException("Physical meshes need vertices, triangles and patch metadata.");
        foreach (Vector3D vertex in _vertices) FormationInput.Finite(vertex, nameof(vertices));
        var patchKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (SupportPatch patch in _patches)
        {
            if (patch == null || !patchKeys.Add(patch.Key))
                throw new FormationValidationException("Physical patch keys must be nonnull and unique.", nameof(patches));
        }
        foreach (GeometryTriangle triangle in _triangles)
        {
            if ((uint)triangle.A >= (uint)_vertices.Length || (uint)triangle.B >= (uint)_vertices.Length ||
                (uint)triangle.C >= (uint)_vertices.Length || (uint)triangle.Patch >= (uint)_patches.Length)
                throw new FormationValidationException("Triangle vertex or patch index is outside its geometry.", nameof(triangles));
            Vector3D normal = Vector3D.Cross(_vertices[triangle.B] - _vertices[triangle.A], _vertices[triangle.C] - _vertices[triangle.A]);
            if (!double.IsFinite(normal.LengthSquared) || normal.LengthSquared <= 0)
                throw new FormationValidationException("Physical triangles must have finite nonzero area.", nameof(triangles));
        }
        Vector3D low = _vertices[0], high = low;
        foreach (Vector3D vertex in _vertices) { low = FormationBounds.Lower(low, vertex); high = FormationBounds.Upper(high, vertex); }
        Bounds = new FormationBounds(low, high);
        EstimatedResidentBytes = 512L + 24L * _vertices.Length + 16L * _triangles.Length + 1024L * _patches.Length;
    }

    public string Key { get; }
    public int Revision { get; }
    public string FamilyKey { get; }
    public int VertexCount => _vertices.Length;
    public int TriangleCount => _triangles.Length;
    public int PatchCount => _patches.Length;
    public ReadOnlySpan<Vector3D> Vertices => _vertices;
    public ReadOnlySpan<GeometryTriangle> Triangles => _triangles;
    public ReadOnlySpan<SupportPatch> Patches => _patches;
    public FormationBounds Bounds { get; }
    /// <summary>Conservative accounting estimate; not a managed-heap measurement.</summary>
    public long EstimatedResidentBytes { get; }

    internal bool SameDefinition(FormationGeometry other)
    {
        if (Key != other.Key || Revision != other.Revision || FamilyKey != other.FamilyKey ||
            !_vertices.AsSpan().SequenceEqual(other._vertices) || !_triangles.AsSpan().SequenceEqual(other._triangles) ||
            _patches.Length != other._patches.Length) return false;
        for (int i = 0; i < _patches.Length; i++)
            if (_patches[i].Key != other._patches[i].Key || _patches[i].SubstrateProfileKey != other._patches[i].SubstrateProfileKey ||
                _patches[i].Capabilities != other._patches[i].Capabilities) return false;
        return true;
    }
}

/// <summary>Finite body-space AABB. Touching bounds intersect; zero extent is valid for a planar triangle.</summary>
public readonly record struct FormationBounds
{
    public FormationBounds(Vector3D min, Vector3D max)
    {
        FormationInput.Finite(min, nameof(min)); FormationInput.Finite(max, nameof(max));
        if (min.X > max.X || min.Y > max.Y || min.Z > max.Z || !double.IsFinite((max - min).LengthSquared))
            throw new FormationValidationException("Invalid formation bounds.");
        Min = min; Max = max;
    }
    public Vector3D Min { get; }
    public Vector3D Max { get; }
    public Vector3D Centre => Min * 0.5 + Max * 0.5;
    public Vector3D Extent => Max - Min;
    public bool Contains(Vector3D point) => point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y && point.Z >= Min.Z && point.Z <= Max.Z;
    public bool Intersects(FormationBounds other) => Min.X <= other.Max.X && Max.X >= other.Min.X && Min.Y <= other.Max.Y && Max.Y >= other.Min.Y && Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;
    public FormationBounds Union(FormationBounds other) => new(Lower(Min, other.Min), Upper(Max, other.Max));
    internal static Vector3D Lower(Vector3D a, Vector3D b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    internal static Vector3D Upper(Vector3D a, Vector3D b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
}

/// <summary>Rigid right-handed basis plus positive uniform scale; no implicit normalization changes inputs.</summary>
public readonly record struct FormationTransform
{
    public FormationTransform(Vector3D position, Vector3D right, Vector3D up, Vector3D forward, double scale)
    {
        FormationInput.Finite(position, nameof(position));
        FormationInput.Unit(right, nameof(right)); FormationInput.Unit(up, nameof(up)); FormationInput.Unit(forward, nameof(forward));
        if (Math.Abs(Vector3D.Dot(right, up)) > 1e-10 || Math.Abs(Vector3D.Dot(right, forward)) > 1e-10 ||
            Math.Abs(Vector3D.Dot(up, forward)) > 1e-10 || (Vector3D.Cross(right, up) - forward).LengthSquared > 1e-20 ||
            !double.IsFinite(scale) || scale <= 0)
            throw new FormationValidationException("Formation transform must have a right-handed orthonormal basis and positive finite scale.");
        Position = position; Right = right; Up = up; Forward = forward; Scale = scale;
    }
    public static FormationTransform Identity => new(Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, 1);
    public Vector3D Position { get; }
    public Vector3D Right { get; }
    public Vector3D Up { get; }
    public Vector3D Forward { get; }
    public double Scale { get; }
    public Vector3D Transform(Vector3D point) => Position + (Right * point.X + Up * point.Y + Forward * point.Z) * Scale;
    internal void Validate() => _ = new FormationTransform(Position, Right, Up, Forward, Scale);
}

internal static class FormationInput
{
    internal static string Key(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value != value.Trim())
            throw new FormationValidationException("Keys must contain 1–128 characters without outer whitespace.", name);
        return value;
    }
    internal static void Finite(Vector3D value, string name)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z) || !double.IsFinite(value.LengthSquared))
            throw new FormationValidationException("A finite vector with representable length is required.", name);
    }
    internal static void Unit(Vector3D value, string name)
    {
        Finite(value, name);
        if (Math.Abs(value.LengthSquared - 1) > 1e-10) throw new FormationValidationException("A unit vector is required.", name);
    }
    internal static void Capabilities(SupportCapabilities value, string name)
    {
        if ((value & ~(SupportCapabilities.Walkable | SupportCapabilities.Plantable | SupportCapabilities.Soil)) != 0)
            throw new FormationValidationException("Unknown support capability.", name);
    }
    internal static T[] Copy<T>(IEnumerable<T> source, int maximum, string name)
    {
        ArgumentNullException.ThrowIfNull(source, name);
        var result = new List<T>();
        foreach (T value in source)
        {
            if (result.Count >= maximum) throw new FormationCapacityException(name + " exceeds its declared count budget.");
            result.Add(value);
        }
        return result.ToArray();
    }
}
