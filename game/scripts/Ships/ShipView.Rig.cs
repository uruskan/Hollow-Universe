using System;
using System.Collections.Generic;
using Godot;
using Hollow.Simulation.Ships;

namespace HollowUniverse.Ships;

/// <summary>
/// A vehicle's own wheels and suspension drawn from its model (VEHICLES.md §7: the arms and dampers seen and moving, the
/// steering turning the hubs, the tyres rolling; art/models/togg.py and zamazingo.py build the nodes). For each wheel of
/// the data, by its key:
/// <list type="bullet">
/// <item><c>Wheel_&lt;key&gt;</c> at the hub, riding its spring; its <c>Steer</c> turns with the steering (what does not roll:
/// a knuckle, a caliper) and its <c>Spin</c> rolls (the tyre, the rim, the disc).</item>
/// <item><c>Fork_&lt;key&gt;</c> (optional): steering about a raked axis, its own up along the axis; the wheel under it slides up
/// that axis (a motorbike's fork), and its own <c>Steer</c> stays still.</item>
/// <item><c>Swing_&lt;key&gt;</c> (optional): an arm carrying the wheel about its pivot (a swingarm): the hub swings with it.</item>
/// <item><c>Arm_&lt;key&gt;_&lt;n&gt;</c>: arms turned about their pivots so that their <c>Tip</c> follows the hub (wishbones, a
/// half-shaft).</item>
/// <item><c>Shock_&lt;key&gt;</c>: a damper from its top mount pointing at <c>ShockLow_&lt;key&gt;</c> (on an arm), its <c>Rod</c>
/// sliding down its axis and its <c>Spring</c> squeezed between the top seat and the rod's <c>SeatLow</c>.</item>
/// </list>
/// Presentation only; a model without these nodes draws no wheels of its own (the blocks do, ShipView.Blocks.cs).
/// </summary>
public partial class ShipView
{
    private readonly List<RigWheel> _rig = new();
    private readonly List<RigShock> _shocks = new();

    private sealed class RigArm
    {
        public required Node3D Node;
        public required Transform3D Rest;      // in the model
        public required Transform3D ParentRest;
        public required Vector3 Pivot;         // in the model
        public required Vector3 Reach;         // the tip (or the carried hub) from the pivot, at rest
        public required Vector3 Axis;          // turned about, in the model; positive lifts the tip
    }

    private sealed class RigWheel
    {
        public required WheelDef Def;
        public required Node3D Hub;
        public required Vector3 HubRest;       // in its parent
        public required Vector3 Slide;         // the hub's way up, in its parent, per metre lifted
        public Node3D? Steer;
        public Basis SteerRest;
        public Node3D? Spin;
        public Basis SpinRest;
        public Node3D? Fork;
        public Basis ForkRest;
        public RigArm? Carrier;
        public readonly List<RigArm> Arms = new();
        public float Angle;
    }

    private sealed class RigShock
    {
        public required Node3D Node;
        public required Transform3D Rest;
        public required Transform3D ParentRest;
        public required Node3D Target;
        public Node3D? Rod;
        public Node3D? Spring;
        public float RestLength;
        public float SpringFree;               // the part of its length the spring does not span
        public float SpringRest;
    }

    /// <summary>Finds the model's rig for each wheel of the data. True when it has one.</summary>
    private bool FindRig()
    {
        foreach (WheelDef w in _def.Wheels)
        {
            if (_model.FindChild($"Wheel_{w.Key}", true, false) is not Node3D hub)
            {
                continue;
            }
            var wheel = new RigWheel { Def = w, Hub = hub, HubRest = hub.Position, Slide = Vector3.Up };
            wheel.Steer = hub.GetNodeOrNull<Node3D>("Steer");
            wheel.Spin = wheel.Steer?.GetNodeOrNull<Node3D>("Spin") ?? hub.GetNodeOrNull<Node3D>("Spin");
            wheel.SteerRest = wheel.Steer?.Basis ?? Basis.Identity;
            wheel.SpinRest = wheel.Spin?.Basis ?? Basis.Identity;
            if (_model.FindChild($"Fork_{w.Key}", true, false) is Node3D fork)
            {
                wheel.Fork = fork;
                wheel.ForkRest = fork.Basis;
                // Up the fork, as much of it as lifts the hub a metre (its axis leans back).
                Vector3 axisInModel = InModel(fork).Basis.Y.Normalized();
                wheel.Slide = Vector3.Up / MathF.Max(axisInModel.Y, 0.3f);
            }
            Vector3 hubInModel = InModel(hub).Origin;
            if (_model.FindChild($"Swing_{w.Key}", true, false) is Node3D swing)
            {
                wheel.Carrier = Arm(swing, hubInModel);
            }
            foreach (Node found in _model.FindChildren($"Arm_{w.Key}_*", "", true, false))
            {
                if (found is Node3D arm && arm.GetNodeOrNull<Node3D>("Tip") is { } tip)
                {
                    wheel.Arms.Add(Arm(arm, InModel(tip).Origin));
                }
            }
            if (_model.FindChild($"Shock_{w.Key}", true, false) is Node3D shock
                && _model.FindChild($"ShockLow_{w.Key}", true, false) is Node3D target)
            {
                _shocks.Add(Shock(shock, target));
            }
            _rig.Add(wheel);
        }
        return _rig.Count > 0;
    }

    private RigArm Arm(Node3D node, Vector3 tipInModel)
    {
        Transform3D rest = InModel(node);
        Vector3 pivot = rest.Origin, reach = tipInModel - pivot;
        // Turned about the horizontal square to its reach; the way that lifts the tip.
        Vector3 flat = new Vector3(reach.X, 0, reach.Z);
        Vector3 axis = flat.LengthSquared() > 1e-8f ? flat.Cross(Vector3.Up).Normalized() : Vector3.Right;
        return new RigArm
        {
            Node = node, Rest = rest, ParentRest = node.GetParent() is Node3D p && p != _model ? InModel(p) : Transform3D.Identity,
            Pivot = pivot, Reach = reach, Axis = axis,
        };
    }

    private RigShock Shock(Node3D node, Node3D target)
    {
        Transform3D rest = InModel(node);
        var shock = new RigShock
        {
            Node = node, Rest = rest, ParentRest = node.GetParent() is Node3D p && p != _model ? InModel(p) : Transform3D.Identity,
            Target = target, Rod = node.GetNodeOrNull<Node3D>("Rod"), Spring = node.GetNodeOrNull<Node3D>("Spring"),
            RestLength = (InModel(target).Origin - rest.Origin).Length(),
        };
        if (shock.Spring != null && shock.Rod?.GetNodeOrNull<Node3D>("SeatLow") is { } seat)
        {
            shock.SpringFree = -shock.Spring.Position.Y + seat.Position.Y;
            shock.SpringRest = shock.RestLength - shock.SpringFree;
        }
        return shock;
    }

    /// <summary>The angle about an arm's axis that lifts its tip by <paramref name="lift"/> (metres, up in the model).</summary>
    private static float ArmAngle(RigArm arm, float lift)
    {
        // The tip's height as it turns: A cos θ + B sin θ, A its height and B how fast it rises at rest.
        Vector3 r = arm.Reach - arm.Axis * arm.Axis.Dot(arm.Reach);
        float a = r.Y, b = arm.Axis.Cross(r).Y;
        float m = MathF.Sqrt(a * a + b * b);
        if (m < 1e-5f)
        {
            return 0;
        }
        float want = Math.Clamp((a + lift) / m, -1, 1);
        return MathF.Asin(want) - MathF.Atan2(a, b);
    }

    private static Transform3D Turned(RigArm arm, float angle) =>
        new Transform3D(new Basis(arm.Axis, angle), arm.Pivot) * new Transform3D(Basis.Identity, -arm.Pivot) * arm.Rest;

    /// <summary>Poses the rig: each wheel on its spring, the steered ones turned, all rolled, then the arms and dampers.</summary>
    private void ShowRig(Func<Vector3, float?> groundBelow, float steer, float along, double delta)
    {
        foreach (RigWheel w in _rig)
        {
            WheelDef d = w.Def;
            Vector3 hub = new(d.Hub.X, d.Hub.Y, d.Hub.Z);
            Vector3 top = GlobalTransform * (hub + Vector3.Up * (d.Travel / 2));
            float reach = d.Travel + d.Radius;
            float distance = MathF.Min(groundBelow(top) ?? reach, reach);
            float lift = Math.Clamp(d.Travel / 2 - (distance - d.Radius), -d.Travel / 2, d.Travel / 2);
            float turn = d.Steers ? -steer : 0;
            if (w.Carrier is { } carrier)
            {
                float angle = ArmAngle(carrier, lift);
                Transform3D posed = Turned(carrier, angle);
                carrier.Node.Transform = carrier.ParentRest.AffineInverse() * posed;
                Vector3 hubInModel = carrier.Pivot + new Basis(carrier.Axis, angle) * carrier.Reach;
                w.Hub.Position = (w.Hub.GetParent() is Node3D p && p != _model ? InModel(p).AffineInverse() : Transform3D.Identity) * hubInModel;
            }
            else
            {
                w.Hub.Position = w.HubRest + w.Slide * lift;
            }
            if (w.Fork != null)
            {
                w.Fork.Basis = w.ForkRest * new Basis(Vector3.Up, turn);
            }
            else if (w.Steer != null)
            {
                w.Steer.Basis = w.SteerRest * new Basis(Vector3.Up, turn);
            }
            w.Angle = (w.Angle + along * (float)delta / d.Radius) % Mathf.Tau;
            if (w.Spin != null)
            {
                w.Spin.Basis = w.SpinRest * new Basis(Vector3.Right, w.Angle);
            }
            foreach (RigArm arm in w.Arms)
            {
                arm.Node.Transform = arm.ParentRest.AffineInverse() * Turned(arm, ArmAngle(arm, lift));
            }
        }
        foreach (RigShock s in _shocks)
        {
            Vector3 low = InModel(s.Target).Origin;
            Vector3 d = low - s.Rest.Origin;
            float length = d.Length();
            if (length < 1e-4f)
            {
                continue;
            }
            Vector3 from = -s.Rest.Basis.Y.Normalized();
            Basis turned = new Basis(new Quaternion(from, d / length)) * s.Rest.Basis;
            s.Node.Transform = s.ParentRest.AffineInverse() * new Transform3D(turned, s.Rest.Origin);
            if (s.Rod != null)
            {
                s.Rod.Position = new Vector3(0, -length, 0);
            }
            if (s.Spring != null && s.SpringRest > 1e-3f)
            {
                s.Spring.Scale = new Vector3(1, Math.Max(0.2f, (length - s.SpringFree) / s.SpringRest), 1);
            }
        }
    }
}
