using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

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
        Assert.Equal(OutcomeCode.NotUsable, rig.Zone.Explosives().ThrowExplosive(ada, 0, 0, 0, 0, lob: false).Code, "a rock isn't thrown");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Explosives().ThrowExplosive(ada, 1, 0, 0, 0, lob: false).Code, "a grenade is");
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

    /// <summary>
    /// GROUND_SYSTEMS.md §7: the inventory's buttons come from the parts. items.json lists the actions and each has one
    /// handler; a stack offers what its parts' handlers offer (EAT food, USE medicine, WEAR and UNEQUIP clothing, LEARN a
    /// blueprint, UNLOAD and REMOVE on a gun), refused ones with why; the server does an action only when its handler
    /// offers it on that stack, enabled; over the network an action shows at once and the server agrees.
    /// </summary>
    [Test]
    public static void InventoryActionsComeFromParts()
    {
        GameContent c = ContentFixture.Content;
        Assert.Equal("use, eat, wear, unequip, learn, unload, remove", string.Join(", ", c.ItemActions.All.Select(a => a.Key)), "the list");
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0);
        Player a = rig.P(ada);
        rig.Step(2);
        string Offers(ContainerRef from, int slot)
        {
            var options = new List<ItemActionOption>();
            c.ItemActions.Offer(new TestActor(c, a), from, slot, options);
            return string.Join(", ", options.Select(o => o.Label + (o.Refusal is { } r ? $" ({r.Code})" : "")));
        }

        a.Inventory.Main.Set(0, ItemStack.Of(c.ItemByKey("bandage"), 2));
        a.Inventory.Main.Set(1, ItemStack.Of(c.ItemByKey("cooked-venison"), 1));
        a.Inventory.Main.Set(2, ItemStack.Of(c.ItemByKey("woven-shirt"), 1));
        a.Inventory.Main.Set(3, ItemStack.Of(c.ItemByKey("woven-shirt"), 1));
        Recipe pistol = c.RecipeByKey("pistol")!;
        a.Inventory.Main.Set(4, Hollow.Simulation.Economy.TechTrees.BlueprintOf(c, pistol));
        var ak = ItemStack.Of(c.ItemByKey("rifle"), 1);
        ak.Loaded = 30;
        ak.LoadedAmmo = c.ItemByKey("rifle-ammo").Id;
        ak.Sight = c.ItemByKey("holo-sight").Id;
        a.Inventory.Main.Set(5, ak);
        a.Inventory.Main.Set(6, ItemStack.Of(c.ItemByKey("rock"), 1));
        Assert.Equal("USE", Offers(ContainerRef.Main, 0), "medicine is used");
        Assert.Equal("EAT", Offers(ContainerRef.Main, 1), "food is eaten");
        Assert.Equal("WEAR", Offers(ContainerRef.Main, 2), "clothing is worn");
        Assert.Equal("LEARN", Offers(ContainerRef.Main, 4), "a blueprint is learnt");
        Assert.Equal("UNLOAD, REMOVE HOLO SIGHT", Offers(ContainerRef.Main, 5), "a gun's magazine and sight");
        Assert.Equal("", Offers(ContainerRef.Main, 6), "a rock offers nothing but DROP (the panel's own)");

        int Act(int slot, string action, int arg = 0, ContainerRef from = ContainerRef.Main) => (int)rig.Act(ada, from, slot, action, arg).Code;
        a.Vitals.Calories = 100;
        Assert.Equal((int)OutcomeCode.Done, Act(1, "eat"), "eaten");
        Assert.True(a.Vitals.Calories > 100 && a.Inventory.Main[1].IsEmpty, "it fed, and it's gone");
        Assert.Equal((int)OutcomeCode.NotUsable, Act(0, "eat"), "medicine isn't eaten: the handler doesn't offer it");
        Assert.Equal((int)OutcomeCode.Done, Act(2, "wear"), "worn");
        Assert.True(a.Inventory.Wear.Slots.ToArray().Any(s => s.Item == c.ItemByKey("woven-shirt").Id), "on");
        Assert.Equal("WEAR (Worn)", Offers(ContainerRef.Main, 3), "a second shirt: something is worn there already");
        Assert.Equal((int)OutcomeCode.Worn, Act(3, "wear"), "and refused so");
        int worn = Enumerable.Range(0, a.Inventory.Wear.Size).First(i => !a.Inventory.Wear[i].IsEmpty);
        Assert.Equal("UNEQUIP", Offers(ContainerRef.Wear, worn), "worn: taken off");
        Assert.Equal((int)OutcomeCode.Done, Act(worn, "unequip", from: ContainerRef.Wear), "off");
        Assert.Equal((int)OutcomeCode.Done, Act(4, "learn"), "learnt");
        Assert.True(a.Blueprints.Knows(pistol) && a.Inventory.Main[4].IsEmpty, "known, and used up");
        a.Inventory.Main.Set(4, Hollow.Simulation.Economy.TechTrees.BlueprintOf(c, pistol));
        Assert.Equal("LEARN (AlreadyKnown)", Offers(ContainerRef.Main, 4), "a known blueprint");
        Assert.Equal((int)OutcomeCode.Done, Act(5, "unload"), "unloaded");
        Assert.True(a.Inventory.Main[5].Loaded == 0 && a.Inventory.Count(c.ItemByKey("rifle-ammo")) == 30, "the rounds are back");
        Assert.Equal("UNLOAD (Empty), REMOVE HOLO SIGHT", Offers(ContainerRef.Main, 5), "nothing to unload");
        Assert.Equal((int)OutcomeCode.Done, Act(5, "remove", (int)AttachmentSlot.Sight), "the sight off");
        Assert.True(a.Inventory.Main[5].Sight == 0 && a.Inventory.Count(c.ItemByKey("holo-sight")) == 1, "into the inventory");
        Assert.Equal((int)OutcomeCode.NotUsable, Act(6, "learn"), "a rock isn't learnt");
        Assert.Equal((int)OutcomeCode.Invalid, (int)rig.Zone.Actions().ItemAction(ada, ContainerRef.Main, 0, 0, 99, 0).Code, "no such action");
        Assert.Equal((int)OutcomeCode.NotAllowed, Act(0, "use", from: ContainerRef.Loot), "only the player's own slots");

        // Over the network: shown at once, confirmed; a refused one says why.
        using var net = new LoopbackRig();
        GameClient bo = net.Join("Bo");
        net.WaitUntilPlaying();
        LocalPlayer me = bo.Local;
        net.Server.Execute("give Bo bandage 2");
        Loopback.RunUntil(net, () => me.Inventory.Count(c.ItemByKey("bandage")) == 2, "the bandages");
        int bandage = Enumerable.Range(0, 24).First(i => me.Inventory.Main[i].Item == c.ItemByKey("bandage").Id);
        var answers = new List<IntentAnswer>();
        me.IntentAnswered += answers.Add;
        uint used = me.UsePart(ContainerRef.Main, bandage, ItemPart.Consumable);
        Assert.Equal(1, me.Inventory.Count(c.ItemByKey("bandage")), "used at once");
        Loopback.RunUntil(net, () => answers.Any(x => x.Seq == used), "the answer");
        Assert.True(answers.Single(x => x.Seq == used).Done && me.Inventory.Count(c.ItemByKey("bandage")) == 1 && me.PendingCount == 0, "and confirmed");
        uint wrong = me.ItemAction(ContainerRef.Main, bandage, c.ItemActions["learn"]!.Id);
        Loopback.RunUntil(net, () => answers.Any(x => x.Seq == wrong), "the refusal");
        Assert.Equal(OutcomeCode.NotUsable, answers.Single(x => x.Seq == wrong).Code, "a bandage isn't learnt");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §7: UNLOAD returns a gun's rounds. The gun in hand's goes on its own timeline (in order with its
    /// shots and reloads), refused while reloading or empty; over the network it shows at once and the server agrees, and a
    /// gun in the main slots is unloaded by an item action, predicted and confirmed the same way.
    /// </summary>
    [Test]
    public static void UnloadReturnsTheRounds()
    {
        GameContent c = ContentFixture.Content;
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0);
        Player a = rig.P(ada);
        Guns.Arm(rig, ada, "rifle", "rifle-ammo", 30);
        Assert.Equal(OutcomeCode.Done, Guns.Queue(rig, ada, 1, GunActionKind.Unload), "queued");
        rig.Step(2, () => rig.Ch(ada).Actions = new ActionInput { HeldSlot = 0 });
        Assert.True(a.Inventory.Belt[0].Loaded == 0 && a.Inventory.Count(c.ItemByKey("rifle-ammo")) == 30 && a.GunRefusedSeq != 1,
            "the magazine out, the rounds in the inventory");
        Assert.Equal(c.ItemByKey("rifle-ammo").Id, a.Inventory.Belt[0].LoadedAmmo, "the kind stays named, for the next reload");
        Guns.Queue(rig, ada, 2, GunActionKind.Unload);
        rig.Step(2, () => rig.Ch(ada).Actions = new ActionInput { HeldSlot = 0 });
        Assert.True(a.GunRefusedSeq == 2 && a.GunRefused == OutcomeCode.Empty, "nothing to unload");
        Guns.Queue(rig, ada, 3, GunActionKind.Reload);
        Guns.Queue(rig, ada, 4, GunActionKind.Unload);
        rig.Step(2, () => rig.Ch(ada).Actions = new ActionInput { HeldSlot = 0 });
        Assert.True(a.Gun.Reloading && a.GunRefusedSeq == 4 && a.GunRefused == OutcomeCode.Reloading, "not while reloading");

        // Over the network: in hand on the gun's timeline, and from the main slots by an item action.
        using var net = new LoopbackRig();
        GameClient bo = net.Join("Bo");
        net.WaitUntilPlaying();
        LocalPlayer me = bo.Local;
        net.Server.Execute("give Bo rifle 2");
        net.Server.Execute("give Bo rifle-ammo 60");
        Loopback.RunUntil(net, () => me.Inventory.Count(c.ItemByKey("rifle")) == 2 && me.Inventory.Count(c.ItemByKey("rifle-ammo")) == 60, "the rifles");
        int[] rifles = Enumerable.Range(0, 24).Where(i => me.Inventory.Main[i].Item == c.ItemByKey("rifle").Id).ToArray();
        me.MoveItem(ContainerRef.Main, rifles[0], ContainerRef.Belt, 1);
        me.HeldSlot = 1;
        Loopback.RunUntil(net, () => me.Gun.Ready, "the rifle in hand");
        me.Reload();
        Loopback.RunUntil(net, () => me.Gun.Loaded == 30 && !me.Gun.Reloading && me.Gun.PendingCount == 0, "loaded", 8);
        Loopback.RunUntil(net, () => me.Inventory.Count(c.ItemByKey("rifle-ammo")) == 30, "the rounds taken");
        var unloaded = new List<UnloadEvent>();
        me.Gun.Unloaded += unloaded.Add;
        me.ItemAction(ContainerRef.Belt, 1, c.ItemActions["unload"]!.Id);
        net.Run(0.1, (_, _) => default);
        Assert.True(me.Gun.Loaded == 0 && unloaded.Count == 1 && unloaded[0].Rounds == 30, "unloaded at once, on the gun's timeline");
        Loopback.RunUntil(net, () => me.Gun.PendingCount == 0 && me.Inventory.Count(c.ItemByKey("rifle-ammo")) == 60, "the server agrees");
        int serverLoaded = net.Server.Inspect(sv =>
        {
            sv.Zone.Players().TryFindPlayer(bo.EntityId, out _, out Player p);
            return (int)p.Inventory.Belt[1].Loaded + 1000 * p.Inventory.Count(c.ItemByKey("rifle-ammo"));
        });
        Assert.Equal(60_000, serverLoaded, "the server's magazine empty and the rounds back");

        // The other rifle, loaded on the server (as if found loaded), unloaded from the main slots.
        net.Server.Inspect(sv =>
        {
            sv.Zone.Players().TryFindPlayer(bo.EntityId, out _, out Player p);
            ItemStack spare = p.Inventory.Main[rifles[1]];
            spare.Loaded = 12;
            spare.LoadedAmmo = c.ItemByKey("rifle-ammo-hv").Id;
            p.Inventory.Main.Set(rifles[1], spare);
            return 0;
        });
        Loopback.RunUntil(net, () => me.Inventory.Main[rifles[1]].Loaded == 12, "the spare's magazine");
        uint seq = me.ItemAction(ContainerRef.Main, rifles[1], c.ItemActions["unload"]!.Id);
        Assert.True(seq != 0 && me.Inventory.Main[rifles[1]].Loaded == 0 && me.Inventory.Count(c.ItemByKey("rifle-ammo-hv")) == 12, "predicted");
        Loopback.RunUntil(net, () => me.PendingCount == 0, "answered");
        Assert.True(me.Inventory.Main[rifles[1]].Loaded == 0 && me.Inventory.Count(c.ItemByKey("rifle-ammo-hv")) == 12, "and confirmed");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §7: what first person shows comes from the model catalogue, not from item keys. Every item whose
    /// held behaviour is shown in the hands has a held pose (no placeholder is drawn any more); every gun names the arms'
    /// pose (pistol or rifle) the views used to pick by its key; the torch's flame is its catalogue entry's.
    /// </summary>
    [Test]
    public static void HeldItemsShowFromTheCatalogue()
    {
        GameContent c = ContentFixture.Content;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures.Root, "game", "data", "ground", "models.json")));
        var held = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);
        foreach (System.Text.Json.JsonElement m in doc.RootElement.GetProperty("models").EnumerateArray())
        {
            if (m.TryGetProperty("held", out System.Text.Json.JsonElement h))
            {
                held[m.GetProperty("key").GetString()!] = h;
            }
        }
        HeldUse[] shown = { HeldUse.Melee, HeldUse.Bow, HeldUse.Firearm, HeldUse.Throwable, HeldUse.Planner, HeldUse.Hammer, HeldUse.Consumable };
        var missing = c.Items.Where(i => shown.Contains(i.Held) && !held.ContainsKey(i.Key)).Select(i => i.Key).ToList();
        Assert.True(missing.Count == 0, "items held without a held pose: " + string.Join(", ", missing));
        foreach (ItemDef gun in c.Items.Where(i => i.Held == HeldUse.Firearm))
        {
            string pose = held[gun.Key].TryGetProperty("pose", out System.Text.Json.JsonElement p) ? p.GetString() ?? "" : "";
            Assert.True(pose is "pistol" or "rifle", $"{gun.Key} names the arms' pose ({pose})");
        }
        Assert.Equal("fire.torch", held["torch"].GetProperty("flame").GetString(), "the torch burns by its catalogue entry");
        // No view picks what to draw by an item's key any more.
        foreach (string file in new[] { "Ground/FirstPersonView.cs", "Characters/HeldItems.cs", "Ground/GroundView.cs" })
        {
            string code = File.ReadAllText(Path.Combine(Fixtures.Root, "game", "scripts", file));
            Assert.True(!System.Text.RegularExpressions.Regex.IsMatch(code, @"Key\s*[!=]=\s*""(torch|rifle|pistol)"""), $"{file} names no item key");
        }
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §7: a workbench above a recipe's level crafts it faster (recipes.json, Rust's: one level above half
    /// the time, two or more a quarter), a station's own recipe (the Tin Finch) keeps its time, and a job keeps the time it
    /// started with when the workbench goes.
    /// </summary>
    [Test]
    public static void AWorkbenchAboveTheRecipeCraftsFaster()
    {
        GameContent c = ContentFixture.Content;
        Assert.Equal("1, 0.5, 0.25", string.Join(", ", c.WorkbenchSpeed.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))), "Rust's speeds");
        Recipe spear = c.RecipeByKey("wooden-spear")!;
        Recipe levelOne = c.Recipes.First(r => r.Workbench == 1 && r.Station == null);
        Recipe ship = c.Recipes.First(r => r.Station != null);
        Assert.True(spear.Workbench == 0, "a spear needs no workbench");
        int[] spearTicks = Enumerable.Range(0, 4).Select(level => CraftingSpeed.UnitTicks(c, spear, level)).ToArray();
        Fixtures.Report($"a wooden spear: {string.Join(", ", spearTicks.Select(t => $"{t / 30.0:0.##} s"))} at workbenches 0-3; {levelOne.Key} (level 1): " +
            $"{string.Join(", ", Enumerable.Range(1, 3).Select(l => $"{CraftingSpeed.Seconds(c, levelOne, l):0.##} s"))} at 1-3");
        Assert.True(spearTicks[0] == spear.UnitTicks && Math.Abs(spearTicks[1] - spear.UnitTicks / 2.0) <= 0.5 &&
            Math.Abs(spearTicks[2] - spear.UnitTicks / 4.0) <= 0.5 && spearTicks[3] == spearTicks[2], "all, a half, a quarter, a quarter");
        Assert.True(CraftingSpeed.UnitTicks(c, levelOne, 1) == levelOne.UnitTicks && Math.Abs(CraftingSpeed.UnitTicks(c, levelOne, 2) - levelOne.UnitTicks / 2.0) <= 0.5,
            "a level 1 recipe: all its time at a level 1 workbench, half at a level 2");
        Assert.Equal(ship.UnitTicks, CraftingSpeed.UnitTicks(c, ship, 3), "a station's recipe keeps its own time");

        // In the zone: a spear started beside a level 2 workbench takes a quarter of its time, even once the bench is gone.
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 30, 30);
        Player a = rig.P(ada);
        rig.Give(ada, "wood", 500);
        var bench = new Deployable(rig.Zone.NextId(), c.Deployable("workbench-2")!, rig.Zone.GroundAt(31.5f, 30),
            rig.Zone.Frame.UpAt(rig.Zone.GroundAt(31.5f, 30)), 0, a.Id);
        rig.Zone.Things().AddStatic(bench);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().Craft(ada, spear.Id, 1, out _), "crafting");
        Assert.Equal(spearTicks[2], a.Crafting.Jobs[0].UnitTicks, "a quarter of the time");
        rig.Zone.Things().RemoveStatic(bench);
        int ticks = 0;
        while (a.Inventory.Count(c.ItemByKey("wooden-spear")) == 0 && ticks < spear.UnitTicks + 10)
        {
            rig.Step();
            ticks++;
        }
        Assert.InRange(spearTicks[2] - 2, spearTicks[2] + 2, ticks, "done at the time it started with");
    }

    /// <summary>The player an action acts for, as a test sees them (no effects besides the items).</summary>
    private sealed class TestActor(GameContent content, Player p) : IItemActor
    {
        public GameContent Content => content;

        public PlayerInventory Inventory => p.Inventory;

        public BlueprintBook Blueprints => p.Blueprints;

        public bool Reloading(ContainerRef from, int slot) => false;

        public void Consumed(ItemDef item)
        {
        }

        public void Learned(Recipe recipe)
        {
        }

        public void MagazineChanged(ContainerRef from, int slot)
        {
        }
    }
}

/// <summary>Running a loopback game until something happens.</summary>
public static class Loopback
{
    public static void RunUntil(LoopbackRig rig, Func<bool> done, string what, double seconds = 5)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < seconds, $"timed out waiting for {what}");
            rig.Run(0.05, (_, _) => default);
        }
    }
}
