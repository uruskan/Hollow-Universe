using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.Teams;
using Hollow.Simulation.World;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>
/// The world on the server (GROUND.md §6.8, G7): the world's props placed at start as stored (their public state goes
/// out as things, like deployables); the economy's and teams' intents; each player's own: the blueprints they know, their
/// team and invites, their map (markers, last death, bags and beds), the machine they are looting; teammates' positions
/// twice a second; team chat; and teams and props saved with the zone.
/// </summary>
public sealed class WorldModule : ServerModule
{
    /// <summary>What a player was last sent of their world state.</summary>
    private sealed class WorldView
    {
        public uint Blueprints = uint.MaxValue;
        public ulong Team = ulong.MaxValue;
        public uint TeamRevision;
        public ulong TeamOnline;
        public bool Invites = true;
        public uint Map = uint.MaxValue;
        public ulong Machine;
        public int MachineProgress = -1;
        public bool MachineRunning;
    }

    private readonly Dictionary<ulong, PropOverride> _storedProps = new();
    private readonly Dictionary<ulong, ulong> _savedProps = new();
    private readonly Dictionary<ulong, uint> _savedTeams = new();
    private readonly List<TeammateNet> _mates = new();
    private uint _teamPositionSequence;

    // The world props as players left them, and the teams.
    protected internal override void Restore(ZoneRecord stored, HashSet<EntityId> saved)
    {
        int lost = 0;
        foreach (PropRecord p in stored.Props)
        {
            try
            {
                _storedProps[p.Spot] = WorldCodec.DecodeProp(p.Data, Content);
                _savedProps[p.Spot] = HashOf(p.Data);
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException)
            {
                lost++;
            }
        }
        foreach (TeamRecord t in stored.Teams)
        {
            try
            {
                (EntityId leader, List<EntityId> members) = TeamCodec.Decode(t.Data);
                if (Zone.World().Teams.Restore(t.Id, leader, members) is { } team)
                {
                    _savedTeams[t.Id] = team.Revision;
                }
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException)
            {
                lost++;
            }
        }
        Zone.World().Teams.Changed.Clear();
        Zone.World().Teams.Affected.Clear();
        Log($"loaded {Zone.World().Teams.Count} teams and {_storedProps.Count} changed world props" + (lost > 0 ? $" ({lost} records left out)" : ""));
    }

    // At start, after loading: the world's barrels, crates and recyclers (GROUND.md §6.8).
    protected internal override void Start()
    {
        if (!Options.World)
        {
            return;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Zone.World().StartWorld(_storedProps);
        _storedProps.Clear();
        if (Zone.World().LootSpots is { } spots)
        {
            Log($"world props: {spots.Spots.Count} spots ({spots.Coasts} coast, {spots.ForestEdges} forest-edge and {spots.Opens} open clusters), " +
                $"{Zone.World().Props.Count} standing, in {watch.Elapsed.TotalMilliseconds:0} ms");
        }
    }

    // ---------------------------------------------------------------- intents

    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        handlers.On<TeamIntent>((PlayerSession session, in TeamIntent message) =>
        {
            TeamIntent m = message;
            Entity me = session.Entity;
            Intent(session, m.Seq, () => Zone.World().TeamAct(me, m.Action, new EntityId(m.Target), m.Team), whileDead: true);
        });
        handlers.On<MarkerIntent>((PlayerSession session, in MarkerIntent message) =>
        {
            MarkerIntent m = message;
            Entity me = session.Entity;
            Intent(session, m.Seq,
                () => m.Remove ? Zone.World().RemoveMarker(me, m.Index) : Zone.World().PlaceMarker(me, new Vector3(m.X, m.Y, m.Z), m.Colour, m.Label), whileDead: true);
        });
        handlers.On<TeamChatSend>((PlayerSession session, in TeamChatSend m) =>
        {
            if (string.IsNullOrWhiteSpace(m.Text))
            {
                session.BadPackets++;
                return;
            }
            TeamChat(session, m.Text.Trim());
        });
    }

    // A line to the sender's team (the chat budget's, as any line).
    private void TeamChat(PlayerSession session, string text)
    {
        session.ChatTokens = Math.Min(5, session.ChatTokens + (Zone.Tick - session.ChatTick) / (double)TickRate);
        session.ChatTick = Zone.Tick;
        if (session.ChatTokens < 1 || !session.Playing || Zone.World().Teams.TeamOf(session.EntityId) is not { } team)
        {
            return;
        }
        session.ChatTokens -= 1;
        Log($"<{session.Name} to team> {text}");
        Writer.Reset();
        new ChatLine { Kind = ChatKind.Team, From = session.Name, Text = text }.WriteMessage(Writer);
        foreach (EntityId member in team.Members)
        {
            if (SessionOf(member) is { } to)
            {
                Send(to);
            }
        }
    }

    // ---------------------------------------------------------------- after each step

    /// <summary>The server's time in the world's messages after each step (blueprints, teams, invites, map, machines, teammates).</summary>
    public TimeSpan WorldTime { get; private set; }

    protected internal override void AfterStep()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        SendWorld();
        WorldTime += System.Diagnostics.Stopwatch.GetElapsedTime(started);
    }

    private void SendWorld()
    {
        foreach (EntityId affected in Zone.World().Teams.Affected)
        {
            if (SessionOf(affected) is { } s)
            {
                s.State<WorldView>().Invites = true;
            }
        }
        Zone.World().Teams.Affected.Clear();
        Zone.World().Teams.Changed.Clear();
        bool second = Zone.Tick % TickRate == 0;
        foreach (PlayerSession session in Players)
        {
            if (!session.Playing || Zone.Players().PlayerOf(session.Entity) is not { } p)
            {
                continue;
            }
            WorldView view = session.State<WorldView>();
            if (view.Blueprints != p.Blueprints.Revision)
            {
                view.Blueprints = p.Blueprints.Revision;
                SendBlueprints(session, p);
            }
            Team? team = Zone.World().Teams.TeamOf(p.Id);
            ulong online = second || view.Team == ulong.MaxValue ? OnlineHash(team) : view.TeamOnline;
            if (view.Team != (team?.Id ?? 0) || view.TeamRevision != (team?.Revision ?? 0) || view.TeamOnline != online)
            {
                view.Team = team?.Id ?? 0;
                view.TeamRevision = team?.Revision ?? 0;
                view.TeamOnline = online;
                SendTeam(session, team);
            }
            if (view.Invites)
            {
                view.Invites = false;
                SendInvites(session, p);
            }
            if (view.Map != p.MapRevision)
            {
                view.Map = p.MapRevision;
                SendMap(session, p);
            }
            SendMachine(session, p, view);
        }
        if (Zone.Tick % (TickRate / 2) == 0)
        {
            SendTeamPositions();
        }
    }

    // Which members play now, as a hash, so a teammate logging in or out refreshes the team panel.
    private ulong OnlineHash(Team? team)
    {
        if (team == null)
        {
            return 0;
        }
        ulong hash = 0x7EA;
        foreach (EntityId m in team.Members)
        {
            hash = Hash64.Combine(hash, SessionOf(m) != null ? m.Value : 0);
        }
        return hash;
    }

    private void SendBlueprints(PlayerSession session, Player p)
    {
        Writer.Reset();
        new BlueprintList
        {
            LastSeq = session.LastIntentSeq,
            Recipes = p.Blueprints.Learnt.OrderBy(r => r).Select(r => new RecipeNet { Recipe = r }).ToArray(),
        }.WriteMessage(Writer);
        Send(session);
    }

    private void SendTeam(PlayerSession session, Team? team)
    {
        var members = new List<TeamMemberNet>();
        foreach (EntityId m in team?.Members ?? (IReadOnlyList<EntityId>)Array.Empty<EntityId>())
        {
            members.Add(new TeamMemberNet
            {
                Id = m.Value,
                Name = Zone.Players().TryFindPlayer(m, out _, out Player member) ? member.Name : "",
                Online = SessionOf(m) != null,
            });
        }
        Writer.Reset();
        new TeamState { LastSeq = session.LastIntentSeq, Team = team?.Id ?? 0, Leader = team?.Leader.Value ?? 0, Members = members.ToArray() }
            .WriteMessage(Writer);
        Send(session);
    }

    private void SendInvites(PlayerSession session, Player p)
    {
        var invites = new List<TeamInviteNet>();
        foreach (TeamInvite invite in Zone.World().Teams.InvitesOf(p.Id).Take(16))
        {
            invites.Add(new TeamInviteNet
            {
                Team = invite.Team,
                From = Zone.Players().TryFindPlayer(invite.From, out _, out Player from) ? from.Name : "",
                Seconds = (uint)Math.Max(0, Math.Ceiling((invite.ExpiresAtUnixMs - Zone.UnixMilliseconds) / 1000.0)),
            });
        }
        Writer.Reset();
        new TeamInvites { Invites = invites.ToArray() }.WriteMessage(Writer);
        Send(session);
    }

    // The player's markers, last death and respawn points (their sleeping bags and beds anywhere in the zone).
    private void SendMap(PlayerSession session, Player p)
    {
        var points = new List<RespawnPointNet>();
        foreach (WorldObject thing in Zone.Things().Statics())
        {
            if (thing is Deployable { Def.IsRespawnPoint: true } bag && bag.Owner == p.Id && points.Count < 64)
            {
                points.Add(new RespawnPointNet { Id = bag.Id.Value, X = bag.Position.X, Y = bag.Position.Y, Z = bag.Position.Z, Bed = bag.Def.Key == "bed",
                    Name = bag.Name.Length > 0 ? bag.Name : bag.Def.Item.Name });
            }
        }
        Vector3 death = p.DeathPoint ?? Vector3.Zero;
        Writer.Reset();
        new MapState
        {
            LastSeq = session.LastIntentSeq,
            Markers = p.Markers.Select(m => new MarkerNet { X = m.Position.X, Y = m.Position.Y, Z = m.Position.Z, Colour = m.Colour, Label = m.Label }).ToArray(),
            HasDeath = p.DeathPoint.HasValue,
            DeathX = death.X,
            DeathY = death.Y,
            DeathZ = death.Z,
            Points = points.ToArray(),
        }.WriteMessage(Writer);
        Send(session);
    }

    // The recycler or research table the player loots: whether it runs and how far through, when that moves on.
    private void SendMachine(PlayerSession session, Player p, WorldView view)
    {
        if (p.Looting.IsNone || Zone.Things().StaticOf(p.Looting) is not Deployable { Def.IsMachine: true, Def.Device: null } machine)
        {
            view.Machine = 0;
            return;
        }
        int total = machine.Def.Recycler?.Ticks ?? machine.Def.Research?.Ticks ?? 1;
        int progress = machine.Lit ? (int)Math.Clamp(Math.Round((1 - machine.BurnTicksLeft / (double)total) * 255), 0, 255) : 0;
        if (view.Machine == machine.Id.Value && view.MachineRunning == machine.Lit && Math.Abs(view.MachineProgress - progress) < 16)
        {
            return;
        }
        view.Machine = machine.Id.Value;
        view.MachineRunning = machine.Lit;
        view.MachineProgress = progress;
        Writer.Reset();
        new MachineState
        {
            Target = machine.Id.Value,
            Running = machine.Lit,
            Progress = (byte)progress,
            SecondsLeft = machine.Lit ? (uint)Math.Ceiling(Math.Max(0, machine.BurnTicksLeft) / (double)TickRate) : 0,
            Item = machine.Def.Research != null ? machine.ResearchItem : 0u,
        }.WriteMessage(Writer);
        Send(session);
    }

    // Twice a second: each online member hears where the others are (unreliable; the newest wins).
    private void SendTeamPositions()
    {
        _teamPositionSequence++;
        foreach (Team team in Zone.World().Teams.All)
        {
            if (team.Members.Count < 2)
            {
                continue;
            }
            _mates.Clear();
            foreach (EntityId m in team.Members)
            {
                if (!Zone.Players().TryFindPlayer(m, out Entity e, out Player p))
                {
                    continue;
                }
                ref Character c = ref Zone.Entities.Get<Character>(e);
                // The map's arrow: the view's heading from local north where the mate stands.
                Vector3 view = Zone.Entities.Has<NetPresence>(e)
                    ? Quantize.FromOctahedral(Zone.Entities.Get<NetPresence>(e).ViewU, Zone.Entities.Get<NetPresence>(e).ViewV)
                    : c.Input.View;
                Vector3 at = Zone.WorldOf(c.State);
                ushort yaw = Quantize.Angle16(Facing.YawOf(Zone.Frames.DirectionToWorld(view, c.State.Frame, Zone.Tick), Zone.Frame.UpAt(at)));
                _mates.Add(new TeammateNet
                {
                    Id = m.Value,
                    X = at.X,
                    Y = at.Y,
                    Z = at.Z,
                    Yaw = yaw,
                    State = p.Dead ? TeammateState.Dead : p.Wounded ? TeammateState.Down : p.Sleeping ? TeammateState.Asleep : TeammateState.Awake,
                });
            }
            foreach (EntityId m in team.Members)
            {
                if (SessionOf(m) is not { } session || !session.Playing)
                {
                    continue;
                }
                Writer.Reset();
                new TeamPositions { Sequence = _teamPositionSequence, Members = _mates.Where(x => x.Id != m.Value).ToArray() }.WriteMessage(Writer);
                session.Peer.Send(Writer.Data, DeliveryMethod.Unreliable);
            }
        }
    }

    // ---------------------------------------------------------------- saving

    // Teams that changed or ended since the last save; world props' overrides that changed, and those back to as generated.
    protected internal override void Save(ZoneSave save, HashSet<EntityId> current)
    {
        var teams = new HashSet<ulong>();
        foreach (Team team in Zone.World().Teams.All)
        {
            teams.Add(team.Id);
            if (!_savedTeams.TryGetValue(team.Id, out uint revision) || revision != team.Revision)
            {
                save.Teams.Add(new TeamRecord(team.Id, TeamCodec.Encode(team)));
                _savedTeams[team.Id] = team.Revision;
            }
        }
        foreach (ulong id in _savedTeams.Keys.Where(id => !teams.Contains(id)).ToList())
        {
            save.RemovedTeams.Add(id);
            _savedTeams.Remove(id);
        }
        Zone.World().Teams.Ended.Clear();
        var props = new HashSet<ulong>();
        foreach ((ulong spot, PropOverride o) in Zone.World().PropOverrides())
        {
            props.Add(spot);
            byte[] data = WorldCodec.EncodeProp(o);
            ulong hash = HashOf(data);
            if (!_savedProps.TryGetValue(spot, out ulong saved) || saved != hash)
            {
                save.Props.Add(new PropRecord(spot, data));
                _savedProps[spot] = hash;
            }
        }
        foreach (ulong spot in _savedProps.Keys.Where(s => !props.Contains(s)).ToList())
        {
            save.RemovedProps.Add(spot);
            _savedProps.Remove(spot);
        }
    }

    private static ulong HashOf(byte[] data)
    {
        ulong hash = Hash64.Of((ulong)data.Length);
        foreach (byte b in data)
        {
            hash = Hash64.Combine(hash, b);
        }
        return hash;
    }

    // ---------------------------------------------------------------- admin

    /// <summary>What <c>world</c> prints: the world's props, teams and economy (G7).</summary>
    internal string WorldStatus() =>
        $"world props: {Zone.World().Props.Count} standing ({Zone.World().Props.Count(x => x.Prop.Kind == WorldPropKind.Barrel)} barrels, " +
        $"{Zone.World().Props.Count(x => x.Prop.Kind == WorldPropKind.Crate)} crates, {Zone.World().Props.Count(x => x.Prop.Kind == WorldPropKind.Recycler)} recyclers), " +
        $"{(Zone.World().LootSpots?.Spots.Count ?? 0) - Zone.World().Props.Count} gone; {Zone.World().Teams.Count} teams; {Zone.World().WorldStats}";
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): the world's props, teams, blueprints and maps.</summary>
public static class WorldModuleAccess
{
    public static WorldModule World(this GameServer server) => server.Require<WorldModule>();
}
