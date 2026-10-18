using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Hollow.Simulation.Content;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Simulation.Nature;

/// <summary>Explicit preparation allowances, not production defaults or measured allocator costs.</summary>
public sealed record NaturalCollisionLimits
{
    public const int MaximumTiles = 1024, MaximumSolids = 262144;
    public const long MaximumCandidateVisits = ScatterGenerationLimits.MaximumCandidates;
    public const long MaximumEstimatedBytes = 128L * 1024 * 1024;
    public required int MaxTiles { get; init; }
    public required long MaxCandidateVisits { get; init; }
    public required int MaxSolids { get; init; }
    public required long MaxEstimatedResidentBytes { get; init; }
    public required long MaxEstimatedScratchBytes { get; init; }
    public required long MaxEstimatedSnapshotBytes { get; init; }

    internal void Validate()
    {
        if (MaxTiles is < 0 or > MaximumTiles || MaxCandidateVisits is < 0 or > MaximumCandidateVisits ||
            MaxSolids is < 0 or > MaximumSolids || MaxEstimatedResidentBytes is < 1 or > MaximumEstimatedBytes ||
            MaxEstimatedScratchBytes is < 1 or > MaximumEstimatedBytes || MaxEstimatedSnapshotBytes is < 1 or > MaximumEstimatedBytes)
            throw new ArgumentOutOfRangeException(nameof(NaturalCollisionLimits));
    }
}

/// <summary>
/// Reserve ResidentBytes plus ScratchBytes until preparation exits; reserve ResidentBytes plus SnapshotBytes while
/// capturing/retaining one depletion snapshot. Every simultaneous job/snapshot needs its own allowance. Capacities
/// include unused output slots, fixed array/object overhead and a transient scalar kind map. No cache credit applies.
/// </summary>
public readonly record struct NaturalCollisionReservation(long CandidateVisits, int OutputCapacity,
    long ResidentBytes, long ScratchBytes, long SnapshotBytes);

/// <summary>Value-only sphere/capsule input. No content object, mutable compound array or native shape is retained.</summary>
public readonly record struct NaturalCollisionShape(PropKind Kind, Vector3 Centre, Quaternion Rotation,
    float Radius, float HalfHeight)
{
    /// <summary>Recreates the exact legacy input, including its native capsule half-height clamp in CreateShape.</summary>
    public PropShape ToPropShape() => new()
    { Kind = Kind, Centre = Centre, Rotation = Rotation, Radius = Radius, HalfHeight = HalfHeight };

    internal static NaturalCollisionShape From(in NaturalObject item, in NaturalCollisionKind kind)
    {
        PropShape shape;
        if (kind.Class == NaturalClass.Tree)
        {
            (Vector3 a, Vector3 b, float radius) = NaturalShapes.Trunk(item, kind.Radius, kind.Height);
            shape = PropShape.Capsule(a, b, radius, item.Up);
        }
        else
        {
            (Vector3 centre, float radius) = NaturalShapes.Rock(item, kind.Radius, kind.Class == NaturalClass.Boulder);
            shape = PropShape.Sphere(centre, radius);
        }
        if (!Finite(item.Position) || !Finite(item.Up) || MathF.Abs(item.Up.LengthSquared() - 1) > .0001f ||
            !float.IsFinite(item.Scale) || item.Scale <= 0 || item.Scale > NaturalShapes.MaximumGeneratedScale ||
            !float.IsFinite(item.Yaw) || !Finite(shape.Centre) || !float.IsFinite(shape.Radius) || shape.Radius <= 0 ||
            !float.IsFinite(shape.HalfHeight) || shape.HalfHeight < 0 ||
            !float.IsFinite(shape.Rotation.X) || !float.IsFinite(shape.Rotation.Y) || !float.IsFinite(shape.Rotation.Z) ||
            !float.IsFinite(shape.Rotation.W) || MathF.Abs(shape.Rotation.LengthSquared() - 1) > .0001f)
            throw new FormationValidationException("Natural collision produced an invalid primitive shape.");
        return new(shape.Kind, shape.Centre, shape.Rotation, shape.Radius, shape.HalfHeight);
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

public readonly record struct NaturalCollisionEntry(NaturalObject Object, NaturalCollisionShape Shape);

internal readonly record struct NaturalCollisionKind(bool Solid, ushort Index, NaturalClass Class,
    float MinScale, float Radius, float Height);

/// <summary>
/// Immutable solid shape inputs for the entire requested root-tile set, in packed tile then original accepted scatter
/// order. The caller supplies complete spatial coverage and owns the source-world pin through all work. This plan
/// proves no arbitrary movement footprint and owns no world, state, callbacks, content definitions or native memory.
/// </summary>
public sealed class NaturalCollisionPlan
{
    private readonly TileKey[] _tiles;
    private readonly NaturalCollisionEntry[] _entries;
    internal object SourceIdentity { get; }
    public int Count { get; }
    public ReadOnlySpan<TileKey> Tiles => _tiles;
    public ReadOnlySpan<NaturalCollisionEntry> Entries => _entries.AsSpan(0, Count);
    public NaturalCollisionReservation Reservation { get; }

    internal NaturalCollisionPlan(object sourceIdentity, TileKey[] tiles, NaturalCollisionEntry[] entries, int count,
        NaturalCollisionReservation reservation)
    { SourceIdentity = sourceIdentity; _tiles = tiles; _entries = entries; Count = count; Reservation = reservation; }

    /// <summary>Pure count/byte admission, before any DensityField, source sampling or output allocation.</summary>
    public static NaturalCollisionReservation Estimate(ScatterPlan scatter, int tileCount, NaturalCollisionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits); limits.Validate();
        if (tileCount < 0) throw new ArgumentOutOfRangeException(nameof(tileCount));
        // Semantic IDs have 16 bits per tile coordinate and 17 bits per candidate. Never silently alias them.
        if (scatter.Level is < 0 or > 16) throw new FormationValidationException("Natural collision scatter level exceeds its semantic ID domain.");
        foreach (int cells in new[] { scatter.TreeCells, scatter.BushCells, scatter.FernCells, scatter.RockCells,
            scatter.BerryCells, scatter.OutcropCells })
            if (cells <= 0 || (long)cells * cells > 131072)
                throw new FormationValidationException("Natural collision candidate cells exceed their semantic ID domain.");
        long perTile = Scatter.CandidateCount(scatter), visits = checked(perTile * tileCount);
        int capacity = (int)Math.Min(visits, limits.MaxSolids);
        long resident = checked(4096L + (long)tileCount * Unsafe.SizeOf<TileKey>() +
            (long)capacity * Unsafe.SizeOf<NaturalCollisionEntry>());
        long scratch = checked(4096L + 256L * Unsafe.SizeOf<NaturalCollisionKind>() +
            (tileCount == 0 ? 0 : Scatter.EstimatedBuildBytes(checked((int)perTile))));
        long snapshot = checked(1024L + capacity);
        if (tileCount > limits.MaxTiles || visits > limits.MaxCandidateVisits ||
            resident > limits.MaxEstimatedResidentBytes || scratch > limits.MaxEstimatedScratchBytes ||
            snapshot > limits.MaxEstimatedSnapshotBytes)
            throw new FormationCapacityException("The complete natural collision plan exceeds its admitted count or byte allowance.");
        return new(visits, capacity, resident, scratch, snapshot);
    }
}

/// <summary>
/// Immutable owner-thread depletion capture. Its opaque provider/state provenance and nonwrapping presence clock
/// must still match immediately before later publication. Health-only changes preserve validity. Retaining this
/// object retains only a value-only plan, presence bits and opaque tokens, never the source world or NaturalState.
/// </summary>
public sealed class NaturalCollisionSnapshot
{
    private readonly bool[] _depleted;
    private readonly object _stateIdentity;
    public NaturalCollisionPlan Plan { get; }
    public ulong PresenceVersion { get; }
    public ReadOnlySpan<bool> Depleted => _depleted;
    private NaturalCollisionSnapshot(NaturalCollisionPlan plan, object stateIdentity, ulong version, bool[] depleted)
    { Plan = plan; _stateIdentity = stateIdentity; PresenceVersion = version; _depleted = depleted; }

    internal static NaturalCollisionSnapshot Capture(NaturalCollisionPlan plan, NaturalState state, CancellationToken cancellation)
    {
        ulong version = state.CollisionPresenceVersion;
        var depleted = new bool[plan.Count];
        for (int i = 0; i < depleted.Length; i++)
        { cancellation.ThrowIfCancellationRequested(); depleted[i] = state.IsDepleted(plan.Entries[i].Object.Id); }
        cancellation.ThrowIfCancellationRequested();
        if (version != state.CollisionPresenceVersion)
            throw new InvalidOperationException("Natural presence changed during its owner-thread collision capture.");
        return new(plan, state.CollisionIdentity, version, depleted);
    }
    internal bool Matches(object sourceIdentity, NaturalState state) =>
        ReferenceEquals(Plan.SourceIdentity, sourceIdentity) && ReferenceEquals(_stateIdentity, state.CollisionIdentity) &&
        PresenceVersion == state.CollisionPresenceVersion;
}
