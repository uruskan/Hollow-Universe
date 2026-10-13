using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace Hollow.Server;

/// <summary>
/// The dedicated server's console program:
/// <c>dotnet run --project src/Hollow.Server -- --port 27015 --db hollow-server.db --lat 28.4 --lon -61.7</c>.
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
                "--catalogue <path> --max-players <n> --relevance <m> --kit <loot table> --raid-bases <n> --raid-radius <m>\nCommands:\n" +
                GameServer.CommandHelp);
            return 0;
        }
        var options = new ServerOptions
        {
            Port = (int)Number(o, "port", Net.Protocol.Protocol.DefaultPort),
            Name = o.GetValueOrDefault("name", "Hollow development server"),
            DatabasePath = o.ContainsKey("no-db") ? null : o.GetValueOrDefault("db", "hollow-server.db"),
            Latitude = Number(o, "lat", 28.4),
            Longitude = Number(o, "lon", -61.7),
            CataloguePath = o.GetValueOrDefault("catalogue"),
            MaxPlayers = (int)Number(o, "max-players", 128),
            RelevanceRadius = (float)Number(o, "relevance", 300),
            JoinKit = o.GetValueOrDefault("kit"),
            RaidBases = (int)Number(o, "raid-bases", 0),
            RaidRadius = (float)Number(o, "raid-radius", 70),
            ReadConsole = true,
        };
        using var server = new GameServer(options);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            server.RequestStop();
        };
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
