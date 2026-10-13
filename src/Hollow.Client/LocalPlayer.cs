using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Targets;

namespace Hollow.Client;

/// <summary>The player's vitals as the UI shows them.</summary>
public readonly record struct VitalsView
{
    public float Health { get; init; }
    public float MaxHealth { get; init; }
    public float Calories { get; init; }
    public float MaxCalories { get; init; }
    public float Hydration { get; init; }
    public float MaxHydration { get; init; }

    /// <summary>Felt temperature in °C, and how cold that is (0 … 1).</summary>
    public float Temperature { get; init; }
    public float Cold { get; init; }

    /// <summary>Health still to bleed away, and to heal over time, and (G4) to burn.</summary>
    public float Bleeding { get; init; }
    public float Healing { get; init; }
    public float Burning { get; init; }

    /// <summary>Near a lit fire; wet from swimming.</summary>
    public bool Comfort { get; init; }
    public bool Wet { get; init; }

    public bool IsBleeding => Bleeding > 0;
    public bool IsBurning => Burning > 0;
    public bool IsStarving => Calories <= 0;
    public bool IsDehydrated => Hydration <= 0;
}

/// <summary>A crafting job as the UI shows it: the recipe, units left, and the unit in progress.</summary>
public readonly record struct CraftJobView(uint Ticket, Recipe Recipe, int Count, float UnitProgress, float SecondsLeft, bool Pending);

/// <summary>A place to respawn: a sleeping bag (or the beach, with no bag), ready in some seconds.</summary>
public readonly record struct RespawnOption(EntityId Bag, Vector2 Position, double ReadyAtSeconds)
{
    public bool IsBeach => Bag.IsNone;
}

/// <summary>Alive or dead (or down), and when dead: how, by whom, where the player can come back.</summary>
public sealed record LifeView
{
    public bool Dead { get; init; }

    /// <summary>Down (wounded, G4): crawling until helped up, up by themselves or dead at <see cref="WoundedUntilSeconds"/> (the client's clock).</summary>
    public bool Wounded { get; init; }
    public double WoundedUntilSeconds { get; init; }
    public string Cause { get; init; } = "";
    public string KilledBy { get; init; } = "";
    public IReadOnlyList<RespawnOption> Options { get; init; } = Array.Empty<RespawnOption>();
}

/// <summary>A hit landed (the UI's hit marker): <paramref name="Downed"/> when it knocked the player down (G4).</summary>
public readonly record struct HitMarkerEvent(TargetFamily Target, HitRegion Region, float Damage, bool Killed, bool WeakSpot, bool Downed = false);

/// <summary>Items gained (gathered, picked up, crafted, refunded) or lost (a tool broke), for the notifications.</summary>
public readonly record struct ItemsChangedEvent(ItemDef Item, int Count, GainReason Reason, bool WeakSpot);

/// <summary>Damage taken from an attack: how much, where, from which point (zone frame), with what (an item, or none).</summary>
public readonly record struct DamageEvent(float Damage, HitRegion Region, Vector3 From, ItemDef? Weapon);

/// <summary>
/// An intent's answer (GROUND_SYSTEMS.md §3.2): done, or the code of why not, with the number its line names
/// (<see cref="Simulation.Content.Texts"/> says it).
/// </summary>
public readonly record struct IntentAnswer(uint Seq, OutcomeCode Code, int Detail = 0)
{
    public bool Done => Code == OutcomeCode.Done;

    public Outcome Outcome => new(Code, Detail);
}

/// <summary>
/// The local player's own survival state and actions, for the UI (GROUND.md §4.3, §6). It mirrors what the server
/// sends about this player only (their containers, what they loot, vitals, crafting queue, life) and offers every
/// action as an intent.
/// <para>
/// <b>Optimistic.</b> An intent shows at once: the client applies the same rules (Hollow.Simulation) to its copy. The
/// UI always reads the server's copy with the intents not yet answered applied on top; when the server's state comes
/// in, or an intent is refused, that is rebuilt, so a refused intent rolls back by itself (and
/// <see cref="IntentRejected"/> says why). Containers are the same objects for the life of the session (except the
/// loot ones, new for each container opened), so the UI can keep references and refresh on <see cref="Changed"/>.
/// </para>
/// Call from the thread that calls <see cref="GameClient.Update"/>.
/// </summary>
public sealed partial class LocalPlayer
{
    private readonly PlayerInventory _server = new();
    private readonly CraftQueue _serverCraft = new();
    private readonly CraftQueue _craft = new();
    private readonly List<Pending> _pending = new();
    private readonly List<ItemStack> _scratch = new();
    private readonly List<Container> _targets = new();
    private Container? _serverLoot, _serverLootBelt, _serverLootWear;
    private uint _craftTick;
    private uint _nextSeq = 1;
    private bool _dirty;

    internal LocalPlayer(GameContent content)
    {
        Content = content;
        Gun = new LocalGun(this);
        _actor = new Prediction(this);
    }

    private readonly Prediction _actor;

    /// <summary>
    /// The player as the prediction acts for them (GROUND_SYSTEMS.md §7): the inventory and blueprints shown; the gun in
    /// hand's reload; nothing more (eating's effects and the like are the server's, and come back with its state).
    /// </summary>
    private sealed class Prediction(LocalPlayer me) : IItemActor
    {
        public GameContent Content => me.Content;

        public PlayerInventory Inventory => me.Inventory;

        public BlueprintBook Blueprints => me.Blueprints;

        public bool Reloading(ContainerRef from, int slot) => from == ContainerRef.Belt && slot == me.Gun.State.Slot && me.Gun.Reloading;

        public void Consumed(ItemDef item)
        {
        }

        public void Learned(Recipe recipe)
        {
        }

        public void MagazineChanged(ContainerRef from, int slot)
        {
        }
    }

    /// <summary>Sends a message on the reliable channel (set by the client); false when not connected and playing.</summary>
    internal Func<Action<BitWriter>, bool>? Send { get; set; }

    /// <summary>Sends a gun intent (reliable, unordered: a lost shot holds nothing else up); falls back to <see cref="Send"/>.</summary>
    internal Func<Action<BitWriter>, bool>? SendGun { get; set; }

    public GameContent Content { get; }

    // ---------------------------------------------------------------- what the UI reads

    /// <summary>Main (24), belt (6) and clothing (7) slots, as the server has them plus pending intents.</summary>
    public PlayerInventory Inventory { get; } = new();

    /// <summary>What is being looted: a box, device or corpse in <see cref="Loot"/>; a sleeper in all three; or nothing.</summary>
    public LootKind LootKind { get; private set; }

    public EntityId LootTarget { get; private set; }

    public Container? Loot { get; private set; }

    public Container? LootBelt { get; private set; }

    public Container? LootWear { get; private set; }

    /// <summary>Whether the loot takes items (boxes and devices do; corpses and sleepers don't).</summary>
    public bool LootTakesDeposits => LootRules.CanDeposit(LootKind);

    /// <summary>The crafting queue, front first, with progress (pending jobs too).</summary>
    public IReadOnlyList<CraftJobView> Crafting => _craftViews;

    private readonly List<CraftJobView> _craftViews = new();

    public VitalsView Vitals { get; private set; }

    public LifeView Life { get; private set; } = new();

    public bool IsDead => Life.Dead;

    /// <summary>Down (wounded): crawling, can't act; another player can help them up (G4).</summary>
    public bool IsWounded => Life.Wounded;

    /// <summary>The gun in hand, predicted: magazine, reload, recoil on the camera, the shot and reload events (G4).</summary>
    public LocalGun Gun { get; }

    /// <summary>The belt slot in hand (0 … 5), or −1 for none: set by the UI (keys 1–6); it goes out with every input.</summary>
    public int HeldSlot { get; set; } = -1;

    /// <summary>The item in hand, if any.</summary>
    public ItemDef? HeldItem => HeldSlot is >= 0 and < PlayerInventory.BeltSlots ? Content.Item(Inventory.Belt[HeldSlot].Item) : null;

    /// <summary>The best workbench level within reach (from the deployables around), for the crafting menu.</summary>
    public int WorkbenchLevel { get; internal set; }

    /// <summary>
    /// The station of a kind within reach (its entity ID, 0 for none): a recipe made at one (a ship at the vehicle
    /// workbench, G6) needs it, as the server's <c>Zone.StationNear</c> does.
    /// </summary>
    public Func<Simulation.Content.DeployableDef, ulong>? StationNear { get; internal set; }

    private ulong StationFor(int recipeId) =>
        Content.Recipe(recipeId)?.Station is { } needs ? StationNear?.Invoke(needs) ?? 0 : 0;

    /// <summary>Intents sent and not yet answered.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>The server's tick as the client estimates it now (crafting progress counts from it).</summary>
    internal double ServerTick { get; set; }

    /// <summary>Seconds on the client's clock (respawn countdowns count to it).</summary>
    internal double ClockSeconds { get; set; }

    // ---------------------------------------------------------------- events for the UI

    /// <summary>The inventory, loot, vitals, crafting queue or life changed: refresh what shows them.</summary>
    public event Action? Changed;

    public event Action<HitMarkerEvent>? HitMarker;

    public event Action<ItemsChangedEvent>? ItemsChanged;

    public event Action<DamageEvent>? DamageTaken;

    /// <summary>A unit finished crafting (its item and count).</summary>
    public event Action<ItemDef, int>? CraftFinished;

    /// <summary>A container opened (what kind, whose) or closed.</summary>
    public event Action<LootKind, EntityId>? ContainerOpened;

    public event Action? ContainerClosed;

    /// <summary>The server refused an intent: its effect has been rolled back.</summary>
    public event Action<IntentAnswer>? IntentRejected;

    /// <summary>Every answer, accepted or not.</summary>
    public event Action<IntentAnswer>? IntentAnswered;

    public event Action<LifeView>? Died;

    public event Action? Respawned;

    /// <summary>Knocked down (wounded), and up again (helped or by themselves), G4.</summary>
    public event Action<LifeView>? Downed;

    public event Action? GotUp;

    // ---------------------------------------------------------------- intents

    // Every intent returns its sequence number (0 if it couldn't be sent): IntentAnswered and IntentRejected name it.

    /// <summary>Drags <paramref name="amount"/> (0: all) from one slot to another: move, split, top up or swap.</summary>
    public uint MoveItem(ContainerRef from, int fromSlot, ContainerRef to, int toSlot, int amount = 0)
    {
        ushort expected = ItemAt(from, fromSlot);
        return Intend(new Pending(p => p.ApplyMove(from, fromSlot, to, toSlot, amount, expected)), w => new MoveItemIntent
        {
            Seq = _nextSeq, From = from, FromSlot = (byte)fromSlot, To = to, ToSlot = (byte)toSlot, Amount = (uint)Math.Max(0, amount), Expected = expected,
        }.WriteMessage(w));
    }

    /// <summary>Right-click: sends a stack where it obviously goes (into the open box, to clothing, between main and belt).</summary>
    public uint QuickMove(ContainerRef from, int slot)
    {
        ushort expected = ItemAt(from, slot);
        return Intend(new Pending(p => p.ApplyQuickMove(from, slot, expected)), w => new QuickMoveIntent
        {
            Seq = _nextSeq, From = from, Slot = (byte)slot, Expected = expected,
        }.WriteMessage(w));
    }

    /// <summary>Drops <paramref name="amount"/> (0: all) in front of the player, into the bag lying there or a new one.</summary>
    public uint Drop(ContainerRef from, int slot, int amount = 0)
    {
        ushort expected = ItemAt(from, slot);
        return Intend(new Pending(p => p.Resolve(from) is { } c && InventoryRules.Remove(c, slot, amount, expected, out _) == OutcomeCode.Done),
            w => new DropIntent { Seq = _nextSeq, From = from, Slot = (byte)slot, Amount = (uint)Math.Max(0, amount), Expected = expected }.WriteMessage(w));
    }

    /// <summary>
    /// An inventory action (GROUND_SYSTEMS.md §7) on a stack in the player's own slots: eat, use, wear, take off, learn,
    /// unload, take an attachment off (<paramref name="arg"/>: which), by its number in items.json's list. Predicted with
    /// the server's own rules (<see cref="ItemActions"/>).
    /// </summary>
    public uint ItemAction(ContainerRef from, int slot, int action, int arg = 0)
    {
        // The gun in hand's magazine changes on the gun's own timeline, in order with its shots and reloads (GROUND_SYSTEMS.md
        // §7): its UNLOAD goes there (no intent number: the gun's update answers it).
        if (Content.ItemActions[action]?.Key == Simulation.Items.ItemActions.UnloadKey && Gun.InHand(from, slot))
        {
            Gun.Unload();
            return 0;
        }
        ushort expected = ItemAt(from, slot);
        return Intend(new Pending(p => p.Content.ItemActions.Do(p._actor, from, slot, expected, action, arg).Ok),
            w => new ItemActionIntent
            {
                Seq = _nextSeq, From = from, Slot = (byte)slot, Expected = expected, Action = (uint)Math.Max(0, action), Arg = (uint)Math.Max(0, arg),
            }.WriteMessage(w));
    }

    /// <summary>The buttons a stack in the player's own slots offers (GROUND_SYSTEMS.md §7), as the server would judge them.</summary>
    public void ItemActions(ContainerRef from, int slot, List<ItemActionOption> into) => Content.ItemActions.Offer(_actor, from, slot, into);

    /// <summary>
    /// What a click with an item in hand does when its held behaviour acts through the inventory (eating, learning): its
    /// part's first button, done; refused with its reason when the button is (0 when nothing was sent).
    /// </summary>
    public uint UsePart(ContainerRef from, int slot, ItemPart part)
    {
        if (Content.ItemActions.First(_actor, from, slot, part) is not { } option)
        {
            return 0;
        }
        if (option.Refusal is { } refused)
        {
            IntentRejected?.Invoke(new IntentAnswer(0, refused.Code, refused.Detail));
            return 0;
        }
        return ItemAction(from, slot, option.Action.Id, option.Arg);
    }

    /// <summary>Starts crafting <paramref name="count"/> of a recipe (by its output item's ID).</summary>
    public uint Craft(int recipeId, int count = 1) =>
        Intend(new Pending(p => p._craft.Start(p.Content, p.Inventory, recipeId, count, p.WorkbenchLevel, out _, p.Blueprints, p.StationFor(recipeId)) == OutcomeCode.Done),
            w => new CraftIntent { Seq = _nextSeq, Recipe = (uint)Math.Max(0, recipeId), Count = (uint)Math.Max(0, count) }.WriteMessage(w));

    /// <summary>Whether a recipe could start now (known, bench in reach, materials for <paramref name="count"/>), as the UI shows it.</summary>
    public Outcome CanCraft(Recipe recipe, int count = 1)
    {
        if (!Blueprints.Knows(recipe))
        {
            return OutcomeCode.UnknownRecipe;
        }
        if (WorkbenchLevel < recipe.Workbench)
        {
            return OutcomeCode.NeedsWorkbench.With(recipe.Workbench);
        }
        if (recipe.Station is { } needs && (StationNear?.Invoke(needs) ?? 0) == 0)
        {
            return OutcomeCode.NeedsStation.With(needs.Item.Id);
        }
        if (_craft.Jobs.Count >= CraftQueue.MaxJobs)
        {
            return OutcomeCode.QueueFull;
        }
        return Inventory.Has(recipe.Ingredients, count) ? OutcomeCode.Done : OutcomeCode.NotEnough;
    }

    /// <summary>Cancels a job (its ingredients come back).</summary>
    public uint CancelCraft(uint ticket) =>
        Intend(new Pending(p => p._craft.Cancel(p.Content, p.Inventory, ticket, p._scratch) == OutcomeCode.Done),
            w => new CancelCraftIntent { Seq = _nextSeq, Ticket = ticket }.WriteMessage(w));

    /// <summary>Opens a box, device, corpse or sleeper (its entity ID); the contents arrive with <see cref="ContainerOpened"/>.</summary>
    public uint Open(EntityId target) => Intend(null, w => new OpenIntent { Seq = _nextSeq, Target = target.Value }.WriteMessage(w));

    /// <summary>Closes the loot at once (the server agrees).</summary>
    public uint Close() => Intend(new Pending(p => p.ApplyClose()), w => new CloseIntent { Seq = _nextSeq }.WriteMessage(w));

    /// <summary>Picks a pickup or berry bush (a natural object's ID).</summary>
    public uint PickUpNatural(ulong naturalId) => Intend(null, w => new PickUpIntent { Seq = _nextSeq, Natural = true, Target = naturalId }.WriteMessage(w));

    /// <summary>Picks up a bag of dropped items (its entity ID): everything in it that fits.</summary>
    public uint PickUpItem(EntityId item) => Intend(null, w => new PickUpIntent { Seq = _nextSeq, Natural = false, Target = item.Value }.WriteMessage(w));

    /// <summary>Places the deployable in a slot at a point on the ground (zone frame), turned by a yaw.</summary>
    public uint Place(ContainerRef from, int slot, Vector3 at, float yaw)
    {
        ushort expected = ItemAt(from, slot);
        return Intend(new Pending(p => p.Resolve(from) is { } c && from is ContainerRef.Main or ContainerRef.Belt &&
                p.Content.Item(expected)?.Deploys != null && InventoryRules.Remove(c, slot, 1, expected, out _) == OutcomeCode.Done),
            w => new PlaceIntent
            {
                Seq = _nextSeq, From = from, Slot = (byte)slot, Expected = expected, X = at.X, Y = at.Y, Z = at.Z, Yaw = Quantize.Angle16(yaw),
            }.WriteMessage(w));
    }

    /// <summary>Lights or puts out a campfire or furnace.</summary>
    public uint Toggle(EntityId device) => Intend(null, w => new ToggleIntent { Seq = _nextSeq, Target = device.Value }.WriteMessage(w));

    /// <summary>Respawns at one of the player's sleeping bags, or on the beach (no bag).</summary>
    public uint Respawn(EntityId bag = default) => Intend(null, w => new RespawnIntent { Seq = _nextSeq, Bag = bag.Value }.WriteMessage(w));

    /// <summary>Reloads the gun in hand (R): with these rounds, or (null) the kind loaded, else any it takes. See <see cref="LocalGun"/>.</summary>
    public void Reload(ItemDef? ammo = null) => Gun.Reload(ammo);

    /// <summary>Puts the attachment in a slot on the firearm in another (main slots or belt); what was on comes back.</summary>
    public uint Attach(ContainerRef from, int slot, ContainerRef weaponIn, int weaponSlot)
    {
        ushort expected = ItemAt(from, slot);
        return Intend(new Pending(p => from is ContainerRef.Main or ContainerRef.Belt && weaponIn is ContainerRef.Main or ContainerRef.Belt &&
                AttachmentRules.Attach(p.Content, p.Resolve(from)!, slot, p.Resolve(weaponIn)!, weaponSlot, expected) == OutcomeCode.Done),
            w => new AttachIntent { Seq = _nextSeq, From = from, Slot = (byte)slot, WeaponIn = weaponIn, WeaponSlot = (byte)weaponSlot, Expected = expected }.WriteMessage(w));
    }

    /// <summary>Helps a wounded player up (the interact key on them, within 2.5 m): <see cref="GameClient.Aim"/> finds them.</summary>
    public uint HelpUp(EntityId target) => Intend(null, w => new HelpUpIntent { Seq = _nextSeq, Target = target.Value }.WriteMessage(w));

    /// <summary>The container an intent would name (the UI's view of it).</summary>
    public Container? Resolve(ContainerRef which) => which switch
    {
        ContainerRef.Main => Inventory.Main,
        ContainerRef.Belt => Inventory.Belt,
        ContainerRef.Wear => Inventory.Wear,
        ContainerRef.Loot => Loot,
        ContainerRef.LootBelt => LootBelt,
        ContainerRef.LootWear => LootWear,
        _ => null,
    };

    private ushort ItemAt(ContainerRef which, int slot) => Resolve(which) is { } c && c.InRange(slot) ? c[slot].Item : (ushort)0;

    // Sends an intent and shows its effect at once; returns its sequence number, or 0 when it couldn't be sent (not
    // playing yet, or disconnected).
    private uint Intend(Pending? pending, Action<BitWriter> write)
    {
        uint seq = _nextSeq;
        if (Send?.Invoke(write) != true)
        {
            return 0;
        }
        if (pending != null)
        {
            pending.Seq = seq;
            _pending.Add(pending);
            pending.Apply(this);
            Raise();
        }
        _nextSeq++;
        return seq;
    }

    // ---------------------------------------------------------------- applying intents to the client's copy

    private bool ApplyMove(ContainerRef from, int fromSlot, ContainerRef to, int toSlot, int amount, ushort expected)
    {
        Container? a = Resolve(from), b = Resolve(to);
        if (a == null || b == null || (LootRules.IsLoot(to) && !LootRules.IsLoot(from) && !LootRules.CanDeposit(LootKind)))
        {
            return false;
        }
        return InventoryRules.Move(Content, a, fromSlot, b, toSlot, amount, expected) == OutcomeCode.Done;
    }

    private bool ApplyQuickMove(ContainerRef from, int slot, ushort expected)
    {
        Container? source = Resolve(from);
        if (source == null || !source.InRange(slot) || Content.Item(source[slot].Item) is not { } item)
        {
            return false;
        }
        _targets.Clear();
        foreach (ContainerRef r in LootRules.QuickTargets(from, item, LootKind))
        {
            if (Resolve(r) is { } target)
            {
                _targets.Add(target);
            }
        }
        return InventoryRules.QuickMove(Content, source, slot, _targets, expected) == OutcomeCode.Done;
    }

    private bool ApplyClose()
    {
        Loot = LootBelt = LootWear = null;
        LootKind = LootKind.None;
        LootTarget = EntityId.None;
        return true;
    }

    // The server's copy, then every intent not yet answered on top.
    private void Rebuild()
    {
        Inventory.Main.CopyFrom(_server.Main);
        Inventory.Belt.CopyFrom(_server.Belt);
        Inventory.Wear.CopyFrom(_server.Wear);
        CopyLoot(_serverLoot, Loot);
        CopyLoot(_serverLootBelt, LootBelt);
        CopyLoot(_serverLootWear, LootWear);
        _craft.Set(_serverCraft.Jobs, _serverCraft.NextTicket);
        RebuildWorld();
        foreach (Pending pending in _pending)
        {
            pending.Apply(this);
        }
        Gun.ApplyTo(Inventory.Belt);
        RebuildCraftViews();
    }

    /// <summary>Sends a message on the reliable channel (the gun's intents); false when not connected and playing.</summary>
    internal bool SendRaw(Action<BitWriter> write) => (SendGun ?? Send)?.Invoke(write) == true;

    /// <summary>Something the UI shows changed (the gun's prediction): rebuild and announce at the next update.</summary>
    internal void Touch() => _dirty = true;

    private static void CopyLoot(Container? from, Container? to)
    {
        if (from != null && to != null)
        {
            to.CopyFrom(from);
        }
    }

    private void RebuildCraftViews()
    {
        _craftViews.Clear();
        double elapsed = Math.Max(0, ServerTick - _craftTick);
        for (int i = 0; i < _craft.Jobs.Count; i++)
        {
            CraftJob job = _craft.Jobs[i];
            if (Content.Recipe(job.Recipe) is not { } recipe)
            {
                continue;
            }
            bool fromServer = i < _serverCraft.Jobs.Count && _serverCraft.Jobs[i].Ticket == job.Ticket;
            double left = job.TicksLeft - (i == 0 && fromServer ? elapsed : 0);
            left = Math.Clamp(left, 0, job.UnitTicks);
            float progress = (float)(1 - left / Math.Max(1, job.UnitTicks));
            float seconds = (float)((left + (double)(job.Count - 1) * job.UnitTicks) / CharacterTicks.Rate);
            _craftViews.Add(new CraftJobView(job.Ticket, recipe, job.Count, progress, seconds, !fromServer));
        }
    }

    private void Raise()
    {
        _dirty = false;
        Changed?.Invoke();
    }

    /// <summary>Called by the client every frame: crafting progress moves on, and changes are announced once.</summary>
    internal void Update()
    {
        if (_dirty)
        {
            Rebuild();
            Raise();
        }
        else if (_craftViews.Count > 0)
        {
            RebuildCraftViews();
        }
    }

    // ---------------------------------------------------------------- what the server sends

    private void Acknowledge(uint lastSeq)
    {
        _pending.RemoveAll(p => p.Seq <= lastSeq);
        _dirty = true;
    }

    internal void OnContents(in ContainerContents m)
    {
        Container? target = ServerContainer(m.Which, m.Size);
        if (target != null)
        {
            target.Clear();
            foreach (SlotNet s in m.Slots ?? Array.Empty<SlotNet>())
            {
                SetSlot(target, s);
            }
        }
        Acknowledge(m.LastSeq);
    }

    internal void OnSlots(in ContainerSlots m)
    {
        Container? target = m.Which switch
        {
            ContainerRef.Main => _server.Main,
            ContainerRef.Belt => _server.Belt,
            ContainerRef.Wear => _server.Wear,
            ContainerRef.Loot => _serverLoot,
            ContainerRef.LootBelt => _serverLootBelt,
            ContainerRef.LootWear => _serverLootWear,
            _ => null,
        };
        if (target != null)
        {
            foreach (SlotNet s in m.Slots ?? Array.Empty<SlotNet>())
            {
                SetSlot(target, s);
            }
        }
        Acknowledge(m.LastSeq);
    }

    private void SetSlot(Container target, in SlotNet s)
    {
        if (!target.InRange(s.Index))
        {
            return;
        }
        ItemDef? item = Content.Item((int)Math.Min(s.Item, ushort.MaxValue));
        if (item == null || s.Count == 0)
        {
            target.Set(s.Index, default);
            return;
        }
        var stack = new ItemStack(item.Id, (int)Math.Min(s.Count, int.MaxValue), (ushort)Math.Min(s.Condition, ushort.MaxValue));
        if (s.Gun is { Length: > 0 } gun)
        {
            stack.Loaded = (ushort)Math.Min(gun[0].Loaded, ushort.MaxValue);
            stack.LoadedAmmo = (ushort)Math.Min(gun[0].LoadedAmmo, ushort.MaxValue);
            stack.Sight = (ushort)Math.Min(gun[0].Sight, ushort.MaxValue);
            stack.Muzzle = (ushort)Math.Min(gun[0].Muzzle, ushort.MaxValue);
            stack.Rail = (ushort)Math.Min(gun[0].Rail, ushort.MaxValue);
        }
        target.Set(s.Index, stack);
    }

    // The server's copy of a container named by an intent's reference, created for the loot as it opens.
    private Container? ServerContainer(ContainerRef which, int size)
    {
        switch (which)
        {
            case ContainerRef.Main:
                return _server.Main;
            case ContainerRef.Belt:
                return _server.Belt;
            case ContainerRef.Wear:
                return _server.Wear;
        }
        if (size < 1)
        {
            return null;
        }
        ContainerKind kind = LootKind switch
        {
            LootKind.Box => ContainerKind.Box,
            LootKind.Device => ContainerKind.Device,
            LootKind.Corpse => ContainerKind.Corpse,
            LootKind.Crate => ContainerKind.Crate,
            _ => which == ContainerRef.LootBelt ? ContainerKind.Belt : which == ContainerRef.LootWear ? ContainerKind.Wear : ContainerKind.Main,
        };
        var server = new Container(kind, size, LootDevice);
        var shown = new Container(kind, size, LootDevice);
        switch (which)
        {
            case ContainerRef.Loot:
                _serverLoot = server;
                Loot = shown;
                break;
            case ContainerRef.LootBelt:
                _serverLootBelt = server;
                LootBelt = shown;
                break;
            case ContainerRef.LootWear:
                _serverLootWear = server;
                LootWear = shown;
                break;
            default:
                return null;
        }
        return server;
    }

    /// <summary>The deployable being looted, if it is a device (its slots take fuel and cookables): set by the client.</summary>
    internal DeployableDef? LootDevice { get; set; }

    internal void OnLoot(in LootState m, DeployableDef? device)
    {
        bool wasOpen = LootTarget.Value != 0;
        _serverLoot = _serverLootBelt = _serverLootWear = null;
        Loot = LootBelt = LootWear = null;
        LootTarget = new EntityId(m.Target);
        LootKind = m.Target == 0 ? LootKind.None : m.Kind;
        LootDevice = LootKind == LootKind.Device ? device : null;
        Acknowledge(m.LastSeq);
        if (m.Target != 0)
        {
            _openedPending = true;
        }
        else if (wasOpen)
        {
            ContainerClosed?.Invoke();
        }
    }

    private bool _openedPending;

    // A loot's contents follow its LootState; announce the opening once they are in.
    internal void AfterMessages()
    {
        if (_openedPending)
        {
            _openedPending = false;
            Rebuild();
            Raise();
            ContainerOpened?.Invoke(LootKind, LootTarget);
        }
    }

    internal void OnVitals(in VitalsUpdate m)
    {
        SurvivalTuning s = Content.Survival;
        Vitals = new VitalsView
        {
            Health = m.Health / 10f,
            MaxHealth = s.MaxHealth,
            Calories = m.Calories,
            MaxCalories = s.MaxCalories,
            Hydration = m.Hydration,
            MaxHydration = s.MaxHydration,
            Temperature = m.Temperature,
            Cold = m.Cold / 255f,
            Bleeding = m.Bleeding,
            Healing = m.Healing,
            Burning = m.Burning,
            Comfort = m.Comfort,
            Wet = m.Wet,
        };
        _dirty = true;
    }

    internal void OnCraftQueue(in CraftQueueState m)
    {
        var jobs = new List<CraftJob>();
        foreach (CraftJobNet j in m.Jobs ?? Array.Empty<CraftJobNet>())
        {
            jobs.Add(new CraftJob
            {
                Ticket = j.Ticket, Recipe = (ushort)Math.Min(j.Recipe, ushort.MaxValue), Count = (int)Math.Min(j.Count, int.MaxValue),
                TicksLeft = (int)Math.Min(j.TicksLeft, int.MaxValue), UnitTicks = (int)Math.Min(j.UnitTicks, int.MaxValue),
            });
        }
        _serverCraft.Set(jobs, m.NextTicket);
        _craftTick = m.ServerTick;
        Acknowledge(m.LastSeq);
    }

    internal void OnResult(in IntentResult m)
    {
        uint seq = m.Seq;
        int index = _pending.FindIndex(p => p.Seq == seq);
        if (index >= 0)
        {
            _pending.RemoveAt(index);
            _dirty = true;
        }
        var answer = new IntentAnswer(m.Seq, m.Code, (int)Math.Min(m.Detail, int.MaxValue));
        AnsweredPiece(m.Seq, answer.Done);
        IntentAnswered?.Invoke(answer);
        if (!answer.Done)
        {
            IntentRejected?.Invoke(answer);
        }
    }

    internal void OnLife(in LifeState m)
    {
        bool wasDead = Life.Dead;
        var options = new List<RespawnOption>();
        if (m.Dead)
        {
            options.Add(new RespawnOption(EntityId.None, Vector2.Zero, ClockSeconds + m.BeachReadySeconds));
            foreach (BagNet bag in m.Bags ?? Array.Empty<BagNet>())
            {
                options.Add(new RespawnOption(new EntityId(bag.Id), new Vector2(bag.X, bag.Z), ClockSeconds + bag.ReadySeconds));
            }
        }
        bool wasDown = Life.Wounded;
        Life = new LifeView
        {
            Dead = m.Dead, Cause = m.Cause ?? "", KilledBy = m.KilledBy ?? "", Options = options,
            Wounded = m.Wounded && !m.Dead, WoundedUntilSeconds = ClockSeconds + m.WoundedSeconds,
        };
        _dirty = true;
        if (m.Dead && !wasDead)
        {
            Gun.Reset();
            Died?.Invoke(Life);
        }
        else if (!m.Dead && wasDead)
        {
            Gun.Reset();
            Respawned?.Invoke();
        }
        if (Life.Wounded && !wasDown)
        {
            Downed?.Invoke(Life);
        }
        else if (!Life.Wounded && wasDown && !m.Dead)
        {
            GotUp?.Invoke();
        }
    }

    /// <summary>Seconds until a respawn option is ready (0: now).</summary>
    public double SecondsUntilReady(RespawnOption option) => Math.Max(0, option.ReadyAtSeconds - ClockSeconds);

    internal void OnHitMarker(in HitMarker m) => HitMarker?.Invoke(new HitMarkerEvent(m.Target, m.Region, m.Damage / 10f, m.Killed, m.WeakSpot, m.Downed));

    internal void OnItemsChanged(in ItemsChanged m)
    {
        if (Content.Item((int)Math.Min(m.Item, ushort.MaxValue)) is { } item)
        {
            ItemsChanged?.Invoke(new ItemsChangedEvent(item, m.Count, m.Reason, m.WeakSpot));
        }
    }

    internal void OnDamage(in DamageTaken m) =>
        DamageTaken?.Invoke(new DamageEvent(m.Damage / 10f, m.Region, new Vector3(m.FromX, m.FromY, m.FromZ), Content.Item((int)Math.Min(m.Weapon, ushort.MaxValue))));

    internal void OnCraftFinished(in CraftFinished m)
    {
        if (Content.Item((int)Math.Min(m.Item, ushort.MaxValue)) is { } item)
        {
            CraftFinished?.Invoke(item, (int)Math.Min(m.Count, int.MaxValue));
        }
    }

    private sealed class Pending(Func<LocalPlayer, bool> apply)
    {
        public uint Seq;

        public bool Apply(LocalPlayer player) => apply(player);
    }
}
