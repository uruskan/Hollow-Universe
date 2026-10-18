using System;
using System.Collections.Generic;
using System.Linq;

namespace HollowUniverse.UI.Model;

/// <summary>
/// The F1 panel on sample data (GROUND.md §8.2), for the interface viewer (<c>--view ui --screen console</c>, and
/// <c>console-info</c>, <c>console-net</c>, <c>console-items</c>, <c>console-tools</c>): a log with every kind of line,
/// a history, simple completion of command names, and the tabs' reads. Lines typed are echoed with a made-up answer.
/// </summary>
public sealed class MockConsole : IConsoleModel
{
    private static readonly string[] Commands =
    {
        "help", "find", "clear", "quit", "fps", "netgraph", "fov", "sensitivity", "kill", "players", "whereami", "give", "kit",
        "learn", "teleport", "tp2me", "tpmarker", "spawn", "heal", "god", "noclip", "time", "timescale", "status", "kick", "say",
        "save", "ents", "freebuild", "bgrade", "thirdperson", "tps", "selfcamera",
    };

    private readonly MockGroundUi _ui;
    private readonly List<ConsoleLine> _lines = new();
    private readonly List<string> _history = new() { "give rifle", "spawn wolf 2", "noclip" };
    private readonly List<ConsoleLine> _answer = new();
    private int _historyAt = -1;
    private readonly float[] _trip = new float[120], _down = new float[120], _error = new float[120];

    public MockConsole(MockGroundUi ui)
    {
        _ui = ui;
        Add("Hollow Universe console. Type help for the commands; Tab completes, Up and Down go through what you typed.", ConsoleLineKind.System);
        Add("give rifle", ConsoleLineKind.Input);
        Add("Gave you 1 Frontier rifle.", ConsoleLineKind.Success);
        Add("spawn wolf 2", ConsoleLineKind.Input);
        Add("2 wolves 22 m in front of you.", ConsoleLineKind.Success);
        Add("teleport Bo", ConsoleLineKind.Input);
        Add("No player called 'Bo' is on.", ConsoleLineKind.Error);
        Add("help", ConsoleLineKind.Input);
        Add("EVERYONE", ConsoleLineKind.Success);
        Add("kill\tYou die where you stand: your body keeps what you carried and the respawn screen opens.", ConsoleLineKind.Output);
        Add("players\tWho is on the server.", ConsoleLineKind.Output);
        Add("ADMIN", ConsoleLineKind.Success);
        Add("give <item> [amount] [player]\tPuts items into an inventory (item keys as in items.json; the rest drops at the feet).", ConsoleLineKind.Output);
        Add("noclip [on|off] [player]\tFlies through everything: W A S D along the view, Space up, Ctrl down, Shift fast.", ConsoleLineKind.Output);
        Add("status", ConsoleLineKind.Input);
        Add("tick 48211 at 30 Hz: 4.12 ms average, 9.80 ms most (last 10 s); 2 players, 64 ground tiles", ConsoleLineKind.Server);
        for (int i = 0; i < _trip.Length; i++)
        {
            _trip[i] = 24 + 6 * MathF.Sin(i * 0.3f) + (i % 17 == 0 ? 20 : 0);
            _down[i] = 60 + 30 * MathF.Sin(i * 0.11f + 1);
            _error[i] = i % 23 == 0 ? 0.004f : 0;
        }
        _answer.Add(new ConsoleLine("ents", ConsoleLineKind.Input));
        _answer.Add(new ConsoleLine("Players: 2 on; 3 characters in the zone (1 asleep, 0 dead, 0 wounded).", ConsoleLineKind.Server));
        _answer.Add(new ConsoleLine("Animals: 14 alive (5 deer, 3 boar, 4 wolf, 2 bear), 1 corpses, 9 awake.", ConsoleLineKind.Server));
    }

    public IReadOnlyList<ConsoleLine> Lines => _lines;

    public int Revision { get; private set; }

    public bool Admin { get; set; } = true;

    public IReadOnlyList<ConsoleLine> LastAnswer => _answer;

    private void Add(string text, ConsoleLineKind kind)
    {
        _lines.Add(new ConsoleLine(text, kind));
        Revision++;
    }

    public void Submit(string line)
    {
        _history.Add(line);
        _historyAt = -1;
        Run(line);
    }

    public void Run(string line)
    {
        Add(line, ConsoleLineKind.Input);
        _answer.Clear();
        _answer.Add(new ConsoleLine(line, ConsoleLineKind.Input));
        string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string word = words.FirstOrDefault()?.ToLowerInvariant() ?? "";
        var answer = Commands.Contains(word)
            ? new ConsoleLine($"(The viewer has no server: '{line}' would run there.)", ConsoleLineKind.Server)
            : new ConsoleLine($"Unknown command '{word}' (help lists them, find searches them).", ConsoleLineKind.Error);
        if (word is "freebuild" or "bgrade")
        {
            answer = Admin ? BuildingTool(word, words.Skip(1).ToArray())
                : new ConsoleLine("This command needs admin rights.", ConsoleLineKind.Error);
        }
        else if (word is "thirdperson" or "tps" or "selfcamera")
        {
            answer = Admin ? CameraTool(words.Skip(1).ToArray())
                : new ConsoleLine("This command needs admin rights.", ConsoleLineKind.Error);
        }
        Add(answer.Text, answer.Kind);
        _answer.Add(answer);
    }

    private ConsoleLine CameraTool(string[] args)
    {
        if (args.Length > 1 || args.Length == 1 && !new[] { "on", "off" }.Contains(args[0], StringComparer.OrdinalIgnoreCase))
        {
            return new ConsoleLine("Usage: thirdperson [on|off]", ConsoleLineKind.Error);
        }
        bool enabled = args.Length == 0 ? !Tools.ThirdPerson : string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase);
        Tools = Tools with { ThirdPerson = enabled };
        return new ConsoleLine($"Third person {(enabled ? "on" : "off")} (viewer sample).", ConsoleLineKind.Success);
    }

    private ConsoleLine BuildingTool(string command, string[] args)
    {
        if (command == "freebuild")
        {
            if (args.Length > 1 || args.Length == 1 && !new[] { "on", "off" }.Contains(args[0], StringComparer.OrdinalIgnoreCase))
            {
                return new ConsoleLine("Usage: freebuild [on|off]", ConsoleLineKind.Error);
            }
            bool enabled = args.Length == 0 ? !Tools.FreeBuild : string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase);
            Tools = Tools with { FreeBuild = enabled };
            _ui.FreeCrafting = enabled;
            return new ConsoleLine($"Free build {(enabled ? "on" : "off")} (viewer sample).", ConsoleLineKind.Success);
        }
        if (args.Length == 1)
        {
            int grade = Tools.BuildGrades.ToList().FindIndex(g => string.Equals(g.Command, "bgrade " + args[0], StringComparison.OrdinalIgnoreCase));
            if (grade >= 0 || string.Equals(args[0], "off", StringComparison.OrdinalIgnoreCase))
            {
                Tools = Tools with { BuildGrade = grade };
                return new ConsoleLine($"BGrade {(grade < 0 ? "off" : Tools.BuildGrades[grade].Label)} (viewer sample).", ConsoleLineKind.Success);
            }
        }
        return new ConsoleLine("Usage: bgrade off|<grade>", ConsoleLineKind.Error);
    }

    public string? History(int step, string typed)
    {
        if (_historyAt < 0)
        {
            if (step > 0)
            {
                return null;
            }
            _historyAt = _history.Count - 1;
            return _history[_historyAt];
        }
        _historyAt = Math.Clamp(_historyAt + step, 0, _history.Count);
        if (_historyAt == _history.Count)
        {
            _historyAt = -1;
            return "";
        }
        return _history[_historyAt];
    }

    public ConsoleCompletionView Complete(string line)
    {
        string[] matches = Commands.Where(c => c.StartsWith(line.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? new ConsoleCompletionView(matches[0] + " ", Array.Empty<string>()) : new ConsoleCompletionView(line, matches);
    }

    public IReadOnlyList<InfoSection> Info { get; } = new[]
    {
        new InfoSection("GAME", new InfoRow[]
        {
            new("Game", "Hollow Universe, the ground game"), new("Version", "development"), new("Build", "2026-10-13 14:02"),
            new("Protocol", "11"), new("Engine", "Godot 4.7.2, .NET 10"),
        }),
        new InfoSection("THIS COMPUTER", new InfoRow[]
        {
            new("System", "Windows 10"), new("Processor", "A sample CPU (16 threads)"), new("Graphics", "A sample GPU"), new("Memory", "16 GB"),
        }),
        new InfoSection("WORLD", new InfoRow[]
        {
            new("Planet", "Nolcas Peris CQ-SLY 7-0 B 3"), new("Landing site", "44.8683° N, 170.0010° W"), new("Local time", "10:12"),
            new("Day", "×24 real (87 minutes a day)"),
        }),
        new InfoSection("SERVER", new InfoRow[] { new("Name", "Survivor's game"), new("Players", "2 of 128"), new("Your rights", "admin") }),
    };

    public IReadOnlyList<InfoSection> Net { get; } = new[]
    {
        new InfoSection("CONNECTION", new InfoRow[]
        {
            new("Ping", "24 ms"), new("Packet loss", "0.0 % of snapshots"), new("Bandwidth in", "61.2 kbit/s"), new("Bandwidth out", "28.9 kbit/s"),
            new("Snapshots", "20.0 a second"), new("Prediction error", "0.0 mm average, 0.0 mm most"), new("Input margin", "2 ticks early"),
        }),
    };

    public NetGraphView NetGraph => new(_trip, _down, _error, 0);

    public IReadOnlyList<string> ItemKeys => _ui.SampleItemKeys;

    public void Give(string key, int amount) => Run($"give {key} {amount}");

    public ToolsView Tools { get; private set; } = new(true, false, 10.2, 1, false, new[] { "Bo", "Cy" }, new[]
    {
        new ToolGroup("ANIMALS", new ToolButton[] { new("DEER", "spawn deer 2"), new("BOAR", "spawn boar 2"), new("WOLF", "spawn wolf 2"), new("BEAR", "spawn bear 1") }),
        new ToolGroup("SHIPS", new ToolButton[] { new("TIN FINCH", "spawn tin-finch") }),
        new ToolGroup("LOOT", new ToolButton[] { new("BARREL", "spawn barrel"), new("FUEL BARREL", "spawn fuel-barrel"), new("CRATE", "spawn crate"), new("MILITARY CRATE", "spawn military-crate"), new("RECYCLER", "spawn recycler") }),
        new ToolGroup("DEPLOYABLES", new ToolButton[] { new("SLEEPING BAG", "spawn sleeping-bag"), new("CAMPFIRE", "spawn campfire"), new("FURNACE", "spawn furnace"), new("SMALL BOX", "spawn small-box") }),
        new ToolGroup("RAID BASE (A CLAIMED HUT)", new ToolButton[] { new("TWIG", "spawn raidbase 0"), new("WOOD", "spawn raidbase 1"), new("STONE", "spawn raidbase 2"), new("METAL", "spawn raidbase 3"), new("ARMOURED", "spawn raidbase 4") }),
    })
    {
        FreeBuild = true,
        BuildGrades = new ToolButton[]
        {
            new("TWIG", "bgrade twig"), new("WOOD", "bgrade wood"), new("STONE", "bgrade stone"),
            new("METAL", "bgrade metal"), new("ARMOURED", "bgrade armoured"),
        },
    };
}
