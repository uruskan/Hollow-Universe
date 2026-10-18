using System;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Godot;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Surface;
using HollowUniverse.App;

namespace HollowUniverse.Surface;

/// <summary>Reproducible real-Earth rendering check, without network/character warm-up or a saved world.</summary>
public sealed partial class GeologyReview : Node
{
    private SurfaceView _world = null!;
    private readonly Stopwatch _clock = new();
    private string _shot = "../.artifacts/geology-review.png";
    private bool _capturing;
    private object? _sceneInfo;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string? Option(string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        double Number(string name, double fallback) => Option(name) is { } value
            ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;
        Vector3D Point(string value)
        {
            double[] coordinates = value.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            if (coordinates.Length != 3 || coordinates.Any(v => !double.IsFinite(v)))
                throw new ArgumentException("A review camera point needs three finite body-frame coordinates.");
            return new Vector3D(coordinates[0], coordinates[1], coordinates[2]);
        }
        bool rocks = args.Contains("--rocks");
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shot") _shot = args[i + 1];
        HollowUniverse.Settings.GraphicsSettings.Load(HollowUniverse.Settings.GraphicsSettings.ParsePreset(Option("--quality") ?? "high"));
        AddChild(new HollowUniverse.Settings.GraphicsNode());
        AddChild(new UiScaler());
        var (system, planet) = StartingWorld.Find();
        WalkingWorld surface = WalkingWorld.For(system, planet, 1200);
        ulong seed = WalkingWorld.SeedFor(Hash64.Of(system.Address.Value, planet.Path));
        Vector3D eye, target;
        string selection = rocks ? "rock" : "first-cliff";
        if (Option("--eye") is { } explicitEye && Option("--target") is { } explicitTarget)
        {
            eye = Point(explicitEye);
            target = Point(explicitTarget);
            selection = "explicit-body-frame-camera";
        }
        else if (Option("--eye") != null || Option("--target") != null)
            throw new ArgumentException("Supply both --eye and --target for a matched comparison camera.");
        else if (rocks)
        {
            ScatterItem? chosen = null;
            foreach (Vector3D direction in PlanetSurface.FibonacciSphere(200))
            {
                if (surface.HeightAt(direction) < 5) continue;
                foreach (ScatterItem rock in surface.ScatterFor(TileKey.At(direction, surface.ScatterLevel)))
                    if (rock.Kind == ScatterKind.Rock && rock.Scale > 1.6f) { chosen = rock; break; }
                if (chosen != null) break;
            }
            ScatterItem item = chosen ?? throw new InvalidOperationException("Earth has no review rock.");
            Vector3D at = item.Position(surface.Radius), up = item.Direction;
            Vector3D side = Vector3D.Cross(Vector3D.UnitY, up).Normalized;
            Vector3D directionEye = (at + side * 4).Normalized;
            eye = directionEye * (surface.Radius + surface.HeightAt(directionEye) + 1.7);
            target = at + up * 0.5;
        }
        else
        {
            CliffPlacement? first = null;
            long selectedTile = (long)Number("--tile", -1);
            if (selectedTile >= 0)
            {
                TileKey key = TileKey.FromIndex(surface.Cliffs.Level, selectedTile)
                    ?? throw new ArgumentException("Cliff review tile is outside this world's grid.");
                int part = (int)Number("--part", 0);
                foreach (CliffPlacement member in surface.Cliffs.FormationAt(key))
                    if (member.Part == part) { first = member; break; }
                if (first == null) throw new ArgumentException("Requested cliff review tile/part has no placement.");
            }
            for (long i = 0; first == null && i < TileKey.CountAt(surface.Cliffs.Level); i++)
                if (surface.Cliffs.FormationAt(TileKey.FromIndex(surface.Cliffs.Level, i)!.Value) is { Count: >= 3 } group)
                { first = group[0]; break; }
            CliffPlacement cliff = first ?? throw new InvalidOperationException("Earth has no review formation.");
            double height = CliffGeometry.All[cliff.Model].Height * cliff.Scale;
            Vector3D eyeDirection = (cliff.Position + cliff.Downslope * Number("--distance", 34)).Normalized;
            eye = eyeDirection * (surface.Radius + surface.HeightAt(eyeDirection) + Number("--eye-height", 3));
            target = cliff.Position + cliff.Up * height * Number("--target-fraction", 0.6);
            selection = $"{cliff.Tile}/{cliff.Part}/{CliffGeometry.All[cliff.Model].Key}";
            GD.Print($"[geology] formation {cliff.Tile} {CliffGeometry.All[cliff.Model].Key}; height {height:0.00}");
        }
        _world = new SurfaceView { Driven = true };
        AddChild(_world);
        const double time = 845000000;
        _world.OpenWorld(system, planet, surface, new SurfacePlants(surface), seed, time, 1, 9.80665f);
        _world.SetTime(time, true);
        double hour = Number("--hour", 16);
        _world.Sky!.Ephemeris.SetLocalSolarHours(eye.Normalized, _world.Sky.Sun.Index, time, hour);
        _world.Camera.Position = V(eye);
        _world.Camera.LookAt(V(target), V(eye.Normalized));
        _world.Camera.Fov = 65;
        _world.Camera.MakeCurrent();
        _sceneInfo = new { selection, seed, time, hour, surface.Radius,
            eye = new[] { eye.X, eye.Y, eye.Z }, target = new[] { target.X, target.Y, target.Z }, fov = 65 };
        _clock.Start();
    }

    private static Vector3 V(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public override async void _Process(double delta)
    {
        if (_capturing || _world == null || _clock.Elapsed.TotalSeconds < 8) return;
        CliffField? cliffs = _world.GetNodeOrNull<CliffField>("Cliffs");
        if (!_world.IsReady && cliffs?.Failure == null && _clock.Elapsed.TotalSeconds < 90) return;
        _capturing = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string output = Path.GetFullPath(_shot);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        Error saved = GetViewport().GetTexture().GetImage().SavePng(_shot);
        File.WriteAllText(output + ".json", JsonSerializer.Serialize(new
        {
            evidence = "Standalone Earth renderer diagnostic, not hosted gameplay or art acceptance",
            ready = _world.IsReady, saved = saved.ToString(), elapsedSeconds = _clock.Elapsed.TotalSeconds,
            scene = _sceneInfo, args = OS.GetCmdlineUserArgs(), graphics = HollowUniverse.Settings.GraphicsSettings.Current,
            resolution = new[] { GetViewport().GetVisibleRect().Size.X, GetViewport().GetVisibleRect().Size.Y },
            module = typeof(GeologyReview).Assembly.ManifestModule.ModuleVersionId,
            geology = cliffs == null ? null : new { cliffs.Count, cliffs.NearCount, cliffs.FarCount,
                cliffs.PendingCount, cliffs.SelectedLodTriangles, cliffs.InstantiationCount,
                cliffs.TransformWriteCount, cliffs.LodSwitchCount, cliffs.LastFollowMicroseconds,
                cliffs.LastStreamMicroseconds, cliffs.FailedModelCount, cliffs.Failure }
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"[geology] screenshot {_shot}: {saved}; streaming ready {_world.IsReady}; elapsed {_clock.Elapsed.TotalSeconds:0.0}s");
        GetTree().Quit(saved == Error.Ok && _world.IsReady ? 0 : 1);
    }
}
