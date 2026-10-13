using System;
using System.Collections.Generic;
using Godot;
using Hollow.Simulation.Content;
using HollowUniverse.Fx;

namespace HollowUniverse.Ground;

/// <summary>
/// The weak spots drawn (GROUND.md §6.1, §9.3), one generic marker for every class: gather.json's
/// <c>look.marker</c> says what it is. A marker is a decal pressed onto the drawn surface where the rules put the spot
/// (paint on bark, a glow on rock: it follows the trunk's curve and the rock's bumps), an optional glint over it that
/// pulses, and an optional effect sparkling on it now and then. Pooled per style (a decal keeps its texture for good:
/// changing it rebuilds Godot's decal atlas); at most <see cref="Max"/> show at once; nothing is allocated per frame.
/// </summary>
public partial class NatureMarkers : Node3D
{
    /// <summary>Markers shown at most.</summary>
    public const int Max = 8;

    private sealed class Marker
    {
        public Decal Decal = null!;
        public MeshInstance3D? Glint;
        public StandardMaterial3D? GlintMaterial;
        public MarkerLook Look = null!;
        public Vector3 Point;
        public Vector3 Normal;
        public float Phase;
        public double NextSparkle;
        public bool Shown;
    }

    private readonly Dictionary<string, List<Marker>> _pools = new(StringComparer.Ordinal);
    private readonly List<Marker> _shown = new();
    private static QuadMesh? _quad;

    /// <summary>Starts a new set: every marker shown since the last <see cref="Begin"/> is hidden unless shown again.</summary>
    public void Begin()
    {
        foreach (Marker m in _shown)
        {
            m.Shown = false;
        }
        _shown.Clear();
    }

    /// <summary>
    /// A marker of <paramref name="look"/> on the surface at <paramref name="point"/>, facing out along
    /// <paramref name="normal"/>; <paramref name="up"/> is the object's up (the paint's top). <paramref name="seed"/> keeps
    /// one object's pulse and sparkles in step from frame to frame.
    /// </summary>
    public void Show(MarkerLook look, Vector3 point, Vector3 normal, Vector3 up, ulong seed)
    {
        if (_shown.Count >= Max || MarkerTextures.For(look.Style) is not { } textures)
        {
            return;
        }
        Marker? marker = null;
        List<Marker> pool = Pool(look.Style);
        foreach (Marker m in pool)
        {
            if (!m.Shown)
            {
                marker = m;
                break;
            }
        }
        if (marker == null)
        {
            marker = Make(look, textures);
            pool.Add(marker);
        }
        marker.Shown = true;
        marker.Look = look;
        marker.Point = point;
        marker.Normal = normal;
        marker.Phase = (seed % 997) / 997f * Mathf.Tau;
        _shown.Add(marker);
        // The paint lies on the surface: projected along the normal, its top toward the object's up where it can be.
        Vector3 n = normal.LengthSquared() > 1e-8f ? normal.Normalized() : Vector3.Up;
        Vector3 across = up.Cross(n);
        if (across.LengthSquared() < 1e-4f)
        {
            across = n.Cross(Vector3.Right.Dot(n) < 0.9f ? Vector3.Right : Vector3.Forward);
        }
        across = across.Normalized();
        Decal decal = marker.Decal;
        decal.GlobalTransform = new Transform3D(new Basis(across, n, across.Cross(n)), point);
        decal.Size = new Vector3(look.Size, look.Depth, look.Size);
        decal.Modulate = new Color(look.Colour.X, look.Colour.Y, look.Colour.Z);
        decal.Visible = true;
        if (marker.Glint != null)
        {
            marker.Glint.Position = point + n * 0.04f;
            marker.Glint.Visible = true;
        }
    }

    /// <summary>Hides what wasn't shown since <see cref="Begin"/>, pulses the glows and plays the sparkles.</summary>
    public void Animate(double now)
    {
        float brightness = FxDirector.Brightness;
        foreach (List<Marker> pool in _pools.Values)
        {
            foreach (Marker m in pool)
            {
                if (!m.Shown)
                {
                    if (m.Decal.Visible)
                    {
                        m.Decal.Visible = false;
                        if (m.Glint != null)
                        {
                            m.Glint.Visible = false;
                        }
                    }
                    continue;
                }
                MarkerLook look = m.Look;
                float pulse = look.Pulse > 0 ? 0.7f + 0.3f * MathF.Sin((float)(now * look.Pulse * Mathf.Tau) + m.Phase) : 1f;
                m.Decal.EmissionEnergy = look.Glow * pulse * brightness;
                if (m.Glint != null && m.GlintMaterial != null)
                {
                    float twinkle = 0.55f + 0.45f * MathF.Pow(MathF.Max(0, MathF.Sin((float)(now * look.Pulse * Mathf.Tau * 1.7) + m.Phase * 2)), 3);
                    m.Glint.Scale = Vector3.One * (look.Glint * (0.75f + 0.5f * twinkle));
                    float k = look.Glow * twinkle * brightness;
                    m.GlintMaterial.AlbedoColor = new Color(look.Colour.X * k, look.Colour.Y * k, look.Colour.Z * k, 1);
                }
                if (look.Sparkle != null && now >= m.NextSparkle)
                {
                    if (m.NextSparkle > 0)
                    {
                        FxDirector.Play(look.Sparkle, m.Point + m.Normal * 0.03f, m.Normal);
                    }
                    m.NextSparkle = now + look.SparkleEvery * (0.75 + 0.5 * ((m.Phase * 7.3) % 1.0));
                }
            }
        }
    }

    /// <summary>One marker of a style, made ahead (while the world opens) so its first show doesn't stall a frame.</summary>
    public void Prepare(MarkerLook look)
    {
        if (MarkerTextures.For(look.Style) is { } textures && Pool(look.Style).Count == 0)
        {
            Pool(look.Style).Add(Make(look, textures));
        }
    }

    /// <summary>Shows every marker made so far where it stands (the world's warm-up, under a tiny node before the camera).</summary>
    public void RevealForWarmup()
    {
        foreach (List<Marker> pool in _pools.Values)
        {
            foreach (Marker m in pool)
            {
                m.Decal.Size = Vector3.One * 0.5f;
                m.Decal.Visible = true;
                if (m.Glint != null)
                {
                    m.Glint.Visible = true;
                }
            }
        }
    }

    private List<Marker> Pool(string style)
    {
        if (!_pools.TryGetValue(style, out List<Marker>? pool))
        {
            pool = new List<Marker>();
            _pools[style] = pool;
        }
        return pool;
    }

    private Marker Make(MarkerLook look, MarkerTextures.Style textures)
    {
        var marker = new Marker
        {
            Decal = new Decal
            {
                Name = "Marker_" + look.Style,
                Visible = false,
                TextureAlbedo = textures.Albedo,
                TextureEmission = look.Glow > 0 ? textures.Emission : null,
                // Not on the arms and tools in view, nor on the effects.
                CullMask = 0xFFFFF & ~(1u << 1) & ~FxDirector.RenderLayer,
                UpperFade = 0.2f,
                LowerFade = 0.35f,
                NormalFade = 0.3f,
                DistanceFadeEnabled = true,
                DistanceFadeBegin = 18f,
                DistanceFadeLength = 6f,
            },
        };
        AddChild(marker.Decal);
        if (look.Glint > 0)
        {
            _quad ??= new QuadMesh { Size = Vector2.One };
            marker.GlintMaterial = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                AlbedoTexture = MarkerTextures.Glint(),
                DisableReceiveShadows = true,
                DisableFog = true,
            };
            marker.Glint = new MeshInstance3D
            {
                Name = "Glint_" + look.Style,
                Mesh = _quad,
                MaterialOverride = marker.GlintMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
                Layers = FxDirector.RenderLayer,
            };
            AddChild(marker.Glint);
        }
        return marker;
    }
}
