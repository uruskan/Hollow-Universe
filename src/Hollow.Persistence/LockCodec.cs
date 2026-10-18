using System;
using System.IO;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;

namespace Hollow.Persistence;

/// <summary>The existing door-lock payload, shared by structural doors and compound gate hosts.</summary>
internal static class LockCodec
{
    public static void Write(BinaryWriter writer, DoorLock? fitted)
    {
        writer.Write((byte)(fitted?.Kind ?? LockKind.None));
        if (fitted == null) return;
        writer.Write(fitted.Id.Value);
        writer.Write(fitted.Owner);
        writer.Write(fitted.Locked);
        switch (fitted)
        {
            case KeyLock key:
                writer.Write(key.KeyCode);
                break;
            case CodeLock code:
                writer.Write(code.CodeHash);
                writer.Write((ushort)code.Authorised.Count);
                foreach (ulong player in code.Authorised) writer.Write(player);
                break;
        }
    }

    public static DoorLock? Read(BinaryReader reader, int maximumAuthorised)
    {
        LockKind kind = (LockKind)reader.ReadByte();
        if (kind == LockKind.None) return null;
        if (kind is not (LockKind.Key or LockKind.Code)) throw new InvalidDataException($"Unknown stored lock kind {(byte)kind}");
        var id = new EntityId(reader.ReadUInt64());
        ulong owner = reader.ReadUInt64();
        bool locked = reader.ReadBoolean();
        if (kind == LockKind.Key) return LockState.RestoreKey(id, owner, reader.ReadInt32(), locked);
        ulong hash = reader.ReadUInt64();
        int count = reader.ReadUInt16();
        var authorised = new ulong[Math.Min(count, Math.Clamp(maximumAuthorised, 0, 1024))];
        for (int i = 0; i < count; i++)
        {
            ulong player = reader.ReadUInt64();
            if (i < authorised.Length) authorised[i] = player;
        }
        return LockState.RestoreCode(id, owner, hash, locked, authorised, maximumAuthorised);
    }
}
