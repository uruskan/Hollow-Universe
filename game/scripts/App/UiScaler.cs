using System;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.App;

/// <summary>
/// Keeps <see cref="Ui.Scale"/> matched to the window: 1.0 at 1080 p (never below 0.875), 1.33 at 1440 p and 2.0 at
/// 2160 p, times the player's own preference (Ctrl + plus / minus, Ctrl + 0 to reset; saved between sessions). The
/// interface is then laid out and rasterised at the screen's real pixel size rather than stretched.
/// </summary>
public partial class UiScaler : Node
{
    private const string SettingsPath = "user://settings.cfg";
    private const float Step = 1.1f;

    private float _preference = 1f;

    /// <summary>Fixed preference for this run (the --ui-scale option); not saved.</summary>
    public float? Override { get; set; }

    public override void _Ready()
    {
        var settings = new ConfigFile();
        if (settings.Load(SettingsPath) == Error.Ok)
        {
            _preference = Math.Clamp((float)settings.GetValue("ui", "scale", 1f).AsDouble(), 0.6f, 2f);
        }
        GetTree().Root.SizeChanged += Apply;
        Apply();
    }

    public override void _ExitTree() => GetTree().Root.SizeChanged -= Apply;

    /// <summary>Recomputes the scale from the window size and the preference.</summary>
    public void Apply()
    {
        float height = GetTree().Root.Size.Y;
        float automatic = Math.Clamp(height / 1080f, 0.875f, 4f);
        float scale = automatic * (Override ?? _preference);
        // Sixteenths: small window changes don't rebuild the theme, and metrics round consistently.
        Ui.SetScale(MathF.Round(scale * 16f) / 16f);
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, CtrlPressed: true } key)
        {
            return;
        }
        float preference = _preference;
        switch (key.PhysicalKeycode)
        {
            case Key.Equal:
            case Key.KpAdd:
                preference *= Step;
                break;
            case Key.Minus:
            case Key.KpSubtract:
                preference /= Step;
                break;
            case Key.Key0:
            case Key.Kp0:
                preference = 1f;
                break;
            default:
                return;
        }
        _preference = Math.Clamp(preference, 0.6f, 2f);
        var settings = new ConfigFile();
        settings.Load(SettingsPath);
        settings.SetValue("ui", "scale", _preference);
        settings.Save(SettingsPath);
        Override = null;
        Apply();
        GetViewport().SetInputAsHandled();
    }
}
