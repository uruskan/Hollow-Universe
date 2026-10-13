using System.Collections.Generic;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;

namespace Hollow.Simulation.Building;

/// <summary>
/// Building materials as items in containers (GROUND.md §6.3, phase 2): a player's main and belt slots pay for placing,
/// upgrading, repairing and keys; a claim device's store pays its upkeep. A material is the item with its key; it is
/// taken in slot order, all or nothing (<see cref="InventoryRules.Take"/>), so the client's optimistic copy takes the
/// same stacks the server does.
/// </summary>
public sealed class ItemWallet : IBuildingWallet
{
    private readonly GameContent _content;
    private readonly IReadOnlyList<Container> _containers;

    public ItemWallet(GameContent content, IReadOnlyList<Container> containers)
    {
        _content = content;
        _containers = containers;
    }

    public int Count(string material) => _content.Item(material) is { } item ? InventoryRules.Count(_containers, item.Id) : 0;

    public bool Take(string material, int amount) =>
        amount <= 0 || (_content.Item(material) is { } item && InventoryRules.Take(_containers, item.Id, amount));
}
