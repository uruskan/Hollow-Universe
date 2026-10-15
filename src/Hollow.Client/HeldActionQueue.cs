using System;
using Hollow.Simulation.Content;

namespace Hollow.Client;

/// <summary>A cancellable local wind-up. The callback sends an ordinary validated server intent exactly once at its commit tick.</summary>
public sealed class HeldActionQueue
{
    private Func<uint>? _dispatch;
    private ushort _item;
    private int _slot;
    private uint _commitTick, _readyTick;
    public bool Pending => _dispatch != null;

    public bool Schedule(uint tick, ushort item, int slot, string action, float duration, Func<uint> dispatch)
    {
        if (Pending || tick < _readyTick) return false;
        _item = item;
        _slot = slot;
        _commitTick = tick + (uint)CharacterTicks.From(HeldActionTiming.AnticipationSeconds(action));
        _readyTick = tick + (uint)Math.Max(1, CharacterTicks.From(duration));
        _dispatch = dispatch;
        return true;
    }

    public uint Step(uint tick, ushort item, int slot, bool available)
    {
        if (_dispatch == null) return 0;
        if (!available || item != _item || slot != _slot)
        {
            Cancel();
            return 0;
        }
        if (tick < _commitTick) return 0;
        Func<uint> dispatch = _dispatch;
        _dispatch = null; // clear before dispatch: optimistic inventory events may call back into cancellation
        return dispatch();
    }

    public void Cancel()
    {
        if (_dispatch != null) _readyTick = 0;
        _dispatch = null;
    }
}
