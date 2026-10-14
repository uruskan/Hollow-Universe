using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Server;

/// <summary>
/// Spent arrows on the server (GROUND_SYSTEMS.md §5): each player is told of the arrows within their range — what holds
/// each and where its tip is in that holder's frame, so their client draws it riding its holder — again when one falls
/// (what held it went), and that it went when it is taken, its time is up or it leaves their range.
/// </summary>
public sealed class ArrowModule : ServerModule
{
    private readonly Dictionary<ulong, (SpentArrow Arrow, Vector3 At)> _arrowsNow = new();
    private readonly List<ulong> _arrowsLeaving = new();
    private readonly List<ArrowNet> _arrowBatch = new();
    private readonly List<BuildIdNet> _arrowGoneBatch = new();

    /// <summary>The arrows this player has, by ID, with the revision sent.</summary>
    private sealed class ArrowView
    {
        public readonly Dictionary<ulong, uint> Sent = new();
    }

    // Once per snapshot: where every arrow is now (on a moving holder, it moves).
    protected internal override void Prepare()
    {
        _arrowsNow.Clear();
        foreach (SpentArrow arrow in Zone.Arrows().All)
        {
            if (Zone.Supports().TryLinkPose(arrow.Link, out TargetPose pose))
            {
                _arrowsNow[arrow.Id.Value] = (arrow, pose.Position);
            }
        }
    }

    // The arrows within the player's range, each when it comes in and whenever it changes; those gone or out of range.
    // Timed with the building's, as the profile has it.
    protected internal override void Replicate(PlayerSession session, Vector3 centre)
    {
        using (Profile.Time(ProfileSection.ReplicateBuilding))
        {
            ReplicateArrows(session, centre);
        }
    }

    private void ReplicateArrows(PlayerSession session, Vector3 centre)
    {
        Dictionary<ulong, uint> sent = session.State<ArrowView>().Sent;
        float radius = Options.RelevanceRadius;
        _arrowsLeaving.Clear();
        foreach (ulong id in sent.Keys)
        {
            if (!_arrowsNow.TryGetValue(id, out (SpentArrow Arrow, Vector3 At) now) || Vector3.DistanceSquared(now.At, centre) > (radius + 20) * (radius + 20))
            {
                _arrowsLeaving.Add(id);
            }
        }
        _arrowGoneBatch.Clear();
        foreach (ulong id in _arrowsLeaving)
        {
            sent.Remove(id);
            _arrowGoneBatch.Add(new BuildIdNet { Id = id });
            if (_arrowGoneBatch.Count == 255)
            {
                SendArrowsGone(session);
            }
        }
        SendArrowsGone(session);

        _arrowBatch.Clear();
        foreach ((ulong id, (SpentArrow arrow, Vector3 at)) in _arrowsNow)
        {
            if (Vector3.DistanceSquared(at, centre) > radius * radius || (sent.TryGetValue(id, out uint revision) && revision == arrow.Revision))
            {
                continue;
            }
            sent[id] = arrow.Revision;
            SupportLink link = arrow.Link;
            _arrowBatch.Add(new ArrowNet
            {
                Id = id, Item = arrow.Item.Id, Support = NetConvert.ToNet(link.Support), X = link.Position.X, Y = link.Position.Y, Z = link.Position.Z,
                RotationX = link.Rotation.X, RotationY = link.Rotation.Y, RotationZ = link.Rotation.Z, RotationW = link.Rotation.W,
            });
            if (_arrowBatch.Count == 64)
            {
                SendArrows(session);
            }
        }
        SendArrows(session);
    }

    private void SendArrows(PlayerSession session)
    {
        if (_arrowBatch.Count == 0)
        {
            return;
        }
        Writer.Reset();
        new ArrowStates { Arrows = _arrowBatch.ToArray() }.WriteMessage(Writer);
        Send(session);
        _arrowBatch.Clear();
    }

    private void SendArrowsGone(PlayerSession session)
    {
        if (_arrowGoneBatch.Count == 0)
        {
            return;
        }
        Writer.Reset();
        new ArrowsGone { Ids = _arrowGoneBatch.ToArray() }.WriteMessage(Writer);
        Send(session);
        _arrowGoneBatch.Clear();
    }
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): spent arrows as replicated.</summary>
public static class ArrowModuleAccess
{
    public static ArrowModule Arrows(this GameServer server) => server.Require<ArrowModule>();
}
