using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Persistence;
using Hollow.Simulation.Authorization;
using Hollow.Simulation.Electricity;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class UtilityPersistenceTests
{
    private static Deployable Device(ulong id = 20) => new(new EntityId(id), ContentFixture.Content.Deployable("auto-turret")!,
        Vector3.Zero, Vector3.UnitY, 0, new EntityId(10));

    private static byte[] Encode(Deployable device) => WorldCodec.EncodeThing(device, Vector3D.Zero, 0);
    private static Deployable Decode(Deployable source, byte[] bytes) => (Deployable)WorldCodec.DecodeThing(source.Id, source.Kind,
        bytes, ContentFixture.Content, p => new Vector3((float)p.X, (float)p.Y, (float)p.Z), _ => Vector3.UnitY, 0)!;

    private static void Reject(Action read, string message)
    {
        bool rejected = false;
        try { read(); } catch (InvalidDataException) { rejected = true; }
        Assert.True(rejected, message);
    }

    [Test]
    public static void UtilityPortsRoutesAndExplicitAuthorizationSurviveCodec()
    {
        Deployable source = Device();
        source.PowerCable = new CableRecord(91, new PortRef(11, 7), new PortRef(source.Id.Value, 4),
            new[] { new RouteAnchor(TargetRef.Ground, new Vector3(3, 2, 1)),
                new RouteAnchor(new TargetRef(TargetFamily.Piece, 34), new Vector3(.2f, .5f, -.7f)) }, 9);
        source.Authorization.Initialized = true;
        source.Authorization.Mode = DeviceAuthorizationMode.ManualOnly;
        source.Authorization.Grants.Add(13);
        source.Authorization.Exclusions.Add(14);
        source.Authorization.Revision = 12;
        source.PowerEnabled = source.DefenseEnabled = false;
        source.Storage!.Set(0, ItemStack.Of(ContentFixture.Item("pistol"), 1));
        source.Storage.Set(1, ItemStack.Of(ContentFixture.Item("pistol-ammo"), 37));
        Deployable restored = Decode(source, Encode(source));
        Assert.True(restored.PowerCable is { } cable && cable.Id == 91 && cable.Output == source.PowerCable.Output &&
            cable.Input == source.PowerCable.Input && cable.Revision == 9 && cable.Anchors.SequenceEqual(source.PowerCable.Anchors),
            "stable port identities and support-local decorative route survive exactly");
        Assert.True(restored.Authorization.Initialized && restored.Authorization.Mode == DeviceAuthorizationMode.ManualOnly &&
            restored.Authorization.Grants.SetEquals(new ulong[] { 13 }) && restored.Authorization.Exclusions.SetEquals(new ulong[] { 14 }) &&
            restored.Authorization.Revision == 12, "cleared inheritance and independent overrides survive exactly");
        Assert.True(!restored.PowerEnabled && !restored.DefenseEnabled && restored.Storage![1].Count == 37 &&
            restored.Storage[0].Item == source.Storage[0].Item, "intentional disable and inventory are preserved");
        Assert.True(!restored.Powered && restored.PowerInput == 0 && restored.PowerConsumed == 0,
            "derived delivery is recomputed rather than trusted from a save");
    }

    [Test]
    public static void UtilityLegacyMigrationDoesNotImportOwnerImmunityOrDiscardItems()
    {
        Deployable source = Device();
        source.PowerSourceEntityId = 123;
        source.DefenseEnabled = false;
        source.Storage!.Set(1, ItemStack.Of(ContentFixture.Item("pistol-ammo"), 19));
        byte[] bytes = Encode(source);
        // With no cable or local entries the v10 extension is exactly nine bytes.
        byte[] legacy = bytes[..^9];
        legacy[0] = 9;
        Deployable restored = Decode(source, legacy);
        Assert.Equal(123UL, restored.PowerSourceEntityId, "legacy scalar retained for whole-graph migration after all records load");
        Assert.True(restored.PowerCable == null && restored.Authorization.Initialized &&
            restored.Authorization.Mode == DeviceAuthorizationMode.FollowCupboard && restored.Authorization.Grants.Count == 0 &&
            restored.Authorization.Exclusions.Count == 0, "legacy registration cannot seed an owner grant before cupboards restore");
        Assert.True(!restored.PowerEnabled && !restored.DefenseEnabled && restored.Storage![1].Count == 19,
            "either old disabled switch stays disabled, while items remain intact");
    }

    [Test]
    public static void UtilityCodecBoundsAuthorizationAndRefusesInvalidRoutes()
    {
        Deployable source = Device();
        source.Authorization.Initialized = true;
        byte[] bytes = Encode(source);
        byte[] excessive = (byte[])bytes.Clone();
        excessive[^2] = 65;
        Reject(() => Decode(source, excessive), "oversized authorization count rejected before allocation or reading entries");
        byte[] badMode = (byte[])bytes.Clone();
        badMode[^7] = 255;
        Reject(() => Decode(source, badMode), "invalid authorization mode cannot silently restore inheritance");
        source.PowerCable = new CableRecord(91, new PortRef(11, 1), new PortRef(source.Id.Value, 1),
            new[] { new RouteAnchor(TargetRef.Ground, new Vector3(float.NaN, 0, 0)) });
        Reject(() => Encode(source), "nonfinite route cannot be persisted");
        source.PowerCable = source.PowerCable with { Anchors = new RouteAnchor[33] };
        Reject(() => Encode(source), "unbounded route cannot be persisted");
    }
}
