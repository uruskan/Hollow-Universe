using System;
using System.Threading;

namespace Hollow.Surface;

/// <summary>
/// Explicit immutable selection for a formation-capable world factory. No package is loaded and no terrain is
/// generated here. The factory must implement TerrainContract and consume these exact inputs after final grading.
/// Null selection preserves the existing world. A selection is never permission to substitute legacy geometry.
/// </summary>
public sealed class FormationWorldSelection
{
    private FormationWorldSelection(string profileId, string terrainContract, FormationCatalog catalog,
        FormationPlannerSettings settings, FormationAlgorithmRevisions revisions, FormationGenerationIdentity identity,
        FormationFieldContract? fieldContract)
    {
        ProfileId = profileId; TerrainContract = terrainContract; Catalog = catalog; Settings = settings;
        Revisions = revisions; Identity = identity; FieldContract = fieldContract;
        EstimatedResidentBytes = checked(4096L + catalog.EstimatedResidentBytes + identity.EstimatedRetainedBytes +
            (settings.Region?.Diagnostics.EstimatedOwnedBytes ?? 0) + (fieldContract == null ? 0 : 1024));
    }

    public string ProfileId { get; }
    public string TerrainContract { get; }
    public FormationCatalog Catalog { get; }
    public FormationPlannerSettings Settings { get; }
    public FormationAlgorithmRevisions Revisions { get; }
    public FormationGenerationIdentity Identity { get; }
    public FormationFieldContract? FieldContract { get; }
    /// <summary>Conservative retained input estimate, including shared catalog content; not a heap measurement.</summary>
    public long EstimatedResidentBytes { get; }

    public static FormationWorldSelection Create(string profileId, string terrainContract, FormationCatalog catalog,
        FormationPlannerSettings settings, FormationAlgorithmRevisions? revisions = null,
        int maximumCanonicalBytes = 1024 * 1024, CancellationToken cancellation = default,
        FormationFieldContract? fieldContract = null)
    {
        cancellation.ThrowIfCancellationRequested();
        Key(profileId, nameof(profileId)); Key(terrainContract, nameof(terrainContract));
        if (!profileId.StartsWith("formation-", StringComparison.Ordinal))
            throw new FormationValidationException("An explicit formation profile must use the formation- namespace.");
        ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(settings);
        if (maximumCanonicalBytes < 1 || maximumCanonicalBytes > 4 * 1024 * 1024)
            throw new FormationValidationException("A selected world's canonical documents must fit within four MiB each.");
        FormationAlgorithmRevisions copied = (revisions ?? new()) with { };
        // Declarations are not implementation dispatch. Until another implementation exists it cannot be selected.
        if (copied != new FormationAlgorithmRevisions())
            throw new NotSupportedException("The requested formation implementation revisions are not available.");
        FormationGenerationIdentity identity = FormationGenerationIdentity.Create(catalog, settings, copied,
            maximumCanonicalBytes, cancellation, fieldContract: fieldContract);
        return new FormationWorldSelection(profileId, terrainContract, catalog, settings, copied, identity, fieldContract);
    }

    private static void Key(string value, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64)
            throw new FormationValidationException("Selected profile and terrain contract keys need 1–64 ASCII characters.", name);
        foreach (char c in value)
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '.'))
                throw new FormationValidationException("Selected keys use lowercase ASCII letters, digits, hyphens and dots.", name);
    }
}
