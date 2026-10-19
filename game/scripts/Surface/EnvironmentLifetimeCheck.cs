using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>Opt-in bounded native regression probe. No save, flora assets or gameplay server are loaded.</summary>
public partial class EnvironmentLifetimeCheck : Node
{
    public string OutputPath { get; set; } = "";
    private readonly List<Result> _results = new();
    private sealed record Result(string Name, bool Passed, string Detail);

    // Pausing the surface, rather than waiting an arbitrary number of frames, forces reset to overlap real work.
    private sealed class ProbeSurface : ISurface, IDisposable
    {
        public double Radius { get; }
        public double SeaRadius => 0;
        public readonly ManualResetEventSlim Entered = new(false);
        public readonly ManualResetEventSlim Continue = new(false);
        public volatile bool Block, Fault;
        public ProbeSurface(double radius, bool block = false, bool fault = false)
        { Radius = radius; Block = block; Fault = fault; }
        public double HeightAt(Vector3D direction)
        {
            Entered.Set();
            if (Block && !Continue.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Probe surface was never released.");
            if (Fault) throw new InvalidOperationException("Expected lifetime-probe terrain failure.");
            return 0;
        }
        public void Dispose() { Entered.Dispose(); Continue.Dispose(); }
    }

    public override async void _Ready()
    {
        string[] arguments = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(arguments, "--environment-formation-visual-only") >= 0)
        {
            try { await CheckFormationVisual(); }
            catch (Exception error) { _results.Add(new Result("formation visual execution", false, error.ToString())); }
            WriteReport("native-formation-visual");
            return;
        }
        if (Array.IndexOf(arguments, "--environment-terrain-appearance-only") >= 0)
        {
            try { await CheckTerrainAppearance(); }
            catch (Exception error) { _results.Add(new Result("terrain appearance execution", false, error.ToString())); }
            WriteReport("native-terrain-appearance");
            return;
        }
        int appearancePackage = Array.IndexOf(arguments, "--environment-flora-package-only");
        if (appearancePackage >= 0)
        {
            try
            {
                if (appearancePackage + 2 >= arguments.Length)
                    throw new ArgumentException("Actual appearance probe needs an external approval record and its independent SHA-256 pin.");
                await CheckNativeFloraAppearance(arguments[appearancePackage + 1], arguments[appearancePackage + 2]);
            }
            catch (Exception error) { _results.Add(new Result("actual flora appearance execution", false, error.ToString())); }
            WriteReport("native-flora-appearance-package");
            return;
        }
        if (Array.IndexOf(arguments, "--environment-ground-normals-only") >= 0)
        {
            try { await CheckGroundNormalFrames(); }
            catch (Exception error) { _results.Add(new Result("ground normal frames execution", false, error.ToString())); }
            WriteReport("native-ground-normals");
            return;
        }
        int lossless = Array.IndexOf(arguments, "--environment-cover-lossless-only");
        if (lossless >= 0)
        {
            try
            {
                if (lossless + 3 >= arguments.Length)
                    throw new ArgumentException("Lossless probe needs its migration receipt path and independently pinned migration and parent SHA-256 values.");
                await CheckCoverLossless(arguments[lossless + 1], arguments[lossless + 2], arguments[lossless + 3]);
            }
            catch (Exception error) { _results.Add(new Result("cover lossless execution", false, error.ToString())); }
            WriteReport("native-cover-lossless");
            return;
        }
        if (Array.IndexOf(arguments, "--environment-flora-appearance-only") >= 0)
        {
            try { await CheckFloraAppearanceSession(); }
            catch (Exception error) { _results.Add(new Result("flora appearance execution", false, error.ToString())); }
            WriteReport("native-flora-appearance");
            return;
        }
        if (Array.IndexOf(arguments, "--environment-formation-view-only") >= 0)
        {
            try { await CheckFormationView(); }
            catch (Exception error) { _results.Add(new Result("formation view execution", false, error.ToString())); }
            WriteReport("native-formation-view");
            return;
        }
        int compressionRender = Array.IndexOf(arguments, "--environment-cover-compression-render-only");
        if (compressionRender >= 0)
        {
            try
            {
                if (compressionRender + 2 >= arguments.Length) throw new ArgumentException("Compression render probe needs its baseline receipt path and independently pinned SHA-256.");
                await CheckCoverCompressionRender(arguments[compressionRender + 1], arguments[compressionRender + 2]);
            }
            catch (Exception error) { _results.Add(new Result("cover compression render execution", false, error.ToString())); }
            WriteReport("native-cover-compression-render");
            return;
        }
        int compression = Array.IndexOf(arguments, "--environment-cover-compression-only");
        if (compression >= 0)
        {
            try
            {
                if (compression + 2 >= arguments.Length) throw new ArgumentException("Compression probe needs its baseline receipt path and independently pinned SHA-256.");
                await CheckCoverCompression(arguments[compression + 1], arguments[compression + 2]);
            }
            catch (Exception error) { _results.Add(new Result("cover compression execution", false, error.ToString())); }
            WriteReport("native-cover-compression");
            return;
        }
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--environment-terrain-cohorts-only") >= 0)
        {
            try { await CheckTerrainCohorts(); }
            catch (Exception error) { _results.Add(new Result("terrain cohort execution", false, error.ToString())); }
            WriteReport("native-terrain-cohorts");
            return;
        }
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--environment-authored-cover-render-only") >= 0)
        {
            try { await CheckAuthoredCoverRender(); }
            catch (Exception error) { _results.Add(new Result("authored render execution", false, error.ToString())); }
            WriteReport("native-authored-cover-render");
            return;
        }
        var terrain = new TerrainLod { Name = "LifetimeTerrain" };
        AddChild(terrain);
        var retiredTasks = new List<Task>();
        var surfaces = new List<ProbeSurface>();
        using var shader = new Shader { Code = "shader_type spatial; void fragment() { ALBEDO = vec3(0.3, 0.4, 0.2); }" };
        using var material = new ShaderMaterial { Shader = shader };
        try
        {
            for (int pass = 0; pass < 3; pass++)
            {
                var old = new ProbeSurface(300 + pass, block: true);
                surfaces.Add(old);
                terrain.Setup(old, RenderFrame.World, material, null);
                terrain.UpdateLod(new Vector3D(100_000, 0, 0));
                await Until(() => old.Entered.IsSet, "old surface build starts");
                Task oldStopped = terrain.WorkersStopped;
                retiredTasks.Add(oldStopped);

                var replacement = new ProbeSurface(900 + pass);
                surfaces.Add(replacement);
                var timer = Stopwatch.StartNew();
                terrain.Setup(replacement, RenderFrame.World, material, null);
                timer.Stop();
                Expect($"reset {pass} does not wait for blocked jobs", !oldStopped.IsCompleted,
                    $"old work remains blocked; Setup returned in {timer.Elapsed.TotalMilliseconds:0.00} ms");
                Expect($"reset {pass} clears old drawing", terrain.DrawnTiles == 0, "no previous grid remains resident");
                old.Continue.Set();
                await Until(() => oldStopped.IsCompleted, "retired workers stop");
                await Until(() => { terrain.UpdateLod(new Vector3D(100_000, 0, 0)); return terrain.IsIdle; }, "replacement terrain settles");
                var state = terrain.CaptureDiagnostics();
                Expect($"reset {pass} publishes only replacement", state.Drawn == CubeSphere.FaceCount
                    && state.MinimumRadius == replacement.Radius && state.MaximumRadius == replacement.Radius,
                    $"drawn={state.Drawn}, radius={state.MinimumRadius}..{state.MaximumRadius}, pending={state.Pending}");
                Task replacementStopped = terrain.WorkersStopped;
                retiredTasks.Add(replacementStopped);
                terrain.Stop();
                terrain.Stop();
                await Until(() => replacementStopped.IsCompleted, "repeated stop drains workers");
                Expect($"stop {pass} drains queued and completed data", terrain.Pending == 0 && terrain.DrawnTiles == 0,
                    $"pending={terrain.Pending}, drawn={terrain.DrawnTiles}");
            }

            var pruned = new ProbeSurface(700);
            surfaces.Add(pruned);
            terrain.Setup(pruned, RenderFrame.World, material, null);
            await Until(() => { terrain.UpdateLod(new Vector3D(100_000, 0, 0)); return terrain.IsIdle; }, "coarse terrain settles");
            pruned.Entered.Reset();
            pruned.Block = true;
            pruned.Fault = true;
            terrain.UpdateLod(new Vector3D(702, 0, 0));
            await Until(() => pruned.Entered.IsSet, "child terrain build starts");
            terrain.UpdateLod(new Vector3D(100_000, 0, 0)); // prune unfinished children
            pruned.Continue.Set();
            await Until(() => { terrain.UpdateLod(new Vector3D(100_000, 0, 0)); return terrain.Pending == 0; }, "pruned faults retire");
            Expect("pruned child failures do not poison current readiness", terrain.IsIdle && terrain.FailedBuilds == 0 && terrain.DrawnTiles == 6,
                "cancelled child failures are historical diagnostics, not missing required terrain");
            retiredTasks.Add(terrain.WorkersStopped);

            var broken = new ProbeSurface(500, fault: true);
            surfaces.Add(broken);
            terrain.Setup(broken, RenderFrame.World, material, null);
            terrain.UpdateLod(new Vector3D(100_000, 0, 0));
            await Until(() => { terrain.UpdateLod(new Vector3D(100_000, 0, 0)); return terrain.Pending == 0; }, "failed work settles");
            Expect("failed required terrain is not ready", terrain.FailedBuilds > 0 && !terrain.IsIdle,
                $"failed={terrain.FailedBuilds}, idle={terrain.IsIdle}; injected failures are expected");
            Task exit = terrain.WorkersStopped;
            retiredTasks.Add(exit);
            terrain.QueueFree();
            await Until(() => exit.IsCompleted, "tree exit stops workers");
            await CheckGrass();
            await CheckVegetation();
            await CheckSurfaceOwnership();
            await CheckClientViews();
            await CheckWarmupPreviews();
            CheckFloraPixels();
            CheckAuthoredCoverRoots();
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--environment-fern-import-check") >= 0)
                CheckAuthoredFernRootImports();
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--environment-authored-cover-render-check") >= 0)
                await CheckAuthoredCoverRender();
            await CheckTerrainContact();
            // Dummy/headless rendering does not reflect ImageTexture.Update in GPU readbacks. Keep this
            // native texture oracle explicit; the rendered runner executes it without weakening its assertions.
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--environment-contact-atlas-render-check") >= 0)
                await CheckContactAtlas();
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--environment-impostor-render-check") >= 0)
            {
                await CheckImpostorOwnership();
                await CheckImpostorRows();
            }
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--environment-terrain-contact-render-check") >= 0)
                await CheckTerrainContactGpu();
        }
        catch (Exception error) { _results.Add(new Result("probe execution", false, error.ToString())); }
        finally
        {
            foreach (ProbeSurface surface in surfaces) surface.Continue.Set();
            if (GodotObject.IsInstanceValid(terrain))
            {
                retiredTasks.Add(terrain.WorkersStopped);
                terrain.Stop();
            }
            Task drained = Task.WhenAll(retiredTasks);
            await Task.WhenAny(drained, Task.Delay(TimeSpan.FromSeconds(25)));
            Expect("all retired sessions drained", drained.IsCompletedSuccessfully, "all captured worker completion tasks");
            if (drained.IsCompleted) foreach (ProbeSurface surface in surfaces) surface.Dispose();
        }
        WriteReport("native-environment-lifetime");
    }

    private void WriteReport(string kind)
    {
        bool passed = _results.All(result => result.Passed);
        string output = Path.GetFullPath(OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { kind, passed, results = _results },
            new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"[environment-lifetime] passed={passed}; {_results.Count} checks; {output}");
        GetTree().Quit(passed ? 0 : 1);
    }

    private void Expect(string name, bool passed, string detail) => _results.Add(new Result(name, passed, detail));
    private async Task Until(Func<bool> condition, string stage)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed.TotalSeconds > 15) throw new TimeoutException(stage);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
