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
/// record, and whether a stored one belongs on this zone's ground. One logged off aboard a ship (IRON_WAKE.md §4.11) is
/// stored in the ship's frame and comes back aboard, at the same place on the deck.
/// </summary>
internal static class CharacterRecords
{
    /// <summary>
    /// A stored character comes back where it was if that is on this zone's body and near its ground: within a few
    /// hundred metres of it, a pilot up to the leaving height and more (G6: their ship's seat brings them back). One stored
    /// aboard a ship comes back aboard it: at start before the ships do (<paramref name="pending"/>: the ships' module puts
    /// it ashore if its ship doesn't come back), later only while the ship is here. The state it comes back in: standing
    /// (or crouching) where it was, in its frame.
    /// </summary>
    public static bool Restorable(Zone zone, ZoneSpec spec, CharacterRecord record, out CharacterState state, bool pending = false)
    {
        state = default;
        if (record.SystemAddress != spec.SystemAddress || record.BodyPath != spec.BodyPath)
        {
            return false;
        }
        var stance = (Stance)Math.Min(record.Stance, (byte)Stance.Crouching);
        if (record.Frame != 0)
        {
            var local = new Vector3((float)record.BodyX, (float)record.BodyY, (float)record.BodyZ);
            if (!float.IsFinite(local.LengthSquared()) || local.Length() > 500 || (!pending && zone.Frames.Ship(record.Frame) == null))
            {
                return false;
            }
            state = zone.NewCharacter(local + Vector3.UnitY * 0.02f);
            state.Frame = record.Frame;
            state.Stance = stance;
            return true;
        }
        Vector3 at = zone.Frame.ToLocal(new Vector3D(record.BodyX, record.BodyY, record.BodyZ));
        if (!float.IsFinite(at.LengthSquared()))
        {
            return false;
        }
        float height = zone.HeightAboveGround(at);
        float most = record.Stance == (byte)Stance.Seated ? (float)(zone.Surface.Radius * (ZoneSpec.LeavingRadii + 1)) : 500f;
        if (!(height > -100 && height < most))
        {
            return false;
        }
        state = zone.NewCharacter(at + zone.Frame.UpAt(at) * 0.02f);
        state.Stance = stance;
        return true;
    }

    /// <summary>The view a stored character comes back with, in its frame: its heading and pitch where it stands (aboard, against the deck).</summary>
    public static Vector3 ViewOf(Zone zone, CharacterRecord record, in CharacterState state) =>
        Facing.View(record.Yaw, record.Pitch, state.Frame == 0 ? zone.Frame.UpAt(state.Position) : Vector3.UnitY);

    /// <summary>The view a sleeper wakes with, in its frame: the heading it slept facing, the stored pitch.</summary>
    public static Vector3 WokenView(Zone zone, in Character c, float pitch)
    {
        Vector3 up = c.State.Frame == 0 ? zone.Frame.UpAt(c.State.Position) : Vector3.UnitY;
        Vector3 view = zone.Frames.ViewIn(c.Input.View, c.Input.Frame, c.State.Frame, zone.Tick);
        return Facing.View(Facing.YawOf(view, up), pitch, up);
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

    /// <summary>
    /// The record of a character as it stands in the zone, with the account that owns it: its feet in the body frame, or
    /// aboard a ship in the ship's frame with the ship's ID (schema 8, IRON_WAKE.md §4.11); its view as a heading and a
    /// pitch where it stands (aboard, against the deck).
    /// </summary>
    public static CharacterRecord Encode(Zone zone, ZoneSpec spec, Entity entity, Player p, long accountId)
    {
        ref Character c = ref zone.Entities.Get<Character>(entity);
        bool aboard = c.State.Frame != 0 && zone.Frames.Ship(c.State.Frame) != null;
        Vector3D body = aboard ? new Vector3D(c.State.Position.X, c.State.Position.Y, c.State.Position.Z) : zone.Frame.ToBody(zone.WorldOf(c.State));
        // The view is stored as a heading from local north and a pitch where the character stands (the same place it
        // comes back to, so the view comes back exactly).
        Vector3 view = c.Input.View;
        ulong viewFrame = c.Input.Frame;
        if (zone.Entities.Has<NetPresence>(entity))
        {
            ref NetPresence presence = ref zone.Entities.Get<NetPresence>(entity);
            view = Quantize.FromOctahedral(presence.ViewU, presence.ViewV);
            viewFrame = c.State.Frame;
        }
        ulong frame = aboard ? c.State.Frame : 0;
        view = zone.Frames.ViewIn(view, viewFrame, frame, zone.Tick);
        Vector3 up = aboard ? Vector3.UnitY : zone.Frame.UpAt(zone.WorldOf(c.State));
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
            Frame = frame,
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
