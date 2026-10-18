using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>Caller-selected reservations, never a graphics preset or a production default.</summary>
public sealed record TerrainViewLimits
{
    public required int MaxTickets { get; init; }
    public required int MaxTilesPerCohort { get; init; }
    public required int MaxDistinctCohortTiles { get; init; }
    public required int MaxForcedNodes { get; init; }
    public required int MaxTotalNodes { get; init; }
    public required int MaxOutstandingJobs { get; init; }
    public required int MaxRetiringSessions { get; init; }
    public required long MaxEstimatedForcedResidentBytes { get; init; }
    public required long MaxEstimatedResidentBytes { get; init; }
    public required long MaxEstimatedWorkBytes { get; init; }
}

public enum TerrainCohortAcquireStatus { Accepted, CapacityExceeded }
public enum TerrainCohortState { Waiting, Ready, Failed, Retired }

/// <summary>An opaque per-view, per-setup identity. Holds no field, world, view or native resource.</summary>
public sealed class TerrainCohortTicket
{
    internal TerrainCohortTicket(object owner, ulong epoch, ulong id) { Owner = owner; Epoch = epoch; Id = id; }
    internal object Owner { get; }
    internal ulong Epoch { get; }
    internal ulong Id { get; }
}

public readonly record struct TerrainCohortDiagnostics(ulong Epoch, int Tickets, int FinestTiles, int ForcedNodes,
    int LiveNodes, int UploadedNodes, int ReservedNodes, int OutstandingJobsIncludingRetired, int QueuedJobs, int CompletedJobs, int RetiringSessions, int RetiringNativeInstances,
    int DeferredByCapacity, long RefusedAcquisitions, long EstimatedResidentBytes, long ReservedWorkBytes);

public sealed partial class TerrainLod
{
    // Estimates include retained 33x33 grids, terrain/water native buffers and upload headroom; driver/allocator
    // overhead is platform-dependent. These are admission reservations, not measured process/GPU memory.
    public const long EstimatedResidentBytesPerNode = 512L * 1024;
    public const long EstimatedWorkBytesPerJob = 512L * 1024;
    public const int MaximumCohortTickets = 16, MaximumTilesPerCohort = 32, MaximumDistinctCohortTiles = 128;
    public const int MaximumForcedNodes = 512, MaximumTotalNodes = 1024, MaximumOutstandingJobs = 64;
    public const int MaximumRetiringSessions = 2;
    public const int MaximumRetiringNativeInstances = 4 * MaximumTotalNodes;
    public const long EstimatedPoolInstanceBytes = 2048;
    public const long MaximumResidentBytes = 1024L * 1024 * 1024;
    public const long MaximumForcedResidentBytes = 256L * 1024 * 1024;
    public const long MaximumWorkBytes = 32L * 1024 * 1024;

    private sealed record CohortEntry(TileKey[] Leaves, TileKey[] Closure, TileKey[] Splits);
    private readonly object _ticketOwner = new();
    private ulong _cohortEpoch, _nextTicket;
    private TerrainViewLimits? _viewLimits;
    private FormationField? _cohortField;
    private TerrainPinSet? _cohortPins;
    private TerrainLattice? _cohortLattice;
    private readonly Dictionary<ulong, CohortEntry> _cohortTickets = new();
    private readonly Dictionary<TileKey, int> _forcedLeaves = new(), _forcedNodes = new(), _forcedSplits = new();
    private readonly Dictionary<TileKey, Node> _nodes = new();
    private readonly List<BuildSession> _retiringTerrain = new();
    private readonly List<MeshInstance3D> _retiringInstances = new();
    private readonly WorkReservation _workReservation = new();
    private int _capacityDeferred;
    private long _refusedAcquisitions;

    public TerrainCohortDiagnostics CohortDiagnostics
    {
        get
        {
            int reserved = ReservedNodeCount;
            var queue = _work?.QueueCounts ?? (Queued: 0, Completed: 0);
            return new(_cohortEpoch, _cohortTickets.Count, _forcedLeaves.Count, _forcedNodes.Count, _nodes.Count,
                _nodes.Values.Count(node => node.Mesh != null), reserved, _workReservation.Outstanding, queue.Queued, queue.Completed,
                _retiringTerrain.Count(session => !session.Completion.IsCompleted), _retiringInstances.Count,
                _capacityDeferred, _refusedAcquisitions, ResidentReservation(reserved),
                EstimatedWorkBytesPerJob * (_viewLimits?.MaxOutstandingJobs ?? _workReservation.Outstanding));
        }
    }

    /// <summary>
    /// Scene-thread only. The selected field authenticates the cohort by reference. Admission reserves complete
    /// incident leaf copies and every sibling quartet along their paths before changing any live demand.
    /// Distinct tickets share node references, including repeated requests for the same cohort.
    /// </summary>
    public TerrainCohortAcquireStatus TryAcquireCohort(FormationCohort cohort, out TerrainCohortTicket? ticket)
    {
        ArgumentNullException.ThrowIfNull(cohort);
        ticket = null;
        if (_work == null || _work.Stopped || _viewLimits == null || _cohortField == null || _cohortPins == null ||
            _cohortLattice == null || !ReferenceEquals(_cohortField.Cohort(cohort.Id), cohort))
            throw new InvalidOperationException("The cohort does not belong to this view's active pinned field.");
        if (_cohortTickets.Count >= _viewLimits.MaxTickets) return RefuseCohort();
        if (cohort.DependencyTiles.Length > MaximumDistinctCohortTiles) return RefuseCohort();

        var leaves = new HashSet<TileKey>();
        foreach (TileKey tile in cohort.DependencyTiles)
        {
            if (tile.Level != MaxLevel) throw new FormationValidationException("Cohort dependency is not a finest terrain tile.");
            if (!AddLeaf(tile)) return RefuseCohort();
        }
        // No floating face selection: enumerate every face/tile copy of each fixed canonical vertex.
        if (cohort.Pins.Length > TerrainPinSet.MaximumVertices) return RefuseCohort();
        Span<TileKey> cells = stackalloc TileKey[4];
        foreach (FormationTerrainPin pin in cohort.Pins)
        {
            if (!_cohortPins.TryGet(pin.Vertex, out TerrainPinnedVertex frozen) || frozen.Position != pin.Position)
                throw new FormationValidationException("Cohort contact pins do not match this view's frozen pin set.");
            int count = _cohortLattice.IncidentCells(pin.Vertex, cells);
            for (int i = 0; i < count; i++)
            {
                TileKey tile = cells[i];
                while (tile.Level > MaxLevel) tile = tile.Parent;
                if (!AddLeaf(tile)) return RefuseCohort();
            }
        }
        if (leaves.Count == 0) throw new FormationValidationException("A terrain cohort needs at least one supporting tile.");
        int distinct = _forcedLeaves.Count + leaves.Count(key => !_forcedLeaves.ContainsKey(key));
        if (distinct > _viewLimits.MaxDistinctCohortTiles) return RefuseCohort();

        var closure = new HashSet<TileKey>();
        var splits = new HashSet<TileKey>();
        foreach (TileKey leaf in leaves)
        {
            TileKey key = leaf;
            closure.Add(key);
            while (key.Level > 0)
            {
                key = key.Parent;
                splits.Add(key); closure.Add(key);
                for (int child = 0; child < 4; child++) closure.Add(key.Child(child));
                if (closure.Count > _viewLimits.MaxForcedNodes) return RefuseCohort();
            }
        }
        int forced = _forcedNodes.Count + closure.Count(key => !_forcedNodes.ContainsKey(key));
        int total = ReservedNodeCount + closure.Count(key => !_forcedNodes.ContainsKey(key) && !_nodes.ContainsKey(key));
        if (forced > _viewLimits.MaxForcedNodes || EstimatedResidentBytesPerNode * forced > _viewLimits.MaxEstimatedForcedResidentBytes ||
            !FitsTotalReservation(total)) return RefuseCohort();

        // All capacity work finishes before publication. Arrays are small and sorted for repeatable scheduling.
        var entry = new CohortEntry(leaves.OrderBy(key => key.Packed).ToArray(), closure.OrderBy(key => key.Packed).ToArray(),
            splits.OrderBy(key => key.Packed).ToArray());
        ulong id = checked(_nextTicket + 1);
        var accepted = new TerrainCohortTicket(_ticketOwner, _cohortEpoch, id);
        _cohortTickets.Add(id, entry);
        foreach (TileKey key in entry.Leaves) AddReference(_forcedLeaves, key);
        foreach (TileKey key in entry.Closure) AddReference(_forcedNodes, key);
        foreach (TileKey key in entry.Splits) AddReference(_forcedSplits, key);
        _nextTicket = id; ticket = accepted; IsIdle = false;
        return TerrainCohortAcquireStatus.Accepted;

        bool AddLeaf(TileKey key) => leaves.Add(key) ? leaves.Count <= _viewLimits.MaxTilesPerCohort : true;
    }

    /// <summary>Ready means actual visible finest instances after Show, never queued, uploaded-only or a parent fallback.</summary>
    public TerrainCohortState GetCohortState(TerrainCohortTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (!ReferenceEquals(ticket.Owner, _ticketOwner) || ticket.Epoch != _cohortEpoch ||
            !_cohortTickets.TryGetValue(ticket.Id, out CohortEntry? entry)) return TerrainCohortState.Retired;
        foreach (TileKey key in entry.Closure)
            if (_nodes.TryGetValue(key, out Node? node) && node.Failed) return TerrainCohortState.Failed;
        foreach (TileKey key in entry.Leaves)
            if (!_nodes.TryGetValue(key, out Node? node) || !node.Drawn || node.Grid == null || node.Mesh == null ||
                node.Instance == null || !node.Instance.IsVisibleInTree() || !ReferenceEquals(node.Instance.Mesh, node.Mesh))
                return TerrainCohortState.Waiting;
        return TerrainCohortState.Ready;
    }

    /// <summary>
    /// Executes the renderer's synchronous hide BEFORE releasing demand. A failed hide keeps all references held.
    /// Repeated/stale releases are harmless. The owning renderer must likewise hide before Stop/Setup/ExitTree.
    /// </summary>
    public void ReleaseCohort(TerrainCohortTicket ticket, Action hideFormation)
    {
        ArgumentNullException.ThrowIfNull(ticket); ArgumentNullException.ThrowIfNull(hideFormation);
        if (!ReferenceEquals(ticket.Owner, _ticketOwner) || ticket.Epoch != _cohortEpoch ||
            !_cohortTickets.TryGetValue(ticket.Id, out CohortEntry? entry)) return;
        hideFormation();
        // A hide callback can retire the setup. Never decrement the replacement world's references.
        if (ticket.Epoch != _cohortEpoch || !_cohortTickets.Remove(ticket.Id)) return;
        foreach (TileKey key in entry.Leaves) RemoveReference(_forcedLeaves, key);
        foreach (TileKey key in entry.Closure) RemoveReference(_forcedNodes, key);
        foreach (TileKey key in entry.Splits) RemoveReference(_forcedSplits, key);
        IsIdle = false;
    }

    private TerrainCohortAcquireStatus RefuseCohort() { _refusedAcquisitions++; return TerrainCohortAcquireStatus.CapacityExceeded; }
    private static void AddReference(Dictionary<TileKey, int> references, TileKey key) => references[key] = references.GetValueOrDefault(key) + 1;
    private static void RemoveReference(Dictionary<TileKey, int> references, TileKey key)
    { if (references[key] == 1) references.Remove(key); else references[key]--; }
    private int ReservedNodeCount => _nodes.Count + _forcedNodes.Keys.Count(key => !_nodes.ContainsKey(key));
    private long ResidentReservation(int nodes) => EstimatedPoolInstanceBytes * _retiringInstances.Count + (_viewLimits == null ? 0 :
        // Fixed allowance includes bounded ticket dictionaries and BOTH instance pools up to their high-water mark.
        2L * 1024 * 1024 + 2L * EstimatedPoolInstanceBytes * _viewLimits.MaxTotalNodes + EstimatedResidentBytesPerNode * nodes);
    private bool FitsTotalReservation(int nodes) => _viewLimits == null ||
        nodes <= _viewLimits.MaxTotalNodes && ResidentReservation(nodes) <= _viewLimits.MaxEstimatedResidentBytes;

    private bool TryCreateChildren(Node node)
    {
        if (node.Children != null) return true;
        int added = 0;
        for (int child = 0; child < 4; child++) if (!_forcedNodes.ContainsKey(node.Key.Child(child))) added++;
        if (!FitsTotalReservation(ReservedNodeCount + added)) { _capacityDeferred++; return false; }
        node.Children = new[] { NewNode(node.Key.Child(0), node), NewNode(node.Key.Child(1), node),
            NewNode(node.Key.Child(2), node), NewNode(node.Key.Child(3), node) };
        return true;
    }

    private void ValidateViewSetup(ISurface surface, TerrainViewLimits? limits)
    {
        CollectRetiredInstances();
        TerrainPinSet? pins = TerrainPinSet.Of(surface);
        if (pins != null && limits == null) throw new ArgumentException("Pinned terrain requires explicit view reservations.", nameof(limits));
        if (limits == null) return;
        if (limits.MaxTickets < 0 || limits.MaxTickets > MaximumCohortTickets ||
            limits.MaxTilesPerCohort < 1 || limits.MaxTilesPerCohort > MaximumTilesPerCohort ||
            limits.MaxDistinctCohortTiles < 0 || limits.MaxDistinctCohortTiles > MaximumDistinctCohortTiles ||
            limits.MaxForcedNodes < 0 || limits.MaxForcedNodes > MaximumForcedNodes ||
            limits.MaxTotalNodes < 6 || limits.MaxTotalNodes > MaximumTotalNodes ||
            limits.MaxOutstandingJobs < 1 || limits.MaxOutstandingJobs > MaximumOutstandingJobs ||
            limits.MaxRetiringSessions < 1 || limits.MaxRetiringSessions > MaximumRetiringSessions ||
            limits.MaxEstimatedForcedResidentBytes < 0 || limits.MaxEstimatedForcedResidentBytes > MaximumForcedResidentBytes ||
            limits.MaxEstimatedResidentBytes < 0 || limits.MaxEstimatedResidentBytes > MaximumResidentBytes ||
            limits.MaxEstimatedWorkBytes < 0 || limits.MaxEstimatedWorkBytes > MaximumWorkBytes)
            throw new ArgumentOutOfRangeException(nameof(limits), "Terrain view reservations exceed supported hard ceilings.");
        int retiringInstances = _retiringInstances.Count + _pool.Count + _waterPool.Count +
            _nodes.Values.Count(node => node.Instance != null) + _nodes.Values.Count(node => node.WaterInstance != null);
        if (retiringInstances + 2L * limits.MaxTotalNodes > MaximumRetiringNativeInstances)
            throw new InvalidOperationException("Retiring native terrain instances must drain before another setup fits.");
        long rootReservation = 2L * 1024 * 1024 + EstimatedPoolInstanceBytes * 2L * limits.MaxTotalNodes +
            6L * EstimatedResidentBytesPerNode;
        if (rootReservation > limits.MaxEstimatedResidentBytes ||
            EstimatedWorkBytesPerJob * limits.MaxOutstandingJobs > limits.MaxEstimatedWorkBytes)
            throw new ArgumentException("Terrain view byte reservations cannot cover roots/pools or outstanding work.", nameof(limits));
        if (rootReservation + EstimatedPoolInstanceBytes * retiringInstances > limits.MaxEstimatedResidentBytes)
            throw new InvalidOperationException("Retiring native terrain instances must drain before the new byte reservation fits.");
        _retiringTerrain.RemoveAll(session => session.Completion.IsCompleted);
        int retiring = _retiringTerrain.Count;
        if (_work != null && !_work.Completion.IsCompleted && !_retiringTerrain.Contains(_work)) retiring++;
        if (retiring > limits.MaxRetiringSessions || _workReservation.Outstanding > limits.MaxOutstandingJobs)
            throw new InvalidOperationException("Retiring terrain work must drain before this setup can fit its explicit reservations.");
    }

    private void BeginCohorts(ISurface surface, TerrainViewLimits? limits)
    {
        _viewLimits = limits;
        _cohortPins = TerrainPinSet.Of(surface);
        _cohortField = (surface as IFormationSurface)?.Formations;
        _cohortLattice = TerrainLattice.Of(surface);
        _capacityDeferred = 0; _refusedAcquisitions = 0;
    }

    private void RetireCohorts()
    {
        _cohortEpoch = checked(_cohortEpoch + 1);
        _cohortTickets.Clear(); _forcedLeaves.Clear(); _forcedNodes.Clear(); _forcedSplits.Clear();
        _cohortPins = null; _cohortField = null; _cohortLattice = null;
        _capacityDeferred = 0; _viewLimits = null;
        _retiringTerrain.RemoveAll(session => session.Completion.IsCompleted);
        if (_work != null && !_work.Completion.IsCompleted && !_retiringTerrain.Contains(_work)) _retiringTerrain.Add(_work);
    }

    private void CollectRetiredInstances() => _retiringInstances.RemoveAll(instance => !GodotObject.IsInstanceValid(instance));
    private void RetireInstance(MeshInstance3D instance)
    {
        instance.Mesh = null;
        instance.MaterialOverride = null; // A queued empty node must not retain a retired world's materials.
        _retiringInstances.Add(instance);
        instance.QueueFree();
    }

    // Shared by current and retiring sessions. No source/view references; one job owns one slot until its result
    // is consumed or discarded. Stop frees queued/completed slots immediately, running work frees on completion.
    private sealed class WorkReservation
    {
        private readonly object _gate = new();
        private int _outstanding;
        public int Outstanding { get { lock (_gate) return _outstanding; } }
        public bool TryAcquire(int maximum)
        { lock (_gate) { if (_outstanding >= maximum) return false; _outstanding++; return true; } }
        public void Release(int count = 1)
        { lock (_gate) { _outstanding -= count; if (_outstanding < 0) throw new InvalidOperationException("Terrain work reservation underflow."); } }
    }
}
