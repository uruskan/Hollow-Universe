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
/// (<see cref="WarmItem"/>: models, characters, the building kits, gibs, animals, ships, effects, sounds) is admitted
/// through a bounded item/file lookahead. Loaded resources stay pinned for the game; this is an admission bound,
/// not a total residency budget. Once the world is open, admitted items are shown before the world's camera through their
/// library's own drawing path, under the loading screen: the renderer loads every material's shader and builds every
/// pipeline then. Temporary preview batches are retired after their draw frames, rather than retaining a second
/// scene containing the whole catalogue. The stage is done when it has drawn everything for the plan's <c>settleFrames</c> frames in a row
/// without building a pipeline, or after its <c>mostSeconds</c>. Timeout stops further admission and reports the
/// incomplete catalogue; remaining resources and pipelines may then load through their ordinary gameplay consumers.
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


    private readonly WarmupAssetAdmission _admission;
    private readonly int _total;
    private readonly Dictionary<string, Resource?> _held = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loading = new(StringComparer.Ordinal);
    private readonly int _perFrame;
    private readonly int _inFlight;
    private readonly IAssetLoader _loader;
    private readonly Func<string, bool> _ready;
    private bool _awaitingAdmissionDrain;
    private readonly int _settleFrames;
    private readonly double _mostSeconds;
    private readonly Stopwatch _clock = new();
    private readonly List<(double Ms, string Name)> _costly = new();
    private Node3D? _holder;
    private sealed class Preview(WarmItem item, Node3D holder, double readyAt, ulong draw, bool armed)
    {
        public readonly WarmItem Item = item;
        public readonly Node3D Holder = holder;
        public readonly double ReadyAt = readyAt;
        public ulong Draw = draw;
        public bool DrawsArmed = armed;
    }
    private readonly Queue<Preview> _previews = new();
    private readonly List<Node3D> _retiringPreviews = new();
    private ulong _draws;
    private double _previewTime;
    private int _peakPreviews;
    private Camera3D? _camera;
    private int _shown;
    private int _quiet;
    private double _slowestFrameMs;
    private ulong _built;
    private bool _done;

    public RenderWarmup(IEnumerable<WarmItem> items, StagePlan plan) : this(items, plan, new GodotAssetLoader()) { }

    // The native lifecycle fixture supplies a deterministic loader; ordinary construction always uses Godot.
    internal RenderWarmup(IEnumerable<WarmItem> items, StagePlan plan, IAssetLoader loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        _admission = new WarmupAssetAdmission(items,
            Math.Clamp((int)plan.Number("itemLookahead", WarmupAssetAdmission.DefaultLookahead), 1, WarmupAssetAdmission.MaximumLookahead));
        _total = _admission.TotalItems;
        _ready = _held.ContainsKey;
        _perFrame = Math.Clamp((int)plan.Number("perFrame", 12), 1, 64);
        _inFlight = Math.Clamp((int)plan.Number("loadsAtOnce", 6), 1, 6);
        _settleFrames = Math.Max(1, (int)plan.Number("settleFrames", 10));
        _mostSeconds = plan.Number("mostSeconds", 40);
    }

    /// <summary>Files to load, in all.</summary>
    public int Files => _admission.TotalFiles;

    /// <summary>Items shown so far, of <see cref="Items"/>.</summary>
    public int Shown => _shown;

    public int Items => _total;

    public bool Done => _done;

    // Includes nodes queued for deletion until their destruction has been observed on a later process tick.
    public int LivePreviews => _previews.Count + _retiringPreviews.Count;
    public int PeakPreviews => _peakPreviews;
    /// <summary>Explicit catalogue pins; dependencies and resources retained by libraries are not included.</summary>
    public int HeldFiles => _held.Count(pair => pair.Value != null);
    public int LoadingFiles => _loading.Count;
    /// <summary>Whole-plan paths never requested, including paths outside the current admission window.</summary>
    public int QueuedFiles => _admission.UnrequestedFiles;
    public int AdmittedItems => _admission.AdmittedItems;
    /// <summary>Distinct dependencies of admitted unshown items, including loaded entries. Old cache pins are separate.</summary>
    public int AdmittedPendingFiles => _admission.PendingFiles;
    public int QueuedAdmittedFiles => _admission.QueuedAdmittedFiles;
    public int RequestedFiles => _admission.RequestedFiles;

    public override void _EnterTree() => RenderingServer.FramePostDraw += PreviewDrawn;

    public override void _ExitTree()
    {
        RenderingServer.FramePostDraw -= PreviewDrawn;
        RetirePreview(retryUnfinished: false);
        _retiringPreviews.Clear();
        _held.Clear();
        _loading.Clear();
        _admission.Clear();
    }

    /// <summary>Detach camera-owned temporary nodes before a world retires. Loaded catalogue resources stay shared.</summary>
    public void RetirePreview() => RetirePreview(retryUnfinished: !_done);

    private void RetirePreview(bool retryUnfinished)
    {
        ReleasePreviews(all: true, retryUnfinished: retryUnfinished);
        if (_holder != null && GodotObject.IsInstanceValid(_holder)) _holder.QueueFree();
        _holder = null;
        _camera = null;
        if (retryUnfinished) _quiet = 0;
    }

    private void PreviewDrawn() => _draws++;

    private bool PreviewComplete(Preview preview) => preview.DrawsArmed && _draws >= preview.Draw + 2;

    private void ReleasePreviews(bool all = false, bool retryUnfinished = false)
    {
        // A delayed item starts counting draws on the first process tick at/after its deadline. The draw just
        // before that tick cannot count towards its two subsequent frames of rendering.
        foreach (Preview preview in _previews)
            if (!preview.DrawsArmed && _previewTime >= preview.ReadyAt)
            { preview.DrawsArmed = true; preview.Draw = _draws; }
        List<WarmItem>? retry = retryUnfinished ? new List<WarmItem>() : null;
        while (_previews.TryPeek(out var preview) && (all || PreviewComplete(preview)))
        {
            _previews.Dequeue();
            if (retry != null && !PreviewComplete(preview)) retry.Add(preview.Item);
            if (GodotObject.IsInstanceValid(preview.Holder))
            {
                preview.Holder.QueueFree();
                _retiringPreviews.Add(preview.Holder);
            }
        }
        if (retry is { Count: > 0 })
        {
            _admission.Requeue(retry);
            _awaitingAdmissionDrain = _loading.Count != 0;
            _shown -= retry.Count;
        }
    }

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
        _previewTime += Math.Max(0, delta);
        for (int i = _retiringPreviews.Count - 1; i >= 0; i--)
            if (!GodotObject.IsInstanceValid(_retiringPreviews[i])) _retiringPreviews.RemoveAt(i);
        ReleasePreviews();
        Collect();
        if (_done) return;
        // Check the deadline before admitting or starting another request. Already-started work is collected above.
        if (_holder != null && _clock.Elapsed.TotalSeconds > _mostSeconds)
        {
            Finish(timedOut: true);
            return;
        }
        if (_awaitingAdmissionDrain && _loading.Count == 0) _awaitingAdmissionDrain = false;
        if (!_awaitingAdmissionDrain) _admission.Admit();
        Request();
        if (_holder == null) return;
        if (_clock.Elapsed.TotalSeconds > _mostSeconds) { Finish(timedOut: true); return; }
        long start = Stopwatch.GetTimestamp();
        int shown = 0;
        // QueueFree takes effect after this process tick. Do not overlap a replacement batch with the retiring
        // nodes (including nodes under an old camera) just because they were removed from the active queue.
        while (_retiringPreviews.Count == 0 && shown < _perFrame && _previews.Count < _perFrame * 2
            && _clock.Elapsed.TotalSeconds <= _mostSeconds
            && Stopwatch.GetElapsedTime(start).TotalMilliseconds < ShowBudgetMs
            && _admission.TryTakeReady(_ready, out WarmItem? item))
        {
            long began = Stopwatch.GetTimestamp();
            ShowItem(item!);
            _costly.Add((Stopwatch.GetElapsedTime(began).TotalMilliseconds, item!.Name));
            shown++;
        }
        _slowestFrameMs = Math.Max(_slowestFrameMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        // Quiet: a frame of the world's camera with nothing left to show and no pipeline built.
        ulong built = Built();
        _quiet = _admission.Waiting.Count == 0 && LivePreviews == 0 && shown == 0 && built == _built ? _quiet + 1 : 0;
        _built = built;
        if (_quiet >= _settleFrames) Finish(timedOut: false);
        else if (_clock.Elapsed.TotalSeconds > _mostSeconds) Finish(timedOut: true);
    }

    private void Finish(bool timedOut)
    {
        if (timedOut)
        {
            GD.PushWarning($"Warm-up: stopped after {_mostSeconds:0} s with {_admission.Waiting.Count} of {_total} items not shown " +
                $"({string.Join(", ", _admission.Waiting.Take(5).Select(w => w.Name))}); further resource admission stopped.");
        }
        _done = true;
        _admission.Stop();
        RetirePreview(retryUnfinished: false);
    }

    // A few files at once on the loader threads, in the items' order (what is drawn first, sounds last), so a hosted
    // server starting beside the game keeps its share of the processor.
    private void Request()
    {
        while (!_done && !_awaitingAdmissionDrain && (_holder == null || _clock.Elapsed.TotalSeconds <= _mostSeconds)
            && _loading.Count < _inFlight && _admission.TryRequest(out string file))
        {
            if (_loader.Request(file))
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
            if (!_loader.Collect(file, out Resource? resource, out string status)) continue;
            _loading.Remove(file);
            _held[file] = resource;
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
        // A load-only chunk has no render work. It must not allocate a preview or wait for two artificial draws.
        if (item.IsLoadOnly) return;
        // A per-item owner also captures effects that return null and attach their own directors to WarmSpot.
        var preview = new Node3D { Name = "Preview" };
        _holder!.AddChild(preview);
        double delay = Math.Max(0, item.MinimumPreviewSeconds);
        _previews.Enqueue(new Preview(item, preview, _previewTime + delay, _draws, armed: delay == 0));
        _peakPreviews = Math.Max(_peakPreviews, LivePreviews);
        try
        {
            Node3D? node = item.Show(new WarmSpot(preview, place, preview.ToGlobal(place), _camera!, _holder));
            if (node == null)
            {
                return;
            }
            if (node.GetParent() == null)
            {
                preview.AddChild(node);
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

    internal interface IAssetLoader
    {
        bool Request(string file);
        bool Collect(string file, out Resource? resource, out string status);
    }

    private sealed class GodotAssetLoader : IAssetLoader
    {
        public bool Request(string file) => ResourceLoader.Exists(file) && ResourceLoader.LoadThreadedRequest(file) == Error.Ok;
        public bool Collect(string file, out Resource? resource, out string status)
        {
            ResourceLoader.ThreadLoadStatus result = ResourceLoader.LoadThreadedGetStatus(file);
            if (result == ResourceLoader.ThreadLoadStatus.InProgress) { resource = null; status = ""; return false; }
            status = result.ToString();
            resource = result == ResourceLoader.ThreadLoadStatus.Loaded ? ResourceLoader.LoadThreadedGet(file) : null;
            return true;
        }
    }
}
