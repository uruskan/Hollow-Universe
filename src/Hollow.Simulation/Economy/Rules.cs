using System;
using System.Collections.Generic;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;

namespace Hollow.Simulation.Economy;

/// <summary>
/// The recycler's rules (GROUND.md §6.8, Rust's): a loot-only component gives its own list (world.json <c>recycle</c>);
/// anything crafted gives back about half of its recipe, scaled by its condition, fractions of a unit coming out as a
/// chance; raw materials, salvage and blueprints aren't recycled. One batch is a tenth of the item's stack size.
/// </summary>
public static class Recycling
{
    public static bool CanRecycle(GameContent content, ItemDef item) =>
        item != content.World.Currency && item != content.World.Blueprint &&
        (content.World.FixedYield(item.Id) != null || content.Recipe(item.Id) != null);

    /// <summary>How many units of a stack one batch takes.</summary>
    public static int BatchSize(RecyclerStats stats, ItemDef item, int count) =>
        Math.Min(count, Math.Max(1, (int)Math.Ceiling(item.StackSize * stats.Batch)));

    /// <summary>What recycling <paramref name="units"/> of a stack gives, added to <paramref name="into"/> (deterministic for the stream).</summary>
    public static void Yield(GameContent content, RecyclerStats stats, in ItemStack stack, int units, ref Rng rng, List<ItemAmount> into)
    {
        ItemDef? item = content.Item(stack.Item);
        if (item == null || units < 1)
        {
            return;
        }
        if (content.World.FixedYield(item.Id) is { } fixedYield)
        {
            foreach (ItemAmount y in fixedYield)
            {
                into.Add(new ItemAmount(y.Item, y.Count * units));
            }
            return;
        }
        if (content.Recipe(item.Id) is not { } recipe)
        {
            return;
        }
        // A worn tool or gun gives back as much as is left of it.
        double condition = item.HasCondition && stack.Condition > 0 ? Math.Clamp(stack.Condition / (double)item.MaxCondition, 0, 1) : 1;
        foreach (ItemAmount ingredient in recipe.Ingredients)
        {
            double amount = ingredient.Count * stats.Efficiency * condition * units / recipe.Amount;
            int whole = (int)Math.Floor(amount);
            if (rng.NextDouble() < amount - whole)
            {
                whole++;
            }
            if (whole > 0)
            {
                into.Add(new ItemAmount(ingredient.Item, whole));
            }
        }
    }
}

/// <summary>The research table's rules (GROUND.md §6.8, Rust's): an item whose recipe isn't known from the start, for salvage by its tier.</summary>
public static class Research
{
    public static bool CanResearch(GameContent content, ItemDef item) =>
        item != content.World.Blueprint && content.Recipe(item.Id) is { Known: false };

    /// <summary>The salvage researching an item costs (world.json <c>research.cost</c> by its tier).</summary>
    public static int Cost(GameContent content, Recipe recipe) => content.World.Research.CostFor(content.World.TierOf(recipe));
}

/// <summary>The tech trees' rules (GROUND.md §6.8, Rust's): a node is unlocked for its salvage at a workbench of its level, once one of the nodes before it is known.</summary>
public static class TechTrees
{
    public static ActionResult CanUnlock(TechNode node, BlueprintBook known)
    {
        if (known.Knows(node.Recipe))
        {
            return ActionResult.AlreadyKnown;
        }
        if (node.After.Count == 0)
        {
            return ActionResult.Accepted;
        }
        foreach (TechNode before in node.After)
        {
            if (known.Knows(before.Recipe))
            {
                return ActionResult.Accepted;
            }
        }
        return ActionResult.Locked;
    }

    /// <summary>The blueprint item for a recipe (its condition holds the recipe).</summary>
    public static ItemStack BlueprintOf(GameContent content, Recipe recipe) =>
        content.World.Blueprint is { } blueprint ? new ItemStack(blueprint.Id, 1, recipe.Id) : default;
}
