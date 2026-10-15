using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;

namespace Hollow.Simulation;

// Ships people walk in (IRON_WAKE.md §4): who is aboard, a parked ship's body in the zone, and the one entry point that
// moves a ship's parts.
public sealed partial class ShipSystem
{
    /// <summary>Everyone aboard a ship into the zone's frame where it carries them, with its velocity there (a wreck, a ship gone).</summary>
    private void Ashore(Ship ship)
    {
        if (ship.Frame == null)
        {
            return;
        }
        ComponentPool<Character> characters = Entities.Pool<Character>();
        Span<Character> values = characters.Values;
        for (int i = 0; i < values.Length; i++)
        {
            ref Character c = ref values[i];
            if (c.State.Frame == ship.Frame.Key)
            {
                if (c.State.Stance == Stance.Seated)
                {
                    c.State.Stance = Stance.Standing;
                    Entities.Remove<ShipSeat>(Entities.AtSlot(characters.Slots[i]));
                }
                Zone.ToZoneFrame(ref c.State);
            }
        }
    }

    /// <summary>The characters aboard a ship (the frame's key is its ID), for the rules that touch only them.</summary>
    public void Aboard(Ship ship, List<Entity> into)
    {
        into.Clear();
        ComponentPool<Character> characters = Entities.Pool<Character>();
        for (int i = 0; i < characters.Count; i++)
        {
            if (characters.Values[i].State.Frame == ship.Id.Value)
            {
                into.Add(Entities.AtSlot(characters.Slots[i]));
            }
        }
    }

    /// <summary>
    /// A ship asleep with nobody at the helm becomes a static body in the zone's physics (§4.6): the hull's boxes for a
    /// ship people only sit in, the whole collision of one they walk in (a compound of its pieces at its parts' poses,
    /// shared by every ship of the kind with the same parts' states), so people outside walk round its legs and up its
    /// ramp, and a character standing on it is aboard. Its pose rests on the wire's grid first
    /// (<see cref="ShipMotor.RestOnGrid"/>), so every client places the same body.
    /// </summary>
    private void Park(Ship ship)
    {
        ShipMotor.RestOnGrid(ref ship.State);
        ship.ParkedBody = ship.Def.Interior is { } interior
            ? ShipCollider.AddParked(ShipSpace.ParkedShape(interior, ship.Parts), ship.State.Position, ship.State.Rotation)
            : ShipCollider.AddParked(ship.Def, ship.State.Position, ship.State.Rotation);
    }

    /// <summary>
    /// Moves a ship's part to a state (IRON_WAKE.md §4.7), the one entry point for every moving part: a door's progress
    /// (0 shut, 1 open), a turret's angle. A door refuses to move into a character, whoever stands where its pieces would
    /// pass on the way, aboard or ashore (the building doors' rule: <see cref="OutcomeCode.RampBlocked"/>); a turret's
    /// platform turns anyway, and the character it meets is pushed out on its next step. The space's bodies follow, a
    /// parked ship's body in the zone is made again at the new states, and the clients that know the ship are told
    /// (<see cref="Ship.PartsRevision"/>). Callers hold <see cref="JoltRuntime.Gate"/> or run outside the step.
    /// </summary>
    public Outcome MovePart(Ship ship, int part, float state)
    {
        if (ship.Def.Interior is not { } interior || ship.Space is not { } space || part <= 0 || part >= interior.Parts.Count ||
            !float.IsFinite(state))
        {
            return OutcomeCode.Invalid;
        }
        if (ship.Def.DoorOfPart(part) >= 0)
        {
            return OutcomeCode.NotAllowed; // its door's to move (SetDoor), as every machine works it out
        }
        ShipPartDef def = interior.Parts[part];
        if (def.IsDoor)
        {
            state = Math.Clamp(state, 0f, 1f);
        }
        if (ship.Parts[part] == state)
        {
            return Outcome.Done;
        }
        float[] to = (float[])ship.Parts.Clone();
        to[part] = state;
        lock (JoltRuntime.Gate)
        {
            if (def.IsDoor && space.Sweeps(to, CapsulesNear(ship)))
            {
                return OutcomeCode.RampBlocked;
            }
            ship.Parts[part] = state;
            space.Pose(ship.Parts);
            if (ship.ParkedBody != 0)
            {
                ShipCollider.RemoveParked(ship.ParkedBody);
                Park(ship);
            }
        }
        ship.PartsRevision++;
        return Outcome.Done;
    }

    /// <summary>
    /// Clears the ground a ship would stand on of what grows and lies there, for good (an admin's spawn of a big ship in a
    /// wood, IRON_WAKE.md §5.6): where nothing else is in its way, every tree and rock whose body its hull would overlap
    /// standing at a point, its nose toward a yaw, is felled or broken. How many went.
    /// </summary>
    public int ClearNatureFor(ShipDef def, Vector3 ground, float yaw)
    {
        if (Zone.Natural is not { } natural)
        {
            return 0;
        }
        Vector3 up = Frame.UpAt(ground);
        ShipState probe = ShipMotor.Standing(def, ground + up * 0.05f, up, yaw);
        Span<ShipContact> contacts = stackalloc ShipContact[64];
        int cleared = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            int found;
            lock (JoltRuntime.Gate)
            {
                Physics.EnsureGround(ground);
                found = ShipCollider.Overlaps(def, probe.Position, probe.Rotation, ShipCollider.NoBody, contacts);
            }
            // Only where nothing else is in the way: the ground no deeper than a hull may sit in it, no building, ship or prop.
            for (int i = 0; i < found; i++)
            {
                bool nature = Physics.TryPropOf(contacts[i].Body, out PropLayer layer, out _) && layer == PropLayer.Nature;
                if (!nature && (!PhysicsWorld.IsGroundBody(contacts[i].Body) || contacts[i].Depth > Hollow.Simulation.Placement.HullFootprint.GroundDepth))
                {
                    return cleared;
                }
            }
            int before = cleared;
            for (int i = 0; i < found; i++)
            {
                if (Physics.TryPropOf(contacts[i].Body, out PropLayer layer, out ulong key) && layer == PropLayer.Nature && !natural.IsDepleted(key))
                {
                    natural.Apply(key, new NaturalOverride { Depleted = true, RespawnAtUnixMs = long.MaxValue });
                    cleared++;
                }
            }
            if (cleared == before)
            {
                break;
            }
        }
        return cleared;
    }

    private readonly List<Capsule> _partCharacters = new();

    // Every living character a ship's moving part could meet, in the ship's frame: those aboard where they stand, those
    // ashore within its reach turned into its frame (their up with them: the ship may stand on a slope).
    private ReadOnlySpan<Capsule> CapsulesNear(Ship ship)
    {
        _partCharacters.Clear();
        ShipPose pose = ShipFrame.PoseOf(ship.State, ship.Def.CentreOfMass);
        float reach = ship.Def.Interior!.Reach;
        foreach (Character c in Entities.Pool<Character>().Values)
        {
            if (c.State.IsDead || c.State.Stance == Stance.Seated)
            {
                continue;
            }
            float height = c.State.Stance == Stance.Standing ? Tuning.StandingHeight : Tuning.CrouchingHeight;
            if (c.State.Frame == ship.Id.Value)
            {
                _partCharacters.Add(new Capsule(c.State.Position, Vector3.UnitY, Tuning.Radius, height));
            }
            else if (c.State.Frame == 0 && Vector3.DistanceSquared(c.State.Position, pose.Position) <= reach * reach)
            {
                _partCharacters.Add(new Capsule(pose.ToLocal(c.State.Position), pose.DirectionToLocal(Frame.UpAt(c.State.Position)), Tuning.Radius, height));
            }
        }
        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_partCharacters);
    }
}
