using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Items;
using Hollow.Simulation.Placement;

namespace Hollow.Client;

/// <summary>
/// A placement sent and not yet answered (or answered a moment ago): drawn where it will stand in the ghost's pending look
/// until the server says (GROUND_SYSTEMS.md §6), then it goes; refused, at once.
/// </summary>
public sealed record PendingPlacement(uint Seq, Placeable Placeable, Vector3 Position, Quaternion Rotation, object? Detail)
{
    /// <summary>When the server said it was done (client seconds), or NaN while unanswered.</summary>
    public double AcceptedAt { get; internal set; } = double.NaN;
}

// Placing anything (GROUND_SYSTEMS.md §6): one intent, PlaceIntent, for what the ghost shows (GameClient.Plan); its price
// leaves the inventory at once (the item used up, or a piece's materials), the placement is drawn pending until the answer,
// and a refusal rolls it back.
public sealed partial class LocalPlayer
{
    /// <summary>An accepted placement is still drawn this long, while the real thing arrives.</summary>
    private const double PendingLinger = 0.5;

    private readonly List<PendingPlacement> _pendingPlacements = new();

    /// <summary>Placements on their way: draw them in the pending look.</summary>
    public IReadOnlyList<PendingPlacement> PendingPlacements => _pendingPlacements;

    /// <summary>
    /// Places what a ghost shows (<see cref="GameClient.Plan"/>), paid from a slot: the item used up there, or (a building
    /// piece) the plan carried there and the piece's materials from the carried slots. Returns the intent's number, 0 when it
    /// couldn't be sent.
    /// </summary>
    public uint Place(PlacementCandidate candidate, ContainerRef from, int slot)
    {
        Placeable placeable = candidate.Placeable;
        ushort expected = ItemAt(from, slot);
        PlacementClaim claim = candidate.Claim;
        uint seq = Intend(new Pending(p => p.PayFor(placeable, from, slot, expected)), w => new PlaceIntent
        {
            Seq = _nextSeq,
            From = from,
            Slot = (byte)slot,
            Expected = expected,
            Placeable = (uint)placeable.Index,
            Support = new TargetNet { Family = claim.Support.Family, Id = claim.Support.Id, Part = claim.Support.Part },
            Socket = (byte)claim.Socket,
            Pose = new PoseNet { X = claim.Position.X, Y = claim.Position.Y, Z = claim.Position.Z, Yaw = Quantize.Angle16(claim.Yaw) },
            Variant = (byte)claim.Variant,
        }.WriteMessage(w));
        if (seq != 0)
        {
            _pendingPlacements.Add(new PendingPlacement(seq, placeable, candidate.Position, candidate.Rotation, candidate.Detail));
        }
        return seq;
    }

    // The price the server takes for a placement, the same way: the item used up from the slot (as the client saw it), then
    // each material from the carried slots.
    private bool PayFor(Placeable placeable, ContainerRef from, int slot, ushort expected)
    {
        if (from is not (ContainerRef.Main or ContainerRef.Belt) || Resolve(from) is not { } source ||
            (placeable.Item != null && InventoryRules.Remove(source, slot, 1, expected, out _) != OutcomeCode.Done))
        {
            return false;
        }
        var wallet = new ItemWallet(Content, Inventory.Carried);
        foreach (PlacementCost cost in placeable.Costs)
        {
            if (!wallet.Take(cost.Material, cost.Amount))
            {
                return false;
            }
        }
        return true;
    }

    // A placement's answer: accepted, it stays drawn a moment more while the real thing arrives; refused, it goes.
    private void AnsweredPlacement(uint seq, bool done)
    {
        int i = _pendingPlacements.FindIndex(p => p.Seq == seq);
        if (i < 0)
        {
            return;
        }
        if (done)
        {
            _pendingPlacements[i].AcceptedAt = ClockSeconds;
        }
        else
        {
            _pendingPlacements.RemoveAt(i);
        }
    }

    internal void UpdatePlacements()
    {
        for (int i = _pendingPlacements.Count - 1; i >= 0; i--)
        {
            PendingPlacement p = _pendingPlacements[i];
            if ((!double.IsNaN(p.AcceptedAt) && ClockSeconds - p.AcceptedAt > PendingLinger) || i < _pendingPlacements.Count - 64)
            {
                _pendingPlacements.RemoveAt(i);
            }
        }
    }
}
