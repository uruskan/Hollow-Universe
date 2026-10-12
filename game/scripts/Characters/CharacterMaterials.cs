using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Characters;

/// <summary>
/// Materials of the character surfaces, by the surface (material) names the art scripts export. Skin and garments
/// use the character shaders (body regions, first-person drawing); hair, brows, lashes and eyes are standard
/// materials. Garment settings come from clothing.json.
/// </summary>
public static class CharacterMaterials
{
    private const string Textures = CharacterAssets.Folder + "textures/";
    private static Dictionary<string, JsonElement>? _garments;

    /// <summary>The material of a surface; <paramref name="viewmodel"/> picks the first-person variant.</summary>
    public static Material Create(string surface, bool viewmodel) => viewmodel ? ForViewmodel(Create(surface, false)) : surface switch
    {
        "skin" => Skin(),
        "underwear" => Cloth(new Color(0.105f, 0.11f, 0.125f), "knit_normal.png", 46f, 0.7f, 0.86f),
        "eye" => new StandardMaterial3D
        {
            AlbedoTexture = Texture("eye_albedo.png"),
            AlbedoColor = new Color(0.86f, 0.84f, 0.82f),
            // The outer shell (cornea) maps onto a fully transparent patch of the texture.
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.5f,
            Roughness = 0.18f,
            MetallicSpecular = 0.5f,
            // A wet eye: a sharp highlight over the iris and white.
            ClearcoatEnabled = true,
            Clearcoat = 1f,
            ClearcoatRoughness = 0.02f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        },
        "brow" or "lash" => Cards(surface + "_albedo.png", null, 0.75f, surface == "lash" ? 0.35f : 0.3f),
        "hair" => Cards("hair_albedo.png", "hair_normal.png", 0.55f, 0.3f, BaseMaterial3D.CullModeEnum.Back, blended: true),
        _ => Garment(surface),
    };

    private static readonly List<Material> ViewmodelMaterials = new();

    /// <summary>Vertical field of view of the first-person arms and items, degrees (a player setting).</summary>
    public static float ViewmodelFov { get; private set; } = 60f;

    /// <summary>Changes the first-person field of view of every first-person material made so far and to come.</summary>
    public static void SetViewmodelFov(float degrees)
    {
        ViewmodelFov = Mathf.Clamp(degrees, 40f, 100f);
        foreach (Material m in ViewmodelMaterials)
        {
            if (m is ShaderMaterial sm)
            {
                sm.SetShaderParameter("viewmodel_fov", ViewmodelFov);
            }
            else if (m is BaseMaterial3D bm)
            {
                bm.FovOverride = ViewmodelFov;
            }
        }
    }

    /// <summary>The first-person copy of a material: the viewmodel shader variant, or a standard material with its FOV
    /// override and depth pulled towards the near plane.</summary>
    public static Material ForViewmodel(Material normal)
    {
        var copy = (Material)normal.Duplicate();
        ViewmodelMaterials.Add(copy);
        if (copy is ShaderMaterial sm)
        {
            string path = sm.Shader.ResourcePath.Replace(".gdshader", "_viewmodel.gdshader");
            sm.Shader = GD.Load<Shader>(path);
            sm.SetShaderParameter("viewmodel_fov", ViewmodelFov);
            if (path.Contains("character_skin", System.StringComparison.Ordinal))
            {
                // Seen from a hand's length away: finer, fainter pores.
                sm.SetShaderParameter("detail_tiling", 46f);
                sm.SetShaderParameter("detail_strength", 0.22f);
            }
        }
        else if (copy is BaseMaterial3D bm)
        {
            bm.UseFovOverride = true;
            bm.FovOverride = ViewmodelFov;
            bm.UseZClipScale = true;
            bm.ZClipScale = 0.12f;
        }
        return copy;
    }

    private static ShaderMaterial Skin()
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/character_skin.gdshader") };
        m.SetShaderParameter("albedo_texture", Texture("skin_male_albedo.png"));
        m.SetShaderParameter("detail_normal", Texture("skin_detail_normal.png"));
        if (Exists("skin_male_normal.png"))
        {
            m.SetShaderParameter("normal_texture", Texture("skin_male_normal.png"));
        }
        else
        {
            m.SetShaderParameter("normal_strength", 0f);
        }
        return m;
    }

    private static StandardMaterial3D Cards(string albedo, string? normal, float roughness, float threshold,
        BaseMaterial3D.CullModeEnum cull = BaseMaterial3D.CullModeEnum.Disabled, bool blended = false)
    {
        var m = new StandardMaterial3D
        {
            AlbedoTexture = Texture(albedo),
            // Hair blends its thin ends over a depth pre-pass of its solid middle, so the hairline is soft, not cut.
            Transparency = blended ? BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass : BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = threshold,
            AlphaAntialiasingMode = BaseMaterial3D.AlphaAntiAliasing.AlphaToCoverage,
            CullMode = cull,
            Roughness = roughness,
            MetallicSpecular = 0.3f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        if (normal != null)
        {
            m.NormalEnabled = true;
            m.NormalTexture = Texture(normal);
            m.NormalScale = 0.6f;
        }
        return m;
    }

    private static ShaderMaterial Cloth(Color tint, string detail, float tiling, float detailStrength, float roughness)
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/character_cloth.gdshader") };
        m.SetShaderParameter("tint", tint);
        m.SetShaderParameter("roughness", roughness);
        if (Exists(detail))
        {
            m.SetShaderParameter("detail_normal", Texture(detail));
            m.SetShaderParameter("detail_tiling", tiling);
            m.SetShaderParameter("detail_strength", detailStrength);
        }
        else
        {
            m.SetShaderParameter("detail_strength", 0f);
        }
        return m;
    }

    /// <summary>A garment: its own albedo, normal and (for armour) ORM textures, plus a tiled weave or grain.</summary>
    private static Material Garment(string surface)
    {
        JsonElement spec = GarmentSpec(surface);
        string detail = (spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty("detail", out JsonElement d)
            ? d.GetString() : "weave") + "_normal.png";
        ShaderMaterial m = Cloth(Colors.White, detail, Number(spec, "tiling", 40f), Number(spec, "detail_strength", 0.6f),
            Number(spec, "roughness", 0.9f));
        if (Exists(surface + "_albedo.png"))
        {
            m.SetShaderParameter("albedo_texture", Texture(surface + "_albedo.png"));
        }
        if (Exists(surface + "_normal.png"))
        {
            m.SetShaderParameter("normal_texture", Texture(surface + "_normal.png"));
        }
        if (Exists(surface + "_orm.png"))
        {
            m.SetShaderParameter("orm_texture", Texture(surface + "_orm.png"));
            m.SetShaderParameter("use_orm", true);
        }
        m.SetShaderParameter("metallic", Number(spec, "metallic", 0f));
        m.SetShaderParameter("specular_level", Number(spec, "specular", 0.35f));
        m.SetShaderParameter("normal_strength", Number(spec, "normal_strength", 1f));
        return m;
    }

    private static float Number(JsonElement spec, string key, float fallback) =>
        spec.ValueKind == JsonValueKind.Object && spec.TryGetProperty(key, out JsonElement v) ? v.GetSingle() : fallback;

    private static JsonElement GarmentSpec(string surface)
    {
        if (_garments == null)
        {
            _garments = new Dictionary<string, JsonElement>();
            string path = CharacterAssets.Folder + "clothing.json";
            if (FileAccess.FileExists(path))
            {
                JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString(path));
                if (doc.RootElement.TryGetProperty("surfaces", out JsonElement surfaces))
                {
                    foreach (JsonProperty p in surfaces.EnumerateObject())
                    {
                        _garments[p.Name] = p.Value.Clone();
                    }
                }
            }
        }
        return _garments.TryGetValue(surface, out JsonElement e) ? e : default;
    }

    private static bool Exists(string file) => ResourceLoader.Exists(Textures + file);

    private static Texture2D Texture(string file) => GD.Load<Texture2D>(Textures + file);
}
