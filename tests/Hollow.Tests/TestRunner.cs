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
        string filter = args.Length > 0 ? args[0] : "";
        List<MethodInfo> tests = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
            .Where(m => (m.DeclaringType!.Name + "." + m.Name).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.DeclaringType!.Name, StringComparer.Ordinal)
            .ThenBy(m => m.MetadataToken)
            .ToList();
        int failed = 0;
        var total = Stopwatch.StartNew();
        foreach (MethodInfo test in tests)
        {
            string name = test.DeclaringType!.Name + "." + test.Name;
            var watch = Stopwatch.StartNew();
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
        }
        Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed in {total.Elapsed.TotalSeconds:0.0} s.");
        return failed == 0 ? 0 : 1;
    }
}
