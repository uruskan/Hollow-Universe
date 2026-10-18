using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>To everyone near: a projectile was loosed (an arrow, or a bullet with its shot's sequence number). (GROUND_SYSTEMS.md §12.3)</summary>
public readonly record struct ProjectileLaunched(
    EntityId Shooter = default,
    uint Projectile = 0,
    ushort Ammo = 0,
    Vector3 Position = default,
    Vector3 Velocity = default,
    uint Seq = 0,
    bool Silenced = false) : IZoneEvent
{
    public AttackId Attack { get; init; }
    public uint Tick { get; init; }
    public ushort Weapon { get; init; }
    public float Gravity { get; init; }
    public float Drag { get; init; }
    /// <summary>A bullet's shot: the shooter's gun action sequence number (0 for arrows), so their client knows it; a battery's shot's number.</summary>
    public uint Seq { get; init; } = Seq;

    /// <summary>A battery's rocket: its battery's number + 1 (0: a gun's or a bow's), so clients fly it under its motor (IW3).</summary>
    public int Battery { get; init; }
}
