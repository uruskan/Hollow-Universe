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
/// The Zamazingo ridden in the game (VEHICLES.md §6.3; <c>--tour-steps bike</c>): set down beside the player, mounted, flat
/// out from standing (the time to 50 km/h), then twenty seconds across the land weaving gently, printing its speed, its
/// slope and how long it stood stuck with the throttle open; pictures parked, pulling away, riding and leaning.
/// </summary>
public partial class GroundTour
{
    private async Task BikeTour()
    {
        var old = Client.Ships().All.Select(s => s.Id).ToHashSet();
        Admin("ship", "zamazingo");
        await Until(() => Client.Ships().All.Any(s => !old.Contains(s.Id) && s.Def != null), 8);
        RemoteShip bike = Client.Ships().All.FirstOrDefault(s => !old.Contains(s.Id) && s.Def != null)
            ?? throw new InvalidOperationException("bike: the Zamazingo was not set down");
        ShipDef def = bike.Def!;
        await Seconds(2);

        // Parked, from its right and a little ahead.
        Num.Vector3 up = Client.Frame.UpAt(bike.Position);
        Num.Vector3 nose = Num.Vector3.Transform(Num.Vector3.UnitZ, bike.Rotation), right = -Num.Vector3.Transform(Num.Vector3.UnitX, bike.Rotation);
        await TeleportTo(bike.Position + nose * 1.5f + right * 3.2f);
        await Settle();
        Face(bike.Position + up * 0.3f);
        await Seconds(0.8);
        await Shot("bike-parked");

        byte seat = (byte)def.PartOfSeat(def.Helm.Index);
        await TeleportTo(bike.PointOf(def.PartPoint(seat) + new Num.Vector3(1.2f, 0, 0)));
        Face(bike.PointOf(def.PartPoint(seat)));
        await Seconds(0.5);
        Me.Interact(new TargetRef(TargetFamily.Ship, bike.Id.Value, seat), def.Helm.VerbKey);
        await Until(() => Client.Ships().Piloting, 5);
        if (!Client.Ships().Piloting)
        {
            throw new InvalidOperationException("bike: the saddle wasn't taken");
        }
        View.ShipCamera.Mode = ShipCameraMode.Chase;
        View.ShipCamera.Reset();
        await Pilot(new ShipInput { Engines = true }, 1.5);

        // Flat out from standing: the time to 50 km/h.
        View.ShipControls.Throttle = 1;
        double start = Time.GetTicksMsec() / 1000.0;
        await Pilot(new ShipInput { Engines = true, Throttle = 1 }, 0.7);
        await Shot("bike-launch", frames: 0);
        await Pilot(new ShipInput { Engines = true, Throttle = 1 }, 8, () => Client.Ships().OwnShip.Velocity.Length() * 3.6f >= 50);
        double to50 = Time.GetTicksMsec() / 1000.0 - start;
        GD.Print(FormattableString.Invariant($"[tour] bike: 0-50 km/h in {to50:0.00} s (with 0.7 s of picture-taking)"));

        // Twenty seconds weaving across the land.
        double began = Time.GetTicksMsec() / 1000.0, next = 1, stuck = 0, top = 0, last = began;
        Num.Vector3 from = Client.Ships().OwnShip.Position;
        float travelled = 0;
        bool riding = false, leaning = false;
        while (Time.GetTicksMsec() / 1000.0 - began < 20 && Client.Ships().Piloting)
        {
            double t = Time.GetTicksMsec() / 1000.0 - began;
            float yaw = 0.45f * MathF.Sin((float)t * 0.9f);
            View.Scripted = new InputFrame { Ship = new ShipInput { Engines = true, Throttle = 1, Yaw = yaw } };
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            double now = Time.GetTicksMsec() / 1000.0;
            ShipState s = Client.Ships().OwnShip;
            float speed = s.Velocity.Length();
            top = Math.Max(top, speed);
            travelled += Num.Vector3.Distance(from, s.Position);
            from = s.Position;
            stuck += speed < 1 ? now - last : 0;
            last = now;
            if (t >= next)
            {
                next += 1;
                Num.Vector3 u = Client.Frame.UpAt(s.Position);
                float pitch = MathF.Asin(Math.Clamp(Num.Vector3.Dot(s.Forward, u), -1, 1)) * 180 / MathF.PI;
                float roll = MathF.Asin(Math.Clamp(Num.Vector3.Dot(s.Right, u), -1, 1)) * 180 / MathF.PI;
                GD.Print(FormattableString.Invariant($"[tour] bike t={t:0}s {speed * 3.6f:0} km/h pitch {pitch:0}° roll {roll:0}° {s.Flags}"));
            }
            if (!riding && t > 3)
            {
                riding = true;
                await Shot("bike-riding", frames: 0);
            }
            if (!leaning && t > 6 && MathF.Abs(yaw) > 0.4f)
            {
                leaning = true;
                await Shot("bike-leaning", frames: 0);
            }
        }
        GD.Print(FormattableString.Invariant(
            $"[tour] bike: 20 s across the land: {travelled:0} m, top {top * 3.6f:0} km/h, stuck {stuck:0.0} s, hull {Client.Ships().OwnShip.Health:0}"));
        View.ShipControls.Throttle = 0;
        await Pilot(new ShipInput { Engines = true, Throttle = -1 }, 3);
        View.Scripted = null;
        Me.LeaveSeat();
        await Until(() => !Client.Ships().Seated, 5);
    }
}
