using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Targets;
using HollowUniverse.Build;
using HollowUniverse.Items;

namespace HollowUniverse.Ground;

/// <summary>Retains articulated models across state packets and draws their physical power connections.</summary>
public static class CompoundView
{
    private sealed class Rig
    {
        public readonly List<(Node3D Node, Basis Base, float Angle)> Leaves = new();
        public Node3D? Rotor;
        public Basis RotorRest;
        public float Turn, RotorAngle;
        public bool Positioned;
        public Node3D? LockParent, FittedLock;
        public Transform3D LockPose;
        public LockKind LockKind;
        public readonly List<IDisposable> Presentation = new();
        public Action? OnExit;
    }
    private static readonly ConditionalWeakTable<Node3D, Rig> Rigs = new();

    /// <summary>Detach callbacks immediately, before a replacement node or a different world can reuse the identity.</summary>
    public static void Retire(Node3D root)
    {
        if (!Rigs.TryGetValue(root, out Rig? rig)) return;
        foreach (IDisposable token in rig.Presentation) token.Dispose();
        rig.Presentation.Clear();
        if (rig.OnExit != null && GodotObject.IsInstanceValid(root)) root.TreeExiting -= rig.OnExit;
        Rigs.Remove(root);
    }

    public static void Update(Node3D root, RemoteThing thing, GameClient client, double time, double? delta = null)
    {
        ElectricalView.Update(root, thing, client);
        if (thing.Deployable(client.Content) is not { Assembly: not null } def || root.GetChildCount() == 0) return;
        Node3D model = (Node3D)root.GetChild(0);
        var assembly = client.Content.Compound.Find(def.Assembly);
        Rig rig = Rigs.GetValue(root, _ =>
        {
            var value = new Rig();
            if (assembly != null)
                for (int i = 0; i < assembly.Parts.Count; i++)
                {
                    byte partId = (byte)(i + 1);
                    if (model.FindChild(assembly.Parts[i].Mesh, true, false) is Node3D rendered &&
                        CompoundGeometry.TryPartPose(client.Content, def, false, thing.Position, thing.Up, thing.Yaw, partId, out TargetPose physical))
                        value.Presentation.Add(RenderedTargetFrame.Register(client.Targets(),
                            new TargetRef(TargetFamily.Thing, thing.Id.Value, partId), rendered, physical));
                }
            if (def.Gate != null)
                foreach (var leaf in def.Gate.Parts)
                    if (assembly?.Part(leaf.Part) is { } part && model.FindChild(part.Mesh, true, false) is Node3D node)
                        value.Leaves.Add((node, node.Basis, Mathf.DegToRad(leaf.OpenYaw)));
            if (def.Power is { Wind: true } && assembly?.Part("Rotor") is { } rotor && model.FindChild(rotor.Mesh, true, false) is Node3D spin)
            { value.Rotor = spin; value.RotorRest = spin.Basis; }
            if (def.Gate?.LockSocket is { } socket && assembly?.Part(socket.Part) is { } lockPart &&
                model.FindChild(lockPart.Mesh, true, false) is Node3D lockParent &&
                LockMounts.TryCompoundPose(client.Content, def, false, thing.Position, thing.Up, thing.Yaw, out var lockPose))
            {
                value.LockParent = lockParent;
                var rotation = lockPose.Rotation;
                var worldPose = new Transform3D(new Basis(new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W)), GroundView.ToGodot(lockPose.Position));
                // The source leaf is still at rest here. Its authored wrapper/pivot transform is retained as it swings.
                value.LockPose = lockParent.GlobalTransform.AffineInverse() * worldPose;
            }
            return value;
        });
        if (rig.OnExit == null)
        {
            rig.OnExit = () => Retire(root);
            root.TreeExiting += rig.OnExit;
        }
        float frameDelta = (float)(delta ?? root.GetProcessDeltaTime());
        float target = thing.Utility.Open ? 1 : 0;
        rig.Turn = rig.Positioned ? Mathf.MoveToward(rig.Turn, target, frameDelta / (def.Gate?.Seconds ?? 1)) : target;
        rig.Positioned = true;
        foreach (var leaf in rig.Leaves) leaf.Node.Basis = leaf.Base * new Basis(Vector3.Up, leaf.Angle * rig.Turn);
        if (rig.LockKind != thing.Utility.Lock)
        {
            rig.LockKind = thing.Utility.Lock;
            rig.FittedLock?.QueueFree(); rig.FittedLock = null;
            string? key = client.Content.Building.Locks.FirstOrDefault(l => l.Kind == rig.LockKind)?.Key;
            if (rig.LockParent != null && key != null && ModelCatalog.Instantiate(key) is { } fitted)
            {
                rig.FittedLock = new Node3D { Name = "FittedLock", Transform = rig.LockPose };
                rig.FittedLock.AddChild(fitted);
                rig.LockParent.AddChild(rig.FittedLock);
            }
        }
        if (rig.Rotor != null)
        {
            rig.RotorAngle += frameDelta * (thing.Utility.PowerEnabled ? .9f + thing.Utility.PowerOutput * .015f : 0);
            rig.Rotor.Basis = rig.RotorRest * new Basis(Vector3.Right, rig.RotorAngle);
        }
        DefenseView.Update(model, thing, client.Content, time);
    }
}
