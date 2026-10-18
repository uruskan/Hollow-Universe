using System;
using System.Collections.Generic;
using Hollow.Surface;

namespace Hollow.Simulation.Physics;

public sealed record GroundStreamingLimits
{
    public required GroundNativeEstimates Native { get; init; }
    public required int MaxOwners { get; init; }
    public required int MaxTiles { get; init; }
    public required int MaxOutstanding { get; init; }
    public required int MaxExecuting { get; init; }
    public required int MaxCompletionSlots { get; init; }
    public required int MaxBodies { get; init; }
    public required int MaxParked { get; init; }
    public required int MaxAreaTickets { get; init; }
    public required int MaxPlanning { get; init; }
    public required long MaxPlanBytes { get; init; }
    public required long MaxAreaBytes { get; init; }
    public required long MaxPlanningBytes { get; init; }
    public required long MaxManagedBuildBytes { get; init; }
    public required long MaxNativeBuildBytes { get; init; }
    public required long MaxNativeShapeBytes { get; init; }
    public required long MaxNativeBodyBytes { get; init; }
    public required long MaxNativeSetupBytes { get; init; }
    public required long MaxMetadataBytes { get; init; }

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Native); Native.Validate();
        if (MaxOwners is < 1 or > 8 || MaxTiles is < 1 or > GroundCollisionLayout.TileIdCount ||
            MaxOutstanding < 1 || MaxOutstanding > MaxTiles || MaxExecuting is < 1 or > 16 ||
            MaxExecuting > MaxOutstanding || MaxCompletionSlots < 1 || MaxCompletionSlots > MaxOutstanding ||
            MaxBodies < MaxTiles || MaxBodies > GroundCollisionLayout.TileIdCount + 1024 ||
            MaxParked < 0 || MaxParked > 1024 || MaxParked > MaxBodies || MaxAreaTickets is < 1 or > 4096 ||
            MaxPlanning is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(GroundStreamingLimits));
        foreach (long value in new[] { MaxPlanBytes, MaxAreaBytes, MaxPlanningBytes, MaxManagedBuildBytes,
            MaxNativeBuildBytes, MaxNativeShapeBytes, MaxNativeBodyBytes, MaxNativeSetupBytes, MaxMetadataBytes })
            if (value < 1 || value > 8L * 1024 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(GroundStreamingLimits));
    }
}

public readonly record struct GroundStreamingDiagnostics(int Owners, int Tiles, int Outstanding, int Executing,
    int CompletionSlots, int Bodies, int Parked, int AreaTickets, int Planning, long PlanBytes, long AreaBytes,
    long PlanningBytes, long ManagedBuildBytes, long NativeBuildBytes, long NativeShapeBytes, long NativeBodyBytes,
    long NativeSetupBytes, long MetadataBytes);

public enum GroundWorkState { Queued, Running, Completed, Publishing, Resident, Parking, Parked, Retiring, Released }

// The native adapter uses this only when pre-return rollback itself fails. Ownership crosses the failure boundary
// explicitly so the ledger can retain the resource and its reservations until a later disposal succeeds.
internal sealed class GroundOwnedBuildFailure(IDisposable owner, Exception cause) : Exception("Native ground rollback remains owned.", cause)
{
    internal IDisposable Owner { get; } = owner;
}

/// <summary>
/// Engine-independent ownership ledger shared by active and retiring physics owners. It creates no Tasks or native
/// resources. Factories execute only inside admitted calls; a native owner must dispose every shape/body it gains,
/// including partial publication. Estimates are reservations, not a claim about GC, allocator usage or process RSS.
/// Existing PhysicsWorld consumers remain unchanged until their readiness transactions are migrated.
/// </summary>
public sealed class GroundStreamingBudget
{
    internal const long RecordBytes = 1024, OwnerBytes = 4096;
    internal readonly object Gate = new();
    internal readonly GroundStreamingLimits Limits;
    internal int Owners, Tiles, Outstanding, Executing, CompletionSlots, Bodies, Parked, AreaTickets, Planning;
    internal long PlanBytes, AreaBytes, PlanningBytes, ManagedBuildBytes, NativeBuildBytes, NativeShapeBytes,
        NativeBodyBytes, NativeSetupBytes, MetadataBytes;

    public GroundStreamingBudget(GroundStreamingLimits limits)
    { ArgumentNullException.ThrowIfNull(limits); limits.Validate(); Limits = limits; }
    public GroundStreamingDiagnostics Diagnostics
    {
        get { lock (Gate) return new(Owners, Tiles, Outstanding, Executing, CompletionSlots, Bodies, Parked,
            AreaTickets, Planning, PlanBytes, AreaBytes, PlanningBytes, ManagedBuildBytes, NativeBuildBytes,
            NativeShapeBytes, NativeBodyBytes, NativeSetupBytes, MetadataBytes); }
    }
    internal static bool Fits(long current, long additional, long maximum) => additional >= 0 && additional <= maximum - current;

    public bool TryOpenOwner(out GroundStreamingOwner? owner) => TryOpenOwner(0, out owner);

    // The Work/area hash sets retain their high-water backing arrays while the owner is alive, even when every
    // entry has retired. Reserve their caller-limit capacities for the entire owner lifetime, independently of
    // live record/area charges. These conservative managed estimates include bucket/entry arrays, growth overlap
    // and retirement copies; they are not measured runtime allocator sizes. No per-tick trimming is required.
    internal static long OwnerMetadataBytes(GroundStreamingLimits limits) =>
        checked(OwnerBytes + 256L * limits.MaxTiles + 128L * limits.MaxAreaTickets);

    internal bool TryOpenOwner(long additionalMetadataBytes, out GroundStreamingOwner? owner)
    {
        if (additionalMetadataBytes < 0 || additionalMetadataBytes > 8L * 1024 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(additionalMetadataBytes));
        long metadata = checked(OwnerMetadataBytes(Limits) + additionalMetadataBytes);
        lock (Gate)
        {
            owner = null;
            if (Owners == Limits.MaxOwners || !Fits(NativeSetupBytes, Limits.Native.SetupBytes, Limits.MaxNativeSetupBytes) ||
                !Fits(MetadataBytes, metadata, Limits.MaxMetadataBytes)) return false;
            owner = new GroundStreamingOwner(this, metadata);
            Owners++; NativeSetupBytes += Limits.Native.SetupBytes; MetadataBytes += metadata;
            return true;
        }
    }
}

public sealed class GroundStreamingOwner
{
    internal readonly GroundStreamingBudget Budget;
    internal readonly HashSet<GroundBuildLease> Work = new();
    private readonly HashSet<GroundAreaLease> _areas = new();
    internal bool Closed;
    private bool _setupReleased, _releasingSetup;
    private int _planning;
    private readonly long _metadataBytes;
    internal GroundStreamingOwner(GroundStreamingBudget budget, long metadataBytes)
    { Budget = budget; _metadataBytes = metadataBytes; }

    /// <summary>One atomic area/dependency admission. The callback runs only under the full planning reservation.</summary>
    internal bool TryPrepareAreaAndAdmit(long scratchBytes, int maximumPlans,
        Func<(GroundAreaPlan Area, SelectedGroundCollisionPlan[] Plans)> prepare,
        out GroundAreaLease? areaLease, out GroundBuildLease[] leases)
    {
        areaLease = null; leases = Array.Empty<GroundBuildLease>();
        if (scratchBytes < 1 || maximumPlans < 1 || maximumPlans > Budget.Limits.MaxOutstanding)
            throw new ArgumentOutOfRangeException(nameof(maximumPlans));
        lock (Budget.Gate)
        {
            if (Closed || Budget.Planning == Budget.Limits.MaxPlanning ||
                !GroundStreamingBudget.Fits(Budget.PlanningBytes, scratchBytes, Budget.Limits.MaxPlanningBytes)) return false;
            Budget.Planning++; _planning++; Budget.PlanningBytes += scratchBytes;
        }
        try
        {
            var batch = prepare();
            if (batch.Area == null || batch.Plans == null || batch.Plans.Length > maximumPlans)
                throw new InvalidOperationException("Invalid prepared ground area batch.");
            lock (Budget.Gate)
            {
                if (Closed || Budget.AreaTickets == Budget.Limits.MaxAreaTickets ||
                    !GroundStreamingBudget.Fits(Budget.AreaBytes, batch.Area.EstimatedRetainedBytes, Budget.Limits.MaxAreaBytes)) return false;
                if (!TryAdmit(batch.Plans, out leases)) return false;
                areaLease = new GroundAreaLease(this, batch.Area); _areas.Add(areaLease);
                Budget.AreaTickets++; Budget.AreaBytes += batch.Area.EstimatedRetainedBytes;
                return true;
            }
        }
        finally
        {
            lock (Budget.Gate) { Budget.Planning--; _planning--; Budget.PlanningBytes -= scratchBytes; }
        }
    }

    /// <summary>
    /// The trusted preparation callback must honor its supplied source operation limits. Scratch is reserved before
    /// invoking it and stays charged through admission/failure. No output escapes on a capacity/retirement refusal.
    /// The callback may return at most maximumPlans; its aggregate live topology/query/output must fit scratchBytes.
    /// </summary>
    public bool TryPrepareAndAdmit(long scratchBytes, int maximumPlans, Func<SelectedGroundCollisionPlan[]> prepare,
        out GroundBuildLease[] leases)
    {
        ArgumentNullException.ThrowIfNull(prepare); leases = Array.Empty<GroundBuildLease>();
        if (scratchBytes < 1 || maximumPlans < 1 || maximumPlans > Budget.Limits.MaxOutstanding)
            throw new ArgumentOutOfRangeException(nameof(scratchBytes));
        lock (Budget.Gate)
        {
            if (Closed || Budget.Planning == Budget.Limits.MaxPlanning ||
                !GroundStreamingBudget.Fits(Budget.PlanningBytes, scratchBytes, Budget.Limits.MaxPlanningBytes)) return false;
            Budget.Planning++; _planning++; Budget.PlanningBytes += scratchBytes;
        }
        try
        {
            SelectedGroundCollisionPlan[] plans = prepare() ?? throw new InvalidOperationException("Preparation returned no array.");
            if (plans.Length > maximumPlans) throw new InvalidOperationException("Preparation exceeded its admitted plan count.");
            return TryAdmit(plans, out leases);
        }
        finally
        {
            lock (Budget.Gate) { Budget.Planning--; _planning--; Budget.PlanningBytes -= scratchBytes; }
        }
    }

    /// <summary>Internal/test boundary; production preparation must enter TryPrepareAndAdmit before allocating plans.</summary>
    internal bool TryAdmit(ReadOnlySpan<SelectedGroundCollisionPlan> plans, out GroundBuildLease[] leases)
    {
        leases = Array.Empty<GroundBuildLease>();
        if (plans.Length > Budget.Limits.MaxOutstanding) return false;
        var copies = plans.ToArray(); var costs = new GroundBuildCost[copies.Length];
        long retained = 0, native = 0;
        for (int i = 0; i < copies.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(copies[i]);
            costs[i] = GroundStreamingEstimates.For(copies[i], Budget.Limits.Native);
            retained = checked(retained + costs[i].Plan); native = checked(native + costs[i].NativeShape);
            // A record that can never dispatch must not occupy the queue indefinitely.
            if (costs[i].ManagedBuild > Budget.Limits.MaxManagedBuildBytes || costs[i].NativeBuild > Budget.Limits.MaxNativeBuildBytes)
                return false;
        }
        lock (Budget.Gate)
        {
            GroundStreamingLimits limits = Budget.Limits; int count = copies.Length;
            if (Closed || !GroundStreamingBudget.Fits(Budget.Tiles, count, limits.MaxTiles) ||
                !GroundStreamingBudget.Fits(Budget.Outstanding, count, limits.MaxOutstanding) ||
                !GroundStreamingBudget.Fits(Budget.Bodies, count, limits.MaxBodies) ||
                !GroundStreamingBudget.Fits(Budget.PlanBytes, retained, limits.MaxPlanBytes) ||
                !GroundStreamingBudget.Fits(Budget.NativeShapeBytes, native, limits.MaxNativeShapeBytes) ||
                !GroundStreamingBudget.Fits(Budget.NativeBodyBytes, count * limits.Native.BodyBytes, limits.MaxNativeBodyBytes) ||
                !GroundStreamingBudget.Fits(Budget.MetadataBytes, count * GroundStreamingBudget.RecordBytes, limits.MaxMetadataBytes)) return false;
            leases = new GroundBuildLease[count];
            for (int i = 0; i < count; i++) { leases[i] = new GroundBuildLease(this, copies[i], costs[i]); Work.Add(leases[i]); }
            Budget.Tiles += count; Budget.Outstanding += count; Budget.Bodies += count;
            Budget.PlanBytes += retained; Budget.NativeShapeBytes += native;
            Budget.NativeBodyBytes += count * limits.Native.BodyBytes; Budget.MetadataBytes += count * GroundStreamingBudget.RecordBytes;
            return true;
        }
    }

    public bool TryRetainArea(GroundAreaPlan plan, out GroundAreaLease? lease)
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (Budget.Gate)
        {
            lease = null;
            if (Closed || Budget.AreaTickets == Budget.Limits.MaxAreaTickets ||
                !GroundStreamingBudget.Fits(Budget.AreaBytes, plan.EstimatedRetainedBytes, Budget.Limits.MaxAreaBytes)) return false;
            lease = new GroundAreaLease(this, plan); _areas.Add(lease);
            Budget.AreaTickets++; Budget.AreaBytes += plan.EstimatedRetainedBytes; return true;
        }
    }
    internal void ReleaseArea(GroundAreaLease lease, long bytes)
    { lock (Budget.Gate) if (_areas.Remove(lease)) { Budget.AreaTickets--; Budget.AreaBytes -= bytes; } }

    /// <summary>Invalidates readiness immediately. Running callbacks keep ownership until they actually return.</summary>
    public void BeginRetirement()
    {
        GroundBuildLease[] work; GroundAreaLease[] areas;
        lock (Budget.Gate)
        {
            Closed = true; work = new GroundBuildLease[Work.Count]; Work.CopyTo(work);
            areas = new GroundAreaLease[_areas.Count]; _areas.CopyTo(areas);
        }
        List<Exception>? errors = null;
        foreach (GroundAreaLease area in areas) area.Dispose();
        foreach (GroundBuildLease item in work)
            try { item.Retire(); } catch (Exception error) { (errors ??= new()).Add(error); }
        if (errors != null) throw new AggregateException("Ground resources remain charged after failed retirement.", errors);
    }

    /// <summary>The caller's native PhysicsSystem/setup is disposed only after every dependent callback/resource exits.</summary>
    public bool TryReleaseSetup(Action disposeNativeSetup)
    {
        ArgumentNullException.ThrowIfNull(disposeNativeSetup);
        lock (Budget.Gate)
        {
            if (_setupReleased) return true;
            if (!Closed || _releasingSetup || Work.Count != 0 || _areas.Count != 0 || _planning != 0) return false;
            _releasingSetup = true;
        }
        try { disposeNativeSetup(); }
        catch { lock (Budget.Gate) _releasingSetup = false; throw; }
        lock (Budget.Gate)
        {
            // Final retirement only: an externally retained released owner keeps no former backing capacity.
            Work.TrimExcess(); _areas.TrimExcess();
            _setupReleased = true; _releasingSetup = false;
            Budget.Owners--; Budget.NativeSetupBytes -= Budget.Limits.Native.SetupBytes;
            Budget.MetadataBytes -= _metadataBytes;
            return true;
        }
    }
}

/// <summary>Retention only; dependency/readiness tickets will be wired by the later consumer transaction slice.</summary>
public sealed class GroundAreaLease : IDisposable
{
    private readonly GroundStreamingOwner _owner;
    private GroundAreaPlan? _plan;
    internal GroundAreaLease(GroundStreamingOwner owner, GroundAreaPlan plan) { _owner = owner; _plan = plan; }
    public GroundAreaPlan Plan { get { lock (_owner.Budget.Gate) return _plan ?? throw new ObjectDisposedException(nameof(GroundAreaLease)); } }
    public void Dispose()
    {
        lock (_owner.Budget.Gate)
        {
            if (_plan == null) return;
            _owner.ReleaseArea(this, _plan.EstimatedRetainedBytes); _plan = null;
        }
    }
}

public sealed class GroundBuildLease
{
    private readonly GroundStreamingOwner _owner;
    private GroundStreamingBudget Budget => _owner.Budget;
    private readonly GroundBuildCost _cost;
    private SelectedGroundCollisionPlan? _plan;
    private IDisposable? _native;
    private Exception? _failure;
    private GroundWorkState _state = GroundWorkState.Queued;
    private bool _busy, _retire, _working, _completion, _outstanding = true, _shape = true, _parked, _retainedBuild;
    internal GroundBuildLease(GroundStreamingOwner owner, SelectedGroundCollisionPlan plan, GroundBuildCost cost)
    { _owner = owner; _plan = plan; _cost = cost; }
    public GroundWorkState State { get { lock (Budget.Gate) return _state; } }
    public bool Ready { get { lock (Budget.Gate) return !_owner.Closed && !_retire && _state == GroundWorkState.Resident; } }
    public Exception? Failure { get { lock (Budget.Gate) return _failure; } }

    /// <summary>Runs on the caller's chosen worker or synchronous thread, using exactly the same admitted slots.</summary>
    public bool TryRun(Func<SelectedGroundCollisionPlan, IDisposable> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (!TryReserveRun()) return false;
        RunReserved(build);
        return true;
    }

    // The scheduler reserves before creating a Task. Reserved but not-yet-dispatched work remains busy/charged.
    internal bool TryReserveRun()
    {
        lock (Budget.Gate)
        {
            if (_owner.Closed || _state != GroundWorkState.Queued || Budget.Executing == Budget.Limits.MaxExecuting ||
                Budget.CompletionSlots == Budget.Limits.MaxCompletionSlots ||
                !GroundStreamingBudget.Fits(Budget.ManagedBuildBytes, _cost.ManagedBuild, Budget.Limits.MaxManagedBuildBytes) ||
                !GroundStreamingBudget.Fits(Budget.NativeBuildBytes, _cost.NativeBuild, Budget.Limits.MaxNativeBuildBytes)) return false;
            _busy = _working = _completion = true; _state = GroundWorkState.Running;
            Budget.Executing++; Budget.CompletionSlots++; Budget.ManagedBuildBytes += _cost.ManagedBuild; Budget.NativeBuildBytes += _cost.NativeBuild;
        }
        return true;
    }

    private bool _callbackEntered;
    internal void RunReserved(Func<SelectedGroundCollisionPlan, IDisposable> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        lock (Budget.Gate)
        {
            if (!_working || _callbackEntered) throw new InvalidOperationException("Ground execution was not reserved or already entered.");
            _callbackEntered = true;
        }
        try { _native = build(_plan!) ?? throw new InvalidOperationException("A ground build returned no owned native resource."); }
        catch (Exception error)
        {
            lock (Budget.Gate)
            {
                if (error is GroundOwnedBuildFailure owned)
                {
                    _native = owned.Owner;
                    // Failed rollback can still own native settings/input storage. Keep the full build envelope
                    // until disposal succeeds even though the executing callback itself has returned.
                    _retainedBuild = true;
                }
                _failure = error; _retire = true;
            }
        }
        finally
        {
            lock (Budget.Gate)
            {
                _busy = _working = false; Budget.Executing--;
                if (!_retainedBuild)
                { Budget.ManagedBuildBytes -= _cost.ManagedBuild; Budget.NativeBuildBytes -= _cost.NativeBuild; }
                _state = GroundWorkState.Completed;
            }
        }
        bool retiring; lock (Budget.Gate) retiring = _retire || _owner.Closed;
        if (retiring) Retire();
    }

    /// <summary>The owned resource must also own any body created during publication, including a partially failed one.</summary>
    public bool TryAdopt(Action<IDisposable> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        lock (Budget.Gate)
        {
            if (_owner.Closed || _retire || _state != GroundWorkState.Completed) return false;
            _busy = true; _state = GroundWorkState.Publishing;
        }
        try { publish(_native!); }
        catch (Exception error) { lock (Budget.Gate) { _failure = error; _retire = true; } }
        finally
        {
            lock (Budget.Gate)
            {
                _busy = false; _state = GroundWorkState.Resident;
                ReleasePlanAndOutstanding(); ReleaseCompletion();
            }
        }
        bool retiring; lock (Budget.Gate) retiring = _retire || _owner.Closed;
        if (retiring) { Retire(); return false; }
        return true;
    }

    /// <summary>Only succeeds after the callback has removed the shape and left an owned parked body.</summary>
    public bool TryPark(Action<IDisposable> park)
    {
        ArgumentNullException.ThrowIfNull(park);
        lock (Budget.Gate)
        {
            if (_owner.Closed || _retire || _state != GroundWorkState.Resident || Budget.Parked == Budget.Limits.MaxParked) return false;
            _busy = true; _state = GroundWorkState.Parking; Budget.Parked++; _parked = true;
        }
        try { park(_native!); }
        catch (Exception error) { lock (Budget.Gate) { _failure = error; _retire = true; } }
        finally
        {
            lock (Budget.Gate)
            {
                _busy = false; _state = GroundWorkState.Parked;
                if (!_retire) { _shape = false; Budget.Tiles--; Budget.NativeShapeBytes -= _cost.NativeShape; }
            }
        }
        bool retiring; lock (Budget.Gate) retiring = _retire || _owner.Closed;
        if (retiring) { Retire(); return false; }
        return true;
    }

    public void Retire()
    {
        lock (Budget.Gate)
        {
            _retire = true;
            if (_state == GroundWorkState.Released || _busy) return;
            _busy = true; _state = GroundWorkState.Retiring;
        }
        try { _native?.Dispose(); }
        catch { lock (Budget.Gate) _busy = false; throw; }
        lock (Budget.Gate)
        {
            _native = null; _busy = false;
            if (_retainedBuild)
            {
                Budget.ManagedBuildBytes -= _cost.ManagedBuild; Budget.NativeBuildBytes -= _cost.NativeBuild;
                _retainedBuild = false;
            }
            ReleasePlanAndOutstanding(); ReleaseCompletion();
            if (_shape) { Budget.Tiles--; Budget.NativeShapeBytes -= _cost.NativeShape; _shape = false; }
            if (_parked) { Budget.Parked--; _parked = false; }
            Budget.Bodies--; Budget.NativeBodyBytes -= Budget.Limits.Native.BodyBytes;
            Budget.MetadataBytes -= GroundStreamingBudget.RecordBytes; _owner.Work.Remove(this);
            _state = GroundWorkState.Released;
        }
    }
    private void ReleasePlanAndOutstanding()
    {
        if (_plan != null) { _plan = null; Budget.PlanBytes -= _cost.Plan; }
        if (_outstanding) { _outstanding = false; Budget.Outstanding--; }
    }
    private void ReleaseCompletion()
    {
        if (_working) throw new InvalidOperationException("Cannot release an executing worker's completion reservation.");
        if (_completion) { _completion = false; Budget.CompletionSlots--; }
    }
}
