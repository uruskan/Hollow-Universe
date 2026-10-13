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

/// <summary>The ground game on the first world's real surface at the landing site (GROUND.md §3, §6.1).</summary>
public static class PlanetGroundTests
{
    private static GameContent C => ContentFixture.Content;

    private static ZoneSpec Landing => ZoneSpec.FirstWorld(Fixtures.Galaxy, FirstWorld.LandingLatitude, FirstWorld.LandingLongitude)!;

    /// <summary>
    /// The natural objects are the planet's own scatter: each one is a scatter item of the tile its ID names, at the
    /// same place, of the kind gather.json maps it to (trees give wood, berry bushes, ferns as fibre, boulders of size
    /// 0.9 and more); outcrops of all three kinds lie within a few hundred metres; two instances agree; made-up and
    /// far IDs are refused.
    /// </summary>
    [Test]
    public static void ScatterNatureIsThePlanetsOwn()
    {
        ZoneSpec spec = Landing;
        ISurface surface = spec.CreateSurface();
        Assert.True(surface is PlanetSurface, "the zone stands on the real surface");
        Assert.True(ReferenceEquals(surface, spec.CreateSurface()), "one surface per process");
        ZoneFrame frame = spec.CreateFrame(surface);
        INaturalObjects a = spec.CreateNature(surface, frame, C), b = spec.CreateNature(surface, frame, C);
        var first = new List<NaturalObject>();
        var second = new List<NaturalObject>();
        a.Query(-400, -400, 400, 400, first);
        b.Query(-400, -400, 400, 400, second);
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

    /// <summary>The beach points are real coast near the landing site: sand just above the sea, the water close.</summary>
    [Test]
    public static void BeachesAreRealCoast()
    {
        ZoneSpec spec = Landing;
        var surface = (PlanetSurface)spec.CreateSurface();
        ZoneFrame frame = spec.CreateFrame(surface);
        Beaches beaches = Beaches.Find(surface, frame);
        Assert.True(beaches.Points.Count >= 4, $"several beach points ({beaches.Points.Count})");
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        foreach (Vector3 p in beaches.Points)
        {
            Vector3D d = frame.ToBody(p).Normalized;
            SurfaceSample s = surface.Sample(d);
            surface.BiomeWeights(d, s, surface.Climate(d, s), biomes);
            Assert.InRange(0.4, 3.6, s.Height, "just above the sea");
            Assert.True(biomes[(int)Biome.Beach] >= 0.5, "on the beach");
        }
        Assert.Equal(beaches.Points.Count, Beaches.Find(surface, frame).Points.Count, "the same search finds the same points");
        Fixtures.Report($"{beaches.Points.Count} beach points, nearest {beaches.Points[0].Length():0} m, farthest {beaches.Points[^1].Length():0} m from the landing site");
    }

    /// <summary>
    /// GROUND.md §4.5 on the real ground: a walk across the meadow stays on the ground, a walk from the beach into the
    /// sea ends swimming without harm, and a fall from 40 m onto the meadow kills.
    /// </summary>
    [Test]
    public static void WalkSwimAndFallOnTheRealGround()
    {
        ZoneSpec spec = Landing;
        using var zone = new Zone(spec);
        var surface = (PlanetSurface)zone.Surface;

        // Walk north-east for 15 s from the landing site.
        Entity walker = zone.AddCharacter(new EntityId(1), "walker", zone.NewCharacter(zone.SpawnPoint(1)));
        float lifted = 0;
        for (int i = 0; i < 15 * CharacterMotor.TickRate; i++)
        {
            ref Character c = ref zone.Entities.Get<Character>(walker);
            c.Input = new MoveInput { Forward = 1, Yaw = -0.8f };
            zone.Step();
            if ((zone.Entities.Get<Character>(walker).State.Flags & MoveFlags.OnGround) != 0)
            {
                Vector3 at = zone.Entities.Get<Character>(walker).State.Position;
                lifted = Math.Max(lifted, Vector3.Dot(at - zone.GroundAt(at.X, at.Z), zone.Frame.UpAt(at)));
            }
        }
        Vector3 end = zone.Entities.Get<Character>(walker).State.Position;
        Assert.True(new Vector2(end.X, end.Z).Length() > 25, $"walked away ({new Vector2(end.X, end.Z).Length():0.0} m)");
        Assert.True(lifted < 0.15f, $"on the ground ({lifted * 100:0.0} cm above it at most)");

        // From a beach into the sea.
        Beaches beaches = Beaches.Find(surface, zone.Frame);
        Vector3 beach = beaches.Points[0];
        float yaw = 0;
        for (int k = 0; k < 8; k++)
        {
            float angle = k * MathF.PI / 4;
            var towards = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            Vector3D d = zone.Frame.ToBody(beach + towards * 30).Normalized;
            if (surface.HeightAt(d) < 0)
            {
                yaw = MathF.Atan2(-towards.X, -towards.Z);
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
            c.Input = new MoveInput { Forward = 1, Yaw = yaw, Buttons = MoveButtons.Sprint };
            zone.Step();
            damage = Math.Max(damage, zone.Entities.Get<Character>(swimmer).Events.FallDamage);
            swam = (zone.Entities.Get<Character>(swimmer).State.Flags & MoveFlags.Swimming) != 0;
        }
        Vector3 swum = zone.Entities.Get<Character>(swimmer).State.Position;
        Assert.True(swam, $"walked into the sea and swims ({Vector3.Distance(swum, beach):0} m from the beach)");
        Assert.Equal(0, damage, "no harm");

        // A fall from 40 m onto the meadow.
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
        Fixtures.Report($"walked {new Vector2(end.X, end.Z).Length():0} m, {lifted * 100:0.0} cm off the ground at most; swims {Vector3.Distance(swum, beach):0} m out from the beach {beach.Length():0} m from the landing site; 40 m fall: {fall} damage");
    }

    /// <summary>
    /// Over the network on the real planet: a new player arrives on a beach, the client's prediction stands on the
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
        Vector3 inland = -arrived;
        float yaw = MathF.Atan2(-inland.X, -inland.Z);
        rig.Run(3, (_, _) => new InputFrame { Forward = 1, Yaw = yaw });
        Assert.True(Vector3.Distance(ada.Character.Position, arrived) > 5, "walked");
        Assert.Equal(0L, ada.Stats.Corrections, $"prediction on the same ground as the server (worst {ada.Stats.PredictionErrorWorst * 1000:0.0} mm, last {ada.Stats.LastPredictionError * 1000:0.0} mm of {ada.Stats.Reconciliations})");
        var mine = new List<NaturalObject>();
        var servers = new List<NaturalObject>();
        Vector3 at = ada.Character.Position;
        ada.Nature!.Query(at.X - 60, at.Z - 60, at.X + 60, at.Z + 60, mine);
        rig.Server.Zone.Nature!.Query(at.X - 60, at.Z - 60, at.X + 60, at.Z + 60, servers);
        Assert.True(mine.Select(o => o.Id).SequenceEqual(servers.Select(o => o.Id)), $"the same natural objects ({mine.Count})");
        Fixtures.Report($"arrived {arrived.Length():0} m from the landing site; {mine.Count} natural objects within 60 m; {ada.Stats.Corrections} corrections");
    }
}
