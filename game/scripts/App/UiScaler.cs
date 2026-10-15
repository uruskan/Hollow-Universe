using System;
using Godot;
using HollowUniverse.Common;

namespace HollowUniverse.App;

/// <summary>
/// Keeps <see cref="Ui.Scale"/> matched to the window (see <see cref="Automatic"/>), times the player's own
/// preference (Ctrl + plus / minus, Ctrl + 0 to reset; saved between sessions). The interface is then laid out and
/// rasterised at the screen's real pixel size rather than stretched.
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
        HollowUniverse.Settings.GraphicsSettings.Changed += Take;
        Apply();
    }

    public override void _ExitTree()
    {
        GetTree().Root.SizeChanged -= Apply;
        HollowUniverse.Settings.GraphicsSettings.Changed -= Take;
    }

    // The settings panel's INTERFACE SCALE (GROUND.md §8.6), shown at once.
    private void Take(HollowUniverse.Settings.GraphicsValues values)
    {
        if (Math.Abs(values.InterfaceScale - _preference) < 0.001f)
        {
            return;
        }
        _preference = Math.Clamp(values.InterfaceScale, 0.6f, 2f);
        Override = null;
        Apply();
    }

    /// <summary>Recomputes the scale from the window size and the preference.</summary>
    public void Apply()
    {
        Vector2I size = GetTree().Root.Size;
        // Sixteenths: small window changes don't rebuild the theme, and metrics round consistently. The cap rounds
        // down, so the panels always fit.
        float wanted = MathF.Round(Automatic(size.X, size.Y) * (Override ?? _preference) * 16f) / 16f;
        float largest = MathF.Floor(Largest(size.X, size.Y) * 16f) / 16f;
        Ui.SetScale(Math.Min(wanted, largest));
    }

    /// <summary>
    /// The largest scale at which every panel fits the window: the widest row (the system map's body list, time bar
    /// and selected body, 1,430 px at scale 1 with margins) and the tallest column (the galaxy map's search box and
    /// selected system, about 580 px, plus room for a title on two lines).
    /// </summary>
    public static float Largest(float width, float height) => Math.Max(0.75f, Math.Min(height / 600f, width / 1440f));

    /// <summary>
    /// The scale for a window: 1.0 from 1600 × 900 up to 1080 p, then growing with the height (1.33 at 1440 p, 2.0
    /// at 2160 p). A window smaller than 1600 × 900 shrinks it so the layout still fits (0.8 at 1280 × 720).
    /// </summary>
    public static float Automatic(float width, float height)
    {
        float fit = Math.Min(1f, Math.Min(height / 900f, width / 1600f));
        return Math.Clamp(Math.Max(height / 1080f, fit), 0.75f, 4f);
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
        // Never past what the window can show, so Ctrl + minus always has a visible effect.
        Vector2I size = GetTree().Root.Size;
        float most = Math.Max(1f, Largest(size.X, size.Y) / Automatic(size.X, size.Y));
        _preference = Math.Clamp(preference, 0.6f, Math.Min(2f, most));
        var settings = new ConfigFile();
        settings.Load(SettingsPath);
        settings.SetValue("ui", "scale", _preference);
        settings.Save(SettingsPath);
        Override = null;
        HollowUniverse.Settings.GraphicsSettings.SetInterfaceScale(_preference);
        Apply();
        GetViewport().SetInputAsHandled();
    }
}
