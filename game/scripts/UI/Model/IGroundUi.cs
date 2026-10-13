using System;
using System.Collections.Generic;

namespace HollowUniverse.UI.Model;

/// <summary>
/// The ground game's view model (GROUND.md §8): everything the HUD, inventory, crafting, death and radial screens show,
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

    /// <summary>
    /// The item panel's INFORMATION for a slot's stack (GROUND_SYSTEMS.md §7): its kind's rows and its own (condition, a
    /// gun's magazine and attachments, what a blueprint teaches). By default the kind's stats, the condition and the rounds.
    /// </summary>
    IReadOnlyList<ItemStat> Rows(SlotRef slot)
    {
        IReadOnlyList<ItemStack> slots = Slots(slot.Container);
        ItemStack stack = slot.Index >= 0 && slot.Index < slots.Count ? slots[slot.Index] : default;
        if (stack.IsEmpty)
        {
            return Array.Empty<ItemStat>();
        }
        var rows = new List<ItemStat>(Item(stack.Key).Stats);
        if (stack.HasCondition)
        {
            rows.Add(new ItemStat("Condition", MathF.Round(stack.Condition * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%",
                stack.Condition >= 0.25f));
        }
        if (stack.HasAmmo)
        {
            rows.Add(new ItemStat("Loaded", stack.Ammo.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return rows;
    }

    Vitals Vitals { get; }

    /// <summary>A container's slots: Main 24, Belt 6, Wear 7, Loot as many as the open container has (none if closed).</summary>
    IReadOnlyList<ItemStack> Slots(ContainerId container);

    /// <summary>The selected belt slot, 0–5, or −1.</summary>
    int ActiveBelt { get; }

    /// <summary>The held weapon's rounds, or null when the held item takes none.</summary>
    HeldAmmo? Ammo { get; }

    /// <summary>The open container, or null.</summary>
    LootView? Loot { get; }

    /// <summary>The player flies a ship (G6): the flight HUD takes the belt's, the vitals' and the crosshair's place.</summary>
    bool Piloting => false;

    /// <summary>A station (a deployable's key, e.g. the vehicle workbench) is within reach, for a recipe made at one.</summary>
    bool StationNear(string station) => true;

    /// <summary>The crafting categories in display order, each with its number of recipes.</summary>
    IReadOnlyList<CraftCategory> Categories { get; }

    /// <summary>The recipes the player knows.</summary>
    IReadOnlyList<Recipe> Recipes { get; }

    /// <summary>The level of the workbench in reach, 0 for none.</summary>
    int Workbench { get; }

    /// <summary>
    /// Seconds one unit of a recipe takes here: its own, shortened by a workbench in reach above its level
    /// (GROUND_SYSTEMS.md §7). The recipe's own by default.
    /// </summary>
    float SecondsHere(Recipe recipe) => recipe.Seconds;

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

    /// <summary>
    /// The held inputs that open a radial menu and what each opens now (GROUND_SYSTEMS.md §4): the right mouse with the
    /// building plan or the hammer in hand, E on what is aimed at (P1), R with a gun (P3). The interface handles every
    /// binding the same way; none by default.
    /// </summary>
    IReadOnlyList<RadialBinding> Radials => Array.Empty<RadialBinding>();

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

    /// <summary>
    /// The item panel's ACTIONS for a slot's stack (GROUND_SYSTEMS.md §7), besides DROP (the panel's own): what its parts
    /// offer (EAT or USE, WEAR or UNEQUIP, LEARN, UNLOAD, REMOVE an attachment), each with why it can't be done now when it
    /// can't. None by default.
    /// </summary>
    IReadOnlyList<ItemActionView> Actions(SlotRef slot) => Array.Empty<ItemActionView>();

    /// <summary>Splits <paramref name="amount"/> off a stack into a free slot of the same container.</summary>
    void Split(SlotRef slot, int amount);

    /// <summary>Drops <paramref name="amount"/> of a stack on the ground.</summary>
    void Drop(SlotRef slot, int amount);

    /// <summary>Moves a stack to the other side: into the open container and back, or between main and belt.</summary>
    void QuickMove(SlotRef slot);

    void Craft(string key, int amount);

    void CancelCraft(int ticket);

    /// <summary>Respawns at a bag, or on a random beach when <paramref name="bagId"/> is null.</summary>
    void Respawn(string? bagId);

    void SelectBelt(int index);

    /// <summary>Lights or puts out the open furnace or campfire.</summary>
    void SetBurning(bool on);

    /// <summary>The player closed the loot panel.</summary>
    void CloseLoot();

    void Say(string text);

    // ---------------------------------------------------------------- G7: the map, tech trees, teams (GROUND.md §6.8)
    // Default members, so a model that doesn't have these yet (the build sandbox's, the menu's) keeps compiling.

    /// <summary>The zone's map picture, or null (the map screen then says it isn't ready).</summary>
    MapView? Map => null;

    /// <summary>What the map shows: the player, teammates, the last death, bags and beds, the player's markers.</summary>
    IReadOnlyList<MapMarkerView> MapMarkers => Array.Empty<MapMarkerView>();

    /// <summary>How many markers the player may put down.</summary>
    int MaxMarkers => 5;

    /// <summary>Puts a marker on the map at a point (0–1 across and down) with a colour (0–5) and a label.</summary>
    void PlaceMarker(float x, float y, int colour, string label)
    {
    }

    /// <summary>Takes one of the player's markers off the map (its index; −1: all).</summary>
    void RemoveMarker(int index)
    {
    }

    /// <summary>The tech tree of the workbench in reach, or null.</summary>
    TechTreeView? TechTree => null;

    /// <summary>Unlocks a tech tree node (its item key) for its salvage.</summary>
    void Unlock(string key)
    {
    }

    /// <summary>The player's team, or null; the invites waiting for them.</summary>
    TeamInfo? Team => null;

    IReadOnlyList<TeamInviteInfo> TeamInvites => Array.Empty<TeamInviteInfo>();

    void CreateTeam()
    {
    }

    void LeaveTeam()
    {
    }

    void AcceptInvite(string team)
    {
    }

    void DeclineInvite(string team)
    {
    }

    void KickFromTeam(string member)
    {
    }

    void PromoteInTeam(string member)
    {
    }

    /// <summary>Says a line to the player's team alone.</summary>
    void SayTeam(string text)
    {
    }

    // ---------------------------------------------------------------- the F1 panel (GROUND.md §8.2)

    /// <summary>The F1 panel's console and tabs; null where there is none (the menu's model), so F1 does nothing.</summary>
    IConsoleModel? Console => null;
}
