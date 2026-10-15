using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Core;
using Hollow.Net.Commands;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
using Hollow.Surface;
using HollowUniverse.App;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;

namespace HollowUniverse.Ground;

/// <summary>The view's settings the console sets (GROUND.md §8.2), kept on this computer: field of view, mouse sensitivity, the frame rate shown.</summary>
public sealed class ViewSettings
{
    private const string FilePath = "user://ground-settings.cfg";

    /// <summary>Vertical field of view in degrees (60–100; 75 by default).</summary>
    public float Fov { get; set; } = 75;

    /// <summary>Mouse look sensitivity (0.1–5; 1 by default: 0.002 radians a mouse count).</summary>
    public float Sensitivity { get; set; } = 1;

    public bool ShowFps { get; set; }

    public static ViewSettings Load()
    {
        var file = new ConfigFile();
        file.Load(FilePath);
        return new ViewSettings
        {
            Fov = Math.Clamp((float)file.GetValue("view", "fov", 75.0).AsDouble(), 60, 100),
            Sensitivity = Math.Clamp((float)file.GetValue("view", "sensitivity", 1.0).AsDouble(), 0.1f, 5),
            ShowFps = file.GetValue("view", "fps", false).AsBool(),
        };
    }

    public void Save()
    {
        var file = new ConfigFile();
        file.Load(FilePath);
        file.SetValue("view", "fov", Fov);
        file.SetValue("view", "sensitivity", Sensitivity);
        file.SetValue("view", "fps", ShowFps);
        file.Save(FilePath);
    }
}

/// <summary>
/// The F1 panel's model over the client (GROUND.md §8.2): the console's log, history (kept in
/// <c>user://console-history.txt</c>) and Tab completion; the game's own commands (help, find, clear, quit, fps,
/// netgraph, fov, sensitivity) run here, everything else goes to the server, which checks the player's rights and
/// answers line by line; the INFO, NET, ITEMS and TOOLS tabs' reads.
/// </summary>
public sealed class ClientConsole : IConsoleModel, IDisposable
{
    private const int MaxLines = 600;
    private const int MaxHistory = 100;
    private const string HistoryPath = "user://console-history.txt";

    private readonly GameClient _client;
    private readonly List<ConsoleLine> _lines = new();
    private readonly List<string> _history = new();
    private readonly HashSet<uint> _toast = new();
    private readonly List<ConsoleLine> _lastAnswer = new();
    private int _historyAt = -1;
    private string _typed = "";
    private uint _lastSeq;
    private CompletionSources? _sources;
    private IReadOnlyList<(string Name, bool Admin)>? _sourcesPlayers;
    private SkyEphemeris? _sky;
    private ZoneDay? _skyDay;
    private IReadOnlyList<ToolGroup>? _spawns;

    public ClientConsole(GameClient client, ViewSettings settings)
    {
        _client = client;
        Settings = settings;
        _client.Console().ConsoleAnswered += OnAnswer;
        LoadHistory();
        Add("Hollow Universe console. Type help for the commands; Tab completes, Up and Down go through what you typed.", ConsoleLineKind.System);
    }

    public void Dispose() => _client.Console().ConsoleAnswered -= OnAnswer;

    public ViewSettings Settings { get; }

    /// <summary>Shows or hides the network graph (F3); returns whether it shows now.</summary>
    public Func<bool>? ToggleNetGraph { get; set; }

    /// <summary>Leaves the game.</summary>
    public Action? Quit { get; set; }

    /// <summary>A short message on the HUD (a chat line's command answers there too).</summary>
    public Action<string>? Notice { get; set; }

    /// <summary>The local solar time where the player stands, as the sky shows it (null before the world opens).</summary>
    public Func<double?>? LocalHours { get; set; }

    /// <summary>Where the game connected (the INFO tab).</summary>
    public string Address { get; set; } = "";

    // ---------------------------------------------------------------- the log

    public IReadOnlyList<ConsoleLine> Lines => _lines;

    public int Revision { get; private set; }

    public bool Admin => _client.Console().IsAdmin;

    public IReadOnlyList<ConsoleLine> LastAnswer => _lastAnswer;

    private void Add(string text, ConsoleLineKind kind)
    {
        foreach (string line in text.Split('\n'))
        {
            _lines.Add(new ConsoleLine(line.TrimEnd('\r'), kind));
        }
        if (_lines.Count > MaxLines)
        {
            _lines.RemoveRange(0, _lines.Count - MaxLines);
        }
        Revision++;
    }

    private void Out(string text) => Add(text, ConsoleLineKind.Output);

    private void Fail(string text)
    {
        Add(text, ConsoleLineKind.Error);
        _lastAnswer.Add(new ConsoleLine(text, ConsoleLineKind.Error));
    }

    // ---------------------------------------------------------------- running lines

    public void Submit(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
        {
            return;
        }
        _historyAt = -1;
        if (_history.Count == 0 || _history[^1] != line)
        {
            _history.Add(line);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(0, _history.Count - MaxHistory);
            }
            SaveHistory();
        }
        Execute(line, toast: false);
    }

    public void Run(string line) => Execute(line.Trim(), toast: false);

    /// <summary>A command typed in chat after a slash: run as in the console, its answer's first line on the HUD.</summary>
    public void SubmitFromChat(string line) => Execute(line.Trim().TrimStart('/'), toast: true);

    public void Give(string key, int amount) => Run(string.Create(CultureInfo.InvariantCulture, $"give {key} {amount}"));

    private void Execute(string line, bool toast)
    {
        if (line.Length == 0)
        {
            return;
        }
        Add(line, ConsoleLineKind.Input);
        _lastAnswer.Clear();
        _lastAnswer.Add(new ConsoleLine(line, ConsoleLineKind.Input));
        (ConsoleCommandDef? command, string typed, IReadOnlyList<string> args) = ConsoleCatalog.Parse(line);
        if (command == null)
        {
            Fail($"Unknown command '{typed}' (help lists them, find searches them).");
            if (toast)
            {
                Notice?.Invoke($"Unknown command '{typed}' (F1: help).");
            }
            return;
        }
        if (command.Side == CommandSide.Client)
        {
            RunHere(command, args);
            return;
        }
        uint seq = _client.Console().SendConsole(line);
        if (seq == 0)
        {
            Fail("Not connected to a server.");
            return;
        }
        _lastSeq = seq;
        if (toast)
        {
            _toast.Add(seq);
        }
    }

    private void OnAnswer(ConsoleAnswer answer)
    {
        ConsoleLineKind kind = answer.Kind switch
        {
            ConsoleReplyKind.Error => ConsoleLineKind.Error,
            ConsoleReplyKind.Success => ConsoleLineKind.Success,
            _ => ConsoleLineKind.Server,
        };
        Add(answer.Text, kind);
        if (answer.Seq == _lastSeq && answer.Seq != 0)
        {
            _lastAnswer.Add(new ConsoleLine(answer.Text, kind));
        }
        if (_toast.Remove(answer.Seq))
        {
            Notice?.Invoke(answer.Text);
        }
    }

    // The game's own commands.
    private void RunHere(ConsoleCommandDef command, IReadOnlyList<string> args)
    {
        switch (command.Name)
        {
            case "help":
                Help(args.Count > 0 ? args[0] : null);
                break;
            case "find":
                Find(string.Join(' ', args));
                break;
            case "clear":
                _lines.Clear();
                Revision++;
                break;
            case "quit":
                Out("Leaving the game.");
                Quit?.Invoke();
                break;
            case "fps":
                Settings.ShowFps = !Settings.ShowFps;
                Settings.Save();
                Out(Settings.ShowFps ? "The frame rate shows at the top left." : "The frame rate is hidden.");
                break;
            case "netgraph":
                bool shown = ToggleNetGraph?.Invoke() ?? false;
                Out(shown ? "The network graph shows at the top right (F3 hides it)." : "The network graph is hidden.");
                break;
            case "fov":
                Setting(args, "Field of view", Settings.Fov, 60, 100, "°", v => Settings.Fov = v);
                break;
            case "sensitivity":
                Setting(args, "Mouse sensitivity", Settings.Sensitivity, 0.1f, 5, "", v => Settings.Sensitivity = v);
                break;
        }
    }

    private void Setting(IReadOnlyList<string> args, string name, float now, float least, float most, string unit, Action<float> set)
    {
        if (args.Count == 0)
        {
            Out(string.Create(CultureInfo.InvariantCulture, $"{name}: {now:0.##}{unit} ({least:0.#} to {most:0.#}{unit})."));
            return;
        }
        if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
        {
            Fail($"{name} is a number, {least.ToString(CultureInfo.InvariantCulture)} to {most.ToString(CultureInfo.InvariantCulture)}.");
            return;
        }
        value = Math.Clamp(value, least, most);
        set(value);
        Settings.Save();
        Add(string.Create(CultureInfo.InvariantCulture, $"{name} {value:0.##}{unit}, kept on this computer."), ConsoleLineKind.Success);
    }

    private void Help(string? about)
    {
        if (about != null)
        {
            if (ConsoleCatalog.Find(about.TrimStart('/')) is not { } c)
            {
                Fail($"No command called '{about}' (find {about} searches).");
                return;
            }
            foreach (string usage in c.Usage)
            {
                Add(usage, ConsoleLineKind.Output);
            }
            Out("\t" + c.Help);
            Out("\t" + (c.Admin ? "Admins only" : "Everyone") + (c.Side == CommandSide.Client ? "; runs in the game." : "; runs on the server."));
            if (c.Aliases.Length > 0)
            {
                Out("\tAlso: " + string.Join(", ", c.Aliases));
            }
            return;
        }
        Add("EVERYONE", ConsoleLineKind.Success);
        foreach (ConsoleCommandDef c in ConsoleCatalog.All.Where(c => !c.Admin))
        {
            Out($"{c.Usage[0]}\t{c.Help}");
        }
        if (Admin)
        {
            Add("ADMIN", ConsoleLineKind.Success);
            foreach (ConsoleCommandDef c in ConsoleCatalog.All.Where(c => c.Admin))
            {
                Out($"{c.Usage[0]}\t{c.Help}");
            }
        }
        else
        {
            Add("ADMIN (not you, on this server)", ConsoleLineKind.Success);
            Out(string.Join(", ", ConsoleCatalog.All.Where(c => c.Admin).Select(c => c.Name)));
        }
        Out("help <command> tells more; Rust's names work too (inventory.give, global.teleport, env.time, global.god, global.noclip, entity.spawn).");
    }

    private void Find(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            Fail("Usage: find <text>");
            return;
        }
        bool Has(string s) => s.Contains(text, StringComparison.OrdinalIgnoreCase);
        GameContent c = _client.Content;
        var found = new List<string>();
        found.AddRange(ConsoleCatalog.All.Where(x => Has(x.Name) || x.Aliases.Any(Has) || Has(x.Help)).Select(x => $"{x.Usage[0]}\tcommand: {x.Help}"));
        found.AddRange(c.Items.Where(i => Has(i.Key) || Has(i.Name)).Select(i => $"{i.Key}\titem: {i.Name}"));
        found.AddRange(c.LootTables.Where(t => Has(t.Key)).Select(t => $"{t.Key}\tloot table (kit {t.Key})"));
        found.AddRange(Spawning.Kinds(c).Where(Has).Select(k => $"{k}\tto spawn (spawn {k})"));
        if (found.Count == 0)
        {
            Fail($"Nothing called '{text}'.");
            return;
        }
        foreach (string line in found.Take(60))
        {
            Out(line);
        }
        if (found.Count > 60)
        {
            Out($"and {found.Count - 60} more: say more.");
        }
    }

    // ---------------------------------------------------------------- history and completion

    public string? History(int step, string typed)
    {
        if (_history.Count == 0)
        {
            return null;
        }
        if (_historyAt < 0)
        {
            if (step > 0)
            {
                return null;
            }
            _typed = typed;
            _historyAt = _history.Count - 1;
            return _history[_historyAt];
        }
        int next = _historyAt + step;
        if (next < 0)
        {
            return null;
        }
        if (next >= _history.Count)
        {
            _historyAt = -1;
            return _typed;
        }
        _historyAt = next;
        return _history[next];
    }

    public ConsoleCompletionView Complete(string line)
    {
        if (_sources == null || !ReferenceEquals(_sourcesPlayers, _client.Console().ServerPlayers))
        {
            _sourcesPlayers = _client.Console().ServerPlayers;
            _sources = CompletionSources.From(_client.Content, _client.Console().ServerPlayers.Select(p => p.Name));
        }
        Completion done = ConsoleCompletion.Complete(line, _sources);
        return new ConsoleCompletionView(done.Line, done.Candidates);
    }

    private void LoadHistory()
    {
        if (!FileAccess.FileExists(HistoryPath))
        {
            return;
        }
        using FileAccess? file = FileAccess.Open(HistoryPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return;
        }
        foreach (string line in file.GetAsText().Split('\n'))
        {
            if (line.Trim().Length > 0)
            {
                _history.Add(line.Trim());
            }
        }
        if (_history.Count > MaxHistory)
        {
            _history.RemoveRange(0, _history.Count - MaxHistory);
        }
    }

    private void SaveHistory()
    {
        using FileAccess? file = FileAccess.Open(HistoryPath, FileAccess.ModeFlags.Write);
        file?.StoreString(string.Join('\n', _history) + "\n");
    }

    // ---------------------------------------------------------------- INFO

    public IReadOnlyList<InfoSection> Info
    {
        get
        {
            GameContent c = _client.Content;
            var game = new List<InfoRow>
            {
                new("Game", "Hollow Universe, the ground game"),
                new("Version", (string)ProjectSettings.GetSetting("application/config/version", "development") is { Length: > 0 } v ? v : "development"),
                new("Build", Build),
                new("Protocol", string.Create(CultureInfo.InvariantCulture, $"{Protocol.Version} (schema {Protocol.SchemaHash:X16})")),
                new("Game data", c.Hash.ToString("X16", CultureInfo.InvariantCulture)),
                new("Engine", $"Godot {Engine.GetVersionInfo()["string"]}, .NET {System.Environment.Version}"),
            };
            Godot.Collections.Dictionary memory = OS.GetMemoryInfo();
            double ram = memory.ContainsKey("physical") ? memory["physical"].AsInt64() / 1073741824.0 : 0;
            Vector2I window = DisplayServer.WindowGetSize();
            var machine = new List<InfoRow>
            {
                new("System", $"{OS.GetName()} {OS.GetVersion()}"),
                new("Processor", string.Create(CultureInfo.InvariantCulture, $"{OS.GetProcessorName()} ({OS.GetProcessorCount()} threads)")),
                new("Graphics", $"{RenderingServer.GetVideoAdapterName()}, {RenderingServer.GetCurrentRenderingDriverName()} {RenderingServer.GetVideoAdapterApiVersion()}"),
                new("Memory", ram > 0 ? string.Create(CultureInfo.InvariantCulture, $"{ram:0.#} GB") : "unknown"),
                new("Window", string.Create(CultureInfo.InvariantCulture, $"{window.X} × {window.Y}, interface ×{Ui.Scale:0.##}")),
                new("Frame rate", string.Create(CultureInfo.InvariantCulture, $"{Engine.GetFramesPerSecond():0} fps")),
            };
            var world = new List<InfoRow>();
            if (_client.Zone is { } zone)
            {
                PlanetSurfaces.Entry? planet = PlanetSurfaces.Find(zone.SystemAddress, zone.BodyPath);
                world.Add(new InfoRow("Planet", planet != null ? $"{planet.Body.Name}" : "unknown body"));
                world.Add(new InfoRow("System", planet != null
                    ? string.Create(CultureInfo.InvariantCulture, $"{planet.System.Name} ({zone.SystemAddress:X16})")
                    : zone.SystemAddress.ToString("X16", CultureInfo.InvariantCulture)));
                world.Add(new InfoRow("Body", string.Create(CultureInfo.InvariantCulture, $"{zone.BodyPath:X8}, radius {zone.Radius / 1000:0} km, {zone.SurfaceGravity:0.00} g")));
                // The zone holds the whole world (docs/WHOLE_WORLD.md); its landing site is this server's (--lat, --lon).
                world.Add(new InfoRow("Landing site", Degrees(zone.LatitudeDegrees, zone.LongitudeDegrees) + (zone.StandIn ? ", stand-in ground" : "")));
                world.Add(new InfoRow("Local time", LocalHours?.Invoke() is { } hours ? Clock(hours) + " (local solar time here)" : "not yet"));
                ZoneDay? day = _client.Console().Day;
                double scale = day?.Scale ?? zone.RotationTimeScale;
                double period = planet != null ? Math.Abs(planet.Body.RotationPeriod) : 0;
                world.Add(new InfoRow("Day", scale <= 0 ? "stopped by an admin"
                    : string.Create(CultureInfo.InvariantCulture, $"×{scale:0.##} real ({(period > 0 ? Minutes(period / scale) : "?")} a day)") +
                      (day is { Overridden: true } ? ", set by an admin" : "")));
                world.Add(new InfoRow("Universe time", UniverseTime.ToUtc(UniverseTime.Now).ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)));
            }
            var server = new List<InfoRow>
            {
                new("Name", _client.ServerName.Length > 0 ? _client.ServerName : "not connected"),
                new("Address", Address.Length > 0 ? Address : "unknown"),
                new("Players", string.Create(CultureInfo.InvariantCulture, $"{_client.Console().ServerPlayers.Count} of {_client.Console().MaxPlayers}")),
                new("Tick rate", string.Create(CultureInfo.InvariantCulture, $"{GameClient.TickRate} Hz, {Protocol.SnapshotRate} snapshots a second")),
                new("Your rights", _client.Console().IsAdmin ? "admin" : "player"),
            };
            return new[] { new InfoSection("GAME", game), new InfoSection("THIS COMPUTER", machine), new InfoSection("WORLD", world), new InfoSection("SERVER", server) };
        }
    }

    // The commit and branch the game was built from (read from the repository beside game/, for bug reports), and when
    // its C# was built.
    private static string Build => _build ??= FindBuild();

    private static string? _build;

    private static string FindBuild()
    {
        string commit = Checkout.Branch is { } branch ? $"{Checkout.Commit ?? "?"} on {branch}" : Checkout.Commit ?? "";
        string built = "";
        string bin = ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin");
        if (System.IO.Directory.Exists(bin))
        {
            DateTime newest = System.IO.Directory.EnumerateFiles(bin, "HollowUniverse.dll", System.IO.SearchOption.AllDirectories)
                .Select(System.IO.File.GetLastWriteTime).DefaultIfEmpty(DateTime.MinValue).Max();
            if (newest > DateTime.MinValue)
            {
                built = "built " + newest.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
        }
        string text = string.Join(", ", new[] { commit, built }.Where(t => t.Length > 0));
        return text.Length > 0 ? text : "unknown";
    }

    private static string Degrees(double latitude, double longitude) => string.Create(CultureInfo.InvariantCulture,
        $"{Math.Abs(latitude):0.0000}° {(latitude >= 0 ? 'N' : 'S')}, {Math.Abs(longitude):0.0000}° {(longitude >= 0 ? 'E' : 'W')}");

    private static string Clock(double hours)
    {
        int minutes = (int)Math.Round(hours * 60) % (24 * 60);
        return string.Create(CultureInfo.InvariantCulture, $"{minutes / 60:00}:{minutes % 60:00}");
    }

    private static string Minutes(double seconds) =>
        seconds >= 7200 ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:0.#} hours") : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:0} minutes");

    // ---------------------------------------------------------------- NET

    public IReadOnlyList<InfoSection> Net
    {
        get
        {
            ClientStats s = _client.Stats;
            var rows = new List<InfoRow>
            {
                new("Ping", $"{s.RoundTripMs} ms"),
                new("Packet loss", string.Create(CultureInfo.InvariantCulture, $"{s.SnapshotLossPercent:0.0} % of snapshots")),
                new("Bandwidth in", string.Create(CultureInfo.InvariantCulture, $"{s.KilobitsIn:0.0} kbit/s")),
                new("Bandwidth out", string.Create(CultureInfo.InvariantCulture, $"{s.KilobitsOut:0.0} kbit/s")),
                new("Snapshots", string.Create(CultureInfo.InvariantCulture, $"{s.SnapshotsPerSecond:0.0} a second")),
                new("Prediction error", string.Create(CultureInfo.InvariantCulture, $"{s.PredictionErrorAverage * 1000:0.0} mm average, {s.PredictionErrorMax * 1000:0.0} mm most")),
                new("Input margin", $"{s.InputMargin} ticks early"),
                new("Clock rate", string.Create(CultureInfo.InvariantCulture, $"×{s.ClockRate:0.00}")),
                new("Corrections", string.Create(CultureInfo.InvariantCulture, $"{s.Corrections} of {s.Reconciliations}, {s.Teleports} teleports")),
                new("Clock jumps", s.ClockJumps.ToString(CultureInfo.InvariantCulture)),
                new("Ship corrections", s.ShipCorrections.ToString(CultureInfo.InvariantCulture)),
                new("In view", string.Create(CultureInfo.InvariantCulture, $"{_client.EntityCount} players, {_client.Animals().All.Count} animals, {_client.Survival().Things.Count()} things")),
            };
            return new[] { new InfoSection("CONNECTION", rows) };
        }
    }

    public NetGraphView NetGraph => new(_client.Stats.RoundTripHistory, _client.Stats.KilobitsInHistory, _client.Stats.PredictionErrorHistory, _client.Stats.HistoryStart);

    // ---------------------------------------------------------------- ITEMS and TOOLS

    public IReadOnlyList<string> ItemKeys => _itemKeys ??= _client.Content.Items.Select(i => i.Key).ToArray();

    private string[]? _itemKeys;

    public ToolsView Tools
    {
        get
        {
            MoveCheats cheats = _client.HasCharacter ? _client.Console().Cheats : MoveCheats.None;
            ZoneDay? day = _client.Console().Day;
            double factor = day is { } d && _client.Zone is { RotationTimeScale: > 0 } z ? d.Scale / z.RotationTimeScale : 1;
            return new ToolsView((cheats & MoveCheats.God) != 0, (cheats & MoveCheats.Noclip) != 0, ZoneHours() ?? 12, factor, day?.Overridden ?? false,
                _client.Console().ServerPlayers.Select(p => p.Name).Where(n => !string.Equals(n, _client.Name, StringComparison.OrdinalIgnoreCase)).ToArray(),
                _spawns ??= SpawnGroups(_client.Content));
        }
    }

    // The local solar time at the landing site by the zone's clock (as the server keeps it).
    private double? ZoneHours()
    {
        if (_client.Zone is not { } zone || _client.Console().Day is not { } day)
        {
            return null;
        }
        _sky ??= zone.CreateSky();
        if (_sky == null)
        {
            return null;
        }
        if (_skyDay != day)
        {
            _skyDay = day;
            _sky.SetSpin(day.BaseTime, day.BaseTurns, day.Scale);
        }
        return _sky.LocalSolarHours(zone.LandingDirection, _sky.Sun.Index, UniverseTime.Now);
    }

    private static IReadOnlyList<ToolGroup> SpawnGroups(GameContent c)
    {
        string Label(string name) => name.ToUpperInvariant();
        var groups = new List<ToolGroup>
        {
            new("ANIMALS", c.Animals.Select(a => new ToolButton(Label(a.Name), $"spawn {a.Key} {(a.Key is "bear" ? 1 : 2)}")).ToArray()),
            new("SHIPS", c.Ships.Select(s => new ToolButton(Label(s.Name), $"spawn {s.Key}")).ToArray()),
            new("LOOT", Spawning.PropNames.Select(n => (n.Name, Prop: c.World.Prop(n.Prop)))
                .Where(p => p.Prop != null).Select(p => new ToolButton(Label(p.Prop!.Name), $"spawn {p.Name}"))
                .Concat(c.World.Props.Where(p => !Spawning.PropNames.Any(n => n.Prop == p.Key)).Select(p => new ToolButton(Label(p.Name), $"spawn {p.Key}")))
                .ToArray()),
            new("DEPLOYABLES", c.Deployables.Select(d => new ToolButton(Label(d.Item.Name.Replace(", level ", " ", StringComparison.OrdinalIgnoreCase)), $"spawn {d.Key}")).ToArray()),
            new("RAID BASE (A CLAIMED HUT)", CompletionSources.Grades.Select((g, i) => new ToolButton(Label(g), $"spawn raidbase {i}")).ToArray()),
        };
        return groups;
    }
}
