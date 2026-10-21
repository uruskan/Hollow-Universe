using System;
using System.Threading;

namespace Hollow.Surface;

public sealed partial class WalkingWorld : IPlannedGroundCollision
{
    public bool HasPlannedGroundCollision => _selected?.ConstructionLimits.Collision != null;
    public GroundCollisionPreparationBounds CollisionPreparationBounds => GroundCollisionPreparationBounds.From(
        _selected?.ConstructionLimits.Collision ?? throw new NotSupportedException("Selected collision requires explicit per-operation allowances."));

    private TriangleMesh SelectedCollision(in ZoneFrame frame, TileKey tile, double margin)
        => BuildGroundCollision(PrepareGroundCollision(frame, tile, margin));

    /// <summary>Exact metadata/topology admission before sampling or native work. No production consumer is enabled by this API.</summary>
    public SelectedGroundCollisionPlan PrepareGroundCollision(in ZoneFrame frame, TileKey tile, double margin,
        CancellationToken cancellation = default)
    {
        SelectedBundle selected = _selected ?? throw new InvalidOperationException("A selected collision bundle is required.");
        SelectedGroundCollisionLimits limits = selected.ConstructionLimits.Collision ??
            throw new NotSupportedException("Selected collision requires explicit per-operation allowances.");
        return SelectedGroundCollision.Prepare(selected.CollisionIdentity, selected.Lattice, frame, tile, margin,
            selected.Field, selected.CollisionMonuments, limits, cancellation);
    }

    public TriangleMesh BuildGroundCollision(SelectedGroundCollisionPlan plan, CancellationToken cancellation = default)
    {
        SelectedBundle selected = _selected ?? throw new InvalidOperationException("A selected collision bundle is required.");
        return SelectedGroundCollision.Build(this, selected.CollisionIdentity, plan, cancellation);
    }
}
