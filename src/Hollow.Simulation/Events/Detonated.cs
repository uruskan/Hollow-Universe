using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>To everyone near (G5): an explosion. (GROUND_SYSTEMS.md §12.3)</summary>
public readonly record struct Detonated(
    EntityId Player = default,
    EntityId Explosive = default,
    ushort Item = 0,
    Vector3 Position = default,
    Vector3 Normal = default,
    float Radius = 0,
    TargetRef On = default,
    bool Round = false) : IZoneEvent
{
    /// <summary>Whose explosive.</summary>
    public EntityId Player { get; init; } = Player;

    /// <summary>The explosive that went off (none for a rocket or round).</summary>
    public EntityId Explosive { get; init; } = Explosive;

    /// <summary>The explosive or the round.</summary>
    public ushort Item { get; init; } = Item;

    /// <summary>The surface it was on, facing out.</summary>
    public Vector3 Normal { get; init; } = Normal;

    /// <summary>What it was on (only the family matters).</summary>
    public TargetRef On { get; init; } = On;

    /// <summary>An explosive round (many, cosmetic) rather than a charge.</summary>
    public bool Round { get; init; } = Round;
}
