using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// recipes.json: every recipe (output, amount, seconds, workbench level, ingredients, known from the start, its
/// station) and <c>workbenchSpeed</c>, how much faster a better workbench makes it (GROUND_SYSTEMS.md §7). A recipe's
/// number is its output's; a second way to make an item (G7) has a number and a key of its own. The stations are
/// linked by the deployables' reader.
/// </summary>
internal sealed class RecipesReader : IContentReader
{
    public string File => "recipes.json";

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        JsonElement root = r.Root("recipes.json", "workbenchSpeed", "recipes");
        if (f.TryChild(root, "workbenchSpeed", out JsonElement speed))
        {
            const string where = "recipes.json: workbenchSpeed";
            f.OnlyKnown(speed, where, "note", "byLevelsAbove");
            var factors = f.List(speed, "byLevelsAbove", where).Select(e => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : double.NaN).ToList();
            if (factors.Count == 0 || factors[0] != 1 || factors.Any(x => !(x > 0 && x <= 1)) || factors.Zip(factors.Skip(1)).Any(p => p.Second > p.First))
            {
                f.Problem(where, "byLevelsAbove starts at 1 and only falls, every factor in (0, 1]");
            }
            else
            {
                loaded.WorkbenchSpeed = factors;
            }
        }
        JsonElement[] list = f.List(root, "recipes", "recipes.json");
        for (int i = 0; i < list.Length; i++)
        {
            JsonElement e = list[i];
            string output = f.Text(e, "output", $"recipes.json: recipes[{i}]");
            string where = $"recipes.json: {JsonFields.Quote(output)}";
            f.OnlyKnown(e, where, "output", "amount", "seconds", "workbench", "ingredients", "known", "station", "id", "key");
            ItemDef? item = r.Resolve(output, where);
            var ingredients = new List<ItemAmount>();
            foreach (JsonElement ingredient in f.List(e, "ingredients", where))
            {
                if (f.Tuple(ingredient, where + ".ingredients", 1, out string key, out double[] values) &&
                    r.Resolve(key, where + ".ingredients") is { } input)
                {
                    ingredients.Add(new ItemAmount(input, (int)values[0]));
                }
            }
            if (item == null)
            {
                continue;
            }
            // A second way to make an item (G7) has a number and a key of its own; the first is its output's.
            bool own = f.Has(e, "id") || f.Has(e, "key");
            int id = own ? f.Integer(e, "id", where) : item.Id;
            if (id is < 1 or > ushort.MaxValue)
            {
                f.Problem(where, $"id {id} must be 1 … 65535");
            }
            loaded.Recipes.Add(new Recipe
            {
                Output = item,
                Amount = f.Integer(e, "amount", where, 1),
                Seconds = f.Number(e, "seconds", where),
                Workbench = f.Integer(e, "workbench", where, 0),
                Ingredients = ingredients,
                Known = f.Flag(e, "known", where, true),
                Id = (ushort)Math.Clamp(id, 1, ushort.MaxValue),
                Key = own ? f.Text(e, "key", where) : item.Key,
                StationKey = f.Has(e, "station") ? f.Text(e, "station", where) : null,
            });
        }
    }

    /// <summary>Keys and numbers unique (a second way's number no item's); amounts, times, levels and ingredients in range.</summary>
    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        c.Duplicates("recipes.json", loaded.Recipes.Select(r => r.Key), "has more than one recipe (a second way needs its own key and id)");
        foreach (IGrouping<ushort, Recipe> g in loaded.Recipes.GroupBy(r => r.Id).Where(g => g.Count() > 1))
        {
            c.Problem("recipes.json", $"id {g.Key} is used by {string.Join(", ", g.Select(r => r.Key))}");
        }
        foreach (Recipe r in loaded.Recipes.Where(r => r.Id != r.Output.Id && loaded.Items.Any(i => i.Id == r.Id)))
        {
            c.Problem($"recipes.json: \"{r.Key}\"", $"id {r.Id} is an item's; a second way to make something needs a number no item uses");
        }
        foreach (Recipe r in loaded.Recipes)
        {
            string where = $"recipes.json: \"{r.Output.Key}\"";
            if (r.Amount < 1 || r.Amount > r.Output.StackSize)
            {
                c.Problem(where, $"amount {r.Amount} must be 1 … the output's stack ({r.Output.StackSize})");
            }
            if (r.Seconds <= 0 || r.Seconds > 3600)
            {
                c.Problem(where, "seconds must be 0 … 3600");
            }
            if (r.Workbench is < 0 or > 3)
            {
                c.Problem(where, "workbench must be 0 … 3");
            }
            if (r.Ingredients.Count == 0 || r.Ingredients.Any(i => i.Count < 1))
            {
                c.Problem(where, "needs ingredients, each at least 1");
            }
            if (r.Ingredients.GroupBy(i => i.Item).Any(g => g.Count() > 1))
            {
                c.Problem(where, "an ingredient is listed twice");
            }
            if (r.Ingredients.Any(i => i.Item == r.Output))
            {
                c.Problem(where, "an ingredient is its own output");
            }
            if (r.Output.Ship != null && (r.Amount != 1 || (r.Station != null && r.Station.Bay == null)))
            {
                // At a station it rolls out onto the bay (G6); without one it is its item, put down by hand (a boat on the water).
                c.Problem(where, "a ship is made one at a time, at a station with a bay to roll out onto or as its item");
            }
        }
    }
}
