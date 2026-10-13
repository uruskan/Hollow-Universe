using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Server;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class SurvivalNetTests
{
    private static void RunUntil(LoopbackRig rig, Func<bool> done, string what, double seconds = 5)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < seconds, $"timed out waiting for {what}");
            rig.Run(0.05, (_, _) => default);
        }
    }

    private static int SlotOf(Container c, string key) =>
        Enumerable.Range(0, c.Size).First(i => c[i].Item == ContentFixture.Item(key).Id);

    /// <summary>
    /// ARCHITECTURE.md §11.4 over the network: an intent shows at once on the client (optimistic) and is confirmed;
    /// one the server refuses (someone else took the item meanwhile) rolls back to the server's state with the reason;
    /// a burst of intents past the budget (16 at once, 20 a second) is refused as rate limited.
    /// </summary>
    [Test]
    public static void IntentsAreOptimisticRolledBackAndRateLimited()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        rig.WaitUntilPlaying();
        LocalPlayer me = ada.Local;
        RunUntil(rig, () => me.Inventory.Count(ContentFixture.Item("rock")) == 1, "the respawn kit");
        rig.Server.Execute("give Ada wood 300");
        rig.Server.Execute("give Ada stone 50");
        RunUntil(rig, () => me.Inventory.Count(ContentFixture.Item("stone")) == 50, "the stone");

        // Optimistic: a split shows before the server has seen it, and stays once confirmed.
        var answers = new List<IntentAnswer>();
        me.IntentAnswered += answers.Add;
        int wood = SlotOf(me.Inventory.Main, "wood");
        uint split = me.MoveItem(ContainerRef.Main, wood, ContainerRef.Belt, 3, 100);
        Assert.True(me.Inventory.Belt[3].Count == 100 && me.Inventory.Main[wood].Count == 200, "shown at once");
        RunUntil(rig, () => answers.Any(a => a.Seq == split), "the answer");
        Assert.Equal(ActionResult.Accepted, answers.Single(a => a.Seq == split).Result, "accepted");
        Assert.True(me.Inventory.Belt[3].Count == 100 && me.PendingCount == 0, "and still there");

        // Rollback: the stone vanishes on the server (as if another looter took it) just before the client moves it.
        int stone = SlotOf(me.Inventory.Main, "stone");
        var rejected = new List<IntentAnswer>();
        me.IntentRejected += rejected.Add;
        rig.Server.Inspect(sv =>
        {
            sv.Zone.TryFindPlayer(new EntityId(ada.EntityId.Value), out _, out Player p);
            p.Inventory.Main.Set(stone, default);
            return 0;
        });
        uint move = me.MoveItem(ContainerRef.Main, stone, ContainerRef.Belt, 4);
        Assert.Equal(50, me.Inventory.Belt[4].Count, "the client moved it at once");
        RunUntil(rig, () => rejected.Any(a => a.Seq == move), "the refusal");
        Assert.Equal(ActionResult.Stale, rejected.Single(a => a.Seq == move).Result, "stale");
        Assert.True(me.Inventory.Belt[4].IsEmpty && me.Inventory.Main[stone].IsEmpty, "rolled back to the server's state");

        // A refused craft says why (not enough stone now) and changes nothing.
        uint craft = me.Craft(ContentFixture.Item("stone-hatchet").Id);
        RunUntil(rig, () => rejected.Any(a => a.Seq == craft), "the craft's refusal");
        Assert.Equal(ActionResult.NotEnough, rejected.Single(a => a.Seq == craft).Result, "not enough");

        // Forty intents in one frame: the budget lets about sixteen through.
        for (int i = 0; i < 40; i++)
        {
            me.Close();
        }
        RunUntil(rig, () => answers.Count >= 43, "forty answers");
        int limited = answers.Count(a => a.Result == ActionResult.RateLimited);
        Fixtures.Report($"of 40 intents in one frame, {40 - limited} accepted and {limited} rate limited");
        Assert.InRange(20, 26, limited, "rate limited past the burst");
        int refused = rig.Server.Inspect(sv => (int)sv.Players.Single().IntentsRateLimited);
        Assert.Equal(limited, refused, "counted on the server");
    }

    /// <summary>
    /// Gathering's feedback over the network (GROUND.md §6.1, protocol 13): each hit on a tree reaches the client in range
    /// once, fresh, with where it met the trunk and the run of weak-spot hits; a player who comes along later sees the
    /// tree as it is, without its old hits (no effects replayed, no felled tree falling again).
    /// </summary>
    [Test]
    public static void GatherHitsReachClientsOnceWhileFresh()
    {
        GameContent c = ContentFixture.Content;
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport Ada 0 0");
        rig.Run(0.5, (_, _) => default);
        ItemDef hatchet = c.ItemByKey("stone-hatchet");
        NaturalObject tree = default;
        Vector3 Hit(bool atSpot) => rig.Server.Inspect(sv =>
        {
            NaturalKind kind = c.Natural(tree.Kind)!;
            Vector3 eye = tree.Position + new Vector3(2, 1.6f, 0), at = tree.Position + tree.Up * 1.3f;
            if (atSpot && sv.Zone.Natural!.WeakSpotAt(tree, kind) is var (spot, normal))
            {
                (eye, at) = (spot + normal * 1.2f, spot);
            }
            GatherResult r = sv.Zone.Natural!.Hit(tree, new GatherSwing(hatchet, hatchet.Melee!, at, eye, Vector3.Normalize(at - eye), sv.Zone.UnixMilliseconds, sv.Zone.Tick));
            Assert.True(r.Hit && r.WeakSpot == atSpot, atSpot ? "on the X" : "a first hit");
            return r.Point - tree.Position;
        });
        tree = rig.Server.Inspect(sv =>
        {
            var found = new List<NaturalObject>();
            sv.Zone.Nature!.Query(-40, -40, 40, 40, found);
            return found.Where(o => c.Natural(o.Kind)!.Class == NaturalClass.Tree).MinBy(o => o.Position.Length());
        });
        Hit(atSpot: false);
        RunUntil(rig, () => ada.Natural!.TryGetOverride(tree.Id, out NaturalOverride o) && o.SeenHits == 1, "the first hit");
        Vector3 point = Hit(atSpot: true);
        RunUntil(rig, () => ada.Natural!.TryGetOverride(tree.Id, out NaturalOverride o) && o.SeenHits == 2, "the second");
        ada.Natural!.TryGetOverride(tree.Id, out NaturalOverride seen);
        Fixtures.Report($"the X hit met the trunk {point.Length():0.00} m from the base; the client has it {Vector3.Distance(seen.LastHit, point) * 100:0.0} cm off");
        Assert.True(seen.Streak == 1 && Vector3.Distance(seen.LastHit, point) < 0.05f, "with the run and where it landed");
        rig.Run(1.5, (_, _) => default);
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport Bo 0 0");
        RunUntil(rig, () => bo.Natural!.TryGetOverride(tree.Id, out NaturalOverride o) && o.HasWeakSpot, "Bo sees the hit tree");
        Assert.True(bo.Natural!.TryGetOverride(tree.Id, out NaturalOverride late) && late.SeenHits == 0, "but no hits to play");
    }

    /// <summary>
    /// GROUND.md §6.1 over the network: another player sees a bag of dropped items as a bag, not what is in it, with how
    /// many stacks it holds; his interact key opens one holding two, and only then do its contents come to him, take
    /// only.
    /// </summary>
    [Test]
    public static void OthersSeeOnlyABag()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport Ada 0 0");
        rig.Server.Execute("teleport Bo 1 0");
        rig.Run(0.5, (_, _) => default);
        rig.Server.Execute("give Ada stone 50");
        rig.Server.Execute("give Ada bandage 2");
        RunUntil(rig, () => ada.Local.Inventory.Count(ContentFixture.Item("stone")) == 50 && ada.Local.Inventory.Count(ContentFixture.Item("bandage")) == 2, "the gifts");
        ada.Local.Drop(ContainerRef.Main, SlotOf(ada.Local.Inventory.Main, "stone"));
        ada.Local.Drop(ContainerRef.Main, SlotOf(ada.Local.Inventory.Main, "bandage"));
        RunUntil(rig, () => bo.Things.Any(t => t.Kind == StaticKind.DroppedBag && t.Count == 2), "Bo to see a bag of two stacks");
        RemoteThing bag = bo.Things.Single(t => t.Kind == StaticKind.DroppedBag);
        Assert.Equal(0, (int)bag.Look, "a bag, not what is in it");
        Assert.True(bo.Local.Loot == null, "nothing of its contents yet");

        Vector3 eye = bo.Character.Position + bo.Frame.UpAt(bo.Character.Position) * bo.EyeHeight;
        Interaction? aim = bo.Aim(eye, Vector3.Normalize(bag.Position - eye));
        Assert.True(aim is { Kind: InteractionKind.Open } && aim.Value.Entity == bag.Id, $"E opens it ({aim?.Verb} {aim?.Name})");
        bo.Interact(aim!.Value);
        RunUntil(rig, () => bo.Local.LootKind == LootKind.Crate && bo.Local.Loot != null, "the bag to open");
        Assert.True(bo.Local.Loot!.Slots.ToArray().Count(s => !s.IsEmpty) == 2 && InventoryRules.Count(new[] { bo.Local.Loot }, ContentFixture.Item("stone").Id) == 50,
            "its two stacks, to Bo alone");
        Assert.True(!bo.Local.LootTakesDeposits, "take only");
    }

    /// <summary>
    /// GROUND.md §6, persistence of everything: after a server restart the characters come back (the one who stayed
    /// logged in, and a sleeper) with their inventories, vitals and crafting queues; the box with its contents, the
    /// sleeping bag with its cooldown, the corpse and the bag of dropped stone with their timers left, and the natural
    /// object a player damaged, all as they were.
    /// </summary>
    [Test]
    public static void EverythingSurvivesARestart()
    {
        using var file = new TempDatabase();
        GameContent c = ContentFixture.Content;
        EntityId adaId, boId;
        ulong treeId = 0;
        float treeHealth = 0;
        long corpseLeft, droppedLeft, bagReady;
        uint ticket;
        using (var rig = new LoopbackRig(file.Path))
        {
            GameClient ada = rig.Join("Ada");
            GameClient bo = rig.Join("Bo");
            rig.WaitUntilPlaying();
            adaId = ada.EntityId;
            boId = bo.EntityId;
            // Ada at the zone's origin (kept clear of trees), Bo out of her way.
            rig.Server.Execute("teleport Ada 0 0");
            rig.Server.Execute("teleport Bo 25 25");
            rig.Run(0.5, (_, _) => default);
            rig.Server.Execute("give Ada wood 900");
            rig.Server.Execute("give Ada stone 300");
            rig.Server.Execute("give Ada small-box 1");
            rig.Server.Execute("give Ada sleeping-bag 1");
            rig.Server.Execute("give Bo metal-ore 77");
            RunUntil(rig, () => ada.Local.Inventory.Count(c.ItemByKey("sleeping-bag")) == 1 && bo.Local.Inventory.Count(c.ItemByKey("metal-ore")) == 77, "the gifts");

            // Ada: a box with wood in it, a bag, a craft half done, a dropped stone; a tree she hit.
            Vector3 feet = ada.Character.Position;
            Vector3 boxAt = feet + new Vector3(1.6f, 0, 1.2f), bagAt = feet + new Vector3(-1.5f, 0, 1.5f);
            ada.Local.Place(ContainerRef.Main, SlotOf(ada.Local.Inventory.Main, "small-box"), boxAt, 0);
            ada.Local.Place(ContainerRef.Main, SlotOf(ada.Local.Inventory.Main, "sleeping-bag"), bagAt, 0);
            RunUntil(rig, () => ada.Things.Count(t => t.Kind == StaticKind.Deployable) == 2, "both placed");
            RemoteThing box = ada.Things.Single(t => t.Look == c.ItemByKey("small-box").Id);
            ada.Local.Open(box.Id);
            RunUntil(rig, () => ada.Local.LootKind == LootKind.Box && ada.Local.Loot != null, "the box open");
            ada.Local.MoveItem(ContainerRef.Main, SlotOf(ada.Local.Inventory.Main, "wood"), ContainerRef.Loot, 5, 250);
            ada.Local.Craft(c.ItemByKey("stone-hatchet").Id);
            ada.Local.Drop(ContainerRef.Main, SlotOf(ada.Local.Inventory.Main, "stone"), 30);
            RunUntil(rig, () => ada.Local.PendingCount == 0 && ada.Local.Crafting.Count == 1, "the box filled, the craft started");
            ticket = ada.Local.Crafting[0].Ticket;
            (treeId, treeHealth) = rig.Server.Inspect(sv =>
            {
                var found = new List<NaturalObject>();
                sv.Zone.Nature!.Query(-200, -200, 200, 200, found);
                NaturalObject tree = found.First(o => c.Natural(o.Kind)!.Class == NaturalClass.Tree);
                sv.Zone.Natural!.Hit(tree, c.ItemByKey("stone-hatchet").Melee!, tree.Position, tree.Position + new Vector3(2, 1.6f, 0), sv.Zone.UnixMilliseconds);
                return (tree.Id, sv.Zone.Natural.Health(tree, c.Natural(tree.Kind)!));
            });
            rig.Run(1.0, (_, _) => default);

            // Bo logs off (a sleeper with his ore), and someone's corpse lies about.
            bo.Disconnect();
            rig.Clients.Remove(bo);
            bo.Dispose();
            rig.Run(0.5, (_, _) => default);
            (corpseLeft, droppedLeft, bagReady) = rig.Server.Inspect(sv =>
            {
                sv.Zone.TryFindPlayer(boId, out Entity sleeper, out Player p);
                Assert.True(p.Sleeping, "Bo sleeps in the world");
                var at = sv.Zone.Entities.Get<Character>(sleeper).State.Position;
                var corpse = new Corpse(sv.Zone.NextId(), "Cy", EntityId.None, 37, sv.Zone.Settle(at + new Vector3(3, 0, 0)), Vector3.UnitY, 0, sv.Zone.Tick + 5000);
                corpse.Loot.Set(0, ItemStack.Of(c.ItemByKey("bandage"), 2));
                sv.Zone.AddStatic(corpse);
                DroppedBag dropped = sv.Zone.Statics().OfType<DroppedBag>().Single();
                Deployable bag = sv.Zone.Statics().OfType<Deployable>().Single(d => d.Def.IsRespawnPoint);
                return (corpse.DespawnTick - sv.Zone.Tick, dropped.DespawnTick - sv.Zone.Tick, bag.ReadyAtUnixMs);
            });
        }

        using (var rig = new LoopbackRig(file.Path))
        {
            // Before anyone joins: everything is back, timers counting from where they were.
            var report = rig.Server.Inspect(sv =>
            {
                Zone z = sv.Zone;
                Assert.True(z.TryFindPlayer(adaId, out _, out Player ada) && ada.Sleeping, "Ada is back, asleep");
                Assert.True(z.TryFindPlayer(boId, out _, out Player bo) && bo.Sleeping, "Bo too");
                Assert.Equal(77, bo.Inventory.Count(c.ItemByKey("metal-ore")), "with his ore");
                Assert.Equal(1, ada.Crafting.Jobs.Count, "Ada's craft is queued");
                Assert.Equal(ticket, ada.Crafting.Jobs[0].Ticket, "the same job");
                Deployable box = z.Statics().OfType<Deployable>().Single(d => d.Def.Key == "small-box");
                Assert.Equal(250, InventoryRules.Count(new[] { box.Storage! }, c.ItemByKey("wood").Id), "the box keeps its wood");
                Deployable bag = z.Statics().OfType<Deployable>().Single(d => d.Def.IsRespawnPoint);
                Assert.Equal(bagReady, bag.ReadyAtUnixMs, "the bag's cooldown");
                Assert.True(bag.Owner == adaId, "and its owner");
                Corpse corpse = z.Statics().OfType<Corpse>().Single();
                Assert.InRange(corpseLeft - 60, corpseLeft, corpse.DespawnTick - z.Tick, "the corpse's time left");
                Assert.Equal(2, InventoryRules.Count(new[] { corpse.Loot }, c.ItemByKey("bandage").Id), "and its contents");
                DroppedBag dropped = z.Statics().OfType<DroppedBag>().Single();
                Assert.True(InventoryRules.Count(new[] { dropped.Loot }, c.ItemByKey("stone").Id) == 30 && dropped.ShownCount == 1 &&
                    dropped.DespawnTick - z.Tick <= droppedLeft, "the dropped stone in its bag, and its time");
                Assert.True(z.Nature!.TryGet(treeId, out NaturalObject tree) && Math.Abs(z.Natural!.Health(tree, c.Natural(tree.Kind)!) - treeHealth) < 1e-3,
                    "the tree as Ada left it");
                return $"{z.Statics().Count()} things, {z.Natural!.Count} natural changes";
            });
            Fixtures.Report($"after the restart: {report}");

            // Ada logs in: she wakes with her things.
            GameClient again = rig.Join("Ada");
            rig.WaitUntilPlaying();
            RunUntil(rig, () => again.Local.Inventory.Count(c.ItemByKey("wood")) > 0 && again.Local.Crafting.Count == 1, "Ada's state");
            Assert.Equal(again.EntityId, adaId, "the same character");
            Assert.Equal(900 - 250 - 200, again.Local.Inventory.Count(c.ItemByKey("wood")), "her wood");
            Assert.True(rig.Server.Inspect(sv => sv.Zone.TryFindPlayer(adaId, out _, out Player p) && !p.Sleeping), "awake");
        }
    }
}
