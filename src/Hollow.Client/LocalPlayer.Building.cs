using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;

namespace Hollow.Client;

// The building intents (GROUND.md §6.3, phase 2): the hammer's, doors', locks' and the claim device's. Each returns its
// sequence number (0 if it couldn't be sent); the answer comes as every intent's does (IntentAnswered, and IntentRejected
// when refused). Optimistic like every intent: the materials it uses leave the inventory at once, and a refusal rolls back.
// Placing a piece, fitting a door or a lock and placing the claim device are the placement intent's (LocalPlayer.Placement).
public sealed partial class LocalPlayer
{
    /// <summary>The replicated building the intents act on (set by the client).</summary>
    internal BuildingReplica? Building { get; set; }

    /// <summary>Whether the player carries an item of this building role (a building plan, a hammer) in their main or belt slots.</summary>
    public bool Carries(BuildingRole role)
    {
        foreach (Container c in Inventory.Carried)
        {
            for (int i = 0; i < c.Size; i++)
            {
                if (!c[i].IsEmpty && Content.Item(c[i].Item)?.Building == role)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Upgrades a piece to a grade with the hammer (the grade's cost leaves at once).</summary>
    public uint Upgrade(Piece piece, int grade)
    {
        BuildingData data = Content.Building;
        string material = grade >= 0 && grade < data.Grades.Count ? data.Grades[grade].Material : "";
        int cost = data.CostOf(piece.Def, grade);
        return Intend(new Pending(p => new ItemWallet(p.Content, p.Inventory.Carried).Take(material, cost)),
            w => new HammerIntent { Seq = _nextSeq, Action = HammerAction.Upgrade, Target = piece.Id.Value, Grade = (byte)Math.Clamp(grade, 0, 7) }.WriteMessage(w));
    }

    public uint Rotate(Piece piece) =>
        Intend(null, w => new HammerIntent { Seq = _nextSeq, Action = HammerAction.Rotate, Target = piece.Id.Value }.WriteMessage(w));

    public uint Demolish(Piece piece) =>
        Intend(null, w => new HammerIntent { Seq = _nextSeq, Action = HammerAction.Demolish, Target = piece.Id.Value }.WriteMessage(w));

    /// <summary>One hammer hit of repair (its cost leaves at once).</summary>
    public uint Repair(Piece piece)
    {
        BuildingData data = Content.Building;
        string material = data.Grades[piece.Grade].Material;
        double restored = Math.Min(data.RepairPerHit, piece.MaxHealth - piece.Health);
        int cost = restored > 0 ? piece.Structure.World.RepairCost(data.CostOf(piece.Def, piece.Grade), restored, piece.MaxHealth) : 0;
        return Intend(new Pending(p => new ItemWallet(p.Content, p.Inventory.Carried).Take(material, cost)),
            w => new HammerIntent { Seq = _nextSeq, Action = HammerAction.Repair, Target = piece.Id.Value }.WriteMessage(w));
    }

    public uint ToggleDoor(Door door) =>
        Intend(null, w => new DoorIntent { Seq = _nextSeq, Action = DoorAction.Toggle, Target = door.Id.Value }.WriteMessage(w));

    public uint PickUpDoor(Door door) =>
        Intend(null, w => new DoorIntent { Seq = _nextSeq, Action = DoorAction.PickUp, Target = door.Id.Value }.WriteMessage(w));

    public uint SetCode(Door door, string code) => LockAct(LockAction.SetCode, door, code);

    public uint EnterCode(Door door, string code) => LockAct(LockAction.EnterCode, door, code);

    public uint Lock(Door door) => LockAct(LockAction.Lock, door);

    public uint Unlock(Door door) => LockAct(LockAction.Unlock, door);

    public uint RemoveLock(Door door) => LockAct(LockAction.Remove, door);

    /// <summary>Cuts a key for a key lock (its owner; the wood leaves at once).</summary>
    public uint MakeKey(Door door)
    {
        BuildingData data = Content.Building;
        return Intend(new Pending(p => new ItemWallet(p.Content, p.Inventory.Carried).Take(data.KeyCostMaterial, data.KeyCost)),
            w => new LockIntent { Seq = _nextSeq, Action = LockAction.MakeKey, Door = door.Id.Value }.WriteMessage(w));
    }

    private uint LockAct(LockAction action, Door door, string code = "") =>
        Intend(null, w => new LockIntent { Seq = _nextSeq, Action = action, Door = door.Id.Value, Code = code }.WriteMessage(w));

    public uint Authorise(ClaimDevice device) => ClaimAct(ClaimAction.Authorise, device);

    public uint Deauthorise(ClaimDevice device) => ClaimAct(ClaimAction.Deauthorise, device);

    public uint ClearAuthorised(ClaimDevice device) => ClaimAct(ClaimAction.ClearList, device);

    public uint PickUpClaim(ClaimDevice device) => ClaimAct(ClaimAction.PickUp, device);

    private uint ClaimAct(ClaimAction action, ClaimDevice device) =>
        Intend(null, w => new ClaimIntent { Seq = _nextSeq, Action = action, Target = device.Id.Value }.WriteMessage(w));
}
