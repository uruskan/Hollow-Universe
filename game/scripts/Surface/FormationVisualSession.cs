using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// Explicit admission allowances, not measurements or hard limits on managed heap or driver allocation. The
/// caller separately owns and reserves the already qualified package/catalog, borrowed CUSTOM0 CPU arrays and
/// any future override materials. Those caller reservations remain live through this session's retirement.
/// CPU and upload allowances remain charged until the upload job releases its authenticated data and scratch.
/// </summary>
internal sealed class FormationVisualSessionBudget
{
    internal FormationVisualSessionBudget(int maxAcquisitions, int maxConsumerRoots, long maxReservedBytes,
        long cpuAllowanceBytes, long uploadAllowanceBytes, long nativeAllowanceBytes, long bindingAllowanceBytes,
        long consumerRootAllowanceBytes)
    {
        if (maxAcquisitions is < 1 or > 32 || maxConsumerRoots is < 1 or > 128 ||
            maxReservedBytes is < 1 or > 34359738368L || cpuAllowanceBytes is < 1 or > 4294967296L ||
            uploadAllowanceBytes is < 1 or > 4294967296L || nativeAllowanceBytes is < 1 or > 17179869184L ||
            bindingAllowanceBytes is < 1 or > 1048576 || consumerRootAllowanceBytes is < 1 or > 65536)
            throw new FormationCapacityException("Invalid formation session reservations.");
        MaxAcquisitions = maxAcquisitions;
        MaxConsumerRoots = maxConsumerRoots;
        MaxReservedBytes = maxReservedBytes;
        CpuAllowanceBytes = cpuAllowanceBytes;
        UploadAllowanceBytes = uploadAllowanceBytes;
        NativeAllowanceBytes = nativeAllowanceBytes;
        BindingAllowanceBytes = bindingAllowanceBytes;
        ConsumerRootAllowanceBytes = consumerRootAllowanceBytes;
        EntryReservationBytes = checked(cpuAllowanceBytes + uploadAllowanceBytes + nativeAllowanceBytes);
    }

    internal int MaxAcquisitions { get; }
    internal int MaxConsumerRoots { get; }
    internal long MaxReservedBytes { get; }
    internal long CpuAllowanceBytes { get; }
    internal long UploadAllowanceBytes { get; }
    internal long NativeAllowanceBytes { get; }
    internal long BindingAllowanceBytes { get; }
    internal long ConsumerRootAllowanceBytes { get; }
    internal long EntryReservationBytes { get; }
}

/// <summary>
/// Inactive main-thread owner for one independently approved receipt/profile. Admission reserves the configured
/// CPU, native and upload ceilings before CPU preflight or load. One CPU task feeds one incremental native upload;
/// leases share immutable templates and surface addresses. No world, qualification, import, fallback or cache is made.
///
/// Pump must outlive every consumer, including after Stop. A published lease must explicitly Retire with its worker
/// completion task; all registered native roots must be invalid before that lease releases its reservation. Stop
/// does not free live consumers. Caller-owned per-view/per-piece overrides may borrow these resources only under
/// registered roots, and their separate owner/dependency reservations must last through their retirement.
/// </summary>
internal sealed class FormationVisualSession : IDisposable
{
    internal readonly record struct Snapshot(long CpuBytes, long UploadBytes, long NativeBytes, long BindingBytes,
        long ConsumerRootBytes, long TotalBytes, int Acquisitions, int ConsumerRoots, bool Loading, bool Uploading,
        bool Ready, bool Quarantined, int LoadStarts, int UploadStarts);

    private FormationPackage? _package;
    private FormationMaterialProfile? _profile;
    private readonly string _directory, _receipt;
    private readonly FormationVisualLimits _visualLimits;
    private readonly FormationNativeLimits _nativeLimits;
    private readonly FormationVisualSessionBudget _budget;
    private readonly List<Request> _requests = new();
    private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<FormationVisualData>? _load;
    private CancellationTokenSource? _cancellation;
    private FormationVisualLoader.UploadJob? _upload;
    private FormationVisualLoader.Owner? _owner;
    private bool _reserved, _cpuReserved, _uploadReserved, _closing, _discard, _cleanupFailed, _pumping;
    private Exception? _failure;
    private int _loadStarts, _uploadStarts;

    internal FormationVisualSession(FormationPackage package, string sourceDirectory, string expectedReceiptSha256,
        FormationMaterialProfile profile, string expectedProfileIdentity, FormationVisualLimits visualLimits,
        FormationNativeLimits nativeLimits, FormationVisualSessionBudget budget)
    {
        MainThread();
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(sourceDirectory);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(visualLimits);
        ArgumentNullException.ThrowIfNull(nativeLimits);
        ArgumentNullException.ThrowIfNull(budget);
        _receipt = Digest(expectedReceiptSha256);
        if (_receipt != Digest(package.ReceiptSha256) || _receipt != Digest(profile.ApprovedReceiptSha256))
            throw new FormationValidationException("Formation session receipt and material approval differ.");
        if (Digest(expectedProfileIdentity) != Digest(profile.Identity))
            throw new FormationValidationException("Formation material profile does not match its independent approval pin.");
        if (visualLimits.MaxReservationBytes < 1 || nativeLimits.MaxNativeBytes < 1 || nativeLimits.MaxUploadBytes < 1 ||
            budget.CpuAllowanceBytes < visualLimits.MaxReservationBytes ||
            budget.NativeAllowanceBytes < nativeLimits.MaxNativeBytes || budget.UploadAllowanceBytes < nativeLimits.MaxUploadBytes)
            throw new FormationCapacityException("Session reservations must cover every configured CPU/native/upload ceiling.");
        _package = package;
        _profile = profile;
        _directory = Path.GetFullPath(sourceDirectory);
        _visualLimits = visualLimits with { };
        _nativeLimits = nativeLimits with { };
        _budget = budget;
    }

    internal Task Stopped => _stopped.Task;
    internal Exception? Failure { get { MainThread(); return _failure; } }

    /// <summary>Registers a live root before exposing resources; rejection dispatches no payload or native work.</summary>
    internal Request Acquire(Node consumerRoot, CancellationToken cancellation = default)
    {
        MainThread();
        if (_closing || _cleanupFailed) throw new InvalidOperationException("Formation session is closing or quarantined.");
        if (_discard) throw new FormationCapacityException("A cancelled formation load is still retiring.");
        cancellation.ThrowIfCancellationRequested();
        LiveRoot(consumerRoot);
        if (_requests.Count >= _budget.MaxAcquisitions || ConsumerCount >= _budget.MaxConsumerRoots)
            throw new FormationCapacityException("Formation acquisition or consumer-root slots are occupied, including retirement.");
        long required = checked((_reserved ? CaptureDiagnostics().TotalBytes : _budget.EntryReservationBytes) +
            _budget.BindingAllowanceBytes + _budget.ConsumerRootAllowanceBytes);
        if (required > _budget.MaxReservedBytes)
            throw new FormationCapacityException("Formation session reservation would exceed its byte budget.");

        var request = new Request(this, consumerRoot, cancellation);
        _requests.Add(request);
        if (!_reserved)
        {
            // Charge the entire construction envelope BEFORE even the metadata/hash preflight task is dispatched.
            _reserved = _cpuReserved = _uploadReserved = true;
            try
            {
                _cancellation = new CancellationTokenSource();
                CancellationToken token = _cancellation.Token;
                FormationPackage package = _package!;
                string directory = _directory, receipt = _receipt;
                FormationVisualLimits limits = _visualLimits;
                // This closure contains pure inputs only; no session, profile, request, Node or native owner.
                _load = Task.Run(() => LoadCpu(package, directory, receipt, limits, token));
                _loadStarts++;
            }
            catch
            {
                _requests.Remove(request);
                request.Detach();
                ClearEntry();
                throw;
            }
        }
        return request;
    }

    internal Snapshot CaptureDiagnostics()
    {
        MainThread();
        long cpu = _cpuReserved ? _budget.CpuAllowanceBytes : 0;
        long upload = _uploadReserved ? _budget.UploadAllowanceBytes : 0;
        long native = _reserved ? _budget.NativeAllowanceBytes : 0;
        long bindings = checked(_requests.Count * _budget.BindingAllowanceBytes);
        int roots = ConsumerCount;
        long rootBytes = checked(roots * _budget.ConsumerRootAllowanceBytes);
        return new(cpu, upload, native, bindings, rootBytes, checked(cpu + upload + native + bindings + rootBytes),
            _requests.Count, roots, _load != null, _upload != null, _owner != null && !_closing && !_discard,
            _cleanupFailed, _loadStarts, _uploadStarts);
    }

    private int ConsumerCount => _requests.Sum(request => request.Roots.Count);

    /// <summary>Nonblocking close. Published leases still need Retire and an ongoing main-thread pump.</summary>
    internal void Stop()
    {
        MainThread();
        _closing = true;
        _cancellation?.Cancel();
    }

    public void Dispose() => Stop();
    internal void Pump() => PumpCore(retryCleanup: false);

    /// <summary>One cleanup attempt only; failed owners and their reservation remain quarantined.</summary>
    internal void RetryCleanup()
    {
        MainThread();
        if (_cleanupFailed) PumpCore(retryCleanup: true);
    }

    private void PumpCore(bool retryCleanup)
    {
        MainThread();
        if (_pumping) throw new InvalidOperationException("Formation session pumping is not reentrant.");
        _pumping = true;
        try
        {
            CancelPending();
            if (_requests.Count == 0 && _owner == null && (_load != null || _upload != null))
            {
                _discard = true;
                _cancellation?.Cancel();
            }
            CompleteLoad();
            AdvanceUpload();
            if (_owner != null && !_closing && !_discard)
            {
                // Continuations never run inline here. Cancellation after publication belongs to the lease holder.
                foreach (Request request in _requests)
                {
                    if (request.State != RequestState.Pending || request.Cancelled) continue;
                    request.State = RequestState.Live;
                    request.Completion.TrySetResult(new Lease(request));
                }
            }
            CancelPending();
            RetireBindings();
            if (_load == null && _requests.Count == 0 && (_closing || _discard)) RetireEntry(retryCleanup);
            if (_closing && !_reserved && _load == null && _requests.Count == 0)
            {
                _package = null;
                _profile = null;
                if (_failure == null) _stopped.TrySetResult(true);
                else _stopped.TrySetException(_failure);
            }
        }
        finally { _pumping = false; }
    }

    private void CancelPending()
    {
        for (int i = _requests.Count - 1; i >= 0; i--)
        {
            Request request = _requests[i];
            if (request.State != RequestState.Pending) continue;
            bool abandoned = request.Roots.Any(root => !GodotObject.IsInstanceValid(root) || root.IsQueuedForDeletion());
            if (!(_closing || request.Cancelled || abandoned)) continue;
            if (_failure != null) request.Completion.TrySetException(_failure);
            else request.Completion.TrySetCanceled();
            request.Detach();
            _requests.RemoveAt(i);
        }
    }

    private void CompleteLoad()
    {
        if (_load is not { IsCompleted: true } completed) return;
        _load = null;
        try
        {
            if (completed.IsCompletedSuccessfully)
            {
                if (_closing || _discard) return;
                FormationVisualData data = completed.Result;
                if (data.Sizing.PeakReservationBytes > _budget.CpuAllowanceBytes)
                    throw new FormationCapacityException("Loaded formation exceeded its prior CPU reservation.");
                FormationVisualLoader.NativePlan plan = FormationVisualLoader.Preflight(_package!, data, _profile!, _nativeLimits);
                if (plan.NativeReservationBytes > _budget.NativeAllowanceBytes ||
                    plan.UploadReservationBytes > _budget.UploadAllowanceBytes)
                    throw new FormationCapacityException("Native formation plan exceeded its prior reservation.");
                _upload = FormationVisualLoader.BeginUpload(plan, _cancellation!.Token);
                _uploadStarts++;
            }
            else if (completed.IsFaulted) HandleFailure(completed.Exception!);
            else _discard = true;
        }
        catch (Exception error) { HandleFailure(error); }
    }

    private void AdvanceUpload()
    {
        if (_upload == null || _closing || _discard || _cleanupFailed) return;
        try
        {
            // The finite decoder/driver call inside one step cannot be preempted; cancellation is between steps.
            _upload.Pump(1);
            if (_upload.Failure != null) { HandleFailure(_upload.Failure); return; }
            if (!_upload.IsCompleted) return;
            _owner = _upload.TakeCompletedOwner();
            try
            {
                if (!_upload.TryDispose(out Exception? error))
                {
                    _cleanupFailed = true;
                    Fail(error ?? new InvalidOperationException("Transferred formation upload cleanup failed."));
                    return;
                }
            }
            catch (Exception error) { _cleanupFailed = true; Fail(error); return; }
            _upload = null;
            _cancellation?.Dispose();
            _cancellation = null;
            _cpuReserved = _uploadReserved = false;
        }
        catch (Exception error) { HandleFailure(error); }
    }

    private void RetireBindings()
    {
        for (int i = _requests.Count - 1; i >= 0; i--)
        {
            Request request = _requests[i];
            if (request.State != RequestState.Retiring || request.Workers?.IsCompleted != true ||
                request.Roots.Any(GodotObject.IsInstanceValid)) continue;
            Exception? workerError = request.Workers.Exception;
            bool cancelled = request.Workers.IsCanceled;
            request.Detach();
            _requests.RemoveAt(i);
            if (workerError != null) request.RetirementCompletion.TrySetException(workerError);
            else if (cancelled) request.RetirementCompletion.TrySetCanceled();
            else request.RetirementCompletion.TrySetResult(true);
        }
    }

    private void RetireEntry(bool retryCleanup)
    {
        if (_cleanupFailed && !retryCleanup) return;
        Exception? failure = null;
        // Attempt both independent owners even when the first fails. Each retains its own failed private objects.
        if (_upload != null)
        {
            try
            {
                if (_upload.TryDispose(out Exception? error)) _upload = null;
                else failure = error ?? new InvalidOperationException("Formation upload cleanup failed.");
            }
            catch (Exception error) { failure = error; }
        }
        if (_owner != null)
        {
            try
            {
                if (_owner.TryDispose(out Exception? error)) _owner = null;
                else failure ??= error ?? new InvalidOperationException("Formation resource cleanup failed.");
            }
            catch (Exception error) { failure ??= error; }
        }
        if (failure != null)
        {
            _cleanupFailed = true;
            Fail(failure);
            return;
        }
        ClearEntry();
    }

    private void ClearEntry()
    {
        _owner = null;
        _upload = null;
        _cancellation?.Dispose();
        _cancellation = null;
        _reserved = _cpuReserved = _uploadReserved = _discard = _cleanupFailed = false;
    }

    private void HandleFailure(Exception error)
    {
        bool cancelled = error is OperationCanceledException || error is AggregateException aggregate &&
            aggregate.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException);
        if (_cancellation?.IsCancellationRequested == true && cancelled) _discard = true;
        else Fail(error);
    }

    private void Fail(Exception error)
    {
        _failure ??= error; // Retain one error, never an unbounded history of failed cleanup retries.
        _closing = true;
        _cancellation?.Cancel();
    }

    private static FormationVisualData LoadCpu(FormationPackage package, string directory, string receipt,
        FormationVisualLimits limits, CancellationToken cancellation)
    {
        FormationVisualPreflight preflight = FormationVisualDataLoader.Preflight(package, directory, receipt, limits, cancellation);
        cancellation.ThrowIfCancellationRequested();
        return FormationVisualDataLoader.Load(preflight, cancellation);
    }

    private static string Digest(string value)
    {
        if (value == null || value.Length != 64 || value.Any(character =>
            character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f') and not (>= 'A' and <= 'F')))
            throw new FormationValidationException("An independently supplied SHA-256 receipt pin is required.");
        return value.ToLowerInvariant();
    }

    private static void MainThread()
    {
        if (OS.GetThreadCallerId() != OS.GetMainThreadId())
            throw new InvalidOperationException("Formation sessions are acquired, pumped and retired on Godot's main thread.");
    }

    private static void LiveRoot(Node root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!GodotObject.IsInstanceValid(root) || root.IsQueuedForDeletion())
            throw new ArgumentException("A formation consumer root must be live and not queued for deletion.", nameof(root));
    }

    internal enum RequestState : byte { Pending, Live, Retiring, Retired }

    internal sealed class Request
    {
        internal FormationVisualSession? Session;
        private CancellationToken _cancellation;
        private int _cancelled;
        internal readonly TaskCompletionSource<Lease> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> RetirementCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<Node> Roots = new();
        internal RequestState State;
        internal Task? Workers;

        internal Request(FormationVisualSession session, Node root, CancellationToken cancellation)
        { Session = session; Roots.Add(root); _cancellation = cancellation; }
        internal Task<Lease> Ready => Completion.Task;
        /// <summary>Thread-safe pending cancellation. A published lease must instead be retired explicitly.</summary>
        internal void Cancel() => Interlocked.Exchange(ref _cancelled, 1);
        internal bool Cancelled => Volatile.Read(ref _cancelled) != 0 || _cancellation.IsCancellationRequested;
        internal void Detach()
        {
            State = RequestState.Retired;
            Session = null;
            _cancellation = default;
            Roots.Clear();
            Workers = null;
        }
    }

    internal sealed class Lease
    {
        private readonly Request _request;
        internal Lease(Request request) => _request = request;
        private FormationVisualSession Active()
        {
            MainThread();
            if (_request.State != RequestState.Live || _request.Session == null)
                throw new ObjectDisposedException(nameof(Lease));
            return _request.Session;
        }

        /// <summary>Borrowed immutable meshes/materials. Never change surfaces, dispose or mutate their parameters.</summary>
        internal IReadOnlyList<FormationVisualTemplate> Templates => Active()._owner!.Templates;
        /// <summary>Immutable addresses for future caller-owned per-piece surface overrides; no shared mutation.</summary>
        internal IReadOnlyList<FormationVisualSurfaceSlot> SurfaceSlots => Active()._owner!.SurfaceSlots;
        internal Task Retired => _request.RetirementCompletion.Task;

        /// <summary>Register every independent native root before giving it these borrowed resources.</summary>
        internal void AddConsumer(Node root)
        {
            FormationVisualSession session = Active();
            LiveRoot(root);
            if (_request.Roots.Any(candidate => ReferenceEquals(candidate, root))) return;
            if (session.ConsumerCount >= session._budget.MaxConsumerRoots ||
                checked(session.CaptureDiagnostics().TotalBytes + session._budget.ConsumerRootAllowanceBytes) > session._budget.MaxReservedBytes)
                throw new FormationCapacityException("Formation consumer-root count or byte reservation exceeded.");
            _request.Roots.Add(root);
        }

        /// <summary>Revokes access immediately; retirement waits for workers AND actual destruction of every root.</summary>
        internal Task Retire(Task workersStopped)
        {
            MainThread();
            ArgumentNullException.ThrowIfNull(workersStopped);
            if (_request.State is RequestState.Retiring or RequestState.Retired) return Retired;
            _ = Active();
            _request.Workers = workersStopped;
            _request.State = RequestState.Retiring;
            return Retired;
        }
    }
}
