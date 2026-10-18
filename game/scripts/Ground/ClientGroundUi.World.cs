using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hollow.Client;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Economy;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Targets;
using HollowUniverse.UI.Model;
using Num = System.Numerics;
using SimRecipe = Hollow.Simulation.Content.Recipe;
using SimStack = Hollow.Simulation.Items.ItemStack;
using UiContainerKind = HollowUniverse.UI.Model.ContainerKind;

namespace HollowUniverse.Ground;

// The interface's G7 members over the client (GROUND.md §6.8): the map of the whole world and what is on it (the player,
// teammates, the last death, bags and beds, the player's markers, each as its direction from the world's centre; facings
// from local north), the tech tree of the workbench in
// reach, the team and its invites, the recycler's and research table's panels, blueprints drawn as what they teach,
// and the notices and sounds for blueprints learnt, invites and the team's changes.
public sealed partial class ClientGroundUi
{
    /// <summary>The map picture's side in pixels (rendered once, then cached on disk by the interface).</summary>
    private const int MapPixels = 1024;

    /// <summary>Notices wait this long after joining, so the lists the server sends at once (blueprints, the team) aren't news.</summary>
    private const double QuietSeconds = 3;

    private MapView? _mapView;
    private Hollow.Client.Map.ZoneMap? _mapSource;
    private readonly List<MapMarkerView> _mapMarkers = new();
    private double _mapMarkersAt = double.MinValue;
    private TechTreeView? _tree;
    private (TechTree? Tree, int Salvage, long Known) _treeKey;
    private TargetRef _techBench;
    private TeamView? _teamFrom;
    private TeamInfo? _team;
    private readonly List<TeamInviteInfo> _invites = new();
    private TeamView? _teamBefore;
    private double? _settledAt;

    /// <summary>Every frame, from the ground view: notices start once the first lists after joining are in.</summary>
    public void Frame()
    {
        if (_settledAt == null && _client.State == ClientState.Playing)
        {
            _settledAt = _client.ClockSeconds + QuietSeconds;
        }
    }

    private bool Settled => _settledAt is { } at && _client.ClockSeconds >= at;

    // ---------------------------------------------------------------- the map

    public MapView? Map
    {
        get
        {
            if (_client.World().Map is not { } map)
            {
                _mapSource = null;
                return _mapView = null;
            }
            if (_mapView == null || !ReferenceEquals(_mapSource, map))
            {
                _mapSource = map;
                _mapView = new MapView(map.CacheKey, MapPixels, () => map.Render(MapPixels).Rgba, map.Radius, map.Cells,
                    token => map.Render(MapPixels, token).Rgba);
            }
            return _mapView;
        }
    }

    /// <summary>Where the camera looks, a direction in the zone's frame (the ground view sets it): the map's arrow faces it.</summary>
    public Func<Num.Vector3>? ViewDirection { get; set; }

    public IReadOnlyList<MapMarkerView> MapMarkers
    {
        get
        {
            double now = _client.ClockSeconds;
            if (now - _mapMarkersAt < 0.05 && now >= _mapMarkersAt)
            {
                return _mapMarkers;
            }
            _mapMarkersAt = now;
            _mapMarkers.Clear();
            if (_client.World().Map == null)
            {
                return _mapMarkers;
            }
            MapMarkerView At(MapMarkerKind kind, Num.Vector3 zone, string label = "", float facing = 0, int colour = 0, int index = -1, bool dim = false) =>
                new(kind, OnMap(zone), label, facing, colour, index, dim);
            foreach (RespawnPointView point in Me.RespawnPoints)
            {
                _mapMarkers.Add(At(point.Bed ? MapMarkerKind.Bed : MapMarkerKind.Bag, point.Position,
                    point.Name.Length > 0 ? point.Name : C.Item(point.Bed ? "bed" : "sleeping-bag")?.Name ?? (point.Bed ? "Bed" : "Sleeping bag")));
            }
            if (Me.DeathPoint is { } death)
            {
                _mapMarkers.Add(At(MapMarkerKind.Death, death));
            }
            foreach (MarkerView marker in Me.Markers)
            {
                _mapMarkers.Add(At(MapMarkerKind.Marker, marker.Position, marker.Label, colour: marker.Colour, index: marker.Index));
            }
            foreach (TeammateView mate in Me.Teammates)
            {
                // A teammate in range is where they are drawn; further out, where the server last said (twice a second).
                Num.Vector3 position = mate.Position;
                float yaw = mate.Yaw;
                foreach (RemoteEntity e in _client.Entities)
                {
                    if (e.Id == mate.Id)
                    {
                        (position, yaw) = (e.Position, e.Yaw);
                        break;
                    }
                }
                string label = mate.State switch
                {
                    TeammateState.Down => mate.Name + " (down)",
                    TeammateState.Dead => mate.Name + " (dead)",
                    TeammateState.Asleep => mate.Name + " (asleep)",
                    _ => mate.Name,
                };
                _mapMarkers.Add(At(MapMarkerKind.Teammate, position, label, Facing(yaw), dim: mate.State != TeammateState.Awake));
            }
            if (_client.HasCharacter && !Me.IsDead)
            {
                // Facings measured from local north (docs/WHOLE_WORLD.md §5): flying, the ship's nose; on foot, the view.
                if (_client.Ships().Piloting && _client.Ships().OwnShipDef != null)
                {
                    Num.Vector3 at = _client.Ships().OwnShipRenderPosition;
                    _mapMarkers.Add(At(MapMarkerKind.Me, at, facing: Facing(Hollow.Simulation.Movement.Facing.YawOf(_client.Ships().OwnShip.Forward, _client.Frame.UpAt(at)))));
                }
                else
                {
                    Num.Vector3 at = _client.Character.Position;
                    Num.Vector3 view = ViewDirection?.Invoke() ?? _client.Frame.UpAt(at);
                    _mapMarkers.Add(At(MapMarkerKind.Me, at, facing: Facing(Hollow.Simulation.Movement.Facing.YawOf(view, _client.Frame.UpAt(at)))));
                }
            }
            return _mapMarkers;
        }
    }

    public int MaxMarkers => C.World.MaxMarkers;

    public void PlaceMarker(Num.Vector3 direction, int colour, string label)
    {
        if (_client.World().Map is { } map && direction.LengthSquared() > 1e-12f)
        {
            Me.PlaceMarker(map.Ground(new Vector3D(direction.X, direction.Y, direction.Z)), (byte)Math.Clamp(colour, 0, 5), label);
            _mapMarkersAt = double.MinValue;
        }
    }

    public void RemoveMarker(int index)
    {
        Me.RemoveMarker(index);
        _mapMarkersAt = double.MinValue;
    }

    // A yaw (radians from local north, anticlockwise seen from above: the movement code's) as the map's facing: degrees
    // clockwise from local north.
    private static float Facing(float yaw) => Degrees(-yaw);

    private static float Degrees(float radians) => ((radians * 180 / MathF.PI) % 360 + 360) % 360;

    // A point of the zone as the map takes it: its direction from the world's centre, in the body frame.
    private Num.Vector3 OnMap(Num.Vector3 zone)
    {
        if (_client.World().Map is not { } map)
        {
            return Num.Vector3.UnitY;
        }
        Vector3D d = map.DirectionOf(zone);
        return new Num.Vector3((float)d.X, (float)d.Y, (float)d.Z);
    }

    // ---------------------------------------------------------------- the tech tree

    public TechTreeView? TechTree
    {
        get
        {
            if (_techBench.IsNone)
            {
                _techBench = _client.Interaction().BenchNear();
            }
            if (_client.Interaction().TechTreeAt(_techBench) is not { } tree)
            {
                _techBench = TargetRef.None;
                return null;
            }
            int salvage = C.World.Currency is { } currency ? Me.Inventory.Count(currency) : 0;
            (TechTree, int, long) key = (tree, salvage, KnownSignature());
            if (_tree != null && key == _treeKey)
            {
                return _tree;
            }
            _treeKey = key;
            var nodes = new TechNodeView[tree.Nodes.Count];
            for (int i = 0; i < nodes.Length; i++)
            {
                TechNode node = tree.Nodes[i];
                TechNodeState state = Me.Knows(node.Recipe) ? TechNodeState.Known
                    : TechTrees.CanUnlock(node, Me.Blueprints) == OutcomeCode.NeedsEarlierNode ? TechNodeState.Locked
                    : TechNodeState.Open;
                nodes[i] = new TechNodeView(node.Item.Key, node.Cost, node.Depth, node.After.Select(a => a.Item.Key).ToArray(), state);
            }
            return _tree = new TechTreeView(tree.Workbench, nodes, salvage, tree.Station?.Item.Name);
        }
    }

    public void Unlock(string key)
    {
        if (C.Item(key) is not { } item || C.World.NodeOf(item.Id) is not { } node)
        {
            return;
        }
        Outcome can = Me.CanUnlock(node);
        if (!can.Ok)
        {
            Notice?.Invoke(C.Texts.Line(can));
            return;
        }
        if (_client.Interaction().TechTreeAt(_techBench)?.Nodes.Contains(node) != true)
        {
            return;
        }
        Me.Unlock(_techBench, node.Recipe.Id);
    }

    // Available knowledge, including the admin override (the same learnt set gives the same number in any order).
    private long KnownSignature()
    {
        IReadOnlyCollection<ushort> learnt = Me.Blueprints.Learnt;
        long sum = 0, mix = 0;
        foreach (ushort id in learnt)
        {
            sum += id;
            mix ^= (id * 2654435761L) & 0xFFFFFFFF;
        }
        return ((long)learnt.Count << 40) ^ (sum << 20) ^ mix ^ (Me.FreeBuild ? long.MinValue : 0);
    }

    // ---------------------------------------------------------------- the team

    public TeamInfo? Team
    {
        get
        {
            TeamView? team = Me.Team;
            if (!ReferenceEquals(team, _teamFrom))
            {
                _teamFrom = team;
                _team = team == null ? null : new TeamInfo(Id(team.Id),
                    team.Members.Select(m => new TeamMemberInfo(Id(m.Id.Value), m.Name, m.Online, m.Leader, m.Me)).ToArray(), Me.LeadsTeam, C.World.TeamSize);
            }
            return _team;
        }
    }

    public IReadOnlyList<TeamInviteInfo> TeamInvites
    {
        get
        {
            _invites.Clear();
            double now = _client.ClockSeconds;
            foreach (TeamInviteView invite in Me.TeamInvites)
            {
                if (invite.ExpiresAtSeconds > now)
                {
                    _invites.Add(new TeamInviteInfo(Id(invite.Team), invite.From, (float)(invite.ExpiresAtSeconds - now)));
                }
            }
            return _invites;
        }
    }

    public void CreateTeam() => Me.CreateTeam();

    public void LeaveTeam() => Me.LeaveTeam();

    public void AcceptInvite(string team)
    {
        if (ulong.TryParse(team, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong id))
        {
            Me.AcceptInvite(id);
        }
    }

    public void DeclineInvite(string team)
    {
        if (ulong.TryParse(team, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong id))
        {
            Me.DeclineInvite(id);
        }
    }

    public void KickFromTeam(string member)
    {
        if (ulong.TryParse(member, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong id))
        {
            Me.KickFromTeam(new EntityId(id));
        }
    }

    public void PromoteInTeam(string member)
    {
        if (ulong.TryParse(member, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong id))
        {
            Me.PromoteInTeam(new EntityId(id));
        }
    }

    public void SayTeam(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }
        if (Me.Team == null)
        {
            Notice?.Invoke("You're not in a team.");
            return;
        }
        _client.World().SendTeamChat(text);
    }

    private static string Id(ulong id) => id.ToString(CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- the recycler and the research table

    // The looted machine as the server last said, if it is this one.
    private MachineView? MachineOf(RemoteThing thing) => Me.Machine is { } m && m.Target == thing.Id ? m : null;

    private LootView Recycler(RemoteThing thing)
    {
        MachineView? m = MachineOf(thing);
        return new LootView(UiContainerKind.Recycler, (thing.Prop(C)?.Name ?? "Recycler").ToUpperInvariant(), LootSlots())
        {
            IsOn = m?.Running ?? thing.Lit,
            Progress = m?.Progress ?? 0,
        };
    }

    // The research table: what it researches (or would), for how much salvage, and why it can't start.
    private LootView ResearchTable(RemoteThing thing, DeployableDef def, ResearchStats research)
    {
        MachineView? m = MachineOf(thing);
        bool running = m?.Running ?? thing.Lit;
        var view = new LootView(UiContainerKind.ResearchTable, def.Item.Name.ToUpperInvariant(), LootSlots())
        {
            IsOn = running,
            Progress = m?.Progress ?? 0,
            Researching = m?.Researching?.Key,
        };
        if (running || Me.Loot is not { } slots)
        {
            return view;
        }
        SimStack item = research.ItemSlot < slots.Size ? slots[research.ItemSlot] : default;
        if (item.IsEmpty || C.Item(item.Item) is not { } what)
        {
            return view with { Researching = null, Reason = "Put in an item to research" };
        }
        if (!Research.CanResearch(C, what) || C.Recipe(what.Id) is not { } recipe)
        {
            return view with { Researching = what.Key, Reason = $"{what.Name} can't be researched" };
        }
        int cost = Research.Cost(C, recipe);
        view = view with { Researching = what.Key, Cost = cost };
        if (research.OutputSlot < slots.Size && !slots[research.OutputSlot].IsEmpty)
        {
            return view with { Reason = "Take the blueprint out first" };
        }
        SimStack salvage = research.SalvageSlot < slots.Size ? slots[research.SalvageSlot] : default;
        return salvage.Item != (C.World.Currency?.Id ?? 0) || salvage.Count < cost ? view with { Reason = $"Needs {cost} salvage" } : view;
    }

    // ---------------------------------------------------------------- notices and sounds

    private void SubscribeWorld(LocalPlayer me)
    {
        me.BlueprintLearned += OnBlueprintLearned;
        me.TeamChanged += OnTeamChanged;
        me.TeamInviteReceived += OnTeamInvite;
    }

    private void UnsubscribeWorld(LocalPlayer me)
    {
        me.BlueprintLearned -= OnBlueprintLearned;
        me.TeamChanged -= OnTeamChanged;
        me.TeamInviteReceived -= OnTeamInvite;
    }

    private void OnBlueprintLearned(SimRecipe recipe)
    {
        if (!Settled)
        {
            return;
        }
        Notice?.Invoke($"Blueprint learnt: {recipe.Output.Name}.");
        Sounds.Flat(Sounds.Item("blueprint", "learn"));
    }

    private void OnTeamInvite(TeamInviteView invite)
    {
        if (Settled)
        {
            Notice?.Invoke($"{invite.From} invites you to their team: answer on the map (G).");
            Sounds.Flat("ui.notify");
        }
    }

    private void OnTeamChanged(TeamView? team)
    {
        TeamView? before = _teamBefore;
        _teamBefore = team;
        if (!Settled)
        {
            return;
        }
        if (team == null)
        {
            if (before != null)
            {
                Notice?.Invoke("You are no longer in a team.");
            }
            return;
        }
        string NameOf(EntityId id) => team.Members.FirstOrDefault(m => m.Id == id).Name ?? "";
        if (before == null || before.Id != team.Id)
        {
            Notice?.Invoke(team.Leader == Me.EntityId && team.Members.Count <= 1
                ? "You made a team: look at a player close by and press E to invite them."
                : $"You joined {NameOf(team.Leader)}'s team.");
            return;
        }
        foreach (TeamMemberView m in team.Members)
        {
            if (!before.Has(m.Id))
            {
                Notice?.Invoke($"{m.Name} joined the team.");
            }
        }
        foreach (TeamMemberView m in before.Members)
        {
            if (!team.Has(m.Id))
            {
                Notice?.Invoke($"{m.Name} left the team.");
            }
        }
        if (before.Leader != team.Leader && team.Has(team.Leader))
        {
            Notice?.Invoke(team.Leader == Me.EntityId ? "You lead the team now." : $"{NameOf(team.Leader)} leads the team now.");
        }
    }
}
