using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationContactLoopPackageTests
{
    [Test]
    public static void FormationContactLoopPackageLoadsOptionalAuthenticatedBindings()
    {
        foreach (bool loops in new[] { false, true })
        {
            using var fixture = new LoopPackageFixture(loops);
            CliffSupportDefinition definition = fixture.Load().Catalog.Qualified[0];
            Assert.Equal(loops ? 1 : 0, definition.ContactLoops.Length, "omitted loop metadata retains legacy schema behavior");
            foreach (CliffLodBinding lod in definition.Lods)
                Assert.Equal(loops ? 1 : 0, lod.ContactLoops.Length, "every decoded LOD retains its optional loop binding");
            if (loops)
                Assert.True(definition.RequireContactLoop("ring").VertexIndices.SequenceEqual(new[] { 0, 1, 2, 3 }), "the physical loop uses authenticated physical ordinals");
        }
    }

    [Test]
    public static void FormationContactLoopPackageRejectsMissingMismatchedAndMalformedBindings()
    {
        foreach (Action<JsonNode> mutate in new Action<JsonNode>[]
        {
            d => d["lods"]![0]!.AsObject().Remove("contactLoops"),
            d => d["lods"]![0]!["contactLoops"]![0]!["loopKey"] = "unknown",
            d => d["lods"]![0]!["contactLoops"]![0]!["vertices"] = JsonSerializer.SerializeToNode(new[] { 1, 2, 3, 0 }),
            d => d["contactLoops"]![0]!["vertices"] = JsonSerializer.SerializeToNode(new[] { 1, 2, 3 }),
            d => d["contactLoops"]![0]!["vertices"] = JsonSerializer.SerializeToNode(new[] { 0, 1, 2, 0 }),
            d => d["contactLoops"]![0]!["vertices"] = JsonSerializer.SerializeToNode(Enumerable.Range(0, 513).ToArray()),
            d => d["contactLoops"] = null,
            d => d["lods"]![0]!["contactLoops"] = "not-an-array",
        })
        {
            using var fixture = new LoopPackageFixture(true);
            mutate(fixture.Definition); fixture.Repin();
            Reject(() => fixture.Load());
        }
    }

    [Test]
    public static void FormationContactLoopPackageMetadataRemainsCoveredByReceiptDigest()
    {
        using var fixture = new LoopPackageFixture(true);
        fixture.Definition["contactLoops"]![0]!["key"] = "tampered";
        fixture.WriteSidecar();
        Reject(() => fixture.Load());
    }

    [Test]
    public static void FormationContactLoopPackageLoadsAndAuthenticatesExplicitActualEdges()
    {
        using var fixture = new LoopPackageFixture(true);
        foreach (JsonNode? lod in fixture.Definition["lods"]!.AsArray())
            lod!["contactLoops"]![0]!["edgeVertices"] = JsonSerializer.SerializeToNode(new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
        fixture.Repin();
        foreach (CliffLodBinding lod in fixture.Load().Catalog.Qualified[0].Lods)
            Assert.True(lod.ContactLoops[0].EdgeVertexIndices.SequenceEqual(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }), "explicit actual edges roundtrip through pinned sidecar");
        fixture.Definition["lods"]![0]!["contactLoops"]![0]!["edgeVertices"]![0] = 1;
        fixture.WriteSidecar();
        Reject(() => fixture.Load());
        fixture.Repin();
        Reject(() => fixture.Load());
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (FormationValidationException) { return; }
        catch (FormationCapacityException) { return; }
        throw new TestFailure("Expected explicit package loop validation or capacity failure.");
    }

    private sealed class LoopPackageFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "hollow-loop-package-" + Guid.NewGuid().ToString("N"));
        private readonly List<string> _outputs = new();
        private readonly JsonNode _sidecar;
        private readonly JsonNode _receipt;
        private string _receiptDigest = "";
        private string Output => Path.Combine(_root, "package");
        private string Source => Path.Combine(_root, "source");
        public JsonNode Definition => _sidecar["definitions"]![0]!;

        public LoopPackageFixture(bool loops)
        {
            Directory.CreateDirectory(Output); Directory.CreateDirectory(Source);
            float[][] positions = { new[] { 0f, 0f, 0f }, new[] { 0f, 0f, 2f }, new[] { 2f, 0f, 2f }, new[] { 2f, 0f, 0f } };
            byte[] buffer;
            using (var memory = new MemoryStream())
            {
                using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    foreach (float[] vertex in positions) foreach (float value in vertex) writer.Write(value);
                    foreach (ushort index in new ushort[] { 0, 1, 2, 0, 2, 3 }) writer.Write(index);
                }
                buffer = memory.ToArray();
            }
            byte[] Gltf(string file) => JsonSerializer.SerializeToUtf8Bytes(new
            {
                asset = new { version = "2.0" }, scene = 0, scenes = new[] { new { nodes = new[] { 0 } } }, nodes = new[] { new { mesh = 0 } },
                meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 }, indices = 1, mode = 4 } } } },
                buffers = new[] { new { uri = file, byteLength = buffer.Length } },
                bufferViews = new[] { new { buffer = 0, byteOffset = 0, byteLength = 48 }, new { buffer = 0, byteOffset = 48, byteLength = 12 } },
                accessors = new[] { new { bufferView = 0, componentType = 5126, count = 4, type = "VEC3" }, new { bufferView = 1, componentType = 5123, count = 6, type = "SCALAR" } },
            });
            File.WriteAllBytes(Path.Combine(Source, "source.bin"), buffer);
            File.WriteAllBytes(Path.Combine(Source, "source.gltf"), Gltf("source.bin"));
            object source = new { file = "source.gltf", sha256 = Hash(Path.Combine(Source, "source.gltf")),
                dependencies = new[] { new { file = "source.bin", sha256 = Hash(Path.Combine(Source, "source.bin")) } } };
            File.WriteAllBytes(Path.Combine(Source, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(new { assets = new[] { new { outputs = new[] { source } } } }));
            foreach (string key in new[] { "near", "far" })
            { WriteOutput(key + ".gltf", Gltf(key + ".bin")); WriteOutput(key + ".bin", buffer); }
            object physical = new { vertices = positions, triangles = new[] { new[] { 0, 1, 2, 0 }, new[] { 0, 2, 3, 0 } },
                patches = new[] { new { key = "bare", substrateProfileKey = "rock", capabilities = Array.Empty<string>() } } };
            WriteOutput("physical.json", JsonSerializer.SerializeToUtf8Bytes(physical));
            object Lod(string key) => new { key, assetPath = key + ".gltf", contentSha256 = Hash(Path.Combine(Output, key + ".gltf")),
                maximumPositionErrorMetres = 0, minimumNormalDot = 1, triangleMaps = Array.Empty<int[]>() };
            _sidecar = JsonSerializer.SerializeToNode(new
            {
                schemaVersion = 1, definitions = new[] { new
                {
                    key = "fixture", revision = 1, familyKey = "rock", sourceSha256 = Hash(Path.Combine(Source, "source.gltf")),
                    physicalSha256 = Hash(Path.Combine(Output, "physical.json")), units = "metres", basis = "right-handed-y-up", minimumScale = 1, maximumScale = 1,
                    physical, domains = new[] { new { patchKey = "bare", boundaries = new[] { new[] { 0, 1, 2, 3 } } } }, contacts = Array.Empty<object>(), lods = new[] { Lod("near"), Lod("far") },
                } },
                profiles = new[] { new { key = "rock", bedrock = 1, looseStone = 0, soil = 0, soilDepthMetres = 0, plantSuitability = 0,
                    grassCoverage = 0, shrubsPerSquareMetre = 0, treesPerSquareMetre = 0, litter = 0, minimumTemperatureC = 0, maximumTemperatureC = 40, minimumSoilUpDot = .6 } },
                recipes = Array.Empty<object>(),
            })!;
            if (loops)
            {
                Definition["contactLoops"] = JsonSerializer.SerializeToNode(new[] { new { key = "ring", vertices = new[] { 0, 1, 2, 3 } } });
                foreach (JsonNode? lod in Definition["lods"]!.AsArray())
                    lod!["contactLoops"] = JsonSerializer.SerializeToNode(new[] { new { loopKey = "ring", vertices = new[] { 0, 1, 2, 3 } } });
            }
            _receipt = JsonSerializer.SerializeToNode(new { schemaVersion = 1, source,
                manifest = new { file = "manifest.json", sha256 = Hash(Path.Combine(Source, "manifest.json")) }, outputFiles = Array.Empty<object>() })!;
            Repin();
        }
        private void WriteOutput(string name, byte[] bytes)
        {
            File.WriteAllBytes(Path.Combine(Output, name), bytes);
            if (!_outputs.Contains(name)) _outputs.Add(name);
        }
        public void WriteSidecar() => WriteOutput("sidecar.json", JsonSerializer.SerializeToUtf8Bytes(_sidecar));
        public void Repin()
        {
            WriteSidecar();
            _receipt["outputFiles"] = JsonSerializer.SerializeToNode(_outputs.Select(name => new { file = name,
                sha256 = Hash(Path.Combine(Output, name)), bytes = new FileInfo(Path.Combine(Output, name)).Length }).ToArray());
            string receipt = Path.Combine(Output, "qualification-receipt.json");
            File.WriteAllBytes(receipt, JsonSerializer.SerializeToUtf8Bytes(_receipt)); _receiptDigest = Hash(receipt);
        }
        public FormationPackage Load() => FormationPackageLoader.Load(Output, Source, _receiptDigest);
        private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
