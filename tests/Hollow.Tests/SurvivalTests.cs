using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>A survival zone on the first world with players placed by hand, stepped tick by tick.</summary>
public sealed class GameRig : IDisposable
{
    private long _ids = 1;

    /// <summary>The zone on the first world's stand-in ground, or on a surface the test makes for the zone's spec (one with a sea).</summary>
    public GameRig(Func<ZoneSpec, Hollow.Surface.ISurface>? surface = null)
    {
        ZoneSpec spec = ZoneSpec.FirstWorld(Fixtures.Galaxy, 28.4, -61.7)! with { StandIn = true };
        Zone = surface == null ? new Zone(spec, content: ContentFixture.Content) : new Zone(surface(spec), spec, content: ContentFixture.Content);
        Zone.UnixMilliseconds = 1_800_000_000_000;
        Zone.NewEntityId = () => new EntityId(0x100_0000 + (ulong)_ids++);
    }

    public Zone Zone { get; }

    public GameContent C => ContentFixture.Content;

    /// <summary>A new player standing at a point of the zone's tangent plane (no kit: an empty inventory).</summary>
    public Entity Add(string name, float x, float z, bool kit = false)
    {
        Vector3 ground = Zone.GroundAt(x, z);
        Entity e = Zone.Players().AddPlayer(Zone.NextId(), name, Zone.NewCharacter(ground + Zone.Frame.UpAt(ground) * 0.02f));
        Player p = P(e);
        if (!kit)
        {
            p.Inventory.Clear();
        }
        p.Vitals.Health = 100;
        return e;
    }

    public Player P(Entity e) => Zone.Players().PlayerOf(e)!;

    /// <summary>An inventory action (GROUND_SYSTEMS.md §7) by its key in items.json's list: eat, use, wear, learn, unload, remove.</summary>
    public Outcome Act(Entity e, ContainerRef from, int slot, string action, int arg = 0, ushort expected = 0) =>
        Zone.Actions().ItemAction(e, from, slot, expected, C.ItemActions[action]!.Id, arg);

    public ref Character Ch(Entity e) => ref Zone.Entities.Get<Character>(e);

    /// <summary>Steps ticks; the clock follows (33 ms a tick).</summary>
    public void Step(int ticks = 1, Action? each = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            each?.Invoke();
            Zone.UnixMilliseconds += 1000 / CharacterMotor.TickRate;
            Zone.Step();
        }
    }

    /// <summary>Turns a character to look at a point.</summary>
    public void Look(Entity e, Vector3 at)
    {
        ref Character c = ref Ch(e);
        Vector3 eye = Zone.EyeOf(c.State);
        Vector3 up = Zone.Frame.UpAt(c.State.Position);
        Vector3 d = at - eye;
        Vector3 flat = d - up * Vector3.Dot(d, up);
        // Yaw about the zone's +Y: 0 faces −Z, growing anticlockwise from above.
        c.Input.Yaw = MathF.Atan2(-flat.X, -flat.Z);
        c.Input.Pitch = MathF.Atan2(Vector3.Dot(d, up), flat.Length());
    }

    public void Give(Entity e, string key, int count) => P(e).Inventory.Give(C, C.ItemByKey(key), count);

    /// <summary>
    /// Places what the item in a slot places on the ground at a point, turned by a yaw (GROUND_SYSTEMS.md §6): the intent a
    /// client sends for its ghost there, the server working it out again from the claim.
    /// </summary>
    public Outcome Place(Entity e, int slot, Vector3 ground, float yaw, ushort expected = 0, ContainerRef from = ContainerRef.Main)
    {
        Placeable? placeable = C.Item(P(e).Inventory[from][slot].Item)?.Places;
        return placeable == null
            ? OutcomeCode.NotUsable
            : Zone.Placement().Place(e, new PlaceRequest(from, slot, expected, placeable.Index, new PlacementClaim(TargetRef.Ground, 0, ground, yaw, 0)));
    }

    /// <summary>
    /// Places an item's placeable where the character's view meets it (its ghost on the zone's world, then the intent):
    /// a charge on a wall. Out of reach when the view meets nothing.
    /// </summary>
    public Outcome PlaceAimed(Entity e, ContainerRef from, int slot, string itemKey)
    {
        ItemDef item = C.ItemByKey(itemKey);
        ref Character ch = ref Ch(e);
        Vector3 direction = CharacterMotor.ViewDirection(ch.Input.Yaw, ch.Input.Pitch, Zone.Frame.UpAt(ch.State.Position));
        PlacementCandidate? ghost = Zone.Placement().Aim(e, item.Places!, direction);
        return ghost == null ? OutcomeCode.OutOfReach : Zone.Placement().Place(e, new PlaceRequest(from, slot, item.Id, item.Places!.Index, ghost.Claim));
    }

    public void Dispose() => Zone.Dispose();
}

public static class SurvivalTests
{
    /// <summary>
    /// GROUND.md §6, death: everything carried, worn and queued for crafting goes into a corpse where the player fell;
    /// anyone may take from it but not put in; emptied it goes, and otherwise it despawns on its timer. The dead player
    /// respawns on the beach with a rock and a torch, or at their own sleeping bag once it has cooled down.
    /// </summary>
    [Test]
    public static void DeathCorpseLootDespawnAndRespawn()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 20, 20);
        Entity bo = rig.Add("Bo", 21.2f, 20);
        rig.Give(ada, "wood", 600);
        rig.Give(ada, "stone", 300);
        rig.Give(ada, "woven-shirt", 1);
        rig.Give(ada, "sleeping-bag", 1);
        Player a = rig.P(ada);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().QuickMove(ada, ContainerRef.Main, 2, c.ItemByKey("woven-shirt").Id), "wear the shirt");
        Assert.True(!a.Inventory.Wear[0].IsEmpty, "worn");

        // A sleeping bag placed a little way off, then a craft started (its ingredients held by the queue).
        Vector3 bagAt = rig.Zone.GroundAt(19, 21.8f);
        rig.Look(ada, bagAt);
        Assert.Equal(OutcomeCode.Done, rig.Place(ada, 3, bagAt, 0.3f), "place the bag");
        Deployable bag = rig.Zone.Things().Statics().OfType<Deployable>().Single();
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().Craft(ada, c.ItemByKey("stone-hatchet").Id, 1, out _), "craft");
        Assert.Equal(400, a.Inventory.Count(c.ItemByKey("wood")), "the queue holds 200 wood");

        rig.Zone.Players().Hurt(ada, a, 1000, "Test", EntityId.None, "");
        Assert.True(a.Dead && rig.Ch(ada).State.IsDead, "dead");
        Assert.True(!a.Inventory.Stacks().Any() && a.Crafting.IsEmpty, "nothing left on the dead");
        Corpse corpse = rig.Zone.Things().Statics().OfType<Corpse>().Single();
        Assert.Equal(600, InventoryRules.Count(new[] { corpse.Loot }, c.ItemByKey("wood").Id), "the corpse has the wood and the queue's wood");
        Assert.Equal(300, InventoryRules.Count(new[] { corpse.Loot }, c.ItemByKey("stone").Id), "and the stone");
        Assert.Equal(1, InventoryRules.Count(new[] { corpse.Loot }, c.ItemByKey("woven-shirt").Id), "and the shirt");
        Assert.Equal(OutcomeCode.Dead, rig.Zone.Actions().MoveItem(ada, ContainerRef.Main, 0, ContainerRef.Main, 1, 0, 0), "the dead can't act");

        // Bo loots it: takes, can't put in.
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(bo, corpse.Ref, "loot"), "open the corpse");
        Assert.Equal(LootKind.Corpse, rig.Zone.Actions().LootKindOf(rig.P(bo)), "a corpse");
        int woodSlot = Enumerable.Range(0, corpse.Loot.Size).First(i => corpse.Loot[i].Item == c.ItemByKey("wood").Id);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().QuickMove(bo, ContainerRef.Loot, woodSlot, c.ItemByKey("wood").Id), "take the wood");
        Assert.Equal(OutcomeCode.NotAllowed, rig.Zone.Actions().MoveItem(bo, ContainerRef.Main, 0, ContainerRef.Loot, woodSlot, 10, 0), "no putting in");
        Assert.True(rig.P(bo).Inventory.Count(c.ItemByKey("wood")) > 0, "Bo has wood");

        // Its timer: gone when it runs out.
        corpse.DespawnTick = rig.Zone.Tick + 31;
        rig.Step(70);
        Assert.True(!rig.Zone.Things().Statics().OfType<Corpse>().Any(), "despawned on its timer");
        Assert.True(rig.P(bo).Looting.IsNone, "and the loot closed");

        // Respawn: the bag is still cooling down; the beach isn't.
        Assert.Equal(OutcomeCode.Cooldown, rig.Zone.Players().Respawn(ada, bag.Id), "the bag cools down for five minutes from placing");
        Assert.Equal(OutcomeCode.Dead, rig.Zone.Players().Respawn(bo, bag.Id), "Bo isn't dead, so doesn't respawn");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Players().Respawn(ada, EntityId.None), "the beach");
        Assert.True(!a.Dead && a.Vitals.Health == c.Survival.SpawnHealth, "alive again with the spawn vitals");
        Assert.True(a.Inventory.Count(c.ItemByKey("rock")) == 1 && a.Inventory.Count(c.ItemByKey("torch")) == 1, "a rock and a torch");

        // Die again; once the bag has cooled down, respawn at it.
        rig.Zone.Players().Hurt(ada, a, 1000, "Test", EntityId.None, "");
        rig.Zone.UnixMilliseconds = bag.ReadyAtUnixMs;
        Assert.Equal(OutcomeCode.Done, rig.Zone.Players().Respawn(ada, bag.Id), "at the bag");
        Assert.True(Vector3.Distance(rig.Ch(ada).State.Position, bag.Position) < 1.5f, "beside it");
        Assert.True(bag.ReadyAtUnixMs > rig.Zone.UnixMilliseconds, "and it cools down again");
    }

    /// <summary>
    /// GROUND.md §6, sleepers: a player who logs off stays in the world asleep, lootable (take only) and killable; a
    /// killed sleeper leaves a corpse; logging back in wakes them.
    /// </summary>
    [Test]
    public static void SleepersAreLootableAndKillable()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", -20, 20);
        Entity bo = rig.Add("Bo", -21, 20);
        rig.Give(ada, "metal-fragments", 300);
        rig.Give(ada, "bandage", 2);
        rig.Give(bo, "wood", 50);
        rig.Zone.Players().Sleep(ada);
        Player a = rig.P(ada);
        rig.Step(30);
        Assert.True(a.Sleeping && rig.Zone.Entities.IsAlive(ada), "asleep, still in the world");
        float calories = a.Vitals.Calories;
        rig.Step(60);
        Assert.Equal(calories, a.Vitals.Calories, "a sleeper doesn't get hungry");
        Assert.Equal(OutcomeCode.Dead, rig.Zone.Actions().Craft(ada, c.ItemByKey("rock").Id, 1, out _), "a sleeper doesn't act");

        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(bo, a.Ref, "loot"), "Bo loots the sleeper");
        Assert.Equal(LootKind.Sleeper, rig.Zone.Actions().LootKindOf(rig.P(bo)), "a sleeper");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().MoveItem(bo, ContainerRef.Loot, 0, ContainerRef.Main, 5, 100, c.ItemByKey("metal-fragments").Id), "takes some");
        Assert.Equal(200, a.Inventory.Count(c.ItemByKey("metal-fragments")), "taken from the sleeper");
        Assert.Equal(OutcomeCode.NotAllowed, rig.Zone.Actions().MoveItem(bo, ContainerRef.Main, 0, ContainerRef.Loot, 10, 0, 0), "but gives nothing");

        // Killed in their sleep: a corpse with what was left.
        rig.Zone.Combat().DamageCharacter(ada, a, 500, HitRegion.Head, projectile: false, 0, rig.P(bo), Vector3.Zero, 0);
        Assert.True(a.Dead, "a sleeper can be killed");
        Corpse corpse = rig.Zone.Things().Statics().OfType<Corpse>().Single();
        Assert.Equal(200, InventoryRules.Count(new[] { corpse.Loot }, c.ItemByKey("metal-fragments").Id), "the corpse has the rest");
        rig.Step(31);
        Assert.True(rig.P(bo).Looting.IsNone, "the sleeper's loot closed");

        // Another sleeper wakes when its player logs back in.
        Entity cy = rig.Add("Cy", -25, 22);
        rig.Zone.Players().Sleep(cy);
        rig.Zone.Players().Wake(cy);
        Assert.True(!rig.P(cy).Sleeping && rig.P(cy).IsAwakeAndAlive, "awake again");
    }

    /// <summary>
    /// ARCHITECTURE.md §11.5, a melee hit under lag compensation: the victim stepped aside after the attacker saw it.
    /// A swing aimed where the attacker saw it lands when the input says how far back that was, and misses without.
    /// </summary>
    [Test]
    public static void MeleeHitUnderLagCompensation()
    {
        foreach (byte viewDelay in new byte[] { 28, 0 })
        {
            using var rig = new GameRig();
            Entity ada = rig.Add("Ada", 30, 0);
            Entity bo = rig.Add("Bo", 30, -1.3f);
            rig.Give(ada, "stone-spear", 1);
            Player a = rig.P(ada), b = rig.P(bo);
            Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().MoveItem(ada, ContainerRef.Main, 0, ContainerRef.Belt, 0, 0, 0), "spear to the belt");
            rig.Step(15, () => rig.Ch(ada).Actions = new ActionInput { HeldSlot = 0 });
            Vector3 seen = rig.Ch(bo).State.Position + rig.Zone.Frame.UpAt(rig.Ch(bo).State.Position) * 1.15f;
            float health = b.Vitals.Health;
            int swingStart = 0;
            rig.Step(1, () =>
            {
                rig.Look(ada, seen);
                rig.Ch(ada).Actions = new ActionInput { HeldSlot = 0, Buttons = ActionButtons.Primary, ViewDelay = viewDelay };
            });
            swingStart = (int)rig.Zone.Tick;
            // Three ticks later Bo steps a metre east; the spear strikes ten ticks after the swing began (0.35 s), when
            // what Ada saw seven ticks earlier still had Bo in the old place.
            rig.Step(3, () => rig.Ch(ada).Actions = new ActionInput { HeldSlot = 0, ViewDelay = viewDelay });
            rig.Zone.Teleport(bo, rig.Zone.GroundAt(31, -1.3f) + rig.Zone.Frame.UpAt(rig.Ch(bo).State.Position) * 0.02f);
            rig.Step(10, () =>
            {
                rig.Look(ada, seen);
                rig.Ch(ada).Actions = new ActionInput { HeldSlot = 0, ViewDelay = viewDelay };
            });
            float taken = health - b.Vitals.Health;
            List<HitConfirmed> hits = rig.Zone.Events.Of<HitConfirmed>().ToList();
            Fixtures.Report($"view delay {viewDelay / 4.0:0.0} ticks: Bo took {taken:0} damage; swing began at tick {swingStart}");
            if (viewDelay > 0)
            {
                Assert.True(taken >= 39 && b.Vitals.Bleeding > 0, $"lands where Ada saw Bo, and bleeds ({taken})");
            }
            else
            {
                Assert.Equal(0f, taken, "without compensation it misses");
            }
        }
    }

    /// <summary>
    /// GROUND.md §6, the bow: drawn for a second, an arrow flies at 50 m/s and drops under the planet's gravity; aimed
    /// a little high it hits a player 20 m away after about 0.4 s; aimed flat from eye height it falls to the ground
    /// about 30 m out, where it may be picked up again.
    /// </summary>
    [Test]
    public static void ArrowFlight()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", -40, 40);
        Entity bo = rig.Add("Bo", -40, 20);
        rig.Give(ada, "bow", 1);
        rig.Give(ada, "arrow", 10);
        rig.Zone.Actions().MoveItem(ada, ContainerRef.Main, 0, ContainerRef.Belt, 0, 0, 0);
        Player b = rig.P(bo);
        float g = (float)(rig.Zone.Spec.SurfaceGravity * 9.80665);

        // Aim at Bo's chest, raised by the drop over the flight.
        Vector3 chest = rig.Ch(bo).State.Position + rig.Zone.Frame.UpAt(rig.Ch(bo).State.Position) * 1.2f;
        Vector3 eye = rig.Zone.EyeOf(rig.Ch(ada).State);
        float flight = Vector3.Distance(eye, chest) / 50f;
        Vector3 aim = chest + rig.Zone.Frame.UpAt(chest) * (0.5f * g * flight * flight);
        void Hold(bool loose, Vector3 at)
        {
            rig.Look(ada, at);
            rig.Ch(ada).Actions = new ActionInput { HeldSlot = 0, Buttons = ActionButtons.Secondary | (loose ? ActionButtons.Primary | ActionButtons.PrimaryPressed : 0) };
        }
        rig.Step(45, () => Hold(false, aim));
        rig.Zone.Events.Clear();
        rig.Step(1, () => Hold(true, aim));
        long launched = rig.Zone.Tick;
        Assert.True(rig.Zone.Events.Of<ProjectileLaunched>().Count > 0, "loosed");
        Assert.Equal(9, rig.P(ada).Inventory.Count(c.ItemByKey("arrow")), "one arrow used");
        float health = b.Vitals.Health;
        long hitTick = 0;
        rig.Step(30, () =>
        {
            Hold(false, aim);
            if (hitTick == 0 && rig.Zone.Events.Of<Hurt>().Count > 0)
            {
                hitTick = rig.Zone.Tick;
            }
        });
        // The arrow flies its first tick in the tick it is loosed.
        float ticks = hitTick - launched + 1;
        Fixtures.Report($"20 m: hit after {ticks} ticks ({ticks / 30f:0.00} s, {flight:0.00} s expected), {health - b.Vitals.Health:0} damage");
        Assert.True(health - b.Vitals.Health >= 40, "an arrow in the body");
        Assert.InRange(flight * 30 - 1, flight * 30 + 2, ticks, "the flight takes its time");

        // Flat from the eye: it comes down about 30 m out (the ground is gently hilly).
        rig.Zone.Events.Clear();
        Vector3 flat = rig.Zone.EyeOf(rig.Ch(ada).State) + new Vector3(1, 0, 0) * 30;
        rig.Step(35, () => Hold(false, flat));
        rig.Step(1, () => Hold(true, flat));
        ProjectileEnded? landed = null;
        rig.Step(40, () =>
        {
            Hold(false, flat);
            landed ??= rig.Zone.Events.Of<ProjectileEnded>().Select(e => (ProjectileEnded?)e).FirstOrDefault();
        });
        Assert.True(landed?.Target.Family == TargetFamily.Ground, "it came down on the ground");
        float distance = Vector3.Distance(landed!.Value.Position, rig.Ch(ada).State.Position);
        float expected = 50 * MathF.Sqrt(2 * 1.7f / g);
        Fixtures.Report($"flat: landed {distance:0.0} m out (flat ground would be {expected:0.0} m)");
        Assert.InRange(expected * 0.6, expected * 1.5, distance, "where gravity puts it");
    }

    /// <summary>
    /// GROUND.md §6, survival: needs go down (faster sprinting), bleeding drains health until it clots or a bandage
    /// stops it, starvation hurts, the cold hurts unless clothing and a fire keep it off, and food heals over time.
    /// </summary>
    [Test]
    public static void SurvivalNeedsBleedingColdAndHealing()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        SurvivalTuning s = c.Survival;
        Entity ada = rig.Add("Ada", 10, -30);
        Player a = rig.P(ada);
        rig.Step(30 * 10);
        Assert.Near(s.SpawnCalories - 10 * s.CaloriesPerSecond, a.Vitals.Calories, 0.01, "ten seconds of calories");
        Assert.Near(s.SpawnHydration - 10 * s.HydrationPerSecond, a.Vitals.Hydration, 0.01, "and of water");

        // Bleeding: a pool of health lost a point a second, clotting slowly; a bandage stops it at once.
        a.Vitals.Health = 80;
        a.Vitals.Bleeding = 6;
        rig.Step(30 * 3);
        Assert.True(a.Vitals.Health < 78 && a.Vitals.Bleeding > 0, $"bleeding ({a.Vitals.Health:0.0} health, {a.Vitals.Bleeding:0.0} to bleed)");
        rig.Give(ada, "bandage", 1);
        Assert.Equal(OutcomeCode.Done, rig.Act(ada, ContainerRef.Main, 0, "use", expected: c.ItemByKey("bandage").Id), "bandage");
        Assert.Equal(0f, a.Vitals.Bleeding, "stopped");
        float afterBandage = a.Vitals.Health;
        rig.Step(30 * 5);
        Assert.True(a.Vitals.Health > afterBandage + 4, "and it heals over time");

        // Starving hurts.
        a.Vitals.Calories = 0;
        a.Vitals.PendingHeal = 0;
        float h0 = a.Vitals.Health;
        rig.Step(30 * 5);
        Assert.True(a.Vitals.Starving && a.Vitals.Health < h0 - 0.5f, "starvation hurts");
        a.Vitals.Calories = 300;

        // A cold night: freezing without clothes, less so with them, comfortable by a lit campfire.
        rig.Zone.Climate = new FixedClimate(-25);
        rig.Step(30 * 2);
        float bare = a.Vitals.Cold;
        Assert.True(bare >= s.ColdDamageFrom, $"freezing bare ({a.Vitals.Temperature:0} °C)");
        foreach (string key in new[] { "woven-hood", "woven-shirt", "woven-trousers", "woven-gloves", "work-boots" })
        {
            rig.Give(ada, key, 1);
        }
        for (int i = 0; i < 5; i++)
        {
            int slot = Enumerable.Range(0, 24).First(k => c.Item(a.Inventory.Main[k].Item)?.Wear != null);
            Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().QuickMove(ada, ContainerRef.Main, slot, 0), "dress");
        }
        rig.Step(30 * 2);
        Assert.True(a.Vitals.Cold < bare, "clothing helps");
        rig.Give(ada, "campfire", 1);
        rig.Give(ada, "wood", 20);
        Vector3 fireAt = rig.Zone.GroundAt(11.5f, -31.5f);
        int fire = Enumerable.Range(0, 24).First(k => a.Inventory.Main[k].Item == c.ItemByKey("campfire").Id);
        Assert.Equal(OutcomeCode.Done, rig.Place(ada, fire, fireAt, 0), "a campfire");
        Deployable campfire = rig.Zone.Things().Statics().OfType<Deployable>().Single();
        Assert.Equal(OutcomeCode.NoFuel, rig.Zone.Interaction().Interact(ada, campfire.Ref, "light"), "no fuel, no fire");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(ada, campfire.Ref, "open"), "open it");
        int wood = Enumerable.Range(0, 24).First(k => a.Inventory.Main[k].Item == c.ItemByKey("wood").Id);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().QuickMove(ada, ContainerRef.Main, wood, 0), "wood in");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(ada, campfire.Ref, "light"), "lit");
        rig.Step(30 * 2);
        Assert.True(a.Vitals.Comfort && a.Vitals.Cold == 0, $"warm by the fire ({a.Vitals.Temperature:0} °C)");
        Fixtures.Report($"-25 °C air: bare {bare:0.00} cold, dressed {a.Vitals.Cold:0.00} by the fire at {a.Vitals.Temperature:0} °C felt");
    }

    /// <summary>
    /// GROUND.md §6, deployables: placement within reach, on gentle ground, overlapping nothing; a furnace burns wood
    /// into charcoal and smelts ore; a campfire roasts tubers; boxes store; anything can be broken.
    /// </summary>
    [Test]
    public static void DeployablesPlacementAndDevices()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 50, 50);
        Player a = rig.P(ada);
        rig.Give(ada, "furnace", 1);
        rig.Give(ada, "small-box", 1);
        rig.Give(ada, "wood", 40);
        rig.Give(ada, "metal-ore", 10);
        Vector3 eye = rig.Zone.EyeOf(rig.Ch(ada).State);
        Vector3 there = rig.Zone.GroundAt(52, 50);
        Assert.Equal(OutcomeCode.OutOfReach, rig.Place(ada, 0, rig.Zone.GroundAt(58, 50), 0), "too far");
        Assert.Equal(OutcomeCode.BadPlacement, rig.Place(ada, 0, there + rig.Zone.Frame.UpAt(there) * 1.5f, 0), "in the air");
        Assert.Equal(OutcomeCode.PlayerInTheWay, rig.Place(ada, 0, rig.Zone.GroundAt(50.3f, 50), 0), "where Ada stands");
        // A furnace whose rule allows no slope at all: the ground here isn't that flat.
        Placeable furnacePlaceable = c.ItemByKey("furnace").Places!;
        var steep = new Placeable
        {
            Index = furnacePlaceable.Index, Key = "test", Kind = furnacePlaceable.Kind, Definition = furnacePlaceable.Definition,
            Item = furnacePlaceable.Item, Footprint = furnacePlaceable.Footprint, Rule = new PlacementRule { MaxSlopeDegrees = 0.001f },
        };
        Assert.Equal(OutcomeCode.TooSteep, rig.Zone.Placement().Aim(ada, steep, there - eye)?.Outcome.Code, "steeper than allowed");
        Assert.Equal(OutcomeCode.Done, rig.Place(ada, 0, there, 0, c.ItemByKey("furnace").Id), "the furnace");
        Assert.Equal(OutcomeCode.Blocked, rig.Place(ada, 1, rig.Zone.GroundAt(52.75f, 50), 0), "against the furnace");
        Deployable furnace = rig.Zone.Things().Statics().OfType<Deployable>().Single();
        Assert.True(rig.Zone.Physics.HasProp(Hollow.Simulation.Physics.PropLayer.Deployable, furnace.Id.Value), "solid");

        // Smelting: wood to the fuel slot, ore to the inputs (quick move sorts them), light it.
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(ada, furnace.Ref, "open"), "open the furnace");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().QuickMove(ada, ContainerRef.Main, 2, 0), "wood in");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().QuickMove(ada, ContainerRef.Main, 3, 0), "ore in");
        Assert.True(furnace.Storage![0].Item == c.ItemByKey("wood").Id && furnace.Storage[1].Item == c.ItemByKey("metal-ore").Id, "sorted into their slots");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(ada, furnace.Ref, "light"), "lit");
        rig.Step(30 * 9);
        int fragments = InventoryRules.Count(new[] { furnace.Storage }, c.ItemByKey("metal-fragments").Id);
        int charcoal = InventoryRules.Count(new[] { furnace.Storage }, c.ItemByKey("charcoal").Id);
        Fixtures.Report($"nine seconds of furnace: {fragments} metal fragments, {charcoal} charcoal");
        Assert.True(fragments == 2 && charcoal >= 4, "an ore every 4 s, a charcoal per 2 s of wood");
        Assert.True(furnace.Lit, "still burning");

        // Breaking it spills what it held, in one bag where it stood.
        rig.Zone.Things().DamageDeployable(furnace, 10_000);
        DroppedBag spilled = rig.Zone.Things().Statics().OfType<DroppedBag>().Single();
        Assert.True(!rig.Zone.Things().Statics().OfType<Deployable>().Any() && spilled.ShownCount >= 3, "broken, its contents in one bag on the ground");
        Assert.True(!rig.Zone.Physics.HasProp(Hollow.Simulation.Physics.PropLayer.Deployable, furnace.Id.Value), "no longer solid");

        // The bag: picked up again, or gone after its time.
        rig.Zone.Teleport(ada, spilled.Position + spilled.Up * 0.05f);
        int before = a.Inventory.Stacks().Sum(s => s.Count);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(ada, spilled.Ref, "pick-up"), "picked up");
        Assert.True(a.Inventory.Stacks().Sum(s => s.Count) > before && rig.Zone.Things().StaticOf(spilled.Id) == null, "in the inventory, gone from the ground");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().DropItem(ada, ContainerRef.Main, 1, 0, 0), "dropped");
        DroppedBag mine = rig.Zone.Things().Statics().OfType<DroppedBag>().Single();
        Assert.Near(0, rig.Zone.HeightAboveGround(mine.Position), 0.05, "settled on the ground");
        mine.DespawnTick = rig.Zone.Tick + 5;
        rig.Step(31);
        Assert.True(rig.Zone.Things().StaticOf(mine.Id) == null, "and gone after its time");
    }

    /// <summary>
    /// GROUND.md §6.1, bags (decisions U1–U3): whatever lands on the ground within 1.5 m of a bag with room goes into it
    /// (a drop, a craft's or a refund's overflow, a spill), onto stacks of the same item first; farther off it starts a
    /// bag of its own, as does what a full bag can't take. A bag lasts its time from the newest item put in.
    /// </summary>
    [Test]
    public static void DroppedItemsArePackedIntoBags()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 30, 30);
        rig.Give(ada, "wood", 900);
        rig.Give(ada, "stone", 200);
        rig.Give(ada, "bandage", 3);

        // Three drops from where she stands, turning between them: one bag, the stone's two drops in one stack.
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().DropItem(ada, ContainerRef.Main, 1, 120, 0), "stone");
        rig.Ch(ada).Input.Yaw += 1.2f;
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().DropItem(ada, ContainerRef.Main, 1, 80, 0), "more stone, facing elsewhere");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().DropItem(ada, ContainerRef.Main, 2, 0, 0), "the bandages");
        DroppedBag bag = rig.Zone.Things().Statics().OfType<DroppedBag>().Single();
        Assert.Equal(2, bag.ShownCount, "one bag of two stacks");
        Assert.Equal(200, InventoryRules.Count(new[] { bag.Loot }, c.ItemByKey("stone").Id), "the stone together");
        Assert.True(bag.Look == 0, "others see a bag, not what is in it");

        // Later drops start its time again; a refund's overflow (here: given straight to the ground) joins it too.
        rig.Step(30 * 10);
        long before = bag.DespawnTick;
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().DropItem(ada, ContainerRef.Main, 0, 100, 0), "wood");
        Assert.True(bag.DespawnTick >= before + 30 * 10 - 1, "its time runs from the newest item");
        rig.Zone.Spill().Drop(ada, new List<ItemStack> { ItemStack.Of(c.ItemByKey("rope"), 3), ItemStack.Of(c.ItemByKey("cloth"), 2) });
        Assert.Equal(5, bag.ShownCount, "what doesn't fit goes into the same bag");

        // Two metres on, a bag of its own; and what a full bag can't take starts another beside it.
        rig.Zone.Teleport(ada, rig.Zone.GroundAt(30, 32.5f));
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().DropItem(ada, ContainerRef.Main, 0, 100, 0), "wood farther off");
        Assert.Equal(2, rig.Zone.Things().Statics().OfType<DroppedBag>().Count(), "a second bag");
        List<ItemDef> others = c.Items.Where(i => InventoryRules.Count(new[] { bag.Loot }, i.Id) == 0).Take(c.Survival.BagSlots).ToList();
        foreach (ItemDef item in others)
        {
            rig.Zone.Spill().Spill(bag.Position, ItemStack.Of(item, 1));
        }
        Fixtures.Report($"{rig.Zone.Things().Statics().OfType<DroppedBag>().Count()} bags after spilling {c.Survival.BagSlots} more kinds by the first");
        Assert.True(bag.ShownCount == c.Survival.BagSlots && rig.Zone.Things().Statics().OfType<DroppedBag>().Count() == 3, "full, the rest in a third bag");
    }

    /// <summary>
    /// GROUND.md §6.1: the interact key takes a bag of one stack whole; a bag holding more opens as take-only loot (like
    /// a crate), and once its last stack is taken it goes at once, closing the looter's panel.
    /// </summary>
    [Test]
    public static void ABagIsTakenWholeOrOpened()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 40, 30);
        Entity bo = rig.Add("Bo", 41.5f, 30);
        rig.Give(ada, "wood", 300);
        rig.Give(ada, "stone", 100);
        rig.Give(bo, "metal-ore", 10);

        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().DropItem(ada, ContainerRef.Main, 0, 0, 0), "the wood");
        DroppedBag one = rig.Zone.Things().Statics().OfType<DroppedBag>().Single();
        rig.Look(bo, one.Position);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(bo, one.Ref, "pick-up"), "one stack: taken whole");
        Assert.True(rig.P(bo).Inventory.Count(c.ItemByKey("wood")) == 300 && rig.Zone.Things().StaticOf(one.Id) == null, "Bo has it, the bag is gone");

        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().DropItem(ada, ContainerRef.Main, 1, 60, 0), "stone");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().DropItem(ada, ContainerRef.Main, 1, 0, 0), "and the rest of it");
        Player a = rig.P(ada);
        rig.Give(ada, "bandage", 2);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().DropItem(ada, ContainerRef.Main, 0, 0, 0), "bandages");
        DroppedBag two = rig.Zone.Things().Statics().OfType<DroppedBag>().Single();
        Assert.Equal(2, two.ShownCount, "stone and bandages");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(bo, two.Ref, "open"), "opened like a box");
        Assert.Equal(LootKind.Crate, rig.Zone.Actions().LootKindOf(rig.P(bo)), "take only, gone once empty");
        Assert.Equal(OutcomeCode.NotAllowed, rig.Zone.Actions().MoveItem(bo, ContainerRef.Main, 1, ContainerRef.Loot, 5, 0, 0), "nothing goes in");
        int stone = Enumerable.Range(0, two.Loot.Size).First(i => two.Loot[i].Item == c.ItemByKey("stone").Id);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().QuickMove(bo, ContainerRef.Loot, stone, 0), "take the stone");
        Assert.True(rig.Zone.Things().StaticOf(two.Id) != null, "still a bag with the bandages");
        int bandages = Enumerable.Range(0, two.Loot.Size).First(i => !two.Loot[i].IsEmpty);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().QuickMove(bo, ContainerRef.Loot, bandages, 0), "take the bandages");
        Assert.True(rig.Zone.Things().StaticOf(two.Id) == null && rig.P(bo).Looting.IsNone, "emptied: gone at once, the panel closed");
        Assert.True(rig.P(bo).Inventory.Count(c.ItemByKey("stone")) == 100 && a.Inventory.Count(c.ItemByKey("stone")) == 0, "Bo has the stone");
    }

    private sealed class FixedClimate(float celsius) : IClimate
    {
        public float TemperatureAt(Vector3 position, double unixSeconds) => celsius;
    }
}
