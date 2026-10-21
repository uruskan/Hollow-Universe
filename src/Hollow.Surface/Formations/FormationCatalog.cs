using System;
using System.Collections.Generic;

namespace Hollow.Surface;

public sealed record FormationCatalogLimits
{
    public int MaxDefinitions { get; init; } = 64;
    public int MaxProfiles { get; init; } = 128;
    public int MaxRecipes { get; init; } = 128;
    public long MaxEstimatedResidentBytes { get; init; } = 64L * 1024 * 1024;
    public long MaxConstructionScratchBytes { get; init; } = 256L * 1024;
}

/// <summary>
/// A bounded, immutable catalog over explicitly supplied qualified definitions, profiles and recipes.
/// It performs no loading or generation. All references resolve before the catalog is published.
/// Revisions of one recipe key share one identity domain; different keys cannot share that domain.
/// </summary>
public sealed class FormationCatalog
{
    private readonly CliffSupportDefinition[] _qualified;
    private readonly SubstrateProfile[] _profiles;
    private readonly FormationRecipe[] _recipes;

    public FormationCatalog(IEnumerable<CliffSupportDefinition> qualified, IEnumerable<SubstrateProfile> profiles,
        IEnumerable<FormationRecipe> recipes, FormationCatalogLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(qualified);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(recipes);
        limits ??= new FormationCatalogLimits();
        if (limits.MaxDefinitions < 0 || limits.MaxProfiles < 0 || limits.MaxRecipes < 0 ||
            limits.MaxEstimatedResidentBytes < 0 || limits.MaxConstructionScratchBytes < 0)
            throw new FormationValidationException("Invalid formation catalog limits.", nameof(limits));
        long maximumEntries = (long)limits.MaxDefinitions + limits.MaxProfiles + limits.MaxRecipes;
        long scratch = 4096L + 24L * maximumEntries;
        if (scratch > limits.MaxConstructionScratchBytes || 1024L + 8L * maximumEntries > limits.MaxEstimatedResidentBytes)
            throw new FormationCapacityException("Catalog copy/sort reservation exceeds its resident or scratch budget.");

        _qualified = FormationInput.Copy(qualified, limits.MaxDefinitions, nameof(qualified));
        _profiles = FormationInput.Copy(profiles, limits.MaxProfiles, nameof(profiles));
        _recipes = FormationInput.Copy(recipes, limits.MaxRecipes, nameof(recipes));
        long resident = 1024L + 8L * ((long)_qualified.Length + _profiles.Length + _recipes.Length);
        foreach (CliffSupportDefinition definition in _qualified)
        {
            if (definition == null) throw new FormationValidationException("Qualified definitions cannot be null.", nameof(qualified));
            AddResident(ref resident, definition.EstimatedResidentBytes, limits.MaxEstimatedResidentBytes);
        }
        foreach (SubstrateProfile profile in _profiles)
        {
            if (profile == null) throw new FormationValidationException("Substrate profiles cannot be null.", nameof(profiles));
            AddResident(ref resident, 4096, limits.MaxEstimatedResidentBytes);
        }
        foreach (FormationRecipe recipe in _recipes)
        {
            if (recipe == null) throw new FormationValidationException("Formation recipes cannot be null.", nameof(recipes));
            AddResident(ref resident, recipe.EstimatedResidentBytes, limits.MaxEstimatedResidentBytes);
        }

        Array.Sort(_qualified, static (a, b) => Compare(a.Key, a.Revision, b.Key, b.Revision));
        Array.Sort(_profiles, static (a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
        Array.Sort(_recipes, static (a, b) => Compare(a.Key, a.Revision, b.Key, b.Revision));
        for (int i = 0; i < _qualified.Length; i++)
        {
            CliffSupportDefinition definition = _qualified[i];
            if (i > 0 && Compare(_qualified[i - 1].Key, _qualified[i - 1].Revision, definition.Key, definition.Revision) == 0)
                throw new FormationValidationException("Duplicate qualified geometry key/revision.", nameof(qualified));
            foreach (SupportPatch patch in definition.Geometry.Patches) _ = RequireProfile(patch.SubstrateProfileKey);
        }
        for (int i = 1; i < _profiles.Length; i++)
            if (_profiles[i - 1].Key == _profiles[i].Key)
                throw new FormationValidationException("Duplicate substrate profile key.", nameof(profiles));
        for (int i = 0; i < _recipes.Length; i++)
        {
            FormationRecipe recipe = _recipes[i];
            if (i > 0 && Compare(_recipes[i - 1].Key, _recipes[i - 1].Revision, recipe.Key, recipe.Revision) == 0)
                throw new FormationValidationException("Duplicate formation recipe key/revision.", nameof(recipes));
            for (int j = 0; j < i; j++)
            {
                FormationRecipe prior = _recipes[j];
                if ((prior.Key == recipe.Key) != (prior.IdentityDomain == recipe.IdentityDomain))
                    throw new FormationValidationException("A recipe key and identity domain must have a one-to-one association.", nameof(recipes));
            }
            foreach (FormationRecipeSlot slot in recipe.Slots)
                foreach (FormationGeometryChoice choice in slot.Alternatives)
                {
                    CliffSupportDefinition definition = RequireQualified(choice.Key, choice.Revision);
                    if (definition.FamilyKey != recipe.FamilyKey || choice.MinimumScale < definition.MinimumScale ||
                        choice.MaximumScale > definition.MaximumScale)
                        throw new FormationValidationException("Recipe geometry family or scale exceeds its qualification.", nameof(recipes));
                    foreach (string contact in choice.ContactKeys) _ = definition.RequireContact(contact);
                }
        }
        EstimatedResidentBytes = resident;
        ReservedConstructionScratchBytes = scratch;
    }

    public ReadOnlySpan<CliffSupportDefinition> Qualified => _qualified;
    public ReadOnlySpan<SubstrateProfile> Profiles => _profiles;
    public ReadOnlySpan<FormationRecipe> Recipes => _recipes;
    /// <summary>Conservative retained-content estimate, including referenced definitions and recipes even when shared.</summary>
    public long EstimatedResidentBytes { get; }
    /// <summary>Catalog copy/sort scratch only; construction of caller-supplied definitions has its own admission.</summary>
    public long ReservedConstructionScratchBytes { get; }

    public CliffSupportDefinition RequireQualified(string key, int revision)
    {
        ValidateIdentity(key, revision);
        int low = 0, high = _qualified.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            CliffSupportDefinition value = _qualified[middle];
            int comparison = Compare(value.Key, value.Revision, key, revision);
            if (comparison == 0) return value;
            if (comparison < 0) low = middle + 1; else high = middle - 1;
        }
        throw new FormationValidationException("Required qualified geometry is absent: " + key + "/" + revision);
    }

    public SubstrateProfile RequireProfile(string key)
    {
        FormationInput.Key(key, nameof(key));
        int low = 0, high = _profiles.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            SubstrateProfile value = _profiles[middle];
            int comparison = StringComparer.Ordinal.Compare(value.Key, key);
            if (comparison == 0) return value;
            if (comparison < 0) low = middle + 1; else high = middle - 1;
        }
        throw new FormationValidationException("Required substrate profile is absent: " + key);
    }

    public FormationRecipe RequireRecipe(string key, int revision)
    {
        ValidateIdentity(key, revision);
        int low = 0, high = _recipes.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            FormationRecipe value = _recipes[middle];
            int comparison = Compare(value.Key, value.Revision, key, revision);
            if (comparison == 0) return value;
            if (comparison < 0) low = middle + 1; else high = middle - 1;
        }
        throw new FormationValidationException("Required formation recipe is absent: " + key + "/" + revision);
    }

    private static int Compare(string a, int revisionA, string b, int revisionB)
    {
        int key = StringComparer.Ordinal.Compare(a, b);
        return key != 0 ? key : revisionA.CompareTo(revisionB);
    }

    private static void ValidateIdentity(string key, int revision)
    {
        FormationInput.Key(key, nameof(key));
        if (revision < 0) throw new FormationValidationException("A catalog revision must be nonnegative.", nameof(revision));
    }

    private static void AddResident(ref long total, long amount, long maximum)
    {
        if (amount < 0 || amount > maximum - total)
            throw new FormationCapacityException("Catalog retained content exceeds its resident byte budget.");
        total += amount;
    }
}
