using System;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation;

/// <summary>What broke something (GROUND.md §9.4): it decides how the pieces fly (the client's gibs.json).</summary>
public enum DestroyCause : byte
{
    Melee,
    Bullet,
    Arrow,
    Explosion,

    /// <summary>A building piece came down for want of support.</summary>
    Collapse,

    /// <summary>A building piece decayed away, unpaid.</summary>
    Decay,

    /// <summary>A building piece taken down with the hammer.</summary>
    Demolish,

    /// <summary>Burnt (heat damage): none breaks this way yet.</summary>
    Fire,
}

/// <summary>A blow: what struck it, the way it travelled (zero: none) and who struck it (GROUND.md §9.4).</summary>
public readonly record struct Blow(DestroyCause Cause, Vector3 Direction, EntityId By)
{
    /// <summary>A blow travelling along <paramref name="travel"/> (any length; zero: no direction).</summary>
    public static Blow Along(DestroyCause cause, Vector3 travel, EntityId by) =>
        new(cause, travel.LengthSquared() > 1e-10f ? Vector3.Normalize(travel) : Vector3.Zero, by);

    /// <summary>
    /// What a part removed from the building was broken by: <paramref name="destroyedBy"/> when damage broke it, else
    /// the removal itself (a collapse, decay, the hammer); null when it was taken away whole (picked up, forgotten).
    /// </summary>
    public static Blow? Of(BuildingRemoval removal, in Blow destroyedBy) => removal switch
    {
        BuildingRemoval.Destroyed => destroyedBy,
        BuildingRemoval.Collapsed => new Blow(DestroyCause.Collapse, Vector3.Zero, EntityId.None),
        BuildingRemoval.Decayed => new Blow(DestroyCause.Decay, Vector3.Zero, EntityId.None),
        BuildingRemoval.Demolished => new Blow(DestroyCause.Demolish, Vector3.Zero, EntityId.None),
        _ => null,
    };
}

/// <summary>
/// A model of the catalogue (<c>game/data/ground/models.json</c>) as it stands in the zone: its key, the part of a kit (a
/// building piece's or a door's key; empty for a whole model), its origin in the zone frame, its turn and its size. What
/// the client breaks into gibs when the thing is destroyed (GROUND.md §9.4).
/// </summary>
public readonly record struct ModelPlacement(string Model, string Part, Vector3 Position, Quaternion Rotation, float Scale = 1)
{
    /// <summary>
    /// The turn of a thing standing on the ground: its up along <paramref name="up"/>, turned by <paramref name="yaw"/>
    /// about it, exactly as the client draws deployables, props and outcrops (Godot's <c>Basis.LookingAt</c> of the
    /// horizontal forward: the model's −Z along it).
    /// </summary>
    public static Quaternion Upright(Vector3 up, float yaw)
    {
        (Vector3 forward, _) = CharacterMotor.HorizontalBasis(yaw, up);
        Vector3 z = -Vector3.Normalize(forward);
        Vector3 x = Vector3.Normalize(Vector3.Cross(up, z));
        Vector3 y = Vector3.Cross(z, x);
        // System.Numerics is row-vector: the rows are the images of the axes.
        var m = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    /// <summary>The turn <paramref name="rotation"/> followed by half a turn about its own up (a door fitted the other way round).</summary>
    public static Quaternion Flipped(Quaternion rotation, int halfTurns) =>
        halfTurns % 2 == 0 ? rotation : Quaternion.Normalize(rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI));
}
