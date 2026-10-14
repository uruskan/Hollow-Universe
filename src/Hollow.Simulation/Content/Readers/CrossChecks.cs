using System;
using System.Collections.Generic;
using System.Linq;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// The checks across the files, last (GROUND.md §7): every recipe can be made from what can be gathered. Starting from
/// what natural objects yield, what animals' corpses give and what the world's barrels and crates hold, a recipe is
/// makeable once its ingredients are, and its workbench level is (a workbench is an item too); a device cooks or burns
/// things once the device itself can be made, and the recycler turns items into others.
/// </summary>
internal sealed class CrossChecks : IContentReader
{
    /// <summary>No file of its own: the step across them all.</summary>
    public string File => "";

    public void Read(ContentReading r)
    {
        // Nothing to read: every file is read by its own reader.
    }

    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        var obtainable = new HashSet<ItemDef>();
        foreach (NaturalKind k in loaded.Natural)
        {
            foreach (ItemAmount y in k.Yield)
            {
                obtainable.Add(y.Item);
            }
        }
        // G7: what animals' corpses give (animals.json) is gathered too.
        foreach (AnimalSpecies species in loaded.Animals)
        {
            foreach (ItemAmount y in species.Harvest)
            {
                obtainable.Add(y.Item);
            }
        }
        // G7: what the world's barrels and crates hold is found, not made; the recycler turns items into others.
        foreach (PropDef prop in loaded.World?.Props ?? Array.Empty<PropDef>())
        {
            foreach (LootTable.Entry e in prop.Loot is { } table ? table.Guaranteed.Concat(table.Entries) : Array.Empty<LootTable.Entry>())
            {
                if (e.Tier < 0)
                {
                    obtainable.Add(e.Item);
                }
            }
        }
        bool grew = true;
        while (grew)
        {
            grew = false;
            int bench = loaded.Deployables.Where(d => d.Workbench > 0 && obtainable.Contains(d.Item)).Select(d => d.Workbench).DefaultIfEmpty(0).Max();
            var devices = loaded.Deployables.Where(d => d.Device != null && obtainable.Contains(d.Item)).Select(d => d.Device!).ToList();
            foreach (Recipe r in loaded.Recipes)
            {
                if (r.Workbench <= bench && r.Ingredients.All(i => obtainable.Contains(i.Item)) &&
                    (r.Station == null || obtainable.Contains(r.Station.Item)))
                {
                    grew |= obtainable.Add(r.Output);
                }
            }
            foreach (ItemDef item in obtainable.ToList())
            {
                if (item.Cook?.Into is { } into && devices.Any(d => d.Cooks == item.Cook.In))
                {
                    grew |= obtainable.Add(into);
                }
                if (item.Fuel?.Byproduct is { } byproduct && devices.Count > 0)
                {
                    grew |= obtainable.Add(byproduct);
                }
                if (loaded.World is { RecyclerCount: > 0 } world && world.FixedYield(item.Id) is { } yields)
                {
                    foreach (ItemAmount y in yields)
                    {
                        grew |= obtainable.Add(y.Item);
                    }
                }
            }
        }
        foreach (Recipe r in loaded.Recipes.Where(r => !obtainable.Contains(r.Output)))
        {
            string missing = string.Join(", ", r.Ingredients.Where(i => !obtainable.Contains(i.Item)).Select(i => i.Item.Key));
            c.Problem($"recipes.json: \"{r.Output.Key}\"", missing.Length > 0
                ? $"can't be made from gatherable resources (nothing gives {missing})"
                : r.Station != null && !obtainable.Contains(r.Station.Item)
                ? $"needs a {r.Station.Key}, which can't be made"
                : $"needs workbench {r.Workbench}, which can't be made");
        }
    }
}
