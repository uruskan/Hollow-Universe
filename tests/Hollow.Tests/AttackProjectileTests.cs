using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Hollow.Client;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Net.Transport;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

public static class AttackProjectileTests
{
    [Test]
    public static void InvalidTerminalCannotPoisonReorderingCacheOrPresentationCoordinates()
    {
        using var f = new ProjectileClient("rifle", "rifle-ammo");
        ShotEvent shot = f.Fire();
        ProjectileEnd valid = f.End(shot, 92), malformed = valid;
        malformed.X = float.PositiveInfinity;
        f.Client.Survival().OnProjectileEnd(malformed);
        Assert.True(f.Client.Survival().Projectiles.Single().EndedAt == null, "nonfinite endpoint is refused before prediction binding");
        var target = new TargetRef(TargetFamily.Thing, 777, 1);
        valid.Target = (TargetFamily)255;
        valid.Contact = SurfaceContactNet.Of(SurfaceContact.At(target, 255, new Vector3(valid.X, valid.Y, valid.Z), Vector3.UnitZ,
            new TargetPose(Vector3.Zero, Quaternion.Identity)));
        valid.Contact.NormalZ = float.NaN;
        valid.Contact.LocalX = float.PositiveInfinity;
        f.Client.Survival().OnProjectileEnd(valid);
        f.Client.Survival().OnProjectileLaunch(f.Launch(shot, 92));
        ClientProjectile result = f.Client.Survival().Projectiles.Single();
        Assert.True(result.EndedAt != null, "later valid endpoint with same ID is accepted");
        Assert.Equal(target, result.Contact.Target, "safe target retained through cache");
        Assert.Equal(TargetFamily.None, result.EndTarget, "invalid legacy family sanitized");
        Assert.Equal(Vector3.UnitY, result.Contact.Normal, "normal finite before presentation");
        Assert.True(!result.Contact.Anchored, "bad local coordinate removes attachment");
        Assert.Equal("unknown", ContentFixture.Content.Surfaces.Key(result.Contact.Surface), "unknown material remains explicit neutral fallback");
    }

    [Test]
    public static void AuthoritativeContactsSurviveTerminalBeforeLaunchForOwnerAndObserver()
    {
        foreach (bool own in new[] { true, false })
        {
            using var f = new ProjectileClient("rifle", "rifle-ammo");
            ShotEvent shot = f.Fire();
            ProjectileEnd end = f.End(shot, 91);
            ProjectileLaunch launch = f.Launch(shot, 91);
            if (!own) { end.Shooter = 42; launch.Shooter = 42; }
            var target = new TargetRef(TargetFamily.Thing, 0x100000000123UL, 4);
            Vector3 point = new(end.X, end.Y, end.Z);
            SurfaceContact contact = SurfaceContact.At(target, ContentFixture.Content.Surfaces.FromDeployable("stone-compound-wall"),
                point, Vector3.Normalize(new Vector3(.2f, .3f, .7f)), new TargetPose(point - Vector3.UnitX, Quaternion.Identity));
            end.Target = TargetFamily.Thing; end.Contact = SurfaceContactNet.Of(contact);
            f.Client.Survival().OnProjectileEnd(end);
            f.Client.Survival().OnProjectileLaunch(launch);
            ClientProjectile projectile = f.Client.Survival().Projectiles.Single(p => p.Id == 91);
            Assert.Equal(target, projectile.Contact.Target, "full target survives packet reordering");
            Assert.Equal(contact.Surface, projectile.Contact.Surface, "same surface for owner and observer");
            Assert.Equal(contact.Anchor, projectile.Contact.Anchor, "moving frame survives");
            Assert.Near(0, Vector3.Distance(contact.Normal, projectile.Contact.Normal), .00001, "authoritative normal survives");
            SurfaceContact first = projectile.Contact;
            end.Contact.Surface = ContentFixture.Content.Surfaces.FleshId;
            for (int duplicate = 0; duplicate < 4; duplicate++)
            { f.Client.Survival().OnProjectileEnd(end); f.Client.Survival().OnProjectileLaunch(launch); }
            Assert.Equal(first, projectile.Contact, "duplicates cannot replace the first authoritative contact");
        }
    }

    [Test]
    public static void FullAttackLedgerSendsNoInvisibleGunAttemptAndFailedReloadDoesNotStart()
    {
        using (var f = new ProjectileClient())
        {
            for (uint seq = 1; seq <= AttackLedger.PendingLimit; seq++)
                Assert.True(f.Client.Attacks.Predict(f.Client.Attacks.Id(AttackDomain.Station, seq), 1), "fill shared admission budget");
            f.TryFire();
            Assert.Equal(0, f.SentMessages, "no fire intent sent without shared admission");
            Assert.Equal(0, f.Shots.Count, "no presentation for a refused local admission");
            Assert.Equal(0, f.Client.Local.Gun.PendingCount, "no invisible gun reservation");
            Assert.Equal(1, f.Client.Local.Gun.Loaded, "the round remains loaded");
        }
        using (var f = new ProjectileClient("rifle", "rifle-ammo"))
        {
            f.Client.Local.Inventory.Main.Set(0, ItemStack.Of(f.Ammo, 20));
            f.AllowSend = false;
            f.Client.Local.Gun.Reload();
            f.ReadyTick();
            Assert.True(!f.Client.Local.Gun.Reloading, "failed transport cannot leave a predicted reload running");
            Assert.Equal(0, f.Client.Local.Gun.PendingCount, "failed reload is not queued");
        }
    }

    [Test]
    public static void EveryRefusedPredictedRocketIsRemovedBeforeTheGunSnapshot()
    {
        using var f = new ProjectileClient();
        ShotEvent shot = f.Fire();
        Assert.Equal(1, f.Client.Survival().Projectiles.Count, "prediction is immediately visible");
        f.Client.Attacks.Resolve(new AttackResolution(shot.Attack, OutcomeCode.Cooldown, f.FireTick));
        Assert.Equal(0, f.Client.Survival().Projectiles.Count, "refusal retires the rocket without waiting for a gun update");
        Assert.Equal(0, f.Client.Local.Gun.PendingCount, "the refused ammo reservation is removed too");
        // Duplicate authoritative answers cannot replay or restore presentation.
        f.Client.Attacks.Resolve(new AttackResolution(shot.Attack, OutcomeCode.Cooldown, f.FireTick));
        Assert.Equal(0, f.Client.Survival().Projectiles.Count, "duplicate refusal remains final");
        f.Client.Local.Gun.OnUpdate(f.GunUpdate(shot.Seq, loaded: 1));
        Assert.Equal(1, f.Client.Local.Gun.Loaded, "authoritative round restored");
    }

    [Test]
    public static void ProjectileTerminalCanPrecedeOrReplaceItsLaunchAndIsIdempotent()
    {
        using var f = new ProjectileClient();
        ShotEvent shot = f.Fire();
        ClientProjectile projectile = f.Client.Survival().Projectiles.Single();
        int launches = 0;
        f.Client.Survival().ProjectileLaunched += _ => launches++;
        ProjectileEnd end = f.End(shot, 71);
        f.Client.Survival().OnProjectileEnd(end);
        Assert.Equal(71u, projectile.Id, "terminal binds own attempt even when the launch packet is missing");
        Assert.True(projectile.EndedAt != null, "terminal recorded before launch");
        Assert.True(projectile.PositionAt(f.Client.ClockSeconds) == projectile.EndedAt, "authoritative stop does not wait for the visual flight");
        double firstEnd = projectile.EndedTime;
        for (int i = 0; i < 8; i++)
        {
            f.Client.Survival().OnProjectileEnd(end);
            f.Client.Survival().OnProjectileLaunch(f.Launch(shot, 71));
        }
        Assert.Equal(1, f.Client.Survival().Projectiles.Count, "reordering and duplicates keep one object");
        Assert.Equal(0, launches, "own launch never replays its predicted release");
        Assert.Equal(firstEnd, projectile.EndedTime, "duplicate terminal cannot extend visual lifetime");
        Assert.Equal(1u, projectile.Revision, "authority corrects flight once");
    }

    [Test]
    public static void AuthoritativeProjectileLaunchReplacesTrajectoryAndClockWithoutReplayingEffects()
    {
        using var f = new ProjectileClient();
        ShotEvent shot = f.Fire();
        ClientProjectile projectile = f.Client.Survival().Projectiles.Single();
        ProjectileLaunch launch = f.Launch(shot, 72);
        launch.X += 8;
        launch.VelocityX = 3;
        launch.VelocityZ = -17;
        launch.Gravity = 2;
        launch.Drag = 0.25f;
        launch.Tick = 30;
        f.ServerClock(60);
        f.Client.Survival().OnProjectileLaunch(launch);
        Assert.True(projectile.Origin == new Vector3(launch.X, launch.Y, launch.Z), "uses authoritative muzzle/eye");
        Assert.True(projectile.Velocity == new Vector3(3, launch.VelocityY, -17), "uses authoritative velocity");
        Assert.Near(2, projectile.Gravity.Length(), 0.0001, "uses the actual flight profile");
        Assert.Near(0.25, projectile.Drag, 0.0001, "does not infer drag from calibre");
        Assert.Near(1, f.Client.ClockSeconds - projectile.LaunchedAt, 0.1, "launch tick accounts for time already flown");
        Assert.Equal(1, f.Shots.Count, "correction does not emit another local shot");
        Assert.Equal(1u, projectile.Revision, "views have one retarget notification");
    }

    [Test]
    public static void ProjectileTerminalHistoryIsBoundedAndCannotResurrectOldLaunches()
    {
        using var f = new ProjectileClient();
        for (uint id = 1; id <= SurvivalModule.ProjectileHistoryLimit + 16; id++)
            f.Client.Survival().OnProjectileEnd(new ProjectileEnd { Id = id, Shooter = 42, Tick = id });
        f.Client.Survival().OnProjectileLaunch(new ProjectileLaunch { Id = 1, Shooter = 42, Ammo = f.Ammo.Id, Y = 1010, Tick = 1 });
        Assert.Equal(0, f.Client.Survival().Projectiles.Count, "terminal eviction does not permit stale resurrection");
        f.Client.Survival().Dispose();
        f.Client.Survival().OnProjectileLaunch(new ProjectileLaunch { Id = 1, Shooter = 42, Ammo = f.Ammo.Id, Y = 1010, Tick = 1 });
        Assert.Equal(1, f.Client.Survival().Projectiles.Count, "new zone clears old IDs and floors");
    }

    [Test]
    public static void ActiveRocketStillReceivesItsEndpointAfterThousandsOfNewerBullets()
    {
        using var f = new ProjectileClient();
        ShotEvent shot = f.Fire();
        f.Client.Survival().OnProjectileLaunch(f.Launch(shot, 1));
        ClientProjectile projectile = f.Client.Survival().Projectiles.Single();
        for (uint id = 2; id <= SurvivalModule.ProjectileHistoryLimit + 32; id++)
            f.Client.Survival().OnProjectileEnd(new ProjectileEnd { Id = id, Shooter = 42, Tick = id });
        f.Client.Survival().OnProjectileEnd(f.End(shot, 1));
        Assert.True(projectile.EndedAt != null, "bounded duplicate history never hides an active rocket's endpoint");
    }

    [Test]
    public static void PreviousSessionProjectileCannotBindReusedAttackSequenceAfterReconnect()
    {
        ProjectileLaunch oldLaunch;
        ProjectileEnd oldEnd;
        using (var previous = new ProjectileClient())
        {
            previous.Client.Attacks.BeginSession(1);
            ShotEvent shot = previous.Fire();
            oldLaunch = previous.Launch(shot, 81);
            oldEnd = previous.End(shot, 81);
        }
        using var current = new ProjectileClient();
        current.Client.Attacks.BeginSession(2);
        ShotEvent fresh = current.Fire();
        ClientProjectile predicted = current.Client.Survival().Projectiles.Single();
        Assert.Equal(oldLaunch.AttackSeq, fresh.Attack.Sequence, "new session reuses the first sequence");
        current.Client.Survival().OnProjectileEnd(oldEnd);
        current.Client.Survival().OnProjectileLaunch(oldLaunch);
        Assert.True(predicted.Id == 0 && predicted.EndedAt == null, "the old session cannot end or bind the new shot");
        ClientProjectile old = current.Client.Survival().Projectiles.Single(p => p.Id == 81);
        Assert.True(!old.Own && old.EndedAt != null, "the old projectile remains an independently ended remote visual");
        current.Client.Survival().OnProjectileLaunch(current.Launch(fresh, 82));
        current.Client.Survival().OnProjectileEnd(current.End(fresh, 82));
        Assert.True(predicted.Id == 82 && predicted.EndedAt != null, "the current session still binds and ends normally");
    }

    [Test]
    public static void GunAttemptJournalAnswersEveryRejectionAndReplaysAcceptanceWithoutAnotherRound()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0);
        Guns.Arm(rig, ada, "rifle", "rifle-ammo", 2);
        Player p = rig.P(ada);
        rig.Zone.Events.Clear();
        Guns.Queue(rig, ada, 1);
        rig.Step();
        AttackResolution accepted = rig.Zone.Events.Of<AttackResolved>().Single(e => e.Resolution.Id.Sequence == 1).Resolution;
        Assert.Equal(OutcomeCode.Done, accepted.Code, "first shot accepted");
        int loaded = p.Inventory.Belt[0].Loaded;
        int bullets = rig.Zone.Guns().Bullets.Count;
        rig.Zone.Events.Clear();
        Assert.Equal(OutcomeCode.Stale, Guns.Queue(rig, ada, 1), "duplicate is not enqueued twice");
        Assert.Equal(accepted, rig.Zone.Events.Of<AttackResolved>().Single().Resolution, "original identity and decision replayed");
        Assert.Equal(loaded, (int)p.Inventory.Belt[0].Loaded, "duplicate consumes no ammo");
        Assert.Equal(bullets, rig.Zone.Guns().Bullets.Count, "duplicate launches no projectile");
        rig.Zone.Events.Clear();
        for (uint seq = 2; seq <= 6; seq++)
        {
            rig.Zone.Guns().QueueGunAction(ada, new GunAction
            {
                Seq = seq, Tick = rig.Zone.Tick, Slot = 0, Item = p.HeldItem,
                Kind = GunActionKind.Fire, Refused = OutcomeCode.RateLimited,
            });
        }
        AttackResolved[] rejected = rig.Zone.Events.Of<AttackResolved>().ToArray();
        Assert.Equal(5, rejected.Length, "coalesced gun snapshots cannot swallow individual refusals");
        Assert.Equal(5, rejected.Select(e => e.Resolution.Id).Distinct().Count(), "every attempt has an identity");
        Assert.True(rejected.All(e => e.Resolution.Code == OutcomeCode.RateLimited), "the budget result is retained");
    }

    [Test]
    public static void ReorderedGunShotsWaitForTheMissingAttemptBeforeAcknowledgingAmmunition()
    {
        using var rig = new GameRig();
        Entity actor = rig.Add("Order", 0, 0);
        Guns.Arm(rig, actor, "rifle", "rifle-ammo", 2);
        rig.Step(20);
        Player player = rig.P(actor);
        long secondTick = rig.Zone.Tick;
        rig.Zone.Events.Clear();
        Guns.Queue(rig, actor, 2, tick: secondTick);
        rig.Step(2);
        Assert.Equal(0u, player.Gun.LastSeq, "later arrival does not cumulatively acknowledge the missing shot");
        Assert.Equal(2, (int)player.Inventory.Belt[0].Loaded, "neither round is consumed before ordering is known");
        Assert.Equal(0, Guns.Launches(rig).Count, "later input is not executed ahead of the lost request");
        Guns.Queue(rig, actor, 1, tick: secondTick - 6);
        rig.Step();
        ProjectileLaunched[] launched = Guns.Launches(rig).ToArray();
        Assert.Equal(2, launched.Length, "both valid historical shots execute when the retransmission arrives");
        Assert.Equal(1u, launched[0].Attack.Sequence, "first identity is applied first");
        Assert.Equal(2u, launched[1].Attack.Sequence, "second identity follows the same ammunition timeline");
        Assert.Equal(2u, player.Gun.LastSeq, "cumulative state now covers both actual shots");
        Assert.Equal(0, (int)player.Inventory.Belt[0].Loaded, "exactly two rounds were consumed");
        rig.Zone.Events.Clear();
        Guns.Queue(rig, actor, 1, tick: secondTick - 6);
        rig.Step();
        Assert.Equal(0, Guns.Launches(rig).Count, "a retry cannot launch the accepted identity again");
    }

    [Test]
    public static void ReorderedGunRefusalCannotSkipAnEarlierValidShot()
    {
        using var rig = new GameRig();
        Entity actor = rig.Add("RefusalOrder", 0, 0);
        Guns.Arm(rig, actor, "rifle", "rifle-ammo", 2);
        rig.Step(20);
        Player player = rig.P(actor);
        long tick = rig.Zone.Tick;
        rig.Zone.Guns().QueueGunAction(actor, new GunAction
            { Seq = 2, Tick = tick, Kind = GunActionKind.Fire, Refused = OutcomeCode.RateLimited });
        rig.Step();
        Assert.Equal(0u, player.Gun.LastSeq, "even a known refusal cannot acknowledge a missing earlier action");
        Guns.Queue(rig, actor, 1, tick: tick - 6);
        rig.Step();
        Assert.Equal(1, Guns.Launches(rig).Count, "the earlier valid shot still fires exactly once");
        Assert.Equal(1, (int)player.Inventory.Belt[0].Loaded, "only the accepted round is consumed");
        Assert.Equal(2u, player.Gun.LastSeq, "both outcomes are incorporated in sequence");
        Assert.True(player.GunAttackJournal.TryGet(new AttackId(AttackDomain.Firearm, 1), out AttackResolution first) &&
            first.Code == OutcomeCode.Done, "the valid shot keeps its acceptance");
        Assert.True(player.GunAttackJournal.TryGet(new AttackId(AttackDomain.Firearm, 2), out AttackResolution second) &&
            second.Code == OutcomeCode.RateLimited, "the refused shot keeps its own result");
    }

    [Test]
    public static void MissingGunAttemptExpiresWithoutPermanentlyBlockingTheStream()
    {
        using var rig = new GameRig();
        Entity actor = rig.Add("MissingOrder", 0, 0);
        Guns.Arm(rig, actor, "rifle", "rifle-ammo", 1);
        rig.Step(20);
        Player player = rig.P(actor);
        Guns.Queue(rig, actor, 2, tick: rig.Zone.Tick);
        rig.Step(GunSystem.GunActionLateTicks + 1);
        Assert.Equal(0, player.GunQueue.Count, "a missing predecessor cannot hold a queue forever");
        Assert.Equal(2u, player.Gun.LastSeq, "expired gap advances the cumulative frontier");
        Assert.True(player.GunAttackJournal.TryGet(new AttackId(AttackDomain.Firearm, 2), out AttackResolution expired) &&
            expired.Code == OutcomeCode.TooLate, "the expired attempt receives an explicit refusal");
        Assert.Equal(1, (int)player.Inventory.Belt[0].Loaded, "expiry takes no round");
        Guns.Queue(rig, actor, 3);
        rig.Step();
        Assert.Equal(1, Guns.Launches(rig).Count, "the next in-order request fires on its normal next tick");
        Assert.Equal(0, (int)player.Inventory.Belt[0].Loaded, "only the fresh accepted request consumes the round");
    }

    [Test]
    public static void LateGunShotUsesHistoricalMovementForSpreadAndLowering()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0);
        Guns.Arm(rig, ada, "rifle", "rifle-ammo", 3);
        Player p = rig.P(ada);
        long shotTick = rig.Zone.Tick;
        ref Character ch = ref rig.Ch(ada);
        Vector3 up = rig.Zone.Frame.UpAt(ch.State.Position);
        var old = new HitPose
        {
            Feet = ch.State.Position, Up = up, Yaw = 0, Pose = Pose.Crouching,
            Velocity = Vector3.Zero, Flags = MoveFlags.OnGround,
        };
        rig.Zone.History.Record(p.Id, shotTick, old);
        rig.Step();
        ch.State.Flags = MoveFlags.Sprinting;
        ch.State.Stance = Stance.Standing;
        ch.State.Velocity = Facing.Compass(up).East * 6;
        Vector3 view = Facing.View(0.2f, 0.1f, up);
        GunState expectedState = p.Gun;
        ItemStack expectedStack = p.Inventory.Belt[0];
        var context = new ShotContext
        {
            Shooter = p.Id, Seq = 1, Tick = shotTick, Eye = old.Feet + up * MovementTuning.Default.CrouchingEyeHeight,
            Up = up, Yaw = Facing.YawOf(view, up), Pitch = Facing.PitchOf(view, up), Crouched = true, Velocity = old.Velocity,
        };
        Assert.Equal(OutcomeCode.Done, FirearmRules.Fire(ref expectedState, ref expectedStack, rig.C,
            rig.C.ItemByKey("rifle").Firearm!, context, out Shot expected), "historical shot is legal");
        rig.Zone.Events.Clear();
        rig.Zone.Guns().QueueGunAction(ada, new GunAction
        { Seq = 1, Tick = shotTick, Kind = GunActionKind.Fire, Slot = 0, Item = p.HeldItem, View = view });
        rig.Zone.Guns().StepGun(ada, ref ch, p);
        ProjectileLaunched launched = rig.Zone.Events.Of<ProjectileLaunched>().Single();
        Assert.True(launched.Velocity == expected.Direction * expected.Speed, "historical crouch/velocity/airborne state determines spread");
        Assert.True(launched.Position == expected.Origin, "historical eye is retained");
        Assert.Equal((uint)rig.Zone.Tick, launched.Tick, "flight begins on actual simulation spawn tick");
        Assert.Equal(new AttackId(AttackDomain.Firearm, 1), launched.Attack, "projectile keeps originating identity");
    }

    [Test]
    public static void LateGunHandoverUsesHistoricalShipRotationForTheInputFrame()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0);
        Guns.Arm(rig, ada, "rifle", "rifle-ammo", 3);
        Player p = rig.P(ada);
        Ship ship = rig.Zone.Ships().SpawnShip(rig.C.Ship("iron-wake")!, rig.Zone.GroundAt(80, 0), 0, EntityId.None, 100);
        Assert.True(rig.Zone.Frames.Ship(ship.Id.Value) != null, "handover source must have a walkable coordinate frame");
        ShipPose oldShip = ShipFrame.PoseOf(ship.State, ship.Def.CentreOfMass);
        long shotTick = rig.Zone.Tick;
        ref Character ch = ref rig.Ch(ada);
        Vector3 up = rig.Zone.Frame.UpAt(ch.State.Position);
        var old = new HitPose { Feet = ch.State.Position, Up = up, Pose = Pose.Standing, Flags = MoveFlags.OnGround };
        rig.Zone.History.Record(p.Id, shotTick, old);
        rig.Zone.History.RecordShip(ship.Id.Value, shotTick, oldShip);
        rig.Step();
        ship.State.Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(up, 0.9f) * oldShip.Rotation);
        Assert.True(rig.Zone.SeenPose(ship.Id.Value, shotTick, out ShipPose historical), "historical source frame exists");
        Assert.Equal(oldShip.Rotation, historical.Rotation, "requested tick retains the ship's original rotation");
        Vector3 view = Vector3.Normalize(oldShip.DirectionToWorld(Vector3.UnitZ));
        GunState expectedState = p.Gun;
        ItemStack expectedStack = p.Inventory.Belt[0];
        var context = new ShotContext
        {
            Shooter = p.Id, Seq = 1, Tick = shotTick, Eye = old.Feet + up * MovementTuning.Default.StandingEyeHeight,
            Up = up, Yaw = Facing.YawOf(view, up), Pitch = Facing.PitchOf(view, up),
        };
        Assert.Equal(OutcomeCode.Done, FirearmRules.Fire(ref expectedState, ref expectedStack, rig.C,
            rig.C.ItemByKey("rifle").Firearm!, context, out Shot expected), "historical handover shot is legal");
        rig.Zone.Events.Clear();
        rig.Zone.Guns().QueueGunAction(ada, new GunAction
        {
            Seq = 1, Tick = shotTick, Kind = GunActionKind.Fire, Slot = 0, Item = p.HeldItem,
            View = Vector3.UnitZ, Frame = ship.Id.Value,
        });
        rig.Zone.Guns().StepGun(ada, ref ch, p);
        ProjectileLaunched launched = rig.Zone.Events.Of<ProjectileLaunched>().Single();
        Vector3 expectedVelocity = expected.Direction * expected.Speed;
        Assert.True(Vector3.Distance(launched.Velocity, expectedVelocity) < 0.001f,
            $"late view conversion: actual {launched.Velocity}, expected {expectedVelocity}; historical {historical.Rotation}, current {ship.State.Rotation}");
    }

    [Test]
    public static void GunAndRocketLifecycleSettlesThroughLatencyJitterAndLoss()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("AttackAda", new NetConditions { LatencyMs = 120, JitterMs = 40, LossPercent = 12 });
        rig.WaitUntilPlaying();
        GameContent content = ada.Content;
        var actual = new List<ProjectileLaunched>();
        rig.Server.Inspect(server =>
        {
            server.Zone.Players().TryFindPlayer(ada.EntityId, out Entity e, out Player p);
            server.Zone.SetCheats(e, MoveCheats.God); // Lifecycle test is independent of a nearby rocket's self damage.
            ItemStack rifle = ItemStack.Of(content.ItemByKey("rifle"), 1);
            rifle.Loaded = 8;
            rifle.LoadedAmmo = content.ItemByKey("rifle-ammo").Id;
            p.Inventory.Belt.Set(2, rifle);
            ItemStack launcher = ItemStack.Of(content.ItemByKey("rocket-launcher"), 1);
            launcher.Loaded = 1;
            launcher.LoadedAmmo = content.ItemByKey("rocket").Id;
            p.Inventory.Belt.Set(3, launcher);
            server.Zone.Events.On<ProjectileLaunched>((in ProjectileLaunched shot) =>
            {
                if (shot.Shooter == ada.EntityId && shot.Attack.Kind == AttackDomain.Firearm) actual.Add(shot);
            });
            return true;
        });
        InputFrame Still(GameClient _, double __) => new() { Pitch = -0.08f };
        var visual = new Dictionary<AttackId, ClientProjectile>();
        var shots = new List<ShotEvent>();
        ada.Local.Gun.ShotFired += shot =>
        {
            shots.Add(shot);
            Assert.True(visual.TryAdd(shot.Attack, ada.Survival().Projectiles.Single(p => p.Attack == shot.Attack)),
                "rapid clicks never replay a visual attack identity");
        };
        Wait(rig, () => ada.Local.Inventory.Belt[2].Item == content.ItemByKey("rifle").Id &&
            ada.Local.Inventory.Belt[3].Item == content.ItemByKey("rocket-launcher").Id, Still, "weapons replicated");
        foreach ((byte slot, string key, int rounds) in new[] { ((byte)2, "rifle", 8), ((byte)3, "rocket-launcher", 1) })
        {
            ada.Local.HeldSlot = slot;
            ushort weaponId = content.ItemByKey(key).Id;
            Wait(rig, () => ada.Local.Gun.Ready && ada.Local.Gun.Weapon?.Item.Id == weaponId && rig.Server.Inspect(server =>
            {
                server.Zone.Players().TryFindPlayer(ada.EntityId, out _, out Player p);
                return p.HeldSlot == slot && p.HeldItem == weaponId && server.Zone.Tick >= p.Gun.EquipReadyTick;
            }), Still, "client and authority equipped the same ready weapon");
            int before = visual.Count;
            bool wasDown = false;
            int presses = 0;
            rig.Run(1.2, (_, t) =>
            {
                bool down = (int)(t * 40) % 2 == 0; // Twenty press/release cycles per second.
                bool pressed = down && !wasDown;
                wasDown = down;
                if (pressed) presses++;
                return new InputFrame { Pitch = -0.08f, Primary = down, PrimaryPressed = pressed };
            });
            Assert.True(presses >= 3, $"{key} received repeated press/release edges ({presses} presses)");
            Assert.True(visual.Count > before, $"{key} responds locally under network impairment");
            Wait(rig, () => ada.Local.Gun.PendingCount == 0 && visual.Keys.All(id => ada.Attacks.TryGet(id, out AttackRecord r) && r.Resolution != null),
                Still, "every fire intent answered");
            int serverLoaded = rig.Server.Inspect(server =>
            {
                server.Zone.Players().TryFindPlayer(ada.EntityId, out _, out Player p);
                return (int)p.Inventory.Belt[slot].Loaded;
            });
            Assert.Equal(serverLoaded, ada.Local.Gun.Loaded, "authoritative and predicted magazine settle together");
            ShotEvent[] shown = shots.Where(s => s.Weapon.Key == key).ToArray();
            ProjectileLaunched[] served = rig.Server.Inspect(_ => actual.Where(s => s.Weapon == weaponId).ToArray());
            string refusals = string.Join(", ", shown.Select(s => ada.Attacks.TryGet(s.Attack, out AttackRecord r) ? r.Resolution : null)
                .Where(r => r is { Code: not OutcomeCode.Done }).Select(r => $"{r!.Value.Id.Sequence}:{r.Value.Code}@{r.Value.RequestedTick}"));
            if (shown.Length != served.Length || shown.Length > rounds || served.Length > rounds)
                Fixtures.Report($"{key} shot-count mismatch: visual={shown.Length}, authority={served.Length}, loaded={rounds}; refused attempts: {refusals}");
            Assert.True(shown.Length <= rounds && served.Length <= rounds,
                $"{key}: {shown.Length} visual/{served.Length} authority shots cannot exceed {rounds} loaded rounds; refused attempts: {refusals}");
            Assert.Equal(shown.Length, shown.Select(s => s.Attack).Distinct().Count(), $"{key} has unique visual identities");
            Assert.Equal(served.Length, served.Select(s => s.Attack).Distinct().Count(), $"{key} launches each accepted identity once");
            Assert.Equal(rounds - serverLoaded, served.Length, $"{key} authority consumes exactly one round per projectile");
            Assert.True(served.Length > 0 && served.All(s => visual.ContainsKey(s.Attack)), $"{key} authoritative shots match immediate predictions");
            Wait(rig, () => visual.All(pair => ada.Attacks.TryGet(pair.Key, out AttackRecord r) && r.Resolution is { } answer &&
                (answer.Code == OutcomeCode.Done ? pair.Value.EndedAt != null : !ada.Survival().Projectiles.Contains(pair.Value))),
                Still, "each accepted projectile receives its terminal, refusals disappear", 8);
            Fixtures.Report($"{key}: {presses} rapid clicks, {shown.Length} unique visuals, {served.Length} authoritative shots, {serverLoaded}/{rounds} rounds remain");
        }
        Assert.True(visual.Keys.Count(id => ada.Attacks.TryGet(id, out AttackRecord r) && r.Resolution?.Code == OutcomeCode.Done) >= 2,
            "real server accepted both weapon families");
        Wait(rig, () => ada.Survival().Projectiles.Count == 0, Still, "terminal presentation retires", 3);
        Assert.Equal(0, ada.Attacks.PendingCount, "no unresolved attacks");
        Fixtures.Report($"{visual.Count} rapid-click gun/rocket predictions resolved under 120 ms RTT, 40 ms jitter and 12% datagram loss; no unbound or lingering projectiles");
    }

    private static void Wait(LoopbackRig rig, Func<bool> done, Func<GameClient, double, InputFrame> input, string label, double seconds = 8)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < seconds, "timed out: " + label);
            rig.Run(0.05, input);
        }
    }

    // Deliver the real Welcome handler so the client's session owns its world, predictor and module readers.
    // Reflection supplies only message delivery and a clock value; the gun transport sink stays controlled here.
    private sealed class ProjectileClient : IDisposable
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public GameClient Client { get; }
        public ItemDef Weapon { get; }
        public ItemDef Ammo { get; }
        public List<ShotEvent> Shots { get; } = new();
        public uint FireTick { get; }
        public int SentMessages { get; private set; }
        public bool AllowSend { get; set; } = true;
        private readonly Predictor _predictor;
        private readonly CharacterState _character;

        public ProjectileClient(string weapon = "rocket-launcher", string ammo = "rocket")
        {
            GameContent content = ContentFixture.Content;
            Weapon = content.ItemByKey(weapon);
            Ammo = content.ItemByKey(ammo);
            var spec = ZoneSpec.StartingWorld(0, 0) with
            {
                Radius = 1000, SurfaceGravity = 1, SurfaceSeed = 0,
                Ground = GroundKind.StandIn, StandIn = true,
            };
            Client = new GameClient(new ClientOptions { Content = content });
            try
            {
                EnvironmentIdentity identity = NetConvert.ToEnvironment(spec.EnvironmentDescriptor());
                ZoneInfo zone = NetConvert.ToInfo(spec);
                var offer = new WorldOffer { OfferId = 1, Zone = zone, Environment = identity };
                typeof(GameClient).GetMethod("OnWorldOffer", Private)!.Invoke(Client, new object[] { offer });
                var welcome = new Welcome
                {
                    OfferId = offer.OfferId, Environment = identity, Zone = zone, NetId = 1, EntityId = 1,
                    AttackSession = 1, ServerName = "projectile fixture",
                };
                typeof(GameClient).GetMethod("OnWelcome", Private)!.Invoke(Client, new object[] { welcome });
                Assert.Equal(ClientState.Playing, Client.State, "the fixture opens through the production world session");
                _predictor = Client.Prediction ?? throw new TestFailure("Welcome did not create prediction.");
                Client.Local.SendGun = write => { SentMessages++; write(new BitWriter(256)); return AllowSend; };
                ItemStack stack = ItemStack.Of(Weapon, 1);
                stack.Loaded = 1;
                stack.LoadedAmmo = Ammo.Id;
                Client.Local.Inventory.Belt.Set(0, stack);
                _character = new CharacterState { Position = new Vector3(0, 1010, 0), Health = 100, Flags = MoveFlags.OnGround, Stance = Stance.Standing };
                Client.Local.Gun.ShotFired += Shots.Add;
                Tick(1, false);
                FireTick = (uint)Weapon.Firearm!.EquipTicks + 2;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void ServerClock(double tick) => typeof(GameClient).GetField("_remoteClock", Private)!.SetValue(Client, tick);

        private void Tick(uint tick, bool fire)
        {
            InputCommand input = NetConvert.ToCommand(new MoveInput { View = Vector3.UnitX }, new ActionInput
            { HeldSlot = 0, Buttons = fire ? ActionButtons.Primary | ActionButtons.PrimaryPressed : ActionButtons.None });
            Client.Local.Gun.Tick(tick, input, _character, _predictor.Frames, null, _predictor.Tuning, new EntityId(1), true);
        }

        public ShotEvent Fire()
        {
            Tick(FireTick, true);
            Assert.Equal(1, Shots.Count, "one actual local predicted shot");
            return Shots[0];
        }

        public void TryFire() => Tick(FireTick, true);
        public void ReadyTick() => Tick(FireTick, false);

        public GunUpdate GunUpdate(uint seq, uint loaded) => new()
        { LastSeq = seq, Slot = 0, Item = Weapon.Id, Loaded = loaded, LoadedAmmo = Ammo.Id, EquipReadyTick = 1 };

        public ProjectileLaunch Launch(ShotEvent shot, uint id) => new()
        {
            Id = id, Shooter = 1, Ammo = Ammo.Id, Weapon = Weapon.Id, Seq = shot.Seq,
            AttackKind = shot.Attack.Kind, AttackSeq = shot.Attack.Sequence, AttackSession = shot.Attack.Session, Tick = FireTick,
            X = shot.Origin.X, Y = shot.Origin.Y, Z = shot.Origin.Z,
            VelocityX = shot.Direction.X * shot.Speed, VelocityY = shot.Direction.Y * shot.Speed, VelocityZ = shot.Direction.Z * shot.Speed,
            Gravity = (float)Weapon.Firearm!.GravityOf(Ammo.Bullet, 1), Drag = (float)Weapon.Firearm.Drag,
        };

        public ProjectileEnd End(ShotEvent shot, uint id)
        {
            Vector3 at = shot.Origin + shot.Direction * 5;
            return new ProjectileEnd
            {
                Id = id, Shooter = 1, AttackKind = shot.Attack.Kind, AttackSeq = shot.Attack.Sequence, AttackSession = shot.Attack.Session,
                Tick = FireTick + 4, X = at.X, Y = at.Y, Z = at.Z, Target = TargetFamily.Ground,
            };
        }

        public void Dispose()
        {
            try { Client.Dispose(); }
            finally { Client.EnvironmentRetirement.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult(); }
        }
    }
}
