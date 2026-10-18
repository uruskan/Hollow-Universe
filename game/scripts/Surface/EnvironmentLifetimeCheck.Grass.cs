using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    // UpdateGrass also samples ground on the main thread. Block/fault only the real worker's surface reads.
    private sealed class GrassProbeSurface : ISurface
    {
        private readonly ProbeSurface _source;
        private readonly int _mainThread = System.Environment.CurrentManagedThreadId;
        public GrassProbeSurface(ProbeSurface source) => _source = source;
        public double Radius => _source.Radius;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) => System.Environment.CurrentManagedThreadId == _mainThread
            ? 0 : _source.HeightAt(direction);
        public GroundCover Cover(Vector3D direction, in SurfaceSample sample, double slope, Span<double> biomes, Span<double> materials)
        {
            biomes.Clear(); materials.Clear();
            biomes[(int)Biome.Grassland] = 1;
            materials[(int)GroundMaterial.Grass] = 1;
            return new GroundCover(18, 0.7, 1, 0.005);
        }
    }

    private async Task CheckGrass()
    {
        Expect("grass mesh retains one constant support foot per card side", GrassField.InspectGrassFootMetadata(),
            "actual uploaded RGBAFloat CUSTOM0: 40 vertices/30 triangles; all four rows retain own/opposite untapered foot; UV2 growth unchanged");
        CheckGrassAtlas();
        var fields = new List<GrassField>();
        var surfaces = new List<ProbeSurface>();
        var monitors = new List<GrassWorkMonitor>();
        GrassField Create(ProbeSurface source)
        {
            surfaces.Add(source);
            var field = new GrassField { Name = "LifetimeGrass" + fields.Count };
            fields.Add(field);
            monitors.Add(field.WorkMonitor);
            AddChild(field);
            field.SetupLifecycleFixture(new GrassProbeSurface(source), RenderFrame.World);
            return field;
        }
        try
        {
            var blocked = new ProbeSurface(1200, block: true);
            GrassField retiring = Create(blocked);
            var camera = new Vector3D(blocked.Radius + 1.7, 0, 0);
            retiring.UpdateGrass(camera, 1.7, true);
            await Until(() => blocked.Entered.IsSet, "grass worker reaches blocking surface");
            GrassWorkMonitor retired = retiring.WorkMonitor;
            var timer = Stopwatch.StartNew();
            retiring.Stop(); retiring.Stop();
            timer.Stop();
            Expect("grass stop does not wait for blocked worker", retired.Snapshot().Pending == 1 && retiring.IsStopped,
                $"Stop returned in {timer.Elapsed.TotalMilliseconds:0.00} ms with worker still blocked");
            retiring.QueueFree();
            blocked.Continue.Set();
            await Until(() => retired.Snapshot().Pending == 0, "retired grass worker cancellation observed");
            GrassWorkSnapshot retiredState = retired.Snapshot();
            Expect("retired grass work cancels and cannot publish", retiredState.Cancelled == 1 && retiredState.Completed == 0
                && retiredState.Faulted == 0 && retiredState.Discarded == 1,
                $"cancelled={retiredState.Cancelled}, completed={retiredState.Completed}, discarded={retiredState.Discarded}");

            var stableSurface = new ProbeSurface(1200);
            GrassField stable = Create(stableSurface);
            bool secondSetupRefused = false;
            try { stable.SetupLifecycleFixture(new GrassProbeSurface(stableSurface), RenderFrame.World); }
            catch (InvalidOperationException) { secondSetupRefused = true; }
            Expect("grass setup is single use", secondSetupRefused, "second setup rejected before changing field state");
            await Until(() => { stable.UpdateGrass(camera, 1.7, true); return stable.IsIdle; }, "grass fixture becomes resident");
            GroundCoverDiagnostics original = stable.CaptureDiagnostics();
            Expect("grass fixture exercises all production layers", original.Grass.Instances > 0 && original.Litter.Instances > 0
                && original.Authored.Instances > 0, $"grass={original.Grass.Instances}, litter={original.Litter.Instances}, authored={original.Authored.Instances}");
            int step = 0;
            foreach (GroundCoverLayers layers in new[] { GroundCoverLayers.Grass, GroundCoverLayers.Litter,
                GroundCoverLayers.Authored, GroundCoverLayers.None, GroundCoverLayers.All })
            {
                stable.DiagnosticLayers = layers;
                // Cross the authored 2 m refresh threshold repeatedly, while remaining within the same lattice.
                var movedCamera = camera + new Vector3D(0, 0, ++step % 2 == 0 ? 0 : 2.25);
                stable.UpdateGrass(movedCamera, 1.7, true);
                stable.UpdateGrass(movedCamera, 1.7, true);
                GroundCoverDiagnostics state = stable.CaptureDiagnostics();
                bool matches = (state.Grass.Instances > 0) == ((layers & GroundCoverLayers.Grass) != 0)
                    && (state.Litter.Instances > 0) == ((layers & GroundCoverLayers.Litter) != 0)
                    && (state.Authored.Instances > 0) == ((layers & GroundCoverLayers.Authored) != 0);
                Expect("grass mask survives update: " + layers, matches && stable.IsIdle,
                    $"grass={state.Grass.Instances}, litter={state.Litter.Instances}, authored={state.Authored.Instances}, idle={stable.IsIdle}");
            }
            stable.Visible = false;
            GroundCoverDiagnostics hidden = stable.CaptureDiagnostics();
            Expect("parent grass hide suppresses all diagnostic submissions", hidden.Grass.Instances == 0
                && hidden.Litter.Instances == 0 && hidden.Authored.Instances == 0, "layer masks respect whole-field visibility");
            stable.Visible = true;
            stable.Stop();
            GroundCoverDiagnostics stopped = stable.CaptureDiagnostics();
            Expect("grass retirement releases resident inputs", stopped.Stopped && !stopped.Ready && stable.Tufts == 0
                && stopped.Grass.Instances == 0 && stopped.Authored.Instances == 0, "no retained field is reported ready or submitted");
            stable.QueueFree();

            var brokenSurface = new ProbeSurface(1200, fault: true);
            GrassField broken = Create(brokenSurface);
            await Until(() =>
            {
                broken.UpdateGrass(camera, 1.7, true);
                return broken.CaptureDiagnostics().RequiredContentFailed && broken.WorkMonitor.Snapshot().Pending == 0;
            }, "grass fault observed by owner");
            Expect("failed required grass is not ready", !broken.IsIdle && broken.WorkMonitor.Snapshot().Faulted == 1,
                "injected worker exception is observed; no stale-ready loading result");
            broken.QueueFree();
            await Until(() => broken.IsStopped, "grass tree exit retires field");
        }
        finally
        {
            foreach (ProbeSurface surface in surfaces) surface.Continue.Set();
            foreach (GrassField field in fields)
            {
                if (!GodotObject.IsInstanceValid(field)) continue;
                field.Stop();
                field.QueueFree();
            }
            Task drained = Task.WhenAll(monitors.Select(monitor => monitor.WhenIdle));
            await Task.WhenAny(drained, Task.Delay(TimeSpan.FromSeconds(25)));
            Expect("all grass jobs drained", drained.IsCompletedSuccessfully, "managed observation survives native node retirement");
            // A timed-out probe still releases wait handles when the bounded injected surface reads finish.
            _ = drained.ContinueWith(static (_, state) =>
            {
                foreach (ProbeSurface surface in (List<ProbeSurface>)state!) surface.Dispose();
            }, surfaces, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private void CheckGrassAtlas()
    {
        // Inspect real production painter output, not a second implementation of the blade paths or mask.
        // This establishes alpha connectivity/gaps only; root contact, mip appearance and wind lighting need renders.
        using Image atlas = FloraTextures.Atlas(201);
        byte[] pixels = atlas.GetData();
        const int size = FloraTextures.Cell;
        var opaque = new bool[size * size];
        var reached = new bool[opaque.Length];
        var queue = new int[opaque.Length];
        foreach (var cell in new[] { FloraTextures.Grass, FloraTextures.DryGrass, FloraTextures.Meadow })
        {
            Array.Clear(opaque); Array.Clear(reached);
            int covered = 0, firstRow = size, roots = 0, rootRuns = 0;
            bool previousRoot = false;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool on = pixels[((cell.Y * size + y) * FloraTextures.AtlasSize + cell.X * size + x) * 4 + 3] >= 128;
                opaque[y * size + x] = on;
                if (on) { covered++; firstRow = Math.Min(firstRow, y); }
                if (y != size - 1) continue;
                if (on) roots++;
                if (on && !previousRoot) rootRuns++;
                previousRoot = on;
            }
            int tail = 0;
            for (int x = 0; x < size; x++)
            {
                int p = (size - 1) * size + x;
                if (opaque[p]) { reached[p] = true; queue[tail++] = p; }
            }
            for (int head = 0; head < tail; head++)
            {
                int p = queue[head], x = p % size, y = p / size;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= size || yy >= size) continue;
                    int q = yy * size + xx;
                    if (!opaque[q] || reached[q]) continue;
                    reached[q] = true; queue[tail++] = q;
                }
            }
            double coverage = covered / (double)opaque.Length, rooted = tail / (double)Math.Max(1, covered);
            Expect($"grass painted silhouette has separated connected blades {cell.X},{cell.Y}",
                rootRuns >= 10 && roots > size * 0.2 && roots < size * 0.8 && coverage > 0.05 && coverage < 0.7
                    && firstRow < size * 0.25 && rooted > 0.99,
                $"actual RGBA8: roots={rootRuns} runs/{roots} pixels, area={coverage:P1}, root-connected={rooted:P2}, tallest={size - firstRow}px; no second silhouette mask");
        }
    }
}
