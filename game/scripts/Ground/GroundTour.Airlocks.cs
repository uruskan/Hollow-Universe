using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Nature;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

public partial class GroundTour
{
    // --tour-steps airlocks (docs/DOORS.md §7): the hosted game's three airlocks, each built by the admin's `airlock` command on a
    // level dry site: a triangle with two single doors (layouts 1 and 2), a square room with a double door and a single
    // door (0 and 1), and the owner's airlock, the triangle with both doors swinging in (0 and 0). Each is pictured shut and then, the doors opened through the door verb, open: from above, from outside
    // the first doorway, from outside the second and from inside, with the doors moving by the server's motion record.
    private async Task AirlockTour()
    {
        await Until(() => Client.Console().IsAdmin, 10);
        if (!Client.Console().IsAdmin) throw new InvalidOperationException("The airlock tour needs the hosted admin.");
        GroundProbe ground = Client.Placement().Ground!.Value;
        await RepairGod(true);
        // The pictures are of the doors: no hands, held tool or interface over them.
        FirstPersonView hands = View.GetNode<FirstPersonView>("FirstPerson");
        hands.Visible = false;
        View.Interface.Visible = false;
        try
        {
            foreach ((string kind, string layouts, string label) in new[] { ("triangle", "1 2", "triangle"), ("room", "0 1", "room"), ("triangle", "0 0", "triangle-both-in") })
            {
                Num.Vector3 site = await AirlockSite(ground);
                Admin("noclip", "off");
                await TeleportTo(ground.Offset(site, 0, 9));
                Face(site + Client.Frame.UpAt(site) * Client.EyeHeight);
                await Settle();
                var before = Client.Building().World!.AllPieces.Select(p => p.Id).ToHashSet();
                Num.Vector3 playerAt = Client.Character.Position;
                View.Console.Run($"airlock {kind} {layouts} shut");
                await Until(() => Client.Building().World!.AllPieces.Count(p => !before.Contains(p.Id)) >= (kind == "room" ? 5 : 4), 15);
                Piece[] pieces = Client.Building().World!.AllPieces.Where(p => !before.Contains(p.Id)).ToArray();
                Piece[] frames = pieces.Where(p => p.Def.Opening != Opening.None && p.Def.Opening != Opening.Window).ToArray();
                await Until(() => frames.All(p => p.Door != null), 5);
                if (pieces.Length == 0 || frames.Length != 2 || frames.Any(p => p.Door == null))
                    throw new InvalidOperationException("The airlock command did not replicate its pieces and two doors; see F1.");
                // The first doorway is the one that faces the player (the command's edge 0).
                Piece first = frames.OrderBy(p => Num.Vector3.Distance(p.ZoneTransform.Position, playerAt)).First(), second = frames.Single(p => p != first);
                BuildingFrame frame = first.Structure.Frame;
                Admin("noclip", "on");
                await Seconds(0.3);
                foreach (bool open in new[] { false, true })
                {
                    if (open)
                    {
                        foreach (Piece doorway in new[] { first, second })
                        {
                            Door door = doorway.Door!;
                            await TeleportTo(F(frame.ToZone(InFrame(doorway, 0.1, 0, 2.0))));
                            Face(F(frame.ToZone(InFrame(doorway, 0, 1.0, 0))));
                            await Seconds(0.3);
                            Client.Local.Interact(door.Ref, "door-open");
                            await Until(() => door.Open, 4);
                            if (!door.Open) throw new InvalidOperationException("A door did not open through its verb.");
                        }
                        await Seconds(1.8);
                    }
                    Vector3D centre = (InFrame(first, 0, 0, 0) + InFrame(second, 0, 0, 0)) / 2;
                    Vector3D above = (InFrame(first, 0, 0, 2.2) + InFrame(second, 0, 0, 2.2)) / 2 + Vector3D.UnitY * 5.4;
                    string state = open ? "open" : "shut";
                    await ClanBaseShot($"airlock-{label}-{state}-above", frame, above, centre + Vector3D.UnitY * 0.3);
                    await ClanBaseShot($"airlock-{label}-{state}-outside-first", frame, InFrame(first, 0.2, 1.5, 3.4), InFrame(first, 0, 1.0, 0));
                    await ClanBaseShot($"airlock-{label}-{state}-outside-second", frame, InFrame(second, -0.2, 1.5, 3.4), InFrame(second, 0, 1.0, 0));
                    await ClanBaseShot($"airlock-{label}-{state}-inside", frame, InFrame(second, 0.1, 1.5, -0.6), InFrame(first, 0, 1.0, 0));
                }
                View.CameraLift = 0;
                GD.Print($"[airlocks] {kind} {layouts}: shut and open pictured.");
            }
        }
        finally
        {
            hands.Visible = true;
            View.Interface.Visible = true;
            View.CameraLift = 0;
            Admin("noclip", "off");
        }
    }

    // A point in a doorway piece's own metres (x right seen from outside, y up, z outside) in its structure's frame.
    private static Vector3D InFrame(Piece doorway, double x, double y, double z) => PieceGeometry.ToFrame(doorway.Pose, new Vector3D(x, y, z));

    // A dry, level site (within 1.1 m over 7 m square) away from other pieces and solid nature.
    private async Task<Num.Vector3> AirlockSite(GroundProbe ground)
    {
        var nature = new List<NaturalObject>();
        for (int ring = 1; ring <= 9; ring++)
        {
            for (int side = 0; side < 16; side++)
            {
                float angle = side * MathF.Tau / 16;
                Num.Vector3 site = ground.Offset(Landing, MathF.Cos(angle) * ring * 22, MathF.Sin(angle) * ring * 22);
                Num.Vector3 up = Client.Frame.UpAt(site);
                BuildingFrame frame = BuildingFrame.At(new Vector3D(site.X, site.Y, site.Z), new Vector3D(up.X, up.Y, up.Z), 0);
                double low = double.PositiveInfinity, high = double.NegativeInfinity;
                bool wet = false;
                for (double x = -3.5; x <= 3.5; x += 1.75)
                {
                    for (double z = -3.5; z <= 3.5; z += 1.75)
                    {
                        Num.Vector3 point = F(frame.ToZone(new Vector3D(x, 0, z)));
                        double height = -ground.HeightAbove(point);
                        low = Math.Min(low, height);
                        high = Math.Max(high, height);
                        wet |= ground.InWater(ground.Settle(point));
                    }
                }
                nature.Clear();
                Client.Survival().Nature?.Query(site, 12, nature);
                bool blocked = nature.Any(n => Client.Content.Natural(n.Kind)!.IsSolid)
                    || Client.Building().World!.AllPieces.Any(p => Num.Vector3.Distance(p.ZoneTransform.Position, site) < 20);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!wet && !blocked && high - low <= 1.1) return site;
            }
        }
        throw new InvalidOperationException("No clear, level airlock site found near this landing point.");
    }
}
