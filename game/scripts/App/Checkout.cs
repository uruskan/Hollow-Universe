using System;
using System.Linq;
using Godot;

namespace HollowUniverse.App;

/// <summary>
/// The repository this game runs from (the folder above game/): its branch and commit, read from git's own files. For bug
/// reports (the F1 console's INFO tab), and so that each branch keeps its own hosted save (GROUND.md §4.3).
/// </summary>
public static class Checkout
{
    private static (string? Branch, string? Commit)? _head;

    /// <summary>The branch checked out; null when it can't be read or no branch is (a detached commit).</summary>
    public static string? Branch => Head.Branch;

    /// <summary>The commit checked out, its first seven characters; null when it can't be read.</summary>
    public static string? Commit => Head.Commit;

    /// <summary>
    /// The hosted save is isolated by branch and Earth terrain revision. Height or solid-cliff changes start a new world without
    /// opening or overwriting the previous map's characters and buildings.
    /// </summary>
    public static string HostedSave => Branch is { } branch && branch != "main"
        ? $"user://hollow-host-{string.Concat(branch.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-'))}-earth-v{Hollow.Surface.WalkingParameters.Earth.TerrainRevision}.db"
        : $"user://hollow-host-earth-v{Hollow.Surface.WalkingParameters.Earth.TerrainRevision}.db";

    private static (string? Branch, string? Commit) Head => _head ??= Read();

    private static (string? Branch, string? Commit) Read()
    {
        try
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
            string git = System.IO.Path.Combine(root, ".git");
            // A worktree's .git is a file naming its folder in the main repository.
            string dir = System.IO.File.Exists(git) ? System.IO.File.ReadAllText(git).Replace("gitdir:", "", StringComparison.Ordinal).Trim() : git;
            string head = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "HEAD")).Trim();
            string common = System.IO.File.Exists(System.IO.Path.Combine(dir, "commondir"))
                ? System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "commondir")).Trim()))
                : dir;
            if (!head.StartsWith("ref: ", StringComparison.Ordinal))
            {
                return (null, head.Length >= 7 ? head[..7] : head);
            }
            string reference = head[5..];
            string file = System.IO.Path.Combine(common, reference.Replace('/', System.IO.Path.DirectorySeparatorChar));
            string hash = System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file).Trim()
                : System.IO.File.ReadLines(System.IO.Path.Combine(common, "packed-refs")).FirstOrDefault(l => l.EndsWith(" " + reference, StringComparison.Ordinal))?.Split(' ')[0] ?? "";
            return (reference.Replace("refs/heads/", "", StringComparison.Ordinal), hash.Length >= 7 ? hash[..7] : null);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, null);
        }
    }
}
