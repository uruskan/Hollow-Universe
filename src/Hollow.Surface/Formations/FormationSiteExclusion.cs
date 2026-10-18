using System;
using Hollow.Core;

namespace Hollow.Surface;

internal interface IFormationSiteExclusion
{
    bool Intersects(FormationBounds bounds);
}

internal sealed class FormationBoxExclusion(FormationBounds bounds) : IFormationSiteExclusion
{
    public bool Intersects(FormationBounds other) => bounds.Intersects(other);
}

internal sealed class FormationUnionExclusion(IFormationSiteExclusion first, IFormationSiteExclusion second) : IFormationSiteExclusion
{
    public bool Intersects(FormationBounds bounds) => first.Intersects(bounds) || second.Intersects(bounds);
}

/// <summary>
/// Semi-infinite circular cone matching a radial chord footprint. A bounding sphere may conservatively reject
/// more than the original AABB. The fixed arithmetic guard covers floating-point boundary uncertainty;
/// overflow/indeterminate intermediates also reject the candidate. There is no terrain sampling or allocation.
/// </summary>
internal sealed class FormationRadialExclusion : IFormationSiteExclusion
{
    private readonly Vector3D _axis;
    private readonly double _sin, _cos;
    private readonly bool _allDirections;
    internal FormationRadialExclusion(Vector3D axis, double unitChordRadius)
    {
        FormationInput.Unit(axis, nameof(axis));
        if (!double.IsFinite(unitChordRadius) || unitChordRadius < 0)
            throw new FormationValidationException("Protected radial footprints require a finite nonnegative unit chord radius.");
        double axisLength = Math.Sqrt(axis.LengthSquared);
        _axis = axis / axisLength;
        // Original grading compares against the represented axis, not an ideal unit direction.
        // Triangle inequality expands the normalized cone to contain that complete original footprint.
        unitChordRadius += Math.Abs(axisLength - 1);
        if (unitChordRadius >= 2) { _allDirections = true; return; }
        _cos = 1 - unitChordRadius * unitChordRadius * 0.5;
        _sin = unitChordRadius * Math.Sqrt(1 - unitChordRadius * unitChordRadius * 0.25);
    }

    public bool Intersects(FormationBounds bounds)
    {
        if (_allDirections) return true;
        Vector3D centre = bounds.Centre;
        double length = centre.Length, radius = bounds.Extent.Length * 0.5;
        if (!double.IsFinite(length) || !double.IsFinite(radius)) return true;
        double guard = 1e-12 * Math.Max(1, length + radius);
        if (length <= radius + guard) return true;
        double axial = Vector3D.Dot(centre, _axis);
        // Cross product avoids catastrophic subtraction of nearly equal squared lengths near the axis.
        double transverse = Vector3D.Cross(centre, _axis).Length;
        double side = transverse * _cos - axial * _sin;
        double along = axial * _cos + transverse * _sin;
        if (!double.IsFinite(side) || !double.IsFinite(along)) return true;
        if (axial >= length * _cos - guard) return true;
        double distance = along <= 0 ? length : Math.Max(0, side);
        return distance <= radius + guard;
    }
}
