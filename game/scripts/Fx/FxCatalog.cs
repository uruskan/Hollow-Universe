using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Fx;

/// <summary>Where a layer's particles fly (GROUND.md §9.3).</summary>
public enum FxDirection
{
    /// <summary>A cone around the surface's normal.</summary>
    Normal,

    /// <summary>A cone around world up.</summary>
    Up,

    /// <summary>A cone around the effect's direction (a bullet's travel: the exit spray), else the normal.</summary>
    Along,

    /// <summary>A cone around the direction reversed (back toward the shooter), else the normal.</summary>
    Back,

    /// <summary>A cone around the direction mirrored off the surface (ricochets), else the normal.</summary>
    Reflect,

    /// <summary>Every way.</summary>
    Sphere,

    /// <summary>Outward along the surface, rising by up to the cone's angle (a blast's ground ring).</summary>
    Ring,

    /// <summary>A cone around world down (drips).</summary>
    Down,
}

/// <summary>What a layer is.</summary>
public enum FxLayerType
{
    Particles,
    Light,
    Decal,
    Shake,
    Muzzle,
}

/// <summary>
/// One layer of an effect in <c>data/ground/fx.json</c>: a burst of particles into one batch, a light, a decal, a camera
/// shake or the muzzle flash. Ranges are [min, max] and picked per particle.
/// </summary>
public sealed class FxLayer
{
    public FxLayerType Type = FxLayerType.Particles;
    public string Batch = "";
    public Vector2 Count = new(1, 1);
    public Vector2 Size = new(0.1f, 0.1f);
    public Vector2 Life = new(1, 1);
    public Vector2 Speed = new(0, 0);
    public float Grow = 1f;
    public FxDirection Dir = FxDirection.Normal;
    public float Cone = 30f;
    public float Offset;
    public float Jitter;
    public bool InArea;
    public float Delay;
    public Color Color = Colors.White;
    public float Vary;
    public Vector2 Alpha = new(1, 1);
    public float Width = 0.01f;

    // Lights.
    public float Energy = 1f;
    public float Range = 5f;
    public float Duration = 0.1f;
    public float Attack;
    public float Height;

    // Decals.
    public string[] Textures = Array.Empty<string>();
    public float Depth = 0.12f;
    public float Fade = 2f;
    public float Glow;

    // Shakes.
    public float Strength = 0.5f;
    public float Radius = 20f;

    // Muzzle flashes.
    public string Style = "rifle";
}

/// <summary>A continuous effect: flames on cards, emitters with rates, a flickering light (GROUND.md §9.3).</summary>
public sealed class FxLoopDef
{
    public sealed record Flame(Vector3 Offset, Vector2 Size, float Power);

    public List<Flame> Flames { get; } = new();
    public List<(FxLayer Layer, float Rate, Vector3 Offset)> Emit { get; } = new();
    public float FlameBrightness = 3f;
    public float FlameSpeed = 1f;

    /// <summary>The share of a flame card's height its light rises over from the foot (fx.json <c>flameFoot</c>; 0: hottest
    /// at the foot). A pool of many overlapping cards sets it, so their feet don't add into bright domes.</summary>
    public float FlameFoot;
    public Color LightColor = new(1f, 0.55f, 0.25f);
    public float LightEnergy;
    public float LightRange = 6f;
    public float LightFlicker = 0.2f;
    public Vector3 LightOffset;
    public bool LightShadows;
    public float FadeIn = 0.4f;
    public float FadeOut = 0.6f;
}

/// <summary>How a tracer or a trail looks (GROUND.md §9.3).</summary>
public sealed class FxTracerDef
{
    public float Length = 0.02f;
    public float Width = 0.02f;
    public float Brightness = 10f;
    public float Opacity = 1f;
    public Color Color = new(1f, 0.8f, 0.5f);
    public float FadeIn = 0.005f;
    public float MaxTime = 3f;
}

/// <summary>One effect of <c>data/ground/fx.json</c>.</summary>
public sealed class FxDef
{
    public string Key = "";
    public string Family = "";
    public string Description = "";
    public int Cap = 16;
    public float Cull = 150f;
    public float Duration = 1f;
    public float Hero = 0.1f;
    public string Stage = "ground";
    public List<FxLayer> Layers { get; } = new();
    public FxLoopDef? Loop;
    public FxTracerDef? Tracer;

    public bool IsLoop => Loop != null;

    public bool IsTracer => Tracer != null;
}

/// <summary>
/// The effects catalogue (GROUND.md §9.3): every effect key with its layers, loops and tracers, read once from
/// <c>res://data/ground/fx.json</c>.
/// </summary>
public static class FxCatalog
{
    public const string DataPath = "res://data/ground/fx.json";

    private static List<FxDef>? _defs;
    private static Dictionary<string, FxDef>? _byKey;
    private static Dictionary<string, Dictionary<string, string>>? _items;

    /// <summary>Every how many shots a bullet draws a tracer (per shooter; Rust shows some, not all).</summary>
    public static int TracerEvery { get; private set; } = 3;

    /// <summary>The families in the order the viewer shows them.</summary>
    public static IReadOnlyList<string> Families { get; private set; } = Array.Empty<string>();

    public static IReadOnlyList<FxDef> All
    {
        get
        {
            Load();
            return _defs!;
        }
    }

    public static FxDef? Find(string key)
    {
        Load();
        return _byKey!.TryGetValue(key, out FxDef? def) ? def : null;
    }

    /// <summary>
    /// An item's effect for a role (fx.json <c>items</c>, IW4: so a new gun, round or explosive needs data, not code): a gun's
    /// <c>muzzle</c> (and <c>muzzle:&lt;attachment&gt;</c> with that part on its muzzle) and <c>backblast</c>; an attachment's
    /// own <c>muzzle</c> (a silencer's, on any gun); a round's <c>flight</c> (a loop on the round drawn as its model: a
    /// rocket's trail) and its or an explosive's <c>explode</c>; a fire's (by the item that left it) <c>fire</c> and
    /// <c>fireOut</c>; a deployable's <c>fire</c> while it burns. Null when the item has none for the role.
    /// </summary>
    public static string? ItemEffect(string itemKey, string role)
    {
        Load();
        return _items!.TryGetValue(itemKey, out Dictionary<string, string>? roles) && roles.TryGetValue(role, out string? key) ? key : null;
    }

    /// <summary>Reads the catalogue again (the viewer's R key, while tuning).</summary>
    public static void Reload()
    {
        _defs = null;
        _byKey = null;
        _items = null;
        Load();
    }

    private static void Load()
    {
        if (_defs != null)
        {
            return;
        }
        _defs = new List<FxDef>();
        _byKey = new Dictionary<string, FxDef>(StringComparer.Ordinal);
        _items = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (!FileAccess.FileExists(DataPath))
        {
            GD.PushWarning($"FxCatalog: {DataPath} is missing.");
            return;
        }
        using JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(DataPath),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("tracerEvery", out JsonElement every))
        {
            TracerEvery = Math.Max(1, every.GetInt32());
        }
        var families = new List<string>();
        if (root.TryGetProperty("families", out JsonElement fam))
        {
            foreach (JsonElement f in fam.EnumerateArray())
            {
                families.Add(f.GetString()!);
            }
        }
        Families = families;
        foreach (JsonElement e in root.GetProperty("effects").EnumerateArray())
        {
            FxDef def = ParseDef(e);
            _defs.Add(def);
            _byKey[def.Key] = def;
        }
        if (root.TryGetProperty("items", out JsonElement items))
        {
            foreach (JsonProperty item in items.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.Object)
                {
                    continue; // the note
                }
                var roles = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (JsonProperty role in item.Value.EnumerateObject())
                {
                    if (role.Value.ValueKind == JsonValueKind.String)
                    {
                        roles[role.Name] = role.Value.GetString()!;
                    }
                }
                _items[item.Name] = roles;
            }
        }
    }

    private static FxDef ParseDef(JsonElement e)
    {
        var def = new FxDef
        {
            Key = e.GetProperty("key").GetString()!,
            Family = Str(e, "family", ""),
            Description = Str(e, "about", ""),
            Cap = Int(e, "cap", 16),
            Cull = Num(e, "cull", 150f),
            Duration = Num(e, "duration", 1f),
            Hero = Num(e, "hero", 0.1f),
            Stage = Str(e, "stage", "ground"),
        };
        if (e.TryGetProperty("layers", out JsonElement layers))
        {
            foreach (JsonElement l in layers.EnumerateArray())
            {
                def.Layers.Add(ParseLayer(l));
            }
        }
        if (e.TryGetProperty("loop", out JsonElement loop))
        {
            def.Loop = ParseLoop(loop);
        }
        if (e.TryGetProperty("tracer", out JsonElement tracer))
        {
            def.Tracer = new FxTracerDef
            {
                Length = Num(tracer, "length", 0.02f),
                Width = Num(tracer, "width", 0.02f),
                Brightness = Num(tracer, "brightness", 10f),
                Opacity = Num(tracer, "opacity", 1f),
                Color = Col(tracer, "color", new Color(1f, 0.8f, 0.5f)),
                FadeIn = Num(tracer, "fadeIn", 0.005f),
                MaxTime = Num(tracer, "maxTime", 3f),
            };
        }
        return def;
    }

    private static FxLoopDef ParseLoop(JsonElement l)
    {
        var loop = new FxLoopDef
        {
            FlameBrightness = Num(l, "flameBrightness", 3f),
            FlameSpeed = Num(l, "flameSpeed", 1f),
            FlameFoot = Num(l, "flameFoot", 0f),
            FadeIn = Num(l, "fadeIn", 0.4f),
            FadeOut = Num(l, "fadeOut", 0.6f),
        };
        if (l.TryGetProperty("flames", out JsonElement flames))
        {
            foreach (JsonElement f in flames.EnumerateArray())
            {
                loop.Flames.Add(new FxLoopDef.Flame(Vec3(f, "at", Vector3.Zero), Range(f, "size", new Vector2(0.3f, 0.5f)), Num(f, "power", 1f)));
            }
        }
        if (l.TryGetProperty("emit", out JsonElement emit))
        {
            foreach (JsonElement x in emit.EnumerateArray())
            {
                loop.Emit.Add((ParseLayer(x), Num(x, "rate", 1f), Vec3(x, "at", Vector3.Zero)));
            }
        }
        if (l.TryGetProperty("light", out JsonElement light))
        {
            loop.LightColor = Col(light, "color", loop.LightColor);
            loop.LightEnergy = Num(light, "energy", 1f);
            loop.LightRange = Num(light, "range", 6f);
            loop.LightFlicker = Num(light, "flicker", 0.2f);
            loop.LightOffset = Vec3(light, "at", Vector3.Zero);
            loop.LightShadows = light.TryGetProperty("shadows", out JsonElement sh) && sh.GetBoolean();
        }
        return loop;
    }

    private static FxLayer ParseLayer(JsonElement l)
    {
        var layer = new FxLayer
        {
            Type = Str(l, "type", "particles") switch
            {
                "light" => FxLayerType.Light,
                "decal" => FxLayerType.Decal,
                "shake" => FxLayerType.Shake,
                "muzzle" => FxLayerType.Muzzle,
                _ => FxLayerType.Particles,
            },
            Batch = Str(l, "batch", ""),
            Count = Range(l, "count", new Vector2(1, 1)),
            Size = Range(l, "size", new Vector2(0.1f, 0.1f)),
            Life = Range(l, "life", new Vector2(1, 1)),
            Speed = Range(l, "speed", Vector2.Zero),
            Grow = Num(l, "grow", 1f),
            Dir = Str(l, "dir", "normal") switch
            {
                "up" => FxDirection.Up,
                "along" => FxDirection.Along,
                "back" => FxDirection.Back,
                "reflect" => FxDirection.Reflect,
                "sphere" => FxDirection.Sphere,
                "ring" => FxDirection.Ring,
                "down" => FxDirection.Down,
                _ => FxDirection.Normal,
            },
            Cone = Num(l, "cone", 30f),
            Offset = Num(l, "offset", 0f),
            Jitter = Num(l, "jitter", 0f),
            InArea = l.TryGetProperty("area", out JsonElement area) && area.GetBoolean(),
            Delay = Num(l, "delay", 0f),
            Color = Col(l, "color", Colors.White),
            Vary = Num(l, "vary", 0f),
            Alpha = Range(l, "alpha", new Vector2(1, 1)),
            Width = Num(l, "width", 0.01f),
            Energy = Num(l, "energy", 1f),
            Range = Num(l, "range", 5f),
            Duration = Num(l, "duration", 0.1f),
            Attack = Num(l, "attack", 0f),
            Height = Num(l, "height", 0f),
            Depth = Num(l, "depth", 0.12f),
            Fade = Num(l, "fade", 2f),
            Glow = Num(l, "glow", 0f),
            Strength = Num(l, "strength", 0.5f),
            Radius = Num(l, "radius", 20f),
            Style = Str(l, "style", "rifle"),
        };
        if (l.TryGetProperty("textures", out JsonElement tex))
        {
            var list = new List<string>();
            foreach (JsonElement t in tex.EnumerateArray())
            {
                list.Add(t.GetString()!);
            }
            layer.Textures = list.ToArray();
        }
        return layer;
    }

    // ---------------------------------------------------------------- JSON helpers

    private static string Str(JsonElement e, string name, string fallback) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetString() ?? fallback : fallback;

    private static int Int(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetInt32() : fallback;

    private static float Num(JsonElement e, string name, float fallback) =>
        e.TryGetProperty(name, out JsonElement v) ? v.GetSingle() : fallback;

    /// <summary>A number or a [min, max] pair.</summary>
    private static Vector2 Range(JsonElement e, string name, Vector2 fallback)
    {
        if (!e.TryGetProperty(name, out JsonElement v))
        {
            return fallback;
        }
        if (v.ValueKind == JsonValueKind.Number)
        {
            return new Vector2(v.GetSingle(), v.GetSingle());
        }
        return new Vector2(v[0].GetSingle(), v[1].GetSingle());
    }

    private static Vector3 Vec3(JsonElement e, string name, Vector3 fallback) =>
        e.TryGetProperty(name, out JsonElement v) ? new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()) : fallback;

    /// <summary>A colour as [r, g, b] (linear 0..1) or "#rrggbb" (sRGB).</summary>
    private static Color Col(JsonElement e, string name, Color fallback)
    {
        if (!e.TryGetProperty(name, out JsonElement v))
        {
            return fallback;
        }
        if (v.ValueKind == JsonValueKind.String)
        {
            return Color.FromHtml(v.GetString()!).SrgbToLinear();
        }
        return new Color(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle(), v.GetArrayLength() > 3 ? v[3].GetSingle() : 1f);
    }

    internal static string Fmt(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);
}
