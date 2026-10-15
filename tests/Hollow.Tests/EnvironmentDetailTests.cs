using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Text.Json;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Nature;
using Hollow.Surface;

namespace Hollow.Tests;

public static class EnvironmentDetailTests
{
    [Test]
    public static void AuthoredGroundCoverHasBoundedCompleteLods()
    {
        string folder = Path.Combine(Fixtures.Root, "game", "assets", "surface", "ground-cover");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "catalog.json")));
        JsonElement.ArrayEnumerator families = doc.RootElement.GetProperty("families").EnumerateArray();
        int familyCount = 0, meshCount = 0;
        foreach (JsonElement family in families)
        {
            familyCount++;
            string path = family.GetProperty("model").GetString()!;
            Assert.True(path.StartsWith("res://", StringComparison.Ordinal) && File.Exists(Path.Combine(Fixtures.Root, "game", path[6..])), "authored vegetation is local");
            Assert.Equal(3, family.GetProperty("variants").GetArrayLength(), "three distinct variants per family");
            foreach (JsonElement variant in family.GetProperty("variants").EnumerateArray())
            {
                JsonElement levels = variant.GetProperty("levels");
                Assert.Equal(3, levels.GetArrayLength(), "near, middle and far representations");
                for (int i = 0; i < 3; i++)
                {
                    int triangles = levels[i].GetProperty("triangles").GetInt32();
                    Assert.True(triangles > 0 && triangles <= 3000, "near geometry remains bounded rather than using the 40k source meshes");
                    Assert.True(levels[i].GetProperty("height").GetDouble() is > 0.05 and < 1.5, "source centimetres were normalized to metre-scale plants");
                    Assert.Equal(i == 2, levels[i].GetProperty("billboard").GetBoolean(), "far geometry uses the supplied billboard UVs");
                    if (i == 2) Assert.True(triangles <= 8, "far fields have a small geometry budget");
                    meshCount++;
                }
            }
        }
        Assert.Equal(4, familyCount, "wild grass, cotton grass, tundra grass and bracken fern");
        Fixtures.Report($"{familyCount} authored families, {meshCount} bounded LOD meshes; textures 2048 px");
    }

    [Test]
    public static void MonumentArtAndCollisionHaveCompleteLocalDependencies()
    {
        var checkedPaths = new HashSet<string>(StringComparer.Ordinal);
        void Check(string resource)
        {
            Assert.True(resource.StartsWith("res://", StringComparison.Ordinal), "world art resolves inside the game");
            if (!checkedPaths.Add(resource)) return;
            string path = Path.Combine(Fixtures.Root, "game", resource[6..]);
            Assert.True(File.Exists(path), "monument dependency exists: " + resource);
            if (Path.GetExtension(path) is ".tscn" or ".tres")
                foreach (Match match in Regex.Matches(File.ReadAllText(path), "path=\"(res://[^\"]+)\"")) Check(match.Groups[1].Value);
        }
        foreach (MonumentDefinition definition in MonumentDefinition.All)
        {
            Check(definition.Model);
            foreach (MonumentPiece piece in definition.Pieces) Check($"res://assets/monuments/kit/{piece.Key}.glb");
            Assert.True(definition.Indices.Length >= 36 && definition.Indices.Length % 3 == 0, "site has shared solid geometry");
            Assert.True(definition.Indices.All(i => i >= 0 && i < definition.Positions.Length), "shared collision indices are valid");
            Assert.True(definition.Positions.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)), "collision positions are finite");
        }
        Assert.True(MonumentDefinition.All.Any(p => p.Key == "service-substation"), "the supplied R01 industrial landmark is actually registered");
        Fixtures.Report($"{MonumentDefinition.All.Count} monument types, {checkedPaths.Count} local art dependencies");
    }

    [Test]
    public static void EarthMonumentsSharePadsClearingsCollisionAndAuthoritativeLoot()
    {
        var (system, earth) = StartingWorld.Find();
        WalkingWorld world = WalkingWorld.For(system, earth, 1200);
        var independent = new MonumentPlacements(world, WalkingWorld.SeedFor(Hash64.Of(system.Address.Value, earth.Path)));
        Assert.True(world.Monuments.All.SequenceEqual(independent.All), "independent server/client sites agree");
        Assert.True(world.Monuments.All.Count >= 8, "a planet has explorable sites, not a single showcase");
        Assert.True(world.Monuments.All.Select(p => p.Model).Distinct().Count() >= 3, "multiple original monument types appear");
        foreach (MonumentPlacement site in world.Monuments.All)
        {
            Assert.Near(site.Height, world.HeightAt(site.Up), 1e-7, "centre terrain meets the authored base");
            Assert.True(!world.AllowsScatter(site.Up), "no tree or ore node grows through a structure");
            Assert.True(site.Height > world.WaterLevelAt(site.Up), "monument pad is dry");
            Vector3D beside = site.Transform(new Vector3D(3, 0, 2));
            Vector3D ground = beside.Normalized * (world.Radius + world.HeightAt(beside.Normalized));
            Assert.Near(0, Vector3D.Distance(ground, beside), 1e-7, "pad is a tangent plane, not a rounded hill inside rooms");
            TileKey tile = TileKey.At(site.Up, world.ScatterLevel);
            Assert.True(world.ScatterFor(tile).All(o => world.Monuments.At(o.Direction, 2) == null), "actual jittered scatter respects cleared footprints");
        }
        MonumentPlacement first = world.Monuments.All[0];
        TileKey physicsTile = TileKey.At(first.Up, TileKey.LevelFor(world.Radius, 64));
        TriangleMesh terrain = SurfaceGrid.Collision(world, ZoneFrame.World, physicsTile, 2, SurfaceGrid.DefaultSpacing);
        TriangleMesh solid = ((ISurface)world).GroundCollision(ZoneFrame.World, physicsTile, 2);
        Assert.True(solid.TriangleCount >= terrain.TriangleCount + first.Definition.Indices.Length / 3, "authored walls and floors reach shared movement collision");
        var loot = LootSpots.ForMonuments(world, ZoneFrame.World, ContentFixture.Content);
        Assert.True(loot.Count >= world.Monuments.All.Count * 3, "sites have real world containers at authored anchors");
        Assert.Equal(loot.Count, loot.Select(p => p.Id).Distinct().Count(), "monument loot IDs never collide");
        Assert.True(loot.All(p => (p.Id & (0xFFUL << 56)) == LootSpots.MonumentTag), "existing world loot IDs remain a separate namespace");
        Assert.True(loot.SequenceEqual(LootSpots.ForMonuments(world, ZoneFrame.World, ContentFixture.Content)), "loot stays stable across generation");
        Assert.True(loot.Any(p => p.Prop.LootKey == "fuel-barrel"), "wellheads provide the supplied fuel-barrel resource path");
        foreach (var beach in Beaches.Find(world, ZoneFrame.World).Points)
            Assert.True(world.Monuments.At(ZoneFrame.World.ToBody(beach).Normalized, 8) == null, "new players never spawn inside monument walls");
        foreach (MonumentPlacement site in world.Monuments.All.GroupBy(p => p.Model).Select(g => g.First()))
            Fixtures.Report($"{site.Definition.Key}: --lat {DMath.Asin(site.Up.Y) * DMath.RadToDeg:0.0000} --lon {DMath.Atan2(-site.Up.Z, site.Up.X) * DMath.RadToDeg:0.0000}");
        MonumentPlacement substation = world.Monuments.All.First(p => p.Definition.Key == "service-substation");
        ReportCapture("substation exterior", world, substation.Transform(new Vector3D(3, 0, substation.Definition.Radius + 10)),
            substation.Transform(new Vector3D(2, 2, 0)), 3);
        foreach (Vector3D direction in PlanetSurface.FibonacciSphere(4096))
        {
            if (Math.Abs(direction.Y) > 0.7) continue;
            double depth = world.WaterLevelAt(direction) - world.HeightAt(direction);
            if (depth < 1.5 || depth > 3) continue;
            Fixtures.Report($"underwater: --lat {DMath.Asin(direction.Y) * DMath.RadToDeg:0.000000} --lon {DMath.Atan2(-direction.Z, direction.X) * DMath.RadToDeg:0.000000} --yaw 0 --pitch -8 --lift -1.4 (bed depth {depth:0.00} m)");
            break;
        }
        Fixtures.Report($"Earth monuments {world.Monuments.All.Count}, persistent loot anchors {loot.Count}");
    }

    [Test]
    public static void EarthHasAllFourHarvestableOresWithCompatibleIds()
    {
        var (system, earth) = StartingWorld.Find();
        WalkingWorld world = WalkingWorld.For(system, earth, 1200);
        var nature = new ScatterNaturalObjects(world, ZoneFrame.World, ContentFixture.Content);
        var found = new HashSet<string>();
        foreach (Vector3D direction in PlanetSurface.FibonacciSphere(180))
        {
            if (world.HeightAt(direction) < 3) continue;
            foreach (NaturalObject item in nature.InTile(TileKey.At(direction, world.ScatterLevel)))
            {
                string key = ContentFixture.Content.Natural(item.Kind)!.Key;
                if (key.EndsWith("-node", StringComparison.Ordinal))
                {
                    bool firstForKind = found.Add(key);
                    Assert.True(nature.TryGet(item.Id, out NaturalObject decoded) && decoded.Id == item.Id, "resource IDs resolve through the server provider");
                    if (firstForKind && key == "high-grade-node")
                    {
                        Vector3D at = ZoneFrame.World.ToBody(item.Position), up = at.Normalized;
                        Vector3D east = Vector3D.Cross(Vector3D.UnitY, up).Normalized;
                        ReportCapture("HQ node exterior", world, at + east * 7, at + up, 0);
                    }
                }
            }
            if (found.Count >= 4) break;
        }
        Assert.True(new[] { "stone-node", "metal-node", "sulphur-node", "high-grade-node" }.All(found.Contains), "all four supplied node families are playable");
        Assert.Equal(9, (int)ScatterKind.StoneOutcrop, "existing persisted scatter identities stay fixed");
        Assert.Equal(10, (int)ScatterKind.MetalOutcrop, "existing persisted scatter identities stay fixed");
        Assert.Equal(11, (int)ScatterKind.SulphurOutcrop, "existing persisted scatter identities stay fixed");
    }

    [Test]
    public static void EarthCliffsHaveStablePlacementsAndSharedCollision()
    {
        var (system, earth) = StartingWorld.Find();
        WalkingWorld world = WalkingWorld.For(system, earth, 1200);
        var independent = new CliffPlacements(world, WalkingWorld.SeedFor(Hash64.Of(system.Address.Value, earth.Path)));
        CliffPlacement? first = null;
        int count = 0;
        for (long i = 0; i < TileKey.CountAt(world.Cliffs.Level); i++)
        {
            TileKey key = TileKey.FromIndex(world.Cliffs.Level, i)!.Value;
            CliffPlacement? placement = world.Cliffs.At(key);
            Assert.Equal(placement, independent.At(key), "independent server/client placement agrees");
            if (placement is not { } cliff) continue;
            first ??= cliff;
            count++;
            Assert.Near(1, Vector3D.Dot(Vector3D.Cross(cliff.Right, cliff.Up), cliff.Forward), 1e-10, "right-handed planet-relative basis");
            foreach (Vector3D vertex in CliffGeometry.All[cliff.Model].Positions)
                Assert.True(vertex.Length * cliff.Scale <= CliffPlacements.MaximumExtent, "shared streaming/collision bound contains scan");
        }
        Assert.True(count > 12, "Earth has real formations beyond a single hand-picked screenshot");
        CliffPlacement sample = first!.Value;
        TileKey tile = TileKey.At(sample.Position, TileKey.LevelFor(world.Radius, 64));
        TriangleMesh baseMesh = SurfaceGrid.Collision(world, ZoneFrame.World, tile, 2, SurfaceGrid.DefaultSpacing);
        TriangleMesh actual = ((ISurface)world).GroundCollision(ZoneFrame.World, tile, 2);
        TriangleMesh repeat = ((ISurface)world).GroundCollision(ZoneFrame.World, tile, 2);
        Assert.True(actual.TriangleCount > baseMesh.TriangleCount, "the surface collision API includes solid cliffs");
        Assert.True(actual.Positions.SequenceEqual(repeat.Positions) && actual.Indices.SequenceEqual(repeat.Indices), "physics streams exactly the same triangles again");
        double lat = DMath.Asin(sample.Up.Y) * DMath.RadToDeg;
        double lon = DMath.Atan2(-sample.Up.Z, sample.Up.X) * DMath.RadToDeg;
        Fixtures.Report($"Earth scanned formations {count}; first {lat:0.0000}, {lon:0.0000}; collision adds {actual.TriangleCount - baseMesh.TriangleCount} triangles");
        double height = CliffGeometry.All[sample.Model].Height * sample.Scale;
        ReportCapture("cliff exterior", world, sample.Position + sample.Up * height * 0.55 + sample.Forward * 30,
            sample.Position + sample.Up * height * 0.7, 1.5);
    }

    // Diagnostic camera commands look from outside the solids, rather than spawning inside the landmark centre.
    private static void ReportCapture(string label, WalkingWorld world, Vector3D at, Vector3D target, double lift)
    {
        Vector3D up = at.Normalized, east = Vector3D.Cross(Vector3D.UnitY, up).Normalized;
        Vector3D north = Vector3D.Cross(up, east);
        Vector3D eye = up * (world.Radius + world.HeightAt(up) + 1.7 + lift);
        Vector3D view = (target - eye).Normalized;
        double yaw = DMath.Atan2(Vector3D.Dot(view, east), Vector3D.Dot(view, north)) * DMath.RadToDeg;
        double pitch = DMath.Asin(Vector3D.Dot(view, up)) * DMath.RadToDeg;
        Fixtures.Report($"{label}: --lat {DMath.Asin(up.Y) * DMath.RadToDeg:0.000000} --lon {DMath.Atan2(-up.Z, up.X) * DMath.RadToDeg:0.000000} --yaw {yaw:0.000} --pitch {pitch:0.000} --lift {lift:0.0}");
    }

    [Test]
    public static void WoodlandGrassIsNotSuppressedThreeTimesAndWaterHasNoGroundCover()
    {
        Span<double> biomes = stackalloc double[BiomeTable.Count];
        Span<double> materials = stackalloc double[BiomeTable.MaterialCount];
        biomes[(int)Biome.TemperateForest] = 1;
        materials[(int)GroundMaterial.ForestFloor] = 0.85;
        materials[(int)GroundMaterial.Grass] = 0.15;
        var woodland = new GroundCover(15, 0.65, 0.25, 0.0135);
        GroundDetailCover detail = GroundDetailCover.At(woodland, 0.15, 3, biomes, materials);
        Assert.True(detail.Grass > 0.2 && detail.Grass < 0.4, "already shaded woodland cover survives the presentation layer");
        Assert.True(detail.Litter > 0.3 && detail.Plants > 0.2, "woodland keeps litter and understory alongside grass");
        Assert.Equal(default(GroundDetailCover), GroundDetailCover.At(woodland, 0.15, -0.1, biomes, materials), "submerged lake or sea bed has no tufts or litter");
        Assert.Near(0, GroundDetailCover.At(woodland, 0.9, 3, biomes, materials).Grass, 1e-10, "steep cliffs stay bare");
        materials.Clear();
        materials[(int)GroundMaterial.Sand] = 1;
        Assert.Near(0, GroundDetailCover.At(woodland, 0, 3, biomes, materials).Grass, 1e-10, "bare sand stays bare");
        materials.Clear();
        materials[(int)GroundMaterial.Grass] = 1;
        biomes.Clear();
        biomes[(int)Biome.Grassland] = 1;
        detail = GroundDetailCover.At(new GroundCover(18, 0.5, 0.8, 0), 0.1, double.PositiveInfinity, biomes, materials);
        Assert.True(detail.Grass > 0.9 && detail.Flowers > 0.9, "open meadow remains a rich mixed field on dry worlds too");
    }
}
