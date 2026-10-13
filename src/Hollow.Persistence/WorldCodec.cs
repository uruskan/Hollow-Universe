using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

namespace Hollow.Persistence;

/// <summary>
/// The stored form of the survival game's state (ARCHITECTURE.md §13: a compact binary payload with a schema version):
/// a character's vitals, inventory and crafting queue, and the static things of a zone. Positions go in the body frame,
/// so a zone moved later keeps them where they were on the planet. Timers of world time are stored as what is left.
/// Decoding treats the bytes as untrusted: an item or recipe the game no longer has is dropped, and anything that
/// doesn't read is refused with an exception for the caller to log.
/// </summary>
public static class WorldCodec
{
    /// <summary>
    /// Raised by hand when a layout changes; decoding reads every older version. 2 (G4): a firearm's magazine and
    /// attachments in its stack; a character's burning and being down. 3: the body the player chose. 4 (G7): the
    /// blueprints learnt, the map's markers and the last death's point.
    /// </summary>
    public const byte PlayerVersion = 4;

    /// <summary>A world prop's stored state (G7, <see cref="EncodeProp"/>).</summary>
    public const byte PropVersion = 1;
    public const byte ThingVersion = 2;

    // ---------------------------------------------------------------- characters

    public static byte[] EncodePlayer(Player p)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(PlayerVersion);
            w.Write(p.Dead);
            Vitals v = p.Vitals;
            w.Write(v.Health);
            w.Write(v.Calories);
            w.Write(v.Hydration);
            w.Write(v.Bleeding);
            w.Write(v.PendingHeal);
            w.Write(v.Temperature);
            w.Write(v.WetSeconds);
            w.Write(p.BeachReadyAtUnixMs);
            w.Write(p.DeathCause);
            w.Write(p.KilledBy);
            w.Write(v.Burning);
            w.Write(p.Wounded);
            w.Write(p.WoundedCause);
            w.Write(p.WoundedByName);
            w.Write(p.WoundedBy.Value);
            WriteContainer(w, p.Inventory.Main);
            WriteContainer(w, p.Inventory.Belt);
            WriteContainer(w, p.Inventory.Wear);
            w.Write(p.Crafting.NextTicket);
            w.Write((byte)p.Crafting.Jobs.Count);
            foreach (CraftJob job in p.Crafting.Jobs)
            {
                w.Write(job.Ticket);
                w.Write(job.Recipe);
                w.Write(job.Count);
                w.Write(job.TicksLeft);
                w.Write(job.UnitTicks);
            }
            w.Write(p.Body);
            // 4 (G7): blueprints, markers (zone frame), the last death's point.
            w.Write((ushort)p.Blueprints.Learnt.Count);
            foreach (ushort recipe in p.Blueprints.Learnt)
            {
                w.Write(recipe);
            }
            w.Write((byte)p.Markers.Count);
            foreach (MapMarker m in p.Markers)
            {
                w.Write(m.Position.X);
                w.Write(m.Position.Y);
                w.Write(m.Position.Z);
                w.Write(m.Colour);
                w.Write(m.Label);
            }
            w.Write(p.DeathPoint.HasValue);
            Vector3 death = p.DeathPoint ?? Vector3.Zero;
            w.Write(death.X);
            w.Write(death.Y);
            w.Write(death.Z);
        }
        return stream.ToArray();
    }

    /// <summary>A character's survival state from its stored bytes.</summary>
    public static Player DecodePlayer(byte[] data, EntityId id, string name, GameContent content)
    {
        var p = new Player(id, name);
        using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        byte version = r.ReadByte();
        if (version is < 1 or > PlayerVersion)
        {
            throw new InvalidDataException($"player state version {version}; this build reads up to {PlayerVersion}");
        }
        p.Dead = r.ReadBoolean();
        p.Vitals = new Vitals
        {
            Health = Finite(r.ReadSingle()),
            Calories = Finite(r.ReadSingle()),
            Hydration = Finite(r.ReadSingle()),
            Bleeding = Finite(r.ReadSingle()),
            PendingHeal = Finite(r.ReadSingle()),
            Temperature = Finite(r.ReadSingle()),
            WetSeconds = Finite(r.ReadSingle()),
        };
        p.BeachReadyAtUnixMs = r.ReadInt64();
        p.DeathCause = r.ReadString();
        p.KilledBy = r.ReadString();
        if (version >= 2)
        {
            p.Vitals.Burning = Math.Max(0, Finite(r.ReadSingle()));
            // Down when saved: the zone finishes it when the character is added (a wounded player who is gone dies).
            p.Wounded = r.ReadBoolean();
            p.WoundedCause = r.ReadString();
            p.WoundedByName = r.ReadString();
            p.WoundedBy = new EntityId(r.ReadUInt64());
        }
        ReadContainer(r, p.Inventory.Main, content, version);
        ReadContainer(r, p.Inventory.Belt, content, version);
        ReadContainer(r, p.Inventory.Wear, content, version);
        uint nextTicket = r.ReadUInt32();
        int jobs = r.ReadByte();
        var list = new List<CraftJob>();
        for (int i = 0; i < jobs; i++)
        {
            var job = new CraftJob { Ticket = r.ReadUInt32(), Recipe = r.ReadUInt16(), Count = r.ReadInt32(), TicksLeft = r.ReadInt32(), UnitTicks = r.ReadInt32() };
            if (content.Recipe(job.Recipe) != null && job.Count is > 0 and <= CraftQueue.MaxCount && job.UnitTicks > 0 && job.TicksLeft is > 0)
            {
                job.TicksLeft = Math.Min(job.TicksLeft, job.UnitTicks);
                list.Add(job);
            }
        }
        p.Crafting.Set(list, nextTicket);
        if (version >= 3)
        {
            p.Body = Math.Min(r.ReadByte(), (byte)1);
        }
        if (version >= 4)
        {
            int learnt = r.ReadUInt16();
            var recipes = new List<ushort>(learnt);
            for (int i = 0; i < learnt; i++)
            {
                recipes.Add(r.ReadUInt16());
            }
            // Only recipes the game has and doesn't teach everyone from the start (untrusted).
            p.Blueprints.Set(content, recipes);
            int markers = r.ReadByte();
            for (int i = 0; i < markers; i++)
            {
                var at = new Vector3(Finite(r.ReadSingle()), Finite(r.ReadSingle()), Finite(r.ReadSingle()));
                byte colour = r.ReadByte();
                string label = r.ReadString();
                if (p.Markers.Count < content.World.MaxMarkers && colour < MapMarker.Colours)
                {
                    p.Markers.Add(new MapMarker(at, colour, label.Length > MapMarker.MaxLabel ? label[..MapMarker.MaxLabel] : label));
                }
            }
            bool died = r.ReadBoolean();
            var point = new Vector3(Finite(r.ReadSingle()), Finite(r.ReadSingle()), Finite(r.ReadSingle()));
            p.DeathPoint = died ? point : null;
        }
        if (p.Dead)
        {
            p.Vitals = default;
            p.Wounded = false;
        }
        return p;
    }

    // ---------------------------------------------------------------- things

    /// <summary>A static thing's bytes; <paramref name="body"/> is its position in the body frame, <paramref name="tick"/> the zone's tick now.</summary>
    public static byte[] EncodeThing(WorldObject thing, Vector3D body, long tick)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(ThingVersion);
            w.Write(body.X);
            w.Write(body.Y);
            w.Write(body.Z);
            w.Write(thing.Yaw);
            switch (thing)
            {
                case Deployable d:
                    w.Write(d.Def.Id);
                    w.Write(d.Owner.Value);
                    w.Write(d.Health);
                    w.Write(d.Lit);
                    w.Write(d.BurnTicksLeft);
                    w.Write(d.ReadyAtUnixMs);
                    w.Write((ushort)d.CookProgress.Length);
                    foreach (int progress in d.CookProgress)
                    {
                        w.Write(progress);
                    }
                    w.Write(d.Storage != null);
                    if (d.Storage != null)
                    {
                        WriteContainer(w, d.Storage);
                    }
                    break;
                case DroppedItem item:
                    WriteStack(w, item.Stack);
                    w.Write(Math.Max(0, item.DespawnTick - tick));
                    break;
                case Corpse corpse:
                    w.Write(corpse.Name);
                    w.Write(corpse.Of.Value);
                    w.Write(Math.Max(0, corpse.DespawnTick - tick));
                    WriteContainer(w, corpse.Loot);
                    break;
            }
        }
        return stream.ToArray();
    }

    /// <summary>
    /// A static thing from its stored bytes, placed in the zone through <paramref name="toLocal"/> (body frame to zone
    /// frame) and <paramref name="upAt"/>; null when its kind of deployable no longer exists.
    /// </summary>
    public static WorldObject? DecodeThing(EntityId id, StaticKind kind, byte[] data, GameContent content,
        Func<Vector3D, Vector3> toLocal, Func<Vector3, Vector3> upAt, long tick)
    {
        using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        byte version = r.ReadByte();
        if (version is < 1 or > ThingVersion)
        {
            throw new InvalidDataException($"thing version {version}; this build reads up to {ThingVersion}");
        }
        Vector3 position = toLocal(new Vector3D(r.ReadDouble(), r.ReadDouble(), r.ReadDouble()));
        float yaw = Finite(r.ReadSingle());
        Vector3 up = upAt(position);
        switch (kind)
        {
            case StaticKind.Deployable:
            {
                ushort item = r.ReadUInt16();
                var owner = new EntityId(r.ReadUInt64());
                if (content.Deployable(item) is not { } def || def == content.ClaimDeployable)
                {
                    return null; // a claim device is saved with the building
                }
                var d = new Deployable(id, def, position, up, yaw, owner)
                {
                    Health = Math.Clamp(Finite(r.ReadSingle()), 1, def.Health),
                    Lit = r.ReadBoolean(),
                    BurnTicksLeft = r.ReadInt32(),
                    ReadyAtUnixMs = r.ReadInt64(),
                };
                int progress = r.ReadUInt16();
                for (int i = 0; i < progress; i++)
                {
                    int value = r.ReadInt32();
                    if (i < d.CookProgress.Length)
                    {
                        d.CookProgress[i] = Math.Max(0, value);
                    }
                }
                if (r.ReadBoolean())
                {
                    if (d.Storage != null)
                    {
                        ReadContainer(r, d.Storage, content, version);
                    }
                    else
                    {
                        ReadContainer(r, new Container(ContainerKind.Box, 1024), content, version);
                    }
                }
                return d;
            }
            case StaticKind.DroppedItem:
            {
                ItemStack stack = ReadStack(r, content, version);
                long left = r.ReadInt64();
                return stack.IsEmpty ? null : new DroppedItem(id, stack, position, up, yaw, tick + Math.Max(0, left));
            }
            case StaticKind.Corpse:
            {
                string name = r.ReadString();
                var of = new EntityId(r.ReadUInt64());
                long left = r.ReadInt64();
                var slots = new List<ItemStack>();
                int size = r.ReadUInt16();
                for (int i = 0; i < size; i++)
                {
                    slots.Add(ReadStack(r, content, version));
                }
                var corpse = new Corpse(id, name, of, Math.Max(1, size), position, up, yaw, tick + Math.Max(0, left));
                for (int i = 0; i < slots.Count; i++)
                {
                    corpse.Loot.Set(i, slots[i]);
                }
                return corpse;
            }
            default:
                return null;
        }
    }

    // ---------------------------------------------------------------- world props (G7)

    /// <summary>A world prop's stored state: gone until a time, or its health, slots and whether it runs.</summary>
    public static byte[] EncodeProp(Simulation.PropOverride o)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(PropVersion);
            w.Write(o.RespawnAtUnixMs);
            w.Write(o.Health);
            w.Write(o.Lit);
            w.Write(o.BurnTicksLeft);
            w.Write(o.Contents != null);
            if (o.Contents != null)
            {
                w.Write((ushort)o.Contents.Length);
                foreach (ItemStack stack in o.Contents)
                {
                    WriteStack(w, stack);
                }
            }
        }
        return stream.ToArray();
    }

    /// <summary>A world prop's stored state from its bytes (untrusted: items the game no longer has come back empty).</summary>
    public static Simulation.PropOverride DecodeProp(byte[] data, GameContent content)
    {
        using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        byte version = r.ReadByte();
        if (version is < 1 or > PropVersion)
        {
            throw new InvalidDataException($"prop version {version}; this build reads up to {PropVersion}");
        }
        long respawn = r.ReadInt64();
        float health = Finite(r.ReadSingle());
        bool lit = r.ReadBoolean();
        int burn = r.ReadInt32();
        ItemStack[]? contents = null;
        if (r.ReadBoolean())
        {
            int size = r.ReadUInt16();
            contents = new ItemStack[Math.Min(size, 64)];
            for (int i = 0; i < size; i++)
            {
                ItemStack stack = ReadStack(r, content, ThingVersion);
                if (i < contents.Length)
                {
                    contents[i] = stack;
                }
            }
        }
        return new Simulation.PropOverride { RespawnAtUnixMs = Math.Max(0, respawn), Health = health, Lit = lit, BurnTicksLeft = Math.Max(0, burn), Contents = contents };
    }

    // ---------------------------------------------------------------- containers

    private static void WriteContainer(BinaryWriter w, Container c)
    {
        w.Write((ushort)c.Size);
        for (int i = 0; i < c.Size; i++)
        {
            WriteStack(w, c[i]);
        }
    }

    private static void ReadContainer(BinaryReader r, Container into, GameContent content, byte version)
    {
        int size = r.ReadUInt16();
        for (int i = 0; i < size; i++)
        {
            ItemStack stack = ReadStack(r, content, version);
            if (i < into.Size)
            {
                into.Set(i, stack);
            }
        }
        into.ClearDirty();
    }

    private static void WriteStack(BinaryWriter w, ItemStack s)
    {
        if (s.IsEmpty)
        {
            w.Write((ushort)0);
            return;
        }
        w.Write(s.Item);
        w.Write(s.Count);
        w.Write(s.Condition);
        w.Write(s.HasWeaponState);
        if (s.HasWeaponState)
        {
            w.Write(s.Loaded);
            w.Write(s.LoadedAmmo);
            w.Write(s.Sight);
            w.Write(s.Muzzle);
            w.Write(s.Rail);
        }
    }

    // A stack of an item the game no longer has, or a count outside its stack size, comes back empty. A firearm keeps
    // only rounds of its calibre (as many as its magazine holds) and attachments it takes, each in its own slot.
    private static ItemStack ReadStack(BinaryReader r, GameContent content, byte version)
    {
        ushort item = r.ReadUInt16();
        if (item == 0)
        {
            return default;
        }
        int count = r.ReadInt32();
        ushort condition = r.ReadUInt16();
        ushort loaded = 0, loadedAmmo = 0;
        var mods = new ushort[3];
        if (version >= 2 && r.ReadBoolean())
        {
            loaded = r.ReadUInt16();
            loadedAmmo = r.ReadUInt16();
            mods[0] = r.ReadUInt16();
            mods[1] = r.ReadUInt16();
            mods[2] = r.ReadUInt16();
        }
        ItemDef? def = content.Item(item);
        if (def == null || count < 1 || count > def.StackSize)
        {
            return default;
        }
        // A key's condition is the number of the lock it was cut for; a blueprint's, the recipe it teaches (G7).
        ushort kept = def.HasCondition ? (ushort)Math.Clamp((int)condition, 1, def.MaxCondition)
            : def.Building == BuildingRole.Key ? condition
            : def == content.World.Blueprint ? condition : (ushort)0;
        if (def == content.World.Blueprint && content.Recipe(kept) is not { Known: false })
        {
            return default; // a blueprint of nothing the game teaches
        }
        var stack = new ItemStack(item, count, kept);
        if (def.Firearm is { } gun)
        {
            if (content.Item(loadedAmmo)?.Bullet is { } round && round.Calibre == gun.Calibre)
            {
                stack.LoadedAmmo = loadedAmmo;
                stack.Loaded = (ushort)Math.Min((int)loaded, gun.Magazine);
            }
            for (int i = 0; i < 3; i++)
            {
                var slot = (AttachmentSlot)i;
                if (content.Item(mods[i]) is { Attachment: { } a } && a.Slot == slot && gun.Takes(a.Item))
                {
                    stack.SetMod(slot, mods[i]);
                }
            }
        }
        return stack;
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;
}
