using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>To everyone near (GROUND.md §6.4.2): something was destroyed: a deployable or world prop broken, a building piece, door or claim device broken, brought down, decayed or demolished, a ship wrecked. Made in one place, DamageSystem.Destroyed. (GROUND_SYSTEMS.md §12.3)</summary>
public readonly record struct Destroyed(
    EntityId Player = default,
    EntityId Id = default,
    TargetRef Target = default,
    string Key = "",
    byte Grade = 0,
    Vector3 Position = default,
    Quaternion Rotation = default,
    Vector3 Direction = default,
    DestroyCause Cause = default,
    ushort Weapon = 0,
    bool Open = false) : IZoneEvent
{
    /// <summary>Who did it, or none.</summary>
    public EntityId Player { get; init; } = Player;

    /// <summary>The thing, by ID.</summary>
    public EntityId Id { get; init; } = Id;

    /// <summary>The thing: its family and ID.</summary>
    public TargetRef Target { get; init; } = Target;

    /// <summary>Its model key (a piece's with its grade).</summary>
    public string Key { get; init; } = Key;

    /// <summary>Where it stood (zone frame, as it is drawn).</summary>
    public Vector3 Position { get; init; } = Position;

    /// <summary>Its turn as it was drawn.</summary>
    public Quaternion Rotation { get; init; } = Rotation;

    /// <summary>The blow's direction (zero for a collapse, decay or demolition).</summary>
    public Vector3 Direction { get; init; } = Direction;

    /// <summary>What did it, by item.</summary>
    public ushort Weapon { get; init; } = Weapon;

    /// <summary>A door that was open.</summary>
    public bool Open { get; init; } = Open;
}
