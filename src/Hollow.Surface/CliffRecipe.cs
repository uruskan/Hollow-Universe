using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Hollow.Surface;

/// <summary>
/// A cliff formation recipe (docs/TERRAIN_SCENERY_PLAN.md T2, docs/LANDFORM_SURVEY.md): which scans a face takes,
/// how large the group is and how much of a gentle face stays soil. A face takes the recipe of what made it: the
/// coast, or its dominant relief. The recipes are data (Data/cliff-recipes.json), read once.
/// </summary>
public sealed class CliffRecipe
{
    private static readonly Lazy<(CliffRecipe[] Recipes, double CoastMetres)> Loaded = new(Read);

    /// <summary>The recipes in their file's order; the first whose <see cref="Where"/> matches a face is its recipe.</summary>
    public static IReadOnlyList<CliffRecipe> All => Loaded.Value.Recipes;

    /// <summary>A face within this many metres of the sea is a coastal face.</summary>
    public static double CoastMetres => Loaded.Value.CoastMetres;

    /// <summary>The coastal recipe's setting name in <see cref="Where"/>.</summary>
    public const string Coast = "coast";

    public string Key { get; }

    /// <summary>Settings this recipe dresses: <see cref="Coast"/> or relief names (<see cref="Relief"/>).</summary>
    public IReadOnlyList<string> Where { get; }

    public double MinimumScale { get; }
    public double MaximumScale { get; }

    /// <summary>
    /// The share of a face's ground meant to carry rock: <see cref="GentleRockShare"/> at the face grade, rising to
    /// <see cref="RockShare"/> by grade 1.2 (50°); walls always take rock, and the rest stays soil and scree.
    /// </summary>
    public double RockShare { get; }

    public double GentleRockShare { get; }

    /// <summary>A typical scan's height and width before scale, metres: the tiers' and columns' spacing.</summary>
    public double TypicalHeight { get; }

    public double TypicalWidth { get; }

    private readonly Dictionary<string, double> _families;

    private CliffRecipe(string key, string[] where, Dictionary<string, double> families, double minimumScale,
        double maximumScale, double rockShare, double gentleRockShare, double typicalHeight, double typicalWidth)
    {
        if (string.IsNullOrWhiteSpace(key) || where.Length == 0 || families.Count == 0 ||
            !double.IsFinite(minimumScale) || minimumScale <= 0 || !double.IsFinite(maximumScale) || maximumScale < minimumScale ||
            !double.IsFinite(rockShare) || rockShare <= 0 || rockShare > 1 ||
            !double.IsFinite(gentleRockShare) || gentleRockShare < 0 || gentleRockShare > rockShare ||
            !double.IsFinite(typicalHeight) || typicalHeight <= 0 || !double.IsFinite(typicalWidth) || typicalWidth <= 0)
            throw new InvalidOperationException("Invalid cliff recipe: " + key);
        foreach (double weight in families.Values)
            if (!double.IsFinite(weight) || weight <= 0) throw new InvalidOperationException("Invalid family weight in cliff recipe " + key);
        (Key, Where, _families) = (key, where, families);
        (MinimumScale, MaximumScale, RockShare, GentleRockShare) = (minimumScale, maximumScale, rockShare, gentleRockShare);
        (TypicalHeight, TypicalWidth) = (typicalHeight, typicalWidth);
    }

    /// <summary>The share of ground at this grade meant to carry rock (walls are dressed regardless).</summary>
    public double RockShareAt(double gradient, double faceGrade) =>
        GentleRockShare + (RockShare - GentleRockShare) * Math.Clamp((gradient - faceGrade) / (1.2 - faceGrade), 0, 1);

    /// <summary>The weight a scan's material family has in this recipe; zero when the recipe does not use it.</summary>
    public double FamilyWeight(string family) => _families.TryGetValue(family, out double weight) ? weight : 0;

    /// <summary>The recipe for a face: the coastal one near the sea, otherwise the first for its relief.</summary>
    public static CliffRecipe For(bool coast, Relief relief)
    {
        string setting = coast ? Coast : relief.ToString();
        foreach (CliffRecipe recipe in All)
            foreach (string where in recipe.Where)
                if (where == setting) return recipe;
        throw new InvalidOperationException("No cliff recipe dresses " + setting);
    }

    private static (CliffRecipe[], double) Read()
    {
        using Stream data = typeof(CliffRecipe).Assembly.GetManifestResourceStream("Hollow.Surface.Data.cliff-recipes.json")
            ?? throw new InvalidOperationException("Cliff recipe data is missing.");
        using JsonDocument doc = JsonDocument.Parse(data);
        JsonElement root = doc.RootElement;
        double coastMetres = root.GetProperty("coastMetres").GetDouble();
        if (!double.IsFinite(coastMetres) || coastMetres <= 0) throw new InvalidOperationException("Invalid cliff coast distance.");
        var recipes = new List<CliffRecipe>();
        foreach (JsonElement row in root.GetProperty("recipes").EnumerateArray())
        {
            var settings = new List<string>();
            foreach (JsonElement where in row.GetProperty("where").EnumerateArray()) settings.Add(where.GetString()!);
            var families = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (JsonProperty family in row.GetProperty("families").EnumerateObject()) families[family.Name] = family.Value.GetDouble();
            JsonElement scale = row.GetProperty("scale");
            recipes.Add(new CliffRecipe(row.GetProperty("key").GetString()!, settings.ToArray(), families, scale[0].GetDouble(), scale[1].GetDouble(),
                row.GetProperty("rockShare").GetDouble(), row.GetProperty("gentleRockShare").GetDouble(),
                row.GetProperty("typicalHeight").GetDouble(),
                row.GetProperty("typicalWidth").GetDouble()));
        }
        // Every setting a face can have is dressed, and every family a recipe names exists among the scans.
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (CliffGeometry scan in CliffGeometry.All) known.Add(scan.MaterialFamily);
        foreach (CliffRecipe recipe in recipes)
            foreach (string family in recipe._families.Keys)
                if (!known.Contains(family)) throw new InvalidOperationException($"Cliff recipe {recipe.Key} names unknown family {family}");
        foreach (string setting in new[] { Coast, nameof(Relief.Plains), nameof(Relief.Hills), nameof(Relief.Tableland), nameof(Relief.Mountains) })
        {
            bool dressed = false;
            foreach (CliffRecipe recipe in recipes)
                foreach (string where in recipe.Where)
                    dressed |= where == setting;
            if (!dressed) throw new InvalidOperationException("No cliff recipe dresses " + setting);
        }
        return (recipes.ToArray(), coastMetres);
    }
}
