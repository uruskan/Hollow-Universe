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
/// Both take <c>--script wander|circle|jump|mixed|survive|fight|raid</c> and <c>--lag</c>, <c>--jitter</c>, <c>--loss</c>; the load
/// test takes <c>--kit</c> (a loot table every new character gets; with <c>fight</c> and <c>raid</c>, every new life too),
/// <c>--spawn origin|beach</c> (where bots arrive: the landing site, the default, or the beaches) and <c>--stand-in</c>. With
/// <c>raid</c> the server builds <c>--raid-bases</c> huts (half the bots by default) on a ring of <c>--raid-radius</c> metres,
/// and each bot raids the next one's hut.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Dictionary<string, string> o = ParseOptions(args, out List<string> words);
        var script = Enum.TryParse(o.GetValueOrDefault("script", "mixed"), true, out BotScript s) ? s : BotScript.Mixed;
        NetConditions conditions = NetConditions.FromOptions(key => o.GetValueOrDefault(key));
        int count = (int)Number(o, "count", 100);
        int raidBases = (int)Number(o, "raid-bases", script == BotScript.Raid ? Math.Max(1, count / 2) : 0);
        float raidRadius = (float)Number(o, "raid-radius", 70);
        TimerResolution.Begin();
        try
        {
            if (words.Contains("loadtest"))
            {
                return LoadTest(count, Number(o, "duration", 60), Number(o, "warmup", 8), script, conditions, (float)Number(o, "relevance", 300),
                    o.GetValueOrDefault("kit") ?? (script == BotScript.Raid ? "raid-kit" : null), raidBases, raidRadius,
                    o.GetValueOrDefault("spawn", "origin") == "beach", o.ContainsKey("stand-in"));
            }
            if (!o.TryGetValue("server", out string? server))
            {
                Console.WriteLine("Hollow.Bots --server <host:port> [--count 100] [--duration <s>] [--script mixed] [--lag ms --jitter ms --loss %]\n" +
                    "Hollow.Bots loadtest [--count 100] [--duration 60] [--warmup 8] [--script mixed] [--lag ms --jitter ms --loss %] " +
                    "[--spawn origin|beach] [--stand-in]");
                return 1;
            }
            string[] parts = server.Split(':');
            int port = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : Net.Protocol.Protocol.DefaultPort;
            return Play(parts[0], port, count, Number(o, "duration", double.PositiveInfinity), script, conditions,
                o.GetValueOrDefault("prefix", "bot"), Number(o, "report-after", -1), raidBases, raidRadius);
        }
        finally
        {
            TimerResolution.End();
        }
    }

    private static int Play(string host, int port, int count, double duration, BotScript script, NetConditions conditions,
        string prefix, double reportAfter, int raidBases, float raidRadius)
    {
        Console.WriteLine($"{count} bots ({script}) joining {host}:{port}" + (conditions.IsNone ? "" : $", {conditions}"));
        using var swarm = new BotSwarm(host, port, count, script, conditions, prefix, Console.WriteLine, raidBases, raidRadius);
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
                $"{all.Average(c => c.Stats.SnapshotsPerSecond):R} {swarm.Playing} {swarm.Disconnected} {all.Average(c => c.Stats.SnapshotLossPercent):R} " +
                $"{all.Sum(c => c.Stats.ClockJumps)} {swarm.Survival.Crafts} {swarm.Survival.Picks} {swarm.Survival.Fights} {all.Sum(c => c.Stats.Teleports)} " +
                $"{swarm.Fights.Bursts} {swarm.Fights.Reloads} {swarm.Fights.Assists} {all.Sum(c => c.Local.Gun.PendingCount)} " +
                $"{swarm.Building.Done} {swarm.Building.Refused} {swarm.Building.Toggles} {all.Max(c => c.Building?.PieceCount ?? 0)} " +
                $"{swarm.Raids.Placed} {swarm.Raids.Thrown} {swarm.Raids.Rockets} {swarm.Raids.Bursts} {swarm.Raids.Alerts}"));
            return 0;
        }
        Console.CancelKeyPress += (_, _) => Console.WriteLine(swarm.BuildProgress);
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
        if (script == BotScript.Build)
        {
            Console.WriteLine($"Building bots: {swarm.BuildProgress}");
        }
        return 0;
    }

    /// <summary>
    /// The load test of GROUND.md §5: a server in this process and <paramref name="count"/> bots in a child process (so
    /// the bots' own work and garbage collections don't land on the server's ticks), measured after a warm-up.
    /// </summary>
    private static int LoadTest(int count, double duration, double warmup, BotScript script, NetConditions conditions, float relevance, string? kit,
        int raidBases, float raidRadius, bool beaches, bool standIn)
    {
        Console.WriteLine($"Load test: a server and {count} bots ({script}) in another process, {warmup:0} s warm-up, {duration:0} s measured" +
            (conditions.IsNone ? "" : $", bots through a relay: {conditions}") +
            (standIn ? ", on the stand-in ground" : $", on the first world, arriving {(beaches ? "on the beaches" : "at the landing site")}"));
        // By default everyone arrives at the landing site, so each bot sees all the others (the bandwidth worst case).
        // Fighting bots get their kit with every life, so the firefights go on after the first deaths.
        using var server = new GameServer(new ServerOptions
        {
            Port = 0, DatabasePath = null, RelevanceRadius = relevance, JoinKit = kit, KitEveryLife = script is BotScript.Fight or BotScript.Raid,
            RaidBases = script == BotScript.Raid ? raidBases : 0, RaidRadius = raidRadius, Log = _ => { },
            SpawnAtOrigin = !beaches, StandIn = standIn,
        });
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
            "--raid-bases", raidBases.ToString(CultureInfo.InvariantCulture), "--raid-radius", raidRadius.ToString(CultureInfo.InvariantCulture),
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
            else if (e.Data?.Contains("refused", StringComparison.Ordinal) == true && Environment.GetEnvironmentVariable("HOLLOW_BOT_REFUSALS") != null)
            {
                Console.WriteLine("  bot: " + e.Data);
            }
        };
        bots.BeginOutputReadLine();

        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(warmup));
        TimeSpan stepBefore = TimeSpan.Zero, raidBefore = TimeSpan.Zero;
        Dictionary<string, (long Bytes, long Packets, long BytesIn, long PacketsIn, long Applied, long Missing)> before =
            server.Inspect(sv =>
            {
                sv.Ticks.ResetTotals();
                stepBefore = sv.StepTime;
                raidBefore = sv.Zone.RaidStats.Time;
                return sv.Players.ToDictionary(p => p.Name, p => Traffic(p));
            });
        string countersBefore = server.Inspect(sv => sv.Counters.ToString());
        System.Threading.Thread.Sleep(TimeSpan.FromSeconds(duration));
        var measured = server.Inspect(sv => new
        {
            Ticks = sv.Ticks.Ticks,
            StepMs = (sv.StepTime - stepBefore).TotalMilliseconds,
            Average = sv.Ticks.TotalAverageMs,
            P99 = sv.Ticks.TotalPercentileMs(99),
            Max = sv.Ticks.TotalMaxMs,
            Over = sv.Ticks.Overruns,
            Tiles = sv.Zone.Physics.TileCount,
            Props = sv.Zone.Physics.PropCount,
            Things = sv.Zone.Statics().Count(),
            Pieces = sv.Zone.Building!.PieceCount,
            Structures = sv.Zone.Building.Structures.Count,
            Doors = sv.Zone.Building.Doors.Count(),
            Claims = sv.Zone.Building.Claims.Count(),
            Placed = sv.PiecesPlaced,
            Counters = sv.Counters.ToString(),
            Intents = sv.Players.Sum(p => p.IntentsAccepted),
            GunActions = sv.Players.Sum(p => p.GunActions),
            BulletMs = sv.Zone.BulletTime.TotalMilliseconds,
            GunResults = sv.Zone.GunResults.ToArray(),
            BulletSteps = sv.Zone.BulletStepsFlown,
            RaidMs = (sv.Zone.RaidStats.Time - raidBefore).TotalMilliseconds,
            Raid = sv.Zone.RaidStats.ToString(),
            Alerts = sv.Notifications.Counters.ToString(),
            Explosives = sv.Zone.Explosives.Count,
            Restored = sv.Arena?.Restored ?? 0,
            GunLimited = sv.Players.Sum(p => p.GunRateLimited),
            Refused = sv.Players.Sum(p => p.IntentsRefused),
            Limited = sv.Players.Sum(p => p.IntentsRateLimited),
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
            $"{measured.Max:0.0} ms worst, {measured.Over} over 33.3 ms; {measured.Tiles} ground tiles, {measured.OnDemand} built on demand; " +
            $"the zone's step {measured.StepMs / Math.Max(1, measured.Ticks):0.00} ms of it on average\n" +
            $"Bandwidth per client, server to client: {measured.Players.Average(p => p.OutKbit):0.0} kbit/s average, " +
            $"{measured.Players.Max(p => p.OutKbit):0.0} most (UDP and IP headers included)\n" +
            $"Bandwidth per client, client to server: {measured.Players.Average(p => p.InKbit):0.0} kbit/s average\n" +
            $"Entities per snapshot: {measured.Players.Average(p => p.Written):0.0} of {measured.Players.Average(p => p.Offered):0.0} relevant; " +
            $"snapshots received {F(6):0.0}/s per bot, {F(9):0.0} % lost\n" +
            $"Inputs applied on their tick: {100.0 * applied / Math.Max(1, applied + missing):0.00} % ({missing} missing)\n" +
            $"Prediction: {F(1):0} reconciliations, mean error {meanError * 1000:0.000} mm, worst {F(3) * 1000:0.0} mm, " +
            $"{F(4):0} corrections; round trip {F(5):0} ms; {F(10):0} clock jumps\n" +
            $"Bots: {F(7):0} playing, {F(8):0} disconnected"));
        if (r.Length > 18)
        {
            string refusals = string.Join(", ", Enumerable.Range(1, 15).Where(i => measured.GunResults[i] > 0)
                .Select(i => $"{(Hollow.Simulation.Combat.GunResult)i} {measured.GunResults[i]}"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Guns (bots): {F(15):0} bursts, {F(16):0} reloads asked for, {F(17):0} downed players helped up, {F(18):0} gun actions unanswered at the end; " +
                $"server: {measured.GunActions} gun actions arrived ({measured.GunLimited} over budget); bullets flown in {measured.BulletMs / Math.Max(1, measured.Ticks + warmup * 30):0.00} ms a tick " +
                $"on average ({measured.BulletSteps} steps, {measured.BulletMs * 1000 / Math.Max(1, measured.BulletSteps):0.0} µs a step); " +
                $"answered {measured.GunResults[0]} accepted, refused: {(refusals.Length > 0 ? refusals : "none")} (whole run)"));
        }
        if (r.Length > 27 && script == BotScript.Raid)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Raids (bots): {F(23):0} charges placed, {F(24):0} bag charges and grenades thrown, {F(25):0} rockets fired, {F(26):0} bursts of explosive rounds; " +
                $"{F(27):0} raid alerts heard\n" +
                $"Raids (server, whole run): {measured.Raid}; explosives and blasts {measured.RaidMs / Math.Max(1, measured.Ticks):0.000} ms a tick on average " +
                $"(measured part); {measured.Explosives} explosives in the world at the end, {measured.Pieces} building pieces, {measured.Restored} huts put back\n" +
                $"Alerts: {measured.Alerts}"));
        }
        if (r.Length > 13)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Survival (whole run): {measured.Counters}; bots started {F(11):0} crafts, {F(12):0} pickups and {F(13):0} fights; " +
                $"{(r.Length > 14 ? F(14) : 0):0} teleports (respawns, not counted as prediction errors)\n" +
                $"Intents: {measured.Intents} accepted, {measured.Refused} refused ({measured.Limited} over budget); " +
                $"{measured.Things} things in the world, {measured.Props} solid props in the physics"));
        }
        if (r.Length > 22)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Building (whole run): {measured.Placed} pieces placed; the world has {measured.Pieces} pieces in {measured.Structures} structures, " +
                $"{measured.Doors} doors, {measured.Claims} claim devices; bots: {F(19):0} building intents done, {F(20):0} refused, " +
                $"{F(21):0} door toggles; a bot's copy holds {F(22):0} pieces"));
        }
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
