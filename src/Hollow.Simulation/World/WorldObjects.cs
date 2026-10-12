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

/// <summary>A placed object (GROUND.md §6): sleeping bag, campfire, furnace, box, workbench.</summary>
public sealed class Deployable : WorldObject
{
    public Deployable(EntityId id, DeployableDef def, Vector3 position, Vector3 up, float yaw, EntityId owner)
        : base(id, position, up, yaw)
    {
        Def = def;
        Owner = owner;
        Health = def.Health;
        if (def.Slots > 0)
        {
            Storage = new Container(def.Device != null ? ContainerKind.Device : ContainerKind.Box, def.Slots, def);
            CookProgress = def.Device != null ? new int[def.Slots] : Array.Empty<int>();
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
