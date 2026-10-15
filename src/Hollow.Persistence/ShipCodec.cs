using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Ships;
using Hollow.Surface;

namespace Hollow.Persistence;

/// <summary>A ship read back from storage, and who was in each of its seats (the server seats them again if they are there).</summary>
public sealed record StoredShip(Ship Ship, EntityId[] Occupants)
{
    /// <summary>Who was at its helm (version 1 kept no other seat).</summary>
    public EntityId Pilot => Occupants.Length > 0 ? Occupants[Ship.Def.Helm.Index] : EntityId.None;
}

/// <summary>
/// The stored form of a ship (G6, GROUND.md §6.7; IW2, IRON_WAKE.md §12): its kind, owner and lock, its motion in the
/// <b>body frame</b> (centre of mass, turn and velocity), so it stays where it was on the planet whatever zone frame the
/// server uses next, its hull, fuel and timers, who sits in each seat, each door's progress, way and health, its moving
/// parts' states, its parts' health, its crew, its orbit marker and its cargo. Version 1 (one pilot, a hatch) comes back as
/// the helm's occupant and the first door; its crew is its owner. Decoding treats the bytes as untrusted: a kind the game no
/// longer has gives nothing, numbers that aren't are made sane, cargo items the game lost are dropped, seats, doors and
/// parts beyond the kind's are left out.
/// </summary>
public static class ShipCodec
{
    /// <summary>Raised by hand when the layout changes; decoding reads every older version. 2: seats, doors, parts, crew (IW2).</summary>
    public const byte Version = 2;

    private const int HatchTicksV1 = 30;

    /// <summary>A ship's bytes, its doors where they are at <paramref name="tick"/>.</summary>
    public static byte[] Encode(Ship ship, in ZoneFrame zone, long tick = 0)
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
            // Seats, doors, parts, health, crew (version 2).
            w.Write((byte)ship.Occupants.Length);
            foreach (EntityId occupant in ship.Occupants)
            {
                w.Write(occupant.Value);
            }
            w.Write((byte)ship.Doors.Length);
            for (int i = 0; i < ship.Doors.Length; i++)
            {
                ShipDoorState d = ship.Doors[i];
                w.Write(ship.DoorAt(i, tick));
                w.Write(d.Moving);
                w.Write(d.Waiting);
                w.Write(d.Health);
            }
            w.Write((byte)ship.Parts.Length);
            foreach (float part in ship.Parts)
            {
                w.Write(part);
            }
            w.Write((byte)ship.PartHealth.Length);
            foreach (float health in ship.PartHealth)
            {
                w.Write(health);
            }
            w.Write((ushort)ship.Crew.Count);
            foreach (ulong player in ship.Crew)
            {
                w.Write(player);
            }
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
        var occupants = new List<EntityId>();
        var doors = new List<(float Progress, sbyte Moving, sbyte Waiting, float Health)>();
        var parts = new List<float>();
        var partHealth = new List<float>();
        var crew = new List<ulong>();
        if (version == 1)
        {
            // One pilot and a hatch: the helm's occupant and the first door.
            bool hatchOpen = r.ReadBoolean();
            int hatch = r.ReadByte();
            occupants.Add(new EntityId(r.ReadUInt64()));
            doors.Add((Math.Clamp(hatch, 0, HatchTicksV1) / (float)HatchTicksV1, (sbyte)(hatchOpen ? 1 : -1), 0, float.NaN));
            crew.Add(owner.Value);
        }
        else
        {
            int seats = r.ReadByte();
            for (int i = 0; i < seats; i++)
            {
                occupants.Add(new EntityId(r.ReadUInt64()));
            }
            int doorCount = r.ReadByte();
            for (int i = 0; i < doorCount; i++)
            {
                doors.Add((r.ReadSingle(), r.ReadSByte(), r.ReadSByte(), r.ReadSingle()));
            }
            int partCount = r.ReadByte();
            for (int i = 0; i < partCount; i++)
            {
                parts.Add(r.ReadSingle());
            }
            int healthCount = r.ReadByte();
            for (int i = 0; i < healthCount; i++)
            {
                partHealth.Add(r.ReadSingle());
            }
            int crewCount = r.ReadUInt16();
            for (int i = 0; i < crewCount; i++)
            {
                crew.Add(r.ReadUInt64());
            }
        }
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
        // The moving parts first (a turret's angle), then the doors over theirs (their parts follow their progress).
        for (int i = 0; i < parts.Count && i < ship.Parts.Length; i++)
        {
            if (float.IsFinite(parts[i]))
            {
                ship.Parts[i] = def.Interior != null && def.Interior.Parts[i].IsDoor ? Math.Clamp(parts[i], 0, 1) : parts[i];
            }
        }
        for (int i = 0; i < doors.Count && i < ship.Doors.Length; i++)
        {
            (float progress, sbyte moving, sbyte waiting, float doorHealth) = doors[i];
            float max = def.Doors[i].Health?.Max ?? 0;
            ship.Doors[i] = new ShipDoorState
            {
                From = float.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0,
                Moving = (sbyte)Math.Sign(moving),
                Waiting = (sbyte)Math.Sign(waiting),
                Health = float.IsFinite(doorHealth) ? Math.Clamp(doorHealth, 0, max) : max,
            };
            if (ship.Doors[i].Moving == 0 && ship.Doors[i].Waiting == 0 && version == 1)
            {
                ship.Doors[i].From = progress;
            }
        }
        if (version == 1 && ship.Doors.Length > 0)
        {
            // Version 1's hatch: where it was, going the way it was going (its motion goes on from the first tick).
            ShipDoorState hatch = ship.Doors[0];
            bool open = doors.Count > 0 && doors[0].Moving > 0;
            hatch.Moving = (sbyte)(open ? (hatch.From < 1 ? 1 : 0) : (hatch.From > 0 ? -1 : 0));
            ship.Doors[0] = hatch;
        }
        for (int i = 0; i < partHealth.Count && i < ship.PartHealth.Length; i++)
        {
            float max = def.Parts[i].Health?.Max ?? 0;
            ship.PartHealth[i] = float.IsFinite(partHealth[i]) ? Math.Clamp(partHealth[i], 0, max) : max;
        }
        ship.RestoreCrew(crew);
        var seated = new EntityId[def.Seats.Count];
        if (version == 1)
        {
            seated[def.Helm.Index] = occupants[0];
        }
        else
        {
            for (int i = 0; i < occupants.Count && i < seated.Length; i++)
            {
                seated[i] = occupants[i];
            }
        }
        return new StoredShip(ship, seated);
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
