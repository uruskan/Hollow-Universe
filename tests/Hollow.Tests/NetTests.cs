using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.NetGen;

namespace Hollow.Tests;

public static class NetTests
{
    private static readonly Assembly NetAssembly = typeof(Protocol).Assembly;

    private static IEnumerable<Type> Definitions(Type attribute) =>
        NetAssembly.GetTypes().Where(t => t.GetCustomAttribute(attribute) != null).OrderBy(t => t.Name, StringComparer.Ordinal);

    /// <summary>
    /// Every message, struct and state in Messages.cs comes back as it went in: filled with random values in range,
    /// written by its generated serializer and read back, many times. New messages are covered without a new test.
    /// </summary>
    [Test]
    public static void EveryMessageSurvivesARoundTrip()
    {
        var types = Definitions(typeof(NetMessageAttribute)).Concat(Definitions(typeof(NetStructAttribute)))
            .Concat(Definitions(typeof(NetStateAttribute))).ToList();
        var rng = new Rng(42);
        var writer = new BitWriter();
        var reader = new BitReader();
        int bits = 0;
        foreach (Type type in types)
        {
            MethodInfo write = type.GetMethod("Write")!;
            MethodInfo read = type.GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!;
            for (int i = 0; i < 200; i++)
            {
                object value = Fill(type, ref rng);
                writer.Reset();
                write.Invoke(value, new object[] { writer });
                bits += writer.BitCount;
                byte[] data = writer.ToArray();
                reader.Reset(data, 0, data.Length);
                object back = read.Invoke(null, new object[] { reader })!;
                Assert.True(!reader.Failed, $"{type.Name} read back without failing");
                Assert.True(reader.RemainingBits < 8, $"{type.Name} read exactly what was written");
                Compare(type, value, back, type.Name);
            }
        }
        Fixtures.Report($"{types.Count} types, {bits / (200 * types.Count)} bits on average");
        Assert.True(types.Count >= 13, "found the definitions");
    }

    /// <summary>Packets come from outside: no reader throws on garbage, whatever the bytes.</summary>
    [Test]
    public static void ReadersSurviveGarbage()
    {
        var rng = new Rng(7);
        var reader = new BitReader();
        var receiver = new SnapshotReceiver();
        var entities = new List<(uint, CharacterNetState)>();
        var readers = Definitions(typeof(NetMessageAttribute)).Concat(Definitions(typeof(NetStructAttribute)))
            .Select(t => t.GetMethod("Read", BindingFlags.Public | BindingFlags.Static)!).ToList();
        for (int i = 0; i < 3000; i++)
        {
            byte[] junk = new byte[rng.NextInt(64)];
            for (int b = 0; b < junk.Length; b++)
            {
                junk[b] = (byte)rng.NextInt(256);
            }
            foreach (MethodInfo read in readers)
            {
                reader.Reset(junk, 0, junk.Length);
                read.Invoke(null, new object[] { reader });
            }
            reader.Reset(junk, 0, junk.Length);
            receiver.TryRead(reader, out _, entities);
        }
        Fixtures.Report($"{receiver.Undecodable} of 3000 random snapshots refused, {receiver.LatestSequence} accepted as latest");
    }

    /// <summary>
    /// Delta compression survives a bad line: 600 snapshots of 40 moving, appearing and disappearing entities, a fifth
    /// of the snapshots and a fifth of the acknowledgements lost, acknowledgements late. Every entity the client
    /// decodes has exactly the state the server sent.
    /// </summary>
    [Test]
    public static void DeltaCompressionSurvivesLoss()
    {
        var rng = new Rng(99);
        var sender = new SnapshotSender();
        var receiver = new SnapshotReceiver();
        var writer = new BitWriter();
        var reader = new BitReader();
        var decoded = new List<(uint NetId, CharacterNetState State)>();
        var world = new Dictionary<uint, CharacterNetState>();
        var acks = new Queue<(int Due, uint Sequence, uint Bits)>();
        uint nextId = 1;
        int received = 0, entitiesChecked = 0;
        long totalBits = 0;
        for (int tick = 0; tick < 600; tick++)
        {
            // The world changes: some appear, some vanish, most move a little.
            if (world.Count < 40 || rng.Chance(0.05))
            {
                world[nextId++] = new CharacterNetState { X = rng.NextInt(100000) - 50000, Y = rng.NextInt(2000), Z = rng.NextInt(100000) - 50000, Health = 100 };
            }
            if (rng.Chance(0.05) && world.Count > 0)
            {
                uint gone = world.Keys.ElementAt(rng.NextInt(world.Count));
                world.Remove(gone);
                sender.Forget(gone);
            }
            foreach (uint id in world.Keys.ToList())
            {
                CharacterNetState s = world[id];
                if (rng.Chance(0.8))
                {
                    s.X += rng.NextInt(140) - 70;
                    s.Z += rng.NextInt(140) - 70;
                    s.Y += rng.NextInt(10) - 5;
                    s.Yaw = (ushort)(s.Yaw + rng.NextInt(2000) - 1000);
                }
                if (rng.Chance(0.02))
                {
                    s.Health = (byte)rng.NextInt(101);
                }
                world[id] = s;
            }
            SnapshotCandidate[] candidates = world.Select(e => new SnapshotCandidate { NetId = e.Key, State = e.Value, Weight = 1 }).ToArray();
            writer.Reset();
            sender.Write(writer, new SnapshotHeader { ServerTick = (uint)tick }, candidates, 4000);
            totalBits += writer.BitCount;
            Assert.Equal(candidates.Length, sender.LastWritten, "all fit in a large budget");

            while (acks.Count > 0 && acks.Peek().Due <= tick)
            {
                (int _, uint sequence, uint bits) = acks.Dequeue();
                sender.Acknowledge(sequence, bits);
            }
            if (rng.Chance(0.2))
            {
                continue; // snapshot lost
            }
            byte[] data = writer.ToArray();
            reader.Reset(data, 0, data.Length);
            Assert.Equal((uint)Protocol.SnapshotTypeId, reader.ReadBits(8), "type byte");
            Assert.True(receiver.TryRead(reader, out SnapshotHeader header, decoded), $"snapshot {tick} decodes");
            received++;
            Assert.Equal((uint)tick, header.ServerTick, "header");
            Assert.Equal(world.Count, decoded.Count, "every entity arrived");
            foreach ((uint id, CharacterNetState state) in decoded)
            {
                Assert.True(state.SameAs(world[id]), $"entity {id} at snapshot {tick} is exact");
                entitiesChecked++;
            }
            if (rng.Chance(0.8))
            {
                acks.Enqueue((tick + 1 + rng.NextInt(4), receiver.LatestSequence, receiver.AckBits));
            }
        }
        double bitsPerEntity = (double)totalBits / 600 / 40;
        Fixtures.Report($"{received} of 600 snapshots received, {entitiesChecked} entity states checked, {bitsPerEntity:0.0} bits per entity");
        Assert.True(bitsPerEntity < 90, "delta coding is compact (a full state is 141 bits)");
    }

    /// <summary>Messages.g.cs is what tools/Hollow.NetGen makes of Messages.cs (run the tool after changing it).</summary>
    [Test]
    public static void GeneratedSerializersAreUpToDate()
    {
        string definitions = File.ReadAllText(Path.Combine(Fixtures.Root, Hollow.NetGen.Program.DefinitionsPath));
        string generated = File.ReadAllText(Path.Combine(Fixtures.Root, Hollow.NetGen.Program.GeneratedPath));
        Assert.True(NetGenerator.Generate(definitions) == generated.Replace("\r\n", "\n", StringComparison.Ordinal),
            "Messages.g.cs is out of date: run dotnet run --project tools/Hollow.NetGen");
    }

    // ---------------------------------------------------------------- random values in range, and comparison

    private static object Fill(Type type, ref Rng rng)
    {
        object value = Activator.CreateInstance(type)!;
        foreach (FieldInfo f in Fields(type))
        {
            f.SetValue(value, RandomValue(f, f.FieldType, ref rng));
        }
        return value;
    }

    private static object RandomValue(FieldInfo f, Type t, ref Rng rng)
    {
        int bits = f.GetCustomAttribute<BitsAttribute>()?.Bits ?? f.GetCustomAttribute<SignedAttribute>()?.Bits ?? 64;
        ulong raw = rng.NextULong() & (bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1);
        long signed = (long)raw - (bits >= 64 ? 0 : (long)(1UL << (bits - 1)));
        if (t.IsEnum)
        {
            return Enum.ToObject(t, raw);
        }
        if (t.IsArray)
        {
            int count = rng.NextInt(f.GetCustomAttribute<MaxCountAttribute>()!.Count + 1);
            Array array = Array.CreateInstance(t.GetElementType()!, count);
            for (int i = 0; i < count; i++)
            {
                array.SetValue(Fill(t.GetElementType()!, ref rng), i);
            }
            return array;
        }
        if (f.GetCustomAttribute<QuantizeAttribute>() is { } q)
        {
            return (float)rng.Range(q.Min, q.Max);
        }
        return t switch
        {
            _ when t == typeof(bool) => rng.Chance(0.5),
            _ when t == typeof(byte) => (byte)raw,
            _ when t == typeof(ushort) => (ushort)raw,
            _ when t == typeof(uint) => f.GetCustomAttribute<VarIntAttribute>() != null ? (uint)(rng.NextULong() >> (32 + rng.NextInt(32))) : (uint)raw,
            _ when t == typeof(ulong) => f.GetCustomAttribute<VarIntAttribute>() != null ? rng.NextULong() >> rng.NextInt(64) : raw,
            _ when t == typeof(sbyte) => (sbyte)Math.Clamp(signed, sbyte.MinValue, sbyte.MaxValue),
            _ when t == typeof(short) => (short)Math.Clamp(signed, short.MinValue, short.MaxValue),
            _ when t == typeof(int) => (int)Math.Clamp(signed, int.MinValue, int.MaxValue),
            _ when t == typeof(long) => signed,
            _ when t == typeof(float) => (float)rng.Range(-1e6, 1e6),
            _ when t == typeof(double) => rng.Range(-1e9, 1e9),
            _ when t == typeof(string) => RandomText(f.GetCustomAttribute<MaxLengthAttribute>()!.Bytes, ref rng),
            _ => Fill(t, ref rng),
        };
    }

    private static string RandomText(int maxBytes, ref Rng rng)
    {
        const string letters = "abcdefghijklmnopqrstuvwxyz ABCXYZ0123456789éüß";
        var text = new System.Text.StringBuilder();
        int length = rng.NextInt(maxBytes / 2 + 1);
        for (int i = 0; i < length; i++)
        {
            text.Append(letters[rng.NextInt(letters.Length)]);
        }
        return text.ToString();
    }

    private static void Compare(Type type, object expected, object actual, string path)
    {
        foreach (FieldInfo f in Fields(type))
        {
            object? a = f.GetValue(expected), b = f.GetValue(actual);
            string where = path + "." + f.Name;
            if (f.FieldType.IsArray)
            {
                var x = (Array?)a ?? Array.Empty<object>();
                var y = (Array?)b ?? Array.Empty<object>();
                Assert.Equal(x.Length, y.Length, where + " length");
                for (int i = 0; i < x.Length; i++)
                {
                    Compare(f.FieldType.GetElementType()!, x.GetValue(i)!, y.GetValue(i)!, $"{where}[{i}]");
                }
            }
            else if (f.GetCustomAttribute<QuantizeAttribute>() is { } q)
            {
                double step = (q.Max - q.Min) / ((1L << q.Bits) - 1);
                Assert.Near((float)a!, (float)b!, step / 2 + 1e-6, where);
            }
            else if (f.FieldType.GetCustomAttribute<NetStructAttribute>() != null)
            {
                Compare(f.FieldType, a!, b!, where);
            }
            else
            {
                Assert.True(Equals(a ?? "", b ?? ""), $"{where}: sent {a}, got {b}");
            }
        }
    }

    private static IEnumerable<FieldInfo> Fields(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.MetadataToken);
}
