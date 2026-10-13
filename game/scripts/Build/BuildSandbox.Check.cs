using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Core;
using Hollow.Simulation.Building;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Build;

// --view build --scene empty --build-check: plays one round of building through the sandbox's own input paths (the aim,
// the left button, E, the hammer's radial choices) and quits with exit code 0 when every step did what it should: a
// free foundation, a wall snapped to its edge, the wall upgraded and turned, a doorway with a door that opens, then
// the foundation demolished and everything on it coming down.
public partial class BuildSandbox
{
    private async void RunCheck()
    {
        var failures = new List<string>();
        int checks = 0;
        void Expect(bool ok, string what)
        {
            checks++;
            GD.Print($"build-check: {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
            {
                failures.Add(what);
            }
        }
        async System.Threading.Tasks.Task Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        void Aim(Num.Vector3 eye, Num.Vector3 at) =>
            _fixedCamera = new Transform3D(Basis.LookingAt(G(at - eye), G(_zone.Frame.UpAt(eye))), G(eye));
        void Click() => Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        void Press(Key key) => Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        void Plan(string key) => _model.Plan.Piece = _data.Wheel.ToList().FindIndex(p => p.Key == key);
        Num.Vector3 Zone(Piece p, Vector3D local) => F(p.Structure.Frame.ToZone(PieceGeometry.ToFrame(p.Pose, local)));

        await Frames(5);
        Num.Vector3 up = _zone.Frame.UpAt(_site);
        Num.Vector3 eye = _site + up * 1.7f + new Num.Vector3(0, 0, 5);

        // A foundation where the builder looks, 3.5 m ahead.
        _model.SelectBelt(0);
        Plan("foundation");
        Aim(eye, _site + new Num.Vector3(0, 0, 1.5f));
        await Frames(3);
        Click();
        await Frames(2);
        Piece? foundation = _world.AllPieces.FirstOrDefault(p => p.Def.Key == "foundation");
        Expect(foundation != null, "a free foundation placed with the left button");
        if (foundation == null)
        {
            Finish(failures, checks);
            return;
        }

        // A wall on the edge nearest the builder.
        Plan("wall");
        Num.Vector3 nearEdge = Nearest(foundation, eye);
        Aim(eye, nearEdge + up * 0.6f);
        await Frames(3);
        Click();
        await Frames(2);
        Piece? wall = _world.AllPieces.FirstOrDefault(p => p.Def.Key == "wall");
        Expect(wall != null && wall.Supporters.Any(l => l.Other == foundation), "a wall snapped to that edge");

        // The hammer: upgrade to stone, then turn it round (its radial's options 1 and 4).
        if (wall != null)
        {
            _model.SelectBelt(1);
            Aim(eye, Zone(wall, new Vector3D(0.4, 1.4, 0)));
            await Frames(3);
            int yaw = wall.Pose.Yaw;
            _model.HeldRadial!.Choose(1);
            Expect(wall.Grade == 2, "upgraded to stone through the hammer's radial");
            _model.HeldRadial!.Choose(4);
            Expect(wall.Pose.Yaw == PieceGeometry.Wrap(yaw + 6), "turned round through the hammer's radial");
        }

        // A doorway on the far edge, then a door aimed into its opening; E opens it.
        _model.SelectBelt(0);
        Plan("doorway");
        Num.Vector3 farEdge = Farthest(foundation, eye);
        Num.Vector3 above = _site + up * 4.5f + new Num.Vector3(0, 0, 1.0f);
        Aim(above, farEdge + up * 0.8f);
        await Frames(3);
        Click();
        await Frames(2);
        Piece? doorway = _world.AllPieces.FirstOrDefault(p => p.Def.Key == "doorway");
        Expect(doorway != null, "a doorway on the far edge");
        if (doorway != null)
        {
            _model.SelectBelt(2);
            Num.Vector3 opening = Zone(doorway, new Vector3D(0, 1.1, 0));
            Num.Vector3 front = Zone(doorway, new Vector3D(0.3, 1.6, -2.5));
            Aim(front, opening);
            await Frames(3);
            Click();
            await Frames(2);
            Expect(doorway.Door != null, "a wooden door fitted by aiming into the opening");
            Press(Key.E);
            await Frames(2);
            Expect(doorway.Door?.Open == true, "E opens it");
        }

        // Demolish the foundation: everything on it comes down, one layer after another.
        _model.SelectBelt(1);
        Aim(Zone(foundation, new Vector3D(0.2, 3.2, 0.6)), Zone(foundation, new Vector3D(0.3, 0, 0.3)));
        await Frames(3);
        _model.HeldRadial!.Choose(5);
        Expect(foundation.Gone && _world.Collapsing > 0, $"demolished; what stood on it is coming down ({_world.Collapsing} pieces)");
        await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
        Expect(_world.PieceCount == 0, "and is gone a moment later");
        Finish(failures, checks);
    }

    private void Finish(List<string> failures, int checks)
    {
        GD.Print(failures.Count == 0 ? $"build-check: all {checks} checks passed" : $"build-check: {failures.Count} of {checks} failed");
        GetTree().Quit(failures.Count == 0 ? 0 : 1);
    }

    // The middle of a slab's edge nearest to (or farthest from) a point, in the zone.
    private static Num.Vector3 Nearest(Piece slab, Num.Vector3 point) => EdgeMiddles(slab).OrderBy(m => Num.Vector3.Distance(m, point)).First();

    private static Num.Vector3 Farthest(Piece slab, Num.Vector3 point) => EdgeMiddles(slab).OrderByDescending(m => Num.Vector3.Distance(m, point)).First();

    private static IEnumerable<Num.Vector3> EdgeMiddles(Piece slab)
    {
        for (int e = 0; e < slab.Def.EdgeCount; e++)
        {
            Vector3D middle = PieceGeometry.Edge(slab.Def.Shape, e, 3).Middle;
            yield return F(slab.Structure.Frame.ToZone(PieceGeometry.ToFrame(slab.Pose, middle)));
        }
    }
}
