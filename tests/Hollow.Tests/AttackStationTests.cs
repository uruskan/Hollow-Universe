using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Net.Transport;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>Station attack identity, ammunition reservations, ordering and real lossy transport.</summary>
public static class AttackStationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // Inject messages at the actual handlers so their order is deterministic; prediction itself is production code.
    private sealed class ClientRig : IDisposable
    {
        public readonly GameClient Client = new(new ClientOptions { Content = ContentFixture.Content });
        public readonly StationModule Module;
        public readonly ShipDef Def = ContentFixture.Content.Ship("iron-wake")!;
        public readonly ShipStationDef Station;
        public readonly RemoteShip Ship;
        public readonly List<StationShotView> Shots = new();
        public readonly ushort Round = ContentFixture.Item("rocket").Id;
        private readonly Vector3 _view;

        public ClientRig(bool ripple = false)
        {
            Module = Client.Stations();
            Station = ripple ? Def.Stations.First(s => s.Battery.Fire == BatteryFire.Ripple) : Def.Stations.First(s => s.Id == "Siege_Forward");
            Ship = (RemoteShip)typeof(ShipModule).GetMethod("RemoteShipOf", Private)!.Invoke(Client.Ships(), new object[] { 7u })!;
            Ship.Def = Def;
            Ship.Spawned = true;
            ShipInterior interior = Def.Interior!;
            _view = StationRules.BarrelDirection(interior, Station, interior.Rest);
            var mount = new StationState(Station)
            {
                Yaw = interior.Rest[Station.YawPart], Pitch = interior.Rest[Station.PitchPart],
                Input = new StationInput { View = _view },
            };
            typeof(StationModule).GetField("_mount", Private)!.SetValue(Module, mount);
            typeof(StationModule).GetField("_mountShip", Private)!.SetValue(Module, Ship.NetId);
            typeof(StationModule).GetField("_mountStation", Private)!.SetValue(Module, Station.Index);
            Module.Shot += Shots.Add;
            State(0, 0, true, true);
        }

        public void Trigger(uint tick, bool fire) => typeof(StationModule).GetMethod("PredictTrigger", Private)!.Invoke(Module,
            new object[] { tick, Ship, Station, Def.Interior!, new StationInput { View = _view, Fire = fire }, Quaternion.Identity });

        public Vector3 AimAt(float distance)
        {
            var mount = (StationState)typeof(StationModule).GetField("_mount", Private)!.GetValue(Module)!;
            var pose = new ShipPose(Ship.Position, Ship.Rotation, Vector3.Zero, Vector3.Zero, Def.CentreOfMass);
            var parts = new float[Def.Interior!.Parts.Count];
            Ship.PartsInto(Def.Interior, parts, 100);
            StationRules.Into(mount, parts);
            Vector3 eye = pose.ToWorld(ShipSeats.EyeInFrame(Def, Station.Seat, parts));
            mount.Aim = StationRules.AimAt(Def.Interior, Station, parts, pose, eye, _view, distance);
            return mount.Aim.Point;
        }

        public void State(uint number, int next, bool first, bool second)
        {
            var state = new StationState(Station) { Shots = number, NextBarrel = next };
            state.Chambers[0] = first ? Round : (ushort)0;
            state.Chambers[1] = second ? Round : (ushort)0;
            for (int b = 0; b < 2; b++)
            {
                if (state.Chambers[b] == 0)
                {
                    state.ShotTicks[b] = 100 + b * 2;
                    state.Loaders[b] = new LoaderState { Active = true, Since = state.ShotTicks[b] };
                }
            }
            State(state);
        }

        public void State(StationState state) => typeof(StationModule).GetMethod("OnStations", Private)!.Invoke(Module, new object[]
            {
                new ShipStations { Ship = Ship.NetId, Stations = new[] { StationConvert.ToNet(state) } },
            });

        public void Shot(AttackId id, uint number, int barrel, uint tick) => typeof(StationModule).GetMethod("OnShot", Private)!.Invoke(Module,
            new object[] { new TurretShot
            {
                Ship = Ship.NetId, Station = (byte)Station.Index, Barrel = (byte)barrel, Number = number, Tick = tick,
                Kind = Round, Gunner = Client.NetId, AttackKind = id.Kind, AttackSeq = id.Sequence, AttackSession = id.Session,
                OriginX = 4, OriginY = 5, OriginZ = 6, DirectionZ = 1,
            } });

        public void Resolve(uint seq, OutcomeCode code, uint number = 0, int barrel = -1, uint tick = 100) =>
            Client.Attacks.Resolve(new AttackResolution(new AttackId(AttackDomain.Station, seq), code, tick,
                StartTick: tick, StateSequence: number, Barrel: barrel));

        public void Dispose() => Client.Dispose();
    }

    [Test]
    public static void PredictedStationRoundsFromSeparateMuzzlesMeetOneCentreRayPoint()
    {
        using var rig = new ClientRig();
        Vector3 target = rig.AimAt(500);
        rig.Trigger(100, true);
        rig.Trigger(101, false);
        rig.Trigger(102, true);
        Assert.Equal(2, rig.Shots.Count, "both physically separate barrels fire");
        Assert.True(Vector3.Distance(rig.Shots[0].Origin, rig.Shots[1].Origin) > 1, "the rockets keep their muzzle origins");
        foreach (StationShotView shot in rig.Shots)
        {
            Vector3 at = shot.Origin + shot.Direction * Vector3.Dot(target - shot.Origin, shot.Direction);
            Assert.True(Vector3.Distance(at, target) < 0.002f, "the production prediction sends each muzzle to the centre-ray target");
        }
        using var blocked = new ClientRig();
        blocked.AimAt(0.5f);
        blocked.Trigger(100, true);
        Assert.Equal(OutcomeCode.HullBlocked, blocked.Module.Refused.Code, "a surface before the muzzle refuses prediction");
        Assert.Equal(0, blocked.Shots.Count, "a blocked centre-ray shot consumes no predicted round");
    }

    [Test]
    public static void AcceptedReservationsSurviveConfirmationBeforeStateAndLateDuplicates()
    {
        using var rig = new ClientRig();
        rig.Trigger(100, true);
        rig.Trigger(101, false);
        rig.Trigger(102, true);
        rig.Trigger(103, false);
        Assert.Equal(0, rig.Shots[0].Barrel, "the fresh first barrel agrees with authority");
        Assert.Equal(1, rig.Shots[1].Barrel, "the second chamber is reserved separately");
        rig.Shot(rig.Shots[0].Attack, 1, 0, 100);
        Assert.True(!rig.Module.Display(rig.Ship, rig.Station.Index)!.Loaded(0), "confirmation keeps the first reservation before state");
        rig.Trigger(104, true);
        Assert.Equal(2, rig.Shots.Count, "a third click cannot reuse the confirmed chamber");
        Assert.Equal(2, rig.Client.Attacks.Count, "a third empty click submits no deferred fire attempt");
        rig.State(1, 1, false, true);
        Assert.True(!rig.Module.Display(rig.Ship, rig.Station.Index)!.Loaded(1), "the second pending attempt survives the first shot's state");
        rig.Resolve(1, OutcomeCode.Done, 1, 0);
        rig.Resolve(2, OutcomeCode.Done, 2, 1, 102);
        rig.State(2, 0, false, false);
        rig.Shot(rig.Shots[1].Attack, 2, 1, 102);
        rig.Shot(rig.Shots[0].Attack, 1, 0, 100);
        Assert.Equal(2, rig.Shots.Count, "late and duplicate confirmations never replay presentation");
        Assert.Equal(0, rig.Module.PendingShots, "both state reservations settle");
        Assert.Equal(0L, rig.Module.ShotsUndone, "no legitimate prediction times out");
    }

    [Test]
    public static void RefusedAttemptsDoNotConsumeTheNextShotsIdentityAndServerBarrelCorrectsReservation()
    {
        using var rig = new ClientRig();
        rig.Trigger(100, true);
        rig.Resolve(1, OutcomeCode.Traversing);
        rig.Trigger(101, false);
        rig.Trigger(102, true);
        Assert.Equal(2u, rig.Shots[1].Attack.Sequence, "the refused attempt's identity is never reused");
        rig.Shot(rig.Shots[1].Attack, 1, 1, 102); // A different authoritative barrel, still the exact same attempt.
        StationState display = rig.Module.Display(rig.Ship, rig.Station.Index)!;
        Assert.True(display.Loaded(0) && !display.Loaded(1), "acceptance corrects the reserved barrel without firing again");
        rig.State(1, 0, true, false);
        rig.Resolve(2, OutcomeCode.Done, 1, 1, 102);
        rig.Resolve(1, OutcomeCode.Traversing);
        rig.Shot(rig.Shots[1].Attack, 1, 1, 102);
        Assert.Equal(2, rig.Shots.Count, "correction and duplicate answers do not emit another shot");
        Assert.Equal(1L, rig.Module.ShotsConfirmed, "the accepted attempt confirms exactly once");
        Assert.Equal(1L, rig.Module.ShotsUndone, "only the explicit refusal is withdrawn");
        Assert.Equal(0, rig.Module.PendingShots, "no abandoned reservation");
    }

    [Test]
    public static void StateBeforeAcceptancePreservesReservationsUntilTheExactAttemptIsKnown()
    {
        using var rig = new ClientRig();
        rig.Trigger(100, true);
        rig.State(1, 1, false, true);
        rig.Trigger(101, false);
        rig.Trigger(102, true);
        rig.State(2, 0, false, false);
        rig.Resolve(2, OutcomeCode.Done, 2, 1, 102);
        rig.Resolve(1, OutcomeCode.Done, 1, 0);
        rig.Shot(rig.Shots[0].Attack, 1, 0, 100);
        rig.Shot(rig.Shots[1].Attack, 2, 1, 102);
        Assert.Equal(2, rig.Shots.Count, "state-first delivery neither frees a spent chamber nor replays a shot");
        Assert.Equal(0, rig.Module.PendingShots, "acceptance retires already-covered state reservations");
    }

    [Test]
    public static void EmptyPressesNeverBecomeDelayedAuthorityShotsAfterEarlierRefusal()
    {
        using var rig = new ClientRig();
        rig.Trigger(100, true);
        rig.Trigger(100, false);
        rig.Trigger(100, true);
        Assert.Equal(OutcomeCode.Cooldown, rig.Module.Refused.Code, "a too-early press receives its local cadence refusal");
        Assert.Equal(1, rig.Client.Attacks.Count, "a cadence refusal submits no attack identity");
        rig.Trigger(101, false);
        rig.Trigger(102, true);
        for (uint tick = 103; tick < 115; tick++) rig.Trigger(tick, tick % 2 == 0);
        Assert.Equal(2, rig.Shots.Count, "only the two available rounds are presented");
        Assert.Equal(2, rig.Client.Attacks.Count, "all empty spam presses stop before allocating a submitted identity");
        Assert.Equal(2, rig.Module.PendingShots, "only the two submitted predictions await authority");
        rig.Resolve(1, OutcomeCode.Done, 1, 0);
        rig.State(1, 1, false, true);
        rig.Resolve(2, OutcomeCode.TooLate, tick: 102);
        rig.Trigger(120, true); // Still held after the last empty press; a decision cannot turn it into a new edge.
        Assert.Equal(2, rig.Shots.Count, "a refusal cannot replay one of the intervening empty clicks");
        Assert.Equal(2, rig.Client.Attacks.Count, "no formerly empty press reaches authority later");
        Assert.Equal(0, rig.Module.PendingShots, "the confirmed and refused predictions both settle");
        rig.Trigger(121, false);
        rig.Trigger(122, true);
        Assert.Equal(3u, rig.Shots[2].Attack.Sequence, "a genuinely new valid press receives the next contiguous wire identity");
    }

    [Test]
    public static void CompletedSharedLoaderPreservesAllThreePredictedRippleRoundsBeforeState()
    {
        using var rig = new ClientRig(ripple: true);
        var carried = new StationState(rig.Station);
        carried.Loaders[0] = new LoaderState { Active = true, Since = 1, Kind = rig.Round, Count = 3 };
        rig.State(carried); // Last authority record still carries a cassette; it completes before the next trigger.
        uint tick = (uint)Math.Ceiling(rig.Station.Battery.Loader.Seconds * CharacterMotor.TickRate) + 2;
        uint interval = (uint)rig.Station.Battery.IntervalTicks;
        rig.Trigger(tick, true);
        StationState display = rig.Module.Display(rig.Ship, rig.Station.Index)!;
        Assert.True(!display.Loaded(0) && display.Loaded(1) && display.Loaded(2), "first shot reserves only its own round of the completed cassette");
        rig.Trigger(tick + interval, true);
        rig.Trigger(tick + interval * 2, true);
        rig.Trigger(tick + interval * 3, true);
        Assert.Equal(3, rig.Shots.Count, "the three loaded ripple rounds predict without waiting for another station record");
        Assert.Equal(3, rig.Client.Attacks.Count, "the empty fourth ripple interval submits no future surprise shot");
        Assert.Equal(3, rig.Shots.Select(s => s.Barrel).Distinct().Count(), "each tube fires only once");
        var empty = new StationState(rig.Station) { Shots = 3 };
        empty.Loaders[0] = new LoaderState { Active = true, Waiting = true, Since = tick + interval * 2 };
        rig.State(empty);
        for (uint seq = 1; seq <= 3; seq++) rig.Resolve(seq, OutcomeCode.Done, seq, (int)seq - 1, tick + (seq - 1) * interval);
        Assert.Equal(0, rig.Module.PendingShots, "all three reservations retire when authority covers their shots");
    }

    [Test]
    public static void AuthorityUsesCurrentCadenceAndRejectsTheWrongSeat()
    {
        using var rig = new IronWakeRig();
        ShipStationDef station = rig.Def.Stations.First(s => s.Id == "Siege_Forward");
        Entity gunner = rig.Aboard("AttackStation", rig.Def.Interior!.SocketAt(rig.Def.Seats[station.Seat].Exits[0], rig.Ship.Parts), Vector3.UnitZ);
        rig.Step(3);
        rig.Zone.Ships().Authorise(rig.Game.P(gunner).Id.Value, rig.Ship);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(gunner, new TargetRef(TargetFamily.Ship, rig.Ship.Id.Value, (byte)station.Part), "man").Code, "the station is manned");
        Vector3 view = Vector3.Normalize(rig.Pose.DirectionToWorld(new Vector3(0, 0.174f, 0.985f)));
        rig.Zone.Ships().SetStationInput(gunner, new StationInput { View = view });
        rig.Step(90);
        StationState state = rig.Ship.Stations[station.Index];
        state.Magazine.Clear();
        Array.Clear(state.Loaders);
        state.Chambers[0] = state.Chambers[1] = ContentFixture.Item("rocket").Id;
        AttackRequest Request(uint seq, int target) => new(new AttackId(AttackDomain.Station, seq), (uint)rig.Zone.Tick,
            View: view, Source: rig.Ship.Id, Station: target);
        Assert.Equal(OutcomeCode.NotFound, rig.Zone.Ships().FireStation(gunner, Request(1, station.Index + 1)).Code, "another station cannot be fired by this seat");
        AttackResolution first = rig.Zone.Ships().FireStation(gunner, Request(2, station.Index));
        Assert.Equal(OutcomeCode.Done, first.Code, "first authoritative round");
        Assert.Equal(0, first.Barrel, "first authoritative barrel");
        Assert.Equal(OutcomeCode.Cooldown, rig.Zone.Ships().FireStation(gunner, Request(3, station.Index) with { Tick = (uint)rig.Zone.Tick - 2 }).Code, "backdating cannot bypass current cadence");
        rig.Step();
        AttackResolution second = rig.Zone.Ships().FireStation(gunner, Request(4, station.Index));
        Assert.Equal(OutcomeCode.Done, second.Code, "second authoritative round");
        Assert.Equal(1, second.Barrel, "second authoritative barrel");
        rig.Step();
        Assert.True(rig.Zone.Ships().FireStation(gunner, Request(5, station.Index)).Code != OutcomeCode.Done, "no third round exists");
        Assert.Equal(2u, state.Shots, "success count is independent of five attempt identities");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Ships().LeaveSeat(gunner).Code, "the gunner leaves");
        Assert.Equal(OutcomeCode.NotFound, rig.Zone.Ships().FireStation(gunner, Request(6, station.Index)).Code, "an in-flight intent is refused after leaving its seat");
    }

    [Test]
    public static void SpamCannotProduceMoreThanTwoShotsAcrossLatencyJitterAndLoss()
    {
        foreach (NetConditions conditions in new[]
        {
            NetConditions.None,
            new NetConditions { LatencyMs = 180, JitterMs = 35, LossPercent = 15 },
        })
        {
            // On the simulated clock, after the client has settled in: how many of the two rounds the server accepts must not
            // depend on whether a busy PC paused the client's clock or held its prediction for ground.
            using var rig = new LoopbackRig(simulated: true);
            GameClient client = rig.Join("StationSpam", conditions);
            rig.WaitUntilPlaying();
            rig.WaitUntilSettled();
            IronWakeNetTests.ShipFacts ship = IronWakeNetTests.Spawn(rig, client.Name);
            ShipDef def = ContentFixture.Content.Ship("iron-wake")!;
            ShipStationDef station = def.Stations.First(s => s.Id == "Siege_Forward");
            Vector3 view = Vector3.Normalize(ship.Pose.DirectionToWorld(new Vector3(0, 0.174f, 0.985f)));
            InputFrame Input(bool fire) => new() { View = view, ViewFrame = 0, Station = new StationInput { Fire = fire } };
            string State()
            {
                StationModule module = client.Stations();
                StationState? mount = (StationState?)typeof(StationModule).GetField("_mount", Private)!.GetValue(module);
                string authority = rig.Server.Inspect(server =>
                {
                    Ship current = server.Zone.Ships().All.First(s => s.Id.Value == ship.Id);
                    StationState state = current.Stations[station.Index];
                    Outcome check = server.Zone.Ships().CanFire(current, state, false, server.Zone.Tick, out int barrel);
                    return $"authority={check.Code}/b{barrel}, yaw/pitch={state.Yaw:0.000}/{state.Pitch:0.000}, target={state.Aim.Point}, view={state.Input.View}";
                });
                return $"refusal={module.Refused.Code}, predicted={module.ShotsPredicted}, pending={module.PendingShots}, " +
                    $"client yaw/pitch={mount?.Yaw:0.000}/{mount?.Pitch:0.000}, target={mount?.Aim.Point}, view={mount?.Input.View}; {authority}";
            }
            void Wait(Func<bool> ready, string requirement, double seconds = 5)
            {
                double start = rig.Elapsed;
                while (!ready() && rig.Elapsed - start < seconds) rig.Run(0.05, (_, _) => Input(false));
                if (!ready()) throw new TestFailure($"{conditions}: {requirement} within {seconds}s; {State()}");
            }
            IronWakeNetTests.Heal(rig, client.EntityId);
            Vector3 entry = def.Interior!.SocketAt(def.Seats[station.Seat].Exits[0], def.Interior.Rest);
            IronWakeNetTests.PutAboard(rig, client.EntityId, ship.Id, entry);
            Wait(() => client.Ships().ShipByNetId(client.OwnFrame)?.Id.Value == ship.Id &&
                Vector3.Distance(client.Character.Position, entry) < 1.25f, "the authoritative deck teleport reaches client prediction");
            var answers = new List<IntentAnswer>();
            client.Local.IntentAnswered += answers.Add;
            uint seatRequest = client.Local.Interact(new TargetRef(TargetFamily.Ship, ship.Id, (byte)station.Part), "man");
            Wait(() => answers.Any(a => a.Seq == seatRequest), "seat request receives its authoritative decision");
            Assert.Equal(OutcomeCode.Done, answers.Single(a => a.Seq == seatRequest).Code, "seat interaction is accepted");
            Wait(() => client.Stations().Own is { } own && own.Ship.Id.Value == ship.Id && own.Station.Index == station.Index,
                "the client receives the accepted cannon seat");
            var actual = new List<TurretFired>();
            var authorityChecks = new List<string>();
            rig.Server.Inspect(server =>
            {
                Ship authority = server.Zone.Ships().All.First(s => s.Id.Value == ship.Id);
                StationState state = authority.Stations[station.Index];
                state.Magazine.Clear();
                Array.Clear(state.Loaders);
                state.Chambers[0] = state.Chambers[1] = ContentFixture.Item("rocket").Id;
                state.NextBarrel = 0;
                authority.StationsRevision++;
                server.Zone.Events.On<TurretFired>((in TurretFired shot) => { if (shot.Ship.Value == ship.Id) actual.Add(shot); });
                server.Zone.Events.On<AttackResolved>((in AttackResolved answer) =>
                {
                    if (answer.Player != client.EntityId || answer.Resolution.Id.Kind != AttackDomain.Station) return;
                    ShipPose pose = ShipFrame.PoseOf(authority.State, def.CentreOfMass);
                    Vector3 eye = pose.ToWorld(ShipSeats.EyeInFrame(def, station.Seat, authority.Parts));
                    (ushort u, ushort v) = Hollow.Core.Quantize.Octahedral(view);
                    Vector3 requestView = Hollow.Core.Quantize.FromOctahedral(u, v);
                    (u, v) = Hollow.Core.Quantize.Octahedral(requestView);
                    requestView = Hollow.Core.Quantize.FromOctahedral(u, v);
                    server.Zone.Entities.TryFind(client.EntityId, out Entity gunner);
                    byte delay = server.Zone.Entities.Get<Character>(gunner).Actions.ViewDelay;
                    double rewind = delay / 4f + Math.Max(0, server.Zone.Tick - answer.Resolution.RequestedTick);
                    TraceHit hit = server.Zone.Damage().Trace(eye, requestView, StationRules.AimDistance, 0, client.EntityId,
                        server.Zone.Tick - rewind, ignoreShip: ship.Id);
                    StationAim aim = StationRules.AimAt(def.Interior!, station, authority.Parts, pose, eye, requestView,
                        hit.Hit ? hit.Distance : StationRules.AimDistance);
                    Vector3 barrel = pose.DirectionToWorld(StationRules.BarrelDirection(def.Interior!, station, authority.Parts));
                    float error = MathF.Acos(Math.Clamp(Vector3.Dot(barrel, aim.Direction), -1, 1)) * 180 / MathF.PI;
                    authorityChecks.Add($"seq{answer.Resolution.Id.Sequence}@{server.Zone.Tick}: {answer.Resolution.Code}, " +
                        $"pitch={state.Pitch:0.0000}, rate={state.PitchRate:0.0000}, request error={error:0.000}deg, " +
                        $"target={hit.Target.Family}/{(hit.Hit ? hit.Distance : StationRules.AimDistance):0.00}m, " +
                        $"current/request point delta={Vector3.Distance(state.Aim.Point, aim.Point):0.000}, " +
                        $"input/request view delta={Vector3.Distance(state.Input.View, requestView):0.000000}, eye={eye}, hull rotation={pose.Rotation}");
                });
                return true;
            });
            RemoteShip remote = client.Ships().All.First(s => s.Id.Value == ship.Id);
            var parts = new float[def.Interior.Parts.Count];
            bool Ready()
            {
                StationState? state = client.Stations().Display(remote, station.Index);
                if (state == null || !state.Loaded(0) || !state.Loaded(1) || state.Limit) return false;
                StationState? mount = (StationState?)typeof(StationModule).GetField("_mount", Private)!.GetValue(client.Stations());
                if (mount == null || mount.Aim.Blocked) return false;
                remote.PartsInto(def.Interior, parts, client.PredictedTick);
                StationRules.Into(mount, parts);
                Quaternion rotation = remote.TryPoseAt(client.PredictedTick, out ShipPose pose) ? pose.Rotation : remote.Rotation;
                // A camera ray and the offset barrels converge; readiness is alignment with the resolved target.
                return StationRules.Misalignment(def.Interior, mount, parts, rotation) <= station.Battery.Mount.Alignment;
            }
            Wait(Ready, $"station becomes loaded and aligned before input; refusal={client.Stations().Refused.Code}");
            Fixtures.Report($"station before the short press: {State()}");
            var shown = new List<StationShotView>();
            var decisions = new List<AttackResolution>();
            client.Stations().Shot += shown.Add;
            client.Attacks.Resolved += result => { if (result.Id.Kind == AttackDomain.Station) decisions.Add(result); };
            InputFrame tap = Input(false);
            tap.Station.FirePressed = true;
            client.Update(0, tap);
            client.Update(0, Input(false));
            Wait(() => shown.Count > 0, "the retained short press presents its shot", 2);
            Assert.Equal(1, shown.Count, $"a press and release before the next input tick still fires once; refusal={client.Stations().Refused.Code}, pending={client.Stations().PendingShots}, predicted={client.Stations().ShotsPredicted}");
            Assert.Equal(1L, client.Stations().ShotsPredicted, "the short press follows local prediction immediately");
            rig.Run(1.2, (_, t) => Input((int)(t * 10) % 2 == 0));
            Wait(() => client.Stations().PendingShots == 0, "all spam attempts receive decisions and accepted shots reach station state");
            TurretFired[] served = rig.Server.Inspect(_ => actual.ToArray());
            Fixtures.Report($"{conditions}: visuals {shown.Count}, authority {served.Length}, confirmed {client.Stations().ShotsConfirmed}, pending {client.Stations().PendingShots}");
            Fixtures.Report("station visual identities/barrels: " + string.Join(", ", shown.Select(s => $"{s.Attack.Sequence}/b{s.Barrel}")));
            Fixtures.Report("station authoritative identities/barrels/ticks: " + string.Join(", ", served.Select(s => $"{s.Attack.Sequence}/b{s.Barrel}@{s.Tick}")));
            Fixtures.Report("station decisions: " + string.Join(", ", decisions.Select(r => $"{r.Id.Sequence}:{r.Code}/b{r.Barrel} requested={r.RequestedTick} start={r.StartTick}")));
            foreach (string check in rig.Server.Inspect(_ => authorityChecks.ToArray())) Fixtures.Report(check);
            Assert.Equal(2, served.Length, "only the two chambered rounds fire on authority");
            Assert.Equal(2, shown.Count, "spam never adds a phantom visual shot");
            Assert.Equal(2, shown.Select(s => s.Attack).Distinct().Count(), "each visual identity is unique");
            foreach (StationShotView predicted in shown)
            {
                TurretFired confirmed = served.Single(s => s.Attack == predicted.Attack);
                Assert.Equal(predicted.Barrel, confirmed.Barrel, "ordered attempt identity retains the correct barrel through loss");
            }
            Assert.Equal(0, client.Stations().PendingShots, "every decision and reservation settles");
            Assert.Equal(0L, client.Stations().ShotsUndone, "valid two-round predictions were not withdrawn");
            Assert.True(!client.Survival().Projectiles.Any(p => p.Own && p.Attack.Kind == AttackDomain.Station && p.Id == 0), "no unnamed phantom rocket remains");
        }
    }
}
