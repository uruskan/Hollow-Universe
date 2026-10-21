using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Placement;

namespace Hollow.Simulation.Building;

/// <summary>Checks an entire prefab site before creating any gameplay objects.</summary>
public static class BuildingBlueprintSpawner
{
    public static bool TryPlace(Zone zone, BuildingBlueprint blueprint, Vector3 at, double heading,
        int grade, ulong owner, out IReadOnlyList<Piece> pieces, out string reason)
    {
        pieces = Array.Empty<Piece>();
        reason = "This zone has no building.";
        if (zone.Building().World is not { } world) return false;
        if (grade < 0 || grade >= world.Data.Grades.Count)
        {
            reason = "Unknown building grade.";
            return false;
        }
        PlacementWorld placing = zone.Placement().World;
        Vector3 ground = zone.Settle(at), up = zone.Frame.UpAt(ground);
        BuildingFrame probe = BuildingFrame.At(D(ground), D(up), heading);
        double highest = double.NegativeInfinity, lowest = double.PositiveInfinity;
        foreach (Vector3D sample in blueprint.GroundSamples)
        {
            Vector3 point = F(probe.ToZone(sample));
            double height = -zone.HeightAboveGround(point);
            if (!double.IsFinite(height) || placing.Ground.InWater(zone.Settle(point)))
            {
                reason = "The entire base needs dry ground. Choose a clear, level site.";
                return false;
            }
            highest = Math.Max(highest, height);
            lowest = Math.Min(lowest, height);
        }
        // Keep foundations embedded and the entrance reachable on foot. No flattening of terrain.
        double lift = highest + world.Data.FoundationClearance + 0.02;
        Vector3 entrance = F(probe.ToZone(blueprint.Entrance));
        if (highest - lowest > 1.2 || zone.HeightAboveGround(entrance) + lift > 0.65)
        {
            reason = "This ground is too uneven for the whole base or its entrance. Choose a flatter site.";
            return false;
        }
        BuildingFrame frame = BuildingFrame.At(probe.Origin + D(up) * lift, D(up), heading);
        Vector3D min = blueprint.Min - new Vector3D(0.35, 0, 0.35);
        Vector3D max = blueprint.Max + new Vector3D(0.35, 0.35, 0.35);
        Vector3D centre = (min + max) * 0.5;
        Vector3 half = F((max - min) * 0.5);
        Vector3 zoneCentre = F(frame.ToZone(centre));
        float radius = half.Length() + 8;
        // Ensure nearby terrain/nature collision is present even when the admin is in noclip.
        foreach (Vector3D sample in blueprint.GroundSamples)
            zone.Physics.EnsureGround(F(frame.ToZone(sample)));
        (Vector3 boxBase, Quaternion rotation) = frame.ToZone(new Pose(new Vector3D(centre.X, min.Y, centre.Z), 0));
        bool blocked = placing.Overlaps(blueprint, half, boxBase, rotation).Count != 0;
        var characters = new List<Capsule>();
        placing.Around.CharactersNear(zoneCentre, radius, characters);
        foreach (Capsule character in characters)
            blocked |= Inside(character.Feet + character.Up * (character.Height / 2), character.Height / 2);
        // Some deployables have no physical body; they still must not be enclosed by a spawned base.
        foreach (PlacedThing thing in placing.ThingsNear(zoneCentre, radius))
            blocked |= Inside(thing.Centre, thing.Def.HalfExtents.Length());
        foreach (PlacedSpace ship in placing.SpacesNear(zoneCentre, radius + 64))
            blocked |= Inside(ship.Pose.Position, ship.Interior.Reach);
        if (blocked)
        {
            reason = "The base footprint is occupied by a player, building, object or ship. Choose a clear site.";
            return false;
        }
        pieces = blueprint.Place(world, frame, grade, owner, zone.Building().BuildingNow, zone.NextId);
        reason = "";
        return true;

        bool Inside(Vector3 point, double margin)
        {
            Vector3D local = frame.ToLocal(D(point));
            return local.X + margin >= min.X && local.X - margin <= max.X
                && local.Y + margin >= min.Y && local.Y - margin <= max.Y
                && local.Z + margin >= min.Z && local.Z - margin <= max.Z;
        }
    }

    private static Vector3 F(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);
    private static Vector3D D(Vector3 v) => new(v.X, v.Y, v.Z);
}
