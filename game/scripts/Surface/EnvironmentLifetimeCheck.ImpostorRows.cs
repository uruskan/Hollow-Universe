using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Real-renderer oracle: same asymmetric, alpha-cutout source meshes, unchanged production bake shader
    // and Finish. The reference uses the old full atlas camera; the candidate crops one original row at a time.
    private async Task CheckImpostorRows()
    {
        ImpostorRowCapture reference = await CaptureImpostorRows(true);
        ImpostorRowCapture bands = await CaptureImpostorRows(false);
        for (int pass = 0; pass < 2; pass++)
        {
            int rawDifferences = ImpostorByteDifferences(reference.Raw[pass], bands.Raw[pass]);
            Expect($"impostor row bands preserve raw pass {pass}", rawDifferences == 0,
                $"{rawDifferences} differing RGBA bytes; actual 64x24 full viewport versus three 64x8 crops before any dilation or mip processing");
            int finishedDifferences = ImpostorByteDifferences(reference.Finished[pass], bands.Finished[pass]);
            Expect($"impostor row bands preserve processed pass {pass}", finishedDifferences == 0
                && reference.Mips[pass] && bands.Mips[pass],
                $"{finishedDifferences} differing bytes across uploaded base plus all mip levels; same complete-image Finish including cross-row dilation");
        }
        Expect("impostor row oracle has three distinct correctly ordered rows", ImpostorDistinctRows(reference.Raw[0])
            && ImpostorDistinctRows(bands.Raw[0]),
            "top red, middle green and bottom blue each contain opaque pixels; asymmetric geometry, original UV alpha holes, eight azimuths and an odd row count");
        Expect("impostor row viewport is bounded independently of final atlas height",
            reference.ViewportSize == new Vector2I(64, 24) && bands.ViewportSize == new Vector2I(64, 8)
            && bands.Rows == 3 && bands.Raw.All(p => p.Length == 64 * 24 * 4)
            && bands.Bands.SequenceEqual(new[] { "0:0", "0:1", "0:2", "1:0", "1:1", "1:2" }),
            "one fixed 64x8 viewport produces all six row/pass readbacks and complete 64x24 images without truncating the last row");
        Expect("impostor row readbacks retire before the next band", reference.BandsReleased && bands.BandsReleased
            && reference.ViewportRetired && bands.ViewportRetired,
            "actual native row Images are invalid before the next readback; final viewport is destroyed before bake completion");
        await CheckInterruptedImpostorRows(false);
        await CheckInterruptedImpostorRows(true);
    }

    private sealed record ImpostorRowCapture(byte[][] Raw, byte[][] Finished, bool[] Mips,
        Vector2I ViewportSize, int Rows, List<string> Bands, bool BandsReleased, bool ViewportRetired);

    private async Task<ImpostorRowCapture> CaptureImpostorRows(bool fullAtlas)
    {
        var baker = new ImpostorBaker { Name = fullAtlas ? "FullImpostorPixelReference" : "BandedImpostorPixelCandidate" };
        AddChild(baker);
        byte[][] raw = { Array.Empty<byte>(), Array.Empty<byte>() };
        byte[][] finished = { Array.Empty<byte>(), Array.Empty<byte>() };
        bool[] mips = new bool[2];
        var bands = new List<string>();
        var readbacks = new List<Image>();
        Vector2I viewportSize = default;
        SubViewport? viewport = null;
        bool releasedBeforeNext = true;
        ImpostorBaker.Result? result = null;
        var hooks = new ImpostorBaker.OwnershipFixtureHooks
        {
            ViewportCreated = value => { viewport = value; viewportSize = value.Size; },
            BandReadback = (pass, row, image) =>
            {
                releasedBeforeNext &= readbacks.All(previous => !GodotObject.IsInstanceValid(previous));
                bands.Add($"{pass}:{row}"); readbacks.Add(image);
            },
            Readback = (pass, image) => raw[pass] = image.GetData(),
        };
        try
        {
            Task<ImpostorBaker.Result> bake = baker.BakeRowBandFixture(fullAtlas, hooks);
            await AwaitImpostorFixture(bake, baker, "impostor row pixel parity");
            _ = await bake;
            result = baker.TakeResult();
            using (Image colour = result.Colour.GetImage())
            {
                finished[0] = colour.GetData(); mips[0] = colour.HasMipmaps();
            }
            using (Image normal = result.Normal.GetImage())
            {
                finished[1] = normal.GetData(); mips[1] = normal.HasMipmaps();
            }
            return new(raw, finished, mips, viewportSize, result.Rows, bands,
                releasedBeforeNext && readbacks.All(image => !GodotObject.IsInstanceValid(image)),
                viewport != null && !GodotObject.IsInstanceValid(viewport));
        }
        finally
        {
            result?.Dispose();
            await RetireImpostorFixture(baker);
        }
    }

    private async Task CheckInterruptedImpostorRows(bool fault)
    {
        var baker = new ImpostorBaker { Name = fault ? "FailedImpostorRow" : "CancelledImpostorRow" };
        AddChild(baker);
        var expected = new InvalidOperationException("Expected row readback failure.");
        var readbacks = new List<Image>();
        var assemblies = new List<Image>();
        SubViewport? viewport = null;
        ImageTexture? partialColour = null;
        int admissions = 0, finalProcessing = 0;
        bool injected = false, observedExpected = false;
        var hooks = new ImpostorBaker.OwnershipFixtureHooks
        {
            ViewportCreated = value => viewport = value,
            AssemblyCreated = (_, image) => assemblies.Add(image),
            BandReadback = (pass, row, image) =>
            {
                readbacks.Add(image); admissions++;
                if (row != 1 || pass != (fault ? 1 : 0)) return;
                injected = true;
                if (fault) throw expected;
                baker.Stop(); baker.Stop();
            },
            Uploaded = (pass, texture) => { if (pass == 0) partialColour = texture; },
            ProcessingStarting = pass => { if (pass == 1) finalProcessing++; },
        };
        try
        {
            Task<ImpostorBaker.Result> bake = baker.BakeRowBandFixture(false, hooks);
            await AwaitImpostorFixture(bake, baker, "interrupted impostor row");
            try { _ = await bake; }
            catch (OperationCanceledException) when (!fault) { observedExpected = true; }
            catch (InvalidOperationException error) when (ReferenceEquals(error, expected)) { observedExpected = true; }
            bool takeRefused = false;
            try { baker.TakeResult().Dispose(); }
            catch (InvalidOperationException) { takeRefused = true; }
            Expect(fault ? "impostor failed row publishes no partial normal atlas" : "impostor cancellation closes row admission",
                injected && observedExpected && takeRefused && baker.Stopped.IsCompletedSuccessfully
                && admissions == (fault ? 5 : 2) && finalProcessing == 0
                && (fault ? bake.IsFaulted : bake.IsCanceled),
                "failure/cancellation at the second row stops all later bands; the unfinished assembly never enters Finish or becomes a Result");
            Expect(fault ? "impostor failed row releases native images and prior colour" : "impostor cancelled row releases its native readback",
                readbacks.All(image => !GodotObject.IsInstanceValid(image))
                && assemblies.Count == (fault ? 2 : 1)
                && assemblies.All(image => !GodotObject.IsInstanceValid(image))
                && (fault ? partialColour != null && !GodotObject.IsInstanceValid(partialColour) : partialColour == null),
                "actual incomplete assembly plus pending row are disposed; a failure in the normal pass also retires the completed colour input and already-uploaded texture");
            await Until(() => viewport != null && !GodotObject.IsInstanceValid(viewport), "interrupted impostor row viewport deletion");
        }
        finally { await RetireImpostorFixture(baker); }
    }

    private static int ImpostorByteDifferences(byte[] expected, byte[] actual)
    {
        if (expected.Length != actual.Length) return Math.Max(expected.Length, actual.Length);
        int count = 0;
        for (int i = 0; i < expected.Length; i++) if (expected[i] != actual[i]) count++;
        return count;
    }

    private static bool ImpostorDistinctRows(byte[] rgba)
    {
        if (rgba.Length != 64 * 24 * 4) return false;
        for (int row = 0; row < 3; row++)
        {
            long[] sum = new long[3];
            int occupied = 0;
            for (int p = row * 8 * 64 * 4; p < (row + 1) * 8 * 64 * 4; p += 4)
            {
                if (rgba[p + 3] == 0) continue;
                occupied++;
                for (int channel = 0; channel < 3; channel++) sum[channel] += rgba[p + channel];
            }
            if (occupied < 8 || sum[row] <= sum[(row + 1) % 3] || sum[row] <= sum[(row + 2) % 3]) return false;
        }
        return true;
    }
}
