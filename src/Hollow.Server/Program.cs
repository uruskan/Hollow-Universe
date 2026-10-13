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
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Dictionary<string, string> o = ParseOptions(args);
        if (o.ContainsKey("help"))
        {
            Console.WriteLine("Hollow.Server options: --port <n> --name <text> --db <path> | --no-db --lat <deg> --lon <deg> " +
                "--catalogue <path> --max-players <n> --relevance <m> --kit <loot table> --day-scale <x> --spawn beach|origin --stand-in --parent <pid>\nCommands:\n" +
                GameServer.CommandHelp);
            return 0;
        }
        var options = new ServerOptions
        {
            Port = (int)Number(o, "port", Net.Protocol.Protocol.DefaultPort),
            Name = o.GetValueOrDefault("name", "Hollow development server"),
            DatabasePath = o.ContainsKey("no-db") ? null : o.GetValueOrDefault("db", "hollow-server.db"),
            Latitude = Number(o, "lat", Surface.FirstWorld.LandingLatitude),
            Longitude = Number(o, "lon", Surface.FirstWorld.LandingLongitude),
            RotationTimeScale = Math.Clamp(Number(o, "day-scale", Simulation.ZoneSpec.DefaultRotationTimeScale), 0.01, 10000),
            SpawnAtOrigin = o.GetValueOrDefault("spawn", "beach") == "origin",
            StandIn = o.ContainsKey("stand-in"),
            CataloguePath = o.GetValueOrDefault("catalogue"),
            MaxPlayers = (int)Number(o, "max-players", 128),
            RelevanceRadius = (float)Number(o, "relevance", 300),
            JoinKit = o.GetValueOrDefault("kit"),
            ReadConsole = true,
        };
        using var server = new GameServer(options);
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

    private static double Number(Dictionary<string, string> o, string key, double fallback) =>
        o.TryGetValue(key, out string? v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;
}
