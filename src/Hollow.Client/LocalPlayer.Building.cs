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

/// <summary>A placement sent and not yet in the replicated building: drawn at once, gone when the piece arrives or is refused.</summary>
public sealed record PendingPiece(uint Seq, PieceDef Piece, BuildingFrame Frame, Pose Pose)
{
    /// <summary>When the server said it was done (client seconds), or NaN.</summary>
    public double AcceptedAt { get; internal set; } = double.NaN;

    public (Vector3 Position, Quaternion Rotation) ZoneTransform => Frame.ToZone(Pose);
}

// The building intents (GROUND.md §6.3, phase 2). Each returns its sequence number (0 if it couldn't be sent); the
// answer comes as every intent's does (IntentAnswered, and IntentRejected when refused). Optimistic like every intent:
// the materials or the item it uses leave the inventory at once, a placement is drawn at once (PendingPieces), and a
// refusal rolls back. Doors, locks and the tool cupboard are used through verbs (Interact, GROUND_SYSTEMS.md §5).
public sealed partial class LocalPlayer
{
    private readonly List<PendingPiece> _pendingPieces = new();

    /// <summary>The replicated building the intents act on (set by the client).</summary>
    internal BuildingReplica? Building { get; set; }

    /// <summary>Placements on their way: draw them like pieces until they arrive.</summary>
    public IReadOnlyList<PendingPiece> PendingPieces => _pendingPieces;

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

    /// <summary>Places what a plan from <see cref="GameClient.PlanPiece"/> shows (twig, paid at once).</summary>
    public uint PlacePiece(PlacementPlan plan)
    {
        BuildingData data = Content.Building;
        string material = data.Grades[0].Material;
        int cost = data.CostOf(plan.Piece, 0);
        uint seq = Intend(new Pending(p => new ItemWallet(p.Content, p.Inventory.Carried).Take(material, cost)), w => new PlacePieceIntent
        {
            Seq = _nextSeq,
            Piece = (byte)plan.Piece.Id,
            Support = plan.Support?.Id.Value ?? 0,
            Socket = (byte)(plan.Support != null ? plan.Socket.Code : 0),
            Orientation = (byte)plan.Orientation,
            X = plan.Point.X,
            Y = plan.Point.Y,
            Z = plan.Point.Z,
            Heading = (byte)plan.HeadingSteps,
        }.WriteMessage(w));
        if (seq != 0)
        {
            _pendingPieces.Add(new PendingPiece(seq, plan.Piece, plan.Frame, plan.Pose));
        }
        return seq;
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

    /// <summary>Fits the door in a slot into a doorway or wall frame, turned round or not.</summary>
    public uint PlaceDoor(ContainerRef from, int slot, Piece frame, bool flip = false)
    {
        ushort expected = ItemAt(from, slot);
        return Intend(TakingOne(from, slot, expected),
            w => new DoorIntent { Seq = _nextSeq, Action = DoorAction.Place, From = from, Slot = (byte)slot, Expected = expected, Target = frame.Id.Value, Flip = flip }
                .WriteMessage(w));
    }

    /// <summary>Fits the key or code lock in a slot to a door.</summary>
    public uint AttachLock(ContainerRef from, int slot, Door door)
    {
        ushort expected = ItemAt(from, slot);
        return Intend(TakingOne(from, slot, expected),
            w => new LockIntent { Seq = _nextSeq, Action = LockAction.Attach, From = from, Slot = (byte)slot, Expected = expected, Door = door.Id.Value }
                .WriteMessage(w));
    }

    /// <summary>
    /// Places the claim device in a slot on a foundation or floor, at a point of its top (<paramref name="x"/>,
    /// <paramref name="z"/> in the structure's frame) turned by <paramref name="yaw"/> 30° steps.
    /// </summary>
    public uint PlaceClaim(ContainerRef from, int slot, Piece support, double x, double z, int yaw = 0)
    {
        ushort expected = ItemAt(from, slot);
        return Intend(TakingOne(from, slot, expected), w => new ClaimIntent
        {
            Seq = _nextSeq, Action = ClaimAction.Place, From = from, Slot = (byte)slot, Expected = expected, Target = support.Id.Value, X = x, Z = z,
            Yaw = (byte)PieceGeometry.Wrap(yaw),
        }.WriteMessage(w));
    }

    private Pending TakingOne(ContainerRef from, int slot, ushort expected) =>
        new(p => from is ContainerRef.Main or ContainerRef.Belt && p.Resolve(from) is { } c &&
            InventoryRules.Remove(c, slot, 1, expected, out _) == OutcomeCode.Done);

    // ---------------------------------------------------------------- answers

    // A placement's answer: accepted, it stays drawn until its piece arrives (or a moment more); refused, it goes.
    private void AnsweredPiece(uint seq, bool done)
    {
        int piece = _pendingPieces.FindIndex(p => p.Seq == seq);
        if (piece < 0)
        {
            return;
        }
        if (done)
        {
            _pendingPieces[piece].AcceptedAt = ClockSeconds;
        }
        else
        {
            _pendingPieces.RemoveAt(piece);
        }
    }

    // A placement stays drawn until its piece is in the replicated building (or, for a moment, after it was accepted).
    internal void UpdateBuilding()
    {
        for (int i = _pendingPieces.Count - 1; i >= 0; i--)
        {
            PendingPiece p = _pendingPieces[i];
            bool arrived = Building?.World.PieceAt(p.Frame, p.Piece, p.Pose) != null;
            if (arrived || (!double.IsNaN(p.AcceptedAt) && ClockSeconds - p.AcceptedAt > 1.5) || i < _pendingPieces.Count - 64)
            {
                _pendingPieces.RemoveAt(i);
            }
        }
    }
}
