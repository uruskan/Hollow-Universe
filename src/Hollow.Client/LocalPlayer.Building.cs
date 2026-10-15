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
using Hollow.Simulation.Targets;

namespace Hollow.Client;

// The building intents (GROUND.md §6.3, phase 2): the hammer's. Each returns its sequence number (0 if it couldn't be
// sent); the answer comes as every intent's does (IntentAnswered, and IntentRejected when refused). Optimistic like every
// intent: the materials it uses leave the inventory at once, and a refusal rolls back. Placing a piece, fitting a door or
// a lock and placing the tool cupboard are the placement intent's (LocalPlayer.Placement, GROUND_SYSTEMS.md §6); doors,
// locks and the cupboard are used through verbs (LocalPlayer.Interact, §5).
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
            w => new HammerIntent { Seq = _nextSeq, Action = HammerAction.Repair, Target = piece.Id.Value, Family = (byte)TargetFamily.Piece }.WriteMessage(w));
    }

    /// <summary>
    /// One hammer hit of repair on anything else the hammer mends, as the world names it (a ship's door or battery,
    /// IRON_WAKE.md §5.5); its cost is the server's to take.
    /// </summary>
    public uint Repair(in TargetRef target)
    {
        TargetRef t = target;
        return Intend(null, w => new HammerIntent
        {
            Seq = _nextSeq, Action = HammerAction.Repair, Target = t.Id, Family = (byte)t.Family, Part = t.Part,
        }.WriteMessage(w));
    }
}
