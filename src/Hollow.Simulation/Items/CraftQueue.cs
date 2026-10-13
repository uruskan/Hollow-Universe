using System;
using System.Collections.Generic;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Items;

/// <summary>A crafting job: <see cref="Count"/> units of a recipe left, the front one <see cref="TicksLeft"/> from done.</summary>
public struct CraftJob
{
    /// <summary>Names the job for a cancel; grows per character and never repeats.</summary>
    public uint Ticket;

    /// <summary>The recipe (its output item's ID).</summary>
    public ushort Recipe;

    /// <summary>Units still to make, the one in progress included.</summary>
    public int Count;

    /// <summary>Ticks until the unit in progress is done (only the front job progresses).</summary>
    public int TicksLeft;

    public int UnitTicks;
}

/// <summary>
/// A player's crafting queue (GROUND.md §6, RustMVP inventory_survival.md §7): at most <see cref="MaxJobs"/> jobs, the
/// ingredients of every unit taken when the job starts and given back in full on cancel (the unit in progress too),
/// only the front job advancing, one tick at a time. A unit whose output doesn't fit is dropped at the player's feet
/// (Rust) rather than holding the queue.
/// </summary>
public sealed class CraftQueue
{
    public const int MaxJobs = 8;
    public const int MaxCount = 999;

    private readonly List<CraftJob> _jobs = new();

    public IReadOnlyList<CraftJob> Jobs => _jobs;

    /// <summary>The next job's ticket; kept across lives so a ticket never names two jobs.</summary>
    public uint NextTicket { get; set; } = 1;

    /// <summary>Raised on every change (start, cancel, a unit done), for replication.</summary>
    public uint Revision { get; private set; }

    public bool IsEmpty => _jobs.Count == 0;

    /// <summary>
    /// Starts a job: checks the recipe (known from the start, or learnt: <paramref name="known"/>, G7), the workbench
    /// level within reach and the materials for all units, then takes them. <paramref name="workbench"/> is the best
    /// level within reach.
    /// </summary>
    public ActionResult Start(GameContent content, PlayerInventory inventory, int recipeId, int count, int workbench, out uint ticket,
        BlueprintBook? known = null)
    {
        ticket = 0;
        Recipe? recipe = content.Recipe(recipeId);
        if (recipe == null || count is < 1 or > MaxCount)
        {
            return ActionResult.Invalid;
        }
        if (!recipe.Known && known?.Knows(recipe) != true)
        {
            return ActionResult.UnknownRecipe;
        }
        if (workbench < recipe.Workbench)
        {
            return ActionResult.NeedsWorkbench;
        }
        if (_jobs.Count >= MaxJobs)
        {
            return ActionResult.QueueFull;
        }
        if (!inventory.Take(recipe.Ingredients, count))
        {
            return ActionResult.NotEnough;
        }
        ticket = NextTicket++;
        _jobs.Add(new CraftJob { Ticket = ticket, Recipe = recipe.Id, Count = count, TicksLeft = recipe.UnitTicks, UnitTicks = recipe.UnitTicks });
        Revision++;
        return ActionResult.Accepted;
    }

    /// <summary>
    /// Cancels a job and gives back the ingredients of every unit left (the one in progress included). What doesn't
    /// fit goes into <paramref name="overflow"/> for the caller to drop.
    /// </summary>
    public ActionResult Cancel(GameContent content, PlayerInventory inventory, uint ticket, List<ItemStack> overflow)
    {
        int index = _jobs.FindIndex(j => j.Ticket == ticket);
        if (index < 0)
        {
            return ActionResult.NotFound;
        }
        CraftJob job = _jobs[index];
        _jobs.RemoveAt(index);
        Revision++;
        Recipe? recipe = content.Recipe(job.Recipe);
        if (recipe != null)
        {
            foreach (ItemAmount a in recipe.Ingredients)
            {
                Refund(content, inventory, a.Item, a.Count * job.Count, overflow);
            }
        }
        return ActionResult.Accepted;
    }

    /// <summary>
    /// One tick of crafting: the front job's unit advances; when it is done its output goes into the inventory (what
    /// doesn't fit into <paramref name="overflow"/>) and is reported in <paramref name="finished"/>.
    /// </summary>
    public void Advance(GameContent content, PlayerInventory inventory, List<ItemStack> overflow, List<ItemAmount> finished)
    {
        if (_jobs.Count == 0)
        {
            return;
        }
        CraftJob job = _jobs[0];
        if (--job.TicksLeft > 0)
        {
            _jobs[0] = job;
            return;
        }
        Recipe? recipe = content.Recipe(job.Recipe);
        if (recipe != null)
        {
            int left = inventory.Give(content, recipe.Output, recipe.Amount);
            if (left > 0)
            {
                overflow.Add(new ItemStack(recipe.Output.Id, left, (ushort)Math.Clamp(recipe.Output.MaxCondition, 0, ushort.MaxValue)));
            }
            finished.Add(new ItemAmount(recipe.Output, recipe.Amount));
        }
        job.Count--;
        if (job.Count <= 0 || recipe == null)
        {
            _jobs.RemoveAt(0);
        }
        else
        {
            job.TicksLeft = job.UnitTicks;
            _jobs[0] = job;
        }
        Revision++;
    }

    /// <summary>Cancels everything, returning all ingredients held for the jobs (a death puts them in the corpse).</summary>
    public List<ItemStack> TakeAll(GameContent content)
    {
        var result = new List<ItemStack>();
        foreach (CraftJob job in _jobs)
        {
            if (content.Recipe(job.Recipe) is not { } recipe)
            {
                continue;
            }
            foreach (ItemAmount a in recipe.Ingredients)
            {
                int left = a.Count * job.Count;
                while (left > 0)
                {
                    int n = Math.Min(left, a.Item.StackSize);
                    result.Add(ItemStack.Of(a.Item, n));
                    left -= n;
                }
            }
        }
        if (_jobs.Count > 0)
        {
            _jobs.Clear();
            Revision++;
        }
        return result;
    }

    /// <summary>Replaces the queue (a save loaded, or the client taking the server's copy).</summary>
    public void Set(IEnumerable<CraftJob> jobs, uint nextTicket)
    {
        _jobs.Clear();
        foreach (CraftJob job in jobs)
        {
            if (_jobs.Count < MaxJobs && job.Count > 0)
            {
                _jobs.Add(job);
            }
        }
        NextTicket = Math.Max(nextTicket, 1);
        Revision++;
    }

    private static void Refund(GameContent content, PlayerInventory inventory, ItemDef item, int count, List<ItemStack> overflow)
    {
        int left = inventory.Give(content, item, count);
        while (left > 0)
        {
            int n = Math.Min(left, item.StackSize);
            overflow.Add(ItemStack.Of(item, n));
            left -= n;
        }
    }
}
