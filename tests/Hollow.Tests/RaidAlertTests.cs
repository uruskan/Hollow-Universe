using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Server.Notifications;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>Who is "online" for a notification service in a <see cref="GameRig"/>: those awake; what they were sent; what was pushed.</summary>
public sealed class RigPresence(GameRig rig) : IPlayerPresence, IPushChannel
{
    public List<(EntityId Player, RaidAlert Alert)> Delivered { get; } = new();

    public List<(EntityId Player, RaidAlert Alert)> Pushed { get; } = new();

    public bool Deliver(EntityId player, RaidAlert alert)
    {
        if (!rig.Zone.TryFindPlayer(player, out _, out Player p) || p.Sleeping)
        {
            return false;
        }
        Delivered.Add((player, alert));
        return true;
    }

    public void Push(EntityId player, RaidAlert alert) => Pushed.Add((player, alert));
}

public static class RaidAlertTests
{
    // The zone's raid hits of the step to the service, as the server does after each step.
    private static void Route(GameRig rig, NotificationService service)
    {
        foreach (RaidHit hit in rig.Zone.RaidHits)
        {
            service.Raid(hit);
        }
        rig.Zone.RaidHits.Clear();
        service.Tick(rig.Zone.UnixMilliseconds);
    }

    /// <summary>
    /// GROUND.md §6.6, raid alerts: a charge on a claimed hut alerts both players its claim device lists, the one playing
    /// in the game and the one asleep (offline) through the queue (and the push seam); more hits within the base's
    /// minute are summed into one alert when the minute is over; the queued alerts come out once, at login, with the
    /// base, the time and what was hit. Blows by someone the claim lists aren't a raid, by a stranger they are; an
    /// unclaimed structure alerts nobody.
    /// </summary>
    [Test]
    public static void AlertsReachOnlineAndOfflineOwners()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, -6);
        Entity bo = rig.Add("Bo", 2, -6);
        Entity cy = rig.Add("Cy", -2, -6);
        TestBases.Hut hut = Raids.Hut(rig, 0, 0, owner: rig.P(ada).Id.Value);
        Assert.True(rig.Zone.Building!.Authorise(rig.P(bo).Id.Value, hut.Claim!).Ok, "Bo is listed too");
        rig.Zone.Sleep(bo);
        var presence = new RigPresence(rig);
        var store = new MemoryNotificationStore();
        var service = new NotificationService(presence, store, c.RaidAlerts, presence);
        BlastStats charge = c.Blast("timed-charge")!;

        // A charge on the east wall: one alert at once, to Ada in the game and queued (and pushed) for Bo.
        (Vector3 face, Vector3 outward) = hut.Face(hut.Walls[1]);
        rig.Zone.Blast(charge, face + outward * Zone.StuckOffset, outward, hut.Walls[1].Ref, TargetFamily.Piece, rig.P(cy).Id, "Cy", c.ItemByKey("timed-charge").Id);
        Assert.Equal(1, rig.Zone.RaidHits.Count, "one raid hit");
        Route(rig, service);
        Assert.Equal(1, presence.Delivered.Count, "Ada, playing, got it in the game");
        Assert.True(presence.Delivered[0].Player == rig.P(ada).Id, "Ada");
        Assert.Equal(1, store.Count, "queued for Bo, asleep");
        Assert.True(presence.Pushed.Single().Player == rig.P(bo).Id, "and pushed to him");
        RaidAlert first = presence.Delivered[0].Alert;
        Assert.True(first.Base == hut.Claim!.Id && first.What.Contains("Stone wall", StringComparison.Ordinal), $"which base and what: {first.What}");
        Assert.True(Vector3.Distance(first.Where, hut.Inside) < 2, "where it stands");

        // Within the minute: summed, not sent; after it, one alert with both hits (the second charge breaks the east wall;
        // a rocket on the north wall, which still hides the claim device).
        rig.Zone.UnixMilliseconds += 10_000;
        rig.Zone.Blast(charge, face + outward * Zone.StuckOffset, outward, hut.Walls[1].Ref, TargetFamily.Piece, rig.P(cy).Id, "Cy", c.ItemByKey("timed-charge").Id);
        rig.Zone.UnixMilliseconds += 10_000;
        (Vector3 northFace, Vector3 northOut) = hut.Face(hut.Walls[2]);
        rig.Zone.Blast(c.Blast("rocket")!, northFace + northOut * Zone.StuckOffset, northOut, hut.Walls[2].Ref, TargetFamily.Piece, rig.P(cy).Id, "Cy",
            c.ItemByKey("rocket").Id);
        Assert.True(!hut.Claim!.Gone, "the claim device stands");
        Route(rig, service);
        Assert.Equal(1, presence.Delivered.Count, "rate limited: nothing more within the minute");
        rig.Zone.UnixMilliseconds += 45_000;
        service.Tick(rig.Zone.UnixMilliseconds);
        Assert.Equal(2, presence.Delivered.Count, "the follow-up after the minute");
        RaidAlert second = presence.Delivered[1].Alert;
        Assert.True(second.Hits >= 2 && second.Destroyed == 1 && second.What.StartsWith("Stone wall broken", StringComparison.Ordinal),
            $"two blasts summed, the wall broke: {second.Hits} hits, {second.Destroyed} broken, \"{second.What}\"");
        Assert.True(second.FirstUnixMs < second.LastUnixMs, "from the first hit to the last");

        // Blows: by Ada (listed) nothing; by Cy (a stranger), soft-siding the north wall, a raid.
        Piece north = hut.Walls[2];
        (Vector3 inside, Vector3 inward) = hut.Face(north, inside: true);
        rig.Zone.DamageBuildingBy(north, -inward, 10, projectile: false, rig.P(ada), c.ItemByKey("rock").Id);
        Assert.Equal(0, rig.Zone.RaidHits.Count, "the owner's own blows are no raid");
        rig.Zone.DamageBuildingBy(north, -inward, 10, projectile: false, rig.P(cy), c.ItemByKey("rock").Id);
        Assert.Equal(1, rig.Zone.RaidHits.Count, "a stranger's are");
        rig.Zone.RaidHits.Clear();

        // An unclaimed structure: nobody to tell.
        TestBases.Hut open = TestBases.Build(rig.Zone, rig.Zone.GroundAt(0, 7), 0, 2, 1, claim: false)!;
        (Vector3 openFace, Vector3 openOut) = open.Face(open.Walls[1]);
        rig.Zone.Blast(charge, openFace + openOut * Zone.StuckOffset, openOut, open.Walls[1].Ref, TargetFamily.Piece, rig.P(cy).Id, "Cy", c.ItemByKey("timed-charge").Id);
        Assert.Equal(0, rig.Zone.RaidHits.Count, "no claim, no alert");

        // Bo logs in: his two alerts, once.
        IReadOnlyList<RaidAlert> queued = service.TakeQueued(rig.P(bo).Id, rig.Zone.UnixMilliseconds).Result;
        Assert.Equal(2, queued.Count, "both alerts waited for Bo");
        Assert.True(queued[0].LastUnixMs <= queued[1].FirstUnixMs, "oldest first");
        Assert.Equal(0, service.TakeQueued(rig.P(bo).Id, rig.Zone.UnixMilliseconds).Result.Count, "and only once");
        Fixtures.Report($"alerts: \"{first.What}\"; then \"{second.What}\" ({second.Hits} hits); {service.Counters}");
    }

    /// <summary>
    /// GROUND.md §6.6, a whole offline raid (ARCHITECTURE.md §10.4: offline raids are live): Ada logs off asleep in her
    /// stone hut with a sheet metal door, its claim device stocked with stone. Two hours pass: upkeep is paid from the
    /// claim device's store, an unclaimed twig foundation nearby decays away and her hut doesn't. Then Bo, whom the claim
    /// doesn't list, breaks the door with a timed charge and kills her sleeper with a second one inside, which breaks her
    /// claim device too; he loots her body. The alerts were queued for Ada (the second hit summed into a follow-up after
    /// the base's minute) and she gets them when she logs in, dead, with the base, the time and what was broken.
    /// </summary>
    [Test]
    public static void AWholeOfflineRaid()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, -6);
        Entity bo = rig.Add("Bo", 0, 9);
        TestBases.Hut hut = Raids.Hut(rig, 0, 0, owner: rig.P(ada).Id.Value);
        ClaimDevice claim = hut.Claim!;
        Container upkeep = rig.Zone.DeployableOf(claim)!.Storage!;
        upkeep.Set(0, ItemStack.Of(c.ItemByKey("stone"), 500));
        int Stone() => InventoryRules.Count(new[] { upkeep }, c.ItemByKey("stone").Id);
        BuildingWorld world = rig.Zone.Building!;
        Vector3 twigAt = rig.Zone.GroundAt(9, -6);
        rig.Zone.Physics.EnsureGround(twigAt);
        Piece twig = world.AddFirst(TestBases.FrameOver(rig.Zone, c.Building, c.Building.Piece("foundation")!, twigAt, 0), c.Building.Piece("foundation")!, 0, 99,
            rig.Zone.BuildingNow);
        var presence = new RigPresence(rig);
        var store = new MemoryNotificationStore();
        var service = new NotificationService(presence, store, c.RaidAlerts, presence);
        rig.Give(ada, "metal-fragments", 300);

        // Ada lies down inside and logs off. Two hours of upkeep and decay pass.
        Raids.Stand(rig, ada, hut.ToZone(new(-0.5, 0, 0.1)));
        rig.Step(10);
        rig.Zone.Sleep(ada);
        double wallHealth = hut.Walls[1].Health;
        rig.Zone.UnixMilliseconds += 2 * 3_600_000;
        rig.Step();
        int paid = 500 - Stone();
        Assert.True(paid > 0, $"upkeep paid from the claim device ({Stone()} stone left)");
        Assert.True(twig.Gone, "the unclaimed twig foundation decayed away");
        Assert.True(!hut.Walls[1].Gone && hut.Walls[1].Health == wallHealth, "the hut, paid up, doesn't decay");

        // Bo, at the door: a timed charge on it.
        (Vector3 doorFace, Vector3 doorOut) = hut.Face(hut.Doorway, height: 1.1);
        Raids.Stand(rig, bo, rig.Zone.GroundAt(doorFace.X + doorOut.X * 1.3f, doorFace.Z + doorOut.Z * 1.3f));
        Raids.Belt(rig, bo, 0, "timed-charge", 2);
        rig.Step(2);
        rig.Look(bo, doorFace + doorOut * 0.2f);
        Assert.Equal(OutcomeCode.Done, rig.PlaceAimed(bo, ContainerRef.Belt, 0, "timed-charge"), "a charge on the door");
        Assert.True(rig.Zone.Explosives.Single().Link.Support.Whole == hut.Door!.Ref, "stuck to the door");
        Raids.Stand(rig, bo, rig.Zone.GroundAt(doorFace.X + doorOut.X * 8, doorFace.Z + doorOut.Z * 8));
        Raids.StepUntil(rig, () => { Route(rig, service); return hut.Door!.Gone; }, 320, "the door breaking");
        Assert.Equal(0, presence.Delivered.Count, "nobody to tell in the game");
        Assert.Equal(1, store.Count, "the alert waits for Ada");
        Assert.True(!rig.P(ada).Dead && rig.P(ada).Vitals.Health == 100, "the door shielded her");

        // In through the doorway: the second charge on the floor beside her (and a metre from the claim device).
        Raids.Stand(rig, bo, rig.Zone.GroundAt(doorFace.X + doorOut.X * 0.5f, doorFace.Z + doorOut.Z * 0.5f));
        rig.Step(30);
        Vector3 beside = hut.ToZone(new(0.3, 0, 0.7));
        rig.Look(bo, beside);
        Assert.Equal(OutcomeCode.Done, rig.PlaceAimed(bo, ContainerRef.Belt, 0, "timed-charge"), "a charge beside her");
        Assert.True(rig.Zone.Explosives.Single().Link.Support.Whole == hut.Foundation.Ref, "on the floor");
        Raids.Stand(rig, bo, rig.Zone.GroundAt(doorFace.X + doorOut.X * 8, doorFace.Z + doorOut.Z * 8));
        long before = rig.Zone.UnixMilliseconds;
        Raids.StepUntil(rig, () => { Route(rig, service); return rig.Zone.Explosives.Count == 0; }, 320, "the second charge");
        Assert.True(rig.P(ada).Dead, "her sleeper died");
        Assert.True(claim.Gone, "her claim device broke");
        Corpse body = rig.Zone.Statics().OfType<Corpse>().Single(x => x.Of == rig.P(ada).Id);
        Assert.Equal(300, InventoryRules.Count(new[] { body.Loot }, c.ItemByKey("metal-fragments").Id), "her things are in her body");
        Assert.Equal(1, store.Count, "the second hit waits for the base's minute");

        // Bo loots. The base's minute passes.
        Raids.Stand(rig, bo, body.Position);
        rig.Step(2);
        Assert.Equal(OutcomeCode.Done, rig.Zone.Interact(bo, body.Ref, "loot"), "Bo opens her body");
        rig.Zone.UnixMilliseconds = before + 61_000;
        Route(rig, service);
        Assert.Equal(2, store.Count, "the follow-up alert queued");

        // Ada logs in: dead, with her alerts.
        rig.Zone.Wake(ada);
        IReadOnlyList<RaidAlert> alerts = service.TakeQueued(rig.P(ada).Id, rig.Zone.UnixMilliseconds).Result;
        Assert.Equal(2, alerts.Count, "two alerts");
        Assert.True(alerts.All(a => a.Base == claim.Id), "both for her base");
        Assert.True(alerts[0].What.StartsWith("Sheet Metal Door broken", StringComparison.Ordinal), $"the first: {alerts[0].What}");
        Assert.True(alerts[1].What.StartsWith("Tool Cupboard broken", StringComparison.Ordinal) && alerts[1].FirstUnixMs >= alerts[0].LastUnixMs,
            $"the second: {alerts[1].What}");
        Assert.True(rig.P(ada).Dead, "she logs in dead");
        Fixtures.Report($"Ada's alerts: {string.Join("; ", alerts.Select(a => $"{DateTimeOffset.FromUnixTimeMilliseconds(a.LastUnixMs):HH:mm:ss} {a.What} ({a.Hits} hits)"))}; " +
            $"upkeep paid {paid} stone");
    }
}
