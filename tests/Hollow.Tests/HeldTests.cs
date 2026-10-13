using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;

namespace Hollow.Tests;

/// <summary>Held items, item information and inventory actions (GROUND_SYSTEMS.md §7, package P3), engine-free.</summary>
public static class HeldTests
{
    /// <summary>
    /// GROUND_SYSTEMS.md §7: what an item does in the hands comes from its parts. Every part is in the table in its order;
    /// each held behaviour is given by the part that has it (a deployable, a door, a lock, the cupboard and a ship all
    /// place); an item two of whose parts would each give one is refused when the data is read; an intent a held behaviour
    /// sends is checked by that behaviour (a throw of something that isn't thrown is refused).
    /// </summary>
    [Test]
    public static void HeldBehaviourComesFromOnePart()
    {
        GameContent c = ContentFixture.Content;
        foreach (ItemPart part in Enum.GetValues<ItemPart>())
        {
            Assert.Equal(part, ItemPartTable.Of(part).Part, "the part table is in the parts' order");
        }
        (string Item, HeldUse Use, ItemPart? Part)[] expected =
        {
            ("rock", HeldUse.Melee, ItemPart.Melee), ("bow", HeldUse.Bow, ItemPart.Bow), ("rifle", HeldUse.Firearm, ItemPart.Firearm),
            ("bag-charge", HeldUse.Throwable, ItemPart.Throwable), ("campfire", HeldUse.Placer, ItemPart.Deployable),
            ("wooden-door", HeldUse.Placer, ItemPart.Door), ("code-lock", HeldUse.Placer, ItemPart.Lock),
            ("claim-device", HeldUse.Placer, ItemPart.Cupboard), ("tin-finch", HeldUse.Placer, ItemPart.Ship),
            ("building-plan", HeldUse.Planner, ItemPart.Plan), ("hammer", HeldUse.Hammer, ItemPart.Hammer),
            ("bandage", HeldUse.Consumable, ItemPart.Consumable), ("blueprint", HeldUse.Blueprint, ItemPart.Blueprint),
            ("wood", HeldUse.None, null), ("woven-shirt", HeldUse.None, null), ("rifle-ammo", HeldUse.None, null),
        };
        foreach ((string key, HeldUse use, ItemPart? part) in expected)
        {
            ItemDef item = c.ItemByKey(key);
            Assert.Equal(use, item.Held, $"{key} in the hands");
            Assert.Equal(part, item.HeldPart, $"{key}'s held part");
        }
        Assert.True(c.ItemByKey("wood").Parts.Has(ItemPart.Fuel) && c.ItemByKey("wood").Parts.Has(ItemPart.Component), "wood burns and makes things");
        Assert.True(c.ItemByKey("gears").Parts.Has(ItemPart.Component) && c.ItemByKey("gears").UsedIn.Count > 0, "gears are a component");
        Assert.True(c.ItemByKey("wild-tuber").Parts.Has(ItemPart.Consumable) && c.ItemByKey("wild-tuber").Parts.Has(ItemPart.Cookable),
            "a tuber is eaten and cooked: parts without a held behaviour go with one that has");
        Fixtures.Report($"{c.Items.Count(i => i.Held != HeldUse.None)} of {c.Items.Count} items do something in the hands");

        // Two parts that would each say what the hands do: the data is refused.
        Dictionary<string, string> files = GameContent.Files.Concat(GameContent.Unhashed)
            .ToDictionary(f => f, f => File.ReadAllText(Path.Combine(ContentFixture.Folder, f)));
        const string bow = "\"key\": \"bow\",";
        Assert.True(files["items.json"].Contains(bow, StringComparison.Ordinal), "the bow is in items.json");
        files["items.json"] = files["items.json"].Replace(bow,
            bow + " \"melee\": { \"damage\": 5, \"reach\": 1.5, \"swing\": 0.8, \"strike\": 0.2 },", StringComparison.Ordinal);
        string problems = string.Join("\n", GameContent.FromTexts(files).Problems);
        Fixtures.Report(problems.Split('\n').FirstOrDefault(p => p.Contains("held behaviour", StringComparison.Ordinal)) ?? "(none)");
        Assert.True(problems.Contains("items.json: \"bow\": its items.json melee and its items.json bow would both say what it does in the hands",
            StringComparison.Ordinal), "a melee bow is refused");

        // An intent is checked by the behaviour that sends it: throwing a rock is refused, throwing a grenade isn't.
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0);
        rig.P(ada).Inventory.Belt.Set(0, ItemStack.Of(c.ItemByKey("rock"), 1));
        rig.P(ada).Inventory.Belt.Set(1, ItemStack.Of(c.ItemByKey("tin-grenade"), 1));
        rig.Step(2);
        Assert.Equal(OutcomeCode.NotUsable, rig.Zone.ThrowExplosive(ada, 0, 0, 0, 0, lob: false).Code, "a rock isn't thrown");
        Assert.Equal(OutcomeCode.Done, rig.Zone.ThrowExplosive(ada, 1, 0, 0, 0, lob: false).Code, "a grenade is");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §7: an item's information comes from a describer per part, every part has one, and every item
    /// that has a part shows something. The AK says its damage, rate of fire, magazine, reload, aim cone, range and recoil,
    /// and its stack what is loaded and fitted; the bag charge (the owner's example of what was missing) its fuse, blast and
    /// how many it takes to break each wall and door; a blueprint what it teaches, and whether the reader knows it.
    /// </summary>
    [Test]
    public static void EveryPartIsDescribed()
    {
        GameContent c = ContentFixture.Content;
        Assert.Equal(Enum.GetValues<ItemPart>().Length, ItemRows.Create().Handlers.Count(), "a describer for every part");
        var empty = c.Items.Where(i => !i.Parts.IsEmpty && ItemRows.Describe(c, i).Count == 0).Select(i => i.Key).ToList();
        Assert.True(empty.Count == 0, "items with parts but nothing to say: " + string.Join(", ", empty));
        Fixtures.Report($"{c.Items.Count(i => i.Parts.IsEmpty)} items have no part ({string.Join(", ", c.Items.Where(i => i.Parts.IsEmpty).Select(i => i.Key))})");

        string Row(List<ItemRow> rows, string label) => rows.FirstOrDefault(r => r.Label == label).Value ?? "(none)";
        ItemDef rifle = c.ItemByKey("rifle");
        var ak = ItemStack.Of(rifle, 1);
        ak.Loaded = 30;
        ak.LoadedAmmo = c.ItemByKey("rifle-ammo").Id;
        ak.Sight = c.ItemByKey("holo-sight").Id;
        List<ItemRow> akRows = ItemRows.Describe(c, rifle, ak);
        Fixtures.Report("AK: " + string.Join("; ", akRows.Select(r => $"{r.Label} {r.Value}")));
        Assert.Equal("35", Row(akRows, "Damage"), "damage");
        Assert.Equal("Auto, 450 rpm", Row(akRows, "Fire"), "rate of fire");
        Assert.Equal("30", Row(akRows, "Magazine"), "magazine");
        Assert.Equal("2.4 s", Row(akRows, "Reload"), "reload");
        Assert.Equal("1.5° hip, 0.18° aimed", Row(akRows, "Aim cone"), "aim cone, the holo sight's narrower aimed");
        Assert.Equal("200 m, full to 70 m", Row(akRows, "Range"), "range");
        Assert.Equal("29° up, a set pattern", Row(akRows, "Recoil"), "recoil");
        Assert.True(Row(akRows, "Loaded").StartsWith("30 ", StringComparison.Ordinal) && Row(akRows, "Fitted") == c.ItemByKey("holo-sight").Name.ToLowerInvariant(),
            "the stack's magazine and sight");
        Assert.Equal("100%", Row(akRows, "Condition"), "its condition");

        List<ItemRow> charge = ItemRows.Describe(c, c.ItemByKey("bag-charge"));
        Fixtures.Report("bag charge: " + string.Join("; ", charge.Select(r => $"{r.Label} {r.Value}")));
        Assert.Equal("6–12 s, 20% duds", Row(charge, "Fuse"), "fuse");
        Assert.Equal("3 m, 120 damage", Row(charge, "Blast"), "blast");
        Assert.Equal("3 to break", Row(charge, "Wood wall"), "200 / 75");
        Assert.Equal("10 to break", Row(charge, "Stone wall"), "500 / 51");
        Assert.Equal("46 to break", Row(charge, "Armoured wall"), "2000 / 44");
        Assert.Equal("wood 2, metal 4, armoured 12", Row(charge, "Doors"), "the single doors");

        ItemDef blueprint = c.World.Blueprint!;
        Recipe pistol = c.RecipeByKey("pistol")!;
        var book = new BlueprintBook();
        List<ItemRow> teaches = ItemRows.Describe(c, blueprint, new ItemStack(blueprint.Id, 1, pistol.Id), book);
        Assert.True(Row(teaches, "Teaches") == pistol.Output.Name && Row(teaches, "Known") == "Not yet", "a blueprint says what it teaches");
        book.Learn(pistol);
        Assert.Equal("Yes", Row(ItemRows.Describe(c, blueprint, new ItemStack(blueprint.Id, 1, pistol.Id), book), "Known"), "and that it is known");
        Assert.True(Row(ItemRows.Describe(c, c.ItemByKey("gears")), "Recycles into").Contains("salvage", StringComparison.Ordinal), "a component's salvage");
    }
}
