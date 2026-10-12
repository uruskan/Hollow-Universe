using System;
using System.Collections.Generic;

namespace HollowUniverse.UI.Model;

/// <summary>
/// The ground game's view model (GROUND.md §8): everything the HUD, inventory, crafting, death and wheel screens show,
/// and every action they take, as plain calls. The interface never talks to the network; an adapter over the client
/// API implements this, and <see cref="MockGroundUi"/> fakes it with sample data.
/// <para>
/// Reads are polled every frame, so they should be cheap. Actions are requests: the authority answers by changing
/// what the reads return (or by a <see cref="Notice"/> when it refuses).
/// </para>
/// </summary>
public interface IGroundUi
{
    // ---------------------------------------------------------------- reads

    /// <summary>The player's name, shown above the character.</summary>
    string PlayerName { get; }

    /// <summary>Name, description, category, stack size and stats of an item key. Unknown keys get a stand-in.</summary>
    ItemInfo Item(string key);

    Vitals Vitals { get; }

    /// <summary>A container's slots: Main 24, Belt 6, Wear 7, Loot as many as the open container has (none if closed).</summary>
    IReadOnlyList<ItemStack> Slots(ContainerId container);

    /// <summary>The selected belt slot, 0–5, or −1.</summary>
    int ActiveBelt { get; }

    /// <summary>The held weapon's rounds, or null when the held item takes none.</summary>
    HeldAmmo? Ammo { get; }

    /// <summary>The open container, or null.</summary>
    LootView? Loot { get; }

    /// <summary>The crafting categories in display order, each with its number of recipes.</summary>
    IReadOnlyList<CraftCategory> Categories { get; }

    /// <summary>The recipes the player knows.</summary>
    IReadOnlyList<Recipe> Recipes { get; }

    /// <summary>The level of the workbench in reach, 0 for none.</summary>
    int Workbench { get; }

    /// <summary>How many of an item the player has (main and belt), for ingredients' HAVE column.</summary>
    int Have(string key);

    /// <summary>The crafting queue, first job first.</summary>
    IReadOnlyList<CraftJob> Queue { get; }

    /// <summary>How the player died, while dead; null while alive.</summary>
    DeathInfo? Death { get; }

    /// <summary>The player's sleeping bags and beds, while dead.</summary>
    IReadOnlyList<RespawnBag> Bags { get; }

    /// <summary>What pressing the interaction key would do now, or null.</summary>
    InteractionPrompt? Prompt { get; }

    /// <summary>The wheel the held item opens with the right mouse button (the plan's, the hammer's), or null.</summary>
    WheelKind? HeldWheel { get; }

    /// <summary>A wheel's sectors, in clockwise order.</summary>
    IReadOnlyList<WheelChoice> Wheel(WheelKind kind);

    /// <summary>The sector to show when the pointer is in the middle: the plan's selected piece, or −1.</summary>
    int WheelCurrent(WheelKind kind);

    // ---------------------------------------------------------------- events (the HUD's moments)

    /// <summary>The player's shot hit someone (true: a head shot).</summary>
    event Action<bool>? HitConfirmed;

    /// <summary>The player took damage, from this bearing in degrees: 0 ahead, clockwise.</summary>
    event Action<float>? Damaged;

    /// <summary>Items gathered or picked up ("+20 Wood"): item key and amount.</summary>
    event Action<string, int>? Gathered;

    /// <summary>A craft finished: item key and amount.</summary>
    event Action<string, int>? Crafted;

    /// <summary>A short message for the player, such as a refused action.</summary>
    event Action<string>? Notice;

    event Action<ChatLine>? ChatReceived;

    // ---------------------------------------------------------------- actions

    /// <summary>Moves <paramref name="amount"/> of a stack onto another slot: merges, swaps or splits.</summary>
    void Move(SlotRef from, SlotRef to, int amount);

    /// <summary>Splits <paramref name="amount"/> off a stack into a free slot of the same container.</summary>
    void Split(SlotRef slot, int amount);

    /// <summary>Drops <paramref name="amount"/> of a stack on the ground.</summary>
    void Drop(SlotRef slot, int amount);

    /// <summary>Moves a stack to the other side: into the open container and back, or between main and belt.</summary>
    void QuickMove(SlotRef slot);

    /// <summary>Uses an item (a bandage, food).</summary>
    void Use(SlotRef slot);

    void Craft(string key, int amount);

    void CancelCraft(int ticket);

    /// <summary>Respawns at a bag, or on a random beach when <paramref name="bagId"/> is null.</summary>
    void Respawn(string? bagId);

    void SelectBelt(int index);

    /// <summary>Lights or puts out the open furnace or campfire.</summary>
    void SetBurning(bool on);

    /// <summary>Applies a wheel's sector (selects a piece, upgrades, demolishes).</summary>
    void ChooseWheel(WheelKind kind, int index);

    /// <summary>The player closed the loot panel.</summary>
    void CloseLoot();

    void Say(string text);
}
