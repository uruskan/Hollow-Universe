using System;
using System.Linq;
using Godot;
using Hollow.Client;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using HollowUniverse.Build;
using HollowUniverse.Items;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>Native checks of renderer-owned poses: actual leaf meshes, physical coordinate frames, replacement and retirement.</summary>
public static class PresentationPoseCheck
{
    public static void Run(Node parent, Action<string, bool, string> expect)
    {
        GameContent content = GameContent.Load(ProjectSettings.GlobalizePath("res://data/ground"));
        using var client = new GameClient(new ClientOptions { Content = content });
        TargetModule targets = client.Targets();
        var holder = new Node3D { Name = "PresentationPoseFixture", Visible = false };
        parent.AddChild(holder);
        try
        {
            CheckDoor(holder, content, targets, expect);
            CheckCompound(holder, content, client, expect);
        }
        finally { holder.Free(); }
    }

    private static void CheckDoor(Node3D holder, GameContent content, TargetModule targets, Action<string, bool, string> expect)
    {
        ulong next = 8000;
        using var world = new BuildingWorld(content.Building, null, () => new EntityId(++next));
        Piece frame = world.AddFirst(BuildingFrame.At(new Vector3D(2, 3, -1), Vector3D.UnitY, 0.3), content.Building.Piece("doorway")!, 1, 1, 0);
        var outcome = world.PlaceDoor(1, content.Building.Door("wooden-door")!, frame, 1, out Door? door);
        if (!outcome.Ok || door == null) throw new InvalidOperationException("Native door fixture could not fit a door: " + outcome);
        BuildingAssets assets = BuildingAssets.Load(content.Building);
        var renderer = new BuildingRenderer { PresentationTargets = targets };
        holder.AddChild(renderer);
        renderer.Attach(world, assets);
        DoorView view = renderer.GetChildren().OfType<DoorView>().Single();
        view.SetProcess(false);
        TargetRef target = door.Ref with { Part = 1 };
        expect("door renderer registers physical hinge pose", targets.TryPresentationPose(target, out TargetPose closed) &&
            Near(closed, Pose(door.Leaf(0, false))), "flipped and rotated frame agrees with authority when shut");
        Node3D leaf = view.LeafMesh(0) ?? throw new InvalidOperationException("The door view has no leaf.");
        Transform3D localPhysical = leaf.GlobalTransform.AffineInverse() * BuildingRenderer.ToGodot(closed.Position, closed.Rotation);
        view.Target = true;
        view._Process(0.5);
        bool hasMiddle = targets.TryPresentationPose(target, out TargetPose middle);
        expect("door contact follows the actual halfway leaf", hasMiddle && Near(middle, Pose(leaf.GlobalTransform * localPhysical)) &&
            !Near(middle, Pose(door.Leaf(0, false))) && !Near(middle, Pose(door.Leaf(0, true))),
            "presentation uses the animated mesh; neither boolean collision endpoint substitutes for it");
        view._Process(0.5);
        expect("door open endpoint preserves physical coordinates", targets.TryPresentationPose(target, out TargetPose opened) && Near(opened, Pose(door.Leaf(0, true))),
            "hinge origin and orientation stay compatible with stored local contact coordinates");
        renderer.RetireWorld();
        expect("building retirement releases door providers immediately", !targets.TryPresentationPose(target, out _), "no frame or queued-free delay retains old-world callbacks");
        renderer.Attach(world, assets);
        expect("building can register again after retirement", targets.TryPresentationPose(target, out _), "new ownership is independent of old door nodes");
        renderer.RetireWorld();

        Piece garageFrame = world.AddFirst(BuildingFrame.At(new Vector3D(9, 0, 0), Vector3D.UnitY, 0), content.Building.Piece("wall-frame")!, 1, 1, 0);
        outcome = world.PlaceDoor(1, content.Building.Door("garage-door")!, garageFrame, 0, out Door? garage);
        if (!outcome.Ok || garage == null) throw new InvalidOperationException("Native garage fixture could not fit its curtain.");
        renderer.Attach(world, assets);
        expect("skinned garage curtain declines rigid attachments", assets.Garage != null && !targets.TryPresentationPose(garage.Ref with { Part = 1 }, out _),
            "a deforming curtain explicitly suppresses rigid marks until surface or bone attachments are supported");
        renderer.RetireWorld();
        renderer.Free();
    }

    private static void CheckCompound(Node3D holder, GameContent content, GameClient client, Action<string, bool, string> expect)
    {
        DeployableDef def = content.Deployable("wood-compound-gate")!;
        CompoundAssembly assembly = content.Compound.Find(def.Assembly)!;
        string leafName = def.Gate!.Parts[0].Part;
        int partIndex = assembly.Parts.ToList().FindIndex(part => part.Name == leafName);
        var thing = new RemoteThing(999) { Id = new EntityId(9000), Kind = StaticKind.Deployable, Look = def.Item.Id,
            Position = new Num.Vector3(7, 2, -3), Up = Num.Vector3.UnitY, Yaw = 0.4f };
        var root = new Node3D { Transform = new Transform3D(NatureView.Upright(thing.Up, thing.Yaw), GroundView.ToGodot(thing.Position)) };
        root.AddChild(ModelCatalog.Instantiate(def.Key) ?? throw new InvalidOperationException("Compound gate model missing."));
        holder.AddChild(root);
        CompoundView.Update(root, thing, client, 0, delta: 0);
        TargetRef target = new(TargetFamily.Thing, thing.Id.Value, (byte)(partIndex + 1));
        Node3D rendered = (Node3D)root.GetChild(0).FindChild(assembly.Parts[partIndex].Mesh, true, false);
        CompoundGeometry.TryPartPose(content, def, false, thing.Position, thing.Up, thing.Yaw, target.Part, out TargetPose closed);
        expect("compound renderer registers authored physical pivot", client.Targets().TryPresentationPose(target, out TargetPose start) && Near(start, closed),
            "imported wrapper transforms are calibrated to the collision target frame");
        Transform3D offset = rendered.GlobalTransform.AffineInverse() * BuildingRenderer.ToGodot(closed.Position, closed.Rotation);
        thing.Utility = new UtilityState { Open = true };
        CompoundView.Update(root, thing, client, 0.5, delta: def.Gate.Seconds * 0.5);
        CompoundGeometry.TryPartPose(content, def, true, thing.Position, thing.Up, thing.Yaw, target.Part, out TargetPose opened);
        expect("compound contact follows actual intermediate gate mesh", client.Targets().TryPresentationPose(target, out TargetPose halfway) &&
            Near(halfway, Pose(rendered.GlobalTransform * offset)) && !Near(halfway, closed) && !Near(halfway, opened),
            "attachment responds to visible articulation rather than the replicated boolean endpoint");
        root.Position += new Vector3(4, 1, -2);
        expect("compound contact follows rendered parent translation", client.Targets().TryPresentationPose(target, out TargetPose moved) &&
            Near(moved, Pose(rendered.GlobalTransform * offset)), "same physical local contact is retained as its root moves");
        CompoundView.Retire(root);
        expect("compound replacement releases provider synchronously", !client.Targets().TryPresentationPose(target, out _), "explicit retirement precedes queued node deletion");
        root.Free();
    }

    private static TargetPose Pose((Num.Vector3 Position, Num.Quaternion Rotation) pose) => new(pose.Position, pose.Rotation);
    private static TargetPose Pose(Transform3D pose)
    {
        Quaternion q = pose.Basis.Orthonormalized().GetRotationQuaternion();
        return new TargetPose(new Num.Vector3(pose.Origin.X, pose.Origin.Y, pose.Origin.Z), new Num.Quaternion(q.X, q.Y, q.Z, q.W));
    }
    private static bool Near(TargetPose a, TargetPose b) => Num.Vector3.Distance(a.Position, b.Position) < 0.001f &&
        MathF.Abs(Num.Quaternion.Dot(a.Rotation, b.Rotation)) > 0.99999f;
}
