using System;
using System.Collections.Generic;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Electricity;

internal struct ElectricalFlow
{
    public float Input, Output, Consumed, DownstreamDemand, UsedDownstream, Draw, Energy;
    public bool Powered;
}

/// <summary>Transfer behavior is registered once per profile; item identity never selects a power rule.</summary>
internal static class PowerTransferRules
{
    internal interface ITransfer
    {
        void Deliver(PowerStats rating, bool enabled, float generation, float horizon, ref ElectricalFlow flow);
        void Settle(PowerStats rating, bool enabled, float seconds, float horizon, ref ElectricalFlow flow);
    }
    private static readonly IReadOnlyDictionary<PowerTransfer, ITransfer> Rules = new Dictionary<PowerTransfer, ITransfer>
    {
        [PowerTransfer.Generator] = new Generator(),
        [PowerTransfer.Consumer] = new Consumer(),
        [PowerTransfer.Storage] = new Storage(),
    };
    internal static ITransfer Of(PowerTransfer kind) => Rules[kind];

    private sealed class Generator : ITransfer
    {
        public void Deliver(PowerStats rating, bool enabled, float generation, float horizon, ref ElectricalFlow flow)
        { flow.Output = enabled ? generation : 0; flow.Powered = flow.Output > 0; }
        public void Settle(PowerStats rating, bool enabled, float seconds, float horizon, ref ElectricalFlow flow) => flow.Draw = 0;
    }
    private sealed class Consumer : ITransfer
    {
        public void Deliver(PowerStats rating, bool enabled, float generation, float horizon, ref ElectricalFlow flow)
        {
            flow.Powered = enabled && flow.Input + .0001f >= rating.Demand;
            flow.Consumed = flow.Powered ? Math.Min(rating.Demand, flow.Input) : 0;
            flow.Output = Math.Max(0, flow.Input - flow.Consumed);
        }
        public void Settle(PowerStats rating, bool enabled, float seconds, float horizon, ref ElectricalFlow flow) =>
            flow.Draw = Math.Min(flow.Input, flow.Consumed + flow.UsedDownstream);
    }
    private sealed class Storage : ITransfer
    {
        public void Deliver(PowerStats rating, bool enabled, float generation, float horizon, ref ElectricalFlow flow)
        {
            // The cell only supplements a downstream deficit. Its input cannot power upstream consumers.
            float supplement = enabled ? Math.Min(Math.Max(0, flow.DownstreamDemand - flow.Input), Math.Min(rating.DischargeRate, flow.Energy / horizon)) : 0;
            flow.Output = flow.Input + supplement;
            flow.Powered = enabled && flow.Output > 0;
        }
        public void Settle(PowerStats rating, bool enabled, float seconds, float horizon, ref ElectricalFlow flow)
        {
            float delivered = Math.Min(flow.Output, flow.UsedDownstream);
            float discharge = Math.Max(0, delivered - flow.Input);
            float inputUsed = Math.Min(flow.Input, delivered);
            float charge = enabled && discharge <= 0 ? Math.Min(Math.Max(0, flow.Input - inputUsed),
                Math.Min(rating.ChargeRate, (rating.Capacity - flow.Energy) / (horizon * rating.Efficiency))) : 0;
            flow.Energy = Math.Clamp(flow.Energy + charge * rating.Efficiency * seconds - discharge * seconds, 0, rating.Capacity);
            flow.Draw = inputUsed + charge;
        }
    }
}
