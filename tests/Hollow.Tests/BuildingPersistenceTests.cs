using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>The building in storage (GROUND.md §4.7, §6.3, phase 2): a base survives a restart, and decay catches up on load.</summary>
public static class BuildingPersistenceTests
{
    private const ulong Ada = 0x100, Bo = 0x200;

    private static string Fingerprint(Piece p) =>
        FormattableString.Invariant($"{p.Def.Key} g{p.Grade} {p.Pose.Position.X:R},{p.Pose.Position.Y:R},{p.Pose.Position.Z:R} y{p.Pose.Yaw} ") +
        FormattableString.Invariant($"h{p.Health:0.000} s{p.Stability} o{p.Owner:X}");

    /// <summary>
    /// G3's "done when": a base survives a server restart. Pieces come back at their exact poses in one structure with
    /// their grades, health and stability; the door open, its code lock locked with the same code and the same players
    /// let in; the key lock locked, with its number; the claim device with its list and the store that pays its upkeep.
    /// </summary>
    [Test]
    public static void ABaseSurvivesARestart()
    {
        using var file = new TempDatabase();
        GameContent content = ContentFixture.Content;
        Dictionary<EntityId, string> before;
        EntityId codeDoor, keyDoor, claimId;
        int keyCode;
        using (var rig = new LoopbackRig(file.Path))
        {
            (before, codeDoor, keyDoor, claimId, keyCode) = rig.Server.Inspect(sv =>
            {
                Zone z = sv.Zone;
                BuildingWorld b = z.Building!;
                double now = z.BuildingNow;
                Piece f = BuildingNetTests.Foundation(z, z.Settle(new Vector3(30, 0, 30)), Ada, grade: 2);
                Piece f2 = BuildingNetTests.On(z, f, BuildingNetTests.Facing(f, 1, 0), "foundation", Ada, grade: 2);
                Piece doorway = BuildingNetTests.On(z, f, BuildingNetTests.Facing(f, -1, 0), "doorway", Ada, grade: 2);
                Piece second = BuildingNetTests.On(z, f2, BuildingNetTests.Facing(f2, 1, 0), "doorway", Ada, grade: 1);
                Piece wall = BuildingNetTests.On(z, f, BuildingNetTests.Facing(f, 0, 1), "wall", Ada, grade: 1);
                BuildingNetTests.On(z, wall, new SocketId(SocketKind.Top, 0), "floor", Ada, orientation: 0, grade: 0);
                b.Damage(wall, new DamageHit(Vector3.UnitX, Melee: 60), now);
                Vector3 eye = BuildingWorld.F(f.Structure.Frame.ToZone(new Vector3D(0, 1.6, 0)));
                Assert.True(b.PlaceDoor(Ada, b.Data.Door("sheet-metal-door")!, doorway, 1, eye, out Door? door).Ok, "the metal door");
                Assert.True(b.AttachCodeLock(Ada, door!, out _).Ok && b.SetCode(Ada, door!, "2468").Ok, "its code lock");
                Assert.True(b.EnterCode(Bo, door!, "2468", now).Ok, "Bo let in");
                Assert.True(b.Toggle(Bo, door!).Ok, "and the door open");
                Assert.True(b.PlaceDoor(Ada, b.Data.Door("wooden-door")!, second, 0, BuildingWorld.F(f2.Structure.Frame.ToZone(new Vector3D(3, 1.6, 0))),
                    out Door? other).Ok, "the wooden door");
                Assert.True(b.AttachKeyLock(Ada, other!, out KeyLock? key).Ok, "its key lock");
                Assert.True(b.SetLocked(Ada, other!, true).Ok, "locked");
                Assert.True(b.PlaceClaim(Ada, f, new Vector3D(0.6, 0, 0.6), 2, out ClaimDevice? claim).Ok, "the claim device");
                b.Authorise(Bo, claim!);
                Container store = z.DeployableOf(claim!)!.Storage!;
                store.Set(0, ItemStack.Of(content.ItemByKey("wood"), 900));
                store.Set(3, ItemStack.Of(content.ItemByKey("stone"), 450));
                return (b.AllPieces.ToDictionary(p => p.Id, Fingerprint), door!.Id, other!.Id, claim!.Id, key!.KeyCode);
            });
            rig.Run(0.3, (_, _) => default);
            rig.Server.SaveAll().Wait();
            Fixtures.Report($"saved {before.Count} pieces, 2 doors and a claim device");
        }

        using (var rig = new LoopbackRig(file.Path))
        {
            string report = rig.Server.Inspect(sv =>
            {
                Zone z = sv.Zone;
                BuildingWorld b = z.Building!;
                Dictionary<EntityId, string> after = b.AllPieces.ToDictionary(p => p.Id, Fingerprint);
                Assert.Equal(before.Count, after.Count, "every piece is back");
                foreach ((EntityId id, string was) in before)
                {
                    Assert.True(after.TryGetValue(id, out string? now), $"{was} is back");
                    Assert.Equal(was, now, "exactly as it was");
                }
                Assert.Equal(1, b.Structures.Count, "in one structure");
                Door door = b.DoorOf(codeDoor)!;
                Assert.True(door is { Open: true, Lock: CodeLock { Locked: true } }, "the metal door open, its lock locked");
                var code = (CodeLock)door.Lock!;
                Assert.True(code.IsAuthorised(Ada) && code.IsAuthorised(Bo) && code.Authorised.Count == 2, "Ada and Bo let in");
                Assert.Equal(OutcomeCode.WrongCode, b.EnterCode(0x300, door, "1357", z.BuildingNow).Code, "a wrong code is wrong");
                Assert.True(b.EnterCode(0x400, door, "2468", z.BuildingNow).Ok, "the code is the same");
                Door other = b.DoorOf(keyDoor)!;
                Assert.True(other.Lock is KeyLock { Locked: true } k && k.KeyCode == keyCode, "the key lock locked, with its number");
                ClaimDevice claim = b.ClaimOf(claimId)!;
                Assert.True(claim.IsAuthorised(Ada) && claim.IsAuthorised(Bo), "the claim device's list");
                Container store = z.DeployableOf(claim)!.Storage!;
                int wood = InventoryRules.Count(new[] { store }, ContentFixture.Item("wood").Id);
                int stone = InventoryRules.Count(new[] { store }, ContentFixture.Item("stone").Id);
                Assert.True(wood is > 880 and <= 900 && stone is > 440 and <= 450, $"its store ({wood} wood, {stone} stone)");
                Assert.True(z.Statics().OfType<Deployable>().Count(d => z.ClaimOf(d) != null) == 1, "kept by the zone as a deployable, once");
                return $"{after.Count} pieces, stability {string.Join("/", b.AllPieces.Select(p => p.Stability).Distinct().OrderDescending())}";
            });
            Fixtures.Report($"after the restart: {report}");
        }
    }

    /// <summary>
    /// Decay over the time the server was down is applied when the zone loads, in one closed-form step: an unclaimed
    /// wood foundation and wall lose a third of their health over an hour (wood decays in three), and a twig
    /// foundation (an hour) is gone.
    /// </summary>
    [Test]
    public static void DecayCatchesUpOnLoad()
    {
        using var file = new TempDatabase();
        EntityId twig, wood, wall;
        ulong system;
        uint body;
        using (var rig = new LoopbackRig(file.Path))
        {
            (twig, wood, wall, system, body) = rig.Server.Inspect(sv =>
            {
                Zone z = sv.Zone;
                Piece t = BuildingNetTests.Foundation(z, z.Settle(new Vector3(-30, 0, 20)), Ada, grade: 0);
                Piece f = BuildingNetTests.Foundation(z, z.Settle(new Vector3(-30, 0, 30)), Ada, grade: 1);
                Piece w = BuildingNetTests.On(z, f, BuildingNetTests.Facing(f, 1, 0), "wall", Ada, grade: 1);
                return (t.Id, f.Id, w.Id, sv.Spec.SystemAddress, sv.Spec.BodyPath);
            });
            rig.Run(0.2, (_, _) => default);
        }
        // The server was last up an hour ago.
        using (GameDatabase db = GameDatabase.Open(file.Path))
        {
            double hourAgo = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - 3600;
            db.SaveZone(new ZoneSave { SystemAddress = system, BodyPath = body, SavedAtUnixMilliseconds = 0, Meta = BuildingCodec.EncodeZone(hourAgo) });
        }
        using (var rig = new LoopbackRig(file.Path))
        {
            (bool twigGone, double woodHealth, double wallHealth) = rig.Server.Inspect(sv =>
            {
                BuildingWorld b = sv.Zone.Building!;
                return (b.PieceOf(twig) == null, b.PieceOf(wood)?.Health ?? 0, b.PieceOf(wall)?.Health ?? 0);
            });
            Fixtures.Report($"after an hour away: twig foundation {(twigGone ? "gone" : "still there")}, wood foundation {woodHealth:0.0}/200, wall {wallHealth:0.0}/200");
            Assert.True(twigGone, "the twig foundation decayed away");
            Assert.Near(200 * 2.0 / 3, woodHealth, 1.5, "the wood foundation lost a third");
            Assert.Near(200 * 2.0 / 3, wallHealth, 1.5, "and so did the wall");
        }
    }
}
