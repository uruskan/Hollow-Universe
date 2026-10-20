using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using HollowUniverse.Ships;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// The Monster TOGG and its effects in the game (VEHICLES.md §9.9; <c>--tour-steps monster</c>): set down beside the
/// player, parked; its driver's seat taken and its engine started (the start's black puff, then the idle haze); pulling
/// away flat out (soot from its stacks, its tyres spinning up dust); at speed, and turning (dust behind each tyre). Prints
/// its speed as it goes.
/// </summary>
public partial class GroundTour
{
    private async Task MonsterTour()
    {
        var old = Client.Ships().All.Select(s => s.Id).ToHashSet();
        Admin("ship", "monster-togg");
        await Until(() => Client.Ships().All.Any(s => !old.Contains(s.Id) && s.Def != null), 8);
        RemoteShip car = Client.Ships().All.FirstOrDefault(s => !old.Contains(s.Id) && s.Def != null)
            ?? throw new InvalidOperationException("monster: the Monster TOGG was not set down");
        ShipDef def = car.Def!;
        await Seconds(2);

        // Parked, from a little ahead and to its right (its stacks' side).
        Num.Vector3 up = Client.Frame.UpAt(car.Position);
        Num.Vector3 nose = Num.Vector3.Transform(Num.Vector3.UnitZ, car.Rotation), right = -Num.Vector3.Transform(Num.Vector3.UnitX, car.Rotation);
        await TeleportTo(car.Position + nose * 6f + right * 6.5f);
        await Settle();
        Face(car.Position + up * 0.8f);
        await Seconds(0.8);
        await Shot("monster-parked");

        // Into the driver's seat; the engine started (its puff), then idling.
        byte seat = (byte)def.PartOfSeat(def.Helm.Index);
        await TeleportTo(car.PointOf(def.PartPoint(seat) + new Num.Vector3(1.6f, 0, 0)));
        Face(car.PointOf(def.PartPoint(seat)));
        await Seconds(0.5);
        void Refused(IntentAnswer a) => GD.Print($"[tour] monster: refused {a.Code} {a.Detail}");
        Me.IntentRejected += Refused;
        GD.Print($"[tour] monster: {def.Helm.VerbKey} at part {seat}, " +
            $"{Num.Vector3.Distance(Client.Character.Position, car.PointOf(def.PartPoint(seat))):0.0} m from it");
        // The cab's door first (the console's "hatch open" is the ship's own door 0, not a module's).
        Me.Interact(new TargetRef(TargetFamily.Ship, car.Id.Value, seat), "hatch-open");
        await Seconds(1);
        Me.Interact(new TargetRef(TargetFamily.Ship, car.Id.Value, seat), def.Helm.VerbKey);
        await Until(() => Client.Ships().Piloting, 5);
        Me.IntentRejected -= Refused;
        if (!Client.Ships().Piloting)
        {
            throw new InvalidOperationException("monster: the driver's seat wasn't taken");
        }
        View.ShipCamera.Mode = ShipCameraMode.Chase;
        View.ShipCamera.Reset();
        await Seconds(1);
        await Pilot(new ShipInput { Engines = true }, 0.35);
        await Shot("monster-start", frames: 0);
        await Pilot(new ShipInput { Engines = true }, 3);
        await Shot("monster-idle", frames: 0);

        // Flat out from standing: soot and spinning tyres, then at speed and turning.
        View.ShipControls.Throttle = 1;
        await Pilot(new ShipInput { Engines = true, Throttle = 1 }, 0.9);
        await Shot("monster-launch", frames: 0);
        await Pilot(new ShipInput { Engines = true, Throttle = 1 }, 3);
        GD.Print($"[tour] monster: {Client.Ships().OwnShip.Velocity.Length() * 3.6f:0} km/h after 4 s flat out");
        await Shot("monster-driving", frames: 0);
        await Pilot(new ShipInput { Engines = true, Throttle = 1, Yaw = 1 }, 1.5);
        await Shot("monster-turning", frames: 0);
        View.ShipControls.Throttle = 0;
        await Pilot(new ShipInput { Engines = true, Throttle = -1 }, 3);
        GD.Print($"[tour] monster: braked to {Client.Ships().OwnShip.Velocity.Length() * 3.6f:0} km/h");
        View.Scripted = null;
        Me.LeaveSeat();
        await Until(() => !Client.Ships().Seated, 5);
    }
}
