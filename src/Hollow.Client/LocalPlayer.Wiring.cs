using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Targets;

namespace Hollow.Client;

public sealed partial class LocalPlayer
{
    public uint Wire(WireOperation operation, TargetRef target = default, ushort port = 0, Vector3 point = default, uint revision = 0) =>
        Intend(null, w => new WireIntent { Seq = _nextSeq, Operation = (byte)operation, Target = NetConvert.ToNet(target),
            Port = port, X = point.X, Y = point.Y, Z = point.Z, Revision = revision }.WriteMessage(w));
}
