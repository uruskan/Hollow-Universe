using System;
using System.IO;
using System.Numerics;
using System.Text;
using Hollow.Core;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Surface;

namespace Hollow.Tests;

public static class ShipAttitudePersistenceTests
{
    private static Ship SavedShip()
    {
        var ship = new Ship(new EntityId(700), ShipAttitudeTests.Carrier, new EntityId(701)) { State = ShipAttitudeTests.Airborne() };
        ship.LoadFuel(19.75f);
        ShipAttitudeHold.Capture(ref ship.State);
        ship.State.Rotation = Quaternion.Normalize(ship.State.Rotation * Quaternion.CreateFromYawPitchRoll(0.12f, -0.18f, 0.21f));
        ship.State.AngularVelocity = new Vector3(0.2f, -0.1f, 0.15f);
        return ship;
    }

    [Test]
    public static void ShipAttitudeSaveDuringBrakingRetainsTargetAcrossFrames()
    {
        Ship ship = SavedShip();
        byte[] bytes = ShipCodec.Encode(ship, ZoneFrame.World);
        Assert.Equal(ShipCodec.Version, bytes[0], "explicit current payload");
        Assert.Equal((byte)1, bytes[^(ShipCodec.AttitudeTrailerBytes + ShipCodec.SpoolBytes)], "fixed trailer activation");
        ZoneFrame frame = ZoneFrame.At(ShipAttitudeTests.Surface, 0.4, -1.2);
        Ship restored = ShipCodec.Decode(ship.Id, bytes, ContentFixture.Content, frame)!.Ship;
        Assert.True(restored.State.AttitudeHoldActive, "active target survives decode");
        Assert.True(ShipAttitudeTests.Degrees(restored.State.Rotation, restored.State.AttitudeHoldRotation) > 5, "braking pose is not substituted for release target");
        foreach (Vector3 axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            Vector3 original = Vector3.Transform(axis, ship.State.AttitudeHoldRotation);
            Vector3 translated = frame.DirectionToLocal(new Vector3D(original.X, original.Y, original.Z));
            Assert.True(Vector3.Distance(translated, Vector3.Transform(axis, restored.State.AttitudeHoldRotation)) < 1e-5,
                "target uses the same body-frame conversion as physical rotation");
        }
        Assert.Equal(ship.State.AngularVelocity, restored.State.AngularVelocity, "spin remains ship-local");
        Assert.Equal(19, restored.Fuel.Units, "one authoritative fuel tank");
        Assert.Near(0.75, restored.Fuel.Reserve, 1e-7, "fractional fuel survives");
        var context = new ShipContext(frame, ShipAttitudeTests.Surface, 0, 0, 1, null);
        Quaternion target = restored.State.AttitudeHoldRotation;
        for (int tick = 0; tick < 900; tick++) ShipMotor.Step(ref restored.State, ShipAttitudeTests.Hover, restored.Def, context);
        Assert.True(ShipAttitudeTests.Degrees(restored.State.Rotation, target) < 0.2, "resumed motor returns to saved release target");
    }

    [Test]
    public static void ShipAttitudeAllLegacyVersionsStartInactiveWithoutLosingMotionOrFuel()
    {
        for (byte version = 1; version <= 5; version++)
        {
            Ship restored = ShipCodec.Decode(new EntityId(72), Legacy(version), ContentFixture.Content, ZoneFrame.World)!.Ship;
            Assert.True(!restored.State.AttitudeHoldActive, $"v{version} has no invented historical release target");
            Assert.Equal(new Vector3(0.2f, -0.3f, 0.4f), restored.State.AngularVelocity, "legacy spin preserved");
            Assert.Near(19.75, restored.State.Fuel, 1e-6, "legacy fuel migrated once");
            Assert.Equal(ShipFlags.EnginesOn, restored.State.Flags, "legacy engine flag preserved");
            ShipAttitudeHold.Capture(ref restored.State);
            Assert.True(ShipAttitudeTests.Degrees(restored.State.Rotation, restored.State.AttitudeHoldRotation) < 0.001,
                "first activation uses restored physical pose");
        }
    }

    [Test]
    public static void ShipAttitudeMalformedTrailerCannotInjectATarget()
    {
        Ship ship = SavedShip();
        byte[] original = ShipCodec.Encode(ship, ZoneFrame.World);
        foreach (Quaternion invalid in new[] { default(Quaternion), new Quaternion(float.NaN, 0, 0, 1),
            new Quaternion(0, float.PositiveInfinity, 0, 1), new Quaternion(0, 0, 0, 9) })
        {
            byte[] data = (byte[])original.Clone();
            using (var stream = new MemoryStream(data))
            using (var writer = new BinaryWriter(stream))
            {
                stream.Position = data.Length - 16 - ShipCodec.SpoolBytes;
                writer.Write(invalid.X); writer.Write(invalid.Y); writer.Write(invalid.Z); writer.Write(invalid.W);
            }
            Ship decoded = ShipCodec.Decode(ship.Id, data, ContentFixture.Content, ZoneFrame.World)!.Ship;
            Assert.True(!decoded.State.AttitudeHoldActive, "invalid quaternion disables hold instead of becoming an identity target");
            Assert.True(ShipAttitudeTests.Degrees(decoded.State.Rotation, ship.State.Rotation) < 0.001, "physical pose remains intact");
        }
        for (int removed = 1; removed <= ShipCodec.AttitudeTrailerBytes + ShipCodec.SpoolBytes; removed++)
        {
            byte[] truncated = original[..^removed];
            Throws<EndOfStreamException>(() => ShipCodec.Decode(ship.Id, truncated, ContentFixture.Content, ZoneFrame.World));
        }
        byte[] badActive = (byte[])original.Clone();
        badActive[^(ShipCodec.AttitudeTrailerBytes + ShipCodec.SpoolBytes)] = 2;
        Throws<InvalidDataException>(() => ShipCodec.Decode(ship.Id, badActive, ContentFixture.Content, ZoneFrame.World));
        ShipAttitudeHold.Clear(ref ship.State);
        Ship inactive = ShipCodec.Decode(ship.Id, ShipCodec.Encode(ship, ZoneFrame.World), ContentFixture.Content, ZoneFrame.World)!.Ship;
        Assert.True(!inactive.State.AttitudeHoldActive, "inactive v6 round trip");
    }

    [Test]
    public static void ShipAttitudeDecodedTargetSurvivesSleepingReseatUntilWake()
    {
        Ship original = SavedShip();
        byte[] bytes = ShipCodec.Encode(original, ZoneFrame.World);
        using var rig = new ShipRig(content: true, worldSpec: ZoneSpec.StartingWorld(10, 20));
        Ship restored = ShipCodec.Decode(original.Id, bytes, ContentFixture.Content, rig.Zone.Frame)!.Ship;
        Quaternion savedTarget = restored.State.AttitudeHoldRotation;
        rig.Zone.Ships().AddShip(restored);
        Entity sleeper = rig.Zone.Players().AddPlayer(rig.Zone.NextId(), "Saved helmsman", rig.Zone.NewCharacter(restored.State.Position));
        Player player = rig.Zone.Players().PlayerOf(sleeper)!;
        player.Sleeping = true;
        Assert.True(rig.Zone.Ships().Reseat(sleeper, restored, restored.Def.Helm.Index), "server restore seat path");
        Assert.Equal(savedTarget, restored.State.AttitudeHoldRotation, "Reseat does not overwrite release target with braking pose");
        rig.Step();
        Assert.True(restored.State.AttitudeHoldActive, "sleeping saved helm retains target in simulation");
        player.Sleeping = false;
        rig.Step();
        Assert.True(!restored.State.AttitudeHoldActive, "reconnect wake clears before piloted step");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new TestFailure($"Expected {typeof(T).Name}");
    }

    // Independent old layouts, not a current encoder with its version byte changed and unknown trailing bytes.
    private static byte[] Legacy(byte version)
    {
        ShipDef def = ShipRig.Finch;
        var ship = new Ship(new EntityId(72), def, new EntityId(73));
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(version); w.Write(def.Id); w.Write(ship.Owner.Value); w.Write(false);
        w.Write(0.0); w.Write(1600.0); w.Write(0.0);
        Quaternion q = Quaternion.CreateFromYawPitchRoll(0.3f, -0.2f, 0.1f);
        w.Write(q.X); w.Write(q.Y); w.Write(q.Z); w.Write(q.W);
        w.Write(0f); w.Write(0f); w.Write(0f);
        w.Write(0.2f); w.Write(-0.3f); w.Write(0.4f);
        w.Write(def.Health); w.Write(19.75f);
        w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)ShipFlags.EnginesOn);
        if (version == 1)
        {
            w.Write(false); w.Write((byte)0); w.Write(0UL);
        }
        else
        {
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((ushort)0);
        }
        w.Write(0.0); w.Write(1600.0); w.Write(0.0);
        WorldCodec.WriteContainer(w, ship.Cargo);
        if (version >= 3) w.Write((byte)0);
        if (version >= 4) { w.Write(0f); w.Write(0f); w.Write(0f); w.Write((byte)0); }
        if (version >= 5) { w.Write(19); w.Write(0.75f); }
        w.Flush();
        return stream.ToArray();
    }
}
