using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationSubstrateTests
{
    private static readonly FormationPieceId Piece = new(new FormationId(101, 2), 3);
    private const SupportCapabilities FullCaps = SupportCapabilities.Soil | SupportCapabilities.Plantable | SupportCapabilities.Walkable;

    [Test]
    public static void MaterialFractionsAndHabitatUnitsAreExplicit()
    {
        var evaluator = new FormationSubstrate(Profile(), Array.Empty<SubstrateInfluence>());
        SubstrateEvaluation result = evaluator.Evaluate(Hit(), Inputs());
        Assert.Near(0.2, result.Substrate.Bedrock, 1e-14, "relative bedrock weight normalized");
        Assert.Near(0.1, result.Substrate.LooseStone, 1e-14, "relative loose-stone weight normalized");
        Assert.Near(0.7, result.Substrate.Soil, 1e-14, "relative soil weight normalized");
        Assert.Near(1, result.Substrate.Bedrock + result.Substrate.LooseStone + result.Substrate.Soil, 1e-14, "material partition");
        Assert.Near(0.25, result.Substrate.SoilDepthMetres, 0, "soil depth remains metres");
        Assert.Near(0.56, result.Substrate.PlantSuitability, 1e-14, "soil and water suitability applied once");
        Assert.Near(0.28, result.Habitat.GrassCoverage, 1e-14, "grass is occupied-area fraction");
        Assert.Near(0.01092, result.Habitat.ShrubsPerSquareMetre, 1e-14, "shrub density is instances per square metre");
        Assert.Near(0.0028, result.Habitat.TreesPerSquareMetre, 1e-14, "tree density is instances per square metre");
        Assert.Near(0.3, result.Substrate.Litter, 1e-14, "litter uses explicit raw forest potential");
        Assert.Equal(0, result.AppliedInfluences, "no influence was invented");
    }

    [Test]
    public static void ContactInfluenceRequiresItsExactPiecePatchCapabilitiesAndSphere()
    {
        var evaluator = new FormationSubstrate(RockProfile(), new[] { Influence(0, soil: 1, strength: 1) });
        SubstrateEvaluation centre = evaluator.Evaluate(Hit(), Inputs());
        Assert.Near(1, centre.Substrate.Soil, 0, "the intended soil pocket is present");
        Assert.Equal(1, centre.AppliedInfluences, "one matching contact");
        foreach (SupportHit other in new[]
        {
            Hit(patch: "face"), Hit(piece: new FormationPieceId(Piece.Formation, 4)),
            Hit(caps: SupportCapabilities.Plantable), Hit(point: new Vector3D(2, 10, 0)),
            Hit(point: new Vector3D(3, 10, 0)), Hit(point: new Vector3D(1.8, 11.8, 0)),
        })
        {
            SubstrateEvaluation result = evaluator.Evaluate(other, Inputs());
            Assert.Equal(0, result.AppliedInfluences, "other patch/piece/capability or outside sphere is untouched");
            Assert.Near(0, result.Substrate.Soil, 0, "contact does not paint nearby unsupported surfaces");
        }
        SubstrateEvaluation halfway = evaluator.Evaluate(Hit(point: new Vector3D(1, 10, 0)), Inputs());
        Assert.Near(0.5, halfway.Substrate.Soil, 1e-14, "sphere influence has a finite radial falloff");
    }

    [Test]
    public static void SoilAndPlantPermissionsPreserveFacesAndOverhangs()
    {
        var evaluator = new FormationSubstrate(Profile(), Array.Empty<SubstrateInfluence>());
        foreach (SupportHit hit in new[] { Hit(caps: SupportCapabilities.Plantable), Hit(caps: SupportCapabilities.None),
            Hit(normal: Vector3D.UnitX), Hit(normal: -Vector3D.UnitY) })
        {
            SubstrateEvaluation result = evaluator.Evaluate(hit, Inputs());
            Assert.Near(0, result.Substrate.Soil, 0, "unpermitted or steep support retains no soil coat");
            Assert.Near(0, result.Substrate.SoilDepthMetres, 0, "no hidden depth on exposed faces");
            Assert.Near(0, result.Substrate.Litter, 0, "no blanket litter on faces");
            NoPlants(result);
            Assert.Near(1, result.Substrate.Bedrock + result.Substrate.LooseStone, 1e-14, "removed soil returns to mineral partition");
        }
        SubstrateEvaluation soilOnly = evaluator.Evaluate(Hit(caps: SupportCapabilities.Soil), Inputs());
        Assert.True(soilOnly.Substrate.Soil > 0, "soil permission is independent of plant permission");
        NoPlants(soilOnly);
    }

    [Test]
    public static void RawClimateWaterAndBiosphereGateTerrestrialHabitat()
    {
        var evaluator = new FormationSubstrate(Profile(), Array.Empty<SubstrateInfluence>());
        foreach (SubstrateInputs input in new[] { Inputs(temperature: -10), Inputs(temperature: 0), Inputs(temperature: 40),
            Inputs(temperature: 100), Inputs(moisture: 0, wetness: 0), Inputs(submerged: true), Inputs(liquid: false), Inputs(biosphere: false) })
        {
            SubstrateEvaluation result = evaluator.Evaluate(Hit(), input);
            NoPlants(result);
            Assert.Near(0.7, result.Substrate.Soil, 1e-14, "ecology gating does not alter physical support or abiotic soil");
            if (input.Submerged || !input.LiquidEligible || !input.BiosphereEligible)
                Assert.Near(0, result.Substrate.Litter, 0, "ineligible terrestrial ecology has no litter");
        }
        Assert.Near(0.28, evaluator.Evaluate(Hit(), Inputs(temperature: 10)).Substrate.PlantSuitability, 1e-14,
            "temperature response is declared and continuous inside its range");
        Assert.True(evaluator.Evaluate(Hit(), Inputs()).Habitat.GrassCoverage > 0, "a suitable environment supports plants");
    }

    [Test]
    public static void RawForestPotentialChangesHabitatWithoutFeedingBackIntoSoil()
    {
        var evaluator = new FormationSubstrate(Profile(), Array.Empty<SubstrateInfluence>());
        SubstrateEvaluation open = evaluator.Evaluate(Hit(), Inputs(forest: 0));
        SubstrateEvaluation woods = evaluator.Evaluate(Hit(), Inputs(forest: 1));
        Assert.Equal(open.Substrate.Soil, woods.Substrate.Soil, "forest potential does not regrade or create soil");
        Assert.Equal(open.Substrate.PlantSuitability, woods.Substrate.PlantSuitability, "coverage is not recursively thinned");
        Assert.Near(0, open.Habitat.TreesPerSquareMetre, 0, "open habitat has no trees");
        Assert.True(woods.Habitat.TreesPerSquareMetre > 0 && woods.Habitat.GrassCoverage < open.Habitat.GrassCoverage,
            "raw forest potential partitions habitat");
        Assert.Near(0, open.Substrate.Litter, 0, "open habitat has no forest litter");
    }

    [Test]
    public static void SupportInclinationUsesRadialUpRatherThanGlobalY()
    {
        var evaluator = new FormationSubstrate(Profile(), Array.Empty<SubstrateInfluence>());
        SubstrateEvaluation pole = evaluator.Evaluate(Hit(), Inputs());
        SubstrateEvaluation equator = evaluator.Evaluate(Hit(point: new Vector3D(10, 0, 0), normal: Vector3D.UnitX), Inputs(up: Vector3D.UnitX));
        Assert.Equal(pole, equator, "rotating the same support relative to the body preserves substrate and habitat");
    }

    [Test]
    public static void CanonicalInfluenceReductionIgnoresArrivalOrderAndThreadOrder()
    {
        SubstrateInfluence[] values = { Influence(9, 0.2, 0.35), Influence(2, 0.9, 0.7), Influence(5, 0.5, 0.2) };
        var evaluator = new FormationSubstrate(Profile(), values);
        SubstrateEvaluation expected = evaluator.Evaluate(Hit(), Inputs());
        foreach (int[] order in new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
            new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } })
        {
            var reordered = new FormationSubstrate(Profile(), order.Select(i => values[i]));
            Assert.Equal(expected, reordered.Evaluate(Hit(), Inputs()), "floating-point reduction follows canonical keys");
        }
        Parallel.For(0, 32, _ => Assert.Equal(expected, evaluator.Evaluate(Hit(), Inputs()), "concurrent reads are independent"));
        Assert.Equal(2, evaluator.Influences[0].Id.Slot, "snapshot exposes canonical order");
        Assert.Equal(3, expected.AppliedInfluences, "every matching influence is accounted for");
    }

    [Test]
    public static void InfluenceSnapshotCannotFollowSourceCollectionMutation()
    {
        var source = new List<SubstrateInfluence> { Influence(1, 0.8, 0.5) };
        var evaluator = new FormationSubstrate(Profile(), source);
        SubstrateEvaluation expected = evaluator.Evaluate(Hit(), Inputs());
        source[0] = Influence(7, 0, 1);
        source.Clear();
        Assert.Equal(expected, evaluator.Evaluate(Hit(), Inputs()), "mutating the source list cannot change the immutable snapshot");
        Assert.Equal(1, evaluator.Influences.Length, "the evaluator retains its own bounded array");
        foreach (Type type in new[] { typeof(SubstrateProfile), typeof(SubstrateInfluence), typeof(SubstrateInputs) })
            Assert.True(type.GetProperties().All(property => property.SetMethod == null), "published input properties are get-only: " + type.Name);
    }

    [Test]
    public static void InvalidOrUnknownSupportAndDuplicateInfluenceKeysFailExplicitly()
    {
        var evaluator = new FormationSubstrate(Profile(), Array.Empty<SubstrateInfluence>());
        Reject<FormationValidationException>(() => evaluator.Evaluate(default, Inputs()));
        Reject<FormationValidationException>(() => evaluator.Evaluate(Hit(), default));
        Reject<FormationValidationException>(() => evaluator.Evaluate(Hit(profile: "unknown"), Inputs()));
        Reject<FormationValidationException>(() => new FormationSubstrate(Profile(), new[] { Influence(1, 0.3, 0.5), Influence(1, 0.9, 1) }));
        Reject<FormationValidationException>(() => new FormationSubstrate(Profile(), new SubstrateInfluence[] { null! }));
    }

    [Test]
    public static void FiniteRangesAreValidatedBeforePublishingSubstrateInputs()
    {
        foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1 })
        {
            Reject<FormationValidationException>(() => new SubstrateProfile("invalid", invalid, 1, 1, 0.2, 1, 1, 1, 1));
            Reject<FormationValidationException>(() => new SubstrateInfluence(new(Piece, 0), Piece, "shelf", FullCaps,
                new Vector3D(0, 10, 0), invalid, 1, 0, 0, 1, 0.2));
        }
        Reject<FormationValidationException>(() => new SubstrateProfile("empty", 0, 0, 0, 0, 1, 1, 1, 1));
        Reject<FormationValidationException>(() => new SubstrateProfile("invalid", 1, 0, 0, 0.2, 1, 1, 1, 1));
        Reject<FormationValidationException>(() => new SubstrateProfile("invalid", 0, 0, 1, 0.2, 1.1, 1, 1, 1));
        Reject<FormationValidationException>(() => new SubstrateProfile("invalid", 0, 0, 1, 0.2, 1, 1, 1, 1, maximumTemperatureC: 0));
        Reject<FormationValidationException>(() => Inputs(temperature: double.NaN));
        Reject<FormationValidationException>(() => Inputs(moisture: 1.1));
        Reject<FormationValidationException>(() => Inputs(forest: -0.1));
        Reject<FormationValidationException>(() => Inputs(up: Vector3D.Zero));
        Reject<FormationValidationException>(() => new SubstrateInfluence(new(Piece, -1), Piece, "shelf", FullCaps,
            new Vector3D(0, 10, 0), 2, 1, 0, 0, 1, 0.2));
        Reject<FormationValidationException>(() => new SubstrateInfluence(new(Piece, 1), Piece, "shelf", (SupportCapabilities)128,
            new Vector3D(0, 10, 0), 2, 1, 0, 0, 1, 0.2));
        Reject<FormationValidationException>(() => new SubstrateInfluence(new(Piece, 1), Piece, "shelf", FullCaps,
            new Vector3D(0, 10, 0), double.Epsilon, 1, 0, 0, 1, 0.2));
        var hugeWeights = new SubstrateProfile("huge", double.MaxValue, double.MaxValue, double.MaxValue, 0.2, 1, 1, 1, 1);
        Assert.Near(1, hugeWeights.Bedrock + hugeWeights.LooseStone + hugeWeights.Soil, 1e-14, "finite large weights normalize without overflow");
    }

    [Test]
    public static void CapacityFailuresAreTypedAndReservationPrecedesEnumeration()
    {
        var limits = new FormationSubstrateLimits { MaxInfluences = 1, MaxEstimatedResidentBytes = 8192, MaxConstructionScratchBytes = 536 };
        var evaluator = new FormationSubstrate(Profile(), new[] { Influence(1, 0.8, 0.5) }, limits);
        Assert.Equal(8192L, evaluator.EstimatedResidentBytes, "declared resident estimate includes snapshot and one influence");
        Assert.Equal(536L, evaluator.ReservedConstructionScratchBytes, "bounded copy/sort reservation");
        Reject<FormationCapacityException>(() => new FormationSubstrate(Profile(), new[] { Influence(1, 0.8, 0.5), Influence(2, 0.4, 0.5) }, limits));
        int enumerations = 0;
        IEnumerable<SubstrateInfluence> Source()
        {
            enumerations++;
            yield return Influence(1, 0.8, 0.5);
        }
        Reject<FormationCapacityException>(() => new FormationSubstrate(Profile(), Source(), limits with { MaxEstimatedResidentBytes = 8191 }));
        Reject<FormationCapacityException>(() => new FormationSubstrate(Profile(), Source(), limits with { MaxConstructionScratchBytes = 535 }));
        Assert.Equal(0, enumerations, "capacity is refused before an enumerable can start building influences");
        Reject<FormationValidationException>(() => new FormationSubstrate(Profile(), Source(), limits with { MaxInfluences = -1 }));
        var empty = new FormationSubstrate(Profile(), Array.Empty<SubstrateInfluence>(), limits with { MaxInfluences = 0 });
        Assert.Equal(0, empty.Influences.Length, "zero-influence snapshots are explicitly supported");
    }

    [Test]
    public static void EvaluationOnlyConsumesTheSelectedSupportAndNeverChangesItsGeometry()
    {
        SupportHit hit = Hit();
        SupportHit original = hit;
        var evaluator = new FormationSubstrate(Profile(), new[] { Influence(0, 1, 1) });
        for (int i = 0; i < 8; i++) _ = evaluator.Evaluate(hit, Inputs(temperature: i * 5));
        Assert.Equal(original, hit, "identity, point, normal and barycentrics remain exactly the selected physical support");
        foreach (FieldInfo field in typeof(FormationSubstrate).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            Assert.True(!typeof(Delegate).IsAssignableFrom(field.FieldType) && !typeof(ISurface).IsAssignableFrom(field.FieldType),
                "the evaluator cannot retain a terrain/support callback: " + field.Name);
        Assert.Equal(typeof(SupportHit), typeof(FormationSubstrate).GetMethod(nameof(FormationSubstrate.Evaluate))!
            .GetParameters()[0].ParameterType.GetElementType(), "evaluation accepts the chosen hit by readonly reference");
    }

    private static SubstrateProfile Profile() => new("test-soil", 2, 1, 7, 0.25, 1, 0.8, 0.03, 0.01, litter: 0.6);
    private static SubstrateProfile RockProfile() => new("test-soil", 1, 0, 0, 0, 1, 0.8, 0.03, 0.01);
    private static SubstrateInfluence Influence(int slot, double soil, double strength) => new(new SubstrateInfluenceId(Piece, slot),
        Piece, "shelf", SupportCapabilities.Soil, new Vector3D(0, 10, 0), 2, strength, 1 - soil, 0, soil, soil > 0 ? 0.4 : 0, 1, 0.6);
    private static SupportHit Hit(Vector3D? point = null, Vector3D? normal = null, FormationPieceId? piece = null,
        string patch = "shelf", string profile = "test-soil", SupportCapabilities caps = FullCaps) =>
        new(new PhysicalPrimitiveId(piece ?? Piece, "synthetic", 1, 0), point ?? new Vector3D(0, 10, 0), normal ?? Vector3D.UnitY,
            Vector3D.UnitX, 1, patch, "appearance-test", profile, caps);
    private static SubstrateInputs Inputs(double temperature = 20, double moisture = 0.8, double wetness = 0.2,
        double forest = 0.5, bool submerged = false, bool liquid = true, bool biosphere = true, Vector3D? up = null) =>
        new(up ?? Vector3D.UnitY, temperature, moisture, wetness, forest, submerged, liquid, biosphere);
    private static void NoPlants(SubstrateEvaluation result)
    {
        Assert.Near(0, result.Substrate.PlantSuitability, 0, "no plant suitability");
        Assert.Equal(new HabitatSample(0, 0, 0), result.Habitat, "no terrestrial plants");
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }
}
