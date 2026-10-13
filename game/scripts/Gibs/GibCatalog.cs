using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Hollow.Simulation;

namespace HollowUniverse.Gibs;

/// <summary>A material's pieces (gibs.json): how they bounce, slide and spin, their dust (an effect, tinted) and sound.</summary>
public sealed record GibMaterial(string Key, float Bounce, float Friction, float Spin, string Dust, Color Colour, string? Sound);

/// <summary>How a cause throws the pieces (gibs.json): push along the blow, spread from the middle, lift, spin, the dust's size, a sound that wins.</summary>
public sealed record GibCause(Vector2 Push, Vector2 Spread, Vector2 Lift, Vector2 Spin, float Dust, string? Sound);

/// <summary>
/// The numbers of the gib runtime (GROUND.md §9.4), read from <c>res://data/ground/gibs.json</c> once: the limits, the
/// integrator's physics, the pieces' life, every material and every cause. Which pieces a model breaks into is the model
/// catalogue's (<see cref="Items.GibsInfo"/>).
/// </summary>
public static class GibCatalog
{
    public const string DataPath = "res://data/ground/gibs.json";

    private static bool _loaded;
    private static readonly Dictionary<string, GibMaterial> _materials = new(StringComparer.Ordinal);
    private static readonly GibCause[] _causes = new GibCause[8];
    private static readonly GibMaterial Fallback = new("default", 0.2f, 0.6f, 1f, "gibs.dust.stone", new Color(0.6f, 0.58f, 0.54f), null);

    /// <summary>Pieces alive at once; the oldest go first.</summary>
    public static int MaxPieces { get; private set; } = 640;

    /// <summary>A break shows its pieces within this distance of the camera; beyond, its dust only.</summary>
    public static float Near { get; private set; } = 70;

    /// <summary>Nothing shows beyond this distance.</summary>
    public static float Far { get; private set; } = 170;

    /// <summary>Above this share of <see cref="MaxPieces"/> alive, a break keeps only its bigger pieces.</summary>
    public static float Busy { get; private set; } = 0.75f;

    public static float Gravity { get; private set; } = 9.81f;
    public static float AirDrag { get; private set; } = 0.06f;
    public static float SpinDrag { get; private set; } = 0.25f;
    public static float ContactSpinDrag { get; private set; } = 3.5f;

    /// <summary>Hits slower than this (m/s into the floor) don't bounce.</summary>
    public static float BounceStop { get; private set; } = 0.6f;

    public static float SleepSpeed { get; private set; } = 0.18f;
    public static float SleepSpin { get; private set; } = 0.7f;
    public static float SleepTime { get; private set; } = 0.3f;

    /// <summary>How far under a piece its floor is looked for.</summary>
    public static float FloorDepth { get; private set; } = 40;

    /// <summary>The longest a piece lives, at rest or not.</summary>
    public static float MaxLife { get; private set; } = 14;

    /// <summary>Seconds a piece lies at rest before it sinks.</summary>
    public static Vector2 Rest { get; private set; } = new(3.5f, 5.5f);

    /// <summary>Seconds it takes to sink away, how deep (in its own sizes) and how much it shrinks meanwhile.</summary>
    public static float Sink { get; private set; } = 1.6f;
    public static float SinkDepth { get; private set; } = 1.2f;
    public static float Shrink { get; private set; } = 0.35f;

    public static IReadOnlyDictionary<string, GibMaterial> Materials
    {
        get
        {
            Load();
            return _materials;
        }
    }

    public static GibMaterial Material(string? key)
    {
        Load();
        return key != null && _materials.TryGetValue(key, out GibMaterial? m) ? m : Fallback;
    }

    public static GibCause Cause(DestroyCause cause)
    {
        Load();
        return _causes[Math.Clamp((int)cause, 0, _causes.Length - 1)];
    }

    /// <summary>Reads the file again (the viewer, while tuning).</summary>
    public static void Reload()
    {
        _loaded = false;
        Load();
    }

    private static void Load()
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        _materials.Clear();
        var still = new GibCause(new Vector2(0, 0.3f), new Vector2(0.2f, 0.8f), Vector2.Zero, new Vector2(0.5f, 2f), 1f, null);
        Array.Fill(_causes, still);
        if (!FileAccess.FileExists(DataPath))
        {
            GD.PushWarning($"GibCatalog: {DataPath} is missing.");
            return;
        }
        using JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(DataPath));
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("limits", out JsonElement limits))
        {
            MaxPieces = Math.Clamp((int)Number(limits, "pieces", MaxPieces), 16, 8192);
            Near = Number(limits, "near", Near);
            Far = Math.Max(Near, Number(limits, "far", Far));
            Busy = Math.Clamp(Number(limits, "busy", Busy), 0.1f, 1f);
        }
        if (root.TryGetProperty("physics", out JsonElement p))
        {
            Gravity = Number(p, "gravity", Gravity);
            AirDrag = Number(p, "airDrag", AirDrag);
            SpinDrag = Number(p, "spinDrag", SpinDrag);
            ContactSpinDrag = Number(p, "contactSpinDrag", ContactSpinDrag);
            BounceStop = Number(p, "bounceStop", BounceStop);
            SleepSpeed = Number(p, "sleepSpeed", SleepSpeed);
            SleepSpin = Number(p, "sleepSpin", SleepSpin);
            SleepTime = Number(p, "sleepTime", SleepTime);
            FloorDepth = Number(p, "floorDepth", FloorDepth);
            MaxLife = Number(p, "maxLife", MaxLife);
        }
        if (root.TryGetProperty("life", out JsonElement life))
        {
            Rest = Range(life, "rest", Rest);
            Sink = Math.Max(0.05f, Number(life, "sink", Sink));
            SinkDepth = Number(life, "sinkDepth", SinkDepth);
            Shrink = Math.Clamp(Number(life, "shrink", Shrink), 0f, 1f);
        }
        if (root.TryGetProperty("materials", out JsonElement materials))
        {
            foreach (JsonProperty m in materials.EnumerateObject())
            {
                JsonElement e = m.Value;
                Color colour = e.TryGetProperty("colour", out JsonElement c) && c.GetArrayLength() >= 3
                    ? new Color(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle())
                    : Fallback.Colour;
                _materials[m.Name] = new GibMaterial(m.Name, Number(e, "bounce", 0.2f), Number(e, "friction", 0.6f), Number(e, "spin", 1f),
                    e.TryGetProperty("dust", out JsonElement dust) ? dust.GetString() ?? Fallback.Dust : Fallback.Dust, colour,
                    e.TryGetProperty("sound", out JsonElement sound) ? sound.GetString() : null);
            }
        }
        if (root.TryGetProperty("causes", out JsonElement causes))
        {
            foreach (DestroyCause cause in Enum.GetValues<DestroyCause>())
            {
                if (causes.TryGetProperty(cause.ToString().ToLowerInvariant(), out JsonElement e))
                {
                    _causes[(int)cause] = new GibCause(Range(e, "push", still.Push), Range(e, "spread", still.Spread), Range(e, "lift", still.Lift),
                        Range(e, "spin", still.Spin), Number(e, "dust", 1f), e.TryGetProperty("sound", out JsonElement s) ? s.GetString() : null);
                }
            }
        }
    }

    private static float Number(JsonElement e, string name, float fallback) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    private static Vector2 Range(JsonElement e, string name, Vector2 fallback)
    {
        if (!e.TryGetProperty(name, out JsonElement v))
        {
            return fallback;
        }
        return v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 2 ? new Vector2(v[0].GetSingle(), v[1].GetSingle())
            : v.ValueKind == JsonValueKind.Number ? new Vector2(v.GetSingle(), v.GetSingle()) : fallback;
    }
}
