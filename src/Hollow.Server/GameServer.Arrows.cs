using Hollow.Simulation;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Hollow.Net.Protocol;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Server;

// Spent arrows on the server (GROUND_SYSTEMS.md §5): each player is told of the arrows within their range — what holds
// each and where its tip is in that holder's frame, so their client draws it riding its holder — again when one falls
// (what held it went), and that it went when it is taken, its time is up or it leaves their range.
public sealed partial class GameServer
{
    private readonly ConditionalWeakTable<PlayerSession, Dictionary<ulong, uint>> _arrowsSent = new();
    private readonly Dictionary<ulong, (SpentArrow Arrow, Vector3 At)> _arrowsNow = new();
    private readonly List<ulong> _arrowsLeaving = new();
    private readonly List<ArrowNet> _arrowBatch = new();
    private readonly List<BuildIdNet> _arrowGoneBatch = new();

    // Once per snapshot: where every arrow is now (on a moving holder, it moves).
    private void PrepareArrows()
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
    private void ReplicateArrows(PlayerSession session, Vector3 centre)
    {
        Dictionary<ulong, uint> sent = _arrowsSent.GetValue(session, _ => new Dictionary<ulong, uint>());
        float radius = _options.RelevanceRadius;
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
        _writer.Reset();
        new ArrowStates { Arrows = _arrowBatch.ToArray() }.WriteMessage(_writer);
        Send(session);
        _arrowBatch.Clear();
    }

    private void SendArrowsGone(PlayerSession session)
    {
        if (_arrowGoneBatch.Count == 0)
        {
            return;
        }
        _writer.Reset();
        new ArrowsGone { Ids = _arrowGoneBatch.ToArray() }.WriteMessage(_writer);
        Send(session);
        _arrowGoneBatch.Clear();
    }
}
