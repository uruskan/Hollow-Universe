using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Client;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class CorpseIntegrationTests
{
    private static void Until(LoopbackRig rig, Func<bool> ready, string what)
    {
        var timer = Stopwatch.StartNew();
        while (!ready())
        {
            Assert.True(timer.Elapsed.TotalSeconds < 6, what);
            rig.Run(0.05, (_, _) => default);
        }
    }

    [Test]
    public static void CorpseNetworkPhaseAndAppearanceSurviveALateJoin()
    {
        using var rig = new LoopbackRig();
        GameClient witness = rig.Join("Witness");
        rig.WaitUntilPlaying();
        EntityId id = rig.Server.Inspect(server =>
        {
            Zone z = server.Zone;
            Vector3 at = z.Settle(z.Frame.Beside(z.Landing, 2, 0, 0));
            var life = new CorpseLifecycle(z.Content!.Survival.Corpse, z.Tick, 1, new[] { z.Content.ItemByKey("woven-shirt").Id });
            var corpse = new Corpse(z.NextId(), "Ada", new EntityId(123), 37, at, z.Frame.UpAt(at), 0, z.Content.Survival.BodyLifetime, life.BodyUntilTick) { Life = life };
            corpse.Loot.Set(0, ItemStack.Of(z.Content.ItemByKey("wood"), 137));
            z.Things().AddStatic(corpse);
            return corpse.Id;
        });
        Until(rig, () => witness.Survival().Things.Any(t => t.Id == id), "body spawn replicated");
        RemoteThing body = witness.Survival().Things.Single(t => t.Id == id);
        Assert.True(body.IsBody && body.CorpseOwner == 123 && body.CorpseBody == 1 && body.CorpseWear[0] == ContentFixture.Item("woven-shirt").Id,
            "spawn contains authoritative phase and original appearance");
        uint netId = body.NetId;
        rig.Server.Inspect(server =>
        {
            var corpse = (Corpse)server.Zone.Things().StaticOf(id)!;
            corpse.Life!.Restore(CorpseStage.Body, 1, corpse.Life.HarvestLeft, server.Zone.Tick);
            return 0;
        });
        Until(rig, () => !body.IsBody, "body conversion replicated");
        Assert.Equal(netId, body.NetId, "conversion preserves network identity");
        GameClient later = rig.Join("Later");
        rig.WaitUntilPlaying();
        Until(rig, () => later.Survival().Things.Any(t => t.Id == id), "late join sees corpse container");
        RemoteThing bag = later.Survival().Things.Single(t => t.Id == id);
        Assert.True(!bag.IsBody && bag.CorpseOwner == 123, "late join sees an existing bag, never a fresh body timer");
        Assert.Equal(137, rig.Server.Inspect(server => ((Corpse)server.Zone.Things().StaticOf(id)!).Loot[0].Count), "replication and late join preserve loot");
    }

    [Test]
    public static void CorpseEmptyDeathLeavesHarvestableBodyAndGodDeathLeavesNothing()
    {
        using var rig = new GameRig();
        Entity god = rig.Add("Immune", 12, 10);
        rig.Zone.SetCheats(god, MoveCheats.God);
        rig.Zone.Players().Die(god, rig.P(god), "Test", EntityId.None, "");
        Assert.True(!rig.P(god).Dead && !rig.Zone.Things().Statics().OfType<Corpse>().Any(), "immune death neither empties inventory nor creates body");
        Entity empty = rig.Add("Empty", 10, 10);
        rig.Zone.Players().Die(empty, rig.P(empty), "Test", EntityId.None, "");
        Corpse corpse = rig.Zone.Things().Statics().OfType<Corpse>().Single();
        rig.Step(61);
        Assert.True(corpse.IsBody && corpse.Loot.IsEmpty && rig.Zone.Things().StaticOf(corpse.Id) == corpse, "empty body remains harvestable");
        corpse.Life!.Restore(CorpseStage.Body, 1, corpse.Life.HarvestLeft, rig.Zone.Tick);
        rig.Step(31);
        Assert.True(rig.Zone.Things().StaticOf(corpse.Id) == null, "empty bag cleans up after the body expires");
    }

    [Test]
    public static void CorpseKeepsFoundationSupportAndFallsOnlyWhenItGoes()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 20, 20);
        Vector3 ground = rig.Zone.GroundAt(20, 20), up = rig.Zone.Frame.UpAt(ground);
        rig.Clear(ground, ground, 5);
        Piece foundation = BuildingNetTests.Foundation(rig.Zone, ground + up * 1.8f, rig.P(ada).Id.Value, 2);
        Vector3 top = BuildingWorld.F(foundation.Structure.Frame.ToZone(foundation.Pose.Position));
        rig.Ch(ada).State.Position = top + up * 0.04f;
        rig.Zone.Players().Die(ada, rig.P(ada), "Test", EntityId.None, "");
        Corpse corpse = rig.Zone.Things().Statics().OfType<Corpse>().Single();
        Assert.True(corpse.Support == foundation.Ref, "body holds the actual building floor, not terrain underneath");
        Assert.Near(0, Vector3.Dot(corpse.Position - top, up), 0.08, "body rests on foundation top");
        Assert.True(rig.Zone.Supports().DependentsOf(foundation.Ref).Contains(corpse.Ref), "support graph owns body");
        rig.Zone.Building().World!.Remove(foundation, rig.Zone.Building().BuildingNow, immediate: true);
        rig.Step(2);
        Assert.True(corpse.Support.IsNone && !corpse.Rides && corpse.Moved > 0, "removed support releases body and replicates its new position");
        Assert.Near(0, rig.Zone.HeightAboveGround(corpse.Position), 0.08, "body falls to ground after floor removal");
    }

    [Test]
    public static void CorpseFallingFromHighAboveLandsOnTheRoofBeforeTheTerrain()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 20, 20);
        TestBases.Hut hut = Raids.Hut(rig, 20, 20, owner: rig.P(ada).Id.Value);
        Piece roof = hut.Roof!;
        Vector3 top = roof.ZoneTransform.Position, up = rig.Zone.Frame.UpAt(top);
        rig.Ch(ada).State.Position = top + up * 25;
        rig.Zone.Players().Die(ada, rig.P(ada), "Test", EntityId.None, "");
        Corpse corpse = rig.Zone.Things().Statics().OfType<Corpse>().Single();
        Assert.True(corpse.Support == roof.Ref, "long downward query finds the roof instead of skipping to the terrain");
        Assert.Near(0, Vector3.Dot(corpse.Position - top, up), 0.08, "body lands on the first roof surface");
    }

    [Test]
    public static void CorpseOnAStorageBoxPersistsItsSupportAndFallsWhenTheBoxGoes()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 20, 20);
        Vector3 at = rig.Zone.GroundAt(20, 20), up = rig.Zone.Frame.UpAt(at);
        var box = new Deployable(rig.Zone.NextId(), rig.C.Deployable("large-box")!, at, up, 0, rig.P(ada).Id);
        rig.Zone.Things().AddStatic(box);
        Vector3 top = box.Centre + up * box.Def.HalfExtents.Y;
        rig.Ch(ada).State.Position = top + up * 0.04f;
        rig.Zone.Players().Die(ada, rig.P(ada), "Test", EntityId.None, "");
        Corpse corpse = rig.Zone.Things().Statics().OfType<Corpse>().Single();
        Assert.True(corpse.Support == box.Ref, "solid deployable owns the body's support link");
        Assert.Near(0, Vector3.Dot(corpse.Position - top, up), 0.08, "body lies on the box lid");
        byte[] saved = WorldCodec.EncodeThing(corpse, rig.Zone.Frame.ToBody(corpse.Position), rig.Zone.Tick);
        Corpse restored = (Corpse)WorldCodec.DecodeThing(corpse.Id, corpse.Kind, saved, rig.C, rig.Zone.Frame.ToLocal, rig.Zone.Frame.UpAt, 100)!;
        Assert.True(restored.Support == box.Ref, "save preserves removable deployable support");
        rig.Zone.Things().RemoveStatic(box);
        Assert.True(corpse.Support.IsNone && corpse.Moved > 0, "box removal releases and republishes the body's position");
        Assert.Near(0, rig.Zone.HeightAboveGround(corpse.Position), 0.08, "body falls to ground when box disappears");
    }

    [Test]
    public static void CorpseHarvestUsesSharedDamageAndNeverConsumesTheLoot()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 20, 20), bo = rig.Add("Bo", 21, 20);
        rig.Give(ada, "wood", 137);
        rig.Zone.Players().Die(ada, rig.P(ada), "Test", EntityId.None, "");
        Corpse corpse = rig.Zone.Things().Statics().OfType<Corpse>().Single();
        ItemStack[] loot = corpse.Loot.Slots.ToArray();
        Container identity = corpse.Loot;
        Vector3 origin = corpse.Position + corpse.Up * 1.5f;
        TraceHit hit = rig.Zone.Damage().Trace(origin, -corpse.Up, 2, 0, rig.P(bo).Id, rig.Zone.Tick);
        Assert.True(hit.Target == corpse.Ref, "body is found through the normal melee trace");
        ItemDef tool = rig.C.ItemByKey("stone-hatchet");
        var request = new DamageRequest { Hit = hit, By = rig.P(bo), Weapon = tool.Id, Channel = DamageType.Melee, Delivery = DamageDelivery.Melee };
        for (int i = 0; i < 100 && corpse.IsBody; i++) rig.Zone.Damage().Damage(request);
        Assert.True(!corpse.IsBody && ReferenceEquals(identity, corpse.Loot), "harvesting changes phase without replacing container");
        Assert.True(corpse.Loot.Slots.SequenceEqual(loot), "all loot and layout survives harvest exactly");
        foreach (ItemAmount item in rig.C.Survival.Corpse.Yield)
            Assert.Equal(item.Count, rig.P(bo).Inventory.Count(item.Item), "harvest gives the configured yield once");
        rig.Zone.Damage().Damage(request);
        foreach (ItemAmount item in rig.C.Survival.Corpse.Yield)
            Assert.Equal(item.Count, rig.P(bo).Inventory.Count(item.Item), "bag cannot renew harvest");
    }

    [Test]
    public static void CorpseSaveKeepsPhaseHarvestAppearanceAndExactLoot()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 20, 20);
        rig.P(ada).Body = 1;
        ItemDef shirt = rig.C.ItemByKey("woven-shirt");
        rig.P(ada).Inventory.Wear.Set(0, ItemStack.Of(shirt, 1));
        rig.Give(ada, "wood", 137);
        rig.Zone.Players().Die(ada, rig.P(ada), "Test", EntityId.None, "");
        Corpse corpse = rig.Zone.Things().Statics().OfType<Corpse>().Single();
        var gathered = new List<ItemAmount>();
        corpse.Life!.Harvest(33, gathered);
        corpse.Life.Restore(CorpseStage.Body, 1200, corpse.Life.HarvestLeft, rig.Zone.Tick);
        byte[] bytes = WorldCodec.EncodeThing(corpse, rig.Zone.Frame.ToBody(corpse.Position), rig.Zone.Tick);
        Corpse restored = (Corpse)WorldCodec.DecodeThing(corpse.Id, corpse.Kind, bytes, rig.C, rig.Zone.Frame.ToLocal, rig.Zone.Frame.UpAt, 50)!;
        Assert.True(restored.IsBody && restored.Life!.Body == 1 && restored.Life.Wear[0] == shirt.Id, "phase and original appearance survive save");
        Assert.Equal(1200L, restored.Life!.RemainingBodyTicks(50), "server downtime does not age the body");
        Assert.Near(67, restored.Life.HarvestLeft, 0, "partial harvest preserved");
        Assert.True(restored.Loot.Slots.SequenceEqual(corpse.Loot.Slots), "saved loot is exact");
        restored.Life.Harvest(1000, gathered);
        foreach (ItemAmount item in rig.C.Survival.Corpse.Yield)
            Assert.Equal(item.Count, gathered.Where(x => x.Item == item.Item).Sum(x => x.Count), "restart never replenishes harvested yield");
        restored.Life.Advance(51);
        byte[] bagBytes = WorldCodec.EncodeThing(restored, rig.Zone.Frame.ToBody(restored.Position), 51);
        Corpse bag = (Corpse)WorldCodec.DecodeThing(restored.Id, restored.Kind, bagBytes, rig.C, rig.Zone.Frame.ToLocal, rig.Zone.Frame.UpAt, 200)!;
        Assert.True(!bag.IsBody && bag.Life!.HarvestLeft == 0, "saved bag never turns back into body");
        // v6 ends after the common rides flag; its lack of body authority must never award fresh harvest.
        var legacy = new Corpse(new EntityId(999), "Old", EntityId.None, 1, corpse.Position, corpse.Up, 0, rig.C.Survival.BodyLifetime, 500);
        legacy.Loot.Set(0, ItemStack.Of(shirt, 1));
        byte[] old = WorldCodec.EncodeThing(legacy, rig.Zone.Frame.ToBody(legacy.Position), 0);
        Array.Resize(ref old, old.Length - 10); // v7 support family/id + absent lifecycle marker
        old[0] = 6;
        Corpse migrated = (Corpse)WorldCodec.DecodeThing(legacy.Id, legacy.Kind, old, rig.C, rig.Zone.Frame.ToLocal, rig.Zone.Frame.UpAt, 0)!;
        Assert.True(!migrated.IsBody && migrated.Life == null && migrated.Loot[0].Item == shirt.Id, "legacy loot migrates as bag with no duplicate harvest");
    }

    [Test]
    public static void CorpseAboardKeepsItsDeckPoseThroughFlightAndBagConversion()
    {
        using var rig = new IronWakeRig();
        Entity ada = rig.Aboard("Ada", new Vector3(-2, 3.2f, 7), -Vector3.UnitZ);
        rig.Game.Give(ada, "wood", 47);
        rig.Step(20);
        rig.Zone.Players().Die(ada, rig.Game.P(ada), "Test", EntityId.None, "");
        Corpse corpse = rig.Zone.Things().Statics().OfType<Corpse>().Single();
        Assert.True(corpse.Rides && corpse.Link.Support == rig.Ship.Ref && corpse.IsBody, "body lies on the ship's support frame");
        Vector3 local = corpse.Link.Position;
        Assert.Near(3.2, local.Y, 0.1, "body rests on the deck instead of terrain underneath");
        rig.Pilot("Bo");
        rig.Fly(60, _ => new Hollow.Simulation.Ships.ShipInput { Vertical = 0.7f, Yaw = 0.3f });
        Assert.Near(0, Vector3.Distance(corpse.Position, rig.Pose.ToWorld(local)), 0.002, "body follows moving deck");
        corpse.Life!.Restore(CorpseStage.Body, 1, corpse.Life.HarvestLeft, rig.Zone.Tick);
        rig.Step(31);
        Assert.True(!corpse.IsBody && corpse.Link.Position == local && corpse.Loot[0].Count == 47, "bag retains support and exact loot");
    }
}
