using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Net.Transport;
using Hollow.Server;
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

public static class AttackMeleeTests
{
    private sealed class Rig : IDisposable
    {
        public GameRig Game { get; } = new();
        public Entity User { get; }
        public Entity Target { get; }
        public ItemDef Tool { get; }
        public AttackLedger Ledger { get; } = new();
        public LocalMelee Local { get; }
        public List<AttackRequest> Requests { get; } = new();
        public List<AttackResolution> Decisions { get; } = new();
        public List<HitConfirmed> Hits { get; } = new();
        public int Presentations { get; private set; }

        public Rig()
        {
            User = Game.Add("User", 30, 0);
            Target = Game.Add("Target", 30, -1.2f);
            Game.Clear(Game.Ch(User).State.Position, Game.Ch(Target).State.Position);
            Game.Give(User, "metal-hatchet", 1);
            Game.Zone.Actions().MoveItem(User, ContainerRef.Main, 0, ContainerRef.Belt, 2, 0, 0);
            Tool = Game.C.ItemByKey("metal-hatchet");
            Local = new LocalMelee(Ledger);
            Local.Requested += Requests.Add;
            Local.Started += _ => Presentations++;
            Ledger.Resolved += Local.Reconcile;
            Game.Zone.Combat().BeginMeleeSession(User);
            Game.Zone.Events.On<AttackResolved>((in AttackResolved answer) =>
            {
                if (answer.Player != Game.P(User).Id) return;
                Decisions.Add(answer.Resolution);
                Ledger.Resolve(answer.Resolution);
            });
            Game.Zone.Events.On<HitConfirmed>((in HitConfirmed hit) =>
            {
                if (hit.By == Game.P(User).Id && hit.Target.Family == TargetFamily.Character) Hits.Add(hit);
            });
            Step(30);
            Vector3 at = Game.Ch(Target).State.Position;
            Game.Look(User, at + Game.Zone.Frame.UpAt(at) * 1.15f);
        }

        public void Step(int ticks = 1, bool attack = false, byte slot = 2, ActionButtons authorityButtons = ActionButtons.None)
        {
            for (int i = 0; i < ticks; i++)
            {
                uint tick = (uint)Game.Zone.Tick + 1;
                Local.Tick(tick, slot == 2 ? Tool : null, slot, attack, !Game.P(User).Dead && !Game.P(User).Wounded,
                    Game.C.Survival.EquipSeconds);
                Game.Step(1, () => Game.Ch(User).Actions = new ActionInput { HeldSlot = slot, Buttons = authorityButtons });
            }
        }

        public void Dispose() => Game.Dispose();
    }

    [Test]
    public static void DelayedMeleeAttemptReconcilesByIdentityAndKeepsItsDamageMetadata()
    {
        using var rig = new Rig();
        rig.Step(attack: true);
        AttackRequest request = rig.Requests.Single();
        Assert.Equal(0, rig.Decisions.Count, "prediction alone grants no authoritative result");
        Assert.Equal(100f, rig.Game.P(rig.Target).Vitals.Health, "prediction alone does not damage");
        rig.Game.Zone.Combat().QueueMelee(rig.User, request);
        rig.Step();
        AttackResolution accepted = rig.Decisions.Single();
        Assert.Equal(request.Tick + 1, accepted.StartTick, "late request receives a full authoritative windup");
        Assert.Equal(accepted.StartTick, rig.Local.ActionTick, "prediction adopts the accepted start tick");
        Assert.Equal(request.Id, rig.Local.AttackId, "retiming preserves attack identity");
        Assert.Equal(1, rig.Presentations, "acceptance does not replay the swing");
        // Another presentation action cannot relabel the pending blow.
        rig.Game.P(rig.User).RecordAction(ActionKind.Use, rig.Tool.Id, rig.Game.Zone.Tick + 1);
        rig.Step(rig.Tool.Melee!.StrikeTicks);
        HitConfirmed hit = rig.Hits.Single();
        Assert.Equal(request.Id, hit.Attack, "damage names its originating attempt");
        Assert.Equal(accepted.StartTick, hit.ActionTick, "damage retains its original start despite a later presentation action");
        Assert.Equal(rig.Tool.Id, hit.Weapon, "damage retains the tool identity");
        Assert.True(rig.Game.P(rig.Target).Vitals.Health < 100, "the authoritative contact dealt damage");
        Assert.Equal(1, rig.Presentations, "contact does not replay the swing");
    }

    [Test]
    public static void DuplicateMeleeRequestsAndRepeatedMovementInputProduceOnlyOneSwing()
    {
        using var rig = new Rig();
        rig.Step(attack: true);
        AttackRequest request = rig.Requests.Single();
        rig.Game.Zone.Combat().QueueMelee(rig.User, request);
        rig.Game.Zone.Combat().QueueMelee(rig.User, request);
        Assert.Equal(1, rig.Game.P(rig.User).MeleeQueue.Count, "duplicate request is queued once");
        rig.Step();
        AttackResolution accepted = rig.Decisions.Single();
        rig.Game.Zone.Combat().QueueMelee(rig.User, request);
        Assert.Equal(accepted, rig.Decisions.Last(), "duplicate accepted request replays the original decision");
        rig.Step(rig.Tool.Melee!.SwingTicks * 2, authorityButtons: ActionButtons.Primary | ActionButtons.PrimaryPressed);
        Assert.Equal(1, rig.Hits.Count, "held/repeated movement input cannot invent more network attempts");
        Assert.Equal(1, rig.Presentations, "duplicate acceptance is never a second presentation");
    }

    [Test]
    public static void CancellationThatOvertakesRequestCannotBeResurrectedByRetries()
    {
        using var rig = new Rig();
        rig.Step(attack: true);
        AttackRequest request = rig.Requests.Single();
        rig.Game.Zone.Combat().CancelMelee(rig.User, request.Id);
        rig.Game.Zone.Combat().QueueMelee(rig.User, request);
        rig.Game.Zone.Combat().QueueMelee(rig.User, request);
        rig.Step(rig.Tool.Melee!.SwingTicks);
        Assert.Equal(0, rig.Game.P(rig.User).MeleeQueue.Count, "cancelled attempt is never enqueued");
        Assert.True(rig.Decisions.All(d => d.Cancelled && d.Code == OutcomeCode.Stale), "every retry preserves the cancellation");
        Assert.Equal(0, rig.Hits.Count, "cancelled attempt never damages");
    }

    [Test]
    public static void EquipAndDeathCancelAcceptedPendingMeleeContacts()
    {
        foreach (bool dies in new[] { false, true })
        {
            using var rig = new Rig();
            rig.Step(attack: true);
            AttackRequest request = rig.Requests.Single();
            rig.Game.Zone.Combat().QueueMelee(rig.User, request);
            rig.Step();
            Assert.Equal(OutcomeCode.Done, rig.Decisions.Last().Code, "accepted before interruption");
            if (dies) rig.Game.P(rig.User).Dead = true;
            rig.Step(rig.Tool.Melee!.StrikeTicks + 1, slot: dies ? (byte)2 : (byte)3);
            Assert.True(rig.Decisions.Last().Cancelled, "authority explicitly reports cancellation");
            Assert.Equal(0, rig.Hits.Count, "interrupted windup cannot damage");
            Assert.Equal(100f, rig.Game.P(rig.Target).Vitals.Health, "target remains unharmed");
        }
    }

    [Test]
    public static void MeleeSpamHonoursCadenceAndCachesBudgetRefusals()
    {
        using var rig = new Rig();
        uint tick = (uint)rig.Game.Zone.Tick + 1;
        for (uint sequence = 1; sequence <= 3; sequence++)
            rig.Game.Zone.Combat().QueueMelee(rig.User, new AttackRequest(new AttackId(AttackDomain.Melee, sequence), tick, 2, rig.Tool.Id));
        rig.Step();
        Assert.Equal(1, rig.Decisions.Count(d => d.Code == OutcomeCode.Done), "only one same-tick attempt starts");
        Assert.Equal(2, rig.Decisions.Count(d => d.Code == OutcomeCode.Cooldown), "extra distinct attempts receive cooldown refusal");
        var refused = new AttackRequest(new AttackId(AttackDomain.Melee, 4), tick, 2, rig.Tool.Id) { Refused = OutcomeCode.RateLimited };
        rig.Game.Zone.Combat().QueueMelee(rig.User, refused);
        rig.Game.Zone.Combat().QueueMelee(rig.User, refused with { Refused = OutcomeCode.Done });
        Assert.Equal(OutcomeCode.RateLimited, rig.Decisions.Last().Code, "retry cannot resurrect a budget-refused attempt");
        rig.Step(rig.Tool.Melee!.StrikeTicks);
        Assert.Equal(1, rig.Hits.Count, "spam produces only the one validated contact");
    }

    [Test]
    public static void LocalMeleeCancelsRequestsInFlightWithoutRevivingOnLateAcceptance()
    {
        using var rig = new Rig();
        var cancels = new List<AttackId>();
        rig.Local.CancelRequested += cancels.Add;
        rig.Step(attack: true);
        AttackRequest request = rig.Requests.Single();
        rig.Step(slot: 3);
        Assert.Equal(request.Id, cancels.Single(), "changing hands cancels the unresolved request by identity");
        rig.Ledger.Resolve(new AttackResolution(request.Id, OutcomeCode.Done, request.Tick, request.Tick + 2,
            request.Tick + 10, request.Tick + 26));
        Assert.Equal(1, rig.Presentations, "late acceptance cannot restart a cancelled local action");
        rig.Local.Reset();
        Assert.Equal(1, cancels.Count, "connection reset never sends a cancellation into the new session");
    }

    [Test]
    public static void MeleeRefusalCarriesTheAuthoritativeEquipCooldown()
    {
        using var rig = new Rig();
        uint equippedAt = (uint)rig.Game.Zone.Tick + 20;
        rig.Game.P(rig.User).EquipReadyTick = equippedAt;
        rig.Step(attack: true);
        rig.Game.Zone.Combat().QueueMelee(rig.User, rig.Requests.Single());
        rig.Step();
        AttackResolution refused = rig.Decisions.Single();
        Assert.Equal(OutcomeCode.Cooldown, refused.Code, "authority still waits for equip");
        Assert.Equal(equippedAt, refused.ReadyTick, "refusal carries the later equip deadline");
        Assert.Equal(equippedAt, rig.Local.ReadyTick, "prediction adopts the authoritative deadline");
        rig.Step(5, attack: true);
        Assert.Equal(1, rig.Requests.Count, "held input cannot repeatedly predict during the refused equip period");
    }

    [Test]
    public static void CancellationAfterPredictedContactStillStopsAnAcceptedAuthoritativeWindup()
    {
        using var rig = new Rig();
        var cancels = new List<AttackId>();
        rig.Local.CancelRequested += id =>
        {
            cancels.Add(id);
            rig.Game.Zone.Combat().CancelMelee(rig.User, id);
        };
        rig.Step(attack: true);
        AttackRequest request = rig.Requests.Single();
        rig.Step(rig.Tool.Melee!.StrikeTicks + 1);
        rig.Game.Zone.Combat().QueueMelee(rig.User, request);
        rig.Step();
        AttackResolution accepted = rig.Decisions.Single();
        Assert.Equal(OutcomeCode.Done, accepted.Code, "late request starts a full authoritative windup");
        Assert.True(accepted.ContactTick > rig.Game.Zone.Tick, "authority has not reached contact yet");
        Assert.Equal(0, rig.Ledger.PendingCount, "acceptance has removed the request from unresolved work");

        // Keep the server's movement input unchanged: the reliable cancellation itself must
        // stop this attack, even though the client's predicted contact already passed.
        rig.Local.Cancel();
        rig.Step(rig.Tool.Melee.StrikeTicks + 1);
        Assert.Equal(request.Id, cancels.Single(), "cancellation still names the accepted current attempt");
        Assert.True(rig.Decisions.Last().Cancelled, "authority confirms the cancelled windup");
        Assert.Equal(0, rig.Hits.Count, "the late authoritative contact is cancelled");
        Assert.Equal(1, rig.Presentations, "cancellation never starts another presentation");
    }

    [Test]
    public static void OneCancellationFrontierOvertakesAFullUnresolvedBacklog()
    {
        using var rig = new Rig();
        var frontiers = new List<AttackId>();
        rig.Local.CancelRequested += frontiers.Add;
        uint tick = 100;
        for (int i = 0; i < AttackLedger.PendingLimit; i++)
        {
            rig.Local.Tick(tick, rig.Tool, 2, true, true, 0);
            rig.Local.Tick(tick + (uint)rig.Tool.Melee!.StrikeTicks, rig.Tool, 2, false, true, 0);
            tick += (uint)rig.Tool.Melee.SwingTicks;
        }
        Assert.Equal(AttackLedger.PendingLimit, rig.Ledger.PendingCount, "the connection has its full bounded backlog");
        rig.Local.Cancel();
        AttackId through = frontiers.Single();
        Assert.Equal((uint)AttackLedger.PendingLimit, through.Sequence, "one message cancels through the newest abandoned attempt");

        // Exhausting acknowledgement allowance cannot discard the frontier, even when no
        // corresponding request has reached authority yet. Deliver all requests backwards.
        rig.Game.Zone.Combat().CancelMelee(rig.User, through, acknowledge: false);
        Assert.Equal(0, rig.Decisions.Count, "an unknown cancellation needs no immediate acknowledgement");
        for (int i = rig.Requests.Count - 1; i >= 0; i--)
            rig.Game.Zone.Combat().QueueMelee(rig.User, rig.Requests[i]);
        rig.Game.Zone.Combat().CancelMelee(rig.User, through with { Sequence = 1 }, acknowledge: false);
        rig.Game.Zone.Combat().QueueMelee(rig.User, rig.Requests[0]);
        Assert.Equal(through.Sequence, rig.Game.P(rig.User).MeleeCancelledThrough, "an older reordered cancellation cannot move the frontier backwards");
        Assert.True(rig.Decisions.All(r => r.Cancelled), "every overtaken request receives an explicit cancellation");
        Assert.Equal(0, rig.Ledger.PendingCount, "the eventual requests settle every pending local identity");
        Assert.Equal(0, rig.Game.P(rig.User).MeleeQueue.Count, "no abandoned request can enter the action queue");
        Assert.Equal(0, rig.Hits.Count, "cancelled backlog deals no damage");
    }

    [Test]
    public static void ExhaustedCancellationReplyBudgetCannotPreserveQueuedOrActiveAttacks()
    {
        using var rig = new Rig();
        rig.Step(attack: true);
        AttackRequest completed = rig.Requests.Single();
        rig.Game.Zone.Combat().QueueMelee(rig.User, completed);
        rig.Step(rig.Tool.Melee!.SwingTicks + 1);
        Assert.Equal(1, rig.Hits.Count, "first attack completed before cancellation");

        uint next = (uint)rig.Game.Zone.Tick + 1;
        var active = new AttackRequest(new AttackId(AttackDomain.Melee, 34), next, 2, rig.Tool.Id);
        var queued = new AttackRequest(new AttackId(AttackDomain.Melee, 35), next + 10, 2, rig.Tool.Id);
        rig.Game.Zone.Combat().QueueMelee(rig.User, active);
        rig.Game.Zone.Combat().QueueMelee(rig.User, queued);
        rig.Step();
        var budget = new GunBudget();
        for (uint sequence = 2; sequence <= 33; sequence++)
            rig.Game.Zone.Combat().CancelMelee(rig.User, new AttackId(AttackDomain.Melee, sequence),
                acknowledge: budget.Spend(rig.Game.Zone.Tick, 32, 32));
        bool acknowledge = budget.Spend(rig.Game.Zone.Tick, 32, 32);
        Assert.True(!acknowledge, "unknown cancellation replies exhausted their separate allowance");
        var frontier = new AttackId(AttackDomain.Melee, 36);
        rig.Game.Zone.Combat().CancelMelee(rig.User, frontier, acknowledge);
        Assert.True(rig.Game.P(rig.User).MeleeJournal.TryGet(active.Id, out AttackResolution activeResult) && activeResult.Cancelled,
            "active accepted windup receives its final cancellation despite the exhausted reply budget");
        Assert.True(rig.Game.P(rig.User).MeleeJournal.TryGet(queued.Id, out AttackResolution queuedResult) && queuedResult.Cancelled,
            "queued attempt also receives its final cancellation");
        rig.Game.Zone.Combat().QueueMelee(rig.User, queued);
        rig.Game.Zone.Combat().CancelMelee(rig.User, active.Id, acknowledge: false);
        Assert.Equal(36u, rig.Game.P(rig.User).MeleeCancelledThrough, "retries and reordered frontiers remain monotonic");
        Assert.Equal(OutcomeCode.Done, rig.Game.Zone.Combat().CancelMelee(rig.User, completed.Id),
            "a completed attack keeps its original accepted decision");
        Assert.Equal(OutcomeCode.Done, rig.Game.Zone.Combat().QueueMelee(rig.User, completed),
            "replaying a completed request cannot relabel its actual damage as cancelled");
        rig.Step(rig.Tool.Melee.StrikeTicks + 11);
        Assert.Equal(1, rig.Hits.Count, "only the previously completed attack damaged the target");
        rig.Game.Zone.Combat().BeginMeleeSession(rig.User);
        Assert.Equal(0u, rig.Game.P(rig.User).MeleeCancelledThrough, "a new connection clears the cancellation frontier");
    }

    [Test]
    public static void ReliableMeleeAttemptsSettleUnderLatencyJitterAndLossIncludingFastTap()
    {
        using var rig = new LoopbackRig();
        GameClient user = rig.Join("User", new NetConditions { LatencyMs = 180, JitterMs = 40, LossPercent = 10 });
        GameClient target = rig.Join("Target");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport User 30 0");
        rig.Server.Execute("teleport Target 30 -1.2");
        ItemDef rock = user.Content.ItemByKey("rock");
        Loopback.RunUntil(rig, () => user.Local.Inventory.Belt[0].Item == rock.Id, "starter rock is replicated", seconds: 8);
        user.Local.HeldSlot = 0;
        var authoritative = new ConcurrentQueue<HitConfirmed>();
        long clearedAt = rig.Server.Inspect(sv =>
        {
            sv.Zone.Players().TryFindPlayer(target.EntityId, out Entity targetEntity, out _);
            Vector3 at = sv.Zone.Entities.Get<Character>(targetEntity).State.Position;
            var objects = new List<NaturalObject>();
            sv.Zone.Nature!.Query(at, 5, objects);
            foreach (NaturalObject natural in objects)
                sv.Zone.Natural!.Apply(natural.Id, new NaturalOverride { Depleted = true, RespawnAtUnixMs = long.MaxValue });
            sv.Zone.Events.On<HitConfirmed>((in HitConfirmed hit) =>
            {
                if (hit.By == user.EntityId && hit.Target.Family == TargetFamily.Character) authoritative.Enqueue(hit);
            });
            return sv.Zone.Tick;
        });
        Loopback.RunUntil(rig, () => user.Local.HeldItem?.Id == rock.Id && user.PredictedTick >= user.Melee.ReadyTick &&
            rig.Server.Inspect(sv => sv.Zone.Players().TryFindPlayer(user.EntityId, out _, out var player) &&
                player.HeldItem == rock.Id && player.HeldSlot == 0 && sv.Zone.Tick >= player.EquipReadyTick && sv.Zone.Tick >= clearedAt + 20),
            "both sides are equipped and collision/history has settled", seconds: 10);
        Vector3 view = rig.Server.Inspect(sv =>
        {
            sv.Zone.Players().TryFindPlayer(target.EntityId, out Entity victim, out _);
            sv.Zone.Players().TryFindPlayer(user.EntityId, out Entity actor, out _);
            Vector3 at = sv.Zone.Entities.Get<Character>(victim).State.Position;
            return Vector3.Normalize(at + sv.Zone.Frame.UpAt(at) * 1.15f - sv.Zone.EyeOf(sv.Zone.Entities.Get<Character>(actor).State));
        });
        var requests = new List<AttackRequest>();
        var markers = new List<HitMarkerEvent>();
        int presentations = 0;
        user.Melee.Requested += requests.Add;
        user.Melee.Started += _ => presentations++;
        user.Local.HitMarker += markers.Add;
        InputFrame Frame(GameClient who, bool held = false) => who == user
            ? new InputFrame { View = view, ViewFrame = 0, Primary = held } : default;

        // A real GameClient render-frame press that is already released at the next tick.
        // Its latched edge must start one reliable attempt even without held Primary.
        user.Update(0, new InputFrame { View = view, ViewFrame = 0, PrimaryPressed = true });
        rig.Run(0.8, (who, _) => Frame(who));
        Assert.Equal(1, requests.Count, "a short press survives until an input tick without becoming repeated attacks");
        rig.Run(6, (who, _) => Frame(who, held: true));
        rig.Run(3, (who, _) => Frame(who));
        Assert.True(requests.Count >= 5, "sustained input generated multiple real network attempts");
        Assert.Equal(requests.Count, presentations, "every attempted swing was presented once");
        Assert.Equal(0, user.Attacks.PendingCount, "every reliable attempt eventually received a decision");
        Assert.True(requests.All(r => user.Attacks.TryGet(r.Id, out AttackRecord record) && record.Resolution.HasValue),
            "no attack identity is left without an outcome");
        Assert.True(markers.Count >= 3, "several authoritative contacts reached the shooter through the impaired line");
        Assert.Equal(markers.Count, markers.Select(m => m.Attack).Distinct().Count(), "one marker per actual melee contact");
        foreach (HitMarkerEvent marker in markers)
        {
            Assert.True(requests.Any(r => r.Id == marker.Attack), "marker names an actual local attempt");
            Assert.True(user.Attacks.TryGet(marker.Attack, out AttackRecord record) && record.Resolution is { Code: OutcomeCode.Done, Cancelled: false },
                "marker belongs to an accepted attempt");
            HitConfirmed actual = authoritative.Single(h => h.Attack == marker.Attack);
            Assert.Equal(actual.ActionTick, marker.ActionTick, "server damage and client marker agree on the originating action tick");
            Assert.True(Vector3.Distance(actual.Point, marker.Point) < 0.001f, "actual authoritative contact point survives the network");
        }
        Assert.Equal(authoritative.Count, markers.Count, "every authoritative contact arrived once");
        Fixtures.Report($"180 ms RTT, 40 ms jitter, 10% loss: {requests.Count} attempts, {markers.Count} contacts, no pending outcomes");
    }
}
