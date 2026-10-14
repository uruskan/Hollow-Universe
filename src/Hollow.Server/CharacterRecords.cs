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
    // A stored character comes back where it was if that is on this zone's body and near its ground: within a few
    // hundred metres of it, a pilot up to the leaving height and more (G6: their ship's seat brings them back).
    public static bool Restorable(Zone zone, ZoneSpec spec, CharacterRecord record, out Vector3 local)
    {
        local = zone.Frame.ToLocal(new Vector3D(record.BodyX, record.BodyY, record.BodyZ));
        if (record.SystemAddress != spec.SystemAddress || record.BodyPath != spec.BodyPath || !float.IsFinite(local.LengthSquared()))
        {
            return false;
        }
        float height = zone.HeightAboveGround(local);
        float most = record.Stance == (byte)Stance.Seated ? (float)(zone.Surface.Radius * (ZoneSpec.LeavingRadii + 1)) : 500f;
        return height > -100 && height < most;
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
        // The view is stored as a heading from local north and a pitch where the character stands (the same place it
        // comes back to, so the view comes back exactly).
        Vector3 view = c.Input.View;
        if (zone.Entities.Has<NetPresence>(entity))
        {
            ref NetPresence presence = ref zone.Entities.Get<NetPresence>(entity);
            view = Quantize.FromOctahedral(presence.ViewU, presence.ViewV);
        }
        Vector3 up = zone.Frame.UpAt(c.State.Position);
        float yaw = Facing.YawOf(view, up), pitch = Facing.PitchOf(view, up);
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
