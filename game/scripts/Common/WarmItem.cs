using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.Common;

/// <summary>
/// One thing the game can draw, for the renderer's warm-up (GROUND.md §4.3.1): the files it is made of, loaded on Godot's
/// loader threads from the moment a game starts, and how to show it once, through its library's own drawing path, while
/// the loading screen covers the world (so the renderer builds its pipelines then, not in the middle of play).
/// <see cref="Show"/> returns the node it made (placed in the holder and freed with it) or null when it showed itself
/// another way (an effect played, a gib set drawn). A library lists its items from its own catalogue: new content is
/// warmed without a line of code.
/// </summary>
public sealed record WarmItem(string Name, IReadOnlyList<string> Files, Func<WarmSpot, Node3D?> Show)
{
    /// <summary>Something with nothing to load first.</summary>
    public WarmItem(string name, Func<WarmSpot, Node3D?> show) : this(name, Array.Empty<string>(), show)
    {
    }

    /// <summary>Files to have in memory before play, with nothing of their own to draw (another item draws them).</summary>
    public static WarmItem Load(string name, IReadOnlyList<string> files) => new(name, files, _ => null);
}

/// <summary>
/// Where a warm item shows: the holder (before the camera, tiny, drawn with the world), the point in it to put the item at
/// (each item gets its own), and that point in the world.
/// </summary>
public readonly record struct WarmSpot(Node3D Holder, Vector3 Place, Vector3 World, Camera3D Camera);
