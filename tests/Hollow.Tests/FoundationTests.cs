using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Teams;
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

    /// <summary>
    /// GROUND_SYSTEMS.md §3.4: one reach rule. Reach runs from the eye to a thing's nearest surface, within a reach from
    /// data: a box opens from 3.4 m of its side and not from 3.6 m; a wall between hides it (a box inside a hut, a dud on the
    /// wall's far side), while what holds a thing doesn't (a door seen past its frame from aside); and the dud is lit
    /// from in front.
    /// </summary>
    [Test]
    public static void ReachIsToTheNearestSurfaceInSight()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        GameContent c = rig.C;
        SurvivalTuning s = c.Survival;
        Entity ada = rig.Add("Ada", 30, 30);
        Vector3 Eye() => z.EyeOf(rig.Ch(ada).State);

        // A box in the open, Ada east of its east side.
        Vector3 boxAt = z.GroundAt(30, 40);
        var box = new Deployable(z.NextId(), c.Deployable("large-box")!, boxAt, z.Frame.UpAt(boxAt), 0, rig.P(ada).Id);
        z.AddStatic(box);
        Vector3 StandFromSide(float gap)
        {
            // Stand so that the eye is gap metres from the box's nearest surface.
            for (float x = 0.5f; x < 8; x += 0.01f)
            {
                Raids.Stand(rig, ada, z.GroundAt(30 + box.Def.HalfExtents.X + x, 40));
                if (Vector3.Distance(box.Nearest(Eye()), Eye()) >= gap)
                {
                    return Eye();
                }
            }
            throw new TestFailure("no place");
        }
        StandFromSide(s.OpenReach - 0.1f);
        Assert.Equal(OutcomeCode.Done, z.Open(ada, box.Id), "opened from 0.1 m inside the reach of its side");
        z.Close(ada);
        Vector3 eye = StandFromSide(s.OpenReach + 0.1f);
        Assert.Equal(OutcomeCode.OutOfReach, z.Open(ada, box.Id), "not from 0.1 m beyond it");
        Fixtures.Report($"reach {s.OpenReach} m to the side: {Vector3.Distance(eye, box.Centre):0.00} m from its centre is too far");

        // A hut: a box inside is hidden from outside by a wall, not from inside.
        TestBases.Hut hut = Raids.Hut(rig, 0, 0, door: "wooden-door");
        Vector3 inside = hut.Inside;
        var chest = new Deployable(z.NextId(), c.Deployable("large-box")!, inside, z.Frame.UpAt(inside), 0, rig.P(ada).Id);
        z.AddStatic(chest);
        (Vector3 face, Vector3 outward) = hut.Face(hut.Walls[1], height: 0);
        Raids.Stand(rig, ada, z.Settle(face + outward * 1.2f));
        Assert.True(Vector3.Distance(chest.Nearest(Eye()), Eye()) < s.OpenReach, "the box inside is within reach");
        Assert.Equal(OutcomeCode.OutOfSight, z.Open(ada, chest.Id), "but the wall hides it");
        Raids.Stand(rig, ada, z.Settle(inside + hut.Direction(new(0, 0, 1)) * 1.0f));
        Assert.Equal(OutcomeCode.Done, z.Open(ada, chest.Id), "from inside it opens");
        z.Close(ada);

        // The door, from aside in front of the doorway: its frame holds it and doesn't hide it.
        Door door = hut.Door!;
        (Vector3 front, Vector3 doorOut) = hut.Face(hut.Doorway, height: 0);
        Vector3 aside = Vector3.Normalize(Vector3.Cross(z.Frame.UpAt(front), doorOut));
        Raids.Stand(rig, ada, z.Settle(front + doorOut * 0.8f + aside * 1.6f));
        Assert.Equal(OutcomeCode.Done, z.DoorAct(ada, DoorAction.Toggle, ContainerRef.Main, 0, 0, door.Id, 0), "the door opens from aside");

        // A dud stuck on the east wall's outside: not lit through the wall, lit from in front.
        (Vector3 east, Vector3 eastOut) = hut.Face(hut.Walls[1], height: 1.0);
        LiveExplosive dud = z.StickExplosive(c.ItemByKey("bag-charge").Throwable!, east + eastOut * 1.5f, -eastOut, 3, EntityId.None, "", dud: true)!;
        Raids.StepUntil(rig, () => dud.Dud, 12 * 30 + 2, "the fuse going out");
        (Vector3 eastIn, Vector3 inward) = hut.Face(hut.Walls[1], height: 0, inside: true);
        Raids.Stand(rig, ada, z.Settle(eastIn + inward * 0.6f));
        Assert.Equal(OutcomeCode.OutOfSight, z.Rearm(ada, dud.Id), "the wall hides the dud");
        Raids.Stand(rig, ada, z.Settle(face + outward * 1.0f));
        Assert.Equal(OutcomeCode.Done, z.Rearm(ada, dud.Id), "lit from in front");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §3.3: viewer flags, made by each thing's capabilities through their registry. A box is Ada's own,
    /// her teammate's to Bo once he joins her team, Empty until it holds something; a fire is Lit; a fresh code lock is
    /// Ada's (she fitted it) with no code; once she sets one it is Locked with a code, Ada on its list and able to pass,
    /// Bo neither; her ship is hers and Locked to everyone. Every capability in capabilities.json has its handler.
    /// </summary>
    [Test]
    public static void ViewerFlagsComeFromCapabilities()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        GameContent c = rig.C;
        Assert.True(CapabilityHandlers.Create(c.Capabilities).Problems.Count == 0 && z.Capabilities.Count == c.Capabilities.Keys.Count,
            "every capability in the data has its handler");
        Entity ada = rig.Add("Ada", 0, -4), bo = rig.Add("Bo", 1, -4);
        Player a = rig.P(ada), b = rig.P(bo);
        ViewerFlags As(Player p, ITargetable thing) => z.FlagsFor(thing.Ref, z.ViewerOf(p));

        Vector3 at = z.GroundAt(6, 6);
        var box = new Deployable(z.NextId(), c.Deployable("large-box")!, at, z.Frame.UpAt(at), 0, a.Id);
        z.AddStatic(box);
        Assert.Equal(ViewerFlags.Mine | ViewerFlags.Empty, As(a, box), "Ada's empty box, to her");
        Assert.Equal(ViewerFlags.Empty, As(b, box), "to Bo: empty, not his");
        box.Storage!.Set(0, ItemStack.Of(c.ItemByKey("wood"), 10));
        Assert.Equal(ViewerFlags.Mine, As(a, box), "no longer empty");
        z.TeamAct(ada, TeamAction.Create, EntityId.None, 0);
        z.TeamAct(ada, TeamAction.Invite, b.Id, 0);
        z.TeamAct(bo, TeamAction.Accept, EntityId.None, z.Teams.TeamOf(a.Id)!.Id);
        Assert.Equal(ViewerFlags.Teammate, As(b, box), "to Bo, his teammate's box");

        Vector3 fireAt = z.GroundAt(-6, 6);
        var fire = new Deployable(z.NextId(), c.Deployable("campfire")!, fireAt, z.Frame.UpAt(fireAt), 0, a.Id) { Lit = true };
        z.AddStatic(fire);
        Assert.True(As(b, fire).HasFlag(ViewerFlags.Lit), "a fire burning is Lit to anyone");

        TestBases.Hut hut = Raids.Hut(rig, 0, 12, owner: 99, door: "wooden-door");
        Door door = hut.Door!;
        Assert.True(z.Building!.Authorise(a.Id.Value, hut.Claim!).Ok && z.Building.AttachCodeLock(a.Id.Value, door, out _).Ok, "Ada fits a code lock");
        Assert.Equal(ViewerFlags.Mine | ViewerFlags.CanUse | ViewerFlags.Authorised, As(a, door), "her fresh lock: hers, open, no code yet");
        Assert.Equal(ViewerFlags.CanUse | ViewerFlags.Teammate, As(b, door), "to Bo: an unlocked door, his teammate's lock");
        Assert.True(z.Building.SetCode(a.Id.Value, door, "1234").Ok, "she sets a code");
        Assert.Equal(ViewerFlags.Mine | ViewerFlags.CanUse | ViewerFlags.Authorised | ViewerFlags.HasCode | ViewerFlags.Locked, As(a, door),
            "locked with a code, Ada on its list and through");
        Assert.Equal(ViewerFlags.HasCode | ViewerFlags.Locked | ViewerFlags.Teammate, As(b, door), "Bo: locked with a code, not his way");
        Assert.True(As(b, hut.Claim!) == ViewerFlags.None && z.Building.Authorise(b.Id.Value, hut.Claim!).Ok &&
            As(b, hut.Claim!) == ViewerFlags.Authorised, "the claim device: Authorised once Bo is on its list");

        Ship ship = z.SpawnShip(c.Ship("tin-finch")!, z.GroundAt(30, 30), 0, a.Id, 10);
        ship.Locked = true;
        Assert.Equal(ViewerFlags.Mine | ViewerFlags.Locked | ViewerFlags.Empty, As(a, ship), "Ada's locked ship, its cargo empty");
        Assert.Equal(ViewerFlags.Teammate | ViewerFlags.Locked | ViewerFlags.Empty, As(b, ship), "to Bo, his teammate's");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §3.3 over the network: the viewer flags each player gets differ, and change with them. Ada's door
    /// with a fresh code lock is hers and has no code on her client (the game offers SET CODE), not on Bo's; after the code
    /// her client shows it locked and hers to pass, Bo's locked to him; her sleeping bag is hers on her client alone, and
    /// shows as a teammate's on Bo's once he joins her team (sent again when only that changed).
    /// </summary>
    [Test]
    public static void ViewerFlagsReachEachPlayer()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport Ada 0 0");
        rig.Server.Execute("teleport Bo 2 -2");
        rig.Server.Execute("give Ada code-lock 1");
        void Until(Func<bool> done, string what)
        {
            var watch = Stopwatch.StartNew();
            while (!done())
            {
                Assert.True(watch.Elapsed.TotalSeconds < 6, $"timed out waiting for {what}");
                rig.Run(0.05, (_, _) => default);
            }
        }
        Until(() => ada.Local.Inventory.Count(ContentFixture.Item("code-lock")) == 1, "the lock");
        ulong adaId = ada.EntityId.Value;
        (EntityId doorId, EntityId bagId, Vector3 front) = rig.Server.Inspect(sv =>
        {
            TestBases.Hut hut = TestBases.Build(sv.Zone, sv.Zone.GroundAt(0, 4), 0, 1, adaId, "wooden-door", claim: false)!;
            Vector3 bagAt = sv.Zone.GroundAt(-3, -1);
            var bag = new Deployable(sv.Zone.NextId(), sv.Content.Deployable("sleeping-bag")!, bagAt, sv.Zone.Frame.UpAt(bagAt), 0, new EntityId(adaId));
            sv.Zone.AddStatic(bag);
            (Vector3 face, Vector3 outward) = hut.Face(hut.Doorway, height: 0);
            return (hut.Door!.Id, bag.Id, face + outward * 1.5f);
        });
        // Ada in front of the door, in reach and in sight of it.
        rig.Server.Execute(FormattableString.Invariant($"teleport Ada {front.X:0.00} {front.Z:0.00}"));
        Until(() => ada.Building!.DoorOf(doorId) != null && bo.Building!.DoorOf(doorId) != null && ada.ThingOf(bagId) != null && bo.ThingOf(bagId) != null,
            "the door and the bag on both clients");
        Assert.True(ada.ThingOf(bagId)!.Mine && !bo.ThingOf(bagId)!.Mine, "the bag is Ada's on her client alone");
        Door adaDoor = ada.Building!.DoorOf(doorId)!, boDoor = bo.Building!.DoorOf(doorId)!;
        int lockSlot = Enumerable.Range(0, ada.Local.Inventory.Main.Size).First(i => ada.Local.Inventory.Main[i].Item == ContentFixture.Item("code-lock").Id);
        ada.Local.AttachLock(ContainerRef.Main, lockSlot, adaDoor);
        Until(() => ada.BuildingReplica!.StateOf(adaDoor).Lock == LockKind.Code && bo.BuildingReplica!.StateOf(boDoor).Lock == LockKind.Code, "the lock on both");
        DoorState mine = ada.BuildingReplica!.StateOf(adaDoor), his = bo.BuildingReplica!.StateOf(boDoor);
        Assert.True(mine is { Mine: true, HasCode: false, CanUse: true } && his is { Mine: false, HasCode: false },
            $"a fresh code lock: Ada's, no code ({mine.Flags}; Bo: {his.Flags})");
        ada.Local.SetCode(adaDoor, "4711");
        Until(() => ada.BuildingReplica!.StateOf(adaDoor).HasCode && bo.BuildingReplica!.StateOf(boDoor).HasCode, "the code on both");
        mine = ada.BuildingReplica!.StateOf(adaDoor);
        his = bo.BuildingReplica!.StateOf(boDoor);
        Assert.True(mine is { Locked: true, Authorised: true, CanUse: true } && his is { Locked: true, Authorised: false, CanUse: false },
            $"locked: Ada passes, Bo doesn't ({mine.Flags}; Bo: {his.Flags})");

        rig.Server.Execute(FormattableString.Invariant($"teleport Bo {front.X + 1.2f:0.00} {front.Z:0.00}"));
        rig.Run(0.5, (_, _) => default);
        ada.Local.CreateTeam();
        Until(() => ada.Local.Team != null, "Ada's team");
        ada.Local.InviteToTeam(bo.EntityId);
        Until(() => bo.Local.TeamInvites.Count > 0, "Bo's invite");
        bo.Local.AcceptInvite(bo.Local.TeamInvites[0].Team);
        Until(() => bo.ThingOf(bagId)!.Flags.HasFlag(ViewerFlags.Teammate), "Bo told the bag is his teammate's");
        Fixtures.Report($"Ada's door to her: {mine.Flags}; to Bo: {his.Flags}; her bag to Bo now: {bo.ThingOf(bagId)!.Flags}");
    }
}
