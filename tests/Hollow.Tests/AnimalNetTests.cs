using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Players;

namespace Hollow.Tests;

public static class AnimalNetTests
{
    private static void RunUntil(LoopbackRig rig, Func<bool> done, string what, double seconds = 6)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < seconds, $"timed out waiting for {what}");
            rig.Run(0.05, (_, _) => default);
        }
    }

    /// <summary>
    /// GROUND.md §6.9 over the network: a herd the server places in front of a player appears in her client as animals
    /// of its species (not as characters), interpolated where the server has them; a hit makes one flinch and the herd
    /// run (the client sees the hurt event and the flight); a kill leaves a corpse that stays in view with its harvest
    /// left; when the corpse goes, it leaves the client too.
    /// </summary>
    [Test]
    public static void AnimalsReplicateWithTheirEvents()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        rig.WaitUntilPlaying();
        var events = new List<AnimalEvent>();
        ada.AnimalEvent += events.Add;
        int characters = ada.Entities.Count();

        rig.Server.Execute("spawn deer Ada 3 35");
        RunUntil(rig, () => ada.Animals.Count == 3, "three deer in view");
        Assert.True(ada.Animals.All(a => a.SpeciesKey == "deer" && a.Species?.Key == "deer" && !a.Dead), "deer, alive");
        Assert.Equal(characters, ada.Entities.Count(), "animals aren't characters");
        Assert.Equal(3, events.Count(e => e.Kind == AnimalEventKind.Appeared), "each appeared once");
        rig.Run(0.5, (_, _) => default);
        Dictionary<EntityId, Vector3> server = rig.Server.Inspect(sv => sv.Zone.Animals!.All.ToDictionary(a => a.Id, a => a.Position));
        foreach (ClientAnimal a in ada.Animals)
        {
            float off = Vector3.Distance(a.Position, server[a.Id]);
            Assert.True(off < 1.5f, $"{a} drawn {off:0.00} m from the server's (100 ms behind)");
        }

        // A hit: it flinches, the herd runs.
        ClientAnimal target = ada.Animals.First();
        rig.Server.Inspect(sv =>
        {
            sv.Zone.TryFindPlayer(new EntityId(ada.EntityId.Value), out _, out Player p);
            Animal deer = sv.Zone.Animals!.Find(target.Id)!;
            return sv.Zone.DamageAnimal(deer, 40, HitRegion.Body, deer.Position + new Vector3(0, 1, 30), p, p.Id, p.Name);
        });
        RunUntil(rig, () => events.Any(e => e.Kind == AnimalEventKind.Hurt && e.Animal == target), "the flinch");
        RunUntil(rig, () => ada.Animals.Count(a => a.State == AnimalState.Flee) == 3, "the herd's flight");
        Assert.True(events.Count(e => e.Kind == AnimalEventKind.Fled) >= 3, "each one's fright");
        Assert.Near(110 / 150.0, target.Health, 0.01, "its health as the server has it");
        RunUntil(rig, () => ada.Animals.Max(a => a.Speed) > 7, "running");

        // A kill: a corpse in view, then gone with its timer.
        rig.Server.Inspect(sv =>
        {
            Animal deer = sv.Zone.Animals!.Find(target.Id)!;
            return sv.Zone.DamageAnimal(deer, 500, HitRegion.Head, deer.Position, null);
        });
        RunUntil(rig, () => target.Dead, "the death");
        Assert.True(events.Any(e => e.Kind == AnimalEventKind.Died && e.Animal == target), "the death event");
        Assert.Near(1, target.Health, 0.01, "a whole corpse to harvest");
        rig.Server.Inspect(sv =>
        {
            Animal corpse = sv.Zone.Animals!.Find(target.Id)!;
            corpse.DespawnTick = sv.Zone.Tick + 3;
            return 0;
        });
        RunUntil(rig, () => ada.AnimalOf(target.NetId) == null, "the corpse leaving");
        Assert.True(events.Any(e => e.Kind == AnimalEventKind.Gone && e.Animal == target), "the gone event");
        Fixtures.Report($"{events.Count} events: " + string.Join(", ", events.GroupBy(e => e.Kind).Select(g => $"{g.Count()} {g.Key}")));
    }

    /// <summary>
    /// GROUND.md §6.9, persistence: animals are never stored, but a corpse survives a restart with what is left to harvest
    /// and its timer (world time, paused while the server was down); living animals don't come back from storage.
    /// </summary>
    [Test]
    public static void CorpsesSurviveARestart()
    {
        using var file = new TempDatabase();
        EntityId corpseId = default;
        long ticksLeft = 0;
        float left = 0;
        using (var rig = new LoopbackRig(file.Path))
        {
            GameClient ada = rig.Join("Ada");
            rig.WaitUntilPlaying();
            rig.Server.Execute("spawn wolf Ada 2 20");
            RunUntil(rig, () => ada.Animals.Count == 2, "two wolves");
            (corpseId, ticksLeft, left) = rig.Server.Inspect(sv =>
            {
                Animal wolf = sv.Zone.Animals!.All.First();
                sv.Zone.DamageAnimal(wolf, 500, HitRegion.Body, wolf.Position, null);
                sv.Zone.Animals.Harvest(wolf, 40);
                return (wolf.Id, wolf.DespawnTick - sv.Zone.Tick, wolf.HarvestLeft);
            });
            rig.Server.SaveAll().Wait();
        }
        using (var rig = new LoopbackRig(file.Path))
        {
            var report = rig.Server.Inspect(sv => (sv.Zone.Animals!.All.Select(a => (a.Id, a.IsDead, a.Species.Key, a.HarvestLeft, a.DespawnTick - sv.Zone.Tick)).ToList()));
            Fixtures.Report("after the restart: " + string.Join(", ", report.Select(r => $"{r.Key} {(r.IsDead ? "corpse" : "alive")} " +
                $"{r.HarvestLeft:0} left, {r.Item5 / 30.0:0} s")));
            Assert.Equal(1, report.Count, "only the corpse came back");
            (EntityId id, bool dead, string key, float harvest, long timer) = report[0];
            Assert.True(id == corpseId && dead && key == "wolf", "the same corpse");
            Assert.Near(left, harvest, 1e-3, "with its harvest left");
            Assert.InRange(ticksLeft - 90, ticksLeft, timer, "and its timer, paused while the server was down");
        }
    }
}
