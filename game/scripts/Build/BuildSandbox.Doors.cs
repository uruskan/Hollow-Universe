using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Hollow.Core;
using Hollow.Simulation.Building;
using Num = System.Numerics;

namespace HollowUniverse.Build;

// --view build --scene doors: every door the building has, each in its own doorway or wall frame along one wall, for the
// pictures of docs/DOORS.md (both faces, the lock on both faces, the frame holding the leaf). A row of seven stone
// foundations; on each one's south edge a wall piece with its opening and a door:
//
//      k0 wooden door      (wood doorway)      k3 wooden double door      (wood wall frame)
//      k1 sheet metal door (metal doorway)     k4 sheet metal double door (metal wall frame)
//      k2 armoured door    (armoured doorway)  k5 armoured double door    (armoured wall frame)
//                                              k6 garage door             (metal wall frame)
//
// k0 and k1 carry a code lock, k2 a key lock. --tour <folder> (relative to game/) takes a picture of every door from
// outside (+Z, south) and from inside, and of each lock's side of the leaf from both faces, then quits.
public partial class BuildSandbox
{
    private const double DoorSpacing = 3;

    // The door of each foundation along the row, the wall piece that holds it and that wall's grade.
    private static readonly (string Door, string Wall, int Grade)[] DoorRow =
    {
        ("wooden-door", "doorway", 1),
        ("sheet-metal-door", "doorway", 3),
        ("armoured-door", "doorway", 4),
        ("wooden-double-door", "wall-frame", 1),
        ("sheet-metal-double-door", "wall-frame", 3),
        ("armoured-double-door", "wall-frame", 4),
        ("garage-door", "wall-frame", 3),
    };

    private BuildingFrame _doorFrame;

    private void Doors(PlacementPlan first)
    {
        _doorFrame = first.Frame;
        Piece slab = _world.AddFirst(first.Frame, first.Piece, 2, 1, 0);
        for (int k = 0; k < DoorRow.Length; k++)
        {
            if (k > 0)
            {
                slab = DemoBases.On(_world, slab, DemoBases.Facing(slab, 1, 0), "foundation", 2);
            }
            (string door, string wall, int grade) = DoorRow[k];
            Piece frame = DemoBases.On(_world, slab, DemoBases.Facing(slab, 0, 1), wall, grade);
            _world.PlaceDoor(1, _data.Door(door)!, frame, 0, out Door? placed);
            if (placed != null && k < 3)
            {
                if (k == 2)
                {
                    _world.AttachKeyLock(1, placed, out _);
                }
                else
                {
                    _world.AttachCodeLock(1, placed, out _);
                }
            }
        }
        // One more doorway with no door in it, at the end of the row, for the placement ghost (the tour's last pictures).
        slab = DemoBases.On(_world, slab, DemoBases.Facing(slab, 1, 0), "foundation", 2);
        DemoBases.On(_world, slab, DemoBases.Facing(slab, 0, 1), "doorway", 2);
        _model.SelectBelt(1);
        Look(_doorFrame, new Vector3D(0, 1.3, 5.2), new Vector3D(0, 1.15, 1.5));
    }

    private static double DoorX(int k) => k * DoorSpacing;

    // A door's two faces: from outside (south) and from inside (north), straight on, 3 m or 4.2 m from the wall.
    private static (Vector3D From, Vector3D At) FaceView(int k, bool outside)
    {
        bool wide = DoorRow[k].Wall != "doorway";
        double d = wide ? 4.4 : 3.2;
        double y = wide ? 1.4 : 1.2;
        double z = outside ? 1.5 + d : 1.5 - d;
        return (new Vector3D(DoorX(k) + 0.02, y, z), new Vector3D(DoorX(k) + 0.02, y - 0.05, 1.5));
    }

    // A single door's lock seat by the free edge, close up, from the face the keypad is on and from the other.
    private static (Vector3D From, Vector3D At) LockView(int k, bool outside)
    {
        double x = DoorX(k) - 0.562 + 0.984;
        return (new Vector3D(x - 0.15, 1.2, outside ? 1.5 + 0.9 : 1.5 - 0.9), new Vector3D(x, 1.05, 1.5));
    }

    private async void DoorTour(string folder)
    {
        string dir = Path.IsPathRooted(folder) ? folder : Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), folder));
        Directory.CreateDirectory(dir);
        _interface.Visible = false;
        ulong began = Time.GetTicksMsec(), lastReport = began;
        while (!ReadyForShot)
        {
            ulong now = Time.GetTicksMsec();
            if (now - lastReport >= 1000)
            {
                GD.Print($"[doors] waiting frames={_frames} terrainSettled={_terrain.IsSettled} {_terrain.StreamingStatus}");
                lastReport = now;
            }
            if (now - began > 45000)
            {
                GD.PushError($"[doors] readiness failed after 45s: frames={_frames} {_terrain.StreamingStatus}");
                GetTree().Quit(1);
                return;
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        var sheet = new List<Image>();
        for (int k = 0; k < DoorRow.Length; k++)
        {
            foreach (bool outside in new[] { true, false })
            {
                (Vector3D from, Vector3D at) = FaceView(k, outside);
                Image shot = await Shoot(_doorFrame, from, at);
                shot.SaveJpg(Path.Combine(dir, $"{DoorRow[k].Door}-{(outside ? "outside" : "inside")}.jpg"), 0.9f);
                sheet.Add(shot);
            }
        }
        Sheet(sheet, 4).SaveJpg(Path.Combine(dir, "doors.jpg"), 0.9f);
        var locks = new List<Image>();
        for (int k = 0; k < 3; k++)
        {
            foreach (bool outside in new[] { true, false })
            {
                (Vector3D from, Vector3D at) = LockView(k, outside);
                Image shot = await Shoot(_doorFrame, from, at);
                shot.SaveJpg(Path.Combine(dir, $"lock-{DoorRow[k].Door}-{(outside ? "outside" : "inside")}.jpg"), 0.9f);
                locks.Add(shot);
            }
        }
        Sheet(locks, 2).SaveJpg(Path.Combine(dir, "locks.jpg"), 0.9f);
        // The placement ghost of a wooden door in the empty doorway at the row's end: each of its four layouts (R's steps), the
        // leaves part open and the arc they sweep, seen from outside; and the first, seen from inside.
        _model.SelectBelt(2);
        var ghosts = new List<Image>();
        double ghostX = DoorX(DoorRow.Length);
        for (int turns = 0; turns < 5; turns++)
        {
            bool inside = turns == 4;
            _extraTurns = inside ? 0 : turns;
            Vector3D from = new(ghostX + 0.05, 1.45, inside ? 1.5 - 3.0 : 1.5 + 3.0), at = new(ghostX, 1.0, 1.5);
            await Settled(0.4);
            Image shot = await Shoot(_doorFrame, from, at);
            shot.SaveJpg(Path.Combine(dir, $"ghost-{(inside ? "inside" : turns.ToString())}.jpg"), 0.9f);
            ghosts.Add(shot);
        }
        _extraTurns = 0;
        _model.SelectBelt(1);
        Sheet(ghosts, 5).SaveJpg(Path.Combine(dir, "ghosts.jpg"), 0.9f);
        // Motion (docs/DOORS.md §4): the wooden door and the garage door opened, a picture every 0.45 s from outside.
        var motion = new List<Image>();
        foreach (int k in new[] { 0, 6 })
        {
            Door door = _world.Doors.Single(d => d.Def.Key == DoorRow[k].Door);
            (Vector3D from, Vector3D at) = FaceView(k, outside: true);
            _world.Toggle(1, door);
            for (int step = 0; step < 6; step++)
            {
                Image shot = await Shoot(_doorFrame, from, at);
                shot.SaveJpg(Path.Combine(dir, $"motion-{DoorRow[k].Door}-{step}.jpg"), 0.9f);
                motion.Add(shot);
                await Settled(k == 6 ? 0.55 : 0.18);
            }
            await Settled(3.2);
        }
        Sheet(motion, 6).SaveJpg(Path.Combine(dir, "motion.jpg"), 0.9f);
        GD.Print($"[doors] tour saved in {dir}");
        GetTree().Quit(0);
    }
}
