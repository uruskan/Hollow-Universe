using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Net.Transport;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>
/// IW3 (IRON_WAKE.md §6): the Iron Wake's crewed batteries. The mount holds its aim in the world while the ship turns; the
/// siege battery fires a barrel a press, alternating; the loader runs batteries.json's phases and a barrel is loaded at
/// their end; each refusal of §6.7; a rocket strikes a hut with its burst and an incendiary leaves a fire; a defense mount
/// ripples three and changes its clip in the table's time; over a lagged loopback the gunner's predicted shots are the
/// server's; a stored ship keeps its magazine and its loaded barrel.
/// </summary>
public static class BatteryTests
{
    private const float Deg = MathF.PI / 180;

    private static StationState Station(IronWakeRig rig, string id) => rig.Ship.Stations.First(s => s.Def.Id == id);

    // A gunner: aboard at the station's seat's exit, on the ship's list, then into the seat with its verb (man).
    private static Entity Gunner(IronWakeRig rig, string name, string station)
    {
        ShipStationDef def = rig.Def.Stations.First(s => s.Id == station);
        ShipSeatDef seat = rig.Def.Seats[def.Seat];
        Entity e = rig.Aboard(name, rig.Def.Interior!.SocketAt(seat.Exits[0], rig.Ship.Parts), Vector3.UnitZ);
        rig.Step(3);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Ships().Authorise(rig.Game.P(e).Id.Value, rig.Ship).Code, $"{name} on the list");
        Outcome manned = rig.Zone.Interaction().Interact(e, new TargetRef(TargetFamily.Ship, rig.Ship.Id.Value, (byte)def.Part), "man");
        Assert.Equal(OutcomeCode.Done, manned.Code, $"{name} mans {station}");
        return e;
    }

    // Rockets into a magazine, three to a slot after what is there.
    private static void Fill(IronWakeRig rig, StationState st, string item, int count)
    {
        ItemDef def = rig.Game.C.ItemByKey(item);
        for (int slot = 0; slot < st.Magazine.Size && count > 0; slot++)
        {
            if (st.Magazine[slot].IsEmpty)
            {
                int n = Math.Min(count, def.StackSize);
                st.Magazine.Set(slot, ItemStack.Of(def, n));
                count -= n;
            }
        }
    }

    // A direction of the world from the mount's angles (degrees from the bow, + to port; up) at the ship's pose now.
    private static Vector3 Direction(IronWakeRig rig, float yaw, float pitch) =>
        Vector3.Normalize(rig.Pose.DirectionToWorld(new Vector3(MathF.Sin(yaw * Deg) * MathF.Cos(pitch * Deg), MathF.Sin(pitch * Deg),
            MathF.Cos(yaw * Deg) * MathF.Cos(pitch * Deg))));

    private static void Aim(IronWakeRig rig, Entity gunner, Vector3 world, bool fire = false, byte choice = 0, byte cycle = 0) =>
        Assert.True(rig.Zone.Ships().SetStationInput(gunner, new StationInput { View = world, Fire = fire, Choice = choice, Cycle = cycle }), "a gunner's controls");

    // A press of the trigger: held one tick, then let go. The shots it fired.
    private static List<TurretFired> Press(IronWakeRig rig, Entity gunner, Vector3 world)
    {
        var shots = new List<TurretFired>();
        Aim(rig, gunner, world, fire: true);
        rig.Step(1, () => shots.AddRange(rig.Zone.Events.Of<TurretFired>()));
        Aim(rig, gunner, world);
        rig.Step(1, () => shots.AddRange(rig.Zone.Events.Of<TurretFired>()));
        return shots;
    }

    private static int StepUntil(IronWakeRig rig, Func<bool> done, int most, string what)
    {
        for (int i = 0; i <= most; i++)
        {
            if (done())
            {
                return i;
            }
            rig.Step();
        }
        throw new TestFailure($"timed out ({most} ticks) waiting for {what}");
    }

    // The first tick from a cycle's start at which it is <paramref name="seconds"/> in, by the rules' own arithmetic
    // (LoaderState.Elapsed against the table's phase times).
    private static long TickAt(long start, float seconds)
    {
        long t = start;
        while ((float)((t - start) / (double)CharacterMotor.TickRate) < seconds)
        {
            t++;
        }
        return t;
    }

    private static float Misaligned(IronWakeRig rig, StationState st) =>
        StationRules.Misalignment(rig.Def.Interior!, st, rig.Ship.Parts, rig.Ship.State.Rotation) / Deg;

    /// <summary>
    /// IRON_WAKE.md §6.2: at the forward siege battery of a flying Iron Wake the gunner asks for a direction in the world;
    /// the mount slews to it, and while the pilot yaws the ship for five seconds it stays on it within the battery's
    /// alignment (0.5°): the aim is held in the world, not dragged round with the hull.
    /// </summary>
    [Test]
    public static void TheAimIsHeldInTheWorldWhileTheShipYaws()
    {
        using var rig = new IronWakeRig();
        Entity ada = Gunner(rig, "Ada", "Siege_Forward");
        rig.Pilot("Bo");
        StationState st = Station(rig, "Siege_Forward");
        rig.Fly(30 * 4, _ => new ShipInput { Vertical = 1 });
        Vector3 asked = Direction(rig, 20, 6);
        Aim(rig, ada, asked);
        rig.Fly(30 * 3, _ => rig.HoldHeight(rig.Height));
        float settled = Misaligned(rig, st);
        Vector3 nose = rig.Pose.DirectionToWorld(Vector3.UnitZ);
        float worst = 0, mountTurned = 0, yawBefore = st.Yaw;
        rig.Fly(30 * 5, i => new ShipInput { Yaw = 0.7f, Vertical = 0 }, i =>
        {
            if (i >= 10)
            {
                worst = MathF.Max(worst, Misaligned(rig, st));
            }
        });
        mountTurned = (st.Yaw - yawBefore) / Deg;
        float shipTurned = MathF.Acos(Math.Clamp(Vector3.Dot(nose, rig.Pose.DirectionToWorld(Vector3.UnitZ)), -1, 1)) / Deg;
        Fixtures.Report($"settled {settled:0.000}°; the ship yawed {shipTurned:0.0}°, the mount turned {mountTurned:0.0}° against it; " +
            $"the barrels at most {worst:0.000}° off the asked direction while it turned (alignment {st.Def.Battery.Mount.Alignment / Deg:0.0}°)");
        Assert.True(settled <= st.Def.Battery.Mount.Alignment / Deg, "the mount slewed onto the asked direction");
        Assert.True(shipTurned > 10, "the ship yawed");
        Assert.True(MathF.Abs(mountTurned) > 10, "the mount turned against the ship's turn");
        Assert.True(worst <= st.Def.Battery.Mount.Alignment / Deg, "the aim held in the world within the battery's alignment");
    }

    /// <summary>
    /// IRON_WAKE.md §6.3, §6.6: the forward siege battery, its magazine holding six rockets, loads both barrels from an empty
    /// breech; one press fires barrel 0, the next barrel 1. Barrel 0's loader then runs batteries.json's phases: each begins
    /// at the tick its start says, a round leaves the magazine as the table's "waitsBefore" phase begins, and the barrel
    /// counts as loaded at the end of the last phase, not a tick before.
    /// </summary>
    [Test]
    public static void FiringAlternatesAndTheLoaderFollowsTheTable()
    {
        using var rig = new IronWakeRig();
        Entity ada = Gunner(rig, "Ada", "Siege_Forward");
        StationState st = Station(rig, "Siege_Forward");
        BatteryLoader loader = st.Def.Battery.Loader;
        ushort rocket = rig.Game.C.ItemByKey("rocket").Id;
        Fill(rig, st, "rocket", 6);
        Vector3 ahead = Direction(rig, 0, 10);
        Aim(rig, ada, ahead);
        int loading = StepUntil(rig, () => st.Loaded(0) && st.Loaded(1), 30 * 8, "both barrels loaded");
        Assert.Equal(4, st.Count(rocket), "a round each taken from the magazine");
        List<TurretFired> first = Press(rig, ada, ahead);
        rig.Step(10);
        List<TurretFired> second = Press(rig, ada, ahead);
        Assert.True(first.Count == 1 && second.Count == 1, "one barrel a press");
        Assert.Equal(0, first[0].Barrel, "the first press fires barrel 0");
        Assert.Equal(1, second[0].Barrel, "the next, barrel 1");
        Assert.True(first[0].Number + 1 == second[0].Number && first[0].Kind == rocket, "numbered shots of rockets");

        // Barrel 0's cycle against the table, from its shot's tick.
        long shot = first[0].Tick;
        var began = new Dictionary<int, long>();
        long took = -1, loadedAt = -1;
        // Already stepped 12 ticks since the shot: its phases so far.
        for (long t = shot; t <= rig.Zone.Tick; t++)
        {
            int p = loader.PhaseAt((t - shot) / (float)CharacterMotor.TickRate);
            began.TryAdd(p, t);
        }
        while (loadedAt < 0 && rig.Zone.Tick < shot + 30 * 8)
        {
            int count = st.Count(rocket);
            rig.Step();
            LoaderState l = st.Loaders[0];
            if (l.Active)
            {
                began.TryAdd(loader.PhaseAt(l.Elapsed(rig.Zone.Tick)), rig.Zone.Tick);
            }
            if (took < 0 && st.Count(rocket) < count)
            {
                took = rig.Zone.Tick;
            }
            if (st.Loaded(0))
            {
                loadedAt = rig.Zone.Tick;
            }
        }
        long Expected(float seconds) => TickAt(shot, seconds);
        var lines = loader.Phases.Select(p => $"{p.Name} {began.GetValueOrDefault(p.Index, -1) - shot}/{Expected(p.Start) - shot}");
        Fixtures.Report($"loaded both from empty in {loading / 30.0:0.00} s; barrel 0's phases began at ticks (seen/table) {string.Join(", ", lines)}; " +
            $"its round taken at {took - shot} (table {Expected(loader.TakeAt) - shot}), loaded at {loadedAt - shot} (table {Expected(loader.Seconds) - shot})");
        foreach (LoaderPhase p in loader.Phases)
        {
            Assert.Equal(Expected(p.Start), began.GetValueOrDefault(p.Index, -1), $"{p.Name} begins where the table says");
        }
        Assert.Equal(Expected(loader.TakeAt), took, "the round leaves the magazine as the table's phase begins");
        Assert.Equal(Expected(loader.Seconds), loadedAt, "loaded at the end of the last phase");
        Assert.Equal(2, st.Count(rocket), "a round for each barrel's cycle");
    }

    /// <summary>
    /// IRON_WAKE.md §6.6, §6.7: with nothing in the magazine the loaders open their breeches and wait before "fetch", and a
    /// press is refused EMPTY; with the station disabled too, DISABLED comes first.
    /// </summary>
    [Test]
    public static void EmptyWithAnEmptyMagazine()
    {
        using var rig = new IronWakeRig();
        Entity ada = Gunner(rig, "Ada", "Siege_Forward");
        StationState st = Station(rig, "Siege_Forward");
        BatteryLoader loader = st.Def.Battery.Loader;
        Vector3 ahead = Direction(rig, 0, 10);
        Aim(rig, ada, ahead);
        rig.Step(30 * 3);
        Assert.True(st.Loaders.All(l => l.Active && l.Waiting && MathF.Abs(l.At - loader.TakeAt) < 1e-5f),
            $"both loaders wait with the breech open before {loader.Phases[loader.WaitsBefore].Name}");
        Assert.True(Press(rig, ada, ahead).Count == 0 && st.Refused == OutcomeCode.Empty, $"EMPTY ({st.Refused})");
        rig.Ship.PartHealth[st.Def.Part] = 0;
        Press(rig, ada, ahead);
        Assert.Equal(OutcomeCode.Disabled, st.Refused, "DISABLED before EMPTY");
        rig.Ship.PartHealth[st.Def.Part] = st.Def.Battery.Mount.Health;
        Fill(rig, st, "rocket", 1);
        rig.Step(2);
        Assert.True(st.Loaders.Count(l => l.Waiting) == 1 && st.Loaders.Count(l => l.Active && !l.Waiting && l.Kind == rig.Game.C.ItemByKey("rocket").Id) == 1,
            "a round in the magazine: one loader takes it and goes on, the other waits");
    }

    /// <summary>
    /// IRON_WAKE.md §6.7, in its order: LOADING (a loader at work, its progress), LIMIT (astern, past the arcs), TRAVERSING
    /// (a loaded barrel not on the asked direction yet), and HULL BLOCKED: the dorsal battery laid forward and down across
    /// the bridge, its barrel's line meeting the ship.
    /// </summary>
    [Test]
    public static void EachRefusal()
    {
        using var rig = new IronWakeRig();
        Entity ada = Gunner(rig, "Ada", "Siege_Forward");
        StationState st = Station(rig, "Siege_Forward");
        Fill(rig, st, "rocket", 6);
        Vector3 ahead = Direction(rig, 0, 10);
        Aim(rig, ada, ahead);
        rig.Step(20);
        Press(rig, ada, ahead);
        Assert.Equal(OutcomeCode.Loading, st.Refused, "LOADING while the loader works from an empty breech");
        int progress = st.RefusedDetail;
        StepUntil(rig, () => st.Loaded(0) && st.Loaded(1), 30 * 8, "both loaded");
        Press(rig, ada, Direction(rig, 180, 10));
        rig.Step(30 * 2);
        Press(rig, ada, Direction(rig, 180, 10));
        Assert.Equal(OutcomeCode.Limit, st.Refused, "LIMIT astern");
        Aim(rig, ada, ahead);
        rig.Step(30 * 12);
        Vector3 aside = Direction(rig, 70, 10);
        Press(rig, ada, aside);
        Assert.Equal(OutcomeCode.Traversing, st.Refused, "TRAVERSING the moment the aim swings 70°");

        // The dorsal battery laid forward and down: the first such line that meets the ship.
        Entity bo = Gunner(rig, "Bo", "Siege_Dorsal");
        StationState dorsal = Station(rig, "Siege_Dorsal");
        Fill(rig, dorsal, "rocket", 6);
        StepUntil(rig, () => dorsal.Loaded(0) && dorsal.Loaded(1), 30 * 8, "the dorsal battery loaded");
        float pitch = dorsal.Def.Battery.Mount.PitchMin / Deg + 0.1f;
        (float yaw, float down) found = (float.NaN, 0);
        foreach (float yaw in new[] { 0f, 5f, -5f, 10f, -10f, 20f, -20f, 30f, -30f })
        {
            foreach (float p in new[] { pitch, pitch + 3, pitch + 6 })
            {
                dorsal.Yaw = yaw * Deg;
                dorsal.Pitch = p * Deg;
                dorsal.HasTarget = false;
                rig.Zone.Ships().MoveParts(rig.Ship, new[] { dorsal.Def.YawPart, dorsal.Def.PitchPart }, new[] { dorsal.Yaw, dorsal.Pitch });
                if (float.IsNaN(found.yaw) && rig.Zone.Ships().HullBlocked(rig.Ship, dorsal, 0))
                {
                    found = (yaw, p);
                }
            }
        }
        Assert.True(!float.IsNaN(found.yaw), "a line forward and down from the dorsal battery meets the ship");
        dorsal.Yaw = found.yaw * Deg;
        dorsal.Pitch = found.down * Deg;
        dorsal.HasTarget = false;
        rig.Zone.Ships().MoveParts(rig.Ship, new[] { dorsal.Def.YawPart, dorsal.Def.PitchPart }, new[] { dorsal.Yaw, dorsal.Pitch });
        Vector3 across = Direction(rig, found.yaw, found.down);
        Aim(rig, bo, across);
        rig.Step(30);
        Press(rig, bo, across);
        Fixtures.Report($"LOADING at {progress}%; the dorsal battery's line meets the ship laid {found.yaw:0}° and {found.down:0.0}°: {dorsal.Refused}");
        Assert.Equal(OutcomeCode.HullBlocked, dorsal.Refused, "HULL BLOCKED across the bridge");
    }

    // A hut ahead of the forward battery, its wall facing the ship, and the line from the station's muzzle to that wall's middle.
    private static (TestBases.Hut Hut, Piece Wall, Vector3 Target) HutAhead(IronWakeRig rig, float metres)
    {
        Vector3 at = rig.Zone.Settle(rig.Pose.ToWorld(new Vector3(0, 0, 23 + metres)));
        rig.Game.Clear(rig.Pose.ToWorld(new Vector3(0, 0, 30)), at, 6);
        rig.Game.Clear(at, at, 8);
        TestBases.Hut hut = TestBases.Build(rig.Zone, at, 0, grade: 1, owner: 77, door: null) ?? throw new TestFailure("no hut");
        Vector3 toShip = Vector3.Normalize(rig.Pose.Position - at);
        Piece wall = hut.Walls.Skip(1).OrderByDescending(w => Vector3.Dot(hut.Face(w).Normal, toShip)).First();
        return (hut, wall, hut.Face(wall, height: 1.6).Point);
    }

    // The asked direction from a barrel's muzzle that puts the round's flight through a target: the straight line, then the
    // pitch raised until the motor's flight (RocketFlight, as the server flies it) passes nearest it.
    private static Vector3 Lay(IronWakeRig rig, StationState st, int barrel, string round, Vector3 target)
    {
        ShipInterior interior = rig.Def.Interior!;
        (Vector3 muzzleLocal, _) = StationRules.BarrelLine(interior, st.Def, barrel, rig.Ship.Parts);
        Vector3 muzzle = rig.Pose.ToWorld(muzzleLocal);
        LaunchProfile profile = st.Def.Battery.Launch.Of(rig.Game.C.ItemByKey(round).Id)!;
        RocketMotor motor = RocketMotor.Of(st.Def.Battery.Launch, profile);
        float g = (float)(rig.Zone.Spec.SurfaceGravity * 9.80665);
        Vector3 straight = Vector3.Normalize(target - muzzle), up = rig.Zone.Frame.UpAt(muzzle);
        Vector3 side = Vector3.Normalize(Vector3.Cross(straight, up));
        Vector3 best = straight;
        float nearest = float.MaxValue;
        for (float raise = 0; raise < 6; raise += 0.02f)
        {
            Vector3 dir = Vector3.Normalize(Vector3.Transform(straight, Quaternion.CreateFromAxisAngle(side, -raise * Deg)));
            Vector3 p = muzzle, v = dir * profile.Muzzle;
            float flown = 0, burnt = 0, closest = float.MaxValue;
            for (int i = 0; i < 2 * 30 * 6; i++)
            {
                (Vector3 q, Vector3 w, burnt) = RocketFlight.Step(p, v, -rig.Zone.Frame.UpAt(p), g, motor, flown, burnt, RocketFlight.StepSeconds);
                flown += (q - p).Length();
                (p, v) = (q, w);
                closest = MathF.Min(closest, Vector3.Distance(p, target));
                if (Vector3.Dot(p - target, straight) > 1)
                {
                    break;
                }
            }
            if (closest < nearest)
            {
                (nearest, best) = (closest, dir);
            }
        }
        return best;
    }

    /// <summary>
    /// IRON_WAKE.md §6.3, §7: from the parked Iron Wake's forward battery a rocket strikes a wooden hut 110 m ahead with its
    /// burst (the wall it meets loses health, the blast is the rocket's own); then the gunner chooses incendiary rockets,
    /// the other barrel's rocket is drawn back into the magazine by the mechanism in reverse, and the incendiary that loads
    /// leaves a fire where it strikes.
    /// </summary>
    [Test]
    public static void ARocketStrikesAHutAndAnIncendiaryLeavesAFire()
    {
        using var rig = new IronWakeRig();
        (TestBases.Hut hut, Piece wall, Vector3 target) = HutAhead(rig, 110);
        Entity ada = Gunner(rig, "Ada", "Siege_Forward");
        StationState st = Station(rig, "Siege_Forward");
        GameContent c = rig.Game.C;
        Fill(rig, st, "rocket", 3);
        Fill(rig, st, "rocket-incendiary", 3);
        Vector3 laid = Lay(rig, st, 0, "rocket", target);
        Aim(rig, ada, laid);
        StepUntil(rig, () => st.Loaded(0) && st.Loaded(1), 30 * 8, "both loaded");
        rig.Step(30 * 3);
        double before = wall.Health;
        var ended = new List<ProjectileEnded>();
        var blasts = new List<Detonated>();
        void Watch()
        {
            ended.AddRange(rig.Zone.Events.Of<ProjectileEnded>());
            blasts.AddRange(rig.Zone.Events.Of<Detonated>());
        }
        Aim(rig, ada, laid, fire: true);
        rig.Step(1, Watch);
        // At once: the incendiary chosen and the cycle tapped (the other barrel's rocket goes back).
        Aim(rig, ada, laid, choice: (byte)(st.Def.Battery.AmmoIndex(c.ItemByKey("rocket-incendiary").Id) + 1), cycle: 1);
        rig.Step(30 * 4, Watch);
        ProjectileEnded strike = ended.FirstOrDefault(e => e.Target.Family == TargetFamily.Piece);
        Fixtures.Report($"the rocket ended on {string.Join(", ", ended.Select(e => e.Target.Family))}; the wall {before} → {wall.Health}; " +
            $"blasts {blasts.Count}; the magazine: {st.Count(c.ItemByKey("rocket").Id)} rockets, {st.Count(c.ItemByKey("rocket-incendiary").Id)} incendiary");
        Assert.True(strike.Target.Family == TargetFamily.Piece, "the rocket struck the hut");
        Assert.True(blasts.Count >= 1 && wall.Health < before, "with its burst");
        Assert.Equal(2, st.Count(c.ItemByKey("rocket").Id), "barrel 1's rocket drawn back into the magazine (one was left there)");
        StepUntil(rig, () => st.Loaded(0) && st.Chambers[0] == c.ItemByKey("rocket-incendiary").Id, 30 * 8, "an incendiary loaded");
        rig.Step(30 * 2);
        int firesBefore = rig.Zone.Fires().All.Count;
        Aim(rig, ada, Lay(rig, st, 0, "rocket-incendiary", target), fire: true);
        rig.Step(1, Watch);
        Aim(rig, ada, laid);
        rig.Step(30 * 4, Watch);
        Fixtures.Report($"fires burning: {rig.Zone.Fires().All.Count} (before {firesBefore})");
        Assert.True(rig.Zone.Fires().All.Count > firesBefore, "the incendiary left a fire");
        Assert.True(hut.Walls.Length > 0, "the hut");
    }

    /// <summary>
    /// IRON_WAKE.md §6.3, §6.6: a defense mount, six rockets in its magazine, loads a clip of three; with the trigger held it
    /// ripples them a quarter second apart, then changes its clip: loaded again 2.52 s after the third shot, three more
    /// rockets out of the magazine.
    /// </summary>
    [Test]
    public static void ADefenseMountRipplesThreeThenChangesItsClip()
    {
        using var rig = new IronWakeRig();
        Entity ada = Gunner(rig, "Ada", "Defense_F_P");
        StationState st = Station(rig, "Defense_F_P");
        BatteryDef battery = st.Def.Battery;
        ushort rocket = rig.Game.C.ItemByKey("rocket").Id;
        Fill(rig, st, "rocket", 6);
        float outboard = st.Def.Side < 0 ? -90 : 90;
        Vector3 aim = Direction(rig, outboard, 12);
        Aim(rig, ada, aim);
        StepUntil(rig, () => Enumerable.Range(0, 3).All(st.Loaded), 30 * 6, "the clip loaded");
        Assert.Equal(3, st.Count(rocket), "a clip of three from the magazine");
        rig.Step(30 * 2);
        var shots = new List<TurretFired>();
        long loadedAt = -1;
        Aim(rig, ada, aim, fire: true);
        for (int i = 0; i < 30 * 4 && loadedAt < 0; i++)
        {
            rig.Step(1, () => shots.AddRange(rig.Zone.Events.Of<TurretFired>()));
            if (shots.Count == 3)
            {
                Aim(rig, ada, aim); // let go once the clip is spent
            }
            if (shots.Count == 3 && Enumerable.Range(0, 3).All(st.Loaded))
            {
                loadedAt = rig.Zone.Tick;
            }
        }
        Aim(rig, ada, aim);
        long[] ticks = shots.Select(s => s.Tick).ToArray();
        long clip = TickAt(0, battery.Loader.Seconds);
        Fixtures.Report($"shots at ticks {string.Join(", ", ticks.Select(t => t - ticks[0]))} (barrels {string.Join(", ", shots.Select(s => s.Barrel))}); " +
            $"loaded again {loadedAt - ticks[^1]} ticks after the third (table {clip}); {st.Count(rocket)} left");
        Assert.Equal(3, shots.Count, "three rippled while held");
        Assert.True(shots.Select(s => s.Barrel).SequenceEqual(battery.Order.Take(3)), "in the ripple's order");
        Assert.True(ticks[1] - ticks[0] == battery.IntervalTicks && ticks[2] - ticks[1] == battery.IntervalTicks, "a quarter second apart");
        Assert.Equal(clip, loadedAt - ticks[2], "the clip changed in the table's time");
        Assert.Equal(0, st.Count(rocket), "three more out of the magazine");
    }

    /// <summary>
    /// IRON_WAKE.md §6.9: over a loopback with 120 ms of latency each way, a gunner at the forward battery presses six times
    /// while loaded; every shot their client predicted is the server's, matched by its number: none doubled, none lost,
    /// none taken back, none the server fired unpredicted.
    /// </summary>
    [Test]
    public static void PredictedShotsMatchTheServersOverALaggedLoopback()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada", new NetConditions { LatencyMs = 120 });
        rig.WaitUntilPlaying();
        IronWakeNetTests.ShipFacts ship = IronWakeNetTests.Spawn(rig, "Ada");
        ShipDef def = ContentFixture.Content.Ship("iron-wake")!;
        ShipStationDef station = def.Stations.First(s => s.Id == "Siege_Forward");
        IronWakeNetTests.Heal(rig, ada.EntityId);
        IronWakeNetTests.PutAboard(rig, ada.EntityId, ship.Id, def.Interior!.SocketAt(def.Seats[station.Seat].Exits[0], def.Interior.Rest));
        rig.Run(1.0, (_, _) => default);
        ada.Local.Interact(new TargetRef(TargetFamily.Ship, ship.Id, (byte)station.Part), "man");
        rig.Run(1.0, (_, _) => default);
        Assert.True(ada.Stations().AtStation, "Ada at the forward battery, as her client has it");
        rig.Server.Inspect(sv =>
        {
            Ship s = sv.Zone.Ships().All.First(x => x.Id.Value == ship.Id);
            StationState st = s.Stations[station.Index];
            ItemDef rocket = sv.Zone.Content!.ItemByKey("rocket");
            for (int slot = 0; slot < 3; slot++)
            {
                st.Magazine.Set(slot, ItemStack.Of(rocket, 3));
            }
            return true;
        });
        Vector3 ahead = Vector3.Normalize(ship.Pose.DirectionToWorld(new Vector3(0, MathF.Sin(10 * Deg), MathF.Cos(10 * Deg))));
        InputFrame Gunner(GameClient c, bool fire) => new() { View = ahead, ViewFrame = 0, Station = new StationInput { Fire = fire } };
        rig.Run(8.0, (c, _) => Gunner(c, false));
        double[] presses = { 0.5, 1.5, 6.6, 7.6, 12.6, 13.6 };
        rig.Run(16.0, (c, t) => Gunner(c, presses.Any(p => t >= p && t < p + 0.25)));
        rig.Run(1.5, (c, _) => Gunner(c, false));
        uint served = rig.Server.Inspect(sv => sv.Zone.Ships().All.First(x => x.Id.Value == ship.Id).Stations[station.Index].Shots);
        StationModule m = ada.Stations();
        Fixtures.Report($"the server fired {served}; Ada's client predicted {m.ShotsPredicted}, confirmed {m.ShotsConfirmed}, took back {m.ShotsUndone}, " +
            $"saw unpredicted {m.ShotsUnpredicted}, waiting {m.PendingShots}");
        Assert.Equal(6u, served, "six presses, six shots");
        Assert.Equal((long)served, m.ShotsPredicted, "each predicted once (none doubled)");
        Assert.Equal((long)served, m.ShotsConfirmed, "each matched by its number (none lost)");
        Assert.True(m.ShotsUndone == 0 && m.ShotsUnpredicted == 0 && m.PendingShots == 0, "none taken back, none unpredicted");
    }

    /// <summary>
    /// IRON_WAKE.md §6.10, §12: a stored Iron Wake (ShipCodec 3) keeps a half-full magazine, the chosen kind, a loaded
    /// barrel and the other barrel's loader mid-cycle, and the aim; added back to the zone, the cycle goes on from where it
    /// was and loads that barrel.
    /// </summary>
    [Test]
    public static void ARestartKeepsAHalfFullMagazineAndALoadedBarrel()
    {
        using var rig = new IronWakeRig();
        Entity ada = Gunner(rig, "Ada", "Siege_Forward");
        StationState st = Station(rig, "Siege_Forward");
        GameContent c = rig.Game.C;
        Fill(rig, st, "rocket-hv", 18);
        Vector3 ahead = Direction(rig, 15, 8);
        Aim(rig, ada, ahead, choice: (byte)(st.Def.Battery.AmmoIndex(c.ItemByKey("rocket-hv").Id) + 1));
        StepUntil(rig, () => st.Loaded(0) && st.Loaded(1), 30 * 8, "both loaded");
        rig.Step(30 * 3);
        Press(rig, ada, ahead);
        rig.Step(45);
        LoaderState cycling = st.Loaders[0];
        byte[] data = ShipCodec.Encode(rig.Ship, rig.Zone.Frame, rig.Zone.Tick);
        StoredShip back = ShipCodec.Decode(rig.Ship.Id, data, c, rig.Zone.Frame)!;
        StationState kept = back.Ship.Stations[st.Def.Index];
        Fixtures.Report($"stored: chosen {kept.ChosenItem.Key}, magazine {kept.Count(c.ItemByKey("rocket-hv").Id)}, chambers {string.Join(", ", kept.Chambers)}, " +
            $"loader 0 {kept.Loaders[0]} (was {cycling}), aim {kept.Yaw / Deg:0.0}°, {kept.Pitch / Deg:0.0}°; {data.Length} bytes");
        Assert.Equal(st.Count(c.ItemByKey("rocket-hv").Id), kept.Count(c.ItemByKey("rocket-hv").Id), "the half-full magazine");
        Assert.True(kept.Count(c.ItemByKey("rocket-hv").Id) is >= 15 and <= 16 && kept.Loaders[0].Kind == st.Loaders[0].Kind, "rounds taken, the carried one kept");
        Assert.Equal(c.ItemByKey("rocket-hv").Id, kept.ChosenItem.Id, "the chosen kind");
        Assert.True(kept.Chambers[1] == c.ItemByKey("rocket-hv").Id && kept.Chambers[0] == 0, "barrel 1 loaded, barrel 0 fired");
        Assert.True(kept.Loaders[0].Active && MathF.Abs(kept.Loaders[0].At - cycling.Elapsed(rig.Zone.Tick)) < 1e-4f, "barrel 0's cycle where it was");
        Assert.True(MathF.Abs(kept.Yaw - st.Yaw) < 1e-6f && MathF.Abs(kept.Pitch - st.Pitch) < 1e-6f, "the aim");

        // Back in the zone (the server after a restart): the cycle goes on and loads barrel 0.
        rig.Zone.Ships().Release(ada, SeatRelease.Moved);
        rig.Zone.Ships().RemoveShip(rig.Ship);
        Ship restored = rig.Zone.Ships().AddShip(back.Ship);
        StationState again = restored.Stations[st.Def.Index];
        float left = st.Def.Battery.Loader.Seconds - again.Loaders[0].At;
        int took = StepUntil(rig, () => again.Loaded(0), 30 * 8, "barrel 0 loaded after the restart");
        Fixtures.Report($"after the restart barrel 0 loaded {took / 30.0:0.00} s on ({left:0.00} s were left)");
        Assert.True(MathF.Abs(took / 30f - left) < 0.1f, "its cycle went on from where it was stored");
    }
}
