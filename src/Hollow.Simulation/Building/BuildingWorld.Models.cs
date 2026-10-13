using System.Numerics;
using Hollow.Simulation.Entities;

namespace Hollow.Simulation.Building;

// The models the client draws, and breaks into gibs (GROUND.md §9.4), for what the building holds: a piece is a part of
// its grade's kit, a door a part of its kit. (The claim device is drawn as the deployable it also is.)
public sealed partial class BuildingWorld
{
    /// <summary>A piece as the catalogue's model: its grade's kit (building.json's <c>model</c>), the piece's key as the part.</summary>
    public ModelPlacement ModelOf(Piece piece)
    {
        (Vector3 position, Quaternion rotation) = piece.ZoneTransform;
        return new ModelPlacement(Data.Grades[piece.Grade].Model, piece.Def.Key, position, rotation);
    }

    /// <summary>A door as the catalogue's model: its kit, the door's key as the part, on its frame and turned as fitted.</summary>
    public static ModelPlacement ModelOf(Door door)
    {
        (Vector3 position, Quaternion rotation) = door.Frame.ZoneTransform;
        return new ModelPlacement(door.Def.Model, door.Def.Key, position, ModelPlacement.Flipped(rotation, door.Flip));
    }

    /// <summary>What a removal took away, as the catalogue's model: a piece or a door (null for anything else).</summary>
    public (EntityId Id, ModelPlacement Model)? Removed(in BuildingEvent e) => e.Change switch
    {
        BuildingChange.PieceRemoved when e.Piece is { } piece => (piece.Id, ModelOf(piece)),
        BuildingChange.DoorRemoved when e.Door is { } door => (door.Id, ModelOf(door)),
        _ => null,
    };
}
