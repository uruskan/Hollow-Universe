using System;
using System.Linq;
using System.Diagnostics;
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

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        bool rocks = args.Contains("--rocks");
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--shot") _shot = args[i + 1];
        HollowUniverse.Settings.GraphicsSettings.Load(HollowUniverse.Settings.GraphicsSettings.ParsePreset("high"));
        AddChild(new HollowUniverse.Settings.GraphicsNode());
        AddChild(new UiScaler());
        var (system, planet) = StartingWorld.Find();
        WalkingWorld surface = WalkingWorld.For(system, planet, 1200);
        ulong seed = WalkingWorld.SeedFor(Hash64.Of(system.Address.Value, planet.Path));
        Vector3D eye, target;
        if (rocks)
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
            for (long i = 0; i < TileKey.CountAt(surface.Cliffs.Level); i++)
                if (surface.Cliffs.FormationAt(TileKey.FromIndex(surface.Cliffs.Level, i)!.Value) is { Count: >= 3 } group)
                { first = group[0]; break; }
            CliffPlacement cliff = first ?? throw new InvalidOperationException("Earth has no review formation.");
            double height = CliffGeometry.All[cliff.Model].Height * cliff.Scale;
            Vector3D eyeDirection = (cliff.Position + cliff.Forward * 34).Normalized;
            eye = eyeDirection * (surface.Radius + surface.HeightAt(eyeDirection) + 3);
            target = cliff.Position + cliff.Up * height * 0.6;
            GD.Print($"[geology] formation {cliff.Tile} {CliffGeometry.All[cliff.Model].Key}; height {height:0.00}");
        }
        _world = new SurfaceView { Driven = true };
        AddChild(_world);
        const double time = 845000000;
        _world.OpenWorld(system, planet, surface, new SurfacePlants(surface), seed, time, 1, 9.80665f);
        _world.SetTime(time, true);
        _world.Sky!.Ephemeris.SetLocalSolarHours(eye.Normalized, _world.Sky.Sun.Index, time, 16);
        _world.Camera.Position = V(eye);
        _world.Camera.LookAt(V(target), V(eye.Normalized));
        _world.Camera.Fov = 65;
        _world.Camera.MakeCurrent();
        _clock.Start();
    }

    private static Vector3 V(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public override async void _Process(double delta)
    {
        if (_capturing || _world == null || _clock.Elapsed.TotalSeconds < 8) return;
        if (!_world.IsReady && _clock.Elapsed.TotalSeconds < 90) return;
        _capturing = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error saved = GetViewport().GetTexture().GetImage().SavePng(_shot);
        GD.Print($"[geology] screenshot {_shot}: {saved}; streaming ready {_world.IsReady}; elapsed {_clock.Elapsed.TotalSeconds:0.0}s");
        GetTree().Quit(saved == Error.Ok && _world.IsReady ? 0 : 1);
    }
}
