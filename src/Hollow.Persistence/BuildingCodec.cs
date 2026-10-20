using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Surface;

namespace Hollow.Persistence;

/// <summary>A stored piece, door (with its lock) or claim device (with its list and store): kind and versioned bytes.</summary>
public sealed record BuildingRecord(EntityId Id, BuildPartKind Kind, byte[] Data);

/// <summary>A claim device as stored, with what the zone keeps of it: its store's slots and the structure's upkeep debt.</summary>
public sealed record StoredClaim(ClaimRecord Claim, List<ItemStack> Store, Dictionary<string, double> Debt);

/// <summary>A claim device's own fields, to put it back.</summary>
public readonly record struct ClaimRecord(EntityId Id, EntityId Support, Vector3D Position, int Yaw, ulong Owner, double Health, ulong[] Authorised);

/// <summary>
/// The stored form of the building (GROUND.md §4.7, §6.3, phase 2; ARCHITECTURE.md §13): one record per piece, door
/// and claim device, so a save writes only what changed. A piece keeps its structure's frame in the body frame (12
/// doubles; pieces of one lattice store the same ones and come back in one lattice), its exact pose there, kind, grade,
/// health, owner, when it was placed and last damaged (real-clock seconds). A door keeps its frame piece, kind, turn,
/// open, health and its lock: a key lock's number, a code lock's code as a salted hash and who it lets in. A claim
/// device keeps its piece, place, list, health, store and what its structure owes. Decoding treats the bytes as
/// untrusted: anything that no longer exists (a piece kind, a door kind, an item) is left out.
/// </summary>
public static class BuildingCodec
{
    public const byte PieceVersion = 1;
    public const byte DoorVersion = 2;
    public const byte ClaimVersion = 1;

    // ---------------------------------------------------------------- frames between the zone and the body

    private static Vector3D ToBodyDirection(in ZoneFrame zone, Vector3D d) => zone.East * d.X + zone.Up * d.Y + zone.South * d.Z;

    private static Vector3D ToLocalDirection(in ZoneFrame zone, Vector3D d) =>
        new(Vector3D.Dot(d, zone.East), Vector3D.Dot(d, zone.Up), Vector3D.Dot(d, zone.South));

    private static void WriteFrame(BinaryWriter w, in BuildingFrame f, in ZoneFrame zone)
    {
        Write(w, zone.ToBody(f.Origin.X, f.Origin.Y, f.Origin.Z));
        Write(w, ToBodyDirection(zone, f.AxisX));
        Write(w, ToBodyDirection(zone, f.AxisY));
        Write(w, ToBodyDirection(zone, f.AxisZ));
    }

    private static BuildingFrame ReadFrame(BinaryReader r, in ZoneFrame zone)
    {
        Vector3D origin = ToLocalDirection(zone, Read(r) - zone.Origin);
        Vector3D x = ToLocalDirection(zone, Read(r)), y = ToLocalDirection(zone, Read(r)), z = ToLocalDirection(zone, Read(r));
        return new BuildingFrame(origin, x, y, z);
    }

    private static void Write(BinaryWriter w, Vector3D v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    private static Vector3D Read(BinaryReader r)
    {
        var v = new Vector3D(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
        if (!double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z))
        {
            throw new InvalidDataException("a position that isn't finite");
        }
        return v;
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            write(w);
        }
        return stream.ToArray();
    }

    private static BinaryReader Reader(byte[] data, byte newest, string what, out byte version)
    {
        var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        version = r.ReadByte();
        if (version < 1 || version > newest)
        {
            throw new InvalidDataException($"{what} version {version}; this build reads up to {newest}");
        }
        return r;
    }

    // ---------------------------------------------------------------- pieces

    public static byte[] EncodePiece(Piece p, in ZoneFrame zone)
    {
        BuildingFrame frame = p.Structure.Frame;
        ZoneFrame z = zone;
        return Bytes(w =>
        {
            w.Write(PieceVersion);
            WriteFrame(w, frame, z);
            w.Write((ushort)p.Def.Id);
            w.Write((byte)p.Grade);
            w.Write((byte)p.Pose.Yaw);
            Write(w, p.Pose.Position);
            w.Write(p.Health);
            w.Write(p.Owner);
            w.Write(p.PlacedAt);
            w.Write(double.IsFinite(p.LastDamagedAt) ? p.LastDamagedAt : double.MinValue);
        });
    }

    /// <summary>Puts a stored piece back into a building (in a batch: stability is solved at its end); null when its kind no longer exists.</summary>
    public static Piece? RestorePiece(BuildingWorld building, EntityId id, byte[] data, in ZoneFrame zone)
    {
        using BinaryReader r = Reader(data, PieceVersion, "piece", out _);
        BuildingFrame frame = ReadFrame(r, zone);
        int kind = r.ReadUInt16();
        int grade = r.ReadByte();
        int yaw = r.ReadByte();
        Vector3D position = Read(r);
        double health = r.ReadDouble();
        ulong owner = r.ReadUInt64();
        double placedAt = r.ReadDouble();
        double damagedAt = r.ReadDouble();
        if (building.Data.Piece(kind) is not { } def || grade >= building.Data.Grades.Count || !double.IsFinite(health) || health <= 0)
        {
            return null;
        }
        return building.Restore(frame, def, grade, new Pose(position, PieceGeometry.Wrap(yaw)), owner, Finite(placedAt), health, id,
            lastDamagedAt: damagedAt == double.MinValue || !double.IsFinite(damagedAt) ? double.NegativeInfinity : damagedAt);
    }

    // ---------------------------------------------------------------- doors and their locks

    public static byte[] EncodeDoor(Door d) => Bytes(w =>
    {
        w.Write(DoorVersion);
        w.Write(d.Frame.Id.Value);
        w.Write(d.Def.Key);
        w.Write((byte)d.Layout);
        w.Write(d.Open);
        w.Write(d.Health);
        w.Write(d.Owner);
        LockCodec.Write(w, d.Lock);
    });

    /// <summary>Puts a stored door (and its lock) back on its frame piece; null when the piece or the door's kind is gone.</summary>
    public static Door? RestoreDoor(BuildingWorld building, EntityId id, byte[] data)
    {
        using BinaryReader r = Reader(data, DoorVersion, "door", out byte version);
        var frameId = new EntityId(r.ReadUInt64());
        string key = r.ReadString();
        // Version 1 stored a half turn of the whole door (hinge on the other side and swinging the other way: layout 3).
        int layout = version < 2 ? (r.ReadByte() != 0 ? 3 : 0) : r.ReadByte();
        bool open = r.ReadBoolean();
        double health = r.ReadDouble();
        ulong owner = r.ReadUInt64();
        if (building.PieceOf(frameId) is not { } frame || building.Data.Door(key) is not { } def || !double.IsFinite(health) || health <= 0)
        {
            return null;
        }
        Door? door = building.RestoreDoor(id, def, frame, layout, owner, open, health);
        if (door == null)
        {
            return null;
        }
        ((ILockHost)door).Lock = LockCodec.Read(r, building.Data.LockMaxAuthorised);
        building.ObserveLock(door.Lock);
        ((ILockHost)door).LockChanged();
        return door;
    }

    private static ulong[] ReadPlayers(BinaryReader r)
    {
        int count = r.ReadUInt16();
        var players = new ulong[Math.Min(count, 1024)];
        for (int i = 0; i < count; i++)
        {
            ulong player = r.ReadUInt64();
            if (i < players.Length)
            {
                players[i] = player;
            }
        }
        return players;
    }

    // ---------------------------------------------------------------- claim devices

    /// <summary>A claim device with its store's slots and what its structure owes.</summary>
    public static byte[] EncodeClaim(ClaimDevice c, Container? store, IReadOnlyDictionary<string, double> debt) => Bytes(w =>
    {
        w.Write(ClaimVersion);
        w.Write(c.Support.Id.Value);
        Write(w, c.Position);
        w.Write((byte)c.Yaw);
        w.Write(c.Owner);
        w.Write(c.Health);
        w.Write((ushort)c.Authorised.Count);
        foreach (ulong player in c.Authorised)
        {
            w.Write(player);
        }
        int size = store?.Size ?? 0;
        w.Write((ushort)size);
        for (int i = 0; i < size; i++)
        {
            ItemStack s = store![i];
            w.Write(s.IsEmpty ? (ushort)0 : s.Item);
            if (!s.IsEmpty)
            {
                w.Write(s.Count);
                w.Write(s.Condition);
            }
        }
        w.Write((byte)debt.Count);
        foreach ((string material, double owed) in debt.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            w.Write(material);
            w.Write(owed);
        }
    });

    /// <summary>A stored claim device read back (the server puts it on its piece and fills its store).</summary>
    public static StoredClaim DecodeClaim(EntityId id, byte[] data, GameContent content)
    {
        using BinaryReader r = Reader(data, ClaimVersion, "claim device", out _);
        var support = new EntityId(r.ReadUInt64());
        Vector3D position = Read(r);
        int yaw = r.ReadByte();
        ulong owner = r.ReadUInt64();
        double health = r.ReadDouble();
        ulong[] authorised = ReadPlayers(r);
        int size = r.ReadUInt16();
        var store = new List<ItemStack>();
        for (int i = 0; i < size; i++)
        {
            ushort item = r.ReadUInt16();
            if (item == 0)
            {
                store.Add(default);
                continue;
            }
            int count = r.ReadInt32();
            ushort condition = r.ReadUInt16();
            ItemDef? def = content.Item(item);
            store.Add(def == null || count < 1 || count > def.StackSize ? default : new ItemStack(item, count, condition));
        }
        var debt = new Dictionary<string, double>(StringComparer.Ordinal);
        int debts = r.ReadByte();
        for (int i = 0; i < debts; i++)
        {
            string material = r.ReadString();
            debt[material] = r.ReadDouble();
        }
        return new StoredClaim(new ClaimRecord(id, support, position, yaw, owner, double.IsFinite(health) ? health : 1, authorised), store, debt);
    }

    // ---------------------------------------------------------------- the zone's own record

    public const byte ZoneVersion = 1;

    /// <summary>What a zone keeps of itself: when upkeep and decay were last applied (real-clock seconds).</summary>
    public static byte[] EncodeZone(double buildingAdvancedAt) => Bytes(w =>
    {
        w.Write(ZoneVersion);
        w.Write(double.IsFinite(buildingAdvancedAt) ? buildingAdvancedAt : 0);
    });

    /// <summary>The zone's record, or NaN for none (a G2 save: nothing to catch up).</summary>
    public static double DecodeZone(byte[]? data)
    {
        if (data == null || data.Length == 0)
        {
            return double.NaN;
        }
        using BinaryReader r = Reader(data, ZoneVersion, "zone", out _);
        double at = r.ReadDouble();
        return double.IsFinite(at) && at > 0 ? at : double.NaN;
    }

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
}
