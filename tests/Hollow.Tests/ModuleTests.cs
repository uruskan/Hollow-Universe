using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Ships.Modules;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>
/// VEHICLES.md §9.1, V1: the module core. A kind's sockets take modules (items, read from modules.json as a ship's own
/// layout is), a build is checked against the rules of §3.3, and a kind with a build composes into one layout: the kind's
/// seats, doors, parts and hull, then each module's where its socket puts it, the mass, centre of mass and inertia of the
/// whole, the fuel added, the holds. The same build makes the same layout once. In a zone a module's hold opens at its part
/// and goes into the wreck, a shot on its boxes is its body's, and the hammer mends hulls and modules alike (§5.5).
/// </summary>
public static class ModuleTests
{
    // The test's two modules, put at the head of the game's own list (whatever it holds).
    private const string Modules = """
            { "key": "test-locker", "item": "test-locker", "fits": ["rack"], "levels": [1, 2], "height": 0.8, "carries": true,
              "mass": 120, "centreOfMass": [0, 0.4, 0],
              "hull": [{ "centre": [0, 0.4, 0], "size": [0.5, 0.4, 0.4] }],
              "parts": [
                { "key": "body", "name": "Locker", "at": [0, 0.4, 0], "radius": 0.5, "capabilities": ["container"],
                  "health": { "max": 400, "protection": "metal", "table": "pieces", "repair": ["metal-fragments", 40] } }
              ],
              "hold": 12 },
            { "key": "test-bench", "item": "test-bench", "fits": ["rack"], "span": 2, "levels": [1], "height": 1.2,
              "mass": 60, "centreOfMass": [0, 0.6, -0.5], "fuel": 40,
              "hull": [{ "centre": [0, 0.3, -0.5], "size": [0.4, 0.3, 0.9] }],
              "seats": [
                { "key": "rider", "role": "helm", "verb": "sit", "sitters": "anyone", "exposed": true,
                  "at": [0, 0.6, -0.5], "eye": [0, 1.4, -0.5], "exits": [[1.2, 0, -0.5]] }
              ],
              "parts": [
                { "key": "body", "name": "Bench", "at": [0, 0.6, -0.5], "radius": 0.5, "capabilities": ["seat"], "seat": "rider",
                  "health": { "max": 200, "protection": "wood", "table": "pieces", "repair": ["wood", 20] } }
              ] },
        """;

    private const string Sockets = """
          "sockets": [
            { "key": "rack-a", "at": [0, 1.0, -2.6], "fits": ["rack"], "row": "rack", "levels": 2 },
            { "key": "rack-b", "at": [0, 1.0, -3.6], "fits": ["rack"], "row": "rack", "levels": 2 },
            { "key": "nose", "at": [0, 1.0, 2.4], "fits": ["nose"] }
          ],
          "buildHeight": 3.0,
        """;

    private static readonly Lazy<GameContent> Loaded = new(() =>
    {
        Dictionary<string, string> texts = GameContent.ReadTexts(ContentFixture.Folder);
        texts["modules.json"] = ReplaceOnce(texts["modules.json"], "\"modules\": [", "\"modules\": [\n" + Modules);
        texts["ships.json"] = ReplaceOnce(texts["ships.json"], "\"item\": \"tin-finch\",", "\"item\": \"tin-finch\",\n" + Sockets);
        texts["items.json"] = ReplaceOnce(texts["items.json"], "\"items\": [",
            "\"items\": [\n    { \"id\": 9001, \"key\": \"test-locker\", \"name\": \"Test locker\", \"category\": \"vehicles\", \"module\": \"test-locker\" },\n" +
            "    { \"id\": 9002, \"key\": \"test-bench\", \"name\": \"Test bench\", \"category\": \"vehicles\", \"module\": \"test-bench\" },");
        GameContent content = GameContent.FromTexts(texts);
        Assert.Equal("", string.Join("\n", content.Problems), "the test modules read without a problem");
        return content;
    });

    private static GameContent C => Loaded.Value;

    private static ShipDef Finch => C.Ship("tin-finch")!;

    private static ushort Locker => C.ItemByKey("test-locker").Id;

    private static ushort Bench => C.ItemByKey("test-bench").Id;

    private static string ReplaceOnce(string text, string what, string with)
    {
        int at = text.IndexOf(what, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the data has {what}");
        return text[..at] + with + text[(at + what.Length)..];
    }

    private static BuildProblem Check(params Installed[] entries) => ShipAssembler.Check(Finch, new ShipBuild(entries), C.Module);

    [Test]
    public static void TheGameDataTakesModules()
    {
        GameContent real = ContentFixture.Content;
        Assert.True(real.Ships.All(s => s.Kind == s && s.Build.IsEmpty), "a kind as read is its own kind, with nothing installed");
        Assert.True(real.Ship("tin-finch")!.HullRepair is { Full: 150 } && real.Ship("iron-wake")!.HullRepair is { Full: 3600 },
            "both hulls are mended with the hammer (ships.json repair)");
        Assert.Equal(ShipBuild.Empty, new ShipBuild(Array.Empty<Installed>()), "two empty builds are equal");
        Assert.True(C.Module(Locker)?.Key == "test-locker" && C.Module(1) == null, "modules hang off their items; other items aren't");
        ModuleDef bench = C.Module(Bench)!;
        Assert.True(bench.Span == 2 && bench.Lower && !bench.Upper && bench.Seats.Count == 1 && bench.Parts[0].Health!.Protection.Key == "wood",
            "a module's layout is read as a ship's, its protection resolved");
        Assert.True(C.Module(Locker)!.HoldPart == 0 && C.Module(Locker)!.Hold == 12, "the hold opens at the part with the container capability");
    }

    [Test]
    public static void ABuildKeepsTheRulesOfItsSockets()
    {
        Assert.Equal(BuildProblem.None, Check(new Installed(0, 1, Locker), new Installed(0, 2, Locker)), "a locker on a locker");
        Assert.Equal(BuildProblem.None, Check(new Installed(0, 1, Bench)), "a bench across both rack sockets");
        Assert.Equal(BuildProblem.NoSocket, Check(new Installed(9, 1, Locker)), "a socket the kind hasn't");
        Assert.Equal(BuildProblem.UnknownModule, Check(new Installed(0, 1, C.ItemByKey("wood").Id)), "wood is no module");
        Assert.Equal(BuildProblem.DoesNotFit, Check(new Installed(2, 1, Locker)), "the nose takes no locker");
        Assert.Equal(BuildProblem.NoLevel, Check(new Installed(0, 1, Locker), new Installed(0, 2, Bench)), "a bench stands on level 1 only");
        Assert.Equal(BuildProblem.NoLevel, Check(new Installed(0, 3, Locker)), "there is no level 3");
        Assert.Equal(BuildProblem.NoRoom, Check(new Installed(1, 1, Bench)), "a bench two long runs off the row's end");
        Assert.Equal(BuildProblem.Taken, Check(new Installed(0, 1, Bench), new Installed(1, 1, Locker)), "the bench's second socket is taken");
        Assert.Equal(BuildProblem.NoRoof, Check(new Installed(0, 2, Locker)), "nothing under a level-2 locker");
        Assert.Equal(BuildProblem.NoRoof, Check(new Installed(0, 1, Bench), new Installed(1, 2, Locker)), "a bench's roof carries nothing");
        Assert.Equal(BuildProblem.None, Check(new Installed(0, 1, Locker), new Installed(1, 1, Locker), new Installed(1, 2, Locker)),
            "level 2 over its own socket's locker");
        // The build height: 1.0 to the rack, 0.8 a locker: two are 2.6, under the kind's 3.0; a third level doesn't exist.
        ShipBuild sane = ShipAssembler.Sane(Finch, new[]
        {
            new Installed(0, 2, Locker), new Installed(0, 1, Locker), new Installed(1, 1, Bench), new Installed(7, 1, Locker),
            new Installed(1, 1, C.ItemByKey("wood").Id),
        }, C.Module);
        Assert.Equal("0.1:9001 0.2:9001", sane.ToString(), "untrusted entries made sane: the impossible ones left out, level 2 kept over its roof");
    }

    [Test]
    public static void AKindWithABuildComposesIntoOneLayout()
    {
        ShipDef kind = Finch;
        var build = new ShipBuild(new[] { new Installed(0, 1, Bench), new Installed(0, 2, Locker) });
        Assert.Equal(BuildProblem.NoRoof, ShipAssembler.Check(kind, build, C.Module), "nothing stands on a bench");
        build = new ShipBuild(new[] { new Installed(1, 2, Locker), new Installed(0, 1, Locker), new Installed(1, 1, Locker) });
        ShipDef d = C.Compose(kind, build);
        Assert.True(ReferenceEquals(d, C.Compose(kind, new ShipBuild(build.Entries.Reverse()))), "made once: the same build in any order is the same layout");
        Assert.True(ReferenceEquals(kind, C.Compose(kind, ShipBuild.Empty)) && d.Kind == kind && d.Build.Equals(build), "its kind and build");
        Assert.True(d.Id == kind.Id && d.Key == kind.Key && d.Drive == kind.Drive && d.Controls == kind.Controls && d.HullRepair == kind.HullRepair,
            "everything else is the kind's");
        Assert.Equal(kind.Parts.Count + 3, d.Parts.Count, "a body part each");
        Assert.Equal(kind.Hull.Count + 3, d.Hull.Count, "a box each");
        Assert.Equal(3, d.Holds.Count, "a hold each");
        Assert.Equal(kind.Seats.Count, d.Seats.Count, "no seats added");
        // Origins: level 1 at the socket; level 2 on the roof of the locker under it.
        PlacedModule top = d.Placed.Single(p => p.Entry.Level == 2);
        Assert.True(Vector3.Distance(top.Origin, new Vector3(0, 1.8f, -3.6f)) < 1e-5f, $"level 2 stands on the locker's roof ({top.Origin})");
        ShipTargetPart topBody = d.Parts[top.FirstPart];
        Assert.True(topBody.Key == "rack-b.2.body" && topBody.HasBall && Vector3.Distance(topBody.At.Position, new Vector3(0, 2.2f, -3.6f)) < 1e-5f,
            "its part keyed by where it stands, its ball moved with it");
        Assert.Equal(top.FirstPart, d.Holds[d.HoldOfPart(top.FirstPart)].Part, "its hold opens at its body");
        Assert.True(d.HullPart(top.FirstBox) == top.FirstPart && d.HullPart(0) == ShipParts.Hull, "its box is its body's, the kind's the hull's");
        // Mass and balance: the kind's plus three lockers at their centres.
        float mass = kind.Mass + 3 * 120;
        Vector3 moment = kind.Mass * kind.CentreOfMass + 120 * (new Vector3(0, 1.4f, -2.6f) + new Vector3(0, 1.4f, -3.6f) + new Vector3(0, 2.2f, -3.6f));
        Assert.Near(mass, d.Mass, 1e-3, "the mass adds");
        Assert.True(Vector3.Distance(moment / mass, d.CentreOfMass) < 1e-4f, "the centre of mass moves back and up");
        Assert.True(d.Inertia.X > kind.Inertia.X && d.Inertia.Y > kind.Inertia.Y, "lockers far aft turn it harder");
        Assert.True(d.BoundsHalf.Z > kind.BoundsHalf.Z * 0.99f && d.BoundingRadius > kind.BoundingRadius, "the bounds hold the lockers");
        // A helm seat on a kind with its own helm rides as a passenger; seats and parts point at each other's new numbers.
        ShipDef benched = C.Compose(kind, new ShipBuild(new[] { new Installed(0, 1, Bench) }));
        ShipSeatDef rider = benched.Seats[^1];
        ShipTargetPart seatPart = benched.Parts[benched.PartOfSeat(rider.Index)];
        Assert.True(rider.Role == SeatRole.Passenger && rider.Key == "rack-a.1.rider" && benched.Helm == kind.Helm, "the kind's helm keeps flying");
        Assert.True(seatPart.Seat == rider.Index && seatPart.Index == benched.Parts.Count - 1 && rider.Index == kind.Seats.Count, "the part names the moved seat");
        Assert.Near(kind.FuelCapacity + 40, benched.FuelCapacity, 1e-3, "the bench's tank adds to the ship's");
        Assert.True(rider.Exits[0].Position == new Vector3(1.2f, 1.0f, -3.1f), "its way out moves with it");
    }

    [Test]
    public static void AModuleHoldsShieldsAndIsMendedInAZone()
    {
        using var rig = new ShipRig();
        Zone z = new(new LevelSurface(ShipRig.Earthlike), rig.Zone.Spec, content: C) { UnixMilliseconds = 1_800_000_000_000 };
        try
        {
            ShipDef d = C.Compose(Finch, new ShipBuild(new[] { new Installed(0, 1, Locker), new Installed(1, 1, Locker) }));
            Vector3 ground = z.GroundAt(0, 0);
            Entity ada = z.Players().AddPlayer(z.NextId(), "Ada", z.NewCharacter(ground + z.Frame.UpAt(ground) * 0.1f));
            Player pa = z.Players().PlayerOf(ada)!;
            Ship ship = z.Ships().SpawnShip(d, ground, 0, pa.Id, 100);
            Assert.True(ship.Holds.Length == 2 && ship.Holds[0].Size == 12, "a hold for each locker");
            PlacedModule first = d.Placed[0];
            var lockerRef = new TargetRef(TargetFamily.Ship, ship.Id.Value, (byte)first.FirstPart);
            // Ada beside the first locker, looking at it.
            Vector3 lockerAt = ship.PointOf(d.Parts[first.FirstPart].At.Position);
            Vector3 up = z.Frame.UpAt(lockerAt);
            Vector3 side = Vector3.Normalize(Vector3.Cross(up, ship.PointOf(Vector3.UnitZ) - ship.Origin));
            z.Teleport(ada, z.Settle(lockerAt + side * 1.4f));
            Step(z, 5);
            Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, lockerRef, "open").Code, "E opens the locker");
            Assert.True(ReferenceEquals(z.Ships().LootContainer(pa, ship), ship.Holds[0]), "its own hold, not the cargo");
            ship.Holds[1].Set(0, ItemStack.Of(C.ItemByKey("wood"), 300));
            // A shot on the second locker's box is its body's.
            PlacedModule second = d.Placed[1];
            Vector3 box = ship.PointOf(d.Hull[second.FirstBox].Centre);
            Vector3 from = box + side * 10;
            float t = z.Ships().TraceShips(from, Vector3.Normalize(box - from), 20, 0, out Ship? hit, out _, out byte part);
            Assert.True(t > 0 && hit == ship && part == second.FirstPart, $"the ray meets the locker's body (part {part})");
            // The hammer mends the hull (the kind's repair) and a module (its own), each with its own material.
            pa.Inventory.Belt.Set(0, ItemStack.Of(C.ItemByKey("hammer"), 1));
            pa.Inventory.Belt.Set(1, ItemStack.Of(C.ItemByKey("metal-fragments"), 500));
            ship.State.Health = 450;
            ship.PartHealth[first.FirstPart] = 200;
            Assert.Equal(OutcomeCode.Done, z.Building().Repair(ada, ship.Ref).Code, "a hit on the hull");
            Assert.Near(550, ship.State.Health, 1e-3, "100 a hit");
            Assert.Equal(480, pa.Inventory.Belt[1].Count, "for 20 metal fragments: a fifth of 150 × 100/750, rounded up");
            Assert.Equal(OutcomeCode.Done, z.Building().Repair(ada, lockerRef).Code, "a hit on the locker");
            Assert.True(ship.PartHealth[first.FirstPart] == 300 && pa.Inventory.Belt[1].Count == 470, "100 for 10 of its 40 a full mend");
            // Its holds go into the wreck.
            z.Ships().DamageShip(ship, 1e7f, ShipDamage.Explosive, null);
            Assert.True(ship.Destroyed, "destroyed");
            Corpse? wreck = z.Things().Statics().OfType<Corpse>().FirstOrDefault(k => k.Of == ship.Id);
            Assert.True(wreck != null && Enumerable.Range(0, wreck.Loot.Size).Any(i => wreck.Loot[i].Item == C.ItemByKey("wood").Id && wreck.Loot[i].Count == 300),
                "the locker's wood is in the wreck");
        }
        finally
        {
            z.Dispose();
        }
    }

    [Test]
    public static void BadModuleDataIsTold()
    {
        Dictionary<string, string> texts = GameContent.ReadTexts(ContentFixture.Folder);
        texts["modules.json"] = ReplaceOnce(texts["modules.json"], "\"modules\": [", "\"modules\": [\n" + Modules.Replace("\"hold\": 12", "\"hold\": 0", StringComparison.Ordinal)
            .Replace("\"exposed\": true", "\"exposed\": false", StringComparison.Ordinal)
            .Replace("\"levels\": [1, 2]", "\"levels\": [3]", StringComparison.Ordinal));
        texts["items.json"] = ReplaceOnce(texts["items.json"], "\"items\": [",
            "\"items\": [\n    { \"id\": 9001, \"key\": \"test-locker\", \"name\": \"Test locker\", \"category\": \"vehicles\", \"module\": \"test-locker\" },\n" +
            "    { \"id\": 9002, \"key\": \"test-bench\", \"name\": \"Test bench\", \"category\": \"vehicles\", \"module\": \"test-bench\" },");
        IReadOnlyList<string> problems = GameContent.FromTexts(texts).Problems;
        foreach (string p in problems)
        {
            Fixtures.Report(p);
        }
        Assert.True(problems.Any(p => p.Contains("test-locker", StringComparison.Ordinal) && p.Contains("levels", StringComparison.Ordinal)), "a level 3");
        Assert.True(problems.Any(p => p.Contains("test-locker", StringComparison.Ordinal) && p.Contains("opens a hold", StringComparison.Ordinal)),
            "a container part with no hold");
        Assert.True(problems.Any(p => p.Contains("rider", StringComparison.Ordinal) && p.Contains("exposed", StringComparison.Ordinal)),
            "a sheltered seat with no door");
        Assert.True(problems.Any(p => p.Contains("no ship's socket fits", StringComparison.Ordinal)), "no kind has a rack yet");
    }

    /// <summary>
    /// ShipCodec 8: the build is stored after the kind and comes back as the same layout, each hold by its module's socket and
    /// level, the modules' health with it. A game that lost a module brings the ship back without it.
    /// </summary>
    [Test]
    public static void AShipWithModulesIsSavedAndReadBack()
    {
        using var rig = new ShipRig();
        Zone z = new(new LevelSurface(ShipRig.Earthlike), rig.Zone.Spec, content: C) { UnixMilliseconds = 1_800_000_000_000 };
        try
        {
            var build = new ShipBuild(new[] { new Installed(0, 1, Locker), new Installed(1, 1, Locker), new Installed(1, 2, Locker) });
            ShipDef d = C.Compose(Finch, build);
            Ship ship = z.Ships().SpawnShip(d, z.GroundAt(0, 0), 0, new EntityId(77), 100);
            int top = d.HoldOfPart(d.Placed.Single(p => p.Entry.Level == 2).FirstPart);
            ship.Holds[top].Set(3, ItemStack.Of(C.ItemByKey("wood"), 250));
            ship.PartHealth[d.Placed[0].FirstPart] = 123;
            byte[] bytes = Hollow.Persistence.ShipCodec.Encode(ship, z.Frame, z.Tick);
            Assert.Equal((byte)8, bytes[0], "version 8");
            Hollow.Persistence.StoredShip back = Hollow.Persistence.ShipCodec.Decode(ship.Id, bytes, C, z.Frame)!;
            Ship again = back.Ship;
            Assert.True(ReferenceEquals(again.Def, d), "the same composed layout");
            Assert.True(again.Holds[top][3].Count == 250 && again.Holds.Where((h, i) => i != top).All(h => h.IsEmpty), "the wood in the top locker's hold");
            Assert.Near(123, again.PartHealth[d.Placed[0].FirstPart], 1e-3, "the first locker's health");
            // A game without the test modules (the real data): the build goes, the ship stays.
            Hollow.Persistence.StoredShip plain = Hollow.Persistence.ShipCodec.Decode(ship.Id, bytes, ContentFixture.Content, z.Frame)!;
            Assert.True(plain.Ship.Def.Build.IsEmpty && plain.Ship.Holds.Length == 0 && plain.Ship.Def.Key == "tin-finch",
                "a module the game lost is left out");
        }
        finally
        {
            z.Dispose();
        }
    }

    /// <summary>Protocol 47: a ship's details carry its build through the wire, and a client composes the server's layout from it.</summary>
    [Test]
    public static void TheBuildTravelsInTheDetails()
    {
        using var rig = new ShipRig();
        Zone z = new(new LevelSurface(ShipRig.Earthlike), rig.Zone.Spec, content: C) { UnixMilliseconds = 1_800_000_000_000 };
        try
        {
            ShipDef d = C.Compose(Finch, new ShipBuild(new[] { new Installed(0, 1, Bench) }));
            Ship ship = z.Ships().SpawnShip(d, z.GroundAt(0, 0), 0, new EntityId(77), 100);
            var writer = new Hollow.Net.Bits.BitWriter();
            Hollow.Net.Protocol.ShipConvert.Details(ship, 5, id => id.Value).WriteMessage(writer);
            byte[] bytes = writer.ToArray();
            var reader = new Hollow.Net.Bits.BitReader();
            reader.Reset(bytes, 0, bytes.Length);
            Assert.Equal((uint)Hollow.Net.Protocol.ShipDetails.TypeId, reader.ReadBits(8), "a ship's details");
            Hollow.Net.Protocol.ShipDetails m = Hollow.Net.Protocol.ShipDetails.Read(reader);
            Installed[] entries = Hollow.Net.Protocol.ShipConvert.BuildOf(m);
            ShipDef client = C.Compose(Finch, ShipAssembler.Sane(Finch, entries, C.Module));
            Assert.True(!reader.Failed && ReferenceEquals(client, d), "the client's layout is the server's");
        }
        finally
        {
            z.Dispose();
        }
    }

    private static void Step(Zone z, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            z.UnixMilliseconds += 33;
            z.Step();
        }
    }
}
