using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>To the attacker: their hit landed, with what it did. (GROUND_SYSTEMS.md §12.3)</summary>
public readonly record struct HitConfirmed(
    EntityId By = default,
    EntityId Victim = default,
    TargetRef Target = default,
    HitRegion Region = default,
    float Amount = 0,
    bool Flag = false,
    bool Downed = false,
    Vector3 Point = default,
    ushort Weapon = 0) : IZoneEvent
{
    /// <summary>Who hit.</summary>
    public EntityId By { get; init; } = By;

    /// <summary>What was hit, by ID (none for the ground or a natural thing).</summary>
    public EntityId Victim { get; init; } = Victim;

    /// <summary>What was hit (GROUND_SYSTEMS.md §3.1): its family, ID and part.</summary>
    public TargetRef Target { get; init; } = Target;

    /// <summary>The damage dealt (or a harvest's rate).</summary>
    public float Amount { get; init; } = Amount;

    /// <summary>Killed (a character or an animal); on the weak spot (a natural thing).</summary>
    public bool Flag { get; init; } = Flag;

    /// <summary>The hit knocked the victim down (wounded them), G4.</summary>
    public bool Downed { get; init; } = Downed;

    /// <summary>Where the blow landed.</summary>
    public Vector3 Point { get; init; } = Point;

    /// <summary>The item that hit (0: a bare hand or a blast).</summary>
    public ushort Weapon { get; init; } = Weapon;
}
