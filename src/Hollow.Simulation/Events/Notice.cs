using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>To a player: a line to read (a built ship waiting for room, G6). (GROUND_SYSTEMS.md §12.3)</summary>
public readonly record struct Notice(
    EntityId Player = default,
    string Text = "") : IZoneEvent
{
}
