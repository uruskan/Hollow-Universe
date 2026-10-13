using System;
using System.Numerics;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;

namespace Hollow.Simulation.World;

/// <summary>What kind of static thing an entity is (characters are not static).</summary>
public enum StaticKind : byte
{
    Deployable,
    DroppedItem,
    Corpse,

    /// <summary>A world prop (G7): a barrel, a crate or a recycler the world places (<see cref="WorldProp"/>).</summary>
    Prop,
}

/// <summary>
/// A static entity of the zone (ARCHITECTURE.md §10.2): a deployable, a dropped item or a corpse. It costs nothing per
/// tick until something happens to it; <see cref="Revision"/> rises when what others see changes, so the server sends
/// it again.
/// </summary>
public abstract class WorldObject
{
    protected WorldObject(EntityId id, Vector3 position, Vector3 up, float yaw)
    {
        Id = id;
        Position = position;
        Up = up;
        Yaw = yaw;
    }

    public EntityId Id { get; }

    public abstract StaticKind Kind { get; }

    /// <summary>Its base on the ground, in the zone frame; up there; turn about up.</summary>
    public Vector3 Position { get; set; }

    public Vector3 Up { get; set; }

    public float Yaw { get; set; }

    /// <summary>Raised on every change others can see.</summary>
    public uint Revision { get; set; }

    /// <summary>Its storage, if it has any.</summary>
    public virtual Container? Container => null;

    /// <summary>The thing others see it as: an item ID (a deployable's item, a dropped stack's item; 0 for a corpse).</summary>
    public abstract ushort Look { get; }
}

/// <summary>
/// A placed object (GROUND.md §6): sleeping bag, campfire, furnace, box, workbench, research table; and (G7) the
/// world's own props (<see cref="WorldProp"/>), which hits, blasts, opening and replication treat the same way.
/// </summary>
public class Deployable : WorldObject
{
    public Deployable(EntityId id, DeployableDef def, Vector3 position, Vector3 up, float yaw, EntityId owner)
        : base(id, position, up, yaw)
    {
        Def = def;
        Owner = owner;
        Health = def.Health;
        if (def.Slots > 0)
        {
            ContainerKind kind = def.IsMachine ? ContainerKind.Device : def.TakeOnly ? ContainerKind.Crate : ContainerKind.Box;
            Storage = new Container(kind, def.Slots, def);
            CookProgress = def.IsMachine ? new int[def.Slots] : Array.Empty<int>();
        }
        else
        {
            CookProgress = Array.Empty<int>();
        }
    }

    public override StaticKind Kind => StaticKind.Deployable;

    public DeployableDef Def { get; }

    /// <summary>The character that placed it (a bag is only theirs to respawn at).</summary>
    public EntityId Owner { get; set; }

    public float Health { get; set; }

    public Container? Storage { get; }

    public override Container? Container => Storage;

    public override ushort Look => Def.Id;

    /// <summary>A device burning its fuel.</summary>
    public bool Lit { get; set; }

    /// <summary>Ticks until the fuel burning now is spent.</summary>
    public int BurnTicksLeft { get; set; }

    /// <summary>Ticks each input slot has cooked for.</summary>
    public int[] CookProgress { get; }

    /// <summary>A respawn point is ready again at this time (Unix ms).</summary>
    public long ReadyAtUnixMs { get; set; }

    /// <summary>Where a box stands: its centre (the base is on the ground).</summary>
    public Vector3 Centre => Position + Up * Def.HalfExtents.Y;

    /// <summary>Its model in the catalogue (its item's key; a prop's own) as it stands (GROUND.md §9.4).</summary>
    public ModelPlacement Model => new(Def.Key, "", Position, ModelPlacement.Upright(Up, Yaw));

    /// <summary>A research table's work under way (G7): the item being taken apart (0: none; kept in its first cook slot, so it is saved).</summary>
    public ushort ResearchItem
    {
        get => CookProgress.Length > 0 ? (ushort)Math.Clamp(CookProgress[0], 0, ushort.MaxValue) : (ushort)0;
        set
        {
            if (CookProgress.Length > 0)
            {
                CookProgress[0] = value;
            }
        }
    }

    /// <summary>The salvage a research under way was paid with (given back if the table breaks first; its second cook slot).</summary>
    public int ResearchSalvage
    {
        get => CookProgress.Length > 1 ? CookProgress[1] : 0;
        set
        {
            if (CookProgress.Length > 1)
            {
                CookProgress[1] = Math.Max(0, value);
            }
        }
    }
}

/// <summary>
/// A prop the world places (GROUND.md §6.8, G7): a barrel to break, a crate to loot, a recycler. Its ID is its loot
/// spot's (or recycler point's), stable across restarts, so only what players changed is stored (overrides). Clients
/// see it as a thing of kind <see cref="StaticKind.Prop"/> whose look is the prop's number.
/// </summary>
public sealed class WorldProp : Deployable
{
    public WorldProp(EntityId id, PropDef prop, Vector3 position, Vector3 up, float yaw)
        : base(id, prop.Deployable, position, up, yaw, EntityId.None)
    {
        Prop = prop;
    }

    public PropDef Prop { get; }

    public override StaticKind Kind => StaticKind.Prop;

    /// <summary>A barrel's loot is only had by breaking it; a crate and a recycler open like a box.</summary>
    public override Container? Container => Prop.Openable ? Storage : null;

    /// <summary>Whether players changed it since it spawned (looted, damaged, used): then it is stored.</summary>
    public bool Touched { get; set; }
}

/// <summary>An item dropped in the world (GROUND.md §6): picked up with the interact key, gone after a while.</summary>
public sealed class DroppedItem : WorldObject
{
    public DroppedItem(EntityId id, ItemStack stack, Vector3 position, Vector3 up, float yaw, long despawnTick)
        : base(id, position, up, yaw)
    {
        Stack = stack;
        DespawnTick = despawnTick;
    }

    public override StaticKind Kind => StaticKind.DroppedItem;

    public ItemStack Stack { get; set; }

    /// <summary>The zone tick it disappears at (world time: it pauses while the server is down).</summary>
    public long DespawnTick { get; set; }

    public override ushort Look => Stack.Item;
}

/// <summary>A dead player's body with everything they carried (GROUND.md §6): anyone may take from it.</summary>
public sealed class Corpse : WorldObject
{
    public Corpse(EntityId id, string name, EntityId of, int slots, Vector3 position, Vector3 up, float yaw, long despawnTick)
        : base(id, position, up, yaw)
    {
        Name = name;
        Of = of;
        Loot = new Container(ContainerKind.Corpse, Math.Max(1, slots));
        DespawnTick = despawnTick;
    }

    public override StaticKind Kind => StaticKind.Corpse;

    /// <summary>Whose body it was.</summary>
    public string Name { get; }

    public EntityId Of { get; }

    public Container Loot { get; }

    public override Container? Container => Loot;

    public long DespawnTick { get; set; }

    public override ushort Look => 0;
}

/// <summary>The entity component linking a static entity to its object.</summary>
public struct StaticLink
{
    public WorldObject Object;
}

/// <summary>The entity component linking a character to its survival state.</summary>
public struct PlayerLink
{
    public Players.Player Player;
}
