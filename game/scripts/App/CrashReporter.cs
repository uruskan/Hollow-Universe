using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Godot;
using Hollow.Server.Diagnostics;

namespace HollowUniverse.App;

/// <summary>
/// The game's crash reports (GROUND.md §4.3), in <c>user://crashes</c>: .NET's unhandled and unobserved exceptions,
/// the exceptions Godot catches in scripts (the game goes on; the first three of a run are reported), and a marker that
/// tells the next start a run died without one (a native crash, or a freeze that was killed). Every line Godot logs is
/// kept for the reports. The main menu shows <see cref="Notice"/>: the newest report it has not shown yet.
/// </summary>
public static class CrashReporter
{
    private const string SeenFile = "seen.txt";

    private static CrashLog? _log;
    private static readonly HashSet<string> Reported = new();

    /// <summary>The reports' folder.</summary>
    public static string Folder => ProjectSettings.GlobalizePath("user://crashes");

    /// <summary>What the game is doing, for a report (Main sets the view; a game its role).</summary>
    public static string Running { get; set; } = "starting";

    /// <summary>A report the menu has not shown yet: its kind and path; null when there is none (or it was dismissed).</summary>
    public static (string Kind, string Path)? Notice { get; private set; }

    /// <summary>Hides the menu's notice.</summary>
    public static void Dismiss() => Notice = null;

    /// <summary>Starts the reports, first thing at start.</summary>
    public static void Install()
    {
        if (_log != null)
        {
            return;
        }
        _log = new CrashLog(Folder, "game", typeof(CrashReporter).Assembly) { Running = () => Running };
        _log.HookDotNet();
        OS.AddLogger(new CrashLogger(_log));
        // Reports since the menu last showed one, this start's own (a run that died without one) among them.
        DateTime seen = DateTime.MinValue;
        string seenPath = Path.Combine(Folder, SeenFile);
        try
        {
            if (File.Exists(seenPath))
            {
                seen = File.GetLastWriteTimeUtc(seenPath);
            }
        }
        catch (Exception)
        {
            // Then every report is new.
        }
        _log.Start();
        (string Path, string Kind)? newest = CrashLog.Newest(Folder, seen);
        if (newest is { } report)
        {
            Notice = (report.Kind, report.Path);
        }
        try
        {
            File.WriteAllText(seenPath, newest?.Path ?? "");
        }
        catch (Exception)
        {
            // Shown again next time.
        }
        GD.Print($"Hollow Universe {_log.Build}");
    }

    /// <summary>A clean exit: the marker goes.</summary>
    public static void Stop() => _log?.Stop();

    /// <summary>A script exception Godot caught and logged: reported, the first few of a run, once each.</summary>
    internal static void ScriptException(string text)
    {
        if (_log == null)
        {
            return;
        }
        lock (Reported)
        {
            string key = text.Length > 200 ? text[..200] : text;
            if (Reported.Count >= 3 || !Reported.Add(key))
            {
                return;
            }
        }
        _log.Write("an exception in a game script (the game went on)", null, text);
    }
}

/// <summary>Every line Godot logs, kept for the crash reports; an error carrying a .NET exception is reported.</summary>
internal sealed partial class CrashLogger : Logger
{
    private static readonly Regex DotNetException = new(@"\b[A-Z]\w*Exception\b", RegexOptions.Compiled);
    private readonly CrashLog _log;

    public CrashLogger(CrashLog log) => _log = log;

    public CrashLogger() => _log = null!;

    public override void _LogMessage(string message, bool error) => _log.Line(message);

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType,
        Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        string text = $"ERROR: {(rationale.Length > 0 ? rationale : code)}\n   at: {function} ({file}:{line})";
        if (rationale.Length > 0 && code.Length > 0)
        {
            text += "\n" + code;
        }
        _log.Line(text);
        if (DotNetException.IsMatch(code) || DotNetException.IsMatch(rationale))
        {
            CrashReporter.ScriptException(text);
        }
    }
}
