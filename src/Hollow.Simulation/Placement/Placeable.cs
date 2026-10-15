using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Placement;

/// <summary>A material a placement costs (a building piece's twig, from the carried slots).</summary>
public readonly record struct PlacementCost(string Material, int Amount);

/// <summary>
/// Something a player can place (GROUND_SYSTEMS.md §6): an entry of placement.json's append-only list, whose index is its
/// number on the wire. Its kind says what it is and what placing it makes (<see cref="PlaceableKind"/>, by name); its
/// definition is its kind's (a deployable, a building piece, a door, a lock, the claim device, a charge's throwable, a
/// ship); its rule says where and how it goes. Read once with the game data.
/// </summary>
public sealed class Placeable
{
    /// <summary>Its number: its index in placement.json's list, and on the wire.</summary>
    public int Index { get; init; }

    /// <summary>Its definition's key (a deployable's, a piece's, a door's, a lock's, the claim device's, a charge's item's, a ship's).</summary>
    public string Key { get; init; } = "";

    public PlaceableKind Kind { get; init; } = null!;

    public PlacementRule Rule { get; init; } = null!;

    /// <summary>Its kind's definition of it (a <see cref="DeployableDef"/>, a <see cref="PieceDef"/>, a <see cref="DoorDef"/>…).</summary>
    public object Definition { get; init; } = null!;

    /// <summary>The item used up placing it (null: none; a building piece is paid in materials).</summary>
    public ItemDef? Item { get; init; }

    /// <summary>The item that must be carried to place it, and stays (the building plan), or null.</summary>
    public ItemDef? Tool { get; init; }

    /// <summary>Materials taken from the carried slots (a piece's twig).</summary>
    public IReadOnlyList<PlacementCost> Costs { get; init; } = Array.Empty<PlacementCost>();

    /// <summary>What it occupies for the overlap, support and reach checks.</summary>
    public Footprint Footprint { get; init; } = Footprint.Point;

    /// <summary>What the ghost and the placed thing are drawn as (the model catalogue's key, or a kit's piece).</summary>
    public string ModelKey { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>The item a slot must hold to place it: the one used up, else the tool.</summary>
    public ItemDef? SlotItem => Item ?? Tool;

    public override string ToString() => $"{Kind.Name} {Key} (#{Index})";
}

/// <summary>
/// placement.json as read (GROUND_SYSTEMS.md §6): the placeables by their wire numbers, the names of the solvers,
/// rotations, checks and kinds (each with one handler), the checks' fixed order, and which surface each family of
/// building piece offers.
/// </summary>
public sealed class PlacementData
{
    public const string File = "placement.json";

    private readonly Dictionary<object, Placeable> _byDefinition = new(ReferenceEqualityComparer.Instance);

    internal PlacementData(IReadOnlyList<Placeable> placeables, IReadOnlyList<string> solvers, IReadOnlyList<string> rotations,
        IReadOnlyList<string> checks, IReadOnlyList<string> kinds, IReadOnlyDictionary<PieceFamily, PlacementSurfaces> pieceSurfaces)
    {
        Placeables = placeables;
        Solvers = solvers;
        Rotations = rotations;
        Checks = checks;
        Kinds = kinds;
        PieceSurfaces = pieceSurfaces;
        foreach (Placeable p in placeables)
        {
            _byDefinition.TryAdd(p.Definition, p);
        }
    }

    /// <summary>An empty set (no placement.json).</summary>
    public static PlacementData Empty { get; } = new(Array.Empty<Placeable>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), new Dictionary<PieceFamily, PlacementSurfaces>());

    /// <summary>Every placeable, by its number.</summary>
    public IReadOnlyList<Placeable> Placeables { get; }

    public IReadOnlyList<string> Solvers { get; }

    public IReadOnlyList<string> Rotations { get; }

    /// <summary>The checks, in the pipeline's order.</summary>
    public IReadOnlyList<string> Checks { get; }

    public IReadOnlyList<string> Kinds { get; }

    public IReadOnlyDictionary<PieceFamily, PlacementSurfaces> PieceSurfaces { get; }

    /// <summary>The handlers, sealed against the lists (set when the data is read).</summary>
    public PlacementHandlers Handlers { get; internal set; } = null!;

    /// <summary>A placeable by its number (from the wire: untrusted, null when unknown).</summary>
    public Placeable? this[int index] => (uint)index < (uint)Placeables.Count ? Placeables[index] : null;

    /// <summary>The placeable of a definition (a deployable, a piece, a door…), or null.</summary>
    public Placeable? Of(object definition) => _byDefinition.GetValueOrDefault(definition);

    /// <summary>The surface a building piece of a family offers (what stands on it sees it as).</summary>
    public PlacementSurfaces SurfaceOf(PieceFamily family) => PieceSurfaces.GetValueOrDefault(family, PlacementSurfaces.Wall);
}

/// <summary>Who places what, from where: the player, their eye, the placeable, and what they can pay with.</summary>
public readonly struct PlacementContext(ulong player, Vector3 eye, Placeable placeable, IBuildingWallet? wallet)
{
    public ulong Player { get; } = player;

    public Vector3 Eye { get; } = eye;

    public Placeable Placeable { get; } = placeable;

    public PlacementRule Rule => Placeable.Rule;

    /// <summary>The materials the player carries (a piece's price), or null not to count them.</summary>
    public IBuildingWallet? Wallet { get; } = wallet;
}

/// <summary>Where a player aims (the client's every frame): along a direction from the eye, the view's yaw, R pressed so often.</summary>
public readonly record struct PlacementAim(Vector3 Direction, float ViewYaw, int Turns);

/// <summary>
/// What an intent says was placed (PlaceIntent): what holds it (a piece, a door, the ground…), the socket it snaps to, where
/// it goes and its turn, and its variant (a piece's orientation, a door's side, a turn in its support's steps). The server
/// works the candidate out again from it and accepts only a pose within the rule's tolerance of its own. With a ship as
/// its support, the pose is in the ship's frame (IRON_WAKE.md §8): its base there and its turn about the ship's up.
/// </summary>
public readonly record struct PlacementClaim(TargetRef Support, int Socket, Vector3 Position, float Yaw, int Variant);

/// <summary>
/// A placement as a solver proposes it and the checks judge it: where it goes and how it is turned (as it will be drawn),
/// what holds it and what surface that is, the nearest point of it to the eye, and the verdict. The client's ghost and
/// the server's check are the same object from the same code.
/// </summary>
public sealed class PlacementCandidate
{
    public PlacementCandidate(Placeable placeable)
    {
        Placeable = placeable;
    }

    public Placeable Placeable { get; }

    /// <summary>Its base on what holds it (a deployable, a ship, the claim device), its pivot (a piece, a door's frame), where it sticks (a charge).</summary>
    public Vector3 Position { get; set; }

    /// <summary>Its turn as it will be drawn.</summary>
    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    /// <summary>Its up: the planet's, or the surface's (a charge).</summary>
    public Vector3 Up { get; set; } = Vector3.UnitY;

    /// <summary>Its turn about <see cref="Up"/>, in the characters' convention (radians).</summary>
    public float Yaw { get; set; }

    /// <summary>What holds it (the ground, a piece, a door's leaf, a tree…).</summary>
    public TargetRef Support { get; set; }

    /// <summary>The kind of surface <see cref="Support"/> is.</summary>
    public PlacementSurfaces Surface { get; set; }

    /// <summary>The normal of the surface under it.</summary>
    public Vector3 Normal { get; set; } = Vector3.UnitY;

    /// <summary>How far the surface under it is from where it was aimed or claimed (∞: none found).</summary>
    public float Step { get; set; }

    /// <summary>The nearest point of it to the eye (reach and sight are measured to it).</summary>
    public Vector3 Nearest { get; set; }

    public int Socket { get; set; }

    public int Variant { get; set; }

    /// <summary>The ground doesn't hide it (a foundation sits partly in the ground).</summary>
    public bool SightThroughGround { get; set; }

    /// <summary>What its kind and solver worked out: a piece's <see cref="PlacementPlan"/>, a door's frame, a lock's door.</summary>
    public object? Detail { get; set; }

    /// <summary>Aboard a ship (IRON_WAKE.md §8): where it goes in the ship's frame; null in the zone's.</summary>
    public AboardSpot? Aboard { get; set; }

    /// <summary>The verdict: done, or why not.</summary>
    public Outcome Outcome { get; set; }

    public bool Valid => Outcome.Ok;

    /// <summary>What an intent sends of it: aboard a ship, its pose in the ship's frame (IRON_WAKE.md §8).</summary>
    public PlacementClaim Claim => Aboard is { } aboard
        ? new(Support, Socket, aboard.Position, aboard.Yaw, Variant)
        : new(Support, Socket, Position, Yaw, Variant);

    public override string ToString() => $"{Placeable.Key} at {Position} on {Support} ({Surface}): {Outcome}";
}
