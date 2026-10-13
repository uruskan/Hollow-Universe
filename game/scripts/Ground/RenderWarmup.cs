using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Godot;
using Hollow.Client.Loading;
using HollowUniverse.Common;

namespace HollowUniverse.Ground;

/// <summary>
/// The renderer's warm-up (GROUND.md §4.3.1, the loading stage <c>renderer</c>). Every file the game's libraries list
/// (<see cref="WarmItem"/>: models, characters, the building kits, gibs, animals, ships, effects, sounds) is asked of
/// Godot's loader threads from the game's first frames, and kept for the whole game, so nothing the game uses later
/// waits on a file. Once the world is open, every item is shown once before the world's camera, tiny, through its
/// library's own drawing path, under the loading screen: the renderer loads every material's shader and builds every
/// pipeline then. The stage is done when it has drawn everything for the plan's <c>settleFrames</c> frames in a row
/// without building a pipeline, or after its <c>mostSeconds</c>.
/// </summary>
public partial class RenderWarmup : Node
{
    public const string Key = "renderer";

    /// <summary>The pipelines that cost a frame when built: while drawing, for a new surface, for a new mesh.</summary>
    private static readonly RenderingServer.RenderingInfo[] Builds =
    {
        RenderingServer.RenderingInfo.PipelineCompilationsDraw,
        RenderingServer.RenderingInfo.PipelineCompilationsSurface,
        RenderingServer.RenderingInfo.PipelineCompilationsMesh,
    };

    /// <summary>The main thread's time a frame for showing items (their first draw costs more, later in the frame).</summary>
    private const double ShowBudgetMs = 12;


    private readonly List<WarmItem> _waiting;
    private readonly Queue<string> _toRequest;
    private readonly int _total;
    private readonly Dictionary<string, Resource?> _held = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loading = new(StringComparer.Ordinal);
    private readonly int _perFrame;
    private readonly int _inFlight;
    private readonly int _settleFrames;
    private readonly double _mostSeconds;
    private readonly Stopwatch _clock = new();
    private readonly List<(double Ms, string Name)> _costly = new();
    private Node3D? _holder;
    private Camera3D? _camera;
    private int _shown;
    private int _quiet;
    private double _slowestFrameMs;
    private ulong _built;
    private bool _done;

    public RenderWarmup(IEnumerable<WarmItem> items, StagePlan plan)
    {
        _waiting = items.ToList();
        _toRequest = new Queue<string>(_waiting.SelectMany(i => i.Files).Distinct(StringComparer.Ordinal));
        _total = _waiting.Count;
        _perFrame = Math.Max(1, (int)plan.Number("perFrame", 12));
        _inFlight = Math.Max(1, (int)plan.Number("loadsAtOnce", 4));
        _settleFrames = Math.Max(1, (int)plan.Number("settleFrames", 10));
        _mostSeconds = plan.Number("mostSeconds", 40);
    }

    /// <summary>Files to load, in all.</summary>
    public int Files => _held.Count + _loading.Count + _toRequest.Count;

    /// <summary>Items shown so far, of <see cref="Items"/>.</summary>
    public int Shown => _shown;

    public int Items => _total;

    public bool Done => _done;

    /// <summary>What showing cost the main thread: the costliest items and the most in one frame (the start's timeline).</summary>
    public string Costs => string.Create(CultureInfo.InvariantCulture,
        $"at most {_slowestFrameMs:0} ms in a frame; costliest: {string.Join(", ", _costly.OrderByDescending(c => c.Ms).Take(8).Select(c => $"{c.Name} {c.Ms:0} ms"))}");

    /// <summary>The loading stage: done when the world's camera drew everything and the renderer stopped building pipelines.</summary>
    public LoadStage Stage() => new(Key, () => _done, Progress);

    /// <summary>
    /// The world is open (its camera current): items are shown before its camera, in its light, tiny, two metres ahead.
    /// Not sooner: the first draw of a material loads its shader's variants, seconds for the characters' alone, and the
    /// world's opening loads those anyway (the arms, the spare bodies); before it the main thread must stay free to see
    /// the hosted server ready and connect.
    /// </summary>
    public void Begin(Camera3D camera)
    {
        if (_done || _holder != null)
        {
            return;
        }
        _camera = camera;
        _holder = new Node3D { Name = "Shown", Transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * 0.004f), new Vector3(0, 0, -2)) };
        camera.AddChild(_holder);
        _built = Built();
        _clock.Restart();
    }

    public override void _Process(double delta)
    {
        Request();
        Collect();
        if (_holder == null || _done)
        {
            return;
        }
        long start = Stopwatch.GetTimestamp();
        int shown = 0;
        for (int i = 0; i < _waiting.Count && shown < _perFrame && Stopwatch.GetElapsedTime(start).TotalMilliseconds < ShowBudgetMs; i++)
        {
            WarmItem item = _waiting[i];
            if (item.Files.Any(f => !_held.ContainsKey(f)))
            {
                continue;
            }
            _waiting.RemoveAt(i--);
            long began = Stopwatch.GetTimestamp();
            ShowItem(item);
            _costly.Add((Stopwatch.GetElapsedTime(began).TotalMilliseconds, item.Name));
            shown++;
        }
        _slowestFrameMs = Math.Max(_slowestFrameMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        // Quiet: a frame of the world's camera with nothing left to show and no pipeline built.
        ulong built = Built();
        _quiet = _waiting.Count == 0 && shown == 0 && built == _built ? _quiet + 1 : 0;
        _built = built;
        if (_quiet >= _settleFrames || _clock.Elapsed.TotalSeconds > _mostSeconds)
        {
            if (_quiet < _settleFrames)
            {
                GD.PushWarning($"Warm-up: stopped after {_mostSeconds:0} s with {_waiting.Count} of {_total} items not shown " +
                    $"({string.Join(", ", _waiting.Take(5).Select(w => w.Name))}).");
            }
            _done = true;
            _holder.QueueFree();
            _holder = null;
        }
    }

    // A few files at once on the loader threads, in the items' order (what is drawn first, sounds last), so a hosted
    // server starting beside the game keeps its share of the processor.
    private void Request()
    {
        while (_loading.Count < _inFlight && _toRequest.TryDequeue(out string? file))
        {
            if (ResourceLoader.Exists(file) && ResourceLoader.LoadThreadedRequest(file) == Error.Ok)
            {
                _loading.Add(file);
            }
            else
            {
                _held[file] = null;
            }
        }
    }

    // Files the loader threads finished: held for the game (a later load of the same path is then immediate).
    private void Collect()
    {
        if (_loading.Count == 0)
        {
            return;
        }
        foreach (string file in _loading.ToArray())
        {
            ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(file);
            if (status == ResourceLoader.ThreadLoadStatus.InProgress)
            {
                continue;
            }
            _loading.Remove(file);
            _held[file] = status == ResourceLoader.ThreadLoadStatus.Loaded ? ResourceLoader.LoadThreadedGet(file) : null;
            if (_held[file] == null)
            {
                GD.PushWarning($"Warm-up: {file} did not load ({status}).");
            }
        }
    }

    private void ShowItem(WarmItem item)
    {
        var place = new Vector3(_shown % 16, _shown / 16 % 16, 0);
        _shown++;
        try
        {
            Node3D? node = item.Show(new WarmSpot(_holder!, place, _holder!.ToGlobal(place), _camera!));
            if (node == null)
            {
                return;
            }
            if (node.GetParent() == null)
            {
                _holder!.AddChild(node);
            }
            node.Position = place;
        }
        catch (Exception e)
        {
            GD.PushWarning($"Warm-up: {item.Name} could not be shown: {e.Message}");
        }
    }

    private double Progress()
    {
        int files = Files;
        double loaded = files == 0 ? 1 : (double)_held.Count / files;
        double shown = _total == 0 ? 1 : (double)_shown / _total;
        return 0.45 * loaded + 0.45 * shown + 0.1 * Math.Min(1, (double)_quiet / _settleFrames);
    }

    private static ulong Built()
    {
        ulong n = 0;
        foreach (RenderingServer.RenderingInfo info in Builds)
        {
            n += RenderingServer.GetRenderingInfo(info);
        }
        return n;
    }
}
