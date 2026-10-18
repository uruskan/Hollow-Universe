using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    private async Task CheckSpatialGrass()
    {
        var fields = new List<GrassField>();
        var surfaces = new List<ProbeSurface>();
        var monitors = new List<GrassWorkMonitor>();
        GrassField Create(ProbeSurface? source = null)
        {
            source ??= new ProbeSurface(1200);
            surfaces.Add(source);
            var field = new GrassField { Name = "SpatialLifetimeGrass" + fields.Count };
            fields.Add(field); monitors.Add(field.WorkMonitor); AddChild(field);
            field.SetupSpatialLifecycleFixture(new GrassProbeSurface(source), RenderFrame.World);
            return field;
        }
        try
        {
            GrassField stable = Create();
            var coverage = stable.InspectGrassCoverageAdmission();
            Expect("grass CPU compaction bounds the combined habitat/quality gate", coverage.Conservative && coverage.Nested
                && coverage.Endpoints && coverage.Retained > 0 && coverage.Retained < coverage.Candidates,
                $"{coverage.Probes} bilinear/patch probes, {coverage.Retained}/{coverage.Candidates} actual compacted cells; nested quality, empty/full endpoints; GPU parity remains a render check");
            var cells = stable.InspectSpatialLifecycleCells();
            Expect("spatial cells preserve identity across 16/32 tiles and face clipping", cells.Stable && cells.Owned && cells.Compact,
                $"stable={cells.Stable}, half-open ownership={cells.Owned}, compact={cells.Compact}, instances={cells.Instances}");
            stable.QueueSpatialLifecycleGeneration();
            stable.AdvanceSpatialLifecycleUpload(); // immutable texture pair only; tile upload remains pending
            SpatialGrassDiagnostics staged = stable.CaptureSpatialDiagnostics();
            Expect("spatial readiness waits for all required tile uploads", !stable.IsIdle && staged.ResidentGeneration == 0
                && staged.PendingTileUploads > 0 && stable.SpatialLifecycleDirectBatches == 0,
                $"pending={staged.PendingTileUploads}, resident={staged.ResidentGeneration}");
            await Until(() => { stable.AdvanceSpatialLifecycleUpload(); return stable.IsIdle; }, "spatial fixture publishes complete generation");
            var bounds = stable.InspectSpatialLifecycleBounds();
            Expect("spatial native bounds contain support and deformation envelope", bounds.Finite && bounds.Enclosed && bounds.SmallMeshes
                && bounds.TexturesBound && bounds.Batches > 0,
                $"finite={bounds.Finite}, enclosed={bounds.Enclosed}, local meshes={bounds.SmallMeshes}, immutable bindings={bounds.TexturesBound}; no GPU culling claim");
            long first = stable.CaptureSpatialDiagnostics().ResidentGeneration;
            GodotObject[] oldHandles = stable.SpatialLifecycleNativeResources();
            stable.QueueSpatialLifecycleGeneration(revised: true);
            stable.AdvanceSpatialLifecycleUpload();
            GodotObject[] stagedHandles = stable.SpatialLifecycleNativeResources(staging: true);
            Expect("staged spatial generation cannot mutate old lattice resources", stable.CaptureSpatialDiagnostics().ResidentGeneration == first
                && oldHandles.All(GodotObject.IsInstanceValid) && stagedHandles.Length > 0
                && !oldHandles.Select(handle => handle.GetInstanceId()).Intersect(stagedHandles.Select(handle => handle.GetInstanceId())).Any()
                && stable.InspectSpatialLifecycleBounds().TexturesBound,
                "old batches still reference their original live textures while independent replacement textures await tile publication");
            stable.InjectSpatialLifecycleFault(GrassField.SpatialFixtureFault.DuringRetirement);
            await Until(() => { stable.AdvanceSpatialLifecycleUpload(); return stable.IsIdle; }, "spatial replacement survives old retirement fault");
            Expect("spatial retirement failure cannot dispose committed replacement", stable.CaptureSpatialDiagnostics().ResidentGeneration > first
                && stable.CaptureSpatialDiagnostics().RetirementError != null && stable.SpatialLifecycleNativeResources().All(GodotObject.IsInstanceValid)
                && oldHandles.All(handle => !GodotObject.IsInstanceValid(handle)),
                "injected old-resource retirement failure is reported after all old resources retire; new publication remains live");

            foreach (GrassField.SpatialFixtureFault fault in new[] { GrassField.SpatialFixtureFault.AfterGrassBatch,
                GrassField.SpatialFixtureFault.BeforePublish })
            {
                GrassField failed = Create();
                failed.QueueSpatialLifecycleGeneration();
                await Until(() => { failed.AdvanceSpatialLifecycleUpload(); return failed.IsIdle; }, "failure fixture initial generation");
                long previous = failed.CaptureSpatialDiagnostics().ResidentGeneration;
                GodotObject[] retained = failed.SpatialLifecycleNativeResources();
                failed.QueueSpatialLifecycleGeneration(revised: true);
                failed.AdvanceSpatialLifecycleUpload();
                GodotObject[] failedTextures = failed.SpatialLifecycleNativeResources(staging: true);
                failed.InjectSpatialLifecycleFault(fault);
                await Until(() =>
                {
                    failed.AdvanceSpatialLifecycleUpload();
                    return failed.CaptureSpatialDiagnostics().RequiredContentFailed;
                }, "spatial injected upload fault is observed");
                SpatialGrassDiagnostics result = failed.CaptureSpatialDiagnostics();
                Expect("spatial upload failure rolls back only staging: " + fault, !failed.IsIdle && result.ResidentGeneration == previous
                    && result.StagedTiles == 0 && retained.All(GodotObject.IsInstanceValid)
                    && failedTextures.All(handle => !GodotObject.IsInstanceValid(handle))
                    && failed.SpatialLifecycleDirectBatches == result.ResidentBatches,
                    "required failed content is not ready; previous generation remains live and partial replacement nodes/textures are released");
                failed.Stop();
            }

            stable.QueueSpatialLifecycleGeneration();
            stable.AdvanceSpatialLifecycleUpload();
            GodotObject[] retiring = stable.SpatialLifecycleNativeResources().Concat(stable.SpatialLifecycleNativeResources(staging: true)).ToArray();
            stable.Stop(); stable.Stop();
            Expect("spatial stop releases resident and partially uploaded generations", stable.IsStopped
                && stable.CaptureSpatialDiagnostics().ResidentTiles == 0 && stable.CaptureSpatialDiagnostics().StagedTiles == 0
                && stable.SpatialLifecycleDirectBatches == 0 && retiring.All(handle => !GodotObject.IsInstanceValid(handle)),
                "both native generations retire synchronously and repeated Stop is safe");

            var blocked = new ProbeSurface(1200, block: true);
            GrassField pending = Create(blocked);
            pending.UpdateGrass(new Vector3D(1201.7, 0, 0), 1.7, true);
            await Until(() => blocked.Entered.IsSet, "spatial real worker blocks in source");
            pending.Stop();
            Task drained = pending.WorkMonitor.WhenIdle;
            Expect("spatial stop retains actual blocked-worker barrier", !drained.IsCompleted && pending.IsStopped,
                "native retirement does not pretend to cancel an arbitrary in-flight source callback");
            blocked.Continue.Set();
            await Until(() => drained.IsCompleted, "spatial cancelled worker drains");
            Expect("retired spatial worker cannot publish a generation", drained.IsCompletedSuccessfully
                && pending.WorkMonitor.Snapshot().Cancelled == 1 && pending.CaptureSpatialDiagnostics().ResidentTiles == 0,
                "production worker cancellation is observed and no native upload follows retirement");
        }
        finally
        {
            foreach (ProbeSurface surface in surfaces) surface.Continue.Set();
            foreach (GrassField field in fields)
                if (GodotObject.IsInstanceValid(field)) { field.Stop(); field.QueueFree(); }
            Task all = Task.WhenAll(monitors.Select(monitor => monitor.WhenIdle));
            await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(25)));
            Expect("all spatial grass fixture workers drained", all.IsCompletedSuccessfully, "all compact-data source readers have retired");
            _ = all.ContinueWith(static (_, state) =>
            {
                foreach (ProbeSurface surface in (List<ProbeSurface>)state!) surface.Dispose();
            }, surfaces, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
