using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Teams;

namespace Hollow.Persistence;

/// <summary>A stored team (G7, GROUND.md §6.8): its leader and members, oldest first. Invites aren't stored.</summary>
public static class TeamCodec
{
    public const byte Version = 1;

    public static byte[] Encode(Team team)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Version);
            w.Write(team.Leader.Value);
            w.Write((byte)Math.Min(team.Members.Count, 255));
            for (int i = 0; i < team.Members.Count && i < 255; i++)
            {
                w.Write(team.Members[i].Value);
            }
        }
        return stream.ToArray();
    }

    /// <summary>A team's leader and members from its bytes (untrusted: the registry drops what doesn't fit).</summary>
    public static (EntityId Leader, List<EntityId> Members) Decode(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        byte version = r.ReadByte();
        if (version is < 1 or > Version)
        {
            throw new InvalidDataException($"team version {version}; this build reads up to {Version}");
        }
        var leader = new EntityId(r.ReadUInt64());
        int count = r.ReadByte();
        var members = new List<EntityId>(count);
        for (int i = 0; i < count; i++)
        {
            members.Add(new EntityId(r.ReadUInt64()));
        }
        return (leader, members);
    }
}
