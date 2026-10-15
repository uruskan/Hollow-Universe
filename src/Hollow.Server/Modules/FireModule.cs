using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Targets;

namespace Hollow.Server;

/// <summary>
/// Fires on the server (IW4, IRON_WAKE.md §7): each player is told of the fires within their range (reliably) when one is lit
/// or comes into range, and again when one falls onto something else, with what holds it and its place there, so their
/// client draws it riding a ship's deck; and that it ended when it goes out (the scorch is left) or leaves their range.
/// Fires aren't saved: the longest lasts eighteen seconds.
/// </summary>
public sealed class FireModule : ServerModule
{
    private readonly Dictionary<ulong, (Fire Fire, Vector3 At)> _firesNow = new();
    private readonly HashSet<ulong> _wentOut = new();
    private readonly List<ulong> _leaving = new();

    /// <summary>The fires this player was told of, by ID, with the revision sent.</summary>
    private sealed class FireView
    {
        public readonly Dictionary<ulong, uint> Sent = new();
    }

    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        Zone.Events.On<FireOut>((in FireOut e) => _wentOut.Add(e.Fire.Value));
    }

    // Once per snapshot: where every fire burns now (on a ship's deck it moves).
    protected internal override void Prepare()
    {
        _firesNow.Clear();
        foreach (Fire fire in Zone.Fires().All)
        {
            if (!fire.Out)
            {
                _firesNow[fire.Id.Value] = (fire, Zone.Supports().TryLinkPose(fire.Link, out TargetPose pose) ? pose.Position : fire.Position);
            }
        }
    }

    // The fires within the player's range: each when it is lit or comes into range and whenever it falls onto something
    // else; one gone out or out of range is said ended. Timed with the building's, as the profile has it.
    protected internal override void Replicate(PlayerSession session, Vector3 centre)
    {
        using (Profile.Time(ProfileSection.ReplicateBuilding))
        {
            ReplicateFires(session, centre);
        }
    }

    private void ReplicateFires(PlayerSession session, Vector3 centre)
    {
        Dictionary<ulong, uint> sent = session.State<FireView>().Sent;
        float radius = Options.RelevanceRadius;
        _leaving.Clear();
        foreach (ulong id in sent.Keys)
        {
            if (!_firesNow.TryGetValue(id, out (Fire Fire, Vector3 At) now) || Vector3.DistanceSquared(now.At, centre) > (radius + 20) * (radius + 20))
            {
                _leaving.Add(id);
            }
        }
        foreach (ulong id in _leaving)
        {
            sent.Remove(id);
            Writer.Reset();
            new FireEnded { Id = id, Out = _wentOut.Contains(id) }.WriteMessage(Writer);
            Send(session);
        }
        foreach ((ulong id, (Fire fire, Vector3 at)) in _firesNow)
        {
            if (Vector3.DistanceSquared(at, centre) > radius * radius || (sent.TryGetValue(id, out uint revision) && revision == fire.Revision))
            {
                continue;
            }
            sent[id] = fire.Revision;
            Writer.Reset();
            Net(fire, at).WriteMessage(Writer);
            Send(session);
        }
    }

    // After every player's replication: the fires that went out have been said ended to all who knew them.
    protected internal override void Finish() => _wentOut.Clear();

    private FireStarted Net(Fire fire, Vector3 at)
    {
        SupportLink link = fire.Link;
        Vector3 normal = fire.Normal;
        return new FireStarted
        {
            Id = fire.Id.Value,
            Item = fire.Item.Id,
            Owner = NetIdOf(fire.Owner),
            X = at.X,
            Y = at.Y,
            Z = at.Z,
            NormalX = normal.X,
            NormalY = normal.Y,
            NormalZ = normal.Z,
            Radius = (float)Math.Clamp(fire.Kind.Radius, 0, 20),
            Support = NetConvert.ToNet(link.Support),
            LocalX = link.Position.X,
            LocalY = link.Position.Y,
            LocalZ = link.Position.Z,
            RotationX = link.Rotation.X,
            RotationY = link.Rotation.Y,
            RotationZ = link.Rotation.Z,
            RotationW = link.Rotation.W,
            StartTick = (uint)Math.Max(0, fire.StartTick),
            EndTick = (uint)Math.Max(0, fire.EndTick),
        };
    }
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): fires as replicated.</summary>
public static class FireModuleAccess
{
    public static FireModule Fires(this GameServer server) => server.Require<FireModule>();
}
