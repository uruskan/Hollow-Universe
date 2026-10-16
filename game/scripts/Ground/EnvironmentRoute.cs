using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Client.Diagnostics;
using Hollow.Core;
using Hollow.Simulation.Movement;
using Hollow.Surface;
using HollowUniverse.App;
using HollowUniverse.Settings;
using HollowUniverse.UI;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>A real hosted-client walking route, separate from the gameplay/audio tours. No teleport/noclip fallback.</summary>
public sealed partial class EnvironmentRoute : Node
{
    public GroundView View { get; set; } = null!;
    public string Folder { get; set; } = "";
    public double ProfileSeconds { get; set; } = 20;
    public double SolarHour { get; set; } = 10;
    /// <summary>Positive: observe normal human input/settings at the ordinary spawn, without route selection or steering.</summary>
    public double ObserveSeconds { get; set; }
    private readonly List<object> _checkpoints = new(), _trajectory = new(), _profiles = new();
    private object? _plan, _spawnConnector;
    private string? _failure;
    private double _began;
    private bool _stopping;
    private DisplayServer.VSyncMode _originalVsync;
    private int _originalFrameCap;
    private GameClient Client => View.Client;
    private static double Clock => Time.GetTicksUsec() / 1_000_000.0;

    public override void _Ready()
    {
        Directory.CreateDirectory(Folder);
        if (ObserveSeconds <= 0)
        {
            View.Scripted = new InputFrame();
            AddChild(new CaptureIsolation { Name = "RouteInputIsolation", Resolution = CaptureIsolation.RequestedResolution(new Vector2I(1600, 900)) });
        }
        Run();
    }

    private async void Run()
    {
        _began = Clock;
        var vsync = _originalVsync = DisplayServer.WindowGetVsyncMode();
        int frameCap = _originalFrameCap = Engine.MaxFps;
        try
        {
            if (View.IsolatedCapture) throw new InvalidOperationException("Environment route requires normal movement; omit --shot.");
            await WaitUntilReady("initial", 75);
            var renderWorld = View.World;
            if (renderWorld?.Surface is not WalkingWorld world || Math.Abs(world.Radius - 1200) > 0.01)
                throw new InvalidOperationException("Environment route requires live Earth1200.");
            if (ObserveSeconds > 0)
            {
                // Deliberately preserve ordinary input, settings, camera, survival, location and time of day.
                await Capture("ordinary-host-start");
                await Observe(Math.Clamp(ObserveSeconds, 5, 120));
                await WaitUntilReady("ordinary-host-end", 35);
                await Capture("ordinary-host-end");
            }
            else
            {
                View.Interface.Open(GroundScreen.Hud);
                View.Scripted = new InputFrame();
                EnvironmentRoutePlan route = await Task.Run(() => EarthEnvironmentRoute.Find(world));
                EnsureActive();
                _plan = new { route.Region, route.Metres, route.CandidatesChecked,
                    stops = route.Stops.Select(s => new { s.Label, position = Point(s.Position), lookAt = Point(s.LookAt),
                        latitude = EarthEnvironmentRoute.Latitude(s.Position), longitude = EarthEnvironmentRoute.Longitude(s.Position),
                        s.Feature, s.Height, s.Grade, s.Grass, s.Woodland, s.Trees }),
                    legs = route.Legs.Select(l => new { l.Metres, points = l.Points.Select(Point) }) };
                GD.Print($"[environment-route] selected {route.Region}; {route.Metres:0.0} m; {route.CandidatesChecked} candidates");
                Healthy();
                if ((Client.Character.Flags & MoveFlags.OnGround) == 0) throw new InvalidOperationException("Route spawn is not grounded.");
                // Use the existing ephemeris's local-hour setter, not the CLI's limited search for a future hour.
                // A single frozen date/phase carries through the whole region; observations never change the clock.
                var routeSky = renderWorld.Sky ?? throw new InvalidOperationException("Route sky is missing");
                renderWorld.SetTime(845000000, true);
                routeSky.Ephemeris.SetLocalSolarHours(route.Stops[0].Position.Normalized,
                    routeSky.Sun.Index, 845000000, SolarHour);
                await WalkSpawnConnector(world, route.Stops[0].Position);
                for (int i = 0; i < route.Stops.Count; i++)
                {
                    if (i > 0) await Walk(route.Legs[i - 1], route.Stops[i].Label);
                    EnvironmentRouteStop stop = route.Stops[i];
                    View.Scripted = new InputFrame();
                    Num.Vector3 eye = Client.Character.Position + Client.Frame.UpAt(Client.Character.Position) * Client.EyeHeight;
                    View.LookToward(Client.Frame.ToLocal(stop.LookAt) - eye);
                    await WaitUntilReady(stop.Label, 45);
                    if ((Client.Character.Flags & MoveFlags.OnGround) == 0) throw new InvalidOperationException(stop.Label + " stopped off the ground");
                    await Capture($"{i:00}-{stop.Label}");
                }
            }
            GD.Print("[environment-route] technical route/observation complete; visual quality requires inspection of the unedited frames");
        }
        catch (OperationCanceledException) when (_stopping)
        {
            _failure = "Diagnostic session cancelled before completion";
        }
        catch (Exception error)
        {
            _failure = error.ToString();
            View.Scripted = new InputFrame();
            GD.PushError("[environment-route] failed: " + error.Message);
            // Failure evidence is separate, never substituted for a ready checkpoint or measured frame.
            try { await Capture("FAILED-diagnostic", requireReady: false); }
            catch (Exception capture) { GD.Print("[environment-route] diagnostic image failed: " + capture.Message); }
        }
        finally
        {
            try
            {
                if (GodotObject.IsInstanceValid(View)) View.Scripted = null;
                if (!_stopping) { Engine.MaxFps = frameCap; DisplayServer.WindowSetVsyncMode(vsync); }
            }
            catch (Exception cleanup)
            {
                _failure ??= "Diagnostic cleanup failed: " + cleanup;
                GD.PushError("[environment-route] cleanup failed: " + cleanup.Message);
            }
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string? location = string.IsNullOrEmpty(assembly.Location) ? null : assembly.Location;
                File.WriteAllText(Path.Combine(Folder, "environment-route.json"), JsonSerializer.Serialize(new
                {
                    technicalChecksPassed = _failure == null, visualQualityAccepted = false, failure = _failure,
                    evidence = "Normal hosted simulation/prediction. Route inputs are scripted, observation inputs are human. No spawn teleport, noclip, placement or density overrides. Screenshots are outside frame measurements. Sampling alone is not navigation or art acceptance.",
                    mode = ObserveSeconds > 0 ? "ordinary-host-observation" : "connected-walk",
                    version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                    runtimeModuleVersionId = assembly.ManifestModule.ModuleVersionId,
                    runtimeAssemblyLocation = location, runtimeHash = AssemblyHash(location), args = OS.GetCmdlineUserArgs(),
                    graphics = GraphicsSettings.Current, displayVsync = vsync.ToString(), frameCap,
                    seconds = Clock - _began, plan = _plan, spawnConnector = _spawnConnector,
                    checkpoints = _checkpoints, profiles = _profiles, trajectory = _trajectory,
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception report)
            {
                _failure ??= "Diagnostic report failed: " + report;
                GD.PushError("[environment-route] report failed: " + report.Message);
            }
            finally
            {
                if (!_stopping && IsInsideTree()) GetTree().Quit(_failure == null ? 0 : 1);
            }
        }
    }

    private async Task WalkSpawnConnector(WalkingWorld world, Vector3D beach)
    {
        // Zone.SpawnPoint deliberately spreads arrivals3–8m round the landing site. Validate that this is the
        // requested site, then traverse that real offset; it must not disappear through a wider start tolerance.
        if (Client.Zone is not { } zone || EarthEnvironmentRoute.Distance(zone.LandingDirection, beach) > 0.25)
            throw new InvalidOperationException($"Launch fresh --spawn origin --lat {EarthEnvironmentRoute.Latitude(beach):0.000000} --lon {EarthEnvironmentRoute.Longitude(beach):0.000000}; the server landing site is not the route beach.");
        Vector3D start = BodyPosition();
        double metres = EarthEnvironmentRoute.Distance(start, beach);
        if (metres > 8.25)
            throw new InvalidOperationException($"Spawn is {metres:0.00}m from the beach, beyond the documented3–8m landing ring; use a fresh diagnostic save.");
        if (!EarthEnvironmentRoute.Corridor(world, start, beach))
            throw new InvalidOperationException("Spawn-to-beach connector crosses water, a monument or an unsafe grade; no movement or teleport attempted.");
        int steps = Math.Max(1, (int)Math.Ceiling(metres / 2));
        var points = new List<Vector3D> { start };
        for (int i = 1; i <= steps; i++)
            points.Add(EarthEnvironmentRoute.Ground(world, start.Normalized * (1.0 - (double)i / steps) + beach.Normalized * ((double)i / steps)));
        _spawnConnector = new { metres, points = points.Select(Point),
            proof = "Same dry/grade/monument samples as route; normal predicted/server movement validates object collision." };
        _trajectory.Add(new { stage = "spawn-connector-start", seconds = Clock - _began, pose = Pose() });
        GD.Print($"[environment-route] walking checked spawn connector {metres:0.00}m from the normal landing ring");
        await Walk(new EnvironmentRouteLeg(points, metres), "spawn-to-beach");
    }

    public void Cancel()
    {
        if (_stopping) return;
        _stopping = true;
        if (GodotObject.IsInstanceValid(View)) View.Scripted = null;
        if (_began > 0) { Engine.MaxFps = _originalFrameCap; DisplayServer.WindowSetVsyncMode(_originalVsync); }
    }

    public override void _ExitTree() => Cancel();
    private void EnsureActive() { if (_stopping || !IsInsideTree()) throw new OperationCanceledException(); }
    private async Task Frame()
    {
        EnsureActive();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        EnsureActive();
    }

    private async Task Walk(EnvironmentRouteLeg leg, string label)
    {
        Healthy();
        var frames = new List<double>();
        double gpu = 0, cpu = 0, start = Clock, previous = start, sampled = start, offGround = 0;
        double limit = Math.Clamp(leg.Metres / 1.5 + 25, 35, 300);
        var originalVsync = DisplayServer.WindowGetVsyncMode();
        int cap = Engine.MaxFps;
        if (ProfileSeconds > 0) { DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); Engine.MaxFps = 0; }
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        try
        {
            for (int i = 1; i < leg.Points.Count; i++)
            {
                double best = double.PositiveInfinity, progressed = Clock;
                while (EarthEnvironmentRoute.Distance(BodyPosition(), leg.Points[i]) > 1.25)
                {
                    Healthy();
                    double now = Clock, distance = EarthEnvironmentRoute.Distance(BodyPosition(), leg.Points[i]);
                    if (distance < best - 0.15) { best = distance; progressed = now; }
                    if (now - progressed > 8) throw new InvalidOperationException($"{label}: blocked for 8s at waypoint {i}; no teleport fallback");
                    if (now - start > limit) throw new InvalidOperationException(label + ": leg exceeded its movement deadline");
                    if ((Client.Character.Flags & MoveFlags.OnGround) != 0) offGround = 0;
                    else if (offGround == 0) offGround = now;
                    else if (now - offGround > 2) throw new InvalidOperationException(label + ": airborne/swimming for more than 2s");
                    View.TurnToward(Client.Frame.ToLocal(leg.Points[i]) - Client.Character.Position);
                    View.Pitch = -0.04f;
                    View.Scripted = new InputFrame { Forward = 1 };
                    await Frame();
                    now = Clock;
                    if (now - start <= ProfileSeconds && now > previous)
                    {
                        frames.Add((now - previous) * 1000);
                        gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
                        cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()) + RenderingServer.GetFrameSetupTimeCpu();
                    }
                    previous = now;
                    if (now - sampled >= 0.5) { _trajectory.Add(new { stage = label, seconds = now - _began, pose = Pose() }); sampled = now; }
                }
            }
        }
        finally
        {
            if (!_stopping)
            {
                View.Scripted = new InputFrame();
                DisplayServer.WindowSetVsyncMode(originalVsync); Engine.MaxFps = cap;
            }
            _profiles.Add(Profile(label, frames, gpu, cpu, Clock - start));
        }
    }

    private async Task Observe(double seconds)
    {
        var frames = new List<double>();
        double start = Clock, previous = start, sampled = start, gpu = 0, cpu = 0;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        while (Clock - start < seconds)
        {
            Healthy();
            await Frame();
            double now = Clock;
            frames.Add((now - previous) * 1000); previous = now;
            gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
            cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid());
            if (now - sampled >= 0.5) { _trajectory.Add(new { stage = "human-input", seconds = now - _began, pose = Pose() }); sampled = now; }
        }
        _profiles.Add(Profile("ordinary-settings-observation", frames, gpu, cpu, Clock - start));
    }

    private static object Profile(string stage, List<double> frames, double gpu, double cpu, double elapsed)
    {
        double[] sorted = frames.Order().ToArray();
        double Percentile(double p) => sorted.Length == 0 ? 0 : sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * p) - 1, 0, sorted.Length - 1)];
        return new { stage, elapsedSeconds = elapsed, frames = frames.Count, measuredSeconds = frames.Sum() / 1000,
            meanMs = frames.Count == 0 ? 0 : frames.Average(), medianMs = Percentile(0.5), p95Ms = Percentile(0.95),
            p99Ms = Percentile(0.99), worstMs = Percentile(1), gpuMs = frames.Count == 0 ? 0 : gpu / frames.Count,
            renderCpuMs = frames.Count == 0 ? 0 : cpu / frames.Count, frameMs = frames };
    }

    private async Task WaitUntilReady(string stage, double seconds)
    {
        double start = Clock, nextLog = start;
        int settled = 0;
        while (settled < 12)
        {
            if (Clock - start > seconds) throw new InvalidOperationException(stage + ": readiness timeout; " + View.World?.StreamingStatus);
            settled = View.ReadyForShot && Client.HasCharacter && !Client.Local.IsDead ? settled + 1 : 0;
            if (Clock >= nextLog)
            {
                GD.Print($"[environment-route] waiting {stage}: state={Client.State} loading={View.LoadingShown} ready={View.ReadyForShot}; {View.World?.StreamingStatus}");
                nextLog = Clock + 5;
            }
            await Frame();
        }
    }

    private void Healthy()
    {
        EnsureActive();
        if (Client.State != ClientState.Playing || !Client.HasCharacter || Client.Local.IsDead)
            throw new InvalidOperationException("Route lost its living hosted character");
        if (Client.Character.Frame != 0 || Client.Character.Has(MoveCheats.Noclip))
            throw new InvalidOperationException("Route must walk in the planet frame with noclip off");
        if (View.Interface.Screen != GroundScreen.Hud || View.LoadingShown)
            throw new InvalidOperationException("Unexpected UI/loading interrupted ordinary movement");
    }

    private async Task Capture(string label, bool requireReady = true)
    {
        EnsureActive();
        if (requireReady && !View.ReadyForShot) throw new InvalidOperationException(label + ": not ready for screenshot");
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        EnsureActive();
        using Image image = GetViewport().GetTexture().GetImage();
        string path = Path.Combine(Folder, label + ".png");
        if (image.SavePng(path) != Error.Ok) throw new IOException("Cannot save " + path);
        var sky = View.World?.Sky;
        var camera = View.Camera;
        if (requireReady && ObserveSeconds <= 0 && sky != null &&
            ((SolarHour == 0 && sky.SunElevation >= -6) || (SolarHour == 10 && sky.SunElevation <= 10)))
            throw new InvalidOperationException($"{label}: actual sun elevation {sky.SunElevation:0.0} disagrees with requested hour {SolarHour}");
        var record = new { label, path, sha256 = Hash(path), width = image.GetWidth(), height = image.GetHeight(),
            seconds = Clock - _began, ready = View.ReadyForShot, streaming = View.World?.StreamingStatus, pose = Pose(),
            camera = camera == null ? null : new { position = Point(camera.GlobalPosition),
                forward = Point(-camera.GlobalBasis.Z), up = Point(camera.GlobalBasis.Y), camera.Fov },
            lighting = sky == null ? null : new { sky.LocalHours, sky.SunElevation, sky.MoonElevation, sky.MoonPhase,
                sky.Exposure, sun = Point(sky.SunIlluminance), moon = Point(sky.MoonIlluminance), light = View.World?.LightDebug },
            meshes = Meshes(View.World).ToArray() };
        _checkpoints.Add(record);
        GD.Print("[environment-route] checkpoint " + JsonSerializer.Serialize(record));
    }

    private object Pose()
    {
        if (!Client.HasCharacter) return new { available = false };
        var c = Client.Character;
        Vector3D body = BodyPosition();
        var surface = View.World?.Surface;
        return new { position = Point(c.Position), velocity = Point(c.Velocity), c.Frame, flags = c.Flags.ToString(),
            cheats = c.Cheats.ToString(), stance = c.Stance.ToString(), Client.PredictedTick, Client.ServerTickNow,
            Client.Stats.Snapshots, Client.Stats.Corrections, Client.Stats.PredictionErrorMax,
            latitude = EarthEnvironmentRoute.Latitude(body), longitude = EarthEnvironmentRoute.Longitude(body),
            groundClearance = surface == null ? 0 : body.Length - surface.Radius - surface.HeightAt(body.Normalized),
            yaw = View.Yaw, pitch = View.Pitch };
    }

    private static IEnumerable<object> Meshes(Node? root)
    {
        if (root == null) yield break;
        foreach (Node child in root.GetChildren())
        {
            if (child is MultiMeshInstance3D mesh && mesh.Multimesh is { } instances)
                yield return new { node = mesh.GetPath().ToString(), visible = mesh.IsVisibleInTree(),
                    count = instances.InstanceCount, visibleCount = instances.VisibleInstanceCount };
            foreach (object item in Meshes(child)) yield return item;
        }
    }
    private Vector3D BodyPosition() => Client.Frame.ToBody(Client.Character.Position);
    private static double[] Point(Vector3D p) => new[] { p.X, p.Y, p.Z };
    private static float[] Point(Num.Vector3 p) => new[] { p.X, p.Y, p.Z };
    private static float[] Point(Vector3 p) => new[] { p.X, p.Y, p.Z };
    private static string Hash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
    private static string? AssemblyHash(string? path)
    {
        if (path == null) return null; // Godot can load the assembly from bytes; the launch manifest retains its file SHA.
        try { return Hash(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            GD.Print("[environment-route] assembly file hash unavailable: " + error.Message);
            return null;
        }
    }
}
