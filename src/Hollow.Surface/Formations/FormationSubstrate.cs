using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Immutable synthetic substrate recipe. Material inputs are nonnegative relative weights, normalized on entry.
/// Soil depth is an apparent depth in metres, never an instruction to add or move physical geometry.
/// Ecology potentials are evaluated once against explicit raw inputs; no live-world profile is selected here.
/// </summary>
public sealed class SubstrateProfile
{
    public SubstrateProfile(string key, double bedrock, double looseStone, double soil, double soilDepthMetres,
        double plantSuitability, double grassCoverage, double shrubsPerSquareMetre, double treesPerSquareMetre,
        double litter = 0, double minimumTemperatureC = 0, double maximumTemperatureC = 40, double minimumSoilUpDot = 0.6)
    {
        Key = FormationInput.Key(key, nameof(key));
        (Bedrock, LooseStone, Soil) = SubstrateInput.Fractions(bedrock, looseStone, soil);
        SubstrateInput.Nonnegative(soilDepthMetres, nameof(soilDepthMetres));
        SubstrateInput.UnitInterval(plantSuitability, nameof(plantSuitability));
        SubstrateInput.UnitInterval(grassCoverage, nameof(grassCoverage));
        SubstrateInput.Nonnegative(shrubsPerSquareMetre, nameof(shrubsPerSquareMetre));
        SubstrateInput.Nonnegative(treesPerSquareMetre, nameof(treesPerSquareMetre));
        SubstrateInput.UnitInterval(litter, nameof(litter));
        SubstrateInput.UnitInterval(minimumSoilUpDot, nameof(minimumSoilUpDot));
        if (minimumSoilUpDot >= 1 || !double.IsFinite(minimumTemperatureC) || !double.IsFinite(maximumTemperatureC) ||
            !double.IsFinite(maximumTemperatureC - minimumTemperatureC) || maximumTemperatureC <= minimumTemperatureC ||
            (Soil == 0 && soilDepthMetres != 0))
            throw new FormationValidationException("Invalid soil-depth, temperature or support-inclination range.");
        SoilDepthMetres = soilDepthMetres; PlantSuitability = plantSuitability; GrassCoverage = grassCoverage;
        ShrubsPerSquareMetre = shrubsPerSquareMetre; TreesPerSquareMetre = treesPerSquareMetre; Litter = litter;
        MinimumTemperatureC = minimumTemperatureC; MaximumTemperatureC = maximumTemperatureC;
        MinimumSoilUpDot = minimumSoilUpDot;
    }

    public string Key { get; }
    public double Bedrock { get; }
    public double LooseStone { get; }
    public double Soil { get; }
    public double SoilDepthMetres { get; }
    public double PlantSuitability { get; }
    /// <summary>Maximum occupied-area fraction, not instances per square metre.</summary>
    public double GrassCoverage { get; }
    public double ShrubsPerSquareMetre { get; }
    public double TreesPerSquareMetre { get; }
    public double Litter { get; }
    public double MinimumTemperatureC { get; }
    public double MaximumTemperatureC { get; }
    /// <summary>Dot product of geometric support normal with explicit radial up; not rise/run or sine of tilt.</summary>
    public double MinimumSoilUpDot { get; }
}

/// <summary>Stable influence slot owned by a canonical physical piece, independent of collection order.</summary>
public readonly record struct SubstrateInfluenceId(FormationPieceId Owner, int Slot) : IComparable<SubstrateInfluenceId>
{
    public int CompareTo(SubstrateInfluenceId other)
    {
        int c = Owner.CompareTo(other.Owner);
        return c != 0 ? c : Slot.CompareTo(other.Slot);
    }
}

/// <summary>
/// A finite body-space contact sphere with an exact target piece and patch. It cannot paint all surfaces near a cliff.
/// Strength falls linearly from its centre to zero at the radius. Target fractions/depth are blended in canonical
/// influence order; wetness is a lower bound within the influence, and litter is a potential before ecology gating.
/// </summary>
public sealed class SubstrateInfluence
{
    public SubstrateInfluence(SubstrateInfluenceId id, FormationPieceId targetPiece, string targetPatchKey,
        SupportCapabilities requiredCapabilities, Vector3D centre, double radiusMetres, double strength,
        double bedrock, double looseStone, double soil, double soilDepthMetres, double wetness = 0, double litter = 0)
    {
        if (id.Owner.Slot < 0 || id.Slot < 0 || targetPiece.Slot < 0)
            throw new FormationValidationException("Influence owner, slot and target must be nonnegative.");
        TargetPatchKey = FormationInput.Key(targetPatchKey, nameof(targetPatchKey));
        FormationInput.Capabilities(requiredCapabilities, nameof(requiredCapabilities));
        FormationInput.Finite(centre, nameof(centre));
        if (!double.IsFinite(radiusMetres) || radiusMetres <= 0 || !double.IsFinite(radiusMetres * radiusMetres) ||
            radiusMetres * radiusMetres == 0)
            throw new FormationValidationException("An influence radius must have a finite positive representable square.", nameof(radiusMetres));
        SubstrateInput.UnitInterval(strength, nameof(strength));
        (Bedrock, LooseStone, Soil) = SubstrateInput.Fractions(bedrock, looseStone, soil);
        SubstrateInput.Nonnegative(soilDepthMetres, nameof(soilDepthMetres));
        SubstrateInput.UnitInterval(wetness, nameof(wetness)); SubstrateInput.UnitInterval(litter, nameof(litter));
        if (Soil == 0 && soilDepthMetres != 0)
            throw new FormationValidationException("A zero-soil influence cannot declare soil depth.", nameof(soilDepthMetres));
        var extent = new Vector3D(radiusMetres, radiusMetres, radiusMetres);
        Bounds = new FormationBounds(centre - extent, centre + extent);
        Id = id; TargetPiece = targetPiece; RequiredCapabilities = requiredCapabilities; Centre = centre;
        RadiusMetres = radiusMetres; Strength = strength; SoilDepthMetres = soilDepthMetres; Wetness = wetness; Litter = litter;
    }

    public SubstrateInfluenceId Id { get; }
    public FormationPieceId TargetPiece { get; }
    public string TargetPatchKey { get; }
    public SupportCapabilities RequiredCapabilities { get; }
    public Vector3D Centre { get; }
    public double RadiusMetres { get; }
    public double Strength { get; }
    public double Bedrock { get; }
    public double LooseStone { get; }
    public double Soil { get; }
    public double SoilDepthMetres { get; }
    public double Wetness { get; }
    public double Litter { get; }
    public FormationBounds Bounds { get; }

    internal double Weight(in SupportHit hit)
    {
        if (hit.Primitive.Piece != TargetPiece || !StringComparer.Ordinal.Equals(hit.PatchKey, TargetPatchKey) ||
            (hit.Capabilities & RequiredCapabilities) != RequiredCapabilities || !Bounds.Contains(hit.Point)) return 0;
        double distanceSquared = (hit.Point - Centre).LengthSquared;
        if (distanceSquared >= RadiusMetres * RadiusMetres) return 0;
        return Strength * (1 - Math.Sqrt(distanceSquared) / RadiusMetres);
    }
}

/// <summary>
/// Explicit raw environment inputs. Moisture, wetness and forest potential are normalized; temperature is Celsius.
/// LiquidEligible means compatible with this terrestrial ecology, not merely that some liquid exists.
/// Submerged terrestrial roots, incompatible liquid and an ineligible biosphere suppress plants and litter.
/// </summary>
public readonly record struct SubstrateInputs
{
    public SubstrateInputs(Vector3D radialUp, double temperatureC, double moisture, double wetness, double forestPotential,
        bool submerged, bool liquidEligible, bool biosphereEligible)
    {
        RadialUp = radialUp; TemperatureC = temperatureC; Moisture = moisture; Wetness = wetness;
        ForestPotential = forestPotential; Submerged = submerged; LiquidEligible = liquidEligible; BiosphereEligible = biosphereEligible;
        Validate();
    }
    public Vector3D RadialUp { get; }
    public double TemperatureC { get; }
    public double Moisture { get; }
    public double Wetness { get; }
    public double ForestPotential { get; }
    public bool Submerged { get; }
    public bool LiquidEligible { get; }
    public bool BiosphereEligible { get; }

    internal void Validate()
    {
        FormationInput.Unit(RadialUp, nameof(RadialUp));
        if (!double.IsFinite(TemperatureC)) throw new FormationValidationException("Temperature must be finite.", nameof(TemperatureC));
        SubstrateInput.UnitInterval(Moisture, nameof(Moisture)); SubstrateInput.UnitInterval(Wetness, nameof(Wetness));
        SubstrateInput.UnitInterval(ForestPotential, nameof(ForestPotential));
    }
}

/// <summary>Material fractions sum to one; wetness, litter and plant suitability are in [0,1]. Depth is a visual proxy in metres.</summary>
public readonly record struct SubstrateSample(double Bedrock, double LooseStone, double Soil, double SoilDepthMetres,
    double Wetness, double Litter, double PlantSuitability);

/// <summary>Grass is an occupied-area fraction; shrubs and trees are densities in instances per square metre.</summary>
public readonly record struct HabitatSample(double GrassCoverage, double ShrubsPerSquareMetre, double TreesPerSquareMetre);

public readonly record struct SubstrateEvaluation(SubstrateSample Substrate, HabitatSample Habitat, int AppliedInfluences);

public sealed record FormationSubstrateLimits
{
    public int MaxInfluences { get; init; } = 64;
    public long MaxEstimatedResidentBytes { get; init; } = 512L * 1024;
    public long MaxConstructionScratchBytes { get; init; } = 16L * 1024;
}

/// <summary>
/// Immutable bounded influence snapshot, not a query cache. Construction copies and sorts once; Evaluate uses only
/// local scalars, retains no hit/input, and allocates no arrays or collections. There are no terrain/support callbacks.
/// Estimates bound declared bookkeeping reservations, not measured managed-heap totals. The enclosing world/session
/// owns the snapshot and must include it in its resident and construction admission budget.
/// </summary>
public sealed class FormationSubstrate
{
    private readonly SubstrateInfluence[] _influences;

    public FormationSubstrate(SubstrateProfile profile, IEnumerable<SubstrateInfluence> influences, FormationSubstrateLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        limits ??= new FormationSubstrateLimits();
        if (limits.MaxInfluences < 0 || limits.MaxEstimatedResidentBytes < 0 || limits.MaxConstructionScratchBytes < 0)
            throw new FormationValidationException("Invalid substrate snapshot limits.", nameof(limits));
        // Reserve the configured maximum before reading an arbitrary enumerable. Keys are bounded by FormationInput.
        long residentReservation = 4096L * (1L + limits.MaxInfluences);
        long scratchReservation = 512L + 24L * limits.MaxInfluences;
        if (residentReservation > limits.MaxEstimatedResidentBytes || scratchReservation > limits.MaxConstructionScratchBytes)
            throw new FormationCapacityException("Substrate copy/sort reservation exceeds its byte budget.");
        _influences = FormationInput.Copy(influences, limits.MaxInfluences, nameof(influences));
        foreach (SubstrateInfluence influence in _influences)
            if (influence == null) throw new FormationValidationException("Substrate influences must be nonnull.", nameof(influences));
        Array.Sort(_influences, static (a, b) => a.Id.CompareTo(b.Id));
        for (int i = 1; i < _influences.Length; i++)
            if (_influences[i - 1].Id == _influences[i].Id)
                throw new FormationValidationException("Substrate influence keys must be unique.", nameof(influences));
        Profile = profile;
        EstimatedResidentBytes = 4096L * (1L + _influences.Length);
        ReservedConstructionScratchBytes = scratchReservation;
    }

    public SubstrateProfile Profile { get; }
    public ReadOnlySpan<SubstrateInfluence> Influences => _influences;
    public long EstimatedResidentBytes { get; }
    public long ReservedConstructionScratchBytes { get; }

    public SubstrateEvaluation Evaluate(in SupportHit hit, in SubstrateInputs inputs)
    {
        hit.Validate(); inputs.Validate();
        if (!StringComparer.Ordinal.Equals(hit.SubstrateProfileKey, Profile.Key))
            throw new FormationValidationException("The selected support has no matching substrate profile.", nameof(hit));
        double bedrock = Profile.Bedrock, loose = Profile.LooseStone, soil = Profile.Soil;
        double depth = Profile.SoilDepthMetres, wetness = Math.Max(inputs.Wetness, inputs.Moisture), litter = Profile.Litter;
        int applied = 0;
        foreach (SubstrateInfluence influence in _influences)
        {
            double weight = influence.Weight(hit);
            if (weight <= 0) continue;
            applied++;
            bedrock = Blend(bedrock, influence.Bedrock, weight);
            loose = Blend(loose, influence.LooseStone, weight);
            soil = Blend(soil, influence.Soil, weight);
            depth = Blend(depth, influence.SoilDepthMetres, weight);
            wetness = Math.Max(wetness, influence.Wetness * weight);
            litter = Blend(litter, influence.Litter, weight);
        }
        double upDot = Math.Clamp(Vector3D.Dot(hit.Normal, inputs.RadialUp), -1, 1);
        double soilSupport = (hit.Capabilities & SupportCapabilities.Soil) == 0 ? 0
            : Math.Clamp((upDot - Profile.MinimumSoilUpDot) / (1 - Profile.MinimumSoilUpDot), 0, 1);
        double retainedSoil = soil * soilSupport;
        bedrock += soil - retainedSoil; soil = retainedSoil;
        (bedrock, loose, soil) = SubstrateInput.Fractions(bedrock, loose, soil);
        depth = soil > 0 ? depth * soilSupport : 0;
        double temperaturePosition = (inputs.TemperatureC - Profile.MinimumTemperatureC) /
            (Profile.MaximumTemperatureC - Profile.MinimumTemperatureC);
        // A declared triangular temperature response: zero at/beyond endpoints, one at their midpoint.
        double thermal = Math.Clamp(1 - Math.Abs(2 * temperaturePosition - 1), 0, 1);
        bool ecology = inputs.BiosphereEligible && inputs.LiquidEligible && !inputs.Submerged;
        bool plantable = ecology && (hit.Capabilities & SupportCapabilities.Plantable) != 0 && depth > 0;
        double suitability = plantable ? Math.Clamp(Profile.PlantSuitability * soil * wetness * thermal, 0, 1) : 0;
        litter = ecology ? Math.Clamp(litter * inputs.ForestPotential * soilSupport, 0, 1) : 0;
        var substrate = new SubstrateSample(bedrock, loose, soil, depth, wetness, litter, suitability);
        var habitat = new HabitatSample(Profile.GrassCoverage * suitability * (1 - 0.75 * inputs.ForestPotential),
            Profile.ShrubsPerSquareMetre * suitability * (0.3 + 0.7 * inputs.ForestPotential),
            Profile.TreesPerSquareMetre * suitability * inputs.ForestPotential);
        return new SubstrateEvaluation(substrate, habitat, applied);
    }

    private static double Blend(double from, double to, double amount) => from + (to - from) * amount;
}

internal static class SubstrateInput
{
    internal static void Nonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0) throw new FormationValidationException("A finite nonnegative value is required.", name);
    }
    internal static void UnitInterval(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1) throw new FormationValidationException("A finite value in [0,1] is required.", name);
    }
    internal static (double Bedrock, double LooseStone, double Soil) Fractions(double bedrock, double loose, double soil)
    {
        Nonnegative(bedrock, nameof(bedrock)); Nonnegative(loose, nameof(loose)); Nonnegative(soil, nameof(soil));
        double largest = Math.Max(bedrock, Math.Max(loose, soil));
        if (largest == 0) throw new FormationValidationException("Substrate needs at least one positive material weight.");
        bedrock /= largest; loose /= largest; soil /= largest;
        double sum = bedrock + loose + soil;
        return (bedrock / sum, loose / sum, soil / sum);
    }
}
