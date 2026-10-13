using System;
using Hollow.Net.Protocol;
using Hollow.Simulation;

namespace Hollow.Server;

// Things breaking apart (GROUND.md §9.4): the zone reports what broke, as the model that stood there; everyone near is told,
// and their clients break it into gibs. Only the fact travels: no pieces, no physics.
public sealed partial class GameServer
{
    /// <summary>Destructions sent (statistics).</summary>
    public long DestructionsSent { get; private set; }

    private void SendDestruction(in ZoneEvent e)
    {
        ModelPlacement m = e.Model;
        _writer.Reset();
        new Destruction
        {
            Receiver = e.Target,
            Id = e.Other.Value,
            Model = m.Model ?? "",
            Part = m.Part ?? "",
            X = m.Position.X,
            Y = m.Position.Y,
            Z = m.Position.Z,
            RotationX = m.Rotation.X,
            RotationY = m.Rotation.Y,
            RotationZ = m.Rotation.Z,
            RotationW = m.Rotation.W,
            Scale = Math.Clamp(m.Scale, 0, 8),
            BlowX = e.Velocity.X,
            BlowY = e.Velocity.Y,
            BlowZ = e.Velocity.Z,
            Cause = e.Cause,
        }.WriteMessage(_writer);
        DestructionsSent++;
        SendNear(m.Position);
    }
}
