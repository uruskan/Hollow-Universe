using System;
using System.IO;
using System.Numerics;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;

namespace Hollow.Server;

/// <summary>
/// How a character is stored and comes back (GROUND.md §4.7): its record from the zone, its survival state from the
/// record, and whether a stored one belongs on this zone's ground.
/// </summary>
internal static class CharacterRecords
{
    // A stored character comes back where it was if that is on this zone's body and within its ground.
    public static bool Restorable(Zone zone, ZoneSpec spec, CharacterRecord record, out Vector3 local)
    {
        local = zone.Frame.ToLocal(new Vector3D(record.BodyX, record.BodyY, record.BodyZ));
        float reach = Simulation.Physics.PhysicsWorld.Extent - 2 * Simulation.Physics.PhysicsWorld.TileSize;
        return record.SystemAddress == spec.SystemAddress && record.BodyPath == spec.BodyPath &&
            MathF.Abs(local.X) < reach && MathF.Abs(local.Z) < reach &&
            // A pilot may be high up, in orbit even (G6): their ship's seat brings them back.
            MathF.Abs(local.Y) < (record.Stance == (byte)Stance.Seated ? 25_000 : 5000);
    }

    /// <summary>The survival state a record holds; null for a new life (a G1 character, or a state this build doesn't read).</summary>
    public static Player? Decode(CharacterRecord record, GameContent content, Action<string> log)
    {
        if (record.State == null)
        {
            return null; // a G1 character: a new life (with the respawn kit) where it stood
        }
        try
        {
            return WorldCodec.DecodePlayer(record.State, record.Id, record.Name, content);
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException)
        {
            log($"{record.Name}'s survival state doesn't load ({e.Message}); a new life");
            return null;
        }
    }

    /// <summary>The record of a character as it stands in the zone, with the account that owns it.</summary>
    public static CharacterRecord Encode(Zone zone, ZoneSpec spec, Entity entity, Player p, long accountId)
    {
        ref Character c = ref zone.Entities.Get<Character>(entity);
        Vector3D body = zone.Frame.ToBody(c.State.Position);
        float yaw = c.Input.Yaw, pitch = c.Input.Pitch;
        if (zone.Entities.Has<NetPresence>(entity))
        {
            ref NetPresence presence = ref zone.Entities.Get<NetPresence>(entity);
            yaw = Quantize.FromAngle16(presence.Yaw);
            pitch = Quantize.FromAngle16(presence.Pitch);
        }
        return new CharacterRecord
        {
            Id = p.Id,
            AccountId = accountId,
            Name = p.Name,
            SystemAddress = spec.SystemAddress,
            BodyPath = spec.BodyPath,
            BodyX = body.X,
            BodyY = body.Y,
            BodyZ = body.Z,
            Yaw = yaw,
            Pitch = pitch,
            Stance = (byte)c.State.Stance,
            Health = c.State.Health,
            SavedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Dead = p.Dead,
            State = WorldCodec.EncodePlayer(p),
        };
    }
}
