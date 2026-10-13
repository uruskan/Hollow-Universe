using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using Hollow.Client;

namespace HollowUniverse.Ground;

/// <summary>
/// The ground game's start, timed (<c>--load-trace [seconds]</c>, GROUND.md §4.3.1): from the process's start, when the
/// hosted server is up, the client connected and spawned, the world view opened, its land, plants and grass streamed,
/// the world's things received; every frame over the budget with the pipelines the renderer compiled in it; the first
/// steady second. After the given seconds (45 by default) it prints the timeline and quits. It only watches: the same
/// signals with or without the loading pipeline, so a start before and after a change compare.
/// </summary>
public partial class LoadTrace : Node
{
    /// <summary>A frame slower than this is over the budget (two frames at 60 Hz).</summary>
    public const double BudgetMs = 33.4;

    private static readonly (string Name, RenderingServer.RenderingInfo Info)[] Pipelines =
    {
        ("draw", RenderingServer.RenderingInfo.PipelineCompilationsDraw),
        ("surface", RenderingServer.RenderingInfo.PipelineCompilationsSurface),
        ("mesh", RenderingServer.RenderingInfo.PipelineCompilationsMesh),
        ("canvas", RenderingServer.RenderingInfo.PipelineCompilationsCanvas),
        ("specialization", RenderingServer.RenderingInfo.PipelineCompilationsSpecialization),
    };

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly List<(double At, string What)> _marks = new();
    private readonly HashSet<string> _marked = new();
    private readonly List<(double At, double Ms, string Compiled)> _slow = new();
    private readonly ulong[] _compiled = new ulong[Pipelines.Length];
    private readonly ulong[] _compiledInLoad = new ulong[Pipelines.Length];
    private readonly ulong[] _compiledAtPlay = new ulong[Pipelines.Length];
    private double _offset;
    private double _steadySince = -1;
    private double _firstSteady = -1;
    private double _firstSteadyAfterReady = -1;
    private double _lastArrival = -1;
    private (int Things, long Pieces, int Natural) _signature;
    private bool _done;
    private int _frames;
    private double _lastFrame;
    private TimeSpan _gcPause, _jit;
    private Late? _late;

    // The last node to process each frame: when the process phase ended (the rest of a frame is drawing it).
    private sealed partial class Late : Node
    {
        public double At;
        public System.Diagnostics.Stopwatch Clock = null!;

        public override void _Process(double delta) => At = Clock.Elapsed.TotalSeconds;
    }

    /// <summary>The game being started.</summary>
    public GroundMode Mode { get; set; } = null!;

    /// <summary>Seconds after the process started when the timeline is printed and the game quits.</summary>
    public double Seconds { get; set; } = 45;

    /// <summary>A picture of the loading screen to save once the load is half done (<c>--load-shot &lt;file&gt;</c>).</summary>
    public string? ShotPath { get; set; }

    private bool _shot;

    /// <summary>
    /// <c>--arrival-check</c>: three seconds into play, the hosting player is teleported to the landing site (an arrival,
    /// GROUND.md §4.3.1), and the arrival's short load is timed.
    /// </summary>
    public bool ArrivalCheck { get; set; }

    private double _teleportAt = -1;

    /// <summary>Seconds since the process started.</summary>
    public double Now => _offset + _clock.Elapsed.TotalSeconds;

    public override void _EnterTree()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        _offset = Math.Max(0, (DateTime.Now - process.StartTime).TotalSeconds);
        ProcessPriority = int.MinValue;
        Mark("ground mode");
        _late = new Late { Name = "LoadTraceLate", ProcessPriority = int.MaxValue, Clock = _clock };
        GetParent().CallDeferred(Node.MethodName.AddChild, _late);
    }

    /// <summary>Notes a moment once (later calls with the same name are ignored).</summary>
    public void Mark(string what)
    {
        if (_marked.Add(what))
        {
            _marks.Add((Now, what));
        }
    }

    public override void _Process(double delta)
    {
        if (_done)
        {
            return;
        }
        double now = Now;
        if (_frames++ == 0)
        {
            Mark("first frame");
        }
        // The renderer's pipeline compilations since the last frame, by what triggered them.
        var compiled = new StringBuilder();
        for (int i = 0; i < Pipelines.Length; i++)
        {
            ulong total = RenderingServer.GetRenderingInfo(Pipelines[i].Info);
            ulong added = total - _compiled[i];
            _compiled[i] = total;
            if (added > 0 && _frames > 1)
            {
                compiled.Append(compiled.Length > 0 ? " " : "").Append(Pipelines[i].Name).Append(' ').Append(added);
            }
        }
        // The frame as the player saw it: the wall clock between two frames (Godot's delta is smoothed and capped).
        double ms = (now - _lastFrame) * 1000;
        // Of it: the process phase (to the last node's process), the garbage collector's pauses, the JIT on this thread.
        double processMs = _late != null && _late.At > 0 ? (_late.At + _offset - _lastFrame) * 1000 : -1;
        TimeSpan gc = GC.GetTotalPauseDuration(), jit = System.Runtime.JitInfo.GetCompilationTime(currentThread: true);
        double gcMs = (gc - _gcPause).TotalMilliseconds, jitMs = (jit - _jit).TotalMilliseconds;
        (_gcPause, _jit) = (gc, jit);
        _lastFrame = now;
        bool spawned = Mode.Client?.HasCharacter == true;
        if (ms > BudgetMs && _frames > 1)
        {
            _slow.Add((now, ms, string.Create(CultureInfo.InvariantCulture, $"process {processMs:0} ms, gc {gcMs:0} ms, jit {jitMs:0} ms") +
                (compiled.Length > 0 ? ", compiled: " + compiled : "")));
            _steadySince = -1;
        }
        else if (_steadySince < 0)
        {
            _steadySince = now;
        }
        Watch(now, spawned);
        Arrival(now);
        if (ShotPath != null && !_shot && Mode.Loading is { ShowScreen: true, Progress: >= 0.5 })
        {
            _shot = true;
            Shoot(ShotPath);
        }
        if (spawned && _steadySince >= 0 && now - _steadySince >= 1)
        {
            if (_firstSteady < 0)
            {
                _firstSteady = _steadySince;
            }
            if (_firstSteadyAfterReady < 0 && _marked.Contains("world ready") && _steadySince >= MarkAt("world ready"))
            {
                _firstSteadyAfterReady = _steadySince;
            }
        }
        if (now >= Seconds)
        {
            Print();
            _done = true;
            GetTree().Quit();
        }
    }

    // The signals polled every frame: the connection, the world view's streaming, the replicated things.
    private void Watch(double now, bool spawned)
    {
        if (Mode.Child?.Ready == true)
        {
            Mark("server ready");
        }
        GameClient? client = Mode.Client;
        if (client == null)
        {
            return;
        }
        if (client.State is ClientState.Joining or ClientState.Playing)
        {
            Mark("connected");
        }
        if (client.State == ClientState.Playing)
        {
            Mark("welcomed");
        }
        if (spawned)
        {
            Mark("spawned");
        }
        if (Mode.View is { Camera: not null, World: { } world })
        {
            Mark("world opened");
            if (world.Terrain.IsIdle)
            {
                Mark("land idle");
            }
            if (world.Plants?.Library != null)
            {
                Mark("plant library");
            }
            if (world.Plants?.IsIdle == true)
            {
                Mark("plants idle (impostors baked)");
            }
            if (world.Grass?.IsIdle == true)
            {
                Mark("grass idle");
            }
            if (world.IsReady)
            {
                Mark("world ready");
            }
        }
        if (Mode.View?.ReadyForShot == true)
        {
            Mark("ready for play (ReadyForShot)");
        }
        if (Mode.Loading is { } loading)
        {
            foreach (Hollow.Client.Loading.LoadStage stage in loading.Stages)
            {
                if (stage.Done)
                {
                    Mark($"stage {stage.Key} done");
                }
            }
            if (loading.Playing)
            {
                Mark("play handed over");
            }
        }
        // The things, building pieces and natural states the server sent: the last time one arrived.
        (int Things, long Pieces, int Natural) signature = (client.Things.Count(), client.BuildingReplica?.PiecesReceived ?? 0, client.Natural?.Count ?? 0);
        if (signature != _signature)
        {
            _signature = signature;
            _lastArrival = now;
        }
        if (!spawned)
        {
            Array.Copy(_compiled, _compiledInLoad, _compiled.Length);
        }
        if (Mode.Loading?.Playing != true)
        {
            Array.Copy(_compiled, _compiledAtPlay, _compiled.Length);
        }
    }

    private double MarkAt(string what) => _marks.First(m => m.What == what).At;

    // The arrival check: a far teleport three seconds into play, then the arrival's phases as they come.
    private void Arrival(double now)
    {
        if (!ArrivalCheck || Mode.Loading is not { } loading || Mode.Client is not { } client || !_marked.Contains("play handed over"))
        {
            return;
        }
        if (_teleportAt < 0 && now - MarkAt("play handed over") >= 3)
        {
            _teleportAt = now;
            Mark(string.Create(CultureInfo.InvariantCulture,
                $"arrival check: teleported {Vector2Distance(client):0} m to the landing site ({Mode.AdminCommand?.Invoke($"teleport {client.Name} 5 5") == true})"));
            return;
        }
        if (_teleportAt < 0)
        {
            return;
        }
        if (loading.Phase == Hollow.Client.Loading.LoadPhase.Arriving)
        {
            Mark("arrival: input held, the arrival's stages streaming");
        }
        if (loading.Phase == Hollow.Client.Loading.LoadPhase.Arriving && loading.ShowScreen)
        {
            Mark("arrival: the short screen shows (past the grace)");
        }
        if (_marked.Contains("arrival: input held, the arrival's stages streaming") && loading.Playing)
        {
            Mark("arrival: play again");
        }
    }

    private static double Vector2Distance(GameClient client) =>
        new System.Numerics.Vector2(client.Character.Position.X - 5, client.Character.Position.Z - 5).Length();

    // The window as drawn this frame (the loading screen over the world).
    private async void Shoot(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image image = GetViewport().GetTexture().GetImage();
        Error error = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? image.SavePng(path) : image.SaveJpg(path, 0.9f);
        Mark($"loading screen saved ({error}, {Mode.Loading?.Current?.Title}, {Mode.Loading?.Progress:P0})");
    }

    private void Print()
    {
        var c = CultureInfo.InvariantCulture;
        GD.Print($"[load] timeline, seconds since the process started (window {GetViewport().GetVisibleRect().Size}, {RenderingServer.GetVideoAdapterName()}):");
        foreach ((double at, string what) in _marks.OrderBy(m => m.At))
        {
            GD.Print(string.Create(c, $"[load]   {at,6:0.00}  {what}"));
        }
        if (_lastArrival >= 0)
        {
            GD.Print(string.Create(c, $"[load]   {_lastArrival,6:0.00}  last thing, building piece or natural state received ({_signature.Things} things, {_signature.Pieces} pieces, {_signature.Natural} natural states)"));
        }
        GD.Print(_firstSteady >= 0 ? string.Create(c, $"[load]   {_firstSteady,6:0.00}  first steady second after spawning (no frame over {BudgetMs:0} ms)") : "[load]   no steady second after spawning");
        if (_firstSteadyAfterReady >= 0)
        {
            GD.Print(string.Create(c, $"[load]   {_firstSteadyAfterReady,6:0.00}  first steady second once the world was ready"));
        }
        double spawnedAt = _marked.Contains("spawned") ? MarkAt("spawned") : double.MaxValue;
        GD.Print(string.Create(c, $"[load] frames over {BudgetMs:0} ms: {_slow.Count} ({_slow.Count(s => s.At >= spawnedAt)} after spawning), of {_frames} frames"));
        foreach ((double at, double ms, string what) in _slow)
        {
            GD.Print(string.Create(c, $"[load]   {at,6:0.00}  {ms,5:0} ms  {what}"));
        }
        var totals = new StringBuilder();
        for (int i = 0; i < Pipelines.Length; i++)
        {
            totals.Append(string.Create(c, $"{Pipelines[i].Name} {_compiled[i]} ({_compiled[i] - _compiledInLoad[i]} after spawning, " +
                $"{_compiled[i] - _compiledAtPlay[i]} in play)"));
            totals.Append(i + 1 < Pipelines.Length ? ", " : "");
        }
        GD.Print("[load] pipelines compiled: " + totals);
        if (Mode.View is { } view)
        {
            GD.Print("[load] the ground view's slowest parts: " + string.Join(", ",
                view.Slowest.OrderByDescending(p => p.Value).Take(8).Select(p => string.Create(c, $"{p.Key} {p.Value:0} ms"))));
            GD.Print("[load] warm-up's showing: " + view.Warmup.Costs);
            GD.Print(string.Create(c, $"[load] warm-up: {view.Warmup.Files} files, {view.Warmup.Shown} of {view.Warmup.Items} things shown, done {view.Warmup.Done}; " +
                $"video memory {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576} MB, " +
                $"static memory {OS.GetStaticMemoryUsage() / 1048576} MB, managed {GC.GetTotalMemory(false) / 1048576} MB"));
        }
        if (Mode.View?.World is { } world)
        {
            GD.Print("[load] opening the world view took: " + world.OpenTimes);
        }
        if (Mode.Loading is { } loading)
        {
            GD.Print("[load] the loading pipeline: " + loading.Timeline());
        }
    }
}
