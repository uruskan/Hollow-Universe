using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Hollow.Client.Loading;

/// <summary>
/// One stage of the plan: its words on the loading screen, its weight (the seconds it takes on the reference PC, which
/// is also its share of the bar) and the numbers its own rule reads (<see cref="Number"/>).
/// </summary>
public sealed record StagePlan(string Key, string Title, double Weight, int Order, IReadOnlyDictionary<string, double> Numbers)
{
    /// <summary>A number of this stage's own rule ("quietSeconds", "frameMs"...), or <paramref name="fallback"/>.</summary>
    public double Number(string name, double fallback) => Numbers.TryGetValue(name, out double v) ? v : fallback;
}

/// <summary>
/// The loading screen's data (GROUND.md §4.3.1, <c>game/data/ground/loading.json</c>), without Godot: every stage a part
/// of the game may register, in the order the screen names them, with its title and weight; the picture; the tips; how
/// the screen fades; what counts as arriving somewhere new. Code registers stages by key; their words and numbers are
/// here, so a translation or a retune is one file.
/// </summary>
public sealed class LoadingPlan
{
    public const string FileName = "loading.json";

    private readonly Dictionary<string, StagePlan> _stages;

    private LoadingPlan(List<StagePlan> stages, IReadOnlyList<string> tips, string picture, double fadeSeconds, double tipSeconds,
        double arrivalMetres, double arrivalGraceSeconds)
    {
        Stages = stages;
        _stages = stages.ToDictionary(s => s.Key, StringComparer.Ordinal);
        Tips = tips;
        Picture = picture;
        FadeSeconds = fadeSeconds;
        TipSeconds = tipSeconds;
        ArrivalMetres = arrivalMetres;
        ArrivalGraceSeconds = arrivalGraceSeconds;
    }

    /// <summary>Every stage, in the order the screen names them.</summary>
    public IReadOnlyList<StagePlan> Stages { get; }

    /// <summary>Short lines shown one at a time under the bar.</summary>
    public IReadOnlyList<string> Tips { get; }

    /// <summary>The picture behind the screen (a <c>res://</c> path).</summary>
    public string Picture { get; }

    /// <summary>How long the screen takes to fade out once play starts (and in, on an arrival).</summary>
    public double FadeSeconds { get; }

    /// <summary>How long each tip stays.</summary>
    public double TipSeconds { get; }

    /// <summary>The player moved at least this far at once (a respawn, a teleport): the arrival's stages run again.</summary>
    public double ArrivalMetres { get; }

    /// <summary>An arrival shows the screen only if its stages aren't done within this time.</summary>
    public double ArrivalGraceSeconds { get; }

    /// <summary>A stage's plan; an unknown key gets its key as the title and a weight of one second (and shows so).</summary>
    public StagePlan Stage(string key) =>
        _stages.TryGetValue(key, out StagePlan? plan) ? plan : new StagePlan(key, key, 1, int.MaxValue, new Dictionary<string, double>());

    /// <summary>A plan with no data: every stage named by its key (tests, or a missing file).</summary>
    public static LoadingPlan Empty { get; } = new(new List<StagePlan>(), Array.Empty<string>(), "", 0.6, 9, 120, 0.25);

    public static LoadingPlan Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Parses the plan; lists every problem at once (a stage without a key, title or weight, a key twice).</summary>
    public static LoadingPlan Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement root = doc.RootElement;
        var problems = new List<string>();
        var stages = new List<StagePlan>();
        if (root.TryGetProperty("stages", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement s in list.EnumerateArray())
            {
                string key = Str(s, "key", "");
                string title = Str(s, "title", "");
                double weight = Num(s, "weight", 0);
                if (key.Length == 0)
                {
                    problems.Add("a stage has no key");
                    continue;
                }
                if (stages.Any(p => p.Key == key))
                {
                    problems.Add($"{key}: listed twice");
                }
                if (title.Length == 0)
                {
                    problems.Add($"{key}: no title");
                }
                if (!(weight > 0))
                {
                    problems.Add($"{key}: the weight must be above zero");
                }
                var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (JsonProperty p in s.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.Number && p.Name != "weight")
                    {
                        numbers[p.Name] = p.Value.GetDouble();
                    }
                }
                stages.Add(new StagePlan(key, title, weight, stages.Count, numbers));
            }
        }
        var tips = new List<string>();
        if (root.TryGetProperty("tips", out JsonElement t) && t.ValueKind == JsonValueKind.Array)
        {
            tips.AddRange(t.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0));
        }
        JsonElement arrival = root.TryGetProperty("arrival", out JsonElement a) ? a : default;
        double fade = Num(root, "fadeSeconds", 0.6), tip = Num(root, "tipSeconds", 9);
        double metres = Num(arrival, "metres", 120), grace = Num(arrival, "graceSeconds", 0.25);
        if (fade < 0 || tip <= 0 || metres <= 0 || grace < 0)
        {
            problems.Add("fadeSeconds, tipSeconds, arrival.metres and arrival.graceSeconds must be positive");
        }
        if (problems.Count > 0)
        {
            throw new InvalidDataException($"{FileName}: " + string.Join("; ", problems));
        }
        return new LoadingPlan(stages, tips, Str(root, "picture", ""), fade, tip, metres, grace);
    }

    private static string Str(JsonElement e, string name, string fallback) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : fallback;

    private static double Num(JsonElement e, string name, double fallback) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v)
            ? v.ValueKind == JsonValueKind.Number ? v.GetDouble()
            : v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d
            : fallback
            : fallback;
}
