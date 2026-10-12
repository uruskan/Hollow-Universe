using System;
using System.IO;

namespace Hollow.NetGen;

/// <summary>
/// Regenerates <c>src/Hollow.Net/Protocol/Messages.g.cs</c> from <c>Messages.cs</c> (ARCHITECTURE.md §11.2).
/// <c>--check</c> only reports whether the file is up to date (exit code 1 if not).
/// </summary>
public static class Program
{
    public const string DefinitionsPath = "src/Hollow.Net/Protocol/Messages.cs";
    public const string GeneratedPath = "src/Hollow.Net/Protocol/Messages.g.cs";

    public static int Main(string[] args)
    {
        bool check = Array.IndexOf(args, "--check") >= 0;
        string root = FindRoot();
        string definitions = Path.Combine(root, DefinitionsPath);
        string generated = Path.Combine(root, GeneratedPath);
        string text;
        try
        {
            text = NetGenerator.Generate(File.ReadAllText(definitions));
        }
        catch (NetGenException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
        string current = File.Exists(generated) ? File.ReadAllText(generated) : "";
        if (current == text)
        {
            Console.WriteLine($"{GeneratedPath} is up to date.");
            return 0;
        }
        if (check)
        {
            Console.WriteLine($"{GeneratedPath} is out of date: run dotnet run --project tools/Hollow.NetGen");
            return 1;
        }
        File.WriteAllText(generated, text);
        Console.WriteLine($"Wrote {GeneratedPath}.");
        return 0;
    }

    public static string FindRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "HollowUniverse.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("Repository root (HollowUniverse.sln) not found.");
    }
}
