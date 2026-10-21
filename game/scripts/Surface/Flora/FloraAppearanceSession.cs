using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Surface;

namespace HollowUniverse.Surface.Flora;

/// <summary>
/// Explicit admission reservations for one approved appearance. Payload is exact; the other byte allowances are
/// declared by the host, not measurements or hard limits on Godot/driver allocation. Geometry includes CPU builders
/// and import inspection. Upload includes managed copies, Image storage and marshalling; native includes imported
/// meshes/textures as well as the seven images. Borrowed GroundLayers/shaders remain charged to their existing owner.
/// </summary>
public sealed class FloraAppearanceSessionBudget
{
    public FloraAppearanceSessionBudget(int maxAcquisitions, int maxConsumerRoots, long maxReservedBytes,
        long geometryAllowanceBytes, long uploadAllowanceBytes, long nativeAllowanceBytes, long bindingAllowanceBytes)
    {
        long minimumUpload = checked(3L * FloraAppearanceSchema.Images.Max(image => image.ByteLength));
        if (maxAcquisitions is < 1 or > 32 || maxConsumerRoots is < 1 or > 128 || maxReservedBytes < 1 ||
            geometryAllowanceBytes is < 1 or > 2147483648L || uploadAllowanceBytes < minimumUpload ||
            uploadAllowanceBytes > 2147483648L || nativeAllowanceBytes < FloraAppearanceSchema.PayloadBytes ||
            nativeAllowanceBytes > 8589934592L || bindingAllowanceBytes is < 1 or > 1048576)
            throw new FloraAppearanceCapacityException("Invalid appearance session reservations.");
        MaxAcquisitions = maxAcquisitions; MaxConsumerRoots = maxConsumerRoots; MaxReservedBytes = maxReservedBytes;
        GeometryAllowanceBytes = geometryAllowanceBytes; UploadAllowanceBytes = uploadAllowanceBytes;
        NativeAllowanceBytes = nativeAllowanceBytes; BindingAllowanceBytes = bindingAllowanceBytes;
        EntryReservationBytes = checked(FloraAppearanceSchema.PayloadBytes + geometryAllowanceBytes +
            uploadAllowanceBytes + nativeAllowanceBytes);
    }
    public int MaxAcquisitions { get; }
    public int MaxConsumerRoots { get; }
    public long MaxReservedBytes { get; }
    public long GeometryAllowanceBytes { get; }
    public long UploadAllowanceBytes { get; }
    public long NativeAllowanceBytes { get; }
    public long BindingAllowanceBytes { get; }
    public long EntryReservationBytes { get; }
}

/// <summary>
/// Main-thread, finite session owner: one independently pinned appearance, one CPU job and one native upload.
/// Call Pump on the main thread until Stopped completes, including after Stop/Dispose. Stop never waits for jobs
/// or assumes that a live lease's consumers have detached. Each published lease must explicitly Retire with its
/// worker-completion task; all registered native consumer roots must actually be freed before resources retire.
///
/// Borrowed GroundLayers must remain valid until Stopped. Library/mesh/texture references are borrowed and immutable
/// by contract: never Dispose, rebake, change surfaces,
/// or modify their parameters. Each lease owns its mutable impostor material. Other view-owned materials may borrow
/// the immutable atlas while their consumer roots are registered. No world, scatter RNG, save or fallback is created.
/// </summary>
public sealed class FloraAppearanceSession : IDisposable
{
    public readonly record struct Snapshot(long PayloadBytes, long GeometryBytes, long UploadBytes, long NativeBytes,
        long BindingBytes, long TotalBytes, int Acquisitions, bool Loading, bool Ready, bool Quarantined,
        int LoadStarts, int UploadStarts);

    private readonly FloraAppearanceCatalog _catalog;
    private readonly FloraAppearanceSessionBudget _budget;
    private Operations? _operations;
    private readonly List<Request> _requests = new();
    private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<CpuWork>? _load;
    private CancellationTokenSource? _loadCancellation;
    private Retirement? _entryOwners;
    private CpuWork? _cpu;
    private SharedAssets? _assets;
    private bool _reserved, _geometryReserved, _uploadReserved, _closing, _discardLoad, _entryCleanupFailed, _pumping;
    private Exception? _failure;
    private int _loadStarts, _uploadStarts;

    public FloraAppearanceSession(FloraAppearanceCatalog catalog, string packageDirectory,
        FloraAppearanceSessionBudget budget, FloraAppearanceLimits packageLimits, FloraAppearanceInputLimits inputLimits,
        TextureLayered groundAlbedo, TextureLayered groundNormal)
        : this(catalog, budget, Production(catalog, packageDirectory, budget, packageLimits, inputLimits, groundAlbedo, groundNormal)) { }

    // Injected only by the isolated native lifetime fixture. Production always uses the authenticated loader below.
    internal FloraAppearanceSession(FloraAppearanceCatalog catalog, FloraAppearanceSessionBudget budget, Operations operations)
    {
        MainThread();
        ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(operations);
        if (catalog.Appearances.Count != 1)
            throw new FloraAppearanceCapacityException("This session admits exactly one explicitly approved appearance.");
        _catalog = catalog; _budget = budget; _operations = operations;
    }

    public Task Stopped => _stopped.Task;

    /// <summary>Resolves only the supplied immutable catalog. Rejection allocates no payload and dispatches no work.</summary>
    public Request Acquire(FloraAppearanceWorldKey world, string visualVersion, string catalogIdentity,
        Node consumerRoot, CancellationToken cancellation = default)
    {
        MainThread();
        if (_closing || Quarantined) throw new InvalidOperationException("Appearance session is closing or quarantined.");
        if (_discardLoad) throw new FloraAppearanceCapacityException("The cancelled appearance load is still retiring.");
        FloraAppearanceResolution resolution = _catalog.Resolve(world, visualVersion, catalogIdentity);
        if (resolution.Status != FloraAppearanceResolutionStatus.Exact)
            throw new FloraAppearanceValidationException("Selected appearance is unsupported: " + resolution.Status);
        cancellation.ThrowIfCancellationRequested();
        LiveRoot(consumerRoot);
        if (_requests.Count >= _budget.MaxAcquisitions)
            throw new FloraAppearanceCapacityException("Appearance acquisition slots are occupied, including retirement.");
        long required = checked((_reserved ? CaptureDiagnostics().TotalBytes : _budget.EntryReservationBytes) + _budget.BindingAllowanceBytes);
        if (required > _budget.MaxReservedBytes)
            throw new FloraAppearanceCapacityException("Appearance session reservation would exceed its byte budget.");
        var request = new Request(this, consumerRoot, cancellation);
        _requests.Add(request);
        if (!_reserved)
        {
            // Reserve the entire exact payload and every declared construction/native allowance BEFORE dispatch.
            _reserved = _geometryReserved = _uploadReserved = true;
            _entryOwners = new Retirement();
            _loadCancellation = new CancellationTokenSource();
            CancellationToken token = _loadCancellation.Token;
            Func<CancellationToken, CpuWork> load = _operations!.Load;
            try
            {
                // The worker closure contains no session, request, view, Node or borrowed native resource.
                _load = Task.Run(() => load(token));
                _loadStarts++;
            }
            catch
            {
                _requests.Remove(request); request.Detach();
                ClearEntry();
                throw;
            }
        }
        return request;
    }

    public Snapshot CaptureDiagnostics()
    {
        MainThread();
        long payload = _reserved ? FloraAppearanceSchema.PayloadBytes : 0;
        long geometry = _geometryReserved ? _budget.GeometryAllowanceBytes : 0;
        long upload = _uploadReserved ? _budget.UploadAllowanceBytes : 0;
        long native = _reserved ? _budget.NativeAllowanceBytes : 0;
        long bindings = checked(_requests.Count * _budget.BindingAllowanceBytes);
        return new(payload, geometry, upload, native, bindings, checked(payload + geometry + upload + native + bindings),
            _requests.Count, _load != null, _assets != null && !_closing, Quarantined, _loadStarts, _uploadStarts);
    }

    private bool Quarantined => _entryCleanupFailed || _requests.Any(request => request.CleanupFailed);

    /// <summary>Nonblocking close. Published leases still require Retire; Pump must continue until Stopped.</summary>
    public void Stop()
    {
        MainThread();
        _closing = true;
        _loadCancellation?.Cancel();
    }
    public void Dispose() => Stop();
    public void Pump() => PumpCore(retryCleanup: false);

    /// <summary>One attempt for each remaining failed owner; no retry loop, new upload or replacement admission.</summary>
    public void RetryCleanup()
    {
        MainThread();
        if (Quarantined) PumpCore(retryCleanup: true);
    }

    private void PumpCore(bool retryCleanup)
    {
        MainThread();
        if (_pumping) throw new InvalidOperationException("Appearance session pumping is not reentrant.");
        _pumping = true;
        try
        {
            CancelPending();
            if (_load != null && _requests.Count == 0)
            {
                _discardLoad = true;
                _loadCancellation!.Cancel();
            }
            CompleteLoad();
            if (_assets != null && !_closing)
            {
                // No callbacks run inline from Ready. A cancellation racing publication is handled by the caller
                // taking ownership of the resulting lease; cancellation applies only while acquisition is pending.
                foreach (Request request in _requests.ToArray())
                {
                    if (_closing) break;
                    if (request.State == RequestState.Pending && !request.Cancelled) Publish(request);
                }
            }
            CancelPending();
            RetireBindings(retryCleanup);
            if (_load == null && _requests.Count == 0 && (_closing || _discardLoad)) RetireEntry(retryCleanup);
            if (_closing && !_reserved && _requests.Count == 0 && _load == null)
            {
                _operations = null; // release upload closures and their borrowed GroundLayers references, too.
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
            request.Detach(); _requests.RemoveAt(i);
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
                _cpu = _entryOwners!.Own(completed.Result);
                if (_closing || _discardLoad) return;
                _uploadStarts++;
                _assets = _operations!.Upload(_cpu, _loadCancellation!.Token, _entryOwners);
                _cpu.ReleaseGeometry();
                _geometryReserved = _uploadReserved = false;
            }
            else if (completed.IsFaulted)
            {
                Exception error = completed.Exception!;
                bool expectedCancellation = _loadCancellation!.IsCancellationRequested &&
                    completed.Exception!.Flatten().InnerExceptions.All(inner => inner is OperationCanceledException);
                if (!expectedCancellation) Fail(error);
                else _discardLoad = true;
            }
            else _discardLoad = true;
        }
        catch (FloraLibrary.CreationCleanupException error)
        {
            // Even if an injected uploader fails to register it, the creation exception's retained owner is adopted.
            _entryOwners!.Own(error.PendingCleanup);
            Fail(error);
        }
        catch (Exception error) { Fail(error); }
        finally { _loadCancellation?.Dispose(); _loadCancellation = null; }
    }

    private void Publish(Request request)
    {
        try
        {
            request.Material = _operations!.CreateBinding(_assets!, request.Owners);
            request.State = RequestState.Live;
            request.Completion.TrySetResult(new Lease(request));
        }
        catch (Exception error)
        {
            // No native references were exposed, so this failed binding has no consumer/work barrier.
            request.Completion.TrySetException(error);
            request.State = RequestState.Retiring;
            request.Workers = Task.CompletedTask;
            request.Roots.Clear();
            Fail(error);
        }
    }

    private void RetireBindings(bool retryCleanup)
    {
        for (int i = _requests.Count - 1; i >= 0; i--)
        {
            Request request = _requests[i];
            if (request.State != RequestState.Retiring || request.Workers?.IsCompleted != true ||
                request.Roots.Any(GodotObject.IsInstanceValid) || request.CleanupFailed && !retryCleanup) continue;
            if (!request.Owners.TryDispose(out Exception? error))
            {
                request.CleanupFailed = true;
                Fail(error!);
                continue;
            }
            request.CleanupFailed = false;
            Exception? workerError = request.Workers.Exception;
            bool cancelled = request.Workers.IsCanceled;
            request.Detach(); _requests.RemoveAt(i);
            if (workerError != null) request.RetirementCompletion.TrySetException(workerError);
            else if (cancelled) request.RetirementCompletion.TrySetCanceled();
            else request.RetirementCompletion.TrySetResult(true);
        }
    }

    private void RetireEntry(bool retryCleanup)
    {
        if (_entryCleanupFailed && !retryCleanup) return;
        if (_entryOwners != null && !_entryOwners.TryDispose(out Exception? error))
        {
            _entryCleanupFailed = true;
            Fail(error!);
            return;
        }
        ClearEntry();
    }

    private void ClearEntry()
    {
        _cpu = null; _assets = null; _entryOwners = null;
        _loadCancellation?.Dispose(); _loadCancellation = null;
        _reserved = _geometryReserved = _uploadReserved = _discardLoad = _entryCleanupFailed = false;
    }

    private void Fail(Exception error)
    {
        _failure ??= error; // bounded: keep the first failure, never append retry history.
        _closing = true;
        _loadCancellation?.Cancel();
    }

    private static void MainThread()
    {
        if (OS.GetThreadCallerId() != OS.GetMainThreadId())
            throw new InvalidOperationException("Appearance sessions are pumped and retired on Godot's main thread.");
    }
    private static void LiveRoot(Node root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!GodotObject.IsInstanceValid(root) || root.IsQueuedForDeletion())
            throw new ArgumentException("An appearance consumer root must be live and not queued for deletion.", nameof(root));
    }

    internal enum RequestState : byte { Pending, Live, Retiring, Retired }

    public sealed class Request
    {
        internal FloraAppearanceSession? _session;
        private CancellationToken _cancellation;
        private int _cancelled;
        private readonly TaskCompletionSource<Lease> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> _retirementCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Retirement _owners = new();
        internal readonly List<Node> _roots = new();
        internal RequestState _state;
        internal ShaderMaterial? _material;
        internal Task? _workers;
        private bool _cleanupFailed;

        internal Request(FloraAppearanceSession session, Node root, CancellationToken cancellation)
        { _session = session; _roots.Add(root); _cancellation = cancellation; }
        public Task<Lease> Ready => _completion.Task;
        /// <summary>Thread-safe cancellation of a pending request; published leases must Retire explicitly.</summary>
        public void Cancel() => Interlocked.Exchange(ref _cancelled, 1);

        // Assembly-internal state is used by the containing session and sibling Lease, never by view consumers.
        internal bool Cancelled => Volatile.Read(ref _cancelled) != 0 || _cancellation.IsCancellationRequested;
        internal TaskCompletionSource<Lease> Completion => _completion;
        internal TaskCompletionSource<bool> RetirementCompletion => _retirementCompletion;
        internal Retirement Owners => _owners;
        internal List<Node> Roots => _roots;
        internal RequestState State { get => _state; set => _state = value; }
        internal ShaderMaterial? Material { get => _material; set => _material = value; }
        internal Task? Workers { get => _workers; set => _workers = value; }
        internal bool CleanupFailed { get => _cleanupFailed; set => _cleanupFailed = value; }
        internal void Detach()
        {
            _state = RequestState.Retired; _session = null; _cancellation = default;
            _roots.Clear(); _material = null; _workers = null;
        }
    }

    public sealed class Lease
    {
        private readonly Request _request;
        internal Lease(Request request) => _request = request;
        private FloraAppearanceSession Active()
        {
            MainThread();
            if (_request._state != RequestState.Live || _request._session == null)
                throw new ObjectDisposedException(nameof(Lease));
            return _request._session;
        }
        /// <summary>Borrowed immutable library. Its Rock binds the session's fixed borrowed GroundLayers arrays.</summary>
        public FloraLibrary Library => Active()._assets!.Library;
        /// <summary>This lease's mutable material; never shared with another view or disposed by the caller.</summary>
        public ShaderMaterial ImpostorMaterial { get { _ = Active(); return _request._material!; } }
        public Task Retired => _request._retirementCompletion.Task;

        /// <summary>Register every independent native consumer root before handing it borrowed appearance resources.</summary>
        public void AddConsumer(Node root)
        {
            FloraAppearanceSession session = Active(); LiveRoot(root);
            if (_request._roots.Contains(root)) return;
            if (_request._roots.Count >= session._budget.MaxConsumerRoots)
                throw new FloraAppearanceCapacityException("Appearance consumer-root bound exceeded.");
            _request._roots.Add(root);
        }

        /// <summary>Stops publication immediately. Retains the lease until work completes and all roots are freed.</summary>
        public Task Retire(Task workersStopped)
        {
            MainThread(); ArgumentNullException.ThrowIfNull(workersStopped);
            if (_request._state is RequestState.Retiring or RequestState.Retired) return Retired;
            _ = Active();
            _request._workers = workersStopped;
            _request._state = RequestState.Retiring;
            return Retired;
        }
    }

    internal abstract class CpuWork : IDisposable
    {
        public abstract void ReleaseGeometry();
        public abstract void Dispose();
    }
    internal sealed record SharedAssets(FloraLibrary Library, Texture2D Colour, Texture2D Normal, Shader ImpostorShader);
    internal sealed record Operations(Func<CancellationToken, CpuWork> Load,
        Func<CpuWork, CancellationToken, Retirement, SharedAssets> Upload,
        Func<SharedAssets, Retirement, ShaderMaterial> CreateBinding);

    /// <summary>Finite construction ledger. Failed owners remain reachable; each pass attempts all remaining owners.</summary>
    internal sealed class Retirement
    {
        private readonly List<IDisposable> _owners = new();
        private readonly HashSet<IDisposable> _identities = new(ReferenceEqualityComparer.Instance);
        private bool _retiring;
        public T Own<T>(T owner) where T : IDisposable
        {
            MainThread();
            ArgumentNullException.ThrowIfNull(owner);
            ObjectDisposedException.ThrowIf(_retiring, this);
            if (_identities.Add(owner)) _owners.Add(owner);
            return owner;
        }
        public void Release(IDisposable owner)
        {
            MainThread();
            if (!_identities.Contains(owner)) throw new InvalidOperationException("Cleanup owner was not registered.");
            int index = _owners.FindIndex(candidate => ReferenceEquals(candidate, owner));
            DisposeOne(owner);
            // Reference identity avoids invoking a Godot object's equality implementation after disposal.
            _identities.Remove(owner); _owners.RemoveAt(index);
        }
        private static void DisposeOne(IDisposable owner)
        {
            if (owner is not GodotObject native || GodotObject.IsInstanceValid(native)) owner.Dispose();
        }
        public bool TryDispose(out Exception? failure)
        {
            MainThread(); _retiring = true;
            List<Exception>? errors = null;
            for (int i = _owners.Count - 1; i >= 0; i--)
            {
                IDisposable owner = _owners[i];
                try { DisposeOne(owner); _owners.RemoveAt(i); _identities.Remove(owner); }
                catch (Exception error) { (errors ??= new()).Add(error); }
            }
            failure = errors == null ? null : new AggregateException("Appearance cleanup is quarantined; retry explicitly.", errors);
            return failure == null;
        }
    }

    private sealed class LoadedAppearance : CpuWork
    {
        public FloraAppearancePackage Package;
        public FloraLibrary.Data Geometry;
        public LoadedAppearance(FloraAppearancePackage package, FloraLibrary.Data geometry)
        { Package = package; Geometry = geometry; }
        public override void ReleaseGeometry() => Geometry.Species.Clear();
        public override void Dispose() { Geometry.Species.Clear(); Package = null!; Geometry = null!; }
    }

    private static Operations Production(FloraAppearanceCatalog catalog, string directory, FloraAppearanceSessionBudget budget,
        FloraAppearanceLimits packageLimits, FloraAppearanceInputLimits inputLimits, TextureLayered groundAlbedo, TextureLayered groundNormal)
    {
        MainThread();
        ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(budget); ArgumentNullException.ThrowIfNull(packageLimits);
        ArgumentNullException.ThrowIfNull(inputLimits); ArgumentNullException.ThrowIfNull(groundAlbedo);
        ArgumentNullException.ThrowIfNull(groundNormal);
        if (catalog.Appearances.Count != 1) throw new FloraAppearanceCapacityException("A session requires exactly one pin.");
        long minimumCpuAllowance = checked(inputLimits.MaxGeometryBytes + inputLimits.MaxFileBytes +
            2L * inputLimits.MaxMetadataBytes + packageLimits.MaxManifestBytes);
        if (budget.GeometryAllowanceBytes < minimumCpuAllowance)
            throw new FloraAppearanceCapacityException("Geometry/import allowance is below the configured input limits.");
        FloraAppearancePin pin = catalog.Appearances[0];
        string path = System.IO.Path.GetFullPath(directory);

        // Separate helper captures only CPU-safe inputs. The worker never retains this Operations object (which also
        // owns the main-thread upload delegate and its borrowed ground textures).
        Func<CancellationToken, CpuWork> load = CpuLoader(path, pin, packageLimits);
        SharedAssets Upload(CpuWork work, CancellationToken cancellation, Retirement owners)
        {
            var loaded = (LoadedAppearance)work;
            cancellation.ThrowIfCancellationRequested();
            var inputs = owners.Own(new FloraAppearanceInputs(inputLimits, cancellation));
            FloraLibrary library;
            try { library = owners.Own(FloraLibrary.CreateFromAppearance(loaded.Geometry, groundAlbedo, groundNormal, loaded.Package, inputs)); }
            catch (FloraLibrary.CreationCleanupException error) { owners.Own(error.PendingCleanup); throw; }
            // Bind authenticated row ordinals exactly once before any lease can see the immutable library.
            foreach (FloraAppearanceRow row in loaded.Package.Inputs.Rows)
            {
                FloraLibrary.Species species = library.For(row.Kind) ?? throw new FloraAppearanceValidationException("Missing appearance species.");
                if ((uint)row.Variant >= (uint)species.Variants.Count)
                    throw new FloraAppearanceValidationException("Missing appearance variant.");
                species.Variants[row.Variant].ImpostorRow = row.Row;
            }
            ImageTexture colour = UploadFar(loaded.Package, "impostor-colour", owners, cancellation);
            ImageTexture normal = UploadFar(loaded.Package, "impostor-normal", owners, cancellation);
            Shader shader = GD.Load<Shader>("res://shaders/surface_impostor.gdshader")
                ?? throw new FloraAppearanceValidationException("Missing impostor shader."); // borrowed cached shader
            return new(library, colour, normal, shader);
        }
        return new(load, Upload, CreateImpostorBinding);
    }

    private static Func<CancellationToken, CpuWork> CpuLoader(string directory, FloraAppearancePin pin, FloraAppearanceLimits limits)
        => cancellation =>
        {
            FloraAppearancePackage package = FloraAppearancePackageLoader.Load(directory, pin, limits, cancellation);
            cancellation.ThrowIfCancellationRequested();
            FloraLibrary.Data geometry = FloraLibrary.BuildGeometry(); // no painting, Images, world or renderer calls
            if (cancellation.IsCancellationRequested) { geometry.Species.Clear(); cancellation.ThrowIfCancellationRequested(); }
            return new LoadedAppearance(package, geometry);
        };

    private static ImageTexture UploadFar(FloraAppearancePackage package, string role, Retirement owners, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        FloraAppearanceImage payload = package.Images.Single(image => image.Layout.Role == role);
        byte[] bytes = new byte[payload.ByteLength]; payload.CopyTo(bytes);
        FloraAppearanceImageLayout layout = payload.Layout;
        Image image = owners.Own(Image.CreateFromData(layout.Width, layout.Height, true, Image.Format.Rgba8, bytes));
        ImageTexture texture = owners.Own(ImageTexture.CreateFromImage(image));
        owners.Release(image);
        return texture;
    }

    private static ShaderMaterial CreateImpostorBinding(SharedAssets assets, Retirement owners)
    {
        var material = owners.Own(new ShaderMaterial());
        material.Shader = assets.ImpostorShader;
        material.SetShaderParameter("colour_atlas", assets.Colour);
        material.SetShaderParameter("normal_atlas", assets.Normal);
        material.SetShaderParameter("rows", (float)FloraAppearanceSchema.Rows);
        material.SetShaderParameter("native_rows", new Vector2(FloraAppearanceSchema.NativeRowStart, FloraAppearanceSchema.NativeRowEnd));
        material.SetShaderParameter("views", (float)FloraAppearanceSchema.Views);
        material.SetShaderParameter("fade_out", Vegetation.ImpostorEnd);
        return material;
    }
}
