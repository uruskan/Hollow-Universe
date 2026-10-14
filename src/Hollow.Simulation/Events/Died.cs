using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>To a player: they died. (GROUND_SYSTEMS.md §12.3)</summary>
public readonly record struct Died(
    EntityId Player = default,
    EntityId By = default,
    string Cause = "",
    Vector3 Position = default) : IZoneEvent
{
    /// <summary>Who killed them, or none.</summary>
    public EntityId By { get; init; } = By;

    /// <summary>How, for the death screen.</summary>
    public string Cause { get; init; } = Cause;
}
