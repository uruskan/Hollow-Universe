using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using HollowUniverse.Fx;
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
    // Drives flat out ahead for some seconds and prints the frame times (mean, the slowest twentieth, the worst).
    private async Task DriveTimed(string label, double seconds)
    {
        var frames = new System.Collections.Generic.List<double>();
        View.Timings.Clear();
        View.Slowest.Clear();
        double until = Time.GetTicksMsec() / 1000.0 + seconds;
        ulong last = Time.GetTicksUsec(), secondFrom = last;
        var second = new System.Collections.Generic.List<double>();
        long corrections = Client.Ships().ShipPrediction?.Corrections ?? 0, replayed = Client.Ships().ShipPrediction?.Replayed ?? 0;
        while (Time.GetTicksMsec() / 1000.0 < until)
        {
            View.Scripted = new InputFrame { Ship = new ShipInput { Engines = true, Throttle = 1 } };
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong now = Time.GetTicksUsec();
            frames.Add((now - last) / 1000.0);
            last = now;
            second.Add(frames[^1]);
            if (now - secondFrom > 1_000_000)
            {
                // Each second: the speed, the frames, and Godot's own counters (process and physics time, what was drawn).
                GD.Print($"[tour] monster:   {Client.Ships().OwnShip.Velocity.Length():0} m/s, {second.Average():0.0} ms a frame " +
                    $"(worst {second.Max():0}), process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.0} ms, " +
                    $"physics {Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:0.0} ms, " +
                    $"{Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0} draws, " +
                    $"{Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1e6:0.00} M triangles, " +
                    $"{Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0} nodes; " +
                    $"{(Client.Ships().ShipPrediction?.Corrections ?? 0) - corrections} corrections (largest {Client.Ships().LargestCorrection:0.00} m), " +
                    $"{(Client.Ships().ShipPrediction?.Replayed ?? 0) - replayed} ticks replayed");
                corrections = Client.Ships().ShipPrediction?.Corrections ?? 0;
                replayed = Client.Ships().ShipPrediction?.Replayed ?? 0;
                Client.Ships().LargestCorrection = 0;
                second.Clear();
                secondFrom = now;
            }
        }
        frames.Sort();
        double mean = frames.Count > 0 ? frames.Average() : 0;
        double p95 = frames.Count > 0 ? frames[(int)(frames.Count * 0.95)] : 0;
        GD.Print($"[tour] monster: {label}: {frames.Count} frames, {mean:0.0} ms mean, {p95:0.0} ms at 95 %, " +
            $"{(frames.Count > 0 ? frames[^1] : 0):0.0} ms worst, {Client.Ships().OwnShip.Velocity.Length():0} m/s");
        // The game view's own parts (GroundView.Time), the costliest first: what a frame spends where.
        GD.Print("[tour] monster:   " + string.Join(", ", View.Timings.OrderByDescending(p => p.Value).Take(8)
            .Select(p => $"{p.Key} {p.Value / Math.Max(frames.Count, 1):0.0} ms (worst {System.Collections.Generic.CollectionExtensions.GetValueOrDefault(View.Slowest, p.Key):0})")));
    }

    private async Task MonsterTour()
    {
        // HOLLOW_TOUR_NO_FX=1: the whole step without the effects (FxDirector draws no batch), to time what they cost.
        if (System.Environment.GetEnvironmentVariable("HOLLOW_TOUR_NO_FX") == "1")
        {
            FxDirector.Instance.OnlyBatch = "(none)";
        }
        var old = Client.Ships().All.Select(s => s.Id).ToHashSet();
        Admin("ship", "monster-togg");
        await Until(() => Client.Ships().All.Any(s => !old.Contains(s.Id) && s.Def != null), 8);
        RemoteShip car = Client.Ships().All.FirstOrDefault(s => !old.Contains(s.Id) && s.Def != null)
            ?? throw new InvalidOperationException("monster: the Monster TOGG was not set down");
        ShipDef def = car.Def!;
        // HOLLOW_TOUR_TWO=1: a second one set down beside the first, as two spawns in a row do.
        if (System.Environment.GetEnvironmentVariable("HOLLOW_TOUR_TWO") == "1")
        {
            int before = Client.Ships().All.Count();
            Admin("ship", "monster-togg");
            await Until(() => Client.Ships().All.Count() > before, 8);
        }
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
        await DriveTimed(FxDirector.Instance.OnlyBatch == null ? "pulling away, with effects" : "pulling away, without effects", 6);
        GD.Print($"[tour] monster: {Client.Ships().OwnShip.Velocity.Length() * 3.6f:0} km/h after 7 s flat out");
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
