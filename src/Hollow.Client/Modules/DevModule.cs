using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Targets;

namespace Hollow.Client;

/// <summary>A player's state as the F2 panel lists it.</summary>
public enum DevPlayerState : byte
{
    LoggingIn,
    Alive,
    Wounded,
    Dead,
}

/// <summary>One section of the tick's profile as the server last sent it, with its depth in the profile's tree.</summary>
public readonly record struct ServerSection(ProfileSection Section, int Depth, float AverageMs, float WorstMs);

/// <summary>A player on the server as the F2 panel lists them.</summary>
public readonly record struct ServerPlayer(string Name, bool Admin, DevPlayerState State, int PingMs, Vector3 Position, int OnTimePercent, int InView,
    float KilobitsDown, float KilobitsUp);

/// <summary>A fact the server told of a target, or a line of its log.</summary>
public readonly record struct DevLine(string Label, string Value);

/// <summary>The animals' nav grid around the player as last sent: a square of cells of 2 m from a corner, row by row, each cell's flags (DevCellNet).</summary>
public sealed record NavWindow(System.Numerics.Vector3 Centre, int Cells, byte[] Flags);

/// <summary>A slot of a spawn area: its species, its herd's size, how many live, its home and its range.</summary>
public readonly record struct SpawnSlotView(string Species, int Size, int Living, Vector3 Home, float Range);

/// <summary>A spawn area of the animals near the player.</summary>
public sealed record SpawnAreaView(Vector3 Centre, bool Active, IReadOnlyList<SpawnSlotView> Slots);

/// <summary>An intent answered, and the clock time the answer came.</summary>
public readonly record struct AnsweredIntent(IntentAnswer Answer, double ClockSeconds);

/// <summary>
/// The F2 developer panel's side of the client (GROUND.md §8.5): asks the server to be watched and keeps what it sends
/// (the stats and their history, a target's description, the nav grid window, the spawn areas, the log), the last
/// intents answered, and the DRAW tab's toggles, which the game reads to draw. Engine-free: the panel and the drawing
/// are the Godot project's.
/// </summary>
public sealed class DevModule : ClientModule
{
    /// <summary>Samples kept of the tick: one a half second, a minute.</summary>
    public const int HistoryLength = 120;

    /// <summary>The least time between two questions about a target.</summary>
    public const double InspectSeconds = 0.25;

    public const int LogKept = 400;
    public const int AnswersKept = 8;

    private readonly float[] _average = new float[HistoryLength];
    private readonly float[] _worst = new float[HistoryLength];
    private readonly List<ServerSection> _sections = new();
    private readonly List<ServerPlayer> _players = new();
    private readonly List<DevLine> _lines = new();
    private readonly List<string> _log = new();
    private readonly List<AnsweredIntent> _answers = new();
    private readonly List<SpawnAreaView> _areas = new();
    private int _historyNext, _historyCount;
    private bool _watching, _drawNav, _drawAreas, _wantLog, _watchPending;
    private TargetRef _inspectWanted, _inspectSent;
    private double _inspectAt = -10;

    // ---------------------------------------------------------------- what the server sent

    /// <summary>Whether the panel is open: the server sends its stats while it is (an admin's only).</summary>
    public bool Watching => _watching;

    /// <summary>The server's last stats; null before the first (or when the server does not answer: not an admin).</summary>
    public DevStats? Server { get; private set; }

    /// <summary>The clock time the last stats came; −1 for none.</summary>
    public double StatsClock { get; private set; } = -1;

    /// <summary>The average and the longest tick of each half second, a ring from <see cref="HistoryStart"/>, <see cref="HistoryCount"/> long.</summary>
    public float[] AverageHistory => _average;

    public float[] WorstHistory => _worst;

    public int HistoryStart => _historyCount < HistoryLength ? 0 : _historyNext;

    public int HistoryCount => _historyCount;

    /// <summary>The profile's sections over the last half second, in the tree's order.</summary>
    public IReadOnlyList<ServerSection> Sections => _sections;

    public IReadOnlyList<ServerPlayer> Players => _players;

    /// <summary>What the panel looks at now (the last target given to <see cref="Inspect"/>).</summary>
    public TargetRef Looked => _inspectWanted;

    /// <summary>What <see cref="Lines"/> describe; none before the server answered.</summary>
    public TargetRef Inspected { get; private set; }

    public IReadOnlyList<DevLine> Lines => _lines;

    /// <summary>Rises with every description received (the panel words it once per answer).</summary>
    public int LinesRevision { get; private set; }

    public NavWindow? Nav { get; private set; }

    /// <summary>Rises with every nav window received (the drawing rebuilds on it).</summary>
    public int NavRevision { get; private set; }

    public IReadOnlyList<SpawnAreaView> Areas => _areas;

    public int AreasRevision { get; private set; }

    /// <summary>The server's log, oldest first, as far back as it was sent.</summary>
    public IReadOnlyList<string> Log => _log;

    public int LogRevision { get; private set; }

    /// <summary>The last intents the server answered, oldest first.</summary>
    public IReadOnlyList<AnsweredIntent> Answers => _answers;

    /// <summary>The server's relevance radius (GROUND.md §4.4), as its stats say it; the default before they came.</summary>
    public float RelevanceRadius => Server?.RelevanceRadius ?? 300f;

    // ---------------------------------------------------------------- the DRAW tab's toggles (the game draws them)

    public bool DrawHitboxes { get; set; }

    public bool DrawTiles { get; set; }

    public bool DrawRelevance { get; set; }

    /// <summary>The nav grid around the player, from the server (an admin's).</summary>
    public bool DrawNav
    {
        get => _drawNav;
        set => Set(ref _drawNav, value);
    }

    /// <summary>The spawn areas near the player, from the server (an admin's).</summary>
    public bool DrawAreas
    {
        get => _drawAreas;
        set => Set(ref _drawAreas, value);
    }

    /// <summary>The server's log, streamed (an admin's).</summary>
    public bool WantLog
    {
        get => _wantLog;
        set => Set(ref _wantLog, value);
    }

    /// <summary>The panel opened or closed: the server sends its stats to an open one.</summary>
    public void Watch(bool on) => Set(ref _watching, on);

    /// <summary>The LOOK tab's target: described by the server when it changes (at most four times a second), and again every second.</summary>
    public void Inspect(in TargetRef target) => _inspectWanted = target;

    private void Set(ref bool field, bool value)
    {
        if (field != value)
        {
            field = value;
            _watchPending = true;
        }
    }

    protected internal override void Init(MessageHandlers<GameClient> handlers)
    {
        handlers
            .On<DevStats>((_, in m) => OnStats(m))
            .On<DevInspected>((_, in m) => OnInspected(m))
            .On<DevNav>((_, in m) => OnNav(m))
            .On<DevAreas>((_, in m) => OnAreas(m))
            .On<DevLog>((_, in m) => OnLog(m));
        Local.IntentAnswered += answer =>
        {
            _answers.Add(new AnsweredIntent(answer, ClockSeconds));
            if (_answers.Count > AnswersKept)
            {
                _answers.RemoveAt(0);
            }
        };
    }

    /// <summary>A new zone: the server knows nothing of this watcher yet; what was asked is asked again once playing.</summary>
    protected internal override void Welcome() => _watchPending = _watching || _drawNav || _drawAreas || _wantLog;

    protected internal override void Update(double delta)
    {
        if (_watchPending && SendReliable(new DevWatch { On = _watching, Nav = _drawNav, Areas = _drawAreas, Log = _wantLog }))
        {
            _watchPending = false;
        }
        if (!_watching || _inspectWanted.Family == TargetFamily.None)
        {
            return;
        }
        bool changed = _inspectWanted != _inspectSent;
        double since = ClockSeconds - _inspectAt;
        if (((changed && since >= InspectSeconds) || since >= 1.0) && SendReliable(new DevInspect { Target = NetConvert.ToNet(_inspectWanted) }))
        {
            _inspectSent = _inspectWanted;
            _inspectAt = ClockSeconds;
        }
    }

    public override void Dispose()
    {
        Server = null;
        StatsClock = -1;
        _sections.Clear();
        _players.Clear();
        _lines.Clear();
        Inspected = TargetRef.None;
        LinesRevision++;
        _inspectSent = TargetRef.None;
        _inspectAt = -10;
        Nav = null;
        NavRevision++;
        _areas.Clear();
        AreasRevision++;
    }

    // ---------------------------------------------------------------- the messages

    private void OnStats(in DevStats m)
    {
        Server = m;
        StatsClock = ClockSeconds;
        _average[_historyNext] = Finite(m.AverageMs);
        _worst[_historyNext] = Finite(m.WorstMs);
        _historyNext = (_historyNext + 1) % HistoryLength;
        _historyCount = Math.Min(HistoryLength, _historyCount + 1);
        _sections.Clear();
        foreach (DevSectionNet s in m.Sections ?? Array.Empty<DevSectionNet>())
        {
            int depth = 0;
            foreach ((ProfileSection section, int d) in TickProfile.Tree)
            {
                if (section == s.Section)
                {
                    depth = d;
                    break;
                }
            }
            _sections.Add(new ServerSection(s.Section, depth, Finite(s.AverageMs), Finite(s.WorstMs)));
        }
        _players.Clear();
        foreach (DevPlayerNet p in m.Players ?? Array.Empty<DevPlayerNet>())
        {
            _players.Add(new ServerPlayer(p.Name ?? "", p.Admin, (DevPlayerState)Math.Min(p.State, (byte)3), (int)Math.Min(p.PingMs, int.MaxValue),
                new Vector3(Finite(p.X), Finite(p.Y), Finite(p.Z)), Math.Min(p.OnTimePercent, (byte)100), (int)Math.Min(p.InView, int.MaxValue),
                Finite(p.KilobitsDown), Finite(p.KilobitsUp)));
        }
    }

    private void OnInspected(in DevInspected m)
    {
        Inspected = new TargetRef(m.Target.Family, m.Target.Id, m.Target.Part);
        _lines.Clear();
        foreach (DevLineNet line in m.Lines ?? Array.Empty<DevLineNet>())
        {
            _lines.Add(new DevLine(line.Label ?? "", line.Value ?? ""));
        }
        LinesRevision++;
    }

    private void OnNav(in DevNav m)
    {
        DevCellNet[] cells = m.Flags ?? Array.Empty<DevCellNet>();
        if (m.Cells is < 1 or > 64 || cells.Length != m.Cells * m.Cells || !float.IsFinite(m.CentreX) || !float.IsFinite(m.CentreY) || !float.IsFinite(m.CentreZ))
        {
            return;
        }
        var flags = new byte[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            flags[i] = cells[i].Flags;
        }
        Nav = new NavWindow(new System.Numerics.Vector3(m.CentreX, m.CentreY, m.CentreZ), m.Cells, flags);
        NavRevision++;
    }

    private void OnAreas(in DevAreas m)
    {
        _areas.Clear();
        foreach (DevAreaNet a in m.Areas ?? Array.Empty<DevAreaNet>())
        {
            var slots = new List<SpawnSlotView>();
            foreach (DevSlotNet s in a.Slots ?? Array.Empty<DevSlotNet>())
            {
                slots.Add(new SpawnSlotView(s.Species ?? "", (int)Math.Min(s.Size, int.MaxValue), (int)Math.Min(s.Living, int.MaxValue),
                    new Vector3(Finite(s.HomeX), Finite(s.HomeY), Finite(s.HomeZ)), Finite(s.Range)));
            }
            _areas.Add(new SpawnAreaView(new Vector3(Finite(a.CentreX), Finite(a.CentreY), Finite(a.CentreZ)), a.Active, slots));
        }
        AreasRevision++;
    }

    private void OnLog(in DevLog m)
    {
        foreach (DevLineNet line in m.Lines ?? Array.Empty<DevLineNet>())
        {
            string label = line.Label ?? "", value = line.Value ?? "";
            _log.Add(label.Length > 0 ? label + "  " + value : value);
        }
        if (_log.Count > LogKept)
        {
            _log.RemoveRange(0, _log.Count - LogKept);
        }
        LogRevision++;
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11): the F2 panel's reads and toggles.</summary>
public static class DevModuleAccess
{
    public static DevModule Dev(this GameClient client) => client.Require<DevModule>();
}
