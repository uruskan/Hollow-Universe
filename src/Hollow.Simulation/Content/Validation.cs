using System;
using System.Collections.Generic;
using System.Linq;

namespace Hollow.Simulation.Content;

/// <summary>
/// Checks across the tables (GROUND.md §7): IDs and keys are unique, every reference resolves, the numbers make sense,
/// and every recipe can be made from what can be gathered: starting from what natural objects yield, a recipe is
/// makeable once its ingredients are, and its workbench level is (a workbench is an item too); a device cooks or
/// burns things once the device itself can be made.
/// </summary>
internal static class Validation
{
    public static void Check(GameContent.Loaded c, List<string> problems)
    {
        void Problem(string where, string what) => problems.Add($"{where}: {what}");

        // ---------------------------------------------------------------- uniqueness
        foreach (IGrouping<ushort, ItemDef> g in c.Items.GroupBy(i => i.Id).Where(g => g.Count() > 1))
        {
            Problem("items.json", $"id {g.Key} is used by {string.Join(", ", g.Select(i => i.Key))}");
        }
        Duplicates("categories.json", c.Categories.Select(x => x.Key));
        Duplicates("recipes.json", c.Recipes.Select(r => r.Output.Key), "has more than one recipe");
        Duplicates("gather.json", c.Natural.Select(k => k.Key));
        Duplicates("deployables.json", c.Deployables.Select(d => d.Key));
        Duplicates("loot.json", c.Loot.Select(t => t.Key));

        void Duplicates(string file, IEnumerable<string> keys, string what = "is used twice")
        {
            foreach (string key in keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key))
            {
                Problem(file, $"\"{key}\" {what}");
            }
        }

        // ---------------------------------------------------------------- items
        var categories = new HashSet<string>(c.Categories.Select(x => x.Key), StringComparer.Ordinal);
        var cookers = new HashSet<string>(c.Deployables.Where(d => d.Device != null).Select(d => d.Device!.Cooks), StringComparer.Ordinal);
        foreach (ItemDef item in c.Items)
        {
            string where = $"items.json: \"{item.Key}\"";
            if (item.Key.Length == 0 || item.Key.Any(ch => !(char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '-')))
            {
                Problem(where, "keys are lower-case letters, digits and '-'");
            }
            if (!categories.Contains(item.Category))
            {
                Problem(where, $"category \"{item.Category}\" is not in categories.json");
            }
            if (item.StackSize < 1 || item.StackSize > 60_000)
            {
                Problem(where, $"stack {item.StackSize} must be 1 … 60000");
            }
            bool single = item.HasCondition || item.Wear != null || item.Melee != null || item.Bow != null || item.Deploys != null;
            if (single && item.StackSize != 1)
            {
                Problem(where, "tools, weapons, clothing and deployables stack to 1");
            }
            if (item.MaxCondition < 0 || item.MaxCondition > 60_000)
            {
                Problem(where, "condition must be 0 … 60000");
            }
            if (item.Melee is { } m && (m.Reach <= 0 || m.Reach > 4 || m.SwingSeconds <= 0 || m.StrikeSeconds < 0 ||
                m.StrikeSeconds > m.SwingSeconds || m.Damage < 0 || m.Tree < 0 || m.Ore < 0 || m.Bleed is < 0 or > 2))
            {
                Problem(where, "melee: reach 0 … 4 m, swing > 0, strike within the swing, nothing negative");
            }
            if (item.Bow is { } bow && (bow.Ammo.Count == 0 || bow.Ammo.Any(a => a.Ammo == null) || bow.DrawSeconds <= 0))
            {
                Problem(where, "bow: needs ammo that is ammunition, and a draw time");
            }
            if (item.Ammo is { } ammo && (ammo.Speed is <= 0 or > 2000 || ammo.Damage < 0 || ammo.LifetimeSeconds is <= 0 or > 10 ||
                ammo.Recover is < 0 or > 1 || ammo.Gravity < 0))
            {
                Problem(where, "ammo: speed 0 … 2000 m/s, lifetime 0 … 10 s, recover 0 … 1");
            }
            if (item.Wear is { } wear && (wear.Layers == WearLayer.None || wear.Melee is < 0 or > 0.9f || wear.Projectile is < 0 or > 0.9f))
            {
                Problem(where, "wear: needs a layer; protection 0 … 0.9");
            }
            if (item.Fuel is { } fuel && fuel.Seconds <= 0)
            {
                Problem(where, "fuel: seconds must be positive");
            }
            if (item.Cook is { } cook)
            {
                if (cook.Seconds <= 0)
                {
                    Problem(where, "cook: seconds must be positive");
                }
                if (!cookers.Contains(cook.In))
                {
                    Problem(where, $"cook: no device cooks \"{cook.In}\"");
                }
                if (cook.Into == item)
                {
                    Problem(where, "cook: an item can't cook into itself");
                }
            }
        }

        // ---------------------------------------------------------------- recipes
        foreach (Recipe r in c.Recipes)
        {
            string where = $"recipes.json: \"{r.Output.Key}\"";
            if (r.Amount < 1 || r.Amount > r.Output.StackSize)
            {
                Problem(where, $"amount {r.Amount} must be 1 … the output's stack ({r.Output.StackSize})");
            }
            if (r.Seconds <= 0 || r.Seconds > 3600)
            {
                Problem(where, "seconds must be 0 … 3600");
            }
            if (r.Workbench is < 0 or > 3)
            {
                Problem(where, "workbench must be 0 … 3");
            }
            if (r.Ingredients.Count == 0 || r.Ingredients.Any(i => i.Count < 1))
            {
                Problem(where, "needs ingredients, each at least 1");
            }
            if (r.Ingredients.GroupBy(i => i.Item).Any(g => g.Count() > 1))
            {
                Problem(where, "an ingredient is listed twice");
            }
            if (r.Ingredients.Any(i => i.Item == r.Output))
            {
                Problem(where, "an ingredient is its own output");
            }
        }

        // ---------------------------------------------------------------- natural objects
        foreach (NaturalKind k in c.Natural)
        {
            string where = $"gather.json: \"{k.Key}\"";
            if (k.Class != NaturalClass.Boulder && (k.Yield.Count == 0 || k.Yield.Any(y => y.Count < 1)))
            {
                Problem(where, "needs a yield, each at least 1");
            }
            if (k.IsHarvested && k.Health <= 0)
            {
                Problem(where, "trees and outcrops need health");
            }
            if (k.Stages is < 1 or > 3)
            {
                Problem(where, "stages must be 1 … 3");
            }
            if (k.RespawnSeconds <= 0 || k.Radius <= 0 || k.Height <= 0 || k.StandInPerTile < 0)
            {
                Problem(where, "respawn, radius and height must be positive");
            }
        }
        if (c.WeakSpot.Radius <= 0 || c.WeakSpot.Bonus < 1)
        {
            Problem("gather.json: weakSpot", "radius must be positive and the bonus at least 1");
        }

        // ---------------------------------------------------------------- deployables
        foreach (DeployableDef d in c.Deployables)
        {
            string where = $"deployables.json: \"{d.Key}\"";
            if (d.Item.Deploys != d)
            {
                Problem(where, $"item \"{d.Item.Key}\" must say \"deploys\": \"{d.Key}\"");
            }
            if (d.Health <= 0 || d.HalfExtents.X <= 0 || d.HalfExtents.Y <= 0 || d.HalfExtents.Z <= 0)
            {
                Problem(where, "health and size must be positive");
            }
            if (d.MaxSlopeDegrees is <= 0 or > 60 || d.Slots is < 0 or > 64 || d.Workbench is < 0 or > 3 || d.RespawnSeconds < 0)
            {
                Problem(where, "max slope 0 … 60°, slots 0 … 64, workbench 0 … 3");
            }
            if (d.Device is { } device)
            {
                int[] all = device.FuelSlots.Concat(device.InputSlots).Concat(device.OutputSlots).ToArray();
                if (all.Any(s => s < 0 || s >= d.Slots) || all.Distinct().Count() != all.Length)
                {
                    Problem(where, "device slots must be distinct and within the slots");
                }
                if (device.FuelSlots.Count == 0 || device.OutputSlots.Count == 0)
                {
                    Problem(where, "a device needs fuel and output slots");
                }
            }
        }
        if (c.PlacementReach <= 0 || c.PlacementMaxStep <= 0)
        {
            Problem("deployables.json: placement", "reach and maxStep must be positive");
        }

        // ---------------------------------------------------------------- loot and survival
        foreach (LootTable t in c.Loot)
        {
            string where = $"loot.json: \"{t.Key}\"";
            if (t.Guaranteed.Concat(t.Entries).Any(e => e.Least < 1 || e.Most < e.Least || e.Weight <= 0))
            {
                Problem(where, "each entry needs 1 ≤ least ≤ most and a positive weight");
            }
            if (t.LeastRolls < 0 || t.MostRolls < t.LeastRolls)
            {
                Problem(where, "rolls must be 0 ≤ least ≤ most");
            }
        }
        SurvivalTuning s = c.Survival;
        if (!c.Loot.Any(t => t.Key == s.RespawnKitKey))
        {
            Problem("survival.json: world.respawnKit", $"no loot table \"{s.RespawnKitKey}\"");
        }
        if (s.MaxHealth <= 0 || s.SpawnHealth <= 0 || s.SpawnHealth > s.MaxHealth || s.SpawnCalories > s.MaxCalories ||
            s.SpawnHydration > s.MaxHydration || s.MaxRewindSeconds is < 0 or > 1 || s.IntentBurst < 1 || s.IntentsPerSecond <= 0)
        {
            Problem("survival.json", "spawn vitals within the maxima; rewind 0 … 1 s; positive rate limits");
        }

        // ---------------------------------------------------------------- every recipe from gatherable resources
        var obtainable = new HashSet<ItemDef>();
        foreach (NaturalKind k in c.Natural)
        {
            foreach (ItemAmount y in k.Yield)
            {
                obtainable.Add(y.Item);
            }
        }
        bool grew = true;
        while (grew)
        {
            grew = false;
            int bench = c.Deployables.Where(d => d.Workbench > 0 && obtainable.Contains(d.Item)).Select(d => d.Workbench).DefaultIfEmpty(0).Max();
            var devices = c.Deployables.Where(d => d.Device != null && obtainable.Contains(d.Item)).Select(d => d.Device!).ToList();
            foreach (Recipe r in c.Recipes)
            {
                if (r.Workbench <= bench && r.Ingredients.All(i => obtainable.Contains(i.Item)))
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
            }
        }
        foreach (Recipe r in c.Recipes.Where(r => !obtainable.Contains(r.Output)))
        {
            string missing = string.Join(", ", r.Ingredients.Where(i => !obtainable.Contains(i.Item)).Select(i => i.Item.Key));
            Problem($"recipes.json: \"{r.Output.Key}\"", missing.Length > 0
                ? $"can't be made from gatherable resources (nothing gives {missing})"
                : $"needs workbench {r.Workbench}, which can't be made");
        }
    }
}
