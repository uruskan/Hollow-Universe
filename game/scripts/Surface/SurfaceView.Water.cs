using System;
using Godot;
using Hollow.Core;

namespace HollowUniverse.Surface;

public partial class SurfaceView
{
    private CanvasLayer _waterLayer = null!;
    private ColorRect _waterScreen = null!;
    private ShaderMaterial _waterScreenMaterial = null!;

    private void SetupWaterView()
    {
        // Ground HUD and menus are on layer 1: immersion never tints text, oxygen or inventory controls.
        _waterLayer = new CanvasLayer { Name = "Underwater", Layer = 0 };
        AddChild(_waterLayer);
        _waterScreenMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_underwater.gdshader") };
        _waterScreen = new ColorRect
        {
            Name = "WaterScreen", MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = _waterScreenMaterial, Visible = false,
        };
        _waterScreen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _waterLayer.AddChild(_waterScreen);
    }

    private void UpdateWaterView(Vector3D camera)
    {
        if (_surface == null || _environment == null)
        {
            return;
        }
        double water = _surface.WaterLevelAt(camera.Normalized);
        double depth = water - (camera.Length - _surface.Radius);
        float amount = double.IsFinite(depth) ? (float)DMath.SmoothStep(-0.04, 0.14, depth) : 0;
        _waterScreen.Visible = amount > 0.001f;
        _waterScreenMaterial.SetShaderParameter("immersion", amount);
        _waterScreenMaterial.SetShaderParameter("water_depth", (float)Math.Clamp(depth, 0, 40));
        // This screen effect is independent from breathing: it follows the actual camera, including a seated
        // orbit camera, and the same water query supports the sea, lakes and streams.
        _environment.FogEnabled = amount > 0.001f;
        _environment.FogDensity = 0.075f * amount;
        _environment.FogLightColor = new Color(0.035f, 0.19f, 0.18f);
        _environment.FogLightEnergy = 0.5f;
        _environment.FogSkyAffect = amount;
    }
}
