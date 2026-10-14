using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Targets;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The F2 panel's model over the client (GROUND.md §8.5): the server's stats, sections and players as the client's dev
/// module keeps them, worded for the panel; what the crosshair is on, from the same aim E uses, with the server's
/// description asked for as it changes; the last intents answered; the DRAW tab's toggles over the module's flags, which
/// <see cref="DevDraw"/> draws; the server's log.
/// </summary>
public sealed class ClientDeveloper : IDeveloperModel
{
    private readonly GameClient _client;
    private readonly List<InteractOption> _options = new();
    private AimHit _hit = AimHit.None;
    private TargetFacts _facts;
    private bool _hasFacts;
    private double _cachedAt = -1;
    private IReadOnlyList<SectionRow> _sections = Array.Empty<SectionRow>();
    private IReadOnlyList<InfoSection> _counts = Array.Empty<InfoSection>();
    private IReadOnlyList<PlayerRow> _players = Array.Empty<PlayerRow>();
    private int _linesSeen = -1;
    private IReadOnlyList<InfoRow> _lookServer = Array.Empty<InfoRow>();

    public ClientDeveloper(GameClient client)
    {
        _client = client;
    }

    private DevModule Dev => _client.Dev();

    public bool Admin => _client.Console().IsAdmin;

    public bool Connected => Dev.StatsClock >= 0 && _client.ClockSeconds - Dev.StatsClock < 3;

    public void Watch(bool on)
    {
        Dev.Watch(on);
        if (on)
        {
            // The log is cheap and its backlog should be there when the tab is clicked; once asked it stays on.
            Dev.WantLog = true;
        }
    }

    public DeveloperTab Shown { get; set; }

    /// <summary>Every frame, from the view: the eye and the look direction, for the LOOK tab (the same aim E uses).</summary>
    public void Aim(Num.Vector3 eye, Num.Vector3 forward)
    {
        if (!Dev.Watching || Shown != DeveloperTab.Look || _client.State != ClientState.Playing)
        {
            return;
        }
        _hit = _client.Interaction().Aim(eye, forward);
        _hasFacts = _hit.Hit && _client.Interaction().Options(_hit.Target, eye, _options, out _facts);
        Dev.Inspect(_hit.Hit ? _hit.Target : TargetRef.None);
    }

    // ---------------------------------------------------------------- SERVER and PLAYERS

    public string Headline
    {
        get
        {
            if (!Admin)
            {
                return "The server answers admins only: hosting a game makes you its admin; a dedicated server names them with --admin or admins.txt.";
            }
            if (Dev.Server is not { } s || !Connected)
            {
                return "Waiting for the server.";
            }
            return string.Create(CultureInfo.InvariantCulture,
                $"tick {s.Tick} at {s.TickRate} Hz: {s.AverageMs:0.00} ms average, {s.WorstMs:0.00} ms most, {s.Overruns} over the budget");
        }
    }

    public TickGraphView Ticks => new(Dev.AverageHistory, Dev.WorstHistory, Dev.HistoryStart, Dev.HistoryCount,
        1000f / (Dev.Server is { TickRate: > 0 } s ? s.TickRate : GameClient.TickRate));

    public IReadOnlyList<SectionRow> Sections
    {
        get
        {
            Refresh();
            return _sections;
        }
    }

    public IReadOnlyList<InfoSection> Counts
    {
        get
        {
            Refresh();
            return _counts;
        }
    }

    public IReadOnlyList<PlayerRow> Players
    {
        get
        {
            Refresh();
            return _players;
        }
    }

    // The server's stats come twice a second: worded once each time, not every frame.
    private void Refresh()
    {
        if (Dev.StatsClock == _cachedAt)
        {
            return;
        }
        _cachedAt = Dev.StatsClock;
        if (Dev.Server is not { } s)
        {
            _sections = Array.Empty<SectionRow>();
            _counts = Array.Empty<InfoSection>();
            _players = Array.Empty<PlayerRow>();
            return;
        }
        _sections = Dev.Sections.Select(x => new SectionRow(TickProfile.Label(x.Section), x.Depth, x.AverageMs, x.WorstMs)).ToArray();
        _counts = CountsOf(s);
        _players = Dev.Players.Select(p => new PlayerRow(p.Name, p.Admin, string.Equals(p.Name, _client.Name, StringComparison.OrdinalIgnoreCase),
            p.State switch
            {
                DevPlayerState.LoggingIn => "logging in",
                DevPlayerState.Wounded => "wounded",
                DevPlayerState.Dead => "dead",
                _ => "alive",
            },
            p.State == DevPlayerState.LoggingIn ? "" : string.Create(CultureInfo.InvariantCulture, $"{p.Position.X:0}, {p.Position.Y:0}, {p.Position.Z:0}"),
            p.PingMs, p.KilobitsDown, p.KilobitsUp, p.OnTimePercent, p.InView)).ToArray();
    }

    private static IReadOnlyList<InfoSection> CountsOf(in DevStats s)
    {
        var zone = new List<InfoRow>
        {
            new("Players on", N(s.Players?.Length ?? 0)),
            new("Characters in the zone", N(s.Characters)),
            new("Asleep, dead, wounded", $"{N(s.Asleep)}, {N(s.Dead)}, {N(s.Wounded)}"),
            new("Animals alive", N(s.AnimalsAlive)),
            new("Awake, lying dead", $"{N(s.AnimalsAwake)}, {N(s.AnimalsDead)}"),
            new("Deployables", N(s.Deployables)),
            new("World props", N(s.Props)),
            new("Loot bags", N(s.Bags)),
            new("Bodies", N(s.Corpses)),
            new("Building pieces", N(s.Pieces) + (s.Collapsing > 0 ? $" ({N(s.Collapsing)} collapsing)" : "")),
            new("Doors", N(s.Doors)),
            new("Claim devices", N(s.Claims)),
            new("Ships", N(s.Ships)),
            new("Explosives", N(s.Explosives)),
            new("Spent arrows", N(s.Arrows)),
            new("Natural objects changed", N(s.NaturalChanged)),
            new("Ground tiles", N(s.Tiles) + (s.TilesOnDemand > 0 ? $" ({N(s.TilesOnDemand)} built on demand)" : "")),
            new("Net entities", N(s.NetEntities)),
            new("Relevance radius", string.Create(CultureInfo.InvariantCulture, $"{s.RelevanceRadius:0} m")),
        };
        var game = new List<InfoRow>
        {
            new("Gathering hits", N(s.GatherHits)),
            new("Items gathered", N(s.Gathered)),
            new("Items picked up", N(s.PickedUp)),
            new("Crafted", N(s.Crafted)),
            new("Arrows shot", N(s.ArrowsShot)),
            new("Shots", $"{N(s.Shots)} ({N(s.BulletHits)} hit someone)"),
            new("Hits on players", N(s.CharacterHits)),
            new("Kills", N(s.Kills)),
            new("Knocked down", $"{N(s.Downed)} ({N(s.Recovered)} up again)"),
            new("Deaths", N(s.Deaths)),
            new("Hits on animals", N(s.AnimalHits)),
            new("Animals killed", N(s.AnimalKills)),
        };
        var process = new List<InfoRow>
        {
            new("Up for", Duration(s.UpSeconds)),
            new("Working set", $"{N(s.WorkingSetMb)} MB"),
            new("Managed heap", $"{N(s.HeapMb)} MB"),
            new("GC pauses", string.Create(CultureInfo.InvariantCulture, $"{s.GcPauseMs:0.00} ms in the last {s.IntervalSeconds:0.0} s")),
            new("Allocation", $"{N(s.AllocatedKbPerTick)} KB a tick"),
            new("Ticks over the budget", N(s.Overruns)),
            new("Last save", s.Saves ? $"{Duration(s.SinceSaveSeconds)} ago" : "no persistence"),
        };
        return new[] { new InfoSection("THE ZONE", zone), new InfoSection("THE GAME", game), new InfoSection("THE PROCESS", process) };
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Duration(long seconds) =>
        seconds >= 3600 ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600} h {seconds % 3600 / 60} min")
        : seconds >= 60 ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 60} min {seconds % 60} s")
        : string.Create(CultureInfo.InvariantCulture, $"{seconds} s");

    // ---------------------------------------------------------------- LOOK

    public string LookTitle
    {
        get
        {
            if (!_hit.Hit)
            {
                return "Nothing under the crosshair";
            }
            string name = _hasFacts && !string.IsNullOrEmpty(_facts.Name) ? _facts.Name : _hit.Target.Family.ToString();
            return string.Create(CultureInfo.InvariantCulture, $"{name}  ·  {Format(_hit.Target)}  ·  {_hit.Distance:0.0} m");
        }
    }

    public IReadOnlyList<InfoRow> LookHere
    {
        get
        {
            if (!_hit.Hit)
            {
                return Array.Empty<InfoRow>();
            }
            var rows = new List<InfoRow>
            {
                new("Family", _hit.Target.Family.ToString()),
                new("Id", _hit.Target.Id.ToString(CultureInfo.InvariantCulture)),
                new("Part", _hit.Target.Part.ToString(CultureInfo.InvariantCulture)),
                new("Distance", string.Create(CultureInfo.InvariantCulture, $"{_hit.Distance:0.00} m")),
                new("Point", string.Create(CultureInfo.InvariantCulture, $"({_hit.Point.X:0.0}, {_hit.Point.Y:0.0}, {_hit.Point.Z:0.0})")),
            };
            if (!_hasFacts)
            {
                rows.Add(new InfoRow("Facts", "none here: this client has no replica of it"));
                return rows;
            }
            var names = new List<string>();
            IReadOnlyList<string> keys = _client.Content.Capabilities.Keys;
            foreach (int index in _facts.Capabilities)
            {
                names.Add(index < keys.Count ? keys[index] : index.ToString(CultureInfo.InvariantCulture));
            }
            rows.Add(new InfoRow("Name", _facts.Name ?? ""));
            rows.Add(new InfoRow("Capabilities", names.Count == 0 ? "none" : string.Join(", ", names)));
            rows.Add(new InfoRow("Flags", _facts.Flags.ToString()));
            rows.Add(new InfoRow("Verbs", _options.Count == 0 ? "none" : string.Join(", ", _options.Select(o =>
                (_client.Content.Interactions.Verb(o.Verb)?.Key ?? o.Verb.ToString(CultureInfo.InvariantCulture)) + (o.Enabled ? "" : $" ({o.Reason.Code})")))));
            if (_facts.Deployable != null)
            {
                rows.Add(new InfoRow("Deployable", _facts.Deployable.Key));
            }
            if (_facts.Count > 0)
            {
                rows.Add(new InfoRow("Count", _facts.Count.ToString(CultureInfo.InvariantCulture)));
            }
            rows.Add(new InfoRow("Lock", _facts.Lock.ToString()));
            rows.Add(new InfoRow("Open, busy, body", $"{Yes(_facts.Open)}, {Yes(_facts.Busy)}, {Yes(_facts.Body)}"));
            return rows;
        }
    }

    public IReadOnlyList<InfoRow> LookServer
    {
        get
        {
            if (Dev.LinesRevision != _linesSeen)
            {
                _linesSeen = Dev.LinesRevision;
                _lookServer = Dev.Lines.Select(l => new InfoRow(l.Label, l.Value)).ToArray();
            }
            return _lookServer;
        }
    }

    public IReadOnlyList<InfoRow> Answers => Dev.Answers.Select(a => new InfoRow(a.Answer.Done ? "done" : "refused",
        string.Create(CultureInfo.InvariantCulture, $"#{a.Answer.Seq} {a.Answer.Code}{(a.Answer.Detail != 0 ? $" ({a.Answer.Detail})" : "")}  ·  {_client.ClockSeconds - a.ClockSeconds:0.0} s ago")))
        .ToArray();

    private static string Format(in TargetRef t) => $"{t.Family} {t.Id}" + (t.Part != 0 ? $", part {t.Part}" : "");

    private static string Yes(bool value) => value ? "yes" : "no";

    // ---------------------------------------------------------------- DRAW and LOG

    public IReadOnlyList<DrawToggle> Draws => new[]
    {
        new DrawToggle("hitboxes", "HITBOXES", "The capsules the shots and arrows test, on every character and animal in view (GROUND.md §5.6).", Dev.DrawHitboxes, false),
        new DrawToggle("tiles", "GROUND TILES", "The collision mesh under your feet and around it, as wireframe: the ground the server walks you on (§4.5).", Dev.DrawTiles, false),
        new DrawToggle("relevance", "RELEVANCE", "The circle of the relevance radius around you: what is inside it is sent to you (§4.4).", Dev.DrawRelevance, false),
        new DrawToggle("nav", "NAV GRID", "The animals' cells around you: green walkable, orange too steep, red blocked by a trunk or a rock, blue under water, grey not built yet (§6.9).", Dev.DrawNav, true),
        new DrawToggle("areas", "SPAWN AREAS", "The animals' spawn areas near you: each slot's home and range, its species and how many live (§6.9).", Dev.DrawAreas, true),
    };

    public void ToggleDraw(string key)
    {
        DevModule dev = Dev;
        switch (key)
        {
            case "hitboxes":
                dev.DrawHitboxes = !dev.DrawHitboxes;
                break;
            case "tiles":
                dev.DrawTiles = !dev.DrawTiles;
                break;
            case "relevance":
                dev.DrawRelevance = !dev.DrawRelevance;
                break;
            case "nav":
                dev.DrawNav = !dev.DrawNav;
                break;
            case "areas":
                dev.DrawAreas = !dev.DrawAreas;
                break;
        }
    }

    public IReadOnlyList<string> Log => Dev.Log;

    public int LogRevision => Dev.LogRevision;
}
