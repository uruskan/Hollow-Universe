using System;
using System.Linq;
using System.Reflection;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;

namespace Hollow.Surface;

/// <summary>Metadata-only validated descriptor resolution. Full worlds are acquired separately through an owning store.</summary>
public static class EnvironmentDescriptors
{
    public static PlanetEnvironmentDescriptor? Resolve(ulong systemAddress, uint bodyPath, double radius,
        EnvironmentSurfaceKind kind = EnvironmentSurfaceKind.Walking, ulong? universeSeed = null,
        WalkingParameters? walkingParameters = null, ulong? surfaceSeed = null, Vector3D? landing = null,
        bool diagnostic = false, FormationWorldSelection? formationSelection = null)
    {
        ValidateRadius(radius, kind, diagnostic);
        ulong resolvedUniverseSeed = universeSeed ?? Galaxy.Galaxy.DefaultSeed;
        PlanetBodies.Entry? found = PlanetBodies.Find(systemAddress, bodyPath, resolvedUniverseSeed);
        return found == null ? null : FromBody(resolvedUniverseSeed, found.System, found.Body, radius, kind,
            walkingParameters, surfaceSeed, landing, diagnostic, formationSelection);
    }

    public static PlanetEnvironmentDescriptor FromBody(ulong universeSeed, StarSystem system, Body body, double radius,
        EnvironmentSurfaceKind kind = EnvironmentSurfaceKind.Walking, WalkingParameters? walkingParameters = null,
        ulong? surfaceSeed = null, Vector3D? landing = null, bool diagnostic = false,
        FormationWorldSelection? formationSelection = null)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(body);
        ValidateRadius(radius, kind, diagnostic);
        if (formationSelection != null && kind != EnvironmentSurfaceKind.Walking)
            throw new NotSupportedException("Explicit formations require a walking-world factory.");
        if (!system.Address.IsCatalogue) _ = system.Address.Cell;
        if (BodyPath.NodeOf(body.Path) == 0 || BodyPath.SlotOf(body.Path) < 0 || (body.Path & 0xFFF) != 0 ||
            body.Kind is not (BodyKind.Planet or BodyKind.Moon) ||
            (body.Kind == BodyKind.Planet && BodyPath.MoonOf(body.Path) >= 0) ||
            (body.Kind == BodyKind.Moon && BodyPath.MoonOf(body.Path) < 0))
            throw new ArgumentException("An environment needs a planet or moon body path.", nameof(body));
        PlanetState state = body.State ?? throw new ArgumentException("The body has no physical surface state.", nameof(body));
        if (state.Surface is not (SurfaceKind.Rock or SurfaceKind.Ice or SurfaceKind.Ocean))
            throw new NotSupportedException("The selected body has no supported solid or ocean surface.");
        PlanetEnvironmentMetadata metadata = Snapshot(body, state);
        ValidateMetadata(metadata);
        if (kind == EnvironmentSurfaceKind.RealRadius && radius != metadata.PhysicalRadius)
            throw new ArgumentException("Real-radius diagnostics must use the body's physical radius.", nameof(radius));
        // The legacy walking ecology cannot represent sterile/ammonia/hydrocarbon/magma worlds yet. Keeping
        // their metadata in identity is necessary but must not pretend that an Earth ecology is appropriate.
        if (!diagnostic && kind == EnvironmentSurfaceKind.Walking &&
            (state.Biosphere != Biosphere.Complex || state.Liquid is not (LiquidKind.None or LiquidKind.Water)))
            throw new NotSupportedException("This body's ecology/liquid requires an environment profile not implemented by legacy walking terrain.");

        WalkingParameters? parameters = null;
        if (kind == EnvironmentSurfaceKind.Walking)
        {
            parameters = (walkingParameters ?? WalkingParameters.ForBody(system.Address.Value, body.Path)) with { };
            ValidateParameters(parameters);
        }
        else if (walkingParameters != null)
            throw new ArgumentException("Walking parameters cannot configure another generator kind.", nameof(walkingParameters));

        ulong canonicalSurfaceSeed = Hash64.Of(system.Address.Value, body.Path);
        if (kind != EnvironmentSurfaceKind.StandIn && surfaceSeed.HasValue && surfaceSeed.Value != canonicalSurfaceSeed)
            throw new ArgumentException("Legacy walking/real-radius generation does not accept an arbitrary zone surface seed.", nameof(surfaceSeed));
        ulong resolvedSurfaceSeed = surfaceSeed ?? canonicalSurfaceSeed;
        ulong terrainSeed = kind == EnvironmentSurfaceKind.Walking ? WalkingWorld.SeedFor(canonicalSurfaceSeed)
            : kind == EnvironmentSurfaceKind.RealRadius
                ? Hash64.Of(GeneratorVersion.SurfaceSeed(universeSeed), system.Address.Value, body.Path)
                : resolvedSurfaceSeed;
        Vector3D? resolvedLanding = null;
        if (landing.HasValue)
        {
            if (kind != EnvironmentSurfaceKind.StandIn)
                throw new ArgumentException("Only stand-in generation consumes a supplied landing direction.", nameof(landing));
            Vector3D value = landing.Value;
            if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z) ||
                !double.IsFinite(value.LengthSquared) || value.LengthSquared <= 0)
                throw new ArgumentException("Landing must be a finite nonzero direction.", nameof(landing));
            // StandInWorld normalizes this input itself. Normalizing here as well can change the final bits after
            // a second normalization, so identity preserves exactly the immutable constructor input.
            resolvedLanding = value;
        }

        bool legacy = !diagnostic && universeSeed == Galaxy.Galaxy.DefaultSeed && kind != EnvironmentSurfaceKind.RealRadius &&
            (kind != EnvironmentSurfaceKind.StandIn || resolvedLanding.HasValue) &&
            (parameters == null || parameters == WalkingParameters.ForBody(system.Address.Value, body.Path));
        if (legacy)
        {
            // FromBody is also used by diagnostics: caller-supplied metadata must not masquerade as the current
            // default-universe body merely because it has the same address.
            PlanetBodies.Entry? expected = PlanetBodies.Find(system.Address.Value, body.Path);
            legacy = expected?.Body.State is { } expectedState && Snapshot(expected.Body, expectedState) == metadata;
        }
        return new PlanetEnvironmentDescriptor(universeSeed, new WorldAddress(system.Address.Value, body.Path), kind,
            radius, metadata, parameters, resolvedSurfaceSeed, terrainSeed, resolvedLanding, diagnostic, legacy, formationSelection);
    }

    private static PlanetEnvironmentMetadata Snapshot(Body body, PlanetState state) => new(body.Kind, body.PlanetType,
        body.Radius, body.Mass, body.SurfaceGravity, body.Temperature, body.Atmosphere, body.SurfacePressure,
        body.RotationPeriod, body.AxialTilt, body.TidallyLocked, state with { });

    private static void ValidateRadius(double radius, EnvironmentSurfaceKind kind, bool diagnostic)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!double.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (kind == EnvironmentSurfaceKind.RealRadius && !diagnostic)
            throw new ArgumentException("Real-radius terrain is an explicit diagnostic, not a playable world.", nameof(diagnostic));
        if (!diagnostic && (radius < GameScale.MinimumPlanetRadius || radius > GameScale.MaximumPlanetRadius))
            throw new ArgumentOutOfRangeException(nameof(radius), "Playable radius exceeds the configured surface-area bounds.");
    }

    private static void ValidateMetadata(PlanetEnvironmentMetadata metadata)
    {
        // Traversal rejects nonfinite scalar values, unknown enum values and unsupported added field types.
        _ = EnvironmentCanonical.Value(metadata);
        if (metadata.PhysicalRadius <= 0 || metadata.Mass < 0 || metadata.SurfaceGravity < 0 ||
            metadata.Temperature <= 0 || metadata.SurfacePressure < 0 ||
            metadata.State.OceanCover is < 0 or > 1 || metadata.State.IceCover is < 0 or > 1)
            throw new ArgumentException("Invalid physical metadata for environment generation.", nameof(metadata));
    }

    private static void ValidateParameters(WalkingParameters parameters)
    {
        _ = EnvironmentCanonical.Value(parameters);
        if (parameters.LayoutVariant < 0 || parameters.TerrainRevision < 0 || parameters.Provinces is < 1 or > 4096)
            throw new ArgumentException("Invalid walking revision/layout/province count.", nameof(parameters));
        // These values are divisors, grid sizes or nonnegative amplitudes in the legacy generator. Freeze and
        // validate once before admitting a build; do not allow NaN or zero-width work to consume reservations.
        foreach (PropertyInfo property in typeof(WalkingParameters).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            object? value = property.GetValue(parameters);
            if (value is double number && property.Name != nameof(WalkingParameters.FrozenLakeTemperature) && number < 0)
                throw new ArgumentException("Negative walking parameter: " + property.Name, nameof(parameters));
        }
        double[] positive = { parameters.ContinentWavelength, parameters.IslandWavelength, parameters.CoastDetailWavelength,
            parameters.WarpWavelength, parameters.CoastKindWavelength, parameters.SandyShoreRamp, parameters.RockyShoreRamp,
            parameters.ProvinceBlend, parameters.UpliftWavelength, parameters.HillsWavelength, parameters.RidgeWavelength,
            parameters.RidgeCrest, parameters.DrainageCell, parameters.ValleyCatchment, parameters.StreamCatchment, parameters.CanyonCatchment,
            parameters.CanyonWall, parameters.GorgeWall, parameters.ClimatePatchWavelength, parameters.WoodsWavelength,
            parameters.ScatterTile, parameters.MonumentSpacing };
        if (positive.Any(value => value <= 0) || parameters.OceanCover is < 0 or > 1 ||
            parameters.MonumentChance > 1 || parameters.CliffShare >= 1 ||
            parameters.PlainsWoods > 1 || parameters.HillsWoods > 1 || parameters.TablelandWoods > 1 || parameters.MountainWoods > 1 ||
            parameters.PlainsShare + parameters.HillsShare + parameters.TablelandShare + parameters.MountainShare <= 0)
            throw new ArgumentException("Walking parameter range is invalid.", nameof(parameters));
    }
}
