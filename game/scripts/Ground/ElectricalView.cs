using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Simulation.Content;
using Hollow.Simulation.Electricity;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;

namespace HollowUniverse.Ground;

/// <summary>Shared authored port cubes and retained route geometry. Neither meshes nor anchors participate in power flow.</summary>
public static class ElectricalView
{
    private sealed class Rig
    {
        public ulong CableId;
        public uint Revision;
        public MeshInstance3D? Cable;
        public readonly List<Vector3> Points = new(PowerPorts.MaximumAnchors + 2);
        public readonly List<Vector3> Scratch = new(PowerPorts.MaximumAnchors + 2);
    }
    private static readonly ConditionalWeakTable<Node3D, Rig> Rigs = new();
    private static readonly BoxMesh PortMesh = new() { Size = Vector3.One };
    private static readonly StandardMaterial3D InputMaterial = new()
    {
        AlbedoColor = new Color(.08f, .65f, .85f), Metallic = .3f, Roughness = .45f,
        EmissionEnabled = true, Emission = new Color(.02f, .18f, .24f),
    };
    private static readonly StandardMaterial3D OutputMaterial = new()
    {
        AlbedoColor = new Color(1f, .51f, .12f), Metallic = .3f, Roughness = .45f,
        EmissionEnabled = true, Emission = new Color(.25f, .1f, .01f),
    };
    private static readonly StandardMaterial3D CableMaterial = new()
    { AlbedoColor = new Color(.025f, .03f, .037f), Roughness = .85f };

    public static Node3D AddPorts(Node3D root, PowerStats stats)
    {
        var ports = new Node3D { Name = "ElectricalPorts" };
        foreach (PowerPort port in stats.Ports)
            ports.AddChild(new MeshInstance3D
            {
                Name = "Port_" + port.Id, Mesh = PortMesh, Position = GroundView.ToGodot(port.Position), Scale = Vector3.One * port.Size,
                MaterialOverride = port.Direction == PowerPortDirection.Input ? InputMaterial : OutputMaterial,
            });
        root.AddChild(ports);
        return ports;
    }

    public static void Update(Node3D root, RemoteThing thing, GameClient client)
    {
        if (thing.Deployable(client.Content)?.Power is not { } stats) return;
        if (!Rigs.TryGetValue(root, out Rig? rig))
        { AddPorts(root, stats); rig = new Rig(); Rigs.Add(root, rig); }
        if (thing.PowerCable is not { } cable)
        {
            rig.Cable?.QueueFree(); rig.Cable = null; rig.CableId = 0; rig.Revision = 0; rig.Points.Clear();
            return;
        }
        if (stats.Port(cable.InputPort) is not { } input)
        {
            rig.Cable?.QueueFree(); rig.Cable = null; rig.CableId = 0; rig.Points.Clear();
            return;
        }
        var outputAt = new System.Numerics.Vector3(cable.OutputX, cable.OutputY, cable.OutputZ);
        if (client.Survival().ThingOf(new EntityId(cable.OutputEntity)) is { } source &&
            source.Deployable(client.Content)?.Power?.Port(cable.OutputPort) is { } output)
            outputAt = PowerPorts.Position(source.Position, source.Up, source.Yaw, output);
        rig.Scratch.Clear();
        rig.Scratch.Add(root.ToLocal(GroundView.ToGodot(outputAt)));
        if (cable.Anchors != null)
            foreach (CableAnchorNet anchor in cable.Anchors)
            {
                var target = new TargetRef(anchor.Support.Family, anchor.Support.Id, anchor.Support.Part);
                var local = new System.Numerics.Vector3(anchor.X, anchor.Y, anchor.Z);
                var at = client.Targets().TryPose(target, out TargetPose pose) ? pose.ToZone(local) :
                    new System.Numerics.Vector3(anchor.WorldX, anchor.WorldY, anchor.WorldZ);
                rig.Scratch.Add(root.ToLocal(GroundView.ToGodot(at)));
            }
        rig.Scratch.Add(root.ToLocal(GroundView.ToGodot(PowerPorts.Position(thing.Position, thing.Up, thing.Yaw, input))));
        bool changed = rig.CableId != cable.CableId || rig.Revision != cable.Revision || rig.Points.Count != rig.Scratch.Count;
        for (int i = 0; !changed && i < rig.Points.Count; i++) changed = rig.Points[i].DistanceSquaredTo(rig.Scratch[i]) > .000001f;
        if (!changed) return;
        rig.Points.Clear(); rig.Points.AddRange(rig.Scratch); rig.CableId = cable.CableId; rig.Revision = cable.Revision;
        rig.Cable?.QueueFree(); rig.Cable = CreateCableMesh(rig.Points); root.AddChild(rig.Cable);
    }

    /// <summary>One tube mesh per routed cable, rebuilt only when its record or resolved poses change.</summary>
    public static MeshInstance3D CreateCableMesh(IReadOnlyList<Vector3> points)
    {
        var instance = new MeshInstance3D { Name = "ElectricalCable", MaterialOverride = CableMaterial };
        if (points.Count < 2) return instance;
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        int vertices = 0;
        const int sides = 6;
        const float radius = .012f;
        for (int segment = 1; segment < points.Count; segment++)
        {
            Vector3 start = points[segment - 1], end = points[segment], delta = end - start;
            if (delta.LengthSquared() < .000001f) continue;
            Vector3 direction = delta.Normalized();
            Vector3 right = direction.Cross(Mathf.Abs(direction.Dot(Vector3.Up)) > .95f ? Vector3.Right : Vector3.Up).Normalized();
            Vector3 up = right.Cross(direction).Normalized();
            for (int side = 0; side < sides; side++)
            {
                float a = side * Mathf.Tau / sides, b = (side + 1) * Mathf.Tau / sides;
                Vector3 na = right * Mathf.Cos(a) + up * Mathf.Sin(a), nb = right * Mathf.Cos(b) + up * Mathf.Sin(b);
                Vertex(start + na * radius, na); Vertex(end + na * radius, na); Vertex(end + nb * radius, nb);
                Vertex(start + na * radius, na); Vertex(end + nb * radius, nb); Vertex(start + nb * radius, nb);
            }
        }
        if (vertices > 0) instance.Mesh = surface.Commit();
        return instance;
        void Vertex(Vector3 point, Vector3 normal) { surface.SetNormal(normal); surface.AddVertex(point); vertices++; }
    }
}
