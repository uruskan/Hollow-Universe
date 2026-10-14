using Hollow.Net.Protocol;
using Hollow.Simulation;
using LiteNetLib;

namespace Hollow.Server;

// The damage pipeline's replication (GROUND.md §6.4.2): what was destroyed, to the clients that draw it.
public sealed partial class GameServer
{
    // Something destroyed, to everyone within range (where it was drawn): reliable, so no wall breaks without its pieces.
    private void SendDestroyed(in Destroyed e)
    {
        _writer.Reset();
        new ThingDestroyed
        {
            Target = new TargetNet { Family = e.Target.Family, Id = e.Target.Id, Part = e.Target.Part },
            Key = e.Key is { Length: > 48 } key ? key[..48] : e.Key,
            Grade = (byte)System.Math.Min(e.Grade, (byte)7),
            X = e.Position.X,
            Y = e.Position.Y,
            Z = e.Position.Z,
            RotationX = e.Rotation.X,
            RotationY = e.Rotation.Y,
            RotationZ = e.Rotation.Z,
            RotationW = e.Rotation.W,
            DirectionX = e.Direction.X,
            DirectionY = e.Direction.Y,
            DirectionZ = e.Direction.Z,
            Cause = e.Cause,
            Open = e.Open,
            Attacker = NetIdOf(e.Player),
            Item = e.Weapon,
        }.WriteMessage(_writer);
        SendNear(e.Position, DeliveryMethod.ReliableOrdered);
    }
}
