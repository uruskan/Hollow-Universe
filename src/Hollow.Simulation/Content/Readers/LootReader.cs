using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// loot.json: the loot tables (guaranteed entries, then weighted rolls): the respawn kit, the load tests' kits, the
/// world's barrels and crates (G7), whose blueprint entries take a fifth number, the workbench level of the recipe taught.
/// </summary>
internal sealed class LootReader : IContentReader
{
    public string File => "loot.json";

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        JsonElement root = r.Root("loot.json", "tables");
        JsonElement[] list = f.List(root, "tables", "loot.json");
        for (int i = 0; i < list.Length; i++)
        {
            JsonElement e = list[i];
            string key = f.Text(e, "key", $"loot.json: tables[{i}]");
            string where = $"loot.json: {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "key", "guaranteed", "rolls", "entries");
            var guaranteed = new List<LootTable.Entry>();
            foreach (JsonElement g in f.List(e, "guaranteed", where, required: false))
            {
                if (f.Tuple(g, where + ".guaranteed", 2, out string item, out double[] v) && r.Resolve(item, where + ".guaranteed") is { } def)
                {
                    guaranteed.Add(new LootTable.Entry(def, (int)v[0], (int)v[1], 1));
                }
            }
            var entries = new List<LootTable.Entry>();
            foreach (JsonElement g in f.List(e, "entries", where, required: false))
            {
                // A fifth number makes it a blueprint of that workbench level (G7, world.json).
                if (f.Tuple(g, where + ".entries", 3, out string item, out double[] v, optional: 1) && r.Resolve(item, where + ".entries") is { } def)
                {
                    entries.Add(new LootTable.Entry(def, (int)v[0], (int)v[1], v[2], g.GetArrayLength() == 5 ? (int)v[3] : -1));
                }
            }
            int[] rolls = f.Integers(e, "rolls", where);
            if (entries.Count > 0 && rolls.Length != 2)
            {
                f.Problem(where, "\"rolls\" must be [least, most]");
            }
            loaded.Loot.Add(new LootTable
            {
                Key = key,
                Guaranteed = guaranteed,
                Entries = entries,
                LeastRolls = rolls.Length == 2 ? rolls[0] : 0,
                MostRolls = rolls.Length == 2 ? rolls[1] : 0,
            });
        }
    }

    /// <summary>Keys unique; each entry's counts and weight, and the rolls, in range.</summary>
    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        c.Duplicates("loot.json", loaded.Loot.Select(t => t.Key));
        foreach (LootTable t in loaded.Loot)
        {
            string where = $"loot.json: \"{t.Key}\"";
            if (t.Guaranteed.Concat(t.Entries).Any(e => e.Least < 1 || e.Most < e.Least || e.Weight <= 0))
            {
                c.Problem(where, "each entry needs 1 ≤ least ≤ most and a positive weight");
            }
            if (t.LeastRolls < 0 || t.MostRolls < t.LeastRolls)
            {
                c.Problem(where, "rolls must be 0 ≤ least ≤ most");
            }
        }
    }
}
