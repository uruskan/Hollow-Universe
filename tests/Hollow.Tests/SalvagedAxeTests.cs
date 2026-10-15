using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Economy;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>The rare axe uses the shared gathering, loot, learning and crafting systems.</summary>
public static class SalvagedAxeTests
{
    [Test]
    public static void SalvagedAxeFellsTreesFasterWithoutMultiplyingTheirWood()
    {
        GameContent c = ContentFixture.Content;
        ZoneSpec spec = ZoneSpec.FirstWorld(Fixtures.Galaxy, 28.4, -61.7)! with { StandIn = true };
        ISurface surface = spec.CreateSurface();
        ZoneFrame frame = spec.CreateFrame(surface);
        INaturalObjects objects = spec.CreateNature(surface, frame, c);
        var near = new List<NaturalObject>();
        objects.Query(new GroundProbe(surface, frame).On(spec.LandingDirection), 280, near);
        NaturalKind pine = c.Natural("pine-tree")!;
        NaturalObject tree = near.First(o => o.Kind == pine.Index);
        ItemDef basic = c.ItemByKey("metal-hatchet"), upgraded = c.ItemByKey("salvaged-axe");

        (int basicHits, int basicWood, int basicFirst) = Fell(basic, false);
        (int upgradeHits, int upgradeWood, int upgradeFirst) = Fell(upgraded, false);
        (int spotHits, int spotWood, _) = Fell(upgraded, true);
        Assert.True(upgradeFirst > basicFirst, "the first ordinary hit gathers more wood");
        Assert.True(upgradeHits < basicHits, "the same tree takes fewer ordinary hits");
        Assert.True(upgradeHits * upgraded.Melee!.SwingSeconds < basicHits * basic.Melee!.SwingSeconds,
            "fewer hits also means less gathering time");
        Assert.Equal(NaturalShapes.Total(tree, pine, pine.Yield[0]), upgradeWood, "all the tree's wood, once");
        Assert.Equal(basicWood, upgradeWood, "the upgrade improves rate, not total renewable yield");
        Assert.True(spotHits < upgradeHits && spotWood == upgradeWood, "the new axe also benefits from the shared moving X");
        Assert.True(upgraded.MaxCondition > basic.MaxCondition, "the upgraded tool lasts longer");
        Assert.True(upgraded.Melee.Ore < c.ItemByKey("metal-pickaxe").Melee!.Ore, "an axe does not replace a mining tool");
        Fixtures.Report($"pine: metal axe {basicHits} hits, Salvaged Axe {upgradeHits}, on the X {spotHits}; {upgradeWood} wood each");

        (int Hits, int Wood, int First) Fell(ItemDef tool, bool followSpot)
        {
            var state = new NaturalState(c, objects);
            int hits = 0, wood = 0, first = 0;
            while (!state.IsDepleted(tree.Id) && hits < 100)
            {
                Vector3 point, eye;
                if (followSpot && state.WeakSpotAt(tree, pine) is { } spot)
                {
                    point = spot.Point;
                    eye = point + spot.Normal * 1.2f + tree.Up * 0.25f;
                }
                else
                {
                    // Put the first X on the west, then hit the east to compare ordinary strikes.
                    Vector3 east = Facing.Compass(tree.Up).East * (hits == 0 ? -1 : 1);
                    eye = tree.Position + east * 2 + tree.Up * 1.6f;
                    point = tree.Position + tree.Up * 1.3f;
                }
                GatherResult result = state.Hit(tree, new GatherSwing(tool, tool.Melee!, point, eye,
                    Vector3.Normalize(point - eye), 1_800_000_000_000, hits + 1));
                Assert.True(result.Hit && result.WeakSpot == (followSpot && hits > 0), "the intended kind of hit lands");
                int gained = result.Yield.Sum(y => y.Count);
                if (hits++ == 0)
                {
                    first = gained;
                }
                wood += gained;
            }
            Assert.True(state.IsDepleted(tree.Id), "the tree is felled");
            return (hits, wood, first);
        }
    }

    [Test]
    public static void SalvagedAxeLootBlueprintUnlocksCraftingAtWorkbenchTwo()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        ItemDef axe = c.ItemByKey("salvaged-axe");
        Recipe recipe = c.Recipe(axe.Id)!;
        Assert.True(!recipe.Known && c.World.NodeOf(recipe.Id) == null, "a rare variant is neither default nor a tech-tree unlock");
        Assert.True(c.World.BlueprintPool(2).Contains(recipe), "tier-2 blueprint loot can teach the axe");
        ItemStack found = FindLoot("crate");
        FindLoot("military-crate");

        Entity ada = rig.Add("Ada", 30, 30);
        Player player = rig.P(ada);
        Deployable firstBench = Bench("workbench-1", 31.5f);
        foreach (ItemAmount ingredient in recipe.Ingredients)
        {
            Assert.Equal(0, ingredient.Item.MaxCondition, "stackable materials avoid losing or refreshing a consumed tool's condition");
            rig.Give(ada, ingredient.Item.Key, ingredient.Count);
        }
        Assert.Equal(OutcomeCode.UnknownRecipe, rig.Zone.Actions().Craft(ada, recipe.Id, 1, out _), "materials and a bench do not teach the variant");
        Assert.Equal(OutcomeCode.Invalid, rig.Zone.Interaction().Interact(ada, firstBench.Ref, "unlock", recipe.Id), "the normal tech tree cannot teach it");

        player.Inventory.Belt.Set(0, found);
        Assert.Equal(OutcomeCode.Done, rig.Act(ada, ContainerRef.Belt, 0, "learn"), "the blueprint rolled from a real crate is learned");
        Assert.True(player.Blueprints.Knows(recipe) && player.Inventory.Belt[0].IsEmpty, "learning consumes the blueprint");
        Assert.Equal(OutcomeCode.NeedsWorkbench, rig.Zone.Actions().Craft(ada, recipe.Id, 1, out _), "a learned blueprint still needs level 2");
        Assert.True(player.Inventory.Has(recipe.Ingredients), "a refused craft leaves the materials untouched");
        Bench("workbench-2", 28.5f);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().Craft(ada, recipe.Id, 1, out _), "level 2 permits the learned recipe");
        Assert.True(recipe.Ingredients.All(i => player.Inventory.Count(i.Item) == 0), "one recipe's materials are consumed");
        rig.Step(CraftingSpeed.UnitTicks(c, recipe, 2));
        ItemStack crafted = player.Inventory.Stacks().Single(s => s.Item == axe.Id);
        Assert.True(crafted.Count == 1 && crafted.Condition == axe.MaxCondition && player.Crafting.IsEmpty, "crafting finishes with one full-condition axe");
        Assert.Equal(125, Research.Cost(c, recipe), "the shared research route uses the tier-2 salvage price");

        Deployable Bench(string key, float x)
        {
            Vector3 at = rig.Zone.GroundAt(x, 30);
            var bench = new Deployable(rig.Zone.NextId(), c.Deployable(key)!, at, rig.Zone.Frame.UpAt(at), 0, player.Id);
            rig.Zone.Things().AddStatic(bench);
            return bench;
        }

        ItemStack FindLoot(string key)
        {
            ItemStack blueprint = default;
            bool toolFound = false;
            LootTable table = c.Loot(key)!;
            for (ulong seed = 0; seed < 20_000 && (blueprint.IsEmpty || !toolFound); seed++)
            {
                var rng = new Rng(Hash64.Of(seed, 0x5A17));
                foreach (ItemStack stack in table.RollStacks(ref rng, c))
                {
                    if (stack.Item == c.World.Blueprint!.Id && stack.Condition == recipe.Id)
                    {
                        blueprint = stack;
                    }
                    if (stack.Item == axe.Id)
                    {
                        toolFound = true;
                        Assert.True(stack.Count == 1 && stack.Condition == axe.MaxCondition, "a found axe has valid count and condition");
                    }
                }
            }
            Assert.True(!blueprint.IsEmpty && toolFound, $"{key} can supply both the rare blueprint and the tool");
            return blueprint;
        }
    }
}
