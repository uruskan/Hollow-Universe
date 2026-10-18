using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Two 64x8 synthetic native bakes plus tiny extraction failures. Existing complete pixel parity tests
    // stay unchanged; these assert lifetime ordering while the relevant native owners still exist.
    private async Task CheckImpostorOwnership()
    {
        CheckImpostorReadbackFailures();
        await CheckCompletedImpostorOwnership();
        await CheckCancelledImpostorOwnership();
    }

    private void CheckImpostorReadbackFailures()
    {
        byte[] bytes = FloraPixelFixture(64, 8, 2);
        Image failed = Image.CreateFromData(64, 8, false, Image.Format.Rgba8, bytes);
        var injected = new InvalidOperationException("Expected readback extraction failure.");
        bool extractionOwnedInput = false, processingStarted = false, sameFailure = false;
        var fault = new ImpostorBaker.OwnershipFixtureHooks
        {
            BeforeGetData = (_, input) =>
            {
                extractionOwnedInput = GodotObject.IsInstanceValid(input);
                throw injected;
            },
            BeforePixels = (_, _) => processingStarted = true,
        };
        try { using Image unexpected = ImpostorBaker.FinishLifecycleFixture(failed, 0, 1, fixture: fault); }
        catch (InvalidOperationException error) { sameFailure = ReferenceEquals(error, injected); }
        Expect("impostor extraction failure releases the owned readback", extractionOwnedInput && sameFailure
            && !processingStarted && !GodotObject.IsInstanceValid(failed),
            "fault inside conversion/dump/GetData owning scope propagates before pixel work, with native input disposed");

        using var cancellation = new CancellationTokenSource();
        Image cancelled = Image.CreateFromData(64, 8, false, Image.Format.Rgba8, bytes);
        bool releasedBeforePixels = false, cancelledAfterCopy = false;
        var afterCopy = new ImpostorBaker.OwnershipFixtureHooks
        {
            BeforePixels = (_, input) =>
            {
                releasedBeforePixels = !GodotObject.IsInstanceValid(input);
                cancellation.Cancel();
            },
        };
        try { using Image unexpected = ImpostorBaker.FinishLifecycleFixture(cancelled, 0, 1, cancellation.Token, afterCopy); }
        catch (OperationCanceledException) { cancelledAfterCopy = true; }
        Expect("impostor cancellation after extraction publishes no image", releasedBeforePixels && cancelledAfterCopy
            && !GodotObject.IsInstanceValid(cancelled),
            "input is already released at the processing boundary; cancellation prevents output pixel allocation");
    }

    private async Task CheckCompletedImpostorOwnership()
    {
        var baker = new ImpostorBaker { Name = "CompletedImpostorOwnership" };
        AddChild(baker);
        var readbacks = new Image?[2];
        var releasedBeforePixels = new bool[2];
        SubViewport? viewport = null;
        bool queuedWhileValid = false, finalProcessingAfterDeletion = false;
        ImpostorBaker.Result? result = null;
        var hooks = new ImpostorBaker.OwnershipFixtureHooks
        {
            Readback = (pass, image) => readbacks[pass] = image,
            ViewportRetiring = value =>
            {
                viewport = value;
                queuedWhileValid = GodotObject.IsInstanceValid(value) && value.IsQueuedForDeletion();
            },
            ProcessingStarting = pass =>
            {
                if (pass == 1) finalProcessingAfterDeletion = viewport != null && !GodotObject.IsInstanceValid(viewport);
            },
            BeforePixels = (pass, image) => releasedBeforePixels[pass] = !GodotObject.IsInstanceValid(image),
        };
        try
        {
            Task<ImpostorBaker.Result> bake = baker.BakeLifecycleFixture(hooks);
            await AwaitImpostorFixture(bake, baker, "tiny completed impostor bake");
            _ = await bake;
            result = baker.TakeResult();
            Expect("impostor final processing waits for native viewport destruction", queuedWhileValid
                && finalProcessingAfterDeletion && viewport != null && !GodotObject.IsInstanceValid(viewport),
                "QueueFree is observed while still valid; final worker admission occurs only after actual native deletion");
            Expect("impostor readbacks are released before both pixel stages", releasedBeforePixels.All(value => value)
                && readbacks.All(image => image != null && !GodotObject.IsInstanceValid(image)),
                "checks run at the production processing boundary, not only after the bake returns");
            Expect("impostor completed textures transfer to the caller", result.Colour.GetWidth() == 64
                && result.Colour.GetHeight() == 8 && result.Normal.GetWidth() == 64 && result.Normal.GetHeight() == 8,
                "both tiny atlas textures survive viewport destruction and ownership transfer");
            ImageTexture colour = result.Colour, normal = result.Normal;
            result.Dispose(); result = null;
            Expect("impostor transferred textures release with their owner", !GodotObject.IsInstanceValid(colour)
                && !GodotObject.IsInstanceValid(normal), "native output textures have a separate explicit owner");
        }
        finally
        {
            result?.Dispose();
            await RetireImpostorFixture(baker);
        }
    }

    private async Task CheckCancelledImpostorOwnership()
    {
        var baker = new ImpostorBaker { Name = "CancelledImpostorOwnership" };
        AddChild(baker);
        var readbacks = new Image?[2];
        SubViewport? viewport = null;
        ImageTexture? partialColour = null;
        bool queuedWhileValid = false, retirementWaitArmed = false, finalProcessingStarted = false;
        var hooks = new ImpostorBaker.OwnershipFixtureHooks
        {
            Readback = (pass, image) => readbacks[pass] = image,
            Uploaded = (pass, texture) => { if (pass == 0) partialColour = texture; },
            ProcessingStarting = pass => { if (pass == 1) finalProcessingStarted = true; },
            ViewportRetiring = value =>
            {
                viewport = value;
                queuedWhileValid = GodotObject.IsInstanceValid(value) && value.IsQueuedForDeletion();
            },
            RetirementWaitArmed = () =>
            {
                // The new wait has subscribed to both drawing and cancellation; no subsequent draw is needed.
                retirementWaitArmed = true;
                baker.Stop(); baker.Stop();
            },
        };
        try
        {
            Task<ImpostorBaker.Result> bake = baker.BakeLifecycleFixture(hooks);
            await AwaitImpostorFixture(bake, baker, "cancelled final impostor handoff");
            bool takeRefused = false;
            try { baker.TakeResult().Dispose(); }
            catch (InvalidOperationException) { takeRefused = true; }
            Expect("impostor final retirement cancellation prevents worker admission", queuedWhileValid && retirementWaitArmed
                && bake.IsCanceled && baker.Stopped.IsCompletedSuccessfully && !finalProcessingStarted && takeRefused,
                "repeated Stop cancels after readback and queued deletion without publishing a result or starting final CPU work");
            Expect("impostor cancelled handoff releases readback and partial output", partialColour != null
                && !GodotObject.IsInstanceValid(partialColour)
                && readbacks.All(image => image != null && !GodotObject.IsInstanceValid(image)),
                "the waiting normal readback stays with BakeCore; the already-uploaded colour texture is retired on failure");
            await Until(() => viewport != null && !GodotObject.IsInstanceValid(viewport), "cancelled impostor viewport deletion");
        }
        finally { await RetireImpostorFixture(baker); }
    }

    private static async Task AwaitImpostorFixture(Task task, ImpostorBaker baker, string stage)
    {
        await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(12)));
        if (task.IsCompleted) return;
        baker.Stop();
        await Task.WhenAny(baker.Stopped, Task.Delay(TimeSpan.FromSeconds(2)));
        throw new TimeoutException(stage + " exceeded its bounded fixture deadline.");
    }

    private async Task RetireImpostorFixture(ImpostorBaker baker)
    {
        if (GodotObject.IsInstanceValid(baker)) { baker.Stop(); baker.QueueFree(); }
        await Task.WhenAny(baker.Stopped, Task.Delay(TimeSpan.FromSeconds(2)));
        Expect("impostor ownership fixture drains its baker", baker.Stopped.IsCompletedSuccessfully,
            "bounded cleanup completes without forced collection or synchronous renderer waits");
    }
}
