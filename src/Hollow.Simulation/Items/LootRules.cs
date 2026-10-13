using Hollow.Simulation.Content;

namespace Hollow.Simulation.Items;

/// <summary>What is being looted, which decides what may be put in.</summary>
public enum LootKind : byte
{
    None,

    /// <summary>A storage box: anything may be put in.</summary>
    Box,

    /// <summary>A campfire or furnace: fuel and cookables into their slots.</summary>
    Device,

    /// <summary>A dead player's body: take only.</summary>
    Corpse,

    /// <summary>A sleeping player: take only (their main slots, belt and clothing).</summary>
    Sleeper,

    /// <summary>Take only, gone once empty: a world crate (G7), a bag of dropped items (GROUND.md §6.1).</summary>
    Crate,
}

/// <summary>
/// Rules about looting that the server applies and the client predicts the same way: how a container is looted, what
/// takes deposits, and where a quick move sends an item.
/// </summary>
public static class LootRules
{
    /// <summary>How a container lying or standing in the world is looted, from what kind of container it is.</summary>
    public static LootKind KindOf(Container container) => container.Kind switch
    {
        ContainerKind.Box => LootKind.Box,
        ContainerKind.Device => LootKind.Device,
        ContainerKind.Corpse => LootKind.Corpse,
        ContainerKind.Crate => LootKind.Crate,
        _ => LootKind.None,
    };

    public static bool CanDeposit(LootKind kind) => kind is LootKind.Box or LootKind.Device;

    public static bool IsLoot(ContainerRef which) => which is ContainerRef.Loot or ContainerRef.LootBelt or ContainerRef.LootWear;

    /// <summary>
    /// Where a quick move (right-click) from a container sends an item, in order of preference: into the open box or
    /// device; clothing to the clothing slots; main to belt and belt to main; clothing and loot back to main, then belt.
    /// </summary>
    public static ContainerRef[] QuickTargets(ContainerRef from, ItemDef item, LootKind loot) => from switch
    {
        ContainerRef.Main or ContainerRef.Belt when CanDeposit(loot) => new[] { ContainerRef.Loot },
        ContainerRef.Main or ContainerRef.Belt when item.Wear != null => new[] { ContainerRef.Wear },
        ContainerRef.Main => new[] { ContainerRef.Belt },
        ContainerRef.Belt => new[] { ContainerRef.Main },
        _ => new[] { ContainerRef.Main, ContainerRef.Belt },
    };
}
