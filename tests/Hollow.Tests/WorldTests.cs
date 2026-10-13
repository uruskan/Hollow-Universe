using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Economy;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Teams;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>G7 World, part A (GROUND.md §6.8): loot, the recycler, tech trees and research, teams, the map's markers.</summary>
public static class WorldTests
{
    private static int SlotOf(Container c, string key) =>
        Enumerable.Range(0, c.Size).First(i => c[i].Item == ContentFixture.Item(key).Id);

    /// <summary>Steps until a condition holds, doing something before each tick; fails after <paramref name="most"/> ticks.</summary>
    private static int StepUntil(GameRig rig, Func<bool> done, int most, string what, Action each)
    {
        for (int i = 0; i <= most; i++)
        {
            if (done())
            {
                return i;
            }
            rig.Step(1, each);
        }
        throw new TestFailure($"timed out ({most} ticks) waiting for {what}");
    }

    private static WorldProp NearestProp(GameRig rig, WorldPropKind kind, Vector3? from = null) =>
        rig.Zone.Props.Where(p => p.Prop.Kind == kind).OrderBy(p => Vector3.Distance(p.Position, from ?? Vector3.Zero)).First();

    // Puts a character beside a prop, facing it.
    private static void StandBy(GameRig rig, Entity e, Deployable d, float distance = 1.4f)
    {
        Vector3 away = d.Position.LengthSquared() > 1 ? Vector3.Normalize(new Vector3(-d.Position.X, 0, -d.Position.Z)) : Vector3.UnitX;
        Vector3 ground = rig.Zone.Settle(d.Position + away * (distance + d.Def.HalfExtents.X));
        Raids.Stand(rig, e, ground);
        rig.Look(e, d.Centre);
    }

    /// <summary>
    /// GROUND.md §6.8, loot: every world table gives salvage, Rust's way (barrels 1–3, crates 4–7, military crates
    /// 8–12), then components, ammunition and the rest; a blueprint is of a recipe not known from the start, of its
    /// table's workbench level; what a barrel or crate holds always fits its slots; and a seed always gives the same.
    /// </summary>
    [Test]
    public static void LootTablesGiveSalvageComponentsAndBlueprints()
    {
        GameContent c = ContentFixture.Content;
        ItemDef salvage = c.ItemByKey("salvage"), blueprint = c.ItemByKey("blueprint");
        var components = new HashSet<string> { "gears", "springs", "metal-pipe", "rope", "cloth", "sewing-kit", "tech-scrap", "metal-blade", "sheet-metal",
            "sign-plate", "receiver", "rifle-frame" };
        foreach ((string key, int least, int most) in new[] { ("barrel", 1, 3), ("crate", 4, 7), ("military-crate", 8, 12) })
        {
            LootTable table = c.Loot(key)!;
            PropDef prop = c.World.Props.First(p => p.Loot == table);
            int blueprints = 0, withComponents = 0;
            var tiers = new HashSet<int>();
            for (int i = 0; i < 2000; i++)
            {
                var rng = new Rng(Hash64.Of((ulong)i, 0x1007));
                List<ItemStack> stacks = table.RollStacks(ref rng, c);
                Assert.True(stacks.Count <= prop.Deployable.Slots, $"{key}: fits its slots");
                int s = stacks.Where(x => x.Item == salvage.Id).Sum(x => x.Count);
                Assert.InRange(least, most, s, $"{key}: salvage");
                withComponents += stacks.Any(x => components.Contains(c.Item(x.Item)!.Key)) ? 1 : 0;
                foreach (ItemStack b in stacks.Where(x => x.Item == blueprint.Id))
                {
                    blueprints++;
                    Recipe taught = c.Recipe(b.Condition)!;
                    Assert.True(!taught.Known, $"{key}: a blueprint teaches what isn't known from the start ({taught})");
                    tiers.Add(c.World.TierOf(taught));
                }
                Assert.True(stacks.All(x => x.Count >= 1 && x.Count <= c.Item(x.Item)!.StackSize), $"{key}: stacks within their sizes");
            }
            Fixtures.Report($"{key}: of 2,000, {withComponents} with components, {blueprints} blueprints (levels {string.Join(", ", tiers.OrderBy(t => t))})");
            Assert.True(withComponents > 400 && blueprints > 0, $"{key}: components and now and then a blueprint");
        }
        var a = new Rng(42);
        var b2 = new Rng(42);
        Assert.True(c.Loot("military-crate")!.RollStacks(ref a, c).SequenceEqual(c.Loot("military-crate")!.RollStacks(ref b2, c)), "a seed gives the same");
    }

    /// <summary>
    /// GROUND.md §6.8, where loot stands: the spots are the same every time (a spot's ID names the same place after a
    /// restart), none within 90 m of the origin, each on dry ground; barrels and crates in clusters, one always near
    /// the origin, and three recyclers at their points near the landing site.
    /// </summary>
    [Test]
    public static void LootSpotsAreDeterministicClustersAwayFromTheSpawn()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        LootSpots a = LootSpots.Generate(z.Surface, z.Frame, z.Nature!, rig.C, 7);
        double ms = watch.Elapsed.TotalMilliseconds;
        LootSpots b = LootSpots.Generate(z.Surface, z.Frame, z.Nature!, rig.C, 7);
        Assert.Equal(a.Spots.Count, b.Spots.Count, "the same count");
        Assert.True(a.Spots.Zip(b.Spots).All(p => p.First.Id == p.Second.Id && p.First.Position == p.Second.Position && p.First.Prop == p.Second.Prop),
            "the same spots, IDs and props");
        Assert.True(a.Spots.Select(s => s.Id).Distinct().Count() == a.Spots.Count, "IDs are unique");
        float clear = (float)rig.C.World.Loot.Clear;
        Assert.True(a.Spots.All(s => new Vector2(s.Position.X, s.Position.Z).Length() >= clear), "nothing within 90 m of the origin");
        Assert.True(a.Spots.Count(s => s.Site == LootSite.Landing) >= 2, "a cluster near the origin");
        Assert.Equal(3, a.Spots.Count(s => s.Prop.Kind == WorldPropKind.Recycler), "three recyclers");
        int barrels = a.Spots.Count(s => s.Prop.Kind == WorldPropKind.Barrel), crates = a.Spots.Count(s => s.Prop.Kind == WorldPropKind.Crate);
        Fixtures.Report($"{a.Spots.Count} spots ({barrels} barrels, {crates} crates) in {a.ForestEdges} forest-edge, {a.Coasts} coast and {a.Opens} open clusters; " +
            $"generated in {ms:0} ms");
        Assert.True(barrels > crates && crates > 20, "clusters of barrels, most with a crate");
        Assert.True(a.Spots.All(s => float.IsFinite(s.Position.Y) && Math.Abs(z.HeightAboveGround(s.Position)) < 0.01), "on the ground");
    }

    /// <summary>
    /// GROUND.md §6.8, barrels and crates: a barrel broken with a rock spills its loot on the ground and comes back on
    /// its timer with fresh loot; a crate is looted (take only: nothing goes in) and goes once empty, to come back on its
    /// timer; while a player stands on a spot, it waits.
    /// </summary>
    [Test]
    public static void BarrelsBreakCratesEmptyAndBothComeBack()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        rig.Zone.StartWorld();
        WorldProp barrel = NearestProp(rig, WorldPropKind.Barrel);
        ulong spot = barrel.Id.Value;
        Assert.True(barrel.Container == null && barrel.Storage!.Slots.ToArray().Any(s => s.Item == c.ItemByKey("salvage").Id),
            "a barrel holds its loot, but doesn't open");
        Entity ada = rig.Add("Ada", 0, 0);
        rig.Give(ada, "rock", 1);
        rig.P(ada).Inventory.Belt.Set(0, rig.P(ada).Inventory.Main[SlotOf(rig.P(ada).Inventory.Main, "rock")]);
        rig.P(ada).Inventory.Main.Clear();
        StandBy(rig, ada, barrel, 0.8f);
        Assert.Equal(OutcomeCode.NotUsable, rig.Zone.Interact(ada, barrel.Ref, "open"), "a barrel doesn't open");
        int ticks = StepUntil(rig, () => rig.Zone.PropAt(spot) == null, 30 * 12, "the barrel to break", () =>
        {
            rig.Look(ada, barrel.Centre);
            rig.Ch(ada).Actions = new ActionInput { HeldSlot = 0, Buttons = ActionButtons.Primary };
        });
        Fixtures.Report($"a rock breaks a barrel in {ticks / 30.0:0.0} s");
        rig.Ch(ada).Actions = default;
        Assert.True(rig.Zone.Statics().OfType<DroppedBag>().Any(b => InventoryRules.Count(new[] { b.Loot }, c.ItemByKey("salvage").Id) > 0 && Vector3.Distance(b.Position, barrel.Position) < 2),
            "its salvage lies where it stood, in a bag");
        long respawn = rig.Zone.RespawnOf(spot)!.Value;
        Assert.Near(barrel.Prop.RespawnSeconds * 1000, respawn - rig.Zone.UnixMilliseconds, 2000, "comes back after its time");
        rig.Zone.UnixMilliseconds = respawn + 10;
        rig.Step(31);
        WorldProp? again = rig.Zone.PropAt(spot);
        Assert.True(again != null && again != barrel && again.Health == again.Def.Health && !again.Storage!.IsEmpty,
            $"back, whole, with new loot ({again?.Health}, {rig.Zone.RespawnOf(spot)} vs {rig.Zone.UnixMilliseconds})");

        WorldProp crate = NearestProp(rig, WorldPropKind.Crate);
        ulong crateSpot = crate.Id.Value;
        StandBy(rig, ada, crate);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interact(ada, crate.Ref, "loot"), "a crate opens");
        Assert.Equal(Hollow.Simulation.Items.LootKind.Crate, rig.Zone.LootKindOf(rig.P(ada)), "as a crate");
        rig.Give(ada, "wood", 10);
        int wood = SlotOf(rig.P(ada).Inventory.Main, "wood");
        Assert.Equal(OutcomeCode.NotAllowed, rig.Zone.MoveItem(ada, ContainerRef.Main, wood, ContainerRef.Loot, crate.Storage!.Size - 1, 0, 0),
            "nothing goes into a crate");
        for (int i = 0; i < crate.Storage.Size; i++)
        {
            if (!crate.Storage[i].IsEmpty)
            {
                Assert.Equal(OutcomeCode.Done, rig.Zone.QuickMove(ada, ContainerRef.Loot, i, 0), $"take slot {i}");
            }
        }
        rig.Step(31);
        Assert.True(rig.Zone.PropAt(crateSpot) == null && rig.Zone.RespawnOf(crateSpot) != null, "an empty crate goes");
        // Someone standing on the spot when its time comes: it waits until they move.
        rig.Zone.UnixMilliseconds = rig.Zone.RespawnOf(crateSpot)!.Value + 10;
        Raids.Stand(rig, ada, crate.Position);
        rig.Step(31);
        Assert.True(rig.Zone.PropAt(crateSpot) == null, "not on top of a player");
        StandBy(rig, ada, crate, 3);
        rig.Step(31);
        Assert.True(rig.Zone.PropAt(crateSpot) is { Storage.IsEmpty: false }, "then back, full");
        Fixtures.Report(rig.Zone.WorldStats.ToString());
    }

    /// <summary>
    /// GROUND.md §6.8, the recycler (Rust's): anyone opens it and turns it on, with no fuel; a loot-only component gives
    /// its own list (gears: 10 salvage and 13 metal fragments each), a crafted item about half its recipe; a batch is a
    /// tenth of a stack every 5 s; it won't take salvage or raw materials; with nothing in, it won't start, and it stops
    /// when it runs out or its outputs are full.
    /// </summary>
    [Test]
    public static void RecyclerReturnsHalfAndComponentsTheirOwn()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        rig.Zone.StartWorld();
        WorldProp recycler = NearestProp(rig, WorldPropKind.Recycler);
        Entity ada = rig.Add("Ada", 0, 0);
        StandBy(rig, ada, recycler, 1.0f);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interact(ada, recycler.Ref, "open"), "open");
        Assert.Equal(Hollow.Simulation.Items.LootKind.Device, rig.Zone.LootKindOf(rig.P(ada)), "a machine");
        Assert.Equal(OutcomeCode.CantStart, rig.Zone.Interact(ada, recycler.Ref, "turn-on"), "nothing to take apart");
        rig.Give(ada, "gears", 10);
        rig.Give(ada, "salvage", 50);
        rig.Give(ada, "metal-hatchet", 1);
        Container main = rig.P(ada).Inventory.Main;
        Assert.Equal(OutcomeCode.NotAllowed, rig.Zone.MoveItem(ada, ContainerRef.Main, SlotOf(main, "salvage"), ContainerRef.Loot, 0, 0, 0),
            "salvage isn't recycled");
        Assert.Equal(OutcomeCode.NotAllowed, rig.Zone.MoveItem(ada, ContainerRef.Main, SlotOf(main, "gears"), ContainerRef.Loot, 7, 0, 0),
            "nothing goes into the outputs");
        Assert.Equal(OutcomeCode.Done, rig.Zone.MoveItem(ada, ContainerRef.Main, SlotOf(main, "gears"), ContainerRef.Loot, 0, 0, 0), "gears in");
        Assert.Equal(OutcomeCode.Done, rig.Zone.MoveItem(ada, ContainerRef.Main, SlotOf(main, "metal-hatchet"), ContainerRef.Loot, 1, 0, 0), "a hatchet in");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interact(ada, recycler.Ref, "turn-on"), "on");
        int count(string key) => recycler.Storage!.Slots.ToArray().Where(s => s.Item == c.ItemByKey(key).Id).Sum(s => s.Count);
        // Gears stack to 20: two a batch, five batches; then the hatchet; then it stops by itself.
        int ticks = Raids.StepUntil(rig, () => !recycler.Lit, 30 * 60, "the recycler to finish");
        Fixtures.Report($"10 gears and a metal hatchet recycled in {ticks / 30.0:0} s: {count("salvage")} salvage, {count("metal-fragments")} metal fragments, " +
            $"{count("wood")} wood");
        Assert.InRange(29 * 30, 31 * 30, ticks, "six batches of 5 s, then it stops");
        Assert.Equal(100, count("salvage"), "gears: 10 salvage each");
        Assert.InRange(130 + 37, 130 + 38, count("metal-fragments"), "13 fragments each, and half the hatchet's 75");
        Assert.Equal(50, count("wood"), "half the hatchet's 100 wood");
        Assert.True(recycler.Storage!.Slots[..6].ToArray().All(s => s.IsEmpty), "the inputs are used up");
        Assert.Equal(recycler.Def.Health, recycler.Health, "untouched by the work");
        rig.Zone.DamageDeployable(recycler, 10_000);
        Assert.True(rig.Zone.Props.Contains(recycler), "a recycler can't be broken");
    }

    /// <summary>
    /// GROUND.md §6.8, tech trees and research (Rust's): a recipe in a tree is unknown until learnt; a node is unlocked
    /// for its salvage at a workbench of its level, only after one of the nodes before it; then it crafts. A research
    /// table turns an item and salvage into its blueprint in 10 s; using the blueprint learns it, once. Blueprints
    /// last through death.
    /// </summary>
    [Test]
    public static void TechTreeUnlocksResearchAndBlueprints()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 30, 30);
        Player a = rig.P(ada);
        rig.Give(ada, "salvage", 600);
        rig.Give(ada, "wood", 500);
        rig.Give(ada, "metal-fragments", 500);
        ushort hatchet = c.ItemByKey("metal-hatchet").Id, pickaxe = c.ItemByKey("metal-pickaxe").Id;
        Assert.True(!c.Recipe(hatchet)!.Known && c.Recipe(c.ItemByKey("stone-hatchet").Id)!.Known && c.Recipe(c.ItemByKey("workbench-1").Id)!.Known,
            "Rust's default blueprints: stone tools and the first workbench, not metal ones");
        Assert.Equal(OutcomeCode.UnknownRecipe, rig.Zone.Craft(ada, hatchet, 1, out _), "unknown at first");
        var bench = new Deployable(rig.Zone.NextId(), c.Deployable("workbench-1")!, rig.Zone.Settle(new Vector3(31.5f, 0, 30)),
            rig.Zone.Frame.UpAt(new Vector3(31.5f, 0, 30)), 0, a.Id);
        rig.Zone.AddStatic(bench);
        // A node is unlocked at a workbench (its verb, the recipe its argument), of the node's level within reach.
        Outcome Unlock(ushort item) => rig.Zone.Interact(ada, bench.Ref, "unlock", item);
        Assert.Equal(OutcomeCode.NeedsWorkbench, Unlock(c.ItemByKey("pistol").Id), "the second tree needs a level 2 workbench");
        Assert.Equal(OutcomeCode.NeedsEarlierNode, Unlock(pickaxe), "the pickaxe comes after the hatchet");
        Assert.Equal(OutcomeCode.Done, Unlock(hatchet), "unlocked");
        Assert.Equal(600 - 75, a.Inventory.Count(c.ItemByKey("salvage")), "for its 75 salvage");
        Assert.Equal(OutcomeCode.AlreadyKnown, Unlock(hatchet), "once");
        Assert.Equal(OutcomeCode.Done, Unlock(pickaxe), "then the pickaxe");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Craft(ada, hatchet, 1, out _), "and it crafts");
        Assert.Equal(OutcomeCode.Invalid, Unlock(c.ItemByKey("stone-hatchet").Id), "a default blueprint is in no tree");

        // Research: a pistol and its tier's salvage make a pistol blueprint.
        rig.Give(ada, "research-table", 1);
        Vector3 at = rig.Zone.Settle(new Vector3(30, 0, 31.6f));
        Assert.Equal(OutcomeCode.Done, rig.Zone.Place(ada, ContainerRef.Main, SlotOf(a.Inventory.Main, "research-table"), 0, at, 0), "a research table");
        Deployable table = rig.Zone.Statics().OfType<Deployable>().Single(d => d.Def.Research != null);
        rig.Look(ada, table.Centre);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interact(ada, table.Ref, "open"), "open it");
        rig.Give(ada, "pistol", 1);
        rig.Give(ada, "gears", 1);
        ResearchStats rs = c.World.Research;
        Assert.Equal(OutcomeCode.NotAllowed, rig.Zone.MoveItem(ada, ContainerRef.Main, SlotOf(a.Inventory.Main, "gears"), ContainerRef.Loot, rs.ItemSlot, 0, 0),
            "a component isn't researched");
        Assert.Equal(OutcomeCode.Done, rig.Zone.MoveItem(ada, ContainerRef.Main, SlotOf(a.Inventory.Main, "pistol"), ContainerRef.Loot, rs.ItemSlot, 0, 0), "the pistol in");
        Assert.Equal(OutcomeCode.NotEnough, rig.Zone.Interact(ada, table.Ref, "turn-on"), "no salvage yet");
        int cost = Research.Cost(c, c.Recipe(c.ItemByKey("pistol").Id)!);
        Assert.Equal(125, cost, "a level 2 item costs 125 salvage (Rust's)");
        Assert.Equal(OutcomeCode.Done, rig.Zone.MoveItem(ada, ContainerRef.Main, SlotOf(a.Inventory.Main, "salvage"), ContainerRef.Loot, rs.SalvageSlot, 200, 0),
            "salvage in");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interact(ada, table.Ref, "turn-on"), "start");
        Assert.True(table.Storage![rs.ItemSlot].IsEmpty && table.Storage[rs.SalvageSlot].Count == 200 - cost, "the pistol and its price are taken");
        int ticks = Raids.StepUntil(rig, () => !table.Lit, 30 * 12, "the research");
        Assert.InRange(299, 301, ticks, "10 s");
        ItemStack print = table.Storage[rs.OutputSlot];
        Assert.True(print.Item == c.ItemByKey("blueprint").Id && print.Condition == c.ItemByKey("pistol").Id, "a pistol blueprint");
        Assert.Equal(OutcomeCode.Done, rig.Zone.QuickMove(ada, ContainerRef.Loot, rs.OutputSlot, 0), "taken");
        int slot = SlotOf(a.Inventory.Main, "blueprint");
        Assert.Equal(OutcomeCode.Done, rig.Zone.UseItem(ada, ContainerRef.Main, slot, 0), "read");
        Assert.True(a.Blueprints.Knows(c.Recipe(c.ItemByKey("pistol").Id)!) && a.Inventory.Count(c.ItemByKey("blueprint")) == 0, "learnt, and used up");
        a.Inventory.Main.Set(0, TechTrees.BlueprintOf(c, c.Recipe(c.ItemByKey("pistol").Id)!));
        Assert.Equal(OutcomeCode.AlreadyKnown, rig.Zone.UseItem(ada, ContainerRef.Main, 0, 0), "known already");
        rig.Zone.Hurt(ada, a, 1000, "Test", EntityId.None, "");
        Assert.True(a.Dead && a.Blueprints.Learnt.Count == 3, "blueprints last through death");
        Assert.True(rig.Zone.WorldStats.Unlocked == 2 && rig.Zone.WorldStats.Learned == 1, "two unlocked, one learnt");
    }

    /// <summary>
    /// GROUND.md §6.8, teams (Rust's): a player creates a team and leads it; the leader invites a player close by, who
    /// accepts; only the leader invites, kicks and promotes; a leader who leaves passes the lead to the member who
    /// joined first; the last one out ends the team; eight at most; an invite runs out after five minutes; a player in
    /// a team can't join another; friendly fire stays on.
    /// </summary>
    [Test]
    public static void TeamLifeCycle()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        Entity ada = rig.Add("Ada", 0, 0), bo = rig.Add("Bo", 2, 0), cy = rig.Add("Cy", 0, 2), far = rig.Add("Far", 60, 60);
        EntityId A = rig.P(ada).Id, B = rig.P(bo).Id, C = rig.P(cy).Id;
        Outcome Invite(Entity leader, EntityId whom) => z.Interact(leader, new TargetRef(TargetFamily.Character, whom.Value), "invite");
        Assert.Equal(OutcomeCode.NotUsable, Invite(ada, B), "no team to invite to: not offered");
        Assert.Equal(OutcomeCode.Done, z.TeamAct(ada, TeamAction.Create, EntityId.None, 0), "created");
        Team team = z.Teams.TeamOf(A)!;
        Assert.Equal(A, team.Leader, "she leads it");
        Assert.Equal(OutcomeCode.OutOfReach, Invite(ada, rig.P(far).Id), "only players close by");
        Assert.Equal(OutcomeCode.Done, Invite(ada, B), "Bo invited");
        Assert.Equal(1, z.Teams.InvitesOf(B).Count, "he has the invite");
        Assert.Equal(OutcomeCode.Done, z.TeamAct(bo, TeamAction.Accept, EntityId.None, team.Id), "and accepts");
        Assert.Equal(OutcomeCode.NotUsable, Invite(bo, C), "only the leader is offered the invite");
        Assert.Equal(OutcomeCode.Done, Invite(ada, C), "Cy invited");
        Assert.Equal(OutcomeCode.Done, z.TeamAct(cy, TeamAction.Decline, EntityId.None, team.Id), "declines");
        Assert.Equal(OutcomeCode.NoTeam, z.TeamAct(cy, TeamAction.Accept, EntityId.None, team.Id), "and can't accept after");
        Assert.Equal(OutcomeCode.Done, Invite(ada, C), "invited again");
        z.UnixMilliseconds += 301_000;
        rig.Step(30);
        Assert.Equal(OutcomeCode.NoTeam, z.TeamAct(cy, TeamAction.Accept, EntityId.None, team.Id), "the invite ran out");
        Assert.Equal(OutcomeCode.Done, Invite(ada, C), "invited once more");
        Assert.Equal(OutcomeCode.Done, z.TeamAct(cy, TeamAction.Accept, EntityId.None, team.Id), "joins");
        Assert.Equal(3, team.Members.Count, "three");
        Assert.Equal(OutcomeCode.InTeam, z.TeamAct(cy, TeamAction.Create, EntityId.None, 0), "in a team already");
        Assert.Equal(OutcomeCode.NotLeader, z.TeamAct(bo, TeamAction.Kick, C, 0), "only the leader kicks");
        Assert.Equal(OutcomeCode.Done, z.TeamAct(ada, TeamAction.Kick, C, 0), "Cy kicked");
        Assert.True(z.Teams.TeamOf(C) == null && !team.Has(C), "out");
        Assert.Equal(OutcomeCode.Done, z.TeamAct(ada, TeamAction.Promote, B, 0), "Bo promoted");
        Assert.Equal(B, team.Leader, "Bo leads");
        Assert.Equal(OutcomeCode.Done, z.TeamAct(bo, TeamAction.Leave, EntityId.None, 0), "the leader leaves");
        Assert.Equal(A, team.Leader, "the lead passes to Ada, who joined first");
        Assert.Equal(OutcomeCode.Done, z.TeamAct(ada, TeamAction.Leave, EntityId.None, 0), "the last one out");
        Assert.True(z.Teams.Get(team.Id) == null && z.Teams.Ended.Contains(team.Id), "the team ended");

        // Eight at most.
        Assert.Equal(OutcomeCode.Done, z.TeamAct(ada, TeamAction.Create, EntityId.None, 0), "a new team");
        Team big = z.Teams.TeamOf(A)!;
        for (int i = 0; i < 8; i++)
        {
            Entity e = rig.Add($"M{i}", 1 + i * 0.3f, -1);
            Outcome invited = Invite(ada, rig.P(e).Id);
            Outcome joined = invited == OutcomeCode.Done ? z.TeamAct(e, TeamAction.Accept, EntityId.None, big.Id) : invited;
            Assert.Equal(i < 7 ? OutcomeCode.Done : OutcomeCode.TeamFull, joined, $"member {i + 2}");
        }
        Assert.Equal(8, big.Members.Count, "Rust's eight");

        // Friendly fire: a teammate's blow hurts.
        Entity mate = z.Entities.TryFind(big.Members[1], out Entity found) ? found : default;
        float before = rig.P(mate).Vitals.Health;
        z.Hurt(mate, rig.P(mate), 10, "Test", A, "Ada");
        Assert.True(rig.P(mate).Vitals.Health < before, "friendly fire stays on");
    }

    /// <summary>
    /// GROUND.md §6.8, "authorise team" (Rust's): at a claim device, a player puts themselves and their whole team on
    /// its list (anyone may authorise there); on a code lock, only someone the lock knows can, and it adds the team.
    /// </summary>
    [Test]
    public static void AuthoriseTeamOnClaimDevicesAndLocks()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        BuildingWorld world = z.Building!;
        Entity ada = rig.Add("Ada", 0, -4), bo = rig.Add("Bo", 1, -4), cy = rig.Add("Cy", -1, -4);
        ulong A = rig.P(ada).Id.Value, B = rig.P(bo).Id.Value, C = rig.P(cy).Id.Value;
        TestBases.Hut hut = Raids.Hut(rig, 0, 0, owner: 99);
        ClaimDevice claim = hut.Claim!;
        Door door = hut.Door!;
        Assert.True(world.AttachCodeLock(99, door, out CodeLock? codeLock).Ok && world.SetCode(99, door, "1234").Ok, "the owner's code lock");
        z.TeamAct(ada, TeamAction.Create, EntityId.None, 0);
        ulong team = z.Teams.TeamOf(rig.P(ada).Id)!.Id;
        z.Interact(ada, rig.P(bo).Ref, "invite");
        z.TeamAct(bo, TeamAction.Accept, EntityId.None, team);
        // Inside the hut, in sight of the claim device (since P0 a wall between hides it).
        Raids.Stand(rig, ada, hut.Inside);
        Outcome claimed = z.Interact(ada, claim.Ref, "authorise-team");
        Assert.True(claimed.Ok, $"authorise team at the claim device: {claimed}");
        Assert.True(claim.IsAuthorised(A) && claim.IsAuthorised(B) && !claim.IsAuthorised(C), "Ada and Bo are on its list, Cy isn't");
        Raids.Stand(rig, ada, z.Settle(door.Frame.ZoneTransform.Position + new Vector3(0, 0, -1.5f)));
        Outcome stranger = z.Interact(ada, door.Ref, "code-team");
        Assert.Equal(OutcomeCode.NotUsable, stranger.Code, "a stranger to the lock isn't offered it");
        Assert.True(z.Interact(ada, door.Ref, "enter-code", code: "1234").Ok, "the code");
        Assert.True(z.Interact(ada, door.Ref, "code-team").Ok, "then she can");
        Assert.True(codeLock!.IsAuthorised(B) && !codeLock.IsAuthorised(C), "Bo is on the lock now");
    }

    /// <summary>
    /// GROUND.md §6.8, the map's markers: up to five, within the map, a colour and a label each; one taken off, or all;
    /// where the player died; placing a sleeping bag or bed changes their map.
    /// </summary>
    [Test]
    public static void MapMarkersDeathPointAndBags()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        Entity ada = rig.Add("Ada", 10, 10);
        Player a = rig.P(ada);
        uint revision = a.MapRevision;
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(OutcomeCode.Done, z.PlaceMarker(ada, new Vector3(100 * i, 0, -50 * i), (byte)i, $"spot {i}"), $"marker {i + 1}");
        }
        Assert.Equal(OutcomeCode.NoSpace, z.PlaceMarker(ada, Vector3.Zero, 0, "six"), "five at most");
        Assert.Equal(OutcomeCode.Invalid, z.PlaceMarker(ada, new Vector3(5000, 0, 0), 0, "far"), "within the map");
        Assert.Equal(OutcomeCode.Invalid, z.PlaceMarker(ada, Vector3.Zero, 9, "colour"), "a colour of the palette");
        Assert.True(a.MapRevision > revision && a.Markers[2].Label == "spot 2" && a.Markers[2].Colour == 2, "kept with colour and label");
        Assert.True(Math.Abs(z.HeightAboveGround(a.Markers[3].Position)) < 0.01, "on the ground");
        Assert.Equal(OutcomeCode.Done, z.RemoveMarker(ada, 0), "one off");
        Assert.Equal("spot 1", a.Markers[0].Label, "the rest move up");
        Assert.Equal(OutcomeCode.Done, z.RemoveMarker(ada, -1), "all off");
        Assert.True(a.Markers.Count == 0, "none left");
        rig.Give(ada, "sleeping-bag", 1);
        revision = a.MapRevision;
        Assert.Equal(OutcomeCode.Done, z.Place(ada, ContainerRef.Main, SlotOf(a.Inventory.Main, "sleeping-bag"), 0, z.Settle(new Vector3(11, 0, 11.5f)), 0),
            "a bag");
        Assert.True(a.MapRevision > revision, "the bag is on her map");
        Assert.True(a.DeathPoint == null, "not dead yet");
        z.Hurt(ada, a, 1000, "Test", EntityId.None, "");
        Assert.True(a.DeathPoint is { } dp && Vector3.Distance(dp, rig.Ch(ada).State.Position) < 0.01f, "where she died");
    }

    /// <summary>
    /// GROUND.md §4.7 with G6 and G7 together: a database at schema 4 migrates through 5 (the ships) to 6 (teams and
    /// props), and player records of older versions still read (3 without the world's tail, 2 without the body too).
    /// </summary>
    [Test]
    public static void OldDatabasesAndPlayerRecordsStillRead()
    {
        using var file = new TempDatabase();
        using (GameDatabase.Open(file.Path))
        {
        }
        using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file.Path};Pooling=False"))
        {
            raw.Open();
            using var command = raw.CreateCommand();
            command.CommandText = "DROP TABLE prop_overrides; DROP TABLE teams; DROP TABLE ships; PRAGMA user_version = 4;";
            command.ExecuteNonQuery();
        }
        using (GameDatabase db = GameDatabase.Open(file.Path))
        {
            Assert.Equal(GameDatabase.SchemaVersion, db.Version, "migrated to the newest schema");
            Assert.True(db.ShipCount == 0 && db.TeamCount == 0 && db.PropCount == 0, "through the ships' step and the world's");
        }

        GameContent c = ContentFixture.Content;
        var p = new Player(new EntityId(0xA1), "Ada") { Body = 1 };
        p.Inventory.Main.Set(0, ItemStack.Of(c.ItemByKey("rock"), 1));
        // Version 4 with no blueprints, markers or death ends in 16 bytes (a count, a count, a flag, a point).
        byte[] four = WorldCodec.EncodePlayer(p);
        byte[] three = four[..^16];
        three[0] = 3;
        byte[] two = three[..^1];
        two[0] = 2;
        Player back3 = WorldCodec.DecodePlayer(three, p.Id, p.Name, c), back2 = WorldCodec.DecodePlayer(two, p.Id, p.Name, c);
        Assert.True(back3.Body == 1 && back3.Inventory.Main[0] == p.Inventory.Main[0] && back3.Blueprints.Learnt.Count == 0 &&
                    back3.Markers.Count == 0 && back3.DeathPoint == null, "version 3: as saved, with no world state");
        Assert.True(back2.Body == 0 && back2.Inventory.Main[0] == p.Inventory.Main[0], "version 2: no body either");
    }
}
