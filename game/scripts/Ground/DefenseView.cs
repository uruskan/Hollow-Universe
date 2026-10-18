using System;
using System.Runtime.CompilerServices;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Content;
using HollowUniverse.Items;

namespace HollowUniverse.Ground;

/// <summary>Moves the authored assembly pivots and mounts the replicated installed firearm without rebuilding the prop.</summary>
public static class DefenseView
{
    private sealed class Rig
    {
        public Node3D? Yaw, Pitch, Socket, Weapon;
        public Basis YawRest, PitchRest;
        public uint Installed;
        public float YawAngle, PitchAngle;
        public bool Positioned;
    }
    private static readonly ConditionalWeakTable<Node3D, Rig> Rigs = new();

    public static void Update(Node3D model, RemoteThing thing, GameContent content, double time)
    {
        if (thing.Deployable(content) is not { Defense: { } defense } def) return;
        Rig rig = Rigs.GetValue(model, _ => Create(model, def, defense, content));
        // The admitted compound models point +Z; the simulation's heading is along -Z.
        float desiredYaw = thing.Utility.DefenseYaw + Mathf.Pi;
        float desiredPitch = thing.Utility.DefensePitch;
        float blend = rig.Positioned ? 1 - MathF.Exp(-(float)model.GetProcessDeltaTime() * 18) : 1;
        rig.YawAngle = Mathf.LerpAngle(rig.YawAngle, desiredYaw, blend);
        rig.PitchAngle = Mathf.Lerp(rig.PitchAngle, desiredPitch, blend);
        rig.Positioned = true;
        if (rig.Yaw != null) rig.Yaw.Basis = rig.YawRest * new Basis(Vector3.Up, rig.YawAngle);
        // The source elevation axis is local -Z; negative rotation elevates its +X barrel.
        if (rig.Pitch != null) rig.Pitch.Basis = rig.PitchRest * new Basis(Vector3.Forward, -rig.PitchAngle);
        if (rig.Installed == thing.Utility.InstalledWeapon) return;
        rig.Installed = thing.Utility.InstalledWeapon;
        rig.Weapon?.QueueFree();
        rig.Weapon = null;
        if (rig.Socket == null || rig.Installed > ushort.MaxValue || content.Item((ushort)rig.Installed) is not { } weapon) return;
        rig.Weapon = ModelCatalog.Instantiate(weapon.Key);
        if (rig.Weapon != null) rig.Socket.AddChild(rig.Weapon);
    }

    private static Rig Create(Node3D model, DeployableDef def, DefenseStats defense, GameContent content)
    {
        var assembly = content.Compound.Find(def.Assembly);
        string? yaw = assembly?.Part(defense.YawPart)?.Mesh;
        string? pitch = assembly?.Part(defense.PitchPart)?.Mesh;
        var rig = new Rig
        {
            Yaw = yaw == null ? null : Find(model, name => name == yaw),
            Pitch = pitch == null ? null : Find(model, name => name == pitch),
            Socket = Find(model, name => name.EndsWith("_" + defense.WeaponSocket, StringComparison.Ordinal)),
        };
        if (rig.Yaw != null) rig.YawRest = rig.Yaw.Basis;
        if (rig.Pitch != null) rig.PitchRest = rig.Pitch.Basis;
        return rig;
    }

    private static Node3D? Find(Node node, Func<string, bool> matches)
    {
        if (node is Node3D spatial && matches(node.Name.ToString())) return spatial;
        foreach (Node child in node.GetChildren())
            if (Find(child, matches) is { } found) return found;
        return null;
    }
}
