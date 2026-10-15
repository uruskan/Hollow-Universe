using System.Linq;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Economy;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>The design's fresh start and separate technology, station and crafting-speed rules.</summary>
public static class ProgressionTests
{
    [Test]
    public static void NewLifeHasExactlyOneRockAndOneTorch()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0, kit: true);
        Player player = rig.P(ada);
        AssertKit();
        rig.Zone.Players().Sleep(ada);
        rig.Zone.Players().Wake(ada);
        AssertKit(); // Reconnecting cannot multiply the kit.
        rig.Zone.Players().Hurt(ada, player, 1000, "Test", EntityId.None, "");
        Assert.True(!player.Inventory.Stacks().Any(), "everything is left on the body");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Players().Respawn(ada, EntityId.None), "new life at the beach");
        AssertKit();

        void AssertKit()
        {
            ItemStack[] stacks = player.Inventory.Stacks().ToArray();
            Assert.Equal(2, stacks.Length, "exactly two stacks, without supplies or equipment");
            Assert.Equal(1, player.Inventory.Count(rig.C.ItemByKey("rock")), "one rock");
            Assert.Equal(1, player.Inventory.Count(rig.C.ItemByKey("torch")), "one torch");
            Assert.Equal(rig.C.ItemByKey("rock").Id, player.Inventory.Belt[0].Item, "rock ready in first hotbar slot");
            Assert.Equal(rig.C.ItemByKey("torch").Id, player.Inventory.Belt[1].Item, "torch ready in second hotbar slot");
            Assert.True(player.Inventory.Main.IsEmpty, "empty backpack");
            Assert.True(player.Inventory.Wear.IsEmpty, "no clothing");
        }
    }

    [Test]
    public static void SpecializedProgressionKeepsLearningStationAndSpeedSeparate()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 30, 30);
        Player player = rig.P(ada);
        Deployable vehicle = Bench("vehicle-workbench", 31.5f, 30);
        Deployable engineering = Bench("engineering-workbench", 28.5f, 30);
        Deployable standard = Bench("workbench-3", 30, 28.5f);
        Recipe recipe = c.Recipe(c.ItemByKey("tin-finch").Id)!;
        TechNode node = c.World.NodeOf(recipe.Id)!;

        Assert.True(!recipe.Known && node.Station == vehicle.Def, "spacecraft are learned in the Vehicle Workbench tree");
        Assert.Equal(0, vehicle.Def.Workbench, "the Vehicle Workbench has no standard level");
        Assert.Equal(0, engineering.Def.Workbench, "the Engineering Workbench has no standard level");
        Assert.True(engineering.Def.TechTree is { Nodes.Count: 0 }, "engineering owns an empty tree until its technologies are designed");
        Assert.True(c.World.Tree(1) != null && c.World.Tree(2) != null && c.World.Tree(3) != null, "all standard tiers remain");
        Assert.Equal(OutcomeCode.UnknownRecipe, rig.Zone.Actions().Craft(ada, recipe.Id, 1, out _), "having the station alone does not teach the ship");
        Assert.Equal(OutcomeCode.NeedsStation, Unlock(standard), "a level 3 bench cannot teach vehicle technology");
        Assert.Equal(OutcomeCode.NeedsStation, Unlock(engineering), "engineering cannot teach vehicle technology");
        Assert.Equal(OutcomeCode.NotEnough, Unlock(vehicle), "learning needs salvage");
        rig.Give(ada, "salvage", node.Cost * 2);
        Assert.Equal(OutcomeCode.Done, Unlock(vehicle), "the correct specialized bench unlocks it");
        Assert.Equal(node.Cost, player.Inventory.Count(c.ItemByKey("salvage")), "one unlock price consumed");
        Assert.Equal(OutcomeCode.AlreadyKnown, Unlock(vehicle), "duplicate unlock refused");
        Assert.Equal(node.Cost, player.Inventory.Count(c.ItemByKey("salvage")), "duplicate does not charge again");
        Assert.Near(recipe.Seconds, CraftingSpeed.Seconds(c, recipe, 3), 0.001, "standard bench speed does not accelerate specialized assembly");

        rig.Ch(ada).State.Position = rig.Zone.GroundAt(90, 90);
        foreach (ItemAmount ingredient in recipe.Ingredients)
        {
            rig.Give(ada, ingredient.Item.Key, ingredient.Count);
        }
        Assert.Equal(OutcomeCode.NeedsStation, rig.Zone.Actions().Craft(ada, recipe.Id, 1, out _), "learned spacecraft still require the Vehicle Workbench");

        // Finding a physical blueprint is a separate learning route; it never removes the crafting-station gate.
        Entity bo = rig.Add("Bo", 100, 100);
        Player other = rig.P(bo);
        other.Inventory.Main.Set(0, TechTrees.BlueprintOf(c, recipe));
        Assert.Equal(OutcomeCode.Done, rig.Act(bo, ContainerRef.Main, 0, "learn"), "found ship blueprint is learned");
        Assert.True(other.Blueprints.Knows(recipe) && other.Inventory.Main[0].IsEmpty, "learning consumes the blueprint");
        Assert.Equal(OutcomeCode.NeedsStation, rig.Zone.Actions().Craft(bo, recipe.Id, 1, out _), "the alternative route also requires the station");

        Outcome Unlock(Deployable bench) => rig.Zone.Interaction().Interact(ada, bench.Ref, "unlock", recipe.Id);
        Deployable Bench(string key, float x, float z)
        {
            var at = rig.Zone.GroundAt(x, z);
            var bench = new Deployable(rig.Zone.NextId(), c.Deployable(key)!, at, rig.Zone.Frame.UpAt(at), 0, player.Id);
            rig.Zone.Things().AddStatic(bench);
            return bench;
        }
    }
}
