using System;
using System.Collections.Generic;
using Godot;

namespace HollowUniverse.App;

/// <summary>A standalone --shot is a render job: native input and window gestures cannot change its result.</summary>
public partial class CaptureIsolation : Node
{
    public Vector2I Resolution { get; set; }

    public static bool Requested(IReadOnlyDictionary<string, string> options) =>
        !options.ContainsKey("ui-check") && !options.ContainsKey("window-check") &&
        ((options.ContainsKey("shot") && !options.ContainsKey("tour")) ||
         (options.GetValueOrDefault("view") == "build" && options.GetValueOrDefault("scene") == "joints" && options.ContainsKey("tour")));

    public static Vector2I RequestedResolution(Vector2I fallback)
    {
        string[] args = OS.GetCmdlineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string? value = args[i].StartsWith("--resolution=", StringComparison.Ordinal) ? args[i][13..]
                : args[i] == "--resolution" && i + 1 < args.Length ? args[++i] : null;
            if (value == null) continue;
            string[] dimensions = value.ToLowerInvariant().Split('x');
            if (dimensions.Length == 2 && int.TryParse(dimensions[0], out int width) && int.TryParse(dimensions[1], out int height) && width > 0 && height > 0)
                return new Vector2I(width, height);
        }
        return fallback;
    }

    public override void _Ready()
    {
        ProcessPriority = int.MinValue;
        GetViewport().GuiDisableInput = true;
        GetWindow().Unresizable = true;
        EnforceWindow();
        GD.Print($"[capture] isolated input; requested resolution={Resolution}; preferences are not saved");
    }

    public override void _Process(double delta) => EnforceWindow();

    public override void _Input(InputEvent @event) => GetViewport().SetInputAsHandled();

    private void EnforceWindow()
    {
        if (DisplayServer.GetName() == "headless") return;
        Window window = GetWindow();
        if (window.Mode != Window.ModeEnum.Windowed) window.Mode = Window.ModeEnum.Windowed;
        if (window.Size != Resolution) window.Size = Resolution;
    }

    public static void Report(Viewport viewport)
    {
        Camera3D? camera = viewport.GetCamera3D();
        GD.Print($"[capture] resolution={viewport.GetVisibleRect().Size} window={DisplayServer.WindowGetSize()} mode={DisplayServer.WindowGetMode()} " +
            $"camera={camera?.Name} position={camera?.GlobalPosition} basis={camera?.GlobalBasis} fov={camera?.Fov}");
    }
}
