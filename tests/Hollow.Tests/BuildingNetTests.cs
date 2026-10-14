using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Server;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Placement;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>Building over the network (GROUND.md §6.3, phase 2): intents, replication, streaming and locks.</summary>
public static class BuildingNetTests
{
    private static void RunUntil(LoopbackRig rig, Func<bool> done, string what, double seconds = 5)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < seconds, $"timed out waiting for {what}");
            rig.Run(0.05, (_, _) => default);
        }
    }

    private static int SlotOf(Container c, string key) =>
        Enumerable.Range(0, c.Size).First(i => c[i].Item == ContentFixture.Item(key).Id);

    private static Vector3 Eye(GameClient client) =>
        client.Character.Position + client.Frame.UpAt(client.Character.Position) * client.EyeHeight;

    // Every server and client loads its own copy of the data: pieces are asked of the one that will use them.
    private static PieceDef Piece(GameClient client, string key) => client.Content.Building.Piece(key)!;

    private static PieceDef Piece(Zone zone, string key) => zone.Building().World!.Data.Piece(key)!;

    // A client's ghost of a piece where it aims (the placement pipeline on its replicated world, GROUND_SYSTEMS.md §6).
    private static PlacementCandidate? Ghost(GameClient client, string key, Vector3 eye, Vector3 direction) =>
        client.Plan(client.Content.Placement.Of(Piece(client, key))!, eye, direction, 0);

    // Places a piece's ghost, the building plan carried in the main slots.
    private static uint PlacePiece(GameClient client, PlacementCandidate ghost) =>
        client.Local.Place(ghost, ContainerRef.Main, SlotOf(client.Local.Inventory.Main, "building-plan"));

    // Places what an item places where its ghost shows it, aimed at a point from the player's eye; the ghost must fit.
    private static uint PlaceItem(GameClient client, string item, Vector3 at)
    {
        PlacementCandidate? ghost = client.PlanAt(client.Content.ItemByKey(item).Places!, at, 0);
        Assert.True(ghost is { Valid: true }, $"the {item}'s ghost fits ({ghost})");
        return client.Local.Place(ghost!, ContainerRef.Main, SlotOf(client.Local.Inventory.Main, item));
    }

    // ---------------------------------------------------------------- building a base on the server for a test

    /// <summary>A first foundation on the ground at a zone point, without checks (owned by <paramref name="owner"/>).</summary>
    internal static Piece Foundation(Zone zone, Vector3 at, ulong owner, int grade = 0)
    {
        BuildingWorld b = zone.Building().World!;
        PlacementPlan plan = b.FoundationAt(Piece(zone, "foundation"), at, 0) ?? throw new TestFailure($"no ground for a foundation at {at}");
        return b.AddFirst(plan.Frame, plan.Piece, grade, owner, zone.Building().BuildingNow);
    }

    /// <summary>A piece on a socket of another, without checks.</summary>
    internal static Piece On(Zone zone, Piece parent, SocketId socket, string key, ulong owner, int orientation = 0, int grade = 0)
    {
        BuildingWorld b = zone.Building().World!;
        PlacementPlan plan = b.FromIntent(Piece(zone, key), parent, socket, orientation) ?? throw new TestFailure($"{key} doesn't fit {parent.Def.Key} {socket}");
        return b.Add(plan.Structure!, plan.Piece, grade, plan.Pose, owner, zone.Building().BuildingNow);
    }

    /// <summary>The edge of a slab whose middle points furthest along a frame direction.</summary>
    internal static SocketId Facing(Piece slab, double x, double z)
    {
        int best = 0;
        double most = double.MinValue;
        for (int e = 0; e < slab.Def.EdgeCount; e++)
        {
            Vector3D middle = PieceGeometry.Rotate(PieceGeometry.Edge(slab.Def.Shape, e, 3).Middle, slab.Pose.Yaw);
            double along = middle.X * x + middle.Z * z;
            if (along > most)
            {
                most = along;
                best = e;
            }
        }
        return new SocketId(SocketKind.Edge, best);
    }

    /// <summary>The edge of a slab whose middle is nearest a zone point.</summary>
    internal static SocketId Nearest(Piece slab, Vector3 point)
    {
        int best = 0;
        double least = double.MaxValue;
        for (int e = 0; e < slab.Def.EdgeCount; e++)
        {
            Vector3D middle = PieceGeometry.ToFrame(slab.Pose, PieceGeometry.Edge(slab.Def.Shape, e, 3).Middle);
            double distance = Vector3.Distance(BuildingWorld.F(slab.Structure.Frame.ToZone(middle)), point);
            if (distance < least)
            {
                least = distance;
                best = e;
            }
        }
        return new SocketId(SocketKind.Edge, best);
    }

    /// <summary>
    /// GROUND.md §6.3 over the network: a player without the wood is refused, with the reason, and keeps what he had.
    /// A free foundation and a wall on its edge are placed through intents: each shows at once on the client (the twig
    /// leaves the inventory, the piece is drawn as pending), is checked again by the server, and arrives in every
    /// client's copy at exactly the pose the ghost showed, with the server's stability and body IDs.
    /// </summary>
    [Test]
    public static void PlacingGoesThroughTheServer()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport Ada 0 0");
        rig.Server.Execute("teleport Bo -6 0");
        rig.Server.Execute("give Ada building-plan 1");
        rig.Server.Execute("give Ada wood 300");
        rig.Server.Execute("give Bo building-plan 1");
        RunUntil(rig, () => ada.Local.Inventory.Count(ContentFixture.Item("wood")) == 300 && bo.Local.Carries(Simulation.Content.BuildingRole.Plan),
            "the plans and wood");
        rig.Run(0.5, (_, _) => default);

        // Bo has no wood: refused with the reason, nothing taken.
        var refused = new List<IntentAnswer>();
        bo.Local.IntentRejected += refused.Add;
        Vector3 boEye = Eye(bo);
        Vector3 boGround = bo.Character.Position + new Vector3(3.5f, 0, 0);
        PlacementCandidate? boPlan = Ghost(bo, "foundation", boEye, Vector3.Normalize(boGround - boEye));
        Assert.True(boPlan is { Valid: false } && boPlan.Outcome.Code == OutcomeCode.NotEnough, $"Bo's ghost says why ({boPlan})");
        uint boSeq = PlacePiece(bo, boPlan!);
        RunUntil(rig, () => refused.Any(a => a.Seq == boSeq), "Bo's refusal");
        Assert.Equal(OutcomeCode.NotEnough, refused.Single().Code, "not enough wood");
        Assert.Equal("Not enough wood", ContentFixture.Content.Texts.Line(refused.Single().Outcome), "names the material");
        Fixtures.Report($"Bo was told: \"{ContentFixture.Content.Texts.Line(refused.Single().Outcome)}\"");
        Assert.True(bo.Local.PendingPlacements.Count == 0 && bo.Building!.PieceCount == 0, "and nothing was built");

        var answers = new List<IntentAnswer>();
        ada.Local.IntentAnswered += answers.Add;
        Vector3 eye = Eye(ada), up = ada.Frame.UpAt(eye);
        Vector3 ground = ada.Character.Position + new Vector3(3.5f, 0, 0);
        PlacementCandidate? plan = Ghost(ada, "foundation", eye, Vector3.Normalize(ground - eye));
        Assert.True(plan is { Valid: true, Detail: PlacementPlan { Support: null } }, $"the ghost: a free foundation ({plan})");
        uint seq = PlacePiece(ada, plan!);
        Assert.Equal(250, ada.Local.Inventory.Count(ContentFixture.Item("wood")), "the twig leaves at once");
        Assert.Equal(1, ada.Local.PendingPlacements.Count, "and the piece shows at once");
        RunUntil(rig, () => ada.Building!.PieceCount == 1 && bo.Building!.PieceCount == 1, "the foundation on both clients");
        Assert.True(answers.Single(a => a.Seq == seq).Done, "placed");
        RunUntil(rig, () => ada.Local.PendingPlacements.Count == 0, "the pending piece replaced by the real one");

        // A wall on the foundation's edge nearest Ada: the server derives the pose from support, socket and orientation.
        Piece foundation = ada.Building!.AllPieces.Single();
        (Vector3 centre, _) = foundation.ZoneTransform;
        eye = Eye(ada);
        Vector3 toward = Vector3.Normalize(centre - eye);
        Vector3 edge = centre - new Vector3(toward.X, 0, toward.Z) * 1.4f + up * 1.2f;
        PlacementCandidate? wall = Ghost(ada, "wall", eye, Vector3.Normalize(edge - eye));
        PlacementPlan? wallPlan = wall?.Detail as PlacementPlan;
        Assert.True(wall is { Valid: true } && wallPlan!.Support == foundation, $"the ghost: a wall on the foundation ({wall})");
        uint wallSeq = PlacePiece(ada, wall!);
        RunUntil(rig, () => answers.Any(a => a.Seq == wallSeq) && ada.Building.PieceCount == 2 && bo.Building!.PieceCount == 2, "the wall");
        Piece arrived = ada.Building.AllPieces.Single(p => p.Def.Key == "wall");
        Assert.True(arrived.Pose == wallPlan!.Pose && arrived.Structure.Frame.Equals(wallPlan.Frame), "exactly where the ghost was");
        Assert.Equal(1000, foundation.Stability, "the copy's stability is solved like the server's");
        Assert.Equal(arrived.Stability, rig.Server.Inspect(sv => sv.Zone.Building().World!.PieceOf(arrived.Id)!.Stability), "the wall's too");
        Assert.True(arrived.BodyId != 0 && arrived.BodyId == rig.Server.Inspect(sv => sv.Zone.Building().World!.PieceOf(arrived.Id)!.BodyId),
            "with the server's body ID");

    }

    /// <summary>
    /// Doors and code locks over the network: Ada fits a door and a code lock and sets the code (it locks); Bo is kept
    /// out, waits after a wrong code, then enters the right one and opens the door, which every client sees open. Ada's
    /// claim device keeps Bo from building on her base.
    /// </summary>
    [Test]
    public static void CodeLocksKeepStrangersOut()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport Ada 0 0");
        rig.Server.Execute("teleport Bo -6 -4");
        rig.Server.Execute("give Ada wooden-door 1");
        rig.Server.Execute("give Ada code-lock 1");
        rig.Server.Execute("give Ada claim-device 1");
        rig.Server.Execute("give Bo building-plan 1");
        rig.Server.Execute("give Bo wood 100");
        RunUntil(rig, () => ada.Local.Inventory.Count(ContentFixture.Item("claim-device")) == 1 &&
            bo.Local.Inventory.Count(ContentFixture.Item("wood")) == 100, "the gifts");
        rig.Run(0.5, (_, _) => default);

        // Ada's base, built on the server: a foundation 3 m from her with a doorway on the edge nearest her.
        ulong adaId = ada.EntityId.Value;
        Vector3 adaAt = ada.Character.Position;
        EntityId doorwayId = rig.Server.Inspect(sv =>
        {
            Piece f = Foundation(sv.Zone, sv.Zone.Settle(new Vector3(0, 0, 3)), adaId);
            return On(sv.Zone, f, Nearest(f, adaAt), "doorway", adaId).Id;
        });
        RunUntil(rig, () => ada.Building!.PieceOf(doorwayId) != null && bo.Building!.PieceOf(doorwayId) != null, "the doorway");
        Piece doorway = ada.Building!.PieceOf(doorwayId)!;
        Piece floor = ada.Building.AllPieces.Single(p => p.Def.Key == "foundation");

        var answers = new List<IntentAnswer>();
        ada.Local.IntentAnswered += answers.Add;
        uint Ask(uint seq, string what)
        {
            RunUntil(rig, () => answers.Any(a => a.Seq == seq), what);
            IntentAnswer answer = answers.Single(a => a.Seq == seq);
            Assert.True(answer.Done, $"{what}: {answer.Code} {ContentFixture.Content.Texts.Line(answer.Outcome)}");
            return seq;
        }
        // The door aimed into its opening, the lock at its spot on the door (their ghosts, GROUND_SYSTEMS.md §6).
        Vector3 opening = doorway.ZoneTransform.Position + ada.Frame.UpAt(doorway.ZoneTransform.Position) * 1.1f;
        Ask(PlaceItem(ada, "wooden-door", opening), "the door fitted");
        Assert.Equal(0, ada.Local.Inventory.Count(ContentFixture.Item("wooden-door")), "the door item is used");
        RunUntil(rig, () => doorway.Door != null && bo.Building!.PieceOf(doorwayId)?.Door != null, "the door on both clients");
        Door door = doorway.Door!;
        Ask(PlaceItem(ada, "code-lock", door.LockSpot.Position), "the code lock fitted");
        Ask(ada.Local.Interact(door.Ref, "set-code", code: "4711"), "the code set");
        RunUntil(rig, () => ada.BuildingReplica!.StateOf(door) is { Lock: LockKind.Code, Locked: true, CanUse: true }, "locked, and Ada may pass");
        // The claim device placed from her floor, in sight of it (the doorway's wall and its shut door hide the floor from
        // outside), beside her.
        Vector3 floorMiddle = BuildingWorld.F(floor.Structure.Frame.ToZone(floor.Pose.Position));
        rig.Server.Execute(FormattableString.Invariant($"teleport Ada {floorMiddle.X:0.00} {floorMiddle.Z:0.00}"));
        rig.Run(0.6, (_, _) => default);
        Vector3 claimAt = BuildingWorld.F(floor.Structure.Frame.ToZone(floor.Pose.Position + new Vector3D(0.8, 0, 0.8)));
        Ask(PlaceItem(ada, "claim-device", claimAt), "the claim device placed");
        RunUntil(rig, () => bo.Building!.Claims.Any(), "the claim device on Bo's client");

        // Its store opens like a box, and takes the wood that pays its upkeep.
        rig.Server.Execute("give Ada wood 200");
        RunUntil(rig, () => ada.Local.Inventory.Count(ContentFixture.Item("wood")) == 200, "the wood");
        ClaimDevice claim = ada.Building.Claims.Single();
        Assert.True(ada.ThingOf(claim.Id)?.Deployable(ada.Content)?.Slots == 24, "the claim device is a thing with a 24-slot store");
        ada.Local.Interact(claim.Ref, "open");
        RunUntil(rig, () => ada.Local.LootKind == Simulation.Items.LootKind.Box && ada.Local.Loot?.Size == 24, "its store open");
        ada.Local.MoveItem(ContainerRef.Main, SlotOf(ada.Local.Inventory.Main, "wood"), ContainerRef.Loot, 0);
        RunUntil(rig, () => rig.Server.Inspect(sv => InventoryRules.Count(new[] { sv.Zone.Building().DeployableOf(sv.Zone.Building().World!.ClaimOf(claim.Id)!)!.Storage! },
            ContentFixture.Item("wood").Id)) == 200, "the wood in the store");
        Assert.True(rig.Server.Inspect(sv => sv.Zone.Building().World!.ProtectedSeconds(sv.Zone.Building().World.ClaimOf(claim.Id)!.Structure)) > 0, "paying its upkeep");
        ada.Local.Close();

        // Bo: locked out; a wrong code makes him wait; the right one lets him in. Ada steps out of the door's way.
        rig.Server.Execute("teleport Ada 5 -1");
        (Vector3 doorAt, _) = doorway.ZoneTransform;
        Vector3 middle = BuildingWorld.F(floor.Structure.Frame.ToZone(floor.Pose.Position));
        Vector3 outward = Vector3.Normalize(new Vector3(doorAt.X - middle.X, 0, doorAt.Z - middle.Z));
        Vector3 outside = doorAt + outward * 2.2f;
        rig.Server.Execute(FormattableString.Invariant($"teleport Bo {outside.X:0.00} {outside.Z:0.00}"));
        rig.Run(0.6, (_, _) => default);
        var bos = new List<IntentAnswer>();
        bo.Local.IntentAnswered += bos.Add;
        Door boDoor = bo.Building!.PieceOf(doorwayId)!.Door!;
        Assert.True(bo.BuildingReplica!.StateOf(boDoor) is { Locked: true, CanUse: false }, "Bo sees a locked door he can't pass");
        OutcomeCode BoAsks(uint seq, string what)
        {
            RunUntil(rig, () => bos.Any(a => a.Seq == seq), what);
            return bos.Single(a => a.Seq == seq).Code;
        }
        Assert.Equal(OutcomeCode.Locked, BoAsks(bo.Local.Interact(boDoor.Ref, "door-open"), "Bo tries the door"), "locked");
        Assert.Equal(OutcomeCode.WrongCode, BoAsks(bo.Local.Interact(boDoor.Ref, "enter-code", code: "1234"), "a wrong code"), "wrong code");
        Assert.Equal(OutcomeCode.WaitForCode, BoAsks(bo.Local.Interact(boDoor.Ref, "enter-code", code: "4711"), "the right code too soon"), "he must wait");
        rig.Run(1.1, (_, _) => default);
        Assert.Equal(OutcomeCode.Done, BoAsks(bo.Local.Interact(boDoor.Ref, "enter-code", code: "4711"), "the right code"), "let in");
        Assert.Equal(OutcomeCode.Done, BoAsks(bo.Local.Interact(boDoor.Ref, "door-open"), "Bo opens the door"), "opened");
        RunUntil(rig, () => door.Open && boDoor.Open, "the door open on both clients");
        Assert.True(rig.Server.Inspect(sv => sv.Zone.Building().World!.DoorOf(door.Id)!.Open), "and on the server");

        // The claim: Bo may not build on Ada's base (he aims at its far side, away from the doorway).
        Vector3 far = middle - outward * 1.5f;
        Vector3 side = -outward;
        rig.Server.Execute(FormattableString.Invariant($"teleport Bo {far.X + side.X * 2.5f:0.00} {far.Z + side.Z * 2.5f:0.00}"));
        rig.Run(0.6, (_, _) => default);
        Vector3 boEye = Eye(bo);
        Vector3 target = far + ada.Frame.UpAt(far) * 0.6f;
        PlacementCandidate? wall = Ghost(bo, "wall", boEye, Vector3.Normalize(target - boEye));
        Assert.True(wall is { Valid: false } && wall.Outcome.Code == OutcomeCode.BuildingBlocked, $"Bo's ghost is blocked ({wall})");
        Assert.Equal(OutcomeCode.BuildingBlocked, BoAsks(PlacePiece(bo, wall!), "Bo builds anyway"), "the server agrees");
    }

    /// <summary>
    /// GROUND.md §4.4: a 1,074-piece base (11 × 10 foundations with walls four storeys high) streams in to a player who
    /// joins beside it: a few hundred bytes a snapshot, so it arrives over a couple of seconds and no tick stalls; the
    /// copy links into one structure whose stability matches the server's.
    /// </summary>
    [Test]
    public static void ABigBaseStreamsIn()
    {
        using var rig = new LoopbackRig();
        int pieces = rig.Server.Inspect(sv =>
        {
            Zone z = sv.Zone;
            BuildingWorld b = z.Building().World!;
            b.BeginBatch();
            var grid = new Piece[11, 10];
            for (int i = 0; i < 11; i++)
            {
                for (int j = 0; j < 10; j++)
                {
                    grid[i, j] = i == 0 && j == 0 ? Foundation(z, z.Settle(new Vector3(20, 0, -15)), 1)
                        : i > 0 ? On(z, grid[i - 1, j], Facing(grid[i - 1, j], 1, 0), "foundation", 1)
                        : On(z, grid[i, j - 1], Facing(grid[i, j - 1], 0, 1), "foundation", 1);
                }
            }
            var walls = new List<Piece>();
            var taken = new HashSet<(long, long)>();
            foreach (Piece f in grid)
            {
                for (int e = 0; e < 4; e++)
                {
                    PlacementPlan plan = b.FromIntent(Piece(z, "wall"), f, new SocketId(SocketKind.Edge, e), 0)!;
                    if (taken.Add(((long)Math.Round(plan.Pose.Position.X * 100), (long)Math.Round(plan.Pose.Position.Z * 100))))
                    {
                        walls.Add(b.Add(plan.Structure!, plan.Piece, 1, plan.Pose, 1, z.Building().BuildingNow));
                    }
                }
            }
            foreach (Piece w in walls.ToList())
            {
                Piece below = w;
                for (int storey = 1; storey < 4; storey++)
                {
                    below = On(z, below, new SocketId(SocketKind.Top, 0), "wall", 1, grade: storey % 2 + 1);
                }
            }
            b.EndBatch(z.Building().BuildingNow);
            return b.PieceCount;
        });
        Assert.Equal(1074, pieces, "the base's pieces");
        rig.Run(0.2, (_, _) => default);
        rig.Server.Inspect(sv =>
        {
            sv.Ticks.ResetTotals();
            return 0;
        });

        GameClient cy = rig.Join("Cy");
        var watch = Stopwatch.StartNew();
        double first = -1, last = -1;
        int batches = 0;
        long previous = 0;
        while (watch.Elapsed.TotalSeconds < 15)
        {
            rig.Run(0.05, (_, _) => default);
            long now = cy.BuildingReplica?.PiecesReceived ?? 0;
            if (now > previous)
            {
                batches++;
                first = first < 0 ? watch.Elapsed.TotalSeconds : first;
                previous = now;
            }
            if (now >= pieces)
            {
                last = watch.Elapsed.TotalSeconds;
                break;
            }
        }
        Assert.True(last > 0, $"every piece arrived ({previous} of {pieces})");
        var ticks = rig.Server.Inspect(sv => (sv.Ticks.TotalMaxMs, sv.Ticks.TotalPercentileMs(99)));
        Fixtures.Report($"{pieces} pieces streamed in {last - first:0.00} s ({batches} frames with new pieces); " +
            $"server tick worst {ticks.TotalMaxMs:0.0} ms, 99th percentile {ticks.Item2:0.0} ms");
        Assert.True(last - first > 0.5 && batches > 5, "streamed over several snapshots, not all at once");
        BuildingWorld copy = cy.Building!;
        Assert.Equal(1, copy.Structures.Count, "one structure, as on the server");
        int[] mine = copy.AllPieces.OrderBy(p => p.Id.Value).Select(p => p.Stability).ToArray();
        int[] theirs = rig.Server.Inspect(sv => sv.Zone.Building().World!.AllPieces.OrderBy(p => p.Id.Value).Select(p => p.Stability).ToArray());
        Assert.True(mine.SequenceEqual(theirs), "stability as the server has it");
    }
}
