using System;
using System.Collections.Generic;

namespace HollowUniverse.UI.Model;

// The values the ground game's interface shows (GROUND.md §8). Plain C#, no Godot types: the client API adapts its
// state into these, and MockGroundUi fakes them for the viewer.

/// <summary>Which of the player's containers a slot belongs to, or the open one.</summary>
public enum ContainerId : byte
{
    /// <summary>The 24 main slots, 6 × 4.</summary>
    Main,

    /// <summary>The 6 belt slots (the hotbar).</summary>
    Belt,

    /// <summary>The 7 clothing slots.</summary>
    Wear,

    /// <summary>The container the player has open (<see cref="IGroundUi.Loot"/>).</summary>
    Loot,
}

/// <summary>A slot: its container and its index there.</summary>
public readonly record struct SlotRef(ContainerId Container, int Index);

/// <summary>
/// What a slot holds. <see cref="Condition"/> runs from 0 to 1, or is negative for items without one;
/// <see cref="Ammo"/> is a weapon's loaded rounds, or negative for anything else.
/// </summary>
public readonly record struct ItemStack(string Key, int Amount, float Condition = -1f, int Ammo = -1)
{
    /// <summary>A blueprint's item: what it teaches (G7), drawn as that item in blueprint blue; null for anything else.</summary>
    public string? Teaches { get; init; }

    public bool IsEmpty => string.IsNullOrEmpty(Key) || Amount <= 0;

    public bool HasCondition => Condition >= 0;

    public bool HasAmmo => Ammo >= 0;
}

/// <summary>A line of the item panel's INFORMATION column, such as "Instant health" and "+5".</summary>
public sealed record ItemStat(string Label, string Value, bool Positive = true);

/// <summary>How the interface names and describes an item key.</summary>
public sealed record ItemInfo(string Key, string Name, string Description, string Category, int StackSize = 1,
    bool Wearable = false, bool Usable = false, IReadOnlyList<ItemStat>? Stats = null)
{
    public IReadOnlyList<ItemStat> Stats { get; init; } = Stats ?? Array.Empty<ItemStat>();
}

/// <summary>
/// The kinds of container the loot panel lays out, each in its own way. Slot order: a box holds any number; a body
/// has 0–23 main, 24–29 belt and 30–36 clothing; a furnace 0 fuel, 1–2 input and 3–5 output; a campfire 0 fuel and
/// 1–4 cooking.
/// </summary>
public enum ContainerKind : byte
{
    Box,
    Corpse,
    Furnace,
    Campfire,

    /// <summary>G7: a recycler, 0–5 input and 6–11 output.</summary>
    Recycler,

    /// <summary>G7: a research table, 0 the item, 1 salvage, 2 the blueprint.</summary>
    ResearchTable,

    /// <summary>G7: a world crate, take only.</summary>
    Crate,
}

/// <summary>The container the player has open.</summary>
public sealed record LootView(ContainerKind Kind, string Title, IReadOnlyList<ItemStack> Slots)
{
    /// <summary>Furnace or campfire: burning.</summary>
    public bool IsOn { get; init; }

    /// <summary>Furnace or campfire: progress of what is being smelted or cooked, 0 to 1.</summary>
    public float Progress { get; init; }

    /// <summary>Furnace or campfire: seconds the fuel lasts.</summary>
    public float FuelSeconds { get; init; }

    /// <summary>Research table: the item being researched (or about to be), the salvage it costs, and why it can't start (null: it can).</summary>
    public string? Researching { get; init; }

    public int Cost { get; init; }

    public string? Reason { get; init; }
}

/// <summary>Building rights where the player stands.</summary>
public enum Privilege : byte
{
    None,
    Allowed,
    Blocked,
}

/// <summary>
/// The player's survival state. Health runs to 100, calories to 500 and hydration to 250 (Rust's maxima, as in
/// RustMVP). Temperature is what the body feels in °C (air, clothing and fires); bleeding is health lost per second.
/// </summary>
public sealed record Vitals(float Health, float Calories, float Hydration, float Temperature = 20, float Comfort = 0,
    float Bleeding = 0, float Wetness = 0, Privilege Building = Privilege.None)
{
    public const float MaxHealth = 100, MaxCalories = 500, MaxHydration = 250;

    public bool Cold => Temperature < 5;

    public bool Hot => Temperature > 40;
}

/// <summary>A weapon's loaded rounds and the matching rounds in the inventory.</summary>
public readonly record struct HeldAmmo(int Loaded, int Reserve, bool Reloading = false);

/// <summary>An ingredient of a recipe.</summary>
public readonly record struct Ingredient(string Key, int Amount);

/// <summary>A blueprint the player knows: what it makes, from what, how long it takes and at which workbench.</summary>
public sealed record Recipe(string Key, string Category, float Seconds, IReadOnlyList<Ingredient> Ingredients,
    int Yield = 1, int Workbench = 0);

/// <summary>A crafting category and how many recipes it holds.</summary>
public readonly record struct CraftCategory(string Name, int Count);

/// <summary>A job in the crafting queue. The first one is being made; <see cref="Remaining"/> counts down.</summary>
public sealed record CraftJob(int Ticket, string Key, int Count, float Remaining, float Seconds);

/// <summary>A sleeping bag or bed to respawn at, with its cooldown and its place on the map (0–1 across and down).</summary>
public sealed record RespawnBag(string Id, string Name, float Cooldown, float MapX, float MapY);

/// <summary>
/// How the player died. <see cref="KilledBy"/> is a player's name or a cause ("Cold", "Fall"); <see cref="Weapon"/>
/// an item key, or null. The map position runs 0–1 across and down.
/// </summary>
public sealed record DeathInfo(float AliveSeconds, string KilledBy, string? Weapon, float Distance, float MapX, float MapY);

/// <summary>The interaction prompt: "[E]  OPEN", with "Hold [E] for more options" when there are more.</summary>
public sealed record InteractionPrompt(string Key, string Action, bool MoreOptions = false);

/// <summary>A chat message.</summary>
public sealed record ChatLine(string From, string Text, bool FromServer = false)
{
    /// <summary>A line to the player's team alone (G7).</summary>
    public bool Team { get; init; }
}

/// <summary>The radial wheels: the building plan's pieces and the hammer's actions.</summary>
public enum WheelKind : byte
{
    Plan,
    Hammer,
}

/// <summary>
/// A sector of a wheel: its outline glyph (<c>WheelGlyphs</c>), its title, a line of description, the cost line
/// ("25 x WOOD (915)") and, when it can't be chosen, why ("Not enough wood").
/// </summary>
public sealed record WheelChoice(int Glyph, string Title, string Description, string? Cost = null, string? Reason = null);
