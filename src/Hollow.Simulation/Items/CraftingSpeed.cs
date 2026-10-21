using System;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Items;

/// <summary>
/// The workbench's speed (GROUND_SYSTEMS.md §7, the station capability's share of crafting): a job takes its recipe's time
/// times the factor for how many levels the best workbench in reach is above the recipe's (recipes.json
/// <c>workbenchSpeed</c>: Rust's one level above half, two or more a quarter), fixed when the job starts. A recipe made at a
/// station of its own (a vehicle at the vehicle workbench) counts the levels above the one its station is made at (the vehicle
/// workbench's level 2: a level 3 workbench in reach halves a car's time; the owner, 21 October 2026). The server and the
/// client's prediction and crafting panel use the same numbers.
/// </summary>
public static class CraftingSpeed
{
    /// <summary>The share of a recipe's time a job takes with a workbench of <paramref name="workbench"/> in reach (1: all of it).</summary>
    public static double Factor(GameContent content, Recipe recipe, int workbench)
    {
        if (content.WorkbenchSpeed.Count == 0)
        {
            return 1;
        }
        int needs = recipe.Station is { } station ? content.Recipe(station.Item.Id)?.Workbench ?? 0 : recipe.Workbench;
        int above = Math.Max(0, workbench - needs);
        return content.WorkbenchSpeed[Math.Min(above, content.WorkbenchSpeed.Count - 1)];
    }

    /// <summary>Ticks one unit takes there.</summary>
    public static int UnitTicks(GameContent content, Recipe recipe, int workbench) =>
        Math.Max(1, (int)Math.Round(recipe.UnitTicks * Factor(content, recipe, workbench)));

    /// <summary>Seconds one unit takes there (the crafting panel's).</summary>
    public static float Seconds(GameContent content, Recipe recipe, int workbench) => UnitTicks(content, recipe, workbench) / (float)CharacterTicks.Rate;
}
