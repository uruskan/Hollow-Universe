using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Simulation.Content;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation.Electricity;

/// <summary>
/// Indexed, directed port graph. Membership/topology edits rebuild only touched components; unchanged constant
/// circuits do no periodic work. Only timed sources/storage are scheduled. All mutation belongs to the zone thread.
/// </summary>
public sealed class ElectricalGraph
{
    private sealed class Entry(Deployable node)
    {
        public readonly Deployable Node = node;
        public CableRecord? Incoming;
        public readonly List<CableRecord> Outgoing = new();
        public Circuit? Circuit;
        public ElectricalFlow Flow;
        public bool Enabled => Node.Health > 0 && Node.PowerEnabled;
    }
    private sealed class Circuit
    {
        public readonly List<Entry> Order = new();
    }
    private readonly Dictionary<ulong, Entry> _nodes = new();
    private readonly Dictionary<ulong, CableRecord> _cables = new();
    private readonly Dictionary<PortRef, CableRecord> _ports = new();
    private readonly Dictionary<TargetRef, HashSet<ulong>> _supports = new();
    private readonly HashSet<Entry> _topology = new();
    private readonly HashSet<Entry> _restore = new();
    private readonly HashSet<Circuit> _timed = new(), _dirty = new();
    private readonly List<Circuit> _evaluate = new();
    private readonly Func<ulong> _nextId;
    private ulong _localId;
    public ElectricalGraph(Func<ulong>? nextId = null) => _nextId = nextId ?? (() => ++_localId);
    public int NodeCount => _nodes.Count;
    public int CableCount => _cables.Count;
    public int LastEvaluatedNodes { get; private set; }
    public int LastRebuiltNodes { get; private set; }
    public int RejectedRestoredConnections { get; private set; }
    public int MigratedConnections { get; private set; }
    public IReadOnlyDictionary<ulong, CableRecord> Cables => _cables;

    public void Register(Deployable node)
    {
        if (node.Def.Power == null || _nodes.ContainsKey(node.Id.Value)) return;
        var entry = new Entry(node);
        _nodes.Add(node.Id.Value, entry);
        _topology.Add(entry);
        if (node.PowerCable != null || node.PowerSourceEntityId != 0) _restore.Add(entry);
    }
    public void Unregister(Deployable node)
    {
        if (!_nodes.TryGetValue(node.Id.Value, out Entry? entry)) return;
        Invalidate(entry);
        if (entry.Incoming is { } incoming) Remove(incoming);
        while (entry.Outgoing.Count > 0) Remove(entry.Outgoing[^1]);
        _topology.Remove(entry); _restore.Remove(entry); _nodes.Remove(node.Id.Value);
        node.Powered = false; node.PowerInput = node.PowerOutput = node.PowerConsumed = 0;
    }
    public bool TryPort(PortRef reference, out Deployable node, out PowerPort port, out Vector3 position)
    {
        node = null!; port = null!; position = default;
        if (!_nodes.TryGetValue(reference.EntityId, out Entry? entry) || entry.Node.Def.Power?.Port(reference.PortId) is not { } found) return false;
        node = entry.Node; port = found; position = PowerPorts.Position(node, port);
        return node.Health > 0 && PowerPorts.Finite(position);
    }
    public CableRecord? CableAt(PortRef port) => _ports.GetValueOrDefault(port);

    public Outcome CanConnect(PortRef output, PortRef input, IReadOnlyList<RouteAnchor> anchors, ulong replacing = 0)
    {
        if (!TryPort(output, out Deployable source, out PowerPort from, out Vector3 a) ||
            !TryPort(input, out Deployable target, out PowerPort to, out Vector3 b)) return OutcomeCode.NotFound;
        if (source == target || from.Direction != PowerPortDirection.Output || to.Direction != PowerPortDirection.Input || !PowerPorts.ValidAnchors(anchors)) return OutcomeCode.Invalid;
        if (source.Rides || target.Rides) return OutcomeCode.Invalid;
        float maximum = PowerPorts.MaximumSpan + PowerPorts.SpanTolerance;
        if (Vector3.DistanceSquared(a, b) > maximum * maximum) return OutcomeCode.OutOfReach;
        if ((_ports.TryGetValue(output, out CableRecord? occupiedOutput) && occupiedOutput.Id != replacing) ||
            (_ports.TryGetValue(input, out CableRecord? occupiedInput) && occupiedInput.Id != replacing)) return OutcomeCode.NoSpace;
        // Iterative bounded graph reachability, only on edits. No per-second cycle walk.
        var seen = new HashSet<ulong>();
        var pending = new Queue<ulong>(); pending.Enqueue(input.EntityId);
        while (pending.TryDequeue(out ulong id))
        {
            if (id == output.EntityId) return OutcomeCode.Invalid;
            if (!seen.Add(id)) continue;
            foreach (CableRecord cable in _nodes[id].Outgoing)
                if (cable.Id != replacing) pending.Enqueue(cable.Input.EntityId);
        }
        return Outcome.Done;
    }
    public Outcome Connect(PortRef output, PortRef input, IReadOnlyList<RouteAnchor> anchors, out CableRecord? connected, ulong replacing = 0)
    {
        connected = null;
        Outcome outcome = CanConnect(output, input, anchors, replacing);
        if (!outcome.Ok) return outcome;
        if (replacing != 0 && (!_cables.TryGetValue(replacing, out CableRecord? old) || old.Input != input)) return OutcomeCode.Stale;
        RouteAnchor[] copy = anchors.ToArray();
        ulong id;
        do { id = _nextId(); } while (id == 0 || _cables.ContainsKey(id));
        var record = new CableRecord(id, output, input, copy);
        if (replacing != 0) Remove(_cables[replacing]);
        Add(record); connected = record;
        return Outcome.Done;
    }
    public bool Disconnect(PortRef port)
    {
        if (!_ports.TryGetValue(port, out CableRecord? cable)) return false;
        Remove(cable); return true;
    }
    private void Add(CableRecord cable)
    {
        Entry source = _nodes[cable.Output.EntityId], target = _nodes[cable.Input.EntityId];
        Invalidate(source); Invalidate(target);
        _cables.Add(cable.Id, cable); _ports.Add(cable.Output, cable); _ports.Add(cable.Input, cable);
        source.Outgoing.Add(cable); target.Incoming = cable;
        target.Node.PowerCable = cable; target.Node.PowerSourceEntityId = 0; target.Node.Revision++;
        foreach (RouteAnchor anchor in cable.Anchors)
        {
            TargetRef support = anchor.Support.Whole;
            if (!_supports.TryGetValue(support, out HashSet<ulong>? cables)) _supports.Add(support, cables = new());
            cables.Add(cable.Id);
        }
    }
    private void Remove(CableRecord cable)
    {
        Entry source = _nodes[cable.Output.EntityId], target = _nodes[cable.Input.EntityId];
        Invalidate(source); Invalidate(target);
        _cables.Remove(cable.Id); _ports.Remove(cable.Output); _ports.Remove(cable.Input);
        source.Outgoing.Remove(cable); target.Incoming = null;
        target.Node.PowerCable = null; target.Node.PowerSourceEntityId = 0; target.Node.Revision++;
        foreach (RouteAnchor anchor in cable.Anchors)
            if (_supports.TryGetValue(anchor.Support.Whole, out HashSet<ulong>? cables))
            { cables.Remove(cable.Id); if (cables.Count == 0) _supports.Remove(anchor.Support.Whole); }
    }
    public void SupportGone(TargetRef support)
    {
        if (!_supports.TryGetValue(support.Whole, out HashSet<ulong>? ids)) return;
        foreach (ulong id in ids.ToArray())
        {
            CableRecord old = _cables[id];
            var replacement = old with { Anchors = old.Anchors.Where(a => a.Support.Whole != support.Whole).ToArray(), Revision = old.Revision + 1 };
            _cables[id] = replacement; _ports[old.Output] = replacement; _ports[old.Input] = replacement;
            Entry source = _nodes[old.Output.EntityId], target = _nodes[old.Input.EntityId];
            source.Outgoing[source.Outgoing.IndexOf(old)] = replacement; target.Incoming = replacement;
            target.Node.PowerCable = replacement; target.Node.Revision++;
        }
        _supports.Remove(support.Whole);
    }
    public void Changed(Deployable node)
    {
        if (!_nodes.TryGetValue(node.Id.Value, out Entry? entry)) return;
        if (entry.Incoming is { } incoming && !ValidSpan(incoming)) Remove(incoming);
        for (int i = entry.Outgoing.Count - 1; i >= 0; i--)
            if (!ValidSpan(entry.Outgoing[i])) Remove(entry.Outgoing[i]);
        if (entry.Circuit != null) _dirty.Add(entry.Circuit);
    }
    private bool ValidSpan(CableRecord cable) => TryPort(cable.Output, out _, out _, out Vector3 a) && TryPort(cable.Input, out _, out _, out Vector3 b) &&
        Vector3.DistanceSquared(a, b) <= (PowerPorts.MaximumSpan + PowerPorts.SpanTolerance) * (PowerPorts.MaximumSpan + PowerPorts.SpanTolerance);

    /// <summary>Call only after all restore records are registered. Unknown references and old fan-out are disconnected explicitly.</summary>
    public void RestoreConnections()
    {
        if (_restore.Count == 0) return;
        Entry[] pending = _restore.OrderBy(e => e.Node.Id.Value).ToArray(); _restore.Clear();
        var candidates = new List<(Entry Entry, CableRecord Cable, bool Legacy)>();
        foreach (Entry entry in pending)
        {
            Deployable node = entry.Node;
            CableRecord? cable = node.PowerCable;
            bool legacy = cable == null && node.PowerSourceEntityId != 0;
            if (legacy && _nodes.TryGetValue(node.PowerSourceEntityId, out Entry? source) && source.Node.Def.Power!.Output is { } output && node.Def.Power!.Input is { } input)
            {
                ulong id; do { id = _nextId(); } while (id == 0 || _cables.ContainsKey(id));
                cable = new CableRecord(id, new PortRef(source.Node.Id.Value, output.Id), new PortRef(node.Id.Value, input.Id), Array.Empty<RouteAnchor>());
            }
            node.PowerCable = null; node.PowerSourceEntityId = 0;
            if (cable == null) { RejectedRestoredConnections++; node.Revision++; continue; }
            candidates.Add((entry, cable, legacy));
        }
        // Every conflicting restored edge loses, including legacy fan-out. Never choose an arbitrary child.
        var outputCounts = candidates.GroupBy(c => c.Cable.Output).ToDictionary(g => g.Key, g => g.Count());
        var inputCounts = candidates.GroupBy(c => c.Cable.Input).ToDictionary(g => g.Key, g => g.Count());
        var ids = candidates.GroupBy(c => c.Cable.Id).ToDictionary(g => g.Key, g => g.Count());
        foreach (var candidate in candidates)
        {
            CableRecord cable = candidate.Cable;
            if (cable.Id == 0 || cable.Input.EntityId != candidate.Entry.Node.Id.Value || outputCounts[cable.Output] > 1 || inputCounts[cable.Input] > 1 || ids[cable.Id] > 1 ||
                _cables.ContainsKey(cable.Id) || !CanConnect(cable.Output, cable.Input, cable.Anchors).Ok)
            { RejectedRestoredConnections++; candidate.Entry.Node.Revision++; continue; }
            Add(cable);
            if (candidate.Legacy) MigratedConnections++;
        }
    }
    private void Invalidate(Entry entry)
    {
        if (entry.Circuit is not { } old) { _topology.Add(entry); return; }
        _timed.Remove(old); _dirty.Remove(old);
        foreach (Entry member in old.Order) { member.Circuit = null; _topology.Add(member); }
    }
    private void Rebuild()
    {
        LastRebuiltNodes = 0;
        while (_topology.Count > 0)
        {
            Entry start = _topology.First();
            var members = new List<Entry>();
            var visited = new HashSet<Entry>();
            var pending = new Queue<Entry>(); pending.Enqueue(start);
            while (pending.TryDequeue(out Entry? member))
            {
                if (!visited.Add(member)) continue;
                _topology.Remove(member); members.Add(member);
                if (member.Incoming is { } incoming) pending.Enqueue(_nodes[incoming.Output.EntityId]);
                foreach (CableRecord cable in member.Outgoing) pending.Enqueue(_nodes[cable.Input.EntityId]);
            }
            var circuit = new Circuit();
            foreach (Entry root in members.Where(m => m.Incoming == null).OrderBy(m => m.Node.Id.Value)) pending.Enqueue(root);
            while (pending.TryDequeue(out Entry? member))
            {
                circuit.Order.Add(member); member.Circuit = circuit;
                foreach (CableRecord cable in member.Outgoing) pending.Enqueue(_nodes[cable.Input.EntityId]);
            }
            LastRebuiltNodes += members.Count;
            _dirty.Add(circuit);
            if (members.Any(m => m.Node.Def.Power!.Wind || m.Node.Def.Power.Capacity > 0)) _timed.Add(circuit);
        }
    }
    public void Evaluate(float elapsedSeconds, double unixSeconds)
    {
        Rebuild(); LastEvaluatedNodes = 0;
        float seconds = float.IsFinite(elapsedSeconds) ? Math.Max(0, elapsedSeconds) : 0;
        _evaluate.Clear();
        foreach (Circuit circuit in _dirty) _evaluate.Add(circuit);
        if (seconds > 0) foreach (Circuit circuit in _timed) if (!_dirty.Contains(circuit)) _evaluate.Add(circuit);
        _dirty.Clear();
        foreach (Circuit circuit in _evaluate) Evaluate(circuit, seconds, unixSeconds);
    }
    private void Evaluate(Circuit circuit, float seconds, double unixSeconds)
    {
        float horizon = seconds > 0 ? seconds : 1;
        LastEvaluatedNodes += circuit.Order.Count;
        // Cached reverse demand, then forward delivery, then actual accepted-flow accounting.
        for (int i = circuit.Order.Count - 1; i >= 0; i--)
        {
            Entry entry = circuit.Order[i]; PowerStats rating = entry.Node.Def.Power!;
            entry.Flow = new ElectricalFlow { Energy = float.IsFinite(entry.Node.StoredEnergy) ? Math.Clamp(entry.Node.StoredEnergy, 0, rating.Capacity) : 0 };
            foreach (CableRecord cable in entry.Outgoing)
            {
                Entry child = _nodes[cable.Input.EntityId];
                entry.Flow.DownstreamDemand += child.Flow.DownstreamDemand + (child.Enabled ? child.Node.Def.Power!.Demand : 0);
            }
        }
        foreach (Entry entry in circuit.Order)
        {
            PowerStats rating = entry.Node.Def.Power!;
            entry.Flow.Input = entry.Incoming is { } cable ? _nodes[cable.Output.EntityId].Flow.Output : 0;
            float generation = rating.Generation * (rating.Wind ? PowerSystem.WindFactor(entry.Node.Id.Value, unixSeconds) : 1);
            PowerTransferRules.Of(rating.Transfer).Deliver(rating, entry.Enabled, generation, horizon, ref entry.Flow);
        }
        for (int i = circuit.Order.Count - 1; i >= 0; i--)
        {
            Entry entry = circuit.Order[i]; PowerStats rating = entry.Node.Def.Power!;
            foreach (CableRecord cable in entry.Outgoing) entry.Flow.UsedDownstream += _nodes[cable.Input.EntityId].Flow.Draw;
            PowerTransferRules.Of(rating.Transfer).Settle(rating, entry.Enabled, seconds, horizon, ref entry.Flow);
            Deployable node = entry.Node;
            bool changed = node.Powered != entry.Flow.Powered || Math.Abs(node.PowerInput - entry.Flow.Input) > .0001f ||
                Math.Abs(node.PowerOutput - entry.Flow.Output) > .0001f || Math.Abs(node.PowerConsumed - entry.Flow.Consumed) > .0001f || Math.Abs(node.StoredEnergy - entry.Flow.Energy) > .0001f || !float.IsFinite(node.StoredEnergy);
            node.Powered = entry.Flow.Powered; node.PowerInput = entry.Flow.Input; node.PowerOutput = entry.Flow.Output;
            node.PowerConsumed = entry.Flow.Consumed; node.StoredEnergy = entry.Flow.Energy;
            if (changed) node.Revision++;
        }
    }
}
