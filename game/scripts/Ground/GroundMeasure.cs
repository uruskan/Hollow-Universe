using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Server;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Measures the ground game's frame time where it is played (GROUND.md §5, <c>--measure &lt;s&gt;</c>): once the world
/// is drawn and at least <c>--expect &lt;n&gt;</c> other players are in view (or a minute has passed), it turns towards
/// them, turns vsync off, records every frame for the given seconds, prints the average, 99th percentile and worst
/// frame (and, when hosting, the server's tick over the same seconds) and quits.
/// </summary>
public partial class GroundMeasure : Node
{
    private readonly List<double> _frames = new();
    private double _waited;
    private double _measured;
    private bool _started;
    private double _process, _gpu, _cpu;
    private (int, int, int) _gc, _last;
    private long _allocated, _allocatedHere;
    private readonly List<(double Delta, string Events)> _events = new();

    public GroundView View { get; set; } = null!;

    public GameServer? Server { get; set; }

    /// <summary>Admin commands to a server hosted in its own process: its tick is asked for with "status".</summary>
    public Func<string, bool>? Command { get; set; }

    public double Seconds { get; set; } = 20;

    public int Expect { get; set; }

    // Allocation probes: the first and the last node to process each frame.
    private sealed partial class Probe : Node
    {
        public long Bytes;
        public long Last;
        public long Phase;
        public Probe? First;

        public override void _Process(double delta)
        {
            long now = GC.GetAllocatedBytesForCurrentThread();
            if (First != null)
            {
                Phase += now - First.Last; // this frame's process phase
                First.Bytes += First.Last == 0 ? 0 : First.Last - Last; // the rest of the last frame (drawing, physics)
            }
            Last = now;
        }
    }

    private Probe? _early, _late;

    public override void _Ready()
    {
        _early = new Probe { Name = "ProbeEarly", ProcessPriority = int.MinValue };
        _late = new Probe { Name = "ProbeLate", ProcessPriority = int.MaxValue };
        _late.First = _early;
        GetParent().CallDeferred(Node.MethodName.AddChild, _early);
        GetParent().CallDeferred(Node.MethodName.AddChild, _late);
    }

    public override void _Process(double delta)
    {
        GameClient client = View.Client;
        if (!_started)
        {
            _waited += delta;
            int seen = client.Entities.Count();
            if (!View.ReadyForShot || (seen < Expect && _waited < 60))
            {
                return;
            }
            _started = true;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
            Server?.Ticks.ResetTotals();
            View.Timings.Clear();
            View.Allocations.Clear();
            View.Slowest.Clear();
            _late!.Phase = 0;
            _early!.Bytes = 0;
            _gc = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
            _allocated = GC.GetTotalAllocatedBytes();
            _allocatedHere = GC.GetAllocatedBytesForCurrentThread();
            FaceCrowd(client);
            GD.Print($"[measure] start: {seen} other players in view, after {_waited:0} s");
            FrameEvents.Enabled = true;
            FrameEvents.Take();
            return;
        }
        _measured += delta;
        _frames.Add(delta);
        (int g0, int g1, int g2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        if (g2 != _last.Item3)
        {
            FrameEvents.Note("gc2");
        }
        else if (g1 != _last.Item2)
        {
            FrameEvents.Note("gc1");
        }
        else if (g0 != _last.Item1)
        {
            FrameEvents.Note("gc0");
        }
        _last = (g0, g1, g2);
        _events.Add((delta, FrameEvents.Take()));
        _process += Performance.GetMonitor(Performance.Monitor.TimeProcess);
        _gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
        _cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()) + RenderingServer.GetFrameSetupTimeCpu();
        if (_measured < Seconds)
        {
            return;
        }
        // The slowest frames, each with what the frame before it did (its cost lands on the next frame's delta).
        var slow = new List<string>();
        for (int i = 1; i < _events.Count; i++)
        {
            slow.Add($"{_events[i].Delta * 1000:0} ms [{_events[i - 1].Events}|{_events[i].Events}]");
        }
        var order = Enumerable.Range(1, _events.Count - 1).OrderByDescending(i => _events[i].Delta).Take(12).Select(i => slow[i - 1]);
        GD.Print("[measure] slowest frames: " + string.Join("; ", order));
        GD.Print("[measure] slowest parts of the ground view: " + string.Join(", ", View.Slowest.Select(p => $"{p.Key} {p.Value:0.0} ms")));
        _frames.Sort();
        double average = _frames.Average();
        double p99 = _frames[(int)(_frames.Count * 0.99)];
        GD.Print($"[measure] players in view={client.Entities.Count()} frames={_frames.Count} avg={average * 1000:0.00} ms ({1 / average:0} fps) " +
            $"p99={p99 * 1000:0.00} ms worst={_frames[^1] * 1000:0.00} ms window={Seconds:0}s size={GetViewport().GetVisibleRect().Size} " +
            $"gpu={RenderingServer.GetVideoAdapterName()} plants={View.World?.Plants?.MeshInstances}/{View.World?.Plants?.ImpostorInstances} grass={View.World?.Grass?.Tufts}");
        GD.Print($"[measure] per frame: process {_process / _frames.Count * 1000:0.00} ms, render cpu {_cpu / _frames.Count:0.00} ms, gpu {_gpu / _frames.Count:0.00} ms; " +
            "the ground view: " + string.Join(", ", View.Timings.Select(p => $"{p.Key} {p.Value / _frames.Count:0.00}")) +
            $"; collections gen0 {GC.CollectionCount(0) - _gc.Item1}, gen1 {GC.CollectionCount(1) - _gc.Item2}, gen2 {GC.CollectionCount(2) - _gc.Item3}, " +
            $"pause total {GC.GetTotalPauseDuration().TotalMilliseconds:0} ms since start; allocated {(GC.GetTotalAllocatedBytes() - _allocated) / Seconds / 1e6:0.0} MB/s, " +
            $"{(GC.GetAllocatedBytesForCurrentThread() - _allocatedHere) / Seconds / 1e6:0.0} MB/s on the main thread, of which the ground view's parts (KB a frame): " +
            string.Join(", ", View.Allocations.Select(p => $"{p.Key} {p.Value / 1024.0 / _frames.Count:0.0}")) +
            $"; process phase {_late!.Phase / 1024.0 / _frames.Count:0.0} KB a frame, the rest {_early!.Bytes / 1024.0 / _frames.Count:0.0} KB");
        if (Server == null)
        {
            // A server in its own process prints its tick (the game's log shows it as "[server] tick ...").
            Command?.Invoke("status");
        }
        if (Server != null)
        {
            TickStats t = Server.Ticks;
            GD.Print($"[measure] server: {Server.Players.Count} players, tick avg={t.TotalAverageMs:0.00} ms p99={t.TotalPercentileMs(99):0.00} ms " +
                $"worst={t.TotalMaxMs:0.0} ms over={t.Overruns} of {t.Ticks}; {Server.Zone.Physics.TileCount} ground tiles, {Server.Zone.Physics.PropCount} props");
        }
        GetTree().Quit();
    }

    // Faces the middle of the other players, looking a little down.
    private void FaceCrowd(GameClient client)
    {
        Num.Vector3[] others = client.Entities.Select(e => e.Position).ToArray();
        if (others.Length == 0)
        {
            return;
        }
        Num.Vector3 centre = others.Aggregate(Num.Vector3.Zero, (a, b) => a + b) / others.Length;
        Num.Vector3 d = centre - client.Character.Position;
        View.Yaw = MathF.Atan2(-d.X, -d.Z);
        View.Pitch = -0.08f;
    }
}
