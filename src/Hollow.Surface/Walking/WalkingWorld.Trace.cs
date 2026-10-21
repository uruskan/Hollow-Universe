using System;
using System.Threading;

namespace Hollow.Surface;

public sealed partial class WalkingWorld : ISelectedGroundTraceSurface
{
    /// <summary>Requires the same admitted monument snapshot as selected collision. Legacy worlds remain unsupported.</summary>
    public bool HasSelectedGroundTrace => _selected?.ConstructionLimits.Collision != null;

    public SelectedGroundTraceResult TraceGround(SelectedGroundTraceRequest request, SelectedGroundTraceLimits limits,
        CancellationToken cancellation = default, SelectedGroundTraceWorkspace? workspace = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _ = request.Ray; _ = limits.Reserve();
        if (_selected is not { ConstructionLimits.Collision: not null } selected)
            return new(SelectedGroundTraceStatus.Unsupported, null, _selected?.Field.GenerationDigest,
                _selected?.Field.Contract.TerrainIdentity, default);
        return SelectedGroundTrace.Trace(this, selected.Field, selected.CollisionMonuments, request, limits, cancellation, workspace);
    }
}
