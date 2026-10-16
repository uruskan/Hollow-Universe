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
    public static void CliffChainsHaveDistinctStablePartsAndStreamAcrossCellEdges()
    {
        var (system, earth) = StartingWorld.Find();
        WalkingWorld world = WalkingWorld.For(system, earth, 1200);
        var independent = new CliffPlacements(world, WalkingWorld.SeedFor(Hash64.Of(system.Address.Value, earth.Path)));
        int groups = 0, pieces = 0, tiered = 0;
        for (long i = 0; i < TileKey.CountAt(world.Cliffs.Level) && groups < 30; i++)
        {
            TileKey tile = TileKey.FromIndex(world.Cliffs.Level, i)!.Value;
            IReadOnlyList<CliffPlacement> formation = world.Cliffs.FormationAt(tile);
            if (formation.Count < 2) continue;
            groups++;
            Assert.True(formation.SequenceEqual(independent.FormationAt(tile)), "independent generation agrees on every member, scale and transform");
            Assert.Equal(formation.Count, formation.Select(p => p.Key).Distinct().Count(), "streaming never aliases neighbours under one tile ID");
            Assert.True(formation.Count <= 5 * CliffPlacements.MaximumTiers, "bounded columns and descending rows");
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
        Assert.True(groups >= 12, "connected formations are distributed through the actual Earth");
        Assert.True(tiered >= 12, "actual Earth cliff groups have descending rock coverage");
        Fixtures.Report($"{groups} checked connected formations, {pieces} shared render/collision pieces, {tiered} tiered faces");
    }
}
