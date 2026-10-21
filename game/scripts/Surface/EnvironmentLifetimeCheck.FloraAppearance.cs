using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Synthetic session/lifetime evidence only. The injected backend never reads a package, qualifies production
    // art, paints flora or allocates the 87 MB payload. Real native resources are one pixel, one shader and bindings.
    private async Task CheckFloraAppearanceSession()
    {
        await CheckFloraAppearanceAdmission();
        await CheckFloraAppearanceSharingAndRetirement();
        await CheckFloraAppearancePendingCancellation();
        await CheckFloraAppearanceCancelledWorker();
        await CheckFloraAppearanceStoppedWorker();
        await CheckFloraAppearanceUploadQuarantine();
        await CheckFloraAppearanceBindingFailure();
        await CheckFloraAppearanceBindingQuarantine();
    }

    private const long AppearanceFixtureMiB = 1048576;
    private const long AppearanceFixtureBindingBytes = 4096;
    private static long AppearanceFixtureEntryBytes => FloraAppearanceSchema.PayloadBytes +
        AppearanceFixtureMiB + 96 * AppearanceFixtureMiB + 128 * AppearanceFixtureMiB;

    private static FloraAppearanceSessionBudget AppearanceFixtureBudget(int acquisitions = 2, int roots = 4,
        long? bytes = null) => new(acquisitions, roots, bytes ?? AppearanceFixtureEntryBytes + 16 * AppearanceFixtureBindingBytes,
            AppearanceFixtureMiB, 96 * AppearanceFixtureMiB, 128 * AppearanceFixtureMiB, AppearanceFixtureBindingBytes);

    private static FloraAppearanceCatalog AppearanceFixtureCatalog()
    {
        string digest = new('A', 64);
        var rows = new List<FloraAppearanceRow>();
        for (int row = 0; row < FloraAppearanceSchema.Rows; row++)
        {
            ScatterKind kind = row < 4 ? ScatterKind.Broadleaf : row < 7 ? ScatterKind.Fir : row < 10 ? ScatterKind.Birch : ScatterKind.Pine;
            int variant = row < 4 ? row : row < 7 ? row - 4 : row < 10 ? row - 7 : 0;
            rows.Add(new FloraAppearanceRow(row, kind, variant, "fixture-" + row, 1, 2, 3, digest, digest, digest));
        }
        var inputs = new FloraAppearanceInputContract(FloraAppearanceCatalog.EarthRecipeSeed,
            new FloraAppearanceBakeProfile("synthetic", "synthetic", "synthetic", "fixture-v1", "fixture-v1", "fixture-v1"),
            new[] { new FloraAppearanceDependency("fixture/synthetic", 1, digest) }, rows);
        return FloraAppearanceCatalog.EarthLegacy(new FloraAppearancePin(FloraAppearanceCatalog.EarthAppearanceId, 1, digest, inputs));
    }

    private Node AppearanceFixtureRoot(List<Node> roots)
    {
        var root = new Node { Name = "AppearanceConsumer" + roots.Count };
        roots.Add(root); AddChild(root); return root;
    }

    private static FloraAppearanceSession.Request AppearanceFixtureAcquire(FloraAppearanceSession session,
        FloraAppearanceCatalog catalog, Node root, List<FloraAppearanceSession.Request> requests,
        CancellationToken cancellation = default)
    {
        FloraAppearanceSession.Request request = session.Acquire(FloraAppearanceCatalog.EarthWorld,
            catalog.VisualVersion, catalog.Identity, root, cancellation);
        requests.Add(request); return request;
    }

    private async Task PumpAppearanceUntil(FloraAppearanceSession session, Func<bool> condition, string stage)
        => await Until(() => { session.Pump(); return condition(); }, stage);

    private static bool AppearanceFixtureReject<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    private async Task CheckFloraAppearanceAdmission()
    {
        FloraAppearanceCatalog catalog = AppearanceFixtureCatalog();
        var backend = new AppearanceFixtureBackend();
        var session = new FloraAppearanceSession(catalog,
            AppearanceFixtureBudget(bytes: AppearanceFixtureEntryBytes + AppearanceFixtureBindingBytes - 1), backend.Operations);
        var roots = new List<Node>();
        var requests = new List<FloraAppearanceSession.Request>();
        Node root = AppearanceFixtureRoot(roots);
        try
        {
            bool capacity = AppearanceFixtureReject<FloraAppearanceCapacityException>(() => AppearanceFixtureAcquire(session, catalog, root, requests));
            bool version = AppearanceFixtureReject<FloraAppearanceValidationException>(() => session.Acquire(
                FloraAppearanceCatalog.EarthWorld, "unsupported-version", catalog.Identity, root));
            bool identity = AppearanceFixtureReject<FloraAppearanceValidationException>(() => session.Acquire(
                FloraAppearanceCatalog.EarthWorld, catalog.VisualVersion, new string('B', 64), root));
            FloraAppearanceWorldKey otherWorld = FloraAppearanceCatalog.EarthWorld with
                { SurfaceSeed = FloraAppearanceCatalog.EarthWorld.SurfaceSeed + 1 };
            bool world = AppearanceFixtureReject<FloraAppearanceValidationException>(() => session.Acquire(
                otherWorld, catalog.VisualVersion, catalog.Identity, root));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool cancellation = AppearanceFixtureReject<OperationCanceledException>(() => AppearanceFixtureAcquire(session, catalog, root, requests, cancelled.Token));
            FloraAppearanceSession.Snapshot snapshot = session.CaptureDiagnostics();
            Expect("appearance rejection happens before worker dispatch", capacity && version && identity && world && cancellation
                && snapshot.TotalBytes == 0 && snapshot.Acquisitions == 0 && snapshot.LoadStarts == 0
                && backend.LoadCalls == 0 && backend.UploadCalls == 0,
                "byte shortage, unknown world/version/catalog and pre-cancellation allocate no session entry and never invoke a fallback");
        }
        finally { await RetireAppearanceFixture(session, backend, roots, requests); }
    }

    private async Task CheckFloraAppearanceSharingAndRetirement()
    {
        FloraAppearanceCatalog catalog = AppearanceFixtureCatalog();
        var backend = new AppearanceFixtureBackend(blockLoad: true);
        var session = new FloraAppearanceSession(catalog, AppearanceFixtureBudget(roots: 2), backend.Operations);
        var roots = new List<Node>();
        var requests = new List<FloraAppearanceSession.Request>();
        var workers = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Node firstRoot = AppearanceFixtureRoot(roots), secondRoot = AppearanceFixtureRoot(roots);
        try
        {
            FloraAppearanceSession.Request first = AppearanceFixtureAcquire(session, catalog, firstRoot, requests);
            FloraAppearanceSession.Request second = AppearanceFixtureAcquire(session, catalog, secondRoot, requests);
            await Until(() => backend.Entered.IsSet, "appearance shared CPU load starts");
            FloraAppearanceSession.Snapshot loading = session.CaptureDiagnostics();
            Expect("appearance requests share one fully reserved load", loading.Loading && loading.Acquisitions == 2
                && loading.TotalBytes == AppearanceFixtureEntryBytes + 2 * AppearanceFixtureBindingBytes
                && loading.LoadStarts == 1 && backend.LoadCalls == 1 && backend.UploadCalls == 0,
                "exact payload and all declared allowances are charged before the blocked CPU delegate can finish");
            backend.Continue.Set();
            await PumpAppearanceUntil(session, () => first.Ready.IsCompleted && second.Ready.IsCompleted, "appearance shared upload completes");
            FloraAppearanceSession.Lease left = await first.Ready, right = await second.Ready;
            ShaderMaterial leftMaterial = left.ImpostorMaterial, rightMaterial = right.ImpostorMaterial;
            leftMaterial.SetShaderParameter("fixture_value", 7f);
            FloraAppearanceSession.Snapshot ready = session.CaptureDiagnostics();
            Expect("appearance leases share assets but isolate mutable bindings", ReferenceEquals(left.Library, right.Library)
                && !ReferenceEquals(leftMaterial, rightMaterial) && leftMaterial.GetInstanceId() != rightMaterial.GetInstanceId()
                && rightMaterial.GetShaderParameter("fixture_value").AsDouble() == 1
                && ready.Ready && ready.LoadStarts == 1 && ready.UploadStarts == 1 && backend.UploadCalls == 1
                && backend.BindingCalls == 2 && backend.UploadOnMainThread && backend.BindingOnMainThread
                && ready.GeometryBytes == 0 && ready.UploadBytes == 0 && backend.Cpu?.GeometryReleases == 1,
                "a single native creation releases construction allowances; each view has its own material parameters");

            Node replacementRoot = AppearanceFixtureRoot(roots);
            Task firstRetired = left.Retire(workers.Task);
            firstRoot.QueueFree(); session.Pump();
            bool slotHeld = AppearanceFixtureReject<FloraAppearanceCapacityException>(() => AppearanceFixtureAcquire(session, catalog, replacementRoot, requests));
            Expect("appearance retirement keeps slots and bindings through queued deletion", slotHeld && GodotObject.IsInstanceValid(firstRoot)
                && !firstRetired.IsCompleted && GodotObject.IsInstanceValid(leftMaterial) && session.CaptureDiagnostics().Acquisitions == 2,
                "QueueFree is not native destruction; a retiring lease still occupies its acquisition slot");
            await PumpAppearanceUntil(session, () => !GodotObject.IsInstanceValid(firstRoot), "appearance first consumer deletion");
            long heldBytes = session.CaptureDiagnostics().TotalBytes;
            Expect("appearance worker barrier outlives deleted consumers", !firstRetired.IsCompleted
                && GodotObject.IsInstanceValid(leftMaterial) && session.CaptureDiagnostics().BindingBytes == 2 * AppearanceFixtureBindingBytes,
                "actual node deletion cannot release a material while its registered worker task is incomplete");
            workers.TrySetResult(true);
            await PumpAppearanceUntil(session, () => firstRetired.IsCompleted, "appearance first lease retirement");
            Expect("appearance releases one retired binding without disturbing its sibling", firstRetired.IsCompletedSuccessfully
                && !GodotObject.IsInstanceValid(leftMaterial) && GodotObject.IsInstanceValid(rightMaterial)
                && session.CaptureDiagnostics().Acquisitions == 1 && session.CaptureDiagnostics().TotalBytes == heldBytes - AppearanceFixtureBindingBytes,
                "shared native ownership survives; only the finished lease's binding reservation is removed");

            FloraAppearanceSession.Request replacement = AppearanceFixtureAcquire(session, catalog, replacementRoot, requests);
            await PumpAppearanceUntil(session, () => replacement.Ready.IsCompleted, "appearance vacant acquisition slot reuse");
            FloraAppearanceSession.Lease next = await replacement.Ready;
            ShaderMaterial nextMaterial = next.ImpostorMaterial;
            Node extraRoot = AppearanceFixtureRoot(roots), overflowRoot = AppearanceFixtureRoot(roots);
            next.AddConsumer(extraRoot); next.AddConsumer(extraRoot);
            bool rootCap = AppearanceFixtureReject<FloraAppearanceCapacityException>(() => next.AddConsumer(overflowRoot));
            Task nextRetired = next.Retire(Task.CompletedTask);
            replacementRoot.QueueFree();
            await PumpAppearanceUntil(session, () => !GodotObject.IsInstanceValid(replacementRoot), "appearance replacement root deletion");
            Expect("appearance waits for every independent consumer root", rootCap && !nextRetired.IsCompleted
                && GodotObject.IsInstanceValid(extraRoot) && GodotObject.IsInstanceValid(nextMaterial)
                && backend.LoadCalls == 1 && backend.UploadCalls == 1 && backend.BindingCalls == 3,
                "completed workers and one deleted root cannot retire resources borrowed by another live root");

            session.Stop(); session.Dispose(); session.Pump();
            Expect("appearance Stop does not silently retire a published lease", !session.Stopped.IsCompleted
                && GodotObject.IsInstanceValid(rightMaterial) && session.CaptureDiagnostics().Acquisitions == 2,
                "closing admission is nonblocking; the still-live second lease must explicitly provide its retirement barrier");
            Task secondRetired = right.Retire(Task.CompletedTask);
            secondRoot.QueueFree(); extraRoot.QueueFree();
            session.Pump();
            Expect("appearance final retirement waits for real native deletion", !session.Stopped.IsCompleted
                && !secondRetired.IsCompleted && !nextRetired.IsCompleted && session.CaptureDiagnostics().TotalBytes > 0,
                "all worker tasks are complete, but queued consumer nodes still keep native reservations pinned");
            await PumpAppearanceUntil(session, () => session.Stopped.IsCompleted, "appearance shared session drains");
            Expect("appearance shared ownership retires exactly once", session.Stopped.IsCompletedSuccessfully
                && secondRetired.IsCompletedSuccessfully && nextRetired.IsCompletedSuccessfully
                && !GodotObject.IsInstanceValid(rightMaterial) && !GodotObject.IsInstanceValid(nextMaterial)
                && backend.NativeOwner?.Disposals == 1 && backend.OtherOwner?.Disposals == 1 && backend.Cpu?.Disposals == 1
                && session.CaptureDiagnostics().TotalBytes == 0,
                "the last lease releases its shared native bundle and exact payload reservation after both barrier kinds drain");
        }
        finally { workers.TrySetResult(true); await RetireAppearanceFixture(session, backend, roots, requests); }
    }

    private async Task CheckFloraAppearancePendingCancellation()
    {
        FloraAppearanceCatalog catalog = AppearanceFixtureCatalog();
        var backend = new AppearanceFixtureBackend(blockLoad: true);
        var session = new FloraAppearanceSession(catalog, AppearanceFixtureBudget(), backend.Operations);
        var roots = new List<Node>(); var requests = new List<FloraAppearanceSession.Request>();
        try
        {
            FloraAppearanceSession.Request cancelled = AppearanceFixtureAcquire(session, catalog, AppearanceFixtureRoot(roots), requests);
            FloraAppearanceSession.Request survivor = AppearanceFixtureAcquire(session, catalog, AppearanceFixtureRoot(roots), requests);
            await Until(() => backend.Entered.IsSet, "appearance cancellable shared worker starts");
            await Task.Run(cancelled.Cancel); session.Pump();
            Expect("appearance cancellation is isolated between pending views", cancelled.Ready.IsCanceled && !survivor.Ready.IsCompleted
                && session.CaptureDiagnostics().Acquisitions == 1 && session.CaptureDiagnostics().Loading
                && backend.LoadCalls == 1 && backend.UploadCalls == 0,
                "thread-safe cancellation removes one request without cancelling the shared work still needed by another");
            backend.Continue.Set();
            await PumpAppearanceUntil(session, () => survivor.Ready.IsCompleted, "appearance surviving request completes");
            _ = await survivor.Ready;
            Expect("appearance surviving request publishes once", backend.LoadCalls == 1 && backend.UploadCalls == 1 && backend.BindingCalls == 1,
                "the shared worker was not restarted and no material was created for the cancelled request");
        }
        finally { await RetireAppearanceFixture(session, backend, roots, requests); }
    }

    private async Task CheckFloraAppearanceCancelledWorker()
    {
        FloraAppearanceCatalog catalog = AppearanceFixtureCatalog();
        var backend = new AppearanceFixtureBackend(blockLoad: true);
        var session = new FloraAppearanceSession(catalog, AppearanceFixtureBudget(), backend.Operations);
        var roots = new List<Node>(); var requests = new List<FloraAppearanceSession.Request>();
        Node root = AppearanceFixtureRoot(roots);
        try
        {
            FloraAppearanceSession.Request request = AppearanceFixtureAcquire(session, catalog, root, requests);
            await Until(() => backend.Entered.IsSet, "appearance cancelled-only worker starts");
            root.QueueFree(); session.Pump();
            bool refused = AppearanceFixtureReject<FloraAppearanceCapacityException>(() => AppearanceFixtureAcquire(session, catalog, root, requests));
            FloraAppearanceSession.Snapshot waiting = session.CaptureDiagnostics();
            Expect("appearance cancelled worker retains its full reservation", request.Ready.IsCanceled && refused
                && waiting.Acquisitions == 0 && waiting.Loading && waiting.TotalBytes == AppearanceFixtureEntryBytes
                && backend.LoadCompletions == 0 && backend.UploadCalls == 0,
                "losing a pending consumer cancels its request; the fake worker ignores cancellation until its gate opens, retaining the reservation");
            backend.Continue.Set();
            await PumpAppearanceUntil(session, () => session.CaptureDiagnostics().TotalBytes == 0, "appearance discarded worker drains");
            FloraAppearanceSession.Request replacement = AppearanceFixtureAcquire(session, catalog, AppearanceFixtureRoot(roots), requests);
            await PumpAppearanceUntil(session, () => replacement.Ready.IsCompleted, "appearance idle entry is reusable after worker drain");
            _ = await replacement.Ready;
            Expect("appearance readmission waits for actual cancelled load completion", backend.LoadCalls == 2
                && backend.LoadCompletions == 2 && backend.UploadCalls == 1 && backend.BindingCalls == 1,
                "only the replacement CPU result reaches native upload after the first worker and reservation retire");
        }
        finally { await RetireAppearanceFixture(session, backend, roots, requests); }
    }

    private async Task CheckFloraAppearanceStoppedWorker()
    {
        FloraAppearanceCatalog catalog = AppearanceFixtureCatalog();
        var backend = new AppearanceFixtureBackend(blockLoad: true);
        var session = new FloraAppearanceSession(catalog, AppearanceFixtureBudget(), backend.Operations);
        var roots = new List<Node>(); var requests = new List<FloraAppearanceSession.Request>();
        Node root = AppearanceFixtureRoot(roots);
        try
        {
            FloraAppearanceSession.Request request = AppearanceFixtureAcquire(session, catalog, root, requests);
            await Until(() => backend.Entered.IsSet, "appearance stopped worker starts");
            session.Stop(); session.Dispose(); session.Pump();
            Expect("appearance close remains nonblocking while cancelled CPU work is alive", request.Ready.IsCanceled
                && !session.Stopped.IsCompleted && session.CaptureDiagnostics().Loading
                && session.CaptureDiagnostics().TotalBytes == AppearanceFixtureEntryBytes
                && backend.LoadCompletions == 0 && GodotObject.IsInstanceValid(root) && !root.IsQueuedForDeletion(),
                "Stop and Dispose return with the worker gate still closed and leave caller-owned consumer nodes untouched");
            backend.Continue.Set();
            await PumpAppearanceUntil(session, () => session.Stopped.IsCompleted, "appearance stopped worker drains");
            Expect("appearance closed pending load never uploads", session.Stopped.IsCompletedSuccessfully
                && backend.UploadCalls == 0 && backend.BindingCalls == 0 && session.CaptureDiagnostics().TotalBytes == 0,
                "native creation is skipped after the CPU delegate actually terminates");
        }
        finally { await RetireAppearanceFixture(session, backend, roots, requests); }
    }

    private async Task CheckFloraAppearanceUploadQuarantine()
    {
        FloraAppearanceCatalog catalog = AppearanceFixtureCatalog();
        var backend = new AppearanceFixtureBackend { FailViaCreationCleanup = true, FailNativeDisposal = true };
        var session = new FloraAppearanceSession(catalog, AppearanceFixtureBudget(), backend.Operations);
        var roots = new List<Node>(); var requests = new List<FloraAppearanceSession.Request>();
        Node root = AppearanceFixtureRoot(roots);
        try
        {
            FloraAppearanceSession.Request request = AppearanceFixtureAcquire(session, catalog, root, requests);
            await PumpAppearanceUntil(session, () => request.Ready.IsCompleted, "appearance partial upload fails");
            _ = request.Ready.Exception;
            AppearanceFixtureOwner failed = backend.NativeOwner!;
            Expect("appearance adopts pending creation cleanup and attempts every other owner", request.Ready.IsFaulted
                && request.Ready.Exception!.InnerExceptions.Contains(backend.CreationFailure!)
                && session.CaptureDiagnostics().Quarantined && session.CaptureDiagnostics().TotalBytes == AppearanceFixtureEntryBytes
                && !session.Stopped.IsCompleted && failed.Attempts == 1 && GodotObject.IsInstanceValid(failed.Texture)
                && backend.OtherOwner?.Disposals == 1 && backend.Cpu?.Disposals == 1
                && backend.Shader != null && !GodotObject.IsInstanceValid(backend.Shader) && backend.BindingCalls == 0,
                "the uploader deliberately did not register PendingCleanup; the session adopts it, preserves the original creation error and retains its charge");
            int attempts = failed.Attempts;
            session.Stop(); session.Dispose(); session.Pump(); session.Pump();
            bool refused = AppearanceFixtureReject<InvalidOperationException>(() => AppearanceFixtureAcquire(session, catalog, root, requests));
            Expect("appearance quarantine blocks replacement and automatic retry", refused && failed.Attempts == attempts
                && session.CaptureDiagnostics().TotalBytes == AppearanceFixtureEntryBytes && !session.Stopped.IsCompleted,
                "ordinary pumping and repeated close do not loop over a failing native disposer or admit replacement work");
            session.RetryCleanup();
            Expect("appearance cleanup retry attempts only retained owners once", failed.Attempts == attempts + 1
                && backend.OtherOwner?.Attempts == 1 && backend.Cpu?.Disposals == 1 && !session.Stopped.IsCompleted,
                "a failed explicit retry neither re-disposes successful owners nor drops their entry reservation");
            failed.Fail = false; session.RetryCleanup();
            _ = session.Stopped.Exception;
            Expect("appearance recovered cleanup releases quarantine without erasing failure", session.Stopped.IsCompleted
                && session.Stopped.IsFaulted && !session.CaptureDiagnostics().Quarantined
                && session.CaptureDiagnostics().TotalBytes == 0 && !GodotObject.IsInstanceValid(failed.Texture)
                && failed.Disposals == 1 && backend.LoadCalls == 1 && backend.UploadCalls == 1,
                "the first failure remains reported, but the retained native owner and all charges are gone after recovery");
        }
        finally { await RetireAppearanceFixture(session, backend, roots, requests); }
    }

    private async Task CheckFloraAppearanceBindingFailure()
    {
        FloraAppearanceCatalog catalog = AppearanceFixtureCatalog();
        var backend = new AppearanceFixtureBackend(blockLoad: true) { FailFirstBinding = true };
        var session = new FloraAppearanceSession(catalog, AppearanceFixtureBudget(), backend.Operations);
        var roots = new List<Node>(); var requests = new List<FloraAppearanceSession.Request>();
        try
        {
            FloraAppearanceSession.Request first = AppearanceFixtureAcquire(session, catalog, AppearanceFixtureRoot(roots), requests);
            FloraAppearanceSession.Request second = AppearanceFixtureAcquire(session, catalog, AppearanceFixtureRoot(roots), requests);
            await Until(() => backend.Entered.IsSet, "appearance binding failure shared load starts");
            backend.Continue.Set();
            await PumpAppearanceUntil(session, () => first.Ready.IsCompleted && second.Ready.IsCompleted, "appearance binding failure completes pending requests");
            _ = first.Ready.Exception; _ = second.Ready.Exception;
            Expect("appearance binding failure cannot publish another pending view", first.Ready.IsFaulted && second.Ready.IsFaulted
                && backend.BindingCalls == 1 && backend.Materials.Count == 1 && !GodotObject.IsInstanceValid(backend.Materials[0])
                && session.CaptureDiagnostics().Acquisitions == 0 && session.CaptureDiagnostics().TotalBytes == 0
                && session.Stopped.IsCompleted,
                "the first material is owned before the injected setter-stage failure; fatal closure stops the same publication loop");
        }
        finally { await RetireAppearanceFixture(session, backend, roots, requests); }
    }

    private async Task CheckFloraAppearanceBindingQuarantine()
    {
        FloraAppearanceCatalog catalog = AppearanceFixtureCatalog();
        var backend = new AppearanceFixtureBackend { FailBindingDisposal = true };
        var session = new FloraAppearanceSession(catalog, AppearanceFixtureBudget(), backend.Operations);
        var roots = new List<Node>(); var requests = new List<FloraAppearanceSession.Request>();
        Node root = AppearanceFixtureRoot(roots);
        try
        {
            FloraAppearanceSession.Request request = AppearanceFixtureAcquire(session, catalog, root, requests);
            await PumpAppearanceUntil(session, () => request.Ready.IsCompleted, "appearance binding quarantine lease publishes");
            FloraAppearanceSession.Lease lease = await request.Ready;
            ShaderMaterial material = lease.ImpostorMaterial;
            Task retired = lease.Retire(Task.CompletedTask);
            root.QueueFree(); session.Stop();
            await PumpAppearanceUntil(session, () => session.CaptureDiagnostics().Quarantined, "appearance binding disposal quarantines");
            long held = session.CaptureDiagnostics().TotalBytes;
            Expect("appearance failed binding cleanup keeps its slot and shared bundle", !retired.IsCompleted && !session.Stopped.IsCompleted
                && session.CaptureDiagnostics().Acquisitions == 1 && session.CaptureDiagnostics().BindingBytes == AppearanceFixtureBindingBytes
                && held > 0 && !GodotObject.IsInstanceValid(material) && backend.NativeOwner?.Disposals == 0,
                "attempt-all releases the material but a failed binding owner still prevents shared native retirement");
            backend.BindingOwner!.Fail = false; session.RetryCleanup();
            _ = session.Stopped.Exception;
            Expect("appearance binding recovery finishes lease before shared teardown", retired.IsCompletedSuccessfully
                && session.Stopped.IsCompleted && session.CaptureDiagnostics().TotalBytes == 0
                && backend.BindingOwner.Disposals == 1 && backend.NativeOwner?.Disposals == 1,
                "an explicit retry clears the retained acquisition and then the now-unborrowed shared bundle");
        }
        finally { await RetireAppearanceFixture(session, backend, roots, requests); }
    }

    private async Task RetireAppearanceFixture(FloraAppearanceSession session, AppearanceFixtureBackend backend,
        List<Node> roots, List<FloraAppearanceSession.Request> requests)
    {
        backend.Continue.Set(); backend.AllowCleanup(); session.Stop();
        foreach (FloraAppearanceSession.Request request in requests)
        {
            request.Cancel();
            if (request.Ready.IsCompletedSuccessfully) _ = request.Ready.Result.Retire(Task.CompletedTask);
            else if (request.Ready.IsFaulted) _ = request.Ready.Exception;
        }
        foreach (Node root in roots)
            if (GodotObject.IsInstanceValid(root) && !root.IsQueuedForDeletion()) root.QueueFree();
        await Until(() =>
        {
            session.Pump();
            if (session.CaptureDiagnostics().Quarantined) session.RetryCleanup();
            return session.Stopped.IsCompleted && roots.All(root => !GodotObject.IsInstanceValid(root));
        }, "appearance fixture cleanup drains workers and native owners");
        _ = session.Stopped.Exception;
        Expect("appearance fixture releases its session reservations", session.CaptureDiagnostics().TotalBytes == 0
            && session.CaptureDiagnostics().Acquisitions == 0 && backend.LoadCalls == backend.LoadCompletions,
            "bounded final cleanup opens every CPU gate and waits for actual consumer deletion without forced collection");
        backend.Dispose();
    }

    private sealed class AppearanceFixtureCpu : FloraAppearanceSession.CpuWork
    {
        public int GeometryReleases, Disposals;
        public override void ReleaseGeometry() { if (GeometryReleases == 0) GeometryReleases = 1; }
        public override void Dispose() { if (Disposals == 0) { Disposals = 1; ReleaseGeometry(); } }
    }

    private sealed class AppearanceFixtureOwner : IDisposable
    {
        public ImageTexture? Texture;
        public bool Fail;
        public int Attempts, Disposals;
        public void Dispose()
        {
            Attempts++;
            if (Fail) throw new InvalidOperationException("Expected appearance fixture disposal failure.");
            if (Disposals != 0) return;
            if (Texture != null && GodotObject.IsInstanceValid(Texture)) Texture.Dispose();
            Disposals++;
        }
    }

    private sealed class AppearanceFixtureBackend : IDisposable
    {
        public readonly ManualResetEventSlim Entered = new(false), Continue;
        private int _loadCalls, _loadCompletions;
        public int LoadCalls => Volatile.Read(ref _loadCalls);
        public int LoadCompletions => Volatile.Read(ref _loadCompletions);
        public int UploadCalls, BindingCalls;
        public bool FailViaCreationCleanup, FailNativeDisposal, FailFirstBinding, FailBindingDisposal;
        public bool UploadOnMainThread = true, BindingOnMainThread = true;
        public AppearanceFixtureCpu? Cpu;
        public AppearanceFixtureOwner? NativeOwner, OtherOwner, BindingOwner;
        public FloraLibrary.CreationCleanupException? CreationFailure;
        public Shader? Shader;
        public readonly List<ShaderMaterial> Materials = new();
        public FloraAppearanceSession.Operations Operations { get; }

        public AppearanceFixtureBackend(bool blockLoad = false)
        {
            Continue = new ManualResetEventSlim(!blockLoad);
            Operations = new FloraAppearanceSession.Operations(Load, Upload, CreateBinding);
        }
        private FloraAppearanceSession.CpuWork Load(CancellationToken cancellation)
        {
            Interlocked.Increment(ref _loadCalls); Entered.Set();
            try
            {
                // Intentionally do not observe cancellation until the gate opens: reservations must cover real work.
                if (!Continue.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Appearance fixture load gate was not released.");
                cancellation.ThrowIfCancellationRequested();
                Cpu = new AppearanceFixtureCpu(); return Cpu;
            }
            finally { Interlocked.Increment(ref _loadCompletions); }
        }
        private FloraAppearanceSession.SharedAssets Upload(FloraAppearanceSession.CpuWork cpu, CancellationToken cancellation,
            FloraAppearanceSession.Retirement owners)
        {
            UploadCalls++; UploadOnMainThread &= OS.GetThreadCallerId() == OS.GetMainThreadId();
            cancellation.ThrowIfCancellationRequested();
            Shader = owners.Own(new Shader());
            Shader.Code = "shader_type spatial; uniform float fixture_value = 1.0; void fragment() { ALBEDO = vec3(fixture_value); }";
            NativeOwner = new AppearanceFixtureOwner { Fail = FailNativeDisposal };
            if (!FailViaCreationCleanup) owners.Own(NativeOwner);
            using (Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8))
            {
                image.Fill(Colors.White); NativeOwner.Texture = ImageTexture.CreateFromImage(image);
            }
            OtherOwner = owners.Own(new AppearanceFixtureOwner());
            if (FailViaCreationCleanup)
            {
                CreationFailure = new FloraLibrary.CreationCleanupException(
                    new InvalidOperationException("Expected appearance fixture partial upload failure."),
                    new InvalidOperationException("Expected appearance fixture prior cleanup failure."), NativeOwner);
                throw CreationFailure;
            }
            return new FloraAppearanceSession.SharedAssets(new FloraLibrary(), NativeOwner.Texture!, NativeOwner.Texture!, Shader);
        }
        private ShaderMaterial CreateBinding(FloraAppearanceSession.SharedAssets assets, FloraAppearanceSession.Retirement owners)
        {
            BindingCalls++; BindingOnMainThread &= OS.GetThreadCallerId() == OS.GetMainThreadId();
            ShaderMaterial material = owners.Own(new ShaderMaterial()); Materials.Add(material);
            material.Shader = assets.ImpostorShader; material.SetShaderParameter("fixture_value", 1f);
            if (FailBindingDisposal) BindingOwner = owners.Own(new AppearanceFixtureOwner { Fail = true });
            if (FailFirstBinding && BindingCalls == 1) throw new InvalidOperationException("Expected appearance fixture partial binding failure.");
            return material;
        }
        public void AllowCleanup()
        {
            if (NativeOwner != null) NativeOwner.Fail = false;
            if (OtherOwner != null) OtherOwner.Fail = false;
            if (BindingOwner != null) BindingOwner.Fail = false;
        }
        public void Dispose() { Entered.Dispose(); Continue.Dispose(); }
    }
}
