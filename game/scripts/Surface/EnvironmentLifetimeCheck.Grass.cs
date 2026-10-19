using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
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
        CheckGrassCatalogue();
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
            Expect("grass stop does not wait for blocked worker", retired.Snapshot().Pending >= 1 && retiring.IsStopped,
                $"Stop returned in {timer.Elapsed.TotalMilliseconds:0.00} ms with {retired.Snapshot().Pending} tile builds still blocked");
            retiring.QueueFree();
            blocked.Continue.Set();
            await Until(() => retired.Snapshot().Pending == 0, "retired grass worker cancellation observed");
            GrassWorkSnapshot retiredState = retired.Snapshot();
            Expect("retired grass work cancels and cannot publish", retiredState.Started > 0 && retiredState.Cancelled == retiredState.Started
                && retiredState.Completed == 0 && retiredState.Faulted == 0 && retiredState.Discarded == retiredState.Started,
                $"started={retiredState.Started}, cancelled={retiredState.Cancelled}, completed={retiredState.Completed}, discarded={retiredState.Discarded}");

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
                // A move can bring a tile into reach: the field settles (builds and uploads it) before the mask is read.
                await Until(() => { stable.UpdateGrass(movedCamera, 1.7, true); return stable.IsIdle; }, "grass settles after a move: " + layers);
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
            Expect("failed required grass is not ready", !broken.IsIdle && broken.WorkMonitor.Snapshot().Faulted >= 1,
                "injected worker exceptions are observed; no further tiles start and no stale-ready loading result");
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

    // The clumps as Godot imported them (art/grass/build_grass.py → grass.glb): every level the catalogue names is there
    // with its cards' attributes (height fraction and rank in UV2, no vertex colour; tangents for the folds),
    // under about 300 triangles near by, with the vertex and triangle counts the catalogue records.
    private void CheckGrassCatalogue()
    {
        using JsonDocument catalogue = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/surface/grass/grass.json"));
        Node3D scene = GD.Load<PackedScene>(catalogue.RootElement.GetProperty("model").GetString()!).Instantiate<Node3D>();
        try
        {
            var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            void Collect(Node node)
            {
                if (node is MeshInstance3D m && m.Mesh != null) meshes[m.Name] = m.Mesh;
                foreach (Node child in node.GetChildren()) Collect(child);
            }
            Collect(scene);
            foreach (JsonElement species in catalogue.RootElement.GetProperty("species").EnumerateArray())
            {
                var levels = species.GetProperty("lods").EnumerateArray().ToList();
                if (species.GetProperty("far").ValueKind == JsonValueKind.Object) levels.Add(species.GetProperty("far"));
                foreach (JsonElement level in levels)
                {
                    string node = level.GetProperty("node").GetString()!;
                    bool present = meshes.TryGetValue(node, out Mesh? mesh);
                    using var arrays = present ? mesh!.SurfaceGetArrays(0) : null;
                    Vector3[] vertices = arrays?[(int)Mesh.ArrayType.Vertex].AsVector3Array() ?? Array.Empty<Vector3>();
                    Vector2[] data = arrays?[(int)Mesh.ArrayType.TexUV2].AsVector2Array() ?? Array.Empty<Vector2>();
                    float[] tangents = arrays?[(int)Mesh.ArrayType.Tangent].AsFloat32Array() ?? Array.Empty<float>();
                    int[] indices = arrays?[(int)Mesh.ArrayType.Index].AsInt32Array() ?? Array.Empty<int>();
                    int triangles = indices.Length / 3;
                    bool valid = present && triangles == level.GetProperty("triangles").GetInt32() && triangles <= 300
                        && vertices.Length == level.GetProperty("vertices").GetInt32() && data.Length == vertices.Length
                        && tangents.Length == vertices.Length * 4 && vertices.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z))
                        && data.All(c => c.X >= 0 && c.X <= 1 && c.Y >= 0 && c.Y <= 1) && vertices.Min(v => v.Y) > -0.01f;
                    Expect("grass level imported: " + node, valid,
                        $"{vertices.Length} vertices, {triangles} triangles, {data.Length} UV2, {tangents.Length / 4} tangents");
                }
            }
        }
        finally { scene.Free(); }
    }
}
