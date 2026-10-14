using Hollow.Net.Protocol;
using Hollow.Simulation;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>The damage pipeline's replication (GROUND.md §6.4.2): what was destroyed, to the clients that draw it.</summary>
public sealed class DamageModule : ServerModule
{
    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        Zone.Events.On<Destroyed>((in Destroyed e) => SendDestroyed(e));
    }

    // Something destroyed, to everyone within range (where it was drawn): reliable, so no wall breaks without its pieces.
    private void SendDestroyed(in Destroyed e)
    {
        Writer.Reset();
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
        }.WriteMessage(Writer);
        SendNear(e.Position, DeliveryMethod.ReliableOrdered);
    }
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): the Destroyed event to the clients.</summary>
public static class DamageModuleAccess
{
    public static DamageModule Damage(this GameServer server) => server.Require<DamageModule>();
}
