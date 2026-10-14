using System;
using System.Collections.Generic;
using System.Globalization;

namespace HollowUniverse.UI.Model;

/// <summary>
/// The F2 panel on sample data (GROUND.md §8.5), for the interface viewer (<c>--view ui --screen developer</c>, and
/// <c>developer-players</c>, <c>developer-look</c>, <c>developer-draw</c>, <c>developer-log</c>): a minute of ticks, the
/// profile's sections, the counts, three players, a door looked at, a few intents, the toggles and a log.
/// </summary>
public sealed class MockDeveloper : IDeveloperModel
{
    private static readonly (string Name, int Depth)[] SectionNames =
    {
        ("tick", 0), ("receive", 1), ("logins, respawns", 1), ("step", 1), ("ground (characters)", 2), ("broadphase", 2), ("movement", 2),
        ("ships", 2), ("ground released", 2), ("game", 2), ("after the step", 1), ("replicate", 1), ("snapshots", 2), ("things", 2),
        ("nature", 2), ("building", 2), ("save", 1), ("send", 1), ("[tile built/waited]", 0), ("[tile added]", 0), ("[props added]", 1),
        ("[tile removed]", 0), ("[props removed]", 1), ("[nature looked up]", 0), ("[ground ahead]", 0), ("[respawns]", 0),
    };

    private readonly float[] _average = new float[120], _worst = new float[120];
    private readonly List<string> _log = new();
    private readonly Dictionary<string, bool> _draws = new(StringComparer.Ordinal)
    {
        ["hitboxes"] = true, ["tiles"] = false, ["relevance"] = true, ["nav"] = false, ["areas"] = false,
    };

    public MockDeveloper()
    {
        for (int i = 0; i < _average.Length; i++)
        {
            _average[i] = 4.1f + 0.6f * MathF.Sin(i * 0.21f) + (i % 31 == 0 ? 2.5f : 0);
            _worst[i] = _average[i] + 3.5f + 2f * MathF.Sin(i * 0.37f + 1) + (i % 31 == 0 ? 24f : 0);
        }
        string[] lines =
        {
            "Hollow development server: zone on Nolcas Peris CQ-SLY 7-0 B 3 at 31.2042° N, 12.8810° E, port 27015, database hollow-server.db, data 4F0C7A2B9E11D3A8",
            "admins: Host", "Ready. Type help for commands.", "Host connected from 127.0.0.1:52011 (protocol 20)", "Host logged in: character 2, awake at (12.4, 31.0, -8.2)",
            "[console] Host: give rifle", "[console] Host: spawn wolf 2", "[dev] Host watches the server", "Ada connected from 192.168.1.20:60422 (protocol 20)",
            "Ada logged in: new character 7 on the beach", "saved: 2 characters, 14 things, 23 building pieces", "[console] Host: time 21",
        };
        int second = 0;
        foreach (string line in lines)
        {
            _log.Add(string.Create(CultureInfo.InvariantCulture, $"12:{second / 60:00}:{second % 60:00}  {line}"));
            second += 7;
        }
    }

    public bool Admin => true;

    public bool Connected => true;

    public void Watch(bool on)
    {
    }

    public DeveloperTab Shown { get; set; }

    public string Headline => "tick 48211 at 30 Hz: 4.12 ms average, 9.80 ms most, 3 over the budget";

    public TickGraphView Ticks => new(_average, _worst, 0, _average.Length, 1000f / 30);

    public IReadOnlyList<SectionRow> Sections
    {
        get
        {
            var rows = new List<SectionRow>();
            float[] averages = { 4.12f, 0.31f, 0.02f, 1.64f, 0.08f, 0.21f, 0.74f, 0.09f, 0.03f, 0.49f, 0.11f, 1.73f, 1.02f, 0.38f, 0.12f, 0.21f, 0.00f, 0.29f, 0.00f, 0.05f, 0.03f, 0.01f, 0.01f, 0.02f, 0.04f, 0.00f };
            for (int i = 0; i < SectionNames.Length; i++)
            {
                rows.Add(new SectionRow(SectionNames[i].Name, SectionNames[i].Depth, averages[i], averages[i] * (i == 0 ? 2.4f : 1.8f)));
            }
            return rows;
        }
    }

    public IReadOnlyList<InfoSection> Counts => new[]
    {
        new InfoSection("THE ZONE", new InfoRow[]
        {
            new("Players on", "3"), new("Characters in the zone", "5"), new("Asleep, dead, wounded", "2, 0, 0"), new("Animals alive", "14"),
            new("Awake, lying dead", "9, 1"),
            new("Deployables", "27"), new("World props", "212"), new("Loot bags", "2"), new("Bodies", "1"), new("Building pieces", "23"),
            new("Doors", "2"), new("Claim devices", "1"), new("Ships", "1"), new("Explosives", "0"), new("Spent arrows", "4"),
            new("Natural objects changed", "61"), new("Ground tiles", "48"), new("Net entities", "19"), new("Relevance radius", "300 m"),
        }),
        new InfoSection("THE GAME", new InfoRow[]
        {
            new("Gathering hits", "412"), new("Items gathered", "3,180"), new("Items picked up", "96"), new("Crafted", "38"), new("Arrows shot", "17"),
            new("Shots", "140 (31 hit someone)"), new("Hits on players", "44"), new("Kills", "3"), new("Knocked down", "2 (1 up again)"), new("Deaths", "4"),
            new("Hits on animals", "26"), new("Animals killed", "5"),
        }),
        new InfoSection("THE PROCESS", new InfoRow[]
        {
            new("Up for", "1 h 12 min"), new("Working set", "412 MB"), new("Managed heap", "96 MB"), new("GC pauses", "0.00 ms in the last 0.5 s"),
            new("Allocation", "3 KB a tick"), new("Ticks over the budget", "3"), new("Last save", "14 s ago"),
        }),
    };

    public IReadOnlyList<PlayerRow> Players => new[]
    {
        new PlayerRow("Host", true, true, "alive", "12, 31, -8", 1, 128.4f, 21.2f, 100, 19),
        new PlayerRow("Ada", false, false, "alive", "140, 29, 77", 38, 96.0f, 18.7f, 99, 12),
        new PlayerRow("Ben", false, false, "wounded", "-52, 34, 210", 112, 61.5f, 17.9f, 91, 7),
    };

    public string LookTitle => "Wooden door  ·  Door 1842  ·  2.4 m";

    public IReadOnlyList<InfoRow> LookHere => new InfoRow[]
    {
        new("Family", "Door"), new("Id", "1842"), new("Part", "1 (the leaf)"), new("Distance", "2.4 m"), new("Point", "(14.1, 31.9, -6.0)"),
        new("Name", "Wooden door"), new("Capabilities", "door, key-lock"), new("Flags", "Authorised, CanUse"), new("Verbs", "open, lock, pick-up (NotOwner)"),
        new("Lock", "Key"), new("Open", "no"),
    };

    public IReadOnlyList<InfoRow> LookServer => new InfoRow[]
    {
        new("Target", "Door 1842, part 1"), new("Position", "(14.1, 31.0, -6.0)"), new("Model", "door-wood"), new("Capabilities", "door, key-lock"),
        new("Flags for you", "Authorised, CanUse"), new("Verbs for you", "open, lock, pick-up (NotOwner)"), new("Object", "Door"), new("Id", "1842"),
        new("Ref", "Door 1842"), new("Frame", "Piece 1831"), new("Structure", "4"), new("Kind", "Wood"), new("Health", "250"), new("MaxHealth", "250"),
        new("Open", "no"), new("Lock", "Key"), new("Owner", "7 (Ada)"), new("Keys", "2 items"), new("Yaw", "1.571"), new("Revision", "12"),
    };

    public IReadOnlyList<InfoRow> Answers => new InfoRow[]
    {
        new("done", "#41 Done  ·  3.2 s ago"), new("refused", "#42 NotOwner  ·  2.1 s ago"), new("done", "#43 Done  ·  0.4 s ago"),
    };

    public IReadOnlyList<DrawToggle> Draws => new[]
    {
        new DrawToggle("hitboxes", "HITBOXES", "The capsules the shots and arrows test, on every character and animal in view (GROUND.md §5.6).", _draws["hitboxes"], false),
        new DrawToggle("tiles", "GROUND TILES", "The collision mesh under your feet and around it, as wireframe: the ground the server walks you on (§4.5).", _draws["tiles"], false),
        new DrawToggle("relevance", "RELEVANCE", "The circle of the relevance radius around you: what is inside it is sent to you (§4.4).", _draws["relevance"], false),
        new DrawToggle("nav", "NAV GRID", "The animals' cells around you: green walkable, orange too steep, red blocked by a trunk or a rock, blue under water, grey not built yet (§6.9).", _draws["nav"], true),
        new DrawToggle("areas", "SPAWN AREAS", "The animals' spawn areas near you: each slot's home and range, its species and how many live (§6.9).", _draws["areas"], true),
    };

    public void ToggleDraw(string key)
    {
        if (_draws.ContainsKey(key))
        {
            _draws[key] = !_draws[key];
        }
    }

    public IReadOnlyList<string> Log => _log;

    public int LogRevision => _log.Count;
}
