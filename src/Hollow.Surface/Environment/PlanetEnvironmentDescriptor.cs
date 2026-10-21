using System;
using Hollow.Core;
using Hollow.Galaxy.Systems;

namespace Hollow.Surface;

/// <summary>The selected generator, not the body's physical classification.</summary>
public enum EnvironmentSurfaceKind : byte { Walking = 1, StandIn = 2, RealRadius = 3 }

/// <summary>A system/body identity value. The metadata resolver validates it when constructing a descriptor.</summary>
public readonly record struct WorldAddress(ulong SystemAddress, uint BodyPath);

/// <summary>
/// Values copied from a body, without retaining its mutable containing system/children list. PlanetState,
/// Composition and Gases contain only immutable scalar values. Display names do not select generation.
/// </summary>
public sealed record PlanetEnvironmentMetadata(
    BodyKind BodyKind, PlanetType PlanetType, double PhysicalRadius, double Mass,
    double SurfaceGravity, double Temperature, AtmosphereType Atmosphere, double SurfacePressure,
    double RotationPeriod, double AxialTilt, bool TidallyLocked, PlanetState State);

/// <summary>
/// Frozen inputs for one generated environment. Identity hashing never supplies generation seeds.
/// Equality checks the complete canonical representation, not just its digest. No terrain is constructed here.
/// </summary>
public sealed class PlanetEnvironmentDescriptor : IEquatable<PlanetEnvironmentDescriptor>
{
    internal PlanetEnvironmentDescriptor(ulong universeSeed, WorldAddress address, EnvironmentSurfaceKind kind,
        double radius, PlanetEnvironmentMetadata metadata, WalkingParameters? walkingParameters,
        ulong surfaceSeed, ulong terrainSeed, Vector3D? landing, bool diagnostic, bool legacyCompatible,
        FormationWorldSelection? formations = null)
    {
        UniverseSeed = universeSeed;
        Address = address;
        Kind = kind;
        Radius = radius;
        Metadata = metadata;
        WalkingParameters = walkingParameters;
        SurfaceSeed = surfaceSeed;
        TerrainSeed = terrainSeed;
        Landing = landing;
        IsDiagnostic = diagnostic;
        Formations = formations;
        IsLegacyMultiplayerCompatible = legacyCompatible && formations == null;
        TerrainRevision = walkingParameters?.TerrainRevision ?? (kind == EnvironmentSurfaceKind.RealRadius ? Galaxy.GeneratorVersion.Surface : 0);
        // Existing save compatibility combines physical and population changes. This does not create a new seed domain.
        PopulationRevision = TerrainRevision;
        ProfileId = formations?.ProfileId ?? (kind == EnvironmentSurfaceKind.Walking
            ? (address.SystemAddress == StartingWorld.System.Value && address.BodyPath == StartingWorld.BodyPath
                ? "legacy-walking-earth" : "legacy-walking-default")
            : kind == EnvironmentSurfaceKind.StandIn ? "legacy-stand-in" : "legacy-real-radius");
        RequiredPhysicalContentFingerprint = formations?.Identity.PhysicalContentDigest ?? (kind == EnvironmentSurfaceKind.Walking
            ? EnvironmentCanonical.RequiredPhysicalFingerprint : "no-authored-formations-v1");
        // This is an explicit limitation of the existing wire contract, not a verified concealment-asset digest.
        GameplayVisibilitySignature = "legacy-shared-build-unnegotiated-v1";
        QualificationIdentity = formations?.Identity.QualificationDigest ?? EnvironmentCanonical.LegacyQualificationIdentity;
        byte[] canonical = EnvironmentCanonical.Descriptor(this);
        if (canonical.Length > MaximumCanonicalBytes)
            throw new FormationCapacityException("The environment descriptor exceeds its canonical byte ceiling.");
        CanonicalIdentity = Convert.ToBase64String(canonical);
        Identity = EnvironmentCanonical.Digest(canonical);
        // Save compatibility uses physical bytes. Resident resources also include the complete qualified LOD input,
        // so two visual/qualification variants cannot return a lease holding the first variant's catalog.
        CacheIdentity = formations == null ? CanonicalIdentity
            : CanonicalIdentity + "|" + formations.Identity.QualificationCanonical;
        EstimatedSelectionBytes = formations == null ? 0 : checked(formations.EstimatedResidentBytes +
            4096L + 2L * CanonicalIdentity.Length + 2L * CacheIdentity.Length);
    }

    public const int DescriptorSchema = 1;
    public const int MaximumCanonicalBytes = 8 * 1024 * 1024;
    public int CanonicalSchema => Formations == null ? DescriptorSchema : 2;
    public int SeedSchema => 1;
    public ulong UniverseSeed { get; }
    public WorldAddress Address { get; }
    public EnvironmentSurfaceKind Kind { get; }
    public double Radius { get; }
    public PlanetEnvironmentMetadata Metadata { get; }
    public WalkingParameters? WalkingParameters { get; }
    public FormationWorldSelection? Formations { get; }
    public string ProfileId { get; }
    public int LayoutVariant => WalkingParameters?.LayoutVariant ?? 0;
    public int TerrainRevision { get; }
    public int PopulationRevision { get; }
    /// <summary>Legacy zone/gameplay seed; walking terrain derives its seed from this address-derived value.</summary>
    public ulong SurfaceSeed { get; }
    /// <summary>Constructor seed, before WalkingWorld applies its explicit layout variant.</summary>
    public ulong TerrainSeed { get; }
    /// <summary>Stand-in island input only. Walking landing is computed from the leased world.</summary>
    public Vector3D? Landing { get; }
    public bool IsDiagnostic { get; }
    /// <summary>
    /// True only for local inputs reconstructible by the current legacy wire/shared build. This is not negotiated
    /// peer compatibility; consumers must retain the protocol/save fence and reject false for multiplayer.
    /// </summary>
    public bool IsLegacyMultiplayerCompatible { get; }
    public string RequiredPhysicalContentFingerprint { get; }
    public string GameplayVisibilitySignature { get; }
    /// <summary>Independent admission evidence, never a procedural seed or a physical save-compatibility input.</summary>
    public string QualificationIdentity { get; }
    public string Identity { get; }
    public string CanonicalIdentity { get; }
    internal string CacheIdentity { get; }
    internal long EstimatedSelectionBytes { get; }

    public bool Equals(PlanetEnvironmentDescriptor? other) =>
        other != null && StringComparer.Ordinal.Equals(CacheIdentity, other.CacheIdentity);
    public override bool Equals(object? obj) => obj is PlanetEnvironmentDescriptor descriptor && Equals(descriptor);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(CacheIdentity);
    public override string ToString() => $"{Kind} {Address.SystemAddress:X16}/{Address.BodyPath:X8} {Identity}";
}
