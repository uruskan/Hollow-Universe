using Godot;
using Hollow.Client.Effects;

namespace HollowUniverse.Fx;

/// <summary>Data-backed renderer resource ceilings, separate from individual effect recipes.</summary>
public static class FxBudgetCatalog
{
    public const string Path = "res://data/ground/fx-budgets.json";
    private static EffectBudgetSettings? _settings;
    public static EffectBudgetSettings Settings => _settings ??= FileAccess.FileExists(Path)
        ? EffectBudgetSettings.Read(FileAccess.GetFileAsString(Path)) : new EffectBudgetSettings();
}
