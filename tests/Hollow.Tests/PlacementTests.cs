using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>
/// The placement system (GROUND_SYSTEMS.md §6): one rule per placeable in data; its solvers, rotations, checks and kinds
/// found by name; one pipeline for the client's ghost and the server's verdict. A test a rule.
/// </summary>
public static class PlacementTests
{
    // ---------------------------------------------------------------- helpers

    /// <summary>The ghost of what an item places, aimed from a character's eye at a point, R pressed so often.</summary>
    private static PlacementCandidate? Ghost(GameRig rig, Entity e, string item, Vector3 at, int turns = 0) =>
        Ghost(rig, e, rig.C.ItemByKey(item).Places!, at, turns);

    private static PlacementCandidate? Ghost(GameRig rig, Entity e, Placeable placeable, Vector3 at, int turns = 0) =>
        rig.Zone.Aim(e, placeable, at - rig.Zone.EyeOf(rig.Ch(e).State), turns);

    /// <summary>Places what a ghost shows from the main slot holding its item: the intent its client sends.</summary>
    private static Outcome Place(GameRig rig, Entity e, PlacementCandidate ghost) =>
        rig.Zone.Place(e, ContainerRef.Main, SlotOf(rig.P(e).Inventory.Main, ghost.Placeable.SlotItem!), ghost);

    private static int SlotOf(Container main, ItemDef item) => Enumerable.Range(0, main.Size).First(i => main[i].Item == item.Id);

    /// <summary>A placeable under a rule of the test's own (a limit of one).</summary>
    private static Placeable With(Placeable p, PlacementRule rule) => new()
    {
        Index = p.Index, Key = p.Key, Kind = p.Kind, Rule = rule, Definition = p.Definition, Item = p.Item, Tool = p.Tool, Costs = p.Costs,
        Footprint = p.Footprint, ModelKey = p.ModelKey, Name = p.Name,
    };

    /// <summary>The point of a slab's top over a point of it (metres across it, in its own frame).</summary>
    private static Vector3 Top(Zone zone, Piece slab, double x = 0, double z = 0)
    {
        Vector3 over = BuildingWorld.F(slab.Structure.Frame.ToZone(PieceGeometry.ToFrame(slab.Pose, new Vector3D(x, 1, z))));
        Assert.True(zone.Placement.Ray(over, -zone.Frame.UpAt(over), 3, out PlacementHit hit) && hit.Target == slab.Ref, $"the top of the {slab.Def.Key}");
        return hit.Point;
    }

    private static void RunUntil(LoopbackRig rig, Func<bool> done, string what, double seconds = 6)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < seconds, $"timed out waiting for {what}");
            rig.Run(0.05, (_, _) => default);
        }
    }

    // ---------------------------------------------------------------- the rules

    /// <summary>
    /// Every placeable has one rule: placement.json's defaults, its kind's block over them, its own block next to its
    /// definition over that. Every item that places something names its placeable. A solver, rotation, check or kind
    /// named without a handler, a handler left out of the lists, or something placeable left out of the list is a
    /// problem of the data.
    /// </summary>
    [Test]
    public static void EveryPlaceableHasOneRule()
    {
        GameContent c = ContentFixture.Content;
        PlacementData data = c.Placement;
        Assert.True(data.Placeables.Select((p, i) => p.Index == i && p.SlotItem != null).All(ok => ok), "numbered as listed, each placed with an item");
        foreach (ItemDef item in c.Items.Where(i => i.Places != null))
        {
            Assert.True(item.Places!.SlotItem == item, $"{item.Key} places {item.Places}");
        }
        PlacementRule Rule(string item) => c.ItemByKey(item).Places!.Rule;
        Assert.True(Rule("furnace") is { Reach: 3, PerStructure: 20, MaxSlopeDegrees: 25, Support: SupportMode.Corners, Water: false, Solver: "free" },
            "a furnace: the defaults, the deployables' 20 to a structure, its own slope");
        Assert.True(Rule("sleeping-bag").PerPlayer == 128, "128 sleeping bags a player");
        Assert.True(Rule("claim-device") is { Surfaces: PlacementSurfaces.Floor, PerStructure: 1, Rotation: "support" }, "the claim device: on a slab, one to a structure");
        Assert.True(Rule("wooden-door") is { Solver: "opening", Rotation: "flip", Surfaces: PlacementSurfaces.Opening }, "a door: into an opening, R flips it");
        Assert.True(Rule("code-lock") is { Solver: "attach", Rotation: "fixed", Surfaces: PlacementSurfaces.Door }, "a lock: onto a door");
        Assert.True(Rule("timed-charge") is { Solver: "sweep", Privilege: PrivilegeMode.Ignored, Align: AlignMode.Normal, Water: true },
            "a charge: wherever the view meets a surface, without building rights");
        Assert.True(Rule("tin-finch") is { Reach: 12, MaxSlopeDegrees: 20, Water: false } && c.ItemByKey("tin-finch").Places!.Footprint is HullFootprint,
            "the Tin Finch: its hull, 20 degrees at most, dry, 12 m away");
        Assert.True(data.Of(c.Building.Piece("wall")!)!.Rule is { Solver: "socket", Reach: 8, Padding: 2.25f } && data.Of(c.Building.Piece("wall")!)!.Tool?.Key == "building-plan",
            "a wall: the building's sockets, reach and claim padding, with the building plan");

        Dictionary<string, string> files = GameContent.Files.Concat(GameContent.Unhashed)
            .ToDictionary(f => f, f => File.ReadAllText(Path.Combine(ContentFixture.Folder, f)));
        void Reports(Func<string, string> breaking, params string[] expected)
        {
            var texts = new Dictionary<string, string>(files) { ["placement.json"] = breaking(files["placement.json"]) };
            IReadOnlyList<string> problems = GameContent.FromTexts(texts).Problems;
            foreach (string problem in expected)
            {
                Assert.True(problems.Any(p => p.Contains(problem, StringComparison.Ordinal)), $"reported: {problem}\n        got: {string.Join("\n        ", problems)}");
            }
            Assert.Equal(expected.Length, problems.Count, "and nothing else: " + string.Join("; ", problems));
        }
        Reports(s => s.Replace("\"sweep\"]", "\"sweep\", \"magnet\"]", StringComparison.Ordinal).Replace(", \"own\"]", "]", StringComparison.Ordinal),
            "placement.json: placement solvers: \"magnet\" has no handler", "placement.json: placement checks: a handler (Own) for \"own\", which isn't in the list");
        Reports(s => s.Replace("\"key\": \"key-lock\" }", "\"key\": \"no-such-lock\" }", StringComparison.Ordinal),
            "names no lock in the game data", "the lock \"key-lock\" can't be placed");
    }

    /// <summary>
    /// Where a thing stands (GROUND_SYSTEMS.md §6, the owner's default): on the ground or a foundation's or floor's top. A
    /// furnace fits in the middle of the floor over a hut and stands on it; half over its edge it doesn't (RustMVP's
    /// corners); the claim device goes on a slab, not the ground.
    /// </summary>
    [Test]
    public static void ThingsStandOnTheGroundFoundationsAndFloors()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0);
        rig.Give(ada, "furnace", 1);
        rig.Give(ada, "claim-device", 1);
        Assert.Equal(OutcomeCode.NotOnStructure, Ghost(rig, ada, "claim-device", rig.Zone.GroundAt(1.8f, 0))?.Outcome.Code, "the claim device on the ground");

        TestBases.Hut hut = Raids.Hut(rig, 0, 12, grade: 1, owner: rig.P(ada).Id.Value, door: null);
        Piece roof = hut.Roof!;
        Raids.Stand(rig, ada, Top(rig.Zone, roof, -1.2));
        PlacementCandidate? middle = Ghost(rig, ada, "furnace", Top(rig.Zone, roof));
        Assert.True(middle is { Valid: true, Surface: PlacementSurfaces.Floor } && middle.Support == roof.Ref, $"a furnace on the floor ({middle})");
        Assert.Equal(OutcomeCode.Overhangs, Ghost(rig, ada, "furnace", Top(rig.Zone, roof, 1.3))?.Outcome.Code, "half over its edge");
        Assert.Equal(OutcomeCode.Done, Place(rig, ada, middle!).Code, "placed");
        Deployable furnace = rig.Zone.Statics().OfType<Deployable>().Single(d => d.Def.Key == "furnace");
        Assert.True(furnace.Support == roof.Ref && rig.Zone.DependentsOf(roof.Ref).Contains(furnace.Ref), "standing on the floor");
    }

    /// <summary>Water (RustMVP's 5 cm, every placeable's rule): a box isn't put in the sea; a charge, whose rule lets it, is.</summary>
    [Test]
    public static void BoxesStayDryChargesNeedNot()
    {
        using var rig = new GameRig(spec => new LevelSurface(spec.Radius, groundHeight: -0.4, seaRadius: spec.Radius));
        Entity ada = rig.Add("Ada", 0, 0);
        rig.Give(ada, "small-box", 1);
        rig.Give(ada, "timed-charge", 1);
        // (Aimed off the level ground's grid lines: a ray along one may slip between its triangles.)
        Vector3 seabed = rig.Zone.GroundAt(1.6f, 0.7f);
        Assert.Equal(OutcomeCode.InWater, Ghost(rig, ada, "small-box", seabed)?.Outcome.Code, "a box on the sea floor");
        PlacementCandidate? charge = Ghost(rig, ada, "timed-charge", seabed);
        Assert.True(charge is { Valid: true }, $"a charge there ({charge})");
    }

    /// <summary>
    /// Building rights: by someone else's claim nothing is placed (building blocked), but a charge, which needs none, sticks
    /// to their wall, lying along it.
    /// </summary>
    [Test]
    public static void ClaimsKeepOthersOutButNotCharges()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", -3, 5), bo = rig.Add("Bo", 3, 5);
        TestBases.Hut hut = Raids.Hut(rig, 0, 12, owner: rig.P(ada).Id.Value, door: null);
        rig.Give(ada, "small-box", 1);
        rig.Give(bo, "small-box", 1);
        rig.Give(bo, "timed-charge", 1);
        Assert.Equal(OutcomeCode.BuildingBlocked, Ghost(rig, bo, "small-box", rig.Zone.GroundAt(3, 6.6f))?.Outcome.Code, "Bo's box by Ada's hut");
        PlacementCandidate? hers = Ghost(rig, ada, "small-box", rig.Zone.GroundAt(-3, 6.6f));
        Assert.True(hers is { Valid: true }, $"Ada's ({hers})");

        Vector3 bo0 = rig.Ch(bo).State.Position;
        Piece wall = hut.Walls.Skip(1).MinBy(w => Vector3.Distance(hut.Face(w).Point, bo0))!;
        (Vector3 face, Vector3 outward) = hut.Face(wall);
        Raids.Stand(rig, bo, rig.Zone.Settle(face + outward * 1.4f));
        PlacementCandidate? charge = Ghost(rig, bo, "timed-charge", face);
        Assert.True(charge is { Valid: true } && charge.Support == wall.Ref && Vector3.Dot(charge.Up, outward) > 0.99f,
            $"Bo's charge on her wall, lying along it ({charge})");
        Assert.Equal(OutcomeCode.Done, Place(rig, bo, charge!).Code, "stuck");
    }

    /// <summary>
    /// Limits (RustMVP's): so many of a kind on a structure (one claim device), so many of a thing a player. Rules of the
    /// test's own, one each, stand in for the deployables' 20 to a structure and the bags' 128.
    /// </summary>
    [Test]
    public static void LimitsPerStructureAndPerPlayer()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0), bo = rig.Add("Bo", 4, -4);
        rig.Give(ada, "claim-device", 1);
        rig.Give(ada, "small-box", 1);
        rig.Give(ada, "sleeping-bag", 1);

        Placeable bag = rig.C.ItemByKey("sleeping-bag").Places!, oneBag = With(bag, bag.Rule with { PerPlayer = 1 });
        Assert.Equal(OutcomeCode.Done, Place(rig, ada, Ghost(rig, ada, "sleeping-bag", rig.Zone.GroundAt(1.6f, 0.7f))!).Code, "Ada's bag");
        Assert.Equal(OutcomeCode.TooMany, Ghost(rig, ada, oneBag, rig.Zone.GroundAt(-1.6f, 0.7f))?.Outcome.Code, "a second, one a player");
        PlacementCandidate? his = Ghost(rig, bo, oneBag, rig.Zone.GroundAt(5.6f, -3.3f));
        Assert.True(his is { Valid: true }, $"Bo's first ({his})");

        TestBases.Hut hut = Raids.Hut(rig, 0, 12, grade: 1, owner: rig.P(ada).Id.Value, door: null);
        Piece roof = hut.Roof!;
        Raids.Stand(rig, ada, Top(rig.Zone, roof, -1.2));
        Assert.Equal(OutcomeCode.StructureFull, Ghost(rig, ada, "claim-device", Top(rig.Zone, roof, 0.7))?.Outcome.Code, "a second claim device on the hut");
        Placeable box = rig.C.ItemByKey("small-box").Places!, oneBox = With(box, box.Rule with { PerStructure = 1 });
        Vector3 spot = Top(rig.Zone, roof, 0.6, -0.6), other = Top(rig.Zone, roof, 0.6, 0.6);
        Assert.True(Ghost(rig, ada, oneBox, spot) is { Valid: true }, "a first box on the hut");
        Assert.Equal(OutcomeCode.Done, Place(rig, ada, Ghost(rig, ada, "small-box", spot)!).Code, "placed");
        Assert.Equal(OutcomeCode.StructureFull, Ghost(rig, ada, oneBox, other)?.Outcome.Code, "a second, one to a structure");
    }

    /// <summary>
    /// The server never takes the client's pose: it works the claim out again and takes it only within the rule's
    /// tolerance (30 cm) of its own. A furnace claimed half a metre over the ground, or on the ground where a foundation
    /// stands, is refused; ten centimetres off, it goes where the server's own pose is.
    /// </summary>
    [Test]
    public static void TheServerWorksEveryClaimOutAgain()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0);
        rig.Give(ada, "furnace", 2);
        Placeable furnace = rig.C.ItemByKey("furnace").Places!;
        Outcome Claim(PlacementClaim claim) =>
            rig.Zone.Place(ada, new PlaceRequest(ContainerRef.Main, SlotOf(rig.P(ada).Inventory.Main, furnace.Item!), 0, furnace.Index, claim));
        PlacementCandidate ghost = Ghost(rig, ada, furnace, rig.Zone.GroundAt(2, 0))!;
        Assert.True(ghost.Valid, $"the ghost ({ghost})");
        Vector3 up = rig.Zone.Frame.UpAt(ghost.Position);
        Assert.Equal(OutcomeCode.Misplaced, Claim(ghost.Claim with { Position = ghost.Position + up * 0.5f }).Code, "half a metre over the ground");
        Assert.Equal(OutcomeCode.Done, Claim(ghost.Claim with { Position = ghost.Position + up * 0.1f }).Code, "ten centimetres over it");
        Deployable placed = rig.Zone.Statics().OfType<Deployable>().Single();
        Assert.True(Vector3.Distance(placed.Position, ghost.Position) < 0.01f, $"where the server's own pose is ({placed.Position} for {ghost.Position})");

        Piece foundation = BuildingNetTests.Foundation(rig.Zone, rig.Zone.GroundAt(-3, 0), rig.P(ada).Id.Value);
        Vector3 top = Top(rig.Zone, foundation);
        Assert.Equal(OutcomeCode.Misplaced, Claim(new PlacementClaim(TargetRef.Ground, 0, top, 0, 0)).Code, "on the foundation, claimed on the ground");
        Assert.Equal(OutcomeCode.Done, Claim(new PlacementClaim(foundation.Ref, 0, top, 0, 0)).Code, "on the foundation");
    }

    /// <summary>
    /// Doors and locks go where they fit: a door into the doorway aimed through, R hanging it the other way round; a garage
    /// door needs a wall frame (its ghost says so); a door claimed a metre away is refused; a lock goes onto the door,
    /// one to a door.
    /// </summary>
    [Test]
    public static void DoorsAndLocksGoWhereTheyFit()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0);
        TestBases.Hut hut = Raids.Hut(rig, 0, 12, owner: rig.P(ada).Id.Value, door: null);
        foreach (string item in new[] { "garage-door", "wooden-door", "code-lock", "key-lock" })
        {
            rig.Give(ada, item, 1);
        }
        (Vector3 face, Vector3 outward) = hut.Face(hut.Doorway, height: 0);
        Raids.Stand(rig, ada, rig.Zone.Settle(face + outward * 1.5f));
        Vector3 opening = hut.Doorway.ZoneTransform.Position + rig.Zone.Frame.UpAt(face) * 1.1f;
        Assert.Equal(OutcomeCode.NeedsWallFrame, Ghost(rig, ada, "garage-door", opening)?.Outcome.Code, "a garage door in a doorway");
        PlacementCandidate? door = Ghost(rig, ada, "wooden-door", opening), turned = Ghost(rig, ada, "wooden-door", opening, turns: 1);
        Assert.True(door is { Valid: true, Variant: 0 } && turned is { Valid: true, Variant: 1 } && door.Support == hut.Doorway.Ref,
            $"a door in the doorway ({door}); R hangs it the other way ({turned})");
        Assert.True(MathF.Abs(Quaternion.Dot(door!.Rotation, turned!.Rotation)) < 0.01f, "turned round");
        Placeable wooden = door.Placeable;
        Outcome refused = rig.Zone.Place(ada, new PlaceRequest(ContainerRef.Main, SlotOf(rig.P(ada).Inventory.Main, wooden.Item!), 0, wooden.Index,
            door.Claim with { Position = door.Position + Vector3.Cross(outward, rig.Zone.Frame.UpAt(face)) }));
        Assert.Equal(OutcomeCode.Misplaced, refused.Code, "claimed a metre along the wall");
        Assert.Equal(OutcomeCode.Done, Place(rig, ada, door).Code, "fitted");

        Door fitted = hut.Doorway.Door!;
        PlacementCandidate? codeLock = Ghost(rig, ada, "code-lock", fitted.LockSpot.Position);
        Assert.True(codeLock is { Valid: true } && codeLock.Position == fitted.LockSpot.Position, $"a code lock where a lock sits on it ({codeLock})");
        Assert.Equal(OutcomeCode.Done, Place(rig, ada, codeLock!).Code, "fitted");
        Assert.True(fitted.Lock is CodeLock, "a code lock on the door");
        Assert.Equal(OutcomeCode.HasLock, Ghost(rig, ada, "key-lock", fitted.LockSpot.Position)?.Outcome.Code, "one lock to a door");
    }

    /// <summary>
    /// The Tin Finch from its item: its hull is its footprint (not where someone stands, not on another ship), set down
    /// on the ground within 12 m, with the 50 of lamp oil ships.json gives a placed one; the item is used up.
    /// </summary>
    [Test]
    public static void TheTinFinchIsPlacedFromItsItem()
    {
        using var rig = new GameRig();
        Entity ada = rig.Add("Ada", 0, 0), bo = rig.Add("Bo", 0, -7);
        rig.Give(ada, "tin-finch", 2);
        Vector3 pad = rig.Zone.GroundAt(0, -7);
        Assert.Equal(OutcomeCode.PlayerInTheWay, Ghost(rig, ada, "tin-finch", pad)?.Outcome.Code, "where Bo stands");
        Raids.Stand(rig, bo, rig.Zone.GroundAt(20, 0));
        PlacementCandidate? ghost = Ghost(rig, ada, "tin-finch", pad);
        Assert.True(ghost is { Valid: true, Surface: PlacementSurfaces.Terrain }, $"its ghost on the ground ({ghost})");
        Assert.Equal(OutcomeCode.Done, Place(rig, ada, ghost!).Code, "placed");
        Ship ship = rig.Zone.Ships.Single();
        ShipDef finch = rig.C.Ship("tin-finch")!;
        Assert.True(ship.Owner == rig.P(ada).Id && ship.Def == finch, "Ada's Tin Finch");
        Assert.Near(50, ship.State.Fuel, 1e-3, "with 50 of lamp oil");
        Assert.Equal(1, rig.P(ada).Inventory.Count(rig.C.ItemByKey("tin-finch")), "one item used up");
        Assert.True(Ghost(rig, ada, "tin-finch", pad) is { Valid: false }, "not on the other ship");
    }

    /// <summary>
    /// Support links over the network and a restart (GROUND_SYSTEMS.md §6, §3.6): Ada's client shows a furnace's ghost on
    /// her foundation (the pipeline on its replicated world), the server agrees and links the furnace to the foundation;
    /// after a restart it still stands on it, and when the foundation goes the furnace goes with it.
    /// </summary>
    [Test]
    public static void AThingGoesWithTheFoundationItStandsOn()
    {
        using var file = new TempDatabase();
        ItemDef furnaceItem = ContentFixture.Item("furnace");
        EntityId foundationId, furnaceId;
        using (var rig = new LoopbackRig(file.Path))
        {
            GameClient ada = rig.Join("Ada");
            rig.WaitUntilPlaying();
            rig.Server.Execute("teleport Ada 0 0");
            rig.Server.Execute("give Ada furnace 1");
            rig.Server.Execute("give Ada wood 1000");
            ulong adaId = ada.EntityId.Value;
            Vector3 top;
            (foundationId, top) = rig.Server.Inspect(sv =>
            {
                Piece f = BuildingNetTests.Foundation(sv.Zone, sv.Zone.Settle(new Vector3(2.6f, 0, 0)), adaId);
                return (f.Id, Top(sv.Zone, f));
            });
            RunUntil(rig, () => ada.Building!.PieceOf(foundationId) != null && ada.Local.Inventory.Count(furnaceItem) == 1 &&
                ada.Local.Inventory.Count(ContentFixture.Item("wood")) == 1000 &&
                new Vector2(ada.Character.Position.X, ada.Character.Position.Z).Length() < 0.5f, "the foundation, the furnace and Ada in place");
            Placeable furnacePlaceable = ada.Content.ItemByKey("furnace").Places!;
            PlacementCandidate? ghost = ada.PlanAt(furnacePlaceable, top, 0);
            Assert.True(ghost is { Valid: true, Surface: PlacementSurfaces.Floor } && ghost.Support.Id == foundationId.Value, $"the ghost on the foundation ({ghost})");
            // The ghost's cost a frame (GROUND_SYSTEMS.md §6's budget: 0.2 ms): a deployable's, and a wall's on the foundation's sockets.
            Placeable wall = ada.Content.Placement.Of(ada.Content.Building.Piece("wall")!)!;
            Vector3 eye = ada.Character.Position + ada.Frame.UpAt(ada.Character.Position) * ada.EyeHeight;
            double Cost(Func<PlacementCandidate?> plan)
            {
                plan();
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 500; i++)
                {
                    plan();
                }
                return watch.Elapsed.TotalMilliseconds * 1000 / 500;
            }
            PlacementCandidate? wallGhost = ada.Plan(wall, eye, top - eye, 0);
            double deployable = Cost(() => ada.PlanAt(furnacePlaceable, top, 0)), piece = Cost(() => ada.Plan(wall, eye, top - eye, 0));
            Fixtures.Report($"a ghost a frame: {deployable:0} µs for the furnace, {piece:0} µs for a wall on the foundation's sockets ({wallGhost?.Outcome})");
            ada.Local.Place(ghost!, ContainerRef.Main, SlotOf(ada.Local.Inventory.Main, furnaceItem));
            RunUntil(rig, () => ada.Things.Any(t => t.Look == furnaceItem.Id), "the furnace");
            RemoteThing seen = ada.Things.Single(t => t.Look == furnaceItem.Id);
            Assert.True(seen.Support.Family == TargetFamily.Piece && seen.Support.Id == foundationId.Value, "her client knows what it stands on");
            furnaceId = seen.Id;
            rig.Server.SaveAll().Wait();
        }
        using (var rig = new LoopbackRig(file.Path))
        {
            rig.Server.Inspect(sv =>
            {
                Zone z = sv.Zone;
                Piece f = z.Building!.PieceOf(foundationId)!;
                Deployable furnace = z.Statics().OfType<Deployable>().Single(d => d.Id == furnaceId);
                Assert.True(furnace.Support == f.Ref && z.DependentsOf(f.Ref).Contains(furnace.Ref), "after the restart it stands on the foundation");
                z.Building.Damage(f, new DamageHit(Vector3.UnitY, Melee: 1e6), z.BuildingNow);
                Assert.True(f.Gone && !z.Statics().Any(s => s.Id == furnaceId), "the foundation gone, the furnace with it");
                return 0;
            });
        }
    }
}
