using System;
using System.Linq;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Electricity;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class PowerTests
{
    private static Deployable Node(ulong id, PowerStats power, float x = 0) => new(new EntityId(id), new DeployableDef
    {
        Key = "electrical-test", Item = new ItemDef { Id = (ushort)(id % ushort.MaxValue), Key = "electrical-test" },
        Health = 100, HalfExtents = new Vector3(.2f), Power = power,
    }, new Vector3(x, 0, 0), Vector3.UnitY, 0, new EntityId(500));
    private static PortRef Out(Deployable node) => new(node.Id.Value, node.Def.Power!.Output!.Id);
    private static PortRef In(Deployable node) => new(node.Id.Value, node.Def.Power!.Input!.Id);
    private static ElectricalGraph Graph(params Deployable[] nodes)
    {
        var graph = new ElectricalGraph();
        foreach (Deployable node in nodes) graph.Register(node);
        return graph;
    }
    private static void Wire(ElectricalGraph graph, Deployable from, Deployable to, params RouteAnchor[] anchors) =>
        Assert.True(graph.Connect(Out(from), In(to), anchors, out _).Ok, "explicit output-to-input connection succeeds");

    [Test]
    public static void PowerPortsUseEndpointSpanWithDecorativeAnchorsAndStableIdentities()
    {
        // At the north-pole up vector, yaw zero faces world -X; authored local +Z therefore points along world +X.
        // The fixture must use the same planetary upright frame as placed devices, not assume local X is world X.
        var source = Node(1, new PowerStats { Generation = 40, Ports = new[] { new PowerPort { Id = 37, Key = "supply", Direction = PowerPortDirection.Output, Position = new Vector3(0, 0, 2) } } });
        var target = Node(2, new PowerStats { Demand = 10 }, 22);
        var graph = Graph(source, target);
        RouteAnchor[] route = { new(TargetRef.Ground, new Vector3(100, 4, 0)), new(TargetRef.Ground, new Vector3(-100, 4, 0)) };
        Assert.True(graph.CanConnect(Out(source), In(target), route).Ok, "origins are 22m apart but actual ports are exactly20m; long cosmetic route allowed");
        target.Position += new Vector3(.01f, 0, 0);
        Assert.Equal(OutcomeCode.OutOfReach, graph.CanConnect(Out(source), In(target), route).Code, "20.01m endpoints rejected despite anchors");
        target.Position -= new Vector3(.02f, 0, 0);
        Wire(graph, source, target, route);
        Assert.Equal((ushort)37, target.PowerCable!.Output.PortId, "stable authored port identity persists independently of order");
        graph.Evaluate(0, 0);
        Assert.True(target.Powered, "route geometry does not consume or redirect power");
    }

    [Test]
    public static void PowerWiresRejectOccupancyCyclesAndInvalidDirectionsButAcceptUnderSupply()
    {
        var source = Node(1, new PowerStats { Generation = 5 });
        var first = Node(2, new PowerStats { Demand = 10 }, 5);
        var second = Node(3, new PowerStats { Demand = 25 }, 10);
        var graph = Graph(source, first, second);
        Wire(graph, source, first);
        Assert.Equal(OutcomeCode.NoSpace, graph.CanConnect(Out(source), In(second), Array.Empty<RouteAnchor>()).Code, "one cable per physical output");
        Wire(graph, first, second);
        Assert.Equal(OutcomeCode.Invalid, graph.CanConnect(In(first), In(second), Array.Empty<RouteAnchor>()).Code, "input-to-input rejected");
        Assert.Equal(OutcomeCode.Invalid, graph.CanConnect(Out(first), In(first), Array.Empty<RouteAnchor>()).Code, "self loop rejected");
        graph.Disconnect(In(first));
        Assert.Equal(OutcomeCode.Invalid, graph.CanConnect(Out(second), In(first), Array.Empty<RouteAnchor>()).Code, "two-node feedback rejected");
        Wire(graph, source, first);
        graph.Evaluate(0, 0);
        Assert.True(!first.Powered && !second.Powered && second.PowerInput == 5, "low supply is connectable; insufficient devices pass available input onward");
    }

    [Test]
    public static void PowerDirectedResidualSupplyPreservesPassthroughWhenDisabled()
    {
        var source = Node(1, new PowerStats { Generation = 30 });
        var first = Node(2, new PowerStats { Demand = 10 }, 5);
        var second = Node(3, new PowerStats { Demand = 25 }, 10);
        var graph = Graph(source, first, second); Wire(graph, source, first); Wire(graph, first, second);
        graph.Evaluate(0, 0);
        Assert.True(first.Powered && first.PowerConsumed == 10 && first.PowerOutput == 20 && !second.Powered, "upstream load consumes its own demand before forwarding residual");
        first.PowerEnabled = false; graph.Changed(first); graph.Evaluate(0, 0);
        Assert.True(!first.Powered && first.PowerConsumed == 0 && second.Powered && second.PowerOutput == 5, "disabled consumer forwards all30 while consuming zero");
        source.PowerEnabled = false; graph.Changed(source); graph.Evaluate(0, 0);
        Assert.True(!second.Powered && second.PowerInput == 0, "source loss applies immediately on change");
    }

    [Test]
    public static void PowerStorageConservesEnergyAndDoesNotBackfeedOrSpendUnusedBudget()
    {
        var source = Node(1, new PowerStats { Generation = 20 });
        var battery = Node(2, new PowerStats { Capacity = 50, ChargeRate = 10, DischargeRate = 10, Efficiency = .8f }, 5);
        var load = Node(3, new PowerStats { Demand = 10 }, 10);
        var graph = Graph(source, battery, load); Wire(graph, source, battery); Wire(graph, battery, load);
        graph.Evaluate(1, 0);
        Assert.True(load.Powered && Math.Abs(battery.StoredEnergy - 8) < .001f, "ten spare input over one second adds eight units at80percent efficiency");
        for (int i = 0; i < 30; i++) { graph.Changed(battery); graph.Evaluate(0, 0); }
        Assert.Near(8, battery.StoredEnergy, .001, "zero-time edits never integrate storage");
        graph.Evaluate(1, 1);
        source.PowerEnabled = false; graph.Changed(source); graph.Evaluate(1, 2);
        Assert.True(load.Powered && Math.Abs(battery.StoredEnergy - 6) < .001f, "ten accepted downstream units cost ten stored units");
        graph.Evaluate(1, 3);
        Assert.True(!load.Powered && Math.Abs(battery.StoredEnergy - 6) < .001f, "unusable six-unit output budget does not drain the cell");
        source.PowerEnabled = true; graph.Changed(source);
        for (int i = 0; i < 20; i++) graph.Evaluate(1, i + 4);
        Assert.Near(50, battery.StoredEnergy, .001, "storage capacity enforced");

        var upstream = Node(10, new PowerStats { Demand = 10 });
        var downstreamBattery = Node(11, new PowerStats { Capacity = 50, DischargeRate = 20 }, 5);
        downstreamBattery.StoredEnergy = 50;
        var directed = Graph(upstream, downstreamBattery); Wire(directed, upstream, downstreamBattery); directed.Evaluate(1, 0);
        Assert.True(!upstream.Powered && downstreamBattery.StoredEnergy == 50, "a downstream battery cannot backfeed its upstream consumer or drain without a load");
    }

    [Test]
    public static void PowerStorageSplitIntervalsConserveTheSameEnergy()
    {
        static (ElectricalGraph Graph, Deployable Cell) Make(ulong offset)
        {
            var source = Node(offset + 1, new PowerStats { Generation = 20 });
            var cell = Node(offset + 2, new PowerStats { Capacity = 100, ChargeRate = 10, DischargeRate = 10, Efficiency = .8f }, 5);
            var load = Node(offset + 3, new PowerStats { Demand = 10 }, 10);
            var graph = Graph(source, cell, load); Wire(graph, source, cell); Wire(graph, cell, load);
            return (graph, cell);
        }
        var whole = Make(0); var split = Make(10);
        whole.Graph.Evaluate(1, 1);
        split.Graph.Evaluate(.25f, .25); split.Graph.Evaluate(.75f, 1);
        Assert.Near(whole.Cell.StoredEnergy, split.Cell.StoredEnergy, .0001, "elapsed slices integrate accepted charge exactly once");
    }

    [Test]
    public static void PowerEndpointRemovalAndMovementDisconnectOnlyAffectedCircuits()
    {
        var source = Node(1, new PowerStats { Generation = 40 });
        var first = Node(2, new PowerStats { Demand = 10 }, 5);
        var second = Node(3, new PowerStats { Demand = 25 }, 10);
        var graph = Graph(source, first, second); Wire(graph, source, first); Wire(graph, first, second); graph.Evaluate(0, 0);
        graph.Unregister(source); graph.Evaluate(0, 0);
        Assert.True(first.PowerCable == null && !first.Powered && !second.Powered && second.PowerCable != null, "endpoint death removes its edge and invalidates all downstream budgets");
        first.Position = new Vector3(100, 0, 0); graph.Changed(first); graph.Evaluate(0, 0);
        Assert.True(second.PowerCable == null && graph.CableCount == 0, "changed endpoint over20m detaches its edge");
    }

    [Test]
    public static void PowerRoutesHaveBoundedPayloadAndRemoveMissingVisualSupportOnly()
    {
        var source = Node(1, new PowerStats { Generation = 20 }); var load = Node(2, new PowerStats { Demand = 10 }, 5);
        var graph = Graph(source, load);
        var support = new TargetRef(TargetFamily.Piece, 91);
        var route = Enumerable.Repeat(new RouteAnchor(support, Vector3.One), PowerPorts.MaximumAnchors).ToArray();
        Assert.True(graph.CanConnect(Out(source), In(load), route).Ok, "max anchor count accepted");
        Assert.Equal(OutcomeCode.Invalid, graph.CanConnect(Out(source), In(load), route.Append(route[0]).ToArray()).Code, "over-limit payload rejected");
        Assert.Equal(OutcomeCode.Invalid, graph.CanConnect(Out(source), In(load), new[] { new RouteAnchor(TargetRef.Ground, new Vector3(float.NaN)) }).Code, "nonfinite anchor rejected");
        Wire(graph, source, load, route); graph.Evaluate(0, 0);
        ulong cableId = load.PowerCable!.Id; graph.SupportGone(support); graph.Evaluate(0, 0);
        Assert.True(load.PowerCable.Id == cableId && load.PowerCable.Anchors.Length == 0 && load.Powered && graph.LastEvaluatedNodes == 0,
            "support removal edits cosmetic route without electrical topology work");
    }

    [Test]
    public static void PowerRestoreRejectsLegacyFanoutAndResolvesReverseRegistrationOrder()
    {
        var source = Node(1, new PowerStats { Generation = 40 });
        var first = Node(2, new PowerStats { Demand = 10 }, 5); var second = Node(3, new PowerStats { Demand = 25 }, 10);
        first.PowerSourceEntityId = second.PowerSourceEntityId = source.Id.Value;
        var graph = Graph(second, first, source); graph.RestoreConnections(); graph.Evaluate(0, 0);
        Assert.True(graph.CableCount == 0 && graph.RejectedRestoredConnections == 2 && first.PowerSourceEntityId == 0 && second.PowerSourceEntityId == 0,
            "legacy multi-child output needs repair; neither child wins by restore order");
        first.PowerCable = new CableRecord(71, Out(source), In(first), Array.Empty<RouteAnchor>());
        var restored = Graph(first, source); restored.RestoreConnections(); restored.Evaluate(0, 0);
        Assert.True(first.Powered && first.PowerCable?.Id == 71, "complete restore resolves source registered after consumer");
    }

    [Test]
    public static void PowerRestoreSanitisesInvalidLinksEnergyAndCycles()
    {
        var a = Node(1, new PowerStats { Capacity = 10, DischargeRate = 5 });
        var b = Node(2, new PowerStats { Demand = 2 }, 3);
        a.PowerCable = new CableRecord(10, Out(b), In(a), Array.Empty<RouteAnchor>());
        b.PowerCable = new CableRecord(11, Out(a), In(b), Array.Empty<RouteAnchor>());
        a.StoredEnergy = float.NaN;
        var graph = Graph(a, b); graph.RestoreConnections(); graph.Evaluate(1, 1234);
        Assert.True(graph.CableCount == 1 && graph.RejectedRestoredConnections == 1, "untrusted saved cycle is rejected before evaluation");
        Assert.True(a.StoredEnergy == 0 && !b.Powered, "invalid stored energy cannot power anything");
        float wind = PowerSystem.WindFactor(1, 1234);
        Assert.Equal(wind, PowerSystem.WindFactor(1, 1234), "same clock/identity inputs reproduce wind");
        Assert.True(wind >= .65f && wind <= 1 && Math.Abs(PowerSystem.WindFactor(1, 119.999) - PowerSystem.WindFactor(1, 120.001)) < .001f, "wind interpolation is bounded and continuous");
    }

    [Test]
    public static void PowerIdleAndLocalEditsDoNotScanUnrelatedCircuits()
    {
        var source = Node(1, new PowerStats { Generation = 40 }); var load = Node(2, new PowerStats { Demand = 10 }, 5);
        var graph = Graph(source, load); Wire(graph, source, load);
        for (ulong id = 10; id < 1010; id++) graph.Register(Node(id, new PowerStats { Generation = 10 }, (float)id * 30));
        graph.Evaluate(0, 0); graph.Evaluate(1, 1);
        Assert.True(graph.LastRebuiltNodes == 0 && graph.LastEvaluatedNodes == 0, "unchanged constant networks perform no scheduled solve");
        source.PowerEnabled = false; graph.Changed(source); graph.Evaluate(0, 1);
        Assert.True(graph.LastRebuiltNodes == 0 && graph.LastEvaluatedNodes == 2 && !load.Powered, "switch edit touches only the two-member component");
        graph.Disconnect(In(load)); graph.Evaluate(0, 1);
        Assert.True(graph.LastRebuiltNodes == 2 && graph.LastEvaluatedNodes == 2, "topology edit rebuilds just its former component");
    }
}
