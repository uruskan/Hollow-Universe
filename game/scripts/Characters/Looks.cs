using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>
/// How one player looks (GROUND.md §4.8, looks): skin tone, face, hair style and colour, stubble. The body (male or
/// female) is the player's choice; everything else comes from their ID through <see cref="Looks.ForPlayer(ulong, BodyType)"/>,
/// as in Rust. Give it to <see cref="CharacterView.Look"/> and <see cref="ViewmodelView.Look"/>.
/// </summary>
public sealed record Look
{
    public BodyType Body { get; init; }

    /// <summary>The skin's blend from the light texture (0) to the dark one (1).</summary>
    public float SkinTone { get; init; }

    /// <summary>A multiplier on the skin colour (linear): rosier, more olive, deeper.</summary>
    public Color SkinTint { get; init; } = Colors.White;

    /// <summary>0 is the base face; n is the body's blend shape face_n.</summary>
    public int Face { get; init; }

    /// <summary>A hair style of the body (<see cref="BodyAssets.HairStyles"/>), or <see cref="Looks.Buzz"/>.</summary>
    public string Hair { get; init; } = "";

    /// <summary>Hair, brows and stubble (sRGB).</summary>
    public Color HairColour { get; init; } = new(0.17f, 0.115f, 0.08f);

    /// <summary>Stubble, 0 (clean-shaven) to 1.</summary>
    public float Stubble { get; init; }

    /// <summary>Names of the skin tone and hair colour (the viewer shows them).</summary>
    public string SkinName { get; init; } = "";

    public string HairColourName { get; init; } = "";

    /// <summary>The number the look came from.</summary>
    public ulong Seed { get; init; }
}

/// <summary>
/// Deterministic looks (GROUND.md §4.8): the same ID always gives the same look, on every machine. The tables are
/// natural ranges: ten skin tones from pale to very dark, ten hair colours whose odds follow the skin tone (dark skin
/// mostly black or dark brown hair; blond, red and auburn on lighter skin), the body's MakeHuman hair styles (afro hair
/// mostly on darker skin) or a buzz cut, six faces (two lean African and East Asian, also more likely with the matching
/// tones), and stubble for men.
/// </summary>
public static class Looks
{
    /// <summary>The buzz cut: no hair cards, only the hair base painted on the scalp.</summary>
    public const string Buzz = "buzz";

    /// <summary>Skin tones: name, blend from the light to the dark texture, tint (linear).</summary>
    public static readonly (string Name, float Tone, Color Tint)[] SkinTones =
    {
        ("pale", 0.0f, new Color(1.05f, 1.04f, 1.07f)),
        ("fair", 0.0f, new Color(1f, 1f, 1f)),
        ("light olive", 0.07f, new Color(0.99f, 1.0f, 0.9f)),
        ("light tan", 0.16f, new Color(1.0f, 0.97f, 0.9f)),
        ("olive", 0.27f, new Color(0.97f, 1.0f, 0.86f)),
        ("tan", 0.4f, new Color(1.0f, 0.96f, 0.88f)),
        ("brown", 0.56f, new Color(1.0f, 0.97f, 0.94f)),
        ("dark brown", 0.72f, new Color(1f, 1f, 1f)),
        ("deep", 0.87f, new Color(0.95f, 0.95f, 0.98f)),
        ("very deep", 1.0f, new Color(0.84f, 0.85f, 0.9f)),
    };

    /// <summary>Hair colours (sRGB).</summary>
    public static readonly (string Name, Color Colour)[] HairColours =
    {
        ("black", Color.FromHtml("#16110e")),
        ("dark brown", Color.FromHtml("#2c1e15")),
        ("brown", Color.FromHtml("#46301f")),
        ("light brown", Color.FromHtml("#6b4a2f")),
        ("dark blond", Color.FromHtml("#8d6c43")),
        ("blond", Color.FromHtml("#b8935b")),
        ("ginger", Color.FromHtml("#8e4723")),
        ("auburn", Color.FromHtml("#5c2818")),
        ("grey", Color.FromHtml("#7d7873")),
        ("white", Color.FromHtml("#b4aea6")),
    };

    // Odds of each hair colour on light (tone < 0.25), middle and dark (tone > 0.5) skin.
    private static readonly float[] LightHair = { 12, 22, 22, 14, 10, 8, 5, 5, 1.5f, 0.5f };
    private static readonly float[] MiddleHair = { 35, 35, 20, 5, 0.5f, 0, 0.5f, 2, 1.5f, 0.5f };
    private static readonly float[] DarkHair = { 62, 28, 7, 1, 0, 0, 0, 0, 1.5f, 0.5f };

    private static readonly Dictionary<BodyType, string[]> Styles = new();

    /// <summary>The look of a player: <paramref name="id"/> picks everything but the body.</summary>
    public static Look ForPlayer(ulong id, BodyType body)
    {
        ulong state = id ^ 0x9E3779B97F4A7C15UL;
        float Next() => (float)((Mix(ref state) >> 40) * (1.0 / (1UL << 24)));

        int skin = Math.Min(SkinTones.Length - 1, (int)(Next() * SkinTones.Length));
        (string skinName, float tone, Color tint) = SkinTones[skin];
        tone = Math.Clamp(tone + (Next() - 0.5f) * 0.04f, 0f, 1f);

        float[] odds = tone < 0.25f ? LightHair : tone > 0.5f ? DarkHair : MiddleHair;
        int colour = Pick(odds, Next());

        string[] styles = HairStylesOf(body);
        var styleOdds = new float[styles.Length + 1];
        for (int i = 0; i < styles.Length; i++)
        {
            styleOdds[i] = styles[i].StartsWith("afro", StringComparison.Ordinal) ? 0.1f + 2.2f * tone : 1f;
        }
        // The buzz cut: common for men, rarer for women.
        styleOdds[^1] = body == BodyType.Male ? 1f : 0.35f;
        int style = Pick(styleOdds, Next());

        int faces = FacesOf(body) + 1;
        var faceOdds = new float[faces];
        for (int i = 0; i < faces; i++)
        {
            faceOdds[i] = i switch
            {
                1 => 0.3f + 2.4f * tone,          // leans African
                2 => 1.3f - 0.9f * tone,          // leans East Asian
                _ => 1f,
            };
        }
        int face = Pick(faceOdds, Next());

        float stubble = 0f;
        if (body == BodyType.Male)
        {
            float u = Next();
            stubble = u < 0.3f ? 0f : u < 0.65f ? 0.5f : 1f;
        }
        else
        {
            Next();
        }
        return new Look
        {
            Body = body,
            SkinTone = tone,
            SkinTint = tint,
            Face = face,
            Hair = style < styles.Length ? styles[style] : Buzz,
            HairColour = HairColours[colour].Colour,
            Stubble = stubble,
            SkinName = skinName,
            HairColourName = HairColours[colour].Name,
            Seed = id,
        };
    }

    /// <summary>The look of a player named by a string (an account ID or, with development logins, the name): its
    /// FNV-1a hash over UTF-8, so it is the same everywhere.</summary>
    public static Look ForPlayer(string id, BodyType body) => ForPlayer(Hash(id), body);

    /// <summary>A body's default look: light skin, the base face, its first hair style, dark brown hair.</summary>
    public static Look Default(BodyType body)
    {
        string[] styles = HairStylesOf(body);
        return new Look
        {
            Body = body,
            SkinTint = Colors.White,
            Hair = styles.Length > 0 ? styles[0] : Buzz,
            HairColour = HairColours[1].Colour,
            Stubble = body == BodyType.Male ? 0.6f : 0f,
            SkinName = SkinTones[1].Name,
            HairColourName = HairColours[1].Name,
        };
    }

    /// <summary>64-bit FNV-1a of a string's UTF-8 bytes.</summary>
    public static ulong Hash(string text)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            h = (h ^ b) * 1099511628211UL;
        }
        return h;
    }

    /// <summary>The hair styles built for a body, from its facts file (human_&lt;body&gt;.json), without loading the
    /// body itself.</summary>
    public static string[] HairStylesOf(BodyType body)
    {
        Facts(body);
        return Styles[body];
    }

    private static readonly Dictionary<BodyType, int> FaceCounts = new();

    /// <summary>How many face shapes a body has besides its base face.</summary>
    public static int FacesOf(BodyType body)
    {
        Facts(body);
        return FaceCounts[body];
    }

    private static void Facts(BodyType body)
    {
        if (Styles.ContainsKey(body))
        {
            return;
        }
        BodyType stem = body != BodyType.Male && !CharacterAssets.Has(body) ? BodyType.Male : body;
        var styles = new List<string>();
        int faces = 0;
        string path = CharacterAssets.Folder + "human_" + stem.ToString().ToLowerInvariant() + ".json";
        if (FileAccess.FileExists(path))
        {
            using JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
            if (doc.RootElement.TryGetProperty("hair_styles", out JsonElement list))
            {
                foreach (JsonElement e in list.EnumerateArray())
                {
                    styles.Add(e.GetString() ?? "");
                }
            }
            if (doc.RootElement.TryGetProperty("faces", out JsonElement f))
            {
                faces = f.GetArrayLength();
            }
        }
        Styles[body] = styles.ToArray();
        FaceCounts[body] = faces;
    }

    private static int Pick(float[] odds, float u)
    {
        float total = 0f;
        foreach (float o in odds)
        {
            total += Math.Max(0f, o);
        }
        float at = u * total;
        for (int i = 0; i < odds.Length; i++)
        {
            at -= Math.Max(0f, odds[i]);
            if (at < 0f)
            {
                return i;
            }
        }
        return odds.Length - 1;
    }

    /// <summary>SplitMix64: the next number of a stream.</summary>
    private static ulong Mix(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

/// <summary>
/// Puts a look on a character's meshes through instance uniforms, so the skin, hair and brow materials stay shared:
/// the skin's tone, stubble and hair base (character_skin), the hair and brow colour (character_hair, character_brow).
/// </summary>
internal static class LookUniforms
{
    private static readonly StringName SkinLook = "skin_look";
    private static readonly StringName SkinTint = "skin_tint";
    private static readonly StringName HairColour = "hair_colour";

    public static void Apply(IEnumerable<MeshInstance3D> meshes, Look look)
    {
        var skin = new Vector4(look.SkinTone, look.Stubble, 1f, 0f);
        var tint = new Vector3(look.SkinTint.R, look.SkinTint.G, look.SkinTint.B);
        // Brows are a little darker than the hair.
        Color brows = look.HairColour.Darkened(0.22f);
        foreach (MeshInstance3D mesh in meshes)
        {
            string name = mesh.Name;
            if (name.StartsWith("body", StringComparison.Ordinal))
            {
                mesh.SetInstanceShaderParameter(SkinLook, skin);
                mesh.SetInstanceShaderParameter(SkinTint, tint);
                mesh.SetInstanceShaderParameter(HairColour, look.HairColour);
            }
            else if (name.StartsWith("hair", StringComparison.Ordinal))
            {
                mesh.SetInstanceShaderParameter(HairColour, look.HairColour);
            }
            else if (name.StartsWith("brows", StringComparison.Ordinal))
            {
                mesh.SetInstanceShaderParameter(HairColour, brows);
            }
        }
    }
}
