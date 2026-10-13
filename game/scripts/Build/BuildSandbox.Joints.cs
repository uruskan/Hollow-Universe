using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Hollow.Core;
using Hollow.Simulation.Building;
using Num = System.Numerics;

namespace HollowUniverse.Build;

// --view build --scene joints: the building's joints in every grade (GROUND.md §6.3, "Fit"), one group a grade on a
// 3 × 2 grid 14 m apart, each a structure of its own with its foundations' tops 60 cm over the highest ground under it:
//
//      F0 F1        F0: walls on its north and west edges (an outer corner), a floor on their tops
//   T  F2 F3        F1: a doorway with the grade's door on its north edge, in line with F0's wall
//                   F2 | F3: two foundations side by side;  F3: a wall frame with the grade's double door on its east edge
//                   T: a triangle foundation west of F2, walls on its two outer edges (60° at its apex)
//
// --tour <folder> photographs each group's nine joints and a walk along a wall's base (folder relative to game/), saves
// them with a contact sheet per grade, measures flicker (three pictures of each joint with the camera moved a third of a
// millimetre between them: pixels that change are faces trading places) and saves its map per grade, then quits.
public partial class BuildSandbox
{
    private const double JointSpacing = 14;

    private static readonly string[] JointDoors = { "wooden-door", "wooden-door", "sheet-metal-door", "sheet-metal-door", "armoured-door" };

    private static readonly string[] JointDoubleDoors =
        { "wooden-double-door", "wooden-double-door", "sheet-metal-double-door", "sheet-metal-double-door", "armoured-double-door" };

    // Each joint's camera in its group's frame (+X east, +Z south, the group's first foundation's top at the origin).
    private static readonly (string Name, Vector3D From, Vector3D At)[] JointViews =
    {
        ("wall-inside", new Vector3D(0.7, 1.0, 0.5), new Vector3D(-0.55, 0.0, -1.3)),
        ("wall-outside", new Vector3D(-0.3, 0.75, -3.0), new Vector3D(0.6, -0.1, -1.6)),
        ("foundations", new Vector3D(2.5, 1.0, 6.0), new Vector3D(1.5, -0.2, 4.45)),
        ("floor-on-walls", new Vector3D(-2.7, 4.2, -3.2), new Vector3D(-0.9, 2.95, -1.2)),
        ("ceiling", new Vector3D(0.9, 1.5, 0.9), new Vector3D(-0.7, 2.9, -1.4)),
        ("corner", new Vector3D(-3.0, 1.5, -3.1), new Vector3D(-1.5, 0.5, -1.5)),
        ("triangle", new Vector3D(-7.0, 1.6, 2.4), new Vector3D(-4.0, 0.5, 3.0)),
        ("doorway", new Vector3D(3.6, 1.4, -4.4), new Vector3D(3.0, 0.9, -1.5)),
        ("double-door", new Vector3D(8.0, 1.6, 3.4), new Vector3D(4.5, 1.2, 3.0)),
    };

    // The walk: along F0's north wall and F1's doorway, 1.7 m out, at the height of the wall's base.
    private const int WalkFrames = 8;

    private static (Vector3D From, Vector3D At) WalkView(int i)
    {
        double x = -1.2 + 5.4 * i / (WalkFrames - 1);
        return (new Vector3D(x, 0.6, -3.2), new Vector3D(x + 0.35, 0.08, -1.5));
    }

    private readonly List<BuildingFrame> _jointFrames = new();

    private void Joints(BuildingFrame first)
    {
        for (int g = 0; g < _data.Grades.Count; g++)
        {
            var flat = new BuildingFrame(first.ToZone(new Vector3D(JointSpacing * (g % 3), 0, JointSpacing * (g / 3))),
                first.AxisX, first.AxisY, first.AxisZ);
            // The highest ground under the group's footprint, so no foundation's top is buried.
            double highest = double.MinValue;
            for (double x = -4.2; x <= 4.6; x += 0.55)
            {
                for (double z = -1.6; z <= 4.6; z += 0.55)
                {
                    Num.Vector3 at = F(flat.ToZone(new Vector3D(x, 0, z)));
                    highest = Math.Max(highest, Vector3D.Dot(D(_zone.GroundAt(at.X, at.Z)) - flat.Origin, flat.AxisY));
                }
            }
            var frame = new BuildingFrame(flat.Origin + flat.AxisY * (highest + 0.6), flat.AxisX, flat.AxisY, flat.AxisZ);
            _jointFrames.Add(frame);
            Piece f0 = _world.AddFirst(frame, _data.Piece("foundation")!, g, 1, 0);
            Piece f1 = DemoBases.On(_world, f0, DemoBases.Facing(f0, 1, 0), "foundation", g);
            Piece f2 = DemoBases.On(_world, f0, DemoBases.Facing(f0, 0, 1), "foundation", g);
            Piece f3 = DemoBases.On(_world, f2, DemoBases.Facing(f2, 1, 0), "foundation", g);
            Piece frameWall = DemoBases.On(_world, f3, DemoBases.Facing(f3, 1, 0), "wall-frame", g);
            _world.PlaceDoor(1, _data.Door(JointDoubleDoors[Math.Min(g, JointDoubleDoors.Length - 1)])!, frameWall, 0, out _);
            Piece t = DemoBases.On(_world, f2, DemoBases.Facing(f2, -1, 0), "foundation-triangle", g);
            Piece north = DemoBases.On(_world, f0, DemoBases.Facing(f0, 0, -1), "wall", g);
            DemoBases.On(_world, f0, DemoBases.Facing(f0, -1, 0), "wall", g);
            DemoBases.On(_world, north, new SocketId(SocketKind.Top, 0), "floor", g);
            Piece doorway = DemoBases.On(_world, f1, DemoBases.Facing(f1, 0, -1), "doorway", g);
            _world.PlaceDoor(1, _data.Door(JointDoors[Math.Min(g, JointDoors.Length - 1)])!, doorway, 0, out _);
            DemoBases.On(_world, t, DemoBases.Facing(t, -0.5, -1), "wall", g);
            DemoBases.On(_world, t, DemoBases.Facing(t, -0.5, 1), "wall", g);
        }
        _model.SelectBelt(1);
        Look(_jointFrames[0], JointViews[0].From, JointViews[0].At);
    }

    private async void JointTour(string folder)
    {
        string dir = Path.IsPathRooted(folder) ? folder : Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), folder));
        Directory.CreateDirectory(dir);
        _interface.Visible = false;
        while (!ReadyForShot)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // The control: the same view twice gives the same picture, so whatever changes below is the scene's doing.
        (string firstName, Vector3D firstFrom, Vector3D firstAt) = JointViews[0];
        int still = Flicker(await Shoot(_jointFrames[0], firstFrom, firstAt), await Shoot(_jointFrames[0], firstFrom, firstAt), null).Count;
        GD.Print($"[joints] control: {still} pixels differ between two pictures of the same view ({firstName})");
        int total = 0;
        for (int g = 0; g < _jointFrames.Count; g++)
        {
            string grade = _data.Grades[g].Key;
            var shots = new List<Image>();
            var maps = new List<Image>();
            var counts = new List<string>();
            foreach ((string name, Vector3D from, Vector3D at) in JointViews)
            {
                // Three pictures with the camera moved a third of a millimetre sideways between them: coincident faces
                // trade places from one to the next (z-fighting), while everything else moves by a hundredth of a pixel.
                Image a = await Shoot(_jointFrames[g], from, at);
                Image b = await Shoot(_jointFrames[g], from, at, FlickerStep);
                Image c = await Shoot(_jointFrames[g], from, at, 2 * FlickerStep);
                (int count, Image map) = Flicker(a, b, c);
                a.SaveJpg(Path.Combine(dir, $"{grade}-{name}.jpg"), 0.88f);
                shots.Add(a);
                maps.Add(map);
                counts.Add($"{name} {count}");
                total += count;
            }
            Sheet(shots, 3).SaveJpg(Path.Combine(dir, $"{grade}.jpg"), 0.88f);
            Sheet(maps, 3).SaveJpg(Path.Combine(dir, $"{grade}-flicker.jpg"), 0.88f);
            GD.Print($"[joints] {grade}: flickering pixels by view: {string.Join(", ", counts)}");
        }
        GD.Print($"[joints] flickering pixels in all {JointViews.Length * _jointFrames.Count} views: {total}");
        // The walk along the base of the twig and stone groups' north walls (the owner's case and a flat-faced grade).
        foreach (int g in new[] { 0, 2 })
        {
            var frames = new List<Image>();
            for (int i = 0; i < WalkFrames; i++)
            {
                (Vector3D from, Vector3D at) = WalkView(i);
                Image shot = await Shoot(_jointFrames[g], from, at);
                shot.SaveJpg(Path.Combine(dir, $"walk-{_data.Grades[g].Key}-{i}.jpg"), 0.88f);
                frames.Add(shot);
            }
            Sheet(frames, 4).SaveJpg(Path.Combine(dir, $"walk-{_data.Grades[g].Key}.jpg"), 0.88f);
        }
        GD.Print($"[joints] tour saved in {dir}");
        GetTree().Quit(0);
    }

    // How far the camera moves sideways between the flicker test's pictures.
    private const double FlickerStep = 0.0003;

    // The view from a point at another in a group's frame (moved `aside` metres to the camera's right), once it has been
    // drawn a few times.
    private async System.Threading.Tasks.Task<Image> Shoot(BuildingFrame frame, Vector3D from, Vector3D at, double aside = 0)
    {
        Vector3 eye = G(frame.ToZone(from)), target = G(frame.ToZone(at)), up = G(frame.DirectionToZone(Vector3D.UnitY));
        var basis = Basis.LookingAt(target - eye, up);
        eye += basis.X * (float)aside;
        _fixedCamera = new Transform3D(basis, eye);
        for (int i = 0; i < 6; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return GetViewport().GetTexture().GetImage();
    }

    // Pixels that change by more than a fifth of their range from one picture to the next, and the first picture greyed
    // with them in red.
    private static (int Count, Image Map) Flicker(Image a, Image b, Image? c)
    {
        byte[] da = a.GetData(), db = b.GetData(), dc = c?.GetData() ?? db;
        int channels = da.Length / (a.GetWidth() * a.GetHeight());
        byte[] map = new byte[da.Length];
        int count = 0;
        for (int i = 0; i < da.Length; i += channels)
        {
            int change = 0;
            for (int k = 0; k < 3; k++)
            {
                change = Math.Max(change, Math.Max(Math.Abs(da[i + k] - db[i + k]), Math.Abs(db[i + k] - dc[i + k])));
            }
            bool flickers = change > 51;
            count += flickers ? 1 : 0;
            byte grey = (byte)((da[i] + da[i + 1] + da[i + 2]) / 6 + 40);
            map[i] = flickers ? (byte)255 : grey;
            map[i + 1] = flickers ? (byte)0 : grey;
            map[i + 2] = flickers ? (byte)0 : grey;
            if (channels == 4)
            {
                map[i + 3] = 255;
            }
        }
        return (count, Image.CreateFromData(a.GetWidth(), a.GetHeight(), false, a.GetFormat(), map));
    }

    // Pictures at half size in rows of `columns`.
    private static Image Sheet(List<Image> shots, int columns)
    {
        int w = shots[0].GetWidth() / 2, h = shots[0].GetHeight() / 2;
        int rows = (shots.Count + columns - 1) / columns;
        Image sheet = Image.CreateEmpty(w * columns, h * rows, false, shots[0].GetFormat());
        for (int i = 0; i < shots.Count; i++)
        {
            Image small = (Image)shots[i].Duplicate();
            small.Resize(w, h, Image.Interpolation.Lanczos);
            sheet.BlitRect(small, new Rect2I(0, 0, w, h), new Vector2I(i % columns * w, i / columns * h));
        }
        return sheet;
    }
}
