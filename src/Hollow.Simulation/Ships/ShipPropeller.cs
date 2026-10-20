using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Hollow.Simulation.Content;
using Hollow.Simulation.Content.Readers;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Ships;

/// <summary>
/// An outboard motor (ships.json <c>"drive": { "kind": "propeller" }</c>, docs/MOTORBOAT.md §3): a propeller that pushes
/// only while it is under water, along the nose, from where it is (so a boat lifts its bow a little under power), and a
/// leg that steers by the water flowing past it (the boat's way through the water and the propeller's wash), so a boat
/// turns as it moves, the other way going astern, and barely at rest. The throttle asks for a speed ahead (or astern)
/// and the motor works toward it within its thrust; at zero it idles and the boat coasts. Nothing lifts, strafes, pitches
/// or rolls: the hull's buoyancy does that (<see cref="ShipBuoyancy"/>).
/// </summary>
public sealed class ShipPropeller : IShipDrive
{
    public const string Key = "propeller";

    /// <summary>The propeller, in the ship's frame: it pushes while under the water there.</summary>
    public Vector3 At { get; init; }

    /// <summary>The water past the leg (m/s) that gives full steering.</summary>
    public float Rudder { get; init; }

    /// <summary>The water the propeller's wash sends past the leg at full throttle (m/s): steering with little way on.</summary>
    public float Wash { get; init; }

    /// <summary>A propeller this far under the water (m) pushes fully; less, by how far it is.</summary>
    public const float FullDepth = 0.1f;

    public string Kind => Key;

    public bool IdlesOnSkids => true;

    internal static IShipDrive Read(JsonFields f, JsonElement block, string where)
    {
        f.OnlyKnown(block, where, "kind", "at", "rudder", "wash");
        return new ShipPropeller
        {
            At = f.Vector(block, "at", where),
            Rudder = f.Number(block, "rudder", where),
            Wash = f.Number(block, "wash", where, 0),
        };
    }

    public void Advance(ref ShipState s, in ShipDriveContext c, in ShipInput input, ShipDef def)
    {
    }

    /// <summary>How far the propeller is in the water, 0 … 1.</summary>
    public float Bite(in ShipState s, in ShipDriveContext c, ShipDef def)
    {
        if (c.Water <= 0)
        {
            return 0;
        }
        Vector3 at = s.Position + Vector3.Transform(At - def.CentreOfMass, c.Rotation);
        return Math.Clamp((float)(c.Water - c.World.Frame.ToBody(at).Length) / FullDepth, 0, 1);
    }

    public void Command(in ShipDriveContext c, ref ShipState s, in ShipInput input, ShipDef def, out Vector3 thrust, out Vector3 turn)
    {
        const float dt = CharacterMotor.DeltaTime;
        thrust = Vector3.Zero;
        turn = Vector3.Zero;
        float bite = Bite(s, c, def);
        if (bite <= 0)
        {
            return; // out of the water: the motor races and nothing moves
        }
        float m = def.Mass;
        Vector3 v = c.VelocityLocal;
        float push = 0;
        if (MathF.Abs(input.Throttle) >= 0.05f)
        {
            // Toward the speed the throttle asks for, counting the water's and the air's drag ahead.
            float target = input.Throttle >= 0 ? input.Throttle * def.ForwardSpeed : input.Throttle * def.ReverseSpeed;
            float drag = ShipMotor.Drag(v, c.Density, def).Z + (def.Buoyancy?.DragLocal(v, c.Submerged > 0 ? 1 : 0, def).Z ?? 0);
            float need = (target - v.Z) / dt - c.GravityLocal.Z - drag;
            push = Math.Clamp(need, -def.RetroThrust / m, def.MainThrust / m) * bite;
            // Never against the throttle: a boat going faster than asked coasts down rather than reversing.
            push = input.Throttle > 0 ? MathF.Max(push, 0) : MathF.Min(push, 0);
        }
        thrust = new Vector3(0, 0, push);
        // Pushed from the propeller, below and behind the centre of mass: the bow rises a little under power.
        Vector3 arm = At - def.CentreOfMass;
        turn = Vector3.Cross(arm, thrust * m) / def.Inertia;
        // The leg steers by the water past it: the way on and the wash; going astern it turns the other way.
        float flow = v.Z + Wash * input.Throttle;
        float authority = Rudder > 0 ? Math.Clamp(flow / Rudder, -1, 1) * bite : 0;
        float rate = -input.Yaw * def.Yaw.MaxRate * authority;
        turn.Y += Math.Clamp((rate - s.AngularVelocity.Y) / dt, -def.Yaw.Acceleration, def.Yaw.Acceleration) * MathF.Abs(authority);
    }

    public float Effort(Vector3 thrust, ShipDef def) => Math.Min(1f, MathF.Abs(thrust.Z) * def.Mass / def.MainThrust);

    public Vector3 Output(Vector3 thrust, ShipDef def) =>
        new(0, 0, thrust.Z >= 0 ? thrust.Z * def.Mass / def.MainThrust : thrust.Z * def.Mass / def.RetroThrust);

    public IEnumerable<string> Problems(ShipDef ship)
    {
        if (ship.MainThrust <= 0 || ship.RetroThrust <= 0 || ship.ForwardSpeed is <= 0 or > 100 || ship.ReverseSpeed is <= 0 or > 100)
        {
            yield return "propeller: thrust ahead and astern, speeds 0 … 100 m/s";
        }
        if (ship.Yaw.MaxRate <= 0 || ship.Yaw.Acceleration <= 0 || Rudder <= 0 || Wash < 0)
        {
            yield return "propeller: a yaw rate and acceleration, a positive rudder speed, no negative wash";
        }
        if (ship.Buoyancy == null)
        {
            yield return "propeller: a boat floats on its hull (buoyancy)";
        }
        else if (ship.Buoyancy.Points.Count > 0 && At.Y >= ship.Buoyancy.Points[0].Y + ship.Buoyancy.Depth)
        {
            yield return "propeller: below the gunwale, where the water is";
        }
    }
}
