using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;

namespace Hollow.Simulation;

// People on a vehicle's back (VEHICLES.md §6.4): a vehicle on wheels keeps a solid body as it moves, and the people standing
// on it weigh on its springs. A landing is a blow there, a jump a push, walking about moves the weight; each wakes it.
public sealed partial class ShipSystem
{
    /// <summary>What a person standing on a vehicle weighs on its springs (kg): a person and what they carry.</summary>
    public const float RiderMass = 85;

    /// <summary>
    /// A landing's blow and a jump's push are the person's momentum times this: less than all of it (1), as legs take a
    /// landing over a moment and give some back, so a person jumping on its back rocks it by a hand's breadth, not a foot.
    /// </summary>
    public const float RiderKick = 0.7f;

    /// <summary>Slower than this along up (m/s), a landing or a step off is only the weight coming or going.</summary>
    private const float RiderBlowSpeed = 0.5f;

    /// <summary>Feet this close over the top of a vehicle's box stand on it (m).</summary>
    private const float RiderGap = 0.12f;

    /// <summary>A vehicle asleep under its riders wakes when their weight changes by this (kg) or its centre moves this far (m).</summary>
    private const float RiderLoadChange = 1, RiderShift = 0.3f;

    // Who stood on which vehicle at the last tick and where (in its frame), by character; and each character near a vehicle's
    // speed along up at its last two ticks (a landing's speed is the one before it touched).
    private readonly Dictionary<ulong, (ulong Ship, Vector3 Local, long Tick)> _riders = new();
    private readonly Dictionary<ulong, (long Tick, float Up, float Before)> _upSpeeds = new();
    private readonly List<ulong> _ridersGone = new();

    /// <summary>People standing on vehicles at the last tick (statistics, tests).</summary>
    public int RiderCount => _riders.Count;

    /// <summary>Whether a ship keeps a solid body in the zone while it moves: a vehicle on wheels people only sit in.</summary>
    private static bool KeepsBody(Ship ship) => ship.Def.Ground != null && ship.Def.Interior == null;

    /// <summary>
    /// A vehicle's body follows it (made when it has none, moved when it moved): asleep, on the wire's grid
    /// (<see cref="ShipMotor.RestOnGrid"/>), so every client's copy stands exactly where the server's does.
    /// </summary>
    private void KeepBody(Ship ship)
    {
        if (ship.State.Asleep)
        {
            ShipMotor.RestOnGrid(ref ship.State);
        }
        Vector3 at = ship.State.Position;
        Quaternion turn = ship.State.Rotation;
        if (ship.ParkedBody == 0)
        {
            ship.ParkedBody = ShipCollider.AddParked(ship.Def, at, turn);
        }
        else if (ship.BodyPosition != at || ship.BodyRotation != turn)
        {
            ShipCollider.MoveParked(ship.ParkedBody, at, turn);
        }
        ship.BodyPosition = at;
        ship.BodyRotation = turn;
    }

    /// <summary>
    /// The people standing on a vehicle on wheels this tick (after they moved, before it steps): each one's weight on it
    /// while it is awake; one who lands gives it a blow by the speed they fell at, one who jumps off a push by the speed
    /// they left at; asleep, it wakes when someone comes, goes, lands, jumps or their weight moves.
    /// </summary>
    private void Riders(Ship ship)
    {
        if (!KeepsBody(ship) || ship.Destroyed || ship.InOrbit)
        {
            return;
        }
        ShipDef def = ship.Def;
        float gravity = ShipContext.GravityOf(Spec.SurfaceGravity);
        float reach = def.BoundingRadius + 3f;
        ComponentPool<Character> characters = Entities.Pool<Character>();
        Span<Character> values = characters.Values;
        float load = 0;
        Vector3 loadAt = Vector3.Zero;
        bool wake = false;
        ShipPose pose = ShipFrame.PoseOf(ship.State, def.CentreOfMass);
        for (int i = 0; i < values.Length; i++)
        {
            ref Character c = ref values[i];
            if (c.State.Frame != 0 || c.State.IsDead || c.State.Stance == Stance.Seated || c.State.Has(MoveCheats.Noclip) ||
                Vector3.DistanceSquared(c.State.Position, pose.Position) > reach * reach)
            {
                continue;
            }
            ulong id = Entities.IdOf(Entities.AtSlot(characters.Slots[i])).Value;
            Vector3 feet = c.State.Position, up = Frame.UpAt(feet);
            (long Tick, float Up, float Before) speeds = _upSpeeds.GetValueOrDefault(id, (Tick - 1, 0, 0));
            if (speeds.Tick != Tick)
            {
                speeds = (Tick, Vector3.Dot(c.State.Velocity, up), speeds.Tick == Tick - 1 ? speeds.Up : 0);
                _upSpeeds[id] = speeds;
            }
            bool was = _riders.TryGetValue(id, out (ulong Ship, Vector3 Local, long Tick) rode) && rode.Ship == ship.Id.Value && rode.Tick == Tick - 1;
            Vector3 contact = feet;
            bool on = (c.State.Flags & MoveFlags.OnGround) != 0 && StandsOn(ship, pose, feet, up, out contact);
            if (on)
            {
                Vector3 local = pose.ToLocal(contact);
                _riders[id] = (ship.Id.Value, local, Tick);
                load += RiderMass;
                loadAt += local * RiderMass;
                if (!was)
                {
                    // Came aboard: the blow of the fall that ended here (none for a step across from the ground).
                    float falling = -speeds.Before;
                    if (falling > RiderBlowSpeed)
                    {
                        ShipMotor.Apply(ref ship.State, def, contact - ship.State.Position, -up * (RiderMass * falling * RiderKick));
                    }
                    wake = true;
                }
                else if (!ship.State.Asleep)
                {
                    ShipMotor.Apply(ref ship.State, def, contact - ship.State.Position, -up * (RiderMass * gravity * CharacterMotor.DeltaTime));
                }
            }
            else if (was)
            {
                // Off it: a jump pushes down where they stood.
                _riders.Remove(id);
                float rising = speeds.Up - Vector3.Dot(pose.VelocityAt(pose.ToWorld(rode.Local)), up);
                if (rising > RiderBlowSpeed)
                {
                    Vector3 from = pose.ToWorld(rode.Local);
                    ShipMotor.Apply(ref ship.State, def, from - ship.State.Position, -up * (RiderMass * rising * RiderKick));
                }
                wake = true;
            }
        }
        if (load > 0)
        {
            loadAt /= load;
        }
        if (ship.State.Asleep && (MathF.Abs(load - ship.RestLoad) > RiderLoadChange || Vector3.Distance(loadAt, ship.RestLoadAt) > RiderShift))
        {
            wake = true;
        }
        if (wake || !ship.State.Asleep)
        {
            ship.RestLoad = load;
            ship.RestLoadAt = loadAt;
        }
        if (wake)
        {
            WakeShip(ship);
        }
        ForgetRiders();
    }

    // A character's feet on top of one of a vehicle's hull boxes (the upward face, within the gap), thickened by a part of
    // the capsule's radius so an edge still carries them: the point under them.
    private bool StandsOn(Ship ship, in ShipPose pose, Vector3 feet, Vector3 up, out Vector3 contact)
    {
        contact = feet;
        Vector3 x = Vector3.Transform(Vector3.UnitX, pose.Rotation);
        Vector3 y = Vector3.Transform(Vector3.UnitY, pose.Rotation);
        Vector3 z = Vector3.Transform(Vector3.UnitZ, pose.Rotation);
        float margin = Tuning.Radius * 0.6f, above = 0.3f;
        float best = -1;
        foreach (HullBox box in ship.Def.Hull)
        {
            if (box.Fragile)
            {
                continue;
            }
            Vector3 half = box.HalfExtents + new Vector3(margin, 0, margin);
            Vector3 centre = pose.ToWorld(box.Centre);
            float t = Geometry.RayBox(feet + up * above, -up, above + RiderGap, centre, half, x, y, z);
            if (t < 0 || (best >= 0 && t >= best))
            {
                continue;
            }
            Vector3 point = feet + up * (above - t);
            if (Vector3.Dot(ContactGeometry.BoxNormal(point, centre, half, x, y, z), up) < 0.7f)
            {
                continue; // a side, not a top
            }
            best = t;
            contact = point;
        }
        return best >= 0;
    }

    // Once a second: who no longer stands anywhere, and speeds of characters gone from near the vehicles.
    private void ForgetRiders()
    {
        if (Tick % CharacterMotor.TickRate != 0 || (_riders.Count == 0 && _upSpeeds.Count == 0))
        {
            return;
        }
        _ridersGone.Clear();
        foreach ((ulong id, (ulong Ship, Vector3 Local, long Tick) rode) in _riders)
        {
            if (rode.Tick < Tick - 1)
            {
                _ridersGone.Add(id);
            }
        }
        foreach (ulong id in _ridersGone)
        {
            _riders.Remove(id);
        }
        _ridersGone.Clear();
        foreach ((ulong id, (long Tick, float Up, float Before) speeds) in _upSpeeds)
        {
            if (speeds.Tick < Tick - 1)
            {
                _ridersGone.Add(id);
            }
        }
        foreach (ulong id in _ridersGone)
        {
            _upSpeeds.Remove(id);
        }
    }
}
