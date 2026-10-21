using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Hollow.Client.Feedback;

public sealed record ActionSoundCue(double Fraction, string Role);

/// <summary>Shared normalized cue timelines; gameplay still owns the duration and acceptance/cancellation.</summary>
public sealed class ActionCueData
{
    public const string FileName = "action-cues.json";
    public IReadOnlyDictionary<string, IReadOnlyList<ActionSoundCue>> Actions { get; }
    private ActionCueData(Dictionary<string, IReadOnlyList<ActionSoundCue>> actions) => Actions = actions;
    public IReadOnlyList<ActionSoundCue> For(string action) => Actions.TryGetValue(action, out var cues) ? cues : Array.Empty<ActionSoundCue>();
    public static ActionCueData Load(string path) => Parse(File.ReadAllText(path));
    public static ActionCueData Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var actions = new Dictionary<string, IReadOnlyList<ActionSoundCue>>(StringComparer.Ordinal);
        foreach (JsonProperty action in doc.RootElement.GetProperty("actions").EnumerateObject())
        {
            var cues = new List<ActionSoundCue>();
            var roles = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement e in action.Value.EnumerateArray())
            {
                double fraction = e.GetProperty("at").GetDouble();
                string role = e.GetProperty("role").GetString() ?? "";
                if (!double.IsFinite(fraction) || fraction < 0 || fraction >= 1 || string.IsNullOrWhiteSpace(role) || !roles.Add(role))
                    throw new InvalidDataException($"Invalid/duplicate {action.Name} cue {role} at {fraction}.");
                cues.Add(new ActionSoundCue(fraction, role));
            }
            if (!actions.TryAdd(action.Name, cues.OrderBy(c => c.Fraction).ToArray()))
                throw new InvalidDataException($"Duplicate action cue timeline {action.Name}.");
        }
        return new ActionCueData(actions);
    }
}

/// <summary>A single owned timeline. Starts/late joins skip past cues, cancellation prevents stale callbacks.</summary>
public sealed class ActionCuePlayer
{
    private IReadOnlyList<ActionSoundCue> _cues = Array.Empty<ActionSoundCue>();
    private int _next;
    private double _duration;
    public bool Active => _next < _cues.Count;
    public void Start(IReadOnlyList<ActionSoundCue> cues, double duration, double elapsed = 0)
    {
        Cancel();
        if (!double.IsFinite(duration) || duration <= 0 || !double.IsFinite(elapsed)) return;
        _cues = cues; _duration = duration;
        while (_next < _cues.Count && _cues[_next].Fraction * duration < Math.Max(0, elapsed)) _next++;
    }
    public void Advance(double elapsed, bool valid, Action<string> emit)
    {
        if (!valid) { Cancel(); return; }
        if (!double.IsFinite(elapsed)) return;
        while (_next < _cues.Count && _cues[_next].Fraction * _duration <= elapsed)
            emit(_cues[_next++].Role);
    }
    public void Cancel() { _cues = Array.Empty<ActionSoundCue>(); _next = 0; _duration = 0; }
}
