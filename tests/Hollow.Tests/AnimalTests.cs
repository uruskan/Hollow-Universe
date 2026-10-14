using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>Animals in a <see cref="GameRig"/>: the grid built on the test's thread, herds placed by hand.</summary>
public static class AnimalRig
{
    public static AnimalWorld Animals(GameRig rig)
    {
        AnimalWorld animals = rig.Zone.Animals().World!;
        animals.Nav.Synchronous = true;
        return animals;
    }

    public static AnimalSpecies Species(GameRig rig, string key) => rig.C.Animal(key)!;

    public static Herd Herd(GameRig rig, string species, float x, float z, int count, float range = 60) =>
        Animals(rig).SpawnHerd(Species(rig, species), rig.Zone.GroundAt(x, z), count, range);

    public static IEnumerable<Animal> Alive(Herd herd) => herd.Members.Where(a => !a.IsDead && !a.Removed);

    /// <summary>A player stands still where they are (no input), facing a point.</summary>
    public static void Still(GameRig rig, Entity e) => rig.Ch(e).Input = new MoveInput { View = rig.Ch(e).Input.View };

    /// <summary>The distance between two points along the ground: the arc between their directions from the world's centre.</summary>
    public static float Flat(Vector3 a, Vector3 b)
    {
        float la = a.Length(), lb = b.Length();
        return 2 * MathF.Asin(Math.Min(1, (a / la - b / lb).Length() / 2)) * (la + lb) / 2;
    }
}

public static class AnimalTests
{
    /// <summary>
    /// GROUND.md §6.9, flight: a deer herd grazes 50 m from a player who stands still; a rifle shot hits one of them,
    /// which flinches, and the whole herd runs from the shooter (one runs, all run), getting farther away while it runs.
    /// </summary>
    [Test]
    public static void DeerFleeWhenShot()
    {
        using var rig = new GameRig();
        AnimalWorld animals = AnimalRig.Animals(rig);
        Entity ada = rig.Add("Ada", 0, 0);
        Herd herd = AnimalRig.Herd(rig, "deer", 0, -52, 4);
        rig.Step(60, () => AnimalRig.Still(rig, ada));
        Assert.True(AnimalRig.Alive(herd).All(a => a.State != AnimalState.Flee), "calm while she stands still 50 m off: " +
            string.Join(", ", herd.Members.Select(a => a.State)));

        Guns.Arm(rig, ada, "rifle", "rifle-ammo", 30);
        Animal target = herd.Leader!;
        rig.Look(ada, target.Position + rig.Zone.Frame.UpAt(target.Position) * 0.95f);
        rig.Zone.Events.Clear();
        Assert.Equal(OutcomeCode.Done, Guns.Queue(rig, ada, 1), "the shot");
        float[] before = herd.Members.Select(a => AnimalRig.Flat(a.Position, rig.Ch(ada).State.Position)).ToArray();
        bool hit = false;
        rig.Step(15, () =>
        {
            hit |= rig.Zone.Events.Of<HitConfirmed>().Any(e => e.Target.Family == TargetFamily.Animal);
        });
        Assert.True(hit && target.Health < target.Species.Health && target.HurtCount == 1, $"the deer was hit ({target.Health:0} health left)");
        Assert.True(AnimalRig.Alive(herd).All(a => a.State == AnimalState.Flee), "the whole herd runs: " + string.Join(", ", herd.Members.Select(a => a.State)));
        rig.Step(75);
        float[] after = herd.Members.Select(a => AnimalRig.Flat(a.Position, rig.Ch(ada).State.Position)).ToArray();
        Fixtures.Report("distances before and 2.5 s after the shot: " + string.Join(", ", before.Zip(after, (b, a) => $"{b:0} → {a:0} m")));
        for (int i = 0; i < after.Length; i++)
        {
            Assert.True(after[i] > before[i] + 12, $"deer {i} ran away ({before[i]:0} → {after[i]:0} m)");
        }
    }

    /// <summary>
    /// GROUND.md §6.9, a boar charges when hurt: it ignores a player 12 m off, turns on her when an arrow hits it, runs
    /// her down and gores her (the hit lands at its wind-up's end); when she gets far away it gives up.
    /// </summary>
    [Test]
    public static void BoarChargesWhenHurt()
    {
        using var rig = new GameRig();
        AnimalWorld animals = AnimalRig.Animals(rig);
        Entity ada = rig.Add("Ada", 0, 0);
        Player p = rig.P(ada);
        Herd herd = AnimalRig.Herd(rig, "boar", 0, -12, 1);
        Animal boar = herd.Leader!;
        rig.Step(45, () => AnimalRig.Still(rig, ada));
        Assert.True(boar.State is not (AnimalState.Chase or AnimalState.Attack or AnimalState.Flee), $"a boar 12 m off leaves her be ({boar.State})");

        rig.Zone.Animals().DamageAnimal(boar, 20, HitRegion.Body, rig.Zone.EyeOf(rig.Ch(ada).State), p, p.Id, p.Name, rig.C.ItemByKey("arrow").Id);
        rig.Step(3, () => AnimalRig.Still(rig, ada));
        Assert.True(boar.State == AnimalState.Chase && boar.Target == AnimalTarget.Player(p.Id), $"it charges her ({boar.State})");
        float health = p.Vitals.Health;
        long hitAt = 0;
        rig.Step(150, () =>
        {
            AnimalRig.Still(rig, ada);
            if (hitAt == 0 && p.Vitals.Health < health)
            {
                hitAt = rig.Zone.Tick;
            }
        });
        Assert.True(boar.AttackCount >= 1 && health - p.Vitals.Health >= 15.9f, $"gored ({boar.AttackCount} attacks, {health - p.Vitals.Health:0.0} damage)");

        // She leaves: 70 m away at once; the boar gives up within its chase time.
        rig.Zone.Teleport(ada, rig.Zone.GroundAt(0, 70) + rig.Zone.Frame.UpAt(rig.Zone.GroundAt(0, 70)) * 0.02f);
        rig.Step(30 * 14, () => AnimalRig.Still(rig, ada));
        Assert.True(boar.State is not (AnimalState.Chase or AnimalState.Attack), $"it gave up ({boar.State})");
        Fixtures.Report($"the boar hit her {(hitAt - 0) / 30.0:0.0} s into the run, {boar.AttackCount} attacks in all");
    }

    /// <summary>
    /// GROUND.md §6.9, wolves: a fed pack 25 m away watches a player and leaves her be; hungry, it hunts her together,
    /// at most two biting at once while the rest circle; a fed wolf she walks up to growls first (the warning), then bites.
    /// </summary>
    [Test]
    public static void WolvesHuntWhenHungryAndBiteWhenProvoked()
    {
        using var rig = new GameRig();
        AnimalWorld animals = AnimalRig.Animals(rig);
        Entity ada = rig.Add("Ada", 0, 0);
        Player p = rig.P(ada);
        Herd pack = AnimalRig.Herd(rig, "wolf", 0, -25, 4);
        foreach (Animal w in pack.Members)
        {
            w.Hunger = 0;
        }
        rig.Step(90, () => AnimalRig.Still(rig, ada));
        Assert.True(pack.Members.All(w => w.State is not (AnimalState.Chase or AnimalState.Attack)), "fed, they don't hunt: " +
            string.Join(", ", pack.Members.Select(w => w.State)));

        foreach (Animal w in pack.Members)
        {
            w.Hunger = 0.9f;
        }
        int mostBiting = 0;
        float health = p.Vitals.Health = 100;
        rig.Step(60, () =>
        {
            AnimalRig.Still(rig, ada);
            mostBiting = Math.Max(mostBiting, pack.Members.Count(w => w.State == AnimalState.Attack));
        });
        Assert.True(pack.Members.All(w => w.State is AnimalState.Chase or AnimalState.Attack), "hungry, the pack hunts her: " +
            string.Join(", ", pack.Members.Select(w => w.State)));
        Assert.True(pack.Members.All(w => w.Target == AnimalTarget.Player(p.Id)), "all after her");
        rig.Step(120, () =>
        {
            AnimalRig.Still(rig, ada);
            mostBiting = Math.Max(mostBiting, pack.Members.Count(w => w.State == AnimalState.Attack));
        });
        Fixtures.Report($"a hunting pack of four: at most {mostBiting} biting at once; {health - p.Vitals.Health:0} damage in 6 s " +
            $"({(p.Dead ? "dead" : p.Wounded ? "down" : "standing")})");
        Assert.True(mostBiting is >= 1 and <= AnimalWorld.PackAttackers, $"they take turns ({mostBiting} at once)");
        Assert.True(health - p.Vitals.Health > 20 || p.Wounded || p.Dead, "and they bite");

        // A fed wolf she walks up to: a warning, then a bite.
        using var rig2 = new GameRig();
        AnimalRig.Animals(rig2);
        Entity bo = rig2.Add("Bo", 0, 0);
        Herd lone = AnimalRig.Herd(rig2, "wolf", 0, -12, 1);
        Animal wolf = lone.Leader!;
        wolf.Hunger = 0;
        rig2.Step(60, () => AnimalRig.Still(rig2, bo));
        Assert.True(wolf.State is not (AnimalState.Chase or AnimalState.Attack), $"fed and 12 m off: no bite ({wolf.State})");
        rig2.Zone.Teleport(bo, rig2.Zone.Frame.Beside(wolf.Position, 4.5f, 0.3f, 0));
        AnimalState seen = AnimalState.Idle;
        long warned = -1, charged = -1;
        rig2.Step(90, () =>
        {
            AnimalRig.Still(rig2, bo);
            wolf.Hunger = 0;
            if (wolf.State == AnimalState.Alert && warned < 0)
            {
                warned = rig2.Zone.Tick;
            }
            if (wolf.State is AnimalState.Chase or AnimalState.Attack && charged < 0)
            {
                charged = rig2.Zone.Tick;
            }
            seen = wolf.State;
        });
        Assert.True(warned >= 0 && charged > warned, $"warned at {warned}, charged at {charged} (now {seen})");
        Assert.True((charged - warned) / 30.0 >= AnimalWorld.WarningSeconds - 0.15, $"the warning lasted {(charged - warned) / 30.0:0.00} s");
    }

    /// <summary>
    /// GROUND.md §6.9, herds and packs keep together: a deer herd of five and a wolf pack of four wander for two minutes
    /// with nobody near enough to disturb them; every member stays within reach of its herd and none stand inside another.
    /// </summary>
    [Test]
    public static void HerdsAndPacksStayTogether()
    {
        using var rig = new GameRig();
        AnimalWorld animals = AnimalRig.Animals(rig);
        Entity watcher = rig.Add("Watcher", 0, 0);
        Herd deer = AnimalRig.Herd(rig, "deer", 160, 0, 5, 70);
        Herd wolves = AnimalRig.Herd(rig, "wolf", -160, 0, 4, 70);
        foreach (Animal w in wolves.Members)
        {
            w.Hunger = 0;
        }
        float worstDeer = 0, worstWolf = 0, closest = float.MaxValue;
        string closestWhat = "";
        double travelled = 0;
        Vector3 lastCentre = deer.Centre();
        for (int second = 0; second < 120; second++)
        {
            rig.Step(30, () =>
            {
                AnimalRig.Still(rig, watcher);
                foreach (Animal w in wolves.Members)
                {
                    w.Hunger = 0;
                }
            });
            if (second < 10)
            {
                continue;
            }
            foreach ((Herd herd, bool isDeer) in new[] { (deer, true), (wolves, false) })
            {
                Vector3 centre = herd.Centre();
                foreach (Animal a in herd.Members)
                {
                    float apart = AnimalRig.Flat(a.Position, centre);
                    if (isDeer)
                    {
                        worstDeer = MathF.Max(worstDeer, apart);
                    }
                    else
                    {
                        worstWolf = MathF.Max(worstWolf, apart);
                    }
                    foreach (Animal b in herd.Members.Where(b => b != a))
                    {
                        float gap = AnimalRig.Flat(a.Position, b.Position);
                        if (gap < closest)
                        {
                            closest = gap;
                            closestWhat = $"{a.Species.Key} {a.Slot} {a.State} {a.Speed:0.0} / {b.Slot} {b.State} {b.Speed:0.0} at {second} s";
                        }
                    }
                }
            }
            travelled += AnimalRig.Flat(deer.Centre(), lastCentre);
            lastCentre = deer.Centre();
        }
        AnimalSpecies d = AnimalRig.Species(rig, "deer"), w2 = AnimalRig.Species(rig, "wolf");
        Fixtures.Report($"two minutes: deer at most {worstDeer:0.0} m from their herd's middle (cohesion {d.Cohesion}), wolves {worstWolf:0.0} m " +
            $"({w2.Cohesion}); closest two {closest:0.00} m apart ({closestWhat}); the herd walked {travelled:0} m");
        Assert.True(worstDeer <= d.Cohesion * 1.6f, "the herd keeps together");
        Assert.True(worstWolf <= w2.Cohesion * 1.6f, "the pack keeps together");
        Assert.True(closest > 0.25f, "nobody stands inside another");
        Assert.True(travelled > 20, "and they move about");
    }

    /// <summary>
    /// GROUND.md §6.9, harvest: a dead deer gives exactly its yield (4 raw venison, 50 hide, 50 bone fragments, 10 animal
    /// fat) over as many hits as its harvest health and the tool's rate need: 7 with a bone knife, 25 with a rock; then
    /// the corpse is gone. A corpse left alone goes after its timer.
    /// </summary>
    [Test]
    public static void HarvestYields()
    {
        foreach ((string tool, int hits) in new[] { ("bone-knife", 7), ("rock", 25) })
        {
            using var rig = new GameRig();
            GameContent c = rig.C;
            AnimalWorld animals = AnimalRig.Animals(rig);
            Entity ada = rig.Add("Ada", 0, 0);
            Player p = rig.P(ada);
            Herd herd = AnimalRig.Herd(rig, "deer", 1.4f, 0, 1);
            Animal deer = herd.Leader!;
            rig.Step(2);
            rig.Zone.Animals().DamageAnimal(deer, 1000, HitRegion.Body, rig.Ch(ada).State.Position, p, p.Id, p.Name);
            Assert.True(deer.IsDead && deer.HarvestLeft == deer.Species.HarvestHealth, "a corpse to harvest");
            rig.Give(ada, tool, 1);
            rig.Zone.Actions().MoveItem(ada, ContainerRef.Main, 0, ContainerRef.Belt, 0, 0, 0);
            rig.Zone.Events.Clear();
            int count = 0;
            Vector3 aim = deer.Position + rig.Zone.Frame.UpAt(deer.Position) * 0.25f;
            rig.Step(30 * 30, () =>
            {
                ref Character ch = ref rig.Ch(ada);
                ch.Input = new MoveInput { Buttons = MoveButtons.Crouch };
                rig.Look(ada, aim);
                ch.Actions = new ActionInput { HeldSlot = 0, Buttons = deer.Removed ? ActionButtons.None : ActionButtons.Primary };
                count += rig.Zone.Events.Of<HitConfirmed>().Count(e => e.Target.Family == TargetFamily.Animal && e.By == p.Id);
                rig.Zone.Events.Clear();
            });
            string gained = string.Join(", ", deer.Species.Harvest.Select(y => $"{p.Inventory.Count(y.Item)} {y.Item.Key}"));
            Fixtures.Report($"{tool}: {count} hits, {gained}");
            Assert.True(deer.Removed, $"{tool}: the corpse is used up");
            Assert.Equal(hits, count, $"{tool}: hits");
            foreach (ItemAmount y in deer.Species.Harvest)
            {
                Assert.Equal(y.Count, p.Inventory.Count(y.Item), $"{tool}: {y.Item.Key}");
            }
        }

        // Left alone, a corpse goes after its five minutes.
        using var rig3 = new GameRig();
        AnimalWorld world = AnimalRig.Animals(rig3);
        rig3.Add("Ada", 0, 0);
        Animal wolf = AnimalRig.Herd(rig3, "wolf", 0, -20, 1).Leader!;
        rig3.Step(2);
        rig3.Zone.Animals().DamageAnimal(wolf, 500, HitRegion.Head, rig3.Zone.GroundAt(0, 0), null);
        Assert.Equal(rig3.Zone.Tick + 300 * 30, wolf.DespawnTick, "the corpse's timer");
        wolf.DespawnTick = rig3.Zone.Tick + 5;
        rig3.Step(40); // the lifetime step runs once a second (GROUND_SYSTEMS.md §3.7)
        Assert.True(wolf.Removed && world.Find(wolf.Id) == null, "gone with its timer");
    }

    /// <summary>
    /// GROUND.md §6.9, cooking: raw venison on a lit campfire is cooked venison 15 s later; left on the fire, cooked meat
    /// burns 45 s after that. Raw meat makes the eater sick (poison), cooked meat feeds and heals. Lamp oil is made from
    /// animal fat and fibre (Rust's recipe) beside the charcoal one.
    /// </summary>
    [Test]
    public static void CookingAndBurning()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 20, 20);
        Player p = rig.P(ada);
        rig.Give(ada, "campfire", 1);
        Vector3 at = rig.Zone.GroundAt(21.5f, 20);
        rig.Look(ada, at);
        Assert.Equal(OutcomeCode.Done, rig.Place(ada, 0, at, 0, c.ItemByKey("campfire").Id), "place the campfire");
        Deployable fire = rig.Zone.Things().Statics().OfType<Deployable>().Single();
        DeviceStats device = fire.Def.Device!;
        fire.Storage!.Set(device.FuelSlots[0], ItemStack.Of(c.ItemByKey("wood"), 200));
        fire.Storage.Set(device.InputSlots[0], ItemStack.Of(c.ItemByKey("raw-venison"), 1));
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(ada, fire.Ref, "light"), "light it");
        int Count(string key) => device.OutputSlots.Sum(s => fire.Storage[s].Item == c.ItemByKey(key).Id ? fire.Storage[s].Count : 0);
        rig.Step(15 * 30 - 2);
        Assert.Equal(0, Count("cooked-venison"), "not cooked before 15 s");
        rig.Step(3);
        Assert.Equal(1, Count("cooked-venison"), "cooked after 15 s");
        rig.Step(45 * 30 - 3);
        Assert.Equal(1, Count("cooked-venison"), "not burnt yet");
        rig.Step(4);
        Assert.True(Count("cooked-venison") == 0 && Count("burnt-venison") == 1, "burnt when left 45 s more");

        // Eating: raw is poison, cooked feeds and heals.
        p.Vitals.Health = 80;
        p.Vitals.Calories = 100;
        rig.Give(ada, "raw-venison", 1);
        rig.Give(ada, "cooked-venison", 1);
        int raw = Enumerable.Range(0, 24).First(i => p.Inventory.Main[i].Item == c.ItemByKey("raw-venison").Id);
        Assert.Equal(OutcomeCode.Done, rig.Act(ada, ContainerRef.Main, raw, "eat"), "eat it raw");
        Assert.Near(70, p.Vitals.Health, 0.01, "raw venison costs 10 health");
        Assert.Near(120, p.Vitals.Calories, 0.01, "and gives 20 calories");
        int cooked = Enumerable.Range(0, 24).First(i => p.Inventory.Main[i].Item == c.ItemByKey("cooked-venison").Id);
        Assert.Equal(OutcomeCode.Done, rig.Act(ada, ContainerRef.Main, cooked, "eat"), "eat it cooked");
        Assert.Near(160, p.Vitals.Calories, 0.01, "40 calories");
        Assert.Near(10, p.Vitals.PendingHeal, 0.01, "and 10 health to heal");

        // Lamp oil, Rust's way: 3 fat and a cloth make 4.
        Recipe fat = c.RecipeByKey("lamp-oil-from-fat")!;
        Assert.True(fat.Output == c.ItemByKey("lamp-oil") && fat.Id == 220 && c.Recipe(220) == fat, "the second lamp-oil recipe");
        Assert.True(c.Recipe(c.ItemByKey("lamp-oil").Id)!.Ingredients.Any(i => i.Item.Key == "charcoal"), "beside the charcoal one");
        rig.Give(ada, "animal-fat", 3);
        rig.Give(ada, "cloth", 1);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().Craft(ada, fat.Id, 1, out _), "craft it");
        rig.Step((int)(fat.Seconds * 30) + 2);
        Assert.Equal(4, p.Inventory.Count(c.ItemByKey("lamp-oil")), "four lamp oil");
        Assert.Equal(0, p.Inventory.Count(c.ItemByKey("animal-fat")), "the fat used");
    }

    /// <summary>
    /// GROUND.md §6.9, spawning: with a player at the landing area the spawn areas within reach come out with herds sized
    /// from their habitat (deterministic: the same areas, the same herds); a herd killed off comes back after its
    /// species' respawn time and never above its size; when the player leaves, the areas empty, and they fill again
    /// (less those still waiting) when she returns.
    /// </summary>
    [Test]
    public static void SpawnCapsAndRespawn()
    {
        using var rig = new GameRig();
        AnimalWorld animals = AnimalRig.Animals(rig);
        animals.NaturalSpawning = true;
        Entity ada = rig.Add("Ada", 0, 0);
        rig.Step(30 * 3, () => AnimalRig.Still(rig, ada));
        List<SpawnArea> active = animals.Areas.Where(a => a.Active).ToList();
        List<SpawnSlot> slots = active.SelectMany(a => a.Slots).ToList();
        int expected = slots.Sum(s => s.Size);
        int alive = animals.All.Count(a => !a.IsDead);
        Fixtures.Report($"{active.Count} areas out, {slots.Count} herds: " + string.Join(", ", slots.GroupBy(s => s.Species.Key)
            .Select(g => $"{g.Sum(s => s.Size)} {g.Key}")) + $"; {alive} animals; tree cover {active.Average(a => a.TreeCover):0.00}");
        Assert.True(active.Count >= 9 && slots.Count > 0, "areas around her, with herds");
        Assert.Equal(expected, alive, "every herd out at its size");

        // The same zone, the same herds.
        using (var again = new GameRig())
        {
            AnimalWorld twin = AnimalRig.Animals(again);
            twin.NaturalSpawning = true;
            Entity bo = again.Add("Bo", 0, 0);
            again.Step(30 * 3, () => AnimalRig.Still(again, bo));
            Assert.Equal(string.Join(";", slots.Select(s => $"{s.Species.Key}{s.Size}")),
                string.Join(";", twin.Areas.Where(a => a.Active).SelectMany(a => a.Slots).Select(s => $"{s.Species.Key}{s.Size}")), "deterministic herds");
        }

        // Kill one herd off: its slot waits its respawn time, then brings it back at its size, not more.
        SpawnSlot slot = slots.OrderByDescending(s => s.Size).First();
        Herd herd = slot.Herd!;
        foreach (Animal a in herd.Members.ToList())
        {
            rig.Zone.Animals().DamageAnimal(a, 10_000, HitRegion.Body, a.Position, rig.P(ada));
        }
        Assert.Equal(slot.Size, slot.Respawns.Count, "every member waits to come back");
        Assert.True(slot.Respawns.All(t => t == rig.Zone.Tick + (long)Math.Round(slot.Species.RespawnSeconds * 30)), "after the species' respawn time");
        rig.Step(60, () => AnimalRig.Still(rig, ada));
        Assert.Equal(slot.Size, slot.Respawns.Count, "not before");
        for (int i = 0; i < slot.Respawns.Count; i++)
        {
            slot.Respawns[i] = rig.Zone.Tick + 5;
        }
        rig.Step(90, () => AnimalRig.Still(rig, ada));
        int back = AnimalRig.Alive(slot.Herd!).Count();
        Assert.Equal(slot.Size, back, "back at its size");
        Assert.Equal(0, slot.Respawns.Count, "nobody waiting");
        rig.Step(60, () => AnimalRig.Still(rig, ada));
        Assert.Equal(slot.Size, AnimalRig.Alive(slot.Herd!).Count(), "and never more");

        // She leaves: everything empties after the areas linger; back, they fill again.
        Vector3 far = rig.Zone.GroundAt(3000, 0);
        rig.Zone.Teleport(ada, far + rig.Zone.Frame.UpAt(far) * 0.02f);
        rig.Step(30 * 36, () => AnimalRig.Still(rig, ada));
        Assert.True(active.All(a => !a.Active), "the areas she left are empty");
        Assert.Equal(0, animals.All.Count(a => !a.IsDead && AnimalRig.Flat(a.Position, rig.Zone.Landing) < 600), "no animals left there");
        Vector3 home = rig.Zone.GroundAt(0, 0);
        rig.Zone.Teleport(ada, home + rig.Zone.Frame.UpAt(home) * 0.02f);
        rig.Step(30 * 3, () => AnimalRig.Still(rig, ada));
        Assert.True(active.All(a => a.Active), "out again");
        Assert.Equal(expected, active.SelectMany(a => a.Slots).Sum(s => s.Herd == null ? 0 : AnimalRig.Alive(s.Herd).Count()), "at their sizes");
    }

    /// <summary>
    /// GROUND.md §6.9, attacks and armour: a wolf's bite lands on the legs (12, less what the trousers give against blows),
    /// a bear's swipe on the body (30, less the chest plate and shirt), each with bleeding; an attack whose target steps
    /// out of reach during the wind-up misses.
    /// </summary>
    [Test]
    public static void AttackDamageWithArmour()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        AnimalWorld animals = AnimalRig.Animals(rig);
        Entity ada = rig.Add("Ada", 0, 0);
        Player p = rig.P(ada);
        Animal wolf = AnimalRig.Herd(rig, "wolf", 0, -30, 1).Leader!;
        Animal bear = AnimalRig.Herd(rig, "bear", 30, -30, 1).Leader!;
        rig.Step(2);

        p.Vitals.Health = 100;
        Assert.True(rig.Zone.Animals().AnimalAttacksPlayer(wolf, p.Id), "a bite");
        Assert.Near(88, p.Vitals.Health, 1e-3, "12 on bare legs");
        Assert.Near(6, p.Vitals.Bleeding, 1e-3, "and half of it bleeds");

        p.Vitals.Health = 100;
        p.Vitals.Bleeding = 0;
        rig.Give(ada, "woven-trousers", 1);
        rig.Give(ada, "chest-plate", 1);
        rig.Give(ada, "woven-shirt", 1);
        foreach (string key in new[] { "woven-trousers", "chest-plate", "woven-shirt" })
        {
            int slot = Enumerable.Range(0, 24).First(i => p.Inventory.Main[i].Item == c.ItemByKey(key).Id);
            Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().QuickMove(ada, ContainerRef.Main, slot, c.ItemByKey(key).Id), $"wear the {key}");
        }
        float legs = p.Inventory.Protection(c, HitRegion.Limb, projectile: false), body = p.Inventory.Protection(c, HitRegion.Body, projectile: false);
        rig.Zone.Animals().AnimalAttacksPlayer(wolf, p.Id);
        Assert.Near(100 - 12 * (1 - legs), p.Vitals.Health, 1e-3, $"the trousers take {legs:P0} of a bite");
        p.Vitals.Health = 100;
        rig.Zone.Animals().AnimalAttacksPlayer(bear, p.Id);
        Assert.Near(100 - 30 * (1 - body), p.Vitals.Health, 1e-3, $"the chest plate and shirt take {body:P0} of a swipe");
        Fixtures.Report($"a wolf's bite: 12 bare, {12 * (1 - legs):0.0} through trousers; a bear's swipe: 30 bare, {30 * (1 - body):0.0} through a chest plate and shirt");

        // A step back in the wind-up: the swipe misses.
        p.Vitals.Health = 100;
        p.Vitals.Bleeding = 0;
        Vector3 near = rig.Zone.Frame.Beside(bear.Position, 0, 0, 2.6f);
        rig.Zone.Teleport(ada, near + rig.Zone.Frame.UpAt(near) * 0.02f);
        bear.Hunger = 0;
        rig.Zone.Animals().DamageAnimal(bear, 1, HitRegion.Body, near, p, p.Id, p.Name);
        long started = -1;
        rig.Step(60, () =>
        {
            AnimalRig.Still(rig, ada);
            if (bear.State == AnimalState.Attack && started < 0)
            {
                started = rig.Zone.Tick;
                Vector3 away = rig.Zone.Settle(rig.Zone.Frame.Beside(rig.Ch(ada).State.Position, 0, 0, 4f));
                rig.Zone.Teleport(ada, away + rig.Zone.Frame.UpAt(away) * 0.02f);
            }
        });
        Assert.True(started > 0, "the bear began a swipe");
        Assert.Near(100, p.Vitals.Health, 1e-3, "stepped out of reach: missed");
    }

    /// <summary>
    /// GROUND.md §6.9, cost: 200 animals of every kind around ten players, awake and about their business for 15 s.
    /// Prints the animals' share of the zone's tick and the cost per animal; the AI runs on a staggered slow tick.
    /// </summary>
    [Test]
    public static void AiCostPerAnimal()
    {
        using var rig = new GameRig();
        AnimalWorld animals = AnimalRig.Animals(rig);
        var players = new List<Entity>();
        for (int i = 0; i < 10; i++)
        {
            double angle = i * Math.PI * 2 / 10;
            players.Add(rig.Add($"P{i}", (float)(120 * Math.Cos(angle)), (float)(120 * Math.Sin(angle))));
        }
        animals.Populate(rig.Zone.Landing, 220, 200);
        foreach (Animal a in animals.All)
        {
            a.Hunger = 0;
        }
        rig.Step(60);
        TimeSpan before = rig.Zone.Animals().AnimalTime;
        long thinks = animals.Thinks, steps = animals.Steps;
        var watch = Stopwatch.StartNew();
        int awake = 0;
        rig.Step(450, () => awake = Math.Max(awake, animals.Awake));
        watch.Stop();
        double perTick = (rig.Zone.Animals().AnimalTime - before).TotalMilliseconds / (animals.Steps - steps);
        double perAnimal = perTick * 1000 / Math.Max(1, animals.Count);
        double thinksPerSecond = (animals.Thinks - thinks) / 15.0 / Math.Max(1, animals.Count);
        Fixtures.Report($"{animals.Count} animals ({awake} awake): {perTick:0.000} ms a tick for all of them, {perAnimal:0.0} µs an animal a tick; " +
            $"{thinksPerSecond:0.0} thinks an animal a second; the zone's whole step {watch.Elapsed.TotalMilliseconds / 450:0.00} ms a tick; " +
            $"{animals.Nav.Count} grid areas ({animals.Nav.BuiltOnDemand} built on demand)");
        Assert.True(animals.Count == 200, "200 animals");
        Assert.InRange(3, 9, thinksPerSecond, "the AI on its slow tick");
        Assert.True(perAnimal < 60, "cheap enough for hundreds of animals");
    }
}
