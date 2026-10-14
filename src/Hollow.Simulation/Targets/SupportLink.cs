using System.Numerics;

namespace Hollow.Simulation.Targets;

/// <summary>
/// A world thing resting on or stuck in another (GROUND_SYSTEMS.md §3.6): what holds it (its support; the part may name
/// a door's leaf or a body's bone) and its pose in that support's (or part's) own frame. Recorded the same way for a charge
/// on a wall or a door, an arrow in a tree or a deer's flank, a furnace on a foundation: the thing is found where its
/// support is now, follows it when it moves, and goes with it when it goes (by its own capability's consequence). None:
/// held by nothing (flying); the ground holds what lies on it and never goes.
/// </summary>
public readonly record struct SupportLink(TargetRef Support, Vector3 Position, Quaternion Rotation)
{
    public static readonly SupportLink None = default;

    public bool IsNone => Support.IsNone;

    /// <summary>Where the linked thing is when its support is at <paramref name="support"/>.</summary>
    public TargetPose In(in TargetPose support) =>
        new(support.ToZone(Position), Quaternion.Normalize(support.Rotation * Rotation));

    /// <summary>The link that keeps a thing at <paramref name="at"/> on a support now at <paramref name="support"/>.</summary>
    public static SupportLink Of(in TargetRef holder, in TargetPose support, in TargetPose at) =>
        new(holder, support.ToLocal(at.Position), Quaternion.Normalize(Quaternion.Inverse(support.Rotation) * at.Rotation));
}
