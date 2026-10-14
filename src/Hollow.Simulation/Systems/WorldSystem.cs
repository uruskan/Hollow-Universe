using System.Collections.Generic;
using System.Numerics;
using System;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Economy;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Teams;
using Hollow.Simulation.World;

using Hollow.Simulation.Movement;

namespace Hollow.Simulation;

/// <summary>
/// What is stored about a world prop (GROUND.md §6.8, ARCHITECTURE.md P1: only changes): gone until a time, or standing
/// as players left it (a damaged barrel, a crate partly looted, a recycler with things in it or running).
/// </summary>
public sealed class PropOverride
{
    /// <summary>When it comes back (Unix ms); 0 while it stands.</summary>
    public long RespawnAtUnixMs { get; init; }

    public float Health { get; init; }

    /// <summary>Its slots as players left them (null: as it spawned).</summary>
    public ItemStack[]? Contents { get; init; }

    /// <summary>A recycler running.</summary>
    public bool Lit { get; init; }

    public int BurnTicksLeft { get; init; }
}

/// <summary>What the world's economy did (for the load test and the server's status).</summary>
public sealed class WorldStats
{
    public long BarrelsBroken { get; internal set; }
    public long CratesEmptied { get; internal set; }
    public long Respawned { get; internal set; }
    public long Batches { get; internal set; }
    public long UnitsRecycled { get; internal set; }
    public long Researched { get; internal set; }
    public long Unlocked { get; internal set; }
    public long Learned { get; internal set; }

    /// <summary>The zone's time in the world's steps: props coming and going, recyclers and research tables at work.</summary>
    public TimeSpan Time { get; internal set; }

    public override string ToString() =>
        $"{BarrelsBroken} barrels broken, {CratesEmptied} crates emptied, {Respawned} props back; {UnitsRecycled} units recycled in {Batches} batches; " +
        $"{Unlocked} blueprints unlocked, {Researched} researched, {Learned} learnt from items";
}

// The world's economy and progression in the zone (GROUND.md §6.8, G7): the barrels, crates and recyclers the world
// places, their loot and respawns; recycling and research; tech tree unlocks and blueprints; teams and the map's
// markers. Rules are Economy/, Teams/ and Nature/LootSpots; this file applies them to the zone's players and things.
public sealed class WorldSystem : ZoneSystem
{
    private BuildingSystem? _buildingSystem;
    private BuildingSystem Building => _buildingSystem ??= Zone.Require<BuildingSystem>();
    private PlayerSystem? _playersSystem;
    private PlayerSystem Players => _playersSystem ??= Zone.Require<PlayerSystem>();
    private SpillSystem? _spillSystem;
    private SpillSystem Spill => _spillSystem ??= Zone.Require<SpillSystem>();
    private ThingSystem? _thingsSystem;
    private ThingSystem Things => _thingsSystem ??= Zone.Require<ThingSystem>();

    internal readonly Dictionary<ulong, LootSpot> _spots = new();
    private readonly Dictionary<ulong, WorldProp> _props = new();
    private readonly Dictionary<ulong, long> _depleted = new();
    private readonly Dictionary<WorldProp, uint> _spawnRevisions = new();
    private readonly List<WorldProp> _gone = new();
    private readonly List<ulong> _due = new();
    private readonly List<ItemAmount> _yields = new();
    private readonly List<ulong> _teamIds = new();

    /// <summary>The zone's teams (G7).</summary>
    public TeamRegistry Teams { get; private set; } = null!;

    /// <summary>Where the world's props stand (generated once <see cref="StartWorld"/> runs), or null before.</summary>
    public LootSpots? LootSpots { get; private set; }

    /// <summary>The world props standing now: barrels, crates, recyclers.</summary>
    public IReadOnlyCollection<WorldProp> Props => _props.Values;

    public WorldStats WorldStats { get; } = new();

    /// <summary>The prop standing on a spot, or null (gone until its respawn, or no such spot).</summary>
    public WorldProp? PropAt(ulong spot) => _props.GetValueOrDefault(spot);

    /// <summary>When a gone prop comes back (Unix ms), or null while it stands.</summary>
    public long? RespawnOf(ulong spot) => _depleted.TryGetValue(spot, out long at) ? at : null;

    private void InitWorld(GameContent content)
    {
        Teams = new TeamRegistry(content.World.TeamSize, content.World.InviteSeconds, () => NextId().Value);
    }

    /// <summary>
    /// Places the world's props (the server, after loading what was stored): every loot spot and recycler point the
    /// surface gives, as stored (gone until a time, or as players left it), else fresh with new loot. Once.
    /// </summary>
    public void StartWorld(IReadOnlyDictionary<ulong, PropOverride>? stored = null)
    {
        if (LootSpots != null)
        {
            return;
        }
        LootSpots = Hollow.Simulation.Nature.LootSpots.Generate(Surface, Frame, Nature!, Content, Hash64.Of(Spec.SurfaceSeed, 0x1007));
        foreach (LootSpot spot in LootSpots.Spots)
        {
            _spots[spot.Id] = spot;
            PropOverride? o = stored?.GetValueOrDefault(spot.Id);
            if (o is { RespawnAtUnixMs: > 0 })
            {
                _depleted[spot.Id] = o.RespawnAtUnixMs;
            }
            else
            {
                Spawn(spot, o);
            }
        }
    }

    // A prop on its spot: as stored, or with fresh loot from its table (seeded by the spot and the time).
    internal WorldProp Spawn(in LootSpot spot, PropOverride? stored)
    {
        var prop = new WorldProp(new EntityId(spot.Id), spot.Prop, spot.Position, spot.Up, spot.Yaw);
        if (stored != null)
        {
            prop.Health = Math.Clamp(float.IsFinite(stored.Health) ? stored.Health : prop.Def.Health, 1, prop.Def.Health);
            if (prop.Storage != null && stored.Contents != null)
            {
                for (int i = 0; i < stored.Contents.Length && i < prop.Storage.Size; i++)
                {
                    prop.Storage.Set(i, stored.Contents[i]);
                }
            }
            prop.Lit = stored.Lit && prop.Def.Recycler != null;
            prop.BurnTicksLeft = Math.Max(0, stored.BurnTicksLeft);
        }
        else if (spot.Prop.Loot is { } table && prop.Storage != null)
        {
            var rng = new Rng(Hash64.Of(spot.Id, (ulong)UnixMilliseconds, 0x1007));
            List<ItemStack> loot = table.RollStacks(ref rng, Content);
            for (int i = 0; i < loot.Count && i < prop.Storage.Size; i++)
            {
                prop.Storage.Set(i, loot[i]);
            }
        }
        prop.Storage?.ClearDirty();
        Things.AddStatic(prop);
        _props[spot.Id] = prop;
        // Untouched (and so not stored) until players change something; a stored one stays stored.
        _spawnRevisions[prop] = stored != null ? uint.MaxValue : prop.Storage?.Revision ?? 0;
        return prop;
    }

    /// <summary>Whether players changed a standing prop since it spawned (then it is stored).</summary>
    public bool IsTouched(WorldProp prop) =>
        prop.Health < prop.Def.Health || prop.Lit || (_spawnRevisions.TryGetValue(prop, out uint r) && (prop.Storage?.Revision ?? 0) != r);

    /// <summary>What to store about the props now: every one gone (until when), and every one players changed.</summary>
    public IEnumerable<(ulong Spot, PropOverride Override)> PropOverrides()
    {
        foreach ((ulong spot, long at) in _depleted)
        {
            yield return (spot, new PropOverride { RespawnAtUnixMs = at });
        }
        foreach ((ulong spot, WorldProp prop) in _props)
        {
            if (IsTouched(prop))
            {
                yield return (spot, new PropOverride
                {
                    Health = prop.Health,
                    Contents = prop.Storage?.Slots.ToArray(),
                    Lit = prop.Lit,
                    BurnTicksLeft = prop.BurnTicksLeft,
                });
            }
        }
    }

    // A barrel broken or a crate emptied: gone until its respawn time (the real clock, as nature's).
    internal void PropGone(WorldProp prop)
    {
        if (!_props.Remove(prop.Id.Value))
        {
            return;
        }
        _spawnRevisions.Remove(prop);
        if (Players._oneOffProps.Remove(prop.Id.Value))
        {
            // An admin's (GROUND.md §8.2): gone for good, its spot with it.
            _spots.Remove(prop.Id.Value);
            return;
        }
        _depleted[prop.Id.Value] = UnixMilliseconds + (long)(prop.Prop.RespawnSeconds * 1000);
        if (prop.Prop.Kind == WorldPropKind.Barrel)
        {
            WorldStats.BarrelsBroken++;
        }
        else
        {
            WorldStats.CratesEmptied++;
        }
    }

    // Once a second: emptied crates go; spots whose time came get their prop back (unless someone or something is in
    // the way: a player, a deployable, a building piece); invites run out.
    private void StepWorld()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        _gone.Clear();
        foreach (WorldProp prop in _props.Values)
        {
            if (prop.Prop.Kind == WorldPropKind.Crate && prop.Storage is { IsEmpty: true })
            {
                _gone.Add(prop);
            }
        }
        foreach (WorldProp prop in _gone)
        {
            Things.RemoveStatic(prop);
            PropGone(prop);
        }
        _due.Clear();
        foreach ((ulong spot, long at) in _depleted)
        {
            if (UnixMilliseconds >= at)
            {
                _due.Add(spot);
            }
        }
        foreach (ulong id in _due)
        {
            if (_spots.TryGetValue(id, out LootSpot spot) && SpotFree(spot))
            {
                _depleted.Remove(id);
                Spawn(spot, null);
                WorldStats.Respawned++;
            }
        }
        Teams.Expire(UnixMilliseconds);
        WorldStats.Time += System.Diagnostics.Stopwatch.GetElapsedTime(started);
    }

    private bool SpotFree(in LootSpot spot)
    {
        DeployableDef def = spot.Prop.Deployable;
        Vector3 centre = spot.Position + spot.Up * def.HalfExtents.Y;
        Things._near.Clear();
        Things.DeployablesNear(spot.Position, 4f, Things._near);
        foreach (Deployable other in Things._near)
        {
            if (Combat.Geometry.BoxesOverlap(centre, def.HalfExtents, spot.Yaw, other.Centre, other.Def.HalfExtents, other.Yaw, spot.Up))
            {
                return false;
            }
        }
        ComponentPool<Character> characters = Entities.Pool<Character>();
        for (int i = 0; i < characters.Count; i++)
        {
            ref Character c = ref characters.Values[i];
            if (!c.State.IsDead && Combat.Geometry.BoxCylinderOverlap(centre, def.HalfExtents, spot.Yaw, c.State.Position, Tuning.Radius, Tuning.StandingHeight, spot.Up))
            {
                return false;
            }
        }
        if (Building.World != null)
        {
            _piecesNear.Clear();
            Building.World.PiecesNear(spot.Position, spot.Position, 3f, _piecesNear);
            if (_piecesNear.Count > 0)
            {
                return false;
            }
        }
        return true;
    }

    private readonly List<Piece> _piecesNear = new();

    // ---------------------------------------------------------------- machines: the recycler and the research table

    // A lit recycler or research table, each tick (Zone.Things' devices pass).
    internal void StepMachine(Deployable d)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        if (d.Storage is { } storage)
        {
            if (d.Def.Recycler is { } recycler)
            {
                StepRecycler(d, recycler, storage);
            }
            else if (d.Def.Research is { } research)
            {
                StepResearch(d, research, storage);
            }
        }
        WorldStats.Time += System.Diagnostics.Stopwatch.GetElapsedTime(started);
    }

    // One batch every few seconds: the first input stack's batch comes apart into the outputs; with nothing to take
    // apart, or no room for what comes out, it stops (Rust's).
    private void StepRecycler(Deployable d, RecyclerStats stats, Container storage)
    {
        if (--d.BurnTicksLeft > 0)
        {
            return;
        }
        d.BurnTicksLeft = stats.Ticks;
        GameContent content = Content;
        int from = -1;
        foreach (int s in stats.InputSlots)
        {
            if (!storage[s].IsEmpty && content.Item(storage[s].Item) is { } item && Recycling.CanRecycle(content, item))
            {
                from = s;
                break;
            }
        }
        if (from < 0)
        {
            d.Lit = false;
            d.Revision++;
            return;
        }
        ItemStack input = storage[from];
        ItemDef def = content.Item(input.Item)!;
        int units = Recycling.BatchSize(stats, def, input.Count);
        _yields.Clear();
        var rng = new Rng(Hash64.Of(d.Id.Value, (ulong)Tick, 0x2EC1));
        Recycling.Yield(content, stats, input, units, ref rng, _yields);
        if (!PutAll(storage, stats.OutputSlots, _yields, dryRun: true))
        {
            d.Lit = false;
            d.Revision++;
            return;
        }
        PutAll(storage, stats.OutputSlots, _yields, dryRun: false);
        input.Count -= units;
        storage.Set(from, input);
        WorldStats.Batches++;
        WorldStats.UnitsRecycled += units;
        // Nothing left to take apart: it stops at once (Rust's).
        bool more = false;
        foreach (int s in stats.InputSlots)
        {
            more |= !storage[s].IsEmpty && content.Item(storage[s].Item) is { } next && Recycling.CanRecycle(content, next);
        }
        if (!more)
        {
            d.Lit = false;
            d.Revision++;
        }
    }

    // Items into a container's slots: onto stacks of the same item, then empty slots; all of them or (dry run) whether they fit.
    private bool PutAll(Container storage, IReadOnlyList<int> slots, List<ItemAmount> items, bool dryRun)
    {
        Span<ItemStack> copy = stackalloc ItemStack[slots.Count];
        for (int i = 0; i < slots.Count; i++)
        {
            copy[i] = storage[slots[i]];
        }
        foreach (ItemAmount a in items)
        {
            int left = a.Count;
            for (int i = 0; i < copy.Length && left > 0; i++)
            {
                if (copy[i].Item == a.Item.Id && copy[i].Condition == (ushort)Math.Max(0, a.Item.MaxCondition) && copy[i].Count < a.Item.StackSize)
                {
                    int n = Math.Min(left, a.Item.StackSize - copy[i].Count);
                    copy[i].Count += n;
                    left -= n;
                }
            }
            for (int i = 0; i < copy.Length && left > 0; i++)
            {
                if (copy[i].IsEmpty)
                {
                    int n = Math.Min(left, a.Item.StackSize);
                    copy[i] = ItemStack.Of(a.Item, n);
                    left -= n;
                }
            }
            if (left > 0)
            {
                return false;
            }
        }
        if (!dryRun)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                storage.Set(slots[i], copy[i]);
            }
        }
        return true;
    }

    // A research under way: when its time is up the blueprint comes out (the output slot was empty when it started).
    private void StepResearch(Deployable d, ResearchStats stats, Container storage)
    {
        if (--d.BurnTicksLeft > 0)
        {
            return;
        }
        if (Content.Recipe(d.ResearchItem) is { } recipe && storage[stats.OutputSlot].IsEmpty)
        {
            storage.Set(stats.OutputSlot, TechTrees.BlueprintOf(Content, recipe));
            WorldStats.Researched++;
        }
        d.ResearchItem = 0;
        d.ResearchSalvage = 0;
        d.Lit = false;
        d.Revision++;
    }

    /// <summary>
    /// The switch of a recycler (on or off: it needs no fuel) or research table (on starts a research: the item in its
    /// item slot and the salvage its tier costs are taken, and a blueprint of it comes out after Rust's 10 s), the machine
    /// capability's verbs (GROUND_SYSTEMS.md §5).
    /// </summary>
    internal Outcome SwitchMachine(Deployable d, bool on)
    {
        if (d.Storage is not { } storage)
        {
            return OutcomeCode.NotFound;
        }
        if (d.Lit == on)
        {
            return Outcome.Done;
        }
        GameContent content = Content;
        if (d.Def.Recycler is { } recycler)
        {
            if (d.Lit)
            {
                d.Lit = false;
            }
            else
            {
                bool any = false;
                foreach (int s in recycler.InputSlots)
                {
                    any |= !storage[s].IsEmpty;
                }
                if (!any)
                {
                    return OutcomeCode.CantStart;
                }
                d.Lit = true;
                d.BurnTicksLeft = recycler.Ticks;
            }
            d.Revision++;
            return OutcomeCode.Done;
        }
        if (d.Def.Research is not { } research)
        {
            return OutcomeCode.NotFound;
        }
        if (d.Lit)
        {
            return OutcomeCode.CantStart; // a research can't be stopped (Rust's)
        }
        ItemStack item = storage[research.ItemSlot];
        if (item.IsEmpty || content.Item(item.Item) is not { } def || !Research.CanResearch(content, def) || !storage[research.OutputSlot].IsEmpty)
        {
            return OutcomeCode.CantStart;
        }
        Recipe recipe = content.Recipe(def.Id)!;
        int cost = Research.Cost(content, recipe);
        ItemStack salvage = storage[research.SalvageSlot];
        if (salvage.Item != content.World.Currency?.Id || salvage.Count < cost)
        {
            return OutcomeCode.NotEnough;
        }
        item.Count--;
        storage.Set(research.ItemSlot, item);
        salvage.Count -= cost;
        storage.Set(research.SalvageSlot, salvage);
        d.ResearchItem = def.Id;
        d.ResearchSalvage = cost;
        d.BurnTicksLeft = research.Ticks;
        d.Lit = true;
        d.Revision++;
        return OutcomeCode.Done;
    }

    // A research table broken mid-research gives back what it was working on, with its contents (in their bag).
    internal void SpillResearch(Deployable d, Vector3 at)
    {
        if (d.Def.Research == null || !d.Lit)
        {
            return;
        }
        if (Content.Item(d.ResearchItem) is { } item)
        {
            Spill.Spill(at, ItemStack.Of(item, 1), d.Yaw);
        }
        if (d.ResearchSalvage > 0 && Content.World.Currency is { } salvage)
        {
            Spill.Spill(at, ItemStack.Of(salvage, Math.Min(d.ResearchSalvage, salvage.StackSize)), d.Yaw);
        }
        d.ResearchItem = 0;
        d.ResearchSalvage = 0;
    }

    // ---------------------------------------------------------------- blueprints and the tech trees

    /// <summary>
    /// Unlocks a tech tree node (Rust's; the station capability's verb, its recipe the verb's argument): at a workbench of
    /// its level within reach, once one of the nodes before it is known, for its salvage from the player's main and belt
    /// slots. The blueprint is learnt at once.
    /// </summary>
    internal Outcome UnlockNode(Entity entity, Player p, int recipeId)
    {
        GameContent content = Content;
        if (content.World.NodeOf(recipeId) is not { } node)
        {
            return OutcomeCode.Invalid;
        }
        Outcome can = TechTrees.CanUnlock(node, p.Blueprints);
        if (can != OutcomeCode.Done)
        {
            return can;
        }
        if (Things.WorkbenchLevel(EyeOf(Entities.Get<Character>(entity).State)) < node.Tier)
        {
            return OutcomeCode.NeedsWorkbench.With(node.Tier);
        }
        if (content.World.Currency is not { } salvage || !InventoryRules.Take(p.Inventory.Carried, salvage.Id, node.Cost))
        {
            return OutcomeCode.NotEnough.With(content.World.Currency?.Id ?? 0);
        }
        p.Blueprints.Learn(node.Recipe);
        WorldStats.Unlocked++;
        return OutcomeCode.Done;
    }

    // Using a blueprint item learns its recipe (it is used up), unless that is known already.
    // ---------------------------------------------------------------- teams

    /// <summary>
    /// Team actions (Rust's): create, accept or decline an invite, leave, and the leader's kick and promote. An invite is
    /// a verb on the player invited (the character capability's, GROUND_SYSTEMS.md §5): <see cref="Invite"/>.
    /// </summary>
    public Outcome TeamAct(Entity entity, TeamAction action, EntityId target, ulong team)
    {
        if (Players.PlayerOf(entity) is not { } p || p.Sleeping)
        {
            return OutcomeCode.Invalid;
        }
        switch (action)
        {
            case TeamAction.Create:
                return Teams.Create(p.Id, out _);
            case TeamAction.Accept:
                return Teams.Accept(p.Id, team, UnixMilliseconds);
            case TeamAction.Decline:
                return Teams.Decline(p.Id, team);
            case TeamAction.Leave:
                return Teams.Leave(p.Id);
            case TeamAction.Kick:
                return Teams.Kick(p.Id, target);
            case TeamAction.Promote:
                return Teams.Promote(p.Id, target);
            default:
                return OutcomeCode.Invalid;
        }
    }

    /// <summary>A team's leader invites a player close by into it (Rust's invite by looking at them, in reach and sight).</summary>
    internal Outcome Invite(Player leader, Player invited) =>
        invited.IsAwakeAndAlive ? Teams.Invite(leader.Id, invited.Id, UnixMilliseconds) : OutcomeCode.NotFound;

    // The player and their teammates (the player alone when in no team), for "authorise team".
    internal List<ulong> TeamOf(Player p)
    {
        _teamIds.Clear();
        _teamIds.Add(p.Id.Value);
        if (Teams.TeamOf(p.Id) is { } team)
        {
            foreach (EntityId m in team.Members)
            {
                if (m != p.Id)
                {
                    _teamIds.Add(m.Value);
                }
            }
        }
        return _teamIds;
    }

    // "Authorise team" on a claim device: the player and every teammate onto its list (anyone may authorise there, as in Rust).
    internal Outcome AuthoriseTeam(ulong player, ClaimDevice device, List<ulong> members)
    {
        BuildingWorld building = Building.World!;
        Outcome first = building.Authorise(player, device);
        if (!first.Ok)
        {
            return first;
        }
        foreach (ulong m in members)
        {
            if (m != player && !building.Authorise(m, device).Ok)
            {
                break; // the list is full
            }
        }
        return Outcome.Done;
    }

    // ---------------------------------------------------------------- the map's markers

    /// <summary>Puts a marker on the map (at most world.json's <c>map.markers</c>, within the map), with a colour and a short label.</summary>
    public Outcome PlaceMarker(Entity entity, Vector3 at, byte colour, string? label)
    {
        if (Players.PlayerOf(entity) is not { } p || p.Sleeping)
        {
            return OutcomeCode.Invalid;
        }
        WorldData world = Content.World;
        float half = (float)(world.MapMetres / 2);
        if (!float.IsFinite(at.X) || !float.IsFinite(at.Z) || MathF.Abs(at.X) > half || MathF.Abs(at.Z) > half || colour >= MapMarker.Colours)
        {
            return OutcomeCode.Invalid;
        }
        if (p.Markers.Count >= world.MaxMarkers)
        {
            return OutcomeCode.NoSpace;
        }
        string text = (label ?? "").Trim();
        if (text.Length > MapMarker.MaxLabel)
        {
            text = text[..MapMarker.MaxLabel];
        }
        p.Markers.Add(new MapMarker(Settle(at), colour, text));
        p.MapRevision++;
        return OutcomeCode.Done;
    }

    /// <summary>Takes a marker off the map (−1: all of them).</summary>
    public Outcome RemoveMarker(Entity entity, int index)
    {
        if (Players.PlayerOf(entity) is not { } p || p.Sleeping)
        {
            return OutcomeCode.Invalid;
        }
        if (index == -1)
        {
            p.Markers.Clear();
        }
        else if (index >= 0 && index < p.Markers.Count)
        {
            p.Markers.RemoveAt(index);
        }
        else
        {
            return OutcomeCode.Invalid;
        }
        p.MapRevision++;
        return OutcomeCode.Done;
    }

    // A respawn point placed or gone: its owner's map changes.
    internal void RespawnPointChanged(Deployable d)
    {
        if (d.Def.IsRespawnPoint && Players.TryFindPlayer(d.Owner, out _, out Player owner))
        {
            owner.MapRevision++;
        }
    }

    public override int InitOrder => 40;

    protected internal override void Init() => InitWorld(Content);

    public override int Every => CharacterMotor.TickRate;

    protected internal override void Step()
    {
        StepWorld();
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class WorldSystemAccess
{
    public static WorldSystem World(this Zone zone) => zone.Require<WorldSystem>();
}
