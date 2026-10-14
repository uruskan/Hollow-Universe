using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>To a player: a crafted unit is done. (GROUND_SYSTEMS.md §12.3)</summary>
public readonly record struct Crafted(
    EntityId Player = default,
    ushort Item = 0,
    int Count = 0) : IZoneEvent
{
}
