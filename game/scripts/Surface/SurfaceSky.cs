using System;
using System.Collections.Generic;
using Godot;
using Hollow.Core;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// The sky of the surface view at a universe time (GROUND.md §4.6): the real directions of the planet's sun, its moon,
/// the system's other suns and planets from <see cref="SkyEphemeris"/>, turned into the sun and moon lights, the
/// shader parameters of the air and the sky, and an exposure that keeps nights dark but readable. Presentation only.
/// </summary>
public sealed class SurfaceSky
{
    /// <summary>Sunlight above the air at Earth's distance from the Sun, in render units.</summary>
    public const float SolarConstant = 3.0f;

    /// <summary>Stars and the galaxy's band are brightened by this much: eyes adapted to the dark see them better than a camera.</summary>
    private const float StarBoost = 400f;

    private readonly SkyEphemeris _ephemeris;
    private readonly AtmosphereModel _air;
    private readonly Body _sun;
    private readonly Body? _moon;
    private readonly List<Body> _points = new();
    private readonly Vector3 _sunColour;
    private readonly Basis _systemToGalactic;

    public SurfaceSky(StarSystem system, Body planet, AtmosphereModel air)
    {
        _ephemeris = new SkyEphemeris(system, planet);
        _air = air;
        _sun = _ephemeris.Sun;
        foreach (Body b in system.Bodies)
        {
            if (b.Parent == planet.Index && b.Kind == BodyKind.Moon && (_moon == null || b.Radius > _moon.Radius))
            {
                _moon = b;
            }
        }
        foreach (Body b in system.Bodies)
        {
            if (b.Index == planet.Index || b.Index == _sun.Index || b == _moon)
            {
                continue;
            }
            if (b.Kind == BodyKind.Star || (b.Kind == BodyKind.Planet && b.Radius > 0))
            {
                _points.Add(b);
            }
        }
        // White balance, like a camera set for daylight here: the thick air reddens the sun, so the light that
        // leaves it is balanced against what reaches the ground at 50° (applied to every light, so to the whole image).
        Vector3 colour = AtmosphereModel.Blackbody(_sun.Temperature, 0.75);
        Vector3 noon = air.TransmittanceToSky(1, Math.Sin(50 * Math.PI / 180)) * 0.85f + new Vector3(0.10f, 0.16f, 0.28f);
        Vector3 balance = new Vector3(1 / noon.X, 1 / noon.Y, 1 / noon.Z);
        balance /= AtmosphereModel.Luminance(balance);
        balance = Vector3.One.Lerp(balance, 0.85f);
        _sunColour = colour * balance / AtmosphereModel.Luminance(colour * balance);
        // The system's axes in the galaxy: not part of the generator, so a fixed rotation from the address
        // (presentation only).
        var rng = new Rng(Hash64.Of(system.Address.Value, 0x534B5952)); // "SKYR"
        var q = new Quaternion(new Vector3((float)rng.Range(-1, 1), (float)rng.Range(-1, 1), (float)rng.Range(-1, 1)).Normalized(),
            (float)rng.Range(0, Math.PI * 2));
        _systemToGalactic = new Basis(q);
        GalacticPosition = system.Primary.Position;
    }

    public SkyEphemeris Ephemeris => _ephemeris;
    public Body Sun => _sun;
    public Body? Moon => _moon;

    /// <summary>The system's position in the galaxy (light-years).</summary>
    public Vector3D GalacticPosition { get; }

    // Results of the last Update, render space.
    public Vector3 SunDirection { get; private set; }
    public Vector3 SunIlluminance { get; private set; }
    public Vector3 MoonDirection { get; private set; }
    public Vector3 MoonIlluminance { get; private set; }
    public Vector3 MoonViewDirection { get; private set; }
    public float SunElevation { get; private set; }
    public float MoonElevation { get; private set; }
    public float MoonPhase { get; private set; }
    public float Exposure { get; private set; } = 1;
    public double LocalHours { get; private set; }

    /// <summary>Recomputes the sky for a time and a camera (body-frame position), and sets the lights and shader parameters.</summary>
    public void Update(double time, Vector3D camera, RenderFrame frame, ShaderMaterial skyMaterial,
        DirectionalLight3D sunLight, DirectionalLight3D moonLight, float pixelAngle)
    {
        Vector3D up = camera.Normalized;
        Vector3D sunBody = _ephemeris.Position(_sun.Index, time);
        double sunDistance = (sunBody - camera).Length;
        Vector3 sunDir = frame.DirectionToRender((sunBody - camera).Normalized);
        double au = sunDistance / Units.MetresPerAu;
        float sunE = (float)(SolarConstant * _sun.Luminosity / (au * au));
        SunDirection = sunDir;
        SunIlluminance = _sunColour * sunE;
        double mus = Vector3D.Dot((sunBody - camera).Normalized, up);
        SunElevation = (float)(Math.Asin(Math.Clamp(mus, -1, 1)) * 180 / Math.PI);
        double altitude = camera.Length - _air.BottomRadius;
        LocalHours = _ephemeris.LocalSolarHours(up, _sun.Index, time);

        // The moon: its direction from the planet's centre lights the air; from the camera (with parallax) it is drawn.
        MoonIlluminance = Vector3.Zero;
        float moonAngular = 0;
        Vector3 moonSunDir = Vector3.Up;
        if (_moon != null)
        {
            Vector3D moonBody = _ephemeris.Position(_moon.Index, time);
            Vector3D toMoon = moonBody - camera;
            double d = toMoon.Length;
            MoonViewDirection = frame.DirectionToRender(toMoon / d);
            MoonDirection = MoonViewDirection;
            moonAngular = (float)Math.Asin(Math.Min(1, _moon.Radius / d));
            Vector3D moonToSun = (sunBody - moonBody).Normalized;
            moonSunDir = frame.DirectionToRender(moonToSun);
            double phaseAngle = Math.Acos(Math.Clamp(Vector3D.Dot(moonToSun, -toMoon / d), -1, 1));
            double lambert = (Math.Sin(phaseAngle) + (Math.PI - phaseAngle) * Math.Cos(phaseAngle)) / Math.PI;
            const double GeometricAlbedo = 0.13;
            double moonSunE = SolarConstant * _sun.Luminosity / Math.Pow((sunBody - moonBody).Length / Units.MetresPerAu, 2);
            double e = moonSunE * GeometricAlbedo * Math.Pow(_moon.Radius / d, 2) * lambert;
            MoonIlluminance = _sunColour * (float)e;
            MoonPhase = (float)(0.5 * (1 + Math.Cos(phaseAngle)));
            MoonElevation = (float)(Math.Asin(Math.Clamp(Vector3D.Dot(toMoon / d, up), -1, 1)) * 180 / Math.PI);
            skyMaterial.SetShaderParameter("moon_radiance", _sunColour * (float)(moonSunE * GeometricAlbedo * 1.5 / Math.PI));
            // A camera metering for the moonlit ground would burn the moon out into a huge glow; eyes don't: show it
            // a few times brighter than white at the current exposure, whatever that is.
            skyMaterial.SetShaderParameter("moon_max", 6f / Math.Max(Exposure, 1e-3f));
            skyMaterial.SetShaderParameter("moon_earthshine", _sunColour * (float)(moonSunE * 2e-4));
        }
        skyMaterial.SetShaderParameter("moon_direction_view", MoonViewDirection);
        skyMaterial.SetShaderParameter("moon_angular_radius", moonAngular);
        skyMaterial.SetShaderParameter("moon_sun_direction", moonSunDir);
        skyMaterial.SetShaderParameter("sun_angular_radius", (float)Math.Asin(Math.Min(1, _sun.Radius / sunDistance)));

        // The other suns and the planets as points of light.
        var points = new Godot.Collections.Array<Vector4>();
        var colours = new Godot.Collections.Array<Vector3>();
        foreach (Body b in _points)
        {
            Vector3D p = _ephemeris.Position(b.Index, time) - camera;
            double d = p.Length;
            float e;
            Vector3 colour;
            if (b.Kind == BodyKind.Star)
            {
                e = (float)(SolarConstant * b.Luminosity / Math.Pow(d / Units.MetresPerAu, 2));
                colour = AtmosphereModel.Blackbody(Math.Max(b.Temperature, 1500), 0.5);
            }
            else
            {
                Vector3D planetSun = _ephemeris.Position(_sun.Index, time) - _ephemeris.Position(b.Index, time);
                double alpha = Math.Acos(Math.Clamp(Vector3D.Dot(planetSun.Normalized, -p / d), -1, 1));
                double lambert = (Math.Sin(alpha) + (Math.PI - alpha) * Math.Cos(alpha)) / Math.PI;
                double sunAtPlanet = SolarConstant * _sun.Luminosity / Math.Pow(planetSun.Length / Units.MetresPerAu, 2);
                e = (float)(sunAtPlanet * 0.35 * Math.Pow(b.Radius / d, 2) * lambert);
                colour = b.PlanetType is PlanetType.IceGiant ? new Vector3(0.8f, 0.92f, 1f) : new Vector3(1f, 0.95f, 0.88f);
            }
            points.Add(new Vector4(0, 0, 0, e * StarBoost) + V4(frame.DirectionToRender(p / d)));
            colours.Add(colour);
            if (points.Count == 8)
            {
                break;
            }
        }
        skyMaterial.SetShaderParameter("points", points);
        skyMaterial.SetShaderParameter("point_colours", colours);
        skyMaterial.SetShaderParameter("point_count", points.Count);

        // The star field and the galaxy are fixed in the system frame: render → body → system.
        Vector3 Col(Vector3 axis) => ToGodot(_ephemeris.ToSystem(frame.DirectionToBody(axis), time));
        skyMaterial.SetShaderParameter("celestial", new Basis(Col(Vector3.Right), Col(Vector3.Up), Col(Vector3.Back)));
        Vector3D toCentre = (GalaxyFrame.GalacticCentre - GalacticPosition).Normalized;
        Basis galacticToSystem = _systemToGalactic.Inverse();
        skyMaterial.SetShaderParameter("galaxy_centre", galacticToSystem * ToGodot(toCentre));
        skyMaterial.SetShaderParameter("galaxy_pole", galacticToSystem * Vector3.Up);
        // A magnitude-0 star relative to this sun (magnitude −26.7 at Earth's sunlight).
        float magnitudeZero = SolarConstant * 2.2e-11f * StarBoost;
        skyMaterial.SetShaderParameter("star_brightness", magnitudeZero);
        skyMaterial.SetShaderParameter("galaxy_brightness", magnitudeZero * 2.5e3f);
        skyMaterial.SetShaderParameter("pixel_angle", pixelAngle);

        // Lights at the ground: sunlight and moonlight through the air.
        Vector3 sunT = _air.TransmittanceToSky(altitude, mus);
        Vector3 sunGround = SunIlluminance * sunT;
        SetLight(sunLight, sunDir, sunGround, frame);
        Vector3 moonGround = Vector3.Zero;
        if (_moon != null)
        {
            double mum = Vector3D.Dot(frame.DirectionToBody(MoonDirection), up);
            moonGround = MoonIlluminance * _air.TransmittanceToSky(altitude, mum);
        }
        // Moonlight reads a little blue, as night vision does (and as in Rust).
        SetLight(moonLight, MoonDirection, moonGround * MoonLightBoost * new Vector3(0.78f, 0.9f, 1.25f), frame);

        // Exposure: a camera metering for ground of 15% albedo, but nights stay a few stops darker than days.
        float topLum = AtmosphereModel.Luminance(SunIlluminance);
        float day = topLum * 0.55f;
        float light = AtmosphereModel.Luminance(sunGround) * (float)Math.Max(mus, 0)
            + topLum * 0.1f * (float)SmoothStep(-0.2, 0.3, mus)
            + AtmosphereModel.Luminance(moonGround) * MoonLightBoost + day * 1e-6f;
        float target = 4.6f / (day * MathF.Pow(Math.Max(light, 1e-12f) / day, 0.8f));
        Exposure = Math.Clamp(target, 0.05f, 20000f);
    }

    /// <summary>Moonlight is brightened a little, like eyes adapted to the dark (Rust's nights are readable too).</summary>
    public const float MoonLightBoost = 14f;

    /// <summary>The moonlit air is brightened much less, so the night sky stays dark around the moon.</summary>
    public const float MoonSkyBoost = 2.5f;

    private static void SetLight(DirectionalLight3D light, Vector3 toLight, Vector3 illuminance, RenderFrame frame)
    {
        float energy = AtmosphereModel.Luminance(illuminance);
        light.Visible = energy > 1e-9f && toLight.Y > -0.2f;
        if (!light.Visible)
        {
            return;
        }
        Vector3 colour = illuminance / Math.Max(energy, 1e-12f);
        float max = Math.Max(colour.X, Math.Max(colour.Y, colour.Z));
        // Godot reads a light's colour as sRGB and linearises it; ours is linear already.
        light.LightColor = new Color(colour.X / max, colour.Y / max, colour.Z / max).LinearToSrgb();
        // Godot multiplies light energy by π (so energy 1 lights white to 1); illuminance E gives radiance E/π.
        light.LightEnergy = energy * max / Mathf.Pi;
        Vector3 up = Math.Abs(toLight.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
        light.Basis = Basis.LookingAt(-toLight, up);
    }

    private static Vector4 V4(Vector3 v) => new(v.X, v.Y, v.Z, 0);

    private static Vector3 ToGodot(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private static double SmoothStep(double a, double b, double x)
    {
        double t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
