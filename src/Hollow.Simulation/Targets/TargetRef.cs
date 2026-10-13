using System.Numerics;

namespace Hollow.Simulation.Targets;

/// <summary>
/// The families of world things (GROUND_SYSTEMS.md §3.1): each is one store of the zone with its own lookup, and
/// registers one <see cref="ITargetFamily"/>. Ids are 64-bit and unique across families, so the family only routes the
/// lookup. The value is the wire number (<c>TargetNet.Family</c>, 4 bits): append only.
/// </summary>
public enum TargetFamily : byte
{
    /// <summary>Nothing: a projectile that ran out in the air; no support at all.</summary>
    None = 0,

    /// <summary>The terrain, and the boulders that stop a blow as it does. It holds what lies on it and never goes.</summary>
    Ground = 1,

    /// <summary>Players, awake, down or asleep.</summary>
    Character = 2,

    /// <summary>Animals, alive or a corpse.</summary>
    Animal = 3,

    /// <summary>Trees, outcrops, pickups and bushes: the surface's own, IDs from its tiles.</summary>
    Natural = 4,

    /// <summary>The static things of the entity store: deployables, bags of dropped items, bodies, world props.</summary>
    Thing = 5,

    /// <summary>Building pieces.</summary>
    Piece = 6,

    /// <summary>Doors in their frames (their parts: the leaves).</summary>
    Door = 7,

    /// <summary>The claim device, a base's cupboard of building rights and upkeep.</summary>
    Cupboard = 8,

    /// <summary>Thrown and placed explosives, duds included.</summary>
    Explosive = 9,

    /// <summary>Ships (their parts: the hatch, the fuel cap, the cargo).</summary>
    Ship = 10,
}

/// <summary>
/// Names anything in the zone (GROUND_SYSTEMS.md §3.1): its family, its ID, and a part of it where that matters (a door's
/// leaf, a ship's hatch, a body's bone, which <see cref="Combat.Hitboxes"/> numbers). The one way interaction, placement
/// supports, damage, support links, lifetimes and the Destroyed event name a thing.
/// </summary>
public readonly record struct TargetRef(TargetFamily Family, ulong Id, byte Part = 0)
{
    public static readonly TargetRef None = default;

    /// <summary>The ground: it holds what lies on it, and never goes.</summary>
    public static readonly TargetRef Ground = new(TargetFamily.Ground, 0);

    public bool IsNone => Family == TargetFamily.None;

    /// <summary>The thing itself, without the part.</summary>
    public TargetRef Whole => Part == 0 ? this : this with { Part = 0 };

    public override string ToString() => IsNone ? "nothing" : Part == 0 ? $"{Family} #{Id:X16}" : $"{Family} #{Id:X16}.{Part}";
}

/// <summary>
/// What one viewer is told about a thing that depends on who they are (GROUND_SYSTEMS.md §3.3): one byte on every
/// replicated thing whose facts differ between players, made by the thing's capabilities
/// (<see cref="Capabilities.ICapability.Flags"/>), so a client knows which options to offer without being told secrets
/// (a lock's code, who else is on a list).
/// </summary>
[System.Flags]
public enum ViewerFlags : byte
{
    None = 0,

    /// <summary>The viewer placed or owns it.</summary>
    Mine = 1,

    /// <summary>The viewer is on its list: a cupboard's (building rights) or a code lock's.</summary>
    Authorised = 2,

    /// <summary>A code lock with a code set.</summary>
    HasCode = 4,

    /// <summary>Locked: a door's lock, a ship.</summary>
    Locked = 8,

    /// <summary>The viewer may use it now: pass a door (unlocked, on its lock's list, or with its key).</summary>
    CanUse = 16,

    /// <summary>Holds nothing.</summary>
    Empty = 32,

    /// <summary>Burning or at work: a campfire or furnace lit, a recycler running, a research table researching.</summary>
    Lit = 64,

    /// <summary>Its owner is in the viewer's team.</summary>
    Teammate = 128,
}

/// <summary>Where a thing (or a part of it) is: a position and a turn, in the zone frame.</summary>
public readonly record struct TargetPose(Vector3 Position, Quaternion Rotation)
{
    /// <summary>A point given in this pose's own frame, in the zone frame.</summary>
    public Vector3 ToZone(Vector3 local) => Position + Vector3.Transform(local, Rotation);

    /// <summary>A zone-frame point in this pose's own frame.</summary>
    public Vector3 ToLocal(Vector3 point) => Vector3.Transform(point - Position, Quaternion.Inverse(Rotation));
}

/// <summary>
/// One family of world things (GROUND_SYSTEMS.md §3.1), answering for its members by ID: whether one still exists, the
/// thing itself, where it is (or a part of it: a door's leaf, a body's bone), the nearest point of its bounds to a point
/// (reach is measured to it, §3.4), what it is drawn as, and its capabilities. Registered once per family; IDs from
/// outside are untrusted (an unknown one simply doesn't exist). The damage pipeline's receivers are the families' damage
/// side (GROUND.md §6.4.2).
/// </summary>
public interface ITargetFamily
{
    TargetFamily Family { get; }

    /// <summary>Whether the thing is still in the world (a broken, removed, felled, dead or gone one isn't).</summary>
    bool Exists(ulong id);

    /// <summary>The thing itself (its store's object), or null when it doesn't exist.</summary>
    object? Resolve(ulong id);

    /// <summary>Where the thing, or its part (<see cref="TargetRef.Part"/>), is now.</summary>
    bool TryPose(in TargetRef target, out TargetPose pose);

    /// <summary>The nearest point of the thing's bounds (or its part's) to a point.</summary>
    bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point);

    /// <summary>What it is drawn as (the model catalogue's key); empty when nothing draws it.</summary>
    string ModelKey(ulong id);
}

/// <summary>A world thing that knows how it is named (<see cref="TargetRef"/>): its family and ID.</summary>
public interface ITargetable
{
    TargetRef Ref { get; }
}
