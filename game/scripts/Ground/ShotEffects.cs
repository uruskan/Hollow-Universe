using Godot;
using Hollow.Simulation;

namespace HollowUniverse.Ground;

/// <summary>
/// What a shot looks like (GROUND.md §6.4): the muzzle flash, the tracer a bullet draws, and the puff where it stops.
/// The effects package will provide these; until then <see cref="PlainShotEffects"/> makes them in code. Swap
/// <see cref="ShotEffects.Provider"/> to drop the package's in.
/// </summary>
public interface IShotEffects
{
    /// <summary>A muzzle flash at a point (render space) firing along a direction; it goes by itself.</summary>
    void MuzzleFlash(Node parent, Vector3 at, Vector3 direction);

    /// <summary>A tracer for one bullet; the caller places it every frame, its −Z along the flight, and frees it.</summary>
    Node3D Tracer();

    /// <summary>Where a bullet or arrow stopped, and on what (in water when <paramref name="water"/>); it goes by itself.</summary>
    void Impact(Node parent, Vector3 at, HitTarget target, bool water);
}

/// <summary>The game's shot effects (plain ones until the effects package lands).</summary>
public static class ShotEffects
{
    public static IShotEffects Provider { get; set; } = new PlainShotEffects();
}

/// <summary>
/// Shot effects made in code: a warm burst of light with a small star of flame for 50 ms, a thin glowing streak for a
/// tracer, and a puff of dust (red on a body, white on water) that swells and fades in a third of a second. Glowing
/// things are kept as bright by day as by night (<see cref="Glow"/>).
/// </summary>
public sealed partial class PlainShotEffects : IShotEffects
{
    private static StandardMaterial3D? _flash, _tracer;
    private static readonly System.Collections.Generic.Dictionary<Color, StandardMaterial3D> Dust = new();

    public void MuzzleFlash(Node parent, Vector3 at, Vector3 direction)
    {
        if (_flash == null)
        {
            // Light only (black albedo), its shape and colour from the texture, kept as bright by day as by night.
            ImageTexture star = FlashTexture();
            _flash = new StandardMaterial3D
            {
                AlbedoColor = new Color(0, 0, 0, 1),
                AlbedoTexture = star,
                EmissionEnabled = true,
                Emission = Colors.Black,
                EmissionTexture = star,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                TextureRepeat = false,
                // In front of the muzzle, so over the gun (the arms are drawn over the world).
                NoDepthTest = true,
                RenderPriority = 10,
                DisableReceiveShadows = true,
                Roughness = 1,
                MetallicSpecular = 0,
            };
            Glow.Track(_flash, 5f);
        }
        var flash = new Flash { Name = "MuzzleFlash" };
        parent.AddChild(flash);
        flash.GlobalPosition = at;
        flash.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.26f, 0.26f) },
            MaterialOverride = _flash,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            RotationDegrees = new Vector3(0, 0, GD.Randf() * 90),
        });
        flash.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.7f, 0.35f), LightEnergy = Glow.FireLight * 30, OmniRange = 8 });
    }

    // A flash shows for two drawn frames (a real one is shorter than a frame), never more than 80 ms.
    private sealed partial class Flash : Node3D
    {
        private int _frames;
        private double _age;

        public override void _Process(double delta)
        {
            _age += delta;
            if (++_frames > 2 || _age > 0.08)
            {
                QueueFree();
            }
        }
    }

    // A star of flame: white-yellow at the heart, orange spikes, gone at the rim (alpha in the shape).
    private static ImageTexture FlashTexture()
    {
        const int N = 64;
        Image image = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1;
                float r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
                float spikes = 0.55f + 0.45f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 3)), 6);
                float alpha = Mathf.Clamp(1 - r / spikes, 0, 1);
                alpha *= alpha;
                var hot = new Color(1f, 0.95f, 0.75f);
                var warm = new Color(1f, 0.55f, 0.15f);
                Color c = hot.Lerp(warm, Mathf.Clamp(r / spikes * 1.4f, 0, 1));
                image.SetPixel(x, y, new Color(c.R, c.G, c.B, alpha));
            }
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    public Node3D Tracer()
    {
        if (_tracer == null)
        {
            _tracer = new StandardMaterial3D
            {
                AlbedoColor = Colors.Black,
                EmissionEnabled = true,
                Emission = new Color(1f, 0.78f, 0.4f),
                DisableReceiveShadows = true,
                Roughness = 1,
                MetallicSpecular = 0,
            };
            Glow.Track(_tracer, 4f);
        }
        return new MeshInstance3D
        {
            Name = "Tracer",
            Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.03f, 2.4f) },
            MaterialOverride = _tracer,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    public void Impact(Node parent, Vector3 at, HitTarget target, bool water)
    {
        Color colour = target == HitTarget.Character ? new Color(0.45f, 0.05f, 0.04f)
            : water ? new Color(0.85f, 0.9f, 0.95f)
            : target == HitTarget.Ground ? new Color(0.42f, 0.36f, 0.28f) : new Color(0.55f, 0.5f, 0.45f);
        if (!Dust.TryGetValue(colour, out StandardMaterial3D? material))
        {
            material = new StandardMaterial3D { AlbedoColor = colour, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 1 };
            Dust[colour] = material;
        }
        var puff = new Puff { Material = material };
        parent.AddChild(puff);
        puff.GlobalPosition = at;
    }

    // A puff that swells and fades.
    private sealed partial class Puff : MeshInstance3D
    {
        public StandardMaterial3D Material = null!;
        private double _age;

        public override void _Ready()
        {
            Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 8, Rings = 4 };
            MaterialOverride = (StandardMaterial3D)Material.Duplicate();
            CastShadow = ShadowCastingSetting.Off;
        }

        public override void _Process(double delta)
        {
            _age += delta;
            float t = (float)(_age / 0.35);
            if (t >= 1)
            {
                QueueFree();
                return;
            }
            Scale = Vector3.One * (1 + 2.5f * t);
            var m = (StandardMaterial3D)MaterialOverride;
            Color c = m.AlbedoColor;
            m.AlbedoColor = new Color(c.R, c.G, c.B, 0.8f * (1 - t));
        }
    }
}
