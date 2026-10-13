using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.Items;

namespace HollowUniverse.Characters;

/// <summary>
/// The characters' part of the renderer's warm-up (GROUND.md §4.3.1): each body as others see it and as its own arms in
/// first person, wearing every garment of the wardrobe (in as few outfits as the slots allow), and every item of the
/// catalogue that is held, as the first person draws it (its materials' first-person copies).
/// </summary>
public static class CharacterWarm
{
    public static IEnumerable<WarmItem> Items()
    {
        foreach (BodyType body in Enum.GetValues<BodyType>().Where(CharacterAssets.Has))
        {
            IReadOnlyList<string> files = CharacterAssets.Files(body);
            foreach (Wearable[] outfit in Outfits())
            {
                yield return new WarmItem($"{body} body, {outfit.Length} garments", files, spot =>
                {
                    var view = new CharacterView { Name = $"{body}", Body = body };
                    spot.Holder.AddChild(view);
                    view.Wear(outfit);
                    view.Apply(CharacterFrame.Standing(spot.Place, Vector3.Up));
                    return view;
                });
                yield return new WarmItem($"{body} arms, {outfit.Length} garments", files, spot =>
                {
                    var arms = new ViewmodelView { Name = $"{body} arms", Body = body };
                    spot.Holder.AddChild(arms);
                    arms.Wear(outfit);
                    arms.Visible = true;
                    return arms;
                });
            }
        }
        foreach (ModelEntry entry in ModelCatalog.Entries.Where(e => e.Held != null && e.Files.Count > 0))
        {
            yield return new WarmItem($"{entry.Key} in hand", new[] { entry.Files[0] }, _ =>
            {
                Node3D? item = ModelCatalog.Instantiate(entry.Key);
                if (item != null)
                {
                    ViewmodelView.PrepareItem(item);
                }
                return item;
            });
        }
    }

    // Every garment in as few outfits as the slots allow: the n-th outfit wears the n-th garment of each slot.
    private static IEnumerable<Wearable[]> Outfits()
    {
        Wearable[][] bySlot = Wardrobe.All.GroupBy(w => w.Slot).Select(g => g.ToArray()).ToArray();
        int count = bySlot.Length == 0 ? 0 : bySlot.Max(s => s.Length);
        for (int n = 0; n < count; n++)
        {
            yield return bySlot.Where(s => n < s.Length).Select(s => s[n]).ToArray();
        }
    }
}
