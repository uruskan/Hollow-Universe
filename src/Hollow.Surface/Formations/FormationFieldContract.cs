using System;

namespace Hollow.Surface;

/// <summary>Explicit post-plan policy. Neither choice refits, promotes alternatives, or changes stable slots.</summary>
public enum FormationFieldAdmissionPolicy : byte { IndependentPieces, WholePlannedFormation }

/// <summary>
/// Immutable successful-output contract. TerrainIdentity identifies finalized radial grading before formations,
/// supplied by the owning factory; it must include terrain seed, parameters and grading/site inputs. It is not
/// derived from this selection (which would be circular), and none of these identities are procedural seeds.
/// Operational capacity limits are deliberately absent: exhaustion aborts the entire construction.
/// </summary>
public sealed class FormationFieldContract
{
    public const string Contract = "sealed-formation-field-v1";
    public const string CandidatePolicy = "segment-shadow-one-cell-guard-outward-radial-cull-v1";
    public const string GuardPolicy = "witness-two-edge-rings-incident-finest-tiles-v1";
    public const double MinimumPlaneBurialMetres = 0.05;
    public const int GuardRings = 2;

    public FormationFieldContract(string terrainIdentity, double radius, int finestTileLevel,
        FormationFieldAdmissionPolicy admissionPolicy)
    {
        TerrainIdentity = CliffSupportValidation.Digest(terrainIdentity);
        _ = new TerrainLattice(radius, finestTileLevel);
        if (radius > FormationTerrainSeal.MaximumCoordinateMetres ||
            admissionPolicy is not (FormationFieldAdmissionPolicy.IndependentPieces or FormationFieldAdmissionPolicy.WholePlannedFormation))
            throw new FormationValidationException("Unsupported field radius or admission policy.");
        Radius = radius; FinestTileLevel = finestTileLevel; AdmissionPolicy = admissionPolicy;
    }

    public string TerrainIdentity { get; }
    public double Radius { get; }
    public int FinestTileLevel { get; }
    public FormationFieldAdmissionPolicy AdmissionPolicy { get; }

    internal void Validate(TerrainLattice lattice)
    {
        if (lattice.Radius != Radius || lattice.FinestTileLevel != FinestTileLevel)
            throw new FormationValidationException("Field contract and canonical terrain lattice do not match.");
    }
}
