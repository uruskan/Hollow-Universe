using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Electricity;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>Zone owner for the indexed electrical graph. Wiring input/visibility lives in its held-tool handler.</summary>
public sealed class PowerSystem : ZoneSystem
{
    private ElectricalGraph? _graph;
    public const int MaxAnchors = PowerPorts.MaximumAnchors;
    public ElectricalGraph Graph => _graph ??= new ElectricalGraph(() => NextId().Value);
    public override int Every => CharacterTicks.Rate;
    private ThingSystem Things => Zone.Require<ThingSystem>();
    protected internal override void Step()
    { Graph.RestoreConnections(); Graph.Evaluate(1, UnixMilliseconds / 1000.0); }
    public void Register(Deployable node) => Graph.Register(node);
    public void Unregister(Deployable node)
    { Graph.Unregister(node); Graph.Evaluate(0, UnixMilliseconds / 1000.0); }
    public void Changed(Deployable node)
    { Graph.Changed(node); Graph.Evaluate(0, UnixMilliseconds / 1000.0); }
    public void RestoreConnections()
    { Graph.RestoreConnections(); Graph.Evaluate(0, UnixMilliseconds / 1000.0); }
    public void SupportGone(TargetRef target) => Graph.SupportGone(target);
    public bool TryPort(PortRef reference, out Deployable node, out PowerPort port, out Vector3 position) => Graph.TryPort(reference, out node, out port, out position);

    /// <summary>Configuration rights are independent of target friendship.</summary>
    public Outcome CanEdit(EntityId player, Deployable node)
    {
        if (node.Def.Power == null || node.Health <= 0 || Things.StaticOf(node.Id) != node) return OutcomeCode.NotFound;
        Privilege privilege = Zone.Require<BuildingSystem>().World.PrivilegeAt(player.Value, node.Position);
        return privilege == Privilege.Blocked ? OutcomeCode.BuildingBlocked
            : node.Owner == player || privilege == Privilege.Allowed ? Outcome.Done : OutcomeCode.NotOwner;
    }
    /// <summary>Tombstones preserve append-only interaction IDs but cannot bypass the held Wire Tool.</summary>
    public Outcome SelectOutput(EntityId player, Deployable source) => OutcomeCode.NotUsable;
    public Outcome ConnectSelected(EntityId player, Deployable input) => OutcomeCode.NotUsable;

    /// <summary>Called by the authoritative wiring handler after actor/tool, hit, reach and anchor validation.</summary>
    public Outcome Connect(EntityId player, PortRef output, PortRef input, IReadOnlyList<RouteAnchor> anchors, ulong replacing = 0)
    {
        Graph.RestoreConnections();
        if (!TryPort(output, out Deployable source, out _, out _) || !TryPort(input, out Deployable target, out _, out _)) return OutcomeCode.NotFound;
        Outcome sourceAllowed = CanEdit(player, source), targetAllowed = CanEdit(player, target);
        if (!sourceAllowed.Ok) return sourceAllowed;
        if (!targetAllowed.Ok) return targetAllowed;
        Outcome outcome = Graph.Connect(output, input, anchors, out _, replacing);
        if (outcome.Ok) Graph.Evaluate(0, UnixMilliseconds / 1000.0);
        return outcome;
    }
    /// <summary>Engine-free setup helper. Gameplay messages only reach the Wire Tool handler.</summary>
    public Outcome Connect(EntityId player, Deployable source, Deployable input)
    {
        Register(source); Register(input);
        return source.Def.Power?.Output is { } output && input.Def.Power?.Input is { } port
            ? Connect(player, new PortRef(source.Id.Value, output.Id), new PortRef(input.Id.Value, port.Id), Array.Empty<RouteAnchor>()) : OutcomeCode.NotUsable;
    }
    public Outcome Disconnect(EntityId player, PortRef port)
    {
        if (!TryPort(port, out Deployable node, out _, out _)) return OutcomeCode.NotFound;
        Outcome allowed = CanEdit(player, node);
        if (!allowed.Ok) return allowed;
        CableRecord? cable = Graph.CableAt(port);
        if (cable == null) return OutcomeCode.NotFound;
        PortRef other = cable.Output == port ? cable.Input : cable.Output;
        if (TryPort(other, out Deployable endpoint, out _, out _) && !CanEdit(player, endpoint).Ok) return CanEdit(player, endpoint);
        Graph.Disconnect(port); Graph.Evaluate(0, UnixMilliseconds / 1000.0);
        return Outcome.Done;
    }
    public Outcome Disconnect(EntityId player, Deployable input) => input.Def.Power?.Input is { } port
        ? Disconnect(player, new PortRef(input.Id.Value, port.Id)) : OutcomeCode.NotUsable;
    public Outcome SetEnabled(EntityId player, Deployable node, bool enabled)
    {
        Outcome allowed = CanEdit(player, node);
        if (!allowed.Ok) return allowed;
        node.PowerEnabled = enabled; node.Revision++;
        Changed(node); return Outcome.Done;
    }
    public static Outcome CanConnect(Deployable source, Deployable input, IReadOnlyList<Deployable> nodes)
    {
        var graph = new ElectricalGraph();
        foreach (Deployable node in nodes) graph.Register(node);
        graph.RestoreConnections();
        return source.Def.Power?.Output is { } output && input.Def.Power?.Input is { } port
            ? graph.CanConnect(new PortRef(source.Id.Value, output.Id), new PortRef(input.Id.Value, port.Id), Array.Empty<RouteAnchor>()) : OutcomeCode.NotUsable;
    }
    /// <summary>Independent test/diagnostic solve. Runtime retains its graph; this adapter intentionally rebuilds.</summary>
    public static void Solve(IReadOnlyList<Deployable> nodes, float elapsedSeconds, double unixSeconds)
    {
        var graph = new ElectricalGraph();
        foreach (Deployable node in nodes) graph.Register(node);
        graph.RestoreConnections();
        graph.Evaluate(elapsedSeconds, unixSeconds);
    }
    /// <summary>Slow deterministic wind variation from the authoritative clock.</summary>
    public static float WindFactor(ulong entityId, double unixSeconds)
    {
        double phase = Math.Max(0, unixSeconds) / 120;
        ulong bucket = (ulong)phase;
        float t = (float)(phase - bucket); t = t * t * (3 - 2 * t);
        float a = (Hash64.Of(entityId, bucket, 0x57494E44) >> 40) / 16777215f;
        float b = (Hash64.Of(entityId, bucket + 1, 0x57494E44) >> 40) / 16777215f;
        return 0.65f + .35f * (a + (b - a) * t);
    }
}
public static class PowerSystemAccess
{
    public static PowerSystem Power(this Zone zone) => zone.Require<PowerSystem>();
}
