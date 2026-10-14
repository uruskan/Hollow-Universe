using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Hollow.Core;

namespace Hollow.Tests;

public static class ArchitectureTests
{
    /// <summary>ARCHITECTURE.md §4: nothing in src/ may depend on Godot.</summary>
    [Test]
    public static void EngineFreeLibrariesDoNotReferenceGodot()
    {
        Assembly[] libraries =
        {
            typeof(DMath).Assembly, typeof(Galaxy.Galaxy).Assembly, typeof(Surface.ISurface).Assembly,
            typeof(Simulation.Movement.CharacterMotor).Assembly, typeof(Net.Protocol.Protocol).Assembly,
            typeof(Persistence.GameDatabase).Assembly, typeof(Client.GameClient).Assembly, typeof(Server.GameServer).Assembly,
        };
        foreach (Assembly library in libraries)
        {
            string[] godot = library.GetReferencedAssemblies()
                .Select(a => a.Name ?? "")
                .Where(n => n.Contains("Godot", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.True(godot.Length == 0, $"{library.GetName().Name} references {string.Join(", ", godot)}");
        }
    }

    // Platform maths (results may differ in the last bit between machines), also as method groups such as
    // Select(Math.Log); unseeded randomness; clocks and timers.
    private static readonly Regex Banned = new(
        @"(?<![\w.])(System\.)?(Math|MathF|double|float|Double|Single)\.(Exp|Exp2|Exp10|ExpM1|Log|Log2|Log10|LogP1|Pow|" +
        @"Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Sinh|Cosh|Tanh|Asinh|Acosh|Atanh|Cbrt|Hypot|RootN|SinCos|SinPi|CosPi|TanPi|" +
        @"ReciprocalEstimate|ReciprocalSqrtEstimate)\b" +
        @"|(?<!\w)(Random|RandomNumberGenerator|Stopwatch)(?!\w)|DateTime(Offset)?\.(Now|UtcNow|Today)" +
        @"|Environment\.TickCount|Guid\.NewGuid",
        RegexOptions.Compiled);

    /// <summary>
    /// ARCHITECTURE.md §8: generation code uses only deterministic maths (DMath), seeded randomness (Rng, Hash64)
    /// and no clocks. HashCode and GetHashCode() are randomised per process, so they may appear only inside
    /// GetHashCode overrides (for dictionaries), never in anything generated.
    /// </summary>
    [Test]
    public static void GenerationCodeUsesOnlyDeterministicApis()
    {
        var problems = new List<string>();
        int files = 0;
        // Natural objects and the world's loot spots are generated too (the same every time), and the map's picture (G7).
        // Animals spawn from the planet and their AI is replayed from the same inputs (G7): deterministic too.
        foreach (string folder in new[] { "Hollow.Core", "Hollow.Galaxy", "Hollow.Surface", "Hollow.Simulation/Nature", "Hollow.Client/Map",
                     "Hollow.Simulation/Animals" })
        {
            string root = Path.Combine(Fixtures.Root, "src", folder);
            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(Fixtures.Root, file).Replace('\\', '/');
                if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
                {
                    continue;
                }
                files++;
                // The universe clock is the one place that reads the wall clock.
                bool clock = relative.EndsWith("/UniverseTime.cs", StringComparison.Ordinal);
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = CodeOnly(lines[i]);
                    bool banned = Banned.Matches(code).Any(m => !(clock && m.Value.StartsWith("DateTime.UtcNow", StringComparison.Ordinal)));
                    bool hashCode = code.Contains("HashCode", StringComparison.Ordinal)
                        && !code.Contains("override int GetHashCode()", StringComparison.Ordinal);
                    if (banned || hashCode)
                    {
                        problems.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }
        Fixtures.Report($"{files} source files checked");
        Assert.True(files > 10, "found the sources");
        Assert.True(problems.Count == 0, "non-deterministic API in generation code:\n        " + string.Join("\n        ", problems));
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §12.2, K6: the zone is a host and the rules are its systems. No partial file of Zone is left,
    /// every system is in GroundSystems' lists exactly once (completeness, as a registry), and the host stays small.
    /// </summary>
    [Test]
    public static void TheZoneIsAHost()
    {
        string simulation = Path.Combine(Fixtures.Root, "src", "Hollow.Simulation");
        string[] partials = Directory.GetFiles(simulation, "Zone.*.cs");
        Assert.True(partials.Length == 0, $"Zone has partial files again: {string.Join(", ", partials.Select(Path.GetFileName))}");
        Type[] systems = typeof(Simulation.ZoneSystem).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Simulation.ZoneSystem).IsAssignableFrom(t))
            .ToArray();
        Simulation.ZoneSystem[] game = Simulation.GroundSystems.Game();
        List<Type> listed = game.Concat(Simulation.GroundSystems.MovementOnly()).Select(s => s.GetType()).ToList();
        string[] missing = systems.Where(s => !listed.Contains(s)).Select(s => s.Name).ToArray();
        Assert.True(missing.Length == 0, $"systems in no list: {string.Join(", ", missing)}");
        string[] twice = game.GroupBy(s => s.GetType()).Where(g => g.Count() > 1).Select(g => g.Key.Name).ToArray();
        Assert.True(twice.Length == 0, $"systems listed twice: {string.Join(", ", twice)}");
        int hostLines = File.ReadAllLines(Path.Combine(simulation, "Zone.cs")).Length;
        Assert.True(hostLines < 450, $"Zone.cs has {hostLines} lines: rules are creeping back into the host");
        Fixtures.Report($"{systems.Length} systems, {game.Length} in the game's list; the host is {hostLines} lines");
    }

    /// <summary>
    /// GROUND_SYSTEMS.md §12.5, K10: the server is a host and its concerns are modules. No partial file of GameServer is
    /// left, every module is in ServerModules' list exactly once (completeness, as a registry), and the host stays small.
    /// </summary>
    [Test]
    public static void TheServerIsAHost()
    {
        string server = Path.Combine(Fixtures.Root, "src", "Hollow.Server");
        string[] partials = Directory.GetFiles(server, "GameServer.*.cs");
        Assert.True(partials.Length == 0, $"GameServer has partial files again: {string.Join(", ", partials.Select(Path.GetFileName))}");
        Type[] modules = typeof(Server.ServerModule).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Server.ServerModule).IsAssignableFrom(t))
            .ToArray();
        List<Type> listed = Server.ServerModules.All().Select(m => m.GetType()).ToList();
        string[] missing = modules.Where(m => !listed.Contains(m)).Select(m => m.Name).ToArray();
        Assert.True(missing.Length == 0, $"modules not in the list: {string.Join(", ", missing)}");
        string[] twice = listed.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key.Name).ToArray();
        Assert.True(twice.Length == 0, $"modules listed twice: {string.Join(", ", twice)}");
        int hostLines = File.ReadAllLines(Path.Combine(server, "GameServer.cs")).Length;
        Assert.True(hostLines < 1100, $"GameServer.cs has {hostLines} lines: concerns are creeping back into the host");
        Fixtures.Report($"{modules.Length} modules, {listed.Count} in the list; the host is {hostLines} lines");
    }
    // The line with string and character literals blanked and any // comment removed, so text inside strings can
    // neither hide a banned call nor look like one. (Literals spanning lines are rare enough to ignore.)
    private static string CodeOnly(string line)
    {
        var code = new System.Text.StringBuilder(line.Length);
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;
            }
            if (c is '"' or '\'')
            {
                bool verbatim = c == '"' && i > 0 && line[i - 1] == '@';
                int j = i + 1;
                while (j < line.Length)
                {
                    if (!verbatim && line[j] == '\\')
                    {
                        j += 2;
                        continue;
                    }
                    if (line[j] == c)
                    {
                        if (verbatim && j + 1 < line.Length && line[j + 1] == '"')
                        {
                            j += 2;
                            continue;
                        }
                        break;
                    }
                    j++;
                }
                code.Append(' ', Math.Min(j, line.Length - 1) - i + 1);
                i = j;
                continue;
            }
            code.Append(c);
        }
        return code.ToString();
    }
}
