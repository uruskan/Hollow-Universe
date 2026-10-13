using System;
using System.Collections.Generic;

namespace HollowUniverse.Characters;

/// <summary>The seven wear slots (GROUND.md §6: 24 main, 6 belt, 7 wear).</summary>
public enum WearSlot
{
    Head,
    Face,
    Torso,
    TorsoArmour,
    Legs,
    Hands,
    Feet,
}

/// <summary>A piece of clothing or armour: what it is called, where it goes, which meshes draw it, what it hides.</summary>
public sealed class Wearable
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required WearSlot Slot { get; init; }

    /// <summary>Mesh name stem in clothing.glb (meshes are "stem_lod0" … "stem_lod2").</summary>
    public required string Mesh { get; init; }

    /// <summary>The item key (data/ground/items.json) this garment is the worn look of; empty for the viewer's own.</summary>
    public string ItemKey { get; init; } = "";

    /// <summary>Body regions this covers completely; they are not drawn underneath.</summary>
    public BodyRegions Hides { get; init; }

    /// <summary>Hides the hair (anything worn on the scalp).</summary>
    public bool HidesHair { get; init; }

    /// <summary>Drawn in first person (sleeves, gloves).</summary>
    public bool OnArms { get; init; }

    /// <summary>
    /// Another mesh stem to draw instead while something of a given slot is worn under it: the hood is cut wider round
    /// the face over a metal mask (<c>woven_hood_masked</c>).
    /// </summary>
    public (WearSlot Under, string Mesh)? Over { get; init; }
}

/// <summary>
/// Every wearable, the item each one is, and the outfits the viewer cycles through (GROUND.md §4.8). Names are our own
/// (GROUND.md §1). Everyone starts in underwear, which is part of the body.
/// <para>
/// Layering, from the skin out: trousers and gloves (a glove's cuff goes under the sleeve), the shirt, boots, the chest
/// plate, the metal mask, the hood (over the mask, and it hides the hair). An outer garment hides the parts of inner
/// ones in the body regions it covers completely, so nothing pokes through.
/// </para>
/// </summary>
public static class Wardrobe
{
    /// <summary>A knit cap: the first headwear, kept for the viewer's outfits.</summary>
    public static readonly Wearable KnitCap = new()
    {
        Id = "knit_cap", Name = "Knit cap", Slot = WearSlot.Head, Mesh = "knit_cap",
        Hides = BodyRegions.Scalp, HidesHair = true,
    };

    /// <summary>The woven hood (<c>woven-hood</c>): rough cloth gathered round the face, falling onto the shoulders.</summary>
    public static readonly Wearable WovenHood = new()
    {
        Id = "woven_hood", Name = "Woven hood", Slot = WearSlot.Head, Mesh = "woven_hood", ItemKey = "woven-hood",
        Hides = BodyRegions.Scalp | BodyRegions.Neck, HidesHair = true,
        Over = (WearSlot.Face, "woven_hood_masked"),
    };

    /// <summary>The metal mask (<c>metal-mask</c>): a riveted, welded face plate with an eye slit, and a steel band
    /// over the crown.</summary>
    public static readonly Wearable MetalMask = new()
    {
        Id = "metal_mask", Name = "Metal mask", Slot = WearSlot.Face, Mesh = "metal_mask", ItemKey = "metal-mask",
        HidesHair = true,
    };

    public static readonly Wearable WovenShirt = new()
    {
        Id = "woven_shirt", Name = "Woven shirt", Slot = WearSlot.Torso, Mesh = "woven_shirt", ItemKey = "woven-shirt",
        Hides = BodyRegions.Chest | BodyRegions.Belly | BodyRegions.UpperArm | BodyRegions.Forearm, OnArms = true,
    };

    public static readonly Wearable WovenTrousers = new()
    {
        Id = "woven_trousers", Name = "Woven trousers", Slot = WearSlot.Legs, Mesh = "woven_trousers",
        ItemKey = "woven-trousers",
        Hides = BodyRegions.Pelvis | BodyRegions.Thigh | BodyRegions.Calf | BodyRegions.Ankle,
    };

    /// <summary>Woven gloves (<c>woven-gloves</c>): drawn in first person too.</summary>
    public static readonly Wearable WovenGloves = new()
    {
        Id = "woven_gloves", Name = "Woven gloves", Slot = WearSlot.Hands, Mesh = "woven_gloves", ItemKey = "woven-gloves",
        Hides = BodyRegions.Hand, OnArms = true,
    };

    /// <summary>Laced boots: the work boots' (<c>work-boots</c>) look.</summary>
    public static readonly Wearable LacedBoots = new()
    {
        Id = "laced_boots", Name = "Laced boots", Slot = WearSlot.Feet, Mesh = "laced_boots", ItemKey = "work-boots",
        Hides = BodyRegions.Foot | BodyRegions.Ankle,
    };

    /// <summary>The riveted breastplate: the chest plate's (<c>chest-plate</c>) look.</summary>
    public static readonly Wearable RivetedBreastplate = new()
    {
        Id = "riveted_breastplate", Name = "Riveted breastplate", Slot = WearSlot.TorsoArmour, Mesh = "riveted_breastplate",
        ItemKey = "chest-plate",
    };

    public static readonly IReadOnlyList<Wearable> All = new[]
    {
        KnitCap, WovenHood, MetalMask, WovenShirt, WovenTrousers, WovenGloves, LacedBoots, RivetedBreastplate,
    };

    /// <summary>Outfits for the character viewer.</summary>
    public static readonly IReadOnlyList<(string Name, Wearable[] Items)> Outfits = new (string, Wearable[])[]
    {
        ("Underwear", Array.Empty<Wearable>()),
        ("Shirt and trousers", new[] { WovenShirt, WovenTrousers }),
        ("Work clothes", new[] { KnitCap, WovenShirt, WovenTrousers, LacedBoots }),
        ("Work clothes and breastplate", new[] { KnitCap, WovenShirt, WovenTrousers, LacedBoots, RivetedBreastplate }),
        ("Breastplate on bare skin", new[] { WovenTrousers, LacedBoots, RivetedBreastplate }),
        ("Hood, gloves and work clothes", new[] { WovenHood, WovenShirt, WovenTrousers, WovenGloves, LacedBoots }),
        ("Hood over a mask, plate and gloves", new[] { WovenHood, MetalMask, WovenShirt, WovenTrousers, WovenGloves, LacedBoots, RivetedBreastplate }),
        ("Mask, plate and gloves", new[] { MetalMask, WovenShirt, WovenTrousers, WovenGloves, LacedBoots, RivetedBreastplate }),
        ("Hood on bare skin", new[] { WovenHood, WovenTrousers }),
    };

    private static readonly Dictionary<string, Wearable[]> ByItem = Index();

    /// <summary>
    /// The garments a worn item shows as (items.json keys: woven-hood, woven-shirt, woven-trousers, woven-gloves,
    /// work-boots, chest-plate, metal-mask); empty for keys that aren't worn.
    /// </summary>
    public static IReadOnlyList<Wearable> ForItem(string itemKey) =>
        itemKey != null && ByItem.TryGetValue(itemKey, out Wearable[]? garments) ? garments : Array.Empty<Wearable>();

    /// <summary>An outfit from worn item keys (for <see cref="CharacterView.Wear"/> and <see cref="ViewmodelView.Wear"/>).</summary>
    public static List<Wearable> ForItems(IEnumerable<string> itemKeys)
    {
        var outfit = new List<Wearable>();
        foreach (string key in itemKeys)
        {
            outfit.AddRange(ForItem(key));
        }
        return outfit;
    }

    public static Wearable? Find(string id)
    {
        foreach (Wearable w in All)
        {
            if (w.Id == id)
            {
                return w;
            }
        }
        return null;
    }

    /// <summary>
    /// Which garment goes over which, from the skin out: trousers and gloves (their cuffs go under the sleeves), the
    /// shirt, boots, armour, the mask, the hood.
    /// </summary>
    public static int Layer(Wearable w) => w.Slot switch
    {
        WearSlot.Legs => 1,
        WearSlot.Hands => 1,
        WearSlot.Torso => 2,
        WearSlot.Feet => 3,
        WearSlot.TorsoArmour => 4,
        WearSlot.Face => 5,
        WearSlot.Head => 6,
        _ => 2,
    };

    private static Dictionary<string, Wearable[]> Index()
    {
        var index = new Dictionary<string, List<Wearable>>(StringComparer.Ordinal);
        foreach (Wearable w in All)
        {
            if (w.ItemKey.Length == 0)
            {
                continue;
            }
            if (!index.TryGetValue(w.ItemKey, out List<Wearable>? list))
            {
                index[w.ItemKey] = list = new List<Wearable>();
            }
            list.Add(w);
        }
        var result = new Dictionary<string, Wearable[]>(StringComparer.Ordinal);
        foreach ((string key, List<Wearable> list) in index)
        {
            result[key] = list.ToArray();
        }
        return result;
    }
}
