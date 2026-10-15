using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>
/// Building aboard the Iron Wake (IRON_WAKE.md §8, §4.10, IW5): deployables placed on its decks in its frame, by its crew,
/// inside its build zones and out of its exclusions; what is aboard rides it, works in flight, is a spawn point, is saved
/// with it and breaks and spills when it is wrecked.
/// </summary>
public static class IronWakeBuildTests
{
    /// <summary>Deck A, in the lower forward construction deck: clear of every exclusion, room all round.</summary>
    private static readonly Vector3 DeckSpot = new(-2.0f, 3.2f, 4.8f);

    /// <summary>Where a player stands to place at <see cref="DeckSpot"/>, looking aft.</summary>
    private static readonly Vector3 DeckStand = new(-2.0f, 3.2f, 7.0f);

    // ---------------------------------------------------------------- helpers

    private static Placeable PlaceableOf(IronWakeRig rig, string item) => rig.Game.C.ItemByKey(item).Places!;

    /// <summary>The ghost of what an item places, aimed from a character's eye at a point of the ship's frame.</summary>
    private static PlacementCandidate? Ghost(IronWakeRig rig, Entity e, string item, Vector3 local) =>
        rig.Zone.Placement().Aim(e, PlaceableOf(rig, item), rig.Pose.ToWorld(local) - rig.Zone.EyeOf(rig.Ch(e).State));

    /// <summary>Places what a ghost shows from the main slot holding its item: the intent its client sends.</summary>
    private static Outcome Place(IronWakeRig rig, Entity e, PlacementCandidate ghost)
    {
        Container main = rig.Game.P(e).Inventory.Main;
        int slot = Enumerable.Range(0, main.Size).First(i => main[i].Item == ghost.Placeable.SlotItem!.Id);
        return rig.Zone.Placement().Place(e, ContainerRef.Main, slot, ghost);
    }

    /// <summary>A player aboard at a point of the ship's frame, looking along a direction of it, with items in their main slots.</summary>
    private static Entity Aboard(IronWakeRig rig, string name, Vector3 local, Vector3 looking, bool crew, params (string Item, int Count)[] kit)
    {
        Entity e = rig.Aboard(name, local, looking);
        for (int i = 0; i < kit.Length; i++)
        {
            ItemDef item = rig.Game.C.ItemByKey(kit[i].Item);
            rig.Game.P(e).Inventory.Main.Set(i, ItemStack.Of(item, Math.Min(kit[i].Count, item.StackSize)));
        }
        if (crew)
        {
            Assert.Equal(OutcomeCode.Done, rig.Zone.Ships().Authorise(rig.Game.P(e).Id.Value, rig.Ship).Code, $"{name} is the crew");
        }
        rig.Step(20); // onto the deck
        return e;
    }

    /// <summary>Places an item's deployable aboard at a point of the ship's frame and returns it (asserting it went).</summary>
    private static Deployable PlaceAboard(IronWakeRig rig, Entity e, string item, Vector3 local)
    {
        PlacementCandidate? ghost = Ghost(rig, e, item, local);
        Assert.True(ghost is { Valid: true, Aboard: not null }, $"the {item}'s ghost fits aboard ({ghost})");
        Assert.Equal(OutcomeCode.Done, Place(rig, e, ghost!).Code, $"the {item} placed");
        Deployable placed = rig.Zone.Things().Riders.OfType<Deployable>().Last(d => d.Def.Key == item);
        Assert.True(placed.Link.Support.Whole == rig.Ship.Ref && placed.Support == rig.Ship.Ref, $"the {item} is held by the ship");
        return placed;
    }

    /// <summary>
    /// The pilot flies: up off the pads for 5 s, round on the spot for 6 s while it climbs a little, holds for 2 s, then
    /// down onto its pads; <paramref name="each"/> after every tick.
    /// </summary>
    private static void TakeOffTurnAndLand(IronWakeRig rig, Action<int> each)
    {
        rig.Fly(150, _ => new ShipInput { Vertical = 1 }, each);
        rig.Fly(180, _ => new ShipInput { Vertical = 0.15f, Yaw = 0.7f }, i => each(150 + i));
        rig.Fly(60, _ => default, i => each(330 + i));
        int t = 390;
        for (int i = 0; i < 40 * 30 && !rig.Ship.State.Has(ShipFlags.Landed); i++)
        {
            rig.Fly(1, _ => new ShipInput { Vertical = Math.Clamp(-rig.Height * 0.08f, -0.6f, -0.15f) }, _ => each(t++));
        }
        rig.Fly(30, _ => new ShipInput { Vertical = -0.2f }, _ => each(t++));
    }

    private static float Grid => 1f / 256;

    // ---------------------------------------------------------------- the tests

    /// <summary>
    /// IRON_WAKE.md §8, §14: a small box placed on Deck A by the crew is the ship's: linked to it with its pose in the ship's
    /// frame, a box of its space (people walk into it). It rides the ship through take-off, a turn and landing, its place on
    /// the deck never moving; a player aboard opens it in flight.
    /// </summary>
    [Test]
    public static void ABoxOnDeckARidesTheShipAndOpensInFlight()
    {
        using var rig = new IronWakeRig();
        Entity ada = Aboard(rig, "Ada", DeckStand, -Vector3.UnitZ, crew: true, ("small-box", 1), ("wood", 50));
        Deployable box = PlaceAboard(rig, ada, "small-box", DeckSpot);
        Vector3 placed = box.Link.Position;
        Assert.Near(3.2f, placed.Y, 0.02f, "on Deck A");
        Assert.True(Vector3.Distance(new Vector3(placed.X, 0, placed.Z), new Vector3(DeckSpot.X, 0, DeckSpot.Z)) < 0.05f, "where it was aimed");
        Assert.True(rig.Ship.Space!.ThingBodies == 1, "a box of the ship's space");

        // People aboard meet it: walking aft into it, Ada is stopped, or climbs it (half a metre: a step and a capsule's curve).
        float highest = 0;
        rig.Walk(ada, -Vector3.UnitZ, 60, each: () => highest = MathF.Max(highest, rig.Ch(ada).State.Position.Y));
        float past = rig.Ch(ada).State.Position.Z;
        Fixtures.Report($"Ada walking aft into the box: her feet rose to {highest:0.00} m, ended at z {past:0.00} (the box at {placed.Z:0.00})");
        Assert.True(past > placed.Z || highest > placed.Y + 0.3f, "Ada meets the box: it is solid aboard");
        rig.Ch(ada).State.Position = DeckStand + Vector3.UnitY * 0.02f;
        rig.Step(10);

        Entity bo = rig.Pilot("Bo");
        float drift = 0, climbed = 0, start = rig.Height, travelled = 0;
        Vector3 ground = box.Position;
        float startYaw = Facing.YawOf(rig.Pose.DirectionToWorld(Vector3.UnitZ), rig.Zone.Frame.UpAt(rig.Ship.State.Position)), turned = 0;
        Outcome opened = OutcomeCode.Invalid;
        TakeOffTurnAndLand(rig, i =>
        {
            drift = MathF.Max(drift, Vector3.Distance(rig.Pose.ToLocal(box.Position), placed));
            climbed = MathF.Max(climbed, rig.Height - start);
            travelled = MathF.Max(travelled, Vector3.Distance(box.Position, ground));
            float yaw = Facing.YawOf(rig.Pose.DirectionToWorld(Vector3.UnitZ), rig.Zone.Frame.UpAt(rig.Ship.State.Position));
            turned = MathF.Max(turned, MathF.Abs(MathF.IEEERemainder(yaw - startYaw, MathF.Tau)));
            if (i == 240)
            {
                // In flight, Ada opens it.
                opened = rig.Zone.Interaction().Interact(ada, box.Ref, "open");
            }
        });
        Fixtures.Report($"climbed {climbed:0.0} m, turned {turned * 180 / MathF.PI:0}°, the box carried {travelled:0.0} m through the world and " +
            $"{drift * 1000:0.000} mm off its place on the deck; landed at {rig.Height:0.00} m; opened in flight: {opened}");
        Assert.True(climbed > 10 && turned > 0.5f, "the ship took off and turned");
        Assert.True(rig.Ship.State.Has(ShipFlags.Landed) && rig.Height < 0.6f, "and landed");
        Assert.True(drift < 1e-3f, "the box's place on the deck never moved");
        Assert.True(travelled > 10, "while the ship carried it");
        Assert.Equal(OutcomeCode.Done, opened.Code, "Ada opened it in flight");
        Assert.True(rig.Game.P(ada).Looting == box.Id, "and loots it");
        Assert.True(rig.Ch(ada).State.Frame == rig.Key && rig.Ch(bo).State.Frame == rig.Key, "both aboard all the while");
    }

    /// <summary>
    /// IRON_WAKE.md §8: what may not be placed aboard is refused with its line: in the stair's exclusion, on the ramp, by a
    /// stranger (not on the ship's list), past the ship's 80 deployables, and a tall furnace (3.8 m) outside the high bay,
    /// which takes it.
    /// </summary>
    [Test]
    public static void RefusalsAboardEachWithItsLine()
    {
        using (var rig = new IronWakeRig())
        {
            Texts texts = rig.Game.C.Texts;
            Entity ada = Aboard(rig, "Ada", new Vector3(1.0f, 3.2f, -0.6f), Vector3.UnitX, crew: true, ("small-box", 3));
            PlacementCandidate? stairs = Ghost(rig, ada, "small-box", new Vector3(3.0f, 3.2f, -0.6f));
            Fixtures.Report($"at the stair's foot: {stairs?.Outcome} \"{texts.Line(stairs?.Outcome ?? default)}\"");
            Assert.True(stairs?.Aboard != null, "aimed aboard");
            Assert.Equal(OutcomeCode.KeepClear, stairs!.Outcome.Code, "the stair and its landing are kept clear");

            Entity cy = Aboard(rig, "Cy", new Vector3(0, 3.2f, -16.8f), -Vector3.UnitZ, crew: true, ("small-box", 1));
            PlacementCandidate? ramp = Ghost(rig, cy, "small-box", new Vector3(0.8f, 2.9f, -18.6f));
            Fixtures.Report($"on the ramp: {ramp?.Outcome} on body {ramp?.Aboard?.Body} \"{texts.Line(ramp?.Outcome ?? default)}\"");
            Assert.True(ramp?.Aboard != null, "aimed at the ramp");
            Assert.Equal(OutcomeCode.KeepClear, ramp!.Outcome.Code, "the ramp is kept clear");

            Entity bo = Aboard(rig, "Bo", DeckStand, -Vector3.UnitZ, crew: false, ("small-box", 1));
            PlacementCandidate? stranger = Ghost(rig, bo, "small-box", DeckSpot);
            Fixtures.Report($"a stranger: {stranger?.Outcome} \"{texts.Line(stranger?.Outcome ?? default)}\"");
            Assert.Equal(OutcomeCode.NotCrew, stranger!.Outcome.Code, "a stranger may not build aboard");
            Assert.Equal(OutcomeCode.NotCrew, Place(rig, bo, stranger).Code, "and the server says so");

            // Eighty deployables aboard already (stacked in the aft hold, the server's own): the eighty-first is refused.
            DeployableDef crate = rig.Game.C.ItemByKey("small-box").Places!.Definition as DeployableDef ?? throw new InvalidOperationException();
            int limit = PlaceableOf(rig, "small-box").Rule.PerShip;
            for (int i = 0; i < limit; i++)
            {
                var local = new Vector3(-3.0f, 3.2f, -14.0f);
                var d = new Deployable(rig.Zone.NextId(), crate, rig.Pose.ToWorld(local), rig.Pose.Up, 0, rig.Game.P(ada).Id)
                {
                    Support = rig.Ship.Ref,
                    Link = new SupportLink(rig.Ship.Ref, local, Quaternion.Identity),
                };
                rig.Zone.Things().AddStatic(d);
            }
            Entity dee = Aboard(rig, "Dee", DeckStand, -Vector3.UnitZ, crew: true, ("small-box", 1));
            PlacementCandidate? full = Ghost(rig, dee, "small-box", DeckSpot);
            Fixtures.Report($"past {limit}: {full?.Outcome} \"{texts.Line(full?.Outcome ?? default)}\"");
            Assert.True(limit == 80, "80 deployables a ship");
            Assert.Equal(OutcomeCode.ShipFull, full!.Outcome.Code, "the ship holds no more");
        }

        // A furnace 3.8 m tall: refused on the lower forward deck (3.8 m of headroom), taken in the high bay (7.5 m).
        Dictionary<string, string> data = GameContent.ReadTexts(ContentFixture.Folder);
        data["deployables.json"] = data["deployables.json"].Replace("\"size\": [0.5, 0.85, 0.5]", "\"size\": [0.6, 1.9, 0.6]", StringComparison.Ordinal);
        GameContent tall = GameContent.FromTexts(data);
        Assert.True(tall.Problems.Count == 0 && tall.Deployable(tall.ItemByKey("furnace").Id)?.HalfExtents.Y > 1.8f, "a tall furnace's data");
        using (var rig = new IronWakeRig(content: tall))
        {
            Entity ada = Aboard(rig, "Ada", DeckStand, -Vector3.UnitZ, crew: true, ("furnace", 2));
            PlacementCandidate? low = Ghost(rig, ada, "furnace", DeckSpot);
            Fixtures.Report($"a tall furnace on the forward deck: {low?.Outcome} \"{rig.Game.C.Texts.Line(low?.Outcome ?? default)}\"");
            Assert.Equal(OutcomeCode.TooTall, low!.Outcome.Code, "too tall outside the bay");
            rig.Ch(ada).State.Position = new Vector3(0, 3.22f, -5.6f);
            rig.Ch(ada).Input.View = -Vector3.UnitZ;
            rig.Step(10);
            PlacementCandidate? bay = Ghost(rig, ada, "furnace", new Vector3(0, 3.2f, -8.0f));
            Fixtures.Report($"in the bay: {bay?.Outcome}");
            Assert.True(bay is { Valid: true }, $"the high bay takes it ({bay?.Outcome})");
        }
    }

    /// <summary>IRON_WAKE.md §8: a furnace aboard, lit, smelts metal ore while the ship flies.</summary>
    [Test]
    public static void AFurnaceAboardSmeltsInFlight()
    {
        using var rig = new IronWakeRig();
        Entity ada = Aboard(rig, "Ada", DeckStand, -Vector3.UnitZ, crew: true, ("furnace", 1));
        Deployable furnace = PlaceAboard(rig, ada, "furnace", DeckSpot);
        GameContent c = rig.Game.C;
        furnace.Storage!.Set(furnace.Def.Device!.FuelSlots[0], ItemStack.Of(c.ItemByKey("wood"), 100));
        furnace.Storage.Set(furnace.Def.Device.InputSlots[0], ItemStack.Of(c.ItemByKey("metal-ore"), 20));
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(ada, furnace.Ref, "light").Code, "Ada lights it");
        rig.Pilot("Bo");
        float climbed = 0, start = rig.Height;
        rig.Fly(30 * 25, i => i < 150 ? new ShipInput { Vertical = 1 } : new ShipInput { Vertical = 0.1f, Throttle = 0.3f, Yaw = 0.4f },
            _ => climbed = MathF.Max(climbed, rig.Height - start));
        int fragments = furnace.Storage.Slots.ToArray().Where(s => s.Item == c.ItemByKey("metal-fragments").Id).Sum(s => s.Count);
        Fixtures.Report($"in 25 s of flight ({climbed:0} m up) the furnace made {fragments} metal fragments; lit {furnace.Lit}");
        Assert.True(climbed > 10, "in flight");
        Assert.True(furnace.Lit && fragments >= 4, "it smelts as anywhere");
    }

    /// <summary>
    /// IRON_WAKE.md §8: a sleeping bag aboard is its owner's spawn point aboard: dead after the ship flew off, they come back
    /// in front of the bag, in the ship's frame, wherever the ship is now.
    /// </summary>
    [Test]
    public static void ASleepingBagAboardRespawnsItsOwnerAboard()
    {
        using var rig = new IronWakeRig();
        Entity ada = Aboard(rig, "Ada", DeckStand, -Vector3.UnitZ, crew: true, ("sleeping-bag", 1));
        Deployable bag = PlaceAboard(rig, ada, "sleeping-bag", DeckSpot);
        rig.Pilot("Bo");
        rig.Fly(30 * 6, _ => new ShipInput { Vertical = 1, Yaw = 0.3f });
        Hollow.Simulation.Players.Player p = rig.Game.P(ada);
        rig.Zone.Players().Die(ada, p, "Tests", EntityId.None, "");
        rig.Zone.UnixMilliseconds += (long)(bag.Def.RespawnSeconds * 1000) + 1000;
        rig.Step(2);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Players().Respawn(ada, bag.Id).Code, "Ada respawns at her bag");
        rig.Step(30);
        CharacterState s = rig.Ch(ada).State;
        float from = Vector3.Distance(s.Position, bag.Link.Position);
        Fixtures.Report($"back aboard {s.Frame == rig.Key} at {s.Position} ({from:0.00} m from the bag), {rig.Height:0} m up");
        Assert.True(s.Frame == rig.Key, "aboard, in the ship's frame");
        Assert.True(from < 1.0f && MathF.Abs(s.Position.Y - 3.2f) < 0.1f, "standing on Deck A beside the bag");
        Assert.True(rig.Height > 15, "with the ship far up");
    }

    /// <summary>
    /// IRON_WAKE.md §4.10: a grenade thrown along the deck of a ship that climbs and turns flies in the ship's frame under the
    /// deck's gravity and comes to rest on the deck, riding the ship.
    /// </summary>
    [Test]
    public static void AGrenadeThrownAlongATurningDeckLandsOnTheDeck()
    {
        using var rig = new IronWakeRig();
        Entity ada = Aboard(rig, "Ada", DeckStand, -Vector3.UnitZ, crew: false);
        rig.Pilot("Bo");
        rig.Fly(120, _ => new ShipInput { Vertical = 1 });
        ThrowableStats grenade = rig.Game.C.ItemByKey("tin-grenade").Throwable!;
        // Thrown from Ada's eye, aft along the deck at 7 m/s, while the ship climbs and turns hard.
        ShipPose pose = rig.Pose;
        Vector3 eye = rig.Zone.EyeOf(rig.Ch(ada).State);
        Vector3 along = pose.DirectionToWorld(Vector3.Normalize(new Vector3(0, 0.25f, -1)));
        LiveExplosive e = rig.Zone.Explosives().SpawnExplosive(grenade, eye + along * 0.3f, along * 7 + pose.VelocityAt(eye), rig.Game.P(ada).Id, "Ada", lit: false);
        float spin = 0;
        int ticks = 0;
        for (; ticks < 30 * 6 && e.State == ExplosiveState.Flying; ticks++)
        {
            rig.Fly(1, _ => new ShipInput { Vertical = 0.6f, Yaw = 1f });
            spin = MathF.Max(spin, rig.Ship.State.AngularVelocity.Length());
        }
        Vector3 local = rig.Pose.ToLocal(e.Position);
        Fixtures.Report($"at rest after {ticks / 30f:0.00} s ({e.Bounces} bounces), the ship turning at up to {spin:0.00} rad/s; on the deck at {local} " +
            $"held by {e.Link.Support}");
        Assert.True(spin > 0.05f, "the ship turned");
        Assert.True(e.State == ExplosiveState.Resting, "it came to rest");
        Assert.True(e.Link.Support.Whole == rig.Ship.Ref, "on the ship, held by it");
        Assert.True(MathF.Abs(local.Y - 3.2f) < 0.25f && local.Z < DeckStand.Z - 2, "on Deck A, aft of where it was thrown");
        rig.Fly(60, _ => new ShipInput { Vertical = 0.6f, Yaw = 1f });
        Assert.True(Vector3.Distance(rig.Pose.ToLocal(e.Position), local) < 0.01f, "and rides the ship there");
    }

    /// <summary>
    /// IRON_WAKE.md §5.5, §8: a wrecked ship breaks what was built aboard, and its contents spill into bags at the wreck; a
    /// bag lying aboard falls to the ground below.
    /// </summary>
    [Test]
    public static void AWreckSpillsWhatWasAboard()
    {
        using var rig = new IronWakeRig();
        Entity ada = Aboard(rig, "Ada", DeckStand, -Vector3.UnitZ, crew: true, ("large-box", 1));
        GameContent c = rig.Game.C;
        Deployable box = PlaceAboard(rig, ada, "large-box", DeckSpot);
        box.Storage!.Set(0, ItemStack.Of(c.ItemByKey("metal-fragments"), 300));
        DroppedBag? dropped = rig.Zone.Spill().Spill(rig.Pose.ToWorld(new Vector3(1.0f, 3.6f, 4.0f)), ItemStack.Of(c.ItemByKey("wood"), 40));
        Assert.True(dropped is { Rides: true } && dropped.Link.Support.Whole == rig.Ship.Ref, "a bag dropped on the deck rides the ship");
        rig.Ch(ada).State.Position = DeckStand + Vector3.UnitY * 0.02f;
        rig.Zone.Ships().DamageShip(rig.Ship, 1_000_000, ShipDamage.Explosive, null);
        rig.Step(5);
        Assert.True(rig.Ship.Destroyed, "wrecked");
        Assert.True(rig.Zone.Things().StaticOf(box.Id) == null, "the box broke");
        DroppedBag[] bags = rig.Zone.Things().Statics().OfType<DroppedBag>().ToArray();
        DroppedBag? spilt = bags.FirstOrDefault(b => b.Loot.Slots.ToArray().Any(s => s.Item == c.ItemByKey("metal-fragments").Id && s.Count == 300));
        Fixtures.Report($"{bags.Length} bags after the wreck: the box's at {spilt?.Position} ({rig.Zone.HeightAboveGround(spilt?.Position ?? default):0.00} m up), " +
            $"the dropped one at {rig.Zone.HeightAboveGround(dropped!.Position):0.00} m up, riding {dropped.Rides}");
        Assert.True(spilt != null && !spilt.Rides, "the box's contents spilt into a bag");
        Assert.True(rig.Zone.HeightAboveGround(spilt!.Position) < 1.0f, "at the wreck, on the ground");
        Assert.True(!dropped.Rides && rig.Zone.HeightAboveGround(dropped.Position) < 1.0f, "the loose bag fell to the ground");
        Assert.True(rig.Zone.Things().Riders.Count == 0, "nothing rides the wreck");
    }

    /// <summary>
    /// IRON_WAKE.md §4.11, §8: a box, a furnace and a sleeping bag placed aboard are saved in the ship's frame and come back
    /// aboard after a restart, the ship with them, in its space; a client joining is sent them in the ship's frame.
    /// </summary>
    [Test]
    public static void ARestartKeepsTheBoxTheFurnaceAndTheBagAboard()
    {
        using var file = new TempDatabase();
        var saved = new Dictionary<string, (Vector3 Local, ulong Id)>();
        IronWakeNetTests.ShipFacts ship;
        using (var rig = new LoopbackRig(file.Path))
        {
            GameClient ada = rig.Join("Ada");
            rig.WaitUntilPlaying();
            ship = IronWakeNetTests.Spawn(rig, "Ada");
            EntityId adaId = ada.EntityId;
            IronWakeNetTests.PutAboard(rig, adaId, ship.Id, new Vector3(0, 3.2f, 1.5f));
            rig.Run(1.0, (_, _) => default);
            var spots = new (string Item, Vector3 At)[] { ("small-box", new(-2.0f, 3.2f, 3.2f)), ("furnace", new(-2.0f, 3.2f, 1.6f)), ("sleeping-bag", new(-0.6f, 3.2f, 3.6f)) };
            rig.Server.Inspect(sv =>
            {
                Zone z = sv.Zone;
                Assert.True(z.Entities.TryFind(adaId, out Entity e), "Ada in the zone");
                Ship s = z.Ships().ShipOf(new EntityId(ship.Id))!;
                ShipPose pose = ShipFrame.PoseOf(s.State, s.Def.CentreOfMass);
                foreach ((string item, Vector3 at) in spots)
                {
                    ItemDef def = z.Content!.ItemByKey(item);
                    z.Players().PlayerOf(e)!.Inventory.Main.Set(0, ItemStack.Of(def, 1));
                    PlacementCandidate? ghost = z.Placement().Aim(e, def.Places!, pose.ToWorld(at) - z.EyeOf(z.Entities.Get<Character>(e).State));
                    Assert.True(ghost is { Valid: true, Aboard: not null }, $"the {item} fits aboard ({ghost})");
                    Assert.Equal(OutcomeCode.Done, z.Placement().Place(e, ContainerRef.Main, 0, ghost!).Code, $"the {item} placed");
                    Deployable d = z.Things().Riders.OfType<Deployable>().Last(r => r.Def == def.Places!.Definition);
                    saved[item] = (d.Link.Position, d.Id.Value);
                }
                return true;
            });
            rig.Run(1.0, (_, _) => default);
            // Ada's client was sent them in the ship's frame.
            foreach ((string item, (Vector3 local, ulong id)) in saved)
            {
                RemoteThing? remote = ada.Survival().ThingOf(new EntityId(id));
                Assert.True(remote is { Aboard: true } && Vector3.Distance(remote.LocalPosition, local) < 0.01f, $"Ada's client has the {item} aboard, where it stands");
            }
            rig.Server.Inspect(sv => sv.SaveAll()).Wait(TimeSpan.FromSeconds(10));
        }

        using (var rig = new LoopbackRig(file.Path))
        {
            int boxes = 0;
            string report = rig.Server.Inspect(sv =>
            {
                Zone z = sv.Zone;
                Ship? s = z.Ships().ShipOf(new EntityId(ship.Id));
                Assert.True(s != null, "the ship came back");
                boxes = s!.Space!.ThingBodies;
                var lines = new List<string>();
                foreach ((string item, (Vector3 local, ulong id)) in saved)
                {
                    WorldObject? back = z.Things().StaticOf(new EntityId(id));
                    Assert.True(back is Deployable { Rides: true } d && d.Link.Support.Whole == s.Ref && Vector3.Distance(d.Link.Position, local) < 1e-4f,
                        $"the {item} came back aboard, where it stood");
                    ShipPose pose = ShipFrame.PoseOf(s.State, s.Def.CentreOfMass);
                    Assert.True(Vector3.Distance(pose.ToLocal(back!.Position), local) < 0.01f, $"and the ship carries the {item} there");
                    lines.Add($"{item} at {local}");
                }
                return string.Join("; ", lines);
            });
            Fixtures.Report($"after the restart: {report}; {boxes} boxes in the ship's space");
            Assert.True(boxes == 2, "the box and the furnace are boxes of its space again (the bag is soft)");
            GameClient bo = rig.Join("Bo");
            rig.WaitUntilPlaying();
            rig.Run(1.0, (_, _) => default);
            Assert.True(saved.Values.All(v => bo.Survival().ThingOf(new EntityId(v.Id)) is { Aboard: true }), "a client joining is sent them aboard");
        }
    }
}
