using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>To everyone near: a projectile stopped. (GROUND_SYSTEMS.md §12.3)</summary>
public readonly record struct ProjectileEnded(
    EntityId Shooter = default,
    uint Projectile = 0,
    Vector3 Position = default,
    TargetRef Target = default,
    uint Seq = 0) : IZoneEvent
{
    public AttackId Attack { get; init; }
    public uint Tick { get; init; }
    public int Battery { get; init; }
    public ushort Ammo { get; init; }
    public SurfaceContact Contact { get; init; }
    /// <summary>What it ended on, before damage could remove it.</summary>
    public TargetRef Target { get; init; } = Target;

    /// <summary>0 for an arrow.</summary>
    public uint Seq { get; init; } = Seq;
}
