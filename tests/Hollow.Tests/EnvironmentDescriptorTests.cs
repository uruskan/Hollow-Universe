using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;
using Hollow.Surface;

namespace Hollow.Tests;

public static class EnvironmentDescriptorTests
{
    [Test]
    public static void ResolutionCapturesIdentityAndMetadataWithoutBuildingTerrain()
    {
        AssertMetadataOwnersDoNotConstructWorlds();
        var (system, earth) = StartingWorld.Find();
        PlanetEnvironmentDescriptor descriptor = Earth();

        Assert.Equal(system.Address.Value, descriptor.Address.SystemAddress, "descriptor system address");
        Assert.Equal(earth.Path, descriptor.Address.BodyPath, "descriptor body path");
        Assert.Equal(Galaxy.Galaxy.DefaultSeed, descriptor.UniverseSeed, "explicit default universe identity");
        Assert.Equal(EnvironmentSurfaceKind.Walking, descriptor.Kind, "walking is the default surface kind");
        Assert.Equal(GameScale.PlanetRadius(earth), descriptor.Radius, "playable radius is preserved");
        Assert.Equal(earth.State, descriptor.Metadata.State, "physical state is captured without terrain");
        Assert.Equal(WalkingParameters.Earth, descriptor.WalkingParameters, "the production profile is resolved");
        Assert.Equal(1, descriptor.SeedSchema, "legacy construction uses seed schema one");
        Assert.Equal(WalkingParameters.Earth.TerrainRevision, descriptor.TerrainRevision, "resolved compatibility revision");
        Assert.True(!descriptor.IsDiagnostic && descriptor.IsLegacyMultiplayerCompatible,
            "current Earth inputs remain admissible through the temporary legacy compatibility fence");
        Assert.True(!string.IsNullOrWhiteSpace(descriptor.RequiredPhysicalContentFingerprint), "required physical content has an identity");
        Assert.True(!string.IsNullOrWhiteSpace(descriptor.GameplayVisibilitySignature), "legacy visibility compatibility is explicitly identified");
        byte[] canonical = Convert.FromBase64String(descriptor.CanonicalIdentity);
        Assert.True(canonical.Length > 0, "full canonical identity is retained");
        Assert.True(SHA256.HashData(canonical).SequenceEqual(Convert.FromHexString(descriptor.Identity)),
            "the display identity is SHA-256 of the full canonical identity");

        uint starPath = system.Bodies.First(body => body.State == null).Path;
        Assert.True(EnvironmentDescriptors.Resolve(system.Address.Value, starPath, 1200) == null, "stars have no environment descriptor");
        for (uint i = 0; i < 8; i++)
        {
            Assert.True(EnvironmentDescriptors.Resolve(ulong.MaxValue - i, uint.MaxValue - i, 1200) == null,
                "unknown addresses do not resolve another world's metadata");
            Assert.True(EnvironmentDescriptors.Resolve(system.Address.Value, uint.MaxValue - i, 1200) == null,
                "unknown body paths have no descriptor");
        }
    }

    [Test]
    public static void EquivalentValuesHaveCanonicalIdentityIndependentOfAllocationAndInitializerOrder()
    {
        var (system, earth) = StartingWorld.Find();
        WalkingParameters firstProfile = WalkingParameters.Earth with { LayoutVariant = 0, HillsHeight = 45 };
        WalkingParameters secondProfile = WalkingParameters.Earth with { HillsHeight = 45, LayoutVariant = 0 };
        Body copiedBody = CopyBody(earth, state: earth.State! with { });
        PlanetEnvironmentDescriptor first = EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed,
            system, earth, 1200, walkingParameters: firstProfile);
        PlanetEnvironmentDescriptor second = EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed,
            system, copiedBody, 1200, walkingParameters: secondProfile);

        Assert.Equal(first.CanonicalIdentity, second.CanonicalIdentity, "equivalent physical/profile values serialize identically");
        Assert.Equal(first.Identity, second.Identity, "equivalent separately allocated inputs have the same digest");
        Assert.Equal(first.TerrainSeed, second.TerrainSeed, "equivalent layouts have the same terrain seed");
        Assert.True(first.Equals(second), "descriptor equality uses the equivalent complete identity");
        Assert.Equal(first.GetHashCode(), second.GetHashCode(), "equal descriptors can share dictionary keys");
        Assert.True(!first.IsLegacyMultiplayerCompatible && !second.IsLegacyMultiplayerCompatible,
            "equal overridden profiles do not pretend to be negotiated by the legacy wire format");
        PlanetEnvironmentDescriptor defaultCopy = Earth(walkingParameters: WalkingParameters.Earth with { });
        Assert.Equal(Earth().CanonicalIdentity, defaultCopy.CanonicalIdentity, "an explicit value-equal default profile is canonical");
        Assert.True(defaultCopy.IsLegacyMultiplayerCompatible, "allocation identity does not reject the unchanged production profile");
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "tr-TR", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                PlanetEnvironmentDescriptor localized = EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed,
                    system, copiedBody, 1200, walkingParameters: secondProfile);
                Assert.Equal(first.CanonicalIdentity, localized.CanonicalIdentity, "canonical scalar encoding is independent of culture");
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Test]
    public static void UniverseProfileLayoutRadiusAndSurfaceKindCannotAlias()
    {
        var (_, earth) = StartingWorld.Find();
        PlanetEnvironmentDescriptor baseline = Earth();
        PlanetEnvironmentDescriptor customUniverse = Earth(universeSeed: 11);
        PlanetEnvironmentDescriptor changedProfile = Earth(walkingParameters: WalkingParameters.Earth with { HillsHeight = 45 });
        PlanetEnvironmentDescriptor changedLayout = Earth(walkingParameters: WalkingParameters.Earth with { LayoutVariant = 0 });
        PlanetEnvironmentDescriptor changedRadius = Earth(radius: GameScale.MinimumPlanetRadius);
        PlanetEnvironmentDescriptor standIn = Earth(kind: EnvironmentSurfaceKind.StandIn, landing: Vector3D.UnitX);
        PlanetEnvironmentDescriptor real = Earth(radius: earth.Radius, kind: EnvironmentSurfaceKind.RealRadius, diagnostic: true);
        PlanetEnvironmentDescriptor[] descriptors = { baseline, customUniverse, changedProfile, changedLayout, changedRadius, standIn, real };
        Assert.Equal(descriptors.Length, descriptors.Select(d => d.CanonicalIdentity).Distinct().Count(),
            "different universe/profile/layout/radius/kind inputs have distinct complete identities");
        Assert.Equal(descriptors.Length, descriptors.Select(d => d.Identity).Distinct().Count(), "distinct fixture identities have distinct digests");
        Assert.True(!customUniverse.IsLegacyMultiplayerCompatible && !changedProfile.IsLegacyMultiplayerCompatible
            && !changedLayout.IsLegacyMultiplayerCompatible && !real.IsLegacyMultiplayerCompatible,
            "unnegotiated universe/profile/layout and diagnostic worlds cannot join as legacy compatible");
        Assert.True(real.IsDiagnostic, "real-radius terrain is an explicit diagnostic context");
        Assert.Equal(baseline.SurfaceSeed, customUniverse.SurfaceSeed, "schema one keeps the address-derived surface seed");
        Assert.Equal(baseline.TerrainSeed, customUniverse.TerrainSeed, "adding universe identity does not silently reseed schema-one Earth");
        ulong surfaceSeed = Hash64.Of(baseline.Address.SystemAddress, baseline.Address.BodyPath);
        Assert.Equal(surfaceSeed, baseline.SurfaceSeed, "the legacy surface seed mapping is unchanged");
        Assert.Equal(WalkingWorld.SeedFor(surfaceSeed), baseline.TerrainSeed,
            "the descriptor preserves the constructor seed before WalkingWorld applies its explicit layout");

        PlanetEnvironmentDescriptor differentLanding = Earth(kind: EnvironmentSurfaceKind.StandIn, landing: Vector3D.UnitY);
        PlanetEnvironmentDescriptor differentStandInSeed = Earth(kind: EnvironmentSurfaceKind.StandIn,
            surfaceSeed: surfaceSeed ^ 1UL, landing: Vector3D.UnitX);
        Assert.True(standIn.CanonicalIdentity != differentLanding.CanonicalIdentity, "stand-in landing changes generated ground identity");
        Assert.True(standIn.CanonicalIdentity != differentStandInSeed.CanonicalIdentity, "stand-in surface seed changes generated ground identity");
    }

    [Test]
    public static void PhysicalSnapshotsRemainStableWhenCallerInputsAreReplaced()
    {
        var (system, earth) = StartingWorld.Find();
        PlanetState initialState = earth.State! with { };
        Body initialBody = CopyBody(earth, state: initialState);
        PlanetEnvironmentDescriptor descriptor = EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed, system, initialBody, 1200);
        string identity = descriptor.CanonicalIdentity;

        // Body/PlanetState physical properties are init-only. Replace caller-owned snapshots rather
        // than bypassing that contract with reflection; the descriptor must retain its resolved values.
        Body warmer = CopyBody(initialBody, temperature: earth.Temperature + 1);
        PlanetState changedState = initialState with { HeatFlow = initialState.HeatFlow + 0.01 };
        Body differentGeology = CopyBody(initialBody, state: changedState);
        PlanetEnvironmentDescriptor warmerDescriptor = EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed, system, warmer, 1200);
        PlanetEnvironmentDescriptor geologyDescriptor = EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed, system, differentGeology, 1200);
        initialBody = warmer;
        initialState = changedState;
        system.Bodies.Clear(); // caller-owned metadata graph is no longer available for deferred lookup

        Assert.Equal(identity, descriptor.CanonicalIdentity, "the original descriptor's identity does not follow caller replacement");
        Assert.Equal(earth.State, descriptor.Metadata.State, "the original physical state remains frozen");
        Assert.True(descriptor.CanonicalIdentity != warmerDescriptor.CanonicalIdentity, "temperature belongs to environment identity");
        Assert.True(descriptor.CanonicalIdentity != geologyDescriptor.CanonicalIdentity, "full geological metadata belongs to environment identity");
        Assert.True(!warmerDescriptor.IsLegacyMultiplayerCompatible && !geologyDescriptor.IsLegacyMultiplayerCompatible,
            "changed physical metadata cannot masquerade as the current default-universe body");
        Assert.Equal(initialState, geologyDescriptor.Metadata.State, "the replacement descriptor owns the replacement state");
        Assert.Equal(initialBody.Temperature, warmerDescriptor.Metadata.Temperature, "the replacement descriptor captures its new temperature");
    }

    [Test]
    public static void DirectFactoryPreservesLegacyWalkingFieldsAndLandingForLayoutsZeroAndFive()
    {
        var (system, earth) = StartingWorld.Find();
        Vector3D[] directions = PlanetSurface.FibonacciSphere(32).Concat(new[]
            { Vector3D.UnitX, -Vector3D.UnitX, Vector3D.UnitY, -Vector3D.UnitY, Vector3D.UnitZ, -Vector3D.UnitZ }).ToArray();
        foreach (int layout in new[] { 0, 5 })
        {
            WalkingParameters profile = WalkingParameters.Earth with { LayoutVariant = layout };
            PlanetEnvironmentDescriptor descriptor = EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed,
                system, earth, 1200, walkingParameters: profile);
            ISurface generated = EnvironmentWorldFactory.Create(descriptor);
            Assert.True(generated is WalkingWorld, "walking descriptors build walking terrain");
            var actual = (WalkingWorld)generated;
            WalkingWorld expected = WalkingWorld.For(system, earth, 1200, profile);
            Assert.True(!ReferenceEquals(expected, actual), "comparison uses separate direct generation instances");
            foreach (Vector3D direction in directions)
            {
                Assert.Equal(expected.Fields(direction), actual.Fields(direction), $"layout {layout}: all raw walking fields agree");
                Assert.Equal(expected.HeightAt(direction), actual.HeightAt(direction), $"layout {layout}: graded ground agrees");
                Assert.Equal(expected.WaterLevelAt(direction), actual.WaterLevelAt(direction), $"layout {layout}: water agrees");
            }
            Assert.Equal(expected.LandingSite, actual.LandingSite, $"layout {layout}: landing uses the same generated world");
            Assert.Equal(profile, actual.Parameters, $"layout {layout}: complete resolved profile reaches construction");
        }
        Fixtures.Report($"two direct layout constructions each match an independent legacy world at {directions.Length} points and landing");
    }

    [Test]
    public static void DirectFactoryPreservesStandInLandingInputAndLegacyCompatibility()
    {
        var rawLanding = new Vector3D(0.3, 0.7, -0.41);
        PlanetEnvironmentDescriptor descriptor = Earth(kind: EnvironmentSurfaceKind.StandIn,
            landing: rawLanding, diagnostic: true);
        Assert.Equal<Vector3D?>(rawLanding, descriptor.Landing,
            "stand-in input reaches its constructor without an extra normalization");
        ISurface actual = EnvironmentWorldFactory.Create(descriptor);
        Assert.True(actual is StandInWorld, "stand-in descriptors build stand-in terrain");
        ISurface repeated = EnvironmentWorldFactory.Create(descriptor);
        Assert.True(!ReferenceEquals(actual, repeated), "each direct factory call transfers a fresh independently owned surface");
        var expected = new StandInWorld(descriptor.Radius, descriptor.SurfaceSeed, rawLanding);
        Vector3D[] directions = PlanetSurface.FibonacciSphere(24).Concat(new[] { rawLanding.Normalized }).ToArray();
        foreach (Vector3D direction in directions)
        {
            Assert.Equal(expected.HeightAt(direction), actual.HeightAt(direction),
                "stand-in factory preserves exact legacy heights with a nonunit landing input");
            Assert.Equal(expected.HeightAt(direction), repeated.HeightAt(direction),
                "fresh direct factory instances preserve the same deterministic heights");
        }

        PlanetEnvironmentDescriptor missingLanding = Earth(kind: EnvironmentSurfaceKind.StandIn);
        Assert.True(!missingLanding.IsDiagnostic && !missingLanding.IsLegacyMultiplayerCompatible,
            "a missing stand-in landing cannot be reconstructed by the current wire format");
    }

    [Test]
    public static void InvalidNumericProfilesAndUnrepresentableLegacySeedsAreRejected()
    {
        foreach (double radius in new[] { 0, -1, double.NaN, double.NegativeInfinity, double.PositiveInfinity,
                     GameScale.MinimumPlanetRadius - 1, GameScale.MaximumPlanetRadius + 1 })
            Rejected(() => Earth(radius: radius), "invalid playable radius");
        foreach (double radius in new[] { 0, -1, double.NaN, double.PositiveInfinity })
            Rejected(() => Earth(radius: radius, diagnostic: true), "diagnostics still require finite positive radius");
        WalkingParameters[] invalidProfiles =
        {
            WalkingParameters.Earth with { LayoutVariant = -1 },
            WalkingParameters.Earth with { OceanCover = double.NaN },
            WalkingParameters.Earth with { OceanCover = -0.1 },
            WalkingParameters.Earth with { OceanCover = 1.1 },
            WalkingParameters.Earth with { ContinentWavelength = 0 },
            WalkingParameters.Earth with { ProvinceBlend = double.PositiveInfinity },
            WalkingParameters.Earth with { DrainageCell = 0 },
            WalkingParameters.Earth with { ValleyCatchment = 0 },
            WalkingParameters.Earth with { ResourceDensity = -1 },
        };
        foreach (WalkingParameters profile in invalidProfiles)
            Rejected(() => Earth(walkingParameters: profile), "invalid profile cannot enter generation");
        Rejected(() => Earth(kind: (EnvironmentSurfaceKind)255), "unknown surface kind");
        Rejected(() => Earth(surfaceSeed: Earth().SurfaceSeed ^ 1UL), "walking rejects a seed that the legacy generator cannot represent");
        Rejected(() => Earth(kind: EnvironmentSurfaceKind.StandIn, landing: new Vector3D(double.NaN, 1, 0)),
            "nonfinite stand-in landing");
        var (_, earth) = StartingWorld.Find();
        Rejected(() => Earth(radius: earth.Radius, kind: EnvironmentSurfaceKind.RealRadius), "real-radius generation must be diagnostic");
        Rejected(() => Earth(radius: 1200, kind: EnvironmentSurfaceKind.RealRadius, diagnostic: true),
            "real-radius descriptor must match its physical radius");
    }

    [Test]
    public static void InvalidPhysicalNumbersAndUnimplementedEnvironmentsCannotEnterPlayableGeneration()
    {
        var (system, earth) = StartingWorld.Find();
        PlanetState state = earth.State!;
        Rejected(() => EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed, system,
            CopyBody(earth, temperature: double.NaN), 1200), "nonfinite physical temperature");
        Rejected(() => EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed, system,
            CopyBody(earth, state: state with { OceanCover = double.PositiveInfinity }), 1200), "nonfinite physical ocean share");
        Rejected(() => EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed, system,
            CopyBody(earth, state: state with { IceCover = -0.01 }), 1200), "negative physical ice share");

        Body sterile = CopyBody(earth, state: state with { Biosphere = Biosphere.None });
        Body foreignLiquid = CopyBody(earth, state: state with { Liquid = LiquidKind.Hydrocarbons });
        Unsupported(() => EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed, system, sterile, 1200),
            "sterile worlds require an implemented ecology profile");
        Unsupported(() => EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed, system, foreignLiquid, 1200),
            "hydrocarbons cannot silently use water ecology");
        Unsupported(() => EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed, system,
            CopyBody(earth, state: state with { Surface = SurfaceKind.None }), 1200, diagnostic: true),
            "diagnostic mode cannot give a solid surface to an unsupported body");
        PlanetEnvironmentDescriptor diagnostic = EnvironmentDescriptors.FromBody(Galaxy.Galaxy.DefaultSeed,
            system, sterile, 1200, diagnostic: true);
        Assert.Equal(Biosphere.None, diagnostic.Metadata.State.Biosphere, "diagnostic preserves unsupported metadata honestly");
        Assert.True(diagnostic.IsDiagnostic && !diagnostic.IsLegacyMultiplayerCompatible,
            "explicit legacy look-dev does not authorize an unsupported playable ecology");
    }

    private static void AssertMetadataOwnersDoNotConstructWorlds()
    {
        // This architectural boundary replaces the removed process-wide cache counters. Check every current
        // metadata/identity owner before invoking resolution, including its catalog fingerprint helper. Required
        // immutable catalog loading is allowed; full terrain, drainage, placements and store acquisition are not.
        string[] owners =
        {
            "PlanetBodies.cs",
            "Environment/EnvironmentDescriptors.cs",
            "Environment/PlanetEnvironmentDescriptor.cs",
            "Environment/EnvironmentCanonical.cs",
        };
        var forbiddenOwner = new Regex(
            @"\b(?:WalkingWorld|PlanetSurface|StandInWorld|Drainage|CliffPlacements|MonumentPlacements|EnvironmentWorldFactory|WorldStore|WorldLease)\b",
            RegexOptions.CultureInvariant);
        foreach (string owner in owners)
        {
            string source = File.ReadAllText(Path.Combine(Fixtures.Root, "src", "Hollow.Surface", owner));
            string code = Regex.Replace(source, @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
            // Seed mapping and revision/rule constants do not construct or retain a generated environment.
            code = Regex.Replace(code, @"\bWalkingWorld\s*\.\s*(?:SeedFor|Version)\b", "");
            code = Regex.Replace(code,
                @"\bCliffPlacements\s*\.\s*(?:GenerationRevision|CellMetres|MaximumExtent|MaximumOffset|MaximumTiers|MaximumColumns)\b", "");
            code = Regex.Replace(code,
                @"\bMonumentPlacements\s*\.\s*(?:GenerationContract|Blend|MinimumSpacing|LandingClearance|IndexMargin)\b", "");
            Match forbidden = forbiddenOwner.Match(code);
            Assert.True(!forbidden.Success,
                $"metadata owner {owner} must not depend on generated-world ownership or construction: {forbidden.Value}");
        }
    }

    private static PlanetEnvironmentDescriptor Earth(double radius = 1200, EnvironmentSurfaceKind kind = EnvironmentSurfaceKind.Walking,
        ulong? universeSeed = null, WalkingParameters? walkingParameters = null,
        ulong? surfaceSeed = null, Vector3D? landing = null, bool diagnostic = false) =>
        EnvironmentDescriptors.Resolve(StartingWorld.System.Value, StartingWorld.BodyPath, radius, kind,
            universeSeed, walkingParameters, surfaceSeed, landing, diagnostic)
        ?? throw new TestFailure("the known Earth body must resolve");

    private static void Rejected(Action action, string context)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new TestFailure(context + ": expected an argument rejection before world construction");
    }

    private static void Unsupported(Action action, string context)
    {
        try { action(); }
        catch (NotSupportedException) { return; }
        throw new TestFailure(context + ": expected an explicit unsupported-profile rejection");
    }

    private static Body CopyBody(Body body, double? temperature = null, PlanetState? state = null) => new()
    {
        Index = body.Index, Parent = body.Parent, Kind = body.Kind, Name = body.Name, Designation = body.Designation,
        Path = body.Path, StarClass = body.StarClass, PlanetType = body.PlanetType, Mass = body.Mass, Radius = body.Radius,
        Temperature = temperature ?? body.Temperature, Luminosity = body.Luminosity, Atmosphere = body.Atmosphere,
        SurfacePressure = body.SurfacePressure, Orbit = body.Orbit, RotationPeriod = body.RotationPeriod,
        AxialTilt = body.AxialTilt, TidallyLocked = body.TidallyLocked, RingInnerRadius = body.RingInnerRadius,
        RingOuterRadius = body.RingOuterRadius, RingMaterial = body.RingMaterial, RingMass = body.RingMass,
        BeltWidth = body.BeltWidth, BeltMaterial = body.BeltMaterial, State = state ?? body.State,
    };
}
