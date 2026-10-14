using System.Collections.Generic;

namespace HollowUniverse.Ground;

/// <summary>What happened in this frame that may cost time (for <c>--measure</c>'s report of the slowest frames).</summary>
public static class FrameEvents
{
    private static readonly List<string> Current = new();

    /// <summary>Recording only while measuring.</summary>
    public static bool Enabled { get; set; }

    public static void Note(string what)
    {
        if (Enabled)
        {
            Current.Add(what);
        }
    }

    /// <summary>This frame's events, and a fresh list for the next.</summary>
    public static string Take()
    {
        string text = string.Join(", ", Current);
        Current.Clear();
        return text;
    }
}
