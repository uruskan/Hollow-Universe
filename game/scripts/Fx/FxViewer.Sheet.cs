using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HollowUniverse.Characters;
using HollowUniverse.Common;
using HollowUniverse.Items;

namespace HollowUniverse.Fx;

/// <summary>The contact sheet (<c>--sheet &lt;family&gt;</c>): every effect of a family in its own cell, all caught at their
/// hero moments, each on its own station far from the others. Muzzle effects get a first-person cell too.</summary>
public partial class FxViewer
{
    private sealed record Cell(FxDef Def, bool FirstPerson, FxStation Station, Camera3D Camera, double Time);

    private void StartSheet(string family)
    {
        var cells = new List<(FxDef Def, bool FirstPerson, double Time)>();
        foreach (FxDef d in FxCatalog.All.Where(d => d.Family == family))
        {
            cells.Add((d, false, d.Hero));
            if (family == "muzzle" && d.Key is "muzzle.rifle" or "muzzle.pistol" or "muzzle.rifle.brake")
            {
                cells.Add((d, true, d.Hero));
            }
        }
        BuildSheet(cells);
    }

    /// <summary>One effect at several moments (<c>--strip &lt;key&gt; --times a,b,c</c>): for tuning.</summary>
    private void StartStrip(string key, string times)
    {
        FxDef? def = FxCatalog.Find(key);
        if (def == null)
        {
            GD.PushError($"--strip: no effect '{key}'.");
            return;
        }
        var cells = times.Split(',').Select(t => (def, Options.ContainsKey("fp"), double.Parse(t, System.Globalization.CultureInfo.InvariantCulture))).ToList();
        BuildSheet(cells);
    }

    private void BuildSheet(List<(FxDef Def, bool FirstPerson, double Time)> cells)
    {
        _fx.DistanceLod = false;
        _fx.Culling = false;
        _fx.Capped = false;
        _hud.Visible = false;
        GetViewport().Disable3D = true;
        int n = cells.Count;
        int cols = n <= 3 ? n : n <= 4 ? 2 : n <= 6 ? 3 : n <= 8 ? 4 : n <= 12 ? 4 : 5;
        int rows = (n + cols - 1) / cols;
        var screen = new Control { Name = "Sheet" };
        screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var layer = new CanvasLayer { Name = "SheetLayer" };
        AddChild(layer);
        layer.AddChild(screen);
        var background = new ColorRect { Color = new Color(0.07f, 0.075f, 0.08f) };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        screen.AddChild(background);
        var grid = new GridContainer { Columns = cols };
        grid.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Ui.Track(grid, g =>
        {
            g.AddThemeConstantOverride("h_separation", Ui.Px(4));
            g.AddThemeConstantOverride("v_separation", Ui.Px(4));
        });
        screen.AddChild(grid);
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int labelHeight = Ui.Px(26);
        int cellW = (int)((size.X - Ui.Px(4) * (cols - 1)) / cols);
        int cellH = (int)((size.Y - Ui.Px(4) * (rows - 1)) / rows);
        var made = new List<Cell>();
        for (int i = 0; i < n; i++)
        {
            (FxDef def, bool fp, double time) = cells[i];
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 0);
            var container = new SubViewportContainer { Stretch = true, CustomMinimumSize = new Vector2(cellW, cellH - labelHeight) };
            var viewport = new SubViewport
            {
                Size = new Vector2I(cellW, cellH - labelHeight),
                Msaa3D = Viewport.Msaa.Msaa4X,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            container.AddChild(viewport);
            box.AddChild(container);
            string caption = def.Key + (fp ? " (first person)" : "") + (Options.ContainsKey("strip") ? $" · {time:0.00} s" : "");
            var label = Ui.Label(caption, TextStyle.Secondary);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.CustomMinimumSize = new Vector2(cellW, labelHeight);
            box.AddChild(label);
            grid.AddChild(box);
            var at = new Vector3((i % 5) * 140f - 280f, 0, (i / 5) * 140f - 140f);
            FxStation station = FxStage.Build(this, SheetStation(def), at, 0f);
            var camera = new Camera3D { Fov = 55, Near = 0.02f, Far = 600, Current = true };
            viewport.AddChild(camera);
            if (fp)
            {
                FirstPersonGun(camera, def, station);
            }
            else
            {
                if (station.Scatter != null && def.Family is "impact" or "blood" or "gather" && def.Key is not ("blood.drip" or "gather.tree-fall" or "gather.node-break"))
                {
                    (Vector3 hit, Vector3 hitNormal) = station.Scatter(new FxRandom(7));
                    station.Look = hit + hitNormal * 0.12f;
                }
                Aim(camera, station, SheetYaw(def), station.Pitch, station.Distance * Zoom(def) * SheetZoom(def));
            }
            made.Add(new Cell(def, fp, station, camera, time));
        }
        // Everything reaches its hero moment together.
        double start = 2.0;
        double longest = made.Max(c => c.Time);
        foreach (Cell c in made)
        {
            Cell cell = c;
            At(start + longest - c.Time, () => PlaySheetCell(cell));
        }
        At(start + longest, () =>
        {
            _fx.Paused = true;
            At(0.15, () => ReadyForShot = true);
        });
    }

    private static string SheetStation(FxDef def) => def.Stage switch
    {
        "gun" => def.Key.Contains("pistol", StringComparison.Ordinal) ? "pistol" : def.Key.Contains("rocket", StringComparison.Ordinal) ? "launcher" : "rifle",
        _ => def.Stage,
    };

    private static float SheetYaw(FxDef def) => def.Family switch
    {
        "muzzle" => def.Key == "backblast.rocket" ? 140f : def.Key.Contains("rocket", StringComparison.Ordinal) ? 70f : 96f,
        "explosion" => 20f,
        _ => 28f,
    };

    /// <summary>How far the sheet's camera stands from a family's effects, as a share of the station's own distance.</summary>
    private static float SheetZoom(FxDef def) => def.Family switch
    {
        "muzzle" => def.Key.Contains("rocket", StringComparison.Ordinal) ? 0.8f : 0.36f,
        "explosion" => 0.9f,
        "impact" => def.Stage is "flesh" ? 0.45f : def.Stage is "water" or "dirt" or "grass" or "sand" ? 0.38f : 0.42f,
        "blood" => def.Key == "blood.drip" ? 0.45f : 0.6f,
        "gather" => def.Key is "gather.tree-fall" or "gather.node-break" ? 0.6f : 0.55f,
        "world" => def.Key is "world.door-slam" ? 0.4f : def.Key == "world.footstep.sand" ? 0.3f : def.Key == "world.collapse" ? 0.8f : 0.6f,
        _ => 0.85f,
    };

    private void PlaySheetCell(Cell cell)
    {
        FxDef def = cell.Def;
        if (cell.FirstPerson)
        {
            _fx.CameraOverride = cell.Camera;
            if (cell.Station.Root.GetMeta("fp_muzzle").AsGodotObject() is Node3D muzzle)
            {
                FxDirector.Muzzle(def.Key, muzzle, true);
            }
            _fx.CameraOverride = null;
            return;
        }
        if (def.IsTracer && def.Key != "trail.rocket")
        {
            // The head is at the middle of the view at its hero moment.
            float speed = def.Key == "trail.arrow" ? 55f : 300f;
            Vector3 dir = (cell.Station.Root.Basis.X + new Vector3(0, 0.02f, 0)).Normalized();
            Vector3 from = cell.Station.Look - dir * speed * def.Hero - cell.Station.Root.Basis.Z * 0.6f;
            FxDirector.Tracer(def.Key, from, dir * speed, new Vector3(0, -9f, 0), def.Key == "trail.arrow" ? 0f : 0.3f);
            return;
        }
        if (def.Key == "trail.rocket")
        {
            StartRocketFlight(cell.Station);
            return;
        }
        PlayAt(def, cell.Station, _random, scatter: false);
    }

    /// <summary>A rocket flying across the cell, its trail running (the sheet and the action scene).</summary>
    private void StartRocketFlight(FxStation st)
    {
        var rocket = ModelCatalog.Instantiate("rocket") ?? new Node3D();
        AddChild(rocket);
        Basis b = st.Root.Basis;
        const float speed = 18f;
        Vector3 from = st.Look - b.X * (speed * 0.75f) + Vector3.Up * 0.3f;
        // The rocket model's nose points along its -Z (it lies along Blender's Y): face it along the station's +X.
        rocket.GlobalPosition = from;
        rocket.LookAt(from + b.X, Vector3.Up);
        var tail = new Node3D { Name = "Tail" };
        rocket.AddChild(tail);
        tail.Position = new Vector3(0, 0.05f, 0.34f);
        FxDirector.Start("trail.rocket", from, Vector3.Up, new FxParams { Follow = tail, Direction = b.X });
        var tween = CreateTween();
        tween.SetProcessMode(Tween.TweenProcessMode.Idle);
        tween.TweenMethod(Callable.From<float>(t =>
        {
            if (!_fx.Paused)
            {
                rocket.GlobalPosition = from + b.X * t;
            }
        }), 0f, speed * 3f, 3.0f);
    }

    /// <summary>The cell's gun held in first person: drawn like the view model (its field of view, its layer), at the
    /// catalogue's first-person pose, the flash on its muzzle.</summary>
    private static void FirstPersonGun(Camera3D camera, FxDef def, FxStation station)
    {
        string key = def.Key.Contains("pistol", StringComparison.Ordinal) ? "pistol" : "rifle";
        Node3D? gun = ModelCatalog.Instantiate(key);
        ModelEntry? entry = ModelCatalog.Find(key);
        if (gun == null || entry?.Held == null)
        {
            return;
        }
        ModelCatalog.ForceLod(gun, 0);
        if (def.Key.EndsWith(".brake", StringComparison.Ordinal))
        {
            ModelCatalog.Attach(gun, key, "muzzle-brake");
        }
        PrepareViewmodel(gun);
        camera.AddChild(gun);
        gun.Transform = entry.Held.FirstPerson.Transform;
        camera.Fov = 75;
        camera.GlobalPosition = station.Root.GlobalPosition + new Vector3(0, 1.6f, 0);
        camera.LookAt(camera.GlobalPosition + new Vector3(0.0f, -0.02f, -1f), Vector3.Up);
        var muzzle = (def.Key.EndsWith(".brake", StringComparison.Ordinal) ? gun.FindChild("muzzle", true, false) : null) as Node3D;
        // A brake on the gun moves the muzzle to its front.
        Node3D? socket = gun.FindChild("socket_muzzle", true, false) as Node3D;
        if (muzzle != null && socket != null)
        {
            var front = new Node3D { Name = "BrakeFront", Position = new Vector3(0.09f, 0, 0) };
            socket.AddChild(front);
            socket = front;
        }
        station.Root.SetMeta("fp_muzzle", socket!);
    }

    /// <summary>Layer 2 and the view model's materials, as ViewmodelView prepares a held item.</summary>
    private static void PrepareViewmodel(Node node)
    {
        if (node is MeshInstance3D mi)
        {
            mi.Layers = ViewmodelView.RenderLayer;
            mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            for (int i = 0; i < mi.GetSurfaceOverrideMaterialCount(); i++)
            {
                if (mi.GetActiveMaterial(i) is Material m)
                {
                    mi.SetSurfaceOverrideMaterial(i, CharacterMaterials.ForViewmodel(m));
                }
            }
        }
        foreach (Node child in node.GetChildren())
        {
            PrepareViewmodel(child);
        }
    }
}
