using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Hollow.Simulation.Content;

/// <summary>
/// A part of an item (GROUND_SYSTEMS.md §7): a block of its data that gives it behaviour (items.json's <c>melee</c>,
/// weapons.json's firearm, explosives.json's throwable, world.json's blueprint, ...). An item has any number of parts;
/// each part has, in registries found by its number, at most one held behaviour (<see cref="HeldUse"/>: what the item
/// does in the hands), a describer (its rows in the item panel's INFORMATION) and the inventory actions it offers. Not on
/// the wire and never saved: an item's parts are worked out from its data when the data is read
/// (<see cref="ItemPartTable"/>), so the numbers may change.
/// </summary>
public enum ItemPart : byte
{
    /// <summary>items.json <c>melee</c>: swung (tools, blades, spears).</summary>
    Melee,

    /// <summary>items.json <c>bow</c>: drawn and loosed.</summary>
    Bow,

    /// <summary>items.json <c>ammo</c>: an arrow a bow shoots.</summary>
    Arrow,

    /// <summary>items.json <c>consume</c>: eaten (food) or used (medicine).</summary>
    Consumable,

    /// <summary>items.json <c>wear</c>: clothing and armour.</summary>
    Wearable,

    /// <summary>items.json <c>fuel</c>: burns in a fire or furnace.</summary>
    Fuel,

    /// <summary>items.json <c>cook</c>: smelted or cooked into another item.</summary>
    Cookable,

    /// <summary>items.json <c>deploys</c>: placed as a deployable (deployables.json).</summary>
    Deployable,

    /// <summary>items.json <c>ship</c>: a ship (ships.json).</summary>
    Ship,

    /// <summary>weapons.json <c>weapons</c>: a firearm.</summary>
    Firearm,

    /// <summary>weapons.json <c>ammo</c>: a firearm's round.</summary>
    Round,

    /// <summary>weapons.json <c>attachments</c>: a sight, a muzzle part or a light.</summary>
    Attachment,

    /// <summary>explosives.json <c>throwables</c>: a charge or grenade, thrown or placed by hand.</summary>
    Throwable,

    /// <summary>items.json <c>building: plan</c>: the building plan.</summary>
    Plan,

    /// <summary>items.json <c>building: hammer</c>.</summary>
    Hammer,

    /// <summary>items.json <c>building: claim</c>: the tool cupboard (building.json <c>claim</c>).</summary>
    Cupboard,

    /// <summary>items.json <c>building: key-lock</c> or <c>code-lock</c>: a lock fitted to a door.</summary>
    Lock,

    /// <summary>items.json <c>building: key</c>: a key cut for a key lock.</summary>
    Key,

    /// <summary>items.json <c>building: door</c> and <c>door</c>: a door (building.json <c>doors</c>).</summary>
    Door,

    /// <summary>world.json <c>blueprint</c>: a blueprint, whose stack's condition names the recipe it teaches.</summary>
    Blueprint,

    /// <summary>recipes.json ingredients and world.json <c>recycle</c>: what other items are made of.</summary>
    Component,
}

/// <summary>
/// What an item does in the hands (GROUND_SYSTEMS.md §7): the held behaviour one of its parts gives, at most one per item
/// (checked when the data is read). Each has one handler in a registry on each side: on the client what the mouse
/// buttons and R do and the radials they open, on the server its per-tick buttons. <see cref="None"/>: the hands hold
/// it and do nothing (resources, clothing, components).
/// </summary>
public enum HeldUse : byte
{
    None,

    /// <summary>Swung while the attack button is held (<see cref="ItemPart.Melee"/>).</summary>
    Melee,

    /// <summary>Drawn with the aim button and loosed with the attack button (<see cref="ItemPart.Bow"/>).</summary>
    Bow,

    /// <summary>Fired, aimed down the sights, reloaded and unloaded with R (<see cref="ItemPart.Firearm"/>).</summary>
    Firearm,

    /// <summary>Thrown, or put on a surface or lobbed with the aim button (<see cref="ItemPart.Throwable"/>).</summary>
    Throwable,

    /// <summary>Placed where the aim is, R turning it: deployables, doors, locks, the tool cupboard, ships.</summary>
    Placer,

    /// <summary>The building plan: places the piece chosen in its radial (the aim button), R turning it.</summary>
    Planner,

    /// <summary>The hammer: repairs, and its radial upgrades, turns and demolishes.</summary>
    Hammer,

    /// <summary>Eaten or used (<see cref="ItemPart.Consumable"/>).</summary>
    Consumable,

    /// <summary>Learnt (<see cref="ItemPart.Blueprint"/>).</summary>
    Blueprint,
}

/// <summary>The parts of an item (GROUND_SYSTEMS.md §7), as a set: bit <c>i</c> is <see cref="ItemPart"/> <c>i</c>.</summary>
public readonly record struct ItemParts(uint Bits)
{
    public static readonly ItemParts None = default;

    public bool Has(ItemPart part) => (Bits & 1u << (int)part) != 0;

    public ItemParts With(ItemPart part) => new(Bits | 1u << (int)part);

    public bool IsEmpty => Bits == 0;

    /// <summary>The parts, in <see cref="ItemPart"/>'s order, without allocating.</summary>
    public Enumerator GetEnumerator() => new(Bits);

    public struct Enumerator(uint bits)
    {
        private uint _left = bits;

        public ItemPart Current { get; private set; }

        public bool MoveNext()
        {
            if (_left == 0)
            {
                return false;
            }
            Current = (ItemPart)BitOperations.TrailingZeroCount(_left);
            _left &= _left - 1;
            return true;
        }
    }

    public override string ToString()
    {
        var names = new List<string>();
        foreach (ItemPart part in this)
        {
            names.Add(part.ToString());
        }
        return string.Join(", ", names);
    }
}

/// <summary>
/// Every item part (GROUND_SYSTEMS.md §7) in one table: where its data is declared, the held behaviour it gives, and
/// whether an item has it. When the data has been read (every file linked), each item gets its parts and its one held
/// behaviour from here; an item two of whose parts would give one is a problem in the data.
/// </summary>
internal static class ItemPartTable
{
    /// <summary>One part: its declaration in the data (for problems and the docs), its held behaviour, and the test.</summary>
    public sealed record Row(ItemPart Part, string Declared, HeldUse Held, Func<ItemDef, WorldData, bool> Has);

    public static readonly Row[] Rows =
    {
        new(ItemPart.Melee, "items.json melee", HeldUse.Melee, (i, _) => i.Melee != null),
        new(ItemPart.Bow, "items.json bow", HeldUse.Bow, (i, _) => i.Bow != null),
        new(ItemPart.Arrow, "items.json ammo", HeldUse.None, (i, _) => i.Ammo != null),
        new(ItemPart.Consumable, "items.json consume", HeldUse.Consumable, (i, _) => i.Consume != null),
        new(ItemPart.Wearable, "items.json wear", HeldUse.None, (i, _) => i.Wear != null),
        new(ItemPart.Fuel, "items.json fuel", HeldUse.None, (i, _) => i.Fuel != null),
        new(ItemPart.Cookable, "items.json cook", HeldUse.None, (i, _) => i.Cook != null),
        new(ItemPart.Deployable, "items.json deploys", HeldUse.Placer, (i, _) => i.Deploys != null),
        new(ItemPart.Ship, "items.json ship", HeldUse.Placer, (i, _) => i.Ship != null),
        new(ItemPart.Firearm, "weapons.json weapons", HeldUse.Firearm, (i, _) => i.Firearm != null),
        new(ItemPart.Round, "weapons.json ammo", HeldUse.None, (i, _) => i.Bullet != null),
        new(ItemPart.Attachment, "weapons.json attachments", HeldUse.None, (i, _) => i.Attachment != null),
        new(ItemPart.Throwable, "explosives.json throwables", HeldUse.Throwable, (i, _) => i.Throwable != null),
        new(ItemPart.Plan, "items.json building: plan", HeldUse.Planner, (i, _) => i.Building == BuildingRole.Plan),
        new(ItemPart.Hammer, "items.json building: hammer", HeldUse.Hammer, (i, _) => i.Building == BuildingRole.Hammer),
        new(ItemPart.Cupboard, "items.json building: claim", HeldUse.Placer, (i, _) => i.Building == BuildingRole.Claim),
        new(ItemPart.Lock, "items.json building: key-lock, code-lock", HeldUse.Placer, (i, _) => i.Building is BuildingRole.KeyLock or BuildingRole.CodeLock),
        new(ItemPart.Key, "items.json building: key", HeldUse.None, (i, _) => i.Building == BuildingRole.Key),
        new(ItemPart.Door, "items.json building: door", HeldUse.Placer, (i, _) => i.Building == BuildingRole.Door),
        new(ItemPart.Blueprint, "world.json blueprint", HeldUse.Blueprint, (i, w) => w.Blueprint == i),
        new(ItemPart.Component, "recipes.json ingredients, world.json recycle", HeldUse.None, (i, w) => i.UsedIn.Count > 0 || w.FixedYield(i.Id) != null),
    };

    /// <summary>A part's row (the table lists the parts in their order; a test checks it).</summary>
    public static Row Of(ItemPart part) => Rows[(int)part];

    /// <summary>
    /// Gives every item its parts and its held behaviour, once the files are linked; what uses each item (recipes that
    /// take it) first. Two parts that would each give one held behaviour are a problem: an item does one thing in the hands.
    /// </summary>
    public static void Assign(GameContent.Loaded loaded, List<string> problems)
    {
        var usedIn = new Dictionary<ItemDef, List<Recipe>>();
        foreach (Recipe recipe in loaded.Recipes)
        {
            foreach (ItemAmount ingredient in recipe.Ingredients)
            {
                if (!usedIn.TryGetValue(ingredient.Item, out List<Recipe>? list))
                {
                    usedIn[ingredient.Item] = list = new List<Recipe>();
                }
                if (!list.Contains(recipe))
                {
                    list.Add(recipe);
                }
            }
        }
        WorldData world = loaded.World ?? new WorldData();
        foreach (ItemDef item in loaded.Items)
        {
            item.UsedIn = usedIn.TryGetValue(item, out List<Recipe>? recipes) ? recipes : Array.Empty<Recipe>();
            ItemParts parts = ItemParts.None;
            Row? held = null;
            foreach (Row row in Rows)
            {
                if (!row.Has(item, world))
                {
                    continue;
                }
                parts = parts.With(row.Part);
                if (row.Held == HeldUse.None)
                {
                    continue;
                }
                if (held != null)
                {
                    problems.Add($"items.json: \"{item.Key}\": its {held.Declared} and its {row.Declared} would both say what it does in " +
                        "the hands (an item has one held behaviour, GROUND_SYSTEMS.md §7)");
                    continue;
                }
                held = row;
            }
            item.Parts = parts;
            item.Held = held?.Held ?? HeldUse.None;
            item.HeldPart = held?.Part;
        }
    }
}
