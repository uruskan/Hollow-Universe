using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace Hollow.Server;

/// <summary>
/// The dedicated server's console program:
/// <c>dotnet run --project src/Hollow.Server -- --port 27015 --db hollow-server.db</c> (the zone at the landing site).
/// Type admin commands at the prompt (<c>help</c>); Ctrl+C or <c>quit</c> saves everyone and stops.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        // Hosted commands arrive through ChildServer's UTF-8 pipe (including degree suffixes and player names).
        // A directly opened interactive console keeps its own input code page.
        if (Console.IsInputRedirected) Console.InputEncoding = new System.Text.UTF8Encoding(false);
        Dictionary<string, string> o = ParseOptions(args);
        if (o.ContainsKey("help"))
        {
            Console.WriteLine("Hollow.Server options: --port <n> --name <text> --db <path> | --no-db --crash-dir <path> --lat <deg> --lon <deg> " +
                "--catalogue <path> --max-players <n> --relevance <m> --kit <loot table> --raid-bases <n> --raid-radius <m> --day-scale <x> --spawn beach|origin --stand-in --ground stand-in|walking --parent <pid> " +
                "--no-animals --animals <n> --animal-radius <m> --admin <name> (as often as wanted) --admins <file> (admins.txt) " +
                "--dev (everyone is an admin)\nCommands:\n" +
                GameServer.CommandHelp);
            return 0;
        }
        var options = new ServerOptions
        {
            Port = (int)Number(o, "port", Net.Protocol.Protocol.DefaultPort),
            Name = o.GetValueOrDefault("name", "Hollow development server"),
            DatabasePath = o.ContainsKey("no-db") ? null : o.GetValueOrDefault("db", ServerOptions.DefaultDatabasePath),
            Latitude = Number(o, "lat", Surface.FirstWorld.LandingLatitude),
            Longitude = Number(o, "lon", Surface.FirstWorld.LandingLongitude),
            RotationTimeScale = Math.Clamp(Number(o, "day-scale", Simulation.ZoneSpec.DefaultRotationTimeScale), 0.01, 10000),
            SpawnAtOrigin = o.GetValueOrDefault("spawn", "beach") == "origin",
            Ground = Simulation.GroundKind.Walking,
            CataloguePath = o.GetValueOrDefault("catalogue"),
            MaxPlayers = (int)Number(o, "max-players", 128),
            RelevanceRadius = (float)Number(o, "relevance", 300),
            JoinKit = o.GetValueOrDefault("kit"),
            RaidBases = (int)Number(o, "raid-bases", 0),
            RaidRadius = (float)Number(o, "raid-radius", 70),
            Animals = !o.ContainsKey("no-animals"),
            AnimalPopulation = (int)Number(o, "animals", 0),
            AnimalRadius = (float)Number(o, "animal-radius", 220),
            ReadConsole = true,
            // The F1 console's admins (GROUND.md §8.2): --admin <name> as often as wanted, the names in admins.txt (or
            // --admins <file>), or with --dev everyone.
            Admins = AllValues(args, "admin"),
            AdminsFile = System.IO.Path.GetFullPath(o.GetValueOrDefault("admins", "admins.txt")),
            EveryoneAdmin = o.ContainsKey("dev"),
        };
        // Crash reports (GROUND.md §4.3): beside the save in crashes/ (a hosted game's save is in the game's user://, so
        // its menu finds them), or --crash-dir.
        var crash = new Diagnostics.CrashLog(
            o.GetValueOrDefault("crash-dir") ?? System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(options.DatabasePath ?? ServerOptions.DefaultDatabasePath)) ?? ".", "crashes"),
            "server", typeof(Program).Assembly)
        {
            Running = () => $"the dedicated server \"{options.Name}\" on port {options.Port}",
        };
        Console.SetOut(new Diagnostics.CrashLogWriter(Console.Out, crash));
        Console.SetError(new Diagnostics.CrashLogWriter(Console.Error, crash));
        crash.HookDotNet();
        if (crash.Start() is { } unclean)
        {
            Console.WriteLine($"The last server run ended without stopping cleanly: {unclean}");
        }
        Console.WriteLine($"Hollow.Server {crash.Build}");
        GameServer created;
        try
        {
            created = new GameServer(options);
        }
        catch (Hollow.Persistence.NewerSaveException e)
        {
            // One line the hosting game shows on its menu ("error: "), and the path for whoever reads the log.
            Console.Error.WriteLine($"error: {e.Message}");
            Console.Error.WriteLine($"The save is {e.SavePath}: run the build that wrote it, or give this one another (--db <path>).");
            crash.Stop();
            return 2;
        }
        catch (Hollow.Persistence.TerrainMismatchException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            crash.Stop();
            return 2;
        }
        using GameServer server = created;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            server.RequestStop();
        };
        // Started by a game that hosts it (--parent <pid>): stop, saving everyone, when that game is gone.
        if (o.TryGetValue("parent", out string? parent) && int.TryParse(parent, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid))
        {
            new Thread(() =>
            {
                try
                {
                    using var game = System.Diagnostics.Process.GetProcessById(pid);
                    game.WaitForExit();
                }
                catch (ArgumentException)
                {
                    // Already gone.
                }
                server.RequestStop();
            })
            { IsBackground = true, Name = "Hollow parent" }.Start();
        }
        var console = new Thread(() =>
        {
            while (!server.IsStopping)
            {
                string? line = Console.ReadLine();
                if (line == null)
                {
                    // No console (input redirected and closed): keep running until stopped another way.
                    return;
                }
                server.Execute(line);
            }
        })
        { IsBackground = true, Name = "Hollow console" };
        console.Start();
        Console.WriteLine("Ready. Type help for commands.");
        server.Run();
        crash.Stop();
        return 0;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
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

    // Every value of an option given more than once ("--admin Ada --admin Bo").
    private static List<string> AllValues(string[] args, string key)
    {
        var values = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--" + key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length &&
                !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values.Add(args[++i]);
            }
            else if (args[i].StartsWith($"--{key}=", StringComparison.OrdinalIgnoreCase))
            {
                values.Add(args[i][(key.Length + 3)..]);
            }
        }
        return values;
    }

    private static double Number(Dictionary<string, string> o, string key, double fallback) =>
        o.TryGetValue(key, out string? v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;
}
