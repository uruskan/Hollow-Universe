using System;

namespace Hollow.Server;

/// <summary>How a server runs. The console program fills it from its command line; the game fills it for Host mode.</summary>
public sealed record ServerOptions
{
    /// <summary>UDP port; 0 picks a free one (tests).</summary>
    public int Port { get; init; } = Net.Protocol.Protocol.DefaultPort;

    public string Name { get; init; } = "Hollow development server";

    /// <summary>
    /// Where the zone's origin is on the first world, in degrees. The planet package chooses the landing site; until
    /// then this is a temperate point picked by hand.
    /// </summary>
    public double Latitude { get; init; } = 28.4;

    public double Longitude { get; init; } = -61.7;

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

    /// <summary>Every new character gets a Tin Finch beside them, full and theirs (the G6 load test's pilots).</summary>
    public bool ShipOnJoin { get; init; }

    /// <summary>The game's data (game/data/ground); null finds and loads it (GameContent.FindFolder).</summary>
    public Simulation.Content.GameContent? Content { get; init; }

    /// <summary>Where the server's lines go (the console, or Godot's output in Host mode).</summary>
    public Action<string> Log { get; init; } = Console.WriteLine;
}
