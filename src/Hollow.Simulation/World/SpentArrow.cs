using System.Numerics;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation.World;

/// <summary>
/// A spent arrow (GROUND_SYSTEMS.md §5, the owner's): it stays where it stuck, in the ground, a building, a deployable, a
/// tree, an animal or a player alive or dead, held there by a support link (GROUND_SYSTEMS.md §3.6: a body's bone, which
/// it rides as the body moves), until someone takes it with E or its lifetime (§3.7) ends. Its pose is its tip's, the
/// shaft along its +X (the arrow model's own frame), as it flew.
/// </summary>
public sealed class SpentArrow : ILifetime
{
    public SpentArrow(EntityId id, ItemDef item, SupportLink link, LifetimeRule lifetime, long endsAt, long serial)
    {
        Id = id;
        Item = item;
        Link = link;
        Lifetime = lifetime;
        EndsAt = endsAt;
        Serial = serial;
    }

    public EntityId Id { get; }

    /// <summary>How the world names it (GROUND_SYSTEMS.md §3.1).</summary>
    public TargetRef Ref => new(TargetFamily.Arrow, Id.Value);

    /// <summary>What it is (items.json's arrow): what is picked up.</summary>
    public ItemDef Item { get; }

    /// <summary>What holds it and where its tip is there (a falling arrow is held by the ground).</summary>
    public SupportLink Link { get; set; }

    /// <summary>Where it was last seen, in the zone frame: where it falls from when what held it is already gone.</summary>
    public TargetPose LastPose { get; set; }

    /// <summary>Its order of sticking: when there are too many, the lowest goes first.</summary>
    public long Serial { get; }

    /// <summary>Raised when what clients are told of it changes (it fell).</summary>
    public uint Revision { get; set; }

    /// <summary>Taken or gone.</summary>
    public bool Gone { get; set; }

    public LifetimeRule Lifetime { get; }

    public long EndsAt { get; set; }

    /// <summary>An arrow is taken whole: it is never emptied, it goes.</summary>
    public bool Emptied => false;

    /// <summary>The length of an arrow, nock to tip (the model's).</summary>
    public const float Length = 0.8f;

    /// <summary>Its shaft at a pose: the nock and the tip.</summary>
    public static (Vector3 Nock, Vector3 Tip) Shaft(in TargetPose pose) =>
        (pose.Position - Vector3.Transform(Vector3.UnitX, pose.Rotation) * Length, pose.Position);
}
