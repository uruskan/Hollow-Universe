using System.Collections.Generic;
using Hollow.Simulation.Content;
using HollowUniverse.Characters;

namespace HollowUniverse.Ground;

/// <summary>
/// How items look on a character (GROUND.md §4.8): the held model and pose an items.json key takes in the hands (the
/// model catalogue's held poses, through the character package's <see cref="HeldItems"/>), and which garment a worn
/// item is. Every item that does something in the hands has a held pose (the model catalogue's test checks it); one
/// without shows empty hands.
/// </summary>
public static class ItemLooks
{
    /// <summary>The held model of an item key, and the pose it takes; null when the hands show nothing.</summary>
    public static (HeldKind Kind, string? Model)? Held(string key) => HeldItems.Find(key) is { } model ? (model.Kind, model.Id) : null;

    /// <summary>The garment a worn item shows as, or null (no model yet).</summary>
    public static Wearable? Worn(string key) => Wardrobe.ForItem(key) is { Count: > 0 } worn ? worn[0] : null;

    /// <summary>The garments of worn item IDs.</summary>
    public static List<Wearable> Outfit(GameContent content, IEnumerable<ushort> itemIds)
    {
        var outfit = new List<Wearable>();
        foreach (ushort id in itemIds)
        {
            if (content.Item(id) is { } item && Worn(item.Key) is { } w)
            {
                outfit.Add(w);
            }
        }
        return outfit;
    }
}
