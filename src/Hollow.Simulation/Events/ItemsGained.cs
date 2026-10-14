using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>To a player: items gained (gathering, picking up, crafting, looting) or lost (a tool broke, fuel burnt). (GROUND_SYSTEMS.md §12.3)</summary>
public readonly record struct ItemsGained(
    EntityId Player = default,
    ushort Item = 0,
    int Count = 0,
    GainReason Reason = default,
    bool WeakSpot = false) : IZoneEvent
{
    /// <summary>Negative for a loss.</summary>
    public int Count { get; init; } = Count;

    /// <summary>Gathered on the weak spot.</summary>
    public bool WeakSpot { get; init; } = WeakSpot;
}
