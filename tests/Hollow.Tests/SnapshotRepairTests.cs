using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;

namespace Hollow.Tests;

public static class SnapshotRepairTests
{
    [Test]
    public static void GeneratedStateSizesMatchEveryVarintBoundary()
    {
        var writer = new BitWriter();
        CharacterNetState previousCharacter = default;
        ShipNetState previousShip = default;
        foreach (uint value in new uint[] { 0, 1, 127, 128, 16383, 16384, 2097151, 2097152, 268435455, 268435456, uint.MaxValue, 0 })
        {
            var character = new CharacterNetState { ActionItem = 221, ActionTick = value, Frame = value, X = unchecked((int)value), Health = 100 };
            writer.Reset();
            character.Write(writer);
            Assert.Equal(writer.BitCount, character.EncodedBits, $"character full at {value}");
            writer.Reset();
            character.WriteDelta(writer, previousCharacter);
            Assert.Equal(writer.BitCount, character.DeltaBits(previousCharacter), $"character delta at {value}");
            var ship = new ShipNetState { Kind = value, Body = value, X = unchecked((int)value), Health = 255 };
            writer.Reset();
            ship.Write(writer);
            Assert.Equal(writer.BitCount, ship.EncodedBits, $"ship full at {value}");
            writer.Reset();
            ship.WriteDelta(writer, previousShip);
            Assert.Equal(writer.BitCount, ship.DeltaBits(previousShip), $"ship delta at {value}");
            previousCharacter = character;
            previousShip = ship;
        }
        foreach (ulong value in new ulong[] { 0, 127, 128, uint.MaxValue, 1UL << 35, 1UL << 63, ulong.MaxValue })
        {
            writer.Reset();
            writer.WriteVarULong(value);
            Assert.Equal(writer.BitCount, BitWriter.VarUIntBits(value), $"64-bit varint at {value}");
        }
    }

    [Test]
    public static void PilotCruiseHeaderAndActionStatesStayInsideActualPacketBudget()
    {
        var sender = new SnapshotSender();
        var receiver = new SnapshotReceiver();
        var writer = new BitWriter();
        var reader = new BitReader();
        var decoded = new List<(uint NetId, CharacterNetState State)>();
        var decodedShips = new List<(uint NetId, ShipNetState State)>();
        SnapshotCandidate[] crowd = Enumerable.Range(0, 140).Select(i => new SnapshotCandidate
        {
            NetId = 1 + (uint)i * 50000, Weight = 1,
            State = new CharacterNetState { ActionItem = 221, ActionTick = uint.MaxValue, Frame = uint.MaxValue, X = i * 10000, Health = 100 },
        }).ToArray();
        ShipCandidate[] ships = Enumerable.Range(0, 12).Select(i => new ShipCandidate
        {
            NetId = 100 + (uint)i * 50000, Weight = 1,
            State = new ShipNetState { Kind = 6000, Body = uint.MaxValue, X = i * 10000, Health = 255 },
        }).ToArray();
        var seen = new HashSet<uint>();
        for (int tick = 0; tick < 45; tick++)
        {
            var header = new SnapshotHeader
            {
                Own = new OwnState { Frame = uint.MaxValue, Seat = 31, Health = 100 },
                OwnShip = new[] { new OwnShipState { NetId = uint.MaxValue, Kind = uint.MaxValue,
                    CruiseThrottle = 0.75f, CruiseStrafe = -0.5f, CruiseVertical = 0.25f, Fuel = 5999.375f, FuelReserve = 0.375f } },
            };
            writer.Reset();
            sender.Write(writer, header, crowd, ships, 600);
            Assert.True(writer.ByteCount <= 600, $"pilot packet {tick}: {writer.ByteCount} bytes");
            byte[] bytes = writer.ToArray();
            reader.Reset(bytes, 0, bytes.Length);
            reader.ReadBits(8);
            Assert.True(receiver.TryRead(reader, out SnapshotHeader read, decoded, decodedShips), "pilot packet decodes");
            Assert.Near(0.75, read.OwnShip[0].CruiseThrottle, 0, "new cruise field retained in header");
            Assert.Near(0.375, read.OwnShip[0].FuelReserve, 0, "fractional fuel retained in measured header");
            foreach ((uint id, CharacterNetState state) in decoded)
            {
                seen.Add(id);
                Assert.True(state.SameAs(crowd.First(c => c.NetId == id).State), "action/frame state remains exact");
            }
            sender.Acknowledge(receiver.LatestSequence, receiver.AckBits);
        }
        Assert.Equal(crowd.Length, seen.Count, "priority reaches everyone with a pilot and ships sharing packets");
    }
}
