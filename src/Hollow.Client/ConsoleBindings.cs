using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Net.Commands;

namespace Hollow.Client;

/// <summary>Saved press-to-run console shortcuts. Commands still use the ordinary client/server permission path.</summary>
public sealed class ConsoleBindings
{
    private readonly SortedDictionary<string, string> _commands = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> Commands => _commands;

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
        if (!_commands.ContainsKey(normal) && _commands.Count >= 128)
        {
            error = "At most 128 shortcuts can be saved; unbind one first.";
            return false;
        }
        _commands[normal] = command;
        return true;
    }

    public bool Remove(string key) => Normalize(key) is { } normal && _commands.Remove(normal);
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
