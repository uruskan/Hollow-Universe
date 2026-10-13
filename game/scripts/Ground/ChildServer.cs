using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace HollowUniverse.Ground;

/// <summary>
/// A game hosted on this computer in a process of its own (GROUND.md §4.3): the dedicated server (Hollow.Server, built
/// with the solution) started on localhost with this game as its parent, so its garbage collections and ticks never
/// stall the game's frames, nor the game's its ticks. Admin commands go to its console; asked to stop, it saves
/// everyone and quits; if the game goes away without asking, it stops by itself (<c>--parent</c>).
/// </summary>
public sealed class ChildServer : IDisposable
{
    private readonly Process _process;
    private readonly ConcurrentQueue<string> _lines = new();
    private volatile bool _ready;
    private volatile bool _portTaken;

    private ChildServer(Process process, int port)
    {
        _process = process;
        Port = port;
    }

    public int Port { get; }

    /// <summary>It listens: the game can connect.</summary>
    public bool Ready => _ready;

    /// <summary>It stopped (by itself: an error, or the port was taken).</summary>
    public bool Exited => _process.HasExited;

    /// <summary>It couldn't listen: another game hosts on the port here.</summary>
    public bool PortTaken => _portTaken;

    /// <summary>
    /// The server's program in the repository (<paramref name="repository"/>: the folder above game/): the Release
    /// build if it is at least as new as the Debug one, else the Debug build; null when neither is built.
    /// </summary>
    public static string? Find(string repository)
    {
        string? best = null;
        DateTime newest = DateTime.MinValue;
        foreach (string configuration in new[] { "Release", "Debug" })
        {
            string dll = Path.Combine(repository, "src", "Hollow.Server", "bin", configuration, "net10.0", "Hollow.Server.dll");
            if (File.Exists(dll) && File.GetLastWriteTimeUtc(dll) > newest)
            {
                newest = File.GetLastWriteTimeUtc(dll);
                best = dll;
            }
        }
        return best;
    }

    /// <summary>
    /// Starts the server; it is <see cref="Ready"/> once its world is built (a few seconds). <paramref name="admin"/>, the
    /// hosting player, may run the F1 console's admin commands (GROUND.md §8.2).
    /// </summary>
    public static ChildServer Start(string serverDll, string repository, int port, string database, string name, bool spawnAtOrigin, string admin)
    {
        string exe = Path.ChangeExtension(serverDll, OperatingSystem.IsWindows() ? ".exe" : null);
        var info = new ProcessStartInfo
        {
            FileName = File.Exists(exe) ? exe : "dotnet",
            WorkingDirectory = repository,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (!File.Exists(exe))
        {
            info.ArgumentList.Add(serverDll);
        }
        foreach (string a in new[]
                 {
                     "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--db", database, "--name", name,
                     "--spawn", spawnAtOrigin ? "origin" : "beach", "--parent", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "--admin", admin,
                 })
        {
            info.ArgumentList.Add(a);
        }
        Process process = Process.Start(info) ?? throw new InvalidOperationException("The server didn't start.");
        var child = new ChildServer(process, port);
        process.OutputDataReceived += (_, e) => child.Line(e.Data);
        process.ErrorDataReceived += (_, e) => child.Line(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return child;
    }

    private void Line(string? line)
    {
        if (line == null)
        {
            return;
        }
        _lines.Enqueue(line);
        if (line.StartsWith("Ready.", StringComparison.Ordinal))
        {
            _ready = true;
        }
        // The socket refused: "Only one usage of each socket address" (Windows, 10048), "Address already in use".
        if (line.Contains("10048", StringComparison.Ordinal) || line.Contains("already in use", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase))
        {
            _portTaken = true;
        }
    }

    /// <summary>What the server printed since the last call (the game puts it in its own log).</summary>
    public IEnumerable<string> TakeLines()
    {
        while (_lines.TryDequeue(out string? line))
        {
            yield return line;
        }
    }

    /// <summary>An admin command, as typed at the server's console.</summary>
    public void Execute(string command)
    {
        if (!_process.HasExited)
        {
            _process.StandardInput.WriteLine(command);
            _process.StandardInput.Flush();
        }
    }

    /// <summary>Saves everyone and stops the server (waits up to five seconds, then ends it).</summary>
    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                Execute("quit");
                if (!_process.WaitForExit(5000))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        _process.Dispose();
    }
}
