using System;
using System.Threading;

namespace Hollow.Surface;

/// <summary>Selected physical collision with immutable preparation bounds and authenticated, single-use plans.</summary>
public interface IPlannedGroundCollision : ISurface
{
    bool HasPlannedGroundCollision { get; }
    GroundCollisionPreparationBounds CollisionPreparationBounds { get; }
    SelectedGroundCollisionPlan PrepareGroundCollision(in ZoneFrame frame, TileKey tile, double margin,
        CancellationToken cancellation = default);
    TriangleMesh BuildGroundCollision(SelectedGroundCollisionPlan plan, CancellationToken cancellation = default);
}

/// <summary>Derived from the selected source's admitted limits; these are allocation estimates, not measurements.</summary>
public sealed class GroundCollisionPreparationBounds
{
    private GroundCollisionPreparationBounds(long scratch, long plan)
    { SourceScratchBytes = scratch; MaximumPlanBytes = plan; }
    public long SourceScratchBytes { get; }
    public long MaximumPlanBytes { get; }
    public long BatchBytes(int maximumTiles)
    {
        if (maximumTiles < 1 || maximumTiles > GroundCollisionLayout.TileIdCount)
            throw new ArgumentOutOfRangeException(nameof(maximumTiles));
        // All retained plans, area storage, transient entry/cost/reference arrays, and one active source operation.
        return checked(SourceScratchBytes + maximumTiles * MaximumPlanBytes + 2048 + 16L * maximumTiles +
            1024L * maximumTiles + 8192);
    }
    internal static GroundCollisionPreparationBounds From(SelectedGroundCollisionLimits limits) =>
        new(limits.Reserve().Scratch, checked(6144L + 16L * limits.Terrain.MaxCells +
            16L * limits.Query.MaxPieces + 4L * limits.MaxMonuments));
}
