using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using Hollow.Surface;
using Hollow.Client.Feedback;

namespace Hollow.Client.Audio;

/// <summary>Foot contact in zone space; interiors use their deck, constructed floors their actual grade.</summary>
public static class FootstepEnvironment
{
    public static FootSurface At(GameContent content, ContactResponseData responses, ISurface surface, in ZoneFrame frame,
        BuildingWorld? building, Vector3 feet, ulong movementFrame = 0, ShipModule? ships = null,
        SurvivalModule? things = null, Vector3? frameFeet = null)
    {
        PhysicalSurfaceData materials = content.Surfaces;
        FootSurface Bank(byte id) => responses.Surface(materials.Key(id)).Footstep;
        if (movementFrame != 0)
        {
            uint frameId = movementFrame <= uint.MaxValue ? (uint)movementFrame : 0;
            byte part = 0;
            if (ships?.FrameOf(frameId) is { } aboard)
            {
                // Predicted feet and interpolated ship poses need not represent the same tick.
                // Use the character's own frame coordinates whenever they are available.
                Vector3 local = frameFeet ?? Vector3.Transform(feet - aboard.Ship.Origin, Quaternion.Inverse(aboard.Ship.Rotation));
                lock (JoltRuntime.Gate)
                {
                    if (aboard.Space.Ray(local + Vector3.UnitY * 0.25f, -Vector3.UnitY, 0.8f, out SpaceHit contact, things: true))
                    {
                        uint body = (uint)contact.Part;
                        if (aboard.Space.IsThing(body))
                        {
                            RemoteThing? thing = things?.ThingOf(new EntityId(aboard.Space.ThingOf(body)));
                            return Bank(materials.FromDeployable(thing?.Deployable(content)?.Key ?? ""));
                        }
                        part = (byte)aboard.Def.PartOfPieces(contact.Part);
                    }
                }
            }
            return Bank(materials.FromShip(ships?.ShipByNetId(frameId)?.Def?.Key ?? "", part));
        }
        Vector3 up = frame.UpAt(feet);
        if (building?.Physics is { } physics && physics.Ray(feet + up * 0.25f, -up, 0.8f, out RayHit hit))
        {
            if (physics.OwnerOf(hit.Body) is Piece piece) return Bank(materials.FromGrade(building.Data.Grades[piece.Grade].Key));
            if (hit.Body >= PhysicsWorld.FirstShipBody && hit.Body < PhysicsWorld.FirstShipBody + PhysicsWorld.MaxShipBodies)
            {
                return Bank(materials.FromShip(ships?.ShipForBody(hit.Body)?.Def?.Key ?? ""));
            }
            if (hit.IsGround && surface is WalkingWorld world)
            {
                Vector3D contact = frame.ToBody(hit.Point);
                Vector3D direction = contact.Normalized;
                // Scanned cliffs share the terrain collision tiles but protrude above the height field.
                // Monuments share those tiles too; their authored floors must not all become rock.
                if (world.Monuments.At(direction) == null && contact.Length - surface.Radius - surface.HeightAt(direction) > 0.5)
                    return Bank(materials.RockId);
            }
        }
        return Bank(materials.AtGround(surface, frame, feet));
    }

    /// <summary>Includes authored inland lakes/streams as well as sea water.</summary>
    public static double WaterDepth(ISurface surface, in ZoneFrame frame, Vector3 feet)
    {
        Vector3D point = frame.ToBody(feet);
        double radial = point.Length;
        return radial <= 0 ? 0 : Math.Max(0, surface.Radius + surface.WaterLevelAt(point / radial) - radial);
    }
}
