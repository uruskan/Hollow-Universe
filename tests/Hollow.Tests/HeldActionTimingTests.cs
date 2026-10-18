using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

public static class HeldActionTimingTests
{
    [Test]
    public static void PresentationUsesTheSameMeleeContactTickAsAuthority()
    {
        var melee = new MeleeStats { SwingSeconds = 0.8f, StrikeSeconds = 0.28f };
        Assert.Equal(24, melee.SwingTicks, "swing tick count");
        Assert.Equal(8, melee.StrikeTicks, "strike tick count");
        float duration = HeldActionTiming.Seconds(melee.SwingTicks);
        float contact = duration * HeldActionTiming.StrikeFraction(melee);
        Assert.True(Math.Abs(contact - 8f / 30f) < 0.00001f, "presentation contact lands on authoritative strike tick");
        Assert.True(Math.Abs(contact - 0.28f) > 0.01f, "does not accidentally use the unrounded authored seconds");
        var weapon = new ItemDef { Firearm = new FirearmStats { ReloadSeconds = 0.28 } };
        Assert.Equal(8f / 30f, HeldActionTiming.Duration(weapon, "reload", 99), "reload ignores art duration and uses the same rounded simulation ticks");
    }

    [Test]
    public static void AuthoredContactCanBeRetimedWithoutMovingItsPose()
    {
        const float authored = 0.45f, gameplay = 1f / 3f;
        Assert.True(Math.Abs(HeldActionTiming.SamplePhase(gameplay, authored, gameplay) - authored) < 0.00001f, "contact pose preserved");
        Assert.Equal(0f, HeldActionTiming.SamplePhase(0f, authored, gameplay), "starts at first pose");
        Assert.Equal(1f, HeldActionTiming.SamplePhase(1f, authored, gameplay), "ends at last pose");
        float previous = -1;
        for (int i = 0; i <= 100; i++)
        {
            float now = HeldActionTiming.SamplePhase(i / 100f, authored, gameplay);
            Assert.True(now >= previous, "retiming must not run the animation backwards");
            previous = now;
        }
    }

    [Test]
    public static void MeleePredictionUsesTicksAndCancelsPendingContacts()
    {
        var item = new ItemDef { Id = 24, Key = "metal-hatchet", Melee = new MeleeStats { SwingSeconds = 0.8f, StrikeSeconds = 0.28f } };
        var clock = new LocalMelee();
        var starts = new List<uint>();
        int contacts = 0;
        clock.Started += _ => starts.Add(clock.ActionTick);
        clock.StrikeReached += _ => contacts++;
        for (uint tick = 100; tick <= 116; tick++) clock.Tick(tick, item, 0, true, true, 0.3);
        Assert.True(starts.SequenceEqual(new uint[] { 109 }), "waits nine equip ticks before first swing");
        Assert.Equal(0, contacts, "no impact before eight strike ticks");
        clock.Tick(117, item, 0, true, true, 0.3);
        Assert.Equal(1, contacts, "exact strike tick");
        for (uint tick = 118; tick <= 133; tick++) clock.Tick(tick, item, 0, true, true, 0.3);
        Assert.True(starts.SequenceEqual(new uint[] { 109, 133 }), "repeat exactly after 24 ticks");
        clock.Tick(134, item, 0, true, false, 0.3);
        for (uint tick = 135; tick <= 142; tick++) clock.Tick(tick, item, 0, false, true, 0.3);
        Assert.Equal(1, contacts, "wounding/seating cancels the pending strike without late replay");
        clock.Tick(143, item, 1, true, true, 0.3);
        for (uint tick = 144; tick <= 156; tick++) clock.Tick(tick, item, 1, true, true, 0.3);
        Assert.Equal(2, starts.Count, "switching slots retains the server's attack cooldown");
        clock.Tick(157, item, 1, true, true, 0.3);
        Assert.Equal((uint)157, starts.Last(), "next allowed attack after equip and cooldown");
    }

    [Test]
    public static void HeldWindupCommitsOnceAtTheRuleTickAndCancellationSendsNothing()
    {
        foreach (string action in new[] { "throw", "eat", "bandage", "inject", "repair", "use" })
        {
            var queue = new HeldActionQueue();
            int sends = 0;
            float duration = HeldActionTiming.Duration(null, action, 1f);
            uint commit = 100 + (uint)CharacterTicks.From(HeldActionTiming.AnticipationSeconds(action));
            Assert.True(queue.Schedule(100, 42, 2, action, duration, () => { sends++; return 7; }), action + " queued");
            Assert.True(!queue.Schedule(100, 42, 2, action, duration, () => 8), "a second click cannot replace pending action");
            Assert.Equal(0u, queue.Step(commit - 1, 42, 2, true), action + " sends nothing before commit");
            Assert.Equal(7u, queue.Step(commit, 42, 2, true), action + " sends at exact rule tick");
            Assert.Equal(0u, queue.Step(commit + 1, 0, 2, true), "final-stack disappearance cannot resend");
            Assert.Equal(1, sends, action + " sent exactly once");
            Assert.True(!queue.Pending, "committed queue cleared");
        }
        for (int interruption = 0; interruption < 4; interruption++)
        {
            var queue = new HeldActionQueue();
            int sends = 0;
            queue.Schedule(100, 42, 2, "bandage", 2.3f, () => { sends++; return 1; });
            if (interruption == 0) queue.Cancel();
            else queue.Step(101, interruption == 1 ? (ushort)43 : (ushort)42,
                interruption == 2 ? 3 : 2, interruption != 3);
            Assert.Equal(0u, queue.Step(200, 42, 2, true), "cancelled intent never commits later");
            Assert.Equal(0, sends, "explicit/UI, item, slot or unavailable interruption sent nothing");
        }
        var refused = new HeldActionQueue();
        int refusals = 0;
        refused.Schedule(1, 42, 2, "repair", 0.65f, () => { refusals++; return 0; });
        Assert.Equal(0u, refused.Step(30, 42, 2, true), "refused dispatch returns no sequence");
        refused.Step(31, 42, 2, true);
        Assert.True(!refused.Pending && refusals == 1, "refused callback is never retried");
        var hitch = new HeldActionQueue();
        hitch.Schedule(100, 42, 2, "throw", 1f, () => 17);
        Assert.Equal(17u, hitch.Step(115, 42, 2, true), "a frame crossing the commit tick dispatches once and retains its refusal correlation sequence");
        Assert.Equal(0u, hitch.Step(116, 42, 2, true), "catch-up cannot dispatch the intent twice");
    }

    [Test]
    public static void MeleeAuthorityMissesOutsideReachAndCancelsBeforeContact()
    {
        foreach (string scenario in new[] { "hit", "far", "slot", "wounded" })
        {
            using var rig = new GameRig();
            Entity user = rig.Add("User", 30, 0);
            Entity target = rig.Add("Target", 30, scenario == "far" ? -4 : -1.2f);
            rig.Clear(rig.Ch(user).State.Position, rig.Ch(target).State.Position);
            rig.Give(user, "metal-hatchet", 1);
            rig.Zone.Actions().MoveItem(user, ContainerRef.Main, 0, ContainerRef.Belt, 2, 0, 0);
            rig.Step(15, () => rig.Ch(user).Actions = new ActionInput { HeldSlot = 2 });
            Vector3 at = rig.Ch(target).State.Position;
            rig.Look(user, at + rig.Zone.Frame.UpAt(at) * 1.15f);
            rig.Zone.Events.Clear();
            rig.Step(1, () => rig.Ch(user).Actions = new ActionInput { HeldSlot = 2, Buttons = ActionButtons.Primary });
            Player p = rig.P(user);
            uint started = p.LastActionTick;
            Assert.Equal(ActionKind.Swing, p.LastAction, "accepted swing recorded before contact");
            if (scenario == "wounded") p.Wounded = true;
            rig.Step(8, () => rig.Ch(user).Actions = new ActionInput { HeldSlot = (byte)(scenario == "slot" ? 3 : 2) });
            HitConfirmed[] hits = rig.Zone.Events.Of<HitConfirmed>().Where(h => h.By == p.Id).ToArray();
            Assert.Equal(scenario == "hit" ? 1 : 0, hits.Length, scenario + " authoritative contacts");
            if (scenario == "hit")
            {
                Assert.Equal(rig.P(target).Id, hits[0].Victim, "near target is damaged");
                Assert.Equal(rig.C.ItemByKey("metal-hatchet").Id, hits[0].Weapon, "confirmed contact identifies the actual tool");
                Assert.Equal((long)started + 8, rig.Zone.Tick, "contact occurs eight ticks after the recorded action");
                Assert.True(Vector3.Distance(hits[0].Point, rig.Zone.EyeOf(rig.Ch(user).State)) <= 1.7f, "contact point within tool reach");
            }
            else Assert.Equal(100f, rig.P(target).Vitals.Health, scenario + " target is undamaged");
            if (scenario == "slot") Assert.Equal(ActionKind.None, p.LastAction, "slot cancellation is replicated as an action boundary");
        }
    }

    [Test]
    public static void SwitchingIdenticalGunsReplicatesReloadCancellationWithoutLoadingEitherOne()
    {
        using var rig = new LoopbackRig();
        GameClient user = rig.Join("User"), observer = rig.Join("Observer");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport User 0 0");
        rig.Server.Execute("teleport Observer 3 0");
        rig.Server.Execute("give User pistol 2");
        rig.Server.Execute("give User pistol-ammo 30");
        ItemDef pistol = user.Content.ItemByKey("pistol"), ammo = user.Content.ItemByKey("pistol-ammo");
        Loopback.RunUntil(rig, () => user.Local.Inventory.Count(pistol) == 2 && user.Local.Inventory.Count(ammo) == 30, "two guns and reserve rounds received");
        foreach (int belt in new[] { 2, 3 })
        {
            int source = Enumerable.Range(0, 24).First(i => user.Local.Inventory.Main[i].Item == pistol.Id);
            user.Local.MoveItem(ContainerRef.Main, source, ContainerRef.Belt, belt);
            Loopback.RunUntil(rig, () => user.Local.Inventory.Belt[belt].Item == pistol.Id, "gun placed in distinct belt slot");
        }
        user.Local.HeldSlot = 2;
        Loopback.RunUntil(rig, () => user.Local.Gun.Ready && rig.Server.Inspect(sv =>
        {
            sv.Zone.Players().TryFindPlayer(user.EntityId, out _, out Player p);
            return p.HeldSlot == 2 && p.HeldItem == pistol.Id && sv.Zone.Tick >= p.Gun.EquipReadyTick;
        }), "both sides have equipped the first pistol before requesting reload");
        user.Local.Reload();
        Loopback.RunUntil(rig, () => observer.Entities.Any(e => e.NetId == user.NetId && e.Action == ActionKind.Reload), "observer sees reload start");
        RemoteEntity remote = observer.Entities.Single(e => e.NetId == user.NetId);
        byte reloadCount = remote.ActionCount;
        user.Local.HeldSlot = 3;
        Loopback.RunUntil(rig, () => remote.ActionCount != reloadCount && remote.Action == ActionKind.None && remote.Held == pistol.Id,
            "same item identity still carries explicit cancellation");
        Assert.Equal(pistol.Id, remote.ActionItem, "cancellation identifies newly selected identical gun");
        Assert.True(rig.Server.Inspect(sv =>
        {
            sv.Zone.Players().TryFindPlayer(user.EntityId, out _, out Player p);
            return p.HeldSlot == 3 && !p.Gun.Reloading;
        }), "authoritative reload was cancelled by the slot change");
        rig.Run(pistol.Firearm!.ReloadSeconds + 0.2, (_, _) => default);
        Assert.Equal(0, (int)user.Local.Inventory.Belt[2].Loaded, "put-away gun does not finish its cancelled reload");
        Assert.Equal(0, (int)user.Local.Inventory.Belt[3].Loaded, "new gun does not inherit old reload");
        Assert.Equal(30, user.Local.Inventory.Count(ammo), "cancellation consumes no reserve rounds");
    }

    [Test]
    public static void MeleeHitMarkerCarriesTheAuthoritativeToolTickAndContactPoint()
    {
        using var rig = new LoopbackRig();
        GameClient user = rig.Join("User"), target = rig.Join("Target");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport User 30 0");
        rig.Server.Execute("teleport Target 30 -1.2");
        rig.Server.Execute("give User metal-hatchet 1");
        ItemDef tool = user.Content.ItemByKey("metal-hatchet");
        Loopback.RunUntil(rig, () => user.Local.Inventory.Count(tool) == 1, "hatchet received");
        int slot = Enumerable.Range(0, 24).First(i => user.Local.Inventory.Main[i].Item == tool.Id);
        user.Local.MoveItem(ContainerRef.Main, slot, ContainerRef.Belt, 2);
        user.Local.HeldSlot = 2;
        rig.Run(0.6, (_, _) => default);
        var authority = new ConcurrentQueue<(HitConfirmed Hit, uint Tick)>();
        Vector3 strikeDirection = rig.Server.Inspect(sv =>
        {
            sv.Zone.Players().TryFindPlayer(target.EntityId, out Entity e, out _);
            Vector3 at = sv.Zone.Entities.Get<Character>(e).State.Position;
            var objects = new List<NaturalObject>();
            sv.Zone.Nature!.Query(at, 5, objects);
            foreach (NaturalObject o in objects)
                sv.Zone.Natural!.Apply(o.Id, new NaturalOverride { Depleted = true, RespawnAtUnixMs = long.MaxValue });
            sv.Zone.Events.On<HitConfirmed>((in HitConfirmed hit) =>
            {
                if (hit.By != user.EntityId) return;
                sv.Zone.Players().TryFindPlayer(user.EntityId, out _, out Player p);
                authority.Enqueue((hit, p.LastActionTick));
            });
            sv.Zone.Players().TryFindPlayer(user.EntityId, out Entity userEntity, out _);
            Vector3 eye = sv.Zone.EyeOf(sv.Zone.Entities.Get<Character>(userEntity).State);
            return Vector3.Normalize(at + sv.Zone.Frame.UpAt(at) * 1.15f - eye);
        });
        // Clearing solid nature can rebuild collision near the actors. Let both history and prediction settle first.
        rig.Run(0.6, (_, _) => default);
        strikeDirection = rig.Server.Inspect(sv =>
        {
            sv.Zone.Players().TryFindPlayer(target.EntityId, out Entity e, out _);
            sv.Zone.Players().TryFindPlayer(user.EntityId, out Entity userEntity, out _);
            Vector3 at = sv.Zone.Entities.Get<Character>(e).State.Position;
            Vector3 eye = sv.Zone.EyeOf(sv.Zone.Entities.Get<Character>(userEntity).State);
            return Vector3.Normalize(at + sv.Zone.Frame.UpAt(at) * 1.15f - eye);
        });
        var markers = new List<HitMarkerEvent>();
        var predicted = new List<AttackId>();
        user.Local.HitMarker += markers.Add;
        user.Melee.Started += _ => predicted.Add(user.Melee.AttackId);
        bool pressed = false;
        rig.Run(0.3, (who, time) =>
        {
            if (who != user) return default;
            // Both actors stand still: use the known world aim, independent of teleport correction smoothing.
            bool edge = !pressed;
            pressed = true;
            return new InputFrame { View = strikeDirection, ViewFrame = 0, PrimaryPressed = edge };
        });
        var deadline = Stopwatch.StartNew();
        while (markers.Count == 0 && deadline.Elapsed.TotalSeconds < 5)
            rig.Run(0.1, (who, _) => who == user ? new InputFrame { View = strikeDirection, ViewFrame = 0 } : default);
        string detail = rig.Server.Inspect(sv =>
        {
            sv.Zone.Players().TryFindPlayer(user.EntityId, out Entity e, out Player p);
            Character character = sv.Zone.Entities.Get<Character>(e);
            var line = sv.Zone.AttackLine(character, sv.Zone.Tick);
            var trace = sv.Zone.Damage().Trace(line.Eye, line.Direction, tool.Melee!.Reach, CombatSystem.SweepRadius, p.Id, sv.Zone.Tick);
            float rewind = Math.Min(character.Actions.ViewDelay / 4f, sv.Zone.Content!.Survival.MaxRewindSeconds * CharacterTicks.Rate);
            var historical = sv.Zone.Damage().Trace(line.Eye, line.Direction, tool.Melee!.Reach, CombatSystem.SweepRadius, p.Id, sv.Zone.Tick - rewind);
            return $"server tick {sv.Zone.Tick}, strike {p.StrikeTick}, held {p.HeldItem}, action {p.LastAction}/{p.LastActionTick}, predicted [{string.Join(',', predicted)}], trace {trace.Target}/{trace.Distance}, rewind {rewind} trace {historical.Target}/{historical.Distance}, markers {markers.Count}, authoritative {authority.Count}";
        });
        Assert.Equal(1, markers.Count(m => m.Target == TargetFamily.Character), "one target marker: " + detail);
        HitMarkerEvent marker = markers.Single(m => m.Target == TargetFamily.Character);
        var actual = authority.Single(h => h.Hit.Victim == target.EntityId);
        Assert.Equal(tool.Id, marker.Weapon, "network marker identifies held weapon");
        Assert.Equal(actual.Tick, marker.ActionTick, "network marker carries accepted action timestamp");
        Assert.True(Vector3.Distance(actual.Hit.Point, marker.Point) < 0.001f, "network contact is the actual damage trace point");
        Assert.True(predicted.Contains(marker.Attack), "authoritative marker correlates with the predicted attempt identity");
        Assert.True(user.Attacks.TryGet(marker.Attack, out var attack) && attack.Resolution?.StartTick == marker.ActionTick,
            "marker uses the accepted start tick even if the server retimed the prediction");
        Assert.Equal(1, markers.Count, "one real contact produces one feedback marker");
    }

    [Test]
    public static void AcceptedUseRetainsConsumedItemForObserversAndRefusalDoesNotReplay()
    {
        using var rig = new LoopbackRig();
        GameClient user = rig.Join("User");
        GameClient observer = rig.Join("Observer");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport User 0 0");
        rig.Server.Execute("teleport Observer 3 0");
        rig.Server.Execute("give User bandage 1");
        ItemDef bandage = user.Content.ItemByKey("bandage");
        Loopback.RunUntil(rig, () => user.Local.Inventory.Count(bandage) == 1, "one bandage received");
        int slot = Enumerable.Range(0, 24).First(i => user.Local.Inventory.Main[i].Item == bandage.Id);
        user.Local.MoveItem(ContainerRef.Main, slot, ContainerRef.Belt, 2);
        user.Local.HeldSlot = 2;
        rig.Run(0.5, (_, _) => default);
        var queue = new HeldActionQueue();
        uint seq = 0;
        Assert.True(queue.Schedule(user.PredictedTick, bandage.Id, 2, "bandage", HeldActionTiming.Duration(bandage, "bandage", 2.3f),
            () => seq = user.Local.UsePart(ContainerRef.Belt, 2, ItemPart.Consumable)), "held bandage windup queued");
        InputFrame Windup(GameClient who, double _)
        {
            if (who == user) queue.Step(user.PredictedTick, user.Local.HeldItem?.Id ?? 0, user.Local.HeldSlot, true);
            return default;
        }
        rig.Run(0.5, Windup);
        Assert.Equal(0u, seq, "windup has not dispatched use early");
        Assert.Equal(1, user.Local.Inventory.Count(bandage), "bandage remains until commit");
        rig.Run(1.6, Windup);
        Assert.True(seq != 0, "valid use sent");
        Loopback.RunUntil(rig, () => observer.Entities.Any(e => e.NetId == user.NetId && e.Action == ActionKind.Use && e.ActionItem == bandage.Id),
            "consumed item action replicated");
        RemoteEntity remote = observer.Entities.Single(e => e.NetId == user.NetId);
        Assert.True(remote.ActionTick > 0, "authoritative action timestamp replicated");
        Assert.Equal(0, user.Local.Inventory.Count(bandage), "the last item was consumed once");
        byte count = remote.ActionCount;
        user.Local.ItemAction(ContainerRef.Belt, 2, user.Content.ItemActions["use"]!.Id);
        rig.Run(0.3, (_, _) => default);
        Assert.Equal(count, remote.ActionCount, "a refused repeat creates no second animation event");
    }
}
