using System;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class DeployableRepairTests
{
    [Test]
    public static void DeployableRepairUsesHammerRightsRaidDelayAndAllMaterials()
    {
        using var rig = new GameRig();
        Zone zone = rig.Zone;
        Entity owner = rig.Add("RepairOwner", 20, 20), other = rig.Add("RepairOther", 21, 20);
        ItemDef wood = rig.C.ItemByKey("wood"), metal = rig.C.ItemByKey("metal-fragments");
        Deployable thing = zone.Players().SpawnDeployable(new DeployableDef
        {
            Key = "repair-fixture", Item = rig.C.ItemByKey("small-box"), Health = 500, HalfExtents = new Vector3(.3f), Solid = false,
            RepairCost = new[] { new ItemAmount(wood, 100), new ItemAmount(metal, 50) },
        }, zone.GroundAt(20, 21.2f), 0, rig.P(owner).Id);
        thing.Health = 250;
        zone.Damage().Damage(new DamageRequest
        {
            Delivery = DamageDelivery.Melee, Channel = DamageType.Melee, Amounts = new DamageAmounts { Structure = 10 },
            Hit = new TraceHit { Target = thing.Ref, Point = thing.Centre },
        });
        Assert.Equal(zone.UnixMilliseconds, thing.LastDamagedAtUnixMs, "shared damage receiver starts the raid repair delay");
        Assert.True(zone.Building().Repair(owner, thing.Ref) == OutcomeCode.NoTool, "repair requires the hammer through the ordinary input route");
        rig.Give(owner, "hammer", 1);
        rig.Give(other, "hammer", 1);
        Assert.True(zone.Building().Repair(owner, thing.Ref) == OutcomeCode.RecentlyDamaged, "recent damage refuses repair");
        zone.UnixMilliseconds += 31_000;
        Assert.True(zone.Building().Repair(other, thing.Ref) == OutcomeCode.NotOwner, "unclaimed object remains its owner's to repair");
        rig.Give(owner, "wood", 100);
        int beforeWood = rig.P(owner).Inventory.Count(wood);
        Assert.True(zone.Building().Repair(owner, thing.Ref) == OutcomeCode.NotEnough, "all required material kinds must be available");
        Assert.Equal(beforeWood, rig.P(owner).Inventory.Count(wood), "missing second material consumes none of the first");
        rig.Give(owner, "metal-fragments", 100);
        float before = thing.Health, restored = Math.Min((float)rig.C.Building.RepairPerHit, thing.Def.Health - before);
        int woodCost = zone.Building().World.RepairCost(100, restored, 500), metalCost = zone.Building().World.RepairCost(50, restored, 500);
        Assert.True(zone.Building().Repair(owner, thing.Ref).Ok, "owner repairs with material after raid delay");
        Assert.Near(before + restored, thing.Health, .001, "repair restores the shared per-hit health");
        Assert.Equal(beforeWood - woodCost, rig.P(owner).Inventory.Count(wood), "wood uses shared proportional repair cost");
        Assert.Equal(100 - metalCost, rig.P(owner).Inventory.Count(metal), "metal uses shared proportional repair cost");
        thing.Health = thing.Def.Health;
        Assert.True(zone.Building().Repair(owner, thing.Ref) == OutcomeCode.FullHealth, "full health costs nothing");
    }

    [Test]
    public static void UtilityStateSurvivesTheRealThingCodecAndRejectsInvalidEnergy()
    {
        // Exercise optional storage without inventing a production battery asset.
        var texts = GameContent.ReadTexts(ContentFixture.Folder);
        JsonNode document = JsonNode.Parse(texts["deployables.json"])!;
        JsonNode power = document["deployables"]!.AsArray().First(n => n!["key"]!.GetValue<string>() == "windmill")!["power"]!;
        power["capacity"] = 100; power["chargeRate"] = 10; power["dischargeRate"] = 10;
        power["transfer"] = "storage"; power["generation"] = 0; power["wind"] = false;
        power["ports"] = JsonNode.Parse("""[{"id":1,"key":"input","direction":"input","at":[0,1,0]},{"id":2,"key":"output","direction":"output","at":[0,1,0.2]}]""");
        texts["deployables.json"] = document.ToJsonString();
        GameContent content = GameContent.FromTexts(texts);
        Assert.True(content.Problems.Count == 0, "data-driven storage fixture is valid");
        using var rig = new GameRig(content: content);
        Zone zone = rig.Zone;
        var windmill = new Deployable(zone.NextId(), content.Deployable("windmill")!, zone.Landing, zone.Frame.UpAt(zone.Landing), 0, new EntityId(17))
        {
            PowerSourceEntityId = 1234567, PowerEnabled = false, StoredEnergy = 43.25f, DefenseEnabled = false,
            LastDamagedAtUnixMs = zone.UnixMilliseconds - 12_000,
        };
        Deployable RoundTrip(Deployable source) => (Deployable)WorldCodec.DecodeThing(source.Id, source.Kind,
            WorldCodec.EncodeThing(source, zone.Frame.ToBody(source.Position), zone.Tick), content, zone.Frame.ToLocal, zone.Frame.UpAt, zone.Tick)!;
        Deployable restored = RoundTrip(windmill);
        Assert.Equal(windmill.PowerSourceEntityId, restored.PowerSourceEntityId, "stable source identity survives restart");
        Assert.True(!restored.PowerEnabled && !restored.DefenseEnabled, "both switches survive restart");
        Assert.Near(43.25, restored.StoredEnergy, .0001, "stored energy survives restart exactly");
        Assert.Equal(windmill.LastDamagedAtUnixMs, restored.LastDamagedAtUnixMs, "restarting does not erase the raid repair delay");
        windmill.StoredEnergy = 1_000;
        Assert.Near(100, RoundTrip(windmill).StoredEnergy, .0001, "restored energy is clamped to capacity");
        var gate = new Deployable(zone.NextId(), content.Deployable("wood-compound-gate")!, zone.Landing, zone.Frame.UpAt(zone.Landing), 0, new EntityId(17)) { Open = true };
        Assert.True(RoundTrip(gate).Open, "gate's open target survives restart");
    }
}
