using System;
using System.Collections.Generic;

namespace Hollow.Surface;

/// <summary>One explicitly referenced geometry alternative and the contact domains required by its role.</summary>
public sealed class FormationGeometryChoice
{
    private readonly string[] _contactKeys;
    public FormationGeometryChoice(string key, int revision, IEnumerable<string> contactKeys, double minimumScale, double maximumScale)
    {
        Key = FormationInput.Key(key, nameof(key));
        if (revision < 0 || !double.IsFinite(minimumScale) || !double.IsFinite(maximumScale) || minimumScale <= 0 || maximumScale < minimumScale)
            throw new FormationValidationException("Invalid geometry revision or scale interval.");
        Revision = revision; MinimumScale = minimumScale; MaximumScale = maximumScale;
        _contactKeys = FormationInput.Copy(contactKeys, 8, nameof(contactKeys));
        if (_contactKeys.Length == 0) throw new FormationValidationException("A geometry choice must declare its required contacts.");
        for (int i = 0; i < _contactKeys.Length; i++) FormationInput.Key(_contactKeys[i], nameof(contactKeys));
        Array.Sort(_contactKeys, StringComparer.Ordinal);
        for (int i = 1; i < _contactKeys.Length; i++)
            if (_contactKeys[i - 1] == _contactKeys[i]) throw new FormationValidationException("Duplicate required contact key.");
    }
    public string Key { get; }
    public int Revision { get; }
    public ReadOnlySpan<string> ContactKeys => _contactKeys;
    public double MinimumScale { get; }
    public double MaximumScale { get; }
    public long EstimatedResidentBytes => 1024L + 512L * _contactKeys.Length;
}

/// <summary>A preassigned slot. Role is a handler key, not an asset-kind branch or acceptance-order ordinal.</summary>
public sealed class FormationRecipeSlot
{
    private readonly FormationGeometryChoice[] _alternatives;
    public FormationRecipeSlot(int slot, string role, bool required, IEnumerable<FormationGeometryChoice> alternatives)
    {
        if (slot < 0 || slot >= FormationRecord.MaximumSlots) throw new FormationValidationException("Recipe slot is outside the physical slot namespace.");
        Slot = slot; Role = FormationInput.Key(role, nameof(role)); Required = required;
        _alternatives = FormationInput.Copy(alternatives, 5, nameof(alternatives));
        if (_alternatives.Length == 0) throw new FormationValidationException("A recipe slot needs a geometry alternative.");
        foreach (FormationGeometryChoice choice in _alternatives)
            if (choice == null) throw new FormationValidationException("Geometry alternatives cannot be null.");
        Array.Sort(_alternatives, static (a, b) =>
        { int order = StringComparer.Ordinal.Compare(a.Key, b.Key); return order != 0 ? order : a.Revision.CompareTo(b.Revision); });
        for (int i = 1; i < _alternatives.Length; i++)
            if (_alternatives[i - 1].Key == _alternatives[i].Key && _alternatives[i - 1].Revision == _alternatives[i].Revision)
                throw new FormationValidationException("Duplicate geometry alternative.");
        long bytes = 1024;
        foreach (FormationGeometryChoice choice in _alternatives) bytes += choice.EstimatedResidentBytes;
        EstimatedResidentBytes = bytes;
    }
    public int Slot { get; }
    public string Role { get; }
    public bool Required { get; }
    public ReadOnlySpan<FormationGeometryChoice> Alternatives => _alternatives;
    public long EstimatedResidentBytes { get; }
}

/// <summary>Validated explicit planner inputs only. This class neither chooses production tuning nor runs a planner.</summary>
public sealed class FormationRecipe
{
    private readonly FormationRecipeSlot[] _slots;
    public FormationRecipe(string key, int revision, ushort identityDomain, string familyKey, IEnumerable<FormationRecipeSlot> slots,
        double maximumSectionLengthMetres, double maximumExtentMetres, double minimumCoverage,
        int maximumGuideStations = 9, int maximumFitProbes = 3)
    {
        Key = FormationInput.Key(key, nameof(key)); FamilyKey = FormationInput.Key(familyKey, nameof(familyKey));
        if (revision < 0 || !double.IsFinite(maximumSectionLengthMetres) || maximumSectionLengthMetres <= 0 ||
            !double.IsFinite(maximumExtentMetres) || maximumExtentMetres <= 0 ||
            !double.IsFinite(minimumCoverage) || minimumCoverage < 0 || minimumCoverage > 1 ||
            maximumGuideStations < 2 || maximumGuideStations > FormationRecord.MaximumGuidePoints || maximumFitProbes < 1 || maximumFitProbes > 16)
            throw new FormationValidationException("Invalid recipe limits or coverage target.");
        Revision = revision; IdentityDomain = identityDomain; MaximumSectionLengthMetres = maximumSectionLengthMetres;
        MaximumExtentMetres = maximumExtentMetres; MinimumCoverage = minimumCoverage;
        MaximumGuideStations = maximumGuideStations; MaximumFitProbes = maximumFitProbes;
        _slots = FormationInput.Copy(slots, 12, nameof(slots));
        if (_slots.Length == 0) throw new FormationValidationException("A recipe needs at least one slot.");
        foreach (FormationRecipeSlot slot in _slots)
            if (slot == null) throw new FormationValidationException("Recipe slots cannot be null.");
        Array.Sort(_slots, static (a, b) => a.Slot.CompareTo(b.Slot));
        long bytes = 1536;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (i > 0 && _slots[i - 1].Slot == _slots[i].Slot) throw new FormationValidationException("Recipe slots must be unique.");
            bytes += _slots[i].EstimatedResidentBytes;
        }
        EstimatedResidentBytes = bytes;
    }
    public string Key { get; }
    public int Revision { get; }
    public ushort IdentityDomain { get; }
    public string FamilyKey { get; }
    public ReadOnlySpan<FormationRecipeSlot> Slots => _slots;
    public double MaximumSectionLengthMetres { get; }
    public double MaximumExtentMetres { get; }
    public double MinimumCoverage { get; }
    public int MaximumGuideStations { get; }
    public int MaximumFitProbes { get; }
    public long EstimatedResidentBytes { get; }
}
