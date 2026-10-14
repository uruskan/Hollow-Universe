using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Targets;

namespace Hollow.Server;

/// <summary>
/// Placing anything (GROUND_SYSTEMS.md §6): one intent for every placeable, on the general budget, answered by
/// IntentResult; the zone works the placement out again and checks it with the same code as the client's ghost.
/// </summary>
public sealed class PlacementModule : ServerModule
{
    private readonly Dictionary<string, long> _placed = new(StringComparer.Ordinal);

    /// <summary>Placements accepted, by placeable kind (placement.json's names), for the load test and <c>status</c>.</summary>
    public long PlacedOf(string kind) => _placed.GetValueOrDefault(kind);

    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        handlers.On<PlaceIntent>((PlayerSession session, in PlaceIntent message) =>
        {
            PlaceIntent m = message;
            Intent(session, m.Seq, () => Place(session, m));
        });
    }

    private Outcome Place(PlayerSession session, in PlaceIntent m)
    {
        var claim = new PlacementClaim(new TargetRef(m.Support.Family, m.Support.Id, m.Support.Part), m.Socket,
            new Vector3(m.Pose.X, m.Pose.Y, m.Pose.Z), Quantize.FromAngle16(m.Pose.Yaw), m.Variant);
        int placeable = (int)Math.Min(m.Placeable, int.MaxValue);
        Outcome placed = Zone.Placement().Place(session.Entity, new PlaceRequest(m.From, m.Slot, Expected(m.Expected), placeable, claim));
        if (placed.Ok && Content.Placement[placeable] is { } p)
        {
            _placed[p.Kind.Name] = _placed.GetValueOrDefault(p.Kind.Name) + 1;
        }
        return placed;
    }
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): the placement intent and its counts.</summary>
public static class PlacementModuleAccess
{
    public static PlacementModule Placement(this GameServer server) => server.Require<PlacementModule>();
}
