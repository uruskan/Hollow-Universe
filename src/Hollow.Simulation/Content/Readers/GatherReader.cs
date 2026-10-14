using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Simulation.Nature;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// gather.json: the natural kinds (trees, outcrops, pickups, bushes, boulders) with their health, yields, stages,
/// respawn and shape, and <c>classes</c>, how each class is gathered (GROUND.md §6.1): the tool rate it takes, its weak
/// spot on the drawn surface (surfaces.json), what the last hit pays and how it all shows; a kind's <c>look</c>
/// replaces parts of its class's.
/// </summary>
internal sealed class GatherReader : IContentReader
{
    private static readonly string[] Cues = { "hit", "spotHit", "missed", "stage", "depleted" };

    public string File => "gather.json";

    public void Read(ContentReading r)
    {
        static string Hyphenated(Surface.ScatterKind k) =>
            string.Concat(k.ToString().Select((ch, i) => char.IsUpper(ch) && i > 0 ? "-" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
        static IEnumerable<string> ScatterKindNames() => Enum.GetValues<Surface.ScatterKind>().Select(Hyphenated);
        static Surface.ScatterKind? ScatterKindNamed(string name) =>
            Enum.GetValues<Surface.ScatterKind>().Where(k => Hyphenated(k) == name).Select(k => (Surface.ScatterKind?)k).FirstOrDefault();
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        JsonElement root = r.Root("gather.json", "classes", "kinds");
        // How each class is gathered, and the drawn surfaces the weak spots sit on (GROUND.md §6.1).
        Dictionary<string, (GatherRules Rules, JsonElement Spot)> classes = Classes(f, root, r.Resolve);
        JsonElement surfaces = r.Documents[SurfacesReader.FileName];
        JsonElement[] list = f.List(root, "kinds", "gather.json");
        for (int i = 0; i < list.Length; i++)
        {
            JsonElement e = list[i];
            string key = f.Text(e, "key", $"gather.json: kinds[{i}]");
            string where = $"gather.json: {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "key", "name", "class", "health", "yield", "stages", "respawn", "radius", "height", "standIn", "scatter", "look");
            string className = f.Text(e, "class", where);
            NaturalClass kind = className switch
            {
                "tree" => NaturalClass.Tree,
                "node" => NaturalClass.Node,
                "pickup" => NaturalClass.Pickup,
                "bush" => NaturalClass.Bush,
                "boulder" => NaturalClass.Boulder,
                _ => NaturalClass.Pickup,
            };
            if (className is not ("tree" or "node" or "pickup" or "bush" or "boulder"))
            {
                f.Problem(where, $"class \"{className}\" is not tree, node, pickup, bush or boulder");
            }
            var yields = new List<ItemAmount>();
            foreach (JsonElement y in f.List(e, "yield", where))
            {
                if (f.Tuple(y, where + ".yield", 1, out string item, out double[] values) && r.Resolve(item, where + ".yield") is { } def)
                {
                    yields.Add(new ItemAmount(def, (int)values[0]));
                }
            }
            float perTile = 0;
            if (f.TryChild(e, "standIn", out JsonElement standIn))
            {
                f.OnlyKnown(standIn, where + ".standIn", "perTile");
                perTile = f.Number(standIn, "perTile", where + ".standIn");
            }
            var scatterKinds = new List<Surface.ScatterKind>();
            float minScale = 0;
            if (f.TryChild(e, "scatter", out JsonElement scatter))
            {
                f.OnlyKnown(scatter, where + ".scatter", "kinds", "minScale");
                foreach (string name in f.Texts(scatter, "kinds", where + ".scatter"))
                {
                    if (ScatterKindNamed(name) is { } sk)
                    {
                        scatterKinds.Add(sk);
                    }
                    else
                    {
                        f.Problem(where + ".scatter", $"\"{name}\" is not a scatter kind ({string.Join(", ", ScatterKindNames())})");
                    }
                }
                minScale = f.Number(scatter, "minScale", where + ".scatter", 0f);
            }
            bool harvested = kind is NaturalClass.Tree or NaturalClass.Node;
            var natural = new NaturalKind
            {
                Index = (ushort)i,
                Key = key,
                Name = f.Text(e, "name", where),
                Class = kind,
                Health = harvested ? f.Number(e, "health", where) : 0,
                Yield = yields,
                Stages = f.Integer(e, "stages", where, 1),
                RespawnSeconds = f.Number(e, "respawn", where),
                Radius = f.Number(e, "radius", where),
                Height = f.Number(e, "height", where),
                StandInPerTile = perTile,
                ScatterKinds = scatterKinds,
                ScatterMinScale = minScale,
            };
            if (harvested && classes.TryGetValue(className, out var rules))
            {
                // The class's rules with this kind's own surface, and any parts of the look it replaces.
                ISpotSurface? surface = SurfaceOf(f, rules.Spot, $"gather.json: classes.{className}.spot", natural, surfaces);
                GatherLook look = f.TryChild(e, "look", out JsonElement own) ? Look(f, own, where + ".look", rules.Rules.Look) : rules.Rules.Look;
                natural.Gather = rules.Rules with { Spot = rules.Rules.Spot with { Surface = surface! }, Look = look };
                if (surface == null)
                {
                    natural.Gather = null;
                }
            }
            else if (harvested)
            {
                f.Problem(where, $"gather.json has no \"classes\" entry for \"{className}\"");
            }
            else if (f.Has(e, "look"))
            {
                f.Problem(where, "only trees and outcrops have a look");
            }
            loaded.Natural.Add(natural);
        }
    }

    /// <summary>The rules of each class named in gather.json's <c>classes</c>, without a surface yet.</summary>
    private static Dictionary<string, (GatherRules Rules, JsonElement Spot)> Classes(JsonFields f, JsonElement gather, Func<string, string, ItemDef?> resolve)
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
    private static GatherLook Look(JsonFields f, JsonElement e, string where, GatherLook basis)
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
    private static float[] Numbers(JsonFields f, JsonElement o, string name, string where, int count)
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
    private static ISpotSurface? SurfaceOf(JsonFields f, JsonElement spot, string where, NaturalKind kind, JsonElement surfaces)
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
                    f.Problem($"gather.json: \"{kind.Key}\"", $"its spot is on the measured surface, but {SurfacesReader.FileName} has none for it");
                    return null;
                }
                JsonElement[] ringList = f.List(surfaces, "rings", SurfacesReader.FileName);
                float[] rings = ringList.Select(r => r.ValueKind == JsonValueKind.Number ? (float)r.GetDouble() : 0f).ToArray();
                int directions = f.Integer(surfaces, "directions", SurfacesReader.FileName);
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
                        f.Problem($"{SurfacesReader.FileName}: \"{kind.Key}\"", $"each stage must be {rings.Length} rings of {directions} heights");
                        return null;
                    }
                    heights.Add(table);
                }
                if (heights.Count < kind.Stages)
                {
                    f.Problem($"{SurfacesReader.FileName}: \"{kind.Key}\"", $"{heights.Count} stages measured, {kind.Stages} drawn");
                    return null;
                }
                return new MeasuredSurface(rings, heights, f.Number(spot, "low", where, 0.4f));
            }
            default:
                f.Problem(where, $"surface \"{type}\" is not trunk or measured");
                return null;
        }
    }

    /// <summary>Keys unique; yields, health, stages, respawn and shape in range; the weak spot, the last hit and the marker sensible.</summary>
    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        c.Duplicates("gather.json", loaded.Natural.Select(k => k.Key));
        foreach (NaturalKind k in loaded.Natural)
        {
            string where = $"gather.json: \"{k.Key}\"";
            if (k.Class != NaturalClass.Boulder && (k.Yield.Count == 0 || k.Yield.Any(y => y.Count < 1)))
            {
                c.Problem(where, "needs a yield, each at least 1");
            }
            if (k.IsHarvested && k.Health <= 0)
            {
                c.Problem(where, "trees and outcrops need health");
            }
            if (k.Stages is < 1 or > 3)
            {
                c.Problem(where, "stages must be 1 … 3");
            }
            if (k.RespawnSeconds <= 0 || k.Radius <= 0 || k.Height <= 0 || k.StandInPerTile < 0)
            {
                c.Problem(where, "respawn, radius and height must be positive");
            }
            if (k.Gather is { } g)
            {
                if (g.Spot.Radius <= 0 || g.Spot.Step < 0 || g.Spot.Max < 1 || g.Spot.FacingDegrees is < 0 or > 180)
                {
                    c.Problem(where, "the weak spot's radius must be positive, its step at least 0, its ceiling at least 1, its facing 0 … 180°");
                }
                if (g.Last.Share is < 0 or >= 1 || g.Last.Bonus < 0)
                {
                    c.Problem(where, "the last hit's share must be 0 … under 1 and its bonus at least 0");
                }
                if (g.Look.Marker is { } m && (m.Style.Length == 0 || m.Size <= 0 || m.Depth <= 0 || m.SparkleEvery <= 0))
                {
                    c.Problem(where, "the marker needs a style, a size, a depth and a sparkle interval");
                }
            }
        }
    }
}
