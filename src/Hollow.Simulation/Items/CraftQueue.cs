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

    /// <summary>The station it is made at (a vehicle workbench's entity ID, G6), or 0.</summary>
    public ulong Station;

    /// <summary>No ingredients were paid for this job; cancellation and death must not refund any.</summary>
    public bool Free;
}

/// <summary>
/// A player's crafting queue (GROUND.md §6, RustMVP inventory_survival.md §7): at most <see cref="MaxJobs"/> jobs, the
/// ingredients of every unit taken when the job starts and given back in full on cancel (the unit in progress too),
/// only the front job advancing, one tick at a time. A unit whose output doesn't fit is dropped at the player's feet
/// (Rust) rather than holding the queue. Free admin jobs pay and refund nothing.
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
    /// level within reach: above the recipe's, the job is faster (<see cref="CraftingSpeed"/>, fixed now);
    /// <paramref name="station"/> the station the job is made at (a ship at the vehicle workbench, G6), 0 for none.
    /// <paramref name="free"/> bypasses prerequisites and payment, with the job retaining that fact for refunds.
    /// </summary>
    public Outcome Start(GameContent content, PlayerInventory inventory, int recipeId, int count, int workbench, out uint ticket,
        BlueprintBook? known = null, ulong station = 0, bool free = false)
    {
        ticket = 0;
        Recipe? recipe = content.Recipe(recipeId);
        if (recipe == null || count is < 1 or > MaxCount)
        {
            return OutcomeCode.Invalid;
        }
        if (!free && !recipe.Known && known?.Knows(recipe) != true)
        {
            return OutcomeCode.UnknownRecipe;
        }
        if (!free && workbench < recipe.Workbench)
        {
            return OutcomeCode.NeedsWorkbench.With(recipe.Workbench);
        }
        if (!free && recipe.Station != null && station == 0)
        {
            return OutcomeCode.NeedsStation.With(recipe.Station.Item.Id);
        }
        if (_jobs.Count >= MaxJobs)
        {
            return OutcomeCode.QueueFull;
        }
        if (!free && !inventory.Take(recipe.Ingredients, count))
        {
            return OutcomeCode.NotEnough;
        }
        ticket = NextTicket++;
        int unit = CraftingSpeed.UnitTicks(content, recipe, workbench);
        _jobs.Add(new CraftJob { Ticket = ticket, Recipe = recipe.Id, Count = count, TicksLeft = unit, UnitTicks = unit, Station = station, Free = free });
        Revision++;
        return OutcomeCode.Done;
    }

    /// <summary>
    /// Cancels a job and gives back the ingredients of every unit left (the one in progress included). What doesn't
    /// fit goes into <paramref name="overflow"/> for the caller to drop.
    /// </summary>
    public Outcome Cancel(GameContent content, PlayerInventory inventory, uint ticket, List<ItemStack> overflow)
    {
        int index = _jobs.FindIndex(j => j.Ticket == ticket);
        if (index < 0)
        {
            return OutcomeCode.NotFound;
        }
        CraftJob job = _jobs[index];
        _jobs.RemoveAt(index);
        Revision++;
        Recipe? recipe = content.Recipe(job.Recipe);
        if (recipe != null && !job.Free)
        {
            foreach (ItemAmount a in recipe.Ingredients)
            {
                Refund(content, inventory, a.Item, a.Count * job.Count, overflow);
            }
        }
        return OutcomeCode.Done;
    }

    /// <summary>
    /// One tick of crafting: the front job's unit advances; when it is done its output goes into the inventory (what
    /// doesn't fit into <paramref name="overflow"/>) and is reported in <paramref name="finished"/>. A ship (G6) made at a
    /// station is rolled out there by <paramref name="deliver"/>, or waits and asks again a second later. A ship whose recipe
    /// names no station (the motorboat), and a free admin ship without a station, become its placeable inventory item.
    /// </summary>
    public void Advance(GameContent content, PlayerInventory inventory, List<ItemStack> overflow, List<ItemAmount> finished,
        Func<Recipe, ulong, bool>? deliver = null)
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
        // A ship made at a station rolls out onto its bay. A ship whose recipe names no station (a boat, put on the water by
        // hand), and a free admin ship without a station, become the placeable item; placement validates hull and rights.
        if (recipe?.Output.Ship != null && recipe.Station != null && (!job.Free || job.Station != 0))
        {
            if (deliver == null || !deliver(recipe, job.Station))
            {
                job.TicksLeft = Movement.CharacterMotor.TickRate;
                _jobs[0] = job;
                return;
            }
            finished.Add(new ItemAmount(recipe.Output, recipe.Amount));
        }
        else if (recipe != null)
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
            if (job.Free || content.Recipe(job.Recipe) is not { } recipe)
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
