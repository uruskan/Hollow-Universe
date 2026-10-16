using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Simulation.Content;

namespace Hollow.Net.Commands;

/// <summary>What Tab completes from: the game's data, and the players the server says are on.</summary>
public sealed class CompletionSources
{
    public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Players { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LootTables { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Spawnables { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Blueprints { get; init; } = Array.Empty<string>();

    /// <summary>The ships' kinds (IRON_WAKE.md §5.6).</summary>
    public IReadOnlyList<string> Ships { get; init; } = Array.Empty<string>();

    /// <summary>The building grades by name, for <c>raidbase</c> (0 twig … 4 armoured).</summary>
    public static readonly string[] Grades = ["twig", "wood", "stone", "metal", "armoured"];
    public IReadOnlyList<string> BuildGrades { get; init; } = Grades;

    /// <summary>Everything the game's data offers, and the players on.</summary>
    public static CompletionSources From(GameContent content, IEnumerable<string> players) => new()
    {
        Items = content.Items.Select(i => i.Key).ToArray(),
        Players = players.ToArray(),
        LootTables = content.LootTables.Select(t => t.Key).ToArray(),
        Spawnables = Spawning.Kinds(content),
        Blueprints = content.Recipes.Where(r => !r.Known).Select(r => r.Output.Key).Distinct().Prepend("all").ToArray(),
        Ships = content.Ships.Select(s => s.Key).ToArray(),
        BuildGrades = content.Building.Grades.Select(g => g.Key).Prepend("off").ToArray(),
    };

    public IReadOnlyList<string> Of(ArgKind kind) => kind switch
    {
        ArgKind.Item => Items,
        ArgKind.Player => Players,
        ArgKind.LootTable => LootTables,
        ArgKind.Spawnable => Spawnables,
        ArgKind.Blueprint => Blueprints,
        ArgKind.Grade => BuildGrades,
        ArgKind.OnOff => ["on", "off"],
        ArgKind.Ship => Ships,
        ArgKind.Command => ConsoleCatalog.All.Select(c => c.Name).ToArray(),
        _ => Array.Empty<string>(),
    };
}

/// <summary>A completed line and, when the word could go more than one way, the words it could become.</summary>
public readonly record struct Completion(string Line, IReadOnlyList<string> Candidates);

/// <summary>
/// Tab completion for the F1 console (GROUND.md §8.2): the word being typed (the last one, or a new one after a space)
/// is completed from what that position of the command takes. One match completes it and adds a space; several extend
/// it to what they share and are listed.
/// </summary>
public static class ConsoleCompletion
{
    /// <summary>At most this many candidates are listed.</summary>
    public const int MaxCandidates = 48;

    public static Completion Complete(string line, CompletionSources sources)
    {
        int start = line.Length;
        while (start > 0 && !char.IsWhiteSpace(line[start - 1]))
        {
            start--;
        }
        string prefix = line[start..];
        List<string> before = ConsoleCatalog.Split(line[..start]);
        IReadOnlyList<string> pool;
        if (before.Count == 0)
        {
            // The command: names, and Rust's dotted aliases once a dot is typed.
            bool dotted = prefix.Contains('.', StringComparison.Ordinal);
            string bare = prefix.TrimStart('/');
            pool = ConsoleCatalog.All.Select(c => c.Name)
                .Concat(dotted ? ConsoleCatalog.All.SelectMany(c => c.Aliases.Where(a => a.Contains('.', StringComparison.Ordinal))) : [])
                .ToArray();
            int slash = prefix.Length - bare.Length;
            return Finish(line, start + slash, bare, pool);
        }
        if (ConsoleCatalog.Find(before[0].TrimStart('/')) is not { } command)
        {
            return new Completion(line, Array.Empty<string>());
        }
        pool = Pool(command, before.Skip(1).ToList(), prefix, sources);
        return Finish(line, start, prefix, pool);
    }

    // What the word after these ones may be: the argument it stands for, walking past optional counts the words
    // given didn't use; at an optional count, the argument after it too (a count can't be completed, a name can).
    private static IReadOnlyList<string> Pool(ConsoleCommandDef command, List<string> given, string prefix, CompletionSources sources)
    {
        int arg = 0;
        foreach (string word in given)
        {
            while (command.OptionalCount(arg) && !ConsoleCatalog.IsNumber(word))
            {
                arg++;
            }
            arg++;
        }
        // "teleport Ada <...>" and "spawn wolf 2 <...>" go on; past the last argument nothing does.
        var pool = new List<string>();
        while (arg < command.Args.Length)
        {
            pool.AddRange(sources.Of(command.Args[arg]));
            if (!command.OptionalCount(arg) || (prefix.Length > 0 && ConsoleCatalog.IsNumber(prefix)))
            {
                break;
            }
            arg++;
        }
        return pool;
    }

    private static Completion Finish(string line, int start, string prefix, IReadOnlyList<string> pool)
    {
        string[] matches = pool.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (matches.Length == 0)
        {
            return new Completion(line, Array.Empty<string>());
        }
        if (matches.Length == 1)
        {
            return new Completion(line[..start] + ConsoleCatalog.Quote(matches[0]) + " ", Array.Empty<string>());
        }
        string shared = matches[0];
        foreach (string m in matches)
        {
            int n = 0;
            while (n < shared.Length && n < m.Length && char.ToLowerInvariant(shared[n]) == char.ToLowerInvariant(m[n]))
            {
                n++;
            }
            shared = shared[..n];
        }
        string completed = shared.Length > prefix.Length ? line[..start] + shared : line;
        return new Completion(completed, matches.Take(MaxCandidates).ToArray());
    }
}

/// <summary>
/// What <c>spawn</c> can put in front of a player (GROUND.md §8.2): animals by species, the Tin Finch, the world's
/// barrels and crates and the recycler, every deployable, and a raid base.
/// </summary>
public static class Spawning
{
    /// <summary>The raid base's kind.</summary>
    public const string RaidBase = "raidbase";

    /// <summary>Plain names for the world's props, besides their model keys.</summary>
    public static readonly (string Name, string Prop)[] PropNames =
    [
        ("barrel", "loot-barrel-1"), ("fuel-barrel", "loot-barrel-2"), ("crate", "loot-crate"),
    ];

    /// <summary>Every kind, in the order the Tools tab shows them: animals, ships, props, deployables, the raid base.</summary>
    public static string[] Kinds(GameContent content) =>
        content.Animals.Select(a => a.Key)
            .Concat(content.Ships.Select(s => s.Key))
            .Concat(PropNames.Where(n => content.World.Prop(n.Prop) != null).Select(n => n.Name))
            .Concat(content.World.Props.Where(p => !PropNames.Any(n => n.Prop == p.Key)).Select(p => p.Key))
            .Concat(content.Deployables.Select(d => d.Key))
            .Append(RaidBase)
            .ToArray();

    /// <summary>A world prop by its plain name or model key, or null.</summary>
    public static PropDef? Prop(GameContent content, string kind)
    {
        foreach ((string name, string prop) in PropNames)
        {
            if (string.Equals(name, kind, StringComparison.OrdinalIgnoreCase))
            {
                return content.World.Prop(prop);
            }
        }
        return content.World.Props.FirstOrDefault(p => string.Equals(p.Key, kind, StringComparison.OrdinalIgnoreCase));
    }
}
