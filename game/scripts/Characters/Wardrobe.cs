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

    /// <summary>Body regions this covers completely; they are not drawn underneath.</summary>
    public BodyRegions Hides { get; init; }

    /// <summary>Hides the hair (anything worn on the scalp).</summary>
    public bool HidesHair { get; init; }

    /// <summary>Drawn in first person (sleeves, gloves).</summary>
    public bool OnArms { get; init; }
}

/// <summary>
/// Every wearable and the outfits the viewer cycles through. Names are our own (GROUND.md §1). Everyone starts in
/// underwear, which is part of the body.
/// </summary>
public static class Wardrobe
{
    public static readonly Wearable KnitCap = new()
    {
        Id = "knit_cap", Name = "Knit cap", Slot = WearSlot.Head, Mesh = "knit_cap",
        Hides = BodyRegions.Scalp, HidesHair = true,
    };

    public static readonly Wearable WovenShirt = new()
    {
        Id = "woven_shirt", Name = "Woven shirt", Slot = WearSlot.Torso, Mesh = "woven_shirt",
        Hides = BodyRegions.Chest | BodyRegions.Belly | BodyRegions.UpperArm | BodyRegions.Forearm, OnArms = true,
    };

    public static readonly Wearable WovenTrousers = new()
    {
        Id = "woven_trousers", Name = "Woven trousers", Slot = WearSlot.Legs, Mesh = "woven_trousers",
        Hides = BodyRegions.Pelvis | BodyRegions.Thigh | BodyRegions.Calf | BodyRegions.Ankle,
    };

    public static readonly Wearable LacedBoots = new()
    {
        Id = "laced_boots", Name = "Laced boots", Slot = WearSlot.Feet, Mesh = "laced_boots",
        Hides = BodyRegions.Foot | BodyRegions.Ankle,
    };

    public static readonly Wearable RivetedBreastplate = new()
    {
        Id = "riveted_breastplate", Name = "Riveted breastplate", Slot = WearSlot.TorsoArmour, Mesh = "riveted_breastplate",
    };

    public static readonly IReadOnlyList<Wearable> All = new[] { KnitCap, WovenShirt, WovenTrousers, LacedBoots, RivetedBreastplate };

    /// <summary>Outfits for the character viewer.</summary>
    public static readonly IReadOnlyList<(string Name, Wearable[] Items)> Outfits = new (string, Wearable[])[]
    {
        ("Underwear", System.Array.Empty<Wearable>()),
        ("Shirt and trousers", new[] { WovenShirt, WovenTrousers }),
        ("Work clothes", new[] { KnitCap, WovenShirt, WovenTrousers, LacedBoots }),
        ("Work clothes and breastplate", new[] { KnitCap, WovenShirt, WovenTrousers, LacedBoots, RivetedBreastplate }),
        ("Breastplate on bare skin", new[] { WovenTrousers, LacedBoots, RivetedBreastplate }),
    };

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
}
