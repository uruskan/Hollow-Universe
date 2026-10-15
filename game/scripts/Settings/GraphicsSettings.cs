using System;
using Godot;

namespace HollowUniverse.Settings;

/// <summary>The quality presets (GROUND.md §8.6); <see cref="Custom"/> once a single setting differs from all of them.</summary>
public enum QualityPreset
{
    Low,
    Medium,
    High,
    Ultra,
    Maximum,
    Custom,
}

/// <summary>Anti-aliasing: none, a screen-space filter, multisampling, temporal, or AMD FSR 2 (temporal, can upscale).</summary>
public enum AntiAliasingMode
{
    Off,
    Fxaa,
    Smaa,
    Msaa2,
    Msaa4,
    Taa,
    Fsr2,
}

/// <summary>
/// One set of graphics settings (GROUND.md §8.6). The presets set everything above <see cref="VSync"/>; vsync, the frame
/// cap, the field of view and the interface scale are the player's own and no preset touches them.
/// </summary>
public sealed record GraphicsValues
{
    public QualityPreset Preset { get; set; } = QualityPreset.High;
    public AntiAliasingMode AntiAliasing { get; set; } = AntiAliasingMode.Msaa2;

    /// <summary>The 3D image's resolution against the window's (0.5–1.5); below 1 FSR upscales it.</summary>
    public float RenderScale { get; set; } = 1;

    public bool Sharpen { get; set; } = true;

    /// <summary>0 (none) to 1 (strongest).</summary>
    public float SharpenStrength { get; set; } = 0.25f;

    public bool Glow { get; set; } = true;

    /// <summary>Times the look's own glow and bloom (0–2).</summary>
    public float GlowStrength { get; set; } = 1;

    public bool Ssao { get; set; } = true;
    public bool Ssil { get; set; }
    public bool Sdfgi { get; set; }
    public bool Reflections { get; set; }
    public bool Fog { get; set; }

    /// <summary>The screen-space effects' and GI's quality: 0 low, 1 medium, 2 high, 3 ultra.</summary>
    public int EffectQuality { get; set; } = 1;

    /// <summary>The sun's shadows: 0 off, 1 low, 2 medium (as before the settings), 3 high, 4 ultra.</summary>
    public int Shadows { get; set; } = 2;

    /// <summary>How far the sun's shadows reach, metres.</summary>
    public float ShadowDistance { get; set; } = 1600;

    /// <summary>Times the terrain's split distances (0.6–1.6): finer ground farther away.</summary>
    public float TerrainDetail { get; set; } = 1;

    /// <summary>Times the trees' and plants' detail distances (0.6–1.4).</summary>
    public float PlantDistance { get; set; } = 1;

    /// <summary>The share of grass tufts kept (0.4–1).</summary>
    public float GrassDensity { get; set; } = 1;

    /// <summary>Times the grass's reach, 42 m (0.6–1.6).</summary>
    public float GrassDistance { get; set; } = 1;

    /// <summary>Times the models' level-of-detail distances (0.5–2).</summary>
    public float ModelDetail { get; set; } = 1;

    /// <summary>Anisotropic filtering: 0 off, 1 2×, 2 4×, 3 8×, 4 16×.</summary>
    public int Anisotropy { get; set; } = 2;

    // ------------------------------------------------ the player's own (no preset sets them)

    /// <summary>0 off, 1 on, 2 adaptive, 3 mailbox.</summary>
    public int VSync { get; set; } = 1;

    /// <summary>An index into <see cref="GraphicsSettings.FrameCaps"/> (0: none).</summary>
    public int FrameCap { get; set; }

    /// <summary>Vertical field of view on foot, degrees (60–100); kept in ground-settings.cfg with the F1 console's fov.</summary>
    public float FieldOfView { get; set; } = 75;

    /// <summary>The interface scale preference (0.6–2), kept in settings.cfg as Ctrl + plus and minus keep it.</summary>
    public float InterfaceScale { get; set; } = 1;
}

/// <summary>
/// The game's graphics settings (GROUND.md §8.6): the values shown now (<see cref="Current"/>, a preview while the
/// settings panel is open) and those saved (<see cref="Saved"/>, <c>user://graphics.cfg</c>), the presets, and one
/// function per thing they reach: the viewport, the rendering server, the surface's environment and sun. The drawers
/// (terrain, plants, grass, models) read their distance factors from <see cref="Current"/>; whatever draws subscribes
/// to <see cref="Changed"/>.
/// </summary>
public static class GraphicsSettings
{
    private const string FilePath = "user://graphics.cfg";
    private const string ViewPath = "user://ground-settings.cfg";
    private const string InterfacePath = "user://settings.cfg";

    /// <summary>The frame caps offered (0: none).</summary>
    public static readonly int[] FrameCaps = { 0, 30, 60, 75, 90, 120, 144, 165, 240 };

    /// <summary>The values in effect now (while the settings panel is open, the preview).</summary>
    public static GraphicsValues Current { get; private set; } = new();

    /// <summary>The values last saved.</summary>
    public static GraphicsValues Saved { get; private set; } = new();

    /// <summary>Whether what is shown differs from what is saved.</summary>
    public static bool Dirty => Current != Saved;

    /// <summary>Raised after <see cref="Current"/> changes (on the main thread).</summary>
    public static event Action<GraphicsValues>? Changed;

    /// <summary>This computer's preset when nothing is saved: Medium on an integrated graphics card, otherwise High.</summary>
    public static QualityPreset DefaultPreset => RenderingServer.GetVideoAdapterType() switch
    {
        RenderingDevice.DeviceType.IntegratedGpu => QualityPreset.Medium,
        _ => QualityPreset.High,
    };

    /// <summary>The default values: this computer's preset, the player's own settings at theirs.</summary>
    public static GraphicsValues Defaults() => WithPreset(new GraphicsValues(), DefaultPreset);

    /// <summary>
    /// <paramref name="values"/> with <paramref name="preset"/>'s settings; the player's own (vsync, frame cap, field of
    /// view, interface scale) kept. High is the game as it looked before the settings existed.
    /// </summary>
    public static GraphicsValues WithPreset(GraphicsValues values, QualityPreset preset)
    {
        if (preset == QualityPreset.Custom)
        {
            return values with { Preset = QualityPreset.Custom };
        }
        int p = (int)preset; // 0 low … 4 maximum
        T Pick<T>(T low, T medium, T high, T ultra, T maximum) => p switch { 0 => low, 1 => medium, 2 => high, 3 => ultra, _ => maximum };
        return values with
        {
            Preset = preset,
            AntiAliasing = Pick(AntiAliasingMode.Fxaa, AntiAliasingMode.Fxaa, AntiAliasingMode.Msaa2, AntiAliasingMode.Msaa4, AntiAliasingMode.Msaa4),
            RenderScale = Pick(0.75f, 1f, 1f, 1f, 1f),
            Sharpen = true,
            SharpenStrength = Pick(0.4f, 0.25f, 0.25f, 0.3f, 0.35f),
            Glow = true,
            GlowStrength = 1,
            Ssao = p >= 1,
            Ssil = p >= 3,
            Sdfgi = p >= 4,
            Reflections = p >= 3,
            Fog = p >= 3,
            EffectQuality = Pick(0, 1, 1, 2, 2),
            Shadows = Pick(1, 2, 2, 3, 4),
            ShadowDistance = Pick(600f, 1000f, 1600f, 1600f, 2000f),
            TerrainDetail = Pick(0.75f, 0.9f, 1f, 1.15f, 1.35f),
            PlantDistance = Pick(0.75f, 0.9f, 1f, 1.15f, 1.3f),
            GrassDensity = Pick(0.6f, 0.8f, 1f, 1f, 1f),
            GrassDistance = Pick(0.7f, 0.85f, 1f, 1.2f, 1.4f),
            ModelDetail = Pick(0.75f, 0.9f, 1f, 1.25f, 1.5f),
            Anisotropy = Pick(1, 2, 2, 3, 4),
        };
    }

    /// <summary>The preset whose settings these are, or <see cref="QualityPreset.Custom"/>.</summary>
    public static QualityPreset Match(GraphicsValues values)
    {
        for (var p = QualityPreset.Low; p <= QualityPreset.Maximum; p++)
        {
            if (WithPreset(values, p) == (values with { Preset = p }))
            {
                return p;
            }
        }
        return QualityPreset.Custom;
    }

    /// <summary>The preset named on the command line (<c>--quality low|medium|high|ultra|maximum</c>), if it is one.</summary>
    public static QualityPreset? ParsePreset(string? name) => name?.ToLowerInvariant() switch
    {
        "low" => QualityPreset.Low,
        "medium" => QualityPreset.Medium,
        "high" => QualityPreset.High,
        "ultra" => QualityPreset.Ultra,
        "maximum" => QualityPreset.Maximum,
        _ => null,
    };

    /// <summary>
    /// Reads the saved settings (this computer's defaults where nothing is saved). <paramref name="forRun"/>, from
    /// <c>--quality</c>, shows another preset for this run without saving it.
    /// </summary>
    public static void Load(QualityPreset? forRun = null)
    {
        GraphicsValues d = Defaults();
        var file = new ConfigFile();
        GraphicsValues saved = d;
        if (file.Load(FilePath) == Error.Ok)
        {
            float F(string key, float fallback, float min, float max) =>
                Math.Clamp((float)file.GetValue("graphics", key, fallback).AsDouble(), min, max);
            int I(string key, int fallback, int min, int max) => Math.Clamp(file.GetValue("graphics", key, fallback).AsInt32(), min, max);
            bool B(string key, bool fallback) => file.GetValue("graphics", key, fallback).AsBool();
            saved = d with
            {
                AntiAliasing = (AntiAliasingMode)I("anti_aliasing", (int)d.AntiAliasing, 0, (int)AntiAliasingMode.Fsr2),
                RenderScale = F("render_scale", d.RenderScale, 0.5f, 1.5f),
                Sharpen = B("sharpen", d.Sharpen),
                SharpenStrength = F("sharpen_strength", d.SharpenStrength, 0, 1),
                Glow = B("glow", d.Glow),
                GlowStrength = F("glow_strength", d.GlowStrength, 0, 2),
                Ssao = B("ssao", d.Ssao),
                Ssil = B("ssil", d.Ssil),
                Sdfgi = B("sdfgi", d.Sdfgi),
                Reflections = B("reflections", d.Reflections),
                Fog = B("fog", d.Fog),
                EffectQuality = I("effect_quality", d.EffectQuality, 0, 3),
                Shadows = I("shadows", d.Shadows, 0, 4),
                ShadowDistance = F("shadow_distance", d.ShadowDistance, 300, 3000),
                TerrainDetail = F("terrain_detail", d.TerrainDetail, 0.6f, 1.6f),
                PlantDistance = F("plant_distance", d.PlantDistance, 0.6f, 1.4f),
                GrassDensity = F("grass_density", d.GrassDensity, 0.4f, 1f),
                GrassDistance = F("grass_distance", d.GrassDistance, 0.6f, 1.6f),
                ModelDetail = F("model_detail", d.ModelDetail, 0.5f, 2f),
                Anisotropy = I("anisotropy", d.Anisotropy, 0, 4),
                VSync = I("vsync", d.VSync, 0, 3),
                FrameCap = I("frame_cap", d.FrameCap, 0, FrameCaps.Length - 1),
            };
        }
        var view = new ConfigFile();
        if (view.Load(ViewPath) == Error.Ok)
        {
            saved = saved with { FieldOfView = Math.Clamp((float)view.GetValue("view", "fov", 75.0).AsDouble(), 60, 100) };
        }
        var ui = new ConfigFile();
        if (ui.Load(InterfacePath) == Error.Ok)
        {
            saved = saved with { InterfaceScale = Math.Clamp((float)ui.GetValue("ui", "scale", 1f).AsDouble(), 0.6f, 2f) };
        }
        saved = saved with { Preset = Match(saved) };
        Saved = saved;
        Current = forRun is { } preset ? WithPreset(saved, preset) : saved;
        Changed?.Invoke(Current);
    }

    /// <summary>Shows <paramref name="values"/> at once (the panel's preview); saved only by <see cref="Save"/>.</summary>
    public static void Preview(GraphicsValues values)
    {
        GraphicsValues shown = values with { Preset = Match(values) };
        if (shown == Current)
        {
            return;
        }
        Current = shown;
        Changed?.Invoke(Current);
    }

    /// <summary>Saves what is shown.</summary>
    public static void Save()
    {
        GraphicsValues v = Current;
        var file = new ConfigFile();
        file.SetValue("graphics", "preset", v.Preset.ToString().ToLowerInvariant());
        file.SetValue("graphics", "anti_aliasing", (int)v.AntiAliasing);
        file.SetValue("graphics", "render_scale", v.RenderScale);
        file.SetValue("graphics", "sharpen", v.Sharpen);
        file.SetValue("graphics", "sharpen_strength", v.SharpenStrength);
        file.SetValue("graphics", "glow", v.Glow);
        file.SetValue("graphics", "glow_strength", v.GlowStrength);
        file.SetValue("graphics", "ssao", v.Ssao);
        file.SetValue("graphics", "ssil", v.Ssil);
        file.SetValue("graphics", "sdfgi", v.Sdfgi);
        file.SetValue("graphics", "reflections", v.Reflections);
        file.SetValue("graphics", "fog", v.Fog);
        file.SetValue("graphics", "effect_quality", v.EffectQuality);
        file.SetValue("graphics", "shadows", v.Shadows);
        file.SetValue("graphics", "shadow_distance", v.ShadowDistance);
        file.SetValue("graphics", "terrain_detail", v.TerrainDetail);
        file.SetValue("graphics", "plant_distance", v.PlantDistance);
        file.SetValue("graphics", "grass_density", v.GrassDensity);
        file.SetValue("graphics", "grass_distance", v.GrassDistance);
        file.SetValue("graphics", "model_detail", v.ModelDetail);
        file.SetValue("graphics", "anisotropy", v.Anisotropy);
        file.SetValue("graphics", "vsync", v.VSync);
        file.SetValue("graphics", "frame_cap", v.FrameCap);
        file.Save(FilePath);
        var view = new ConfigFile();
        view.Load(ViewPath);
        view.SetValue("view", "fov", v.FieldOfView);
        view.Save(ViewPath);
        var ui = new ConfigFile();
        ui.Load(InterfacePath);
        ui.SetValue("ui", "scale", v.InterfaceScale);
        ui.Save(InterfacePath);
        Saved = v;
    }

    /// <summary>Back to what is saved.</summary>
    public static void Revert() => Preview(Saved);

    /// <summary>The field of view set elsewhere (the F1 console's fov, saved by it): shown and taken as saved.</summary>
    public static void SetFieldOfView(float degrees)
    {
        float fov = Math.Clamp(degrees, 60, 100);
        Saved = Saved with { FieldOfView = fov };
        Preview(Current with { FieldOfView = fov });
    }

    /// <summary>The interface scale set elsewhere (Ctrl + plus and minus, saved by the scaler): shown and taken as saved.</summary>
    public static void SetInterfaceScale(float scale)
    {
        Saved = Saved with { InterfaceScale = scale };
        Preview(Current with { InterfaceScale = scale });
    }

    // ------------------------------------------------------------------ applying

    /// <summary>
    /// The window's viewport and everything global: anti-aliasing, render scale and upscaling, FSR's sharpness,
    /// debanding, texture filtering, the models' automatic LOD, the shadow atlas and its filter, the screen-space
    /// effects' and GI's quality, the fog's volume, vsync and the frame cap. Returns whether the sharpening pass should
    /// draw (when FSR does not sharpen already).
    /// </summary>
    public static bool ApplyGlobal(Viewport viewport, GraphicsValues v)
    {
        AntiAliasingMode aa = v.AntiAliasing;
        viewport.Msaa3D = aa switch
        {
            AntiAliasingMode.Msaa2 => Viewport.Msaa.Msaa2X,
            AntiAliasingMode.Msaa4 => Viewport.Msaa.Msaa4X,
            _ => Viewport.Msaa.Disabled,
        };
        viewport.ScreenSpaceAA = aa switch
        {
            AntiAliasingMode.Fxaa => Viewport.ScreenSpaceAAEnum.Fxaa,
            AntiAliasingMode.Smaa => Viewport.ScreenSpaceAAEnum.Smaa,
            _ => Viewport.ScreenSpaceAAEnum.Disabled,
        };
        viewport.UseTaa = aa == AntiAliasingMode.Taa;
        // FSR 2 is temporal anti-aliasing and an upscaler in one (at 100 %, native anti-aliasing); otherwise FSR 1
        // upscales a smaller image, and a larger one is downsampled.
        bool fsr2 = aa == AntiAliasingMode.Fsr2;
        float scale = fsr2 ? Math.Min(v.RenderScale, 1f) : v.RenderScale;
        viewport.Scaling3DMode = fsr2 ? Viewport.Scaling3DModeEnum.Fsr2
            : scale < 0.999f ? Viewport.Scaling3DModeEnum.Fsr
            : Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = scale;
        bool fsr = viewport.Scaling3DMode != Viewport.Scaling3DModeEnum.Bilinear;
        // FSR's own sharpening (0 sharpest, 2 none) when FSR draws; our pass otherwise.
        viewport.FsrSharpness = v.Sharpen ? 2f * (1f - v.SharpenStrength) : 2f;
        viewport.UseDebanding = true;
        viewport.AnisotropicFilteringLevel = (Viewport.AnisotropicFiltering)Math.Clamp(v.Anisotropy, 0, 4);
        // Godot's automatic mesh LOD: the error in pixels a simpler level may make (1 by default).
        viewport.MeshLodThreshold = 1f / Math.Max(v.ModelDetail, 0.1f);

        (int atlas, RenderingServer.ShadowQuality filter) = v.Shadows switch
        {
            0 or 1 => (2048, RenderingServer.ShadowQuality.SoftVeryLow),
            2 => (4096, RenderingServer.ShadowQuality.SoftLow),
            3 => (4096, RenderingServer.ShadowQuality.SoftMedium),
            _ => (4096, RenderingServer.ShadowQuality.SoftHigh),
        };
        RenderingServer.DirectionalShadowAtlasSetSize(atlas, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(filter);
        RenderingServer.PositionalSoftShadowFilterSetQuality(filter);

        int q = Math.Clamp(v.EffectQuality, 0, 3);
        RenderingServer.EnvironmentSetSsaoQuality((RenderingServer.EnvironmentSsaoQuality)(q + 1), q == 0, 0.5f, 2, 50, 300);
        RenderingServer.EnvironmentSetSsilQuality((RenderingServer.EnvironmentSsilQuality)(q + 1), q < 2, 0.5f, q >= 2 ? 3 : 2, 50, 300);
        RenderingServer.EnvironmentSetSsrHalfSize(q < 2);
        RenderingServer.GISetUseHalfResolution(q < 2);
        RenderingServer.EnvironmentSetSdfgiRayCount(q switch
        {
            0 => RenderingServer.EnvironmentSdfgiRayCount.Count16,
            1 => RenderingServer.EnvironmentSdfgiRayCount.Count32,
            2 => RenderingServer.EnvironmentSdfgiRayCount.Count64,
            _ => RenderingServer.EnvironmentSdfgiRayCount.Count96,
        });
        int froxels = q switch { 0 => 48, 1 => 64, 2 => 96, _ => 128 };
        RenderingServer.EnvironmentSetVolumetricFogVolumeSize(froxels, froxels);
        RenderingServer.EnvironmentSetVolumetricFogFilterActive(true);
        RenderingServer.EnvironmentGlowSetUseBicubicUpscale(q >= 2);

        Engine.MaxFps = FrameCaps[Math.Clamp(v.FrameCap, 0, FrameCaps.Length - 1)];
        DisplayServer.VSyncMode vsync = v.VSync switch
        {
            0 => DisplayServer.VSyncMode.Disabled,
            2 => DisplayServer.VSyncMode.Adaptive,
            3 => DisplayServer.VSyncMode.Mailbox,
            _ => DisplayServer.VSyncMode.Enabled,
        };
        if (DisplayServer.WindowGetVsyncMode() != vsync)
        {
            DisplayServer.WindowSetVsyncMode(vsync);
        }
        return v.Sharpen && v.SharpenStrength > 0.001f && !fsr;
    }

    /// <summary>The look of the air the volumetric fog stands for: its density per metre, colour and forward scattering.</summary>
    public readonly record struct FogLook(float Density, Color Albedo, float Anisotropy);

    // The surface's own look (SurfaceView): glow and bloom at strength 1, as they were.
    private const float GlowIntensity = 0.35f;
    private const float GlowBloom = 0.02f;

    /// <summary>
    /// A surface's environment: glow and bloom, SSAO, SSIL, SDFGI sized for a small world, screen-space reflections and
    /// faint volumetric fog in the air's colours. Exposure stays the sky's (SurfaceView sets it every frame).
    /// </summary>
    public static void ApplyTo(Godot.Environment env, GraphicsValues v, FogLook fog)
    {
        int q = Math.Clamp(v.EffectQuality, 0, 3);
        env.GlowEnabled = v.Glow && v.GlowStrength > 0.001f;
        env.GlowIntensity = GlowIntensity * v.GlowStrength;
        env.GlowBloom = GlowBloom * v.GlowStrength;
        env.SsaoEnabled = v.Ssao && OS.GetEnvironment("HU_NO_SSAO").Length == 0;

        env.SsilEnabled = v.Ssil;
        env.SsilRadius = 4;
        env.SsilIntensity = 0.7f;

        // SDFGI on a ball 2.41 km across: cascades follow the camera along the world's axes, which are not the local up
        // anywhere but at the poles, so the vertical scale stays 100 % (the flat-world 50 % would halve the detail
        // sideways wherever the ground is not level in the world's frame). A 0.2 m first cell and 5–7 cascades reach
        // 410–1,640 m, past what the ground hides; the sky lights the rest.
        env.SdfgiEnabled = v.Sdfgi;
        env.SdfgiYScale = Godot.Environment.SdfgiyScale.Scale100Percent;
        env.SdfgiCascades = q switch { 0 => 4, 1 => 5, 2 => 6, _ => 7 };
        env.SdfgiMinCellSize = 0.2f;
        env.SdfgiUseOcclusion = true;
        env.SdfgiReadSkyLight = true;
        env.SdfgiBounceFeedback = 0.5f;
        env.SdfgiEnergy = 1;

        env.SsrEnabled = v.Reflections;
        env.SsrMaxSteps = q switch { 0 => 32, 1 => 48, 2 => 64, _ => 96 };
        env.SsrFadeIn = 0.15f;
        env.SsrFadeOut = 2;
        env.SsrDepthTolerance = 0.2f;

        // Light, faint fog in the air's colours: shafts of sun through the trees, never a wall; the sky draws its own air.
        env.VolumetricFogEnabled = v.Fog;
        env.VolumetricFogDensity = fog.Density;
        env.VolumetricFogAlbedo = fog.Albedo;
        env.VolumetricFogAnisotropy = fog.Anisotropy;
        env.VolumetricFogLength = q >= 2 ? 256 : 160;
        env.VolumetricFogDetailSpread = 2;
        env.VolumetricFogSkyAffect = 0;
        env.VolumetricFogAmbientInject = 0.3f;
        env.VolumetricFogGIInject = 0.5f;
        env.VolumetricFogTemporalReprojectionEnabled = true;
    }

    /// <summary>The sun's shadows: on or off, their reach, and their cascades (tighter near the camera at ultra).</summary>
    public static void ApplyTo(DirectionalLight3D sun, GraphicsValues v)
    {
        sun.ShadowEnabled = v.Shadows > 0;
        sun.DirectionalShadowMaxDistance = v.ShadowDistance;
        bool tight = v.Shadows >= 4;
        // Godot's own splits at 1,600 m put the first cascade's edge 160 m out; ultra keeps it near 80 m however far
        // the shadows reach, so the ground at the feet stays sharp.
        sun.DirectionalShadowSplit1 = tight ? Math.Clamp(80f / v.ShadowDistance, 0.02f, 0.1f) : 0.1f;
        sun.DirectionalShadowSplit2 = tight ? Math.Clamp(260f / v.ShadowDistance, 0.06f, 0.2f) : 0.2f;
        sun.DirectionalShadowSplit3 = tight ? 0.45f : 0.5f;
    }
}
