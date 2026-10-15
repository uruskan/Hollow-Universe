using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>Rockets and fires in a <see cref="GameRig"/> (IW4, IRON_WAKE.md §7, GROUND.md §6.6).</summary>
public static class FireRig
{
    public static FireDef Incendiary(GameRig rig) => rig.C.Fire("incendiary")!;

    /// <summary>
    /// Turns a character to fire the launcher's round at a point: aimed above it by what the round drops on the way (its
    /// gravity at its speed: a player aims high for a far target).
    /// </summary>
    public static void Aim(GameRig rig, Entity e, string round, Vector3 at)
    {
        FirearmStats launcher = rig.C.ItemByKey("rocket-launcher").Firearm!;
        BulletStats b = rig.C.ItemByKey(round).Bullet!;
        Vector3 eye = rig.Zone.EyeOf(rig.Ch(e).State);
        float t = Vector3.Distance(eye, at) / (float)(launcher.Speed * b.Speed);
        float drop = 0.5f * (float)launcher.GravityOf(b, rig.Zone.Spec.SurfaceGravity) * t * t;
        rig.Look(e, at + rig.Zone.Frame.UpAt(at) * drop);
    }

    /// <summary>Fires the loaded launcher along the view and steps until its round bursts; returns the ticks it flew.</summary>
    public static int Shoot(GameRig rig, Entity e, uint seq, int most = 150)
    {
        long before = rig.Zone.Explosives().RaidStats.Detonations;
        Assert.Equal(OutcomeCode.Done, Guns.Queue(rig, e, seq), "fired");
        return Raids.StepUntil(rig, () => rig.Zone.Explosives().RaidStats.Detonations > before, most, "the rocket's burst");
    }

    /// <summary>Loads the launcher in hand with a round from the inventory (a 5 s reload).</summary>
    public static void Reload(GameRig rig, Entity e, uint seq, string round)
    {
        Assert.Equal(OutcomeCode.Done, Guns.Queue(rig, e, seq, GunActionKind.Reload, ammo: rig.C.ItemByKey(round).Id), "reloading");
        rig.Step(152);
        Assert.Equal(1, (int)rig.P(e).Inventory.Belt[0].Loaded, $"loaded with a {round}");
    }

    /// <summary>Steps until a fire has gone out; returns the ticks it took.</summary>
    public static int BurnOut(GameRig rig, Fire fire) => Raids.StepUntil(rig, () => fire.Out, fire.Kind.Ticks + 60, "the fire going out");

    /// <summary>A point on the sea floor at least <paramref name="depth"/> metres under the sea, the nearest the landing site on rings round it.</summary>
    public static Vector3 DeepWater(Zone zone, float depth = 3)
    {
        for (int r = 100; r <= 3600; r += 50)
        {
            for (int k = 0; k < 36; k++)
            {
                float a = k * MathF.PI / 18;
                Vector3 p = zone.GroundAt(r * MathF.Cos(a), r * MathF.Sin(a));
                if (zone.Surface.SeaRadius > 0 && zone.Frame.ToBody(p).Length < zone.Surface.SeaRadius - depth)
                {
                    return p;
                }
            }
        }
        throw new TestFailure("no sea near the landing site");
    }

    /// <summary>Puts on everything worn on the body, head and limbs: the best protection there is.</summary>
    public static void BestArmour(GameRig rig, Entity e)
    {
        Player p = rig.P(e);
        foreach (string key in new[] { "woven-hood", "metal-mask", "woven-shirt", "chest-plate", "woven-trousers", "woven-gloves", "work-boots" })
        {
            rig.Give(e, key, 1);
            int slot = Enumerable.Range(0, PlayerInventory.MainSlots).First(i => p.Inventory.Main[i].Item == rig.C.ItemByKey(key).Id);
            Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().QuickMove(e, ContainerRef.Main, slot, 0), $"wears the {key}");
        }
    }
}

public static class FireTests
{
    /// <summary>
    /// IRON_WAKE.md §7, the item: the incendiary rocket is a round of the launcher's calibre (stack 3) whose blast leaves a
    /// fire; Rust's recipe (two metal pipes, 150 gunpowder, 75 lamp oil) at workbench 2, researchable in the second tree
    /// after the launcher, so it is unlocked there and made there in its 10 s; the raid bots carry two; its item rows show
    /// its burst and its fire.
    /// </summary>
    [Test]
    public static void TheIncendiaryRocketIsALauncherRoundMadeAtWorkbenchTwo()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        ItemDef rocket = c.ItemByKey("rocket-incendiary");
        Assert.True(rocket.StackSize == 3 && rocket.Bullet is { Calibre: "rocket" } && c.ItemByKey("rocket-launcher").Firearm!.Ammo.Contains(rocket),
            "a stack of 3, the launcher's round");
        Assert.True(rocket.Bullet!.Blast is { Key: "rocket-incendiary", Radius: 2.5, Flesh: 60, Deployable: 60, Fire: { Key: "incendiary", Radius: 3.5, Seconds: 18 } },
            "its burst and the fire it leaves");
        Recipe recipe = c.Recipe(rocket.Id)!;
        string ingredients = string.Join(", ", recipe.Ingredients.Select(i => $"{i.Count} {i.Item.Key}"));
        Assert.Equal("2 metal-pipe, 150 gunpowder, 75 lamp-oil", ingredients, "Rust's recipe");
        Assert.True(recipe.Workbench == 2 && !recipe.Known, "at workbench 2, learnt");
        Assert.True(c.Loot("raid-kit")!.Guaranteed.Any(e => e.Item == rocket && e.Least == 2), "the raid bots carry two");

        Entity ada = rig.Add("Ada", 20, 20);
        Player a = rig.P(ada);
        rig.Give(ada, "salvage", 1200);
        foreach (ItemAmount i in recipe.Ingredients)
        {
            rig.Give(ada, i.Item.Key, i.Count);
        }
        Assert.Equal(OutcomeCode.UnknownRecipe, rig.Zone.Actions().Craft(ada, rocket.Id, 1, out _), "unknown at first");
        Vector3 at = rig.Zone.GroundAt(21.6f, 20);
        var bench = new Deployable(rig.Zone.NextId(), c.Deployable("workbench-2")!, at, rig.Zone.Frame.UpAt(at), 0, a.Id);
        rig.Zone.Things().AddStatic(bench);
        Outcome Unlock(string key) => rig.Zone.Interaction().Interact(ada, bench.Ref, "unlock", c.ItemByKey(key).Id);
        Assert.Equal(OutcomeCode.NeedsEarlierNode, Unlock("rocket-incendiary"), "after the launcher");
        foreach (string key in new[] { "garage-door", "explosives", "rocket-launcher", "rocket-incendiary" })
        {
            Assert.Equal(OutcomeCode.Done, Unlock(key), $"unlocked {key}");
        }
        Assert.Equal(OutcomeCode.Done, rig.Zone.Actions().Craft(ada, rocket.Id, 1, out _), "made at the level 2 workbench");
        rig.Step(recipe.UnitTicks + 3);
        Assert.Equal(1, a.Inventory.Count(rocket), "an incendiary rocket");
        Assert.True(a.Inventory.Count(c.ItemByKey("lamp-oil")) == 0 && a.Inventory.Count(c.ItemByKey("metal-pipe")) == 0, "for its ingredients");

        List<ItemRow> rows = ItemRows.Describe(c, rocket);
        string Row(string label) => rows.FirstOrDefault(r => r.Label == label).Value ?? "(none)";
        Assert.Equal("2.5 m, 60 damage", Row("Blast"), "its burst");
        Assert.Equal("3.5 m for 18 s", Row("Fire"), "its fire");
        Assert.Equal("Rocket launcher", Row("For"), "the launcher's");
        Fixtures.Report(string.Join("; ", rows.Select(r => $"{r.Label}: {r.Value}")));
    }

    /// <summary>
    /// IRON_WAKE.md §7: an incendiary rocket from the launcher strikes a small wooden box: its burst does 60 of the box's
    /// 150, the burning fuel runs down the box's side and burns 18 s at its foot, and the box burns down as the fire dies
    /// (90 more), spilling what it held; the fire then goes out.
    /// </summary>
    [Test]
    public static void ARocketsFireBurnsDownTheBoxItStruck()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, 0);
        Vector3 boxAt = rig.Zone.GroundAt(0, -7);
        rig.Clear(rig.Zone.GroundAt(0, 0), boxAt, 5);
        var box = new Deployable(rig.Zone.NextId(), c.Deployable("small-box")!, boxAt, rig.Zone.Frame.UpAt(boxAt), 0, EntityId.None);
        box.Storage!.Set(0, ItemStack.Of(c.ItemByKey("wood"), 50));
        rig.Zone.Things().AddStatic(box);
        Guns.Arm(rig, ada, "rocket-launcher", "rocket-incendiary", 1);
        rig.Step(20);
        FireRig.Aim(rig, ada, "rocket-incendiary", box.Centre);
        int flight = FireRig.Shoot(rig, ada, 1);
        Assert.Near(150 - 60, box.Health, 0.01f, "the burst's 60");
        Fire fire = rig.Zone.Fires().All.Single();
        float toBox = Vector3.Distance(BlastSystem.ClosestPoint(box, fire.Position), fire.Position);
        Assert.True(fire.Item.Key == "rocket-incendiary" && fire.Owner == rig.P(ada).Id && toBox < 0.5f,
            $"a fire at the box's foot ({toBox:0.00} m from it, on {fire.Link.Support})");
        int burnt = FireRig.BurnOut(rig, fire);
        Assert.InRange(18 * 30 - 1, 18 * 30 + 1, burnt, "it burnt 18 s");
        Assert.True(rig.Zone.Things().StaticOf(box.Id) == null, $"the box burnt down ({box.Health:0.0} left)");
        Destroyed broke = rig.Zone.Events.Of<Destroyed>().Single(e => e.Target == box.Ref);
        Assert.True(broke.Cause == DestroyCause.Fire && broke.Player == rig.P(ada).Id, $"by fire, Ada's ({broke.Cause})");
        Assert.True(rig.Zone.Things().Statics().OfType<DroppedBag>().Any(b => Vector3.Distance(b.Position, boxAt) < 2), "what it held spilled");
        Assert.True(rig.Zone.Events.Of<FireOut>().Single().Why == FireEnd.BurntOut && rig.Zone.Fires().Burning == 0, "and the fire went out");
        Fixtures.Report($"the rocket flew {flight} ticks; the fire {toBox:0.00} m from the box, on {fire.Link.Support.Family}; " +
            $"burnt {burnt / 30.0:0.0} s; {rig.Zone.Fires().Stats}");
    }

    /// <summary>
    /// IRON_WAKE.md §7, the building: a wooden hut's wall struck by an incendiary rocket takes the burst's 20, then about 108
    /// from the fire at its foot (6 a second for 18 s through the heat channel); a second rocket's fire breaks it, a raid on
    /// the claimed hut. A stone hut's wall in the same fire takes nothing (stone doesn't burn).
    /// </summary>
    [Test]
    public static void AWoodWallBurnsTwoFiresBreakItAStoneWallDoesNot()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        TestBases.Hut wood = Raids.Hut(rig, 0, 0, grade: 1, door: null);
        Piece east = wood.Walls[1];
        (Vector3 face, Vector3 outward) = wood.Face(east, height: 1.5);
        Entity ada = rig.Add("Ada", 0, 0);
        Raids.Stand(rig, ada, rig.Zone.Settle(face + outward * 9));
        rig.Clear(face, face + outward * 10, 3);
        Guns.Arm(rig, ada, "rocket-launcher", "rocket-incendiary", 1);
        rig.Give(ada, "rocket-incendiary", 1);
        rig.Step(20);
        FireRig.Aim(rig, ada, "rocket-incendiary", face);
        FireRig.Shoot(rig, ada, 1);
        double afterBurst = east.Health;
        Assert.Equal(200.0 - 20, afterBurst, "the burst's 20 against wood");
        Fire first = rig.Zone.Fires().All.Single();
        FireRig.BurnOut(rig, first);
        double lost = afterBurst - east.Health;
        Assert.InRange(105, 108, lost, "about 108 from one fire");
        Assert.True(rig.Zone.Explosives().RaidHits.Count(h => h.Item == c.ItemByKey("rocket-incendiary").Id) >= 2,
            "the burst and the fire both raid the claimed hut");

        FireRig.Reload(rig, ada, 2, "rocket-incendiary");
        FireRig.Aim(rig, ada, "rocket-incendiary", face);
        FireRig.Shoot(rig, ada, 3);
        Fire second = rig.Zone.Fires().All.Single();
        int took = Raids.StepUntil(rig, () => east.Gone, second.Kind.Ticks + 30, "the second fire breaking the wall");
        Destroyed broke = rig.Zone.Events.Of<Destroyed>().First(e => e.Target == east.Ref);
        Assert.Equal(DestroyCause.Fire, broke.Cause, "burnt through");

        // Stone doesn't burn.
        TestBases.Hut stone = Raids.Hut(rig, 40, 0, grade: 2, door: null);
        Piece wall = stone.Walls[1];
        (Vector3 stoneFace, Vector3 stoneOut) = stone.Face(wall, height: 1.5);
        Fire beside = rig.Zone.Fires().Ignite(FireRig.Incendiary(rig), stoneFace + stoneOut * 0.05f, stoneOut, c.ItemByKey("rocket-incendiary"),
            rig.P(ada).Id, "Ada")!;
        Assert.True(beside != null && Vector3.Distance(BlastSystem.ClosestPoint(c.Building, wall, beside.Position), beside.Position) < 0.6f,
            "the fuel ran down the stone wall to its foot");
        FireRig.BurnOut(rig, beside!);
        Assert.Equal(500.0, wall.Health, "a stone wall takes nothing");
        Fixtures.Report($"wood: 200 → {afterBurst} after the burst → {afterBurst - lost} after its fire (−{lost}); the second fire broke it " +
            $"{took / 30.0:0.0} s in; stone: {wall.Health}; {rig.Zone.Fires().Stats}");
    }

    /// <summary>
    /// IRON_WAKE.md §7, people: a player standing in a fire burns (8 a second at its middle) and is set burning; once out of
    /// it, the burning goes on hurting them (2 a second) until it runs out, or they swim.
    /// </summary>
    [Test]
    public static void APlayerInAFireBurnsAndKeepsBurningUntilTheySwim()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Vector3 at = rig.Zone.GroundAt(0, -20);
        rig.Clear(at, at, 6);
        Fire fire = rig.Zone.Fires().Ignite(FireRig.Incendiary(rig), at + rig.Zone.Frame.UpAt(at) * 0.6f, rig.Zone.Frame.UpAt(at),
            c.ItemByKey("rocket-incendiary"), EntityId.None, "")!;
        Assert.True(fire is { Link.Support.Family: TargetFamily.Ground }, "on the ground");
        Entity bo = rig.Add("Bo", 0, -20);
        Player b = rig.P(bo);
        rig.Step(3 * 30);
        float inFire = 100 - b.Vitals.Health, burning = b.Vitals.Burning;
        Assert.True(inFire >= 20 && burning > 10 && !b.Dead, $"burnt in it ({inFire:0.0}) and burning ({burning:0.0})");

        Raids.Stand(rig, bo, rig.Zone.GroundAt(0, -40));
        float left = b.Vitals.Health;
        rig.Step(3 * 30);
        float after = left - b.Vitals.Health;
        Assert.True(after >= 4 && b.Vitals.Burning < burning, $"still burning out of it ({after:0.0} more, {b.Vitals.Burning:0.0} left to burn)");

        rig.Zone.Teleport(bo, FireRig.DeepWater(rig.Zone));
        rig.Step(2 * 30);
        Assert.True((rig.Ch(bo).State.Flags & MoveFlags.Swimming) != 0 && b.Vitals.Burning == 0, $"swimming, it went out ({b.Vitals.Burning:0.0})");
        float wet = b.Vitals.Health;
        rig.Step(2 * 30);
        Assert.Near(wet, b.Vitals.Health, 1.0f, "and hurts no more");
        Fixtures.Report($"3 s in the fire: −{inFire:0.0} health, {burning:0.0} set burning; 3 s out of it: −{after:0.0}; in the sea: out");
    }

    /// <summary>
    /// IRON_WAKE.md §7, GROUND_SYSTEMS.md §3.6: a fire on a twig hut's roof burns through it, and its fuel falls to what is
    /// below and burns on there, its time going on; with the zone's most burning (64), a new one puts the oldest out.
    /// </summary>
    [Test]
    public static void AFireFallsWhenWhatItBurnsOnGoesAndTheOldestMakesWay()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        TestBases.Hut twig = Raids.Hut(rig, 0, 0, grade: 0, door: null);
        Piece roof = twig.Roof!;
        Vector3 up = rig.Zone.Frame.UpAt(twig.Inside);
        Fire fire = rig.Zone.Fires().Ignite(FireRig.Incendiary(rig), twig.Inside + up * 4.5f, up, c.ItemByKey("rocket-incendiary"), EntityId.None, "")!;
        Assert.True(fire != null && fire.Link.Support == roof.Ref, $"on the roof ({fire?.Link.Support})");
        float high = Vector3.Dot(fire!.Position - twig.Inside, up);
        long end = fire.EndTick;
        Raids.StepUntil(rig, () => roof.Gone, 3 * 30, "the roof burning through");
        rig.Step(30);
        float low = Vector3.Dot(fire.Position - twig.Inside, up);
        Assert.True(!fire.Out && fire.EndTick == end && low < high - 2 && fire.Link.Support != roof.Ref,
            $"the fire fell {high - low:0.0} m onto {fire.Link.Support} and burns on");
        Assert.True(rig.Zone.Fires().Stats.Moved >= 1 && rig.Zone.Events.Of<FireLit>().Any(e => e.Fire == fire.Id && e.Moved), "told as moved");

        var lit = new List<Fire>();
        Vector3 field = rig.Zone.GroundAt(60, 60);
        rig.Clear(field, field, 40);
        for (int i = 0; i < 65; i++)
        {
            Vector3 at = rig.Zone.GroundAt(60 + (i % 9) * 4, 60 + (i / 9) * 4);
            lit.Add(rig.Zone.Fires().Ignite(FireRig.Incendiary(rig), at + rig.Zone.Frame.UpAt(at) * 0.5f, rig.Zone.Frame.UpAt(at), c.ItemByKey("rocket-incendiary"),
                EntityId.None, "")!);
        }
        Assert.Equal(c.FireRules.Most, rig.Zone.Fires().Burning, "at most 64 burn");
        Assert.True(fire.Out && rig.Zone.Events.Of<FireOut>().Any(e => e.Fire == fire.Id && e.Why == FireEnd.Replaced) && lit.All(f => !f.Out || f == lit[0]),
            "the oldest made way for the newest");
        Fixtures.Report($"the fire fell {high - low:0.0} m from the roof onto {fire.Link.Support.Family}; {rig.Zone.Fires().Stats}");
    }

    /// <summary>IRON_WAKE.md §7, water: an incendiary rocket into the sea leaves no fire (it goes out at once).</summary>
    [Test]
    public static void AFireOnWaterGoesOut()
    {
        using var rig = new GameRig(spec => new LevelSurface(spec.Radius, groundHeight: -0.4, seaRadius: spec.Radius));
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, 0);
        Guns.Arm(rig, ada, "rocket-launcher", "rocket-incendiary", 1);
        rig.Step(20);
        rig.Look(ada, rig.Zone.GroundAt(0, -8));
        FireRig.Shoot(rig, ada, 1);
        Assert.Equal(0, rig.Zone.Fires().Burning, "no fire burns");
        Assert.True(rig.Zone.Fires().Stats is { Bursts: 1, Lit: 0, Doused: 1 }, $"it went out at once ({rig.Zone.Fires().Stats})");
        Assert.True(rig.Zone.Events.Of<FireLit>().Count == 0, "none was lit");
    }

    /// <summary>
    /// IRON_WAKE.md §4.10, §7: a fire on a flying Iron Wake's deck rides with the ship (held by it through its support link)
    /// and burns someone standing beside it aboard while the ship climbs.
    /// </summary>
    [Test]
    public static void AFireOnAFlyingDeckRidesTheShipAndBurnsTheCrew()
    {
        using var rig = new IronWakeRig();
        GameContent c = rig.Game.C;
        Entity ada = rig.Aboard("Ada", new Vector3(0, 3.2f, 0), Vector3.UnitZ);
        rig.Step(30);
        Entity bo = rig.Pilot("Bo");
        ShipPose pose = rig.Pose;
        Fire fire = rig.Zone.Fires().Ignite(c.Fire("incendiary")!, pose.ToWorld(new Vector3(0, 3.7f, 1.2f)), pose.Up, c.ItemByKey("rocket-incendiary"),
            EntityId.None, "")!;
        Assert.True(fire != null && fire.Link.Support == rig.Ship.Ref, $"on the ship ({fire?.Link.Support})");
        Vector3 local = rig.Pose.ToLocal(fire!.Position);
        Assert.Near(3.2f, local.Y, 0.05f, "on Deck A");
        float health = rig.Game.P(ada).Vitals.Health, height = rig.Height, drift = 0;
        rig.Fly(30 * 5, i => new ShipInput { Vertical = 1 }, i =>
        {
            drift = MathF.Max(drift, Vector3.Distance(rig.Pose.ToLocal(fire.Position), local));
        });
        float climbed = rig.Height - height, hurt = health - rig.Game.P(ada).Vitals.Health;
        Assert.True(climbed > 5, $"the ship climbed ({climbed:0.0} m)");
        Assert.True(drift < 0.01f, $"the fire rode it ({drift * 1000:0.0} mm off its place on the deck)");
        Assert.True(hurt > 20 && rig.Game.P(ada).Vitals.Burning > 0, $"Ada beside it burnt ({hurt:0.0}) and burning");
        Assert.True(rig.Ch(ada).State.Frame == rig.Key, "aboard all the while");
        Fixtures.Report($"climbed {climbed:0.0} m in 5 s; the fire {drift * 1000:0.00} mm off its place on Deck A; Ada −{hurt:0.0}, burning {rig.Game.P(ada).Vitals.Burning:0.0}");
    }

    /// <summary>
    /// KNOWN_PROBLEMS.md finding 26: the high-velocity rocket kills whatever living thing it strikes: a bear (400) and a player
    /// in the best armour there is; a regular rocket's burst leaves a bear standing.
    /// </summary>
    [Test]
    public static void TheHighVelocityRocketKillsABearAndAnArmouredPlayer()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, 0);
        rig.Clear(rig.Zone.GroundAt(0, 0), rig.Zone.GroundAt(0, -12), 4);
        rig.Clear(rig.Zone.GroundAt(0, 0), rig.Zone.GroundAt(10, 0), 4);
        Guns.Arm(rig, ada, "rocket-launcher", "rocket-hv", 1);
        rig.Give(ada, "rocket-hv", 1);
        rig.Step(20);

        // A regular rocket's burst at a bear's flank: it lives.
        Herd herd = AnimalRig.Herd(rig, "bear", 0, -9, 1);
        Animal bear = herd.Leader!;
        rig.Step(2);
        rig.Zone.Damage().Blast(c.Blast("rocket")!, bear.Position + rig.Zone.Frame.UpAt(bear.Position) * 0.6f, rig.Zone.Frame.UpAt(bear.Position),
            bear.Ref, TargetFamily.Animal, EntityId.None, "", c.ItemByKey("rocket").Id);
        Assert.True(!bear.IsDead && bear.Health is > 150 and < 400, $"a rocket's burst leaves a bear standing ({bear.Health:0} left)");

        // The high-velocity rocket, striking it: dead.
        Vector3 middle = AnimalHitboxes.Middle(bear.Species, rig.Zone.Animals().PoseOf(bear));
        FireRig.Aim(rig, ada, "rocket-hv", middle);
        int flight = FireRig.Shoot(rig, ada, 1);
        Assert.True(bear.IsDead, $"the high-velocity rocket killed the bear ({bear.Health:0} left)");

        // A player in the best armour, struck in the chest: dead outright, not down.
        Entity bo = rig.Add("Bo", 10, 0);
        FireRig.BestArmour(rig, bo);
        FireRig.Reload(rig, ada, 2, "rocket-hv");
        Player b = rig.P(bo);
        float armour = b.Inventory.Protection(c, HitRegion.Body, projectile: true);
        Assert.Near(0.04f + 0.25f + 0.04f, armour, 1e-4f, "shirt, chest plate and trousers on the body");
        Vector3 chest = rig.Ch(bo).State.Position + rig.Zone.Frame.UpAt(rig.Ch(bo).State.Position) * 1.2f;
        FireRig.Aim(rig, ada, "rocket-hv", chest);
        FireRig.Shoot(rig, ada, 3);
        Assert.True(b.Dead && !b.Wounded, "Bo in the best armour died outright");
        Fixtures.Report($"the bear struck after {flight} ticks: dead; Bo with {armour * 100:0}% on the body: dead");
    }

    /// <summary>
    /// KNOWN_PROBLEMS.md finding 26: each rocket drops by its own gravity: the regular rocket, slow and heavy, falls far more
    /// over 50 m than the high-velocity one (fired level, high above the ground, measured from the line it left along).
    /// </summary>
    [Test]
    public static void TheRegularRocketDropsMoreThanTheHighVelocityOne()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0);
        Guns.Arm(rig, ada, "rocket-launcher", "rocket", 1);
        rig.Give(ada, "rocket-hv", 1);
        Vector3 ground = rig.Zone.GroundAt(0, 0), up = rig.Zone.Frame.UpAt(ground);
        rig.Zone.SetCheats(ada, MoveCheats.Noclip);
        rig.Zone.Teleport(ada, ground + up * 80);
        rig.Step(20);
        float Drop(uint seq)
        {
            rig.Turn(ada, 0, 0);
            Assert.Equal(OutcomeCode.Done, Guns.Queue(rig, ada, seq), "fired level");
            rig.Step();
            Bullet b = rig.Zone.Guns().Bullets.Single();
            Vector3 start = b.Position, v = b.Velocity;
            Vector3 level = Vector3.Normalize(v - up * Vector3.Dot(v, up));
            float t = 0;
            while (Vector3.Dot(b.Position - start, level) < 50)
            {
                rig.Step();
                t += 1f / CharacterMotor.TickRate;
                Assert.True(rig.Zone.Guns().Bullets.Contains(b), "still flying");
            }
            // Below the straight line it left along, at 50 m out.
            float along = Vector3.Dot(b.Position - start, level) / Vector3.Dot(v, level);
            return Vector3.Dot(start + v * along - b.Position, up);
        }
        float rocket = Drop(1);
        rig.Step(30);
        FireRig.Reload(rig, ada, 2, "rocket-hv");
        float hv = Drop(3);
        Fixtures.Report($"over 50 m: the rocket drops {rocket:0.00} m, the high-velocity rocket {hv:0.00} m");
        Assert.True(rocket > 8 && hv < 1.5f && rocket > 6 * hv, "the regular rocket drops far more");
    }

    /// <summary>
    /// IRON_WAKE.md §7, the network: a fire lit near a player reaches their client (FireStarted: what left it, where, how
    /// big, what holds it, when it burns out); put out, it ends there (FireEnded, out); a player far away hears nothing.
    /// </summary>
    [Test]
    public static void FiresReachClientsAndEndThere()
    {
        using var rig = new LoopbackRig();
        GameContent c = ContentFixture.Content;
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport Bo 0 600");
        rig.Run(1, (_, _) => default);
        var started = new List<ClientFire>();
        var ended = new List<(ClientFire Fire, bool Out)>();
        ada.Fires().Started += (f, _) => started.Add(f);
        ada.Fires().Ended += (f, o) => ended.Add((f, o));
        bool boHeard = false;
        bo.Fires().Started += (_, _) => boHeard = true;
        (EntityId id, Vector3 at, long end) = rig.Server.Inspect(sv =>
        {
            Vector3 ground = sv.Zone.GroundAt(4, 4);
            Fire f = sv.Zone.Fires().Ignite(c.Fire("incendiary")!, ground + sv.Zone.Frame.UpAt(ground) * 0.5f, sv.Zone.Frame.UpAt(ground),
                c.ItemByKey("rocket-incendiary"), ada.EntityId, "Ada")!;
            return (f.Id, f.Position, f.EndTick - f.StartTick);
        });
        var watch = Stopwatch.StartNew();
        while (started.Count == 0)
        {
            Assert.True(watch.Elapsed.TotalSeconds < 5, "the fire reached Ada's client");
            rig.Run(0.05, (_, _) => default);
        }
        ClientFire fire = started[0];
        Assert.True(fire.Id == id && fire.Item.Key == "rocket-incendiary" && Math.Abs(fire.Radius - 3.5f) < 0.05f && fire.Owner == ada.NetId,
            $"what it is ({fire})");
        Assert.True(Vector3.Distance(fire.Position, at) < 0.05f && fire.Link.Support.Family == TargetFamily.Ground && fire.EndTick - fire.StartTick == end,
            $"where, on what, until when ({Vector3.Distance(fire.Position, at):0.000} m off; {fire.EndTick - fire.StartTick} ticks)");
        rig.Server.Inspect(sv =>
        {
            sv.Zone.Fires().PutOut(sv.Zone.Fires().FireOf(id)!);
            return 0;
        });
        watch.Restart();
        while (ended.Count == 0)
        {
            Assert.True(watch.Elapsed.TotalSeconds < 5, "the fire's end reached Ada's client");
            rig.Run(0.05, (_, _) => default);
        }
        Assert.True(ended[0].Fire.Id == id && ended[0].Out && ada.Fires().All.Count == 0, "it went out there");
        Assert.True(!boHeard, "Bo, 600 m away, heard nothing of it");
        Fixtures.Report($"Ada's client: {fire}; out after {watch.Elapsed.TotalMilliseconds:0} ms");
    }
}
