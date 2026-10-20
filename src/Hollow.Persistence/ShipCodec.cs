using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Ships.Modules;
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
    /// <summary>
    /// Raised by hand when the layout changes; decoding reads every older version. 2: seats, doors, parts, crew (IW2); 3: the
    /// crewed batteries' stations (IW3): the chosen kind, the mount's aim, the shots, each chamber, each loader's cycle and
    /// the magazine. 4: unattended controls. 5: the fuel tank's items and fractional burn reserve. 6: captured attitude.
    /// 7: the drive's spool (a rotor's spin, docs/MINICOPTER.md §9), so a craft held hovering is still hovering after a restart.
    /// 8: one block right after its kind (VEHICLES.md §9.1): its build, so the layout its seats, doors and parts are numbered by
    /// is known before they are read, and its modules' holds, each by the socket and level of its module. An older version's
    /// bytes are a version 8's without that block.
    /// </summary>
    public const byte Version = 8;

    /// <summary>Where version 8's block starts (after the version and the kind), and its size for a ship without modules.</summary>
    public const int ModulesAt = 3, NoModulesBytes = 2;

    public const int AttitudeTrailerBytes = 17;

    /// <summary>Version 7's spool, after the attitude trailer: the stored form's last bytes.</summary>
    public const int SpoolBytes = 4;

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
            // Version 8: the build, and the holds, each by its module's socket and level.
            w.Write((byte)ship.Def.Build.Count);
            foreach (Installed entry in ship.Def.Build.Entries)
            {
                w.Write(entry.Socket);
                w.Write(entry.Level);
                w.Write(entry.Module);
            }
            w.Write((byte)ship.Holds.Length);
            for (int i = 0; i < ship.Holds.Length; i++)
            {
                Installed module = ship.Def.Placed[ship.Def.Holds[i].Module].Entry;
                w.Write(module.Socket);
                w.Write(module.Level);
                WorldCodec.WriteContainer(w, ship.Holds[i]);
            }
            w.Write(ship.Owner.Value);
            w.Write(ship.Locked);
            Write(w, zone.ToBody(s.Position));
            Write(w, Quaternion.Normalize(toBody * s.Rotation));
            Write(w, Vector3.Transform(s.Velocity, toBody));
            Write(w, s.AngularVelocity);
            w.Write(s.Health);
            w.Write(ship.Fuel.Amount);
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
            // The stations (version 3): a loader's cycle as seconds into it now (it goes on from there when loaded).
            w.Write((byte)ship.Stations.Length);
            foreach (StationState st in ship.Stations)
            {
                w.Write(st.Chosen);
                w.Write(st.Yaw);
                w.Write(st.Pitch);
                w.Write(st.Shots);
                w.Write((byte)st.Chambers.Length);
                foreach (ushort chamber in st.Chambers)
                {
                    w.Write(chamber);
                }
                w.Write((byte)st.Loaders.Length);
                foreach (LoaderState l in st.Loaders)
                {
                    w.Write(l.Active);
                    w.Write(l.Waiting);
                    w.Write(l.Reverse);
                    w.Write(l.Elapsed(tick));
                    w.Write(l.Kind);
                    w.Write(l.Count);
                }
                WorldCodec.WriteContainer(w, st.Magazine);
            }
            // Version 4: unattended translation/assist survives a restart; turning and boost are momentary.
            w.Write(ship.Input.Throttle);
            w.Write(ship.Input.Strafe);
            w.Write(ship.Input.Vertical);
            w.Write((byte)(ship.Input.Buttons & ShipButtons.AssistOff));
            // Version 5: unopened fuel items and a non-withdrawable partial unit. The earlier float is only a cache.
            w.Write(ship.Fuel.Units);
            w.Write(ship.Fuel.Reserve);
            // Version 6: a fixed trailer; both physical and commanded orientation live in the body's frame on disk.
            bool hold = s.AttitudeHoldActive && ShipAttitudeHold.Valid(s.AttitudeHoldRotation);
            w.Write((byte)(hold ? 1 : 0));
            Write(w, hold ? Quaternion.Normalize(toBody * s.AttitudeHoldRotation) : Quaternion.Identity);
            // Version 7: the drive's spool.
            w.Write(s.Spool);
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
        var holds = new List<(byte Socket, byte Level, Container Stored)>();
        if (version >= 8)
        {
            // The build, made sane on the kind as it is now (a module the game lost, a socket gone: left out).
            var entries = new Installed[r.ReadByte()];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new Installed(r.ReadByte(), r.ReadByte(), r.ReadUInt16());
            }
            if (def != null && entries.Length > 0)
            {
                def = content.Compose(def, ShipAssembler.Sane(def, entries, content.Module));
            }
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                byte socket = r.ReadByte(), level = r.ReadByte();
                var stored = new Container(ContainerKind.Box, 64);
                WorldCodec.ReadContainer(r, stored, content, WorldCodec.PlayerVersion);
                holds.Add((socket, level, stored));
            }
        }
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
        // Each hold onto the hold of the module at its socket and level; one whose module is gone was read and is dropped.
        foreach ((byte socket, byte level, Container stored) in holds)
        {
            int hold = HoldAt(def, socket, level);
            for (int k = 0; hold >= 0 && k < stored.Size && k < ship.Holds[hold].Size; k++)
            {
                ship.Holds[hold].Set(k, stored[k]);
            }
            if (hold >= 0)
            {
                ship.Holds[hold].ClearDirty();
            }
        }
        if (version >= 3)
        {
            ReadStations(r, ship, content);
        }
        if (version >= 4)
        {
            static float Axis(float value) => float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
            ship.Input = new ShipInput
            {
                Throttle = Axis(r.ReadSingle()), Strafe = Axis(r.ReadSingle()), Vertical = Axis(r.ReadSingle()),
                Buttons = (ShipButtons)r.ReadByte() & ShipButtons.AssistOff,
            };
        }
        if (version >= 5)
        {
            ship.Fuel.Restore(r.ReadInt32(), r.ReadSingle());
            ship.SyncFuel();
        }
        else
        {
            ship.LoadFuel(fuel);
        }
        ShipAttitudeHold.Clear(ref ship.State);
        if (version >= 6)
        {
            byte active = r.ReadByte();
            // Read raw: ReadQ repairs invalid rotations to identity, which would invent a target for malformed data.
            var target = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            if (active > 1) throw new InvalidDataException("invalid ship attitude hold activation");
            if (active == 1 && ShipAttitudeHold.Valid(target))
            {
                ship.State.AttitudeHoldRotation = Quaternion.Normalize(toZone * target);
                ship.State.AttitudeHoldActive = true;
            }
        }
        if (version >= 7)
        {
            float spool = r.ReadSingle();
            ship.State.Spool = float.IsFinite(spool) ? Math.Clamp(spool, 0, 1) : 0;
        }
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

    // The stations of version 3, each onto the kind's station of its index (more than the kind has are read and left out):
    // kinds the battery doesn't take are dropped, angles and times made sane, the magazine keeping only its rockets.
    private static void ReadStations(BinaryReader r, Ship ship, GameContent content)
    {
        int count = r.ReadByte();
        for (int i = 0; i < count; i++)
        {
            byte chosen = r.ReadByte();
            float yaw = r.ReadSingle(), pitch = r.ReadSingle();
            uint shots = r.ReadUInt32();
            var chambers = new ushort[r.ReadByte()];
            for (int b = 0; b < chambers.Length; b++)
            {
                chambers[b] = r.ReadUInt16();
            }
            var loaders = new LoaderState[r.ReadByte()];
            for (int l = 0; l < loaders.Length; l++)
            {
                loaders[l] = new LoaderState
                {
                    Active = r.ReadBoolean(), Waiting = r.ReadBoolean(), Reverse = r.ReadBoolean(), At = r.ReadSingle(), Kind = r.ReadUInt16(), Count = r.ReadByte(),
                };
            }
            var magazine = new Container(ContainerKind.Box, 64);
            WorldCodec.ReadContainer(r, magazine, content, WorldCodec.PlayerVersion);
            if (i >= ship.Stations.Length)
            {
                continue;
            }
            StationState st = ship.Stations[i];
            Simulation.Content.BatteryDef battery = st.Def.Battery;
            st.Chosen = (byte)Math.Min(chosen, Math.Max(0, battery.Ammo.Count - 1));
            st.Yaw = float.IsFinite(yaw) ? yaw : 0;
            st.Pitch = float.IsFinite(pitch) ? Math.Clamp(pitch, battery.Mount.PitchMin, battery.Mount.PitchMax) : 0;
            st.Shots = shots;
            for (int b = 0; b < st.Chambers.Length && b < chambers.Length; b++)
            {
                st.Chambers[b] = battery.Takes(chambers[b]) ? chambers[b] : (ushort)0;
            }
            float seconds = battery.Loader.Seconds;
            for (int l = 0; l < st.Loaders.Length && l < loaders.Length; l++)
            {
                LoaderState s = loaders[l];
                s.At = float.IsFinite(s.At) ? Math.Clamp(s.At, 0, seconds) : 0;
                s.Kind = battery.Takes(s.Kind) ? s.Kind : (ushort)0;
                s.Count = s.Kind == 0 ? (byte)0 : (byte)Math.Min(s.Count, (byte)Math.Max(1, battery.RoundsPerLoad));
                st.Loaders[l] = s.Active ? s : default;
            }
            for (int k = 0; k < magazine.Size && k < st.Magazine.Size; k++)
            {
                ItemStack stack = magazine[k];
                st.Magazine.Set(k, battery.Takes(stack.Item) ? stack : default);
            }
            st.Magazine.ClearDirty();
            // The mount's parts follow its aim.
            if (st.Def.YawPart >= 0 && st.Def.YawPart < ship.Parts.Length)
            {
                ship.Parts[st.Def.YawPart] = st.Yaw;
            }
            if (st.Def.PitchPart >= 0 && st.Def.PitchPart < ship.Parts.Length)
            {
                ship.Parts[st.Def.PitchPart] = st.Pitch;
            }
        }
    }

    // The hold of the module that stands at a socket and level, or −1.
    private static int HoldAt(ShipDef def, byte socket, byte level)
    {
        for (int i = 0; i < def.Holds.Count; i++)
        {
            Installed entry = def.Placed[def.Holds[i].Module].Entry;
            if (entry.Socket == socket && entry.Level == level)
            {
                return i;
            }
        }
        return -1;
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
