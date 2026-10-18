using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Hollow.Client.Loading;
using HollowUniverse.Common;
using HollowUniverse.Ground;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    private async Task CheckWarmupPreviews()
    {
        await CheckWarmupAssetAdmission();
        var holders = new List<Node3D>();
        var shownIds = new List<int>();
        var shared = new List<Node3D>();
        var items = new List<WarmItem>();
        for (int i = 0; i < 16; i++)
        {
            int id = i;
            items.Add(new WarmItem("bounded-preview-" + i, spot =>
            {
                holders.Add(spot.Holder);
                shownIds.Add(id);
                Node3D owner = spot.SharedHolder ?? spot.Holder;
                Node3D? renderer = owner.GetNodeOrNull<Node3D>("SharedRenderer");
                if (renderer == null)
                {
                    renderer = new Node3D { Name = "SharedRenderer" };
                    owner.AddChild(renderer);
                }
                shared.Add(renderer);
                // Exercise the null-return route used by private effect directors as well as ordinary nodes.
                spot.Holder.AddChild(new Node3D());
                return null;
            }) { MinimumPreviewSeconds = i == 0 ? 1 : 0 });
        }
        var plan = new StagePlan("renderer", "renderer", 1, 0,
            new Dictionary<string, double> { ["perFrame"] = 2, ["mostSeconds"] = 60 });
        var camera = new Camera3D();
        var replacement = new Camera3D();
        var warmup = new RenderWarmup(items, plan);
        AddChild(camera); AddChild(replacement); AddChild(warmup);
        warmup.SetProcess(false);
        MethodInfo drawn = typeof(RenderWarmup).GetMethod("PreviewDrawn", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            warmup.Begin(camera);
            for (int i = 0; i < 12; i++) warmup._Process(0);
            Expect("warm-up preview admission waits for draw events", warmup.LivePreviews == 4 && warmup.Shown == 4,
                $"simulated FramePostDraw notifications; two preview batches retained; shown={warmup.Shown}, live={warmup.LivePreviews}");
            Expect("warm-up shared renderer ownership stays outside each item", shared.Distinct().Count() == 1,
                "four temporary item owners reuse one renderer under the shared warmup root");
            drawn.Invoke(warmup, null); drawn.Invoke(warmup, null);
            warmup._Process(0.5);
            Expect("warm-up preserves delayed effect previews", warmup.LivePreviews == 4 && !holders[0].IsQueuedForDeletion(),
                "two draws cannot retire a preview before its delayed renderers appear");
            drawn.Invoke(warmup, null); // a draw before the next process tick crosses the deadline
            warmup._Process(0.6);
            Expect("warm-up delayed previews still need subsequent draws", !holders[0].IsQueuedForDeletion(),
                "simulation time alone cannot retire the last delayed layer");
            drawn.Invoke(warmup, null);
            warmup._Process(0);
            Expect("warm-up pre-deadline draw cannot count towards final two", !holders[0].IsQueuedForDeletion(),
                "one draw after crossing the delay is insufficient even when the preceding frame also drew");
            drawn.Invoke(warmup, null);
            warmup._Process(0);
            Expect("warm-up retires returned-null owners without overlapping replacement admission", holders.Take(4).All(h => h.IsQueuedForDeletion())
                && holders.Count == 4 && warmup.Shown == 4 && warmup.LivePreviews == 4 && warmup.PeakPreviews <= 4,
                "queued nodes remain counted; no new batch is created before their actual deletion");
            warmup._Process(0);
            Expect("warm-up repeated process ticks cannot bypass deferred deletion bound", holders.Count == 4
                && holders.Count(GodotObject.IsInstanceValid) == 4,
                "admission waits for destroyed owners, not merely removal from the active queue");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            warmup._Process(0);
            Expect("warm-up admits the next batch after queued owners are destroyed", holders.Count == 6
                && holders.Count(GodotObject.IsInstanceValid) == 2 && shownIds.TakeLast(2).SequenceEqual(new[] { 4, 5 }),
                "the normal deferred-free boundary releases native preview owners before replacement");

            // Interrupt this new batch before any draw notification. Its items must be replayed on the new camera.
            warmup.RetirePreview();
            Expect("warm-up camera retirement requeues unfinished previews", warmup.Shown == 4
                && holders.TakeLast(2).All(h => h.IsQueuedForDeletion()),
                "items removed from the waiting catalogue but not yet rendered return to it");
            warmup.Begin(replacement);
            warmup._Process(0);
            Expect("warm-up replacement camera waits for old preview deletion", holders.Count == 6 && warmup.Shown == 4,
                "a camera handover cannot overlap two generations of temporary preview nodes");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            warmup._Process(0);
            Expect("warm-up replays unfinished catalogue on replacement camera", warmup.LivePreviews == 2 && warmup.Shown == 6
                && shownIds.TakeLast(2).SequenceEqual(new[] { 4, 5 }) && holders[^1].GetParent()?.GetParent() == replacement,
                "both interrupted items are shown again, in order, under the replacement camera");
            Expect("warm-up replacement keeps one new shared renderer", shared[^1] == shared[^2]
                && !GodotObject.IsInstanceValid(shared[0]), "the old shared renderer retires with its camera-owned root");
        }
        finally
        {
            warmup.Free();
            camera.Free();
            replacement.Free();
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task CheckWarmupAssetAdmission()
    {
        // The admission model has no native resources. Exercise ready-but-unshown dependencies, not only requests
        // in flight: otherwise a blocked camera could still accumulate the whole catalogue six files at a time.
        var sounds = Enumerable.Range(0, 1210).Select(i => "fixture/sound-" + i).ToArray();
        var soundPlan = new WarmupAssetAdmission(new[] { WarmItem.Load("sounds", sounds) });
        soundPlan.Admit();
        Expect("warm-up large load-only groups split without losing catalogue files", soundPlan.TotalItems == 38
            && soundPlan.TotalFiles == 1210 && soundPlan.Waiting.All(item => item.IsLoadOnly && item.Files.Count <= 32)
            && soundPlan.AdmittedItems == 2 && soundPlan.PendingFiles == 64 && soundPlan.QueuedAdmittedFiles == 64,
            "1,210 distinct sound dependencies become 38 bounded chunks; only two chunks fit the independent file window");
        var ready = new HashSet<string>(StringComparer.Ordinal);
        while (soundPlan.TryRequest(out string initialSound)) ready.Add(initialSound);
        soundPlan.Admit();
        Expect("warm-up ready unshown files continue consuming the admission budget", soundPlan.PendingFiles == 64
            && soundPlan.RequestedFiles == 64 && soundPlan.AdmittedItems == 2 && soundPlan.QueuedAdmittedFiles == 0,
            "finishing loader work cannot advance a camera-blocked catalogue");
        int consumed = 0;
        for (int guard = 0; guard < 100 && consumed < soundPlan.TotalItems; guard++)
        {
            while (soundPlan.TryTakeReady(ready.Contains, out _)) consumed++;
            soundPlan.Admit();
            while (soundPlan.TryRequest(out string nextSound)) ready.Add(nextSound);
        }
        Expect("warm-up chunked load-only catalogue can finish", consumed == 38 && ready.Count == 1210
            && soundPlan.Waiting.Count == 0 && soundPlan.PendingFiles == 0,
            "the file cap cannot deadlock readiness on a 1,210-file atomic load group");

        string[] kit = Enumerable.Range(0, 10).Select(i => "fixture/kit-" + i).ToArray();
        var sharedPlan = new WarmupAssetAdmission(new[]
        {
            new WarmItem("kit-a", kit, _ => null), new WarmItem("kit-b", kit.Concat(kit).ToArray(), _ => null),
            new WarmItem("empty-c", _ => null)
        });
        sharedPlan.Admit();
        var requested = new HashSet<string>(StringComparer.Ordinal);
        while (sharedPlan.TryRequest(out string sharedFile)) requested.Add(sharedFile);
        sharedPlan.TryTakeReady(requested.Contains, out _);
        Expect("warm-up shared files are admitted and requested once", sharedPlan.TotalItems == 3 && sharedPlan.TotalFiles == 10
            && requested.Count == 10 && sharedPlan.RequestedFiles == 10 && sharedPlan.PendingFiles == 10,
            "the second atomic ten-file kit retains the shared pending references after the first item is consumed");
        sharedPlan.TryTakeReady(requested.Contains, out _);
        Expect("warm-up final shared consumer releases pending references", sharedPlan.PendingFiles == 0,
            "retained cache ownership is distinct from the unshown dependency window");
        var emptyPlan = new WarmupAssetAdmission(Enumerable.Range(0, 100).Select(i => new WarmItem("empty-" + i, _ => null)));
        emptyPlan.Admit();
        Expect("warm-up empty-file callbacks obey item lookahead", emptyPlan.AdmittedItems == 24 && emptyPlan.PendingFiles == 0,
            "callbacks that load their own assets cannot skip the waiting prefix");
        var blockedPlan = new WarmupAssetAdmission(new[]
        {
            new WarmItem("blocked-a", new[] { "fixture/a" }, _ => null),
            new WarmItem("blocked-b", new[] { "fixture/b" }, _ => null), new WarmItem("outside-prefix", _ => null)
        }, lookahead: 2);
        blockedPlan.Admit();
        Expect("warm-up ready callbacks cannot bypass the admitted prefix", !blockedPlan.TryTakeReady(_ => false, out _)
            && blockedPlan.AdmittedItems == 2 && blockedPlan.Waiting.Count == 3,
            "an empty-file callback beyond two blocked admitted items cannot start hidden loading work");
        bool rejected = false;
        try { _ = new WarmupAssetAdmission(new[] { new WarmItem("oversized-draw", sounds.Take(65).ToArray(), _ => null) }); }
        catch (ArgumentException error) { rejected = error.Message.Contains("oversized-draw", StringComparison.Ordinal) && error.Message.Contains("64", StringComparison.Ordinal); }
        Expect("warm-up oversized normal dependency groups fail explicitly", rejected,
            "draw callbacks are never silently truncated or converted into load-only chunks");

        using var resource = new Resource();
        var loader = new WarmupFixtureLoader(resource) { CanComplete = true };
        var normalItems = Enumerable.Range(0, 100).Select(i => new WarmItem("resource-" + i,
            new[] { "fixture/model-" + i }, _ => null)).ToArray();
        var plan = new StagePlan("renderer", "renderer", 1, 0,
            new Dictionary<string, double> { ["loadsAtOnce"] = 6, ["mostSeconds"] = 60 });
        var warmup = new RenderWarmup(normalItems, plan, loader);
        AddChild(warmup); warmup.SetProcess(false);
        try
        {
            for (int i = 0; i < 100; i++) warmup._Process(0);
            Expect("warm-up blocked camera cannot preload the entire catalogue", warmup.Shown == 0 && warmup.HeldFiles == 24
                && warmup.RequestedFiles == 24 && warmup.Files == 100 && warmup.QueuedFiles == 76
                && warmup.AdmittedItems == 24 && warmup.AdmittedPendingFiles == 24 && warmup.LoadingFiles == 0,
                $"the real warmup owner holds only its admitted prefix; held={warmup.HeldFiles}, requested={warmup.RequestedFiles}, deferred={warmup.QueuedFiles}");
            Expect("warm-up native owner respects six concurrent requests", loader.PeakOutstanding <= 6,
                "deterministic loader completions exercise the production admission/request/collect loop");
        }
        finally { warmup.Free(); }

        var timeoutLoader = new WarmupFixtureLoader(resource);
        var timeoutPlan = plan with { Numbers = new Dictionary<string, double> { ["loadsAtOnce"] = 6, ["mostSeconds"] = -1 } };
        var timeout = new RenderWarmup(normalItems, timeoutPlan, timeoutLoader);
        var camera = new Camera3D();
        AddChild(camera); AddChild(timeout); timeout.SetProcess(false);
        try
        {
            timeout._Process(0);
            Expect("warm-up timeout fixture admits only its initial in-flight requests", timeout.LoadingFiles == 6
                && timeout.RequestedFiles == 6 && timeout.AdmittedPendingFiles == 24, "camera remains blocked while six requests start");
            timeout.Begin(camera);
            timeout._Process(0);
            timeoutLoader.CanComplete = true;
            for (int i = 0; i < 10; i++) timeout._Process(0);
            Expect("warm-up timeout stops admission and drains only existing requests", timeout.Done && timeout.RequestedFiles == 6
                && timeout.HeldFiles == 6 && timeout.LoadingFiles == 0 && timeout.QueuedAdmittedFiles == 0 && timeout.QueuedFiles == 94
                && timeoutLoader.Requests.Count == 6 && timeout.Shown == 0,
                "post-timeout process ticks collect the bounded in-flight work without loading unseen catalogue entries");
        }
        finally { timeout.Free(); camera.Free(); }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        int retryShows = 0;
        var retryLoader = new WarmupFixtureLoader(resource);
        var retryPlan = plan with { Numbers = new Dictionary<string, double> { ["perFrame"] = 1, ["itemLookahead"] = 2, ["mostSeconds"] = 60 } };
        var retryItems = new[] { new WarmItem("retry-empty", _ => { retryShows++; return null; }) }.Concat(normalItems);
        var retryWarmup = new RenderWarmup(retryItems, retryPlan, retryLoader);
        var firstCamera = new Camera3D();
        var secondCamera = new Camera3D();
        AddChild(firstCamera); AddChild(secondCamera); AddChild(retryWarmup); retryWarmup.SetProcess(false);
        try
        {
            retryWarmup._Process(0);
            retryWarmup.Begin(firstCamera);
            retryWarmup._Process(0);
            retryWarmup._Process(0);
            retryWarmup.RetirePreview();
            retryWarmup.Begin(secondCamera);
            retryWarmup._Process(0);
            Expect("warm-up camera retry drains the old request window before replacement admission", retryWarmup.RequestedFiles == 2
                && retryWarmup.LoadingFiles == 2 && retryWarmup.AdmittedItems == 0 && retryWarmup.Shown == 0 && retryShows == 1,
                "requeued unfinished previews cannot admit another file window while the previous two requests remain active");
            retryLoader.CanComplete = true;
            retryWarmup._Process(0);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            retryWarmup._Process(0);
            Expect("warm-up camera retry reuses drained files without requesting them twice", retryWarmup.RequestedFiles == 2
                && retryLoader.Requests.Count == 2 && retryWarmup.LoadingFiles == 0 && retryShows == 2 && retryWarmup.Shown == 1,
                "the new prefix replays the unfinished item after native preview retirement and reuses retained dependency pins");
        }
        finally { retryWarmup.Free(); firstCamera.Free(); secondCamera.Free(); }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private sealed class WarmupFixtureLoader(Resource resource) : RenderWarmup.IAssetLoader
    {
        internal readonly HashSet<string> Requests = new(StringComparer.Ordinal);
        private readonly HashSet<string> _outstanding = new(StringComparer.Ordinal);
        internal bool CanComplete;
        internal int PeakOutstanding;
        public bool Request(string file)
        {
            if (!Requests.Add(file)) throw new InvalidOperationException("Warm-up fixture received a duplicate request: " + file);
            _outstanding.Add(file);
            PeakOutstanding = Math.Max(PeakOutstanding, _outstanding.Count);
            return true;
        }
        public bool Collect(string file, out Resource? loaded, out string status)
        {
            loaded = CanComplete ? resource : null;
            status = CanComplete ? "Loaded" : "InProgress";
            if (CanComplete) _outstanding.Remove(file);
            return CanComplete;
        }
    }
}
