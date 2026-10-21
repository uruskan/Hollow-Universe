using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Hollow.Tests;

/// <summary>Marks a public static method as a test.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute
{
}

public sealed class TestFailure : Exception
{
    public TestFailure(string message) : base(message)
    {
    }
}

/// <summary>Minimal assertions; a failure throws <see cref="TestFailure"/>.</summary>
public static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new TestFailure(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new TestFailure($"{what}: expected {expected}, got {actual}");
        }
    }

    public static void Near(double expected, double actual, double tolerance, string what)
    {
        if (!(Math.Abs(expected - actual) <= tolerance))
        {
            throw new TestFailure(string.Create(CultureInfo.InvariantCulture,
                $"{what}: expected {expected} ± {tolerance}, got {actual}"));
        }
    }

    public static void InRange(double min, double max, double actual, string what)
    {
        if (!(actual >= min && actual <= max))
        {
            throw new TestFailure(string.Create(CultureInfo.InvariantCulture, $"{what}: expected {min} … {max}, got {actual}"));
        }
    }
}

/// <summary>Shared fixtures.</summary>
public static class Fixtures
{
    private static readonly Lazy<string> RepoRoot = new(() =>
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "HollowUniverse.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("Repository root (HollowUniverse.sln) not found.");
    });

    private static readonly Lazy<global::Hollow.Galaxy.StarCatalogue> CatalogueLazy = new(() =>
        global::Hollow.Galaxy.StarCatalogue.Load(Path.Combine(RepoRoot.Value, "game", "data", "catalog", "hyg41.bin.gz")));

    private static readonly Lazy<global::Hollow.Galaxy.Galaxy> GalaxyLazy = new(() => new global::Hollow.Galaxy.Galaxy(Catalogue));

    public static string Root => RepoRoot.Value;
    public static global::Hollow.Galaxy.StarCatalogue Catalogue => CatalogueLazy.Value;
    public static global::Hollow.Galaxy.Galaxy Galaxy => GalaxyLazy.Value;

    /// <summary>Prints a measured value next to the test output.</summary>
    public static void Report(string text) => Console.WriteLine("      " + text);
}

public static class Program
{
    public static int Main(string[] args)
    {
        // Every test, or those whose name contains any of the words given.
        IEnumerable<MethodInfo> discovered = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() != null);
        if (args.Length == 1 && args[0] == "--list-classes")
        {
            foreach (string name in discovered.Select(m => m.DeclaringType!.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal))
                Console.WriteLine(name);
            return 0;
        }
        string? exactClass = args.Length == 2 && args[0] == "--class" ? args[1] : null;
        List<MethodInfo> tests = discovered
            .Where(m => exactClass != null ? m.DeclaringType!.Name == exactClass
                : args.Length == 0 || args.Any(f => (m.DeclaringType!.Name + "." + m.Name).Contains(f, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => m.DeclaringType!.Name, StringComparer.Ordinal)
            .ThenBy(m => m.MetadataToken)
            .ToList();
        if (tests.Count == 0) { Console.Error.WriteLine("No matching tests."); return 2; }
        int failed = 0;
        bool reportMemory = Environment.GetEnvironmentVariable("HOLLOW_TEST_MEMORY") == "1";
        var total = Stopwatch.StartNew();
        foreach (MethodInfo test in tests)
        {
            string name = test.DeclaringType!.Name + "." + test.Name;
            var watch = Stopwatch.StartNew();
            if (reportMemory) ReportMemory(name, "before");
            try
            {
                Console.WriteLine($"  RUN  {name}");
                test.Invoke(null, null);
                Console.WriteLine($"  ok   {name} ({watch.ElapsedMilliseconds} ms)");
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                failed++;
                Exception inner = e.InnerException;
                Console.WriteLine($"  FAIL {name}: {(inner is TestFailure ? inner.Message : inner.ToString())}");
            }
            finally
            {
                if (reportMemory) ReportMemory(name, "after");
            }
        }
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed in {total.Elapsed.TotalSeconds:0.0} s.");
        return failed == 0 ? 0 : 1;
    }

    private static void ReportMemory(string test, string stage)
    {
        using Process process = Process.GetCurrentProcess();
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        Console.WriteLine("  MEMORY " + System.Text.Json.JsonSerializer.Serialize(new
        {
            test, stage, serverGc = System.Runtime.GCSettings.IsServerGC,
            workingSetMiB = process.WorkingSet64 / 1048576.0,
            privateMiB = process.PrivateMemorySize64 / 1048576.0,
            managedMiB = GC.GetTotalMemory(false) / 1048576.0,
            managedCommittedMiB = memory.TotalCommittedBytes / 1048576.0,
            gen2Collections = GC.CollectionCount(2),
        }));
    }
}
