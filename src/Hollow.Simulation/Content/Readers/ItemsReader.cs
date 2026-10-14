using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// items.json: every item with its parts (<c>melee</c>, <c>bow</c>, <c>ammo</c>, <c>consume</c>, <c>wear</c>, <c>fuel</c>,
/// <c>cook</c>; GROUND_SYSTEMS.md §7), what it deploys, does with the building rules or is as a ship (linked by those
/// files' readers), and the inventory's actions. Read before every file that names an item.
/// </summary>
internal sealed class ItemsReader : IContentReader
{
    public string File => "items.json";

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        JsonElement root = r.Root("items.json", "actions", "items");
        ReadItemActions(f, root, loaded);
        JsonElement[] list = f.List(root, "items", "items.json");
        // Keys first, so references between items (fuel byproducts, cooking) resolve in any order.
        var drafts = new List<(ItemDef Item, JsonElement Element, string Where)>();
        for (int i = 0; i < list.Length; i++)
        {
            JsonElement e = list[i];
            string key = f.Text(e, "key", $"items.json: items[{i}]");
            string where = $"items.json: {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "id", "key", "name", "description", "category", "stack", "condition", "melee", "bow", "ammo",
                "consume", "wear", "fuel", "cook", "deploys", "building", "door", "ship");
            int id = f.Integer(e, "id", where);
            if (id is < 1 or > ushort.MaxValue)
            {
                f.Problem(where, $"id {id} must be 1 … 65535");
            }
            var item = new ItemDef
            {
                Id = (ushort)Math.Clamp(id, 0, ushort.MaxValue),
                Key = key,
                Name = f.Text(e, "name", where),
                Description = f.Text(e, "description", where, ""),
                Category = f.Text(e, "category", where),
                StackSize = f.Integer(e, "stack", where, 1),
                MaxCondition = f.Integer(e, "condition", where, 0),
                Melee = f.TryChild(e, "melee", out JsonElement melee) ? Melee(f, melee, where + ".melee") : null,
                Bow = f.TryChild(e, "bow", out JsonElement bow) ? Bow(f, bow, where + ".bow") : null,
                Ammo = f.TryChild(e, "ammo", out JsonElement ammo) ? Ammo(f, ammo, where + ".ammo") : null,
                Consume = f.TryChild(e, "consume", out JsonElement consume) ? Consume(f, consume, where + ".consume") : null,
                Wear = f.TryChild(e, "wear", out JsonElement wear) ? Wear(f, wear, where + ".wear") : null,
                Fuel = f.TryChild(e, "fuel", out JsonElement fuel) ? Fuel(f, fuel, where + ".fuel") : null,
                Cook = f.TryChild(e, "cook", out JsonElement cook) ? Cook(f, cook, where + ".cook") : null,
                DeploysKey = f.Has(e, "deploys") ? f.Text(e, "deploys", where) : null,
                Building = f.Has(e, "building") ? Role(f, f.Text(e, "building", where), where) : BuildingRole.None,
                DoorKey = f.Has(e, "door") ? f.Text(e, "door", where) : null,
                ShipKey = f.Has(e, "ship") ? f.Text(e, "ship", where) : null,
            };
            if (!r.AddItem(item))
            {
                f.Problem(where, "the key is used twice");
            }
            drafts.Add((item, e, where));
            loaded.Items.Add(item);
        }
        foreach ((ItemDef item, JsonElement _, string where) in drafts)
        {
            if (item.Fuel?.ByproductKey is { } byproduct)
            {
                item.Fuel.Byproduct = r.Resolve(byproduct, where + ".fuel.byproduct");
            }
            if (item.Cook != null)
            {
                item.Cook.Into = r.Resolve(item.Cook.IntoKey, where + ".cook.into");
            }
            if (item.Bow != null)
            {
                item.Bow.Ammo = item.Bow.AmmoKeys.Select(k => r.Resolve(k, where + ".bow.ammo")).OfType<ItemDef>().ToArray();
            }
        }
    }

    // items.json's actions (GROUND_SYSTEMS.md §7): the inventory's buttons, append only (a place is a wire number).
    private static void ReadItemActions(JsonFields f, JsonElement root, GameContent.Loaded loaded)
    {
        foreach ((JsonElement e, int i) in f.List(root, "actions", "items.json").Select((e, i) => (e, i)))
        {
            string where = $"items.json: actions[{i}]";
            f.OnlyKnown(e, where, "key", "label", "note");
            loaded.ItemActions.Add(new global::Hollow.Simulation.Items.ItemActionDef(i, f.Text(e, "key", where), f.Text(e, "label", where)));
        }
    }

    private static BuildingRole Role(JsonFields f, string text, string where)
    {
        switch (text)
        {
            case "plan": return BuildingRole.Plan;
            case "hammer": return BuildingRole.Hammer;
            case "claim": return BuildingRole.Claim;
            case "key-lock": return BuildingRole.KeyLock;
            case "code-lock": return BuildingRole.CodeLock;
            case "key": return BuildingRole.Key;
            case "door": return BuildingRole.Door;
        }
        f.Problem(where + ".building", $"\"{text}\" is not plan, hammer, claim, key-lock, code-lock, key or door");
        return BuildingRole.None;
    }

    private static MeleeStats Melee(JsonFields f, JsonElement e, string where)
    {
        f.OnlyKnown(e, where, "damage", "reach", "swing", "strike", "tree", "ore", "structure", "bleed");
        return new MeleeStats
        {
            Damage = f.Number(e, "damage", where),
            Reach = f.Number(e, "reach", where),
            SwingSeconds = f.Number(e, "swing", where),
            StrikeSeconds = f.Number(e, "strike", where),
            Tree = f.Number(e, "tree", where, 0),
            Ore = f.Number(e, "ore", where, 0),
            Structure = f.Number(e, "structure", where, 0),
            Bleed = f.Number(e, "bleed", where, 0),
        };
    }

    private static BowStats Bow(JsonFields f, JsonElement e, string where)
    {
        f.OnlyKnown(e, where, "ammo", "draw", "speed", "damage");
        return new BowStats
        {
            AmmoKeys = f.Texts(e, "ammo", where),
            DrawSeconds = f.Number(e, "draw", where),
            SpeedScale = f.Number(e, "speed", where, 1),
            DamageScale = f.Number(e, "damage", where, 1),
        };
    }

    private static AmmoStats Ammo(JsonFields f, JsonElement e, string where)
    {
        f.OnlyKnown(e, where, "damage", "speed", "gravity", "bleed", "structure", "recover", "lifetime");
        return new AmmoStats
        {
            Damage = f.Number(e, "damage", where),
            Speed = f.Number(e, "speed", where),
            Gravity = f.Number(e, "gravity", where, 1),
            Bleed = f.Number(e, "bleed", where, 0),
            Structure = f.Number(e, "structure", where, 0),
            Recover = f.Number(e, "recover", where, 0),
            LifetimeSeconds = f.Number(e, "lifetime", where, 4),
        };
    }

    private static ConsumeStats Consume(JsonFields f, JsonElement e, string where)
    {
        f.OnlyKnown(e, where, "calories", "hydration", "health", "heal", "stopsBleeding", "poison");
        return new ConsumeStats
        {
            Calories = f.Number(e, "calories", where, 0),
            Hydration = f.Number(e, "hydration", where, 0),
            Health = f.Number(e, "health", where, 0),
            Heal = f.Number(e, "heal", where, 0),
            StopsBleeding = f.Flag(e, "stopsBleeding", where, false),
            Poison = f.Number(e, "poison", where, 0),
        };
    }

    private static WearStats Wear(JsonFields f, JsonElement e, string where)
    {
        f.OnlyKnown(e, where, "layers", "covers", "melee", "projectile", "insulation");
        WearLayer layers = WearLayer.None;
        foreach (string name in f.Texts(e, "layers", where))
        {
            WearLayer? layer = name switch
            {
                "head" => WearLayer.Head,
                "face" => WearLayer.Face,
                "torso" => WearLayer.Torso,
                "torso-armour" => WearLayer.TorsoArmour,
                "legs" => WearLayer.Legs,
                "legs-armour" => WearLayer.LegsArmour,
                "hands" => WearLayer.Hands,
                "feet" => WearLayer.Feet,
                _ => null,
            };
            if (layer == null)
            {
                f.Problem(where, $"unknown layer \"{name}\"");
            }
            layers |= layer ?? WearLayer.None;
        }
        BodyRegions covers = BodyRegions.None;
        foreach (string name in f.Texts(e, "covers", where))
        {
            int index = Array.IndexOf(GameContent.BodyRegionNames, name);
            if (index < 0)
            {
                f.Problem(where, $"unknown body region \"{name}\" (art/lib/hollow_art.py REGIONS)");
                continue;
            }
            covers |= (BodyRegions)(1 << index);
        }
        return new WearStats
        {
            Layers = layers,
            Covers = covers,
            Melee = f.Number(e, "melee", where, 0),
            Projectile = f.Number(e, "projectile", where, 0),
            Insulation = f.Number(e, "insulation", where, 0),
        };
    }

    private static FuelStats Fuel(JsonFields f, JsonElement e, string where)
    {
        f.OnlyKnown(e, where, "seconds", "byproduct");
        return new FuelStats
        {
            Seconds = f.Number(e, "seconds", where),
            ByproductKey = f.Has(e, "byproduct") ? f.Text(e, "byproduct", where) : null,
        };
    }

    private static CookStats Cook(JsonFields f, JsonElement e, string where)
    {
        f.OnlyKnown(e, where, "into", "seconds", "in");
        return new CookStats { IntoKey = f.Text(e, "into", where), Seconds = f.Number(e, "seconds", where), In = f.Text(e, "in", where) };
    }

    /// <summary>IDs unique; keys well formed; a category that exists; stacks, condition and each part's numbers in range.</summary>
    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        foreach (IGrouping<ushort, ItemDef> g in loaded.Items.GroupBy(i => i.Id).Where(g => g.Count() > 1))
        {
            c.Problem("items.json", $"id {g.Key} is used by {string.Join(", ", g.Select(i => i.Key))}");
        }
        var categories = new HashSet<string>(loaded.Categories.Select(x => x.Key), StringComparer.Ordinal);
        var cookers = new HashSet<string>(loaded.Deployables.Where(d => d.Device != null).Select(d => d.Device!.Cooks), StringComparer.Ordinal);
        foreach (ItemDef item in loaded.Items)
        {
            string where = $"items.json: \"{item.Key}\"";
            if (item.Key.Length == 0 || item.Key.Any(ch => !(char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '-')))
            {
                c.Problem(where, "keys are lower-case letters, digits and '-'");
            }
            if (!categories.Contains(item.Category))
            {
                c.Problem(where, $"category \"{item.Category}\" is not in categories.json");
            }
            if (item.StackSize < 1 || item.StackSize > 60_000)
            {
                c.Problem(where, $"stack {item.StackSize} must be 1 … 60000");
            }
            bool single = item.HasCondition || item.Wear != null || item.Melee != null || item.Bow != null || item.Deploys != null ||
                item.Firearm != null || item.Attachment != null || item.Ship != null;
            if (single && item.StackSize != 1)
            {
                c.Problem(where, "tools, weapons, clothing and deployables stack to 1");
            }
            if (item.MaxCondition < 0 || item.MaxCondition > 60_000)
            {
                c.Problem(where, "condition must be 0 … 60000");
            }
            if (item.Melee is { } m && (m.Reach <= 0 || m.Reach > 4 || m.SwingSeconds <= 0 || m.StrikeSeconds < 0 ||
                m.StrikeSeconds > m.SwingSeconds || m.Damage < 0 || m.Tree < 0 || m.Ore < 0 || m.Bleed is < 0 or > 2))
            {
                c.Problem(where, "melee: reach 0 … 4 m, swing > 0, strike within the swing, nothing negative");
            }
            if (item.Bow is { } bow && (bow.Ammo.Count == 0 || bow.Ammo.Any(a => a.Ammo == null) || bow.DrawSeconds <= 0))
            {
                c.Problem(where, "bow: needs ammo that is ammunition, and a draw time");
            }
            if (item.Ammo is { } ammo && (ammo.Speed is <= 0 or > 2000 || ammo.Damage < 0 || ammo.LifetimeSeconds is <= 0 or > 10 ||
                ammo.Recover is < 0 or > 1 || ammo.Gravity < 0))
            {
                c.Problem(where, "ammo: speed 0 … 2000 m/s, lifetime 0 … 10 s, recover 0 … 1");
            }
            if (item.Wear is { } wear && (wear.Layers == WearLayer.None || wear.Melee is < 0 or > 0.9f || wear.Projectile is < 0 or > 0.9f))
            {
                c.Problem(where, "wear: needs a layer; protection 0 … 0.9");
            }
            if (item.Fuel is { } fuel && fuel.Seconds <= 0)
            {
                c.Problem(where, "fuel: seconds must be positive");
            }
            if (item.Cook is { } cook)
            {
                if (cook.Seconds <= 0)
                {
                    c.Problem(where, "cook: seconds must be positive");
                }
                if (!cookers.Contains(cook.In))
                {
                    c.Problem(where, $"cook: no device cooks \"{cook.In}\"");
                }
                if (cook.Into == item)
                {
                    c.Problem(where, "cook: an item can't cook into itself");
                }
            }
        }
    }
}
