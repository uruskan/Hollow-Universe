using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Presentation lookup key only; no reference to a generated world or mutation of its descriptor.</summary>
public readonly record struct FloraAppearanceWorldKey(ulong UniverseSeed, WorldAddress Address,
    EnvironmentSurfaceKind SurfaceKind, ulong SurfaceSeed)
{
    public static FloraAppearanceWorldKey From(PlanetEnvironmentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new(descriptor.UniverseSeed, descriptor.Address, descriptor.Kind, descriptor.SurfaceSeed);
    }
}

/// <summary>
/// Schema v1 admits only the identity palette. Future tint semantics need an explicit renderer/visual revision;
/// adding palette records must not multiply baked payloads or alter the package/input fingerprint.
/// </summary>
public sealed class FloraAppearancePalette
{
    private FloraAppearancePalette() { }
    public static FloraAppearancePalette Identity { get; } = new();
    public string Id => "identity-v1";
    public FloraAppearanceColour Multiplier => new(1, 1, 1);
}

/// <summary>Explicit immutable data mapping. No catch-all Earth entry and no modulo-current-catalog selection.</summary>
public sealed record FloraAppearanceWorldBinding(FloraAppearanceWorldKey World, string AppearanceId, string PaletteId);

public enum FloraAppearanceResolutionStatus : byte
{ Exact = 1, UnsupportedVisualVersion = 2, UnsupportedWorld = 3, UnsupportedCatalogIdentity = 4 }

/// <summary>Only a successful exact binding carries a pin. Schema v1 has no implicit fallback or runtime-bake request.</summary>
public sealed class FloraAppearanceResolution
{
    internal FloraAppearanceResolution(FloraAppearanceResolutionStatus status, FloraAppearancePin? pin,
        FloraAppearancePalette? palette, string visualVersion)
    { Status = status; Pin = pin; Palette = palette; VisualVersion = visualVersion; }
    public FloraAppearanceResolutionStatus Status { get; }
    public FloraAppearancePin? Pin { get; }
    public FloraAppearancePalette? Palette { get; }
    public string VisualVersion { get; }
}

/// <summary>
/// Finite frozen presentation catalog. Hosts supply approved pins and explicit mappings, and version any mapping change.
/// Does not construct a world, consume a placement RNG, read a package, start jobs, retain native resources or change saves.
/// No global registry permits a version to be mutated after publication. Persist the pinned catalog identity externally.
/// </summary>
public sealed class FloraAppearanceCatalog
{
    public const string EarthVisualVersion = "earth-legacy-appearance-v1";
    public const string EarthAppearanceId = "earth-legacy";
    public const int MaxAppearances = 64, MaxBindings = 4096;
    public static FloraAppearanceWorldKey EarthWorld { get; } = new(Galaxy.Galaxy.DefaultSeed,
        new WorldAddress(StartingWorld.System.Value, StartingWorld.BodyPath), EnvironmentSurfaceKind.Walking,
        Hash64.Of(StartingWorld.System.Value, StartingWorld.BodyPath));
    public static int EarthRecipeSeed => (int)(EarthWorld.SurfaceSeed & 0x7FFFFFFF);

    private readonly Dictionary<string, FloraAppearancePin> _appearances;
    private readonly Dictionary<FloraAppearanceWorldKey, FloraAppearanceWorldBinding> _bindings;

    public FloraAppearanceCatalog(string visualVersion, IReadOnlyList<FloraAppearancePin> appearances,
        IReadOnlyList<FloraAppearanceWorldBinding> bindings)
    {
        VisualVersion = FloraAppearanceCheck.Id(visualVersion);
        ArgumentNullException.ThrowIfNull(appearances); ArgumentNullException.ThrowIfNull(bindings);
        int appearanceCount = appearances.Count, bindingCount = bindings.Count;
        if (appearanceCount is < 1 or > MaxAppearances || bindingCount is < 1 or > MaxBindings)
            throw new FloraAppearanceCapacityException("Flora catalog count bound exceeded.");
        _appearances = new Dictionary<string, FloraAppearancePin>(StringComparer.Ordinal);
        for (int i = 0; i < appearanceCount; i++)
        {
            FloraAppearancePin pin = appearances[i] ?? throw new FloraAppearanceValidationException("Null appearance pin.");
            if (!_appearances.TryAdd(pin.AppearanceId, pin)) throw new FloraAppearanceValidationException("Duplicate appearance ID.");
        }
        _bindings = new Dictionary<FloraAppearanceWorldKey, FloraAppearanceWorldBinding>();
        for (int i = 0; i < bindingCount; i++)
        {
            FloraAppearanceWorldBinding binding = bindings[i] ?? throw new FloraAppearanceValidationException("Null world binding.");
            FloraAppearanceCheck.Id(binding.AppearanceId); FloraAppearanceCheck.Id(binding.PaletteId);
            if (!_appearances.ContainsKey(binding.AppearanceId) || binding.PaletteId != FloraAppearancePalette.Identity.Id ||
                !Enum.IsDefined(binding.World.SurfaceKind) || !_bindings.TryAdd(binding.World, binding))
                throw new FloraAppearanceValidationException("Invalid, duplicate or unsupported flora world binding.");
        }
        Appearances = Array.AsReadOnly(_appearances.Values.OrderBy(p => p.AppearanceId, StringComparer.Ordinal).ToArray());
        Bindings = Array.AsReadOnly(_bindings.Values.OrderBy(b => b.World.UniverseSeed).ThenBy(b => b.World.Address.SystemAddress)
            .ThenBy(b => b.World.Address.BodyPath).ThenBy(b => b.World.SurfaceKind).ThenBy(b => b.World.SurfaceSeed).ToArray());
        // The mapping and external manifest/input pins are frozen together; same version label with different data is
        // detectably different. This digest is never injected into physical generation or used as a random seed.
        using var stream = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write("hollow-flora-appearance-catalog-v1"); writer.Write(VisualVersion);
        writer.Write(Appearances.Count);
        foreach (FloraAppearancePin pin in Appearances)
        { writer.Write(pin.AppearanceId); writer.Write(pin.Revision); writer.Write(pin.ManifestSha256); writer.Write(pin.ExpectedInputs.Fingerprint); }
        writer.Write(FloraAppearancePalette.Identity.Id); writer.Write(Bindings.Count);
        foreach (FloraAppearanceWorldBinding binding in Bindings)
        {
            writer.Write(binding.World.UniverseSeed); writer.Write(binding.World.Address.SystemAddress);
            writer.Write(binding.World.Address.BodyPath); writer.Write((byte)binding.World.SurfaceKind);
            writer.Write(binding.World.SurfaceSeed); writer.Write(binding.AppearanceId); writer.Write(binding.PaletteId);
        }
        writer.Flush();
        Identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
    public string VisualVersion { get; }
    public string Identity { get; }
    public IReadOnlyList<FloraAppearancePin> Appearances { get; }
    public IReadOnlyList<FloraAppearanceWorldBinding> Bindings { get; }

    /// <summary>Only the default-universe hosted walking Earth is supported by this first mapping.</summary>
    public static FloraAppearanceCatalog EarthLegacy(FloraAppearancePin approvedPin)
    {
        ArgumentNullException.ThrowIfNull(approvedPin);
        if (approvedPin.AppearanceId != EarthAppearanceId || approvedPin.Revision != 1 ||
            approvedPin.ExpectedInputs.RecipeSeed != EarthRecipeSeed)
            throw new FloraAppearanceValidationException("Earth legacy appearance requires its approved ID/revision and hosted surface recipe seed.");
        return new FloraAppearanceCatalog(EarthVisualVersion, new[] { approvedPin },
            new[] { new FloraAppearanceWorldBinding(EarthWorld, EarthAppearanceId, FloraAppearancePalette.Identity.Id) });
    }

    /// <summary>Both externally pinned version and mapping digest must agree before any world can resolve.</summary>
    public FloraAppearanceResolution Resolve(FloraAppearanceWorldKey world, string visualVersion, string expectedCatalogIdentity)
    {
        if (!string.Equals(visualVersion, VisualVersion, StringComparison.Ordinal))
            return new(FloraAppearanceResolutionStatus.UnsupportedVisualVersion, null, null, VisualVersion);
        if (!string.Equals(expectedCatalogIdentity, Identity, StringComparison.OrdinalIgnoreCase))
            return new(FloraAppearanceResolutionStatus.UnsupportedCatalogIdentity, null, null, VisualVersion);
        if (!_bindings.TryGetValue(world, out FloraAppearanceWorldBinding? binding))
            return new(FloraAppearanceResolutionStatus.UnsupportedWorld, null, null, VisualVersion);
        return new(FloraAppearanceResolutionStatus.Exact, _appearances[binding.AppearanceId], FloraAppearancePalette.Identity, VisualVersion);
    }
}
