using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;

namespace Hollow.Simulation.Ships;

/// <summary>
/// A hull that floats on its own shape (ships.json <c>buoyancy</c>, docs/MOTORBOAT.md §3): the water under it is a column
/// of <see cref="Depth"/> above each of its <see cref="Points"/> (the keel line at a station, in the ship's frame), each
/// displacing its share of <see cref="Volume"/> as far as it is under. The lift of each pushes where it is, so a hull rides
/// level, pitches and rolls back, and settles at the draft its mass gives; a ship without the block floats by the band
/// round its centre of mass (ships.json <c>water</c>). The water is the sea, a lake or a stream
/// (<see cref="Hollow.Surface.ISurface.WaterLevelAt"/>), its level taken under the centre of mass once a tick. In the
/// water the hull drags, a little ahead, much more sideways (its keel) and up and down, and its turning is damped.
/// </summary>
public sealed class ShipBuoyancy
{
    public const float WaterDensity = 1000f;

    /// <summary>Each column's foot (the hull's bottom at a station), in the ship's frame; at least three, not all in a line.</summary>
    public IReadOnlyList<Vector3> Points { get; init; } = Array.Empty<Vector3>();

    /// <summary>How tall each column is (metres, along the ship's up): from the bottom to the gunwale.</summary>
    public float Depth { get; init; }

    /// <summary>The water the hull displaces with every column under (m³), shared evenly.</summary>
    public float Volume { get; init; }

    /// <summary>Linear damping in the water along the ship's x (sideways), y (up) and z (ahead), per second.</summary>
    public Vector3 Damping { get; init; }

    /// <summary>The hull's drag areas in the water (Cd × m²) sideways, up and down, and ahead.</summary>
    public Vector3 Drag { get; init; }

    /// <summary>Turning damped in the water about the ship's x (pitch), y (yaw) and z (roll), per second.</summary>
    public Vector3 TurnDamping { get; init; }

    /// <summary>
    /// The water's surface under a point, as its distance from the world's centre (metres), or 0 where there is none.
    /// <paramref name="body"/> is the point in the body frame, <paramref name="radius"/> its length.
    /// </summary>
    public static double WaterRadius(Hollow.Surface.ISurface surface, Vector3D body, double radius)
    {
        if (radius <= 0)
        {
            return 0;
        }
        double level = surface.WaterLevelAt(body / radius);
        return double.IsFinite(level) ? surface.Radius + level : 0;
    }

    /// <summary>How far one column is under the water, 0 … 1, for its foot at <paramref name="foot"/> in the zone's frame.</summary>
    private float Under(in ShipContext ctx, Vector3 foot, double water) =>
        water <= 0 ? 0 : Math.Clamp((float)(water - ctx.Frame.ToBody(foot).Length) / Depth, 0, 1);

    /// <summary>The share of the hull's volume under the water now, 0 … 1 (0: out of it, or no water).</summary>
    public float Submerged(in ShipState s, ShipDef def, in ShipContext ctx, double water)
    {
        if (water <= 0 || Points.Count == 0)
        {
            return 0;
        }
        float sum = 0;
        foreach (Vector3 p in Points)
        {
            sum += Under(ctx, s.Position + Vector3.Transform(p - def.CentreOfMass, s.Rotation), water);
        }
        return sum / Points.Count;
    }

    /// <summary>
    /// The water's forces on the hull for a substep, added to its acceleration (the zone's frame) and its angular
    /// acceleration (the ship's frame): each column's lift where it pushes, the drag and the damping by how much of the
    /// hull is in the water.
    /// </summary>
    public void Apply(in ShipState s, ShipDef def, in ShipContext ctx, double water, ref Vector3 accel, ref Vector3 angular)
    {
        if (water <= 0 || Points.Count == 0)
        {
            return;
        }
        Quaternion q = s.Rotation;
        Quaternion qi = Quaternion.Conjugate(q);
        Vector3 shipUp = Vector3.Transform(Vector3.UnitY, q);
        float share = WaterDensity * Volume / Points.Count * ctx.Gravity;
        int wet = 0;
        foreach (Vector3 p in Points)
        {
            Vector3 foot = s.Position + Vector3.Transform(p - def.CentreOfMass, q);
            float under = Under(ctx, foot, water);
            if (under <= 0)
            {
                continue;
            }
            wet++;
            // The lift of the column's wet part, at its middle.
            Vector3 lift = ctx.Frame.UpAt(foot) * (share * under);
            Vector3 arm = foot + shipUp * (Depth * under * 0.5f) - s.Position;
            accel += lift / def.Mass;
            angular += Vector3.Transform(Vector3.Cross(arm, lift), qi) / def.Inertia;
        }
        if (wet == 0)
        {
            return;
        }
        float inWater = (float)wet / Points.Count;
        accel += Vector3.Transform(DragLocal(Vector3.Transform(s.Velocity, qi), inWater, def), q);
        angular -= s.AngularVelocity * TurnDamping * inWater;
    }

    /// <summary>The water's drag and damping as an acceleration in the ship's frame, for a velocity in it, with a share of the hull wet.</summary>
    public Vector3 DragLocal(Vector3 vLocal, float inWater, ShipDef def)
    {
        if (inWater <= 0)
        {
            return Vector3.Zero;
        }
        float speed = vLocal.Length();
        Vector3 quadratic = speed < 1e-6f ? Vector3.Zero : vLocal * Drag * (-0.5f * WaterDensity * speed / def.Mass);
        return (quadratic - vLocal * Damping) * inWater;
    }

    /// <summary>What is wrong with the numbers, each a sentence (the content check).</summary>
    public IEnumerable<string> Problems(ShipDef ship)
    {
        if (Points.Count < 3 || Depth <= 0 || Volume <= 0)
        {
            yield return "buoyancy: at least three points, a positive depth and volume";
        }
        else if (WaterDensity * Volume <= ship.Mass)
        {
            yield return $"buoyancy: {Volume} m³ can't float {ship.Mass} kg";
        }
        if (Damping.X < 0 || Damping.Y < 0 || Damping.Z < 0 || Drag.X < 0 || Drag.Y < 0 || Drag.Z < 0 ||
            TurnDamping.X < 0 || TurnDamping.Y < 0 || TurnDamping.Z < 0)
        {
            yield return "buoyancy: damping, drag and turn damping can't be negative";
        }
    }
}
