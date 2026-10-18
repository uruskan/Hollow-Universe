using System;
using System.Reflection;
using Hollow.Client;
using Hollow.Client.Audio;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;

namespace Hollow.Tests;

public static class MeleeSwingCueTests
{
    [Test]
    public static void SwishWaitsForContactPreparationAndCannotReplayAfterClockRewind()
    {
        var cue = new MeleeSwingCue<AttackId>();
        var attack = new AttackId(AttackDomain.Melee, 1, 3);
        cue.Start(attack, 100, 108);
        Assert.True(!cue.Step(100) && !cue.Step(105.99), "silent during early windup");
        Assert.True(cue.Step(106), "swish begins in the final quarter before tick-108 contact");
        cue.Start(attack, 100, 108);
        Assert.True(!cue.Step(105) && !cue.Step(106) && !cue.Step(108), "duplicate events and clock rewinds cannot replay it");
    }

    [Test]
    public static void SwishReconciliationRetimesTheSameAttemptWithoutRearmingIt()
    {
        var cue = new MeleeSwingCue<AttackId>();
        var first = new AttackId(AttackDomain.Melee, 1, 7);
        var second = new AttackId(AttackDomain.Melee, 2, 7);
        cue.Start(first, 100, 108);
        cue.Reconcile(first, 105, 113);
        Assert.True(!cue.Step(106) && cue.Step(111), "authority moves pending anticipation with contact");
        cue.Reconcile(first, 110, 118);
        Assert.True(!cue.Step(116), "late acceptance cannot play a second sound");
        cue.Start(second, 120, 128);
        cue.Reconcile(first, 130, 138);
        Assert.True(cue.Step(126), "a stale earlier acceptance cannot move the current cue");
    }

    [Test]
    public static void SwishLateActivationAndHitchesNeverPlayAfterContact()
    {
        var cue = new MeleeSwingCue<(uint Tick, byte Count)>();
        cue.Start((100, 255), 100, 108);
        Assert.True(!cue.Step(109), "an already-completed remote contact does not replay its anticipation");
        Assert.True(!cue.Step(106), "subsequent interpolation rewind does not recover the discarded cue");
        cue.Start((120, 0), 120, 128);
        Assert.True(cue.Step(127.5), "late snapshot before contact can still present the pending cue");
        cue.Start((140, 1), 140, 148);
        Assert.True(!cue.Step(149), "a frame hitch across the complete contact window stays silent");
        cue.Start((160, 2), 160, 168);
        Assert.True(!cue.Step(168) && !cue.Step(166), "contact itself is already too late, including after a clock rewind");
    }

    [Test]
    public static void SwishCancellationAndReplacementCannotLeakOldSound()
    {
        var cue = new MeleeSwingCue<AttackId>();
        var first = new AttackId(AttackDomain.Melee, 1);
        var second = new AttackId(AttackDomain.Melee, 2);
        cue.Start(first, 100, 108);
        cue.Step(103, canPresent: false);
        cue.Start(first, 100, 108);
        cue.Reconcile(first, 105, 113);
        Assert.True(!cue.Step(111), "death, seating, missing hands or changed item/slot cannot rearm the attempt");
        cue.Start(second, 120, 128);
        cue.Cancel();
        Assert.True(!cue.Step(126), "explicit rejection/action replacement cancels waiting audio");
        cue.Start(new AttackId(AttackDomain.Melee, 3), 140, 148);
        cue.Start(new AttackId(AttackDomain.Melee, 4), 141, 149);
        Assert.True(!cue.Step(146) && cue.Step(147), "replacement has only its own pending time");
    }

    [Test]
    public static void SwishAnticipationFractionIsSharedAndConfigurable()
    {
        var cue = new MeleeSwingCue<AttackId>(anticipationFraction: 0.5);
        cue.Start(new AttackId(AttackDomain.Melee, 1), 10, 18);
        Assert.True(!cue.Step(13.99) && cue.Step(14), "presentation tuning can change without item-kind branches");
        cue.Start(new AttackId(AttackDomain.Melee, 2), 20, 20);
        Assert.True(!cue.Step(20), "invalid or absent anticipation cannot create a start-time sound");
    }

    [Test]
    public static void SwishTracksRealMeleeRepeatCancellationAndAcceptedContactTicks()
    {
        var item = new ItemDef { Id = 24, Key = "metal-hatchet", Melee = new MeleeStats { SwingSeconds = 0.8f, StrikeSeconds = 0.28f } };
        var melee = new LocalMelee();
        var cue = new MeleeSwingCue<AttackId>();
        int sounds = 0;
        melee.Started += held => cue.Start(melee.AttackId, melee.ActionTick, (double)melee.ActionTick + held.Melee!.StrikeTicks);
        melee.Cancelled += cue.Cancel;
        melee.Reconciled += accepted =>
        {
            if (accepted.Code == OutcomeCode.Done && !accepted.Cancelled)
                cue.Reconcile(accepted.Id, accepted.StartTick, accepted.ContactTick);
        };
        for (uint tick = 100; tick <= 126; tick++)
        {
            melee.Tick(tick, item, 0, attack: true, canAct: true, equipSeconds: 0);
            if (tick == 103)
                melee.Reconcile(new AttackResolution(melee.AttackId, OutcomeCode.Done, 100, 102, 110, 126));
            if (cue.Step(tick)) sounds++;
        }
        Assert.Equal(1, sounds, "accepted first attack swishes once; the repeat is still preparing");
        melee.Tick(127, item, 1, attack: false, canAct: true, equipSeconds: 0);
        for (uint tick = 127; tick <= 136; tick++) if (cue.Step(tick)) sounds++;
        Assert.Equal(1, sounds, "switching the same item to another slot cancels the second cue");
    }

    [Test]
    public static void RemoteSwishUsesServerClockWhileSnapshotsInterpolateBehindIt()
    {
        foreach (string key in new[] { "metal-hatchet", "stone-pickaxe", "bone-knife" })
        {
            using var rig = new RemoteCueRig(key);
            rig.Snapshot(98, 0, 0, ActionKind.None);
            rig.Snapshot(100, 100, 1);
            rig.Snapshot(104, 100, 1);
            double cueTick = 100 + rig.Item.Melee!.StrikeTicks * MeleeSwingCue<AttackId>.DefaultAnticipationFraction;
            rig.Sample(cueTick - 0.01);
            rig.Cue.Start(rig.Remote, rig.Item);
            Assert.True(!rig.Step(), key + " stays silent before the shared server-clock cue");
            rig.Sample(cueTick);
            Assert.Near(Protocol.InterpolationDelaySeconds * GameClient.TickRate,
                rig.Client.ServerTickNow - rig.Remote.RenderTick, 1e-8, "actual remote sample retains the render buffer");
            Assert.True(rig.Step(), key + " swishes before authoritative contact despite the buffered animation");
            rig.Cue.Start(rig.Remote, rig.Item);
            rig.Sample(100 + rig.Item.Melee.StrikeTicks);
            Assert.True(!rig.Step(), key + " does not replay at contact");
        }
    }

    [Test]
    public static void RemoteSwishCannotFollowContactWhileRenderClockStillShowsAnticipation()
    {
        foreach (string key in new[] { "metal-hatchet", "stone-pickaxe", "bone-knife" })
        {
            using var rig = new RemoteCueRig(key);
            rig.Snapshot(100, 100, 1);
            double contact = 100 + rig.Item.Melee!.StrikeTicks;
            double cueTick = 100 + rig.Item.Melee.StrikeTicks * MeleeSwingCue<AttackId>.DefaultAnticipationFraction;
            rig.Sample(contact);
            rig.Cue.Start(rig.Remote, rig.Item);
            Assert.True(rig.Remote.RenderTick < cueTick, "buffered view still precedes the swish when contact is current");
            Assert.True(!rig.Step(), key + " cannot start a swish on the authoritative contact tick");

            // The former render-clock integration fired here, after the world hit was already presented.
            rig.Sample(cueTick + Protocol.InterpolationDelaySeconds * GameClient.TickRate + 0.01);
            Assert.True(rig.Client.ServerTickNow > contact && rig.Remote.RenderTick >= cueTick && rig.Remote.RenderTick < contact,
                "real snapshot sampling exposes the two clocks' conflicting anticipation windows");
            Assert.True(!rig.Step(), key + " remains silent when the delayed animation reaches its cue");
        }
    }

    [Test]
    public static void RemoteSwishDropsLateSnapshotsAndSlotCancellationButKeepsTheNextRepeat()
    {
        using var rig = new RemoteCueRig("metal-hatchet");
        rig.Snapshot(115, 100, 15);
        rig.Sample(115);
        rig.Cue.Start(rig.Remote, rig.Item);
        Assert.True(!rig.Step(), "first observed snapshot already beyond contact does not swish");

        rig.Snapshot(120, 120, 0);
        rig.Sample(123.1);
        rig.Cue.Start(rig.Remote, rig.Item);
        Assert.True(!rig.Step(), "wrapped action count belongs to a fresh pending attempt");
        rig.Sample(126);
        Assert.True(rig.Step(), "the next real snapshot action still swishes once");
        Assert.True(!rig.Step(), "repeated presentation of the same snapshot is silent");

        rig.Snapshot(144, 144, 1);
        rig.Sample(147.1);
        rig.Cue.Start(rig.Remote, rig.Item);
        Assert.True(!rig.Step(), "next repeat starts with anticipation remaining");
        rig.Snapshot(145, 145, 2, ActionKind.None);
        rig.Sample(148.1);
        Assert.True(!rig.Step(), "same-item slot switch replicated as None cancels the cue");
        rig.Sample(150);
        Assert.True(!rig.Step(), "cancelled cue cannot leak at its former due time");
    }

    // Seed only the normally snapshot-driven clock. The production clock properties, RemoteEntity sampling,
    // content timings and remote-cue driver used by PeopleView remain real; no server or renderer is started.
    private sealed class RemoteCueRig : IDisposable
    {
        public GameClient Client { get; } = new(new ClientOptions { Content = ContentFixture.Content });
        public RemoteEntity Remote { get; } = new(1);
        public RemoteMeleeSwingCue Cue { get; } = new();
        public ItemDef Item { get; }

        public RemoteCueRig(string key) => Item = Client.Content.ItemByKey(key);

        public void Snapshot(uint sentTick, uint actionTick, byte count, ActionKind action = ActionKind.Swing) =>
            Remote.Add(sentTick, new CharacterNetState
            {
                Y = 256000, Health = 100, Flags = MoveFlags.OnGround, Stance = Stance.Standing,
                Held = Item.Id, ActionItem = Item.Id, Action = action, ActionTick = actionTick, ActionCount = count,
            });

        public void Sample(double serverTick)
        {
            typeof(GameClient).GetField("_remoteClock", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Client, serverTick);
            Remote.Sample(Client.RenderTick);
        }

        public bool Step() => Cue.Step(Client, Remote);
        public void Dispose() => Client.Dispose();
    }
}
