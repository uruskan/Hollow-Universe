using System;
using System.Globalization;
using System.Text;

namespace Hollow.Simulation.Interaction;

/// <summary>Player-facing labels, never paths or commands. Stored canonically and bounded by Unicode characters.</summary>
public static class Names
{
    public const int MaxCharacters = 48;
    public const int MaxUtf8Bytes = 192;

    public static bool TryNormalize(string? value, out string name)
    {
        name = "";
        if (value == null || value.Length > MaxCharacters * 2) return false;
        string normalized;
        try { normalized = value.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return false; }
        int count = 0;
        foreach (Rune rune in normalized.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or
                UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate) return false;
            count++;
        }
        normalized = normalized.Trim();
        if (count > MaxCharacters || normalized.Length == 0) return false;
        name = normalized;
        return true;
    }
}
