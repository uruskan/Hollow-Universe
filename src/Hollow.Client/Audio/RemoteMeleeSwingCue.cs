using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;

namespace Hollow.Client.Audio;

/// <summary>A remote swing's sound follows the current server clock, like authoritative world feedback.
/// Its animation can remain on the buffered render clock without delaying the swish until after impact.</summary>
public sealed class RemoteMeleeSwingCue
{
    private readonly MeleeSwingCue<(uint Tick, byte Count)> _cue = new();
    private (uint Tick, byte Count) _attempt;
    private ushort _item;

    public void Start(RemoteEntity remote, ItemDef item)
    {
        if (remote.Action != ActionKind.Swing || item.Melee is not { } melee)
        {
            Cancel();
            return;
        }
        _attempt = (remote.ActionTick, remote.ActionCount);
        _item = item.Id;
        _cue.Start(_attempt, remote.ActionTick, (double)remote.ActionTick + melee.StrikeTicks);
    }

    public bool Step(GameClient client, RemoteEntity remote, bool canPresent = true)
    {
        // Natural-state hits have no attacker/attack identity, so arbitrary network reordering cannot be
        // correlated here. Do not infer a hit from proximity or delay its authoritative sound. The shared
        // server clock at least prevents the fixed interpolation delay from inverting swish and impact.
        return _cue.Step(client.ServerTickNow, canPresent && !remote.Sleeping && !remote.Wounded &&
            (remote.Flags & MoveFlags.Dead) == 0 && remote.Stance != Stance.Seated &&
            remote.Action == ActionKind.Swing && remote.Held == _item &&
            (remote.ActionTick, remote.ActionCount) == _attempt);
    }

    public void Cancel() => _cue.Cancel();
}
