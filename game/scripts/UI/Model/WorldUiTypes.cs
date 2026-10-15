using System;
using System.Collections.Generic;
using System.Numerics;

namespace HollowUniverse.UI.Model;

// The values the map, tech tree, team and machine screens show (GROUND.md §6.8, §8, G7). Plain C#, no Godot types: the
// client adapter fills them from the client API (LocalPlayer.World, GameClient.Map), MockGroundUi fakes them.

/// <summary>
/// The map of the zone's whole world (docs/WHOLE_WORLD.md §11): its picture, six cube faces of <see cref="Pixels"/> ×
/// <see cref="Pixels"/> RGBA stacked in <c>CubeSphere</c>'s order, made by <see cref="Render"/> (slow the first time; the
/// interface calls it off the main thread and caches the picture on disk under <see cref="CacheKey"/>); the world's
/// <see cref="Radius"/> in metres; and its grid of <see cref="Cells"/> × <see cref="Cells"/> cells a face, named by
/// <c>WorldGrid</c> ("C K12").
/// </summary>
public sealed record MapView(string CacheKey, int Pixels, Func<byte[]> Render, double Radius, int Cells);

/// <summary>What a map marker is.</summary>
public enum MapMarkerKind : byte
{
    /// <summary>The player, with their facing.</summary>
    Me,

    /// <summary>A teammate: a dot and their name (dim while asleep, red while down or dead).</summary>
    Teammate,

    /// <summary>Where the player died last.</summary>
    Death,
    Bag,
    Bed,

    /// <summary>A marker the player put down: a pin in one of six colours, with a label.</summary>
    Marker,
}

/// <summary>
/// Something on the map where a direction from the world's centre meets the ground (<see cref="At"/>, the body frame;
/// its length doesn't matter): its kind, a label, a facing (degrees clockwise from local north, for
/// <see cref="MapMarkerKind.Me"/> and teammates), a colour (0–5, markers), the index that removes it (markers) and
/// whether it is dim (an asleep teammate).
/// </summary>
public sealed record MapMarkerView(MapMarkerKind Kind, Vector3 At, string Label = "", float Facing = 0, int Colour = 0, int Index = -1,
    bool Dim = false);

/// <summary>How a tech tree node stands for the player.</summary>
public enum TechNodeState : byte
{
    Known,

    /// <summary>Its nodes before it are known: it can be unlocked (if the salvage is there).</summary>
    Open,

    /// <summary>Its nodes before it aren't known yet.</summary>
    Locked,
}

/// <summary>A node of a tech tree: the item it unlocks, its salvage, its row (steps from a root) and the nodes before it.</summary>
public sealed record TechNodeView(string Key, int Cost, int Depth, IReadOnlyList<string> After, TechNodeState State);

/// <summary>The tech tree of the workbench in reach: its level, its nodes in order, the salvage the player has.</summary>
public sealed record TechTreeView(int Workbench, IReadOnlyList<TechNodeView> Nodes, int Salvage);

/// <summary>A member of the player's team.</summary>
public sealed record TeamMemberInfo(string Id, string Name, bool Online, bool Leader, bool Me);

/// <summary>The player's team: its members (oldest first) and whether the player leads it.</summary>
public sealed record TeamInfo(string Id, IReadOnlyList<TeamMemberInfo> Members, bool Leading, int MaxMembers = 8);

/// <summary>An invite to a team waiting for the player: the team, who sent it, seconds left.</summary>
public sealed record TeamInviteInfo(string Team, string From, float Seconds);
