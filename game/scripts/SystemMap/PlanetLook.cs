using System;
using Godot;
using Hollow.Core;
using Hollow.Galaxy.Systems;

namespace HollowUniverse.SystemMap;

/// <summary>Colours and surface parameters of planets by type (presentation only).</summary>
public static class PlanetLook
{
    public static void Apply(ShaderMaterial material, Body body)
    {
        var rng = new Rng(Hash64.Of((ulong)body.Path, Hash64.Of(body.Name)));
        float seed = (float)rng.Range(0, 100);
        float vary = (float)rng.Range(-0.06, 0.06);
        Color a;
        Color b;
        var c = new Color(0.08f, 0.2f, 0.45f);
        float banding = 0;
        float ocean = 0;
        float ice = 0;
        float atmosphere = 0;
        var atmosphereColour = new Color(0.5f, 0.7f, 1f);
        float noiseScale = 3.2f;

        switch (body.PlanetType)
        {
            case PlanetType.MetalRich:
                a = new Color(0.40f, 0.38f, 0.36f);
                b = new Color(0.62f, 0.56f, 0.50f);
                noiseScale = 4.5f;
                break;
            case PlanetType.Rocky:
                a = new Color(0.50f, 0.42f, 0.35f);
                b = new Color(0.72f, 0.62f, 0.50f);
                break;
            case PlanetType.RockyIce:
                a = new Color(0.52f, 0.52f, 0.55f);
                b = new Color(0.84f, 0.86f, 0.90f);
                break;
            case PlanetType.Icy:
                a = new Color(0.72f, 0.80f, 0.88f);
                b = new Color(0.94f, 0.96f, 1.00f);
                break;
            case PlanetType.WaterWorld:
                a = new Color(0.12f, 0.30f, 0.55f);
                b = new Color(0.20f, 0.45f, 0.72f);
                atmosphere = 0.5f;
                break;
            case PlanetType.EarthLike:
                a = new Color(0.22f, 0.42f, 0.18f);
                b = new Color(0.58f, 0.52f, 0.36f);
                c = new Color(0.05f, 0.16f, 0.40f);
                ocean = 0.53f;
                ice = 0.16f;
                atmosphere = 0.9f;
                atmosphereColour = new Color(0.45f, 0.65f, 1f);
                break;
            case PlanetType.AmmoniaWorld:
                a = new Color(0.58f, 0.48f, 0.30f);
                b = new Color(0.76f, 0.66f, 0.42f);
                atmosphere = 0.5f;
                atmosphereColour = new Color(0.85f, 0.75f, 0.45f);
                break;
            case PlanetType.IceGiant:
                a = new Color(0.42f, 0.66f, 0.82f);
                b = new Color(0.55f, 0.78f, 0.90f);
                c = new Color(0.36f, 0.58f, 0.78f);
                banding = 9;
                atmosphere = 0.45f;
                break;
            case PlanetType.GasGiant:
                if (body.Temperature > 900)
                {
                    a = new Color(0.45f, 0.26f, 0.22f);
                    b = new Color(0.62f, 0.38f, 0.28f);
                    c = new Color(0.85f, 0.55f, 0.35f);
                }
                else
                {
                    a = new Color(0.84f, 0.70f, 0.54f);
                    b = new Color(0.62f, 0.48f, 0.36f);
                    c = new Color(0.92f, 0.86f, 0.76f);
                }
                banding = 16;
                atmosphere = 0.3f;
                atmosphereColour = new Color(1f, 0.9f, 0.7f);
                break;
            default:
                a = new Color(0.5f, 0.5f, 0.5f);
                b = new Color(0.7f, 0.7f, 0.7f);
                break;
        }

        if (body.Atmosphere == AtmosphereType.Thick && body.PlanetType is PlanetType.Rocky or PlanetType.MetalRich)
        {
            a = new Color(0.82f, 0.74f, 0.55f);
            b = new Color(0.90f, 0.84f, 0.66f);
            atmosphere = 0.6f;
            atmosphereColour = new Color(0.95f, 0.85f, 0.6f);
            noiseScale = 1.5f;
        }

        // A few real bodies have well-known looks.
        switch (body.Name)
        {
            case "Mars":
                a = new Color(0.62f, 0.32f, 0.18f);
                b = new Color(0.80f, 0.50f, 0.30f);
                ice = 0.08f;
                atmosphere = 0.15f;
                atmosphereColour = new Color(0.9f, 0.6f, 0.4f);
                break;
            case "Uranus":
                a = new Color(0.60f, 0.82f, 0.86f);
                b = new Color(0.66f, 0.86f, 0.88f);
                banding = 4;
                break;
            case "Neptune":
                a = new Color(0.22f, 0.38f, 0.82f);
                b = new Color(0.32f, 0.50f, 0.90f);
                break;
            case "Moon":
            case "Mercury":
                a = new Color(0.42f, 0.41f, 0.40f);
                b = new Color(0.66f, 0.64f, 0.62f);
                break;
            case "Io":
                a = new Color(0.85f, 0.75f, 0.35f);
                b = new Color(0.70f, 0.50f, 0.25f);
                break;
        }

        material.SetShaderParameter("colour_a", Shift(a, vary));
        material.SetShaderParameter("colour_b", Shift(b, -vary));
        material.SetShaderParameter("colour_c", c);
        material.SetShaderParameter("banding", banding);
        material.SetShaderParameter("noise_scale", noiseScale);
        material.SetShaderParameter("seed", seed);
        material.SetShaderParameter("ocean", ocean);
        material.SetShaderParameter("ice_caps", ice);
        material.SetShaderParameter("atmosphere", atmosphere);
        material.SetShaderParameter("atmosphere_colour", atmosphereColour);
    }

    private static Color Shift(Color c, float amount) =>
        new(Math.Clamp(c.R + amount, 0, 1), Math.Clamp(c.G + amount * 0.6f, 0, 1), Math.Clamp(c.B - amount * 0.4f, 0, 1));
}
