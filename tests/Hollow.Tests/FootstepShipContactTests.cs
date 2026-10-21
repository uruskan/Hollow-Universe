using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Client.Audio;
using Hollow.Client.Feedback;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Ships;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>Actual client replicas and collision bodies choose foot contacts across world and ship frames.</summary>
public static class FootstepShipContactTests
{
    [Test]
    public static void ParkedFinchHullResolvesWithoutAnInteriorIncludingSharedPhysicsClients()
    {
        using var rig = new LoopbackRig();
        GameClient owner = rig.Join("FootOwner");
        rig.WaitUntilPlaying();
        rig.Server.Execute("ship FootOwner");
        Wait(rig, () => owner.Ships().All.Any(s => s.Def?.Key == "tin-finch" && s.Newest.Body != 0), "parked Finch snapshot");
        RemoteShip finch = owner.Ships().All.Single(s => s.Def?.Key == "tin-finch");
        Assert.True(finch.Def!.Interior == null && owner.Ships().FrameOf(finch.NetId) == null,
            "fixture has a parked hull but no walkable-interior frame");
        uint body = finch.Newest.Body;
        Assert.True(owner.Ships().ShipForBody(body) == finch, "snapshot indexes parked body independently of interior frames");
        Vector3 feet = HullTop(owner, finch);
        Assert.Equal(FootSurface.Metal, Contact(owner, feet), "real Finch hull selects metal foot contacts");

        // A borrowed physics client receives the same snapshots but must neither create nor own the hull body.
        var borrower = new GameClient(new ClientOptions
        {
            Name = "FootBorrower", Content = owner.Content,
            SharedPhysics = (_, _) => owner.Physics!,
            SharedNature = (_, _) => owner.Survival().Natural!,
            SharedBuilding = (_, _, _) => owner.Building().Replica!,
        });
        try
        {
            borrower.Connect("127.0.0.1", rig.Server.Port);
            rig.Clients.Add(borrower);
            rig.WaitUntilPlaying();
            Wait(rig, () => borrower.Ships().ShipForBody(body) != null, "borrower's parked body identity");
            RemoteShip borrowed = borrower.Ships().ShipForBody(body)!;
            Assert.True(ReferenceEquals(owner.Physics, borrower.Physics), "both clients query the same native physics world");
            Assert.Equal(finch.Id, borrowed.Id, "borrower maps the server's body ID to the same ship");
            Assert.True(borrower.Ships().FrameOf(borrowed.NetId) == null, "borrower also has no interior frame to scan");
            Assert.Equal(FootSurface.Metal, Contact(borrower, feet), "shared-physics client also hears the hull");
        }
        finally
        {
            rig.Clients.Remove(borrower);
            borrower.Dispose();
        }
        Assert.Equal(body, RayAt(owner, feet).Body, "borrower teardown leaves the owning client's hull intact");
        Assert.Equal(FootSurface.Metal, Contact(owner, feet), "owner retains its foot contact after borrower teardown");
    }

    [Test]
    public static void ReplicatedBoxAboardUsesLocalFeetAndDiffersFromTheMetalDeck()
    {
        using var rig = new LoopbackRig();
        GameClient client = rig.Join("FootPassenger");
        rig.WaitUntilPlaying();
        IronWakeNetTests.ShipFacts ship = IronWakeNetTests.Spawn(rig, client.Name);
        IronWakeNetTests.PutAboard(rig, client.EntityId, ship.Id, new Vector3(0, 3.2f, 1.5f));
        rig.Run(0.5, (_, _) => default);

        EntityId boxId = rig.Server.Inspect(server =>
        {
            Zone zone = server.Zone;
            Assert.True(zone.Entities.TryFind(client.EntityId, out Entity player), "passenger exists on the authority");
            Ship aboard = zone.Ships().ShipOf(new EntityId(ship.Id))!;
            ShipPose pose = ShipFrame.PoseOf(aboard.State, aboard.Def.CentreOfMass);
            ItemDef item = zone.Content!.ItemByKey("small-box");
            zone.Players().PlayerOf(player)!.Inventory.Main.Set(0, ItemStack.Of(item, 1));
            Vector3 aimedAt = pose.ToWorld(new Vector3(-2, 3.2f, 3.2f));
            PlacementCandidate? ghost = zone.Placement().Aim(player, item.Places!, aimedAt - zone.EyeOf(zone.Entities.Get<Character>(player).State));
            Assert.True(ghost is { Valid: true, Aboard: not null }, "wooden box fits on the actual deck");
            Assert.Equal(OutcomeCode.Done, zone.Placement().Place(player, ContainerRef.Main, 0, ghost!).Code, "authority places the wooden box");
            return zone.Things().Riders.OfType<Deployable>().Last(d => d.Def == item.Places!.Definition).Id;
        });
        Wait(rig, () => client.Survival().ThingOf(boxId) is { Aboard: true } &&
            client.Ships().ShipOf(new EntityId(ship.Id)) is { } remote && client.Ships().FrameOf(remote.NetId) != null,
            "box and ship frame replicated to the client");
        RemoteThing box = client.Survival().ThingOf(boxId)!;
        RemoteShip remoteShip = client.Ships().ShipOf(new EntityId(ship.Id))!;
        ClientShipFrame frame = client.Ships().FrameOf(remoteShip.NetId)!;
        Vector3 boxFeet = box.LocalPosition + Vector3.UnitY * (2 * box.Deployable(client.Content)!.HalfExtents.Y + 0.02f);
        Assert.True(frame.Space.Ray(boxFeet + Vector3.UnitY * 0.25f, -Vector3.UnitY, 0.8f, out SpaceHit boxHit, things: true),
            "replicated placed box participates in the ship-space collision query");
        Assert.True(frame.Space.IsThing((uint)boxHit.Part) && frame.Space.ThingOf((uint)boxHit.Part) == boxId.Value,
            "foot ray contacts the placed box rather than the deck underneath");

        Vector3 deckFeet = new(0, 3.22f, 1.5f);
        Assert.True(frame.Space.Ray(deckFeet + Vector3.UnitY * 0.25f, -Vector3.UnitY, 0.8f, out SpaceHit deckHit, things: true) &&
            !frame.Space.IsThing((uint)deckHit.Part), "nearby deck contact belongs to the ship itself");
        foreach (Vector3 worldOffset in new[] { Vector3.Zero, new Vector3(1800, -4300, 2750) })
        {
            // Rendering/interpolation can disagree with prediction's world pose; frame feet remain authoritative.
            Assert.Equal(FootSurface.Wood, Contact(client, remoteShip.PointOf(boxFeet) + worldOffset, remoteShip.NetId, boxFeet),
                "local feet choose the wooden box despite world-pose disagreement");
            Assert.Equal(FootSurface.Metal, Contact(client, remoteShip.PointOf(deckFeet) + worldOffset, remoteShip.NetId, deckFeet),
                "local feet beside the box choose the metal deck");
        }

        // Destruction travels through the same replicated lifetime path and reveals the deck again.
        rig.Server.Inspect(server =>
        {
            server.Zone.Things().Break((Deployable)server.Zone.Things().StaticOf(boxId)!);
            return true;
        });
        Wait(rig, () => client.Survival().ThingOf(boxId) == null, "destroyed box retired from the replica");
        Vector3 clearedFeet = box.LocalPosition + Vector3.UnitY * 0.02f;
        Assert.Equal(FootSurface.Metal, Contact(client, remoteShip.PointOf(clearedFeet), remoteShip.NetId, clearedFeet),
            "destroyed box no longer supplies wood contacts over the deck");
    }

    private static FootSurface Contact(GameClient client, Vector3 feet, ulong movementFrame = 0, Vector3? frameFeet = null) =>
        FootstepEnvironment.At(client.Content, Responses, client.Surface!, client.Frame, client.Building().World, feet,
            movementFrame, client.Ships(), client.Survival(), frameFeet);

    private static readonly ContactResponseData Responses = ContactResponseData.Load(
        Path.Combine(ContentFixture.Folder, ContactResponseData.FileName));

    private static Vector3 HullTop(GameClient client, RemoteShip ship)
    {
        foreach (HullBox box in ship.Def!.Hull)
        {
            Vector3 at = ship.PointOf(box.Centre);
            Vector3 up = client.Frame.UpAt(at);
            if (client.Building().World!.Physics!.Ray(at + up * 20, -up, 40, out RayHit hit) && hit.Body == ship.Newest.Body)
                return hit.Point + up * 0.02f;
        }
        throw new TestFailure("the client's actual parked hull must be hit from above");
    }

    private static RayHit RayAt(GameClient client, Vector3 feet)
    {
        Vector3 up = client.Frame.UpAt(feet);
        Assert.True(client.Building().World!.Physics!.Ray(feet + up * 0.25f, -up, 0.8f, out RayHit hit), "foot contact remains in physics");
        return hit;
    }

    private static void Wait(LoopbackRig rig, Func<bool> ready, string message)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed.TotalSeconds < 10) rig.Run(0.1, (_, _) => default);
        Assert.True(ready(), message);
    }
}
