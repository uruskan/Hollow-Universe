using System.Numerics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation;

/// <summary>
/// A fire was lit where a blast left it (IW4, IRON_WAKE.md §7), or fell onto something else when what held it went: the
/// fire, its kind's radius, its middle and the surface's normal (zone frame) and what holds it. (GROUND_SYSTEMS.md §12.3)
/// </summary>
public readonly record struct FireLit(
    EntityId Fire = default,
    EntityId Player = default,
    ushort Item = 0,
    Vector3 Position = default,
    Vector3 Normal = default,
    float Radius = 0,
    TargetRef On = default,
    bool Moved = false) : IZoneEvent
{
    /// <summary>The fire, by ID (its family is <see cref="TargetFamily.Fire"/>).</summary>
    public EntityId Fire { get; init; } = Fire;

    /// <summary>Whose: who fired what left it.</summary>
    public EntityId Player { get; init; } = Player;

    /// <summary>What left it (the round or explosive).</summary>
    public ushort Item { get; init; } = Item;

    /// <summary>What it burns on.</summary>
    public TargetRef On { get; init; } = On;

    /// <summary>It was already burning and fell onto something else (what held it went).</summary>
    public bool Moved { get; init; } = Moved;
}
