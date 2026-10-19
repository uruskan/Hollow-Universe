using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using HollowUniverse.Settings;
using HollowUniverse.Surface;

namespace HollowUniverse.Ground;

public sealed partial class EnvironmentRoute
{
    /// <summary>Opt-in stationary rendering diagnosis; temporary layer removal is never an art-quality result.</summary>
    public bool PerformanceBreakdown { get; set; }
    public bool PerformanceBaselineOnly { get; set; }
    public bool PerformanceShadowSweep { get; set; }

    private async Task RunPerformanceBreakdown()
    {
        var world = View.World ?? throw new InvalidOperationException("Performance diagnosis needs the loaded world");
        View.Scripted = new InputFrame();
        // Keep lighting identical across the sequential ablations and between separate comparison runs.
        world.SetTime(View.TimeOverride ?? Hollow.Core.UniverseTime.Now, true, resetCloudDrift: true);
        Engine.MaxFps = 0;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Healthy();
        GD.Print("[environment-performance] startup " + View.Loading?.Timeline());
        GD.Print("[environment-performance] graphics " + JsonSerializer.Serialize(GraphicsSettings.Current));
        GD.Print("[environment-performance] diagnostic ablations only; stationary input, unchanged saved preferences");
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        await SettlePerformance();
        await Capture("performance-baseline-start");
        await SettlePerformance();
        await SamplePerformance("baseline-start", null);

        if (PerformanceBaselineOnly)
        {
            await Capture("performance-baseline-end");
            return;
        }

        if (PerformanceShadowSweep)
        {
            GraphicsValues savedPreview = GraphicsSettings.Current with { };
            try
            {
                foreach ((int cascades, float distance) in new[] { (2, savedPreview.ShadowDistance), (2, 250f), (1, 150f) })
                {
                    GraphicsSettings.Preview(savedPreview with { ShadowCascades = cascades, ShadowDistance = distance });
                    UncapPerformance();
                    await SettlePerformance();
                    string label = $"shadows-{cascades}-cascades-{distance:0}m";
                    await SamplePerformance(label, null);
                    await Capture(label);
                }
            }
            finally
            {
                GraphicsSettings.Preview(savedPreview);
                UncapPerformance();
            }
            await SettlePerformance();
            await SamplePerformance("baseline-end", null);
            await Capture("performance-baseline-end");
            return;
        }

        // CliffsFar: the cliffs' far layer alone (T2), every formation beyond the pieces the near layer installed;
        // CliffWalls: the walls' rock skins.
        foreach ((string name, string path) in new[] { ("Vegetation", "Vegetation"), ("Grass", "Grass"), ("Cliffs", "Cliffs"),
                     ("CliffsFar", "Cliffs/Far"), ("CliffWalls", "Cliffs/Walls"), ("Monuments", "Monuments") })
        {
            Node3D? layer = world.GetNodeOrNull<Node3D>(path);
            if (layer == null || !layer.Visible)
            {
                GD.Print($"[environment-performance] skipped {name}: absent or already hidden");
                continue;
            }
            await MeasureHiddenLayer(name, new[] { layer });
        }

        // The far layer casting shadows, which it does not: what they would cost.
        if (world.GetNodeOrNull<CliffField>("Cliffs") is { FarShadows: false } cliffs && cliffs.Visible)
        {
            try
            {
                cliffs.FarShadows = true;
                await SettlePerformance();
                await SamplePerformance("diagnostic-with-CliffsFar-shadows", null);
            }
            finally
            {
                if (GodotObject.IsInstanceValid(cliffs)) cliffs.FarShadows = false;
                if (!_stopping)
                {
                    await SettlePerformance();
                    await WaitUntilReady("performance-restored-CliffsFar-shadows", 12);
                }
            }
        }

        // T0.4: the three ground-cover workloads, each removed alone through the field's diagnostic mask, so their
        // separate costs come from one loaded field rather than from hiding the whole node.
        if (world.GetNodeOrNull<GrassField>("Grass") is { } cover && cover.Visible)
        {
            foreach (GroundCoverLayers layer in new[] { GroundCoverLayers.Grass, GroundCoverLayers.Litter, GroundCoverLayers.Authored })
                await MeasureWithoutCoverLayer(cover, layer);
        }
        else GD.Print("[environment-performance] skipped ground-cover layers: grass field absent or hidden");

        // Terrain and water are separate meshes under the same tile owner. Identify the existing water material,
        // keeping the terrain visible and recording each mesh's prior visibility for restoration.
        Node3D[] water = world.Terrain.GetChildren().OfType<MeshInstance3D>()
            .Where(mesh => mesh.MaterialOverride is ShaderMaterial material &&
                material.Shader?.ResourcePath == "res://shaders/surface_water.gdshader")
            .Cast<Node3D>().ToArray();
        if (water.Any(mesh => mesh.Visible)) await MeasureHiddenLayer("Water", water);
        else GD.Print("[environment-performance] skipped Water: no visible water meshes");

        DirectionalLight3D[] lights = world.GetChildren().OfType<DirectionalLight3D>()
            .Where(light => light.ShadowEnabled).ToArray();
        if (lights.Length > 0)
        {
            try
            {
                foreach (DirectionalLight3D light in lights) light.ShadowEnabled = false;
                await SettlePerformance();
                await SamplePerformance("diagnostic-without-shadows", "directional shadows");
            }
            finally
            {
                foreach (DirectionalLight3D light in lights)
                    if (GodotObject.IsInstanceValid(light)) light.ShadowEnabled = true;
                if (!_stopping) await SettlePerformance();
            }
        }

        GraphicsValues original = GraphicsSettings.Current with { };
        if (original.Sdfgi)
        {
            try
            {
                GraphicsSettings.Preview(original with { Sdfgi = false });
                UncapPerformance();
                await SettlePerformance();
                await SamplePerformance("diagnostic-without-SDFGI", "SDFGI");
            }
            finally
            {
                GraphicsSettings.Preview(original);
                UncapPerformance();
                if (!_stopping)
                {
                    await SettlePerformance();
                    await WaitUntilReady("performance-restored-SDFGI", 12);
                }
            }
        }
        else GD.Print("[environment-performance] skipped SDFGI: already disabled");

        await SettlePerformance();
        await WaitUntilReady("performance-baseline-end", 12);
        await SamplePerformance("baseline-end", null);
        await Capture("performance-baseline-end");
    }

    private async Task MeasureHiddenLayer(string label, IReadOnlyList<Node3D> nodes)
    {
        bool[] visible = nodes.Select(node => node.Visible).ToArray();
        try
        {
            foreach (Node3D node in nodes) node.Visible = false;
            await SettlePerformance();
            await SamplePerformance("diagnostic-without-" + label, label);
        }
        finally
        {
            for (int i = 0; i < nodes.Count; i++)
                if (GodotObject.IsInstanceValid(nodes[i])) nodes[i].Visible = visible[i];
            if (!_stopping)
            {
                await SettlePerformance();
                await WaitUntilReady("performance-restored-" + label, 12);
            }
        }
    }

    private async Task MeasureWithoutCoverLayer(GrassField cover, GroundCoverLayers layer)
    {
        GroundCoverLayers before = cover.DiagnosticLayers;
        string name = layer.ToString().ToLowerInvariant();
        try
        {
            cover.DiagnosticLayers = before & ~layer;
            await SettlePerformance();
            await SamplePerformance("diagnostic-without-cover-" + name, "ground cover: " + name);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(cover)) cover.DiagnosticLayers = before;
            if (!_stopping)
            {
                await SettlePerformance();
                await WaitUntilReady("performance-restored-cover-" + name, 12);
            }
        }
    }

    /// <summary>
    /// This client process only; a child server's memory is its own process's. Private memory is split into the graphics
    /// driver's write-combined commit (video memory's backing, mapped GPU buffers) and the rest (HollowUniverse.Common.ProcessCommit).
    /// </summary>
    private static object ProcessMemory()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        HollowUniverse.Common.ProcessCommit.Split commit = HollowUniverse.Common.ProcessCommit.Measure();
        return new
        {
            workingSetMiB = process.WorkingSet64 / 1048576.0, privateMiB = commit.PrivateMiB,
            graphicsCommitMiB = commit.GraphicsMiB, cpuCommitMiB = commit.CpuMiB,
            managedMiB = GC.GetTotalMemory(false) / 1048576.0, godotStaticMiB = OS.GetStaticMemoryUsage() / 1048576.0,
        };
    }

    private async Task SettlePerformance()
    {
        double start = Clock;
        while (Clock - start < 2)
        {
            Healthy();
            View.Scripted = new InputFrame();
            await Frame();
        }
    }

    private static void UncapPerformance()
    {
        Engine.MaxFps = 0;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
    }

    private async Task SamplePerformance(string stage, string? disabledLayer)
    {
        var frames = new List<double>();
        var renderer = new List<object>();
        double start = Clock, previous = start, gpu = 0, cpu = 0, draws = 0, primitives = 0, video = 0;
        ulong peakVideo = 0;
        Rid viewport = GetViewport().GetViewportRid();
        while (Clock - start < 8)
        {
            Healthy();
            View.Scripted = new InputFrame();
            await Frame();
            double now = Clock;
            frames.Add((now - previous) * 1000);
            previous = now;
            double gpuMs = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
            double cpuMs = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport);
            ulong drawCalls = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            ulong submittedPrimitives = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
            ulong videoBytes = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed);
            gpu += gpuMs; cpu += cpuMs; draws += drawCalls; primitives += submittedPrimitives; video += videoBytes;
            peakVideo = Math.Max(peakVideo, videoBytes);
            renderer.Add(new { gpuMs, renderCpuMs = cpuMs, drawCalls, primitives = submittedPrimitives, videoBytes });
        }
        int count = frames.Count;
        object profile = new
        {
            stage, diagnosticAblation = disabledLayer != null, disabledLayer,
            timing = Profile(stage, frames, gpu, cpu, Clock - start),
            meanDrawCalls = count > 0 ? draws / count : 0,
            meanPrimitives = count > 0 ? primitives / count : 0,
            meanVideoBytes = count > 0 ? video / count : 0, peakVideoBytes = peakVideo,
            rendererSamples = renderer,
            // Submitted ground-cover instances/vertices/triangles before culling, and its job counts at the sample's end.
            groundCover = View.World?.GetNodeOrNull<GrassField>("Grass")?.CaptureDiagnostics(),
            processMemory = ProcessMemory(),
            pose = Pose(), graphics = GraphicsSettings.Current with { },
            effectiveFrameCap = Engine.MaxFps, effectiveVsync = DisplayServer.WindowGetVsyncMode().ToString(),
        };
        _profiles.Add(profile);
        GD.Print($"[environment-performance] {stage}: {count} frames, " +
            $"mean {(count > 0 ? frames.Average() : 0):0.00} ms, GPU {(count > 0 ? gpu / count : 0):0.00} ms, " +
            $"draws {(count > 0 ? draws / count : 0):0}, primitives {(count > 0 ? primitives / count : 0):0}, " +
            $"peak video {peakVideo / 1048576.0:0.0} MiB");
    }
}
