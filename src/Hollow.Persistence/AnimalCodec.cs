using System;
using System.IO;
using System.Numerics;
using System.Text;
using Hollow.Core;
using Hollow.Simulation.Entities;

namespace Hollow.Persistence;

/// <summary>An animal's corpse as stored: its species (key), where it lies, what is left to harvest, its timer.</summary>
public readonly record struct CorpseSave(string Species, Vector3 Position, float Yaw, float HarvestLeft, long TicksLeft, EntityId KilledBy,
    string KilledByName);

/// <summary>
/// An animal corpse's stored form (GROUND.md §6.9, G7): animals are never stored, but a corpse survives a restart while
/// its timer runs (world time: it pauses while the server is down). Kept in the <c>things</c> table under
/// <see cref="ThingKind"/>, its position in the body frame; decoded as untrusted (a species that no longer exists is left
/// out by the caller).
/// </summary>
public static class AnimalCodec
{
    /// <summary>The <c>things.kind</c> of an animal corpse (after the explosives' 16).</summary>
    public const byte ThingKind = 17;

    public const byte Version = 1;

    /// <summary>The longest a stored timer may run (a day of ticks): anything longer is nonsense.</summary>
    private const long MaxTicks = 24L * 3600 * 30;

    public static byte[] Encode(in CorpseSave s, Func<Vector3, Vector3D> toBody)
    {
        Vector3D at = toBody(s.Position);
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Version);
            w.Write(s.Species);
            w.Write(at.X);
            w.Write(at.Y);
            w.Write(at.Z);
            w.Write(s.Yaw);
            w.Write(s.HarvestLeft);
            w.Write(s.TicksLeft);
            w.Write(s.KilledBy.Value);
            w.Write(s.KilledByName);
        }
        return stream.ToArray();
    }

    public static CorpseSave Decode(byte[] data, Func<Vector3D, Vector3> toLocal)
    {
        using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        byte version = r.ReadByte();
        if (version is < 1 or > Version)
        {
            throw new InvalidDataException($"animal corpse version {version}; this build reads up to {Version}");
        }
        string species = r.ReadString();
        var at = new Vector3D(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());
        float yaw = r.ReadSingle();
        float left = r.ReadSingle();
        long ticks = r.ReadInt64();
        var killedBy = new EntityId(r.ReadUInt64());
        string killer = r.ReadString();
        if (!double.IsFinite(at.X) || !double.IsFinite(at.Y) || !double.IsFinite(at.Z) || !float.IsFinite(yaw) || !float.IsFinite(left) ||
            species.Length is 0 or > 64 || killer.Length > 64)
        {
            throw new InvalidDataException("animal corpse: a number or name out of range");
        }
        return new CorpseSave(species, toLocal(at), yaw, Math.Max(0.01f, left), Math.Clamp(ticks, 0, MaxTicks), killedBy, killer);
    }
}
