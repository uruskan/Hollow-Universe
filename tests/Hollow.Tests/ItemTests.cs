using System.Collections.Generic;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;

namespace Hollow.Tests;

public static class ItemTests
{
    private static GameContent C => ContentFixture.Content;

    private static ItemStack S(string key, int count) => ItemStack.Of(ContentFixture.Item(key), count);

    /// <summary>
    /// GROUND.md §6, inventory edge cases: moves, splits, top-ups that leave the rest, swaps of whole stacks only, stale
    /// and malformed requests; clothing one per layer; device slots by kind; corpses take only; quick move merges first.
    /// </summary>
    [Test]
    public static void InventoryEdgeCases()
    {
        var inv = new PlayerInventory();
        Container main = inv.Main, belt = inv.Belt, wear = inv.Wear;
        main.Set(0, S("wood", 900));
        main.Set(1, S("wood", 300));
        main.Set(2, S("stone", 50));

        // A top-up moves what fits and leaves the rest; onto a full stack there's no room.
        Assert.Equal(ActionResult.Accepted, InventoryRules.Move(C, main, 1, main, 0, 0), "top up");
        Assert.Equal(1000, main[0].Count, "full stack");
        Assert.Equal(200, main[1].Count, "the rest stays");
        Assert.Equal(ActionResult.NoSpace, InventoryRules.Move(C, main, 1, main, 0, 0), "onto a full stack");

        // Split into an empty slot; more than there is, or the wrong item, is stale; the same slot is malformed.
        Assert.Equal(ActionResult.Accepted, InventoryRules.Move(C, main, 1, belt, 0, 75), "split");
        Assert.True(main[1].Count == 125 && belt[0].Count == 75, "split counts");
        Assert.Equal(ActionResult.Stale, InventoryRules.Move(C, main, 1, belt, 1, 126), "more than there is");
        Assert.Equal(ActionResult.Stale, InventoryRules.Move(C, main, 1, belt, 1, 0, C.ItemByKey("stone").Id), "not the item expected");
        Assert.Equal(ActionResult.Invalid, InventoryRules.Move(C, main, 1, main, 1, 0), "onto itself");
        Assert.Equal(ActionResult.Invalid, InventoryRules.Move(C, main, 1, main, 24, 0), "past the end");
        Assert.Equal(ActionResult.Stale, InventoryRules.Move(C, main, 5, main, 6, 0), "from an empty slot");

        // Different items swap, but only whole stacks.
        Assert.Equal(ActionResult.NotAllowed, InventoryRules.Move(C, main, 2, main, 1, 10), "part of a stack onto another item");
        Assert.Equal(ActionResult.Accepted, InventoryRules.Move(C, main, 2, main, 1, 0), "swap");
        Assert.True(main[1].Item == C.ItemByKey("stone").Id && main[2].Item == C.ItemByKey("wood").Id, "swapped");

        // Clothing: only wearables, one per layer; a shirt and a chest plate go together; a worn shirt swaps for another.
        main.Set(3, S("woven-shirt", 1));
        main.Set(4, S("woven-shirt", 1));
        main.Set(5, S("chest-plate", 1));
        Assert.Equal(ActionResult.NotAllowed, InventoryRules.Move(C, main, 2, wear, 0, 0), "wood isn't worn");
        Assert.Equal(ActionResult.Accepted, InventoryRules.Move(C, main, 3, wear, 0, 0), "wear a shirt");
        Assert.Equal(ActionResult.NotAllowed, InventoryRules.Move(C, main, 4, wear, 1, 0), "a second shirt");
        Assert.Equal(ActionResult.Accepted, InventoryRules.Move(C, main, 5, wear, 1, 0), "a chest plate over it");
        Assert.Equal(ActionResult.Accepted, InventoryRules.Move(C, main, 4, wear, 0, 0), "swap the worn shirt for another");
        Assert.True(!main[4].IsEmpty && main[4].Item == C.ItemByKey("woven-shirt").Id, "the old shirt came back");
        Assert.True(inv.Protection(C, HitRegion.Body, projectile: false) > 0.3f, "body protection adds up");
        Assert.Equal(0f, inv.Protection(C, HitRegion.Head, projectile: false), "nothing on the head");

        // A furnace takes fuel in its fuel slot, ore in its inputs, nothing in its outputs; a corpse takes nothing.
        var furnace = new Container(ContainerKind.Device, 7, C.Deployable("furnace"));
        main.Set(6, S("metal-ore", 100));
        Assert.Equal(ActionResult.NotAllowed, InventoryRules.Move(C, main, 6, furnace, 0, 0), "ore isn't fuel");
        Assert.Equal(ActionResult.NotAllowed, InventoryRules.Move(C, main, 6, furnace, 4, 0), "nothing goes into an output");
        Assert.Equal(ActionResult.Accepted, InventoryRules.Move(C, main, 6, furnace, 1, 0), "ore into an input");
        Assert.Equal(ActionResult.Accepted, InventoryRules.QuickMove(C, main, 2, new[] { furnace }), "quick move finds the fuel slot");
        Assert.Equal(C.ItemByKey("wood").Id, furnace[0].Item, "wood in the fuel slot");
        var corpse = new Container(ContainerKind.Corpse, 37);
        Assert.Equal(ActionResult.NotAllowed, InventoryRules.Move(C, main, 1, corpse, 0, 0), "corpses are take only");

        // Quick move merges onto stacks first, then fills empty slots; what doesn't fit stays.
        var box = new Container(ContainerKind.Box, 2);
        box.Set(0, S("stone", 990));
        box.Set(1, S("rock", 1));
        main.Set(7, S("stone", 30));
        Assert.Equal(ActionResult.Accepted, InventoryRules.QuickMove(C, main, 7, new[] { box }), "quick move");
        Assert.True(box[0].Count == 1000 && main[7].Count == 20, "topped up, the rest stays");
        Assert.Equal(ActionResult.NoSpace, InventoryRules.QuickMove(C, main, 7, new[] { box }), "a full box");

        // Gains land on stacks, then main, then belt; the overflow is returned; conditioned items never stack.
        var small = new PlayerInventory();
        for (int i = 0; i < PlayerInventory.MainSlots; i++)
        {
            small.Main.Set(i, S("stone", 1000));
        }
        Assert.Equal(0, small.Give(C, C.ItemByKey("wood"), 2500), "fits on the belt");
        Assert.True(small.Belt[1].Count == 1000 && small.Belt[2].Count == 500, "two and a half stacks on the belt");
        Assert.Equal(500, small.Give(C, C.ItemByKey("wood"), 4000), "500 tops up, 3000 fill the belt, 500 over");
        var tools = new PlayerInventory();
        tools.Give(C, C.ItemByKey("rock"), 2);
        Assert.True(tools.Main[0].Count == 1 && tools.Main[1].Count == 1 && tools.Main[0].Condition == 200, "tools come new, one per slot");
        Assert.True(!tools.Take(new[] { new ItemAmount(C.ItemByKey("rock"), 3) }) && tools.Count(C.ItemByKey("rock")) == 2,
            "taking more than there is takes nothing");
    }

    /// <summary>
    /// GROUND.md §6, crafting: ingredients for every unit taken at once; each unit takes the recipe's time; a cancel
    /// gives everything back (the unit in progress too, the overflow for the ground); limits on the queue and benches.
    /// </summary>
    [Test]
    public static void CraftingTimingAndRefunds()
    {
        var inv = new PlayerInventory();
        var queue = new CraftQueue();
        var overflow = new List<ItemStack>();
        var finished = new List<ItemAmount>();
        inv.Give(C, C.ItemByKey("wood"), 500);
        inv.Give(C, C.ItemByKey("stone"), 300);
        Recipe hatchet = C.Recipe(C.ItemByKey("stone-hatchet").Id)!;
        Assert.Equal(ActionResult.NotEnough, queue.Start(C, inv, hatchet.Id, 3, 0, out _), "three need 600 wood");
        Assert.Equal(500, inv.Count(C.ItemByKey("wood")), "nothing taken");
        Assert.Equal(ActionResult.Accepted, queue.Start(C, inv, hatchet.Id, 2, 0, out uint ticket), "two");
        Assert.True(inv.Count(C.ItemByKey("wood")) == 100 && inv.Count(C.ItemByKey("stone")) == 100, "ingredients for both taken");
        Assert.Equal(ActionResult.NeedsWorkbench, queue.Start(C, inv, C.ItemByKey("metal-hatchet").Id, 1, 0, out _), "needs a bench");
        Assert.Equal(ActionResult.Invalid, queue.Start(C, inv, 9999, 1, 0, out _), "no such recipe");

        int unit = hatchet.UnitTicks;
        Assert.Equal(15 * CharacterTicks.Rate, unit, "15 s a hatchet");
        for (int t = 1; t < unit; t++)
        {
            queue.Advance(C, inv, overflow, finished);
        }
        Assert.Equal(0, finished.Count, "not before its time");
        queue.Advance(C, inv, overflow, finished);
        Assert.True(finished.Count == 1 && inv.Count(C.ItemByKey("stone-hatchet")) == 1, "the first on its tick");
        for (int t = 0; t < unit / 2; t++)
        {
            queue.Advance(C, inv, overflow, finished);
        }
        // Cancel halfway through the second: its ingredients come back whole.
        Assert.Equal(ActionResult.Accepted, queue.Cancel(C, inv, ticket, overflow), "cancel");
        Assert.True(inv.Count(C.ItemByKey("wood")) == 300 && inv.Count(C.ItemByKey("stone")) == 200 && queue.IsEmpty,
            $"refund of the unit in progress: {inv.Count(C.ItemByKey("wood"))} wood");
        Assert.Equal(ActionResult.NotFound, queue.Cancel(C, inv, ticket, overflow), "cancelled once");

        // The queue holds eight jobs; a refund into a full inventory overflows instead of vanishing.
        inv.Give(C, C.ItemByKey("stone"), 200);
        for (int i = 0; i < CraftQueue.MaxJobs; i++)
        {
            Assert.Equal(ActionResult.Accepted, queue.Start(C, inv, C.ItemByKey("rock").Id, 1, 0, out _), $"job {i + 1}");
        }
        Assert.Equal(ActionResult.QueueFull, queue.Start(C, inv, C.ItemByKey("rock").Id, 1, 0, out _), "a ninth");
        for (int i = 0; i < PlayerInventory.MainSlots; i++)
        {
            inv.Main.Set(i, S("wood", 1000));
        }
        for (int i = 0; i < PlayerInventory.BeltSlots; i++)
        {
            inv.Belt.Set(i, S("wood", 1000));
        }
        overflow.Clear();
        Assert.Equal(ActionResult.Accepted, queue.Cancel(C, inv, queue.Jobs[3].Ticket, overflow), "cancel with no room");
        Assert.True(overflow.Count == 1 && overflow[0].Count == 10, "the refund overflows to the ground");
        List<ItemStack> escrow = queue.TakeAll(C);
        Assert.True(escrow.Count == 7 && queue.IsEmpty, "a death takes what the queue held");
    }
}
