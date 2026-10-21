using System;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Finalized radial grading only. Implementations must be immutable and deterministic for the build's lifetime.
/// This deliberately exposes no cover, formation, support, habitat, population or collision query.
/// </summary>
public interface IGradedTerrain
{
    bool IsFinalized { get; }
    double Radius { get; }
    double HeightAt(Vector3D unitDirection);
    /// <summary>Radial height; negative infinity means no water, matching ISurface.</summary>
    double WaterLevelAt(Vector3D unitDirection);
    GradedTerrainFields RawFieldsAt(Vector3D unitDirection);
    ReadOnlySpan<FormationProtectedSite> ProtectedSites { get; }
}

public readonly record struct GradedTerrainFields(string ProfileKey, double TemperatureC, double Moisture, double ForestPotential);

/// <summary>Immutable conservative site exclusion, copied before survey. Intersection never grants support.</summary>
public sealed class FormationProtectedSite
{
    public FormationProtectedSite(string key, FormationBounds bounds)
        : this(key, new FormationBoxExclusion(bounds)) { }
    public FormationProtectedSite(string key, FormationBounds meshBounds, Vector3D footprintAxis, double unitChordRadius)
        : this(key, new FormationUnionExclusion(new FormationBoxExclusion(meshBounds),
            new FormationRadialExclusion(footprintAxis, unitChordRadius))) { }
    private FormationProtectedSite(string key, IFormationSiteExclusion volume)
    { Key = FormationInput.Key(key, nameof(key)); _volume = volume; }
    private readonly IFormationSiteExclusion _volume;
    public string Key { get; }
    public bool Intersects(FormationBounds bounds) => _volume.Intersects(bounds);
    /// <summary>Protects every radial height within a chord-distance footprint on a unit sphere.</summary>
    public static FormationProtectedSite RadialFootprint(string key, Vector3D axis, double unitChordRadius) =>
        new(key, new FormationRadialExclusion(axis, unitChordRadius));
}

/// <summary>Explicit experimental tuning for one selected recipe/profile. There are no production tuning defaults.</summary>
public sealed class FormationPlannerSettings
{
    public FormationPlannerSettings(string recipeKey, int recipeRevision, string terrainProfileKey,
        double gradientProbeMetres, double minimumRiseRun, double crossSectionStepMetres,
        int crossSectionSteps, int coverageAlong, int coverageDown, double coverageReachMetres,
        Vector3D geometryUp, string anchorContactRole, int surveyLevel = 6, FormationSurveyRegion? region = null)
    {
        RecipeKey = FormationInput.Key(recipeKey, nameof(recipeKey));
        TerrainProfileKey = FormationInput.Key(terrainProfileKey, nameof(terrainProfileKey));
        AnchorContactRole = FormationInput.Key(anchorContactRole, nameof(anchorContactRole));
        if (recipeRevision < 0 || surveyLevel < 0 || surveyLevel > 6 ||
            !Positive(gradientProbeMetres) || !Positive(minimumRiseRun) || !Positive(crossSectionStepMetres) ||
            !Positive(coverageReachMetres) || crossSectionSteps < 1 || crossSectionSteps > 12 ||
            coverageAlong < 1 || coverageDown < 1 || (long)coverageAlong * coverageDown > 48)
            throw new FormationValidationException("Invalid explicit planner survey, cross-section or coverage settings.");
        FormationInput.Unit(geometryUp, nameof(geometryUp));
        if (region != null && region.GridLevel != surveyLevel + 1)
            throw new FormationValidationException("Region cells must use the planner's actual survey grid level.");
        RecipeRevision = recipeRevision; SurveyLevel = surveyLevel; GradientProbeMetres = gradientProbeMetres;
        MinimumRiseRun = minimumRiseRun; CrossSectionStepMetres = crossSectionStepMetres;
        CrossSectionSteps = crossSectionSteps; CoverageAlong = coverageAlong; CoverageDown = coverageDown;
        CoverageReachMetres = coverageReachMetres; GeometryUp = geometryUp; Region = region;
    }
    private static bool Positive(double x) => double.IsFinite(x) && x > 0 && double.IsFinite(x * x);
    public string RecipeKey { get; }
    public int RecipeRevision { get; }
    public string TerrainProfileKey { get; }
    public int SurveyLevel { get; }
    /// <summary>Optional canonical publication core plus complete fitting/conflict-evidence halo. Null surveys the whole planet.</summary>
    public FormationSurveyRegion? Region { get; }
    public double GradientProbeMetres { get; }
    public double MinimumRiseRun { get; }
    public double CrossSectionStepMetres { get; }
    public int CrossSectionSteps { get; }
    public int CoverageAlong { get; }
    public int CoverageDown { get; }
    public double CoverageReachMetres { get; }
    /// <summary>Explicit common authored up convention for this selected recipe; never inferred from asset names.</summary>
    public Vector3D GeometryUp { get; }
    /// <summary>Exactly one required contact per alternative must have this authored role. No key-order inference.</summary>
    public string AnchorContactRole { get; }
}

public sealed record FormationPlannerLimits
{
    public int MaxRecords { get; init; } = 4096;
    public int MaxContourSegments { get; init; } = 8192;
    public int MaxProtectedSites { get; init; } = 1024;
    public int MaxContactAndCoverageSamplesPerRecord { get; init; } = 48;
    public long MaxTerrainSamples { get; init; } = 4000000;
    public long MaxTriangleTests { get; init; } = 4000000;
    public long MaxGeometryVertexVisits { get; init; } = 4000000;
    public long MaxConflictTests { get; init; } = 8000000;
    /// <summary>Every angular-domain or provenance/neighbor-closure cell visit, including repeats and unrestricted provenance gates.</summary>
    public long MaxRegionCellTests { get; init; } = 1000000;
    public long MaxEstimatedOwnedBytes { get; init; } = 16L * 1024 * 1024;
    public long MaxEstimatedScratchBytes { get; init; } = 32L * 1024 * 1024;
}
