using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using HollowUniverse.Ships;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The motorboat in the game (docs/MOTORBOAT.md, <c>--tour-steps boat</c>), from a beach: its ghost on the water from the
/// shore, the boat put there, the helm taken from the water beside it, driven ahead and turned (the orbit camera and the
/// helm's), coasting, and its driver going over the side. Prints the boat's speed and draft as it goes.
/// </summary>
public partial class GroundTour
{
    // The beach the boat step put its boat out from, and the way to the water there (the bay step builds on that sand).
    private (Num.Vector3 Shore, Num.Vector3 ToWater)? _beach;

    private async Task BoatTour()
    {
        if (Client.Placement().Ground is not { } ground)
        {
            throw new InvalidOperationException("boat: no ground to look for the water on");
        }
        // The nearest water a metre deep from where the tour stands (it starts on a beach), and the dry spot before it.
        Num.Vector3 home = Client.Character.Position;
        Num.Vector3 up = Client.Frame.UpAt(home);
        (Num.Vector3 north, Num.Vector3 east) = Facing.Compass(up);
        Num.Vector3? shore = null, water = null;
        float best = float.MaxValue;
        for (int i = 0; i < 32; i++)
        {
            float angle = i * MathF.Tau / 32;
            Num.Vector3 way = north * MathF.Cos(angle) + east * MathF.Sin(angle);
            Num.Vector3 dry = home;
            for (float d = 2; d < 120; d += 1.5f)
            {
                Num.Vector3 bed = ground.Settle(home + way * d);
                if (!ground.InWater(bed, 0.05f))
                {
                    dry = bed;
                    continue;
                }
                if (ground.InWater(bed, -1.5f) && d < best)
                {
                    best = d;
                    shore = dry;
                    water = bed - Client.Frame.UpAt(bed) * ground.HeightAboveWater(bed);
                }
                if (ground.InWater(bed, -1.5f))
                {
                    break;
                }
            }
        }
        if (shore is not { } edge || water is not { } sea)
        {
            throw new InvalidOperationException("boat: no water a metre and a half deep within 120 m of the beach");
        }
        GD.Print($"[tour] boat: water {best:0} m from the beach");

        // The ghost on the water from the shore, then the boat put there, by day where the tour stands.
        await TeleportTo(edge);
        await Settle();
        View.Console.Submit("time 11");
        Num.Vector3 toWater = Num.Vector3.Normalize(sea - edge);
        toWater -= Client.Frame.UpAt(edge) * Num.Vector3.Dot(toWater, Client.Frame.UpAt(edge));
        toWater = Num.Vector3.Normalize(toWater);
        _beach = (edge, toWater);
        View.TurnToward(toWater);
        Admin("give", "motorboat 1");
        await Until(() => SlotOf(Me.Inventory.Main, "motorboat") != null || SlotOf(Me.Inventory.Belt, "motorboat") != null, 5);
        await Hold("motorboat", 5);
        Num.Vector3 aim = Client.Character.Position + toWater * 6.5f;
        aim -= Client.Frame.UpAt(aim) * ground.HeightAboveWater(aim);
        Face(aim);
        await Seconds(0.8);
        await Shot("boat-ghost");
        int ships = Client.Ships().All.Count();
        View.UseHeld();
        await Until(() => Client.Ships().All.Count() > ships, 5);
        RemoteShip boat = Client.Ships().All.FirstOrDefault(s => s.Def?.Key == "motorboat")
            ?? throw new InvalidOperationException("boat: not placed on the water");
        ShipDef def = boat.Def!;
        Me.HeldSlot = -1;
        await Seconds(3);
        Face(boat.Position);
        await Seconds(0.5);
        await Shot("boat-afloat");

        // Into the water beside the helm, and take it.
        byte helm = (byte)def.PartOfSeat(def.Helm.Index);
        Num.Vector3 beside = boat.PointOf(def.PartPoint(helm) + new Num.Vector3(1.25f, 0, 0));
        await TeleportTo(beside);
        await Seconds(2.5); // up to the surface
        Face(boat.PointOf(def.PartPoint(helm)));
        await Seconds(0.3);
        Me.Interact(new TargetRef(TargetFamily.Ship, boat.Id.Value, helm), def.Helm.VerbKey);
        await Until(() => Client.Ships().Piloting, 5);
        if (!Client.Ships().Piloting)
        {
            throw new InvalidOperationException("boat: the helm wasn't taken from the water");
        }
        View.ShipCamera.Mode = ShipCameraMode.Chase;
        View.ShipCamera.Reset();
        await Seconds(1);
        await Shot("boat-helm-orbit");

        // Ahead, turning, the helm's own view, coasting (the HUD's lever shows what the scripted keys hold).
        View.ShipControls.Throttle = 1;
        await Pilot(new ShipInput { Engines = true, Throttle = 1 }, 6);
        ShipState s = Client.Ships().OwnShip;
        GD.Print($"[tour] boat: full ahead {s.Velocity.Length():0.0} m/s, {(s.Has(ShipFlags.Wet) ? "afloat" : "aground")}");
        await Shot("boat-driving", frames: 0);
        await Pilot(new ShipInput { Engines = true, Throttle = 1, Yaw = 1 }, 2);
        await Shot("boat-turning", frames: 0);
        View.ShipCamera.Mode = ShipCameraMode.Cockpit;
        View.ShipCamera.Reset();
        await Pilot(new ShipInput { Engines = true, Throttle = 1 }, 2);
        await Shot("boat-helm", frames: 0);
        View.ShipControls.Throttle = 0;
        await Pilot(new ShipInput { Engines = true }, 8);
        GD.Print($"[tour] boat: coasting 8 s, {Client.Ships().OwnShip.Velocity.Length():0.0} m/s");
        View.Scripted = null;

        // Over the side.
        View.ShipCamera.Mode = ShipCameraMode.Chase;
        Me.LeaveSeat();
        await Until(() => !Client.Ships().Seated, 5);
        await Seconds(1.5);
        Face(boat.Position);
        await Seconds(0.4);
        await Shot("boat-overboard");
        GD.Print($"[tour] boat: {(Client.Ships().Seated ? "still seated" : "overboard")}, " +
            $"{ground.HeightAboveWater(Client.Character.Position):0.00} m from the surface");
    }
}
