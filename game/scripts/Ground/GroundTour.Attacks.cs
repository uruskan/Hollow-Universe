using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using HollowUniverse.Ships;
using HollowUniverse.UI;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

// Real hosted-client regression: ordinary seating, magazine inventory, loaders and repeated trigger presses.
// Run against a fresh isolated save: --tour <folder> --tour-steps attack-sync [--lag 180 --jitter 35 --loss 15].
public partial class GroundTour
{
    // A separate fresh-save step: one ordinary shoulder-launcher round, rapid trigger edges,
    // immediate local presentation, then the reliable authoritative launch/endpoint and cleanup.
    private async Task AttackLauncher()
    {
        var shown = new List<ShotEvent>();
        ClientProjectile? predicted = null;
        Task? launchCapture = null;
        Task? endpointCapture = null;
        void CaptureEndpoint()
        {
            if (predicted?.EndedAt != null) endpointCapture ??= Shot("attack-launcher-endpoint", frames: 0);
        }
        ushort launcher = Client.Content.ItemByKey("rocket-launcher").Id;
        void Fired(ShotEvent shot)
        {
            if (shot.Weapon.Id != launcher) return;
            shown.Add(shot);
            predicted ??= Client.Survival().Projectiles.Single(p => p.Own && p.Attack == shot.Attack);
            launchCapture ??= Shot("attack-launcher-release", frames: 0);
        }
        try
        {
            await RepairGod(true);
            AttackCheck(Count("rocket") == 0 && Count("rocket-launcher") == 0,
                "launcher step starts without reserve rockets or an existing launcher");
            Admin("give", "rocket-launcher 1");
            Admin("give", "rocket 1");
            await AttackWait(() => Count("rocket-launcher") == 1 && Count("rocket") == 1, 5,
                "exactly one launcher and one round replicate");
            MoveToBelt("rocket-launcher", 5);
            await AttackWait(() => Me.Inventory.Belt[5].Item == launcher, 5, "launcher reaches belt slot");
            Me.HeldSlot = 5;
            await AttackWait(() => Me.Gun.State.Item == launcher && Me.Gun.Ready, 5, "launcher equip finishes");
            Me.Gun.Reload();
            await AttackWait(() => Me.Gun.Loaded == 1 && !Me.Gun.Reloading && Me.Gun.PendingCount == 0 && Count("rocket") == 0,
                12, "ordinary reload confirms one chambered round and no reserve");
            View.Interface.Open(GroundScreen.Hud);
            Num.Vector3 up = Client.Frame.UpAt(Client.Character.Position);
            // Aim down into nearby terrain so the authority endpoint is observable within this bounded run.
            Num.Vector3 view = Num.Vector3.Normalize(Forward() - up * 0.3f);
            InputFrame Aim(bool fire = false) => new()
                { View = view, ViewFrame = 0, Primary = fire, PrimaryPressed = fire, Secondary = true };
            View.LookToward(view);
            View.Scripted = Aim();
            await Seconds(0.5);
            await Shot("attack-launcher-loaded");
            Me.Gun.ShotFired += Fired;
            View.Scripted = Aim(true);
            await AttackWait(() => shown.Count > 0, 2, "ordinary trigger immediately presents the rocket");
            View.Scripted = Aim();
            if (launchCapture != null) await launchCapture;
            for (int click = 0; click < 20; click++)
            {
                View.Scripted = Aim(true);
                await Seconds(0.025);
                CaptureEndpoint();
                View.Scripted = Aim();
                await Seconds(0.025);
                CaptureEndpoint();
            }
            await AttackWait(() =>
                {
                    CaptureEndpoint();
                    return Me.Gun.PendingCount == 0 && Client.Attacks.PendingCount == 0 &&
                        predicted is { LaunchConfirmed: true, EndedAt: not null };
                },
                8, "launcher attempt resolves and receives its authoritative endpoint");
            AttackCheck(shown.Count == 1, $"one chambered round produces exactly one visual launch under spam: {shown.Count}");
            AttackCheck(Client.Attacks.TryGet(shown[0].Attack, out AttackRecord record) &&
                record.Resolution is { Code: OutcomeCode.Done, Cancelled: false }, "the server accepts that same attack identity");
            AttackCheck(predicted is { Id: > 0, LaunchConfirmed: true } && predicted.Attack == shown[0].Attack,
                "the authoritative launch corrects the original predicted projectile");
            AttackCheck(Me.Gun.Loaded == 0 && Me.Gun.Reserve == 0 && Me.Inventory.Belt[5].Loaded == 0,
                "predicted and replicated ammunition settle empty without a second round");
            AttackCheck(predicted!.PositionAt(Client.ClockSeconds) == predicted.EndedAt,
                "the visual projectile stops at its authoritative endpoint");
            if (endpointCapture != null) await endpointCapture;
            await AttackWait(() => !Client.Survival().Projectiles.Any(p => p.Own && p.Attack == shown[0].Attack),
                3, "the finished projectile retires without an orphaned effect");
            GD.Print($"[attack-sync] PASS launcher clicks=21 visual=1 authority=1 pending=0 reserve=0 attack={shown[0].Attack} endpoint={predicted.EndedAt}");
        }
        catch
        {
            View.Scripted = new InputFrame();
            await Shot("attack-launcher-failed");
            throw;
        }
        finally
        {
            Me.Gun.ShotFired -= Fired;
            View.Scripted = null;
        }
    }

    private async Task AttackSync()
    {
        StationModule module = Client.Stations();
        var shown = new List<StationShotView>();
        var resolved = new List<AttackResolution>();
        void OnShot(StationShotView shot) { if (shot.Own) shown.Add(shot); }
        void OnResolved(AttackResolution result) { if (result.Id.Kind == AttackDomain.Station) resolved.Add(result); }
        bool observing = false;
        try
        {
            await RepairGod(true);
            await TeleportTo(Offset(70, 0));
            var old = Client.Ships().All.Select(s => s.Id).ToHashSet();
            Admin("ship", "iron-wake");
            await AttackWait(() => Client.Ships().All.Any(s => !old.Contains(s.Id) && s.Def is { Stations.Count: > 0 }), 8,
                "fresh Iron Wake replicates");
            RemoteShip ship = Client.Ships().All.First(s => !old.Contains(s.Id) && s.Def is { Stations.Count: > 0 });
            ShipDef def = ship.Def!;
            ShipInterior interior = def.Interior!;
            ShipStationDef station = def.Stations.First(s => s.Id == "Siege_Forward");
            ShipSeatDef seat = def.Seats[station.Seat];
            Num.Vector3 entry = ship.PointOf(interior.SocketAt(seat.Exits[0], interior.Rest));
            await TeleportTo(entry);
            View.Console.Submit("noclip");
            await AttackWait(() => Client.Character.Has(MoveCheats.Noclip), 5, "setup reaches the cannon's authored deck entry");
            double approachAt = Time.GetTicksMsec() / 1000.0;
            while (Num.Vector3.Distance(Client.RenderPosition, entry) > 0.25f && Time.GetTicksMsec() / 1000.0 - approachAt < 15)
            {
                entry = ship.PointOf(interior.SocketAt(seat.Exits[0], interior.Rest));
                View.Scripted = new InputFrame { View = Num.Vector3.Normalize(entry - Client.RenderPosition), ViewFrame = 0, Forward = 1, NoclipSpeed = 3 };
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            View.Scripted = new InputFrame();
            AttackCheck(Num.Vector3.Distance(Client.RenderPosition, entry) < 0.6f, "setup arrived at deck entry");
            byte part = (byte)def.PartOfSeat(seat.Index);
            Face(ship.PointOf(def.PartPoint(part)));
            Me.Interact(new TargetRef(TargetFamily.Ship, ship.Id.Value, part), seat.VerbKey);
            await AttackWait(() => module.Own is { } own && own.Ship.Id == ship.Id && own.Station.Index == station.Index, 5,
                "ordinary interaction seats the gunner in the forward siege battery");
            View.Console.Submit("noclip");
            await AttackWait(() => !Client.Character.Has(MoveCheats.Noclip), 5, "gunner is in normal seated movement state");

            StationState Record() => module.Records(ship)![station.Index];
            AttackCheck(Record().Shots == 0 && Record().Magazine.IsEmpty && Record().Chambers.All(c => c == 0), "fresh cannon begins empty");
            AttackCheck(Count("rocket") == 0, "fresh test inventory contains no reserve rockets");
            Admin("give", "rocket 2");
            await AttackWait(() => Count("rocket") == 2, 5, "exactly two rockets arrive in the inventory");
            Me.OpenMagazine(ship.Id);
            await AttackWait(() => Me.LootTarget == ship.Id && Me.Loot != null, 5, "ordinary magazine inventory opens");
            int slot = SlotOf(Me.Inventory.Main, "rocket") ?? throw new InvalidOperationException("attack-sync: rocket stack is absent from the main inventory");
            Me.QuickMove(ContainerRef.Main, slot);
            await AttackWait(() => Count("rocket") == 0, 5, "the two-round stack transfers into the cannon magazine");
            Me.Close();
            View.Interface.Open(GroundScreen.Hud);
            View.ShipCamera.Mode = ShipCameraMode.Cockpit;
            View.ShipCamera.Reset();
            Num.Vector3 view = Num.Vector3.Normalize(Num.Vector3.Transform(new Num.Vector3(0, 0.174f, 0.985f), ship.Rotation));
            InputFrame Aim(bool fire = false) => new() { View = view, ViewFrame = 0, Station = new StationInput { Fire = fire } };
            View.Scripted = Aim();
            View.LookToward(view);
            await AttackWait(() => Record().Loaded(0) && Record().Loaded(1) && Record().Magazine.IsEmpty, 12,
                "both loaders finish with two chambered rounds and no reserve ammunition");
            var parts = new float[interior.Parts.Count];
            bool Aligned()
            {
                if (module.Display(ship, station.Index) is not { Limit: false } state) return false;
                ship.PartsInto(interior, parts, Client.PredictedTick);
                StationRules.Into(state, parts);
                Num.Vector3 barrel = Num.Vector3.Transform(StationRules.BarrelDirection(interior, station, parts), ship.Rotation);
                return Num.Vector3.Dot(barrel, view) >= MathF.Cos(station.Battery.Mount.Alignment);
            }
            await AttackWait(Aligned, 5, "loaded mount aligns before trigger input");
            await Shot("attack-sync-loaded-two-rounds");
            long confirmedBefore = module.ShotsConfirmed, undoneBefore = module.ShotsUndone;
            module.Shot += OnShot;
            Client.Attacks.Resolved += OnResolved;
            observing = true;

            View.Scripted = Aim(true);
            await AttackWait(() => shown.Count > 0, 2, "first ordinary trigger press presents a shot");
            View.Scripted = Aim();
            await Shot("attack-sync-first-shot", 0);
            await Seconds(0.1);
            // Repeated press/release edges exercise the original two-barrel/three-visual failure window.
            for (int click = 0; click < 10; click++)
            {
                View.Scripted = Aim(true);
                await Seconds(0.065);
                View.Scripted = Aim();
                await Seconds(0.065);
            }
            await AttackWait(() => module.PendingShots == 0 && Record().Shots == 2, 8,
                $"all cannon attempts settle (visuals={shown.Count}, refusal={module.Refused.Code})");
            AttackResolution[] accepted = resolved.Where(r => r.Code == OutcomeCode.Done && !r.Cancelled).ToArray();
            AttackCheck(shown.Count == 2 && shown.Select(s => s.Attack).Distinct().Count() == 2, $"two rounds produce exactly two unique visual shots: {shown.Count}");
            AttackCheck(accepted.Length == 2 && Record().Shots == 2, $"authority accepted exactly two attempts: {accepted.Length}");
            foreach (StationShotView shot in shown)
            {
                AttackResolution result = accepted.Single(r => r.Id == shot.Attack);
                AttackCheck(result.Barrel == shot.Barrel, $"attempt {shot.Attack.Sequence} uses the same predicted and authoritative barrel {shot.Barrel}");
            }
            AttackCheck(shown[0].Barrel == 0 && shown[1].Barrel == 1, "the two shots use the two separate barrels in order");
            AttackCheck(module.ShotsConfirmed - confirmedBefore == 2 && module.ShotsUndone == undoneBefore, "both visual predictions confirm without a withdrawal");
            AttackCheck(Record().Chambers.All(c => c == 0) && Record().Magazine.IsEmpty && Count("rocket") == 0, "no spare round can reload the cannon during the test");
            AttackCheck(!Client.Survival().Projectiles.Any(p => p.Own && p.Attack.Kind == AttackDomain.Station && p.Id == 0), "no unnamed predicted rocket remains");
            await Shot("attack-sync-two-rounds-spam");
            GD.Print($"[attack-sync] PASS visual=2 authority=2 barrels=0,1 confirmed=2 pending={module.PendingShots} refused={resolved.Count - accepted.Length} reserve=0");
        }
        catch
        {
            View.Scripted = new InputFrame();
            await Shot("attack-sync-failed");
            throw;
        }
        finally
        {
            if (observing)
            {
                module.Shot -= OnShot;
                Client.Attacks.Resolved -= OnResolved;
            }
            View.Scripted = null;
        }
    }

    private async Task AttackWait(Func<bool> ready, double seconds, string requirement)
    {
        await Until(ready, seconds);
        AttackCheck(ready(), requirement + $" (within {seconds:0.#}s)");
    }

    private static void AttackCheck(bool okay, string requirement)
    {
        if (!okay) throw new InvalidOperationException("attack-sync: " + requirement);
        GD.Print("[attack-sync] check: " + requirement);
    }
}
