using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Net.Commands;

namespace Hollow.Client;

/// <summary>Saved press-to-run console shortcuts. Commands still use the ordinary client/server permission path.</summary>
public sealed class ConsoleBindings
{
    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["N"] = "admire",
    };
    private readonly SortedDictionary<string, string> _commands = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _unboundDefaults = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> Commands => _commands;
    /// <summary>Explicit removals of defaults survive a reload and future default merging.</summary>
    public IReadOnlyCollection<string> UnboundDefaults => _unboundDefaults;

    public ConsoleBindings() => Restore(Array.Empty<KeyValuePair<string, string>>());

    /// <summary>Merge saved bindings over current defaults. Older files need no migration;
    /// explicit removals stay removed, and a saved binding takes precedence over a stale removal.</summary>
    public void Restore(IEnumerable<KeyValuePair<string, string>> saved, IEnumerable<string>? unbound = null)
    {
        _commands.Clear();
        _unboundDefaults.Clear();
        foreach ((string key, string command) in Defaults) _commands[key] = command;
        if (unbound != null)
            foreach (string key in unbound) Remove(key);
        foreach ((string key, string command) in saved) Set(key, command, out _);
    }

    public bool Set(string key, string command, out string error)
    {
        error = "";
        string? normal = Normalize(key);
        if (normal == null)
        {
            error = "Use a physical letter, number, F1–F24, named key, MWHEELUP or MWHEELDOWN; optional CTRL+, ALT+, SHIFT+.";
            return false;
        }
        command = command.Trim();
        if (command.Length == 0 || command.Length > 512 || command.Contains('\n') || command.Contains('\r') || ConsoleCatalog.Parse(command).Command == null)
        {
            error = "Bind one known console command, at most 512 characters. Type help to list commands.";
            return false;
        }
        if (!_commands.ContainsKey(normal) && _commands.Count >= 128 + Defaults.Count)
        {
            error = "At most 128 additional shortcuts can be saved; unbind one first.";
            return false;
        }
        _commands[normal] = command;
        _unboundDefaults.Remove(normal);
        return true;
    }

    public bool Remove(string key)
    {
        if (Normalize(key) is not { } normal) return false;
        bool removed = _commands.Remove(normal);
        if (Defaults.ContainsKey(normal)) _unboundDefaults.Add(normal);
        return removed;
    }
    public string? Find(string key) => Normalize(key) is { } normal && _commands.TryGetValue(normal, out string? command) ? command : null;

    public static string? Normalize(string key)
    {
        string[] words = key.Trim().ToUpperInvariant().Split('+', StringSplitOptions.TrimEntries);
        if (words.Length == 0 || words.Any(w => w.Length == 0)) return null;
        bool ctrl = false, alt = false, shift = false;
        foreach (string word in words[..^1])
        {
            switch (word)
            {
                case "CTRL": case "CONTROL": ctrl = true; break;
                case "ALT": alt = true; break;
                case "SHIFT": shift = true; break;
                default: return null;
            }
        }
        string button = words[^1] switch
        {
            "ESC" => "ESCAPE", "RETURN" => "ENTER", "CONTROL" => "CTRL", "DEL" => "DELETE",
            "PGUP" => "PAGEUP", "PGDN" => "PAGEDOWN", "WHEELUP" => "MWHEELUP", "WHEELDOWN" => "MWHEELDOWN",
            var word => word,
        };
        bool valid = button.Length == 1 && char.IsAsciiLetterOrDigit(button[0]) ||
            button.StartsWith('F') && int.TryParse(button.AsSpan(1), out int fn) && fn is >= 1 and <= 24 ||
            button is "ESCAPE" or "TAB" or "BACKSPACE" or "ENTER" or "INSERT" or "DELETE" or "HOME" or "END" or
                "LEFT" or "RIGHT" or "UP" or "DOWN" or "PAGEUP" or "PAGEDOWN" or "SPACE" or "SHIFT" or "CTRL" or "ALT" or
                "CAPSLOCK" or "NUMLOCK" or "SCROLLLOCK" or "PAUSE" or "PRINT" or "MWHEELUP" or "MWHEELDOWN" or
                "MINUS" or "EQUAL" or "BRACKETLEFT" or "BRACKETRIGHT" or "BACKSLASH" or "SEMICOLON" or "APOSTROPHE" or
                "COMMA" or "PERIOD" or "SLASH" or "QUOTELEFT";
        return valid ? (ctrl ? "CTRL+" : "") + (alt ? "ALT+" : "") + (shift ? "SHIFT+" : "") + button : null;
    }
}
