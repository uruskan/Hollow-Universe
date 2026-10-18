using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Canonical owner/section identity within one explicitly owned world. No acceptance-order index.</summary>
public readonly record struct FormationId(ulong Owner, uint Section) : IComparable<FormationId>
{
    public int CompareTo(FormationId other) { int c = Owner.CompareTo(other.Owner); return c != 0 ? c : Section.CompareTo(other.Section); }
}

public readonly record struct FormationPieceId(FormationId Formation, int Slot) : IComparable<FormationPieceId>
{
    public int CompareTo(FormationPieceId other) { int c = Formation.CompareTo(other.Formation); return c != 0 ? c : Slot.CompareTo(other.Slot); }
}

public readonly record struct PhysicalPrimitiveId(FormationPieceId Piece, string GeometryKey, int GeometryRevision, int Triangle) : IComparable<PhysicalPrimitiveId>
{
    public int CompareTo(PhysicalPrimitiveId other)
    {
        int c = Piece.CompareTo(other.Piece);
        if (c == 0) c = StringComparer.Ordinal.Compare(GeometryKey, other.GeometryKey);
        if (c == 0) c = GeometryRevision.CompareTo(other.GeometryRevision);
        return c != 0 ? c : Triangle.CompareTo(other.Triangle);
    }
}

/// <summary>Finalized physical piece. Geometry is shared immutable data; transformed vertices are not cached here.</summary>
public sealed class FormationPiece
{
    public FormationPiece(FormationPieceId id, string role, FormationGeometry geometry, FormationTransform transform)
    {
        if (id.Slot < 0) throw new FormationValidationException("A piece slot must be nonnegative.", nameof(id));
        ArgumentNullException.ThrowIfNull(geometry);
        transform.Validate();
        Id = id; Role = FormationInput.Key(role, nameof(role)); Geometry = geometry; Transform = transform;
        Vector3D low = transform.Transform(geometry.Vertices[0]), high = low;
        foreach (Vector3D vertex in geometry.Vertices)
        {
            Vector3D point = transform.Transform(vertex);
            FormationInput.Finite(point, nameof(transform));
            low = FormationBounds.Lower(low, point); high = FormationBounds.Upper(high, point);
        }
        foreach (GeometryTriangle triangle in geometry.Triangles)
        {
            Vector3D a = transform.Transform(geometry.Vertices[triangle.A]);
            Vector3D b = transform.Transform(geometry.Vertices[triangle.B]);
            Vector3D c = transform.Transform(geometry.Vertices[triangle.C]);
            double area = Vector3D.Cross(b - a, c - a).LengthSquared;
            if (!double.IsFinite(area) || area <= 0)
                throw new FormationValidationException("Transform collapses or overflows a physical triangle.", nameof(transform));
        }
        Bounds = new FormationBounds(low, high);
    }
    public FormationPieceId Id { get; }
    public string Role { get; }
    public FormationGeometry Geometry { get; }
    public FormationTransform Transform { get; }
    public FormationBounds Bounds { get; }
    public PhysicalPrimitiveId PrimitiveId(int triangle)
    {
        if ((uint)triangle >= (uint)Geometry.TriangleCount) throw new ArgumentOutOfRangeException(nameof(triangle));
        return new PhysicalPrimitiveId(Id, Geometry.Key, Geometry.Revision, triangle);
    }
}

/// <summary>Geometry-only support. Substrate evaluation consumes this value and cannot call back into support.</summary>
public readonly record struct SupportHit
{
    public SupportHit(PhysicalPrimitiveId Primitive, Vector3D Point, Vector3D Normal, Vector3D Barycentric,
        double Distance, string PatchKey, string FamilyKey, string SubstrateProfileKey, SupportCapabilities Capabilities)
    {
        this.Primitive = Primitive; this.Point = Point; this.Normal = Normal; this.Barycentric = Barycentric;
        this.Distance = Distance; this.PatchKey = PatchKey; this.FamilyKey = FamilyKey;
        this.SubstrateProfileKey = SubstrateProfileKey; this.Capabilities = Capabilities;
        Validate();
    }
    public PhysicalPrimitiveId Primitive { get; }
    public Vector3D Point { get; }
    public Vector3D Normal { get; }
    public Vector3D Barycentric { get; }
    public double Distance { get; }
    public string PatchKey { get; }
    public string FamilyKey { get; }
    public string SubstrateProfileKey { get; }
    public SupportCapabilities Capabilities { get; }
    internal void Validate()
    {
        if (Primitive.Piece.Slot < 0 || Primitive.GeometryRevision < 0 || Primitive.Triangle < 0 ||
            !double.IsFinite(Distance) || Distance < 0)
            throw new FormationValidationException("Invalid physical support identity or distance.");
        FormationInput.Key(Primitive.GeometryKey, nameof(Primitive));
        FormationInput.Key(PatchKey, nameof(PatchKey)); FormationInput.Key(FamilyKey, nameof(FamilyKey));
        FormationInput.Key(SubstrateProfileKey, nameof(SubstrateProfileKey));
        FormationInput.Finite(Point, nameof(Point)); FormationInput.Unit(Normal, nameof(Normal));
        FormationInput.Finite(Barycentric, nameof(Barycentric)); FormationInput.Capabilities(Capabilities, nameof(Capabilities));
        if (Barycentric.X < -1e-10 || Barycentric.Y < -1e-10 || Barycentric.Z < -1e-10 ||
            Math.Abs(Barycentric.X + Barycentric.Y + Barycentric.Z - 1) > 1e-10)
            throw new FormationValidationException("Support barycentrics must describe a triangle point.", nameof(Barycentric));
    }
}

public enum FormationRejectionReason : byte { MissingGeometry, Contact, Clearance, ProtectedSite, Coverage, Extent }

/// <summary>A rejected intended slot stays identified without renumbering accepted pieces.</summary>
public sealed class FormationRejection
{
    public FormationRejection(int slot, string role, FormationRejectionReason reason, FormationBounds intendedRegion)
    {
        if (slot < 0 || !Enum.IsDefined(reason)) throw new FormationValidationException("Invalid rejected formation slot.");
        Slot = slot; Role = FormationInput.Key(role, nameof(role)); Reason = reason; IntendedRegion = intendedRegion;
    }
    public int Slot { get; }
    public string Role { get; }
    public FormationRejectionReason Reason { get; }
    public FormationBounds IntendedRegion { get; }
}

public readonly record struct FormationCoverage
{
    public FormationCoverage(double intendedArea, double coveredArea, double allowedSoilArea)
    {
        if (!double.IsFinite(intendedArea) || !double.IsFinite(coveredArea) || !double.IsFinite(allowedSoilArea) ||
            intendedArea < 0 || coveredArea < 0 || allowedSoilArea < 0 || coveredArea + allowedSoilArea > intendedArea)
            throw new FormationValidationException("Coverage areas in square metres must form a finite nonnegative partition.");
        IntendedArea = intendedArea; CoveredArea = coveredArea; AllowedSoilArea = allowedSoilArea;
    }
    public double IntendedArea { get; }
    public double CoveredArea { get; }
    public double AllowedSoilArea { get; }
    public double UncoveredArea => Math.Max(0, IntendedArea - CoveredArea - AllowedSoilArea);
}

/// <summary>
/// A finalized synthetic formation, not a planner. Copies bounded guides and slot lists, verifies identity/bounds,
/// and publishes no callbacks. A future producer must resolve all conflicts and physical contacts before making it.
/// </summary>
public sealed class FormationRecord
{
    private readonly Vector3D[] _crest, _face, _toe;
    private readonly FormationPiece[] _pieces;
    private readonly FormationRejection[] _rejections;
    public const int MaximumGuidePoints = 64, MaximumSlots = 64;

    public FormationRecord(FormationId id, string recipeKey, int recipeRevision, string familyKey,
        IEnumerable<Vector3D> crest, IEnumerable<Vector3D> face, IEnumerable<Vector3D> toe,
        IEnumerable<FormationPiece> pieces, IEnumerable<FormationRejection> rejections,
        FormationBounds influenceBounds, FormationCoverage coverage)
    {
        if (recipeRevision < 0) throw new FormationValidationException("Recipe revision must be nonnegative.", nameof(recipeRevision));
        Id = id; RecipeKey = FormationInput.Key(recipeKey, nameof(recipeKey)); RecipeRevision = recipeRevision;
        FamilyKey = FormationInput.Key(familyKey, nameof(familyKey)); InfluenceBounds = influenceBounds; Coverage = coverage;
        _crest = FormationInput.Copy(crest, MaximumGuidePoints, nameof(crest));
        _face = FormationInput.Copy(face, MaximumGuidePoints, nameof(face));
        _toe = FormationInput.Copy(toe, MaximumGuidePoints, nameof(toe));
        foreach (Vector3D[] guide in new[] { _crest, _face, _toe })
        {
            if (guide.Length < 2) throw new FormationValidationException("Each formation guide needs at least two points.");
            foreach (Vector3D point in guide)
            {
                FormationInput.Finite(point, nameof(crest));
                if (!influenceBounds.Contains(point)) throw new FormationValidationException("Influence bounds must include formation guides.", nameof(influenceBounds));
            }
        }
        _pieces = FormationInput.Copy(pieces, MaximumSlots, nameof(pieces));
        _rejections = FormationInput.Copy(rejections, MaximumSlots, nameof(rejections));
        if (_pieces.Length + _rejections.Length > MaximumSlots) throw new FormationCapacityException("Formation slot limit exceeded.");
        var slots = new HashSet<int>();
        foreach (FormationPiece piece in _pieces)
        {
            if (piece == null || piece.Id.Formation != id || !slots.Add(piece.Id.Slot))
                throw new FormationValidationException("Formation piece owners and slots must be valid and unique.", nameof(pieces));
            if (!influenceBounds.Contains(piece.Bounds.Min) || !influenceBounds.Contains(piece.Bounds.Max))
                throw new FormationValidationException("Influence bounds must include every physical piece.", nameof(influenceBounds));
        }
        foreach (FormationRejection rejection in _rejections)
        {
            if (rejection == null || !slots.Add(rejection.Slot)) throw new FormationValidationException("Rejected and accepted slots must be distinct.", nameof(rejections));
            if (!influenceBounds.Contains(rejection.IntendedRegion.Min) || !influenceBounds.Contains(rejection.IntendedRegion.Max))
                throw new FormationValidationException("Influence bounds must include intended rejected regions.", nameof(influenceBounds));
        }
        Array.Sort(_pieces, static (a, b) => a.Id.CompareTo(b.Id));
        Array.Sort(_rejections, static (a, b) => a.Slot.CompareTo(b.Slot));
    }
    public FormationId Id { get; }
    public string RecipeKey { get; }
    public int RecipeRevision { get; }
    public string FamilyKey { get; }
    public FormationBounds InfluenceBounds { get; }
    public FormationCoverage Coverage { get; }
    public ReadOnlySpan<Vector3D> Crest => _crest;
    public ReadOnlySpan<Vector3D> Face => _face;
    public ReadOnlySpan<Vector3D> Toe => _toe;
    public ReadOnlySpan<FormationPiece> Pieces => _pieces;
    public ReadOnlySpan<FormationRejection> Rejections => _rejections;
}
