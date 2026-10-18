using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationPackageLoaderTests
{
    [Test]
    public static void PackageAuthenticatesActualSourcePhysicalAndBothLodBuffers()
    {
        using var fixture = new PackageFixture();
        FormationPackage package = fixture.Load();
        Assert.Equal(1, package.Catalog.Qualified.Length, "one bounded qualified geometry");
        CliffSupportDefinition definition = package.Catalog.Qualified[0];
        Assert.Equal(2, definition.Lods.Length, "both decoded render LODs are checked");
        Assert.Equal(2, definition.Geometry.TriangleCount, "physical triangle order retained");
        Assert.Equal(0, package.Catalog.Recipes.Length, "package is not generation activation");
        Assert.Equal(fixture.Digest, package.ReceiptSha256, "receipt is authenticated from independent caller pin");
        Assert.True(definition.Geometry.Vertices.SequenceEqual(definition.Lods[0].Mesh.Vertices), "actual decoded corner coordinates agree");
        Assert.Equal(2, package.Visuals.Count, "both LOD paths survive admission for the renderer");
        foreach (FormationVisualAsset visual in package.Visuals)
        {
            Assert.Equal(definition.Key, visual.GeometryKey, "binding retains qualified geometry identity");
            Assert.Equal(definition.Revision, visual.GeometryRevision, "binding retains geometry revision");
            FormationPackageFile file = package.Files.Single(f => f.Path == visual.AssetPath);
            Assert.Equal(file.Sha256, visual.ContentSha256, "render document binding retains its authenticated bytes");
            Assert.Equal(PackageFixture.Hash(Path.Combine(fixture.Output, file.Path)), file.Sha256, "actual bytes match binding");
        }
        Assert.True(package.Files.Any(f => f.Path == "rock.png") && package.Files.Any(f => f.Path == "near.bin"),
            "renderer retains texture and buffer dependency pins without loading their pixels");
        Assert.True(package.EstimatedBindingBytes > 0, "caller can reserve the retained presentation metadata");
        Assert.True(package.Files is not FormationPackageFile[] && package.Visuals is not FormationVisualAsset[],
            "published visual bindings do not expose mutable backing arrays");
    }

    [Test]
    public static void PackageRejectsChangedFilesIncludingNonDecodedTextures()
    {
        foreach (string path in new[] { "package/physical.json", "package/near.bin", "package/far.bin", "package/rock.png", "source/source.bin", "source/rock.png", "source/manifest.json" })
        {
            using var fixture = new PackageFixture();
            string file = Path.Combine(fixture.Root, path);
            byte[] bytes = File.ReadAllBytes(file); bytes[^1] ^= 1; File.WriteAllBytes(file, bytes);
            Reject(() => fixture.Load());
        }
    }

    [Test]
    public static void PackageChecksEmbeddedPhysicalAgainstSeparateAuthenticatedBytes()
    {
        using var fixture = new PackageFixture();
        fixture.Sidecar["definitions"]![0]!["physical"]!["vertices"]![0]![0] = 99;
        fixture.Repin();
        Reject(() => fixture.Load());
    }

    [Test]
    public static void PackageRequiresDeclaredLodAndPhysicalHashesEvenWithValidReceipt()
    {
        foreach (bool physical in new[] { true, false })
        {
            using var fixture = new PackageFixture();
            JsonNode definition = fixture.Sidecar["definitions"]![0]!;
            if (physical) definition["physicalSha256"] = new string('0', 64);
            else definition["lods"]![0]!["contentSha256"] = new string('0', 64);
            fixture.Repin(); Reject(() => fixture.Load());
        }
    }

    [Test]
    public static void PackageRejectsUnlistedBufferAndEscapingReceiptPaths()
    {
        using (var fixture = new PackageFixture())
        {
            JsonNode near = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Output, "near.gltf")))!;
            near["buffers"]![0]!["uri"] = "unlisted.bin";
            fixture.WriteOutput("near.gltf", JsonSerializer.SerializeToUtf8Bytes(near));
            fixture.Sidecar["definitions"]![0]!["lods"]![0]!["contentSha256"] = PackageFixture.Hash(Path.Combine(fixture.Output, "near.gltf"));
            fixture.Repin(); Reject(() => fixture.Load());
        }
        foreach (string path in new[] { "../physical.json", "C:/physical.json", "a\\physical.json", "/physical.json", "./physical.json", "a//b" })
        {
            using var fixture = new PackageFixture();
            fixture.Receipt["outputFiles"]![0]!["file"] = path;
            fixture.WriteReceipt(); Reject(() => fixture.Load());
        }
    }

    [Test]
    public static void PackageEnforcesAdmissionBeforeDecodingAndDoesNotAcceptRecipes()
    {
        using var fixture = new PackageFixture();
        Reject(() => fixture.Load(new FormationPackageLimits { MaxFiles = 1 }));
        Reject(() => fixture.Load(new FormationPackageLimits { MaxFileBytes = 16 }));
        Reject(() => fixture.Load(new FormationPackageLimits { MaxTotalFileBytes = 32 }));
        Reject(() => fixture.Load(new FormationPackageLimits { MaxJsonBytes = 32 }));
        fixture.Sidecar["recipes"]!.AsArray().Add(new JsonObject { ["key"] = "unauthorized-recipe" });
        fixture.Repin(); Reject(() => fixture.Load());
    }

    [Test]
    public static void PackageStillPerformsGeometryQualificationAfterAuthentication()
    {
        foreach (string fault in new[] { "units", "mapping", "capability", "boundary" })
        {
            using var fixture = new PackageFixture();
            JsonNode definition = fixture.Sidecar["definitions"]![0]!;
            switch (fault)
            {
                case "units": definition["units"] = "centimetres"; break;
                case "mapping": definition["lods"]![0]!["triangleMaps"] = new JsonArray(); break;
                case "capability":
                    definition["physical"]!["patches"]![0]!["capabilities"]![0] = "GuessFromNormal";
                    fixture.WriteOutput("physical.json", JsonSerializer.SerializeToUtf8Bytes(definition["physical"]));
                    definition["physicalSha256"] = PackageFixture.Hash(Path.Combine(fixture.Output, "physical.json"));
                    break;
                case "boundary": definition["domains"]![0]!["boundaries"]![0] = JsonSerializer.SerializeToNode(new[] { 0, 2, 1, 3 }); break;
            }
            fixture.Repin(); Reject(() => fixture.Load());
        }
    }

    [Test]
    public static void PackageRequiresReferencedImagesAndEverySourceDependency()
    {
        foreach (string uri in new[] { "unlisted.png", "../rock.png", "%2e%2e/rock.png" })
        {
            using var fixture = new PackageFixture();
            JsonNode near = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Output, "near.gltf")))!;
            near["images"]![0]!["uri"] = uri;
            fixture.WriteOutput("near.gltf", JsonSerializer.SerializeToUtf8Bytes(near));
            fixture.Sidecar["definitions"]![0]!["lods"]![0]!["contentSha256"] = PackageFixture.Hash(Path.Combine(fixture.Output, "near.gltf"));
            fixture.Repin(); Reject(() => fixture.Load());
        }
        using (var fixture = new PackageFixture())
        {
            fixture.Receipt["source"]!["dependencies"]!.AsArray().RemoveAt(1);
            fixture.WriteReceipt(); Reject(() => fixture.Load());
        }
    }

    [Test]
    public static void PackageReconcilesManifestAndAccountsForItsBytes()
    {
        using (var fixture = new PackageFixture())
        {
            JsonNode manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Source, "manifest.json")))!;
            manifest["assets"]![0]!["outputs"]![0]!["sha256"] = new string('f', 64);
            File.WriteAllBytes(Path.Combine(fixture.Source, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest));
            fixture.Receipt["manifest"]!["sha256"] = PackageFixture.Hash(Path.Combine(fixture.Source, "manifest.json"));
            fixture.WriteReceipt(); Reject(() => fixture.Load());
        }
        using (var fixture = new PackageFixture())
        {
            long allBytes = Directory.EnumerateFiles(fixture.Root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
            long manifestBytes = new FileInfo(Path.Combine(fixture.Source, "manifest.json")).Length;
            Reject(() => fixture.Load(new FormationPackageLimits { MaxTotalFileBytes = allBytes - manifestBytes / 2 }));
        }
    }

    [Test]
    public static void PackageMalformedRequiredFieldsAreExplicitValidationFailures()
    {
        foreach (bool missing in new[] { true, false })
        {
            using var fixture = new PackageFixture();
            JsonObject definition = fixture.Sidecar["definitions"]![0]!.AsObject();
            if (missing) definition.Remove("revision"); else definition["revision"] = "not-an-integer";
            fixture.Repin(); Reject(() => fixture.Load());
        }
    }

    [Test]
    public static void PackageAppliesSmallerJsonLimitToSourceAndVisualDocuments()
    {
        foreach (bool original in new[] { true, false })
        {
            using var fixture = new PackageFixture();
            string folder = original ? fixture.Source : fixture.Output;
            string file = original ? "source.gltf" : "near.gltf";
            JsonNode gltf = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, file)))!;
            gltf["extras"] = new string('x', 20000);
            File.WriteAllBytes(Path.Combine(folder, file), JsonSerializer.SerializeToUtf8Bytes(gltf));
            if (original)
            {
                string hash = PackageFixture.Hash(Path.Combine(folder, file));
                fixture.Receipt["source"]!["sha256"] = hash;
                fixture.Sidecar["definitions"]![0]!["sourceSha256"] = hash;
                JsonNode manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Source, "manifest.json")))!;
                manifest["assets"]![0]!["outputs"]![0]!["sha256"] = hash;
                File.WriteAllBytes(Path.Combine(fixture.Source, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest));
                fixture.Receipt["manifest"]!["sha256"] = PackageFixture.Hash(Path.Combine(fixture.Source, "manifest.json"));
            }
            else fixture.Sidecar["definitions"]![0]!["lods"]![0]!["contentSha256"] = PackageFixture.Hash(Path.Combine(folder, file));
            fixture.Repin(); Reject(() => fixture.Load(new FormationPackageLimits { MaxJsonBytes = 10000 }));
        }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (FormationValidationException) { return; }
        catch (FormationCapacityException) { return; }
        throw new Exception("Expected explicit formation validation/capacity failure.");
    }

    private sealed class PackageFixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "hollow-cliff-package-" + Guid.NewGuid().ToString("N"));
        public string Output => Path.Combine(Root, "package");
        public string Source => Path.Combine(Root, "source");
        public JsonNode Sidecar { get; }
        public JsonNode Receipt { get; }
        public string Digest { get; private set; } = "";
        private readonly List<string> _outputs = new();
        public PackageFixture()
        {
            Directory.CreateDirectory(Output); Directory.CreateDirectory(Source);
            float[][] positions = { new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 2f }, new[] { 2f, 0f, 2f }, new[] { 2f, 0f, 0f } };
            byte[] buffer;
            using (var memory = new MemoryStream())
            {
                using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    foreach (float[] vertex in positions) foreach (float value in vertex) writer.Write(value);
                    foreach (ushort value in new ushort[] { 0, 1, 2, 0, 2, 3 }) writer.Write(value);
                }
                buffer = memory.ToArray();
            }
            byte[] Gltf(string name) => JsonSerializer.SerializeToUtf8Bytes(new
            {
                asset = new { version = "2.0" }, scene = 0, scenes = new[] { new { nodes = new[] { 0 } } }, nodes = new[] { new { mesh = 0 } },
                meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 }, indices = 1, mode = 4 } } } },
                buffers = new[] { new { uri = name, byteLength = buffer.Length } },
                images = new[] { new { uri = "rock.png" } },
                bufferViews = new[] { new { buffer = 0, byteOffset = 0, byteLength = 48 }, new { buffer = 0, byteOffset = 48, byteLength = 12 } },
                accessors = new[] { new { bufferView = 0, componentType = 5126, count = 4, type = "VEC3" }, new { bufferView = 1, componentType = 5123, count = 6, type = "SCALAR" } },
            });
            File.WriteAllBytes(Path.Combine(Source, "source.bin"), buffer);
            File.WriteAllBytes(Path.Combine(Source, "source.gltf"), Gltf("source.bin"));
            File.WriteAllBytes(Path.Combine(Source, "rock.png"), new byte[] { 1, 2, 3, 4 });
            object source = new { file = "source.gltf", sha256 = Hash(Path.Combine(Source, "source.gltf")), dependencies = new[]
                { new { file = "source.bin", sha256 = Hash(Path.Combine(Source, "source.bin")) }, new { file = "rock.png", sha256 = Hash(Path.Combine(Source, "rock.png")) } } };
            File.WriteAllBytes(Path.Combine(Source, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(new { assets = new[] { new { key = "synthetic", outputs = new[] { source } } } }));
            WriteOutput("near.gltf", Gltf("near.bin")); WriteOutput("near.bin", buffer);
            WriteOutput("far.gltf", Gltf("far.bin")); WriteOutput("far.bin", buffer);
            WriteOutput("rock.png", new byte[] { 1, 2, 3, 4 });
            object physical = new
            {
                vertices = positions, triangles = new[] { new[] { 0, 1, 2, 0 }, new[] { 0, 2, 3, 0 } },
                patches = new[] { new { key = "crown", substrateProfileKey = "soil", capabilities = new[] { "Soil", "Plantable" } } },
            };
            WriteOutput("physical.json", JsonSerializer.SerializeToUtf8Bytes(physical));
            object Lod(string key) => new
            {
                key, assetPath = key + ".gltf", contentSha256 = Hash(Path.Combine(Output, key + ".gltf")), maximumPositionErrorMetres = 0,
                minimumNormalDot = 0.999999, triangleMaps = new[] { new[] { 0, 0 }, new[] { 1, 1 } },
            };
            Sidecar = JsonSerializer.SerializeToNode(new
            {
                schemaVersion = 1, definitions = new[] { new
                {
                    key = "synthetic", revision = 1, familyKey = "synthetic-rock", sourceSha256 = Hash(Path.Combine(Source, "source.gltf")),
                    physicalSha256 = Hash(Path.Combine(Output, "physical.json")), units = "metres", basis = "right-handed-y-up", minimumScale = 1, maximumScale = 1,
                    physical, domains = new[] { new { patchKey = "crown", boundaries = new[] { new[] { 0, 1, 2, 3 } } } }, contacts = System.Array.Empty<object>(),
                    lods = new[] { Lod("near"), Lod("far") },
                } },
                profiles = new[] { new { key = "soil", bedrock = 0, looseStone = 0, soil = 1, soilDepthMetres = .1, plantSuitability = 1,
                    grassCoverage = 1, shrubsPerSquareMetre = 0, treesPerSquareMetre = 0, litter = 0, minimumTemperatureC = 0, maximumTemperatureC = 40, minimumSoilUpDot = .6 } },
                recipes = System.Array.Empty<object>(),
            })!;
            Receipt = JsonSerializer.SerializeToNode(new
            {
                schemaVersion = 1,
                source,
                manifest = new { file = "manifest.json", sha256 = Hash(Path.Combine(Source, "manifest.json")) }, outputFiles = System.Array.Empty<object>(),
            })!;
            Repin();
        }
        public void WriteOutput(string name, byte[] data)
        { File.WriteAllBytes(Path.Combine(Output, name), data); if (!_outputs.Contains(name)) _outputs.Add(name); }
        public void Repin()
        {
            WriteOutput("sidecar.json", JsonSerializer.SerializeToUtf8Bytes(Sidecar));
            Receipt["outputFiles"] = JsonSerializer.SerializeToNode(_outputs.Select(name => new
                { file = name, sha256 = Hash(Path.Combine(Output, name)), bytes = new FileInfo(Path.Combine(Output, name)).Length }).ToArray());
            WriteReceipt();
        }
        public void WriteReceipt()
        { File.WriteAllBytes(Path.Combine(Output, "qualification-receipt.json"), JsonSerializer.SerializeToUtf8Bytes(Receipt)); Digest = Hash(Path.Combine(Output, "qualification-receipt.json")); }
        public FormationPackage Load(FormationPackageLimits? limits = null) => FormationPackageLoader.Load(Output, Source, Digest, limits);
        public static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
