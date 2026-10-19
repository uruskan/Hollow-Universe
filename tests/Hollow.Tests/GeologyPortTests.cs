using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class GeologyPortTests
{
    [Test]
    public static void CliffPlacementProfilesRetainTheExportedCollisionReceipt()
    {
        string directory = Path.Combine(Fixtures.Root, "game/assets/surface/cliffs");
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "provenance.json")));
        using FileStream data = File.OpenRead(Path.Combine(Fixtures.Root, "src/Hollow.Surface/Data/cliffs.json"));
        Assert.Equal(receipt.RootElement.GetProperty("collision_sha256").GetString()!,
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), "placement metadata is covered by the shared collision receipt");
        foreach (CliffGeometry model in CliffGeometry.All)
        {
            Assert.True(model.BoundingRadius > 0 && model.Width > 0 && model.Depth > 0, "shared bounds describe real geometry");
            Assert.True(model.Positions.All(p => p.Length <= model.BoundingRadius), "shared broad-phase bound contains every collision vertex");
            Assert.True(model.Indices.All(i => i >= 0 && i < model.Positions.Length), "exported triangles address real vertices");
        }
        CliffGeometry tall = CliffGeometry.All.Single(m => m.Key == "nordic_tall");
        double lower = tall.Positions.Where(p => p.Y < tall.Height * 0.15).Average(p => p.Z);
        double upper = tall.Positions.Where(p => p.Y > tall.Height * 0.7).Average(p => p.Z);
        Assert.True((upper - lower) * tall.Facing < 0,
            "the tall scan's crest leans uphill after authored orientation, instead of making downhill spikes");
    }

    [Test]
    public static void ScannedRockCatalogMatchesRealMeshAndMaterialData()
    {
        string directory = Path.Combine(Fixtures.Root, "game/assets/surface/rocks");
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "catalog.json")));
        string model = Path.Combine(Fixtures.Root, "game", catalog.RootElement.GetProperty("model").GetString()![6..]);
        using FileStream input = File.OpenRead(model);
        Assert.Equal(catalog.RootElement.GetProperty("sha256").GetString()!, Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(), "model matches the conversion receipt");
        using JsonDocument glb = JsonDocument.Parse(File.ReadAllText(model));
        foreach (JsonElement output in catalog.RootElement.GetProperty("outputs").EnumerateArray())
        {
            string file = Path.Combine(Fixtures.Root, output.GetProperty("file").GetString()!);
            using FileStream dependency = File.OpenRead(file);
            Assert.Equal(output.GetProperty("sha256").GetString()!, Convert.ToHexString(SHA256.HashData(dependency)).ToLowerInvariant(), "all external PBR maps and buffers match their receipt");
            if (Path.GetExtension(file) is ".jpg" or ".png")
            {
                string options = File.ReadAllText(file + ".import");
                Assert.True(Regex.IsMatch(options, @"(?m)^compress/mode=2\r?$") && Regex.IsMatch(options, @"(?m)^mipmaps/generate=true\r?$"),
                    "scanned maps must be GPU-compressed with mipmaps; lossless runtime imports caused loading stalls/device loss");
            }
        }
        JsonElement document = glb.RootElement;
        var nodes = document.GetProperty("nodes").EnumerateArray().Where(n => n.TryGetProperty("mesh", out _))
            .ToDictionary(n => n.GetProperty("name").GetString()!, n => n);
        int variants = 0;
        foreach (JsonElement variant in catalog.RootElement.GetProperty("variants").EnumerateArray())
        {
            variants++;
            JsonElement levels = variant.GetProperty("levels");
            Assert.Equal(2, levels.GetArrayLength(), "each scan has near/far geometry");
            for (int lod = 0; lod < 2; lod++)
            {
                JsonElement record = levels[lod], node = nodes[record.GetProperty("node").GetString()!];
                Assert.True(!node.TryGetProperty("matrix", out _) && !node.TryGetProperty("translation", out _) &&
                    !node.TryGetProperty("rotation", out _) && !node.TryGetProperty("scale", out _), "instance-ready geometry has baked transforms");
                int triangles = 0;
                foreach (JsonElement primitive in document.GetProperty("meshes")[node.GetProperty("mesh").GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    triangles += document.GetProperty("accessors")[primitive.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32() / 3;
                    JsonElement attributes = primitive.GetProperty("attributes");
                    Assert.True(attributes.TryGetProperty("NORMAL", out _) && attributes.TryGetProperty("TEXCOORD_0", out _), "scan normals and UVs survive conversion");
                    JsonElement material = document.GetProperty("materials")[primitive.GetProperty("material").GetInt32()];
                    Assert.True(material.TryGetProperty("normalTexture", out _), "real tangent-space normal texture");
                    JsonElement pbr = material.GetProperty("pbrMetallicRoughness");
                    Assert.True(pbr.TryGetProperty("baseColorTexture", out _) && pbr.TryGetProperty("metallicRoughnessTexture", out _), "source albedo and roughness remain bound");
                    Assert.Near(0, pbr.GetProperty("metallicFactor").GetDouble(), 1e-9, "stone is nonmetallic");
                }
                Assert.Equal(record.GetProperty("triangles").GetInt32(), triangles, "receipt matches actual indexed geometry");
                Assert.True(triangles <= (lod == 0 ? 6500 : 900), "bounded triangle cost");
                Assert.True(record.GetProperty("height").GetDouble() <= 0.773, "preserves existing boulder gameplay envelope after 0.12 m sink, within 3 mm simplification tolerance");
            }
        }
        Assert.Equal(9, variants, "seven distinct moss stones plus two tundra boulders");
        Fixtures.Report($"{variants} real rock variants, 18 LOD meshes; native PBR textures; source/output hashes recorded");
    }

    [Test]
    public static void CliffFormationsStreamAcrossCubeFaceSeams()
    {
        // T2's gate: stable IDs and collision across seams. Every piece of a cell on a face's edge is found, once, by a
        // neighbour query from the other face, and the collision tile there carries its exact transform.
        var (system, earth) = StartingWorld.Find();
        WalkingWorld world = WalkingWorld.For(system, earth, 1200);
        CliffPlacements cliffs = world.Cliffs;
        int side = 1 << cliffs.Level, cells = 0, crossings = 0, collided = 0;
        int collisionLevel = TileKey.LevelFor(world.Radius, 64);
        for (long i = 0; i < TileKey.CountAt(cliffs.Level); i++)
        {
            TileKey tile = TileKey.FromIndex(cliffs.Level, i)!.Value;
            if (tile.X != 0 && tile.Y != 0 && tile.X != side - 1 && tile.Y != side - 1) continue;
            IReadOnlyList<CliffPlacement> formation = cliffs.FormationAt(tile);
            if (formation.Count == 0) continue;
            cells++;
            foreach (CliffPlacement piece in formation)
            {
                Vector3D east = Vector3D.Cross(Vector3D.UnitY, piece.Up);
                east = east.LengthSquared > 1e-16 ? east.Normalized : Vector3D.UnitZ;
                Vector3D north = Vector3D.Cross(piece.Up, east);
                for (int bearing = 0; bearing < 8; bearing++)
                {
                    double angle = bearing * Math.PI / 4;
                    Vector3D across = (piece.Position + (east * Math.Cos(angle) + north * Math.Sin(angle)) * 50).Normalized;
                    if (TileKey.At(across, cliffs.Level).Face == tile.Face) continue;
                    Vector3D probe = across * (world.Radius + world.HeightAt(across));
                    List<CliffPlacement> found = cliffs.Near(probe, Vector3D.Distance(probe, piece.Position) + 1);
                    Assert.Equal(1, found.Count(p => p.Key == piece.Key), "a piece is found once from across its face's seam");
                    Assert.True(found.Contains(piece), "with the transform its own cell gave it");
                    Assert.Equal(found.Count, found.Select(p => p.Key).Distinct().Count(), "no piece is listed twice at a seam");
                    crossings++;
                    TileKey collisionTile = TileKey.At(probe, collisionLevel);
                    if (collisionTile.Face == tile.Face || collided >= 24) continue;
                    double reach = TileKey.EdgeLength(collisionLevel, world.Radius) * 0.95 + 2;
                    Vector3D centre = collisionTile.Centre * (world.Radius + world.HeightAt(collisionTile.Centre));
                    if (Vector3D.Distance(centre, piece.Position) > reach) continue;
                    TriangleMesh collision = cliffs.Collision(ZoneFrame.World, collisionTile, 2);
                    var expected = ZoneFrame.World.ToLocal(piece.Transform(CliffGeometry.All[piece.Model].Positions[0]));
                    bool present = false;
                    for (int k = 0; k + 2 < collision.Positions.Length && !present; k += 3)
                        present = collision.Positions[k] == expected.X && collision.Positions[k + 1] == expected.Y &&
                            collision.Positions[k + 2] == expected.Z;
                    Assert.True(present, "a collision tile on the other face carries the piece's exact transform");
                    collided++;
                }
            }
        }
        Fixtures.Report($"{cells} seam cells with formations, {crossings} neighbour queries across a seam, {collided} collision tiles");
        Assert.True(cells >= 5 && crossings >= 20 && collided >= 5, "Earth has formations on its cube's seams to check");
    }

    [Test]
    public static void CliffChainsHaveDistinctStablePartsAndStreamAcrossCellEdges()
    {
        var (system, earth) = StartingWorld.Find();
        WalkingWorld world = WalkingWorld.For(system, earth, 1200);
        CliffPlacements independent = WalkingWorld.For(system, earth, world.Radius, world.Parameters).Cliffs;
        int groups = 0, pieces = 0, tiered = 0;
        for (long i = 0; i < TileKey.CountAt(world.Cliffs.Level) && groups < 30; i++)
        {
            TileKey tile = TileKey.FromIndex(world.Cliffs.Level, i)!.Value;
            IReadOnlyList<CliffPlacement> formation = world.Cliffs.FormationAt(tile);
            if (formation.Count < 2) continue;
            groups++;
            Assert.True(formation.SequenceEqual(independent.FormationAt(tile)), "independent generation agrees on every member, scale and transform");
            Assert.Equal(formation.Count, formation.Select(p => p.Key).Distinct().Count(), "streaming never aliases neighbours under one tile ID");
            Assert.True(formation.Count <= CliffPlacements.MaximumParts, "bounded fitted columns and descending rows");
            foreach (var face in formation.GroupBy(p => p.Part / (CliffPlacements.MaximumColumns * CliffPlacements.MaximumTiers)))
                Assert.Equal(1, face.Select(p => CliffGeometry.All[p.Model].MaterialFamily).Distinct().Count(),
                    "one face uses compatible geology rather than a random patchwork of scanned materials");
            var column = formation.Where(p => p.Part < CliffPlacements.MaximumTiers).ToArray();
            if (column.Length > 1)
            {
                tiered++;
                Assert.True(column[^1].Position.Length < column[0].Position.Length - 0.39,
                    "lower rows cover the vertical face, not just another contour at the same height");
            }
            foreach (CliffPlacement piece in formation)
            {
                pieces++;
                Assert.Near(1, Vector3D.Dot(Vector3D.Cross(piece.Right, piece.Up), piece.Forward), 1e-10, "proper radial basis");
                Assert.True(world.Monuments.At(piece.Up, CliffPlacements.MaximumExtent) == null, "formation leaves monument pads clear");
                Assert.True(CliffGeometry.All[piece.Model].Positions.All(p => p.Length * piece.Scale <= CliffPlacements.MaximumExtent), "bound contains every shared collision vertex");
            }
            CliffPlacement edge = formation[^1];
            var near = world.Cliffs.Near(edge.Position, 1);
            Assert.True(near.Contains(edge), "a neighbour crossing its source cell is discoverable by collision/render streaming");
        }
        Fixtures.Report($"{groups} checked connected formations, {pieces} shared render/collision pieces, {tiered} tiered faces");
        Assert.True(groups >= 12, "connected formations are distributed through the actual Earth");
        Assert.True(tiered >= 12, "actual Earth cliff groups have descending rock coverage");
    }
}
