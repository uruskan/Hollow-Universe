using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Hollow.Client;
using Hollow.Net.Commands;

namespace HollowUniverse.Ground;

public sealed partial class ClientConsole
{
    private const string BindingsPath = "user://console-bindings.cfg";
    private readonly ConsoleBindings _bindings = new();

    public bool RunBinding(string key)
    {
        if (_bindings.Find(key) is not { } command) return false;
        // A normal inspect key press is a cosmetic input, with no console or HUD log entry.
        var parsed = ConsoleCatalog.Parse(command);
        if (parsed.Command?.Name == "admire" && parsed.Args.Count == 0)
        {
            Admire?.Invoke();
            return true;
        }
        Execute(command, toast: true);
        return true;
    }

    public void AdjustNoclipSpeed(int steps)
    {
        float previous = float.IsFinite(Settings.NoclipSpeed) ? Settings.NoclipSpeed : 10;
        Settings.NoclipSpeed = Math.Clamp(previous * MathF.Pow(1.25f, steps), 1, 200);
        Settings.Save();
        Notice?.Invoke(string.Create(CultureInfo.InvariantCulture, $"Noclip {MathF.Round(Settings.NoclipSpeed):0} m/s · Shift ×4"));
    }

    private bool RunBindingCommand(string name, IReadOnlyList<string> args)
    {
        switch (name)
        {
            case "bind":
                if (args.Count == 0) { Fail("Usage: bind <key> [command], e.g. bind F6 \"noclip\"."); return true; }
                if (args.Count == 1) { Out(_bindings.Find(args[0]) is { } current ? $"{ConsoleBindings.Normalize(args[0])}: {current}" : $"{args[0]} is not bound."); return true; }
                // A quoted command keeps its inner arguments; unquoted words use the same command parser.
                string command = args.Count == 2 ? args[1] : string.Join(' ', System.Linq.Enumerable.Skip(args, 1));
                if (!_bindings.Set(args[0], command, out string error)) { Fail(error); return true; }
                SaveBindings();
                Out($"{ConsoleBindings.Normalize(args[0])}: {command}, kept on this computer.");
                return true;
            case "unbind":
                if (args.Count != 1) { Fail("Usage: unbind <key>."); return true; }
                bool removed = _bindings.Remove(args[0]);
                if (removed) SaveBindings();
                Out(removed ? $"Removed {args[0]}." : $"{args[0]} is not bound.");
                return true;
            case "binds":
                if (_bindings.Commands.Count == 0) Out("No saved command bindings. Example: bind F6 \"noclip\".");
                foreach ((string key, string value) in _bindings.Commands) Out($"{key}: {value}");
                return true;
            case "noclipspeed":
                Setting(args, "Noclip speed", Settings.NoclipSpeed, 1, 200, " m/s", v => Settings.NoclipSpeed = v);
                return true;
            default:
                return false;
        }
    }

    private void LoadBindings()
    {
        var file = new ConfigFile();
        if (file.Load(BindingsPath) != Error.Ok) return;
        var saved = new List<KeyValuePair<string, string>>();
        if (file.HasSection("bindings"))
            foreach (string key in file.GetSectionKeys("bindings"))
                saved.Add(new KeyValuePair<string, string>(key, file.GetValue("bindings", key).AsString()));
        var unbound = new List<string>();
        if (file.HasSection("unbound"))
            foreach (string key in file.GetSectionKeys("unbound"))
                if (file.GetValue("unbound", key).AsBool()) unbound.Add(key);
        _bindings.Restore(saved, unbound);
    }

    private void SaveBindings()
    {
        var file = new ConfigFile();
        foreach ((string key, string command) in _bindings.Commands) file.SetValue("bindings", key, command);
        foreach (string key in _bindings.UnboundDefaults) file.SetValue("unbound", key, true);
        file.Save(BindingsPath);
    }
}
