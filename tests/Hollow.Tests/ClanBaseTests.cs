using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;

namespace Hollow.Tests;

public static class ClanBaseTests
{
    private static string Json => File.ReadAllText(Path.Combine(ContentFixture.Folder, "admin-bases", "clan-3x3.json"));
    private static BuildingBlueprint Blueprint(BuildingData? data = null) => BuildingBlueprint.Parse(data ?? ContentFixture.Content.Building, Json);

    [Test]
    public static void ClanBaseHasSupportedFloorsProtectedPeeksAndDoorsAtEveryGrade()
    {
        BuildingData data = ContentFixture.Content.Building;
        BuildingBlueprint blueprint = Blueprint();
        for (int grade = 0; grade < data.Grades.Count; grade++)
        {
            ulong id = 0;
            EntityId Next() => new(++id);
            using var world = new BuildingWorld(data, null, Next);
            IReadOnlyList<Piece> pieces = blueprint.Place(world, BuildingFrame.At(Vector3D.Zero, Vector3D.UnitY, 0), grade, 42, 0, Next);
            Assert.True(pieces.Count > 200 && world.Structures.Count == 1 && world.Collapsing == 0, "one complete supported clan base");
            Assert.True(pieces.All(p => p.Grade == grade && p.Owner == 42 && p.Stability > 0), "selected grade and ownership throughout");
            Assert.Equal(9, pieces.Count(p => p.Def.Key == "foundation" && Math.Abs(p.Pose.Position.Z) <= 3.01), "3x3 core foundations");
            Assert.Equal(4, pieces.Count(p => p.Def.Key == "floor-frame-triangle" && p.Pose.Position.Y == 6), "four open downward firing bays");
            Assert.Equal(3, pieces.Count(p => p.Def.Key == "stairs-u"), "stairs to living, shooting and roof levels");
            Assert.Equal(3, pieces.Count(p => p.Def.Key == "floor-frame"), "each stair landing stays open");
            Assert.Equal(20, pieces.Count(p => p.Def.Key == "window" && p.Pose.Position.Y == 6), "covered windows around the gallery boundary");
            Assert.Equal(2, pieces.Count(p => p.Def.Key == "roof"), "sloped roof fighting cover");
            Door[] doors = pieces.Where(p => p.Door != null).Select(p => p.Door!).ToArray();
            Assert.Equal(4, doors.Length, "two entrance doors, cupboard room and roof exit");
            Assert.True(doors.All(d => d.Owner == 42 && d.Def.Key == (grade < 2 ? "wooden-door" : grade < 4 ? "sheet-metal-door" : "armoured-door")), "tier-appropriate doors");
            Assert.True(world.Structures.Single().Claim!.IsAuthorised(42), "owner is on the cupboard list");
            world.Tick(100);
            Assert.Equal(pieces.Count, world.PieceCount, "no delayed unsupported collapse");
        }
    }

    [Test]
    public static void ClanBaseRejectsBrokenBlueprintBeforeTouchingAWorld()
    {
        foreach (string bad in new[]
        {
            Json.Replace("\"foundation\"", "\"missing-piece\"", StringComparison.Ordinal),
            Json.Replace("\"socket\": 16", "\"socket\": 31", StringComparison.Ordinal),
            Json.Replace("\"parent\": \"core-0-1-1\"", "\"parent\": \"missing-parent\"", StringComparison.Ordinal),
            "{}",
        })
        {
            bool rejected = false;
            try { BuildingBlueprint.Parse(ContentFixture.Content.Building, bad); }
            catch (ContentException) { rejected = true; }
            Assert.True(rejected, "bad blueprint rejected before insertion");
        }
    }

    [Test]
    public static void ClanBaseSiteRefusalLeavesNoPartialPiecesAndProtectsPlayers()
    {
        using var rig = new GameRig();
        Zone zone = rig.Zone;
        BuildingBlueprint blueprint = Blueprint();
        Vector3 at = zone.GroundAt(40, 40);
        Entity occupant = rig.Add("Occupant", 40, 40);
        Assert.True(!BuildingBlueprintSpawner.TryPlace(zone, blueprint, at, 0, 2, 42, out _, out string occupied)
            && occupied.Contains("occupied", StringComparison.Ordinal), "a character blocks the whole site");
        Assert.Equal(0, zone.Building().World!.PieceCount, "refusal adds no pieces");
        zone.Teleport(occupant, zone.GroundAt(0, 0));
        Assert.True(BuildingBlueprintSpawner.TryPlace(zone, blueprint, at, 0, 2, 42, out var pieces, out string reason), "clear dry site: " + reason);
        int count = pieces.Count;
        foreach (Piece peek in pieces.Where(p => p.Def.Key == "floor-frame-triangle"))
        {
            Vector3 origin = peek.ZoneTransform.Position - zone.Frame.UpAt(peek.ZoneTransform.Position) * 0.25f;
            Vector3 down = -zone.Frame.UpAt(origin);
            Assert.True(!zone.Building().World.Physics!.Ray(origin, down, 6, out _, buildingOnly: true),
                "peek-down opening looks outside the base, not into a honeycomb compartment");
        }
        Assert.True(!BuildingBlueprintSpawner.TryPlace(zone, blueprint, at, 0, 2, 42, out _, out _), "another base cannot overlap");
        Assert.Equal(count, zone.Building().World.PieceCount, "overlap refusal leaves first base intact");
    }

    [Test]
    public static void ClanBaseRejectsSubmergedAndSteepSitesWithoutLeavingPieces()
    {
        BuildingBlueprint blueprint = Blueprint();
        using (var wet = new GameRig(spec => new LevelSurface(spec.Radius, 0, spec.Radius + 2)))
        {
            Assert.True(!BuildingBlueprintSpawner.TryPlace(wet.Zone, blueprint, wet.Zone.GroundAt(40, 40), 0, 2, 42,
                out _, out string reason) && reason.Contains("dry", StringComparison.Ordinal), "submerged footprint refused");
            Assert.Equal(0, wet.Zone.Building().World!.PieceCount, "water refusal is atomic");
        }
        using (var slope = new GameRig(spec => new SlopeSurface(spec.Radius, Vector3D.UnitY, 20)))
        {
            Assert.True(!BuildingBlueprintSpawner.TryPlace(slope.Zone, blueprint, slope.Zone.GroundAt(40, 40), 0, 2, 42,
                out _, out string reason) && reason.Contains("uneven", StringComparison.Ordinal), "steep footprint refused");
            Assert.Equal(0, slope.Zone.Building().World!.PieceCount, "slope refusal is atomic");
        }
    }

    [Test]
    public static void ClanBaseAdminCommandReplicatesAndSurvivesRestart()
    {
        using var file = new TempDatabase();
        Dictionary<EntityId, string> saved;
        using (var rig = new LoopbackRig(file.Path, admins: new[] { "Builder" }))
        {
            GameClient admin = rig.Join("Builder"), guest = rig.Join("Guest");
            rig.WaitUntilPlaying();
            Await(rig, () => admin.Console().IsAdmin, "admin state");
            Assert.True(Command(rig, guest, "spawn raidbase 2").Any(a => a.Kind == ConsoleReplyKind.Error), "guest cannot spawn a base");
            Assert.Equal(0, rig.Server.Inspect(s => s.Zone.Building().World!.PieceCount), "unauthorized command has no side effect");

            // The 26 m command target is outside the stand-in world's 9 m arrival clearing. Prepare this isolated
            // network/persistence fixture through the normal depletion state, as other loopback tests clear lanes.
            // Do not change the production spawner's overlap checks: exercise its player refusal before success.
            Vector3 site = rig.Server.Inspect(server =>
            {
                Zone zone = server.Zone;
                Assert.True(zone.Players().TryFindPlayer(admin.EntityId, out Entity builder, out _), "builder exists");
                ref Character character = ref zone.Entities.Get<Character>(builder);
                Vector3 feet = zone.WorldOf(character.State);
                Vector3 ahead = zone.Settle(feet + Facing.Basis(zone.ViewOf(character), zone.Frame.UpAt(feet)).Forward * 26);
                var objects = new List<NaturalObject>();
                zone.Nature!.Query(ahead, 20, objects);
                int cleared = 0;
                foreach (NaturalObject obj in objects)
                {
                    if (server.Content.Natural(obj.Kind) is not { IsSolid: true }) continue;
                    zone.Natural!.Apply(obj.Id, new NaturalOverride { Depleted = true, RespawnAtUnixMs = long.MaxValue });
                    cleared++;
                }
                Assert.True(zone.Players().TryFindPlayer(guest.EntityId, out Entity occupant, out _), "guest exists");
                zone.Teleport(occupant, ahead);
                Fixtures.Report($"clan-base network fixture: cleared {cleared} solid natural objects; guest occupies the command target");
                return ahead;
            });
            Assert.True(Command(rig, admin, "spawn raidbase 2").Any(a => a.Kind == ConsoleReplyKind.Error
                && a.Text.Contains("occupied", StringComparison.Ordinal)), "F1 refuses to enclose a player");
            Assert.Equal(0, rig.Server.Inspect(s => s.Zone.Building().World!.PieceCount), "occupied F1 refusal adds no partial base");
            rig.Server.Inspect(server =>
            {
                Zone zone = server.Zone;
                Assert.True(zone.Players().TryFindPlayer(guest.EntityId, out Entity occupant, out _), "guest still exists");
                zone.Teleport(occupant, zone.Settle(zone.Frame.Beside(site, 30, 0, 0)));
                return true;
            });
            var answers = Command(rig, admin, "spawn raidbase 2");
            Assert.True(answers.Any(a => a.Kind != ConsoleReplyKind.Error && a.Text.Contains("3×3", StringComparison.Ordinal)), "F1 spawn succeeds: " + string.Join(" / ", answers.Select(a => a.Text)));
            int expected = Blueprint().Nodes.Count;
            Await(rig, () => admin.Building().World!.PieceCount == expected && guest.Building().World!.PieceCount == expected, "complete base on both clients");
            Await(rig, () => guest.Building().World!.AllPieces.Count(p => p.Door != null) == 4
                && admin.Building().World!.Structures.Single().Claim != null, "doors and cupboard on clients");
            Assert.Equal(4, guest.Building().World!.AllPieces.Count(p => p.Door != null), "doors replicated");
            Assert.True(admin.Building().World!.Structures.Single().Claim != null, "cupboard replicated");
            saved = rig.Server.Inspect(s => s.Zone.Building().World!.AllPieces.ToDictionary(p => p.Id, Fingerprint));
            rig.Server.SaveAll().Wait();
        }
        using (var loaded = new LoopbackRig(file.Path))
        {
            loaded.Server.Inspect(s =>
            {
                BuildingWorld world = s.Zone.Building().World!;
                Assert.Equal(saved.Count, world.PieceCount, "every piece survives restart");
                foreach (Piece piece in world.AllPieces)
                    Assert.Equal(saved[piece.Id], Fingerprint(piece), "pose/grade/owner/door persisted");
                Assert.True(world.Structures.Single().Claim is { Authorised.Count: 1 }, "cupboard authorization persists");
                Assert.Equal(0, world.Collapsing, "loaded structure remains supported");
                return true;
            });
        }
    }

    private static string Fingerprint(Piece p) => $"{p.Def.Key}/{p.Grade}/{p.Pose}/{p.Owner}/{p.Door?.Def.Key}";

    private static void Await(LoopbackRig rig, Func<bool> ready, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!ready())
        {
            Assert.True(watch.Elapsed.TotalSeconds < 10, "timed out: " + what);
            rig.Run(0.05, (_, _) => default);
        }
    }

    private static List<ConsoleAnswer> Command(LoopbackRig rig, GameClient client, string line)
    {
        uint sequence = client.Console().SendConsole(line);
        var answers = new List<ConsoleAnswer>();
        void Receive(ConsoleAnswer answer) { if (answer.Seq == sequence) answers.Add(answer); }
        client.Console().ConsoleAnswered += Receive;
        try { Await(rig, () => answers.Count > 0, line); }
        finally { client.Console().ConsoleAnswered -= Receive; }
        return answers;
    }
}
