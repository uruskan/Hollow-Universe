using System;
using System.Buffers.Binary;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Galaxy;

namespace HollowUniverse.GalaxyMap;

/// <summary>
/// The galaxy's overall glow: a volume texture of light and dust computed from the density model on a worker
/// thread, drawn as the sky by ray marching (shaders/galaxy_sky.gdshader). Presentation only.
/// </summary>
public sealed class GalaxyGlow
{
    public static readonly Vector3 HalfExtent = new(60000, 12000, 60000);
    private const int Nx = 224;
    private const int Ny = 40;
    private const int Nz = 224;
    private const float EmissionScale = 1000f;
    private const float DustScale = 1000f;

    // Light per system and colour of each density component (young, thin, thick, bulge, nuclear, halo).
    private static readonly float[] LightWeight = { 220f, 0.55f, 0.5f, 1.1f, 1.0f, 0.4f };
    private static readonly Color[] LightColour =
    {
        new(0.32f, 0.52f, 1.00f),
        new(1.00f, 0.90f, 0.74f),
        new(1.00f, 0.78f, 0.52f),
        new(1.00f, 0.70f, 0.38f),
        new(1.00f, 0.78f, 0.50f),
        new(0.85f, 0.80f, 0.72f),
    };

    private readonly DensityModel _model;
    private readonly double _solSky;
    private Task<byte[][]>? _build;

    public GalaxyGlow(DensityModel model)
    {
        _model = model;
        _solSky = SkyBrightness(model, Vector3D.Zero);
        Material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/galaxy_sky.gdshader") };
        Material.SetShaderParameter("half_extent", HalfExtent);
        Material.SetShaderParameter("ready", false);
        _build = Task.Run(() => Compute(model));
    }

    /// <summary>
    /// How bright the sky is around a point compared with the sky seen from Sol (median over directions, dust
    /// ignored). Inside the bulge it is hundreds of times brighter; the map dims the glow by it, like an eye adapting.
    /// About a millisecond.
    /// </summary>
    public double RelativeSkyBrightness(Vector3D galactic) => SkyBrightness(_model, galactic) / _solSky;

    private static readonly Vector3D[] SkyDirections = BuildSkyDirections();

    private static double SkyBrightness(DensityModel model, Vector3D from)
    {
        Span<double> c = stackalloc double[DensityModel.ComponentCount];
        var perDirection = new double[SkyDirections.Length];
        for (int d = 0; d < SkyDirections.Length; d++)
        {
            double sum = 0;
            double t = 0;
            double step = 10;
            while (t < 80000)
            {
                model.Evaluate(from + SkyDirections[d] * (t + 0.5 * step), c);
                double light = 0;
                for (int k = 0; k < DensityModel.ComponentCount; k++)
                {
                    light += c[k] * LightWeight[k];
                }
                sum += light * step;
                t += step;
                step = Math.Min(step * 1.15, 2000);
            }
            perDirection[d] = sum;
        }
        Array.Sort(perDirection);
        return perDirection[perDirection.Length / 2];
    }

    // The 26 directions to the faces, edges and corners of a cube.
    private static Vector3D[] BuildSkyDirections()
    {
        var list = new System.Collections.Generic.List<Vector3D>();
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (x != 0 || y != 0 || z != 0)
                    {
                        list.Add(new Vector3D(x, y, z).Normalized);
                    }
                }
            }
        }
        return list.ToArray();
    }

    /// <summary>The sky material to put in the map's environment.</summary>
    public ShaderMaterial Material { get; }

    public bool IsReady { get; private set; }
    public float Intensity { get; set; } = 1f;
    public float DustStrength { get; set; } = 1f;
    public float Gamma { get; set; } = 1f;

    /// <summary>Centre of the galaxy relative to the render origin.</summary>
    public void SetRenderOrigin(Vector3D origin)
    {
        Vector3D d = GalaxyFrame.GalacticCentre - origin;
        Material.SetShaderParameter("galaxy_centre", new Vector3((float)d.X, (float)d.Y, (float)d.Z));
    }

    /// <summary>Call once per frame: uploads the texture when ready and applies the current settings.</summary>
    public void Update()
    {
        if (_build is { IsCompleted: true })
        {
            var slices = new Godot.Collections.Array<Image>();
            foreach (byte[] slice in _build.Result)
            {
                slices.Add(Image.CreateFromData(Nx, Ny, false, Image.Format.Rgbah, slice));
            }
            var texture = new ImageTexture3D();
            texture.Create(Image.Format.Rgbah, Nx, Ny, Nz, false, slices);
            Material.SetShaderParameter("volume", texture);
            Material.SetShaderParameter("ready", true);
            IsReady = true;
            _build = null;
        }
        Material.SetShaderParameter("intensity", Intensity * 1.6e-4f);
        Material.SetShaderParameter("gamma", Gamma);
        // About 1 magnitude of extinction per kiloparsec in the plane near the Sun, tripled for contrast.
        Material.SetShaderParameter("dust_strength", DustStrength * 0.035f);
    }

    private static byte[][] Compute(DensityModel model)
    {
        var slices = new byte[Nz][];
        Parallel.For(0, Nz, iz =>
        {
            Span<double> c = stackalloc double[DensityModel.ComponentCount];
            var bytes = new byte[Nx * Ny * 8];
            double z = ((iz + 0.5) / Nz - 0.5) * 2 * HalfExtent.Z;
            for (int iy = 0; iy < Ny; iy++)
            {
                double v = (iy + 0.5) / Ny - 0.5;
                double yn = Math.Sign(v) * (2 * v) * (2 * v);
                double y = yn * HalfExtent.Y;
                for (int ix = 0; ix < Nx; ix++)
                {
                    double x = ((ix + 0.5) / Nx - 0.5) * 2 * HalfExtent.X;
                    var p = new Vector3D(x, y, z);
                    model.EvaluateModel(p, c);
                    // Arms are much brighter in light than in star counts (young stars and glowing gas).
                    float armBoost = 1f + 1.5f * (float)DensityModel.ArmStrength(p, wide: true);
                    float r = 0;
                    float g = 0;
                    float b = 0;
                    for (int k = 0; k < DensityModel.ComponentCount; k++)
                    {
                        float light = (float)c[k] * LightWeight[k] * (k == (int)GalaxyComponent.ThinDisk ? armBoost : 1f);
                        r += light * LightColour[k].R;
                        g += light * LightColour[k].G;
                        b += light * LightColour[k].B;
                    }
                    // Glowing gas (pink) where young stars are densest.
                    float gas = (float)(c[(int)GalaxyComponent.YoungDisk] * c[(int)GalaxyComponent.YoungDisk]) * 3e5f;
                    r += gas * 1.0f;
                    g += gas * 0.35f;
                    b += gas * 0.55f;
                    float dust = (float)(c[(int)GalaxyComponent.YoungDisk] * 1.0 + c[(int)GalaxyComponent.ThinDisk] * 0.02);
                    int o = (iy * Nx + ix) * 8;
                    BinaryPrimitives.WriteHalfLittleEndian(bytes.AsSpan(o), (Half)(r * EmissionScale));
                    BinaryPrimitives.WriteHalfLittleEndian(bytes.AsSpan(o + 2), (Half)(g * EmissionScale));
                    BinaryPrimitives.WriteHalfLittleEndian(bytes.AsSpan(o + 4), (Half)(b * EmissionScale));
                    BinaryPrimitives.WriteHalfLittleEndian(bytes.AsSpan(o + 6), (Half)(dust * DustScale));
                }
            }
            slices[iz] = bytes;
        });
        return slices;
    }
}
