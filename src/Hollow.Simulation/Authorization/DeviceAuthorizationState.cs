using System;
using System.Collections.Generic;

namespace Hollow.Simulation.Authorization;

public enum DeviceAuthorizationMode : byte { FollowCupboard = 0, ManualOnly = 1 }

/// <summary>Persistent device-local intent. Inherited cupboard membership is never copied into these sets.</summary>
public sealed class DeviceAuthorizationState
{
    public const int MaximumEntries = 64;
    public bool Initialized { get; set; }
    public DeviceAuthorizationMode Mode { get; set; }
    public HashSet<ulong> Grants { get; } = new();
    public HashSet<ulong> Exclusions { get; } = new();
    public uint Revision { get; set; }
}

/// <summary>Viewer-specific access, separate from generic owner/lock/crew flags.</summary>
public readonly record struct DeviceAccessFacts(bool Present, bool Friendly, bool CanManage, bool Enabled,
    bool FollowsCupboard, uint Revision);

/// <summary>Shared policy registry. A capability chooses a policy key; no item identity participates in its rules.</summary>
public static class DeviceAuthorizationPolicies
{
    public const string CupboardDevice = "cupboard-device";
    private static readonly IReadOnlyDictionary<string, Func<DeviceAuthorizationState, ulong, bool, bool>> Policies =
        new Dictionary<string, Func<DeviceAuthorizationState, ulong, bool, bool>>(StringComparer.Ordinal)
        {
            [CupboardDevice] = (state, player, inherited) => player != 0 && !state.Exclusions.Contains(player) &&
                (state.Grants.Contains(player) || state.Mode == DeviceAuthorizationMode.FollowCupboard && inherited),
        };

    public static bool Contains(string key) => Policies.ContainsKey(key);

    public static bool IsFriendly(string key, DeviceAuthorizationState state, ulong player, bool inherited) =>
        Policies.TryGetValue(key, out var policy) && policy(state, player, inherited);
}
