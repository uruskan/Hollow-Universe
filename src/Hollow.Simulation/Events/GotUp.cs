using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>To a player: they got up again. (GROUND_SYSTEMS.md §12.3)</summary>
public readonly record struct GotUp(
    EntityId Player = default,
    EntityId Helper = default,
    Vector3 Position = default) : IZoneEvent
{
    /// <summary>Who helped them up, or none.</summary>
    public EntityId Helper { get; init; } = Helper;
}
