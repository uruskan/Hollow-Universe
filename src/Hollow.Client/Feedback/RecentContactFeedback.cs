using System;
using System.Collections.Generic;
using Hollow.Simulation.Targets;

namespace Hollow.Client.Feedback;

/// <summary>Bounded per-world victim history; one body's projectile feedback cannot suppress a neighbor's flinch.</summary>
public sealed class RecentContactFeedback
{
    private readonly Dictionary<(TargetFamily Family, ulong Id), (double Time, ulong Version)> _times = new();
    private readonly Queue<((TargetFamily Family, ulong Id) Key, ulong Version)> _order = new();
    private ulong _version;
    private const int Capacity = 256;
    public void Note(in TargetRef target, double time)
    {
        if (target.IsNone || !double.IsFinite(time)) return;
        var key = (target.Family, target.Id);
        ulong version = ++_version;
        _times[key] = (time, version);
        _order.Enqueue((key, version));
        while (_order.Count > Capacity)
        {
            var old = _order.Dequeue();
            if (_times.TryGetValue(old.Key, out var current) && current.Version == old.Version) _times.Remove(old.Key);
        }
    }
    public bool Contains(in TargetRef target, double time) =>
        _times.TryGetValue((target.Family, target.Id), out var when) && time >= when.Time && time - when.Time < 0.4;
    public void Clear() { _times.Clear(); _order.Clear(); }
}
