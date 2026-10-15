using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>Reads world.json (G7) and links it to the items, recipes, loot tables and deployables; then checks it.</summary>
internal sealed class WorldReader : IContentReader
{
    public string File => "world.json";

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        JsonElement root = r.Documents[File];
        var world = new WorldData();
        loaded.World = world;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        f.OnlyKnown(root, File, "source", "note", "currency", "blueprint", "props", "recycler", "recycle", "research", "techTrees", "loot",
            "recyclers", "teams", "map");
        world.Currency = r.Resolve(f.Text(root, "currency", File), File + ": currency");
        world.Blueprint = r.Resolve(f.Text(root, "blueprint", File), File + ": blueprint");

        // The recycler's numbers first: the recycler prop carries them.
        if (f.TryChild(root, "recycler", out JsonElement recycler))
        {
            string where = File + ": recycler";
            f.OnlyKnown(recycler, where, "input", "output", "seconds", "efficiency", "batch", "note");
            world.Recycler = new RecyclerStats
            {
                InputSlots = f.Integers(recycler, "input", where),
                OutputSlots = f.Integers(recycler, "output", where),
                Seconds = f.Double(recycler, "seconds", where, 5),
                Efficiency = f.Double(recycler, "efficiency", where, 0.5),
                Batch = f.Double(recycler, "batch", where, 0.1),
            };
        }
        else
        {
            f.Problem(File, "\"recycler\" is missing");
        }

        var props = new List<PropDef>();
        JsonElement[] propList = f.List(root, "props", File);
        for (int i = 0; i < propList.Length; i++)
        {
            JsonElement e = propList[i];
            string key = f.Text(e, "key", $"{File}: props[{i}]");
            string where = $"{File}: prop {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "key", "id", "name", "kind", "health", "size", "slots", "loot", "respawn", "note");
            string kindName = f.Text(e, "kind", where);
            WorldPropKind? kind = kindName switch
            {
                "barrel" => WorldPropKind.Barrel,
                "crate" => WorldPropKind.Crate,
                "recycler" => WorldPropKind.Recycler,
                _ => null,
            };
            if (kind == null)
            {
                f.Problem(where, $"kind \"{kindName}\" is not barrel, crate or recycler");
            }
            int id = f.Integer(e, "id", where);
            if (id is < 1 or > 255)
            {
                f.Problem(where, $"id {id} must be 1 … 255");
            }
            Vector3 half = Vector3.Zero;
            JsonElement[] size = f.List(e, "size", where);
            if (size.Length == 3 && size.All(s => s.ValueKind == JsonValueKind.Number))
            {
                half = new Vector3((float)size[0].GetDouble(), (float)size[1].GetDouble(), (float)size[2].GetDouble());
            }
            else
            {
                f.Problem(where, "\"size\" must be three numbers (half extents x, y, z)");
            }
            string? lootKey = f.Has(e, "loot") ? f.Text(e, "loot", where) : null;
            string name = f.Text(e, "name", where);
            var prop = new PropDef
            {
                Id = (byte)Math.Clamp(id, 0, 255),
                Key = key,
                Name = name,
                Kind = kind ?? WorldPropKind.Crate,
                LootKey = lootKey,
                Loot = lootKey != null ? loaded.Loot.FirstOrDefault(t => t.Key == lootKey) : null,
                RespawnSeconds = f.Double(e, "respawn", where, 0),
            };
            if (lootKey != null && prop.Loot == null)
            {
                f.Problem(where, $"no loot table \"{lootKey}\" in loot.json");
            }
            // The deployable it lives as: an item of its own that no inventory holds, so the wire's look is the prop's ID.
            prop.Deployable = new DeployableDef
            {
                Key = key,
                Item = new ItemDef { Id = prop.Id, Key = key, Name = name, Category = "world" },
                Health = f.Number(e, "health", where, 1000),
                HalfExtents = half,
                Solid = true,
                Slots = f.Integer(e, "slots", where, 0),
                TakeOnly = prop.Kind == WorldPropKind.Crate,
                Recycler = prop.Kind == WorldPropKind.Recycler ? world.Recycler : null,
            };
            props.Add(prop);
        }
        world.Props = props;

        foreach (JsonElement e in f.List(root, "recycle", File))
        {
            string key = f.Text(e, "item", File + ": recycle");
            string where = $"{File}: recycle {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "item", "yield", "note");
            ItemDef? item = r.Resolve(key, where);
            var yields = new List<ItemAmount>();
            foreach (JsonElement y in f.List(e, "yield", where))
            {
                if (f.Tuple(y, where + ".yield", 1, out string k, out double[] v) && r.Resolve(k, where + ".yield") is { } def)
                {
                    yields.Add(new ItemAmount(def, (int)v[0]));
                }
            }
            if (item != null)
            {
                if (world.FixedYield(item.Id) != null)
                {
                    f.Problem(where, "is listed twice");
                }
                world.SetYield(item.Id, yields);
            }
        }

        if (f.TryChild(root, "research", out JsonElement research))
        {
            string where = File + ": research";
            f.OnlyKnown(research, where, "table", "item", "salvage", "output", "seconds", "cost", "note");
            world.Research = new ResearchStats
            {
                TableKey = f.Text(research, "table", where),
                ItemSlot = f.Integer(research, "item", where, 0),
                SalvageSlot = f.Integer(research, "salvage", where, 1),
                OutputSlot = f.Integer(research, "output", where, 2),
                Seconds = f.Double(research, "seconds", where, 10),
                Costs = f.Integers(research, "cost", where),
            };
            DeployableDef? table = loaded.Deployables.FirstOrDefault(d => d.Key == world.Research.TableKey);
            if (table == null)
            {
                f.Problem(where, $"no deployable \"{world.Research.TableKey}\" in deployables.json");
            }
            else
            {
                table.Research = world.Research;
            }
        }
        else
        {
            f.Problem(File, "\"research\" is missing");
        }

        // Tech trees: nodes by recipe, then their links; a recipe in a tree is not known from the start. A node names an
        // item, so its item's own recipe (a second way to make it, G7's lamp oil from fat, has a key of its own).
        var recipes = loaded.Recipes.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var trees = new List<TechTree>();
        foreach (JsonElement t in f.List(root, "techTrees", File))
        {
            int bench = f.Integer(t, "workbench", File + ": techTrees");
            string where = $"{File}: tech tree {bench}";
            f.OnlyKnown(t, where, "workbench", "nodes", "note");
            var nodes = new List<TechNode>();
            foreach (JsonElement n in f.List(t, "nodes", where))
            {
                string key = f.Text(n, "item", where);
                string nw = $"{where}: {JsonFields.Quote(key)}";
                f.OnlyKnown(n, nw, "item", "cost", "after", "note");
                if (!recipes.TryGetValue(key, out Recipe? recipe))
                {
                    f.Problem(nw, $"no recipe makes \"{key}\"");
                    continue;
                }
                var node = new TechNode { Recipe = recipe, Tier = bench, Cost = f.Integer(n, "cost", nw), AfterKeys = f.Texts(n, "after", nw) };
                nodes.Add(node);
                if (world.NodeOf(recipe.Id) != null)
                {
                    f.Problem(nw, "is in more than one tech tree");
                }
                world.AddNode(node);
            }
            foreach (TechNode node in nodes)
            {
                var after = new List<TechNode>();
                foreach (string key in node.AfterKeys)
                {
                    TechNode? before = nodes.FirstOrDefault(x => x.Item.Key == key);
                    if (before == null)
                    {
                        f.Problem($"{where}: {JsonFields.Quote(node.Item.Key)}", $"\"after\": \"{key}\" is not a node of this tree");
                    }
                    else
                    {
                        after.Add(before);
                    }
                }
                node.After = after;
            }
            foreach (TechNode node in nodes)
            {
                node.Depth = DepthOf(node, 0);
            }
            trees.Add(new TechTree { Workbench = bench, Nodes = nodes });
        }
        world.TechTrees = trees;
        foreach (Recipe recipe in loaded.Recipes)
        {
            recipe.Known &= world.NodeOf(recipe.Id) == null;
        }
        foreach (Recipe recipe in loaded.Recipes.Where(x => !x.Known))
        {
            world.AddToPool(world.TierOf(recipe), recipe);
        }

        if (f.TryChild(root, "loot", out JsonElement loot))
        {
            string where = File + ": loot";
            f.OnlyKnown(loot, where, "cell", "clear", "spread", "coast", "forestEdge", "open", "barrels", "fuelBarrel", "crate",
                "military", "landing", "note");
            int[] barrels = f.Integers(loot, "barrels", where);
            JsonElement[] landing = f.List(loot, "landing", where, required: false);
            world.Loot = new LootSpotSettings
            {
                Cell = f.Double(loot, "cell", where),
                Clear = f.Double(loot, "clear", where),
                Spread = f.Double(loot, "spread", where),
                Coast = f.Double(loot, "coast", where),
                ForestEdge = f.Double(loot, "forestEdge", where),
                Open = f.Double(loot, "open", where),
                LeastBarrels = barrels.Length == 2 ? barrels[0] : 2,
                MostBarrels = barrels.Length == 2 ? barrels[1] : 4,
                FuelBarrel = f.Double(loot, "fuelBarrel", where),
                Crate = f.Double(loot, "crate", where),
                Military = f.Double(loot, "military", where),
                Landing = landing.Length == 3 && landing.All(x => x.ValueKind == JsonValueKind.Number)
                    ? new Vector3((float)landing[0].GetDouble(), (float)landing[1].GetDouble(), (float)landing[2].GetDouble())
                    : null,
            };
            if (barrels.Length != 2)
            {
                f.Problem(where, "\"barrels\" must be [least, most]");
            }
        }
        else
        {
            f.Problem(File, "\"loot\" is missing");
        }

        if (f.TryChild(root, "recyclers", out JsonElement recyclers))
        {
            string where = File + ": recyclers";
            f.OnlyKnown(recyclers, where, "perFace", "note");
            // One per cube face of the world (docs/WHOLE_WORLD.md decision 9): 0 or 1 a face.
            world.RecyclerCount = Math.Clamp(f.Integer(recyclers, "perFace", where), 0, 1) * Surface.CubeSphere.FaceCount;
        }
        if (f.TryChild(root, "teams", out JsonElement teams))
        {
            f.OnlyKnown(teams, File + ": teams", "size", "inviteSeconds", "note");
            world.TeamSize = f.Integer(teams, "size", File + ": teams", 8);
            world.InviteSeconds = f.Double(teams, "inviteSeconds", File + ": teams", 300);
        }
        if (f.TryChild(root, "map", out JsonElement map))
        {
            f.OnlyKnown(map, File + ": map", "markers", "cell", "note");
            world.MaxMarkers = f.Integer(map, "markers", File + ": map", 5);
            world.MapCell = f.Double(map, "cell", File + ": map", 150);
        }
    }

    // Steps from a root along the shortest path back (a cycle reads as deep, and the check below reports it).
    private static int DepthOf(TechNode node, int guard)
    {
        if (node.After.Count == 0 || guard > 64)
        {
            return guard > 64 ? 64 : 0;
        }
        return 1 + node.After.Min(n => DepthOf(n, guard + 1));
    }

    /// <summary>The checks of world.json against the rest (GROUND.md §7).</summary>
    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        WorldData? w = loaded.World;
        if (w == null)
        {
            return;
        }
        if (w.Currency != null && (w.Currency.StackSize < 100 || w.Currency.HasCondition))
        {
            c.Problem(File + ": currency", "salvage stacks (at least 100) and has no condition");
        }
        if (w.Blueprint != null && (w.Blueprint.StackSize != 1 || w.Blueprint.HasCondition || w.Blueprint.Deploys != null))
        {
            c.Problem(File + ": blueprint", "the blueprint item stacks to 1 and has no condition of its own (it holds the recipe)");
        }
        foreach (IGrouping<int, PropDef> g in w.Props.GroupBy(p => (int)p.Id).Where(g => g.Count() > 1))
        {
            c.Problem(File, $"prop id {g.Key} is used by {string.Join(", ", g.Select(p => p.Key))}");
        }
        foreach (IGrouping<string, PropDef> g in w.Props.GroupBy(p => p.Key).Where(g => g.Count() > 1))
        {
            c.Problem(File, $"prop \"{g.Key}\" is listed twice");
        }
        foreach (PropDef p in w.Props)
        {
            string where = $"{File}: prop \"{p.Key}\"";
            DeployableDef d = p.Deployable;
            if (d.Health <= 0 || d.HalfExtents.X <= 0 || d.HalfExtents.Y <= 0 || d.HalfExtents.Z <= 0 || d.Slots is < 1 or > 64)
            {
                c.Problem(where, "health and size positive, 1 … 64 slots");
            }
            if (p.Kind != WorldPropKind.Recycler && (p.Loot == null || p.RespawnSeconds <= 0))
            {
                c.Problem(where, "a barrel or crate needs a loot table and a respawn time");
            }
            if (p.Kind == WorldPropKind.Recycler && p.LootKey != null)
            {
                c.Problem(where, "a recycler has no loot");
            }
            if (p.Loot is { } t && t.Guaranteed.Count + t.MostRolls > d.Slots)
            {
                c.Problem(where, $"its {d.Slots} slots can't hold what \"{t.Key}\" gives at most ({t.Guaranteed.Count + t.MostRolls} stacks)");
            }
        }
        if (w.Props.Count(p => p.Kind == WorldPropKind.Recycler) != 1 && w.RecyclerCount > 0)
        {
            c.Problem(File, "exactly one recycler prop when recyclers stand in the world");
        }
        RecyclerStats r = w.Recycler;
        int recyclerSlots = w.Props.FirstOrDefault(p => p.Kind == WorldPropKind.Recycler)?.Deployable.Slots ?? 0;
        int[] all = r.InputSlots.Concat(r.OutputSlots).ToArray();
        if (r.InputSlots.Count == 0 || r.OutputSlots.Count == 0 || all.Distinct().Count() != all.Length || all.Any(s => s < 0 || s >= recyclerSlots))
        {
            c.Problem(File + ": recycler", "input and output slots, distinct and within the recycler's slots");
        }
        if (r.Seconds <= 0 || r.Efficiency is <= 0 or > 1 || r.Batch is <= 0 or > 1)
        {
            c.Problem(File + ": recycler", "seconds positive, efficiency and batch 0 … 1");
        }
        foreach (ItemDef item in loaded.Items)
        {
            if (w.FixedYield(item.Id) is not { } yields)
            {
                continue;
            }
            string where = $"{File}: recycle \"{item.Key}\"";
            if (yields.Count == 0 || yields.Any(y => y.Count < 1 || y.Item == item))
            {
                c.Problem(where, "yields at least one unit of each, never itself");
            }
            if (item == w.Currency || item == w.Blueprint)
            {
                c.Problem(where, "salvage and blueprints aren't recycled");
            }
        }
        ResearchStats rs = w.Research;
        DeployableDef? table = loaded.Deployables.FirstOrDefault(d => d.Research == rs);
        int[] researchSlots = { rs.ItemSlot, rs.SalvageSlot, rs.OutputSlot };
        if (table != null && (researchSlots.Distinct().Count() != 3 || researchSlots.Any(x => x < 0 || x >= table.Slots) || table.Device != null))
        {
            c.Problem(File + ": research", "the item, salvage and output slots, distinct and within the table's slots (not a furnace)");
        }
        if (rs.Seconds <= 0 || rs.Costs.Count != 4 || rs.Costs.Any(x => x < 0))
        {
            c.Problem(File + ": research", "seconds positive; four costs (no workbench, levels 1 to 3), none negative");
        }
        foreach (TechTree tree in w.TechTrees)
        {
            string where = $"{File}: tech tree {tree.Workbench}";
            if (tree.Workbench is < 1 or > 3 || w.TechTrees.Count(t => t.Workbench == tree.Workbench) > 1)
            {
                c.Problem(where, "workbench levels 1 … 3, one tree each");
            }
            foreach (TechNode n in tree.Nodes)
            {
                if (n.Cost < 1 || n.Recipe.Workbench > tree.Workbench || n.Depth >= 64)
                {
                    c.Problem($"{where}: \"{n.Item.Key}\"", "a positive cost, a recipe this bench or a lower one makes, and no loop through \"after\"");
                }
            }
        }
        foreach (LootTable t in loaded.Loot)
        {
            foreach (LootTable.Entry e in t.Entries.Concat(t.Guaranteed))
            {
                bool blueprint = e.Item == w.Blueprint;
                if (blueprint != e.Tier >= 0)
                {
                    c.Problem($"loot.json: \"{t.Key}\"", blueprint ? "a blueprint entry needs a fifth number, the workbench level" : $"only blueprints take a workbench level ({e.Item.Key})");
                }
                else if (blueprint && (e.Least != 1 || e.Most != 1 || w.BlueprintPool(e.Tier).Count == 0))
                {
                    c.Problem($"loot.json: \"{t.Key}\"", $"a blueprint entry gives one, of a level with recipes to learn ({e.Tier})");
                }
            }
        }
        LootSpotSettings l = w.Loot;
        if (l.Cell < 10 || l.Clear < 0 || l.Spread <= 0 || l.LeastBarrels < 0 || l.MostBarrels < l.LeastBarrels ||
            new[] { l.Coast, l.ForestEdge, l.Open, l.FuelBarrel, l.Crate, l.Military }.Any(x => x is < 0 or > 1))
        {
            c.Problem(File + ": loot", "cells of 10 m or more, chances 0 … 1, barrels [least ≤ most]");
        }
        foreach (string key in new[] { "barrel", "crate" })
        {
            if (!w.Props.Any(p => p.Kind == (key == "barrel" ? WorldPropKind.Barrel : WorldPropKind.Crate)))
            {
                c.Problem(File, $"no {key} prop for the loot spots");
            }
        }
        if (w.TeamSize is < 2 or > 16 || w.InviteSeconds <= 0)
        {
            c.Problem(File + ": teams", "2 … 16 members, invites last a while");
        }
        if (w.MaxMarkers is < 1 or > 10 || w.MapCell is < 25 or > 5000)
        {
            c.Problem(File + ": map", "1 … 10 markers, cells of 25 … 5,000 m");
        }
    }
}
