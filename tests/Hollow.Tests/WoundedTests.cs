using System;
using System.Linq;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class WoundedTests
{
    /// <summary>
    /// GROUND.md §6.3, the wounded state (Rust's): a lethal body shot knocks the player down with a little health instead
    /// of killing them; down they crawl slowly and can't act, and are helped up by another player with the interact key.
    /// Shot again while down they die (finished off). A shot to the head, or a hit of 75 or more, kills outright. When the
    /// time runs out they get up by themselves or die, as their roll says.
    /// </summary>
    [Test]
    public static void DownHelpedUpFinishedOffAndBledOut()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        SurvivalTuning s = c.Survival;
        Entity ada = rig.Add("Ada", 12, -20);
        Entity bo = rig.Add("Bo", 13, -20);
        Player a = rig.P(ada), b = rig.P(bo);
        rig.Give(bo, "wood", 100);

        // Down: a lethal body shot from Ada.
        b.Vitals.Health = 20;
        rig.Zone.Guns().ShootCharacter(bo, b, Guns.Round(c, "rifle", "rifle-ammo", a.Id), HitRegion.Body, 5, Vector3.Zero, a);
        Assert.True(b.Wounded && !b.Dead, "knocked down, not dead");
        Assert.InRange(s.WoundedHealthLeast, s.WoundedHealthMost, b.Vitals.Health, "a little health");
        Assert.Equal(Stance.Crawling, rig.Ch(bo).State.Stance, "crawling");
        Assert.InRange(rig.Zone.Tick + s.WoundedSecondsLeast * 30 - 1, rig.Zone.Tick + s.WoundedSecondsMost * 30 + 1, b.WoundedUntilTick, "40-50 s");
        Assert.True(rig.Zone.Events.Of<HitConfirmed>().Any(e => e.By == a.Id && e.Downed), "Ada's marker says downed");
        Assert.Equal(OutcomeCode.Dead, rig.Zone.Actions().Craft(bo, c.ItemByKey("campfire").Id, 1, out _), "the wounded can't act");
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(ada, b.Ref, "loot"), "the wounded can be looted");
        rig.Zone.Actions().Close(ada);

        // Crawling: slow, no jumping, the stance kept.
        Vector3 start = rig.Ch(bo).State.Position;
        rig.Step(60, () => rig.Ch(bo).Input = new MoveInput { Forward = 1, Buttons = MoveButtons.Sprint | MoveButtons.JumpPressed | MoveButtons.Jump });
        float crawled = Vector3.Distance(start, rig.Ch(bo).State.Position);
        Fixtures.Report($"down with {b.Vitals.Health:0} health; crawled {crawled:0.00} m in 2 s");
        Assert.InRange(1.0, 2 * MovementTuning.Default.CrawlSpeed + 0.05, crawled, "crawls about 0.8 m/s, no faster sprinting");
        Assert.Equal(Stance.Crawling, rig.Ch(bo).State.Stance, "still down");

        // Helped up by Ada (within reach), back on his feet with the health he had.
        float downHealth = b.Vitals.Health;
        rig.Zone.Teleport(ada, rig.Zone.Frame.Beside(rig.Ch(bo).State.Position, 1, 0, 0));
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interaction().Interact(ada, b.Ref, "help-up"), "help up");
        Assert.True(!b.Wounded && !b.Dead && b.Vitals.Health == downHealth, "up again");
        rig.Step(2, () => rig.Ch(bo).Input = default);
        Assert.Equal(Stance.Standing, rig.Ch(bo).State.Stance, "standing where there is room");
        Assert.Equal(OutcomeCode.NotUsable, rig.Zone.Interaction().Interact(ada, b.Ref, "help-up"), "nobody to help now");

        // Finished off: down again, and one more hit kills.
        b.Vitals.Health = 10;
        rig.Zone.Guns().ShootCharacter(bo, b, Guns.Round(c, "pistol", "pistol-ammo", a.Id), HitRegion.Limb, 5, Vector3.Zero, a);
        Assert.True(b.Wounded, "down again");
        rig.Zone.Guns().ShootCharacter(bo, b, Guns.Round(c, "pistol", "pistol-ammo", a.Id), HitRegion.Limb, 5, Vector3.Zero, a);
        Assert.True(b.Dead && !b.Wounded, "finished off");
        Assert.Equal(100, InventoryRules.Count(new[] { rig.Zone.Things().Statics().OfType<Corpse>().Single().Loot }, c.ItemByKey("wood").Id), "a corpse");

        // Outright: a shot to the head, a hit of 75 or more, the cold.
        Entity cy = rig.Add("Cy", 15, -22);
        Player cyp = rig.P(cy);
        cyp.Vitals.Health = 30;
        rig.Zone.Guns().ShootCharacter(cy, cyp, Guns.Round(c, "rifle", "rifle-ammo", a.Id), HitRegion.Head, 5, Vector3.Zero, a);
        Assert.True(cyp.Dead && !cyp.Wounded, "a head shot kills");
        Entity dee = rig.Add("Dee", 16, -22);
        Player d = rig.P(dee);
        d.Vitals.Health = 10;
        rig.Zone.Players().Hurt(dee, d, s.WoundMaxDamage, "Test", EntityId.None, "");
        Assert.True(d.Dead, "a hit of 75 kills");

        // Their time runs out: up by themselves, or dead of it, as their roll says.
        foreach (string name in new[] { "Eve", "Fay", "Gus", "Hal" })
        {
            Entity e = rig.Add(name, 18 + name.Length, -24);
            Player p = rig.P(e);
            p.Vitals.Health = 5;
            p.Vitals.Calories = name == "Eve" ? s.MaxCalories : 0;
            p.Vitals.Hydration = name == "Eve" ? s.MaxHydration : 0;
            rig.Zone.Players().Hurt(e, p, 20, "Fell", EntityId.None, "");
            Assert.True(p.Wounded, $"{name} down");
            bool recovers = rig.Zone.Wounded().RecoversByThemselves(p);
            rig.Step((int)(p.WoundedUntilTick - rig.Zone.Tick) + 1);
            Fixtures.Report($"{name} ({(name == "Eve" ? "fed and watered" : "starving")}): {(recovers ? "got up" : "died")}");
            Assert.True(recovers ? !p.Wounded && !p.Dead : p.Dead && p.DeathCause == "Fell", $"{name} as the roll says");
        }

        // A wounded player who logs off dies.
        Entity ivy = rig.Add("Ivy", 25, -25);
        Player iv = rig.P(ivy);
        iv.Vitals.Health = 5;
        rig.Zone.Players().Hurt(ivy, iv, 10, "Struck down", a.Id, "Ada");
        rig.Zone.Players().Sleep(ivy);
        Assert.True(iv.Dead && iv.KilledBy == "Ada", "dies on logging off, killed by whoever downed them");
    }
}
