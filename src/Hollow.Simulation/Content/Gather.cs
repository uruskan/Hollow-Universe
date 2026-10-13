using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Simulation.Nature;

namespace Hollow.Simulation.Content;

/// <summary>Which of a tool's gather rates a class of natural objects takes (items.json <c>tree</c>, <c>ore</c>).</summary>
public enum GatherRate : byte
{
    Tree,
    Ore,
}

/// <summary>
/// Rust's weak spot (gather.json <c>classes.*.spot</c>, GROUND.md §6.1): where it shows and what a hit on it does. It
/// appears with the first hit, facing the hitter; a hit within <see cref="Radius"/> of it pays <see cref="Scale"/>
/// (rising with each hit on it in a row, up to <see cref="Max"/>) and moves it; a hit elsewhere resets the run and leaves
/// it where it is. A hit takes that much more health too, so the object's whole yield stays the same: the spot makes it
/// quicker, not richer.
/// </summary>
public sealed record SpotRule
{
    /// <summary>A hit whose point on the drawn surface is this close to the spot (metres) is on it.</summary>
    public float Radius { get; init; } = 0.3f;

    /// <summary>A new spot faces the hitter within this many degrees either way.</summary>
    public float FacingDegrees { get; init; } = 50;

    /// <summary>Each hit on the spot in a row adds this much to the multiplier (the first: 1 + step).</summary>
    public float Step { get; init; } = 0.5f;

    /// <summary>The multiplier's ceiling.</summary>
    public float Max { get; init; } = 3f;

    /// <summary>Where on the object it sits: the drawn surface (<see cref="TrunkSurface"/>, <see cref="MeasuredSurface"/>).</summary>
    public ISpotSurface Surface { get; init; } = null!;

    /// <summary>What a hit pays and takes after <paramref name="streak"/> hits on the spot in a row (1 when none).</summary>
    public float Scale(int streak) => streak <= 0 ? 1f : Math.Min(1f + Step * streak, Max);
}

/// <summary>
/// What the last hit pays (gather.json <c>classes.*.last</c>): a share of the whole yield kept back from the hits
/// (Rust's trees: half when it falls), and a bonus on top for a proper tool (Rust's ore nodes: about 20%, not with a
/// rock).
/// </summary>
public sealed record LastHitRule
{
    /// <summary>The share of the whole yield the last hit pays; the hits pay the rest as they take health.</summary>
    public float Share { get; init; }

    /// <summary>A share of the whole yield paid on top by the last hit with one of <see cref="Tools"/>.</summary>
    public float Bonus { get; init; }

    /// <summary>The tools that earn the bonus (items.json IDs).</summary>
    public IReadOnlySet<ushort> Tools { get; init; } = new HashSet<ushort>();

    public bool PaysBonus(ItemDef? tool) => Bonus > 0 && tool != null && Tools.Contains(tool.Id);
}

/// <summary>
/// One thing that happens on screen and in the ears (gather.json <c>look</c>): an effect of fx.json and a sound event
/// of sounds.json, both optional. <c>{stage}</c> in <see cref="Sound"/> becomes the stage reached. With
/// <see cref="Gibs"/>, the object's model breaks into its gibs where it stood (GROUND.md §9.4: an outcrop's last remnant
/// crumbling).
/// </summary>
public sealed record GatherCue(string? Fx, string? Sound, float FxScale = 1, float SoundDelay = 0, float PitchStep = 0, bool Gibs = false);

/// <summary>The weak spot drawn (gather.json <c>look.marker</c>): a decal style and its glints.</summary>
public sealed record MarkerLook
{
    /// <summary>The painter of its decal (the client's marker textures: "painted-x", "hotspot").</summary>
    public string Style { get; init; } = "";

    /// <summary>Width of the decal in metres, and how deep it projects (to cover bark and rock that bulge or dip).</summary>
    public float Size { get; init; } = 0.3f;

    public float Depth { get; init; } = 0.4f;

    /// <summary>Its colour, as picked (sRGB, 0 … 1): the paint's, or the glow's.</summary>
    public Vector3 Colour { get; init; } = Vector3.One;

    /// <summary>How brightly it glows, as seen whatever the exposure (0: paint).</summary>
    public float Glow { get; init; }

    /// <summary>Pulses per second of the glow and the glint (0: steady).</summary>
    public float Pulse { get; init; }

    /// <summary>A glint drawn over it, this wide in metres (0: none).</summary>
    public float Glint { get; init; }

    /// <summary>An effect of fx.json played on it every <see cref="SparkleEvery"/> seconds (null: none).</summary>
    public string? Sparkle { get; init; }

    public float SparkleEvery { get; init; } = 1;
}

/// <summary>A felled object falling (gather.json <c>look.fall</c>): seconds to tip over, to lie, to sink; the landing's effect.</summary>
public sealed record FallLook(float Tip, float Lie, float Sink, string? Fx);

/// <summary>A hit shaking the object's crown (gather.json <c>look.shake</c>): how far its top sways and for how long.</summary>
public sealed record ShakeLook(float Metres, float Seconds);

/// <summary>How a class of natural objects shows being gathered (gather.json <c>look</c>; a kind may replace parts).</summary>
public sealed record GatherLook
{
    public MarkerLook? Marker { get; init; }

    /// <summary>Every hit, where it met the drawn surface.</summary>
    public GatherCue? Hit { get; init; }

    /// <summary>A hit on the weak spot (its sound rises by <see cref="GatherCue.PitchStep"/> with each in a row).</summary>
    public GatherCue? SpotHit { get; init; }

    /// <summary>A run of weak-spot hits broken by a hit elsewhere.</summary>
    public GatherCue? Missed { get; init; }

    /// <summary>A depletion stage reached (an outcrop breaking up).</summary>
    public GatherCue? Stage { get; init; }

    /// <summary>Gone: felled or broken.</summary>
    public GatherCue? Depleted { get; init; }

    public FallLook? Fall { get; init; }

    public ShakeLook? Shake { get; init; }
}

/// <summary>
/// How a class of natural objects is gathered (gather.json <c>classes</c>, GROUND.md §6.1): the tool rate it takes, its
/// weak spot, what the last hit pays and how it all shows. One rule for every class (<see cref="NaturalState.Hit"/>);
/// each harvested kind carries its own copy, with its drawn surface and any parts of the look it replaces.
/// </summary>
public sealed record GatherRules(GatherRate Rate, SpotRule Spot, LastHitRule Last, GatherLook Look);

/// <summary>Reads gather.json's <c>classes</c> and the kinds' <c>look</c>, and surfaces.json (the drawn surfaces).</summary>
internal static class GatherReader
{
    public const string SurfacesFile = "surfaces.json";

    private static readonly string[] Cues = { "hit", "spotHit", "missed", "stage", "depleted" };

    /// <summary>The rules of each class named in gather.json's <c>classes</c>, without a surface yet.</summary>
    public static Dictionary<string, (GatherRules Rules, JsonElement Spot)> Classes(JsonFields f, JsonElement gather, Func<string, string, ItemDef?> resolve)
    {
        var classes = new Dictionary<string, (GatherRules, JsonElement)>(StringComparer.Ordinal);
        if (!f.TryChild(gather, "classes", out JsonElement all))
        {
            f.Problem("gather.json", "\"classes\" is missing");
            return classes;
        }
        foreach (JsonProperty c in all.EnumerateObject())
        {
            string where = $"gather.json: classes.{c.Name}";
            f.OnlyKnown(c.Value, where, "rate", "spot", "last", "look");
            string rate = f.Text(c.Value, "rate", where);
            if (rate is not ("tree" or "ore"))
            {
                f.Problem(where, $"rate \"{rate}\" is not tree or ore");
            }
            SpotRule spot = new();
            JsonElement spotData = default;
            if (f.TryChild(c.Value, "spot", out spotData))
            {
                string at = where + ".spot";
                f.OnlyKnown(spotData, at, "surface", "radius", "facing", "step", "max", "height", "follow", "low");
                spot = new SpotRule
                {
                    Radius = f.Number(spotData, "radius", at),
                    FacingDegrees = f.Number(spotData, "facing", at),
                    Step = f.Number(spotData, "step", at),
                    Max = f.Number(spotData, "max", at),
                };
            }
            else
            {
                f.Problem(where, "\"spot\" is missing");
            }
            var last = new LastHitRule();
            if (f.TryChild(c.Value, "last", out JsonElement lastData))
            {
                string at = where + ".last";
                f.OnlyKnown(lastData, at, "share", "bonus", "tools");
                var tools = new HashSet<ushort>();
                foreach (string key in f.Texts(lastData, "tools", at))
                {
                    if (resolve(key, at + ".tools") is { } tool)
                    {
                        tools.Add(tool.Id);
                    }
                }
                last = new LastHitRule { Share = f.Number(lastData, "share", at, 0), Bonus = f.Number(lastData, "bonus", at, 0), Tools = tools };
            }
            GatherLook look = f.TryChild(c.Value, "look", out JsonElement lookData) ? Look(f, lookData, where + ".look", new GatherLook()) : new GatherLook();
            classes[c.Name] = (new GatherRules(rate == "ore" ? GatherRate.Ore : GatherRate.Tree, spot, last, look), spotData);
        }
        return classes;
    }

    /// <summary>A look read over <paramref name="basis"/>: what it names replaces the basis's part, the rest is kept.</summary>
    public static GatherLook Look(JsonFields f, JsonElement e, string where, GatherLook basis)
    {
        f.OnlyKnown(e, where, Cues.Concat(new[] { "marker", "fall", "shake" }).ToArray());
        GatherLook look = basis with
        {
            Hit = Cue(f, e, "hit", where) ?? basis.Hit,
            SpotHit = Cue(f, e, "spotHit", where) ?? basis.SpotHit,
            Missed = Cue(f, e, "missed", where) ?? basis.Missed,
            Stage = Cue(f, e, "stage", where) ?? basis.Stage,
            Depleted = Cue(f, e, "depleted", where) ?? basis.Depleted,
        };
        if (f.TryChild(e, "marker", out JsonElement m))
        {
            string at = where + ".marker";
            f.OnlyKnown(m, at, "style", "size", "depth", "colour", "glow", "pulse", "glint", "sparkle", "sparkleEvery");
            float[] colour = Numbers(f, m, "colour", at, 3);
            look = look with
            {
                Marker = new MarkerLook
                {
                    Style = f.Text(m, "style", at),
                    Size = f.Number(m, "size", at),
                    Depth = f.Number(m, "depth", at),
                    Colour = new Vector3(colour[0], colour[1], colour[2]),
                    Glow = f.Number(m, "glow", at, 0),
                    Pulse = f.Number(m, "pulse", at, 0),
                    Glint = f.Number(m, "glint", at, 0),
                    Sparkle = f.Has(m, "sparkle") ? f.Text(m, "sparkle", at) : null,
                    SparkleEvery = f.Number(m, "sparkleEvery", at, 1),
                },
            };
        }
        if (f.TryChild(e, "fall", out JsonElement fall))
        {
            string at = where + ".fall";
            f.OnlyKnown(fall, at, "tip", "lie", "sink", "fx");
            look = look with
            {
                Fall = new FallLook(f.Number(fall, "tip", at), f.Number(fall, "lie", at), f.Number(fall, "sink", at),
                    f.Has(fall, "fx") ? f.Text(fall, "fx", at) : null),
            };
        }
        if (f.TryChild(e, "shake", out JsonElement shake))
        {
            string at = where + ".shake";
            f.OnlyKnown(shake, at, "metres", "seconds");
            look = look with { Shake = new ShakeLook(f.Number(shake, "metres", at), f.Number(shake, "seconds", at)) };
        }
        return look;
    }

    private static GatherCue? Cue(JsonFields f, JsonElement look, string name, string where)
    {
        if (!f.TryChild(look, name, out JsonElement e))
        {
            return null;
        }
        string at = $"{where}.{name}";
        f.OnlyKnown(e, at, "fx", "sound", "fxScale", "soundDelay", "pitchStep", "gibs");
        return new GatherCue(f.Has(e, "fx") ? f.Text(e, "fx", at) : null, f.Has(e, "sound") ? f.Text(e, "sound", at) : null,
            f.Number(e, "fxScale", at, 1), f.Number(e, "soundDelay", at, 0), f.Number(e, "pitchStep", at, 0),
            e.TryGetProperty("gibs", out JsonElement gibs) && gibs.ValueKind == JsonValueKind.True);
    }

    /// <summary>A list of exactly <paramref name="count"/> numbers.</summary>
    public static float[] Numbers(JsonFields f, JsonElement o, string name, string where, int count)
    {
        var values = new float[count];
        JsonElement[] list = f.List(o, name, where);
        if (list.Length != count || list.Any(v => v.ValueKind != JsonValueKind.Number))
        {
            f.Problem(where, $"\"{name}\" must be {count} numbers");
            return values;
        }
        for (int i = 0; i < count; i++)
        {
            values[i] = (float)list[i].GetDouble();
        }
        return values;
    }

    /// <summary>
    /// A kind's spot surface: the trunk (a tree's capsule, at a band of heights) or the measured surface of its drawn
    /// model (surfaces.json), as its class's <c>spot.surface</c> says.
    /// </summary>
    public static ISpotSurface? Surface(JsonFields f, JsonElement spot, string where, NaturalKind kind, JsonElement surfaces)
    {
        string type = f.Text(spot, "surface", where);
        switch (type)
        {
            case "trunk":
            {
                float[] band = Numbers(f, spot, "height", where, 2);
                return new TrunkSurface(band[0], band[1], f.Number(spot, "follow", where, 0));
            }
            case "measured":
            {
                if (!f.TryChild(surfaces, "kinds", out JsonElement kinds) || !kinds.TryGetProperty(kind.Key, out JsonElement stages) ||
                    stages.ValueKind != JsonValueKind.Array)
                {
                    f.Problem($"gather.json: \"{kind.Key}\"", $"its spot is on the measured surface, but {SurfacesFile} has none for it");
                    return null;
                }
                JsonElement[] ringList = f.List(surfaces, "rings", SurfacesFile);
                float[] rings = ringList.Select(r => r.ValueKind == JsonValueKind.Number ? (float)r.GetDouble() : 0f).ToArray();
                int directions = f.Integer(surfaces, "directions", SurfacesFile);
                var heights = new List<float[,]>();
                foreach (JsonElement stage in stages.EnumerateArray())
                {
                    var table = new float[rings.Length, directions];
                    bool shaped = stage.ValueKind == JsonValueKind.Array && stage.GetArrayLength() == rings.Length;
                    for (int r = 0; shaped && r < rings.Length; r++)
                    {
                        JsonElement row = stage[r];
                        shaped = row.ValueKind == JsonValueKind.Array && row.GetArrayLength() == directions;
                        for (int d = 0; shaped && d < directions; d++)
                        {
                            table[r, d] = row[d].ValueKind == JsonValueKind.Number ? (float)row[d].GetDouble() : float.NaN;
                        }
                    }
                    if (!shaped)
                    {
                        f.Problem($"{SurfacesFile}: \"{kind.Key}\"", $"each stage must be {rings.Length} rings of {directions} heights");
                        return null;
                    }
                    heights.Add(table);
                }
                if (heights.Count < kind.Stages)
                {
                    f.Problem($"{SurfacesFile}: \"{kind.Key}\"", $"{heights.Count} stages measured, {kind.Stages} drawn");
                    return null;
                }
                return new MeasuredSurface(rings, heights, f.Number(spot, "low", where, 0.4f));
            }
            default:
                f.Problem(where, $"surface \"{type}\" is not trunk or measured");
                return null;
        }
    }
}
