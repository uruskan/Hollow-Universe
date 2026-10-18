using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>
/// The first world's ground (GROUND.md §3, §6.1): the real planet's own scatter as natural objects, and the whole world
/// a zone stands on, its beaches all round it, walking, swimming and falling on it. Until S4 makes the real ground at
/// walking scale, zones stand on the stand-in world (docs/WHOLE_WORLD.md §4); the planet's scatter is read in a frame at
/// the landing site, as S4 will give it its place.
/// </summary>
public static class PlanetGroundTests
{
    private static GameContent C => ContentFixture.Content;

    private static ZoneSpec Landing => ZoneSpec.FirstWorld(Fixtures.Galaxy, FirstWorld.LandingLatitude, FirstWorld.LandingLongitude)!;

    /// <summary>
    /// The natural objects of the real planet are its own scatter: each one is a scatter item of the tile its ID names,
    /// at the same place, of the kind gather.json maps it to (trees give wood, berry bushes, ferns as fibre, boulders of
    /// size 0.9 and more); outcrops of all three kinds lie within a few hundred metres; two instances agree; made-up and
    /// far IDs are refused.
    /// </summary>
    [Test]
    public static void ScatterNatureIsThePlanetsOwn()
    {
        ZoneSpec spec = Landing;
        var found = FirstWorld.Find(Fixtures.Galaxy)!.Value;
        PlanetSurface surface = PlanetSurface.For(Fixtures.Galaxy, found.System, found.Planet);
        ZoneFrame frame = ZoneFrame.At(surface, spec.LatitudeDegrees * Math.PI / 180, spec.LongitudeDegrees * Math.PI / 180);
        INaturalObjects a = spec.CreateNature(surface, frame, C), b = spec.CreateNature(surface, frame, C);
        Assert.True(a is ScatterNaturalObjects, "the planet's scatter on the planet's surface");
        var first = new List<NaturalObject>();
        var second = new List<NaturalObject>();
        Vector3 landing = new GroundProbe(surface, frame).On(spec.LandingDirection);
        a.Query(landing, 560, first);
        b.Query(landing, 560, second);
        Assert.True(first.Count > 1000 && first.SequenceEqual(second), $"two instances give the same objects ({first.Count})");

        var counts = new Dictionary<string, int>();
        var tiles = new Dictionary<ulong, IReadOnlyList<ScatterItem>>();
        foreach (NaturalObject o in first)
        {
            NaturalKind kind = C.Natural(o.Kind)!;
            counts[kind.Key] = counts.GetValueOrDefault(kind.Key) + 1;
            (TileKey tile, int index) = ScatterNaturalObjects.Decode(o.Id)!.Value;
            if (!tiles.TryGetValue(tile.Packed, out IReadOnlyList<ScatterItem>? items))
            {
                items = surface.ScatterFor(tile);
                tiles[tile.Packed] = items;
            }
            ScatterItem item = items.First(i => i.Index == index);
            Assert.True(kind.ScatterKinds.Contains(item.Kind) && item.Scale >= kind.ScatterMinScale, $"{kind.Key} is a mapped {item.Kind}");
            Assert.True(Vector3.Distance(frame.ToLocal(item.Position(surface.Radius)), o.Position) < 1e-3f, "where the scatter put it");
        }
        foreach (string key in new[] { "fibre-plant", "boulder", "stone-node", "metal-node", "sulphur-node" })
        {
            Assert.True(counts.GetValueOrDefault(key) > 0, $"some {key} within 400 m");
        }
        Assert.True(counts.GetValueOrDefault("red-berry-bush") + counts.GetValueOrDefault("blue-berry-bush") > 0, "berry bushes");
        Assert.True(new[] { "pine-tree", "fir-tree", "birch-tree", "broadleaf-tree" }.Sum(k => counts.GetValueOrDefault(k)) > 50, "a forest edge");

        NaturalObject tree = first.First(o => C.Natural(o.Kind)!.Class == NaturalClass.Tree);
        Assert.True(a.TryGet(tree.Id, out NaturalObject again) && again == tree, "found by ID");
        Assert.True(!a.TryGetNear(tree.Id, tree.Position + new Vector3(2000, 0, 0), 5, out _), "refused far from the asker");
        Assert.True(!a.TryGet(0, out _) && !a.TryGet(ulong.MaxValue, out _) && !a.TryGet(tree.Id ^ 1UL << 63, out _), "made-up IDs aren't found");
        // The same tile and cell, but a kind the zone doesn't play (plain bushes are scenery).
        ulong bush = tree.Id & ~(0xFFUL << 17) | (ulong)ScatterKind.Bush << 17;
        Assert.True(!a.TryGet(bush, out _), "scenery isn't a natural object");
        Fixtures.Report(string.Join(", ", counts.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}")) + $" in 800 × 800 m ({tiles.Count} scatter tiles)");
    }

    /// <summary>
    /// The beach points are coast all round the world a zone stands on (docs/WHOLE_WORLD.md decision 8): beach just above
    /// the sea, the water close, on most faces of the world's cube, and the same every time.
    /// </summary>
    [Test]
    public static void BeachesAreRealCoast()
    {
        ZoneSpec spec = Landing;
        ISurface surface = spec.CreateSurface(diagnostic: true);
        ZoneFrame frame = spec.CreateFrame(surface);
        Beaches beaches = Beaches.Find(surface, frame);
        Assert.True(beaches.Points.Count >= 20, $"beach points all round ({beaches.Points.Count})");
        double sea = surface.SeaRadius - surface.Radius;
        foreach (Vector3 p in beaches.Points)
        {
            Vector3D d = frame.ToBody(p).Normalized;
            Assert.InRange(0.4, 3.6, surface.HeightAt(d) - sea, "just above the sea");
            Assert.Equal(Biome.Beach, surface.BiomeAt(d), "on the beach");
        }
        int faces = beaches.Points.Select(p => TileKey.At(frame.ToBody(p), 0).Face).Distinct().Count();
        Assert.True(faces >= 4, $"on most faces of the world ({faces})");
        Assert.True(beaches.Points.SequenceEqual(Beaches.Find(surface, frame).Points), "the same search finds the same points");
        Vector3 landing = new GroundProbe(surface, frame).On(spec.LandingDirection);
        Fixtures.Report($"{beaches.Points.Count} beach points on {faces} faces, the nearest {beaches.Points.Min(p => Vector3.Distance(p, landing)):0} m from the landing site");
    }

    /// <summary>
    /// GROUND.md §4.5 on a whole world's ground: a walk across the land stays on the ground, a walk from a beach into the
    /// sea ends swimming without harm, and a fall from 40 m onto the land kills.
    /// </summary>
    [Test]
    public static void WalkSwimAndFallOnTheRealGround()
    {
        ZoneSpec spec = Landing;
        using var zone = new Zone(spec.CreateSurface(diagnostic: true), spec);
        ISurface surface = zone.Surface;

        // Walk north-east for 15 s from the landing site.
        Vector3 start = zone.SpawnPoint(1);
        Entity walker = zone.AddCharacter(new EntityId(1), "walker", zone.NewCharacter(start));
        float lifted = 0;
        for (int i = 0; i < 15 * CharacterMotor.TickRate; i++)
        {
            ref Character c = ref zone.Entities.Get<Character>(walker);
            c.Input = new MoveInput { Forward = 1, View = Facing.View(-0.8f, 0, zone.Frame.UpAt(c.State.Position)) };
            zone.Step();
            if ((zone.Entities.Get<Character>(walker).State.Flags & MoveFlags.OnGround) != 0)
            {
                Vector3 at = zone.Entities.Get<Character>(walker).State.Position;
                lifted = Math.Max(lifted, Vector3.Dot(at - zone.Settle(at), zone.Frame.UpAt(at)));
            }
        }
        Vector3 end = zone.Entities.Get<Character>(walker).State.Position;
        float walked = Vector3.Distance(end, start);
        Assert.True(walked > 25, $"walked away ({walked:0.0} m)");
        Assert.True(lifted < 0.15f, $"on the ground ({lifted * 100:0.0} cm above it at most)");

        // From a beach into the sea.
        Beaches beaches = Beaches.Find(surface, zone.Frame);
        Vector3 beach = beaches.Points[0];
        float yaw = 0;
        for (int k = 0; k < 8; k++)
        {
            float heading = k * MathF.PI / 4;
            Vector3D d = zone.Frame.ToBody(beach + Facing.Basis(heading, zone.Frame.UpAt(beach)).Forward * 30).Normalized;
            if (surface.Radius + surface.HeightAt(d) < surface.SeaRadius)
            {
                yaw = heading;
                break;
            }
        }
        Entity swimmer = zone.AddCharacter(new EntityId(2), "swimmer", zone.NewCharacter(beach));
        bool swam = false;
        int damage = 0;
        // The beaches shelve gently: the water gets deep enough to swim about a hundred metres out.
        for (int i = 0; i < 45 * CharacterMotor.TickRate && !swam; i++)
        {
            ref Character c = ref zone.Entities.Get<Character>(swimmer);
            c.Input = new MoveInput { Forward = 1, View = Facing.View(yaw, 0, zone.Frame.UpAt(c.State.Position)), Buttons = MoveButtons.Sprint };
            zone.Step();
            damage = Math.Max(damage, zone.Entities.Get<Character>(swimmer).Events.FallDamage);
            swam = (zone.Entities.Get<Character>(swimmer).State.Flags & MoveFlags.Swimming) != 0;
        }
        Vector3 swum = zone.Entities.Get<Character>(swimmer).State.Position;
        Assert.True(swam, $"walked into the sea and swims ({Vector3.Distance(swum, beach):0} m from the beach)");
        Assert.Equal(0, damage, "no harm");

        // A fall from 40 m onto the land.
        Vector3 meadow = zone.SpawnPoint(3);
        Entity faller = zone.AddCharacter(new EntityId(3), "faller", zone.NewCharacter(meadow));
        zone.Teleport(faller, meadow + zone.Frame.UpAt(meadow) * 40);
        int fall = 0;
        for (int i = 0; i < 5 * CharacterMotor.TickRate; i++)
        {
            zone.Step();
            fall = Math.Max(fall, zone.Entities.Get<Character>(faller).Events.FallDamage);
        }
        Assert.True(fall >= 100, $"a 40 m fall kills ({fall} damage)");
        Fixtures.Report($"walked {walked:0} m, {lifted * 100:0.0} cm off the ground at most; swims {Vector3.Distance(swum, beach):0} m out from the beach {Vector3.Distance(beach, zone.Landing):0} m from the landing site; 40 m fall: {fall} damage");
    }

    /// <summary>
    /// Over the network on a server with beaches: a new player arrives on a beach, the client's prediction stands on the
    /// same ground as the server (no corrections while walking up the beach), and the client knows the same natural
    /// objects under the same IDs.
    /// </summary>
    [Test]
    public static void ArriveOnTheBeachOverTheNetwork()
    {
        using var rig = new LoopbackRig(planet: true);
        GameClient ada = rig.Join("Ada");
        rig.WaitUntilPlaying();
        // Arrival settles onto the sloping sand (a few centimetres, corrected once at most); then the steady state.
        rig.Run(1, (_, _) => default);
        ada.Stats.ResetTotals();
        Vector3 arrived = ada.Character.Position;
        Assert.True(rig.Server.Beaches!.Points.Any(p => Vector3.Distance(p, arrived) < 1), $"arrived on a beach ({arrived})");
        // Walk inland (towards the landing site) for three seconds.
        float yaw = Facing.YawOf(rig.Server.Zone.Landing - arrived, ada.Frame.UpAt(arrived));
        rig.Run(3, (_, _) => new InputFrame { Forward = 1, Yaw = yaw });
        Assert.True(Vector3.Distance(ada.Character.Position, arrived) > 5, "walked");
        Assert.Equal(0L, ada.Stats.Corrections, $"prediction on the same ground as the server (worst {ada.Stats.PredictionErrorWorst * 1000:0.0} mm, last {ada.Stats.LastPredictionError * 1000:0.0} mm of {ada.Stats.Reconciliations})");
        var mine = new List<NaturalObject>();
        var servers = new List<NaturalObject>();
        Vector3 at = ada.Character.Position;
        ada.Survival().Nature!.Query(at, 60, mine);
        rig.Server.Zone.Nature!.Query(at, 60, servers);
        Assert.True(mine.Select(o => o.Id).SequenceEqual(servers.Select(o => o.Id)), $"the same natural objects ({mine.Count})");
        Fixtures.Report($"arrived {Vector3.Distance(arrived, rig.Server.Zone.Landing):0} m from the landing site; {mine.Count} natural objects within 60 m; {ada.Stats.Corrections} corrections");
    }
}
