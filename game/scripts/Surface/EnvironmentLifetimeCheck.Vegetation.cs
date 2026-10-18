using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    private sealed class ProbePlants : IPlantSource, IDisposable
    {
        public int Level => 1;
        public readonly ManualResetEventSlim Continue = new(false);
        public int Entered;
        public bool Block, Fault;
        public IReadOnlyList<ScatterItem> Tile(TileKey tile, bool treesOnly)
        {
            Interlocked.Increment(ref Entered);
            if (Block && !Continue.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Scatter probe was never released.");
            if (Fault) throw new InvalidOperationException("Expected scatter lifetime-probe failure.");
            return new[] { new ScatterItem(0, ScatterKind.Fir, 0, Vector3D.UnitX, 0, 0, 1) };
        }
        public ulong Id(TileKey tile, in ScatterItem item) => tile.Packed;
        public void Dispose() => Continue.Dispose();
    }

    private async Task CheckVegetation()
    {
        var fields = new List<Vegetation>();
        var sources = new List<ProbePlants>();
        var gates = new List<Vegetation.LifecycleGate>();
        var retired = new List<Task>();
        var baker = new ImpostorBaker { Name = "LifetimeBaker" };
        AddChild(baker);
        Vegetation Create(ProbePlants source)
        {
            sources.Add(source);
            var field = new Vegetation { Name = "LifetimeVegetation" + fields.Count };
            fields.Add(field);
            AddChild(field);
            field.SetupLifecycleFixture(source);
            retired.Add(field.WorkersStopped);
            return field;
        }
        try
        {
            var source = new ProbePlants { Block = true };
            Vegetation stopping = Create(source);
            stopping.RequestLifecycleScatter(new TileKey(0, 1, 0, 0));
            stopping.RequestLifecycleScatter(new TileKey(0, 1, 1, 0));
            stopping.RequestLifecycleScatter(new TileKey(0, 1, 0, 1));
            var gate = new Vegetation.LifecycleGate(); gates.Add(gate);
            stopping.QueueLifecycleLayouts(gate);
            await Until(() => Volatile.Read(ref source.Entered) == 2 && gate.NearEntered.IsSet && gate.BlockEntered.IsSet,
                "scatter and both vegetation layout jobs are blocked");
            stopping.Stop(); stopping.Stop();
            Expect("vegetation stop does not wait for blocked work", stopping.IsStopped && !stopping.WorkersStopped.IsCompleted,
                "stable retirement handle remains pending while scatter, near and block workers are blocked");
            stopping.QueueFree();
            source.Continue.Set(); gate.Continue.Set();
            await Until(() => stopping.WorkersStopped.IsCompleted, "vegetation retirement drains all query families");
            var stopped = stopping.LifecycleCounts;
            Expect("vegetation retirement cancels queued and layout work", stopped.Scatter == 0 && stopped.Tasks == 0
                && stopped.Cancelled >= 3 && stopped.Discarded == 2 && stopping.FailedJobs == 0,
                $"scatter={stopped.Scatter}, tasks={stopped.Tasks}, cancelled={stopped.Cancelled}, discarded={stopped.Discarded}");

            Vegetation stable = Create(new ProbePlants());
            stable.RequestLifecycleScatter(new TileKey(0, 1, 0, 0));
            await Until(() => stable.CollectLifecycleScatter() == 1, "production scatter result is collected");
            stable.QueueLifecycleLayouts();
            await Until(() => stable.LifecycleLayoutsComplete, "production near and block builders complete");
            Expect("vegetation fixture executes real layout builders", stable.LifecycleOutputsValid,
                "one mesh instance and one 16-float impostor transform produced by production layout code");
            bool repeatRefused = false;
            try { stable.SetupLifecycleFixture(sources[^1]); }
            catch (InvalidOperationException) { repeatRefused = true; }
            Expect("vegetation setup is single use", repeatRefused, "second setup rejected before replacing the source");
            stable.QueueFree();
            await Until(() => stable.WorkersStopped.IsCompleted, "vegetation exit retires workers");
            Expect("vegetation tree exit closes admission", stable.IsStopped && !stable.IsIdle, "removed fields cannot become ready again");

            var brokenSource = new ProbePlants { Block = true, Fault = true };
            Vegetation broken = Create(brokenSource);
            var brokenGate = new Vegetation.LifecycleGate(); gates.Add(brokenGate);
            broken.RequestLifecycleScatter(new TileKey(0, 1, 0, 0));
            broken.QueueLifecycleLayouts(brokenGate, fault: true);
            await Until(() => Volatile.Read(ref brokenSource.Entered) == 1 && brokenGate.NearEntered.IsSet && brokenGate.BlockEntered.IsSet,
                "late vegetation faults are blocked before retirement");
            broken.Stop(); broken.QueueFree();
            brokenSource.Continue.Set(); brokenGate.Continue.Set();
            await Until(() => broken.WorkersStopped.IsCompleted, "late vegetation faults are observed");
            Expect("retired vegetation observes every query failure", broken.FailedJobs == 3 && !broken.IsIdle,
                $"observed scatter/near/block faults={broken.FailedJobs}");

            Task<ImpostorBaker.Result> bake = baker.BakeLifecycleFixture();
            baker.Stop(); baker.QueueFree();
            await Until(() => baker.Stopped.IsCompleted && bake.IsCompleted, "impostor frame wait cancels without a draw");
            Expect("impostor bake cancellation releases render wait", bake.IsCanceled,
                "actual bake enters its render-frame wait, then retirement cancels before readback or atlas processing");
        }
        finally
        {
            foreach (ProbePlants source in sources) source.Continue.Set();
            foreach (Vegetation.LifecycleGate gate in gates) gate.Continue.Set();
            foreach (Vegetation field in fields)
                if (GodotObject.IsInstanceValid(field)) { field.Stop(); field.QueueFree(); }
            if (GodotObject.IsInstanceValid(baker)) { baker.Stop(); baker.QueueFree(); }
            Task drained = Task.WhenAll(retired.Append(baker.Stopped));
            await Task.WhenAny(drained, Task.Delay(TimeSpan.FromSeconds(25)));
            Expect("all vegetation ownership work drained", drained.IsCompletedSuccessfully,
                "retirement covers scatter threads, layout tasks and cancelled native bake cleanup");
            _ = drained.ContinueWith(static (_, state) =>
            {
                var cleanup = ((List<ProbePlants> Sources, List<Vegetation.LifecycleGate> Gates))state!;
                foreach (ProbePlants source in cleanup.Sources) source.Dispose();
                foreach (Vegetation.LifecycleGate gate in cleanup.Gates) gate.Dispose();
            }, (sources, gates), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
