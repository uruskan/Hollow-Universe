using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>The interaction system (GROUND_SYSTEMS.md §5): what the E key does, one rule a test.</summary>
public static class InteractionTests
{
    /// <summary>
    /// GROUND_SYSTEMS.md §5, verbs are data: every verb in interactions.json has exactly one capability's handler, a reach
    /// from survival.json's, a label and a glyph; the check names what is wrong when it isn't so (a verb no handler does,
    /// one two handlers claim, a handler's verb the data lacks), and nothing starts.
    /// </summary>
    [Test]
    public static void EveryVerbHasExactlyOneHandler()
    {
        GameContent c = ContentFixture.Content;
        InteractionData verbs = c.Interactions;
        var rules = new InteractionRules(c, CapabilityHandlers.Create(c));
        Assert.True(verbs.Verbs.Count > 30 && Enumerable.Range(0, verbs.Verbs.Count).All(v => rules.Handlers.Find(v) != null), "every verb has its handler");
        Assert.True(verbs.Verbs.All(v => v.Reach > 0 && v.Reach <= verbs.LongestReach && v.Label.Length > 0 && v.Glyph.Length > 0),
            "each has a reach, a label and a glyph");
        Assert.Near(0.35, verbs.HoldSeconds, 1e-6, "E held 0.35 s opens the radial");

        // The container's handler swapped for one that claims "open", the door's "door-open" and a verb the data lacks.
        var broken = new Registry<ICapability>("capabilities", c.Capabilities.Keys);
        foreach (ICapability handler in CapabilityHandlers.Create(c).Handlers.Where(h => h.Key != "container"))
        {
            broken.Add(handler.Key, handler);
        }
        broken.Add("container", new Rogue());
        string problems = "";
        try
        {
            _ = new InteractionRules(c, broken);
        }
        catch (InvalidOperationException e)
        {
            problems = e.Message;
        }
        Fixtures.Report($"{verbs.Verbs.Count} verbs; a broken set: {problems.Replace('\n', ';')}");
        Assert.True(problems.Contains("\"loot\" has no handler") && problems.Contains("\"door-open\" has two handlers") &&
            problems.Contains("\"knock\", which isn't in the list"), "the check names each problem");
    }

    private sealed class Rogue : ICapability, IInteractCapability
    {
        public string Key => "container";

        public ViewerFlags Flags(object thing, in Viewer viewer) => ViewerFlags.None;

        public IReadOnlyList<string> Verbs { get; } = new[] { "open", "door-open", "knock" };

        public void Offer(in InteractContext ctx, List<InteractOption> into)
        {
        }

        public Outcome Do(in InteractContext ctx, int verb, in InteractArgs args) => Outcome.Done;
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §5, one targeting query: E acts on the nearest thing along the view, whatever its family, never
    /// one family before another: a bag dropped in front of a box, the box in front of a bag behind it, an arrow stuck
    /// in the box's side before the box; a wall before a box inside it; a player in front; never the one looking; the
    /// ground when there is nothing else; nothing in the sky.
    /// </summary>
    [Test]
    public static void OneQueryMeetsTheNearestThing()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 40, 40);
        Player a = rig.P(ada);
        Vector3 Eye() => z.EyeOf(rig.Ch(ada).State);
        TargetRef AimAt(Vector3 at) => z.Interaction().Aim(Eye(), at - Eye(), a.Id.Value).Target;

        Deployable box = Box(rig, 40, 37, a.Id);
        Assert.Equal(box.Ref, AimAt(box.Centre), "the box");
        DroppedBag front = z.Spill().Spill(z.GroundAt(40, 38.4f), ItemStack.Of(c.ItemByKey("stone"), 5))!;
        Vector3 frontBall = front.Position + front.Up * AimShapes.BagLift;
        Assert.Equal(front.Ref, AimAt(frontBall), "a bag in front of the box: the bag");
        DroppedBag behind = z.Spill().Spill(z.GroundAt(40, 35.8f), ItemStack.Of(c.ItemByKey("wood"), 5))!;
        Assert.True(front.Id != behind.Id, "two bags");
        Assert.Equal(box.Ref, AimAt(behind.Position + behind.Up * AimShapes.BagLift), "a bag behind the box: the box");

        // An arrow stuck in the box's side, its shaft towards Ada: aimed along it, the arrow.
        SpentArrow arrow = StickInto(rig, ada, box.Ref, box.Centre + box.Up * 0.1f);
        (Vector3 nock, _) = SpentArrow.Shaft(Pose(z, arrow));
        Assert.Equal(arrow.Ref, AimAt(nock), "the arrow before the box it is in");

        // A hut: from outside, its wall stands before the box inside (and the wall offers nothing).
        TestBases.Hut hut = Raids.Hut(rig, 60, 40, grade: 1);
        Deployable chest = Box(rig, 0, 0, a.Id, at: hut.Inside);
        (Vector3 face, Vector3 outward) = hut.Face(hut.Walls[1], height: 0);
        Raids.Stand(rig, ada, z.Settle(face + outward * 1.5f));
        TargetRef wall = AimAt(chest.Centre);
        Assert.True(wall.Family == TargetFamily.Piece && wall.Id == hut.Walls[1].Ref.Id, $"the wall, not the box behind it ({wall})");
        var options = new List<InteractOption>();
        Assert.True(z.Interaction().Options(ada, wall, options) && options.Count == 0, "a wall offers nothing");

        // Bo in front of Ada; Ada looking down at her own feet; the sky.
        Raids.Stand(rig, ada, z.GroundAt(40, 45));
        Entity bo = rig.Add("Bo", 40, 43.5f);
        Assert.Equal(rig.P(bo).Ref, AimAt(z.EyeOf(rig.Ch(bo).State) - z.Frame.UpAt(z.GroundAt(40, 43.5f)) * 0.5f) with { Part = 0 }, "Bo");
        Assert.Equal(TargetRef.Ground, AimAt(rig.Ch(ada).State.Position), "her own feet: the ground under them, not herself");
        Assert.True(!z.Interaction().Aim(Eye(), z.Frame.UpAt(Eye()), a.Id.Value).Hit, "nothing in the sky");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §5, a door is its leaves and its opening: a garage door rolled up into its header is still the
    /// door aimed at through its opening, and E shuts it from there (it couldn't be closed: the view went through); yet
    /// an open doorway hides nothing behind it to act on (a box just inside).
    /// </summary>
    [Test]
    public static void GarageDoorClosesThroughItsOpening()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        Entity ada = rig.Add("Ada", 0, 0);
        ulong me = rig.P(ada).Id.Value;
        (Piece frame, Door door) = Garage(rig, 20, 20, me);
        (Vector3 position, Quaternion rotation) = frame.ZoneTransform;
        Vector3 outward = Vector3.Transform(Vector3.UnitZ, rotation), up = Vector3.Transform(Vector3.UnitY, rotation);
        Raids.Stand(rig, ada, z.Settle(position + outward * 2.2f));
        Vector3 eye = z.EyeOf(rig.Ch(ada).State), middle = position + up * 1.2f;

        AimHit shut = z.Interaction().Aim(eye, middle - eye, me);
        Assert.True(shut.Target.Family == TargetFamily.Door && shut.Target.Id == door.Id.Value, $"the shut door ({shut.Target})");
        Assert.Equal("door-open*, door-pick-up (OpenItFirst)", Offer(z, ada, shut.Target), "shut: E opens it");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, shut.Target, "door-open"), "opened");
        Assert.True(door.Open, "rolled up");

        AimHit open = z.Interaction().Aim(eye, middle - eye, me);
        Assert.True(open.Target.Family == TargetFamily.Door && open.Target.Id == door.Id.Value, $"through its opening, the door ({open.Target})");
        Assert.True(Vector3.Distance(eye + Vector3.Normalize(middle - eye) * open.Distance, middle) < 1.0f, "met in its opening");
        Assert.Equal("door-close*, door-pick-up", Offer(z, ada, open.Target), "open: E shuts it");

        // The open doorway hides nothing behind it: a box just inside is aimed at through it.
        Deployable box = Box(rig, 0, 0, rig.P(ada).Id, at: position - outward * 1.0f);
        Assert.Equal(box.Ref, z.Interaction().Aim(eye, box.Centre - eye, me).Target, "a box just inside, through the open doorway");
        z.Things().RemoveStatic(box);

        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, open.Target, "door-close"), "shut through its opening");
        Assert.True(!door.Open, "shut");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §5, the server's one path for every verb: a verb it doesn't know, the verb's gates (the wounded,
    /// one in a ship's seat), a panel's verb (the interface's), the thing, the one reach rule with the verb's reach, the
    /// thing's offer (a verb it doesn't offer) and the offer's reason when it is offered but can't be done now.
    /// </summary>
    [Test]
    public static void OneIntentPassesGatesReachAndTheOffer()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, 0), bo = rig.Add("Bo", 1.5f, 0);
        Player a = rig.P(ada), b = rig.P(bo);
        Deployable box = Box(rig, 0, -2.2f, a.Id);
        Deployable bench = Box(rig, 2, -2.2f, a.Id, "workbench-1");

        Assert.Equal(OutcomeCode.Invalid, z.Interaction().Interact(ada, box.Ref, 9999), "a verb number outside the list");
        Assert.Equal(OutcomeCode.NotFound, z.Interaction().Interact(ada, new TargetRef(TargetFamily.Thing, 0xDEAD_BEEF), "open"), "a thing that isn't");
        Assert.Equal(OutcomeCode.NotUsable, z.Interaction().Interact(ada, box.Ref, "door-open"), "a verb the box doesn't offer");
        Assert.Equal(OutcomeCode.NotUsable, z.Interaction().Interact(ada, bench.Ref, "tech-tree"), "a panel's verb is the interface's own");
        box.Storage!.Set(0, ItemStack.Of(c.ItemByKey("wood"), 10));
        Assert.Equal(OutcomeCode.NotEmpty, z.Interaction().Interact(ada, box.Ref, "retrieve"), "offered, but not now: the offer's reason");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, box.Ref, "open"), "opened");
        Assert.True(a.Looting == box.Id, "looking inside");
        z.Actions().Close(ada);

        z.Wounded().Wound(bo, b, "Test", EntityId.None, "");
        Assert.Equal(OutcomeCode.Dead, z.Interaction().Interact(bo, box.Ref, "open"), "the wounded can't");
        Assert.Equal("help-up*, loot", Offer(z, ada, b.Ref), "a player down: helped up with a tap, looted with held E");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, b.Ref, "help-up"), "helped up");

        Raids.Stand(rig, ada, z.GroundAt(0, 4));
        Assert.Equal(OutcomeCode.OutOfReach, z.Interaction().Interact(ada, box.Ref, "open"), "from 6 m");
        Assert.Equal(OutcomeCode.OutOfReach, z.Interaction().Interact(ada, box.Ref, "retrieve"), "reach before the offer's reason (it isn't empty)");

        // In a ship's seat: nothing on foot.
        Ship ship = z.Ships().SpawnShip(c.Ship("tin-finch")!, z.GroundAt(-20, 0), 0, a.Id, 10);
        rig.Step(45);
        ship.HatchOpen = true;
        ship.HatchProgress = Ship.HatchTicks;
        Raids.Stand(rig, ada, z.Settle(ship.PointOf(ship.Def.Hatch) + new Vector3(1.4f, 0, 0)));
        var hatch = new TargetRef(TargetFamily.Ship, ship.Id.Value, ShipParts.Hatch);
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, hatch, "board"), "Ada boards");
        Assert.Equal(OutcomeCode.Seated, z.Interaction().Interact(ada, box.Ref, "open"), "seated, she does nothing on foot");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §5, the code lock (the owner's): on a fresh one its owner's tap sets the code (the keypad asks
    /// first); locking waits for a code. Once set, a stranger's tap enters a code, the door refusing him with the reason;
    /// the right code lets him through.
    /// </summary>
    [Test]
    public static void FreshCodeLockSetsItsCodeFirst()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        Entity ada = rig.Add("Ada", 0, 0), bo = rig.Add("Bo", 1, 0);
        ulong me = rig.P(ada).Id.Value;
        TestBases.Hut hut = Raids.Hut(rig, 10, 0, grade: 1, owner: me, door: "wooden-door");
        Door door = hut.Door!;
        Assert.True(z.Building().World!.AttachCodeLock(me, door, out _).Ok, "a code lock fitted");
        (Vector3 front, Vector3 outward) = hut.Face(hut.Doorway, height: 0);
        Raids.Stand(rig, ada, z.Settle(front + outward * 1.4f));
        Raids.Stand(rig, bo, z.Settle(front + outward * 1.6f + Vector3.Normalize(Vector3.Cross(z.Frame.UpAt(front), outward)) * 0.8f));
        Assert.True(z.Interaction().Interactions.Verbs.Verb("set-code")!.Code, "set-code asks for a code first");

        Assert.Equal("set-code*, door-open, code-lock (NoCode), code-remove", Offer(z, ada, door.Ref), "fresh: SET CODE is the tap");
        Assert.Equal("door-open*", Offer(z, bo, door.Ref), "to Bo, an open door");
        Assert.Equal(OutcomeCode.NoCode, z.Interaction().Interact(ada, door.Ref, "code-lock"), "no locking without a code");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, door.Ref, "set-code", code: "2580"), "the code set");

        Assert.Equal("door-open*, code-unlock, change-code (UnlockItFirst), code-remove (UnlockItFirst)", Offer(z, ada, door.Ref), "locked, Ada's");
        Assert.Equal("enter-code*, door-open (Locked)", Offer(z, bo, door.Ref), "to Bo: enter the code");
        Assert.Equal(OutcomeCode.Locked, z.Interaction().Interact(bo, door.Ref, "door-open"), "locked to Bo");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(bo, door.Ref, "enter-code", code: "2580"), "the right code");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(bo, door.Ref, "door-open"), "he opens it");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §5, the tool cupboard: a stranger's tap authorises (holding E shows the rest: its store opens to
    /// anyone there, GROUND.md §6.5); once on its list, the tap opens its store, and the list's own verbs are offered.
    /// </summary>
    [Test]
    public static void CupboardAuthorisesFirstThenOpens()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        Entity ada = rig.Add("Ada", 0, 0), bo = rig.Add("Bo", 1, 0);
        TestBases.Hut hut = Raids.Hut(rig, 10, 0, grade: 1, owner: rig.P(ada).Id.Value, door: null);
        ClaimDevice cupboard = hut.Claim!;
        Raids.Stand(rig, bo, z.Settle(hut.Inside + hut.Direction(new(0, 0, 1)) * 0.6f));

        Assert.Equal("authorise*, open", Offer(z, bo, cupboard.Ref), "to a stranger: authorise, and its store");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(bo, cupboard.Ref, "authorise"), "Bo authorised");
        Assert.True(cupboard.IsAuthorised(rig.P(bo).Id.Value), "on its list");
        Assert.Equal("open*, deauthorise, clear-list, cupboard-pick-up", Offer(z, bo, cupboard.Ref), "listed: E opens its store");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(bo, cupboard.Ref, "open"), "its store open");
        Assert.True(rig.P(bo).Looting == cupboard.Id, "looking in it");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §11.3, a deployable picked back up as its item: held E offers it on any deployable, emptied first,
    /// and only where the player may build (another's cupboard covering it refuses).
    /// </summary>
    [Test]
    public static void DeployablesArePickedUpEmptyWhereOneMayBuild()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, 0), bo = rig.Add("Bo", 30, 0);
        Player a = rig.P(ada);
        Assert.True(c.Deployables.All(d => d.Pickup), "every deployable is picked up (deployables.json pickup)");

        Deployable box = Box(rig, 0, -2.2f, a.Id);
        box.Storage!.Set(3, ItemStack.Of(c.ItemByKey("stone"), 5));
        Assert.Equal("open*, retrieve (NotEmpty)", Offer(z, ada, box.Ref), "full: emptied first");
        box.Storage.Set(3, default);
        box.PublishChanges();
        Assert.Equal("open*, retrieve", Offer(z, ada, box.Ref), "empty: picked up with held E");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, box.Ref, "retrieve"), "picked up");
        Assert.True(z.Things().StaticOf(box.Id) == null && a.Inventory.Count(c.ItemByKey("large-box")) == 1, "gone, back as its item");

        // Ada's box in Bo's hut: his cupboard doesn't list her.
        TestBases.Hut hut = Raids.Hut(rig, 30, 10, grade: 1, owner: rig.P(bo).Id.Value, door: null);
        Deployable inside = Box(rig, 0, 0, a.Id, at: hut.Inside + hut.Direction(new(-1, 0, 0)) * 0.9f);
        Raids.Stand(rig, ada, z.Settle(hut.Inside + hut.Direction(new(1, 0, 1)) * 0.5f));
        Assert.Equal(OutcomeCode.BuildingBlocked, z.Interaction().Interact(ada, inside.Ref, "retrieve"), "not under another's cupboard");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §5, spent arrows (the owner's): one that stays whole sticks where it hit, in a wall, a box, a deer
    /// or a player, alive, its shaft out along its flight, never into a bag; it rides what holds it (a deer's bone as it
    /// goes), is its own small target, and is taken back with E, from the living too; when what holds it goes it falls to
    /// the ground; its lifetime takes it away.
    /// </summary>
    [Test]
    public static void SpentArrowsStickRideAndAreTakenBack()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        GameContent c = rig.C;
        ItemDef arrowItem = c.ItemByKey("arrow");
        Entity ada = rig.Add("Ada", -30, 30);
        Player a = rig.P(ada);
        rig.Give(ada, "bow", 1);
        rig.Give(ada, "arrow", 12);
        z.Actions().MoveItem(ada, ContainerRef.Main, 0, ContainerRef.Belt, 0, 0, 0);

        // Shot with the bow at a hut's wall from 7 m: an arrow that stays whole (items.json's recover) sticks in it.
        TestBases.Hut hut = Raids.Hut(rig, -45, 30, grade: 1);
        (Vector3 face, Vector3 outward) = hut.Face(hut.Walls[1]);
        Raids.Stand(rig, ada, z.Settle(face + outward * 7));
        SpentArrow? inWall = null;
        int shots = 0;
        while (inWall == null && shots++ < 8)
        {
            inWall = Shoot(rig, ada, face);
        }
        Assert.True(inWall != null, $"an arrow stuck in {shots} shots");
        Assert.Equal(hut.Walls[1].Ref, inWall!.Link.Support with { Part = 0 }, "held by the wall");
        Assert.True(!z.Things().Statics().OfType<DroppedBag>().Any(), "no bag of arrows anywhere");
        (Vector3 nock, Vector3 tip) = SpentArrow.Shaft(Pose(z, inWall));
        Assert.True(Vector3.Dot(tip - face, outward) < 0.02f && Vector3.Dot(nock - face, outward) > 0.5f, "its tip in the wall, its shaft out");
        int carried = a.Inventory.Count(arrowItem);
        Raids.Stand(rig, ada, z.Settle(face + outward * 1.6f));
        Assert.Equal("pick-up*", Offer(z, ada, inWall.Ref), "E takes it");
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, inWall.Ref, "pick-up"), "taken back");
        Assert.True(z.Arrows().ArrowOf(inWall.Id) == null && a.Inventory.Count(arrowItem) == carried + 1, "out of the wall, into Ada's slots");

        // In a deer, alive: it rides the deer's bone, and is taken from it.
        Herd herd = AnimalRig.Herd(rig, "deer", -30, 10, 1, 2);
        Animal deer = herd.Members[0];
        rig.Step(2); // (the herd is found where it is once it has stepped)
        Raids.Stand(rig, ada, z.Settle(deer.Position + new Vector3(0, 0, 5)));
        z.Targets().TryNearest(deer.Ref, z.EyeOf(rig.Ch(ada).State), out Vector3 hide);
        SpentArrow inDeer = StickInto(rig, ada, deer.Ref, hide);
        Assert.True(inDeer.Link.Support.Family == TargetFamily.Animal && inDeer.Link.Support.Id == deer.Id.Value && inDeer.Link.Support.Part > 0,
            "held by a bone of the deer");
        Vector3 was = Pose(z, inDeer).Position;
        deer.Position += new Vector3(4, 0, 0);
        Assert.True(Vector3.Distance(Pose(z, inDeer).Position, was + new Vector3(4, 0, 0)) < 0.05f, "it goes with the deer");
        Raids.Stand(rig, ada, z.Settle(Pose(z, inDeer).Position + new Vector3(0, 0, 1.5f)));
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, inDeer.Ref, "pick-up"), "taken from the living deer");

        // In Bo, alive: it goes where he goes; taken from him.
        Entity bo = rig.Add("Bo", -10, 30);
        Player b = rig.P(bo);
        Raids.Stand(rig, ada, z.Settle(z.GroundAt(-10, 34)));
        rig.Step(2); // (a body is hit where the shooter saw it: its history needs a tick or two)
        z.Targets().TryNearest(b.Ref with { Part = 0 }, z.EyeOf(rig.Ch(ada).State), out Vector3 chest);
        SpentArrow inBo = StickInto(rig, ada, b.Ref, chest);
        Assert.True(inBo.Link.Support.Family == TargetFamily.Character && inBo.Link.Support.Id == b.Id.Value, "held by Bo");
        was = Pose(z, inBo).Position;
        Raids.Stand(rig, bo, z.GroundAt(-8, 30));
        Vector3 moved = rig.Ch(bo).State.Position - z.GroundAt(-10, 30);
        Assert.True(Vector3.Distance(Pose(z, inBo).Position, was + moved) < 0.1f, "it goes with Bo");
        Raids.Stand(rig, ada, z.Settle(z.GroundAt(-8, 31.5f)));
        Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, inBo.Ref, "pick-up"), "taken from Bo");

        // In a box that goes: it falls to the ground. Its lifetime ends it.
        Deployable box = Box(rig, -30, 26, a.Id);
        Raids.Stand(rig, ada, z.GroundAt(-30, 29));
        SpentArrow inBox = StickInto(rig, ada, box.Ref, box.Centre);
        Assert.Equal(box.Ref, inBox.Link.Support, "held by the box");
        Assert.True(inBox.EndsAt == z.Tick + c.Survival.ArrowLifetime.Ticks, "its lifetime from survival.json");
        z.Things().RemoveStatic(box);
        Assert.True(z.Arrows().ArrowOf(inBox.Id) == inBox && inBox.Link.Support == TargetRef.Ground, "the box gone, the ground holds it");
        Assert.True(z.HeightAboveGround(Pose(z, inBox).Position) < 0.1f, "lying on the ground");
        inBox.EndsAt = z.Tick + 1;
        rig.Step(31);
        Assert.True(z.Arrows().ArrowOf(inBox.Id) == null && inBox.Gone, "its time up, gone");
        Fixtures.Report($"shots until one stuck in the wall: {shots}; arrows left: {z.Arrows().All.Count}");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §5 over the network: a client's one query meets what the server's meets along the same view, its
    /// options are the server's (so the prompt and the radial say what the server does), a stuck arrow comes to it where
    /// the server has it, and its tap takes the arrow on the server.
    /// </summary>
    [Test]
    public static void ClientAimsAndOffersAsTheServer()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport Ada 0 0");
        rig.Run(0.6, (_, _) => default);
        var adaId = new EntityId(ada.EntityId.Value);
        GameContent c = ContentFixture.Content;
        (ulong box, ulong bag, ulong arrow) = rig.Server.Inspect(sv =>
        {
            Zone z = sv.Zone;
            z.Players().TryFindPlayer(adaId, out Entity e, out Player p);
            Vector3 at = z.GroundAt(0, -2.5f);
            var d = new Deployable(z.NextId(), c.Deployable("large-box")!, at, z.Frame.UpAt(at), 0, p.Id);
            z.Things().AddStatic(d);
            DroppedBag dropped = z.Spill().Spill(z.GroundAt(1.2f, -1.4f), ItemStack.Of(c.ItemByKey("stone"), 3))!;
            Vector3 eye = z.EyeOf(z.Entities.Get<Character>(e).State);
            Vector3 aim = d.Centre + new Vector3(-0.4f, 0.1f, 0);
            TraceHit hit = z.Damage().Trace(eye, Vector3.Normalize(aim - eye), 10, CombatSystem.ArrowRadius, p.Id, z.Tick);
            SpentArrow stuck = z.Arrows().StickArrow(new Projectile { Id = 7, Ammo = c.ItemByKey("arrow"), Velocity = Vector3.Normalize(aim - eye) * 50 }, hit)!;
            return (d.Id.Value, dropped.Id.Value, stuck.Id.Value);
        });
        RunUntil(rig, () => ada.ThingOf(new EntityId(box)) != null && ada.ThingOf(new EntityId(bag)) != null && ada.ArrowOf(new EntityId(arrow)) is { Placed: true },
            "the box, the bag and the arrow");
        RemoteArrow remote = ada.ArrowOf(new EntityId(arrow))!;
        TargetPose served = rig.Server.Inspect(sv => sv.Zone.Targets().TryPose(new TargetRef(TargetFamily.Arrow, arrow), out TargetPose pose) ? pose : default);
        Assert.True(Vector3.Distance(remote.Pose.Position, served.Position) < 0.01f &&
            Vector3.Dot(Vector3.Transform(Vector3.UnitX, remote.Pose.Rotation), Vector3.Transform(Vector3.UnitX, served.Rotation)) > 0.999f,
            "the arrow where the server has it, along its flight");

        // Along a fan of views from Ada's eye: the same thing met, the same options offered (the client leaves out those
        // beyond their verb's reach, which the server would refuse).
        Vector3 eye = ada.Character.Position + ada.Frame.UpAt(ada.Character.Position) * ada.EyeHeight;
        var views = new List<Vector3>();
        for (int yaw = -40; yaw <= 40; yaw += 4)
        {
            for (int pitch = -50; pitch <= 10; pitch += 6)
            {
                float y = yaw * MathF.PI / 180, p = pitch * MathF.PI / 180;
                views.Add(new Vector3(MathF.Sin(y) * MathF.Cos(p), MathF.Sin(p), -MathF.Cos(y) * MathF.Cos(p)));
            }
        }
        (AimHit Hit, InteractOption[] Options)[] there = rig.Server.Inspect(sv =>
        {
            sv.Zone.Players().TryFindPlayer(adaId, out Entity e, out _);
            var offered = new List<InteractOption>();
            return views.Select(view =>
            {
                AimHit hit = sv.Zone.Interaction().Aim(eye, view, adaId.Value);
                sv.Zone.Interaction().Options(e, hit.Target, offered);
                return (hit, offered.ToArray());
            }).ToArray();
        });
        var mine = new List<InteractOption>();
        int met = 0;
        var families = new HashSet<TargetFamily>();
        for (int i = 0; i < views.Count; i++)
        {
            AimHit here = ada.Aim(eye, views[i]);
            Assert.True(here.Target == there[i].Hit.Target && MathF.Abs(here.Distance - there[i].Hit.Distance) < 0.05f,
                $"view {i}: the client met {here.Target} at {here.Distance:0.00}, the server {there[i].Hit.Target} at {there[i].Hit.Distance:0.00}");
            if (here.Hit && ada.Options(here.Target, eye, mine, out _) && mine.Count > 0)
            {
                met++;
                families.Add(here.Target.Family);
                InteractOption[] offeredThere = there[i].Options.Where(o => mine.Any(m => m.Verb == o.Verb)).ToArray();
                Assert.True(offeredThere.SequenceEqual(mine), $"{here.Target}: the client offers {string.Join(", ", mine)}, the server {string.Join(", ", there[i].Options)}");
            }
        }
        // A frame's query and options (the prompt's), timed over the fan.
        var clock = Stopwatch.StartNew();
        for (int round = 0; round < 10; round++)
        {
            foreach (Vector3 view in views)
            {
                ada.AimInteraction(eye, view);
            }
        }
        double frame = clock.Elapsed.TotalMilliseconds / (10 * views.Count);
        Fixtures.Report($"{met} views met a thing with options, of {families.Count} families ({string.Join(", ", families)}); " +
            $"a frame's query and options {frame * 1000:0} µs");
        Assert.True(families.IsSupersetOf(new[] { TargetFamily.Thing, TargetFamily.Arrow }), "the box, the bag and the arrow among them");

        // Ada's tap on the arrow: taken on the server, gone from her client, in her slots.
        InteractAim? aim = ada.AimInteraction(eye, Vector3.Normalize(remote.Pose.Position - Vector3.Transform(Vector3.UnitX, remote.Pose.Rotation) * 0.3f - eye));
        Assert.True(aim is { Primary: >= 0 } && aim.Target == new TargetRef(TargetFamily.Arrow, arrow), $"aimed at the arrow ({aim?.Target})");
        ada.Local.Interact(aim!.Target, aim.Options[aim.Primary].Verb);
        RunUntil(rig, () => ada.ArrowOf(new EntityId(arrow)) == null && ada.Local.Inventory.Count(c.ItemByKey("arrow")) == 1, "the arrow taken");
    }

    // ---------------------------------------------------------------- helpers

    private static void RunUntil(LoopbackRig rig, Func<bool> done, string what, double seconds = 6)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < seconds, $"timed out waiting for {what}");
            rig.Run(0.05, (_, _) => default);
        }
    }

    /// <summary>A deployable (a large box unless named) on the ground at a point of the zone, or at a given point.</summary>
    private static Deployable Box(GameRig rig, float x, float z, EntityId owner, string key = "large-box", Vector3? at = null)
    {
        Zone zone = rig.Zone;
        Vector3 where = at ?? zone.GroundAt(x, z);
        var d = new Deployable(zone.NextId(), rig.C.Deployable(key)!, where, zone.Frame.UpAt(where), 0, owner);
        zone.Things().AddStatic(d);
        return d;
    }

    /// <summary>What a player is offered by a target, ranked: each verb's key, the tap's starred, a refusal's reason in brackets.</summary>
    private static string Offer(Zone z, Entity e, in TargetRef target)
    {
        var options = new List<InteractOption>();
        Assert.True(z.Interaction().Options(e, target, options), $"{target} is known");
        int primary = InteractionRules.Primary(options);
        return string.Join(", ", options.Select((o, i) =>
            z.Interaction().Interactions.Verbs.Verb(o.Verb)!.Key + (i == primary ? "*" : "") + (o.Enabled ? "" : $" ({o.Reason.Code})")));
    }

    private static TargetPose Pose(Zone z, SpentArrow arrow) =>
        z.Targets().TryPose(arrow.Ref, out TargetPose pose) ? pose : throw new TestFailure($"arrow {arrow.Id} has no pose");

    /// <summary>An arrow shot from a player's eye at a point sticks in what it meets there (the hit the bow's flight would make).</summary>
    private static SpentArrow StickInto(GameRig rig, Entity e, in TargetRef expected, Vector3 at)
    {
        Zone z = rig.Zone;
        Vector3 eye = z.EyeOf(rig.Ch(e).State), along = Vector3.Normalize(at - eye);
        TraceHit hit = z.Damage().Trace(eye, along, Vector3.Distance(eye, at) + 1, CombatSystem.ArrowRadius, rig.P(e).Id, z.Tick);
        Assert.True(hit.Hit && hit.Target.Family == expected.Family && hit.Target.Id == expected.Id, $"the shot meets {expected} ({hit.Target})");
        return z.Arrows().StickArrow(new Projectile { Id = (uint)z.Tick, Ammo = rig.C.ItemByKey("arrow"), Velocity = along * 50 }, hit) ?? throw new TestFailure("not stuck");
    }

    /// <summary>The bow drawn for a second and loosed at a point; steps until the arrow ends. The arrow it left, if it stuck.</summary>
    private static SpentArrow? Shoot(GameRig rig, Entity e, Vector3 at)
    {
        Zone z = rig.Zone;
        var before = new HashSet<EntityId>(z.Arrows().All.Select(x => x.Id));
        void Hold(bool loose)
        {
            rig.Look(e, at);
            rig.Ch(e).Actions = new ActionInput { HeldSlot = 0, Buttons = ActionButtons.Secondary | (loose ? ActionButtons.Primary | ActionButtons.PrimaryPressed : 0) };
        }
        rig.Step(40, () => Hold(false));
        rig.Step(1, () => Hold(true));
        rig.Step(15, () => Hold(false));
        return z.Arrows().All.FirstOrDefault(x => !before.Contains(x.Id));
    }

    /// <summary>A foundation with a wall frame on its south edge (+Z) and a garage door in it.</summary>
    private static (Piece Frame, Door Door) Garage(GameRig rig, float x, float z, ulong owner)
    {
        Zone zone = rig.Zone;
        BuildingWorld world = zone.Building().World!;
        BuildingData data = world.Data;
        Vector3 at = zone.GroundAt(x, z);
        zone.Physics.EnsureGround(at);
        PieceDef foundationDef = data.Piece("foundation")!, frameDef = data.Piece("wall-frame")!;
        Piece foundation = world.AddFirst(TestBases.FrameOver(zone, data, foundationDef, at, 0), foundationDef, 2, owner, zone.Building().BuildingNow);
        PlacementPlan plan = world.FromIntent(frameDef, foundation, new SocketId(SocketKind.Edge, 0), 0) ?? throw new TestFailure("no wall frame");
        Piece frame = world.Add(plan.Structure!, frameDef, 2, plan.Pose, owner, zone.Building().BuildingNow);
        Assert.True(world.PlaceDoor(owner, data.Door("garage-door")!, frame, 0, out Door? door).Ok, "the garage door fitted");
        return (frame, door!);
    }
}
