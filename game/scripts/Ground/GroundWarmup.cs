using System.Collections.Generic;
using System.Linq;
using Hollow.Simulation.Content;
using HollowUniverse.Animals;
using HollowUniverse.Audio;
using HollowUniverse.Build;
using HollowUniverse.Characters;
using HollowUniverse.Common;
using HollowUniverse.Fx;
using HollowUniverse.Gibs;
using HollowUniverse.Items;
using HollowUniverse.Ships;
using HollowUniverse.Surface;

namespace HollowUniverse.Ground;

/// <summary>
/// Everything the ground game can draw, for the renderer's warm-up (GROUND.md §4.3.1): each library's items, read from its
/// own catalogue (models.json, the wardrobe, building.json's art, the gib files, fx.json, animals.json, ships.json), and
/// the files it reads without drawing them (the ground's textures, sounds.json's sounds). A
/// library that starts drawing something new adds its source here, once; new content in its catalogue needs nothing.
/// </summary>
public static class GroundWarmup
{
    /// <summary>
    /// The items, those the world's opening reads first (the ground's textures, the bodies, the building's art), so their
    /// files are in by the time the server welcomes the player; then the rest; the sounds, many small files, last.
    /// </summary>
    public static IEnumerable<WarmItem> Items(GameContent content) =>
        // The ground's layers are put into their arrays here, under the loading screen (GroundLayers reads them back from
        // the GPU once; held by the warm-up as files, they would stay in memory twice).
        new[] { new WarmItem("the ground's textures", _ => { GroundLayers.Warm(); return null; }) }
            .Concat(CharacterWarm.Items())
            .Concat(BuildingWarm.Items(content.Building))
            .Concat(ModelWarm.Items())
            .Concat(GibWarm.Items())
            .Concat(FxWarm.Items())
            .Concat(AnimalWarm.Items(content.Animals.Select(a => a.Key)))
            .Concat(ShipWarm.Items(content.Ships))
            .Append(new WarmItem("weak spots", _ => NatureView.Warmup(content)))
            .Append(WarmItem.Load("sounds", SoundLibrary.Files));
}
