using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Targets;

namespace Hollow.Server;

public sealed class WiringModule : ServerModule
{
    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        handlers.On<WireIntent>((PlayerSession session, in WireIntent message) =>
        {
            WireIntent m = message;
            Intent(session, m.Seq, () => Zone.Wiring().Execute(session.Entity, (WireOperation)m.Operation,
                new TargetRef(m.Target.Family, m.Target.Id, m.Target.Part), m.Port, new Vector3(m.X, m.Y, m.Z), m.Revision));
        });
    }
}
