using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using HollowUniverse.UI;
using HollowUniverse.UI.Model;

namespace HollowUniverse.Ground;

public partial class GroundTour
{
    // Short native check of the on-foot map's real mouse drag and overlay cost; uses current graphics settings.
    private async Task MapDrag()
    {
        GroundUi ui = View.Interface;
        View.Scripted = null;
        ui.Open(GroundScreen.Map);
        await Until(() => ui.MapReady, 120);
        RequireRepair(ui.MapReady, "the world map picture is ready for dragging");
        await Seconds(0.4);
        int markers = ui.Model.MapMarkers.Count(m => m.Kind == MapMarkerKind.Marker);
        Vector2 centre = ui.MapScreenArea.GetCenter();
        Vector2 pointer = centre + new Vector2(-100, 60);
        void Motion(Vector2 next)
        {
            Input.ParseInputEvent(new InputEventMouseMotion { Position = next, GlobalPosition = next, Relative = next - pointer });
            pointer = next;
        }
        void Button(bool down) => Input.ParseInputEvent(new InputEventMouseButton
        {
            Position = pointer, GlobalPosition = pointer, ButtonIndex = MouseButton.Left, Pressed = down,
        });
        Rid viewport = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(viewport, true);
        async Task<object> Measure(bool dragging)
        {
            ui.MeasureMapDrawing(true);
            var frames = new List<double>();
            double cpu = 0, gpu = 0, start = Time.GetTicksUsec() / 1e6, previous = start;
            while (Time.GetTicksUsec() / 1e6 - start < 3)
            {
                if (dragging)
                {
                    float t = (float)(Time.GetTicksUsec() / 1e6 - start);
                    Motion(centre + new Vector2(-100 + 65 * MathF.Sin(t * 2), 60 + 35 * MathF.Sin(t * 3)));
                }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                double now = Time.GetTicksUsec() / 1e6;
                frames.Add((now - previous) * 1000);
                previous = now;
                cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) + RenderingServer.GetFrameSetupTimeCpu();
                gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
            }
            double[] sorted = frames.Order().ToArray();
            return new { phase = dragging ? "drag" : "idle", frames = frames.Count,
                meanMs = frames.Average(), p95Ms = sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * .95) - 1, 0, sorted.Length - 1)],
                renderCpuMs = cpu / frames.Count, renderGpuMs = gpu / frames.Count, overlay = ui.MapDrawingMeasurement };
        }
        try
        {
            object idle = await Measure(false);
            Motion(pointer);
            Button(true);
            object drag = await Measure(true);
            Button(false);
            await Seconds(0.1);
            RequireRepair(!ui.MapFollowing && ui.Model.MapMarkers.Count(m => m.Kind == MapMarkerKind.Marker) == markers,
                "ordinary mouse dragging moves the world map without adding a marker");
            string report = JsonSerializer.Serialize(new { idle, drag, vsync = DisplayServer.WindowGetVsyncMode().ToString(), maxFps = Engine.MaxFps },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(Folder, "map-drag.json"), report);
            GD.Print("[map-drag] " + report);
            await Shot("map-drag");
        }
        finally
        {
            Button(false);
            ui.MeasureMapDrawing(false);
            ui.Open(GroundScreen.Hud);
        }
    }
}
