using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    private sealed class LeasedProbeSurface : ISurface, IDisposable
    {
        private readonly ISurface _source;
        public int Disposals;
        public LeasedProbeSurface(ISurface source) => _source = source;
        public double Radius => _source.Radius;
        public double SeaRadius => _source.SeaRadius;
        public double HeightAt(Vector3D direction) => _source.HeightAt(direction);
        public GroundCover Cover(Vector3D direction, in SurfaceSample sample, double slope, Span<double> biomes, Span<double> materials) =>
            _source.Cover(direction, sample, slope, biomes, materials);
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }

    private async Task CheckSurfaceOwnership()
    {
        var source = new ProbeSurface(1200, block: true);
        var oldSurface = new LeasedProbeSurface(new GrassProbeSurface(source));
        var nextSource = new ProbeSurface(1100);
        var nextSurface = new LeasedProbeSurface(new GrassProbeSurface(nextSource));
        var retired = new List<Task>();
        var views = new List<SurfaceView>();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new WorldStore(new WorldStoreOptions(), (descriptor, _) =>
            descriptor.Radius == 1200 ? oldSurface : nextSurface, static _ => new WorldMemoryEstimate(1, 1));
        PlanetEnvironmentDescriptor Descriptor(double radius) =>
            EnvironmentDescriptors.Resolve(StartingWorld.System.Value, StartingWorld.BodyPath, radius)
            ?? throw new InvalidOperationException("Lifetime probe could not resolve Earth's metadata.");
        SurfaceView View()
        {
            var view = new SurfaceView { Name = "LifetimeSurface" + views.Count };
            views.Add(view); AddChild(view);
            return view;
        }
        long firstOwner = 0, secondOwner = 0;
        try
        {
            WorldLease first = await store.AcquireAsync(Descriptor(1200));
            var grass = new GrassField { Name = "QualityRetiredGrass" };
            grass.SetupLifecycleFixture(first.Surface, RenderFrame.World);
            SurfaceView view = View();
            view.SetupLifecycleFixture(first, grass);
            grass.UpdateGrass(new Vector3D(1201.7, 0, 0), 1.7, true);
            await Until(() => source.Entered.IsSet, "surface view's old grass query blocks");
            view.RetireLifecycleGrass(); // The same retirement boundary called by quality changes.
            Task firstStopped = view.WorkersStopped;
            retired.Add(firstStopped);
            WorldLease next = await store.AcquireAsync(Descriptor(1100));
            view.SetupLifecycleFixture(next);
            Task nextStopped = view.WorkersStopped;
            retired.Add(nextStopped);
            view.Stop(); view.Stop(); view.QueueFree();
            Expect("surface retirement includes quality work and previous opening", !firstStopped.IsCompleted
                && !nextStopped.IsCompleted && view.Surface == null && store.Diagnostics.ActiveLeases == 2,
                "retired grass still queries the old world; both opening barriers and leases remain held");
            source.Continue.Set();
            await Until(() => firstStopped.IsCompleted && nextStopped.IsCompleted, "surface opening barriers drain");
            Expect("surface retirement releases transferred leases", firstStopped.IsCompletedSuccessfully
                && nextStopped.IsCompletedSuccessfully && store.Diagnostics.ActiveLeases == 0,
                "world pins outlive the actual blocked grass worker and are released after its observer finishes");

            WorldLease faulty = await store.AcquireAsync(Descriptor(1100));
            SurfaceView failed = View();
            failed.SetupLifecycleFixture(faulty, pending: gate.Task);
            Task failedStopped = failed.WorkersStopped;
            retired.Add(failedStopped);
            failed.Stop(); failed.QueueFree();
            Expect("surface fault barrier remains pinned while pending", !failedStopped.IsCompleted && store.Diagnostics.ActiveLeases == 1,
                "the production retirement holder accepts a producer completion barrier");
            gate.TrySetException(new InvalidOperationException("Expected surface retirement probe failure."));
            await Until(() => failedStopped.IsCompleted, "faulted surface barrier releases lease");
            _ = failedStopped.Exception;
            Expect("surface fault barrier still releases lease", failedStopped.IsFaulted && store.Diagnostics.ActiveLeases == 0,
                "producer failure is observed and propagated only after the transferred lease is disposed");

            // An unready view has no camera. This forces a real failure before Begin takes ownership.
            var unopened = new SurfaceView();
            try
            {
                WorldLease rejected = await store.AcquireAsync(Descriptor(1100));
                bool failedBeforeBegin = false;
                try { unopened.OpenWorld(null!, null!, rejected.Surface, new SurfacePlants(rejected.Surface), 0, 0, 1, 9.8f, rejected); }
                catch (NullReferenceException) { failedBeforeBegin = true; }
                Expect("opening failure before Begin consumes transferred lease", failedBeforeBegin && store.Diagnostics.ActiveLeases == 0,
                    "the caller's pin is released even before any world workers or atmosphere are created");
            }
            finally { unopened.Free(); }

            firstOwner = PlanetGlobals.Claim(new RenderFrame(new Vector3D(1200, 0, 0)), 1200);
            secondOwner = PlanetGlobals.Claim(RenderFrame.World, 1100);
            Vector3 centre = PlanetGlobals.Centre;
            Basis basis = PlanetGlobals.RenderToBody;
            double k = PlanetGlobals.K;
            PlanetGlobals.Clear(firstOwner);
            PlanetGlobals.Set(firstOwner, new RenderFrame(new Vector3D(0, 1200, 0)), 1200);
            Expect("old planet owner cannot clear or overwrite successor", PlanetGlobals.IsOwner(secondOwner)
                && PlanetGlobals.Radius == 1100 && PlanetGlobals.Centre == centre && PlanetGlobals.K == k
                && PlanetGlobals.RenderToBody == basis,
                "shader global ownership uses a numeric stamp with no retained Node");
            PlanetGlobals.Clear(secondOwner);
            Expect("current planet owner clears globals", !PlanetGlobals.IsOwner(secondOwner)
                && PlanetGlobals.Radius == PlanetGlobals.NoPlanetRadius, "current retirement restores the no-planet frame");
            await store.BeginRetirement();
            Expect("surface fixture worlds are disposed exactly once", oldSurface.Disposals == 1 && nextSurface.Disposals == 1,
                "store retirement disposes resident fixture worlds after all pins have been released");
        }
        finally
        {
            source.Continue.Set(); nextSource.Continue.Set(); gate.TrySetResult(true);
            foreach (SurfaceView view in views)
                if (GodotObject.IsInstanceValid(view)) { view.Stop(); retired.Add(view.WorkersStopped); view.QueueFree(); }
            Task all = Task.WhenAll(retired.Append(store.BeginRetirement()));
            await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(25)));
            _ = all.Exception;
            Expect("all surface ownership work drained", all.IsCompleted && store.Retirement.IsCompletedSuccessfully,
                "the intentionally faulted producer is observed; all world pins and fixture worlds drain");
            _ = all.ContinueWith(static (_, state) =>
            {
                var sources = ((ProbeSurface First, ProbeSurface Next))state!;
                sources.First.Dispose(); sources.Next.Dispose();
            }, (source, nextSource), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            PlanetGlobals.Clear(firstOwner); PlanetGlobals.Clear(secondOwner);
        }
    }
}
