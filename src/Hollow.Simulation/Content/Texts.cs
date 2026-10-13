using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Hollow.Simulation.Content;

/// <summary>
/// The player's lines (GROUND_SYSTEMS.md §3.2, <c>texts.json</c>): one for every <see cref="OutcomeCode"/>, the one place
/// the interface looks for what an answer says. Read with the game data but not part of its hash: a line changes nothing
/// in the rules, so a translated client may join any server. A line may name its answer's detail: <c>{n}</c> is the
/// number, <c>{item}</c> the name of the item it names in lower case, <c>{item|word}</c> the same with a word for an
/// answer that names none ("Not enough {item|resources}").
/// </summary>
public sealed class Texts
{
    /// <summary>The file in game/data/ground.</summary>
    public const string File = "texts.json";

    private static readonly OutcomeCode[] Codes = Enum.GetValues<OutcomeCode>();

    private readonly string[] _lines;
    private readonly Func<int, string?> _itemName;
    private readonly Dictionary<long, string> _made = new();

    private Texts(string[] lines, Func<int, string?> itemName)
    {
        _lines = lines;
        _itemName = itemName;
    }

    /// <summary>What the player is told of an answer: its code's line with its detail filled in ("" for done).</summary>
    public string Line(in Outcome outcome) => Line(outcome.Code, outcome.Detail);

    public string Line(OutcomeCode code, int detail = 0)
    {
        int index = (int)code;
        if (index >= _lines.Length)
        {
            return code.ToString();
        }
        string template = _lines[index];
        if (template.IndexOf('{') < 0)
        {
            return template;
        }
        long key = (long)index << 32 | (uint)detail;
        lock (_made)
        {
            if (!_made.TryGetValue(key, out string? line))
            {
                line = Fill(template, detail);
                if (_made.Count > 512)
                {
                    _made.Clear();
                }
                _made[key] = line;
            }
            return line;
        }
    }

    // A line's placeholders filled in from the detail.
    private string Fill(string template, int detail)
    {
        var line = new StringBuilder(template.Length + 16);
        int at = 0;
        while (at < template.Length)
        {
            int open = template.IndexOf('{', at);
            int close = open < 0 ? -1 : template.IndexOf('}', open);
            if (open < 0 || close < 0)
            {
                line.Append(template, at, template.Length - at);
                break;
            }
            line.Append(template, at, open - at);
            string field = template.Substring(open + 1, close - open - 1);
            int bar = field.IndexOf('|');
            string name = bar < 0 ? field : field[..bar];
            string fallback = bar < 0 ? "" : field[(bar + 1)..];
            line.Append(name switch
            {
                "n" => detail.ToString(CultureInfo.InvariantCulture),
                "item" => _itemName(detail)?.ToLowerInvariant() ?? fallback,
                _ => field,
            });
            at = close + 1;
        }
        return line.ToString();
    }

    /// <summary>
    /// The lines from texts.json (<paramref name="root"/>, its <c>outcomes</c> by code name), noting every problem: a code
    /// without a line, a line naming no code, an unknown placeholder. <paramref name="itemName"/> names an item by ID.
    /// </summary>
    internal static Texts Read(JsonFields f, JsonElement root, Func<int, string?> itemName)
    {
        var lines = new string[(int)Codes[^1] + 1];
        Array.Fill(lines, "");
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new Texts(lines, itemName);
        }
        f.OnlyKnown(root, File, "note", "outcomes");
        if (!f.TryChild(root, "outcomes", out JsonElement outcomes) || outcomes.ValueKind != JsonValueKind.Object)
        {
            f.Problem(File, "\"outcomes\" is missing");
            return new Texts(lines, itemName);
        }
        var seen = new HashSet<OutcomeCode>();
        foreach (JsonProperty p in outcomes.EnumerateObject())
        {
            string where = $"{File}: outcomes.{p.Name}";
            if (!Enum.TryParse(p.Name, ignoreCase: false, out OutcomeCode code) || !Enum.IsDefined(code) || int.TryParse(p.Name, out _))
            {
                f.Problem(where, "no such answer code (OutcomeCode)");
                continue;
            }
            if (p.Value.ValueKind != JsonValueKind.String)
            {
                f.Problem(where, "must be text");
                continue;
            }
            string line = p.Value.GetString() ?? "";
            lines[(int)code] = line;
            seen.Add(code);
            foreach (string placeholder in Placeholders(line))
            {
                string name = placeholder.Split('|')[0];
                if (name is not ("n" or "item"))
                {
                    f.Problem(where, $"unknown placeholder {{{placeholder}}} (known: {{n}}, {{item}}, {{item|word}})");
                }
            }
        }
        foreach (OutcomeCode code in Codes)
        {
            if (code != OutcomeCode.Done && (!seen.Contains(code) || lines[(int)code].Length == 0))
            {
                f.Problem(File, $"the answer {code} has no line");
            }
        }
        return new Texts(lines, itemName);
    }

    private static IEnumerable<string> Placeholders(string line)
    {
        int at = 0;
        while ((at = line.IndexOf('{', at)) >= 0)
        {
            int close = line.IndexOf('}', at);
            if (close < 0)
            {
                yield break;
            }
            yield return line.Substring(at + 1, close - at - 1);
            at = close + 1;
        }
    }
}
