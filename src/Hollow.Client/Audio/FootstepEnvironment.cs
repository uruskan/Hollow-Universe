using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Client.Audio;

/// <summary>Foot contact in zone space; interiors use their deck, constructed floors their actual grade.</summary>
public static class FootstepEnvironment
{
    public static FootSurface At(ISurface surface, in ZoneFrame frame, BuildingWorld? building, Vector3 feet, ulong movementFrame = 0)
    {
        if (movementFrame != 0) return FootSurface.Metal;
        Vector3 up = frame.UpAt(feet);
        if (building?.Physics is { } physics && physics.Ray(feet + up * 0.25f, -up, 0.8f, out RayHit hit))
        {
            if (physics.OwnerOf(hit.Body) is Piece piece) return Footsteps.FromGrade(building.Data.Grades[piece.Grade].Key);
            if (hit.Body >= PhysicsWorld.FirstShipBody && hit.Body < PhysicsWorld.FirstShipBody + PhysicsWorld.MaxShipBodies)
                return FootSurface.Metal;
            if (hit.IsGround && surface is WalkingWorld world)
            {
                Vector3D contact = frame.ToBody(hit.Point);
                Vector3D direction = contact.Normalized;
                // Scanned cliffs share the terrain collision tiles but protrude above the height field.
                // Monuments share those tiles too; their authored floors must not all become rock.
                if (world.Monuments.At(direction) == null && contact.Length - surface.Radius - surface.HeightAt(direction) > 0.5)
                    return FootSurface.Rock;
            }
        }
        return Footsteps.FromGround(MaterialAt(surface, frame, feet));
    }

    public static GroundMaterial MaterialAt(ISurface surface, in ZoneFrame frame, Vector3 feet)
    {
        Vector3D direction = frame.ToBody(feet).Normalized;
        double slope = Math.Sin(new GroundProbe(surface, frame).Normal(feet).Slope);
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        Span<double> materials = stackalloc double[BiomeTable.MaterialCount];
        surface.Cover(direction, surface.Sample(direction), slope, biomes, materials);
        BiomeTable.FoldFamilies(materials);
        int best = 0;
        for (int i = 1; i < materials.Length; i++) if (materials[i] > materials[best]) best = i;
        return (GroundMaterial)best;
    }

    /// <summary>Includes authored inland lakes/streams as well as sea water.</summary>
    public static double WaterDepth(ISurface surface, in ZoneFrame frame, Vector3 feet)
    {
        Vector3D point = frame.ToBody(feet);
        double radial = point.Length;
        return radial <= 0 ? 0 : Math.Max(0, surface.Radius + surface.WaterLevelAt(point / radial) - radial);
    }
}
