using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Surface;

namespace HollowUniverse.UI.Model;

// The mock's G7 world (GROUND.md §6.8): the first world's map as the game draws it (the same zone, the same stand-in
// ground, so the same picture and cache), a few markers round the landing site, a team with an invite, the level 1
// workbench's tech tree as world.json has it, and a recycler and research table to open.
public sealed partial class MockGroundUi
{
    private MapView? _map;
    private bool _mapTried;
    private readonly List<MapMarkerView> _markers = new();
    private readonly List<TeamInviteInfo> _invites = new();
    private readonly List<(string Key, int Cost, string[] After)> _treeNodes = new()
    {
        ("metal-hatchet", 75, Array.Empty<string>()), ("metal-pickaxe", 125, new[] { "metal-hatchet" }), ("machete", 75, Array.Empty<string>()),
        ("research-table", 75, Array.Empty<string>()), ("large-box", 75, Array.Empty<string>()), ("metal-door", 75, Array.Empty<string>()),
        ("double-door", 125, new[] { "metal-door" }), ("bed", 75, Array.Empty<string>()), ("medical-injector", 75, Array.Empty<string>()),
        ("pistol-ammo", 75, Array.Empty<string>()), ("weapon-light", 75, new[] { "pistol-ammo" }), ("tin-grenade", 125, new[] { "pistol-ammo" }),
        ("bag-charge", 125, new[] { "tin-grenade" }),
    };
    private readonly HashSet<string> _known = new() { "metal-hatchet", "research-table", "pistol-ammo" };
    private int _salvage = 340;
    private bool _world;
    private TeamInfo? _team;

    /// <summary>The first world's zone as the server makes it (ZoneSpec.FirstWorld), without the galaxy.</summary>
    private static readonly ZoneSpec SampleZone = new()
    {
        SystemAddress = FirstWorld.System.Value, BodyPath = FirstWorld.BodyPath, Radius = ZoneSpec.FirstWorldRadius,
        SurfaceSeed = Hash64.Of(FirstWorld.System.Value, FirstWorld.BodyPath), StandIn = true,
        LatitudeDegrees = FirstWorld.LandingLatitude, LongitudeDegrees = FirstWorld.LandingLongitude,
    };

    /// <summary>The whole first world's map (rendered off the main thread by the interface, then cached).</summary>
    public MapView? Map
    {
        get
        {
            if (!_mapTried)
            {
                _mapTried = true;
                ISurface surface = SampleZone.CreateSurface(diagnostic: true);
                var map = new Hollow.Client.Map.ZoneMap(SampleZone, surface, ZoneFrame.World, new WorldData());
                _map = new MapView(map.CacheKey, 1024, () => map.Render(1024).Rgba, map.Radius, map.Cells,
                    RenderCancellable: cancellation => map.Render(1024, cancellation).Rgba);
            }
            return _map;
        }
    }

    /// <summary>A point of the sample world: metres east and south of the landing site, as a direction from the world's centre.</summary>
    internal static Vector3 Near(float east, float south)
    {
        var frame = new ZoneFrame(SampleZone.LandingDirection * SampleZone.Radius);
        Vector3D d = frame.ToBody(east, 0, south).Normalized;
        return new Vector3((float)d.X, (float)d.Y, (float)d.Z);
    }

    public IReadOnlyList<MapMarkerView> MapMarkers => _markers;

    public int MaxMarkers => 5;

    public void PlaceMarker(Vector3 direction, int colour, string label)
    {
        int index = _markers.Count(m => m.Kind == MapMarkerKind.Marker);
        if (index < MaxMarkers)
        {
            _markers.Add(new MapMarkerView(MapMarkerKind.Marker, direction, label, Colour: colour, Index: index));
        }
    }

    public void RemoveMarker(int index)
    {
        _markers.RemoveAll(m => m.Kind == MapMarkerKind.Marker && (index < 0 || m.Index == index));
        int i = 0;
        for (int k = 0; k < _markers.Count; k++)
        {
            if (_markers[k].Kind == MapMarkerKind.Marker)
            {
                _markers[k] = _markers[k] with { Index = i++ };
            }
        }
    }

    public TechTreeView? TechTree => _world ? new TechTreeView(1, _treeNodes.Select(n => new TechNodeView(n.Key, n.Cost, Depth(n.Key), n.After,
        _known.Contains(n.Key) ? TechNodeState.Known : n.After.Length == 0 || n.After.Any(_known.Contains) ? TechNodeState.Open : TechNodeState.Locked)).ToArray(), _salvage) : null;

    private int Depth(string key)
    {
        (string _, int _, string[] after) = _treeNodes.First(n => n.Key == key);
        return after.Length == 0 ? 0 : 1 + Depth(after[0]);
    }

    public void Unlock(string key)
    {
        (string Key, int Cost, string[] After) node = _treeNodes.FirstOrDefault(n => n.Key == key);
        if (node.Key != null && !_known.Contains(key) && _salvage >= node.Cost && (node.After.Length == 0 || node.After.Any(_known.Contains)))
        {
            _known.Add(key);
            _salvage -= node.Cost;
        }
        else
        {
            Notice?.Invoke("You can't unlock that yet");
        }
    }

    public TeamInfo? Team => _team;

    public IReadOnlyList<TeamInviteInfo> TeamInvites => _invites;

    public void CreateTeam() => _team ??= new TeamInfo("team-1", new[] { new TeamMemberInfo("me", PlayerName, true, true, true) }, true);

    public void LeaveTeam() => _team = null;

    public void AcceptInvite(string team)
    {
        _invites.RemoveAll(i => i.Team == team);
        _team = new TeamInfo(team, new[] { new TeamMemberInfo("cy", "Cy", true, true, false), new TeamMemberInfo("me", PlayerName, true, false, true) }, false);
    }

    public void DeclineInvite(string team) => _invites.RemoveAll(i => i.Team == team);

    public void KickFromTeam(string member)
    {
        if (_team != null)
        {
            _team = _team with { Members = _team.Members.Where(m => m.Id != member).ToArray() };
        }
    }

    public void PromoteInTeam(string member)
    {
        if (_team != null)
        {
            _team = _team with
            {
                Members = _team.Members.Select(m => m with { Leader = m.Id == member }).ToArray(),
                Leading = member == "me",
            };
        }
    }

    public void SayTeam(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            ChatReceived?.Invoke(new ChatLine(PlayerName, text.Trim()) { Team = true });
        }
    }

    /// <summary>The world's sample: markers on the map, a team of three with an invite waiting, the tech tree at a level 1 bench.</summary>
    public void WorldSample()
    {
        _world = true;
        _markers.Clear();
        _markers.Add(new MapMarkerView(MapMarkerKind.Me, Near(0, 0), Facing: 40));
        _markers.Add(new MapMarkerView(MapMarkerKind.Teammate, Near(170, -120), "Ada", Facing: 200));
        _markers.Add(new MapMarkerView(MapMarkerKind.Teammate, Near(-260, 210), "Ben", Dim: true));
        _markers.Add(new MapMarkerView(MapMarkerKind.Death, Near(-520, 380)));
        _markers.Add(new MapMarkerView(MapMarkerKind.Bed, Near(120, 90), "Bed"));
        _markers.Add(new MapMarkerView(MapMarkerKind.Bag, Near(-140, -330), "Ridge bag"));
        _markers.Add(new MapMarkerView(MapMarkerKind.Marker, Near(380, -260), "Base", Colour: 2, Index: 0));
        _markers.Add(new MapMarkerView(MapMarkerKind.Marker, Near(-700, -90), "Bay crates", Colour: 3, Index: 1));
        _markers.Add(new MapMarkerView(MapMarkerKind.Marker, Near(640, 520), "Sulphur", Colour: 1, Index: 2));
        _team = new TeamInfo("team-1", new[]
        {
            new TeamMemberInfo("me", PlayerName, true, true, true),
            new TeamMemberInfo("ada", "Ada", true, false, false),
            new TeamMemberInfo("ben", "Ben", false, false, false),
        }, true);
        _invites.Clear();
        _invites.Add(new TeamInviteInfo("team-2", "Cy", 212));
        Workbench = 1;
    }

    // Names for the G7 items and the tech tree's (items.json's keys, as the game's model gives them).
    private void DefineWorldItems()
    {
        void Add(string key, string name, string category, int stack, string description) =>
            _items[key] = new ItemInfo(key, name, description, category, stack);
        Add("salvage", "Salvage", "RESOURCES", 1000, "Scrap metal, wire and broken parts. The recycler pays in it; workbenches, research and the tech tree cost it.");
        Add("gears", "Gears", "COMPONENTS", 20, "A handful of toothed wheels out of an old gearbox. Heavy doors need them.");
        Add("metal-pipe", "Metal pipe", "COMPONENTS", 20, "A length of seamless steel pipe: barrels for guns, tubes for rockets.");
        Add("refined-metal", "Refined metal", "RESOURCES", 100, "Pure metal, for the strongest walls and for firearms.");
        Add("metal-fragments", "Metal fragments", "RESOURCES", 1000, "Smelted metal, for tools, weapons and sheet metal walls.");
        Add("blueprint", "Blueprint", "ITEMS", 1, "Use it to learn how to make the item it shows.");
        Add("pistol", "Service pistol", "WEAPONS", 1, "A semi-automatic pistol.");
        Add("holo-sight", "Holo sight", "WEAPONS", 1, "A reflex sight: twice the zoom, a tighter aim.");
        Add("silencer", "Silencer", "WEAPONS", 1, "Quietens a gun and hides its flash.");
        Add("metal-hatchet", "Metal hatchet", "TOOLS", 1, "Fells trees half again as fast as a stone one.");
        Add("metal-pickaxe", "Metal pickaxe", "TOOLS", 1, "Breaks outcrops half again as fast as a stone one.");
        Add("machete", "Machete", "WEAPONS", 1, "A long blade: quick swings, deep cuts.");
        Add("research-table", "Research table", "ITEMS", 1, "Takes an item apart to learn how it is made. Put the item and salvage in and start: a blueprint comes out.");
        Add("large-box", "Large box", "ITEMS", 1, "Forty-eight slots of storage.");
        Add("metal-door", "Sheet metal door", "CONSTRUCTION", 1, "A door of riveted sheet metal for a doorway.");
        Add("double-door", "Sheet metal double door", "CONSTRUCTION", 1, "A pair of sheet metal doors for a wall frame.");
        Add("bed", "Bed", "ITEMS", 1, "A frame and a mattress. You can come back here after you die, every two minutes.");
        Add("medical-injector", "Medical injector", "MEDICAL", 2, "Heals quickly and stops bleeding.");
        Add("pistol-ammo", "Pistol rounds", "AMMO", 128, "Rounds for the service pistol.");
        Add("weapon-light", "Weapon light", "WEAPONS", 1, "A light on a gun's rail.");
        Add("tin-grenade", "Tin grenade", "WEAPONS", 10, "A can of gunpowder with a fuse. Throw it and run.");
        Add("bag-charge", "Bag charge", "WEAPONS", 10, "Four tin grenades in a bag. Sticks where it lands; now and then it fizzles.");
        Add("togg-cab-one", "One-seat cab", "VEHICLES", 1, "A welded cab for one: the driver's seat when it is the front cab, a lookout on another's roof.");
        Add("togg-engine", "TOGG engine", "VEHICLES", 1, "An open engine block for a bay or the nose mount.");
        Add("monster-togg-engine", "Blown six", "VEHICLES", 1, "A supercharged six for a bay or the nose mount: five times a TOGG engine's power.");
        Add("togg-tyres", "TOGG tyres", "VEHICLES", 1, "Four 0.96 m knobbed tyres on beadlock rims: the TOGG's own running gear.");
        Add("monster-tyres", "Monster tyres", "VEHICLES", 1, "Four 1.7 m flotation tyres. They need a lift kit under them.");
        Add("togg-lift-kit", "TOGG lift kit", "VEHICLES", 1, "Long-travel arms and coil-overs: stands a TOGG's body 0.75 m higher.");
    }

    /// <summary>
    /// A TOGG in the vehicle workbench's bay being made a monster (VEHICLES.md §9.11): its cab, the blown six, the lift kit
    /// fitted, the monster tyres in hand, its stock tyres and engine taken off into the inventory.
    /// </summary>
    public void OpenVehicle()
    {
        string[] labels = { "BAY 1", "BAY 1 ROOF", "BAY 2", "BAY 2 ROOF", "BAY 3  82%", "BAY 3 ROOF", "NOSE  64%", "SUSPENSION  100%", "WHEELS" };
        _loot = new ItemStack[labels.Length];
        _loot[4] = new ItemStack("togg-cab-one", 1, 0.82f);
        _loot[6] = new ItemStack("monster-togg-engine", 1, 0.64f);
        _loot[7] = new ItemStack("togg-lift-kit", 1, 1f);
        _main[0] = new ItemStack("togg-tyres", 1);
        _main[1] = new ItemStack("togg-engine", 1);
        _main[2] = new ItemStack("monster-tyres", 1);
        Loot = new LootView(ContainerKind.Vehicle, "TOGG", _loot)
        {
            Labels = labels,
            Lines = new[] { "CHASSIS 91%", "MASS 1520 KG", "POWER 450 KW", "TOP SPEED 122 KM/H", "NO WHEELS", "LIFTED 0.75 M" },
        };
    }

    /// <summary>Opens a recycler or a research table at work, with sample contents.</summary>
    public void OpenMachine(ContainerKind kind)
    {
        if (kind == ContainerKind.Recycler)
        {
            _loot = new ItemStack[12];
            _loot[0] = new ItemStack("gears", 6);
            _loot[1] = new ItemStack("metal-pipe", 3);
            _loot[2] = new ItemStack("pistol", 1, 0.4f);
            _loot[6] = new ItemStack("salvage", 40);
            _loot[7] = new ItemStack("metal-fragments", 52);
            _loot[8] = new ItemStack("refined-metal", 2);
            Loot = new LootView(kind, "RECYCLER", _loot) { IsOn = true, Progress = 0.6f };
            return;
        }
        _loot = new ItemStack[3];
        _loot[0] = new ItemStack("holo-sight", 1);
        _loot[1] = new ItemStack("salvage", 160);
        _loot[2] = new ItemStack("blueprint", 1) { Teaches = "silencer" };
        Loot = new LootView(kind, "RESEARCH TABLE", _loot) { Researching = "holo-sight", Cost = 125 };
    }
}
