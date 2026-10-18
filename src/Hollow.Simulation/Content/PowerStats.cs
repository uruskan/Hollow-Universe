using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using Hollow.Simulation.Electricity;
using Hollow.Simulation.Content.Readers;

namespace Hollow.Simulation.Content;

/// <summary>Shared electrical ratings. Rates are energy units/second; capacity stores energy units.</summary>
public sealed class PowerStats
{
    public float Generation { get; init; }
    public float Demand { get; init; }
    public int Outputs { get; init; }
    public float WireRange { get; init; } = PowerPorts.MaximumSpan;
    public bool Wind { get; init; }
    public float Capacity { get; init; }
    public float ChargeRate { get; init; }
    public float DischargeRate { get; init; }
    public float Efficiency { get; init; } = 0.9f;
    private PowerTransfer? _transfer;
    private PowerPort[]? _ports;
    public PowerTransfer Transfer { get => _transfer ?? (Generation > 0 ? PowerTransfer.Generator : Capacity > 0 ? PowerTransfer.Storage : PowerTransfer.Consumer); init => _transfer = value; }
    /// <summary>Fallback is only for old/synthetic definitions; production content declares stable physical ports.</summary>
    public PowerPort[] Ports
    {
        get => _ports ??= Transfer == PowerTransfer.Generator
            ? new[] { new PowerPort { Id = 1, Key = "output", Direction = PowerPortDirection.Output } }
            : new[] { new PowerPort { Id = 1, Key = "input", Direction = PowerPortDirection.Input }, new PowerPort { Id = 2, Key = "output", Direction = PowerPortDirection.Output } };
        init => _ports = value;
    }
    public PowerPort? Port(ushort id)
    { foreach (PowerPort port in Ports) if (port.Id == id) return port; return null; }
    public PowerPort? Input => Direction(PowerPortDirection.Input);
    public PowerPort? Output => Direction(PowerPortDirection.Output);
    private PowerPort? Direction(PowerPortDirection direction)
    { foreach (PowerPort port in Ports) if (port.Direction == direction) return port; return null; }

    internal static PowerStats Read(ContentReading r, JsonElement e, string where)
    {
        JsonFields f = r.Fields;
        f.OnlyKnown(e, where, "generation", "demand", "outputs", "wireRange", "wind", "capacity", "chargeRate", "dischargeRate", "efficiency", "transfer", "ports");
        float generation = f.Number(e, "generation", where, 0), capacity = f.Number(e, "capacity", where, 0);
        string kind = f.Text(e, "transfer", where, generation > 0 ? "generator" : capacity > 0 ? "storage" : "consumer");
        if (!Enum.TryParse(kind, true, out PowerTransfer transfer) || !Enum.IsDefined(transfer))
        { f.Problem(where, "unknown electrical transfer profile"); transfer = PowerTransfer.Consumer; }
        var ports = new List<PowerPort>();
        foreach (JsonElement port in f.List(e, "ports", where, required: false))
        {
            f.OnlyKnown(port, where + ".ports", "id", "key", "direction", "socket", "at", "size");
            int id = f.Integer(port, "id", where);
            if (id is <= 0 or > ushort.MaxValue) f.Problem(where, "port id must be 1–65535");
            if (!Enum.TryParse(f.Text(port, "direction", where), true, out PowerPortDirection direction) || !Enum.IsDefined(direction))
            { f.Problem(where, "port direction must be input or output"); direction = PowerPortDirection.Input; }
            ports.Add(new PowerPort { Id = (ushort)Math.Clamp(id, 1, ushort.MaxValue), Key = f.Text(port, "key", where), Direction = direction,
                Socket = f.Text(port, "socket", where, ""), Position = f.Vector(port, "at", where), Size = f.Number(port, "size", where, .12f) });
        }
        return new PowerStats
        {
            Generation = generation, Demand = f.Number(e, "demand", where, 0), Transfer = transfer,
            Ports = ports.Count == 0 ? new PowerStats { Generation = generation, Capacity = capacity, Transfer = transfer }.Ports : ports.ToArray(),
            Outputs = f.Integer(e, "outputs", where, 1), WireRange = f.Number(e, "wireRange", where, PowerPorts.MaximumSpan),
            Wind = f.Flag(e, "wind", where, false), Capacity = capacity,
            ChargeRate = f.Number(e, "chargeRate", where, 0), DischargeRate = f.Number(e, "dischargeRate", where, 0),
            Efficiency = f.Number(e, "efficiency", where, 0.9f),
        };
    }

    internal static void Check(ContentChecking c, DeployableDef def)
    {
        if (def.Power is not { } p) return;
        if (!float.IsFinite(p.Generation + p.Demand + p.WireRange + p.Capacity + p.ChargeRate + p.DischargeRate + p.Efficiency) ||
            p.Generation < 0 || p.Demand < 0 || p.Outputs is < 0 or > 32 || p.WireRange is <= 0 or > 100 ||
            p.Capacity < 0 || p.ChargeRate < 0 || p.DischargeRate < 0 || p.Efficiency is <= 0 or > 1 ||
            (p.Capacity == 0 && (p.ChargeRate > 0 || p.DischargeRate > 0)) ||
            (p.Wind && p.Generation <= 0))
            c.Problem($"deployables.json: \"{def.Key}\".power", "finite nonnegative ratings, 0–32 outputs, wireRange 0–100 m, efficiency 0–1; storage rates need capacity");
        if (p.Ports.Length is < 1 or > 2 || p.Ports.Select(x => x.Id).Distinct().Count() != p.Ports.Length ||
            p.Ports.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != p.Ports.Length ||
            p.Ports.Any(x => x.Id == 0 || string.IsNullOrWhiteSpace(x.Key) || !PowerPorts.Finite(x.Position) || !float.IsFinite(x.Size) || x.Size is <= 0 or > .5f) ||
            p.Ports.Count(x => x.Direction == PowerPortDirection.Output) != 1 ||
            p.Ports.Count(x => x.Direction == PowerPortDirection.Input) != (p.Transfer == PowerTransfer.Generator ? 0 : 1))
            c.Problem($"deployables.json: \"{def.Key}\".power.ports", "one output, one input except generators, unique stable IDs/keys and finite cube poses are required");
        if ((p.Transfer == PowerTransfer.Generator && p.Capacity > 0) || (p.Transfer == PowerTransfer.Storage && (p.Generation > 0 || p.Demand > 0)) ||
            (p.Transfer == PowerTransfer.Consumer && (p.Generation > 0 || p.Capacity > 0)))
            c.Problem($"deployables.json: \"{def.Key}\".power", "ratings must match the declared generator, consumer or storage transfer profile");
        CompoundAssembly? assembly = c.Loaded.Compound?.Find(def.Assembly);
        foreach (PowerPort port in p.Ports)
            if (port.Socket.Length > 0 && (assembly?.Sockets.FirstOrDefault(s => s.Name == port.Socket) is not { } socket ||
                System.Numerics.Vector3.DistanceSquared(socket.At, port.Position) > .000001f))
                c.Problem($"deployables.json: \"{def.Key}\".power.ports", $"port '{port.Key}' must match its explicit assembly socket '{port.Socket}'");
    }
}
