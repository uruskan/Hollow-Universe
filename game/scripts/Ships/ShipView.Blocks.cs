using System;
using System.Collections.Generic;
using Godot;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Ships.Modules;

namespace HollowUniverse.Ships;

/// <summary>
/// A ship with no model (a kind whose art is not made yet, VEHICLES.md §7) drawn as blocks of the right size: each hull
/// box, the chassis's in steel and each module's in its own colour (the Tin Finch's paint: pale blue cabs, cream engines),
/// and a tyre and rim at each wheel that ride their springs, steer and roll. Presentation only.
/// </summary>
public partial class ShipView
{
    private static readonly Color Steel = new(0.36f, 0.39f, 0.41f), Rubber = new(0.11f, 0.11f, 0.12f), Rim = new(0.72f, 0.69f, 0.64f);

    private readonly List<WheelNode> _wheels = new();

    private sealed class WheelNode
    {
        public required WheelDef Def;
        public required Node3D Hub;
        public required Node3D Steer;
        public required Node3D Spin;
        public float Angle;
    }

    /// <summary>Draws the blocks and wheels when the catalogue has no model for this kind.</summary>
    private void BuildBlocks()
    {
        var colours = new Color[_def.Hull.Count];
        Array.Fill(colours, Steel);
        foreach (PlacedModule placed in _def.Placed)
        {
            for (int b = 0; b < placed.Module.Hull.Count && placed.FirstBox + b < colours.Length; b++)
            {
                colours[placed.FirstBox + b] = ModuleColour(placed.Module);
            }
        }
        for (int i = 0; i < _def.Hull.Count; i++)
        {
            HullBox box = _def.Hull[i];
            var mesh = new MeshInstance3D
            {
                Name = $"Block{i}",
                Mesh = new BoxMesh { Size = new Vector3(box.HalfExtents.X, box.HalfExtents.Y, box.HalfExtents.Z) * 2 },
                Position = new Vector3(box.Centre.X, box.Centre.Y, box.Centre.Z),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = colours[i], Roughness = 0.75f, Metallic = 0.2f },
            };
            _model.AddChild(mesh);
        }
        foreach (WheelDef w in _def.Wheels)
        {
            var hub = new Node3D { Name = w.Key, Position = new Vector3(w.Hub.X, w.Hub.Y, w.Hub.Z) };
            var steer = new Node3D { Name = "Steer" };
            var spin = new Node3D { Name = "Spin" };
            // A cylinder stands along Y: laid on its side, its axis along the axle (X).
            var tyre = new MeshInstance3D
            {
                Name = "Tyre",
                Mesh = new CylinderMesh { TopRadius = w.Radius, BottomRadius = w.Radius, Height = w.Width, RadialSegments = 24 },
                RotationDegrees = new Vector3(0, 0, 90),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Rubber, Roughness = 0.95f },
            };
            var rim = new MeshInstance3D
            {
                Name = "Rim",
                Mesh = new CylinderMesh { TopRadius = w.Radius * 0.48f, BottomRadius = w.Radius * 0.48f, Height = w.Width * 1.04f, RadialSegments = 12 },
                RotationDegrees = new Vector3(0, 0, 90),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Rim, Roughness = 0.5f, Metallic = 0.6f },
            };
            spin.AddChild(tyre);
            spin.AddChild(rim);
            steer.AddChild(spin);
            hub.AddChild(steer);
            _model.AddChild(hub);
            _wheels.Add(new WheelNode { Def = w, Hub = hub, Steer = steer, Spin = spin });
        }
    }

    // The Tin Finch's paint by what a module is: cabs pale blue, engines cream, the rest faded red.
    private static Color ModuleColour(ModuleDef module) =>
        module.Seats.Count > 0 ? new Color(0.62f, 0.75f, 0.8f) : module.Power > 0 ? new Color(0.91f, 0.87f, 0.78f) : new Color(0.72f, 0.36f, 0.25f);

    /// <summary>
    /// Poses the wheels: each on its spring where the ground under it is (<paramref name="groundBelow"/>: how far below a
    /// point the ground is, or null for none within reach), the front ones turned by <paramref name="steer"/> (radians, + right),
    /// all rolled by the speed along the vehicle's way.
    /// </summary>
    public void ShowWheels(Func<Vector3, float?> groundBelow, float steer, float along, double delta)
    {
        if (_rig.Count > 0)
        {
            ShowRig(groundBelow, steer, along, delta); // the model's own (ShipView.Rig.cs)
            return;
        }
        foreach (WheelNode w in _wheels)
        {
            WheelDef d = w.Def;
            Vector3 hub = new(d.Hub.X, d.Hub.Y, d.Hub.Z);
            Vector3 top = GlobalTransform * (hub + Vector3.Up * (d.Travel / 2));
            float reach = d.Travel + d.Radius;
            float distance = MathF.Min(groundBelow(top) ?? reach, reach);
            // The wheel's centre is its radius over the ground, within its travel either side of the hub.
            float offset = Math.Clamp(d.Travel / 2 - (distance - d.Radius), -d.Travel / 2, d.Travel / 2);
            w.Hub.Position = hub + Vector3.Up * offset;
            w.Steer.Rotation = new Vector3(0, d.Steers ? -steer : 0, 0);
            w.Angle = (w.Angle + along * (float)delta / d.Radius) % Mathf.Tau;
            w.Spin.Rotation = new Vector3(w.Angle, 0, 0);
        }
    }
}
