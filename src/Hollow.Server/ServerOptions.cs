using System;

namespace Hollow.Server;

/// <summary>How a server runs. The console program fills it from its command line; the game fills it for Host mode.</summary>
public sealed record ServerOptions
{
    /// <summary>UDP port; 0 picks a free one (tests).</summary>
    public int Port { get; init; } = Net.Protocol.Protocol.DefaultPort;

    public string Name { get; init; } = "Hollow development server";

    /// <summary>Where the zone's origin is on the first world, in degrees: by default the landing site (GROUND.md §3).</summary>
    public double Latitude { get; init; } = Surface.FirstWorld.LandingLatitude;

    public double Longitude { get; init; } = Surface.FirstWorld.LandingLongitude;

    /// <summary>The stand-in ground instead of the planet (tests and benchmarks that want a plain world).</summary>
    public bool StandIn { get; init; }

    /// <summary>How much faster than real the planet turns (GROUND.md §1; 1 = its real day).</summary>
    public double RotationTimeScale { get; init; } = Simulation.ZoneSpec.DefaultRotationTimeScale;

    /// <summary>
    /// New characters arrive at the zone's origin (the landing site) instead of on a beach, as in Rust: for load tests
    /// and measurements that want everyone in one place.
    /// </summary>
    public bool SpawnAtOrigin { get; init; }

    /// <summary>The SQLite database; null runs without persistence (characters start fresh every time).</summary>
    public string? DatabasePath { get; init; } = "hollow-server.db";

    /// <summary>The real-star catalogue; the first world doesn't need it, so it is optional.</summary>
    public string? CataloguePath { get; init; }

    public int MaxPlayers { get; init; } = 128;

    /// <summary>Entities within this many metres of a player are sent to them (GROUND.md §4.4).</summary>
    public float RelevanceRadius { get; init; } = 300f;

    /// <summary>Characters are saved this often, besides on disconnect and shutdown.</summary>
    public double SaveIntervalSeconds { get; init; } = 30;

    /// <summary>Read admin commands from the console's standard input (the console program).</summary>
    public bool ReadConsole { get; init; }

    /// <summary>A loot table every new character also gets (the load test's full inventories); null: none.</summary>
    public string? JoinKit { get; init; }

    /// <summary>Gives <see cref="JoinKit"/> with every new life too, not only to new characters (the G4 load test's firefights).</summary>
    public bool KitEveryLife { get; init; }

    /// <summary>The game's data (game/data/ground); null finds and loads it (GameContent.FindFolder).</summary>
    public Simulation.Content.GameContent? Content { get; init; }

    /// <summary>Where the server's lines go (the console, or Godot's output in Host mode).</summary>
    public Action<string> Log { get; init; } = Console.WriteLine;
}
