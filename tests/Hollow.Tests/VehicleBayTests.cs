using System;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Ships.Modules;
using Hollow.Simulation.World;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>
/// VEHICLES.md §9.11, from the owner's question of 21 October 2026 ("how do I change a car's engine or tyres?"):
/// <list type="bullet">
/// <item>running gear as modules: the stock tyres, the monster tyres, and the lift kit that raises the body;</item>
/// <item>the Monster TOGG as a TOGG with monster parts;</item>
/// <item>the vehicle workbench's bay as the place a build changes: a module taken out into the inventory, or one fitted from it,
/// every rule the server's (the vehicle in the bay, still, empty, its crew's; the player near);</item>
/// <item>the build saved and restored (an older save's vehicle on wheels dropped), and over the network.</item>
/// </list>
/// </summary>
public static class VehicleBayTests
{
    private static GameContent C => GroundMotorTests.Bare.Value;

    private static ushort Id(string item) => C.ItemByKey(item).Id;

    private static int Find(Container c, ushort item)
    {
        for (int i = 0; i < c.Size; i++)
        {
            if (c[i].Item == item)
            {
                return i;
            }
        }
        return -1;
    }

    private static int Count(Container c, ushort item)
    {
        int n = 0;
        for (int i = 0; i < c.Size; i++)
        {
            n += c[i].Item == item ? c[i].Count : 0;
        }
        return n;
    }

    /// <summary>
    /// A zone with Ada beside a vehicle workbench's bay and her stock TOGG standing in it, at rest: its centre of mass set down
    /// <paramref name="off"/> from the bay's middle (across, along it), its nose <paramref name="turned"/> from the bench's yaw.
    /// </summary>
    private sealed class Bay : IDisposable
    {
        public readonly Zone Zone;
        public readonly Entity Ada;
        public readonly Player Owner;
        public readonly Deployable Bench;
        public readonly Ship Car;

        public Bay(Vector2 off = default, float turned = 0)
        {
            var spec = new ZoneSpec
            {
                Radius = ShipRig.Earthlike, SurfaceGravity = 0.92, LatitudeDegrees = 10, LongitudeDegrees = 20,
                AirDensity = ZoneSpec.Air(2.23, 291, 0.92).Density, AirScaleHeight = ZoneSpec.Air(2.23, 291, 0.92).ScaleHeight,
            };
            Zone = new Zone(new LevelSurface(spec.Radius), spec, content: C) { UnixMilliseconds = 1_800_000_000_000 };
            Vector3 ground = Zone.GroundAt(0, 0);
            Ada = Zone.Players().AddPlayer(Zone.NextId(), "Ada", Zone.NewCharacter(ground + Zone.Frame.UpAt(ground) * 0.5f));
            Owner = Zone.Players().PlayerOf(Ada)!;
            Bench = Zone.Players().SpawnDeployable(C.Deployable("vehicle-workbench")!, ground, 0, Owner.Id);
            (Vector3 right, _) = BayStats.Axes(Bench.Up, Bench.Yaw);
            BayStats bay = Bench.Def.Bay!;
            Vector3 centre = bay.Centre(Bench.Position, Bench.Up, Bench.Yaw);
            // Ada beside the bay, on the side away from any offset; the TOGG driven in, its centre of mass over the bay's middle.
            float side = off.X > 0 ? -1 : 1;
            Zone.Entities.Get<Character>(Ada).State.Position = centre + right * side * (bay.Clear.X + 1.2f) + Bench.Up * 0.2f;
            ShipDef togg = C.Ship("togg")!;
            (_, Vector3 outward) = BayStats.Axes(Bench.Up, Bench.Yaw);
            (Vector3 nose, _) = CharacterMotor.HorizontalBasis(Bench.Yaw + turned, Bench.Up);
            Vector3 com = Zone.Settle(centre + right * off.X + outward * off.Y);
            Car = Zone.Ships().SpawnShip(togg, com - nose * C.ShipMade(togg.Item)!.CentreOfMass.Z, Bench.Yaw + turned, Owner.Id, 100);
            Settle();
        }

        /// <summary>The bay's middle on the ground.</summary>
        public Vector3 Centre => Bench.Def.Bay!.Centre(Bench.Position, Bench.Up, Bench.Yaw);

        public void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Zone.UnixMilliseconds += 1000 / CharacterMotor.TickRate;
                Zone.Step();
            }
        }

        public void Settle(int most = 400)
        {
            for (int i = 0; i < most && !Car.State.Asleep; i++)
            {
                Step(1);
            }
        }

        public Outcome Take(string socket, int level = 1) =>
            Zone.Ships().TakeModule(Ada, Bench.Id, Car.Id, Socket(socket), level);

        public Outcome Fit(string item, string socket, int level = 1)
        {
            int slot = Find(Owner.Inventory.Main, Id(item));
            if (slot < 0)
            {
                Owner.Inventory.Give(C, C.ItemByKey(item), 1);
                slot = Find(Owner.Inventory.Main, Id(item));
            }
            return Zone.Ships().FitModule(Ada, Bench.Id, Car.Id, Socket(socket), level, ContainerRef.Main, slot);
        }

        public int Socket(string key) => Car.Def.Kind.Sockets.Single(s => s.Key == key).Index;

        public int Carried(string item) => Count(Owner.Inventory.Main, Id(item)) + Count(Owner.Inventory.Belt, Id(item));

        public void Dispose() => Zone.Dispose();
    }

    /// <summary>
    /// The Monster TOGG is now the TOGG with monster tyres, the lift kit and the blown six (ships.json presets), and it is the
    /// car it was: the old kind's mass, centre of mass, inertia, wheels, ground numbers, tank, lamps and frame. The stock TOGG
    /// is too, its tyres a module now.
    /// </summary>
    [Test]
    public static void TheMonsterIsAToggWithMonsterParts()
    {
        ShipDef togg = C.ShipMade(C.ItemByKey("togg"))!, monster = C.Ship("monster-togg")!;
        Assert.True(togg.Kind.Key == "togg" && togg.Build.Count == 3 && togg.Raise == 0 && togg.Model == "togg-frame", "the TOGG: cab, engine, stock tyres");
        Assert.Near(1150, togg.Mass, 0.5, "the TOGG's mass as before");
        Assert.Near(1.1004, togg.CentreOfMass.Y, 0.002, "its centre of mass's height as before");
        Assert.Near(2.1643, togg.CentreOfMass.Z, 0.002, "and along");
        Assert.Near(3200, togg.Inertia.X, 3, "its pitch inertia as before");
        Assert.Near(748.5, togg.Inertia.Z, 3, "its roll inertia as before");
        Assert.True(togg.Wheels.Count == 4 && togg.Wheels.All(w => w.Radius == 0.48f) && togg.Ground!.TopSpeed == 20, "four 0.96 m tyres, 20 m/s");

        Assert.True(ReferenceEquals(monster.Kind, togg.Kind), "the Monster TOGG is a TOGG");
        Assert.True(new[] { "togg-cab-one", "monster-togg-engine", "togg-lift-kit", "monster-tyres" }
            .All(k => monster.Build.Entries.Any(e => e.Module == Id(k))), "with its cab, the blown six, the lift kit and the monster tyres");
        Assert.True(monster.Model == "monster-togg-frame" && monster.Sound == "monster-togg" && monster.Raise == 0.75f,
            "drawn as the monster's frame, sounding like it, its body 0.75 m up");
        Assert.Near(1920, monster.Mass, 0.5, "the monster's mass as before");
        Assert.Near(1.5227, monster.CentreOfMass.Y, 0.002, "its centre of mass's height as before");
        Assert.Near(2.0969, monster.CentreOfMass.Z, 0.002, "and along");
        Assert.Near(6469, monster.Inertia.X, 5, "its pitch inertia as before");
        Assert.Near(6387, monster.Inertia.Y, 5, "its yaw inertia as before");
        Assert.Near(2231, monster.Inertia.Z, 5, "its roll inertia as before");
        GroundDef g = monster.Ground!;
        Assert.True(g.TopSpeed == 34 && g.ReverseSpeed == 9 && g.Traction == 30000 && g.Brake == 34000 && g.Righting == 40 && g.RightingDamping == 6 &&
            g.GripHeight == 0.7f && g.Rolling == 0.02f && MathF.Abs(g.Steer - 30 * MathF.PI / 180) < 1e-5f, "the monster's ground numbers");
        Assert.True(monster.Wheels.Count == 4 && monster.Wheels.All(w => w.Radius == 0.85f && w.Travel == 0.6f && MathF.Abs(MathF.Abs(w.Hub.X) - 1.42f) < 1e-5f),
            "four 1.7 m tyres on a 2.84 m track");
        Assert.True(monster.FuelCapacity == 160 && monster.FuelFull == 0.28f && monster.DragForward == 1.6f && monster.CrashSafeSpeed == 13,
            "its tank, thirst, drag and crash");
        Assert.True(monster.BoostPower > monster.EnginePower, "the blown six still overboosts (VEHICLES.md §9.10)");
        Assert.Near(2.05, monster.Lights.Single(l => l.Key == "head-left").At.Y, 1e-4, "its headlamps up with the body");
        Assert.Near(1.73, monster.Placed.Single(p => p.Module.Key == "togg-cab-one").Origin.Y, 1e-4, "its cab on the raised deck");
        Assert.Near(0, monster.Placed.Single(p => p.Module.Key == "monster-tyres").Origin.Y, 1e-4, "its tyres on the ground");
        Assert.True(monster.Helm.Key == "bay3.1.driver", "driven from its cab");
    }

    /// <summary>The rules between modules: the monster tyres need the lift kit, the stock tyres and the kit don't go together.</summary>
    [Test]
    public static void RunningGearKeepsItsRules()
    {
        ShipDef togg = C.Ship("togg")!.Kind;
        int wheels = togg.Sockets.Single(s => s.Key == "wheels").Index, lift = togg.Sockets.Single(s => s.Key == "suspension").Index;
        var monsterOnly = new ShipBuild(new[] { new Installed((byte)wheels, 1, Id("monster-tyres")) });
        Assert.Equal(BuildProblem.Needs, ShipAssembler.Check(togg, monsterOnly, C.Module, out ModuleDef? needing, out _), "monster tyres alone");
        Assert.True(needing?.Key == "monster-tyres", "the monster tyres need something");
        var clash = new ShipBuild(new[] { new Installed((byte)wheels, 1, Id("togg-tyres")), new Installed((byte)lift, 1, Id("togg-lift-kit")) });
        Assert.Equal(BuildProblem.Excludes, ShipAssembler.Check(togg, clash, C.Module), "stock tyres on the lift kit");
        Assert.Equal(BuildProblem.None, ShipAssembler.Check(togg, new ShipBuild(new[] { new Installed((byte)lift, 1, Id("togg-lift-kit")) }), C.Module),
            "the kit alone, in the bay");
        Assert.True(ShipAssembler.Sane(togg, monsterOnly.Entries, C.Module).IsEmpty, "a save's monster tyres without the kit are left out");
        Assert.True(ShipAssembler.Sane(togg, clash.Entries, C.Module).Count == 1, "of two that clash, one is kept");
        Assert.Equal(BuildProblem.DoesNotFit,
            ShipAssembler.Check(togg, new ShipBuild(new[] { new Installed((byte)wheels, 1, Id("togg-engine")) }), C.Module), "an engine isn't tyres");
    }

    /// <summary>
    /// A stock TOGG made a monster in the vehicle workbench's bay: its stock tyres and engine out into Ada's inventory, the
    /// monster tyres refused until the lift kit is on, then the kit, the tyres and the blown six fitted. It is then the preset's
    /// very layout and stands on its new wheels where it stood; the stock tyres are refused while the kit is on.
    /// </summary>
    [Test]
    public static void TheBayMakesAToggAMonster()
    {
        using var bay = new Bay();
        Ship car = bay.Car;
        Assert.True(car.State.Asleep && ShipSystem.IsInBay(bay.Bench, car.State.Position) && bay.Zone.Ships().InBay(bay.Bench) == car,
            "the TOGG rests in the bay");
        Vector3 origin = car.Origin;
        Assert.Equal(OutcomeCode.Done, bay.Take("wheels").Code, "the stock tyres off");
        Assert.True(bay.Carried("togg-tyres") == 1 && car.Def.Wheels.Count == 0, "into Ada's inventory: no wheels on it");
        Vector3 held = car.State.Position;
        bay.Step(30);
        Assert.True(Vector3.Distance(held, car.State.Position) < 1e-4f, "the bay holds it up without its tyres");
        Outcome early = bay.Fit("monster-tyres", "wheels");
        Assert.Equal(OutcomeCode.FitFirst, early.Code, "the monster tyres want the lift kit first");
        Assert.Equal((int)Id("togg-lift-kit"), early.Detail, "and say so");
        Assert.Equal(OutcomeCode.Done, bay.Fit("togg-lift-kit", "suspension").Code, "the lift kit on");
        Assert.Equal(OutcomeCode.Done, bay.Fit("monster-tyres", "wheels").Code, "the monster tyres on");
        Assert.Equal(OutcomeCode.Done, bay.Take("nose").Code, "the TOGG engine out");
        Assert.Equal(OutcomeCode.Done, bay.Fit("monster-togg-engine", "nose").Code, "the blown six in");
        Assert.True(ReferenceEquals(car.Def, C.Ship("monster-togg")), "the very layout the Monster TOGG is made with");
        Assert.True(bay.Carried("togg-engine") == 1 && bay.Carried("monster-tyres") == 0 && bay.Carried("monster-togg-engine") == 0,
            "the old engine in hand, the new parts used");
        Fixtures.Report(FormattableString.Invariant($"its frame moved {Vector3.Distance(origin, car.Origin) * 1000:0.0} mm through four changes"));
        Assert.True(Vector3.Distance(origin, car.Origin) < 0.02f, "its frame stays where it stood");
        Outcome stock = bay.Fit("togg-tyres", "wheels");
        Assert.True(stock.Code == OutcomeCode.TakeOffFirst && stock.Detail == Id("monster-tyres"), "the wheels are taken: the monster tyres come off first");
        Assert.Equal(OutcomeCode.TakeOffFirst, bay.Take("suspension").Code, "the kit stays while the monster tyres need it");
        bay.Settle();
        Fixtures.Report(FormattableString.Invariant($"on its monster tyres: {Vector3.Dot(car.Origin - bay.Bench.Position, bay.Bench.Up):0.00} m over the bench's base"));
        Assert.True(car.State.Asleep && car.State.Health > 0 && Vector3.Dot(Vector3.Transform(Vector3.UnitY, car.State.Rotation), bay.Bench.Up) > 0.99f,
            "it settles upright on its new wheels");
    }

    /// <summary>
    /// The bay the rules use is the one painted on the ground (the owner, 21 October 2026: a TOGG driven onto the paint wasn't
    /// in the bay; the rules had it on the bench's other side). The client paints it in the frame it draws the bench in
    /// (<see cref="Hollow.Simulation.Combat.Geometry.Upright"/>), so at any yaw the paint's middle is the bay's centre, it
    /// holds the vehicle there, and nothing on the bench's other side.
    /// </summary>
    [Test]
    public static void TheBayIsWhereItIsPainted()
    {
        using var bay = new Bay();
        BayStats stats = bay.Bench.Def.Bay!;
        Vector3 up = bay.Bench.Up;
        foreach (float yaw in new[] { 0f, 0.7f, -2.1f, MathF.PI })
        {
            Vector3 painted = bay.Bench.Position + Vector3.Transform(stats.At, Hollow.Simulation.Combat.Geometry.Upright(up, yaw));
            Vector3 behind = bay.Bench.Position - Vector3.Transform(stats.At, Hollow.Simulation.Combat.Geometry.Upright(up, yaw));
            Assert.True(Vector3.Distance(painted, stats.Centre(bay.Bench.Position, up, yaw)) < 1e-4f, $"yaw {yaw:0.0}: the paint's middle is the bay's centre");
            Assert.True(stats.Holds(bay.Bench.Position, up, yaw, painted + up * 1.2f), $"yaw {yaw:0.0}: a vehicle on the paint is in the bay");
            Assert.True(!stats.Holds(bay.Bench.Position, up, yaw, behind + up * 1.2f), $"yaw {yaw:0.0}: not on the bench's other side");
        }
        Assert.True(bay.Zone.Ships().InBay(bay.Bench) == bay.Car, "the TOGG driven onto the paint is the bay's");
    }

    /// <summary>
    /// Rust's car lift (the owner, 21 October 2026): a vehicle that comes slowly to a bay's edge, askew, is drawn in, its centre
    /// of mass over the bay's middle and its nose along the bay; once there it is left alone. One too far from the bay isn't.
    /// </summary>
    [Test]
    public static void ABayDrawsInAVehicleNearIt()
    {
        BayStats stats = C.Deployable("vehicle-workbench")!.Bay!;
        using (var bay = new Bay(new Vector2(stats.Clear.X + 1.0f, 0.6f), turned: 0.35f))
        {
            Ship car = bay.Car;
            Vector3 up = bay.Bench.Up;
            Vector3 off = car.State.Position - bay.Centre;
            float across = (off - up * Vector3.Dot(off, up)).Length();
            float heading = MathF.Abs(MathF.IEEERemainder(ShipMotor.YawOf(car.State.Rotation, up) - bay.Bench.Yaw, MathF.PI));
            Fixtures.Report(FormattableString.Invariant($"drawn in: {across:0.000} m from the bay's middle, {heading * 180 / MathF.PI:0.0}° off its line"));
            Assert.True(car.PulledInto == bay.Bench.Id && bay.Zone.Ships().InBay(bay.Bench) == car, "the bay drew it in");
            Assert.True(across < 0.15f && heading < 2 * MathF.PI / 180, "over the middle, along the bay");
            Assert.True(car.State.Asleep && car.State.Health > 0, "and it rests there");
            Vector3 rest = car.State.Position;
            bay.Step(CharacterMotor.TickRate * 2);
            Assert.True(Vector3.Distance(rest, car.State.Position) < 0.01f, "drawn in once: it stays as it stood");
            Assert.Equal(OutcomeCode.Done, bay.Take("wheels").Code, "and the bay works on it");
        }
        using (var far = new Bay(new Vector2(stats.Clear.X + ShipSystem.BayPull + 1.5f, 0), turned: 0.35f))
        {
            Vector3 off = far.Car.State.Position - far.Centre;
            Assert.True(far.Car.PulledInto.IsNone && Vector3.Dot(off, BayStats.Axes(far.Bench.Up, far.Bench.Yaw).Right) > far.Bench.Def.Bay!.Clear.X + ShipSystem.BayPull,
                "one too far from the bay stays where it is");
        }
    }

    /// <summary>
    /// The owner, 21 October 2026: "I took out the engine and I can no longer interact with the vehicle at all". A TOGG with
    /// its engine out is still a vehicle: it rests, and its door, seat and tank offer what they did.
    /// </summary>
    [Test]
    public static void AVehicleWithoutItsEngineIsStillWorkedWith()
    {
        using var bay = new Bay();
        Ship car = bay.Car;
        var options = new System.Collections.Generic.List<Hollow.Simulation.Interaction.InteractOption>();
        string Offered()
        {
            var lines = new System.Collections.Generic.List<string>();
            for (int part = 0; part < car.Def.Parts.Count; part++)
            {
                bay.Zone.Interaction().Options(bay.Ada, new Hollow.Simulation.Targets.TargetRef(Hollow.Simulation.Targets.TargetFamily.Ship, car.Id.Value, (byte)part), options);
                lines.Add($"{car.Def.Parts[part].Key}: {string.Join(" ", options.Where(o => o.Enabled).Select(o => C.Interactions.Verb(o.Verb)!.Key))}");
            }
            return string.Join("; ", lines);
        }
        string before = Offered();
        Assert.Equal(OutcomeCode.Done, bay.Take("nose").Code, "the engine out");
        bay.Settle();
        string after = Offered();
        Fixtures.Report($"with its engine: {before}");
        Fixtures.Report($"without: {after}");
        Assert.True(car.State.Asleep, "it rests without its engine");
        Assert.Equal(string.Join("; ", before.Split("; ").Where(l => !l.StartsWith("nose.", StringComparison.Ordinal))), after, "every other part offers what it did");
    }

    /// <summary>The same over the network: Ada's client still aims at the TOGG's door and is offered its verbs.</summary>
    [Test]
    public static void AVehicleWithoutItsEngineIsAimedAtOverTheNetwork()
    {
        using var rig = new LoopbackRig(simulated: true);
        GameClient ada = rig.Join("Ada");
        rig.WaitUntilPlaying();
        Zone z = rig.Server.Zone;
        Assert.True(z.Players().TryFindPlayer(ada.EntityId, out Entity adaEntity, out _), "Ada on the server");
        Player adaPlayer = z.Players().PlayerOf(adaEntity)!;
        GameContent content = z.Content!;
        Deployable bench = z.Players().SpawnDeployable(content.Deployable("vehicle-workbench")!, z.GroundAt(0, 8), 0, adaPlayer.Id);
        BayStats bayStats = bench.Def.Bay!;
        (Vector3 right, _) = BayStats.Axes(bench.Up, bench.Yaw);
        Vector3 centre = bayStats.Centre(bench.Position, bench.Up, bench.Yaw);
        ShipDef togg = content.Ship("togg")!;
        (Vector3 nose, _) = CharacterMotor.HorizontalBasis(bench.Yaw, bench.Up);
        Ship car = z.Ships().SpawnShip(togg, centre - nose * content.ShipMade(togg.Item)!.CentreOfMass.Z, bench.Yaw, adaPlayer.Id, 100);
        rig.Run(2.0, (_, _) => default);
        int doorPart = car.Def.Parts.Single(p => p.Key.EndsWith(".door", StringComparison.Ordinal)).Index;
        rig.Teleport("Ada", car.State.Position + Vector3.Transform(car.Def.Parts[doorPart].At.Position - car.Def.CentreOfMass, car.State.Rotation) +
            Vector3.Transform(Vector3.UnitX, car.State.Rotation) * 1.6f - bench.Up * 0.7f);
        rig.Run(2.0, (_, _) => default);
        string Aimed()
        {
            RemoteShip seen = ada.Ships().All.First(s => s.Id == car.Id);
            Vector3 eye = ada.Character.Position + ada.Frame.UpAt(ada.Character.Position) * ada.EyeHeight;
            Vector3 door = seen.PointOf(seen.Def!.Parts.Single(p => p.Key.EndsWith(".door", StringComparison.Ordinal)).At.Position);
            InteractAim? aim = ada.Interaction().AimInteraction(eye, door - eye);
            return aim == null ? "nothing" : $"{aim.Target.Family} part {aim.Target.Part}: " +
                string.Join(" ", aim.Options.Where(o => o.Enabled).Select(o => content.Interactions.Verb(o.Verb)!.Key));
        }
        int Socket(string key) => car.Def.Kind.Sockets.Single(s => s.Key == key).Index;
        string before = Aimed();
        ada.Local.TakeModule(bench.Id, car.Id, Socket("nose"), 1);
        rig.Run(3.0, (_, _) => default);
        string after = Aimed();
        Fixtures.Report($"aimed at the door with its engine: {before}; without: {after}; the client's build {ada.Ships().All.First(s => s.Id == car.Id).Def!.Build}");
        Assert.True(car.Def.Build.At(Socket("nose"), 1) == null, "the engine is out on the server");
        Assert.True(before.Contains("hatch-open"), "with its engine, its door offers itself");
        Assert.True(after.Contains("hatch-open"), "without, it still does");
        // A blown six in a cargo bay, then out again.
        adaPlayer.Inventory.Give(content, content.ItemByKey("monster-togg-engine"), 1);
        rig.Run(0.5, (_, _) => default);
        ada.Local.FitModule(bench.Id, car.Id, Socket("bay1"), 1, ContainerRef.Main, Find(ada.Local.Inventory.Main, content.ItemByKey("monster-togg-engine").Id));
        rig.Run(3.0, (_, _) => default);
        string bayEngine = Aimed();
        ada.Local.TakeModule(bench.Id, car.Id, Socket("bay1"), 1);
        rig.Run(3.0, (_, _) => default);
        string outAgain = Aimed();
        Fixtures.Report($"with a blown six in bay 1: {bayEngine}; out again: {outAgain}");
        Assert.True(bayEngine.Contains("hatch-open") && outAgain.Contains("hatch-open"), "its door offers itself with an engine in a bay, and after");
    }

    /// <summary>
    /// INSPECT VEHICLE (the owner, 21 October 2026: "even when vehicle is not in the bay I should be able to see its components
    /// healths"): any vehicle built of modules offers it, anywhere, to anyone; a ship without modules doesn't.
    /// </summary>
    [Test]
    public static void AVehicleOffersItsInspection()
    {
        using var bay = new Bay();
        Ship stranger = bay.Zone.Ships().SpawnShip(C.Ship("togg")!, bay.Zone.GroundAt(30, 0), 0, EntityId.None, 100);
        Ship finch = bay.Zone.Ships().SpawnShip(C.Ship("tin-finch")!, bay.Zone.GroundAt(-30, 0), 0, bay.Owner.Id, 100);
        int inspect = C.Interactions.Index("inspect-vehicle");
        var options = new System.Collections.Generic.List<Hollow.Simulation.Interaction.InteractOption>();
        foreach ((Ship ship, bool offered, string what) in new[] { (bay.Car, true, "her TOGG in the bay"), (stranger, true, "a stranger's TOGG far from any bay"), (finch, false, "the Tin Finch, which has no modules") })
        {
            bay.Zone.Entities.Get<Character>(bay.Ada).State.Position = ship.Origin + BayStats.Axes(bay.Bench.Up, 0).Right * 2.5f;
            bay.Zone.Interaction().Options(bay.Ada, new Hollow.Simulation.Targets.TargetRef(Hollow.Simulation.Targets.TargetFamily.Ship, ship.Id.Value, 0), options);
            Assert.Equal(offered, options.Any(o => o.Verb == inspect && o.Enabled), what);
        }
    }

    /// <summary>
    /// The server's rules at the bay: out of the bay, moving, someone in it, not its crew, or the player away from the bench and
    /// its bay, no change; with no room in the inventory a module stays where it is.
    /// </summary>
    [Test]
    public static void TheBayRefusesWhatItMust()
    {
        using var bay = new Bay();
        // Another TOGG beside the bench: not in its bay.
        Ship off = bay.Zone.Ships().SpawnShip(C.Ship("togg")!, bay.Zone.GroundAt(14, 0), 0, bay.Owner.Id, 100);
        Outcome away = bay.Zone.Ships().TakeModule(bay.Ada, bay.Bench.Id, off.Id, bay.Socket("wheels"), 1);
        Assert.True(away.Code == OutcomeCode.NeedsStation && away.Detail == Id("vehicle-workbench"), "out of the bay: needs a vehicle workbench");
        // Moving.
        bay.Car.State.Flags &= ~ShipFlags.Landed;
        bay.Car.State.RestTicks = 0;
        bay.Car.State.Velocity = Vector3.Transform(Vector3.UnitZ, bay.Car.State.Rotation) * 3;
        Assert.Equal(OutcomeCode.ShipMoving, bay.Take("wheels").Code, "not while it moves");
        bay.Car.State.Velocity = Vector3.Zero;
        bay.Settle();
        // Someone in its seat.
        Vector3 beside = bay.Zone.Entities.Get<Character>(bay.Ada).State.Position;
        Entity bo = bay.Zone.Players().AddPlayer(bay.Zone.NextId(), "Bo", bay.Zone.NewCharacter(beside));
        Player boPlayer = bay.Zone.Players().PlayerOf(bo)!;
        Assert.True(bay.Zone.Ships().Reseat(bo, bay.Car, bay.Car.Def.Helm.Index), "Bo sits at the wheel");
        Assert.Equal(OutcomeCode.SomeoneAboard, bay.Take("wheels").Code, "not with someone in it");
        Assert.Equal(OutcomeCode.Done, bay.Zone.Ships().LeaveSeat(bo).Code, "Bo gets out");
        bay.Settle();
        // Bo isn't on its crew.
        bay.Zone.Entities.Get<Character>(bo).State.Position = beside;
        Assert.True(!bay.Car.IsAuthorised(boPlayer.Id.Value), "Bo isn't its crew");
        Assert.Equal(OutcomeCode.NotCrew, bay.Zone.Ships().TakeModule(bo, bay.Bench.Id, bay.Car.Id, bay.Socket("wheels"), 1).Code, "not by a stranger");
        // Ada far from the bench and its bay.
        bay.Zone.Entities.Get<Character>(bay.Ada).State.Position = bay.Zone.GroundAt(-25, 0) + bay.Bench.Up * 0.2f;
        Assert.Equal(OutcomeCode.OutOfReach, bay.Take("wheels").Code, "not from across the yard");
        bay.Zone.Entities.Get<Character>(bay.Ada).State.Position = beside;
        // A full inventory keeps the module where it is.
        while (bay.Owner.Inventory.Give(C, C.ItemByKey("wood"), 1000) == 0)
        {
        }
        Assert.Equal(OutcomeCode.NoSpace, bay.Take("nose").Code, "no room for the engine");
        Assert.True(bay.Car.Def.Build.At(bay.Socket("nose"), 1) != null, "it stays fitted");
    }

    /// <summary>
    /// ShipCodec 9: a rebuilt vehicle saves and comes back as its layout. An older save's vehicle on wheels (version 8, before
    /// running gear was modules) is dropped (the owner, 21 October 2026: no old saves while in development).
    /// </summary>
    [Test]
    public static void BuildsAreSavedAndOldVehiclesDropped()
    {
        using var bay = new Bay();
        Assert.Equal(OutcomeCode.Done, bay.Take("wheels").Code, "stock tyres off");
        Assert.Equal(OutcomeCode.Done, bay.Fit("togg-lift-kit", "suspension").Code, "kit on");
        Assert.Equal(OutcomeCode.Done, bay.Fit("monster-tyres", "wheels").Code, "monster tyres on");
        Zone z = bay.Zone;
        byte[] bytes = Hollow.Persistence.ShipCodec.Encode(bay.Car, z.Frame, z.Tick);
        Assert.Equal(Hollow.Persistence.ShipCodec.Version, bytes[0], "the current version");
        Ship back = Hollow.Persistence.ShipCodec.Decode(bay.Car.Id, bytes, C, z.Frame)!.Ship;
        Assert.True(ReferenceEquals(back.Def, bay.Car.Def) && Vector3.Distance(back.State.Position, bay.Car.State.Position) < 1e-3f,
            "the rebuilt TOGG comes back as it was, where it was");
        byte[] old = (byte[])bytes.Clone();
        old[0] = 8;
        Assert.True(Hollow.Persistence.ShipCodec.Decode(bay.Car.Id, old, C, z.Frame) == null, "an older save's vehicle on wheels is dropped");
    }

    /// <summary>
    /// Over the network: Ada's client takes the TOGG's stock tyres off and fits the kit and the monster tyres through the bay's
    /// intents; the server answers each, and both clients compose the new layout from its details.
    /// </summary>
    [Test]
    public static void TheBayWorksOverTheNetwork()
    {
        using var rig = new LoopbackRig(simulated: true);
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        Zone z = rig.Server.Zone;
        Assert.True(z.Players().TryFindPlayer(ada.EntityId, out Entity adaEntity, out _), "Ada on the server");
        Player adaPlayer = z.Players().PlayerOf(adaEntity)!;
        GameContent content = z.Content!;
        Deployable bench = z.Players().SpawnDeployable(content.Deployable("vehicle-workbench")!, z.GroundAt(0, 8), 0, adaPlayer.Id);
        (Vector3 right, Vector3 forward) = Hollow.Simulation.Combat.Geometry.Axes(bench.Up, bench.Yaw);
        BayStats bayStats = bench.Def.Bay!;
        Vector3 centre = bayStats.Centre(bench.Position, bench.Up, bench.Yaw);
        ShipDef togg = content.Ship("togg")!;
        Ship car = z.Ships().SpawnShip(togg, centre - forward * content.ShipMade(togg.Item)!.CentreOfMass.Z, 0, adaPlayer.Id, 100);
        rig.Teleport("Ada", centre + right * (bayStats.Clear.X + 1.2f));
        rig.Run(3.0, (_, _) => default);
        adaPlayer.Inventory.Give(content, content.ItemByKey("togg-lift-kit"), 1);
        adaPlayer.Inventory.Give(content, content.ItemByKey("monster-tyres"), 1);
        rig.Run(0.5, (_, _) => default);
        int done = 0;
        var refused = new System.Collections.Generic.List<IntentAnswer>();
        ada.Local.IntentAnswered += a => done += a.Code == OutcomeCode.Done ? 1 : 0;
        ada.Local.IntentRejected += a => refused.Add(a);
        int Socket(string key) => togg.Kind.Sockets.Single(s => s.Key == key).Index;
        int Slot(string item) => Find(ada.Local.Inventory.Main, content.ItemByKey(item).Id);
        ada.Local.TakeModule(bench.Id, car.Id, Socket("wheels"), 1);
        rig.Run(0.5, (_, _) => default);
        ada.Local.FitModule(bench.Id, car.Id, Socket("suspension"), 1, ContainerRef.Main, Slot("togg-lift-kit"));
        rig.Run(0.5, (_, _) => default);
        ada.Local.FitModule(bench.Id, car.Id, Socket("wheels"), 1, ContainerRef.Main, Slot("monster-tyres"));
        rig.Run(1.0, (_, _) => default);
        Fixtures.Report($"answers: {done} done, refused {string.Join(", ", refused.Select(r => $"{r.Code} {r.Detail}"))}");
        Assert.True(done == 3 && refused.Count == 0, "the server did all three");
        ShipBuild want = car.Def.Build;
        Assert.True(want.Entries.Any(e => e.Module == content.ItemByKey("monster-tyres").Id) && car.Def.Raise == 0.75f, "on the server: lifted on monster tyres");
        foreach ((GameClient client, string name) in new[] { (ada, "Ada"), (bo, "Bo") })
        {
            RemoteShip? seen = client.Ships().All.FirstOrDefault(s => s.Id == car.Id);
            Assert.True(seen?.Def is { } d && d.Build.Equals(want) && d.Model == "monster-togg-frame" && d.Wheels.All(w => w.Radius == 0.85f),
                $"{name}'s client composes the new build");
        }
        Assert.True(Find(ada.Local.Inventory.Main, content.ItemByKey("togg-tyres").Id) >= 0, "the stock tyres in Ada's inventory");
    }
}
