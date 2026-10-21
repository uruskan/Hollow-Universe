using System;
using System.Threading;
using Hollow.Galaxy;

namespace Hollow.Surface;

/// <summary>
/// Explicit opt-in construction allowances. There are no production defaults. These immutable operational limits
/// refuse a complete build; they never trim successful output and are excluded from physical generation identity.
/// Estimates cover retained objects plus simultaneous build scratch, not cumulative allocation, native memory or RSS.
/// </summary>
public sealed record SelectedWalkingWorldLimits
{
    public const long MaximumGradingResidentBytes = 512L * 1024 * 1024;
    public const long MaximumGradingScratchBytes = 64L * 1024 * 1024;
    public required FormationFieldLimits Field { get; init; }
    public required TerrainPinSetLimits Pins { get; init; }
    /// <summary>Null keeps physical collision inactive. Operational allowances do not change physical identity.</summary>
    public SelectedGroundCollisionLimits? Collision { get; init; }
    public required int MaxProtectedSites { get; init; }
    public required long MaxEstimatedGradingResidentBytes { get; init; }
    public required long MaxEstimatedGradingScratchBytes { get; init; }

    internal SelectedWalkingWorldPlan Prepare(PlanetEnvironmentDescriptor descriptor, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellation.ThrowIfCancellationRequested();
        FormationWorldSelection selection = descriptor.Formations ??
            throw new FormationValidationException("Selected construction requires an explicit formation selection.");
        if (descriptor.Kind != EnvironmentSurfaceKind.Walking || descriptor.WalkingParameters == null ||
            descriptor.Radius > GameScale.MaximumPlanetRadius || selection.TerrainContract != WalkingGradedTerrain.Contract)
            throw new FormationValidationException("Selected construction requires the supported bounded walking grading contract.");
        FormationFieldContract contract = selection.FieldContract ??
            throw new FormationValidationException("Selected construction requires an explicit sealed field contract.");
        if (selection.Settings.Region != null)
            throw new FormationValidationException("Selected walking construction requires a whole-world proposal field.");
        var lattice = new TerrainLattice(descriptor.Radius, contract.FinestTileLevel);
        contract.Validate(lattice);
        string identity = WalkingGradedTerrain.InputIdentity(descriptor, selection.Settings.TerrainProfileKey);
        if (identity != contract.TerrainIdentity)
            throw new FormationValidationException("The selected terrain identity does not match its canonical graded generator inputs.");
        // Authenticate required physical alternatives before any world or user-supplied estimator is invoked.
        FormationRecipe recipe = selection.Catalog.RequireRecipe(selection.Settings.RecipeKey, selection.Settings.RecipeRevision);
        foreach (FormationRecipeSlot slot in recipe.Slots)
        foreach (FormationGeometryChoice choice in slot.Alternatives)
        {
            cancellation.ThrowIfCancellationRequested();
            if (selection.Catalog.RequireQualified(choice.Key, choice.Revision).ContactLoops.Length == 0)
                throw new FormationValidationException("Every selected physical alternative requires qualified closed contact loops.");
        }
        ArgumentNullException.ThrowIfNull(Field); ArgumentNullException.ThrowIfNull(Pins);
        if (MaxProtectedSites < 0 || MaxProtectedSites > 1024 ||
            MaxEstimatedGradingResidentBytes < 0 || MaxEstimatedGradingResidentBytes > MaximumGradingResidentBytes ||
            MaxEstimatedGradingScratchBytes < 0 || MaxEstimatedGradingScratchBytes > MaximumGradingScratchBytes)
            throw new FormationValidationException("Invalid explicit selected-world grading allowances.");
        (long fieldResident, long fieldScratch) = Field.Reserve();
        (long pinResident, long pinScratch) = TerrainPinSet.Reserve(Pins);
        Collision?.Reserve();
        if (Collision != null) GroundCollisionLayout.Validate(lattice, Collision);
        // Collision queries execute later under their caller's per-job reservation. Construction retains only
        // the immutable site/bounds array; 256 bytes/site covers the 120-byte placement and 48-byte AABB plus slack.
        long collisionSnapshot = Collision == null ? 0 : 8192L + 256L * MaxProtectedSites;
        WorldMemoryEstimate grading = EstimateGrading(descriptor);
        if (grading.EstimatedResidentBytes > MaxEstimatedGradingResidentBytes || grading.ReservedBuildBytes > MaxEstimatedGradingScratchBytes)
            throw new FormationCapacityException("Eager monument/site/index reservation exceeds its explicit allowance.");
        WorldMemoryEstimate additional;
        try
        {
            additional = new WorldMemoryEstimate(
                checked(4096 + grading.EstimatedResidentBytes + fieldResident + pinResident + collisionSnapshot),
                checked(4096 + grading.ReservedBuildBytes + fieldScratch + pinScratch + collisionSnapshot));
        }
        catch (OverflowException) { throw new FormationCapacityException("Selected-world aggregate reservation is not representable."); }
        cancellation.ThrowIfCancellationRequested();
        return new SelectedWalkingWorldPlan(descriptor, this, lattice, identity, additional);
    }

    private WorldMemoryEstimate EstimateGrading(PlanetEnvironmentDescriptor descriptor)
    {
        // One candidate site per grid cell; Build may hold a growing list and final array before the protected-site
        // limit is checked. Index is forced only after that check, so at most P sites can enter its dictionaries.
        int level = TileKey.LevelFor(descriptor.Radius, Math.Max(MonumentPlacements.MinimumSpacing,
            descriptor.WalkingParameters!.MonumentSpacing));
        long candidates = TileKey.CountAt(level), sites = Math.Min(candidates, MaxProtectedSites);
        long allIndexTiles = TileKey.CountAt(level + 2), tilesPerSite = 0;
        double step = TileKey.EdgeLength(level + 2, descriptor.Radius) / 3;
        foreach (MonumentDefinition definition in MonumentDefinition.All)
        {
            // This mirrors Around's finite sample grid, capped by the number of distinct cells on the sphere.
            // Very small diagnostic radii need no enormous integer conversion just to reserve that finite set.
            double n = Math.Max(1, Math.Ceiling((definition.Radius + MonumentPlacements.IndexMargin) / step));
            double side = 2 * n + 1;
            long count = !double.IsFinite(side) || side >= allIndexTiles ? allIndexTiles :
                Math.Min(allIndexTiles, checked(1 + (long)side * (long)side));
            tilesPerSite = Math.Max(tilesPerSite, count);
        }
        // 512*N covers simultaneous site-list growth/final arrays. Index lists contain full placement structs,
        // not references: 1024*P*K covers their minimum four-slot allocation, 2x growth, dictionary entries and
        // resize workspace. Scratch
        // also covers the one growing Around list, protected-site objects/keys and sorted planner snapshot.
        return new WorldMemoryEstimate(checked(32768 + 512 * candidates + 1024 * sites * tilesPerSite),
            checked(32768 + 512 * candidates + 64 * tilesPerSite + 2048 * sites));
    }
}

internal sealed record SelectedWalkingWorldPlan(PlanetEnvironmentDescriptor Descriptor, SelectedWalkingWorldLimits Limits,
    TerrainLattice Lattice, string TerrainIdentity, WorldMemoryEstimate AdditionalEstimate);
