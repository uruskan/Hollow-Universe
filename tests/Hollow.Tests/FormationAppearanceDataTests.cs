using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationAppearanceDataTests
{
    [Test]
    public static void AppearanceMasksBindEveryPrimitiveAndReturnImmutableCopyOnlyStreams()
    {
        using var f = new Fixture();
        FormationAppearanceData data = f.Load();
        Assert.Equal(2, data.Lods.Count, "both LODs published together");
        Assert.Equal(f.Visual.ReceiptSha256, data.VisualReceiptSha256, "qualified visual binding retained");
        foreach (FormationAppearanceLod lod in data.Lods)
        {
            Assert.Equal(2, lod.Primitives.Count, "material seam primitives retained");
            foreach (FormationAppearancePrimitive p in lod.Primitives)
            {
                Assert.Equal(3, p.VertexCount, "one mask per original visual vertex");
                var copy = new float[12]; p.Values.CopyTo(copy); copy[0] = 99;
                Assert.Equal(.4f, p.Values[0], "caller copy cannot mutate published stream");
            }
        }
        Assert.True(data.EstimatedResidentBytes < FormationAppearanceDataLoader.LoadingReservationBytes, "retained masks fit charged load allowance");
    }

    [Test]
    public static void AppearanceRefusesInsufficientReservationBeforeReadingAnyFile()
    {
        using var f = new Fixture();
        Reject<FormationCapacityException>(() => FormationAppearanceDataLoader.Load(Path.Combine(f.Root, "missing"),
            f.ReceiptSha, f.Pins, f.Visual, FormationAppearanceDataLoader.LoadingReservationBytes - 1));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject<OperationCanceledException>(() => f.Load(cancelled.Token));
    }

    [Test]
    public static void AppearanceMissingRootUsesValidationFailureAndCancellationPrecedesPathIo()
    {
        using var f = new Fixture();
        string missing = Path.Combine(f.Root, "missing");
        Reject<FormationValidationException>(() => FormationAppearanceDataLoader.Load(missing, f.ReceiptSha,
            f.Pins, f.Visual, FormationAppearanceDataLoader.LoadingReservationBytes));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject<OperationCanceledException>(() => FormationAppearanceDataLoader.Load(missing, f.ReceiptSha,
            f.Pins, f.Visual, FormationAppearanceDataLoader.LoadingReservationBytes, cancelled.Token));
    }

    [Test]
    public static void AppearanceRejectsCaseAliasedFilePinsEvenWhenBothPayloadsAuthenticate()
    {
        using var f = new Fixture();
        const string alias = "NEAR-appearance.bin";
        // The fixture's two mask payloads are equal. On case-sensitive hosts this
        // makes an actual second file; on Windows it rewrites the same exact bytes.
        File.WriteAllBytes(f.PathOf(alias), File.ReadAllBytes(f.PathOf("near-appearance.bin")));
        f.ChangeMetadata(m => m["dataFile"] = alias, "far-appearance.json");
        foreach (JsonNode? output in f.Receipt["outputs"]!.AsArray())
        {
            JsonObject row = output!.AsObject();
            if (row["file"]!.GetValue<string>() == "far-appearance.bin") row["file"] = alias;
        }
        f.Receipt["lods"]![1]!["dataFile"] = alias;
        f.WriteReceipt();
        Reject<FormationValidationException>(() => f.Load());
    }

    [Test]
    public static void AppearanceRefusesIndependentlyMismatchedPinsAndUnpinnedBytes()
    {
        using var f = new Fixture();
        foreach (FormationAppearancePins pins in new[] { f.Pins with { ModuleReceiptSha256 = new('f', 64) },
                     f.Pins with { TangentReceiptSha256 = new('f', 64) }, f.Pins with { RecipeSha256 = new('f', 64) } })
            Reject<FormationValidationException>(() => FormationAppearanceDataLoader.Load(f.Root, f.ReceiptSha, pins, f.Visual,
                FormationAppearanceDataLoader.LoadingReservationBytes));
        byte[] bytes = File.ReadAllBytes(f.PathOf("near-appearance.bin")); bytes[0] ^= 1;
        File.WriteAllBytes(f.PathOf("near-appearance.bin"), bytes);
        Reject<FormationValidationException>(() => f.Load());
    }

    [Test]
    public static void AppearanceRefusesDifferentPositionNormalUvTangentOrCornerOrder()
    {
        foreach (string stream in new[] { "position", "normal", "uv", "tangent", "index" })
        {
            using var f = new Fixture();
            FormationVisualPrimitive p = f.Visual.Lods[0].Primitives[0];
            float[] positions = Copy(p.Positions), normals = Copy(p.Normals), uv = Copy(p.TexCoords), tangents = Copy(p.Tangents);
            int[] indices = Copy(p.Indices);
            switch (stream)
            {
                case "position": positions[0] += .01f; break;
                case "normal": normals[0] = .01f; break;
                case "uv": uv[0] += .01f; break;
                case "tangent": tangents[3] = -1; break;
                case "index": (indices[0], indices[1]) = (indices[1], indices[0]); break;
            }
            var changed = new FormationVisualPrimitive(0, 0, 0, 0, positions, normals, tangents, uv, indices);
            FormationVisualLod first = f.Visual.Lods[0];
            var lod = new FormationVisualLod(first.GeometryKey, first.GeometryRevision, first.LodKey, first.AssetPath,
                new[] { changed, first.Primitives[1] }, Array.Empty<FormationVisualMaterial>());
            var visual = new FormationVisualData(f.Visual.ReceiptSha256, default, new[] { lod, f.Visual.Lods[1] }, Array.Empty<FormationVisualImage>());
            Reject<FormationValidationException>(() => FormationAppearanceDataLoader.Load(f.Root, f.ReceiptSha, f.Pins, visual,
                FormationAppearanceDataLoader.LoadingReservationBytes));
        }
    }

    [Test]
    public static void AppearanceRefusesAuthenticatedNonfiniteOutOfRangeOrOverlappingWeights()
    {
        foreach (float[] mask in new[] { new[] { float.NaN, 0f, 0f, 0f }, new[] { -.1f, 0f, 0f, 0f },
                     new[] { 1.01f, 0f, 0f, 0f }, new[] { 0f, 0f, .75f, .5f } })
        {
            using var f = new Fixture(mask);
            Reject<FormationValidationException>(() => f.Load());
        }
    }

    [Test]
    public static void AppearanceRefusesLayoutAliasesMissingPrimitivesAndScanRoleEscapes()
    {
        Action<JsonObject>[] changes =
        {
            m => m["primitives"]![1]!["byteOffset"] = 0,
            m => m["primitives"]![0]!["vertexCount"] = 60001,
            m => m["primitives"]![0]!["firstTriangle"] = 1,
            m => m["primitives"]![1]!["sourceAtlasSamplingAllowed"] = true,
            m => m["primitives"]![1]!["sourceKind"] = "original-scan",
            m => m["primitives"]!.AsArray().RemoveAt(1),
            m => m["dataFile"] = "../near-appearance.bin",
        };
        foreach (Action<JsonObject> change in changes)
        {
            using var f = new Fixture(); f.ChangeMetadata(change);
            Reject<FormationValidationException>(() => f.Load());
        }
    }

    [Test]
    public static void AppearanceCannotSelfGrantArtOrRootPermissionsAndRejectsDuplicateJsonKeys()
    {
        foreach (string key in new[] { "assetAccepted", "appearanceAccepted", "rootPermissions" })
        {
            using var f = new Fixture(); f.Receipt[key] = true; f.WriteReceipt();
            Reject<FormationValidationException>(() => f.Load());
        }
        using var duplicate = new Fixture();
        string original = File.ReadAllText(duplicate.PathOf("appearance-receipt.json"));
        File.WriteAllText(duplicate.PathOf("appearance-receipt.json"), "{\"schema\":\"duplicate\"," + original[1..]);
        duplicate.ReceiptSha = Digest(File.ReadAllBytes(duplicate.PathOf("appearance-receipt.json")));
        Reject<FormationValidationException>(() => duplicate.Load());
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "hollow-appearance-" + Guid.NewGuid().ToString("N"));
        internal readonly FormationAppearancePins Pins = new(new('a', 64), new('b', 64), new('c', 64));
        internal readonly FormationVisualData Visual;
        internal JsonObject Receipt;
        internal string ReceiptSha = "";
        internal Fixture(float[]? mask = null)
        {
            Directory.CreateDirectory(Root); mask ??= new[] { .4f, .6f, .2f, .3f };
            var lods = new List<FormationVisualLod>(); var outputs = new JsonArray(); var bindings = new JsonArray();
            foreach (string key in new[] { "near", "far" })
            {
                var primitives = new List<FormationVisualPrimitive>(); var rows = new JsonArray();
                using var payload = new MemoryStream();
                for (int part = 0; part < 2; part++)
                {
                    float[] position = { part, 0, 0, part + 1, 0, 0, part, 1, 0 };
                    float[] normal = { 0, 0, 1, 0, 0, 1, 0, 0, 1 }, tangent = { 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1 };
                    float[] uv = { 0, 0, 1, 0, 0, 1 }; int[] index = { 2, 0, 1 };
                    var p = new FormationVisualPrimitive(part, part, part * 3, part, position, normal, tangent, uv, index);
                    primitives.Add(p);
                    byte[] masks = Floats(Enumerable.Range(0, 3).SelectMany(_ => mask)); payload.Write(masks);
                    byte[] attrs = Attributes(p, Enumerable.Range(0, 3)), corners = Attributes(p, index);
                    rows.Add(new JsonObject { ["primitive"] = part, ["sourceKind"] = part == 0 ? "original-scan" : "added-closure",
                        ["material"] = part, ["firstVertex"] = part * 3, ["firstTriangle"] = part, ["vertexCount"] = 3, ["triangleCount"] = 1,
                        ["byteOffset"] = part * 48, ["byteLength"] = 48, ["componentType"] = "float32-le", ["components"] = 4, ["strideBytes"] = 16,
                        ["vertexMaskSha256"] = Digest(masks), ["cornerMaskSha256"] = Digest(index.SelectMany(v => masks.Skip(v * 16).Take(16)).ToArray()),
                        ["orderedVertexPositionNormalUvSha256"] = Digest(attrs), ["orderedLocalIndexSha256"] = Digest(index.SelectMany(BitConverter.GetBytes).ToArray()),
                        ["orderedTangentSha256"] = Digest(Floats(tangent)), ["baseCornerPositionNormalUvSha256"] = Digest(corners),
                        ["sourceAtlasSamplingAllowed"] = part == 0 });
                }
                string dataName = key + "-appearance.bin", metadataName = key + "-appearance.json";
                byte[] data = payload.ToArray(); File.WriteAllBytes(PathOf(dataName), data);
                var metadata = new JsonObject { ["schema"] = "forest-contact-additive-appearance-lod-v1", ["lod"] = key,
                    ["dataFile"] = dataName, ["bytes"] = data.Length, ["sha256"] = Digest(data), ["primitives"] = rows };
                File.WriteAllText(PathOf(metadataName), metadata.ToJsonString());
                outputs.Add(FilePin(dataName)); outputs.Add(FilePin(metadataName));
                bindings.Add(new JsonObject { ["key"] = key, ["metadataFile"] = metadataName, ["dataFile"] = dataName });
                lods.Add(new FormationVisualLod("fixture", 1, key, key + ".gltf", primitives.ToArray(), Array.Empty<FormationVisualMaterial>()));
            }
            Visual = new(new('d', 64), default, lods.ToArray(), Array.Empty<FormationVisualImage>());
            Receipt = new JsonObject { ["schema"] = "forest-contact-additive-appearance-v1", ["status"] = "unadmitted-additive-appearance",
                ["assetAccepted"] = false, ["appearanceAccepted"] = false, ["rootPermissions"] = false, ["capabilities"] = new JsonArray(),
                ["baseModuleReceiptSha256"] = Pins.ModuleReceiptSha256, ["tangentReceiptSha256"] = Pins.TangentReceiptSha256,
                ["recipeSha256"] = Pins.RecipeSha256, ["outputs"] = outputs, ["lods"] = bindings };
            WriteReceipt();
        }
        internal string PathOf(string file) => Path.Combine(Root, file);
        private JsonObject FilePin(string file)
        { byte[] b = File.ReadAllBytes(PathOf(file)); return new() { ["file"] = file, ["bytes"] = b.Length, ["sha256"] = Digest(b) }; }
        internal void ChangeMetadata(Action<JsonObject> change, string file = "near-appearance.json")
        {
            JsonObject metadata = JsonNode.Parse(File.ReadAllText(PathOf(file)))!.AsObject(); change(metadata);
            File.WriteAllText(PathOf(file), metadata.ToJsonString());
            JsonArray rows = Receipt["outputs"]!.AsArray();
            for (int i = 0; i < rows.Count; i++) if (rows[i]!["file"]!.GetValue<string>() == file) rows[i] = FilePin(file);
            WriteReceipt();
        }
        internal void WriteReceipt()
        { File.WriteAllText(PathOf("appearance-receipt.json"), Receipt.ToJsonString()); ReceiptSha = Digest(File.ReadAllBytes(PathOf("appearance-receipt.json"))); }
        internal FormationAppearanceData Load(CancellationToken cancellation = default) => FormationAppearanceDataLoader.Load(Root,
            ReceiptSha, Pins, Visual, FormationAppearanceDataLoader.LoadingReservationBytes, cancellation);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
    private static T[] Copy<T>(FormationVisualArray<T> values) { var result = new T[values.Count]; values.CopyTo(result); return result; }
    private static byte[] Attributes(FormationVisualPrimitive p, IEnumerable<int> order) => Floats(order.SelectMany(v =>
        Enumerable.Range(0, 3).Select(c => p.Positions[v * 3 + c]).Concat(Enumerable.Range(0, 3).Select(c => p.Normals[v * 3 + c]))
            .Concat(Enumerable.Range(0, 2).Select(c => p.TexCoords[v * 2 + c]))));
    private static byte[] Floats(IEnumerable<float> values)
    { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); foreach (float v in values) writer.Write(v); return stream.ToArray(); }
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
