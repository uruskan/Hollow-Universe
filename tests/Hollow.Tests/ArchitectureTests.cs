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
            typeof(Persistence.GameDatabase).Assembly,
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
        foreach (string folder in new[] { "Hollow.Core", "Hollow.Galaxy", "Hollow.Surface" })
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
