using System;

namespace Hollow.Surface;

/// <summary>
/// Finite admission limits for one environment session. Byte values are explicit estimates, not measurements of
/// managed or native memory. The defaults are provisional safety limits; integrated rollout needs measured budgets.
/// There is no queue waiting for a build slot: a request beyond these limits is refused.
/// </summary>
public sealed record WorldStoreOptions
{
    public int MaxResidentWorlds { get; init; } = 4;
    public long MaxEstimatedResidentBytes { get; init; } = 256L * 1024 * 1024;
    public long MaxReservedBuildBytes { get; init; } = 128L * 1024 * 1024;
    public int MaxConcurrentBuilds { get; init; } = 2;
    public int MaxPendingAcquisitions { get; init; } = 64;
    /// <summary>Maximum exception examples retained; total cleanup-failure count is reported separately.</summary>
    public int MaxRetainedCleanupFailures { get; init; } = 16;
    public long DefaultEstimatedResidentBytes { get; init; } = 64L * 1024 * 1024;
    public long DefaultReservedBuildBytes { get; init; } = 64L * 1024 * 1024;
    /// <summary>Null disables sealed walking construction, including injected factories. Explicit qualification only.</summary>
    public SelectedWalkingWorldLimits? SelectedWalkingLimits { get; init; }

    internal void Validate()
    {
        if (MaxResidentWorlds <= 0) throw new ArgumentOutOfRangeException(nameof(MaxResidentWorlds));
        if (MaxEstimatedResidentBytes < 0) throw new ArgumentOutOfRangeException(nameof(MaxEstimatedResidentBytes));
        if (MaxReservedBuildBytes < 0) throw new ArgumentOutOfRangeException(nameof(MaxReservedBuildBytes));
        if (MaxConcurrentBuilds <= 0) throw new ArgumentOutOfRangeException(nameof(MaxConcurrentBuilds));
        if (MaxPendingAcquisitions <= 0) throw new ArgumentOutOfRangeException(nameof(MaxPendingAcquisitions));
        if (MaxRetainedCleanupFailures < 0) throw new ArgumentOutOfRangeException(nameof(MaxRetainedCleanupFailures));
        if (DefaultEstimatedResidentBytes < 0) throw new ArgumentOutOfRangeException(nameof(DefaultEstimatedResidentBytes));
        if (DefaultReservedBuildBytes < 0) throw new ArgumentOutOfRangeException(nameof(DefaultReservedBuildBytes));
    }
}

/// <summary>Estimated retained output and additional construction scratch, reserved before dispatch.</summary>
public readonly record struct WorldMemoryEstimate(long EstimatedResidentBytes, long ReservedBuildBytes);

/// <summary>
/// A snapshot taken under the store's lock. Reserved resident bytes are prospective, not yet published.
/// CleanupFailures counts all observed cleanup failures, saturating at int.MaxValue, rather than retained examples.
/// </summary>
public readonly record struct WorldStoreDiagnostics(
    int ResidentWorlds,
    int BuildingWorlds,
    int ActiveLeases,
    int PendingAcquisitions,
    long EstimatedResidentBytes,
    long ReservedResidentBytes,
    long ReservedBuildBytes,
    bool IsRetiring,
    bool IsRetired,
    int PendingDisposals,
    long PendingDisposalBytes,
    int CleanupFailures);

/// <summary>Essential work could not be admitted without exceeding a configured session limit.</summary>
public sealed class WorldStoreCapacityException(string message) : InvalidOperationException(message);
