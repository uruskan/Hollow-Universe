using System;
using System.Collections.Generic;
using System.Linq;

namespace HollowUniverse.UI.Model;

/// <summary>
/// A stand-in for the client API (GROUND.md §8): a survivor with a believable inventory, recipes, a crafting queue,
/// containers to open, sleeping bags, and simple local rules for moving, splitting, crafting and respawning. The
/// viewer (<c>--view ui</c>) and the interface's own tests run on it; the game uses the adapter over the client API.
/// </summary>
public sealed class MockGroundUi : IGroundUi
{
    private static readonly string[] CategoryOrder =
    {
        "FAVOURITE", "COMMON", "CONSTRUCTION", "ITEMS", "RESOURCES", "CLOTHING", "TOOLS", "MEDICAL", "WEAPONS", "AMMO",
        "TRAPS", "ELECTRICAL", "FUN", "OTHER",
    };

    private readonly Dictionary<string, ItemInfo> _items = new(StringComparer.Ordinal);
    private readonly ItemStack[] _main = new ItemStack[24];
    private readonly ItemStack[] _belt = new ItemStack[6];
    private readonly ItemStack[] _wear = new ItemStack[7];
    private ItemStack[] _loot = Array.Empty<ItemStack>();
    private readonly List<Recipe> _recipes = new();
    private readonly List<CraftJob> _queue = new();
    private readonly List<RespawnBag> _bags = new();
    private int _nextTicket = 1;
    private int _planPiece = 1;

    public MockGroundUi()
    {
        DefineItems();
        DefineRecipes();
        Categories = CategoryOrder
            .Select(name => new CraftCategory(name, _recipes.Count(r => r.Category == name)))
            .ToArray();
        FillInventory();
        _queue.Add(new CraftJob(_nextTicket++, "bandage", 2, 7.5f, 10f));
        _queue.Add(new CraftJob(_nextTicket++, "arrow.wooden", 6, 30f, 30f));
    }

    // ---------------------------------------------------------------- reads

    public string PlayerName { get; set; } = "Survivor";

    public Vitals Vitals { get; set; } = new(100, 250, 100, Temperature: 21);

    public int ActiveBelt { get; private set; }

    public LootView? Loot { get; private set; }

    public IReadOnlyList<CraftCategory> Categories { get; }

    public IReadOnlyList<Recipe> Recipes => _recipes;

    public int Workbench { get; set; } = 1;

    public IReadOnlyList<CraftJob> Queue => _queue;

    public DeathInfo? Death { get; private set; }

    public IReadOnlyList<RespawnBag> Bags => _bags;

    public InteractionPrompt? Prompt { get; set; }

    public HeldAmmo? Ammo
    {
        get
        {
            ItemStack held = ActiveBelt >= 0 ? _belt[ActiveBelt] : default;
            return held.HasAmmo ? new HeldAmmo(held.Ammo, Have("ammo.pistol")) : null;
        }
    }

    public ItemInfo Item(string key) =>
        _items.TryGetValue(key, out ItemInfo? info) ? info : new ItemInfo(key, key.ToUpperInvariant(), "", "OTHER");

    public IReadOnlyList<ItemStack> Slots(ContainerId container) => Of(container);

    public int Have(string key) => _main.Concat(_belt).Where(s => s.Key == key).Sum(s => s.Amount);

    public WheelKind? HeldWheel => ActiveBelt < 0 ? null : _belt[ActiveBelt].Key switch
    {
        "plan" => WheelKind.Plan,
        "hammer" => WheelKind.Hammer,
        _ => null,
    };

    public IReadOnlyList<WheelChoice> Wheel(WheelKind kind) => kind == WheelKind.Plan ? PlanChoices() : HammerChoices();

    public int WheelCurrent(WheelKind kind) => kind == WheelKind.Plan ? _planPiece : -1;

    // ---------------------------------------------------------------- events

    public event Action<bool>? HitConfirmed;

    public event Action<float>? Damaged;

    public event Action<string, int>? Gathered;

    public event Action<string, int>? Crafted;

    public event Action<string>? Notice;

    public event Action<ChatLine>? ChatReceived;

    // ---------------------------------------------------------------- actions

    public void Move(SlotRef from, SlotRef to, int amount)
    {
        if (from == to || !Valid(from) || !Valid(to))
        {
            return;
        }
        ItemStack[] source = Of(from.Container);
        ItemStack[] target = Of(to.Container);
        ItemStack moving = source[from.Index];
        if (moving.IsEmpty)
        {
            return;
        }
        amount = Math.Clamp(amount, 1, moving.Amount);
        ItemStack there = target[to.Index];
        if (!Accepts(to, moving.Key))
        {
            Notice?.Invoke(to.Container == ContainerId.Wear ? "You can't wear that." : "That doesn't go there.");
            return;
        }
        if (there.IsEmpty)
        {
            target[to.Index] = moving with { Amount = amount };
            source[from.Index] = Take(moving, amount);
            return;
        }
        int stack = Item(moving.Key).StackSize;
        if (there.Key == moving.Key && stack > 1)
        {
            int moved = Math.Min(amount, stack - there.Amount);
            if (moved <= 0)
            {
                return;
            }
            target[to.Index] = there with { Amount = there.Amount + moved };
            source[from.Index] = Take(moving, moved);
            return;
        }
        // Different items swap, but only whole stacks, and only where each is allowed.
        if (amount == moving.Amount && Accepts(from, there.Key))
        {
            target[to.Index] = moving;
            source[from.Index] = there;
        }
    }

    public void Split(SlotRef slot, int amount)
    {
        if (!Valid(slot))
        {
            return;
        }
        ItemStack[] slots = Of(slot.Container);
        int free = System.Array.FindIndex(slots, s => s.IsEmpty);
        if (free < 0)
        {
            Notice?.Invoke("No free slot to split into.");
            return;
        }
        Move(slot, slot with { Index = free }, amount);
    }

    public void Drop(SlotRef slot, int amount)
    {
        if (!Valid(slot))
        {
            return;
        }
        ItemStack[] slots = Of(slot.Container);
        ItemStack stack = slots[slot.Index];
        if (!stack.IsEmpty)
        {
            slots[slot.Index] = Take(stack, Math.Clamp(amount, 1, stack.Amount));
        }
    }

    public void QuickMove(SlotRef slot)
    {
        if (!Valid(slot) || Of(slot.Container)[slot.Index].IsEmpty)
        {
            return;
        }
        ContainerId[] targets = slot.Container switch
        {
            ContainerId.Loot => new[] { ContainerId.Main, ContainerId.Belt },
            ContainerId.Wear => new[] { ContainerId.Main, ContainerId.Belt },
            _ when Loot != null => new[] { ContainerId.Loot },
            ContainerId.Main => new[] { ContainerId.Belt },
            _ => new[] { ContainerId.Main },
        };
        foreach (ContainerId target in targets)
        {
            Store(slot, target);
            if (Of(slot.Container)[slot.Index].IsEmpty)
            {
                return;
            }
        }
    }

    public void Use(SlotRef slot)
    {
        if (!Valid(slot))
        {
            return;
        }
        ItemStack[] slots = Of(slot.Container);
        ItemStack stack = slots[slot.Index];
        Vitals v = Vitals;
        switch (stack.Key)
        {
            case "bandage":
                Vitals = v with { Health = Math.Min(Vitals.MaxHealth, v.Health + 5), Bleeding = 0 };
                break;
            case "syringe":
                Vitals = v with { Health = Math.Min(Vitals.MaxHealth, v.Health + 15) };
                break;
            case "meat.cooked":
                Vitals = v with { Calories = Math.Min(Vitals.MaxCalories, v.Calories + 40) };
                break;
            default:
                return;
        }
        slots[slot.Index] = Take(stack, 1);
    }

    public void Craft(string key, int amount)
    {
        Recipe? recipe = _recipes.FirstOrDefault(r => r.Key == key);
        if (recipe == null || amount < 1)
        {
            return;
        }
        if (recipe.Workbench > Workbench)
        {
            Notice?.Invoke($"Needs a level {recipe.Workbench} workbench.");
            return;
        }
        if (recipe.Ingredients.Any(i => Have(i.Key) < i.Amount * amount))
        {
            Notice?.Invoke("Not enough resources.");
            return;
        }
        foreach (Ingredient ingredient in recipe.Ingredients)
        {
            Consume(ingredient.Key, ingredient.Amount * amount);
        }
        _queue.Add(new CraftJob(_nextTicket++, key, amount * recipe.Yield, recipe.Seconds * amount, recipe.Seconds * amount));
    }

    public void CancelCraft(int ticket)
    {
        int index = _queue.FindIndex(j => j.Ticket == ticket);
        if (index < 0)
        {
            return;
        }
        CraftJob job = _queue[index];
        _queue.RemoveAt(index);
        Recipe? recipe = _recipes.FirstOrDefault(r => r.Key == job.Key);
        if (recipe != null)
        {
            int times = Math.Max(1, job.Count / Math.Max(1, recipe.Yield));
            foreach (Ingredient ingredient in recipe.Ingredients)
            {
                Give(ingredient.Key, ingredient.Amount * times);
            }
        }
    }

    public void Respawn(string? bagId)
    {
        if (Death == null)
        {
            return;
        }
        RespawnBag? bag = bagId == null ? null : _bags.FirstOrDefault(b => b.Id == bagId);
        if (bag != null && bag.Cooldown > 0)
        {
            Notice?.Invoke($"{bag.Name} is not ready yet.");
            return;
        }
        Death = null;
        Vitals = new Vitals(60, 180, 120, Temperature: 18);
        Notice?.Invoke(bag == null ? "You wake on a beach." : $"You wake at {bag.Name}.");
    }

    public void SelectBelt(int index) => ActiveBelt = index == ActiveBelt ? -1 : Math.Clamp(index, -1, 5);

    public void SetBurning(bool on)
    {
        if (Loot is { Kind: ContainerKind.Furnace or ContainerKind.Campfire })
        {
            Loot = Loot with { IsOn = on };
        }
    }

    public void ChooseWheel(WheelKind kind, int index)
    {
        IReadOnlyList<WheelChoice> choices = Wheel(kind);
        if (index < 0 || index >= choices.Count)
        {
            return;
        }
        if (kind == WheelKind.Plan)
        {
            _planPiece = index;
        }
        else if (choices[index].Reason is string reason)
        {
            Notice?.Invoke(reason);
        }
    }

    public void CloseLoot()
    {
        Loot = null;
        _loot = System.Array.Empty<ItemStack>();
    }

    public void Say(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            ChatReceived?.Invoke(new ChatLine(PlayerName, text.Trim()));
        }
    }

    // ---------------------------------------------------------------- scenarios and time

    /// <summary>Opens a container of the given kind, filled with sample contents.</summary>
    public void OpenLoot(ContainerKind kind)
    {
        switch (kind)
        {
            case ContainerKind.Furnace:
                _loot = new ItemStack[6];
                _loot[0] = new ItemStack("wood", 200);
                _loot[1] = new ItemStack("metal.ore", 300);
                _loot[2] = new ItemStack("sulfur.ore", 150);
                _loot[3] = new ItemStack("metal.fragments", 45);
                _loot[4] = new ItemStack("charcoal", 30);
                Loot = new LootView(kind, "FURNACE", _loot) { IsOn = true, Progress = 0.4f, FuelSeconds = 400 };
                break;
            case ContainerKind.Campfire:
                _loot = new ItemStack[5];
                _loot[0] = new ItemStack("wood", 50);
                _loot[1] = new ItemStack("meat.raw", 2);
                _loot[2] = new ItemStack("meat.cooked", 1);
                Loot = new LootView(kind, "CAMPFIRE", _loot) { IsOn = true, Progress = 0.65f, FuelSeconds = 100 };
                break;
            case ContainerKind.Corpse:
                _loot = new ItemStack[37];
                _loot[0] = new ItemStack("wood", 340);
                _loot[1] = new ItemStack("stones", 120);
                _loot[2] = new ItemStack("cloth", 18);
                _loot[3] = new ItemStack("meat.raw", 3);
                _loot[24] = new ItemStack("rock", 1, 0.45f);
                _loot[25] = new ItemStack("torch", 1, 0.8f);
                _loot[26] = new ItemStack("spear.wooden", 1, 0.3f);
                _loot[30] = new ItemStack("hood.cloth", 1, 0.7f);
                _loot[34] = new ItemStack("boots.hide", 1, 0.5f);
                Loot = new LootView(kind, "ADA", _loot);
                break;
            default:
                _loot = new ItemStack[30];
                _loot[0] = new ItemStack("wood", 1000);
                _loot[1] = new ItemStack("wood", 1000);
                _loot[2] = new ItemStack("wood", 760);
                _loot[3] = new ItemStack("stones", 500);
                _loot[4] = new ItemStack("metal.fragments", 250);
                _loot[5] = new ItemStack("cloth", 80);
                _loot[6] = new ItemStack("rifle", 1, 0.55f, 0);
                _loot[7] = new ItemStack("hatchet.stone", 1, 0.9f);
                _loot[8] = new ItemStack("trousers.cloth", 1, 1f);
                _loot[9] = new ItemStack("boots.hide", 1, 0.75f);
                _loot[10] = new ItemStack("arrow.wooden", 40);
                _loot[11] = new ItemStack("fat", 60);
                _loot[12] = new ItemStack("bag.sleeping", 1);
                _loot[13] = new ItemStack("metal.refined", 12);
                Loot = new LootView(kind, "LARGE WOODEN BOX", _loot);
                break;
        }
    }

    /// <summary>Kills the player: the death screen's sample.</summary>
    public void Die()
    {
        Vitals = Vitals with { Health = 0, Bleeding = 0 };
        Death = new DeathInfo(730, "Ada", "pistol", 23.4f, 0.47f, 0.43f);
        _bags.Clear();
        _bags.Add(new RespawnBag("bag-1", "Sleeping Bag", 0, 0.36f, 0.56f));
        _bags.Add(new RespawnBag("bag-2", "Hill Base", 143, 0.63f, 0.33f));
        _bags.Add(new RespawnBag("bag-3", "River Camp", 47, 0.24f, 0.30f));
    }

    /// <summary>A cold, wet survivor who was just shot at in their base: the HUD's sample.</summary>
    public void HudSample()
    {
        Vitals = new Vitals(86, 250, 100, Temperature: 3, Comfort: 0, Bleeding: 1.5f, Wetness: 0.4f, Building: Privilege.Allowed);
        Prompt = new InteractionPrompt("E", "OPEN", MoreOptions: true);
    }

    /// <summary>Raises the HUD's sample moments: pickups, a hit, damage, chat.</summary>
    public void RaiseSamples()
    {
        Gathered?.Invoke("wood", 20);
        Gathered?.Invoke("stones", 10);
        Crafted?.Invoke("bandage", 1);
        HitConfirmed?.Invoke(false);
        Damaged?.Invoke(-58f);
        ChatReceived?.Invoke(new ChatLine("", "Welcome to Nolcas Peris CQ-SLY 7-0 B 3.", FromServer: true));
        ChatReceived?.Invoke(new ChatLine("Ada", "anyone near the river? I've got spare cloth"));
        ChatReceived?.Invoke(new ChatLine("Ben", "on my way, bring the bandages"));
    }

    /// <summary>Advances time: the crafting queue, bag cooldowns, the furnace.</summary>
    public void Tick(double delta)
    {
        float dt = (float)delta;
        if (_queue.Count > 0)
        {
            CraftJob job = _queue[0];
            float remaining = job.Remaining - dt;
            if (remaining > 0)
            {
                _queue[0] = job with { Remaining = remaining };
            }
            else
            {
                _queue.RemoveAt(0);
                Give(job.Key, job.Count);
                Crafted?.Invoke(job.Key, job.Count);
            }
        }
        for (int i = 0; i < _bags.Count; i++)
        {
            if (_bags[i].Cooldown > 0)
            {
                _bags[i] = _bags[i] with { Cooldown = Math.Max(0, _bags[i].Cooldown - dt) };
            }
        }
        if (Loot is { IsOn: true } loot)
        {
            float progress = loot.Progress + dt / 8f;
            Loot = loot with { Progress = progress - MathF.Floor(progress), FuelSeconds = Math.Max(0, loot.FuelSeconds - dt) };
        }
    }

    // ---------------------------------------------------------------- rules

    private ItemStack[] Of(ContainerId container) => container switch
    {
        ContainerId.Main => _main,
        ContainerId.Belt => _belt,
        ContainerId.Wear => _wear,
        _ => _loot,
    };

    private bool Valid(SlotRef slot) => slot.Index >= 0 && slot.Index < Of(slot.Container).Length;

    private static ItemStack Take(ItemStack stack, int amount) =>
        stack.Amount - amount <= 0 ? default : stack with { Amount = stack.Amount - amount };

    // Clothing only in clothing slots; a furnace takes fuel and ore, a campfire fuel and food; outputs take nothing.
    private bool Accepts(SlotRef slot, string key)
    {
        if (slot.Container == ContainerId.Wear)
        {
            return Item(key).Wearable;
        }
        if (slot.Container != ContainerId.Loot || Loot == null)
        {
            return true;
        }
        return Loot.Kind switch
        {
            ContainerKind.Furnace => slot.Index == 0 ? key == "wood" : slot.Index <= 2 && key.EndsWith(".ore", StringComparison.Ordinal),
            ContainerKind.Campfire => slot.Index == 0 ? key == "wood" : key.StartsWith("meat.", StringComparison.Ordinal),
            _ => true,
        };
    }

    // Merges into matching stacks first, then fills the first free slot.
    private void Store(SlotRef from, ContainerId target)
    {
        ItemStack[] slots = Of(target);
        ItemStack moving = Of(from.Container)[from.Index];
        for (int pass = 0; pass < 2 && !Of(from.Container)[from.Index].IsEmpty; pass++)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                moving = Of(from.Container)[from.Index];
                if (moving.IsEmpty)
                {
                    return;
                }
                bool merge = pass == 0 && slots[i].Key == moving.Key && slots[i].Amount < Item(moving.Key).StackSize;
                if (merge || (pass == 1 && slots[i].IsEmpty))
                {
                    Move(from, new SlotRef(target, i), moving.Amount);
                }
            }
        }
    }

    private void Consume(string key, int amount)
    {
        foreach (ItemStack[] slots in new[] { _main, _belt })
        {
            for (int i = 0; i < slots.Length && amount > 0; i++)
            {
                if (slots[i].Key == key)
                {
                    int taken = Math.Min(amount, slots[i].Amount);
                    slots[i] = Take(slots[i], taken);
                    amount -= taken;
                }
            }
        }
    }

    private void Give(string key, int amount)
    {
        int stack = Math.Max(1, Item(key).StackSize);
        foreach (ItemStack[] slots in new[] { _main, _belt })
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < slots.Length && amount > 0; i++)
                {
                    bool merge = pass == 0 && slots[i].Key == key && slots[i].Amount < stack;
                    if (merge || (pass == 1 && slots[i].IsEmpty))
                    {
                        int have = slots[i].IsEmpty ? 0 : slots[i].Amount;
                        int added = Math.Min(amount, stack - have);
                        slots[i] = new ItemStack(key, have + added, Item(key).Category is "TOOLS" or "WEAPONS" ? 1f : -1f);
                        amount -= added;
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- sample data (our names; GROUND.md §1)

    private void DefineItems()
    {
        void Add(string key, string name, string category, int stack, string description, params ItemStat[] stats) =>
            _items[key] = new ItemInfo(key, name, description, category, stack, category == "CLOTHING",
                category is "MEDICAL" or "FOOD", stats);

        Add("wood", "Wood", "RESOURCES", 1000, "Logs and branches, for building, tools and fuel.");
        Add("stones", "Stones", "RESOURCES", 1000, "Broken rock, for tools, furnaces and stone walls.");
        Add("metal.ore", "Metal Ore", "RESOURCES", 1000, "Smelt it in a furnace to get metal fragments.");
        Add("sulfur.ore", "Sulfur Ore", "RESOURCES", 1000, "Smelt it in a furnace to get sulfur.");
        Add("metal.fragments", "Metal Fragments", "RESOURCES", 1000, "Smelted metal, for tools, weapons and sheet metal walls.");
        Add("metal.refined", "Refined Metal", "RESOURCES", 100, "Pure metal, for the strongest walls and for firearms.");
        Add("charcoal", "Charcoal", "RESOURCES", 1000, "What is left of burnt wood.");
        Add("cloth", "Cloth", "RESOURCES", 1000, "Woven plant fibre, for clothing, bags and bandages.");
        Add("fat", "Animal Fat", "RESOURCES", 1000, "Rendered from animals. It burns well.");
        Add("scrap", "Scrap", "RESOURCES", 1000, "Salvaged parts, traded and used for research.");
        Add("meat.raw", "Raw Meat", "FOOD", 20, "Cook it on a campfire before eating it.", new ItemStat("Calories", "+5"));
        Add("meat.cooked", "Cooked Meat", "FOOD", 20, "A warm meal.", new ItemStat("Calories", "+40"), new ItemStat("Hydration", "+2"));
        Add("rock", "Rock", "TOOLS", 1, "A fist-sized rock. Hit trees and stones with it.", new ItemStat("Gathering", "Poor", false));
        Add("hatchet.stone", "Stone Hatchet", "TOOLS", 1, "A sharpened stone on a stick, for chopping wood.", new ItemStat("Wood per hit", "+12"));
        Add("pickaxe.stone", "Stone Pickaxe", "TOOLS", 1, "A pointed stone on a stick, for breaking ore.", new ItemStat("Ore per hit", "+10"));
        Add("hammer", "Hammer", "TOOLS", 1, "Repairs, upgrades, rotates and demolishes building pieces.");
        Add("plan", "Building Plan", "CONSTRUCTION", 1, "Lays out foundations, walls, floors and roofs.");
        Add("torch", "Torch", "TOOLS", 1, "Light and a little warmth. It burns down slowly.");
        Add("bandage", "Bandage", "MEDICAL", 3, "Stops bleeding and heals a little.", new ItemStat("Instant Health", "+5"), new ItemStat("Bleeding", "Stops"));
        Add("syringe", "Syringe", "MEDICAL", 2, "A strong dose of medicine.", new ItemStat("Instant Health", "+15"), new ItemStat("Over time", "+20"));
        Add("shirt.cloth", "Cloth Shirt", "CLOTHING", 1, "Rough woven cloth. Keeps you a little warmer.", new ItemStat("Cold", "+8%"));
        Add("trousers.cloth", "Cloth Trousers", "CLOTHING", 1, "Rough woven cloth trousers.", new ItemStat("Cold", "+8%"));
        Add("hood.cloth", "Cloth Hood", "CLOTHING", 1, "A hood against wind and rain.", new ItemStat("Cold", "+5%"));
        Add("boots.hide", "Hide Boots", "CLOTHING", 1, "Boots of tanned hide.", new ItemStat("Cold", "+6%"));
        Add("spear.wooden", "Wooden Spear", "WEAPONS", 1, "A sharpened pole. Thrust or throw it.", new ItemStat("Damage", "35"));
        Add("bow", "Hunting Bow", "WEAPONS", 1, "Quiet, slow and deadly at short range.", new ItemStat("Damage", "50"), new ItemStat("Draw", "1 s"));
        Add("pistol", "Service Pistol", "WEAPONS", 1, "A semi-automatic sidearm that takes pistol rounds.",
            new ItemStat("Damage", "40"), new ItemStat("Rate of fire", "400 rpm"), new ItemStat("Magazine", "15"));
        Add("rifle", "Frontier Rifle", "WEAPONS", 1, "An automatic rifle that takes rifle rounds.",
            new ItemStat("Damage", "50"), new ItemStat("Rate of fire", "450 rpm"), new ItemStat("Magazine", "30"));
        Add("arrow.wooden", "Wooden Arrow", "AMMO", 64, "A plain arrow for the hunting bow.");
        Add("ammo.pistol", "Pistol Rounds", "AMMO", 128, "Rounds for pistols.");
        Add("box.wooden", "Wooden Box", "ITEMS", 1, "A small box that holds 12 stacks.");
        Add("box.large", "Large Wooden Box", "ITEMS", 1, "A big box that holds 30 stacks.");
        Add("bag.sleeping", "Sleeping Bag", "ITEMS", 1, "Place it to respawn there after death.");
        Add("campfire", "Campfire", "ITEMS", 1, "Cooks food and keeps you warm.");
        Add("furnace", "Furnace", "ITEMS", 1, "Smelts ore with wood for fuel.");
        Add("workbench.1", "Workbench Level 1", "ITEMS", 1, "Unlocks level 1 recipes nearby.");
        Add("claim", "Claim Device", "CONSTRUCTION", 1, "Claims the ground around it for building and holds its upkeep.");
        Add("door.wooden", "Wooden Door", "CONSTRUCTION", 1, "A plain door for a doorway.");
        Add("lock.key", "Key Lock", "CONSTRUCTION", 1, "Locks a door; craft keys to share it.");
    }

    private void DefineRecipes()
    {
        void Add(string key, string category, float seconds, int workbench, int yield, params (string Key, int Amount)[] ingredients) =>
            _recipes.Add(new Recipe(key, category, seconds, ingredients.Select(i => new Ingredient(i.Key, i.Amount)).ToArray(), yield, workbench));

        Add("plan", "CONSTRUCTION", 15, 0, 1, ("wood", 20));
        Add("claim", "CONSTRUCTION", 30, 0, 1, ("wood", 1000));
        Add("door.wooden", "CONSTRUCTION", 30, 0, 1, ("wood", 300));
        Add("lock.key", "CONSTRUCTION", 15, 0, 1, ("wood", 75));
        Add("box.wooden", "ITEMS", 15, 0, 1, ("wood", 100));
        Add("box.large", "ITEMS", 30, 1, 1, ("wood", 250), ("metal.fragments", 50));
        Add("bag.sleeping", "ITEMS", 30, 0, 1, ("cloth", 30));
        Add("campfire", "ITEMS", 10, 0, 1, ("wood", 100));
        Add("furnace", "ITEMS", 30, 0, 1, ("stones", 200), ("wood", 100), ("fat", 50));
        Add("workbench.1", "ITEMS", 30, 0, 1, ("wood", 500), ("metal.fragments", 100), ("scrap", 50));
        Add("shirt.cloth", "CLOTHING", 15, 0, 1, ("cloth", 20));
        Add("trousers.cloth", "CLOTHING", 15, 0, 1, ("cloth", 20));
        Add("hood.cloth", "CLOTHING", 10, 0, 1, ("cloth", 10));
        Add("rock", "TOOLS", 5, 0, 1, ("stones", 10));
        Add("hatchet.stone", "TOOLS", 30, 0, 1, ("wood", 200), ("stones", 100));
        Add("pickaxe.stone", "TOOLS", 30, 0, 1, ("wood", 200), ("stones", 100));
        Add("hammer", "TOOLS", 15, 0, 1, ("wood", 100));
        Add("torch", "TOOLS", 5, 0, 1, ("wood", 30), ("fat", 1));
        Add("bandage", "MEDICAL", 5, 0, 1, ("cloth", 4));
        Add("syringe", "MEDICAL", 15, 1, 1, ("cloth", 15), ("metal.fragments", 10), ("fat", 5));
        Add("spear.wooden", "WEAPONS", 15, 0, 1, ("wood", 300));
        Add("bow", "WEAPONS", 15, 0, 1, ("wood", 200), ("cloth", 50));
        Add("pistol", "WEAPONS", 30, 2, 1, ("metal.refined", 5), ("metal.fragments", 75));
        Add("rifle", "WEAPONS", 45, 3, 1, ("metal.refined", 50), ("wood", 200), ("metal.fragments", 250));
        Add("arrow.wooden", "AMMO", 5, 0, 2, ("wood", 25), ("stones", 10));
        Add("ammo.pistol", "AMMO", 5, 2, 4, ("metal.fragments", 10), ("charcoal", 5));
    }

    private void FillInventory()
    {
        string[] keys =
        {
            "wood", "wood", "stones", "stones", "metal.ore", "metal.fragments",
            "sulfur.ore", "cloth", "fat", "scrap", "charcoal", "ammo.pistol",
            "arrow.wooden", "bow", "bandage", "meat.raw", "syringe",
        };
        int[] amounts = { 1000, 1000, 1000, 640, 1000, 100, 300, 500, 20, 45, 120, 45, 12, 1, 2, 4, 1 };
        for (int i = 0; i < keys.Length; i++)
        {
            _main[i] = new ItemStack(keys[i], amounts[i], keys[i] == "bow" ? 0.6f : -1f);
        }
        _belt[0] = new ItemStack("pistol", 1, 0.82f, 15);
        _belt[1] = new ItemStack("hammer", 1, 1f);
        _belt[2] = new ItemStack("plan", 1);
        _belt[3] = new ItemStack("hatchet.stone", 1, 0.64f);
        _belt[4] = new ItemStack("torch", 1, 0.3f);
        _belt[5] = new ItemStack("bandage", 3);
        _wear[0] = new ItemStack("hood.cloth", 1, 0.9f);
        _wear[1] = new ItemStack("shirt.cloth", 1, 0.85f);
        _wear[3] = new ItemStack("trousers.cloth", 1, 0.7f);
    }

    // The plan's pieces in the wheel's clockwise order (RustMVP's, matched to Rust); costs are in wood.
    private static readonly (int Glyph, string Name, string Description, int Cost)[] PlanPieces =
    {
        (0, "Foundation", "A square support for your base.", 50),
        (1, "Triangle Foundation", "A triangular support for your base.", 25),
        (16, "Ramp", "A quarter-height sloping access ramp.", 25),
        (17, "Steps", "Half-height steps attached to a floor.", 25),
        (2, "Floor", "A square upper floor or ceiling.", 25),
        (3, "Triangle Floor", "A triangular upper floor or ceiling.", 13),
        (10, "Floor Frame", "A square frame with an open centre.", 25),
        (11, "Triangle Floor Frame", "A triangular frame with an open centre.", 13),
        (4, "Wall", "Enclose your base with a full wall.", 50),
        (8, "Doorway", "A wall opening for a single door.", 35),
        (7, "Window", "A wall with a window opening.", 35),
        (9, "Wall Frame", "An opening for a double door.", 25),
        (5, "Half Wall", "A wall half the normal height.", 50),
        (6, "Low Wall", "A low protective wall.", 25),
        (12, "L Stairs", "A full-height staircase with a corner.", 50),
        (13, "U Stairs", "A full-height returning staircase.", 50),
        (14, "Spiral Stairs", "A half-height square spiral staircase.", 50),
        (15, "Triangle Spiral Stairs", "A half-height triangular spiral staircase.", 25),
        (18, "Roof", "A sloping square roof section.", 50),
        (19, "Triangle Roof", "A sloping triangular roof section.", 25),
    };

    private IReadOnlyList<WheelChoice> PlanChoices()
    {
        int wood = Have("wood");
        return PlanPieces.Select(p => new WheelChoice(p.Glyph, p.Name, p.Description, $"{p.Cost} x WOOD ({wood})",
            wood < p.Cost ? "Not enough wood" : null)).ToArray();
    }

    // The hammer aims at a wooden wall: wood is already done, stone and metal are affordable.
    private IReadOnlyList<WheelChoice> HammerChoices()
    {
        const string strength = "Improve this piece's strength";
        return new[]
        {
            new WheelChoice(100, "UPGRADE TO WOOD", strength, $"140 x WOOD ({Have("wood")})", "Already at this grade or stronger"),
            new WheelChoice(101, "UPGRADE TO STONE", strength, $"210 x STONES ({Have("stones")})"),
            new WheelChoice(102, "UPGRADE TO METAL", strength, $"140 x METAL FRAGMENTS ({Have("metal.fragments")})",
                Have("metal.fragments") < 140 ? "Not enough materials" : null),
            new WheelChoice(103, "UPGRADE TO REFINED METAL", strength, $"18 x REFINED METAL ({Have("metal.refined")})", "Not enough materials"),
            new WheelChoice(104, "ROTATE", "Turn this piece around"),
            new WheelChoice(105, "DEMOLISH", "Remove this piece and unsupported pieces"),
        };
    }
}
