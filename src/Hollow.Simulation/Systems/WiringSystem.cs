using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Content;
using Hollow.Simulation.Electricity;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

public enum WireOperation : byte { Begin, Anchor, Complete, Cancel, Disconnect }

/// <summary>Short-lived authoritative tool transactions. Decorative anchors never become circuit nodes.</summary>
public sealed class WiringSystem : ZoneSystem
{
    private sealed class Draft(PortRef output, long until, ushort tool)
    {
        public readonly PortRef Output = output;
        public readonly long Until = until;
        public readonly ushort Tool = tool;
        public readonly List<RouteAnchor> Anchors = new();
    }
    private readonly Dictionary<ulong, Draft> _drafts = new();
    private readonly List<ulong> _expired = new();
    public int DraftCount => _drafts.Count;
    public void Cancel(EntityId player) => _drafts.Remove(player.Value);

    public Outcome Execute(Entity entity, WireOperation operation, TargetRef target, ushort portId, Vector3 point, uint revision = 0)
    {
        Player? player = Zone.Players().PlayerOf(entity);
        if (player == null) return OutcomeCode.NotFound;
        if (operation == WireOperation.Cancel) { Cancel(player.Id); return Outcome.Done; }
        if (!Enum.IsDefined(operation)) return OutcomeCode.Invalid;
        if (!player.CanAct || Zone.Ships().SeatOf(entity) != null) { Cancel(player.Id); return OutcomeCode.NotAllowed; }
        if (player.Held(Content) is not { Held: HeldUse.Wiring, Wiring: { } tool } held)
        { Cancel(player.Id); return OutcomeCode.NoTool; }
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) return OutcomeCode.Invalid;
        Vector3 eye = EyeOf(Entities.Get<Character>(entity).State);
        if (operation == WireOperation.Anchor)
            return AddAnchor(player, eye, tool, held.Id, target, point);
        if (!Zone.Power().TryPort(new PortRef(target.Id, portId), out Deployable node, out PowerPort port, out Vector3 at) ||
            target != node.Ref) return OutcomeCode.NotFound;
        if (revision != 0 && revision != node.Revision) return OutcomeCode.Stale;
        Vector3 local = Vector3.Transform(point - at, Quaternion.Inverse(Combat.Geometry.Upright(node.Up, node.Yaw)));
        float half = port.Size * .5f + .005f;
        if (Math.Abs(local.X) > half || Math.Abs(local.Y) > half || Math.Abs(local.Z) > half) return OutcomeCode.Invalid;
        if (Vector3.DistanceSquared(eye, at) > tool.Reach * tool.Reach) return OutcomeCode.OutOfReach;
        if (!PortInSight(player.Id, eye, at, target)) return OutcomeCode.OutOfSight;
        Outcome allowed = Zone.Power().CanEdit(player.Id, node);
        if (!allowed.Ok) return allowed;
        var reference = new PortRef(node.Id.Value, portId);
        if (operation == WireOperation.Disconnect) return Zone.Power().Disconnect(player.Id, reference);
        if (operation == WireOperation.Begin)
        {
            if (port.Direction != PowerPortDirection.Output) return OutcomeCode.NotUsable;
            if (Zone.Power().Graph.CableAt(reference) != null) return OutcomeCode.NoSpace;
            _drafts[player.Id.Value] = new Draft(reference, Tick + tool.DraftSeconds * CharacterTicks.Rate, held.Id);
            return Outcome.Done;
        }
        if (!DraftFor(player.Id, held.Id, out Draft draft)) return OutcomeCode.Stale;
        if (port.Direction != PowerPortDirection.Input) return OutcomeCode.NotUsable;
        foreach (RouteAnchor anchor in draft.Anchors)
        {
            if (!Zone.Targets().TryPose(anchor.Support, out TargetPose pose)) return OutcomeCode.Stale;
            Outcome support = CanAttach(player.Id, anchor.Support, pose.ToZone(anchor.Position));
            if (!support.Ok) return support;
        }
        Outcome connected = Zone.Power().Connect(player.Id, draft.Output, reference, draft.Anchors);
        if (connected.Ok) Cancel(player.Id);
        return connected;
    }

    private bool PortInSight(EntityId player, Vector3 eye, Vector3 at, TargetRef target)
    {
        if (!Zone.Targets().InSight(eye, at, target)) return false;
        Vector3 delta = at - eye;
        float distance = delta.Length();
        if (distance <= .001f) return true;
        // The common interaction sight check covers buildings, ships and terrain. Port picking must also respect
        // intervening deployables/natural objects; use the same registered target geometry used for anchor picking.
        // AttackRadius=0 excludes an open doorway's interaction-only surface from the obstruction query.
        AimHit hit = Targeting.Aim(Zone.Targets().TargetViews,
            new AimRay(eye, delta / distance, distance, player.Value) { AttackRadius = 0 });
        return !hit.Hit || hit.Target.Whole == target.Whole;
    }

    private bool DraftFor(EntityId id, ushort tool, out Draft draft)
    {
        if (_drafts.TryGetValue(id.Value, out draft!) && draft.Until >= Tick && draft.Tool == tool) return true;
        Cancel(id);
        return false;
    }

    private Outcome AddAnchor(Player player, Vector3 eye, WiringStats tool, ushort held, TargetRef support, Vector3 point)
    {
        if (!DraftFor(player.Id, held, out Draft draft)) return OutcomeCode.Stale;
        if (draft.Anchors.Count >= tool.MaxAnchors) return OutcomeCode.NoSpace;
        float distance = Vector3.Distance(eye, point);
        if (distance > tool.Reach || distance < .01f) return OutcomeCode.OutOfReach;
        AimHit hit = Targeting.Aim(Zone.Targets().TargetViews,
            new AimRay(eye, Vector3.Normalize(point - eye), distance + .2f, player.Id.Value));
        if (!hit.Hit || hit.Target.Whole != support.Whole || Vector3.DistanceSquared(hit.Point, point) > .04f)
            return OutcomeCode.OutOfSight;
        Outcome allowed = CanAttach(player.Id, hit.Target, hit.Point);
        if (!allowed.Ok) return allowed;
        if (!Zone.Targets().TryPose(hit.Target, out TargetPose pose)) return OutcomeCode.NotFound;
        draft.Anchors.Add(new RouteAnchor(hit.Target, pose.ToLocal(hit.Point)));
        return Outcome.Done;
    }

    private Outcome CanAttach(EntityId player, TargetRef target, Vector3 point)
    {
        if (Zone.Targets().FamilyOf(target.Family) is not { Moves: false } family || !family.Exists(target.Id))
            return OutcomeCode.NotUsable;
        if (family.Resolve(target.Id) is WorldObject { Rides: true }) return OutcomeCode.NotUsable;
        Privilege privilege = Zone.Building().World?.PrivilegeAt(player.Value, point) ?? Privilege.None;
        if (privilege == Privilege.Blocked) return OutcomeCode.BuildingBlocked;
        if (family.Resolve(target.Id) is IOwned owner && owner.OwnerId != player.Value && privilege != Privilege.Allowed)
            return OutcomeCode.NotOwner;
        return Outcome.Done;
    }

    protected internal override void Step()
    {
        if (_drafts.Count == 0) return;
        _expired.Clear();
        foreach ((ulong id, Draft draft) in _drafts)
            if (draft.Until < Tick || !Zone.Players().TryFindPlayer(new EntityId(id), out Entity entity, out Player p) ||
                !p.CanAct || Zone.Ships().SeatOf(entity) != null || p.Held(Content)?.Id != draft.Tool) _expired.Add(id);
        foreach (ulong id in _expired) _drafts.Remove(id);
    }
}

public static class WiringSystemAccess
{
    public static WiringSystem Wiring(this Zone zone) => zone.Require<WiringSystem>();
}
