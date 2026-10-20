using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Simulation.Building;
using Num = System.Numerics;

namespace HollowUniverse.Build;

// --view build --scene airlocks: the airlocks of docs/DOORS.md §7, each a structure of its own 14 m apart, doors open and shut:
//
//      S0  a triangle foundation with a wooden door on each of two edges, the first hinged at the corner they share and swinging
//          in (layout 1), the second swinging out (layout 2): with both open nobody passes
//      S1  the same triangle hung the other way (layouts 0 and 3): the leaves hug the walls and the way is free
//      S2  a square room with a wooden double door swinging in on one edge and a wooden door hinged on the right (layout 1) on
//          the next: either alone leaves the way free, together they close it
//      S3  the owner's airlock: the same triangle with both doors swinging in (layouts 0 and 0): the first hangs against a wall,
//          the second across the walk, and the open leaves clear one another
//
// --tour <folder> (relative to game/) takes, for each structure and for open and shut doors, a picture from above, one from
// outside the first doorway and one from inside, and a sheet of them all, then quits.
public partial class BuildSandbox
{
    private readonly record struct Airlock(BuildingFrame Frame, Piece First, Piece Second, Door[] Doors, string Name);

    private readonly List<Airlock> _airlocks = new();

    private void Airlocks(BuildingFrame first)
    {
        for (int i = 0; i < 4; i++)
        {
            var flat = new BuildingFrame(first.ToZone(new Vector3D(JointSpacing * i, 0, 0)), first.AxisX, first.AxisY, first.AxisZ);
            double highest = double.MinValue;
            for (double x = -3.2; x <= 3.2; x += 0.55)
            {
                for (double z = -3.2; z <= 3.2; z += 0.55)
                {
                    Num.Vector3 at = F(flat.ToZone(new Vector3D(x, 0, z)));
                    highest = Math.Max(highest, Vector3D.Dot(D(_zone.Settle(at)) - flat.Origin, flat.AxisY));
                }
            }
            var frame = new BuildingFrame(flat.Origin + flat.AxisY * (highest + 0.6), flat.AxisX, flat.AxisY, flat.AxisZ);
            string key = i == 2 ? "foundation" : "foundation-triangle";
            Piece slab = _world.AddFirst(frame, _data.Piece(key)!, 2, 1, 0);
            Piece one = DemoBases.On(_world, slab, new SocketId(SocketKind.Edge, 0), i == 2 ? "wall-frame" : "doorway", 2);
            Piece two = DemoBases.On(_world, slab, new SocketId(SocketKind.Edge, 1), "doorway", 2);
            DemoBases.On(_world, slab, new SocketId(SocketKind.Edge, 2), "wall", 2);
            if (i == 2)
            {
                DemoBases.On(_world, slab, new SocketId(SocketKind.Edge, 3), "wall", 2);
            }
            Door[] doors = i switch
            {
                0 => new[] { Fit("wooden-door", one, 1), Fit("wooden-door", two, 2) },
                1 => new[] { Fit("wooden-door", one, 0), Fit("wooden-door", two, 3) },
                2 => new[] { Fit("wooden-double-door", one, 0), Fit("wooden-door", two, 1) },
                _ => new[] { Fit("wooden-door", one, 0), Fit("wooden-door", two, 0) },
            };
            _airlocks.Add(new Airlock(frame, one, two, doors, i switch { 0 => "triangle-airlock", 1 => "triangle-free", 2 => "room-airlock", _ => "triangle-both-in" }));
        }
        _model.SelectBelt(1);
        Look(_airlocks[0].Frame, new Vector3D(0, 6, 5), new Vector3D(0, 0.3, 0));
    }

    private Door Fit(string door, Piece frame, int layout)
    {
        _world.PlaceDoor(1, _data.Door(door)!, frame, layout, out Door? placed);
        return placed ?? throw new InvalidOperationException(door + " did not fit");
    }

    // A point in a doorway piece's own metres (x right seen from outside, y up, z outside) in its structure's frame.
    private static Vector3D InFrame(Piece doorway, double x, double y, double z) => PieceGeometry.ToFrame(doorway.Pose, new Vector3D(x, y, z));

    private async Task Settled(double seconds)
    {
        ulong until = Time.GetTicksMsec() + (ulong)(seconds * 1000);
        while (Time.GetTicksMsec() < until)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async void AirlockTour(string folder)
    {
        string dir = Path.IsPathRooted(folder) ? folder : Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), folder));
        Directory.CreateDirectory(dir);
        _interface.Visible = false;
        while (!ReadyForShot)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        var sheet = new List<Image>();
        foreach (Airlock airlock in _airlocks)
        {
            Vector3D centre = (InFrame(airlock.First, 0, 0, 0) + InFrame(airlock.Second, 0, 0, 0)) / 2;
            Vector3D above = (InFrame(airlock.First, 0, 0, 2.4) + InFrame(airlock.Second, 0, 0, 2.4)) / 2 + Vector3D.UnitY * 6.8;
            (string View, Vector3D From, Vector3D At)[] views =
            {
                ("above", above, centre + Vector3D.UnitY * 0.3),
                ("outside", InFrame(airlock.First, 0.2, 1.55, 3.3), InFrame(airlock.First, 0, 1.0, 0)),
                ("inside", InFrame(airlock.Second, 0.1, 1.55, -0.55), InFrame(airlock.First, 0, 1.0, 0)),
            };
            foreach (bool open in new[] { true, false })
            {
                foreach (Door door in airlock.Doors)
                {
                    if (door.Open != open)
                    {
                        _world.Toggle(1, door);
                    }
                }
                await Settled(1.4);
                foreach ((string view, Vector3D from, Vector3D at) in views)
                {
                    Image shot = await Shoot(airlock.Frame, from, at);
                    shot.SaveJpg(Path.Combine(dir, $"{airlock.Name}-{(open ? "open" : "shut")}-{view}.jpg"), 0.9f);
                    sheet.Add(shot);
                }
            }
        }
        Sheet(sheet, 3).SaveJpg(Path.Combine(dir, "airlocks.jpg"), 0.9f);
        GD.Print($"[airlocks] tour saved in {dir}");
        GetTree().Quit(0);
    }
}
