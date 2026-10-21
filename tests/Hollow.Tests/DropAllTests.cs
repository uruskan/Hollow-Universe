using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class DropAllTests
{
    private static ItemStack Weapon(GameContent content)
    {
        ItemStack gun = ItemStack.Of(content.ItemByKey("rifle"), 1);
        gun.Condition = 123;
        gun.Loaded = 17;
        gun.LoadedAmmo = content.ItemByKey("rifle-ammo-hv").Id;
        gun.Sight = content.ItemByKey("scope").Id;
        gun.Muzzle = content.ItemByKey("silencer").Id;
        return gun;
    }

    [Test]
    public static void ChangedOrNewStacksRejectTheWholeDrop()
    {
        GameContent content = ContentFixture.Content;
        var main = new Container(ContainerKind.Main, PlayerInventory.MainSlots);
        ItemStack gun = Weapon(content);
        main.Set(0, ItemStack.Of(content.ItemByKey("wood"), 300));
        main.Set(22, gun);
        ItemStack[] expected = main.Slots.ToArray();
        var removed = new List<ItemStack>();

        // A change near the end must not lose an earlier stack. Same-item changes count too.
        var changed = new[]
        {
            gun.WithCount(2),
            new ItemStack(gun.Item, gun.Count, 122) { Loaded = gun.Loaded, LoadedAmmo = gun.LoadedAmmo, Sight = gun.Sight, Muzzle = gun.Muzzle },
            new ItemStack(gun.Item, gun.Count, gun.Condition) { Loaded = 16, LoadedAmmo = gun.LoadedAmmo, Sight = gun.Sight, Muzzle = gun.Muzzle },
            new ItemStack(gun.Item, gun.Count, gun.Condition) { Loaded = gun.Loaded, LoadedAmmo = gun.LoadedAmmo, Muzzle = gun.Muzzle },
        };
        foreach (ItemStack stack in changed)
        {
            main.Set(22, stack);
            Assert.Equal(OutcomeCode.Stale, InventoryRules.RemoveAll(main, expected, removed).Code, "changed stack rejected");
            Assert.True(main[0] == expected[0] && main[22] == stack && removed.Count == 0, "all stacks retained after rejection");
        }
        main.Set(22, gun);
        main.Set(23, ItemStack.Of(content.ItemByKey("stone"), 45));
        Assert.Equal(OutcomeCode.Stale, InventoryRules.RemoveAll(main, expected, removed).Code, "new item in formerly empty slot is protected");
        Assert.True(main[0] == expected[0] && main[22] == gun && main[23].Count == 45, "new and original items retained");
        Assert.Equal(OutcomeCode.Invalid, InventoryRules.RemoveAll(main, Array.Empty<ItemStack>(), removed).Code, "incomplete snapshot rejected");
        Assert.True(main[0] == expected[0] && main[23].Count == 45, "malformed request changes nothing");
    }

    [Test]
    public static void BackpackDropsIntoTheExistingBagKeepingHotbarClothingAndLoot()
    {
        using var rig = new GameRig();
        Entity entity = rig.Add("Ada", 30, 30);
        Player player = rig.P(entity);
        ItemStack gun = Weapon(rig.C);
        for (int i = 0; i < player.Inventory.Main.Size; i++)
            player.Inventory.Main.Set(i, ItemStack.Of(rig.C.ItemByKey("wood"), i + 1));
        player.Inventory.Main.Set(23, gun);
        for (int i = 0; i < player.Inventory.Belt.Size; i++)
            player.Inventory.Belt.Set(i, ItemStack.Of(rig.C.ItemByKey("rock"), 1));
        player.Inventory.Wear.Set(0, ItemStack.Of(rig.C.Items.First(item => item.Wear != null), 1));
        ItemStack[] belt = player.Inventory.Belt.Slots.ToArray(), wear = player.Inventory.Wear.Slots.ToArray();
        var at = rig.Zone.GroundAt(31, 30);
        var box = new Deployable(rig.Zone.NextId(), rig.C.Deployable("large-box")!, at, rig.Zone.Frame.UpAt(at), 0, player.Id);
        box.Container!.Set(0, ItemStack.Of(rig.C.ItemByKey("stone"), 88));
        rig.Zone.Things().AddStatic(box);
        player.Looting = box.Id;
        rig.Zone.Spill().Drop(entity, ItemStack.Of(rig.C.ItemByKey("wood"), 10));
        DroppedBag bag = rig.Zone.Things().Statics().OfType<DroppedBag>().Single();

        ItemStack[] expected = player.Inventory.Main.Slots.ToArray();
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().DropAll(entity, expected).Code, "backpack dropped");
        Assert.Equal(OutcomeCode.Stale, rig.Zone.Actions().DropAll(entity, expected).Code, "replaying the same snapshot cannot drop it twice");
        Assert.True(player.Inventory.Main.IsEmpty, "all 24 backpack slots cleared");
        Assert.True(player.Inventory.Belt.Slots.ToArray().SequenceEqual(belt) && player.Inventory.Wear.Slots.ToArray().SequenceEqual(wear),
            "all six hotbar slots and worn clothing retained");
        Assert.True(box.Container[0].Count == 88 && player.Looting == box.Id, "open storage and loot session retained");
        Assert.Equal(1, rig.Zone.Things().Statics().OfType<DroppedBag>().Count(), "new drops merged into the existing nearby bag");
        Assert.Equal(286, InventoryRules.Count(new[] { bag.Loot }, rig.C.ItemByKey("wood").Id), "all wood conserved including earlier drop");
        Assert.True(bag.Loot.Slots.ToArray().Any(stack => stack == gun), "weapon retains condition, magazine, ammunition and attachments");
        Assert.Near(0, rig.Zone.HeightAboveGround(bag.Position), 0.05, "loot bag rests on ground");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().DropAll(entity, player.Inventory.Main.Slots.ToArray()).Code, "empty backpack is a no-op");
        Assert.Equal(1, rig.Zone.Things().Statics().OfType<DroppedBag>().Count(), "empty backpack creates no bag");

        player.Inventory.Main.Set(0, expected[0]);
        player.Wounded = true;
        Assert.Equal(OutcomeCode.Dead, rig.Zone.Actions().DropAll(entity, player.Inventory.Main.Slots.ToArray()).Code, "wounded player cannot drop");
        Assert.True(player.Inventory.Main[0] == expected[0], "refused action retains items");
    }

    [Test]
    public static void FullBackpackUsesOneIntentAndStalePredictionRollsBack()
    {
        using var rig = new LoopbackRig();
        GameClient client = rig.Join("Dropper");
        rig.WaitUntilPlaying();
        LocalPlayer me = client.Local;
        ItemStack gun = Weapon(client.Content);
        ItemStack worn = ItemStack.Of(client.Content.Items.First(item => item.Wear != null), 1);
        var answers = new List<IntentAnswer>();
        me.IntentAnswered += answers.Add;
        rig.Server.Inspect(server =>
        {
            server.Zone.Players().TryFindPlayer(client.EntityId, out _, out Player p);
            p.Inventory.Clear();
            for (int i = 0; i < PlayerInventory.MainSlots; i++)
                p.Inventory.Main.Set(i, ItemStack.Of(client.Content.ItemByKey("wood"), i + 1));
            p.Inventory.Main.Set(23, gun);
            for (int i = 0; i < PlayerInventory.BeltSlots; i++)
                p.Inventory.Belt.Set(i, ItemStack.Of(client.Content.ItemByKey("rock"), 1));
            p.Inventory.Wear.Set(0, worn);
            return 0;
        });
        Until(rig, () => me.Inventory.Main[22].Count == 23 && me.Inventory.Main[23] == gun && me.Inventory.Wear[0] == worn,
            "full backpack received");
        ItemStack[] belt = me.Inventory.Belt.Slots.ToArray();
        long accepted = rig.Server.Inspect(server => server.Players.Single().IntentsAccepted);
        uint seq = me.DropAll();
        Assert.True(seq != 0 && me.Inventory.Main.IsEmpty, "whole backpack predicted immediately");
        Assert.Equal(0u, me.DropAll(), "immediate double click sends no second intent");
        Assert.Equal(1, me.PendingCount, "double click keeps one pending transaction");
        Assert.True(me.Inventory.Belt.Slots.ToArray().SequenceEqual(belt) && me.Inventory.Wear[0] == worn, "prediction retains hotbar and clothing");
        Until(rig, () => answers.Any(answer => answer.Seq == seq) && me.PendingCount == 0, "bulk drop response");
        Assert.Equal(OutcomeCode.Done, answers.Single(answer => answer.Seq == seq).Code, "full backpack accepted beyond 16-slot burst limit");
        rig.Server.Inspect(server =>
        {
            Assert.Equal(accepted + 1, server.Players.Single().IntentsAccepted, "one accepted intent for 24 slots");
            Assert.Equal(0L, server.Players.Single().IntentsRateLimited, "no individual-slot rate limiting");
            server.Zone.Players().TryFindPlayer(client.EntityId, out _, out Player p);
            Assert.True(p.Inventory.Main.IsEmpty && p.Inventory.Belt.Slots.ToArray().SequenceEqual(belt) && p.Inventory.Wear[0] == worn,
                "server retains exactly the excluded containers");
            DroppedBag bag = server.Zone.Things().Statics().OfType<DroppedBag>().Single();
            Assert.True(bag.Loot.Slots.ToArray().Any(stack => stack == gun), "weapon metadata survived intent and spill");
            Assert.Equal(276, InventoryRules.Count(new[] { bag.Loot }, client.Content.ItemByKey("wood").Id), "all backpack resources reached ground");
            p.Inventory.Main.Set(0, ItemStack.Of(client.Content.ItemByKey("stone"), 45));
            p.Inventory.Main.Set(23, gun);
            return 0;
        });
        Until(rig, () => me.Inventory.Main[0].Count == 45 && me.Inventory.Main[23] == gun, "next inventory received");
        ItemStack changed = gun;
        changed.Loaded--;
        rig.Server.Inspect(server =>
        {
            server.Zone.Players().TryFindPlayer(client.EntityId, out _, out Player p);
            p.Inventory.Main.Set(23, changed);
            return 0;
        });
        seq = me.DropAll();
        Assert.True(me.Inventory.Main.IsEmpty, "stale request initially predicted");
        Until(rig, () => answers.Any(answer => answer.Seq == seq) && me.PendingCount == 0 && me.Inventory.Main[23] == changed,
            "authoritative rollback");
        Assert.Equal(OutcomeCode.Stale, answers.Single(answer => answer.Seq == seq).Code, "changed same-item weapon rejects whole transaction");
        Assert.Equal(45, me.Inventory.Main[0].Count, "earlier stack restored too");
        rig.Server.Inspect(server =>
        {
            Container[] bags = server.Zone.Things().Statics().OfType<DroppedBag>().Select(bag => bag.Loot).ToArray();
            Assert.Equal(0, InventoryRules.Count(bags, client.Content.ItemByKey("stone").Id), "rejected drop added nothing to ground");
            Assert.Equal(1, InventoryRules.Count(bags, gun.Item), "rejected prediction did not duplicate weapon");
            return 0;
        });

        // Bypass normal UI prediction to exercise actual server handling of untrusted and repeated packets.
        ItemStack[] captured = me.Inventory.Main.Slots.ToArray();
        SlotNet[] snapshot = captured.Select(stack => SlotSnapshot.Of(stack)).ToArray();
        SlotNet[] overflow = snapshot.ToArray();
        overflow[0].Count = uint.MaxValue;
        SlotNet[] forged = snapshot.ToArray();
        forged[0].Count++;
        Assert.True(client.SendReliable(new DropAllIntent { Seq = 100, Expected = snapshot.Take(23).ToArray() }), "send short snapshot");
        Assert.True(client.SendReliable(new DropAllIntent { Seq = 101, Expected = overflow }), "send out-of-range count");
        Assert.True(client.SendReliable(new DropAllIntent { Seq = 102, Expected = forged }), "send forged stack count");
        var replayed = new DropAllIntent { Seq = 103, Expected = snapshot };
        Assert.True(client.SendReliable(replayed) && client.SendReliable(replayed), "send identical valid request twice");
        Until(rig, () => answers.Count(answer => answer.Seq >= 100) == 5 && me.Inventory.Main.IsEmpty, "untrusted and replayed responses");
        Assert.Equal(OutcomeCode.Invalid, answers.Single(answer => answer.Seq == 100).Code, "short snapshot rejected");
        Assert.Equal(OutcomeCode.Invalid, answers.Single(answer => answer.Seq == 101).Code, "count overflow rejected without narrowing");
        Assert.Equal(OutcomeCode.Stale, answers.Single(answer => answer.Seq == 102).Code, "client cannot manufacture a larger stack");
        Assert.True(answers.Where(answer => answer.Seq == 103).Select(answer => answer.Code)
            .SequenceEqual(new[] { OutcomeCode.Done, OutcomeCode.Stale }), "first valid request accepted and identical replay refused");
        rig.Server.Inspect(server =>
        {
            server.Zone.Players().TryFindPlayer(client.EntityId, out _, out Player p);
            Container[] bags = server.Zone.Things().Statics().OfType<DroppedBag>().Select(bag => bag.Loot).ToArray();
            Assert.True(p.Inventory.Main.IsEmpty && p.Inventory.Belt.Slots.ToArray().SequenceEqual(belt) && p.Inventory.Wear[0] == worn,
                "all untrusted requests remained scoped to backpack");
            Assert.Equal(45, InventoryRules.Count(bags, client.Content.ItemByKey("stone").Id), "stone reached ground exactly once");
            Assert.Equal(2, InventoryRules.Count(bags, gun.Item), "one weapon from each legitimate drop, no replay duplication");
            Assert.Equal(276, InventoryRules.Count(bags, client.Content.ItemByKey("wood").Id), "earlier drop unchanged");
            Assert.Equal(1, bags.Sum(bag => bag.Slots.ToArray().Count(stack => stack == changed)), "changed weapon metadata conserved exactly once");
            return 0;
        });
    }

    private static void Until(LoopbackRig rig, Func<bool> ready, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!ready())
        {
            Assert.True(watch.Elapsed.TotalSeconds < 8, what);
            rig.Run(0.05, (_, _) => default);
        }
    }
}
