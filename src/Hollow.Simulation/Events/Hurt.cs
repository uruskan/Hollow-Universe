using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>To the victim: damage taken, from a direction. (GROUND_SYSTEMS.md §12.3)</summary>
public readonly record struct Hurt(
    EntityId Player = default,
    EntityId By = default,
    float Amount = 0,
    HitRegion Region = default,
    Vector3 From = default,
    ushort Weapon = 0) : IZoneEvent
{
    /// <summary>The attacker, or the animal.</summary>
    public EntityId By { get; init; } = By;

    /// <summary>Where the blow came from.</summary>
    public Vector3 From { get; init; } = From;
}
