using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using HollowUniverse.UI.Model;
using SimStack = Hollow.Simulation.Items.ItemStack;
using SimRecipe = Hollow.Simulation.Content.Recipe;
using UiStack = HollowUniverse.UI.Model.ItemStack;
using UiRecipe = HollowUniverse.UI.Model.Recipe;
using UiVitals = HollowUniverse.UI.Model.Vitals;
using UiContainerKind = HollowUniverse.UI.Model.ContainerKind;
using CraftJob = HollowUniverse.UI.Model.CraftJob;

namespace HollowUniverse.Ground;

/// <summary>
/// The ground interface's view model over the client (GROUND.md §8.1): <see cref="GroundUi"/> reads the player's own
/// state from <see cref="GameClient.Local"/> (containers with intents not yet answered applied, vitals, crafting, life)
/// and acts through its intents; the client's events become the HUD's moments. Item names, descriptions and stats come
/// from items.json, crafting tabs from categories.json. Reads are cached per revision, so polling them every frame
/// costs nothing.
/// </summary>
public sealed class ClientGroundUi : IGroundUi, IDisposable
{
    private readonly GameClient _client;
    private readonly Dictionary<string, ItemInfo> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<ContainerRef, (uint Revision, Container? Source, UiStack[] Slots)> _slots = new();
    private readonly List<CraftJob> _queue = new();
    private readonly List<RespawnBag> _bags = new();
    private UiStack[] _loot = Array.Empty<UiStack>();
    private (uint, uint, uint, Container?) _lootKey;
    private double _aliveSince;
    private bool _wasDead = true;
    private DamageEvent? _lastDamage;
    private double _lastDamageAt = -100;
    private DeathInfo? _death;

    public ClientGroundUi(GameClient client)
    {
        _client = client;
        GameContent c = client.Content;
        Categories = new[] { new CraftCategory("FAVOURITE", 0), new CraftCategory("COMMON", 0) }
            .Concat(c.Categories.Select(cat => new CraftCategory(cat.Name.ToUpperInvariant(), c.Recipes.Count(r => r.Known && r.Output.Category == cat.Key))))
            .ToArray();
        Recipes = c.Recipes.Where(r => r.Known).Select(r => new UiRecipe(r.Output.Key, CategoryName(r.Output.Category), r.Seconds,
            r.Ingredients.Select(i => new Ingredient(i.Item.Key, i.Count)).ToArray(), r.Amount, r.Workbench)).ToArray();
        LocalPlayer me = client.Local;
        me.HitMarker += OnHitMarker;
        me.DamageTaken += OnDamage;
        me.ItemsChanged += OnItems;
        me.CraftFinished += OnCrafted;
        me.IntentRejected += OnRejected;
        me.Died += OnDied;
        client.ChatReceived += OnChat;
    }

    public void Dispose()
    {
        LocalPlayer me = _client.Local;
        me.HitMarker -= OnHitMarker;
        me.DamageTaken -= OnDamage;
        me.ItemsChanged -= OnItems;
        me.CraftFinished -= OnCrafted;
        me.IntentRejected -= OnRejected;
        me.Died -= OnDied;
        _client.ChatReceived -= OnChat;
    }

    private LocalPlayer Me => _client.Local;

    private GameContent C => _client.Content;

    /// <summary>The camera's yaw (radians, the movement code's convention), for the bearing of damage.</summary>
    public Func<float> ViewYaw { get; set; } = () => 0;

    /// <summary>What the interact key would act on now (set by the view every frame).</summary>
    public InteractionPrompt? Prompt { get; set; }

    /// <summary>Test commands typed in chat (lines starting with '/'); null sends them as chat.</summary>
    public Action<string>? Command { get; set; }

    // ---------------------------------------------------------------- reads

    public string PlayerName => _client.Name;

    public ItemInfo Item(string key)
    {
        if (!_items.TryGetValue(key, out ItemInfo? info))
        {
            info = C.Item(key) is { } def ? Describe(def) : new ItemInfo(key, key, "", "OTHER");
            _items[key] = info;
        }
        return info;
    }

    public UiVitals Vitals
    {
        get
        {
            VitalsView v = Me.Vitals;
            if (v != _vitalsFrom)
            {
                _vitalsFrom = v;
                _vitals = new UiVitals(v.Health, v.Calories, v.Hydration, v.Temperature, v.Comfort ? 1 : 0, v.IsBleeding ? 1 : 0, v.Wet ? 1 : 0);
            }
            return _vitals;
        }
    }

    private VitalsView _vitalsFrom;
    private UiVitals _vitals = new(100, 500, 250);

    public IReadOnlyList<UiStack> Slots(ContainerId container) => container switch
    {
        ContainerId.Main => Of(ContainerRef.Main, Me.Inventory.Main),
        ContainerId.Belt => Of(ContainerRef.Belt, Me.Inventory.Belt),
        ContainerId.Wear => Of(ContainerRef.Wear, Me.Inventory.Wear),
        _ => LootSlots(),
    };

    public int ActiveBelt => Me.HeldSlot;

    public HeldAmmo? Ammo =>
        Me.Gun.Weapon != null ? new HeldAmmo(Me.Gun.Loaded, Me.Gun.Reserve, Me.Gun.Reloading)
        : Me.HeldItem?.Bow is { } bow ? new HeldAmmo(0, bow.Ammo.Sum(a => Me.Inventory.Count(a))) : null;

    public LootView? Loot
    {
        get
        {
            if (Me.LootKind == LootKind.None || Me.Loot == null)
            {
                return null;
            }
            RemoteThing? thing = _client.ThingOf(Me.LootTarget);
            switch (Me.LootKind)
            {
                case LootKind.Device:
                {
                    bool furnace = thing?.Deployable(C)?.Key == "furnace";
                    SimStack fuel = Me.Loot.Size > 0 ? Me.Loot[0] : default;
                    float seconds = C.Item(fuel.Item)?.Fuel is { } f ? f.Seconds * fuel.Count : 0;
                    return new LootView(furnace ? UiContainerKind.Furnace : UiContainerKind.Campfire, furnace ? "FURNACE" : "CAMPFIRE", LootSlots())
                    {
                        IsOn = thing?.Lit ?? false,
                        FuelSeconds = seconds,
                    };
                }
                case LootKind.Corpse:
                    return new LootView(UiContainerKind.Corpse, thing?.Name ?? "BODY", LootSlots());
                case LootKind.Sleeper:
                    string name = _client.Entities.FirstOrDefault(e => e.Id == Me.LootTarget)?.Name ?? "SLEEPER";
                    return new LootView(UiContainerKind.Corpse, name, LootSlots());
                default:
                    return new LootView(UiContainerKind.Box, (thing?.Deployable(C)?.Item.Name ?? "Box").ToUpperInvariant(), LootSlots());
            }
        }
    }

    public IReadOnlyList<CraftCategory> Categories { get; }

    public IReadOnlyList<UiRecipe> Recipes { get; }

    public int Workbench => Me.WorkbenchLevel;

    public int Have(string key) => C.Item(key) is { } def ? Me.Inventory.Count(def) : 0;

    public IReadOnlyList<CraftJob> Queue
    {
        get
        {
            _queue.Clear();
            foreach (CraftJobView job in Me.Crafting)
            {
                _queue.Add(new CraftJob((int)job.Ticket, job.Recipe.Output.Key, job.Count * job.Recipe.Amount, job.SecondsLeft, job.Count * job.Recipe.Seconds));
            }
            return _queue;
        }
    }

    public DeathInfo? Death
    {
        get
        {
            if (!Me.IsDead)
            {
                if (_wasDead && _client.HasCharacter)
                {
                    _aliveSince = _client.ClockSeconds;
                    _wasDead = false;
                }
                _death = null;
                return null;
            }
            _wasDead = true;
            return _death ??= Describe(Me.Life);
        }
    }

    public IReadOnlyList<RespawnBag> Bags
    {
        get
        {
            _bags.Clear();
            if (!Me.IsDead)
            {
                return _bags;
            }
            foreach (RespawnOption option in Me.Life.Options)
            {
                if (option.IsBeach)
                {
                    continue;
                }
                string name = _client.ThingOf(option.Bag)?.Deployable(C)?.Item.Name ?? "Sleeping bag";
                (float x, float y) = ZoneMap.Point(option.Position.X, option.Position.Y);
                _bags.Add(new RespawnBag(option.Bag.Value.ToString(CultureInfo.InvariantCulture), name, (float)Me.SecondsUntilReady(option), x, y));
            }
            return _bags;
        }
    }

    public WheelKind? HeldWheel => null;

    public IReadOnlyList<WheelChoice> Wheel(WheelKind kind) => Array.Empty<WheelChoice>();

    public int WheelCurrent(WheelKind kind) => -1;

    // ---------------------------------------------------------------- events

    public event Action<bool>? HitConfirmed;

    public event Action<float>? Damaged;

    public event Action<string, int>? Gathered;

    public event Action<string, int>? Crafted;

    public event Action<string>? Notice;

    public event Action<ChatLine>? ChatReceived;

    /// <summary>A short message for the player from the game itself (a connection lost, a test command's answer).</summary>
    public void ShowNotice(string notice) => Notice?.Invoke(notice);

    private void OnHitMarker(HitMarkerEvent e)
    {
        if (e.Target == HitTarget.Character)
        {
            HitConfirmed?.Invoke(e.Region == Hollow.Simulation.Content.HitRegion.Head);
        }
    }

    private void OnDamage(DamageEvent e)
    {
        _lastDamage = e;
        _lastDamageAt = _client.ClockSeconds;
        if (!_client.HasCharacter || e.Weapon == null && e.From == default)
        {
            return;
        }
        System.Numerics.Vector3 at = _client.Character.Position;
        System.Numerics.Vector3 up = _client.Frame.UpAt(at), d = e.From - at;
        d -= up * System.Numerics.Vector3.Dot(d, up);
        if (d.LengthSquared() < 0.01f)
        {
            return;
        }
        // The bearing from the view's facing, clockwise: yaw grows anticlockwise from −Z.
        float towards = MathF.Atan2(-d.X, -d.Z);
        float bearing = (ViewYaw() - towards) * 180 / MathF.PI;
        Damaged?.Invoke((bearing % 360 + 540) % 360 - 180);
    }

    private void OnItems(ItemsChangedEvent e)
    {
        if (e.Count > 0 && e.Reason is GainReason.Gathered or GainReason.PickedUp or GainReason.Looted)
        {
            Gathered?.Invoke(e.Item.Key, e.Count);
        }
        else if (e.Reason == GainReason.Broke)
        {
            Notice?.Invoke($"Your {e.Item.Name.ToLowerInvariant()} broke.");
        }
    }

    private void OnCrafted(ItemDef item, int count) => Crafted?.Invoke(item.Key, count);

    private void OnRejected(IntentAnswer answer)
    {
        string? text = answer.Result switch
        {
            ActionResult.NoSpace => "No room for that.",
            ActionResult.NotEnough => "Not enough resources.",
            ActionResult.NeedsWorkbench => "Needs a workbench nearby.",
            ActionResult.QueueFull => "The crafting queue is full.",
            ActionResult.OutOfReach => "Too far away.",
            ActionResult.Blocked => "Something is in the way.",
            ActionResult.TooSteep => "The ground is too steep.",
            ActionResult.Cooldown => "Not ready yet.",
            ActionResult.RateLimited => "Slow down.",
            ActionResult.BadPlacement => "You can't place that here.",
            ActionResult.NotAllowed => "That doesn't go there.",
            ActionResult.NotUsable => "You can't use that.",
            _ => null,
        };
        if (text != null)
        {
            Notice?.Invoke(text);
        }
    }

    private void OnDied(LifeView life) => _death = null;

    private void OnChat(Hollow.Net.Protocol.ChatLine line) =>
        ChatReceived?.Invoke(new ChatLine(line.From ?? "", line.Text ?? "", line.Kind == Hollow.Net.Protocol.ChatKind.Server));

    // ---------------------------------------------------------------- actions

    public void Move(SlotRef from, SlotRef to, int amount)
    {
        if (Resolve(from) is not var (a, i) || Resolve(to) is not var (b, j))
        {
            return;
        }
        Me.MoveItem(a, i, b, j, amount);
    }

    public void Split(SlotRef slot, int amount)
    {
        IReadOnlyList<UiStack> slots = Slots(slot.Container);
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsEmpty)
            {
                Move(slot, slot with { Index = i }, amount);
                return;
            }
        }
        Notice?.Invoke("No free slot to split into.");
    }

    public void Drop(SlotRef slot, int amount)
    {
        if (Resolve(slot) is var (c, i))
        {
            Me.Drop(c, i, amount);
        }
    }

    public void QuickMove(SlotRef slot)
    {
        if (Resolve(slot) is var (c, i))
        {
            Me.QuickMove(c, i);
        }
    }

    public void Use(SlotRef slot)
    {
        if (Resolve(slot) is var (c, i))
        {
            if ((c switch { ContainerRef.Main => Me.Inventory.Main, ContainerRef.Belt => Me.Inventory.Belt, _ => null }) is { } own)
            {
                Sounds.Used(C, own[i].Item);
            }
            Me.Use(c, i);
        }
    }

    public void Craft(string key, int amount)
    {
        if (C.Item(key) is { } def)
        {
            Sounds.Flat("ui.craft.start");
            Me.Craft(def.Id, amount);
        }
    }

    public void CancelCraft(int ticket) => Me.CancelCraft((uint)ticket);

    public void Respawn(string? bagId)
    {
        EntityId bag = bagId != null && ulong.TryParse(bagId, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong id) ? new EntityId(id) : EntityId.None;
        Me.Respawn(bag);
    }

    public void SelectBelt(int index) => Me.HeldSlot = index == Me.HeldSlot ? -1 : Math.Clamp(index, -1, PlayerInventory.BeltSlots - 1);

    public void SetBurning(bool on)
    {
        if (Me.LootKind == LootKind.Device && _client.ThingOf(Me.LootTarget) is { } thing && thing.Lit != on)
        {
            Me.Toggle(Me.LootTarget);
        }
    }

    public void ChooseWheel(WheelKind kind, int index)
    {
    }

    public void CloseLoot()
    {
        if (Me.LootKind != LootKind.None)
        {
            Me.Close();
        }
    }

    public void Say(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }
        if (text.StartsWith('/') && Command != null)
        {
            Command(text);
            return;
        }
        _client.SendChat(text);
    }

    // ---------------------------------------------------------------- helpers

    private string CategoryName(string key) => (C.Categories.FirstOrDefault(c => c.Key == key)?.Name ?? key).ToUpperInvariant();

    private ItemInfo Describe(ItemDef d)
    {
        var stats = new List<ItemStat>();
        string N(float v) => v.ToString(v % 1 == 0 ? "0" : "0.#", CultureInfo.InvariantCulture);
        if (d.Melee is { } m)
        {
            stats.Add(new ItemStat("Damage", N(m.Damage)));
            if (m.Tree > 0)
            {
                stats.Add(new ItemStat("Wood per hit", N(m.Tree), m.Tree >= 20));
            }
            if (m.Ore > 0)
            {
                stats.Add(new ItemStat("Ore per hit", N(m.Ore), m.Ore >= 20));
            }
            stats.Add(new ItemStat("Reach", N(m.Reach) + " m"));
        }
        if (d.Bow is { } bow)
        {
            stats.Add(new ItemStat("Draw", N(bow.DrawSeconds) + " s"));
        }
        if (d.Ammo is { } ammo)
        {
            stats.Add(new ItemStat("Damage", N(ammo.Damage)));
        }
        if (d.Consume is { } food)
        {
            if (food.Calories != 0)
            {
                stats.Add(new ItemStat("Calories", (food.Calories > 0 ? "+" : "") + N(food.Calories), food.Calories > 0));
            }
            if (food.Hydration != 0)
            {
                stats.Add(new ItemStat("Hydration", (food.Hydration > 0 ? "+" : "") + N(food.Hydration), food.Hydration > 0));
            }
            if (food.Health != 0)
            {
                stats.Add(new ItemStat("Instant health", "+" + N(food.Health)));
            }
            if (food.Heal != 0)
            {
                stats.Add(new ItemStat("Healing", "+" + N(food.Heal)));
            }
            if (food.StopsBleeding)
            {
                stats.Add(new ItemStat("Bleeding", "Stops"));
            }
        }
        if (d.Wear is { } wear)
        {
            if (wear.Melee > 0)
            {
                stats.Add(new ItemStat("Melee protection", N(wear.Melee * 100) + "%"));
            }
            if (wear.Projectile > 0)
            {
                stats.Add(new ItemStat("Projectile protection", N(wear.Projectile * 100) + "%"));
            }
            if (wear.Insulation > 0)
            {
                stats.Add(new ItemStat("Warmth", "+" + N(wear.Insulation) + " °C"));
            }
        }
        if (d.Fuel is { } fuel)
        {
            stats.Add(new ItemStat("Burns for", N(fuel.Seconds) + " s"));
        }
        if (d.Cook is { Into: { } into })
        {
            stats.Add(new ItemStat(d.Cook.In == "furnace" ? "Smelts into" : "Cooks into", into.Name));
        }
        return new ItemInfo(d.Key, d.Name, d.Description, CategoryName(d.Category), d.StackSize, d.Wear != null, d.Consume != null, stats);
    }

    private IReadOnlyList<UiStack> Of(ContainerRef which, Container container)
    {
        if (_slots.TryGetValue(which, out var cached) && cached.Revision == container.Revision && cached.Source == container)
        {
            return cached.Slots;
        }
        var slots = new UiStack[container.Size];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = Convert(container[i]);
        }
        _slots[which] = (container.Revision, container, slots);
        return slots;
    }

    private UiStack Convert(SimStack s)
    {
        if (s.IsEmpty || C.Item(s.Item) is not { } def)
        {
            return default;
        }
        float condition = def.HasCondition ? Math.Clamp((float)s.Condition / def.MaxCondition, 0, 1) : -1f;
        // A gun shows the rounds in its magazine on its slot (G4).
        return new UiStack(def.Key, s.Count, condition, def.Firearm != null ? s.Loaded : -1);
    }

    // The open container's slots as the loot panel lays them out: a sleeper's main, belt and clothing in a body's order.
    private IReadOnlyList<UiStack> LootSlots()
    {
        Container? loot = Me.Loot;
        if (loot == null)
        {
            return Array.Empty<UiStack>();
        }
        (uint, uint, uint, Container?) key = (loot.Revision, Me.LootBelt?.Revision ?? 0, Me.LootWear?.Revision ?? 0, loot);
        if (key == _lootKey && _loot.Length > 0)
        {
            return _loot;
        }
        _lootKey = key;
        var all = new List<UiStack>();
        for (int i = 0; i < loot.Size; i++)
        {
            all.Add(Convert(loot[i]));
        }
        if (Me.LootKind == LootKind.Sleeper)
        {
            foreach (Container? part in new[] { Me.LootBelt, Me.LootWear })
            {
                for (int i = 0; i < (part?.Size ?? 0); i++)
                {
                    all.Add(Convert(part![i]));
                }
            }
        }
        _loot = all.ToArray();
        return _loot;
    }

    // A slot of the interface as a container and index of the client's.
    private (ContainerRef, int)? Resolve(SlotRef slot)
    {
        switch (slot.Container)
        {
            case ContainerId.Main:
                return (ContainerRef.Main, slot.Index);
            case ContainerId.Belt:
                return (ContainerRef.Belt, slot.Index);
            case ContainerId.Wear:
                return (ContainerRef.Wear, slot.Index);
        }
        if (Me.Loot == null)
        {
            return null;
        }
        if (Me.LootKind == LootKind.Sleeper)
        {
            int main = Me.Loot.Size, belt = Me.LootBelt?.Size ?? 0;
            return slot.Index < main ? (ContainerRef.Loot, slot.Index)
                : slot.Index < main + belt ? (ContainerRef.LootBelt, slot.Index - main)
                : (ContainerRef.LootWear, slot.Index - main - belt);
        }
        return (ContainerRef.Loot, slot.Index);
    }

    private DeathInfo Describe(LifeView life)
    {
        string by = life.KilledBy.Length > 0 ? life.KilledBy : life.Cause.Length > 0 ? char.ToUpperInvariant(life.Cause[0]) + life.Cause[1..] : "Unknown";
        bool recent = _lastDamage != null && _client.ClockSeconds - _lastDamageAt < 10;
        string? weapon = recent ? _lastDamage!.Value.Weapon?.Key : null;
        float distance = 0;
        System.Numerics.Vector3 at = _client.Character.Position;
        if (recent && _lastDamage!.Value.From != default)
        {
            distance = System.Numerics.Vector3.Distance(at, _lastDamage.Value.From);
        }
        (float x, float y) = ZoneMap.Point(at.X, at.Z);
        return new DeathInfo((float)Math.Max(0, _client.ClockSeconds - _aliveSince), by, weapon, distance, x, y);
    }
}
