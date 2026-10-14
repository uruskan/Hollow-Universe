using System;
using System.Numerics;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Capabilities;

namespace Hollow.Simulation.World;

/// <summary>What kind of static thing an entity is (characters are not static).</summary>
public enum StaticKind : byte
{
    Deployable,

    /// <summary>A bag of items lying on the ground (<see cref="DroppedBag"/>, GROUND.md §6.1).</summary>
    DroppedBag,

    Corpse,

    /// <summary>A world prop (G7): a barrel, a crate or a recycler the world places (<see cref="WorldProp"/>).</summary>
    Prop,
}

/// <summary>
/// A static entity of the zone (ARCHITECTURE.md §10.2): a deployable, a bag of dropped items or a corpse. It costs
/// nothing per tick until something happens to it; <see cref="Revision"/> rises when what others see changes, so the
/// server sends it again.
/// </summary>
public abstract class WorldObject : ITargetable, IContainerHolder
{
    protected WorldObject(EntityId id, Vector3 position, Vector3 up, float yaw)
    {
        Id = id;
        Position = position;
        Up = up;
        Yaw = yaw;
    }

    public EntityId Id { get; }

    /// <summary>How the world names it (GROUND_SYSTEMS.md §3.1).</summary>
    public TargetRef Ref => new(TargetFamily.Thing, Id.Value);

    public abstract StaticKind Kind { get; }

    /// <summary>Its base on the ground, in the zone frame; up there; turn about up.</summary>
    public Vector3 Position { get; set; }

    public Vector3 Up { get; set; }

    public float Yaw { get; set; }

    /// <summary>Raised on every change others can see.</summary>
    public uint Revision { get; set; }

    /// <summary>
    /// What it stands on (GROUND_SYSTEMS.md §3.6, §6): a building piece, held by a support link, so it goes when that goes;
    /// none on the ground, which never goes. Clients are told it (a structure's limit counts what stands on it).
    /// </summary>
    public TargetRef Support { get; set; }

    private int _shown;

    /// <summary>
    /// Raises <see cref="Revision"/> when what others see of its count changed since the last call (a looter took a stack
    /// out of a bag), so clients hear of it.
    /// </summary>
    public void PublishChanges()
    {
        if (ShownCount != _shown)
        {
            _shown = ShownCount;
            Revision++;
        }
    }

    /// <summary>Its storage, if it has any.</summary>
    public virtual Container? Container => null;

    /// <summary>The thing others see it as: a deployable's item, a prop's number; 0 for a corpse or a bag.</summary>
    public abstract ushort Look { get; }

    // What every client is told of it besides its look (GROUND.md §6.2: ThingSpawn, ThingUpdate).

    /// <summary>Its health as a fraction of the most it has (1 for what can't be hurt).</summary>
    public virtual float HealthFraction => 1;

    /// <summary>A number others see: how many stacks a bag holds (0 for the rest).</summary>
    public virtual int ShownCount => 0;

    /// <summary>A device burning or at work.</summary>
    public virtual bool Burning => false;

    /// <summary>Whether a player owns it (a sleeping bag is its owner's alone to respawn at).</summary>
    public virtual bool OwnedBy(EntityId player) => false;

    /// <summary>
    /// Its own name, shown on prompts and loot panels: a body's (the dead player's), a bag's, a world prop's; empty for a
    /// deployable, which clients name by its item.
    /// </summary>
    public virtual string Name => "";

    /// <summary>What it is drawn as (the model catalogue's key); empty for a body, which the characters draw.</summary>
    public virtual string ModelKey => "";

    /// <summary>Its capabilities (GROUND_SYSTEMS.md §2.1): a deployable's from its blocks, a bag's and a body's their kind's.</summary>
    public abstract CapabilitySet CapabilitiesIn(CapabilityData data);

    /// <summary>
    /// The nearest point of its bounds to a point (GROUND_SYSTEMS.md §3.4: reach is measured to it): a sphere of
    /// <see cref="BoundsRadius"/> <see cref="BoundsLift"/> above its base, unless it says otherwise (a deployable's box).
    /// </summary>
    public virtual Vector3 Nearest(Vector3 from)
    {
        Vector3 centre = Position + Up * BoundsLift;
        Vector3 off = from - centre;
        float d = off.Length();
        return d <= BoundsRadius ? from : centre + off * (BoundsRadius / d);
    }

    /// <summary>Its bounds as a sphere: how far above its base, and how big (the interact key's picker uses the same).</summary>
    protected virtual float BoundsLift => 0.15f;

    protected virtual float BoundsRadius => 0.35f;
}

/// <summary>
/// A placed object (GROUND.md §6): sleeping bag, campfire, furnace, box, workbench, research table; and (G7) the
/// world's own props (<see cref="WorldProp"/>), which hits, blasts, opening and replication treat the same way.
/// </summary>
public class Deployable : WorldObject, IOwned
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

    public override float HealthFraction => Health / Def.Health;

    public override string ModelKey => Def.Key;

    public override CapabilitySet CapabilitiesIn(CapabilityData data) => data.Of(Def);

    ulong IOwned.OwnerId => Owner.Value;

    /// <summary>The nearest point of its box.</summary>
    public override Vector3 Nearest(Vector3 from) => Combat.Geometry.NearestOnBox(Position, Up, Yaw, Def.HalfExtents, from);

    public override bool Burning => Lit;

    public override bool OwnedBy(EntityId player) => Owner == player;

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

    public override string Name => Prop.Name;

    public override string ModelKey => Prop.Key;

    /// <summary>A barrel's loot is only had by breaking it; a crate and a recycler open like a box.</summary>
    public override Container? Container => Prop.Openable ? Storage : null;

    /// <summary>A barrel holds its loot but isn't opened: it has no container's capability.</summary>
    public override CapabilitySet CapabilitiesIn(CapabilityData data) =>
        Prop.Openable ? base.CapabilitiesIn(data) : base.CapabilitiesIn(data).Without(data.Set("container"));

    /// <summary>Whether players changed it since it spawned (looted, damaged, used): then it is stored.</summary>
    public bool Touched { get; set; }
}

/// <summary>
/// A container left lying in the world for a while (Rust's dropped item container): a body (<see cref="Corpse"/>) or a
/// bag of dropped items (<see cref="DroppedBag"/>). Anyone may take from it, nobody put into it. Its lifetime is its
/// kind's (survival.json's lifetimes, GROUND_SYSTEMS.md §3.7): it goes when its time is up (world time: it pauses while
/// the server is down) or once emptied, by the zone's one lifetime step.
/// </summary>
public abstract class DroppedContainer : WorldObject, ILifetime
{
    protected DroppedContainer(EntityId id, ContainerKind kind, int slots, Vector3 position, Vector3 up, float yaw, LifetimeRule lifetime, long despawnTick)
        : base(id, position, up, yaw)
    {
        Loot = new Container(kind, Math.Max(1, slots));
        Lifetime = lifetime;
        DespawnTick = despawnTick;
    }

    public Container Loot { get; }

    public override Container? Container => Loot;

    /// <summary>Its kind's lifetime.</summary>
    public LifetimeRule Lifetime { get; }

    /// <summary>The zone tick its time is up at.</summary>
    public long DespawnTick { get; set; }

    long ILifetime.EndsAt
    {
        get => DespawnTick;
        set => DespawnTick = value;
    }

    /// <summary>Looted empty.</summary>
    public bool Emptied => Loot.IsEmpty;
}

/// <summary>
/// Items lying on the ground (GROUND.md §6.1): everything dropped, spilled or left over within a bag's reach is packed
/// into one small bag (<c>Zone.Spill</c>, survival.json's <c>bag</c>). Others see only the bag and how many stacks it
/// holds; what they are goes only to whoever opens it. It lasts <c>droppedItemSeconds</c> from the newest item put in.
/// </summary>
public sealed class DroppedBag : DroppedContainer
{
    public DroppedBag(EntityId id, string name, int slots, Vector3 position, Vector3 up, float yaw, LifetimeRule lifetime, long despawnTick)
        : base(id, ContainerKind.Crate, slots, position, up, yaw, lifetime, despawnTick)
    {
        Name = name;
    }

    public override StaticKind Kind => StaticKind.DroppedBag;

    /// <summary>What bags are called (survival.json's <c>bag.name</c>).</summary>
    public override string Name { get; }

    /// <summary>A bag, whatever is in it.</summary>
    public override ushort Look => 0;

    public override string ModelKey => "dropped-bag";

    public override CapabilitySet CapabilitiesIn(CapabilityData data) => data.Bag;

    /// <summary>How many stacks it holds: one is picked up at once, more open like a box.</summary>
    public override int ShownCount
    {
        get
        {
            int stacks = 0;
            foreach (ItemStack s in Loot.Slots)
            {
                stacks += s.IsEmpty ? 0 : 1;
            }
            return stacks;
        }
    }
}

/// <summary>A dead player's body with everything they carried (GROUND.md §6): anyone may take from it.</summary>
public sealed class Corpse : DroppedContainer
{
    public Corpse(EntityId id, string name, EntityId of, int slots, Vector3 position, Vector3 up, float yaw, LifetimeRule lifetime, long despawnTick)
        : base(id, ContainerKind.Corpse, slots, position, up, yaw, lifetime, despawnTick)
    {
        Name = name;
        Of = of;
    }

    public override StaticKind Kind => StaticKind.Corpse;

    /// <summary>Whose body it was.</summary>
    public override string Name { get; }

    public EntityId Of { get; }

    public override ushort Look => 0;

    public override CapabilitySet CapabilitiesIn(CapabilityData data) => data.Corpse;

    /// <summary>A body lies along the ground: a wider sphere, lower.</summary>
    protected override float BoundsLift => 0.25f;

    protected override float BoundsRadius => 0.6f;
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
