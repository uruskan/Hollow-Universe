using Hollow.Simulation.Entities;
using Hollow.Simulation.Combat;
using System.Numerics;

namespace Hollow.Simulation;

/// <summary>
/// A crewed battery fired (IW3, IRON_WAKE.md §6.3, §6.9): the ship, its station and barrel, the shot's number (the station's
/// count, which the gunner's client matched its predicted shot to), the tick, the round and the gunner. Its rocket is a
/// projectile of its own (<see cref="ProjectileLaunched"/>, the same number). (GROUND_SYSTEMS.md §12.3)
/// </summary>
public readonly record struct TurretFired(
    EntityId Ship = default,
    int Station = 0,
    int Barrel = 0,
    uint Number = 0,
    long Tick = 0,
    ushort Kind = 0,
    EntityId Gunner = default) : IZoneEvent
{
    public AttackId Attack { get; init; }
    public Vector3 Origin { get; init; }
    public Vector3 Direction { get; init; }
}
