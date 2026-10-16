using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Placement;

/// <summary>What a kind finds a placeable's key to name in the game data (its definition and the items it involves).</summary>
internal readonly record struct ResolvedPlaceable(object Definition, ItemDef? Item, ItemDef? Tool, string ModelKey, string Name,
    IReadOnlyList<PlacementCost> Costs, IReadOnlyList<IReadOnlyList<PlacementCost>>? GradeCosts = null);

/// <summary>
/// What placing puts things into (GROUND_SYSTEMS.md §6): a zone on the server; the build sandbox's building alone. The
/// building kinds need only its building; the others put deployables, charges and ships into a zone.
/// </summary>
public interface IPlacementSite
{
    BuildingWorld? Building { get; }

    /// <summary>The building's clock (edit windows count from it).</summary>
    double BuildingNow { get; }

    Outcome PutDeployable(ulong player, DeployableDef def, PlacementCandidate c);

    Outcome PutCharge(ulong player, ThrowableStats stats, PlacementCandidate c);

    Outcome PutShip(ulong player, ShipDef def, PlacementCandidate c);
}

/// <summary>The game data a kind resolves its keys in, while it is read.</summary>
internal sealed class PlacementSources(GameContent.Loaded loaded, IReadOnlyDictionary<string, JsonElement> documents)
{
    public GameContent.Loaded Loaded { get; } = loaded;

    public BuildingData Building => Loaded.Building!;

    public ItemDef? Item(string key) => Loaded.Items.FirstOrDefault(i => i.Key == key);

    /// <summary>The <c>placement</c> block of the entry of a list in a file whose <paramref name="field"/> is <paramref name="key"/>.</summary>
    public JsonElement Own(string file, string list, string field, string key)
    {
        if (!documents.TryGetValue(file, out JsonElement root) || root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(list, out JsonElement entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return default;
        }
        foreach (JsonElement e in entries.EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(field, out JsonElement k) && k.ValueKind == JsonValueKind.String &&
                k.GetString() == key && e.TryGetProperty("placement", out JsonElement own))
            {
                return own;
            }
        }
        return default;
    }

    /// <summary>The <c>placement</c> block of an object at a path of a file (building.json's claim, a lock).</summary>
    public JsonElement OwnAt(string file, params string[] path)
    {
        if (!documents.TryGetValue(file, out JsonElement at))
        {
            return default;
        }
        foreach (string step in path)
        {
            if (at.ValueKind != JsonValueKind.Object || !at.TryGetProperty(step, out at))
            {
                return default;
            }
        }
        return at.ValueKind == JsonValueKind.Object && at.TryGetProperty("placement", out JsonElement own) ? own : default;
    }
}

/// <summary>
/// A kind of placeable (GROUND_SYSTEMS.md §6): what its entries in placement.json name in the game data, what its own
/// rules are beyond the shared checks, how it is drawn turned, what it counts against a limit, and, on the server, what
/// placing one makes. One handler per kind, registered by name (placement.json's kinds): deployable, piece, door, lock,
/// cupboard, charge, ship. Placing goes through the same pipeline whatever the kind.
/// </summary>
public abstract class PlaceableKind
{
    /// <summary>Its name in placement.json.</summary>
    public abstract string Name { get; }

    /// <summary>Building free of costs keeps its source item; consumable placement kinds can opt out.</summary>
    public virtual bool RetainItemWhenFree => true;

    /// <summary>The definition and items its key names in the game data, or null.</summary>
    internal abstract ResolvedPlaceable? Resolve(string key, PlacementSources sources);

    /// <summary>Its own <c>placement</c> block, next to its definition (default: none).</summary>
    internal abstract JsonElement Own(string key, PlacementSources sources);

    /// <summary>Its footprint when its rule gives none (a deployable's size, the claim device's, a ship's hull).</summary>
    internal virtual Footprint OwnFootprint(object definition) => Footprint.Point;

    /// <summary>
    /// The turn a candidate is drawn and made with, once its rotation has turned it: standing along its up at its yaw
    /// (the characters' convention) unless its kind says otherwise.
    /// </summary>
    public virtual Quaternion Orient(PlacementCandidate c) => Geometry.Upright(c.Up, c.Yaw);

    /// <summary>Its own rules, after the shared checks (a piece's stability, a door's opening, one lock to a door).</summary>
    public virtual Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) => Outcome.Done;

    /// <summary>How many of its kind stand on a structure (its rule's limit per structure).</summary>
    public virtual int CountOn(PlacementWorld world, Structure structure) => 0;

    /// <summary>How many of this placeable a player has placed (its rule's limit per player).</summary>
    public virtual int CountOwned(PlacementWorld world, ulong player, Placeable placeable) => 0;

    /// <summary>Makes it where the checked candidate says, the player's (its price is taken after, by the pipeline's caller).</summary>
    public abstract Outcome Put(IPlacementSite site, ulong player, PlacementCandidate c);
}

/// <summary>The kinds, by name.</summary>
internal static class Kinds
{
    public static PlaceableKind[] All => new PlaceableKind[]
    {
        new DeployableKind(), new PieceKind(), new DoorKind(), new LockPlacement(), new CupboardKind(), new ChargeKind(), new ShipKind(),
    };
}

/// <summary>Boxes, furnaces, campfires, workbenches, bags and beds (deployables.json): it stands on what holds it, and goes with it.</summary>
internal sealed class DeployableKind : PlaceableKind
{
    public override string Name => "deployable";

    internal override ResolvedPlaceable? Resolve(string key, PlacementSources sources) =>
        sources.Loaded.Deployables.FirstOrDefault(d => d.Key == key) is { } def
            ? new ResolvedPlaceable(def, def.Item, null, def.Key, def.Item.Name, Array.Empty<PlacementCost>())
            : null;

    internal override JsonElement Own(string key, PlacementSources sources) => sources.Own("deployables.json", "deployables", "key", key);

    internal override Footprint OwnFootprint(object definition) => new BoxFootprint(((DeployableDef)definition).HalfExtents);

    public override int CountOn(PlacementWorld world, Structure structure)
    {
        int count = 0;
        foreach (Piece piece in structure.Pieces)
        {
            count += world.Around.ThingsOn(piece.Ref);
        }
        return count;
    }

    public override int CountOwned(PlacementWorld world, ulong player, Placeable placeable) =>
        world.Around.ThingsOwned(player, (DeployableDef)placeable.Definition);

    public override Outcome Put(IPlacementSite site, ulong player, PlacementCandidate c) => site.PutDeployable(player, (DeployableDef)c.Placeable.Definition, c);
}

/// <summary>Building pieces (building.json), from the building plan: the socket solver's plan, the building's own rules (stability, sockets, collisions, the price).</summary>
internal sealed class PieceKind : PlaceableKind
{
    public override string Name => "piece";

    internal override ResolvedPlaceable? Resolve(string key, PlacementSources sources)
    {
        BuildingData data = sources.Building;
        if (data.Piece(key) is not { } def || data.Grades.Count == 0)
        {
            return null;
        }
        ItemDef? plan = sources.Loaded.Items.FirstOrDefault(i => i.Building == BuildingRole.Plan);
        IReadOnlyList<PlacementCost>[] costs = data.Grades.Select((g, index) =>
            (IReadOnlyList<PlacementCost>)new[] { new PlacementCost(g.Material, data.CostOf(def, index)) }).ToArray();
        return new ResolvedPlaceable(def, null, plan, def.Key, def.Name, costs[0], costs);
    }

    internal override JsonElement Own(string key, PlacementSources sources) => sources.Own("building.json", "pieces", "key", key);

    // As its plan lays it: the socket's orientation, or a free foundation's heading.
    public override Quaternion Orient(PlacementCandidate c) => c.Detail is PlacementPlan plan ? plan.ZoneTransform.Rotation : base.Orient(c);

    public override Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c)
    {
        if (c.Detail is not PlacementPlan plan || world.Building is not { } building)
        {
            return OutcomeCode.NoSocket;
        }
        plan.Grade = ctx.BuildGrade;
        return building.CheckBuilding(plan, ctx.Player, ctx.Wallet);
    }

    public override Outcome Put(IPlacementSite site, ulong player, PlacementCandidate c) =>
        c.Detail is PlacementPlan plan && site.Building is { } building ? building.Build(plan, player, site.BuildingNow, out _) : OutcomeCode.NoSocket;
}

/// <summary>Doors (building.json's doors with an item): fitted into a doorway or wall frame, one to an opening.</summary>
internal sealed class DoorKind : PlaceableKind
{
    public override string Name => "door";

    internal override ResolvedPlaceable? Resolve(string key, PlacementSources sources) =>
        sources.Building.Door(key) is { } def && sources.Loaded.Items.FirstOrDefault(i => i.Door == def) is { } item
            ? new ResolvedPlaceable(def, item, null, def.Key, def.Name, Array.Empty<PlacementCost>())
            : null;

    internal override JsonElement Own(string key, PlacementSources sources) => sources.Own("building.json", "doors", "key", key);

    // As its frame stands, turned round when its variant (R's flip) says so.
    public override Quaternion Orient(PlacementCandidate c) =>
        c.Detail is Piece frame
            ? Quaternion.Normalize(frame.ZoneTransform.Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, (c.Variant & 1) * MathF.PI))
            : base.Orient(c);

    public override Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) =>
        c.Detail is Piece frame && world.Building is { } building ? building.CanFitDoor((DoorDef)c.Placeable.Definition, frame, c.Variant) : OutcomeCode.NotAnOpening;

    public override Outcome Put(IPlacementSite site, ulong player, PlacementCandidate c) =>
        c.Detail is Piece frame && site.Building is { } building
            ? building.PlaceDoor(player, (DoorDef)c.Placeable.Definition, frame, c.Variant, out _)
            : OutcomeCode.NotAnOpening;
}

/// <summary>Key and code locks (building.json's locks): fitted to a door without one.</summary>
internal sealed class LockPlacement : PlaceableKind
{
    public override string Name => "lock";

    internal override ResolvedPlaceable? Resolve(string key, PlacementSources sources) =>
        sources.Building.Lock(key) is { } def && sources.Item(key) is { } item
            ? new ResolvedPlaceable(def, item, null, def.Key, def.Name, Array.Empty<PlacementCost>())
            : null;

    internal override JsonElement Own(string key, PlacementSources sources) =>
        sources.Building.Lock(key) is { } def ? sources.OwnAt("building.json", "locks", def.Field) : default;

    // As it sits at its spot on the door's first leaf.
    public override Quaternion Orient(PlacementCandidate c) => c.Detail is Door door ? door.LockSpot.Rotation : base.Orient(c);

    public override Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) =>
        c.Detail is Door door ? door.Lock != null ? OutcomeCode.HasLock : Outcome.Done : OutcomeCode.NotFound;

    public override Outcome Put(IPlacementSite site, ulong player, PlacementCandidate c) =>
        c.Detail is Door door && site.Building is { } building
            ? building.AttachLock(player, door, ((LockDef)c.Placeable.Definition).Kind, out _)
            : OutcomeCode.NotFound;
}

/// <summary>The claim device (our tool cupboard, building.json's claim): on a foundation's or floor's top, one to a structure.</summary>
internal sealed class CupboardKind : PlaceableKind
{
    public override string Name => "cupboard";

    internal override ResolvedPlaceable? Resolve(string key, PlacementSources sources) =>
        key == sources.Building.ClaimItem && sources.Loaded.ClaimDeployable is { } def
            ? new ResolvedPlaceable(def, def.Item, null, def.Key, sources.Building.ClaimName, Array.Empty<PlacementCost>())
            : null;

    internal override JsonElement Own(string key, PlacementSources sources) => sources.OwnAt("building.json", "claim");

    internal override Footprint OwnFootprint(object definition) => new BoxFootprint(((DeployableDef)definition).HalfExtents);

    // A foundation or floor (not a staircase's landing: the device sits on a slab).
    public override Outcome Check(PlacementWorld world, in PlacementContext ctx, PlacementCandidate c) =>
        Slab(world.Building, c) != null ? Outcome.Done : OutcomeCode.NotOnStructure;

    public override int CountOn(PlacementWorld world, Structure structure) => structure.Claim != null ? 1 : 0;

    public override Outcome Put(IPlacementSite site, ulong player, PlacementCandidate c)
    {
        if (site.Building is not { } building || Slab(building, c) is not { } slab)
        {
            return OutcomeCode.NotOnStructure;
        }
        Vector3D local = slab.Structure.Frame.ToLocal(new Vector3D(c.Position.X, c.Position.Y, c.Position.Z));
        return building.PlaceClaim(player, slab, local, c.Variant, out _);
    }

    private static Piece? Slab(BuildingWorld? building, PlacementCandidate c) =>
        c.Support.Family == TargetFamily.Piece && building?.PieceOf(new EntityId(c.Support.Id)) is { Def.IsSlab: true } slab ? slab : null;
}

/// <summary>Charges put on a surface by hand (explosives.json's throwables that placement.json lists): stuck where the view meets a surface, lit.</summary>
internal sealed class ChargeKind : PlaceableKind
{
    public override string Name => "charge";

    public override bool RetainItemWhenFree => false;

    internal override ResolvedPlaceable? Resolve(string key, PlacementSources sources) =>
        sources.Item(key) is { Throwable: { Sticky: true } stats } item
            ? new ResolvedPlaceable(stats, item, null, item.Key, item.Name, Array.Empty<PlacementCost>())
            : null;

    internal override JsonElement Own(string key, PlacementSources sources) => sources.Own("explosives.json", "throwables", "item", key);

    // Lying on its bottom along the surface's normal, as the raid view draws one stuck (its yaw doesn't matter).
    public override Quaternion Orient(PlacementCandidate c)
    {
        Vector3 up = c.Up;
        Vector3 side = MathF.Abs(up.Y) > 0.9f ? -Vector3.UnitZ : Vector3.UnitY;
        Vector3 x = Vector3.Normalize(Vector3.Cross(side, up));
        Vector3 z = Vector3.Cross(x, up);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
    }

    public override Outcome Put(IPlacementSite site, ulong player, PlacementCandidate c) => site.PutCharge(player, (ThrowableStats)c.Placeable.Definition, c);
}

/// <summary>Ships from their item (ships.json): set down on their skids, their hull clear, with the tank ships.json gives a placed one.</summary>
internal sealed class ShipKind : PlaceableKind
{
    public override string Name => "ship";

    internal override ResolvedPlaceable? Resolve(string key, PlacementSources sources) =>
        sources.Loaded.Ships.FirstOrDefault(s => s.Key == key) is { } def
            ? new ResolvedPlaceable(def, def.Item, null, def.Key, def.Name, Array.Empty<PlacementCost>())
            : null;

    internal override JsonElement Own(string key, PlacementSources sources) => sources.Own("ships.json", "ships", "key", key);

    internal override Footprint OwnFootprint(object definition) => new HullFootprint((ShipDef)definition);

    // The nose (+Z) along the yaw, as a ship stands (ShipMotor's).
    public override Quaternion Orient(PlacementCandidate c) => ShipMotor.Orientation(c.Up, c.Yaw);

    public override Outcome Put(IPlacementSite site, ulong player, PlacementCandidate c) => site.PutShip(player, (ShipDef)c.Placeable.Definition, c);
}
