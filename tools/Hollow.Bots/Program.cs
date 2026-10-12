using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Hollow.Client;
using Hollow.Net.Transport;
using Hollow.Server;
using LiteNetLib;

namespace Hollow.Bots;

/// <summary>
/// Headless clients (GROUND.md §4.3).
/// <list type="bullet">
/// <item><c>--server 127.0.0.1:27015 --count 100</c>: bots join a running server and play until stopped.</item>
/// <item><c>loadtest --count 100 --duration 60</c>: a server in this process plus the bots; prints tick time,
/// bandwidth per client and prediction error (GROUND.md §5, measured).</item>
/// </list>
/// Both take <c>--script wander|circle|jump|mixed</c> and <c>--lag</c>, <c>--jitter</c>, <c>--loss</c>.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Dictionary<string, string> o = ParseOptions(args, out List<string> words);
        var script = Enum.TryParse(o.GetValueOrDefault("script", "mixed"), true, out BotScript s) ? s : BotScript.Mixed;
        NetConditions conditions = NetConditions.FromOptions(key => o.GetValueOrDefault(key));
        int count = (int)Number(o, "count", 100);
        TimerResolution.Begin();
        try
        {
            if (words.Contains("loadtest"))
            {
                return LoadTest(count, Number(o, "duration", 60), Number(o, "warmup", 8), script, conditions, (float)Number(o, "relevance", 300));
            }
            if (!o.TryGetValue("server", out string? server))
            {
                Console.WriteLine("Hollow.Bots --server <host:port> [--count 100] [--duration <s>] [--script mixed] [--lag ms --jitter ms --loss %]\n" +
                    "Hollow.Bots loadtest [--count 100] [--duration 60] [--warmup 8] [--script mixed] [--lag ms --jitter ms --loss %]");
                return 1;
            }
            string[] parts = server.Split(':');
            int port = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : Net.Protocol.Protocol.DefaultPort;
            return Play(parts[0], port, count, Number(o, "duration", double.PositiveInfinity), script, conditions,
                o.GetValueOrDefault("prefix", "bot"), Number(o, "report-after", -1));
        }
        finally
        {
            TimerResolution.End();
        }
    }

    private static int Play(string host, int port, int count, double duration, BotScript script, NetConditions conditions,
        string prefix, double reportAfter)
    {
        Console.WriteLine($"{count} bots ({script}) joining {host}:{port}" + (conditions.IsNone ? "" : $", {conditions}"));
        using var swarm = new BotSwarm(host, port, count, script, conditions, prefix, Console.WriteLine);
        bool stop = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop = true;
        };
        double elapsed = 0;
        if (reportAfter > 0)
        {
            // Load test: settle, start the statistics afresh, run, and report on one line for the parent process.
            swarm.Run(reportAfter, () => stop);
            foreach (GameClient client in swarm.Clients)
            {
                client.Stats.ResetTotals();
            }
            swarm.Run(Math.Max(0, duration - reportAfter), () => stop);
            IReadOnlyList<GameClient> all = swarm.Clients;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"REPORT {all.Sum(c => c.Stats.Reconciliations)} {all.Sum(c => c.Stats.PredictionErrorTotal):R} " +
                $"{all.Max(c => c.Stats.PredictionErrorWorst):R} {all.Sum(c => c.Stats.Corrections)} {all.Average(c => c.Stats.RoundTripMs):R} " +
                $"{all.Average(c => c.Stats.SnapshotsPerSecond):R} {swarm.Playing} {swarm.Disconnected} {all.Average(c => c.Stats.SnapshotLossPercent):R}"));
            return 0;
        }
        while (!stop && elapsed < duration)
        {
            double step = Math.Min(5, duration - elapsed);
            swarm.Run(step, () => stop);
            elapsed += step;
            IReadOnlyList<GameClient> clients = swarm.Clients;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{elapsed,5:0} s: {swarm.Playing} playing, {swarm.Disconnected} disconnected; per bot {clients.Average(c => c.Stats.KilobitsIn):0.0} kbit/s in, " +
                $"{clients.Average(c => c.Stats.KilobitsOut):0.0} out, {clients.Average(c => c.Stats.SnapshotsPerSecond):0.0} snapshots/s, " +
                $"round trip {clients.Average(c => c.Stats.RoundTripMs):0} ms, prediction error {clients.Average(c => c.Stats.PredictionErrorMean) * 1000:0.00} mm"));
        }
        return 0;
    }

    /// <summary>
    /// The load test of GROUND.md §5: a server in this process and <paramref name="count"/> bots in a child process (so
    /// the bots' own work and garbage collections don't land on the server's ticks), measured after a warm-up.
    /// </summary>
    private static int LoadTest(int count, double duration, double warmup, BotScript script, NetConditions conditions, float relevance)
    {
        Console.WriteLine($"Load test: a server and {count} bots ({script}) in another process, {warmup:0} s warm-up, {duration:0} s measured" +
            (conditions.IsNone ? "" : $", bots through a relay: {conditions}"));
        using var server = new GameServer(new ServerOptions { Port = 0, DatabasePath = null, RelevanceRadius = relevance, Log = _ => { } });
        server.StartThread();

        string self = Environment.ProcessPath!;
        var start = new ProcessStartInfo
        {
            FileName = self,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        }
        string[] childArgs =
        {
            "--server", $"127.0.0.1:{server.Port}", "--count", count.ToString(CultureInfo.InvariantCulture),
            "--duration", (warmup + duration).ToString(CultureInfo.InvariantCulture),
            "--report-after", warmup.ToString(CultureInfo.InvariantCulture), "--script", script.ToString(),
            "--lag", conditions.LatencyMs.ToString(CultureInfo.InvariantCulture),
            "--jitter", conditions.JitterMs.ToString(CultureInfo.InvariantCulture),
            "--loss", conditions.LossPercent.ToString(CultureInfo.InvariantCulture),
        };
        foreach (string arg in childArgs)
        {
            start.ArgumentList.Add(arg);
        }
        using Process bots = Process.Start(start)!;
        string? report = null;
        bots.OutputDataReceived += (_, e) =>
        {
            if (e.Data?.StartsWith("REPORT ", StringComparison.Ordinal) == true)
            {
                report = e.Data;
            }
        };
        bots.BeginOutputReadLine();

        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(warmup));
        Dictionary<string, (long Bytes, long Packets, long BytesIn, long PacketsIn, long Applied, long Missing)> before =
            server.Inspect(sv =>
            {
                sv.Ticks.ResetTotals();
                return sv.Players.ToDictionary(p => p.Name, p => Traffic(p));
            });
        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(duration));
        var measured = server.Inspect(sv => new
        {
            Ticks = sv.Ticks.Ticks,
            Average = sv.Ticks.TotalAverageMs,
            P99 = sv.Ticks.TotalPercentileMs(99),
            Max = sv.Ticks.TotalMaxMs,
            Over = sv.Ticks.Overruns,
            Tiles = sv.Zone.Physics.TileCount,
            OnDemand = sv.Zone.Physics.TilesBuiltOnDemand,
            Players = sv.Players.Select(p =>
            {
                var b = before.GetValueOrDefault(p.Name);
                var now = Traffic(p);
                return new
                {
                    OutKbit = ((now.Bytes - b.Bytes) + (now.Packets - b.Packets) * 28) * 8 / 1000.0 / duration,
                    InKbit = ((now.BytesIn - b.BytesIn) + (now.PacketsIn - b.PacketsIn) * 28) * 8 / 1000.0 / duration,
                    Applied = now.Applied - b.Applied,
                    Missing = now.Missing - b.Missing,
                    Written = p.Snapshots.LastWritten,
                    Offered = p.Snapshots.LastOffered,
                };
            }).ToList(),
        });
        if (!bots.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            bots.Kill();
        }
        bots.WaitForExit();
        if (measured.Players.Count == 0 || report == null)
        {
            Console.WriteLine($"The bots didn't play or didn't report ({measured.Players.Count} players).");
            return 2;
        }
        string[] r = report.Split(' ');
        double F(int i) => double.Parse(r[i], CultureInfo.InvariantCulture);
        long applied = measured.Players.Sum(p => p.Applied), missing = measured.Players.Sum(p => p.Missing);
        double meanError = F(1) == 0 ? 0 : F(2) / F(1);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Server tick ({measured.Ticks} ticks): {measured.Average:0.00} ms average, {measured.P99:0.0} ms 99th percentile, " +
            $"{measured.Max:0.0} ms worst, {measured.Over} over 33.3 ms; {measured.Tiles} ground tiles, {measured.OnDemand} built on demand\n" +
            $"Bandwidth per client, server to client: {measured.Players.Average(p => p.OutKbit):0.0} kbit/s average, " +
            $"{measured.Players.Max(p => p.OutKbit):0.0} most (UDP and IP headers included)\n" +
            $"Bandwidth per client, client to server: {measured.Players.Average(p => p.InKbit):0.0} kbit/s average\n" +
            $"Entities per snapshot: {measured.Players.Average(p => p.Written):0.0} of {measured.Players.Average(p => p.Offered):0.0} relevant; " +
            $"snapshots received {F(6):0.0}/s per bot, {F(9):0.0} % lost\n" +
            $"Inputs applied on their tick: {100.0 * applied / Math.Max(1, applied + missing):0.00} % ({missing} missing)\n" +
            $"Prediction: {F(1):0} reconciliations, mean error {meanError * 1000:0.000} mm, worst {F(3) * 1000:0.0} mm, " +
            $"{F(4):0} corrections; round trip {F(5):0} ms\n" +
            $"Bots: {F(7):0} playing, {F(8):0} disconnected"));
        return F(7) == count ? 0 : 2;
    }

    private static (long Bytes, long Packets, long BytesIn, long PacketsIn, long Applied, long Missing) Traffic(PlayerSession p)
    {
        NetStatistics s = p.Peer.Statistics;
        return (s.BytesSent, s.PacketsSent, s.BytesReceived, s.PacketsReceived, p.InputsApplied, p.InputsMissing);
    }

    private static Dictionary<string, string> ParseOptions(string[] args, out List<string> words)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        words = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                words.Add(args[i].ToLowerInvariant());
                continue;
            }
            string key = args[i][2..];
            int equals = key.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
            {
                options[key[..equals]] = key[(equals + 1)..];
            }
            else
            {
                options[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            }
        }
        return options;
    }

    private static double Number(Dictionary<string, string> o, string key, double fallback) =>
        o.TryGetValue(key, out string? v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;
}
