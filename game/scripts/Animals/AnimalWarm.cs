using System.Collections.Generic;
using System.Linq;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.Animals;

/// <summary>The animals' part of the renderer's warm-up (GROUND.md §4.3.1, §6.9): every species' model, skinned, as drawn.</summary>
public static class AnimalWarm
{
    public static IEnumerable<WarmItem> Items(IEnumerable<string> species) =>
        species.Select(key => new WarmItem($"animal {key}", new[] { AnimalAssets.Folder + key + ".glb" },
            _ => AnimalAssets.Get(key)?.Scene.Instantiate<Node3D>()));
}
