using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using HollowUniverse.Settings;

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

        foreach (string name in new[] { "Vegetation", "Grass", "Cliffs", "Monuments" })
        {
            Node3D? layer = world.GetNodeOrNull<Node3D>(name);
            if (layer == null || !layer.Visible)
            {
                GD.Print($"[environment-performance] skipped {name}: absent or already hidden");
                continue;
            }
            await MeasureHiddenLayer(name, new[] { layer });
        }

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
