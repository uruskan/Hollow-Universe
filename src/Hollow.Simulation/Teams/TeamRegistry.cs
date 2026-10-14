using System;
using System.Collections.Generic;
using Hollow.Simulation.Entities;

namespace Hollow.Simulation.Teams;

/// <summary>
/// What a player does with teams (GROUND.md §6.8, Rust's). An invite is a verb on the player invited (GROUND_SYSTEMS.md §5,
/// the character capability's).
/// </summary>
public enum TeamAction : byte
{
    Create,
    Accept,
    Decline,
    Leave,
    Kick,
    Promote,
}

/// <summary>A team: its leader and members (at most world.json's <c>teams.size</c>, Rust's 8), oldest first.</summary>
public sealed class Team
{
    internal readonly List<EntityId> MemberList = new();

    internal Team(ulong id, EntityId leader)
    {
        Id = id;
        Leader = leader;
    }

    public ulong Id { get; }

    public EntityId Leader { get; internal set; }

    public IReadOnlyList<EntityId> Members => MemberList;

    /// <summary>Raised on every change (a member in or out, a new leader), for replication and saving.</summary>
    public uint Revision { get; internal set; }

    public bool Has(EntityId player) => MemberList.Contains(player);

    public override string ToString() => $"team {Id:X} ({MemberList.Count}, led by {Leader})";
}

/// <summary>An invitation to a team: which team, who asked, until when (Unix ms).</summary>
public readonly record struct TeamInvite(ulong Team, EntityId From, long ExpiresAtUnixMs);

/// <summary>
/// The zone's teams (GROUND.md §6.8, Rust's): a player creates one and leads it; the leader invites players who are in
/// no team, and an invite lasts a few minutes; accepting joins while there is room; a member leaves, the leader kicks or
/// passes the lead on; a leader who leaves hands it to the member who joined first, and the last one out ends the team.
/// Teams share building rights through "authorise team" on claim devices and code locks, chat among themselves and see
/// each other on the map. Friendly fire stays on (Rust's). Engine-free; one thread at a time (the zone's).
/// </summary>
public sealed class TeamRegistry
{
    private readonly Dictionary<ulong, Team> _teams = new();
    private readonly Dictionary<EntityId, Team> _of = new();
    private readonly Dictionary<EntityId, List<TeamInvite>> _invites = new();
    private readonly Func<ulong> _newId;

    public TeamRegistry(int maxMembers, double inviteSeconds, Func<ulong> newId)
    {
        MaxMembers = maxMembers;
        InviteSeconds = inviteSeconds;
        _newId = newId;
    }

    public int MaxMembers { get; }

    public double InviteSeconds { get; }

    public IEnumerable<Team> All => _teams.Values;

    public int Count => _teams.Count;

    /// <summary>Teams that changed (or were made) since the server last took them, for saving and replication.</summary>
    public HashSet<Team> Changed { get; } = new();

    /// <summary>Teams that ended, by ID, since the server last took them.</summary>
    public HashSet<ulong> Ended { get; } = new();

    /// <summary>Players whose team (or none) or invites changed, since the server last took them.</summary>
    public HashSet<EntityId> Affected { get; } = new();

    public Team? TeamOf(EntityId player) => _of.GetValueOrDefault(player);

    public Team? Get(ulong id) => _teams.GetValueOrDefault(id);

    /// <summary>Whether two players are in the same team (a player is their own teammate).</summary>
    public bool Together(EntityId a, EntityId b) => a == b || (_of.TryGetValue(a, out Team? t) && t.Has(b));

    /// <summary>The invites waiting for a player, oldest first.</summary>
    public IReadOnlyList<TeamInvite> InvitesOf(EntityId player) =>
        _invites.TryGetValue(player, out List<TeamInvite>? list) ? list : Array.Empty<TeamInvite>();

    public Outcome Create(EntityId player, out Team? team)
    {
        team = null;
        if (_of.ContainsKey(player))
        {
            return OutcomeCode.InTeam;
        }
        team = new Team(_newId(), player);
        team.MemberList.Add(player);
        _teams.Add(team.Id, team);
        _of[player] = team;
        DropInvites(player);
        Touch(team, player);
        return OutcomeCode.Done;
    }

    /// <summary>The leader invites a player in no team (again: the invite's time starts over).</summary>
    public Outcome Invite(EntityId leader, EntityId target, long nowUnixMs)
    {
        if (!_of.TryGetValue(leader, out Team? team))
        {
            return OutcomeCode.NoTeam;
        }
        if (team.Leader != leader)
        {
            return OutcomeCode.NotLeader;
        }
        if (target == leader || target.IsNone)
        {
            return OutcomeCode.Invalid;
        }
        if (_of.ContainsKey(target))
        {
            return OutcomeCode.InTeam;
        }
        if (team.MemberList.Count >= MaxMembers)
        {
            return OutcomeCode.TeamFull;
        }
        if (!_invites.TryGetValue(target, out List<TeamInvite>? list))
        {
            list = new List<TeamInvite>();
            _invites.Add(target, list);
        }
        list.RemoveAll(i => i.Team == team.Id);
        list.Add(new TeamInvite(team.Id, leader, nowUnixMs + (long)(InviteSeconds * 1000)));
        Affected.Add(target);
        return OutcomeCode.Done;
    }

    public Outcome Accept(EntityId player, ulong teamId, long nowUnixMs)
    {
        if (_of.ContainsKey(player))
        {
            return OutcomeCode.InTeam;
        }
        bool invited = _invites.TryGetValue(player, out List<TeamInvite>? list) &&
            list.Exists(i => i.Team == teamId && i.ExpiresAtUnixMs > nowUnixMs);
        if (!invited || !_teams.TryGetValue(teamId, out Team? team))
        {
            return OutcomeCode.NoTeam;
        }
        if (team.MemberList.Count >= MaxMembers)
        {
            return OutcomeCode.TeamFull;
        }
        team.MemberList.Add(player);
        _of[player] = team;
        DropInvites(player);
        Touch(team, player);
        return OutcomeCode.Done;
    }

    public Outcome Decline(EntityId player, ulong teamId)
    {
        if (!_invites.TryGetValue(player, out List<TeamInvite>? list) || list.RemoveAll(i => i.Team == teamId) == 0)
        {
            return OutcomeCode.NoTeam;
        }
        if (list.Count == 0)
        {
            _invites.Remove(player);
        }
        Affected.Add(player);
        return OutcomeCode.Done;
    }

    public Outcome Leave(EntityId player)
    {
        if (!_of.TryGetValue(player, out Team? team))
        {
            return OutcomeCode.NoTeam;
        }
        Remove(team, player);
        return OutcomeCode.Done;
    }

    public Outcome Kick(EntityId leader, EntityId member)
    {
        if (!_of.TryGetValue(leader, out Team? team))
        {
            return OutcomeCode.NoTeam;
        }
        if (team.Leader != leader)
        {
            return OutcomeCode.NotLeader;
        }
        if (member == leader || !team.Has(member))
        {
            return OutcomeCode.Invalid;
        }
        Remove(team, member);
        return OutcomeCode.Done;
    }

    public Outcome Promote(EntityId leader, EntityId member)
    {
        if (!_of.TryGetValue(leader, out Team? team))
        {
            return OutcomeCode.NoTeam;
        }
        if (team.Leader != leader)
        {
            return OutcomeCode.NotLeader;
        }
        if (member == leader || !team.Has(member))
        {
            return OutcomeCode.Invalid;
        }
        team.Leader = member;
        Touch(team, leader);
        return OutcomeCode.Done;
    }

    /// <summary>Drops invites that ran out (once a second).</summary>
    public void Expire(long nowUnixMs)
    {
        List<EntityId>? empty = null;
        foreach ((EntityId player, List<TeamInvite> list) in _invites)
        {
            if (list.RemoveAll(i => i.ExpiresAtUnixMs <= nowUnixMs || !_teams.ContainsKey(i.Team)) > 0)
            {
                Affected.Add(player);
            }
            if (list.Count == 0)
            {
                (empty ??= new List<EntityId>()).Add(player);
            }
        }
        foreach (EntityId player in empty ?? new List<EntityId>())
        {
            _invites.Remove(player);
        }
    }

    /// <summary>A team as it was saved: members who are in another team already are left out; a team left empty isn't made.</summary>
    public Team? Restore(ulong id, EntityId leader, IEnumerable<EntityId> members)
    {
        if (_teams.ContainsKey(id))
        {
            return null;
        }
        var team = new Team(id, leader);
        foreach (EntityId m in members)
        {
            if (!m.IsNone && !_of.ContainsKey(m) && !team.MemberList.Contains(m) && team.MemberList.Count < MaxMembers)
            {
                team.MemberList.Add(m);
            }
        }
        if (team.MemberList.Count == 0)
        {
            return null;
        }
        if (!team.Has(leader))
        {
            team.Leader = team.MemberList[0];
        }
        _teams.Add(id, team);
        foreach (EntityId m in team.MemberList)
        {
            _of[m] = team;
        }
        return team;
    }

    private void Remove(Team team, EntityId player)
    {
        team.MemberList.Remove(player);
        _of.Remove(player);
        Affected.Add(player);
        if (team.MemberList.Count == 0)
        {
            _teams.Remove(team.Id);
            Changed.Remove(team);
            Ended.Add(team.Id);
            return;
        }
        if (team.Leader == player)
        {
            team.Leader = team.MemberList[0];
        }
        Touch(team, player);
    }

    private void DropInvites(EntityId player)
    {
        if (_invites.Remove(player))
        {
            Affected.Add(player);
        }
    }

    private void Touch(Team team, EntityId player)
    {
        team.Revision++;
        Changed.Add(team);
        Ended.Remove(team.Id);
        Affected.Add(player);
        foreach (EntityId m in team.MemberList)
        {
            Affected.Add(m);
        }
    }
}
