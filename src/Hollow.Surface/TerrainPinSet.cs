using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Optional immutable contact data. Absence or null preserves legacy terrain behavior.</summary>
public interface ITerrainPinnedSurface { TerrainPinSet? TerrainPins { get; } }

public readonly record struct TerrainPinnedVertex(CubeVertexKey Vertex, Vector3D Position, Vector3D Normal);

/// <summary>Explicit caller reservations; these are not production defaults.</summary>
public sealed record TerrainPinSetLimits
{
    public required int MaxVertices { get; init; }
    public required int MaxInputPins { get; init; }
    public required int MaxNormalTriangles { get; init; }
    public required int MaxAdditionalSamples { get; init; }
    public required long MaxEstimatedResidentBytes { get; init; }
    public required long MaxEstimatedScratchBytes { get; init; }
}

public readonly record struct TerrainPinSetDiagnostics(int Vertices, int InputPins, int NormalTriangles,
    int AdditionalSamples, long ReservedResidentBytes, long ReservedScratchBytes);

/// <summary>
/// Sparse immutable frozen positions and canonical incident-triangle normals. Retains no field, terrain sampler,
/// world, lease, task or native object. It preserves accepted pins; it neither extends the guard nor proves a seal.
/// </summary>
public sealed class TerrainPinSet
{
    public const string Contract = "canonical-fixed-terrain-pins-v1";
    public const int MaximumVertices = 65536;
    public const int MaximumInputPins = 1048576;
    public const int MaximumNormalTriangles = 1048576;
    public const int MaximumAdditionalSamples = 589824;
    public const long MaximumResidentBytes = 16L * 1024 * 1024;
    public const long MaximumScratchBytes = 32L * 1024 * 1024;
    private readonly TerrainPinnedVertex[] _vertices;

    private TerrainPinSet(TerrainLattice lattice, string terrainIdentity, string generationDigest,
        TerrainPinnedVertex[] vertices, TerrainPinSetDiagnostics diagnostics, bool supportsCanonicalRadialTrace)
    {
        Lattice = lattice; TerrainIdentity = terrainIdentity; GenerationDigest = generationDigest;
        _vertices = vertices; Diagnostics = diagnostics; SupportsCanonicalRadialTrace = supportsCanonicalRadialTrace;
    }
    public TerrainLattice Lattice { get; }
    public string TerrainIdentity { get; }
    public string GenerationDigest { get; }
    public ReadOnlySpan<TerrainPinnedVertex> Vertices => _vertices;
    public TerrainPinSetDiagnostics Diagnostics { get; }
    /// <summary>
    /// Every frozen position is exactly reproducible as its canonical direction times a positive finite scalar.
    /// Computed once within the existing admitted construction scan. False does not invalidate public pins;
    /// it excludes angular-cone tracing, which cannot silently discard their displaced incident triangles.
    /// </summary>
    public bool SupportsCanonicalRadialTrace { get; }

    public bool TryGet(CubeVertexKey vertex, out TerrainPinnedVertex pin)
    {
        int index = Find(_vertices, vertex);
        pin = index >= 0 ? _vertices[index] : default;
        return index >= 0;
    }

    /// <summary>Validates opt-in ownership once per operation, before terrain buffers are allocated.</summary>
    public static TerrainPinSet? Of(ISurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        TerrainPinSet? pins = (surface as ITerrainPinnedSurface)?.TerrainPins;
        if (pins == null) return null;
        TerrainLattice? lattice = TerrainLattice.Of(surface);
        FormationField? field = (surface as IFormationSurface)?.Formations;
        if (lattice == null || field == null || lattice.Radius != pins.Lattice.Radius ||
            lattice.FinestTileLevel != pins.Lattice.FinestTileLevel || field.GenerationDigest != pins.GenerationDigest ||
            field.Contract.TerrainIdentity != pins.TerrainIdentity || field.Contract.Radius != lattice.Radius ||
            field.Contract.FinestTileLevel != lattice.FinestTileLevel)
            throw new FormationValidationException("Terrain pins do not belong to the selected surface field/lattice.");
        return pins;
    }

    /// <summary>
    /// The owning factory supplies the finalized source identity: IGradedTerrain has no identity property. Matching
    /// that value validates the declared contract, not an arbitrary sampler implementation. Failure throws before
    /// publishing any pin set. Additional normal samples are local bounded stencils, never new pinned vertices.
    /// </summary>
    public static TerrainPinSet Build(IGradedTerrain finalized, TerrainLattice lattice, string terrainIdentity,
        FormationField field, TerrainPinSetLimits limits, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(finalized); ArgumentNullException.ThrowIfNull(lattice);
        ArgumentNullException.ThrowIfNull(field); ArgumentNullException.ThrowIfNull(limits);
        terrainIdentity = CliffSupportValidation.Digest(terrainIdentity);
        if (!finalized.IsFinalized || finalized.Radius != lattice.Radius || field.Contract.TerrainIdentity != terrainIdentity)
            throw new FormationValidationException("Terrain pin construction requires its finalized owning terrain.");
        field.Contract.Validate(lattice);
        (long resident, long scratch) = Reserve(limits);
        cancellation.ThrowIfCancellationRequested();
        if (field.Cohorts.Length > 4096) throw new FormationCapacityException("Terrain pin cohort visit ceiling exceeded.");
        var unique = new SortedDictionary<CubeVertexKey, Vector3D>();
        int input = 0, triangles = 0, samples = 0;
        foreach (FormationCohort cohort in field.Cohorts)
        foreach (FormationTerrainPin pin in cohort.Pins)
        {
            cancellation.ThrowIfCancellationRequested();
            Charge(ref input, limits.MaxInputPins, "input pin");
            if (!lattice.Contains(pin.Vertex) || !ValidPosition(pin.Position))
                throw new FormationValidationException("Invalid frozen terrain pin.");
            if (unique.TryGetValue(pin.Vertex, out Vector3D previous))
            {
                if (previous != pin.Position) throw new FormationValidationException("Conflicting frozen positions for one terrain vertex.");
            }
            else
            {
                if (unique.Count == limits.MaxVertices) throw new FormationCapacityException("Terrain pin vertex capacity exceeded.");
                unique.Add(pin.Vertex, pin.Position);
            }
        }
        var entries = new TerrainPinnedVertex[unique.Count];
        int next = 0;
        bool radialTrace = true;
        foreach (var pair in unique)
        {
            cancellation.ThrowIfCancellationRequested();
            entries[next++] = new TerrainPinnedVertex(pair.Key, pair.Value, default);
            radialTrace &= IsCanonicalRadialProduct(lattice.Direction(pair.Key), pair.Value);
        }
        unique.Clear();
        Span<TileKey> cells = stackalloc TileKey[4];
        var localKeys = new CubeVertexKey[9];
        var localPositions = new Vector3D[9];
        for (int index = 0; index < entries.Length; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            CubeVertexKey vertex = entries[index].Vertex;
            int cellCount = lattice.IncidentCells(vertex, cells), localCount = 0;
            Vector3D sum = default;
            for (int cell = 0; cell < cellCount; cell++)
            for (int half = 0; half < 2; half++)
            {
                cancellation.ThrowIfCancellationRequested();
                TerrainLatticeTriangle triangle = lattice.Triangle(cells[cell], half);
                if (triangle.A != vertex && triangle.B != vertex && triangle.C != vertex) continue;
                Charge(ref triangles, limits.MaxNormalTriangles, "normal triangle");
                Vector3D a = Position(triangle.A), b = Position(triangle.B), c = Position(triangle.C);
                Vector3D normal = Vector3D.Cross(b - a, c - a);
                if (!Finite(normal) || Vector3D.Dot(normal, a) <= 0)
                    throw new FormationValidationException("Pinned normal stencil contains an inward or degenerate triangle.");
                sum += normal;
            }
            Vector3D result = sum.Normalized;
            if (!Finite(result) || result == Vector3D.Zero)
                throw new FormationValidationException("Pinned normal is not finite and representable.");
            entries[index] = entries[index] with { Normal = result };

            Vector3D Position(CubeVertexKey key)
            {
                int found = Find(entries, key);
                if (found >= 0) return entries[found].Position;
                for (int k = 0; k < localCount; k++) if (localKeys[k] == key) return localPositions[k];
                cancellation.ThrowIfCancellationRequested();
                Charge(ref samples, limits.MaxAdditionalSamples, "additional normal sample");
                Vector3D direction = lattice.Direction(key);
                Vector3D point = direction * (lattice.Radius + finalized.HeightAt(direction));
                if (!ValidPosition(point)) throw new FormationValidationException("Invalid radial normal stencil position.");
                if (localCount == localKeys.Length) throw new FormationValidationException("Canonical normal stencil exceeds nine vertices.");
                localKeys[localCount] = key; localPositions[localCount++] = point;
                return point;
            }
        }
        cancellation.ThrowIfCancellationRequested();
        return new TerrainPinSet(lattice, terrainIdentity, field.GenerationDigest, entries,
            new TerrainPinSetDiagnostics(entries.Length, input, triangles, samples, resident, scratch), radialTrace);
    }

    internal static bool IsCanonicalRadialProduct(Vector3D direction, Vector3D position)
    {
        int axis = Math.Abs(direction.X) >= Math.Abs(direction.Y) && Math.Abs(direction.X) >= Math.Abs(direction.Z) ? 0 :
            Math.Abs(direction.Y) >= Math.Abs(direction.Z) ? 1 : 2;
        double Component(Vector3D value) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
        double candidate = Component(position) / Component(direction);
        bool Exact(double scalar) => double.IsFinite(scalar) && scalar > 0 && direction * scalar == position;
        // Division can move the original multiplication scalar by adjacent representable values. Only exact
        // reconstruction is accepted; these are candidate scalars, not tolerance on the frozen geometry.
        if (Exact(candidate)) return true;
        double low = candidate, high = candidate;
        for (int step = 0; step < 2; step++)
        {
            low = Math.BitDecrement(low); high = Math.BitIncrement(high);
            if (Exact(low) || Exact(high)) return true;
        }
        return false;
    }

    private static int Find(TerrainPinnedVertex[] entries, CubeVertexKey key)
    {
        int low = 0, high = entries.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2, order = entries[middle].Vertex.CompareTo(key);
            if (order == 0) return middle;
            if (order < 0) low = middle + 1; else high = middle - 1;
        }
        return -1;
    }
    private static bool Finite(Vector3D v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
    private static bool ValidPosition(Vector3D v) => Finite(v) && v != Vector3D.Zero &&
        Math.Abs(v.X) <= FormationTerrainSeal.MaximumCoordinateMetres &&
        Math.Abs(v.Y) <= FormationTerrainSeal.MaximumCoordinateMetres && Math.Abs(v.Z) <= FormationTerrainSeal.MaximumCoordinateMetres;
    private static void Charge(ref int count, int maximum, string name)
    { if (count == maximum) throw new FormationCapacityException("Terrain pin " + name + " capacity exceeded."); count++; }
    private static void ValidateLimits(TerrainPinSetLimits limits)
    {
        if (limits.MaxVertices < 0 || limits.MaxVertices > MaximumVertices || limits.MaxInputPins < 0 || limits.MaxInputPins > MaximumInputPins ||
            limits.MaxNormalTriangles < 0 || limits.MaxNormalTriangles > MaximumNormalTriangles ||
            limits.MaxAdditionalSamples < 0 || limits.MaxAdditionalSamples > MaximumAdditionalSamples ||
            limits.MaxEstimatedResidentBytes < 0 || limits.MaxEstimatedResidentBytes > MaximumResidentBytes ||
            limits.MaxEstimatedScratchBytes < 0 || limits.MaxEstimatedScratchBytes > MaximumScratchBytes)
            throw new FormationValidationException("Invalid explicit terrain pin limits.");
    }

    /// <summary>The same pure reservation is used before world dispatch and before pin allocation.</summary>
    internal static (long Resident, long Scratch) Reserve(TerrainPinSetLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ValidateLimits(limits);
        long resident = 4096L + 96L * limits.MaxVertices;
        long scratch = 16384L + 256L * limits.MaxVertices;
        if (resident > limits.MaxEstimatedResidentBytes || scratch > limits.MaxEstimatedScratchBytes)
            throw new FormationCapacityException("Terrain pin reservation exceeds its caller allowance.");
        return (resident, scratch);
    }
}
