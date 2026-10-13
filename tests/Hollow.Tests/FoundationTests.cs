using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>The ground game's shared foundations (GROUND_SYSTEMS.md §3, package P0), engine-free.</summary>
public static class FoundationTests
{
    /// <summary>
    /// GROUND_SYSTEMS.md §3.2: one answer vocabulary. Every code has a line in texts.json, a line names its answer's
    /// detail (an item, a number), and a texts file that misses a code, names one that doesn't exist or uses a placeholder
    /// nobody fills is reported, while it stays out of the data's hash.
    /// </summary>
    [Test]
    public static void EveryAnswerHasALine()
    {
        GameContent c = ContentFixture.Content;
        Texts texts = c.Texts;
        foreach (OutcomeCode code in Enum.GetValues<OutcomeCode>().Where(code => code != OutcomeCode.Done))
        {
            Assert.True(texts.Line(code).Length > 0, $"{code} has a line");
        }
        Assert.Equal("", texts.Line(Outcome.Done), "done says nothing");
        Assert.Equal("Not enough wood", texts.Line(OutcomeCode.NotEnough.With(c.ItemByKey("wood").Id)), "an item, in lower case");
        Assert.Equal("Not enough resources", texts.Line(OutcomeCode.NotEnough), "the word for none");
        Assert.Equal("You need a hammer", texts.Line(OutcomeCode.NoTool.With(c.ItemByKey("hammer").Id)), "a tool");
        Assert.Equal("Wait 5 s before trying another code", texts.Line(OutcomeCode.WaitForCode.With(5)), "a number");
        Assert.Equal("Needs a level 2 workbench nearby", texts.Line(OutcomeCode.NeedsWorkbench.With(2)), "a level");
        Assert.True(OutcomeCode.Stale == new Outcome(OutcomeCode.Stale, 3) && !new Outcome(OutcomeCode.Stale).Ok && Outcome.Done.Ok,
            "an answer is its code, whatever its detail");
        Assert.True((int)Enum.GetValues<OutcomeCode>().Max() < 128, "the codes fit IntentResult's 7 bits");

        Dictionary<string, string> files = GameContent.Files.Concat(GameContent.Unhashed)
            .ToDictionary(f => f, f => File.ReadAllText(Path.Combine(ContentFixture.Folder, f)));
        ulong hash = GameContent.FromTexts(files).Hash;
        files[Texts.File] = files[Texts.File]
            .Replace("\"Stale\": \"That item has moved\",", "\"Stail\": \"That item has moved\",", StringComparison.Ordinal)
            .Replace("\"Wait {n} s before", "\"Wait {seconds} s before", StringComparison.Ordinal);
        GameContent broken = GameContent.FromTexts(files);
        string all = string.Join("\n", broken.Problems);
        Fixtures.Report($"{broken.Problems.Count} problems, e.g. {broken.Problems.FirstOrDefault()}");
        Assert.True(all.Contains("outcomes.Stail: no such answer code", StringComparison.Ordinal), "a line for no code");
        Assert.True(all.Contains("the answer Stale has no line", StringComparison.Ordinal), "a code without a line");
        Assert.True(all.Contains("unknown placeholder {seconds}", StringComparison.Ordinal), "a placeholder nobody fills");
        Assert.Equal(hash, broken.Hash, "texts aren't in the data's hash: a translated client may join any server");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §3.1: one way to name a world thing. Every family is registered once and answers for its own: a
    /// player, an animal, a tree, a box, a hut's pieces, door and claim device, an explosive, a ship and the ground each
    /// exist, resolve to their object, have a pose and a nearest point on their surface (a body's bone too); an ID the
    /// family never gave, or one of a thing that went, names nothing; a damage request routes by family.
    /// </summary>
    [Test]
    public static void EveryFamilyNamesItsThings()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, 0);
        TestBases.Hut hut = Raids.Hut(rig, 12, 0, door: "wooden-door");
        Vector3 boxAt = z.GroundAt(-6, 4);
        var box = new Deployable(z.NextId(), c.Deployable("large-box")!, boxAt, z.Frame.UpAt(boxAt), 0.3f, rig.P(ada).Id);
        z.AddStatic(box);
        Herd herd = AnimalRig.Herd(rig, "deer", -20, -20, 1, 5);
        Animal deer = herd.Members[0];
        (Vector3 face, Vector3 outward) = hut.Face(hut.Walls[1]);
        LiveExplosive charge = z.StickExplosive(c.ItemByKey("timed-charge").Throwable!, face + outward * 1.5f, -outward, 3, rig.P(ada).Id, "Ada", dud: true)!;
        Ship ship = z.SpawnShip(c.Ship("tin-finch")!, z.GroundAt(30, 30), 0, rig.P(ada).Id, 10);
        var nearby = new List<NaturalObject>();
        z.Nature!.Query(-80, -80, 80, 80, nearby);
        NaturalObject tree = nearby.First(o => c.Natural(o.Kind)?.Class == NaturalClass.Tree);

        var named = new (TargetRef Target, object? Thing, string What)[]
        {
            (rig.P(ada).Ref, rig.P(ada), "a player"),
            (deer.Ref, deer, "a deer"),
            (new TargetRef(TargetFamily.Natural, tree.Id), null, "a tree"),
            (box.Ref, box, "a box"),
            (hut.Walls[1].Ref, hut.Walls[1], "a wall"),
            (hut.Door!.Ref, hut.Door, "a door"),
            (hut.Claim!.Ref, hut.Claim, "the claim device"),
            (charge.Ref, charge, "a charge"),
            (ship.Ref, ship, "a ship"),
            (TargetRef.Ground, null, "the ground"),
        };
        Assert.Equal(Enum.GetValues<TargetFamily>().Length - 1, named.Select(n => n.Target.Family).Distinct().Count(), "every family named once");
        foreach ((TargetRef target, object? thing, string what) in named)
        {
            Assert.True(z.Exists(target), $"{what} exists");
            Assert.True(thing == null || ReferenceEquals(z.Resolve(target), thing), $"{what} resolves to itself");
            Assert.True(z.TryPose(target, out TargetPose pose) && float.IsFinite(pose.Position.X), $"{what} has a pose");
            Assert.True(z.FamilyOf(target.Family)!.Family == target.Family, $"{what}'s family answers for it");
            if (target.Family == TargetFamily.Ground)
            {
                continue;
            }
            // From 12 m to the side: the nearest point is on the thing, nearer than where it stands, not where we stand.
            Vector3 up = z.Frame.UpAt(pose.Position), side = Vector3.Normalize(Vector3.Cross(up, Vector3.UnitZ));
            Vector3 from = pose.Position + side * 12 + up * 0.5f;
            Assert.True(z.TryNearest(target, from, out Vector3 near), $"{what} has a nearest point");
            float toNear = Vector3.Distance(from, near), toBase = Vector3.Distance(from, pose.Position);
            Assert.True(toNear < toBase + 1e-3f && toNear > 0.5f, $"{what}: nearest point {toNear:0.00} m, base {toBase:0.00} m");
            Assert.True(!z.Exists(new TargetRef(target.Family, 0xDEAD_0000_0000_BEEF)) && z.Resolve(new TargetRef(target.Family, 0xDEAD_0000_0000_BEEF)) == null,
                $"a made-up {target.Family} ID names nothing");
        }
        Assert.True(z.FamilyOf((TargetFamily)200) == null && !z.Exists(new TargetRef((TargetFamily)200, 1)), "a family number from outside that names none");

        // A body's bones: the head is above the feet; nearest points of the head and the whole body differ.
        Assert.True(z.TryPose(rig.P(ada).Ref with { Part = 1 }, out TargetPose head), "Ada's head bone");
        Assert.True(z.TryPose(rig.P(ada).Ref, out TargetPose feet), "Ada's feet");
        Assert.True(Vector3.Dot(head.Position - feet.Position, z.Frame.UpAt(feet.Position)) > 1.4f, "Ada's head bone is up at her head");
        Assert.True(z.TryPose(deer.Ref with { Part = 3 }, out TargetPose leg) && Vector3.Distance(leg.Position, deer.Position) < 2, "the deer's leg bone");

        // Things that go are gone from their family.
        z.RemoveStatic(box);
        Assert.True(!z.Exists(box.Ref) && z.Resolve(box.Ref) == null, "a box picked up");
        z.DamageBuilding(hut.Door, Vector3.UnitZ, 1_000_000, projectile: false);
        Assert.True(!z.Exists(hut.Door.Ref), "a door broken");

        // The damage pipeline routes by family: a blow on the wall's TargetRef reaches the building's receiver.
        double before = hut.Walls[1].Health;
        z.DamageBuildingBy(hut.Walls[1], Vector3.UnitX, 50, projectile: false, attacker: null, weapon: 0);
        Assert.True(hut.Walls[1].Health < before, "the wall took the blow through its family's receiver");
        Fixtures.Report($"{named.Length} families named; Ada's head {Vector3.Distance(head.Position, feet.Position):0.00} m above her feet");
    }
}
