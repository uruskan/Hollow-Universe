using System;
using System.IO;
using System.Numerics;
using System.Text;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Ships;
using Hollow.Surface;

namespace Hollow.Persistence;

/// <summary>A ship read back from storage, and who was in its seat (the server seats them again if they are there).</summary>
public sealed record StoredShip(Ship Ship, EntityId Pilot);

/// <summary>
/// The stored form of a ship (G6, GROUND.md §6.7): its kind, owner and lock, its motion in the <b>body frame</b>
/// (centre of mass, turn and velocity), so it stays where it was on the planet whatever zone frame the server uses next,
/// its hull, fuel, timers, hatch, pilot, orbit marker and cargo. Decoding treats the bytes as untrusted: a kind the game
/// no longer has gives nothing, numbers that aren't are made sane, cargo items the game lost are dropped.
/// </summary>
public static class ShipCodec
{
    /// <summary>Raised by hand when the layout changes; decoding reads every older version.</summary>
    public const byte Version = 1;

    public static byte[] Encode(Ship ship, in ZoneFrame zone)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            ShipState s = ship.State;
            Quaternion toBody = BodyTurn(zone);
            w.Write(Version);
            w.Write(ship.Def.Id);
            w.Write(ship.Owner.Value);
            w.Write(ship.Locked);
            Write(w, zone.ToBody(s.Position));
            Write(w, Quaternion.Normalize(toBody * s.Rotation));
            Write(w, Vector3.Transform(s.Velocity, toBody));
            Write(w, s.AngularVelocity);
            w.Write(s.Health);
            w.Write(s.Fuel);
            w.Write(s.BoostTicks);
            w.Write(s.CooldownTicks);
            w.Write(s.RestTicks);
            w.Write((ushort)s.Flags);
            w.Write(ship.HatchOpen);
            w.Write((byte)Math.Clamp(ship.HatchProgress, 0, Ship.HatchTicks));
            w.Write(ship.Pilot.Value);
            Write(w, zone.ToBody(ship.OrbitAnchor));
            WorldCodec.WriteContainer(w, ship.Cargo);
        }
        return stream.ToArray();
    }

    /// <summary>A ship from its stored bytes in a zone's frame, or null when the game no longer has its kind.</summary>
    public static StoredShip? Decode(EntityId id, byte[] data, GameContent content, in ZoneFrame zone)
    {
        using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        byte version = r.ReadByte();
        if (version is < 1 or > Version)
        {
            throw new InvalidDataException($"ship version {version}; this build reads up to {Version}");
        }
        ShipDef? def = content.Ship(r.ReadUInt16());
        var owner = new EntityId(r.ReadUInt64());
        bool locked = r.ReadBoolean();
        Vector3D body = ReadD(r);
        Quaternion turn = ReadQ(r);
        Vector3 velocity = Finite(ReadV(r));
        Vector3 spin = Finite(ReadV(r));
        float health = r.ReadSingle(), fuel = r.ReadSingle();
        ushort boost = r.ReadUInt16(), cooldown = r.ReadUInt16(), rest = r.ReadUInt16();
        var flags = (ShipFlags)r.ReadUInt16();
        bool hatchOpen = r.ReadBoolean();
        int hatch = r.ReadByte();
        var pilot = new EntityId(r.ReadUInt64());
        Vector3D anchor = ReadD(r);
        if (def == null)
        {
            return null;
        }
        if (!(body.LengthSquared > 1) || !double.IsFinite(body.LengthSquared))
        {
            throw new InvalidDataException("a ship's position isn't one");
        }
        Quaternion toZone = Quaternion.Conjugate(BodyTurn(zone));
        var ship = new Ship(id, def, owner)
        {
            Locked = locked,
            HatchOpen = hatchOpen,
            HatchProgress = Math.Clamp(hatch, 0, Ship.HatchTicks),
            State = new ShipState
            {
                Position = zone.ToLocal(body),
                Rotation = Quaternion.Normalize(toZone * turn),
                Velocity = Vector3.Transform(velocity, toZone),
                AngularVelocity = spin,
                Health = Math.Clamp(float.IsFinite(health) ? health : 0, 0, def.Health),
                Fuel = Math.Clamp(float.IsFinite(fuel) ? fuel : 0, 0, def.FuelCapacity),
                BoostTicks = boost,
                CooldownTicks = cooldown,
                RestTicks = rest,
                Flags = flags & ~ShipFlags.Destroyed,
            },
            OrbitAnchor = double.IsFinite(anchor.LengthSquared) && anchor.LengthSquared > 1 ? zone.ToLocal(anchor) : zone.ToLocal(body),
        };
        if (ship.State.Health <= 0)
        {
            return null; // a wreck that was never cleared: gone
        }
        WorldCodec.ReadContainer(r, ship.Cargo, content, WorldCodec.PlayerVersion);
        return new StoredShip(ship, pilot);
    }

    /// <summary>The turn from a zone's frame to the body frame (its axes are east, up and south).</summary>
    private static Quaternion BodyTurn(in ZoneFrame zone)
    {
        var m = new Matrix4x4(
            (float)zone.East.X, (float)zone.East.Y, (float)zone.East.Z, 0,
            (float)zone.Up.X, (float)zone.Up.Y, (float)zone.Up.Z, 0,
            (float)zone.South.X, (float)zone.South.Y, (float)zone.South.Z, 0,
            0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    private static void Write(BinaryWriter w, Vector3D v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static void Write(BinaryWriter w, Vector3 v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static void Write(BinaryWriter w, Quaternion q)
    {
        w.Write(q.X);
        w.Write(q.Y);
        w.Write(q.Z);
        w.Write(q.W);
    }

    private static Vector3D ReadD(BinaryReader r) => new(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());

    private static Vector3 ReadV(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    private static Quaternion ReadQ(BinaryReader r)
    {
        var q = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        float length = q.Length();
        return float.IsFinite(length) && length > 0.5f ? Quaternion.Normalize(q) : Quaternion.Identity;
    }

    private static Vector3 Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) ? v : Vector3.Zero;
}
