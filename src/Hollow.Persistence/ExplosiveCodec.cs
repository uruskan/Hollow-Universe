using System;
using System.IO;
using System.Numerics;
using System.Text;
using Hollow.Core;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Targets;

namespace Hollow.Persistence;

/// <summary>
/// An explosive's stored form (GROUND.md §6.6, G5): a charge stuck or lying in the world with its fuse left survives a
/// restart; its fuse is world time, which pauses while the server is down, so it goes off as many seconds after the
/// start as it had left. Kept in the <c>things</c> table under <see cref="ThingKind"/>. Positions in the body frame
/// (directions too: the zone's frame may move between runs); decoded as untrusted.
/// </summary>
public static class ExplosiveCodec
{
    /// <summary>The <c>things.kind</c> of an explosive (after the static things' kinds).</summary>
    public const byte ThingKind = 16;

    /// <summary>2: what holds it is a TargetRef (family, ID, part); its pose there is worked out again when it comes back.</summary>
    public const byte Version = 2;

    /// <summary>The bytes of an explosive; <paramref name="toBody"/> takes a zone point to the body frame.</summary>
    public static byte[] Encode(ExplosiveSave s, Func<Vector3, Vector3D> toBody)
    {
        Vector3D at = toBody(s.Position);
        Vector3D velocity = toBody(s.Position + s.Velocity) - at;
        Vector3D normal = toBody(s.Position + s.Normal) - at;
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Version);
            w.Write(s.Item);
            w.Write(s.Owner.Value);
            w.Write(s.OwnerName);
            w.Write((byte)s.State);
            w.Write(s.Dud);
            Write(w, at);
            Write(w, velocity);
            Write(w, normal);
            w.Write(s.FuseLeft);
            w.Write(s.FuseTicks);
            w.Write(s.WillDud);
            w.Write(s.Arms);
            w.Write((byte)s.Support.Family);
            w.Write(s.Support.Id);
            w.Write(s.Support.Part);
            w.Write(s.DespawnLeft);
        }
        return stream.ToArray();
    }

    /// <summary>An explosive from its bytes, placed in the zone through <paramref name="toLocal"/> (body frame to zone frame).</summary>
    public static ExplosiveSave Decode(EntityId id, byte[] data, Func<Vector3D, Vector3> toLocal)
    {
        using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        byte version = r.ReadByte();
        if (version is < 1 or > Version)
        {
            throw new InvalidDataException($"explosive version {version}; this build reads up to {Version}");
        }
        ushort item = r.ReadUInt16();
        var owner = new EntityId(r.ReadUInt64());
        string ownerName = r.ReadString();
        byte state = r.ReadByte();
        bool dud = r.ReadBoolean();
        Vector3D at = Read(r), velocity = Read(r), normal = Read(r);
        Vector3 position = toLocal(at);
        long fuseLeft = r.ReadInt64();
        int fuseTicks = r.ReadInt32();
        bool willDud = r.ReadBoolean();
        int arms = r.ReadInt32();
        TargetRef held = version >= 2 ? ReadSupport(r) : ReadSupportV1(r);
        long despawnLeft = r.ReadInt64();
        if (state > (byte)ExplosiveState.Stuck)
        {
            throw new InvalidDataException($"explosive state {state} unknown");
        }
        return new ExplosiveSave
        {
            Id = id,
            Item = item,
            Owner = owner,
            OwnerName = ownerName.Length > 64 ? ownerName[..64] : ownerName,
            State = (ExplosiveState)state,
            Dud = dud,
            Position = position,
            Velocity = toLocal(at + velocity) - position,
            Normal = toLocal(at + normal) - position,
            FuseLeft = Math.Max(-1, fuseLeft),
            FuseTicks = fuseTicks,
            WillDud = willDud,
            Arms = arms,
            Support = held,
            DespawnLeft = Math.Max(-1, despawnLeft),
        };
    }

    // Version 2: what held it, as a TargetRef (the family a number from storage: one no family has is nothing).
    private static TargetRef ReadSupport(BinaryReader r)
    {
        byte family = r.ReadByte();
        ulong id = r.ReadUInt64();
        byte part = r.ReadByte();
        return Enum.IsDefined((TargetFamily)family) ? new TargetRef((TargetFamily)family, id, part) : TargetRef.None;
    }

    // Version 1 named it by its own kind (the ground, a piece, a door and its leaf, a deployable, a natural object) and kept
    // its pose on a door's leaf; the pose is worked out again where it lies.
    private static TargetRef ReadSupportV1(BinaryReader r)
    {
        byte kind = r.ReadByte();
        ulong id = r.ReadUInt64();
        int leaf = r.ReadInt32();
        for (int i = 0; i < 6; i++)
        {
            r.ReadSingle();
        }
        TargetFamily[] families = { TargetFamily.Ground, TargetFamily.Piece, TargetFamily.Door, TargetFamily.Thing, TargetFamily.Natural };
        if (kind >= families.Length)
        {
            throw new InvalidDataException($"explosive support {kind} unknown");
        }
        return new TargetRef(families[kind], families[kind] == TargetFamily.Ground ? 0 : id, families[kind] == TargetFamily.Door ? (byte)(Math.Clamp(leaf, 0, 8) + 1) : (byte)0);
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
            throw new InvalidDataException("explosive position isn't finite");
        }
        return v;
    }
}
