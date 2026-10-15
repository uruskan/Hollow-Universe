using System;
using Hollow.Simulation.Content;

namespace Hollow.Client;

/// <summary>Presentation prediction on input ticks. It never applies damage, gathering or hit effects.</summary>
public sealed class LocalMelee
{
    private ushort _item;
    private int _slot = -2;
    private uint _ready, _nextSwing;
    private long _strike = -1;
    public uint ActionTick { get; private set; }
    public event Action<ItemDef>? Started;
    public event Action<ItemDef>? StrikeReached;
    public event Action? Cancelled;

    public void Tick(uint tick, ItemDef? item, int slot, bool attack, bool canAct, double equipSeconds)
    {
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
            ActionTick = tick;
            _strike = tick + melee.StrikeTicks;
            _nextSwing = tick + (uint)melee.SwingTicks;
            Started?.Invoke(item);
        }
        if (_strike >= 0 && tick >= _strike)
        {
            _strike = -1;
            StrikeReached?.Invoke(item);
        }
    }

    public void Cancel()
    {
        if (_strike >= 0)
        {
            _strike = -1;
            Cancelled?.Invoke();
        }
    }
}
