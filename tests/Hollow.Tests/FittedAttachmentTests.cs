using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Targets;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FittedAttachmentTests
{
    private static BuildingData Data => ContentFixture.Content.Building;

    private static Piece Window(BuildingWorld world)
    {
        Piece floor = world.AddFirst(BuildingFrame.At(new Vector3D(0, 100, 0), Vector3D.UnitY, 0),
            Data.Piece("foundation")!, 1, 1, 0);
        PlacementPlan plan = world.FromIntent(Data.Piece("window")!, floor, new SocketId(SocketKind.Edge, 0), 0)!;
        return world.Add(plan.Structure!, plan.Piece, 1, plan.Pose, 1, 0);
    }

    [Test]
    public static void WindowAttachmentsSharePlacementRepairPickupAndPersistence()
    {
        var content = ContentFixture.Content;
        DoorDef[] windows = Data.Doors.Where(d => d.Opening == Opening.Window).ToArray();
        Assert.Equal(5, windows.Length, "five existing window assemblies reach the common attachment catalogue");
        foreach (DoorDef def in windows)
        {
            Assert.True(content.Items.Any(i => i.Door == def && i.Places != null), $"{def.Key}: obtainable placeable item");
            Assert.True(content.Recipes.Any(r => r.Output.Door == def), $"{def.Key}: recipe");
            ulong next = 0;
            using var world = new BuildingWorld(Data, null, () => new EntityId(++next));
            Piece window = Window(world);
            Assert.True(world.PlaceDoor(1, def, window, 1, out Door? fitted).Ok, $"{def.Key}: fits window");
            Door door = fitted!;
            Assert.Equal(OutcomeCode.HasDoor, world.CanFitDoor(def, window, 0).Code, "occupied aperture refused");
            Assert.Equal(OutcomeCode.NotUsable, world.Toggle(1, door).Code, "fixed attachment cannot swing");
            Assert.Equal(OutcomeCode.NotUsable, world.AttachKeyLock(1, door, out _).Code, "fixed attachment cannot take a lock");
            var rules = new InteractionRules(content, CapabilityHandlers.Create(content));
            var options = new List<InteractOption>();
            rules.Options(new InteractContext { Content = content, Building = world, Target = door.Ref,
                Doer = new Doer(new EntityId(1), DoerState.None),
                Facts = new TargetFacts { Capabilities = content.Capabilities.Door, Name = def.Name, Position = window.ZoneTransform.Position } }, options);
            Assert.True(options.Count == 1 && content.Interactions.Verb(options[0].Verb)!.Key == "door-pick-up" && options[0].Enabled,
                "fixed attachment offers pickup, never open/close");
            world.Damage(door, new DamageHit(Vector3.UnitZ, Explosive: 37), 10);
            Assert.Near(def.Health - 37, door.Health, 1e-6, "shared protection and damage");
            var wallet = new Wallet { [def.RepairMaterial] = 1000 };
            Assert.Equal(OutcomeCode.RecentlyDamaged, world.Repair(1, door, wallet, 10).Code, "same combat repair delay");
            Assert.True(world.Repair(1, door, wallet, 10 + Data.RepairDelay).Ok && door.Health == def.Health, "shared hammer repair");
            Assert.True(wallet[def.RepairMaterial] < 1000, "repair paid its material");
            byte[] saved = BuildingCodec.EncodeDoor(door);
            using var restored = new BuildingWorld(Data, null, () => new EntityId(1000)) { Mirror = true };
            restored.BeginBatch();
            foreach (Piece piece in world.AllPieces)
                BuildingCodec.RestorePiece(restored, piece.Id, BuildingCodec.EncodePiece(piece, ZoneFrame.World), ZoneFrame.World);
            restored.EndBatch();
            Door copy = BuildingCodec.RestoreDoor(restored, door.Id, saved)!;
            Assert.True(copy != null && copy.Def == def && copy.Flip == 1 && !copy.Open && copy.Health == door.Health,
                "existing save codec preserves definition, flip, state and health");
            Assert.True(world.PickUpDoor(1, door).Ok && window.Door == null, "fixed insert returns through common pickup");
        }
        foreach (string key in new[] { "wooden-double-door", "armoured-double-door" })
        {
            var item = content.ItemByKey(key);
            Assert.True(item.Door?.Opening == Opening.Double && item.Places != null && content.Recipes.Any(r => r.Output == item),
                $"{key}: existing fitted-door definition now has a player recipe and placement");
        }
    }

    [Test]
    public static void WindowCollisionPreservesAuthoredShootingOpenings()
    {
        // Witness points are in the supplied meshes' local frame: an open slit and a solid part of each assembly.
        var witnesses = new[] {
            ("metal-window-bars", new Vector2(.22f, .25f), new Vector2(0, .25f), true),
            ("armoured-window-bars", new Vector2(.32f, .24f), new Vector2(0, .24f), true),
            ("reinforced-glass-window", new Vector2(.22f, .25f), new Vector2(0, .4f), false),
            ("horizontal-embrasure", new Vector2(0, .6f), new Vector2(0, .9f), true),
            ("vertical-embrasure", new Vector2(.4f, .6f), new Vector2(0, .6f), true),
        };
        foreach (var (key, opening, solid, hasOpening) in witnesses)
        {
            using var physics = new PhysicsWorld(new LevelSurface(1000), ZoneFrame.World);
            ulong next = 0;
            using var world = new BuildingWorld(Data, physics, () => new EntityId(++next));
            Piece window = Window(world);
            DoorDef def = Data.Door(key)!;
            Assert.True(world.PlaceDoor(1, def, window, 0, out Door? fitted).Ok, key + " fitted");
            Door door = fitted!;
            (Vector3 position, Quaternion rotation) = door.Leaf(0, false);
            bool Hit(Vector2 at)
            {
                Vector3 start = position + Vector3.Transform(new Vector3(at.X, at.Y, 1), rotation);
                Vector3 direction = Vector3.Transform(-Vector3.UnitZ, rotation);
                return world.Physics!.Ray(start, direction, 2, out _, buildingOnly: true);
            }
            Assert.True(Hit(solid), key + ": visible solid blocks physical ray");
            Assert.Equal(!hasOpening, Hit(opening), key + ": authored opening or glass blocks exactly as drawn");
            (Vector3 frame, Quaternion turn) = window.ZoneTransform;
            Vector3 eye = frame + Vector3.Transform(new Vector3(.2f, 1.5f, 2), turn);
            Assert.True(world.OpeningAimed(eye, Vector3.Transform(-Vector3.UnitZ, turn), 4) == window,
                key + ": aim through aperture selects the supporting window");
        }
    }
}
