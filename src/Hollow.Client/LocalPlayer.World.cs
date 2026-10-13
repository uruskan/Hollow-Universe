using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Economy;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.Teams;

namespace Hollow.Client;

/// <summary>A member of the player's team as the interface shows them.</summary>
public readonly record struct TeamMemberView(EntityId Id, string Name, bool Online, bool Leader, bool Me);

/// <summary>The player's team (GROUND.md §6.8): its ID, its leader and its members, oldest first.</summary>
public sealed record TeamView(ulong Id, EntityId Leader, IReadOnlyList<TeamMemberView> Members)
{
    public bool Has(EntityId player) => Members is { } m && FindIndex(m, player) >= 0;

    private static int FindIndex(IReadOnlyList<TeamMemberView> members, EntityId id)
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].Id == id)
            {
                return i;
            }
        }
        return -1;
    }
}

/// <summary>An invite to a team: which, from whom, until when on the client's clock.</summary>
public readonly record struct TeamInviteView(ulong Team, string From, double ExpiresAtSeconds);

/// <summary>A teammate where the server last said (twice a second): for the map, and name tags beyond the 300 m range.</summary>
public readonly record struct TeammateView(EntityId Id, string Name, Vector3 Position, float Yaw, TeammateState State);

/// <summary>A marker the player put on the map (zone frame), its colour (0 … 5) and label; its index names it for removal.</summary>
public readonly record struct MarkerView(int Index, Vector3 Position, byte Colour, string Label);

/// <summary>One of the player's sleeping bags or beds, for the map.</summary>
public readonly record struct RespawnPointView(EntityId Id, Vector3 Position, bool Bed);

/// <summary>The recycler or research table being looted (G7): running, how far through the batch or research, what it researches.</summary>
public sealed record MachineView(EntityId Target, bool Running, float Progress, float SecondsLeft, ItemDef? Researching);

// The player's progression and social world (GROUND.md §6.8, G7): the blueprints they know and the tech trees'
// unlocks, their team and its invites and positions, their map (markers, last death, bags and beds), the machine they
// are looting. Like the rest of LocalPlayer, intents show at once where their outcome is known (an unlock's salvage and
// blueprint, a marker), and the server's copy replaces the client's when it comes.
public sealed partial class LocalPlayer
{
    private readonly BlueprintBook _serverBook = new();
    private readonly List<TeamInviteView> _invites = new();
    private readonly List<TeammateView> _teammates = new();
    private readonly List<MarkerView> _markers = new();
    private readonly List<RespawnPointView> _points = new();
    private uint _teamPositionsSequence;

    /// <summary>The blueprints the player knows beyond the defaults: the server's, with unlocks not yet answered.</summary>
    public BlueprintBook Blueprints { get; } = new();

    /// <summary>Whether a recipe can be crafted as far as knowing it goes (default or learnt).</summary>
    public bool Knows(Recipe recipe) => Blueprints.Knows(recipe);

    /// <summary>The player's team, or null.</summary>
    public TeamView? Team { get; private set; }

    /// <summary>Whether the player leads their team.</summary>
    public bool LeadsTeam => Team != null && Team.Leader == EntityId;

    /// <summary>The team invites waiting for the player.</summary>
    public IReadOnlyList<TeamInviteView> TeamInvites => _invites;

    /// <summary>Where the teammates are, as the server last said (not the player).</summary>
    public IReadOnlyList<TeammateView> Teammates => _teammates;

    public IReadOnlyList<MarkerView> Markers => _markers;

    /// <summary>Where the player died last (zone frame), or null.</summary>
    public Vector3? DeathPoint { get; private set; }

    /// <summary>The player's sleeping bags and beds.</summary>
    public IReadOnlyList<RespawnPointView> RespawnPoints => _points;

    /// <summary>The recycler or research table being looted, or null.</summary>
    public MachineView? Machine { get; private set; }

    /// <summary>The player's own ID (set by the client on joining).</summary>
    public EntityId EntityId { get; internal set; }

    /// <summary>A blueprint was learnt (unlocked, researched and read, or from an item).</summary>
    public event Action<Recipe>? BlueprintLearned;

    /// <summary>The team or its members changed (joined, left, a new leader, someone came online).</summary>
    public event Action<TeamView?>? TeamChanged;

    /// <summary>An invite came.</summary>
    public event Action<TeamInviteView>? TeamInviteReceived;

    /// <summary>The map's own markers, death point, bags or beds changed.</summary>
    public event Action? MapChanged;

    // ---------------------------------------------------------------- intents

    /// <summary>
    /// Unlocks a tech tree node (its recipe) at a workbench of its level within reach, for its salvage: the blueprint
    /// is known and the salvage gone at once (rolled back if the server says no).
    /// </summary>
    public uint Unlock(int recipeId) =>
        Intend(new Pending(p => p.ApplyUnlock(recipeId)), w => new UnlockIntent { Seq = _nextSeq, Recipe = (uint)Math.Max(0, recipeId) }.WriteMessage(w));

    /// <summary>Whether a tech tree node can be unlocked now (as the server will judge it), and why not.</summary>
    public ActionResult CanUnlock(TechNode node)
    {
        ActionResult can = TechTrees.CanUnlock(node, Blueprints);
        if (can != ActionResult.Accepted)
        {
            return can;
        }
        if (WorkbenchLevel < node.Tier)
        {
            return ActionResult.NeedsWorkbench;
        }
        return Content.World.Currency is { } salvage && Inventory.Count(salvage) >= node.Cost ? ActionResult.Accepted : ActionResult.NotEnough;
    }

    /// <summary>Reads a blueprint in the main slots or belt: its recipe is learnt and the item used up.</summary>
    public uint Learn(ContainerRef from, int slot) => Use(from, slot);

    public uint CreateTeam() => TeamIntent(TeamAction.Create, default, 0);

    /// <summary>Invites a player close by (the interact key on them: <see cref="GameClient.Aim"/>), as the team's leader.</summary>
    public uint InviteToTeam(EntityId player) => TeamIntent(TeamAction.Invite, player, 0);

    public uint AcceptInvite(ulong team) => TeamIntent(TeamAction.Accept, default, team);

    public uint DeclineInvite(ulong team) => TeamIntent(TeamAction.Decline, default, team);

    public uint LeaveTeam() => TeamIntent(TeamAction.Leave, default, 0);

    public uint KickFromTeam(EntityId member) => TeamIntent(TeamAction.Kick, member, 0);

    public uint PromoteInTeam(EntityId member) => TeamIntent(TeamAction.Promote, member, 0);

    private uint TeamIntent(TeamAction action, EntityId target, ulong team) =>
        Intend(null, w => new TeamIntent { Seq = _nextSeq, Action = action, Target = target.Value, Team = team }.WriteMessage(w));

    /// <summary>Puts a marker on the map at a point (zone frame; its height is the ground's), with a colour (0 … 5) and a label.</summary>
    public uint PlaceMarker(Vector3 at, byte colour = 0, string label = "")
    {
        string text = label.Length > MapMarker.MaxLabel ? label[..MapMarker.MaxLabel] : label;
        return Intend(null, w => new MarkerIntent
        {
            Seq = _nextSeq, Remove = false, X = at.X, Z = at.Z, Colour = (byte)Math.Clamp((int)colour, 0, MapMarker.Colours - 1), Label = text,
        }.WriteMessage(w));
    }

    /// <summary>Takes a marker off the map (its index; −1: all).</summary>
    public uint RemoveMarker(int index) =>
        Intend(null, w => new MarkerIntent { Seq = _nextSeq, Remove = true, Index = (sbyte)Math.Clamp(index, -1, 100), Label = "" }.WriteMessage(w));

    // An unlock shows at once: the salvage leaves and the blueprint is known.
    private bool ApplyUnlock(int recipeId)
    {
        if (Content.World.NodeOf(recipeId) is not { } node || CanUnlock(node) != ActionResult.Accepted ||
            !InventoryRules.Take(Inventory.Carried, Content.World.Currency!.Id, node.Cost))
        {
            return false;
        }
        Blueprints.Learn(node.Recipe);
        return true;
    }

    // ---------------------------------------------------------------- what the server sends

    internal void OnBlueprints(in BlueprintList m)
    {
        var before = new HashSet<ushort>(_serverBook.Learnt);
        var recipes = new List<ushort>();
        foreach (RecipeNet r in m.Recipes ?? Array.Empty<RecipeNet>())
        {
            recipes.Add((ushort)Math.Min(r.Recipe, ushort.MaxValue));
        }
        _serverBook.Set(Content, recipes);
        Acknowledge(m.LastSeq);
        foreach (ushort id in _serverBook.Learnt)
        {
            if (!before.Contains(id) && Content.Recipe(id) is { } recipe)
            {
                BlueprintLearned?.Invoke(recipe);
            }
        }
    }

    internal void OnTeam(in TeamState m)
    {
        if (m.Team == 0)
        {
            Team = null;
            _teammates.Clear();
        }
        else
        {
            var members = new List<TeamMemberView>();
            foreach (TeamMemberNet n in m.Members ?? Array.Empty<TeamMemberNet>())
            {
                var id = new EntityId(n.Id);
                members.Add(new TeamMemberView(id, n.Name ?? "", n.Online, n.Id == m.Leader, id == EntityId));
            }
            Team = new TeamView(m.Team, new EntityId(m.Leader), members);
            _teammates.RemoveAll(t => !Team.Has(t.Id));
        }
        Acknowledge(m.LastSeq);
        TeamChanged?.Invoke(Team);
    }

    internal void OnInvites(in TeamInvites m)
    {
        var known = new HashSet<ulong>();
        foreach (TeamInviteView i in _invites)
        {
            known.Add(i.Team);
        }
        _invites.Clear();
        foreach (TeamInviteNet n in m.Invites ?? Array.Empty<TeamInviteNet>())
        {
            var invite = new TeamInviteView(n.Team, n.From ?? "", ClockSeconds + n.Seconds);
            _invites.Add(invite);
            if (!known.Contains(n.Team))
            {
                TeamInviteReceived?.Invoke(invite);
            }
        }
        _dirty = true;
    }

    internal void OnTeamPositions(in TeamPositions m)
    {
        // Unreliable: an older packet arriving late is dropped.
        if (_teamPositionsSequence != 0 && (int)(m.Sequence - _teamPositionsSequence) <= 0)
        {
            return;
        }
        _teamPositionsSequence = m.Sequence;
        _teammates.Clear();
        foreach (TeammateNet n in m.Members ?? Array.Empty<TeammateNet>())
        {
            var id = new EntityId(n.Id);
            string name = "";
            foreach (TeamMemberView member in Team?.Members ?? Array.Empty<TeamMemberView>())
            {
                if (member.Id == id)
                {
                    name = member.Name;
                }
            }
            _teammates.Add(new TeammateView(id, name, new Vector3(n.X, n.Y, n.Z), Quantize.FromAngle16(n.Yaw), n.State));
        }
    }

    internal void OnMap(in MapState m)
    {
        _markers.Clear();
        MarkerNet[] markers = m.Markers ?? Array.Empty<MarkerNet>();
        for (int i = 0; i < markers.Length; i++)
        {
            _markers.Add(new MarkerView(i, new Vector3(markers[i].X, markers[i].Y, markers[i].Z), markers[i].Colour, markers[i].Label ?? ""));
        }
        DeathPoint = m.HasDeath ? new Vector3(m.DeathX, m.DeathY, m.DeathZ) : null;
        _points.Clear();
        foreach (RespawnPointNet p in m.Points ?? Array.Empty<RespawnPointNet>())
        {
            _points.Add(new RespawnPointView(new EntityId(p.Id), new Vector3(p.X, p.Y, p.Z), p.Bed));
        }
        Acknowledge(m.LastSeq);
        MapChanged?.Invoke();
    }

    internal void OnMachine(in MachineState m)
    {
        Machine = new MachineView(new EntityId(m.Target), m.Running, m.Progress / 255f, m.SecondsLeft,
            m.Item != 0 ? Content.Item((int)Math.Min(m.Item, ushort.MaxValue)) : null);
        _dirty = true;
    }

    // The server's blueprints, before the pending intents go on top (Rebuild).
    private void RebuildWorld()
    {
        Blueprints.Set(Content, _serverBook.Learnt);
        if (Machine != null && Machine.Target != LootTarget)
        {
            Machine = null;
        }
    }
}
