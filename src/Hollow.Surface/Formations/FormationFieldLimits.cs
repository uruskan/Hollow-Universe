using System;

namespace Hollow.Surface;

/// <summary>Explicit caller reservations, not production defaults. All exhaustion refuses the whole field.</summary>
public sealed record FormationFieldLimits
{
    public required FormationPlannerLimits Planner { get; init; }
    public required FormationSupportIndexLimits Support { get; init; }
    public required FormationTerrainSealLimits Seal { get; init; }
    public required int MaxPieces { get; init; }
    public required int MaxLoopChecks { get; init; }
    public required int MaxReceipts { get; init; }
    public required int MaxCohorts { get; init; }
    public required int MaxRejections { get; init; }
    public required int MaxCandidateTrianglesPerLoop { get; init; }
    /// <summary>Receipt plus cohort copies, including provisional receipts subsequently rejected by cohort policy.</summary>
    public required int MaxDependencyTriangles { get; init; }
    public required int MaxPinnedVertices { get; init; }
    public required int MaxDependencyTiles { get; init; }
    public required long MaxCandidateCellVisits { get; init; }
    public required long MaxTerrainVertexSamples { get; init; }
    public required long MaxCandidateTriangleTests { get; init; }
    public required long MaxSegmentTriangleAttempts { get; init; }
    public required long MaxExactArithmeticOperations { get; init; }
    public required long MaxEstimatedResidentBytes { get; init; }
    public required long MaxEstimatedScratchBytes { get; init; }

    internal (long Resident, long Scratch) Reserve()
    {
        ArgumentNullException.ThrowIfNull(Planner); ArgumentNullException.ThrowIfNull(Support); ArgumentNullException.ThrowIfNull(Seal);
        if (MaxPieces < 0 || MaxPieces > 4096 || MaxCohorts < 0 || MaxCohorts > 4096 ||
            MaxLoopChecks < 0 || MaxLoopChecks > 524288 || MaxReceipts < 0 || MaxReceipts > 524288 ||
            MaxRejections < 0 || MaxRejections > 524288 || MaxDependencyTriangles < 0 || MaxDependencyTriangles > 1048576 ||
            MaxPinnedVertices < 0 || MaxPinnedVertices > 1048576 || MaxDependencyTiles < 0 || MaxDependencyTiles > 1048576 ||
            MaxCandidateTrianglesPerLoop < 0 || MaxCandidateTrianglesPerLoop > 32768 ||
            MaxCandidateCellVisits < 0 || MaxTerrainVertexSamples < 0 || MaxCandidateTriangleTests < 0 || MaxSegmentTriangleAttempts < 0 || MaxExactArithmeticOperations < 0 ||
            MaxEstimatedResidentBytes < 0 || MaxEstimatedScratchBytes < 0 ||
            Planner.MaxEstimatedOwnedBytes < 0 || Planner.MaxEstimatedScratchBytes < 0 ||
            Support.MaxEstimatedResidentBytes < 0 || Support.MaxEstimatedScratchBytes < 0 ||
            Seal.MaxEstimatedScratchBytes < 0 || Seal.MaxEstimatedResultBytes < 0 ||
            Seal.MaxTerrainTriangles < 0 || Seal.MaxTerrainTriangles > FormationTerrainSeal.MaximumTerrainTriangles)
            throw new FormationValidationException("Invalid explicit formation field limits.");
        // Count both collection growth/copies and retained receipt/cohort storage. Catalog geometry is shared by
        // the support index and included in that index's supplied resident ceiling. No cumulative-GC bound is claimed.
        long resident, scratch;
        try
        {
            resident = checked(16384L + 1024L * MaxPieces + 8192L * MaxReceipts + 1024L * MaxCohorts +
                512L * MaxRejections + 256L * MaxDependencyTriangles + 256L * MaxPinnedVertices +
                128L * MaxDependencyTiles + Support.MaxEstimatedResidentBytes);
            scratch = checked(65536L + Planner.MaxEstimatedOwnedBytes + Planner.MaxEstimatedScratchBytes +
                Support.MaxEstimatedScratchBytes + Seal.MaxEstimatedScratchBytes + Seal.MaxEstimatedResultBytes +
                8192L * MaxCandidateTrianglesPerLoop + 1024L * FormationTerrainSeal.MaximumLoopVertices);
        }
        catch (OverflowException)
        { throw new FormationCapacityException("Formation field aggregate byte reservation is not representable."); }
        if (resident > MaxEstimatedResidentBytes || scratch > MaxEstimatedScratchBytes)
            throw new FormationCapacityException("Formation field aggregate reservation exceeds its resident or scratch allowance.");
        return (resident, scratch);
    }
}

/// <summary>AcceptedPieces/Cohorts are final; work and storage counters include discarded provisional receipts.</summary>
public readonly record struct FormationFieldDiagnostics(int ProposedPieces, int AcceptedPieces, int LoopChecks,
    int Receipts, int Cohorts, int RejectedPieces, long CandidateCellVisits, long TerrainVertexSamples, long CandidateTriangleTests,
    long SegmentTriangleAttempts, long ExactArithmeticOperations, int DependencyTriangles, int PinnedVertices,
    int DependencyTiles, long ReservedResidentBytes, long ReservedScratchBytes);

public enum FormationFieldRejectionReason : byte { IncompleteSeal, FormationPolicy }
public readonly record struct FormationFieldRejection(FormationPieceId Piece, string LoopKey,
    FormationFieldRejectionReason Reason, int FailedSegment);

public sealed record FormationFieldQueryLimits
{
    public required int MaxVisitedNodes { get; init; }
    public required int MaxPieces { get; init; }
    public required long MaxEstimatedBytes { get; init; }
}

public sealed record FormationFieldCollisionLimits
{
    public required FormationFieldQueryLimits Query { get; init; }
    /// <summary>Includes the supplied base mesh and every complete intersecting physical piece.</summary>
    public required int MaxVertices { get; init; }
    public required int MaxTriangles { get; init; }
    public required long MaxEstimatedOutputBytes { get; init; }
}
