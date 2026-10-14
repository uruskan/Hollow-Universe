using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Persistence;
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
        z.Things().AddStatic(box);
        Herd herd = AnimalRig.Herd(rig, "deer", -20, -20, 1, 5);
        Animal deer = herd.Members[0];
        (Vector3 face, Vector3 outward) = hut.Face(hut.Walls[1]);
        LiveExplosive charge = z.Explosives().StickExplosive(c.ItemByKey("timed-charge").Throwable!, face + outward * 1.5f, -outward, 3, rig.P(ada).Id, "Ada", dud: true)!;
        Ship ship = z.Ships().SpawnShip(c.Ship("tin-finch")!, z.GroundAt(30, 30), 0, rig.P(ada).Id, 10);
        var nearby = new List<NaturalObject>();
        z.Nature!.Query(z.Landing, 110, nearby);
        NaturalObject tree = nearby.First(o => c.Natural(o.Kind)?.Class == NaturalClass.Tree);
        // A spent arrow stuck in the ground, as one shot down at it leaves it.
        var shot = new Projectile { Id = 1, Ammo = c.ItemByKey("arrow"), Velocity = new Vector3(0, -20, -40) };
        SpentArrow arrow = z.Arrows().StickArrow(shot, new TraceHit { Target = TargetRef.Ground, Point = z.GroundAt(-10, 10) })!;

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
            (arrow.Ref, arrow, "a spent arrow"),
            (TargetRef.Ground, null, "the ground"),
        };
        Assert.Equal(Enum.GetValues<TargetFamily>().Length - 1, named.Select(n => n.Target.Family).Distinct().Count(), "every family named once");
        foreach ((TargetRef target, object? thing, string what) in named)
        {
            Assert.True(z.Targets().Exists(target), $"{what} exists");
            Assert.True(thing == null || ReferenceEquals(z.Targets().Resolve(target), thing), $"{what} resolves to itself");
            Assert.True(z.Targets().TryPose(target, out TargetPose pose) && float.IsFinite(pose.Position.X), $"{what} has a pose");
            Assert.True(z.Targets().FamilyOf(target.Family)!.Family == target.Family, $"{what}'s family answers for it");
            if (target.Family == TargetFamily.Ground)
            {
                continue;
            }
            // From 12 m to the side: the nearest point is on the thing, nearer than where it stands, not where we stand.
            Vector3 up = z.Frame.UpAt(pose.Position), side = Vector3.Normalize(Vector3.Cross(up, Vector3.UnitZ));
            Vector3 from = pose.Position + side * 12 + up * 0.5f;
            Assert.True(z.Targets().TryNearest(target, from, out Vector3 near), $"{what} has a nearest point");
            float toNear = Vector3.Distance(from, near), toBase = Vector3.Distance(from, pose.Position);
            Assert.True(toNear < toBase + 1e-3f && toNear > 0.5f, $"{what}: nearest point {toNear:0.00} m, base {toBase:0.00} m");
            Assert.True(!z.Targets().Exists(new TargetRef(target.Family, 0xDEAD_0000_0000_BEEF)) && z.Targets().Resolve(new TargetRef(target.Family, 0xDEAD_0000_0000_BEEF)) == null,
                $"a made-up {target.Family} ID names nothing");
        }
        Assert.True(z.Targets().FamilyOf((TargetFamily)200) == null && !z.Targets().Exists(new TargetRef((TargetFamily)200, 1)), "a family number from outside that names none");

        // A body's bones: the head is above the feet; nearest points of the head and the whole body differ.
        Assert.True(z.Targets().TryPose(rig.P(ada).Ref with { Part = 1 }, out TargetPose head), "Ada's head bone");
        Assert.True(z.Targets().TryPose(rig.P(ada).Ref, out TargetPose feet), "Ada's feet");
        Assert.True(Vector3.Dot(head.Position - feet.Position, z.Frame.UpAt(feet.Position)) > 1.4f, "Ada's head bone is up at her head");
        Assert.True(z.Targets().TryPose(deer.Ref with { Part = 3 }, out TargetPose leg) && Vector3.Distance(leg.Position, deer.Position) < 2, "the deer's leg bone");

        // Things that go are gone from their family.
        z.Things().RemoveStatic(box);
        Assert.True(!z.Targets().Exists(box.Ref) && z.Targets().Resolve(box.Ref) == null, "a box picked up");
        z.Building().DamageBuilding(hut.Door, Vector3.UnitZ, 1_000_000, projectile: false);
        Assert.True(!z.Targets().Exists(hut.Door.Ref), "a door broken");

        // The damage pipeline routes by family: a blow on the wall's TargetRef reaches the building's receiver.
        double before = hut.Walls[1].Health;
        z.Blasts().DamageBuildingBy(hut.Walls[1], Vector3.UnitX, 50, projectile: false, attacker: null, weapon: 0);
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
        z.Things().AddStatic(box);
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
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, box.Ref, "open"), "opened from 0.1 m inside the reach of its side");
        z.Actions().Close(ada);
        Vector3 eye = StandFromSide(s.OpenReach + 0.1f);
        Assert.Equal(OutcomeCode.OutOfReach, z.Interaction().Interact(ada, box.Ref, "open"), "not from 0.1 m beyond it");
        Fixtures.Report($"reach {s.OpenReach} m to the side: {Vector3.Distance(eye, box.Centre):0.00} m from its centre is too far");

        // A hut: a box inside is hidden from outside by a wall, not from inside.
        TestBases.Hut hut = Raids.Hut(rig, 0, 0, door: "wooden-door");
        Vector3 inside = hut.Inside;
        var chest = new Deployable(z.NextId(), c.Deployable("large-box")!, inside, z.Frame.UpAt(inside), 0, rig.P(ada).Id);
        z.Things().AddStatic(chest);
        (Vector3 face, Vector3 outward) = hut.Face(hut.Walls[1], height: 0);
        Raids.Stand(rig, ada, z.Settle(face + outward * 1.2f));
        Assert.True(Vector3.Distance(chest.Nearest(Eye()), Eye()) < s.OpenReach, "the box inside is within reach");
        Assert.Equal(OutcomeCode.OutOfSight, z.Interaction().Interact(ada, chest.Ref, "open"), "but the wall hides it");
        Raids.Stand(rig, ada, z.Settle(inside + hut.Direction(new(0, 0, 1)) * 1.0f));
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, chest.Ref, "open"), "from inside it opens");
        z.Actions().Close(ada);

        // The door, from aside in front of the doorway: its frame holds it and doesn't hide it.
        Door door = hut.Door!;
        (Vector3 front, Vector3 doorOut) = hut.Face(hut.Doorway, height: 0);
        Vector3 aside = Vector3.Normalize(Vector3.Cross(z.Frame.UpAt(front), doorOut));
        Raids.Stand(rig, ada, z.Settle(front + doorOut * 0.8f + aside * 1.6f));
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, door.Ref, "door-open"), "the door opens from aside");

        // A dud stuck on the east wall's outside: not lit through the wall, lit from in front.
        (Vector3 east, Vector3 eastOut) = hut.Face(hut.Walls[1], height: 1.0);
        LiveExplosive dud = z.Explosives().StickExplosive(c.ItemByKey("bag-charge").Throwable!, east + eastOut * 1.5f, -eastOut, 3, EntityId.None, "", dud: true)!;
        Raids.StepUntil(rig, () => dud.Dud, 12 * 30 + 2, "the fuse going out");
        (Vector3 eastIn, Vector3 inward) = hut.Face(hut.Walls[1], height: 0, inside: true);
        Raids.Stand(rig, ada, z.Settle(eastIn + inward * 0.6f));
        Assert.Equal(OutcomeCode.OutOfSight, z.Interaction().Interact(ada, dud.Ref, "relight"), "the wall hides the dud");
        Raids.Stand(rig, ada, z.Settle(face + outward * 1.0f));
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, dud.Ref, "relight"), "lit from in front");
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
        Assert.True(CapabilityHandlers.Create(c).Problems.Count == 0 && z.Targets().Capabilities.Count == c.Capabilities.Keys.Count,
            "every capability in the data has its handler");
        Entity ada = rig.Add("Ada", 0, -4), bo = rig.Add("Bo", 1, -4);
        Player a = rig.P(ada), b = rig.P(bo);
        ViewerFlags As(Player p, ITargetable thing) => z.Targets().FlagsFor(thing.Ref, z.Targets().ViewerOf(p));

        Vector3 at = z.GroundAt(6, 6);
        var box = new Deployable(z.NextId(), c.Deployable("large-box")!, at, z.Frame.UpAt(at), 0, a.Id);
        z.Things().AddStatic(box);
        Assert.Equal(ViewerFlags.Mine | ViewerFlags.Empty, As(a, box), "Ada's empty box, to her");
        Assert.Equal(ViewerFlags.Empty, As(b, box), "to Bo: empty, not his");
        box.Storage!.Set(0, ItemStack.Of(c.ItemByKey("wood"), 10));
        Assert.Equal(ViewerFlags.Mine, As(a, box), "no longer empty");
        z.World().TeamAct(ada, TeamAction.Create, EntityId.None, 0);
        z.Interaction().Interact(ada, b.Ref, "invite");
        z.World().TeamAct(bo, TeamAction.Accept, EntityId.None, z.World().Teams.TeamOf(a.Id)!.Id);
        Assert.Equal(ViewerFlags.Teammate, As(b, box), "to Bo, his teammate's box");

        Vector3 fireAt = z.GroundAt(-6, 6);
        var fire = new Deployable(z.NextId(), c.Deployable("campfire")!, fireAt, z.Frame.UpAt(fireAt), 0, a.Id) { Lit = true };
        z.Things().AddStatic(fire);
        Assert.True(As(b, fire).HasFlag(ViewerFlags.Lit), "a fire burning is Lit to anyone");

        TestBases.Hut hut = Raids.Hut(rig, 0, 12, owner: 99, door: "wooden-door");
        Door door = hut.Door!;
        Assert.True(z.Building().World!.Authorise(a.Id.Value, hut.Claim!).Ok && z.Building().World.AttachCodeLock(a.Id.Value, door, out _).Ok, "Ada fits a code lock");
        Assert.Equal(ViewerFlags.Mine | ViewerFlags.CanUse | ViewerFlags.Authorised, As(a, door), "her fresh lock: hers, open, no code yet");
        Assert.Equal(ViewerFlags.CanUse | ViewerFlags.Teammate, As(b, door), "to Bo: an unlocked door, his teammate's lock");
        Assert.True(z.Building().World.SetCode(a.Id.Value, door, "1234").Ok, "she sets a code");
        Assert.Equal(ViewerFlags.Mine | ViewerFlags.CanUse | ViewerFlags.Authorised | ViewerFlags.HasCode | ViewerFlags.Locked, As(a, door),
            "locked with a code, Ada on its list and through");
        Assert.Equal(ViewerFlags.HasCode | ViewerFlags.Locked | ViewerFlags.Teammate, As(b, door), "Bo: locked with a code, not his way");
        Assert.True(As(b, hut.Claim!) == ViewerFlags.None && z.Building().World.Authorise(b.Id.Value, hut.Claim!).Ok &&
            As(b, hut.Claim!) == ViewerFlags.Authorised, "the claim device: Authorised once Bo is on its list");

        Ship ship = z.Ships().SpawnShip(c.Ship("tin-finch")!, z.GroundAt(30, 30), 0, a.Id, 10);
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
            sv.Zone.Things().AddStatic(bag);
            (Vector3 face, Vector3 outward) = hut.Face(hut.Doorway, height: 0);
            return (hut.Door!.Id, bag.Id, face + outward * 1.5f);
        });
        // Ada in front of the door, in reach and in sight of it.
        rig.Teleport("Ada", front);
        Until(() => ada.Building().World!.DoorOf(doorId) != null && bo.Building().World!.DoorOf(doorId) != null && ada.Survival().ThingOf(bagId) != null && bo.Survival().ThingOf(bagId) != null &&
            AnimalRig.Flat(ada.Character.Position, front) < 0.5f,
            "the door and the bag on both clients, Ada in front of the door on hers");
        Assert.True(ada.Survival().ThingOf(bagId)!.Mine && !bo.Survival().ThingOf(bagId)!.Mine, "the bag is Ada's on her client alone");
        Door adaDoor = ada.Building().World!.DoorOf(doorId)!, boDoor = bo.Building().World!.DoorOf(doorId)!;
        int lockSlot = Enumerable.Range(0, ada.Local.Inventory.Main.Size).First(i => ada.Local.Inventory.Main[i].Item == ContentFixture.Item("code-lock").Id);
        // Fitted where its ghost shows it on the door (GROUND_SYSTEMS.md §6).
        Simulation.Placement.PlacementCandidate? lockGhost = ada.Placement().PlanAt(ContentFixture.Item("code-lock").Places!, adaDoor.LockSpot.Position, 0);
        Assert.True(lockGhost is { Valid: true }, $"the lock's ghost on the door ({lockGhost})");
        ada.Local.Place(lockGhost!, ContainerRef.Main, lockSlot);
        Until(() => ada.Building().Replica!.StateOf(adaDoor).Lock == LockKind.Code && bo.Building().Replica!.StateOf(boDoor).Lock == LockKind.Code, "the lock on both");
        DoorState mine = ada.Building().Replica!.StateOf(adaDoor), his = bo.Building().Replica!.StateOf(boDoor);
        Assert.True(mine is { Mine: true, HasCode: false, CanUse: true } && his is { Mine: false, HasCode: false },
            $"a fresh code lock: Ada's, no code ({mine.Flags}; Bo: {his.Flags})");
        ada.Local.Interact(adaDoor.Ref, "set-code", code: "4711");
        Until(() => ada.Building().Replica!.StateOf(adaDoor).HasCode && bo.Building().Replica!.StateOf(boDoor).HasCode, "the code on both");
        mine = ada.Building().Replica!.StateOf(adaDoor);
        his = bo.Building().Replica!.StateOf(boDoor);
        Assert.True(mine is { Locked: true, Authorised: true, CanUse: true } && his is { Locked: true, Authorised: false, CanUse: false },
            $"locked: Ada passes, Bo doesn't ({mine.Flags}; Bo: {his.Flags})");

        rig.Teleport("Bo", front, east: 1.2f);
        rig.Run(0.5, (_, _) => default);
        ada.Local.CreateTeam();
        Until(() => ada.Local.Team != null, "Ada's team");
        ada.Local.Interact(new TargetRef(TargetFamily.Character, bo.EntityId.Value), "invite");
        Until(() => bo.Local.TeamInvites.Count > 0, "Bo's invite");
        bo.Local.AcceptInvite(bo.Local.TeamInvites[0].Team);
        Until(() => bo.Survival().ThingOf(bagId)!.Flags.HasFlag(ViewerFlags.Teammate), "Bo told the bag is his teammate's");
        Fixtures.Report($"Ada's door to her: {mine.Flags}; to Bo: {his.Flags}; her bag to Bo now: {bo.Survival().ThingOf(bagId)!.Flags}");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §3.6: support links. A charge stuck on a door is linked to the door's leaf (its part): it swings
    /// with the leaf as the door opens, the door lists it as resting on it, and when the door breaks it goes with it by its
    /// own capability's consequence (it falls). A link to a body names a bone and rides on it as the deer walks. An explosive
    /// saved by the last version (its support named by kind) comes back onto a TargetRef.
    /// </summary>
    [Test]
    public static void SupportLinksFollowAndGoWithTheirSupport()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        GameContent c = rig.C;
        TestBases.Hut hut = Raids.Hut(rig, 0, 0, door: "wooden-door");
        Door door = hut.Door!;
        (Vector3 face, Vector3 outward) = hut.Face(hut.Doorway, height: 1.1, along: 0.25);
        LiveExplosive charge = z.Explosives().StickExplosive(c.ItemByKey("timed-charge").Throwable!, face + outward * 1.5f, -outward, 3, EntityId.None, "", dud: true)!;
        Assert.True(charge.State == ExplosiveState.Stuck && charge.Link.Support.Whole == door.Ref && charge.Link.Support.Part > 0,
            $"stuck to a leaf of the door ({charge.Link.Support})");
        Assert.True(z.Supports().DependentsOf(door.Ref).Contains(charge.Ref), "the door lists it as resting on it");
        Vector3 shut = charge.Position;
        Assert.True(z.Building().World!.Toggle(1, door).Ok, "the door opens");
        rig.Step(1);
        Assert.True(Vector3.Distance(charge.Position, shut) > 0.2f && charge.State == ExplosiveState.Stuck, "the charge swung with the leaf");
        Assert.True(z.Supports().TryLinkPose(charge.Link, out TargetPose on) && Vector3.Distance(on.Position, charge.Position) < 1e-3f, "where its link says");
        z.Building().DamageBuilding(door, Vector3.UnitZ, 1_000_000, projectile: false);
        Assert.True(door.Gone && charge.State == ExplosiveState.Flying && charge.Link.IsNone && z.Supports().DependentsOf(door.Ref).Count == 0,
            "the door broke, and the charge went with it: it falls");

        // A body's bone: a link to the deer's head rides on it as the deer walks away.
        Herd herd = AnimalRig.Herd(rig, "deer", 20, 20, 1, 3);
        Animal deer = herd.Members[0];
        var head = deer.Ref with { Part = 2 };
        Assert.True(z.Targets().TryPose(head, out TargetPose bone), "the deer's head");
        SupportLink link = z.Supports().LinkTo(head, new TargetPose(bone.Position + z.Frame.UpAt(bone.Position) * 0.1f, bone.Rotation));
        Vector3 before = deer.Position;
        z.Animals().World!.Nav.Synchronous = true;
        for (int i = 0; i < 600 && Vector3.Distance(deer.Position, before) < 1.5f; i++)
        {
            rig.Step(1);
            if (i % 60 == 0)
            {
                z.Animals().World.Hurt(deer, 0.01f, z.Frame.Beside(deer.Position, 5, 0, 0), default, "");
            }
        }
        Assert.True(Vector3.Distance(deer.Position, before) >= 1.5f, "the deer walked off");
        Assert.True(z.Targets().TryPose(head, out TargetPose moved) && z.Supports().TryLinkPose(link, out TargetPose rider) &&
            Vector3.Distance(rider.Position, moved.Position + Vector3.Transform(link.Position, moved.Rotation)) < 1e-3f &&
            Vector3.Distance(rider.Position, moved.Position) < 0.2f, "what is linked to its head is still at its head");

        // A charge saved by version 1 (on a door's second leaf) names its support by a TargetRef now.
        using var bytes = new MemoryStream();
        using (var w = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)1);
            w.Write(c.ItemByKey("timed-charge").Id);
            w.Write(0UL);
            w.Write("Ada");
            w.Write((byte)ExplosiveState.Stuck);
            w.Write(false);
            for (int i = 0; i < 9; i++)
            {
                w.Write(1.0);
            }
            w.Write(30L);
            w.Write(300);
            w.Write(false);
            w.Write(1);
            w.Write((byte)2);
            w.Write(0x1234UL);
            w.Write(1);
            for (int i = 0; i < 6; i++)
            {
                w.Write(0f);
            }
            w.Write(-1L);
        }
        ExplosiveSave old = ExplosiveCodec.Decode(new EntityId(7), bytes.ToArray(), v => new Vector3((float)v.X, (float)v.Y, (float)v.Z));
        Assert.Equal(new TargetRef(TargetFamily.Door, 0x1234, 2), old.Support, "a door's second leaf, from version 1");
        Fixtures.Report($"the charge swung {Vector3.Distance(shut, on.Position):0.00} m with the leaf; the deer walked {Vector3.Distance(deer.Position, before):0.0} m");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §3.7: lifetimes. One step ends the loose things of every family, each by its rule from data: a
    /// bag (whose time starts again with each item put in), a body, a dud charge, an animal's carcass; each goes through
    /// its own family. Emptied, a bag goes at once. Nothing else lies loose.
    /// </summary>
    [Test]
    public static void LifetimesAreOneStepForEveryLooseThing()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        GameContent c = rig.C;
        AnimalRig.Animals(rig);
        Entity ada = rig.Add("Ada", 0, 0);
        Entity bo = rig.Add("Bo", 6, 0);
        rig.Step(2);
        Assert.Equal(0, z.Things().LooseCount, "nothing lies loose");

        // Bags: emptied, one goes at once; another's time starts again with the newest item.
        DroppedBag near = z.Spill().Spill(z.GroundAt(1.2f, 0), ItemStack.Of(c.ItemByKey("stone"), 10))!;
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, near.Ref, "pick-up"), "picked up");
        Assert.True(z.Things().StaticOf(near.Id) == null && z.Things().LooseCount == 0, "emptied, gone at once");
        DroppedBag bag = z.Spill().Spill(z.GroundAt(3, 3), ItemStack.Of(c.ItemByKey("stone"), 10))!;
        Assert.True(bag.Lifetime == c.Survival.BagLifetime && bag.DespawnTick == z.Tick + c.Survival.BagLifetime.Ticks, "a bag's lifetime, from data");
        rig.Step(45);
        z.Spill().Spill(bag.Position, ItemStack.Of(c.ItemByKey("wood"), 5));
        Assert.Equal(z.Tick + c.Survival.BagLifetime.Ticks, bag.DespawnTick, "its time started again with the newest item");

        // A body; a dud; a carcass.
        rig.Give(bo, "wood", 20);
        z.Players().Hurt(bo, rig.P(bo), 1000, "Test", EntityId.None, "");
        Corpse body = z.Things().Statics().OfType<Corpse>().Single();
        Vector3 ground = z.GroundAt(-3, -3);
        Vector3 up = z.Frame.UpAt(ground);
        LiveExplosive dud = z.Explosives().StickExplosive(c.ItemByKey("timed-charge").Throwable!, ground + up, -up, 3, EntityId.None, "", dud: true)!;
        for (int i = 0; i < 30 * 60 && !dud.Dud; i++)
        {
            rig.Step(1);
        }
        Animal deer = AnimalRig.Herd(rig, "deer", 12, 12, 1).Leader!;
        rig.Step(2);
        z.Animals().DamageAnimal(deer, 1000, HitRegion.Body, z.GroundAt(0, 0), null);
        int lifetime = c.Capabilities.Index("lifetime");
        Assert.True(body.Lifetime == c.Survival.BodyLifetime && dud.Dud && dud.Lifetime == c.Survival.DudLifetime && deer.IsDead &&
            deer.Lifetime == deer.Species.Carcass, "a body's, a dud's and a carcass's, from data");
        Assert.True(z.Targets().Families.Find((int)TargetFamily.Explosive)!.Capabilities(dud.Id.Value).Has(lifetime) &&
            z.Targets().Families.Find((int)TargetFamily.Animal)!.Capabilities(deer.Id.Value).Has(lifetime), "their capabilities say so");
        Assert.Equal(4, z.Things().LooseCount, "the bag, the body, the dud and the carcass lie loose, nothing else");

        // Their time up, the one step ends each through its family.
        foreach (ILifetime loose in new ILifetime[] { bag, body, dud, deer })
        {
            loose.EndsAt = z.Tick + 5;
        }
        rig.Step(40);
        Assert.True(z.Things().StaticOf(bag.Id) == null && z.Things().StaticOf(body.Id) == null && z.Explosives().ExplosiveOf(dud.Id) == null && deer.Removed &&
            z.Animals().World!.Find(deer.Id) == null, "all four gone");
        Assert.Equal(0, z.Things().LooseCount, "nothing lies loose");
        Fixtures.Report($"loose things by family: a bag and a body (things), a dud (explosive), a carcass (animal), ended within a second of their time");
    }
}
