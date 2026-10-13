using System;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Entities;

namespace Hollow.Simulation.Building;

/// <summary>
/// A piece or door gone from the building as the Destroyed event names it (GROUND.md §6.4.2, §9.4): what it was, its key
/// and a piece's grade, where and how it was drawn, and whether a door's leaves were open.
/// </summary>
public readonly record struct RemovedPart(DestroyedKind Kind, EntityId Id, string Key, int Grade, Vector3 Position, Quaternion Rotation, bool Open);

// What the building's removals name, for the zone's Destroyed event and for anything that draws them breaking (the build
// sandbox): one mapping.
public sealed partial class BuildingWorld
{
    /// <summary>
    /// A removal's piece or door as the Destroyed event names it: a piece by its key and grade at its own transform, a door
    /// by its key on its frame piece, turned half round when it was fitted the other way (as it is drawn). Null for a claim
    /// device (drawn as the deployable it also is) and anything else.
    /// </summary>
    public static RemovedPart? Removed(in BuildingEvent e)
    {
        switch (e.Change)
        {
            case BuildingChange.PieceRemoved when e.Piece is { } piece:
            {
                (Vector3 position, Quaternion rotation) = piece.ZoneTransform;
                return new RemovedPart(DestroyedKind.Piece, piece.Id, piece.Def.Key, piece.Grade, position, rotation, false);
            }
            case BuildingChange.DoorRemoved when e.Door is { } door:
            {
                (Vector3 position, Quaternion rotation) = door.Frame.ZoneTransform;
                if (door.Flip % 2 != 0)
                {
                    rotation = Quaternion.Normalize(rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI));
                }
                return new RemovedPart(DestroyedKind.Door, door.Id, door.Def.Key, 0, position, rotation, door.Open);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// The building's own reason a removal broke something (collapse, decay, demolition); null when it was broken by a blow
    /// (whose cause the zone knows) or taken away whole.
    /// </summary>
    public static DestroyCause? CauseOf(Removal removal) => removal switch
    {
        Removal.Collapsed => DestroyCause.Collapse,
        Removal.Decayed => DestroyCause.Decay,
        Removal.Demolished => DestroyCause.Demolish,
        _ => null,
    };
}
