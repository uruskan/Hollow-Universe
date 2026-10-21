using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FloraAppearanceTests
{
    private const int HostedEarthSeed = 1819634169;
    private const long CompletePayloadBytes = 87381292;
    private static readonly FloraAppearanceColour Mean = new(0.125f, 0.25f, 0.375f);

    [Test]
    public static void FloraAppearanceSchemaPinsSevenCompleteRgbaMipChains()
    {
        (string Role, int Width, int Height, int Levels, int Bytes)[] expected =
        {
            ("near-atlas", 2048, 2048, 12, 22369620),
            ("bark", 512, 512, 10, 1398100), ("bark-normal", 512, 512, 10, 1398100),
            ("birch", 256, 256, 9, 349524), ("birch-normal", 256, 256, 9, 349524),
            ("impostor-colour", 2048, 2816, 12, 30758212),
            ("impostor-normal", 2048, 2816, 12, 30758212),
        };
        Assert.Equal(expected.Length, FloraAppearanceSchema.Images.Count, "seven distinct image roles are required");
        for (int i = 0; i < expected.Length; i++)
        {
            FloraAppearanceImageLayout actual = FloraAppearanceSchema.Images[i];
            var e = expected[i];
            Assert.Equal(e.Role, actual.Role, "image role order");
            Assert.Equal(e.Role + ".rgba8-mips", actual.File, "schema uses a fixed leaf filename");
            Assert.Equal(e.Width, actual.Width, "exact image width");
            Assert.Equal(e.Height, actual.Height, "exact image height");
            Assert.Equal(e.Levels, actual.MipLevels, "level count includes the base image");
            Assert.Equal(e.Bytes, actual.ByteLength, "all mip bytes, including non-square tail levels");
            Assert.Equal("RGBA8", actual.Format, "raw four-channel byte format");
        }
        Assert.Equal(CompletePayloadBytes, FloraAppearanceSchema.Images.Sum(i => (long)i.ByteLength), "complete bundle byte total");
        Assert.Equal(CompletePayloadBytes, FloraAppearanceSchema.PayloadBytes, "published payload reservation");
        Assert.Equal(11, FloraAppearanceSchema.Rows, "legacy tree row count");
        Assert.Equal(8, FloraAppearanceSchema.Views, "azimuth views per row");
        Assert.Equal(256, FloraAppearanceSchema.CellPixels, "pixels per view");
        Assert.Equal(4, FloraAppearanceSchema.NativeRowStart, "four procedural rows precede native trees");
        Assert.Equal(11, FloraAppearanceSchema.NativeRowEnd, "native interval end is exclusive");
    }

    [Test]
    public static void FloraAppearanceRowsRequireExactLegacySpeciesAndVariantOrder()
    {
        FloraAppearanceRow[] rows = Rows();
        ScatterKind[] kinds = { ScatterKind.Broadleaf, ScatterKind.Broadleaf, ScatterKind.Broadleaf, ScatterKind.Broadleaf,
            ScatterKind.Fir, ScatterKind.Fir, ScatterKind.Fir, ScatterKind.Birch, ScatterKind.Birch, ScatterKind.Birch, ScatterKind.Pine };
        int[] variants = { 0, 1, 2, 3, 0, 1, 2, 0, 1, 2, 0 };
        for (int i = 0; i < rows.Length; i++)
        {
            Assert.Equal(kinds[i], rows[i].Kind, "species at each legacy row");
            Assert.Equal(variants[i], rows[i].Variant, "variant ordinal at each legacy row");
            Assert.Equal(i >= 4, rows[i].Native, "native material interpretation follows the admitted interval");
        }
        Reject<FloraAppearanceValidationException>(() => Row(0, ScatterKind.Fir, 0));
        Reject<FloraAppearanceValidationException>(() => Row(3, ScatterKind.Broadleaf, 0));
        Reject<FloraAppearanceValidationException>(() => Row(11, ScatterKind.Pine, 1));
        Reject<FloraAppearanceValidationException>(() => Input(rows: rows[..10]));
        var reordered = (FloraAppearanceRow[])rows.Clone();
        (reordered[0], reordered[1]) = (reordered[1], reordered[0]);
        Reject<FloraAppearanceValidationException>(() => Input(rows: reordered));
        var duplicateKey = (FloraAppearanceRow[])rows.Clone();
        duplicateKey[1] = CopyRow(rows[1], key: rows[0].VariantKey);
        Reject<FloraAppearanceValidationException>(() => Input(rows: duplicateKey));
        Reject<FloraAppearanceValidationException>(() => CopyRow(rows[0], height: float.NaN));
        Reject<FloraAppearanceValidationException>(() => CopyRow(rows[0], scale: 0));
        Reject<FloraAppearanceValidationException>(() => CopyRow(rows[0], extent: float.PositiveInfinity));
    }

    [Test]
    public static void FloraAppearanceInputSnapshotsAreImmutableAndCanonical()
    {
        FloraAppearanceDependency[] dependencies = Dependencies();
        FloraAppearanceRow[] rows = Rows();
        FloraAppearanceInputContract input = Input(dependencies: dependencies, rows: rows);
        string fingerprint = input.Fingerprint;
        string firstKey = input.Dependencies[0].Key;
        FloraAppearanceRow first = rows[0];
        dependencies[0] = new FloraAppearanceDependency("replacement", 1, Digest("replacement"));
        rows[0] = CopyRow(rows[0], height: rows[0].Height + 1);
        Assert.Equal(firstKey, input.Dependencies[0].Key, "dependency input array is copied");
        Assert.True(ReferenceEquals(first, input.Rows[0]), "immutable row survives caller array replacement");
        Assert.Equal(fingerprint, input.Fingerprint, "caller mutations cannot stale a stored fingerprint");
        Reject<NotSupportedException>(() => ((IList<FloraAppearanceRow>)input.Rows)[0] = rows[0]);
        Reject<NotSupportedException>(() => ((IList<FloraAppearanceDependency>)input.Dependencies)[0] = dependencies[0]);
        Assert.Equal(fingerprint, Input(dependencies: Dependencies().Reverse().ToArray()).Fingerprint,
            "dependency inventory order does not change canonical identity");
        Assert.Equal(fingerprint, Input().Fingerprint, "separately allocated equal contracts have equal fingerprints");
        Reject<FloraAppearanceValidationException>(() => Input(dependencies: new[] { Dependencies()[0], Dependencies()[0] }));
        Reject<FloraAppearanceValidationException>(() => Input(dependencies: Array.Empty<FloraAppearanceDependency>()));
        Reject<FloraAppearanceValidationException>(() => Input(seed: -1));
    }

    [Test]
    public static void FloraAppearanceManifestAndInputPinsAreIndependent()
    {
        using var fixture = new PackageFixture();
        byte[] original = fixture.ManifestBytes;
        FloraAppearancePin wrongDigest = new(FloraAppearanceCatalog.EarthAppearanceId, 1, Digest("not this manifest"), fixture.Inputs);
        FloraAppearanceValidationException digestFailure = Reject<FloraAppearanceValidationException>(() => fixture.Load(wrongDigest));
        Assert.True(digestFailure.Message.Contains("SHA-256", StringComparison.Ordinal), "manifest bytes are authenticated before decoding");

        foreach (FloraAppearanceInputContract different in ChangedInputs())
        {
            Assert.True(different.Fingerprint != fixture.Inputs.Fingerprint, "a changed effective input receives another identity");
            // The export is internally self-consistent, and its exact manifest hash is pinned. The independent
            // expected input contract still differs, so no payload read may occur.
            fixture.WriteManifest(Encode(different), trustedInputs: fixture.Inputs);
            RejectMetadata(fixture, "self-declared changed inputs cannot replace trusted inputs");
        }
        fixture.WriteManifest(original);
        FloraAppearancePin wrongId = new("different-appearance", 1, fixture.Pin.ManifestSha256, fixture.Inputs);
        RejectMetadata(fixture, "independent appearance ID is binding", wrongId);
        FloraAppearancePin wrongRevision = new(FloraAppearanceCatalog.EarthAppearanceId, 2, fixture.Pin.ManifestSha256, fixture.Inputs);
        RejectMetadata(fixture, "independent appearance revision is binding", wrongRevision);
        JsonNode forged = JsonNode.Parse(original)!;
        forged["inputFingerprint"] = Digest("invented fingerprint");
        fixture.WriteManifest(JsonSerializer.SerializeToUtf8Bytes(forged));
        RejectMetadata(fixture, "fingerprint text must match the declared canonical inputs too");
        fixture.WriteManifest(original);
        byte[] changedBytes = (byte[])original.Clone();
        changedBytes[^2] ^= 1;
        File.WriteAllBytes(fixture.ManifestPath, changedBytes);
        FloraAppearanceValidationException changedManifest = Reject<FloraAppearanceValidationException>(() => fixture.Load());
        Assert.True(changedManifest.Message.Contains("SHA-256", StringComparison.Ordinal), "same-length changed manifest bytes cannot reuse the old pin");
    }

    [Test]
    public static void FloraAppearanceManifestRejectsUnknownDuplicateAndMissingFields()
    {
        using var fixture = new PackageFixture();
        byte[] original = fixture.ManifestBytes;
        string json = Encoding.UTF8.GetString(original);
        foreach (string duplicate in new[]
        {
            json.Replace("\"complete\":true", "\"complete\":true,\"complete\":true", StringComparison.Ordinal),
            json.Replace("\"recipeSeed\":" + HostedEarthSeed,
                "\"recipeSeed\":" + HostedEarthSeed + ",\"recipeSeed\":" + HostedEarthSeed, StringComparison.Ordinal),
            json.Replace("\"role\":\"bark\"", "\"role\":\"bark\",\"role\":\"bark\"", StringComparison.Ordinal),
        })
        {
            Assert.True(duplicate != json, "duplicate fixture actually changes JSON");
            fixture.WriteManifest(Encoding.UTF8.GetBytes(duplicate));
            RejectMetadata(fixture, "duplicate properties are rejected at every object level");
        }
        foreach (Action<JsonNode> mutate in new Action<JsonNode>[]
        {
            n => n["unrecognised"] = true,
            n => n["inputs"]!["profile"]!["fallback"] = "earth",
            n => n["inputs"]!["dependencies"]![0]!["extra"] = 1,
            n => n["inputs"]!["rows"]![0]!["palette"] = "identity-v1",
            n => n["images"]![0]!["extra"] = 1,
            n => n.AsObject().Remove("complete"),
            n => n["inputs"]!["profile"]!.AsObject().Remove("bakeRevision"),
            n => n["complete"] = false,
            n => n["schema"] = "hollow-flora-appearance-package-v2",
        })
        {
            fixture.Mutate(original, mutate);
            RejectMetadata(fixture, "strict schema fields must be complete and known");
        }
    }

    [Test]
    public static void FloraAppearanceMetadataRejectsCountsOrderingLayoutsAndTraversal()
    {
        using var fixture = new PackageFixture();
        byte[] original = fixture.ManifestBytes;
        foreach (Action<JsonNode> mutate in new Action<JsonNode>[]
        {
            n => n["images"]!.AsArray().RemoveAt(6),
            n => n["images"]!.AsArray().Add(n["images"]![0]!.DeepClone()),
            n => Swap(n["images"]!.AsArray(), 0, 1),
            n => n["images"]![1] = n["images"]![0]!.DeepClone(),
            n => n["inputs"]!["rows"]!.AsArray().RemoveAt(10),
            n => n["inputs"]!["rows"]!.AsArray().Add(n["inputs"]!["rows"]![0]!.DeepClone()),
            n => Swap(n["inputs"]!["rows"]!.AsArray(), 0, 1),
            n => n["inputs"]!["rows"]![0]!["kind"] = 0,
            n => n["inputs"]!["rows"]![0]!["native"] = true,
            n => n["inputs"]!["dependencies"] = new JsonArray(),
            n => n["inputs"]!["dependencies"]!.AsArray().Add(n["inputs"]!["dependencies"]![0]!.DeepClone()),
            n => n["inputs"]!["dependencies"] = new JsonArray(Enumerable.Range(0, 257)
                .Select(_ => n["inputs"]!["dependencies"]![0]!.DeepClone()).ToArray()),
            n => n["inputs"]!["views"] = 7,
            n => n["inputs"]!["nativeRowEnd"] = 10,
            n => n["images"]![0]!["width"] = 4096,
            n => n["images"]![0]!["height"] = -1,
            n => n["images"]![0]!["format"] = "RGBAF",
            n => n["images"]![5]!["mipLevels"] = 11,
            n => n["images"]![5]!["bytes"] = 23068672,
            n => n["images"]![5]!["bytes"] = long.MaxValue,
            n => n["images"]![0]!["mipPolicy"] = "ordinary-v1",
            n => n["images"]![0]!["sha256"] = "not-a-hash",
            n => n["bladeMean"]!.AsArray().RemoveAt(2),
            n => n["bladeMean"]![0] = 1.1,
            n => n["bladeMean"]![0] = 1e100,
        })
        {
            fixture.Mutate(original, mutate);
            RejectMetadata(fixture, "invalid metadata fails without opening image files");
        }
        foreach (string path in new[] { "../outside", "nested/../near-atlas.rgba8-mips", "/outside", "C:/outside",
            "C:outside", "\\\\server\\share\\outside", "near-atlas.rgba8-mips:stream", "near-atlas.rgba8-mips.",
            "NEAR-ATLAS.rgba8-mips", "sub/near-atlas.rgba8-mips", "near-atlas.rgba8-mips " })
        {
            fixture.Mutate(original, n => n["images"]![0]!["file"] = path);
            RejectMetadata(fixture, "only exact fixed payload leaf paths are admissible");
        }
    }

    [Test]
    public static void FloraAppearanceLimitsReserveTheWholeBundleBeforePayloadIo()
    {
        using var fixture = new PackageFixture();
        int manifestLength = fixture.ManifestBytes.Length;
        foreach (FloraAppearanceLimits limits in new[]
        {
            new FloraAppearanceLimits(manifestLength - 1, 30758212, CompletePayloadBytes + manifestLength),
            new FloraAppearanceLimits(manifestLength, 30758211, CompletePayloadBytes + manifestLength),
            new FloraAppearanceLimits(manifestLength, 30758212, CompletePayloadBytes + manifestLength - 1),
        })
            Reject<FloraAppearanceCapacityException>(() => fixture.Load(limits: limits));
        Reject<FloraAppearanceCapacityException>(() => new FloraAppearanceLimits(0, 1, 1));
        Reject<FloraAppearanceCapacityException>(() => new FloraAppearanceLimits(1048577, 1, 1));
        Reject<FloraAppearanceCapacityException>(() => new FloraAppearanceLimits(1, 67108865, 1));
        Reject<FloraAppearanceCapacityException>(() => new FloraAppearanceLimits(1, 1, 268435457));
        // Valid metadata at the exact limits reaches the absent first image; one byte less above did not.
        FloraAppearanceValidationException missing = Reject<FloraAppearanceValidationException>(() => fixture.Load());
        Assert.True(missing.InnerException is IOException, "the exact admission boundary permits payload I/O");
    }

    [Test]
    public static void FloraAppearanceRejectsOversizedInputInventoryAndJsonDepth()
    {
        Reject<FloraAppearanceValidationException>(() => Input(dependencies: Enumerable.Range(0, 257)
            .Select(i => new FloraAppearanceDependency("dependency-" + i, 1, Digest("dependency-" + i))).ToArray()));
        using var fixture = new PackageFixture();
        string nested = new string('[', 20) + "0" + new string(']', 20);
        string json = Encoding.UTF8.GetString(fixture.ManifestBytes);
        fixture.WriteManifest(Encoding.UTF8.GetBytes("{\"nested\":" + nested + "," + json[1..]));
        FloraAppearanceValidationException error = Reject<FloraAppearanceValidationException>(() => fixture.Load());
        Assert.True(error.InnerException is JsonException, "excessive nesting fails in the bounded JSON parser before field or payload processing");
    }

    [Test]
    public static void FloraAppearanceRejectsLinkedPackageRootsWhenSupported()
    {
        using var fixture = new PackageFixture();
        string link = Path.Combine(fixture.Root, "linked-package");
        try { Directory.CreateSymbolicLink(link, fixture.Root); }
        catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        {
            Fixtures.Report("Flora appearance link test skipped: symbolic link creation is unavailable (" + error.GetType().Name + ").");
            return;
        }
        try
        {
            FloraAppearanceValidationException error = Reject<FloraAppearanceValidationException>(() =>
                FloraAppearancePackageLoader.Load(link, fixture.Pin, fixture.ExactLimits));
            Assert.True(error.InnerException == null && error.Message.Contains("links/reparse", StringComparison.Ordinal),
                "the package root itself is rejected as a link before reading its manifest");
        }
        finally { Directory.Delete(link); } // Remove the link itself before the ordinary fixture cleanup.
    }

    [Test]
    public static void FloraAppearanceCancellationWinsBeforeIoAndReturnsNoPackage()
    {
        using var fixture = new PackageFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        FloraAppearancePackage? package = null;
        OperationCanceledException error = Reject<OperationCanceledException>(() =>
            package = FloraAppearancePackageLoader.Load(Path.Combine(fixture.Root, "absent"), fixture.Pin,
                fixture.ExactLimits, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken, "the caller's cancellation is preserved");
        Assert.True(package == null, "cancelled admission publishes no package");
    }

    [Test]
    public static void FloraAppearancePayloadsAreAuthenticatedCompleteAndImmutable()
    {
        // This is the only full-sized disk fixture. All metadata rejection tests write only their manifest.
        using var fixture = new PackageFixture(writeImages: true);
        AssertLoadedPackage(fixture);
        string first = fixture.ImagePath(0);
        WriteByte(first, 0, 255);
        AssertLoadFailsWithoutPackage(fixture, "same-length image corruption");
        WriteByte(first, 0, 1);
        int firstLength = FloraAppearanceSchema.Images[0].ByteLength;
        using (var stream = new FileStream(first, FileMode.Open, FileAccess.Write)) stream.SetLength(firstLength - 1);
        AssertLoadFailsWithoutPackage(fixture, "truncated complete mip chain");
        using (var stream = new FileStream(first, FileMode.Open, FileAccess.Write)) stream.SetLength(firstLength + 1L);
        AssertLoadFailsWithoutPackage(fixture, "trailing byte beyond the complete mip chain");
        using (var stream = new FileStream(first, FileMode.Open, FileAccess.Write)) stream.SetLength(firstLength);
        WriteByte(first, firstLength - 1, 101);
        // Six images can authenticate successfully, but the final corrupt image must prevent publication.
        WriteByte(fixture.ImagePath(6), 0, 255);
        AssertLoadFailsWithoutPackage(fixture, "last image hash failure cannot expose the first six images");
    }

    [Test]
    public static void FloraAppearanceEarthLookupUsesHostedSeedAndNeverFallsBack()
    {
        using var fixture = new PackageFixture();
        FloraAppearanceCatalog catalog = FloraAppearanceCatalog.EarthLegacy(fixture.Pin);
        PlanetEnvironmentDescriptor descriptor = EnvironmentDescriptors.Resolve(StartingWorld.System.Value, StartingWorld.BodyPath, 1200)
            ?? throw new TestFailure("Earth descriptor must resolve without constructing terrain");
        string physicalIdentity = descriptor.CanonicalIdentity;
        ulong surfaceSeed = descriptor.SurfaceSeed, terrainSeed = descriptor.TerrainSeed;
        FloraAppearanceWorldKey world = FloraAppearanceWorldKey.From(descriptor);
        Assert.Equal(0x8000000000000000UL, world.Address.SystemAddress, "built-in Sol address");
        Assert.Equal(0x10300000U, world.Address.BodyPath, "Earth body path");
        Assert.Equal(HostedEarthSeed, FloraAppearanceCatalog.EarthRecipeSeed, "hosted Earth paint seed is fixed");
        Assert.Equal(HostedEarthSeed, (int)(surfaceSeed & 0x7FFFFFFF), "appearance derives from the hosted surface seed");
        Assert.Equal(Hash64.Of(StartingWorld.System.Value, StartingWorld.BodyPath), surfaceSeed, "legacy surface seed mapping");
        Assert.Equal(WalkingWorld.SeedFor(surfaceSeed), terrainSeed, "walking seed remains a separate existing domain");
        FloraAppearanceResolution exact = catalog.Resolve(world, FloraAppearanceCatalog.EarthVisualVersion, catalog.Identity);
        Assert.Equal(FloraAppearanceResolutionStatus.Exact, exact.Status, "the explicitly mapped hosted Earth resolves");
        Assert.True(ReferenceEquals(fixture.Pin, exact.Pin), "exact result carries the approved independent pin");
        Assert.True(ReferenceEquals(FloraAppearancePalette.Identity, exact.Palette), "schema v1 selects the identity palette");
        NoFallback(catalog.Resolve(world, "unrecognised-version", catalog.Identity), FloraAppearanceResolutionStatus.UnsupportedVisualVersion);
        NoFallback(catalog.Resolve(world, catalog.VisualVersion, Digest("another catalog")), FloraAppearanceResolutionStatus.UnsupportedCatalogIdentity);
        foreach (FloraAppearanceWorldKey other in new[]
        {
            world with { UniverseSeed = world.UniverseSeed ^ 1 },
            world with { Address = new WorldAddress(world.Address.SystemAddress, world.Address.BodyPath + 1) },
            world with { SurfaceKind = EnvironmentSurfaceKind.StandIn },
            world with { SurfaceSeed = terrainSeed },
        })
            NoFallback(catalog.Resolve(other, catalog.VisualVersion, catalog.Identity), FloraAppearanceResolutionStatus.UnsupportedWorld);
        Assert.Equal(physicalIdentity, descriptor.CanonicalIdentity, "appearance resolution does not rewrite physical identity");
        Assert.Equal(surfaceSeed, descriptor.SurfaceSeed, "appearance lookup does not reseed placement");
        Assert.Equal(terrainSeed, descriptor.TerrainSeed, "appearance lookup does not reseed terrain");
        Reject<FloraAppearanceValidationException>(() => FloraAppearanceCatalog.EarthLegacy(
            new FloraAppearancePin(FloraAppearanceCatalog.EarthAppearanceId, 1, fixture.Pin.ManifestSha256, Input(seed: HostedEarthSeed + 1))));
    }

    [Test]
    public static void FloraAppearanceCatalogSnapshotsAreStableAndRequireTheirExactIdentity()
    {
        using var fixture = new PackageFixture();
        FloraAppearancePin[] pins = { fixture.Pin, new("other", 1, Digest("other manifest"), Input(seed: 9)) };
        FloraAppearanceWorldKey earth = FloraAppearanceCatalog.EarthWorld;
        FloraAppearanceWorldKey other = earth with { UniverseSeed = earth.UniverseSeed ^ 1 };
        FloraAppearanceWorldBinding[] bindings = { new(earth, fixture.Pin.AppearanceId, "identity-v1"), new(other, "other", "identity-v1") };
        var catalog = new FloraAppearanceCatalog("test-version", pins, bindings);
        var reversed = new FloraAppearanceCatalog("test-version", pins.Reverse().ToArray(), bindings.Reverse().ToArray());
        Assert.Equal(catalog.Identity, reversed.Identity, "catalog identity is independent of input list order");
        string identity = catalog.Identity;
        pins[0] = pins[1]; bindings[0] = bindings[1];
        Assert.Equal(identity, catalog.Identity, "catalog freezes caller lists");
        Assert.True(ReferenceEquals(fixture.Pin, catalog.Resolve(earth, "test-version", identity).Pin), "caller replacement cannot change an existing mapping");
        Reject<NotSupportedException>(() => ((IList<FloraAppearancePin>)catalog.Appearances)[0] = pins[0]);
        Reject<NotSupportedException>(() => ((IList<FloraAppearanceWorldBinding>)catalog.Bindings)[0] = bindings[0]);
        var changed = new FloraAppearanceCatalog("test-version", new[] { fixture.Pin },
            new[] { new FloraAppearanceWorldBinding(other, fixture.Pin.AppearanceId, "identity-v1") });
        Assert.True(changed.Identity != identity, "the same version label cannot hide changed mapping data");
        NoFallback(changed.Resolve(other, "test-version", identity), FloraAppearanceResolutionStatus.UnsupportedCatalogIdentity);
        var differentPin = new FloraAppearancePin(fixture.Pin.AppearanceId, fixture.Pin.Revision, Digest("replacement export"), fixture.Inputs);
        var repinned = new FloraAppearanceCatalog("test-version", new[] { differentPin, reversed.Appearances[1] }, reversed.Bindings);
        Assert.True(repinned.Identity != identity, "the same version label cannot hide a changed external manifest pin");
        NoFallback(repinned.Resolve(earth, "test-version", identity), FloraAppearanceResolutionStatus.UnsupportedCatalogIdentity);
        Reject<FloraAppearanceValidationException>(() => new FloraAppearanceCatalog("test-version", new[] { fixture.Pin, fixture.Pin },
            new[] { new FloraAppearanceWorldBinding(earth, fixture.Pin.AppearanceId, "identity-v1") }));
        Reject<FloraAppearanceValidationException>(() => new FloraAppearanceCatalog("test-version", new[] { fixture.Pin },
            new[] { new FloraAppearanceWorldBinding(earth, "missing", "identity-v1") }));
    }

    [Test]
    public static void FloraAppearanceCatalogAdmitsOnlyFiniteAppearanceAndWorldCounts()
    {
        using var fixture = new PackageFixture();
        Assert.Equal(64, FloraAppearanceCatalog.MaxAppearances, "finite appearance count ceiling");
        Assert.Equal(4096, FloraAppearanceCatalog.MaxBindings, "finite world mapping ceiling");
        FloraAppearanceWorldBinding binding = new(FloraAppearanceCatalog.EarthWorld, fixture.Pin.AppearanceId, "identity-v1");
        Reject<FloraAppearanceCapacityException>(() => new FloraAppearanceCatalog("counts", Array.Empty<FloraAppearancePin>(), new[] { binding }));
        Reject<FloraAppearanceCapacityException>(() => new FloraAppearanceCatalog("counts", new[] { fixture.Pin }, Array.Empty<FloraAppearanceWorldBinding>()));
        Reject<FloraAppearanceCapacityException>(() => new FloraAppearanceCatalog("counts", Enumerable.Repeat(fixture.Pin, 65).ToArray(), new[] { binding }));
        Reject<FloraAppearanceCapacityException>(() => new FloraAppearanceCatalog("counts", new[] { fixture.Pin }, Enumerable.Repeat(binding, 4097).ToArray()));
        FloraAppearancePin[] pins = Enumerable.Range(0, 64).Select(i =>
            new FloraAppearancePin("appearance-" + i, 1, fixture.Pin.ManifestSha256, fixture.Inputs)).ToArray();
        FloraAppearanceWorldBinding[] bindings = Enumerable.Range(0, 4096).Select(i =>
            new FloraAppearanceWorldBinding(FloraAppearanceCatalog.EarthWorld with { UniverseSeed = (ulong)i }, pins[0].AppearanceId, "identity-v1")).ToArray();
        var full = new FloraAppearanceCatalog("counts", pins, bindings);
        Assert.Equal(64, full.Appearances.Count, "the exact appearance ceiling is admitted");
        Assert.Equal(4096, full.Bindings.Count, "the exact binding ceiling is admitted");
        Assert.Equal(FloraAppearanceResolutionStatus.Exact, full.Resolve(bindings[^1].World, "counts", full.Identity).Status,
            "the final binding at the ceiling remains reachable");
    }

    [Test]
    public static void FloraAppearancePaletteAndMappingDoNotBecomeBakedPayloadInputs()
    {
        using var fixture = new PackageFixture();
        byte[] manifest = fixture.ManifestBytes;
        string inputIdentity = fixture.Inputs.Fingerprint;
        FloraAppearanceCatalog earth = FloraAppearanceCatalog.EarthLegacy(fixture.Pin);
        var explicitOther = new FloraAppearanceCatalog("explicit-other-version", new[] { fixture.Pin },
            new[] { new FloraAppearanceWorldBinding(FloraAppearanceCatalog.EarthWorld with { UniverseSeed = 7 },
                fixture.Pin.AppearanceId, FloraAppearancePalette.Identity.Id) });
        Assert.True(earth.Identity != explicitOther.Identity, "presentation mapping has its own identity");
        Assert.Equal(inputIdentity, explicitOther.Appearances[0].ExpectedInputs.Fingerprint, "mapping does not create another baked input identity");
        Assert.Equal(fixture.Pin.ManifestSha256, explicitOther.Appearances[0].ManifestSha256, "mappings reuse the exact admitted payload pin");
        Assert.True(manifest.SequenceEqual(Encode(fixture.Inputs)), "catalog and palette selection cannot mutate encoded baked evidence");
        Assert.Equal(new FloraAppearanceColour(1, 1, 1), FloraAppearancePalette.Identity.Multiplier, "only the no-op palette is admitted in v1");
        Reject<FloraAppearanceValidationException>(() => new FloraAppearanceCatalog("unsupported-palette", new[] { fixture.Pin },
            new[] { new FloraAppearanceWorldBinding(FloraAppearanceCatalog.EarthWorld, fixture.Pin.AppearanceId, "autumn") }));
    }

    private static void AssertLoadedPackage(PackageFixture fixture)
    {
        FloraAppearancePackage package = fixture.Load();
        Assert.Equal(CompletePayloadBytes, package.PayloadBytes, "successful package reserves all authenticated bytes");
        Assert.Equal(7, package.Images.Count, "complete publication contains all seven images");
        Assert.Equal(Mean, package.BladeMean, "linear grass colour survives without pixel recomputation");
        Assert.True(ReferenceEquals(fixture.Inputs, package.Inputs), "package carries the independent contract");
        for (int i = 0; i < package.Images.Count; i++)
        {
            FloraAppearanceImage image = package.Images[i];
            Assert.Equal(FloraAppearanceSchema.Images[i].Role, image.Layout.Role, "authenticated payload follows its role");
            Assert.Equal(image.Layout.ByteLength, image.ByteLength, "returned chain is complete");
            Assert.Equal((byte)(i + 1), image.ByteAt(0), "first payload byte is preserved");
            Assert.Equal((byte)(i + 101), image.ByteAt(image.ByteLength - 1), "last mip byte is preserved");
            Assert.Equal(fixture.ImageHashes[i].Sha256, image.Sha256, "returned hash is the independently pinned payload hash");
        }
        FloraAppearanceImage smallest = package.Images[3];
        byte[] copy = new byte[smallest.ByteLength]; smallest.CopyTo(copy);
        Assert.Equal(smallest.Sha256, Convert.ToHexString(SHA256.HashData(copy)), "CopyTo includes every mip byte exactly");
        copy[0] = 255;
        Assert.Equal((byte)4, smallest.ByteAt(0), "modifying caller staging cannot alter package memory");
        smallest.CopyTo(copy);
        Assert.Equal((byte)4, copy[0], "a second copy is unaffected by the caller's previous write");
        Reject<ArgumentException>(() => smallest.CopyTo(new byte[smallest.ByteLength - 1]));
        Reject<ArgumentException>(() => smallest.CopyTo(new byte[smallest.ByteLength + 1]));
        Reject<NotSupportedException>(() => ((IList<FloraAppearanceImage>)package.Images)[0] = smallest);
        WriteByte(fixture.ImagePath(3), 0, 255);
        Assert.Equal((byte)4, smallest.ByteAt(0), "later disk replacement cannot mutate an authenticated package");
        WriteByte(fixture.ImagePath(3), 0, 4);
    }

    private static void AssertLoadFailsWithoutPackage(PackageFixture fixture, string reason)
    {
        FloraAppearancePackage? package = null;
        Reject<FloraAppearanceValidationException>(() => package = fixture.Load());
        Assert.True(package == null, reason);
    }

    private static void RejectMetadata(PackageFixture fixture, string reason, FloraAppearancePin? pin = null)
    {
        FloraAppearanceValidationException error = Reject<FloraAppearanceValidationException>(() => fixture.Load(pin));
        Assert.True(error is not FloraAppearanceCapacityException && error.InnerException is not IOException, reason);
    }

    private static T Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new TestFailure("Expected " + typeof(T).Name + ".");
    }

    private static void NoFallback(FloraAppearanceResolution resolution, FloraAppearanceResolutionStatus status)
    {
        Assert.Equal(status, resolution.Status, "unsupported resolution reports its exact cause");
        Assert.True(resolution.Pin == null && resolution.Palette == null, "unsupported resolution offers no fallback payload or palette");
    }

    private static void Swap(JsonArray array, int a, int b)
    {
        JsonNode first = array[a]!.DeepClone(), second = array[b]!.DeepClone();
        array[a] = second; array[b] = first;
    }

    private static void WriteByte(string path, int offset, byte value)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = offset; stream.WriteByte(value);
    }

    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static FloraAppearanceBakeProfile Profile(string renderer = "forward_plus", string finish = "finish-v1") =>
        new("4.7.2-stable (official)", renderer, "fixture-device", "bake-v1", finish, "projection-v1");
    private static FloraAppearanceDependency[] Dependencies() => new[]
    {
        new FloraAppearanceDependency("tree/catalog", 79, Digest("catalog")),
        new FloraAppearanceDependency("shader/bake", 42, Digest("bake")),
    };
    private static FloraAppearanceInputContract Input(int seed = HostedEarthSeed,
        FloraAppearanceBakeProfile? profile = null, IReadOnlyList<FloraAppearanceDependency>? dependencies = null,
        IReadOnlyList<FloraAppearanceRow>? rows = null) => new(seed, profile ?? Profile(), dependencies ?? Dependencies(), rows ?? Rows());

    private static IEnumerable<FloraAppearanceInputContract> ChangedInputs()
    {
        yield return Input(seed: HostedEarthSeed + 1);
        yield return Input(profile: Profile(renderer: "gl_compatibility"));
        yield return Input(profile: Profile(finish: "finish-v2"));
        FloraAppearanceDependency[] dependencies = Dependencies();
        dependencies[0] = new FloraAppearanceDependency(dependencies[0].Key, dependencies[0].Bytes, Digest("changed source"));
        yield return Input(dependencies: dependencies);
        foreach (Func<FloraAppearanceRow, FloraAppearanceRow> change in new Func<FloraAppearanceRow, FloraAppearanceRow>[]
        {
            r => CopyRow(r, height: r.Height + 0.01f),
            r => CopyRow(r, scale: r.SpeciesScale + 0.01f),
            r => CopyRow(r, lod0: Digest("changed lod0")),
            r => CopyRow(r, lod1: Digest("changed lod1")),
            r => CopyRow(r, material: Digest("changed alpha cutoff")),
        })
        {
            FloraAppearanceRow[] rows = Rows(); rows[0] = change(rows[0]);
            yield return Input(rows: rows);
        }
    }

    private static FloraAppearanceRow Row(int row, ScatterKind kind, int variant, float height = 12, float extent = 14,
        string? key = null) => new(row, kind, variant, key ?? "broadleaf_" + variant,
            kind == ScatterKind.Broadleaf ? 1.3f : 1f, height, extent,
            Digest("lod0-" + row), Digest("lod1-" + row), Digest("material-" + row));
    private static FloraAppearanceRow CopyRow(FloraAppearanceRow row, string? key = null, float? height = null,
        float? extent = null, float? scale = null, string? lod0 = null, string? lod1 = null, string? material = null) =>
        new(row.Row, row.Kind, row.Variant, key ?? row.VariantKey, scale ?? row.SpeciesScale, height ?? row.Height,
            extent ?? row.Extent, lod0 ?? row.Lod0GeometrySha256, lod1 ?? row.Lod1GeometrySha256, material ?? row.MaterialSha256);
    private static FloraAppearanceRow[] Rows() => new[]
    {
        Row(0, ScatterKind.Broadleaf, 0, 11.3020935f, 13.511151f),
        Row(1, ScatterKind.Broadleaf, 1, 11.916776f, 13.101381f),
        Row(2, ScatterKind.Broadleaf, 2, 11.815333f, 12.965966f),
        Row(3, ScatterKind.Broadleaf, 3, 12.239351f, 13.4049225f),
        Row(4, ScatterKind.Fir, 0, 18.2f, 18.928001f, "fir_00"),
        Row(5, ScatterKind.Fir, 1, 18.199999f, 18.928f, "fir_01"),
        Row(6, ScatterKind.Fir, 2, 18.2f, 18.928001f, "fir_02"),
        Row(7, ScatterKind.Birch, 0, 15f, 21.537657f, "birch_00"),
        Row(8, ScatterKind.Birch, 1, 15.000001f, 21.445131f, "birch_01"),
        Row(9, ScatterKind.Birch, 2, 14.999999f, 21.307306f, "birch_02"),
        Row(10, ScatterKind.Pine, 0, 19.199999f, 19.967999f, "pine_00"),
    };
    private static byte[] Encode(FloraAppearanceInputContract input, IReadOnlyList<FloraAppearanceImageHash>? hashes = null) =>
        FloraAppearanceManifest.Encode(FloraAppearanceCatalog.EarthAppearanceId, 1, input, Mean,
            hashes ?? FloraAppearanceSchema.Images.Select(i => new FloraAppearanceImageHash(i.Role, Digest("payload-" + i.Role))).ToArray());

    private sealed class PackageFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hollow-flora-appearance-" + Guid.NewGuid().ToString("N"));
        public string ManifestPath => Path.Combine(Root, FloraAppearanceManifest.FileName);
        public FloraAppearanceInputContract Inputs { get; } = Input();
        public FloraAppearancePin Pin { get; private set; } = null!;
        public byte[] ManifestBytes { get; private set; } = Array.Empty<byte>();
        public FloraAppearanceImageHash[] ImageHashes { get; }
        public FloraAppearanceLimits ExactLimits => new(ManifestBytes.Length, 30758212, CompletePayloadBytes + ManifestBytes.Length);

        public PackageFixture(bool writeImages = false)
        {
            Directory.CreateDirectory(Root);
            ImageHashes = new FloraAppearanceImageHash[7];
            for (int i = 0; i < ImageHashes.Length; i++)
            {
                FloraAppearanceImageLayout layout = FloraAppearanceSchema.Images[i];
                string digest = writeImages ? WriteImage(ImagePath(i), layout.ByteLength, i) : Digest("payload-" + layout.Role);
                ImageHashes[i] = new FloraAppearanceImageHash(layout.Role, digest);
            }
            WriteManifest(Encode(Inputs, ImageHashes));
        }

        public string ImagePath(int index) => Path.Combine(Root, FloraAppearanceSchema.Images[index].File);
        public FloraAppearancePackage Load(FloraAppearancePin? pin = null, FloraAppearanceLimits? limits = null) =>
            FloraAppearancePackageLoader.Load(Root, pin ?? Pin, limits ?? ExactLimits);
        public void WriteManifest(byte[] bytes, FloraAppearanceInputContract? trustedInputs = null)
        {
            ManifestBytes = bytes;
            File.WriteAllBytes(ManifestPath, bytes);
            Pin = new FloraAppearancePin(FloraAppearanceCatalog.EarthAppearanceId, 1,
                Convert.ToHexString(SHA256.HashData(bytes)), trustedInputs ?? Inputs);
        }
        public void Mutate(byte[] baseline, Action<JsonNode> mutate)
        {
            JsonNode node = JsonNode.Parse(baseline)!; mutate(node);
            WriteManifest(JsonSerializer.SerializeToUtf8Bytes(node));
        }

        private static string WriteImage(string path, int length, int ordinal)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] block = new byte[65536];
            for (int offset = 0; offset < length;)
            {
                Array.Clear(block);
                int count = Math.Min(block.Length, length - offset);
                if (offset == 0) block[0] = (byte)(ordinal + 1);
                if (offset + count == length) block[count - 1] = (byte)(ordinal + 101);
                stream.Write(block, 0, count); hash.AppendData(block, 0, count); offset += count;
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
