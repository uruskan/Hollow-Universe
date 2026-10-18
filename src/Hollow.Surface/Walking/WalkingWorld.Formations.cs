using System;
using System.Threading;
using Hollow.Galaxy.Systems;

namespace Hollow.Surface;

public sealed partial class WalkingWorld : ITerrainLatticeSurface, IFormationSurface, ITerrainPinnedSurface
{
    private readonly SelectedBundle? _selected;

    /// <summary>All three capabilities appear together only after finalized grading, sealing and pins succeed.</summary>
    public TerrainLattice? TerrainLattice => _selected?.Lattice;
    public FormationField? Formations => _selected?.Field;
    public TerrainPinSet? TerrainPins => _selected?.Pins;

    private sealed record SelectedBundle(string DescriptorIdentity, SelectedWalkingWorldLimits ConstructionLimits,
        TerrainLattice Lattice, FormationField Field, TerrainPinSet Pins, SelectedGroundCollision.Monument[] CollisionMonuments)
    {
        // No back-reference to the bundle/world: a pending plan carries only this opaque authentication token.
        internal object CollisionIdentity { get; } = new();
    }

    internal static WalkingWorld CreateSelected(SelectedWalkingWorldPlan plan, CancellationToken cancellation)
    {
        PlanetEnvironmentDescriptor descriptor = plan.Descriptor;
        double ocean = descriptor.Metadata.State.Liquid != LiquidKind.None && descriptor.Metadata.State.OceanCover > 0.001
            ? descriptor.Metadata.State.OceanCover : 0;
        return new WalkingWorld(descriptor.TerrainSeed, descriptor.Radius, ocean, descriptor.Metadata.State.IceCover,
            descriptor.Metadata.Temperature, descriptor.WalkingParameters, plan, cancellation);
    }

    private SelectedBundle BuildSelected(SelectedWalkingWorldPlan plan, CancellationToken cancellation)
    {
        FormationWorldSelection selection = plan.Descriptor.Formations!;
        var graded = new WalkingGradedTerrain(this, selection.Settings.TerrainProfileKey, plan.Limits.MaxProtectedSites, cancellation);
        cancellation.ThrowIfCancellationRequested();
        Monuments.FinalizeGradingIndex();
        cancellation.ThrowIfCancellationRequested();
        FormationField field = FormationField.Build(graded, plan.Lattice, selection, plan.Limits.Field, cancellation);
        TerrainPinSet pins = TerrainPinSet.Build(graded, plan.Lattice, plan.TerrainIdentity, field, plan.Limits.Pins, cancellation);
        if (!pins.SupportsCanonicalRadialTrace)
            throw new FormationValidationException("Selected world pins do not satisfy the exact canonical radial trace domain.");
        SelectedGroundCollision.Monument[] collisionMonuments = plan.Limits.Collision == null ? Array.Empty<SelectedGroundCollision.Monument>() :
            SelectedGroundCollision.Snapshot(Monuments.All, plan.Limits.Collision.MaxMonuments, cancellation);
        cancellation.ThrowIfCancellationRequested();
        // The adapter/planner/plan own this world during construction only. No callback or partially-built bundle
        // escapes. The private descriptor stamp authenticates the entire output even for an injected store factory.
        return new SelectedBundle(plan.Descriptor.CacheIdentity, plan.Limits, plan.Lattice, field, pins, collisionMonuments);
    }

    internal bool MatchesSelected(PlanetEnvironmentDescriptor descriptor) =>
        _selected != null && _selected.DescriptorIdentity == descriptor.CacheIdentity;

    // Admission is independent of physical identity. An injected factory cannot publish a valid descriptor's
    // output built under larger or different allowances than this store actually reserved.
    internal bool MatchesSelected(PlanetEnvironmentDescriptor descriptor, SelectedWalkingWorldLimits constructionLimits) =>
        MatchesSelected(descriptor) && _selected!.ConstructionLimits == constructionLimits;
}
