using System;
using System.Collections.Generic;
using Hollow.Simulation;
using Hollow.Simulation.Combat;

namespace Hollow.Client;

public sealed class AttackRecord
{
    public uint PredictionTick { get; internal set; }
    public bool Predicted { get; internal set; }
    public bool Presented { get; internal set; }
    public AttackResolution? Resolution { get; internal set; }
}

/// <summary>
/// Shared client attack lifecycle. Prediction never implies acceptance; an accepted decision never
/// implies its ammunition snapshot arrived. Presentation is once per identity, independently of ordering.
/// A bounded history outlives network retries; unresolved work is bounded separately.
/// </summary>
public sealed class AttackLedger
{
    public const int HistoryLimit = 4096;
    public const int PendingLimit = 128;
    private readonly Dictionary<AttackId, AttackRecord> _records = new();
    private readonly Queue<AttackId> _finished = new();
    private readonly uint[] _floor = new uint[4];
    private int _pending;
    public uint Session { get; private set; }
    public AttackId Id(AttackDomain domain, uint sequence) => new(domain, sequence, Session);
    public void BeginSession(uint session) { Clear(); Session = session; }
    public event Action<AttackResolution>? Resolved;
    public int Count => _records.Count;
    public int PendingCount => _pending;
    public bool TryGet(AttackId id, out AttackRecord record) => _records.TryGetValue(id, out record!);

    public bool CanPredict(AttackId id) => !id.IsNone && id.Session == Session && (int)id.Kind < _floor.Length &&
        id.Sequence > _floor[(int)id.Kind] && !_records.ContainsKey(id) && _pending < PendingLimit;

    public bool Predict(AttackId id, uint tick)
    {
        if (!CanPredict(id)) return false;
        _records.Add(id, new AttackRecord { PredictionTick = tick, Predicted = true });
        _pending++;
        return true;
    }

    public bool TryPresent(AttackId id)
    {
        if (id.IsNone) return true; // Non-predicted legacy/remote presentation is scoped by its owner.
        if (!_records.TryGetValue(id, out AttackRecord? record)) return false;
        if (record.Presented || record.Resolution is { } r && (r.Cancelled || r.Code != OutcomeCode.Done)) return false;
        record.Presented = true;
        return true;
    }

    public void Resolve(AttackResolution result)
    {
        if (result.Id.IsNone || result.Id.Session != Session || (int)result.Id.Kind >= _floor.Length) return;
        if (!_records.TryGetValue(result.Id, out AttackRecord? record))
        {
            if (result.Id.Sequence <= _floor[(int)result.Id.Kind]) return;
            record = new AttackRecord { PredictionTick = result.RequestedTick };
            _records.Add(result.Id, record);
        }
        if (record.Resolution is { } previous)
        {
            if (previous == result || previous.Cancelled || previous.Code == OutcomeCode.Done && !result.Cancelled) return;
            if (result.Code != OutcomeCode.Done && !result.Cancelled) return;
        }
        else
        {
            if (record.Predicted) _pending--;
            _finished.Enqueue(result.Id);
        }
        record.Resolution = result;
        while (_finished.Count > HistoryLimit)
        {
            AttackId old = _finished.Dequeue();
            _records.Remove(old);
            _floor[(int)old.Kind] = Math.Max(_floor[(int)old.Kind], old.Sequence);
        }
        Resolved?.Invoke(result);
    }

    public void Clear() { _records.Clear(); _finished.Clear(); Array.Clear(_floor); _pending = 0; }
}
