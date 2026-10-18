using Hollow.Net.Protocol;
using Hollow.Simulation.Combat;

namespace Hollow.Client;

/// <summary>One response route for all predicted attacks; handlers consume the shared ledger.</summary>
public sealed class AttackModule : ClientModule
{
    private InputCommand _command;
    protected internal override void Init(MessageHandlers<GameClient> handlers)
    {
        handlers.On<AttackResult>((_, in m) => Client.Attacks.Resolve(new AttackResolution(
            new AttackId(m.Kind, m.Seq, m.Session), m.Code, m.RequestedTick, m.StartTick, m.ContactTick,
            m.ReadyTick, m.StateSequence, m.Barrel) { Cancelled = m.Cancelled }));
        Client.Melee.Requested += Request;
        Client.Melee.CancelRequested += Cancel;
        Client.Attacks.Resolved += Client.Melee.Reconcile;
    }
    protected internal override void Command(uint tick, ref InputCommand command, in InputFrame frame) => _command = command;
    private void Request(AttackRequest r) => SendUnordered(new AttackIntent
    {
        Kind = r.Id.Kind, Seq = r.Id.Sequence, Session = r.Id.Session, Tick = r.Tick, Slot = r.Slot, Item = r.Item,
        ViewU = _command.ViewU, ViewV = _command.ViewV, Frame = _command.Frame, ViewDelay = _command.ViewDelay,
    });
    private void Cancel(AttackId id) => SendUnordered(new AttackIntent
    { Kind = id.Kind, Seq = id.Sequence, Session = id.Session, Tick = NextTick, Cancel = true });
    public override void Dispose() { Client.Melee.Reset(); Client.Attacks.Clear(); _command = default; }
}
