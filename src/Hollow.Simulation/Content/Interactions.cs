using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Hollow.Simulation.Content;

/// <summary>Which way a verb turns a device's switch: the loot panel's on/off button finds its verb by this.</summary>
public enum VerbSwitch : byte
{
    None,
    On,
    Off,
}

/// <summary>
/// One interaction verb (interactions.json, GROUND_SYSTEMS.md §5): what the E key can do to a world thing. Its index in
/// the list is its number on the wire; exactly one capability's handler offers and does it (checked at start).
/// </summary>
public sealed class VerbDef
{
    /// <summary>Its index in interactions.json: its number on the wire.</summary>
    public int Id { get; init; }

    public string Key { get; init; } = "";

    /// <summary>What the prompt and the radial say, before the thing's name ("Open", "Set code").</summary>
    public string Label { get; init; } = "";

    /// <summary>Whether the prompt names the thing after the label ("OPEN WOODEN DOOR"; not "SET CODE", "OPEN TECH TREE").</summary>
    public bool Named { get; init; } = true;

    /// <summary>The radial's outline glyph, by name.</summary>
    public string Glyph { get; init; } = "";

    /// <summary>The radial's line under the title.</summary>
    public string Description { get; init; } = "";

    /// <summary>The ring's order, and which of the options their handlers mark primary a tap does (the highest).</summary>
    public int Priority { get; init; }

    /// <summary>How far the eye may be from the thing's nearest surface (one of survival.json's reaches).</summary>
    public float Reach { get; init; }

    /// <summary>survival.json's reach it names.</summary>
    public string ReachName { get; init; } = "interact";

    /// <summary>Whether the eye must see the thing's nearest surface (a workbench's tree needs no sight, as its level).</summary>
    public bool Sight { get; init; } = true;

    /// <summary>A four-digit code is asked first (the keypad), and goes with the intent.</summary>
    public bool Code { get; init; }

    /// <summary>A panel of the interface it opens instead of asking the server (the tech tree), or null.</summary>
    public string? Panel { get; init; }

    /// <summary>Shown in the radial (false: a panel's own button sends it, as the tech tree's unlock).</summary>
    public bool Menu { get; init; } = true;

    /// <summary>A device's switch: on or off.</summary>
    public VerbSwitch Switch { get; init; }

    /// <summary>The gates: the doer must be alive and awake; may be down, or in a seat.</summary>
    public bool Alive { get; init; } = true;

    public bool Awake { get; init; } = true;

    public bool Wounded { get; init; }

    public bool Seated { get; init; }

    public override string ToString() => Key;
}

/// <summary>The interaction verbs (interactions.json): by number and by key, and how long E is held for the radial.</summary>
public sealed class InteractionData
{
    public const string File = "interactions.json";

    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

    internal InteractionData(IReadOnlyList<VerbDef> verbs, float holdSeconds)
    {
        Verbs = verbs;
        HoldSeconds = holdSeconds;
        Keys = verbs.Select(v => v.Key).ToArray();
        for (int i = 0; i < verbs.Count; i++)
        {
            _index.TryAdd(verbs[i].Key, i);
        }
        LongestReach = verbs.Count > 0 ? verbs.Max(v => v.Reach) : 0;
    }

    /// <summary>The verbs, by their number.</summary>
    public IReadOnlyList<VerbDef> Verbs { get; }

    public IReadOnlyList<string> Keys { get; }

    /// <summary>Holding E this long opens the radial (RustMVP's tap/hold line); a shorter press is a tap.</summary>
    public float HoldSeconds { get; }

    /// <summary>The farthest any verb reaches: what the aim looks along.</summary>
    public float LongestReach { get; }

    /// <summary>A verb by its number (untrusted: null when none).</summary>
    public VerbDef? Verb(int id) => (uint)id < (uint)Verbs.Count ? Verbs[id] : null;

    /// <summary>A verb by its key, or null.</summary>
    public VerbDef? Verb(string key) => _index.TryGetValue(key, out int i) ? Verbs[i] : null;

    /// <summary>A verb's number by its key, or −1.</summary>
    public int Index(string key) => _index.TryGetValue(key, out int i) ? i : -1;
}

/// <summary>Reads interactions.json once survival.json's reaches are known (a verb names one of them).</summary>
internal static class InteractionsReader
{
    public static void Read(JsonFields f, JsonElement root, GameContent.Loaded loaded)
    {
        const string file = InteractionData.File;
        if (root.ValueKind != JsonValueKind.Object)
        {
            loaded.Interactions = new InteractionData(Array.Empty<VerbDef>(), 0.35f);
            return;
        }
        f.OnlyKnown(root, file, "note", "holdSeconds", "verbs");
        float hold = f.Number(root, "holdSeconds", file);
        if (hold is <= 0 or > 2)
        {
            f.Problem(file, "holdSeconds 0 … 2");
        }
        SurvivalTuning s = loaded.Survival;
        var reaches = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["interact"] = s.InteractReach,
            ["open"] = s.OpenReach,
            ["keepOpen"] = s.KeepOpenReach,
            ["help"] = s.HelpReach,
            ["invite"] = s.InviteReach,
            ["station"] = s.StationReach,
        };
        var verbs = new List<VerbDef>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach ((JsonElement e, int i) in f.List(root, "verbs", file).Select((e, i) => (e, i)))
        {
            string where = $"{file}: verbs[{i}]";
            f.OnlyKnown(e, where, "key", "label", "named", "glyph", "description", "priority", "reach", "sight", "code", "panel", "menu",
                "switch", "alive", "awake", "wounded", "seated", "note");
            string key = f.Text(e, "key", where);
            if (!keys.Add(key))
            {
                f.Problem(where, $"\"{key}\" is in the list twice");
            }
            string reachName = f.Text(e, "reach", where, "interact");
            if (!reaches.TryGetValue(reachName, out float reach))
            {
                f.Problem(where, $"reach \"{reachName}\" isn't one of survival.json's ({string.Join(", ", reaches.Keys)})");
            }
            string switchName = f.Text(e, "switch", where, "none");
            if (!Enum.TryParse(switchName, ignoreCase: true, out VerbSwitch turns) || !Enum.IsDefined(turns) || char.IsDigit(switchName[0]))
            {
                f.Problem(where, $"switch \"{switchName}\" (known: on, off)");
                turns = VerbSwitch.None;
            }
            string panel = f.Text(e, "panel", where, "");
            var verb = new VerbDef
            {
                Id = i,
                Key = key,
                Label = f.Text(e, "label", where),
                Named = f.Flag(e, "named", where, true),
                Glyph = f.Text(e, "glyph", where),
                Description = f.Text(e, "description", where, ""),
                Priority = f.Integer(e, "priority", where, 0),
                Reach = reach,
                ReachName = reachName,
                Sight = f.Flag(e, "sight", where, true),
                Code = f.Flag(e, "code", where, false),
                Panel = panel.Length > 0 ? panel : null,
                Menu = f.Flag(e, "menu", where, true),
                Switch = turns,
                Alive = f.Flag(e, "alive", where, true),
                Awake = f.Flag(e, "awake", where, true),
                Wounded = f.Flag(e, "wounded", where, false),
                Seated = f.Flag(e, "seated", where, false),
            };
            if (verb.Label.Length == 0 || verb.Glyph.Length == 0)
            {
                f.Problem(where, "a verb needs a label and a glyph");
            }
            verbs.Add(verb);
        }
        if (verbs.Count > 1 << 14)
        {
            f.Problem(file, "too many verbs for the wire");
        }
        loaded.Interactions = new InteractionData(verbs, hold);
    }
}
