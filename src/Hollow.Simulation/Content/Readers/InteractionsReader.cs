using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>Reads interactions.json (GROUND_SYSTEMS.md §5) once survival.json's reaches are known (a verb names one of them).</summary>
internal sealed class InteractionsReader : IContentReader
{
    public string File => InteractionData.File;

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        const string file = InteractionData.File;
        JsonElement root = r.Documents[file];
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
                "switch", "alive", "awake", "wounded", "seated", "note", "text");
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
                Text = f.Flag(e, "text", where, false),
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

    public void Check(ContentChecking c)
    {
        // Checked as it is read: keys, reaches, switches, labels and glyphs; that one handler offers each verb is the registry's check.
    }
}
