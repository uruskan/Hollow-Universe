using System;
using System.Globalization;

namespace Hollow.Net.Transport;

/// <summary>
/// Bad-network settings for testing (GROUND.md §4.4): extra round-trip latency, jitter and packet loss, applied by a
/// <see cref="ConditionedRelay"/> to every datagram in both directions.
/// </summary>
public sealed record NetConditions
{
    public static readonly NetConditions None = new();

    /// <summary>Added round-trip time in milliseconds (half each way).</summary>
    public int LatencyMs { get; init; }

    /// <summary>Each datagram's one-way delay varies by up to this many milliseconds either way.</summary>
    public int JitterMs { get; init; }

    /// <summary>Chance, in percent, that a datagram is lost, in each direction.</summary>
    public double LossPercent { get; init; }

    public bool IsNone => LatencyMs <= 0 && JitterMs <= 0 && LossPercent <= 0;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"+{LatencyMs} ms ping, ±{JitterMs} ms jitter, {LossPercent:0.#} % loss");

    /// <summary>Reads the command-line options --lag, --jitter and --loss from an option lookup.</summary>
    public static NetConditions FromOptions(Func<string, string?> option)
    {
        static double Number(string? text) =>
            text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        return new NetConditions
        {
            LatencyMs = (int)Math.Max(0, Number(option("lag"))),
            JitterMs = (int)Math.Max(0, Number(option("jitter"))),
            LossPercent = Math.Clamp(Number(option("loss")), 0, 100),
        };
    }
}
