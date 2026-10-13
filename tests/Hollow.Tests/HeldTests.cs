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
}
