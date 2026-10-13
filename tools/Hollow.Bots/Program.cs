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
/// Both take <c>--script wander|circle|jump|mixed|survive|fight|raid|fly|world</c> and <c>--lag</c>, <c>--jitter</c>, <c>--loss</c>; the load
/// test takes <c>--kit</c> (a loot table every new character gets; with <c>fight</c> and <c>raid</c>, every new life too),
/// <c>--spawn origin|beach</c> (where bots arrive: the landing site, the default, or the beaches) and <c>--stand-in</c>. With
/// <c>raid</c> the server builds <c>--raid-bases</c> huts (half the bots by default) on a ring of <c>--raid-radius</c> metres,
/// and each bot raids the next one's hut. <c>--animals &lt;n&gt;</c> puts n animals in herds within 220 m of the origin (G7, GROUND.md §5.6).
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
        // --fly n: the first n bots fly (each gets a ship), the rest play --script (GROUND.md §5.11).
        int fly = (int)Number(o, "fly", script == BotScript.Fly ? count : 0);
        TimerResolution.Begin();
        try
        {
            if (words.Contains("loadtest"))
            {
                return LoadTest(count, Number(o, "duration", 60), Number(o, "warmup", 8), script, conditions, (float)Number(o, "relevance", 300),
                    o.GetValueOrDefault("kit") ?? (script == BotScript.Raid ? "raid-kit" : script == BotScript.World ? "world-kit" : null), raidBases, raidRadius,
                    o.GetValueOrDefault("spawn", "origin") == "beach", o.ContainsKey("stand-in"), fly, Number(o, "storm", 0),
                    (int)Number(o, "animals", 0));
            }
            if (!o.TryGetValue("server", out string? server))
            {
                Console.WriteLine("Hollow.Bots --server <host:port> [--count 100] [--duration <s>] [--script mixed] [--lag ms --jitter ms --loss %]\n" +
                    "Hollow.Bots loadtest [--count 100] [--duration 60] [--warmup 8] [--script mixed] [--lag ms --jitter ms --loss %] " +
                    "[--spawn origin|beach] [--stand-in] [--fly n] [--storm s]");
                return 1;
            }
            string[] parts = server.Split(':');
            int port = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : Net.Protocol.Protocol.DefaultPort;
            return Play(parts[0], port, count, Number(o, "duration", double.PositiveInfinity), script, conditions,
                o.GetValueOrDefault("prefix", "bot"), Number(o, "report-after", -1), raidBases, raidRadius, fly);
        }
        finally
        {
            TimerResolution.End();
        }
    }

    private static int Play(string host, int port, int count, double duration, BotScript script, NetConditions conditions,
        string prefix, double reportAfter, int raidBases, float raidRadius, int fly)
    {
        Console.WriteLine($"{count} bots ({script}) joining {host}:{port}" + (conditions.IsNone ? "" : $", {conditions}"));
        using var swarm = new BotSwarm(host, port, count, script, conditions, prefix, Console.WriteLine, raidBases, raidRadius, fly);
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
                $"{swarm.Raids.Placed} {swarm.Raids.Thrown} {swarm.Raids.Rockets} {swarm.Raids.Bursts} {swarm.Raids.Alerts} " +
                $"{swarm.Flights.Boardings} {swarm.Flights.Flying} {swarm.Flights.Seconds:R} {swarm.Flights.Corrections} " +
                $"{swarm.World.Broken} {swarm.World.Emptied} {swarm.World.Picked} {swarm.World.Recycled} {swarm.World.Researched} {swarm.World.Learnt} {swarm.World.Teamed}"));
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
        int raidBases, float raidRadius, bool beaches, bool standIn, int fly, double storm, int animals = 0)
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
            ShipOnJoin = script == BotScript.Fly,
            ShipOnJoinFor = fly > 0 ? name => name.StartsWith("bot", StringComparison.Ordinal) && int.TryParse(name.AsSpan(3), out int n) && n <= fly : null,
            // G7: a fixed population for the animal load test (none by default, so the earlier measurements compare).
            Animals = false, AnimalPopulation = animals,
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
            "--fly", fly.ToString(CultureInfo.InvariantCulture),
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
        TimeSpan stepBefore = TimeSpan.Zero, raidBefore = TimeSpan.Zero, rebuildTimeBefore = TimeSpan.Zero;
        TimeSpan worldBefore = TimeSpan.Zero, worldSendBefore = TimeSpan.Zero;
        int rebuildsBefore = 0;
        TimeSpan animalBefore = TimeSpan.Zero;
        long thinksBefore = 0;
        Dictionary<string, (long Bytes, long Packets, long BytesIn, long PacketsIn, long Applied, long Missing)> before =
            server.Inspect(sv =>
            {
                sv.Ticks.ResetTotals();
                sv.Profile.Reset();
                stepBefore = sv.StepTime;
                raidBefore = sv.Zone.RaidStats.Time;
                rebuildsBefore = sv.Zone.Physics.BroadPhaseRebuilds;
                rebuildTimeBefore = sv.Zone.Physics.BroadPhaseTime;
                worldBefore = sv.Zone.WorldStats.Time;
                worldSendBefore = sv.WorldTime;
                animalBefore = sv.Zone.AnimalTime;
                thinksBefore = sv.Zone.Animals?.Thinks ?? 0;
                return sv.Players.ToDictionary(p => p.Name, p => Traffic(p));
            });
        string countersBefore = server.Inspect(sv => sv.Counters.ToString());
        (int Gen0, int Gen1, int Gen2, TimeSpan Paused, long Allocated) gcBefore =
            (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration(), GC.GetTotalAllocatedBytes());
        int storms = 0;
        if (storm > 0)
        {
            // Respawn storms (GROUND.md §5.11): every bot killed at once, every few seconds; each respawns on a beach.
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds + storm <= duration)
            {
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(storm));
                foreach (string name in server.Inspect(sv => sv.Players.Where(p => p.Playing).Select(p => p.Name).ToList()))
                {
                    server.Execute($"kill {name}");
                }
                storms++;
            }
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(Math.Max(0, duration - clock.Elapsed.TotalSeconds)));
        }
        else
        {
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(duration));
        }
        (int Gen0, int Gen1, int Gen2, TimeSpan Paused, long Allocated) gcAfter =
            (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration(), GC.GetTotalAllocatedBytes());
        var measured = server.Inspect(sv => new
        {
            Profile = sv.Profile.Report(),
            Respawns = sv.Profile.Calls(Hollow.Simulation.Diagnostics.ProfileSection.Respawn),
            BodiesMade = sv.Zone.Physics.BodiesMade,
            BodiesReused = sv.Zone.Physics.BodiesReused,
            Scatter = sv.Zone.Nature is Hollow.Simulation.Nature.ScatterNaturalObjects scatter ? $"; {scatter.CachedTiles} scatter tiles cached, {scatter.Generated} made in the whole run" : "",
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
            World = sv.Zone.WorldStats.ToString(),
            WorldMs = (sv.Zone.WorldStats.Time - worldBefore).TotalMilliseconds,
            WorldSendMs = (sv.WorldTime - worldSendBefore).TotalMilliseconds,
            WorldProps = sv.Zone.Props.Count,
            Teams = sv.Zone.Teams.Count,
            TeamMembers = sv.Zone.Teams.All.Sum(t => t.Members.Count),
            Explosives = sv.Zone.Explosives.Count,
            Restored = sv.Arena?.Restored ?? 0,
            GunLimited = sv.Players.Sum(p => p.GunRateLimited),
            Refused = sv.Players.Sum(p => p.IntentsRefused),
            Limited = sv.Players.Sum(p => p.IntentsRateLimited),
            OnDemand = sv.Zone.Physics.TilesBuiltOnDemand,
            Ships = sv.Zone.Ships.Count,
            ShipsFlying = sv.Zone.Ships.Count(x => !x.State.Asleep && !x.InOrbit),
            ShipMs = sv.Zone.ShipTime.TotalMilliseconds,
            ShipSteps = sv.Zone.ShipSteps,
            ShipIntents = sv.ShipIntentsAccepted,
            ShipRefused = sv.ShipIntentsRefused,
            Crashes = sv.ShipCrashes,
            Rebuilds = sv.Zone.Physics.BroadPhaseRebuilds - rebuildsBefore,
            RebuildMs = (sv.Zone.Physics.BroadPhaseTime - rebuildTimeBefore).TotalMilliseconds,
            RebuildWorstMs = sv.Zone.Physics.BroadPhaseWorst.TotalMilliseconds,
            AnimalMs = (sv.Zone.AnimalTime - animalBefore).TotalMilliseconds,
            Animals = sv.Zone.Animals?.Count ?? 0,
            AnimalsAwake = sv.Zone.Animals?.Awake ?? 0,
            AnimalsDead = sv.Zone.Animals?.All.Count(a => a.IsDead) ?? 0,
            Thinks = (sv.Zone.Animals?.Thinks ?? 0) - thinksBefore,
            AnimalStatus = sv.AnimalStatus(),
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
        if (r.Length > 38 && script == BotScript.World)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"World (bots, whole run): {F(32):0} barrels broken, {F(33):0} crates emptied, {F(34):0} things picked up, " +
                $"{F(35):0} recycler runs started, {F(36):0} research begun, {F(37):0} blueprints learnt; {F(38):0} bots in teams\n" +
                $"World (server, whole run): {measured.World}; {measured.WorldProps} props in the world at the end; " +
                $"{measured.Teams} teams with {measured.TeamMembers} members; the world's steps {measured.WorldMs / Math.Max(1, measured.Ticks):0.000} ms " +
                $"and its messages {measured.WorldSendMs / Math.Max(1, measured.Ticks):0.000} ms a tick on average (measured part)"));
        }
        if (r.Length > 13)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Survival (whole run): {measured.Counters}; bots started {F(11):0} crafts, {F(12):0} pickups and {F(13):0} fights; " +
                $"{(r.Length > 14 ? F(14) : 0):0} teleports (respawns, not counted as prediction errors)\n" +
                $"Intents: {measured.Intents} accepted, {measured.Refused} refused ({measured.Limited} over budget); " +
                $"{measured.Things} things in the world, {measured.Props} solid props in the physics"));
        }
        if (animals > 0)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Animals: {measured.Animals} at the end ({measured.AnimalsAwake} awake, {measured.AnimalsDead} corpses); their AI, movement and population " +
                $"{measured.AnimalMs / Math.Max(1, measured.Ticks):0.000} ms a tick on average ({measured.AnimalMs * 1000 / Math.Max(1, measured.Ticks) / Math.Max(1, measured.Animals):0.0} µs an animal); " +
                $"{measured.Thinks / duration / Math.Max(1, measured.Animals):0.0} thinks an animal a second\n{measured.AnimalStatus}"));
        }
        if (r.Length > 22)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Building (whole run): {measured.Placed} pieces placed; the world has {measured.Pieces} pieces in {measured.Structures} structures, " +
                $"{measured.Doors} doors, {measured.Claims} claim devices; bots: {F(19):0} building intents done, {F(20):0} refused, " +
                $"{F(21):0} door toggles; a bot's copy holds {F(22):0} pieces"));
        }
        if (r.Length > 31 && fly > 0)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Ships (G6): {measured.Ships} in the zone, {measured.ShipsFlying} awake at the end; bots boarded {F(28):0} times, {F(29):0} flying " +
                $"at the end, {F(30) / Math.Max(1, F(29)):0} s flown each on average; their ships were corrected {F(31):0} times; " +
                $"server: {measured.ShipIntents} ship intents accepted, {measured.ShipRefused} refused, {measured.Crashes} crashes; ships stepped in " +
                $"{measured.ShipMs / Math.Max(1, measured.Ticks + warmup * 30):0.00} ms a tick on average ({measured.ShipMs * 1000 / Math.Max(1, measured.ShipSteps):0.0} µs a ship step); " +
                $"the physics' broadphase was rebuilt {measured.Rebuilds} times while measured, {measured.RebuildMs / Math.Max(1, measured.Rebuilds):0.0} ms each on average " +
                $"({measured.RebuildMs / Math.Max(1, measured.Ticks):0.00} ms a tick), the longest {measured.RebuildWorstMs:0.0} ms"));
        }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Ground: {measured.Tiles} tiles and {measured.Props} props in the physics at the end (bodies made {measured.BodiesMade}, put back {measured.BodiesReused}, whole run); " +
            $"{measured.Respawns} respawns ({storms} storms){measured.Scatter}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Garbage collections in the server's process while measured: {gcAfter.Gen0 - gcBefore.Gen0} gen0, {gcAfter.Gen1 - gcBefore.Gen1} gen1, " +
            $"{gcAfter.Gen2 - gcBefore.Gen2} gen2, {(gcAfter.Paused - gcBefore.Paused).TotalMilliseconds:0} ms paused in all; " +
            $"{(gcAfter.Allocated - gcBefore.Allocated) / 1048576.0:0} MB allocated ({(gcAfter.Allocated - gcBefore.Allocated) / 1024.0 / Math.Max(1, measured.Ticks):0} KB a tick)"));
        Console.Write(measured.Profile);
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
