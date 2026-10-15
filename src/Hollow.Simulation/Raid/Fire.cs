using System;
using System.Numerics;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.Raid;

/// <summary>Why a fire went out (the <see cref="FireOut"/> event).</summary>
public enum FireEnd : byte
{
    /// <summary>Its time was up.</summary>
    BurntOut,

    /// <summary>It found itself in water (the sea), or what it burnt on went into it.</summary>
    Doused,

    /// <summary>Too many fires burnt in the zone: the oldest went for a new one.</summary>
    Replaced,

    /// <summary>What it burnt on went, and nothing below held the fuel.</summary>
    Fell,

    /// <summary>Put out by the server (an admin, a test).</summary>
    PutOut,
}

/// <summary>
/// A fire (IRON_WAKE.md §7, IW4): a pool of burning fuel a blast left, a world thing of its own (the fire family,
/// GROUND_SYSTEMS.md §3.1) held by what it burns on through a support link (§3.6): the ground, a floor, a foundation, a box,
/// a ship's deck it rides as the ship flies. Its pose is its middle on that surface, its +Y the surface's normal. It burns
/// for its kind's time, hurting what is in it every so often (<see cref="FireSystem"/>); when what holds it goes, it falls
/// to what is below. It is owned by whoever fired what left it, who is blamed for what it burns.
/// </summary>
public sealed class Fire : ITargetable, IOwned
{
    public Fire(EntityId id, FireDef kind, ItemDef item, EntityId owner, string ownerName, long serial)
    {
        Id = id;
        Kind = kind;
        Item = item;
        Owner = owner;
        OwnerName = ownerName;
        Serial = serial;
    }

    public EntityId Id { get; }

    /// <summary>How the world names it (GROUND_SYSTEMS.md §3.1).</summary>
    public TargetRef Ref => new(TargetFamily.Fire, Id.Value);

    /// <summary>Its kind (explosives.json fires): its radius, time and what it does.</summary>
    public FireDef Kind { get; }

    /// <summary>What left it: the round or explosive whose blast it is (its sounds and looks are the item's).</summary>
    public ItemDef Item { get; }

    /// <summary>Who fired what left it (none: the world), blamed for what it burns, and their name, kept when they have left.</summary>
    public EntityId Owner { get; }

    public string OwnerName { get; }

    public ulong OwnerId => Owner.Value;

    /// <summary>What holds it and its middle's pose there (+Y the surface's normal).</summary>
    public SupportLink Link { get; internal set; }

    /// <summary>Where it was last seen, in the zone frame: where it falls from when what held it is already gone.</summary>
    public TargetPose LastPose { get; internal set; }

    /// <summary>The tick it was lit, the tick it burns out, and the tick it next hurts what is in it.</summary>
    public long StartTick { get; internal set; }

    public long EndTick { get; internal set; }

    public long NextHurtTick { get; internal set; }

    /// <summary>Its order of lighting: when there are too many, the lowest goes first.</summary>
    public long Serial { get; }

    /// <summary>Raised when what clients are told of it changes (it fell onto something else).</summary>
    public uint Revision { get; internal set; }

    /// <summary>Gone out (taken out of the zone's list at its next step).</summary>
    public bool Out { get; internal set; }

    /// <summary>Its middle as last seen (zone frame) and the surface's normal there.</summary>
    public Vector3 Position => LastPose.Position;

    public Vector3 Normal => Vector3.Transform(Vector3.UnitY, LastPose.Rotation);

    /// <summary>The nearest point of its pool (a disc of its radius on its surface) to a point.</summary>
    public static Vector3 NearestOnPool(in TargetPose pose, float radius, Vector3 from)
    {
        Vector3 normal = Vector3.Transform(Vector3.UnitY, pose.Rotation);
        Vector3 off = from - pose.Position;
        Vector3 flat = off - normal * Vector3.Dot(off, normal);
        float length = flat.Length();
        return length <= radius ? pose.Position + flat : pose.Position + flat * (radius / length);
    }

    public override string ToString() =>
        $"{Kind.Key} fire {Id} of {Item.Key} at {Position} on {Link.Support}{(Out ? ", out" : "")}";
}
