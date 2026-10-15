using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Hollow.Server.Diagnostics;

/// <summary>
/// Crash reports (GROUND.md §4.3), for the game and the dedicated server alike, without the engine: the last
/// <see cref="Kept"/> lines of the log, a report written as <c>crash-&lt;UTC time&gt;-&lt;program&gt;.txt</c> when an
/// exception goes unhandled (the exception, the log, the build and commit, what was running), and a "still running"
/// marker per process, cleared on a clean exit: a marker whose process is gone at the next start means that run died
/// without an exception (a native crash, or a freeze that was killed), and a report says so.
/// </summary>
public sealed class CrashLog
{
    /// <summary>Lines of the log a report carries.</summary>
    public const int Kept = 200;

    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();
    private readonly string _program;
    private readonly string _marker;
    private readonly DateTime _started = DateTime.UtcNow;
    private readonly string _build;

    /// <param name="directory">Where reports and markers go.</param>
    /// <param name="program">"game" or "server": in the files' names.</param>
    /// <param name="assembly">The program's own assembly, for its build and commit.</param>
    public CrashLog(string directory, string program, Assembly assembly)
    {
        Directory = directory;
        _program = program;
        _marker = Path.Combine(directory, $"{program}-running-{Environment.ProcessId}.txt");
        _build = Version(assembly);
    }

    /// <summary>The folder the reports are in.</summary>
    public string Directory { get; }

    /// <summary>What the program is doing, for a report ("hosting on port 27015", a view's name).</summary>
    public Func<string>? Running { get; set; }

    /// <summary>The build: the assembly's version and the commit it was built from, when known.</summary>
    public string Build => _build;

    /// <summary>Keeps a line (or several) of the log; any thread.</summary>
    public void Line(string text)
    {
        lock (_gate)
        {
            foreach (string line in text.Replace("\r", "", StringComparison.Ordinal).TrimEnd('\n').Split('\n'))
            {
                _lines.Enqueue(line);
                while (_lines.Count > Kept)
                {
                    _lines.Dequeue();
                }
            }
        }
    }

    /// <summary>Catches what .NET would otherwise lose: an exception no code caught, and a faulted task nobody looked at.</summary>
    public void HookDotNet()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write(e.IsTerminating ? "crash: an unhandled exception" : "an unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("an exception in a task nobody waited for (the program went on)", e.Exception);
    }

    /// <summary>Writes a report; returns its path, or null when it could not be written.</summary>
    public string? Write(string kind, Exception? exception, string? note = null)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            DateTime now = DateTime.UtcNow;
            string stem = $"crash-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{_program}";
            string path = Path.Combine(Directory, stem + ".txt");
            for (int n = 2; File.Exists(path); n++)
            {
                path = Path.Combine(Directory, $"{stem}-{n}.txt");
            }
            var text = new StringBuilder();
            text.Append("Hollow Universe ").Append(_program).Append(": ").AppendLine(kind);
            text.Append("When (UTC): ").AppendLine(now.ToString("u", CultureInfo.InvariantCulture));
            text.Append("Build: ").AppendLine(_build);
            text.Append("Started (UTC): ").AppendLine(_started.ToString("u", CultureInfo.InvariantCulture));
            text.Append("Process: ").Append(Environment.ProcessId.ToString(CultureInfo.InvariantCulture))
                .Append(", ").Append(Environment.OSVersion).Append(", .NET ").AppendLine(Environment.Version.ToString());
            text.Append("Command line: ").AppendLine(Environment.CommandLine);
            string running = "";
            try
            {
                running = Running?.Invoke() ?? "";
            }
            catch (Exception e)
            {
                running = $"(unknown: {e.GetType().Name})";
            }
            text.Append("Running: ").AppendLine(running);
            if (note != null)
            {
                text.AppendLine().AppendLine(note);
            }
            if (exception != null)
            {
                text.AppendLine().AppendLine("Exception:").AppendLine(exception.ToString());
            }
            string[] lines;
            lock (_gate)
            {
                lines = _lines.ToArray();
            }
            text.AppendLine().Append("The last ").Append(lines.Length.ToString(CultureInfo.InvariantCulture)).AppendLine(" lines of the log:");
            foreach (string line in lines)
            {
                text.AppendLine(line);
            }
            File.WriteAllText(path, text.ToString());
            return path;
        }
        catch (Exception)
        {
            // A report that cannot be written must never become a second crash.
            return null;
        }
    }

    /// <summary>
    /// At start: a marker left by a run of this program whose process is gone becomes a report (that run died without
    /// an exception); then this run's marker is written. Returns the first such report's path, or null.
    /// </summary>
    public string? Start()
    {
        string? found = null;
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            foreach (string marker in System.IO.Directory.EnumerateFiles(Directory, $"{_program}-running-*.txt"))
            {
                if (Alive(marker))
                {
                    continue;
                }
                string left = File.ReadAllText(marker);
                string? report = Write("crash: the last run ended without closing (a native crash, or a freeze that was killed)", null,
                    "That run's marker was still there at this start; it said:\n" + left.TrimEnd() +
                    "\n\nThe log below is this run's, not that one's: the engine's own log (user://logs) holds the last run's.");
                found ??= report;
                File.Delete(marker);
            }
            File.WriteAllText(_marker,
                $"Started (UTC): {_started.ToString("u", CultureInfo.InvariantCulture)}\nProcess: {Environment.ProcessId}\nBuild: {_build}\nCommand line: {Environment.CommandLine}\n");
        }
        catch (Exception)
        {
            // No marker: the next start simply cannot tell.
        }
        return found;
    }

    /// <summary>A clean exit: clears this run's marker.</summary>
    public void Stop()
    {
        try
        {
            File.Delete(_marker);
        }
        catch (Exception)
        {
            // Nothing to do.
        }
    }

    // Whether the process a marker names still runs (another copy of the program on this computer).
    private static bool Alive(string marker)
    {
        string name = Path.GetFileNameWithoutExtension(marker);
        if (!int.TryParse(name[(name.LastIndexOf('-') + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) ||
            pid == Environment.ProcessId)
        {
            return false;
        }
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The newest report in <paramref name="directory"/> newer than <paramref name="since"/> (UTC), and its first line.</summary>
    public static (string Path, string Kind)? Newest(string directory, DateTime since)
    {
        try
        {
            FileInfo? newest = new DirectoryInfo(directory).EnumerateFiles("crash-*.txt")
                .Where(f => f.LastWriteTimeUtc > since)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest == null)
            {
                return null;
            }
            string first = File.ReadLines(newest.FullName).FirstOrDefault() ?? "";
            int colon = first.IndexOf(": ", StringComparison.Ordinal);
            return (newest.FullName, colon >= 0 ? first[(colon + 2)..] : first);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The assembly's informational version, with the commit from the repository it runs in when the build did not record one.</summary>
    public static string Version(Assembly assembly)
    {
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "unknown";
        if (version.Contains('+', StringComparison.Ordinal))
        {
            return version;
        }
        string? commit = GitCommit(AppContext.BaseDirectory) ?? GitCommit(System.IO.Directory.GetCurrentDirectory());
        return commit != null ? $"{version} (commit {commit})" : $"{version} (commit unknown)";
    }

    // The commit checked out in the repository above a folder (a worktree's too), read from .git without git.
    private static string? GitCommit(string from)
    {
        try
        {
            for (DirectoryInfo? dir = new(from); dir != null; dir = dir.Parent)
            {
                string dotGit = Path.Combine(dir.FullName, ".git");
                string? gitDir = System.IO.Directory.Exists(dotGit) ? dotGit
                    : File.Exists(dotGit) && File.ReadAllText(dotGit).Trim() is { } link && link.StartsWith("gitdir:", StringComparison.Ordinal)
                        ? Path.GetFullPath(Path.Combine(dir.FullName, link[7..].Trim()))
                        : null;
                if (gitDir == null)
                {
                    continue;
                }
                string head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
                if (!head.StartsWith("ref:", StringComparison.Ordinal))
                {
                    return Short(head);
                }
                string reference = head[4..].Trim();
                string common = File.Exists(Path.Combine(gitDir, "commondir"))
                    ? Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(Path.Combine(gitDir, "commondir")).Trim()))
                    : gitDir;
                foreach (string root in new[] { gitDir, common })
                {
                    string file = Path.Combine(root, reference.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(file))
                    {
                        return $"{Short(File.ReadAllText(file).Trim())} on {reference.Replace("refs/heads/", "", StringComparison.Ordinal)}";
                    }
                }
                string packed = Path.Combine(common, "packed-refs");
                if (File.Exists(packed))
                {
                    string? line = File.ReadLines(packed).FirstOrDefault(l => l.EndsWith(" " + reference, StringComparison.Ordinal));
                    if (line != null)
                    {
                        return $"{Short(line[..line.IndexOf(' ', StringComparison.Ordinal)])} on {reference.Replace("refs/heads/", "", StringComparison.Ordinal)}";
                    }
                }
                return null;
            }
        }
        catch (Exception)
        {
            // Unknown.
        }
        return null;
    }

    private static string Short(string sha) => sha.Length > 10 ? sha[..10] : sha;
}

/// <summary>A console writer that also keeps every line for the crash log (the dedicated server's output).</summary>
public sealed class CrashLogWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly CrashLog _log;
    private readonly StringBuilder _line = new();

    public CrashLogWriter(TextWriter inner, CrashLog log)
    {
        _inner = inner;
        _log = log;
    }

    public override Encoding Encoding => _inner.Encoding;

    public override void Write(char value)
    {
        _inner.Write(value);
        lock (_line)
        {
            if (value == '\n')
            {
                _log.Line(_line.ToString());
                _line.Clear();
            }
            else if (value != '\r')
            {
                _line.Append(value);
            }
        }
    }

    public override void Write(string? value)
    {
        if (value == null)
        {
            return;
        }
        _inner.Write(value);
        lock (_line)
        {
            foreach (char c in value)
            {
                if (c == '\n')
                {
                    _log.Line(_line.ToString());
                    _line.Clear();
                }
                else if (c != '\r')
                {
                    _line.Append(c);
                }
            }
        }
    }

    public override void WriteLine(string? value)
    {
        Write(value);
        Write('\n');
    }

    public override void Flush() => _inner.Flush();
}
