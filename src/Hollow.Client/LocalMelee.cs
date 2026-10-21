using System;
using System.Collections.Generic;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;

namespace Hollow.Client;

/// <summary>Presentation prediction on input ticks. It never applies damage, gathering or hit effects.</summary>
public sealed class LocalMelee
{
    private readonly AttackLedger _attacks;
    private readonly HashSet<AttackId> _awaiting = new();
    private ushort _item;
    private int _slot = -2;
    private uint _ready, _nextSwing, _tick, _nextSequence = 1;
    private long _strike = -1;
    private bool _active, _reached;

    public LocalMelee(AttackLedger? attacks = null) => _attacks = attacks ?? new AttackLedger();

    public AttackId AttackId { get; private set; }
    public uint ActionTick { get; private set; }
    public uint ReadyTick => Math.Max(_ready, _nextSwing);
    public event Action<ItemDef>? Started;
    public event Action<ItemDef>? StrikeReached;
    public event Action? Cancelled;
    public event Action<AttackRequest>? Requested;
    public event Action<AttackId>? CancelRequested;
    public event Action<AttackResolution>? Reconciled;

    public void Tick(uint tick, ItemDef? item, int slot, bool attack, bool canAct, double equipSeconds)
    {
        _tick = tick;
        if (_item != (item?.Id ?? 0) || slot != _slot)
        {
            _item = item?.Id ?? 0;
            _slot = slot;
            _ready = tick + (uint)Math.Max(0, CharacterTicks.From(equipSeconds));
            Cancel();
        }
        if (!canAct || item?.Melee is not { } melee)
        {
            Cancel();
            return;
        }
        if (_strike < 0 && attack && tick >= _nextSwing && tick >= _ready)
        {
            AttackId attempt = _attacks.Id(AttackDomain.Melee, _nextSequence++);
            if (!_attacks.Predict(attempt, tick)) return;
            AttackId = attempt;
            ActionTick = tick;
            _strike = tick + melee.StrikeTicks;
            _nextSwing = tick + (uint)melee.SwingTicks;
            _active = true;
            _reached = false;
            _awaiting.Add(AttackId);
            if (_attacks.TryPresent(AttackId)) Started?.Invoke(item);
            Requested?.Invoke(new AttackRequest(AttackId, tick, (byte)slot, item.Id));
        }
        if (_strike >= 0 && tick >= _strike)
        {
            _strike = -1;
            _reached = true;
            StrikeReached?.Invoke(item);
        }
    }

    /// <summary>The accepted attempt keeps its identity even when a late request shifts its authoritative timing.</summary>
    public void Reconcile(AttackResolution resolution)
    {
        if (resolution.Id.Kind != AttackDomain.Melee) return;
        _awaiting.Remove(resolution.Id);
        if (resolution.Id != AttackId || !_active) return;
        if (resolution.Code != OutcomeCode.Done || resolution.Cancelled)
        {
            _active = false;
            _strike = -1;
            _nextSwing = Math.Max(_tick, resolution.ReadyTick);
            Cancelled?.Invoke();
        }
        else
        {
            ActionTick = resolution.StartTick;
            _nextSwing = resolution.ReadyTick;
            if (!_reached) _strike = resolution.ContactTick;
        }
        Reconciled?.Invoke(resolution);
    }

    public void Cancel()
    {
        if (_awaiting.Count == 0 && !_active && _strike < 0) return;
        // Changing hands abandons every outstanding attempt. Send one cancellation frontier;
        // authority remembers it even if it arrives before the reliable attack requests.
        AttackId through = _active ? AttackId : default;
        foreach (AttackId id in _awaiting)
            if (id.Sequence > through.Sequence) through = id;
        bool wasActive = _active;
        _awaiting.Clear();
        _active = false;
        _strike = -1;
        if (!through.IsNone) CancelRequested?.Invoke(through);
        if (wasActive) Cancelled?.Invoke();
    }

    /// <summary>A new connection/zone starts a fresh prediction timeline.</summary>
    public void Reset()
    {
        if (_active) Cancelled?.Invoke();
        _active = _reached = false;
        _strike = -1;
        _awaiting.Clear();
        _item = 0;
        _slot = -2;
        _ready = _nextSwing = _tick = 0;
        _nextSequence = 1;
        AttackId = default;
        ActionTick = 0;
    }
}
