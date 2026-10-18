using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.Settings;
using HollowUniverse.UI.Model;

namespace HollowUniverse.UI.Viewer;

public partial class UiViewer
{
    // A native renderer check of the production HUD. Configuration round trips remain in memory;
    // Preview/Revert are exercised, but this diagnostic never calls Save or writes user preferences.
    private async void RunCrosshairCheck()
    {
        if (DisplayServer.GetName() == "headless" || Engine.IsEmbeddedInEditor())
        {
            GD.PrintErr("crosshair-check: requires a standalone rendered window");
            GetTree().Quit(1);
            return;
        }

        int checks = 0, failures = 0;
        void Expect(bool ok, string description)
        {
            checks++;
            if (!ok) failures++;
            GD.Print($"crosshair-check: {(ok ? "ok  " : "FAIL")} {description}");
        }

        GraphicsValues original = GraphicsSettings.Current, saved = GraphicsSettings.Saved;
        DisplayServer.WindowMode windowMode = DisplayServer.WindowGetMode();
        Vector2I windowSize = DisplayServer.WindowGetSize(), windowPosition = DisplayServer.WindowGetPosition();
        float originalScale = Ui.Scale;
        CrosshairView originalState = _model.Crosshair;
        bool originalHold = _ui.HoldEffects, originalProcessing = _ui.IsProcessing();
        string? shots = Options.TryGetValue("crosshair-shots", out string? folder)
            ? ProjectSettings.GlobalizePath(folder) : null;
        try
        {
            if (shots != null) Directory.CreateDirectory(shots);
            CheckCrosshairPreferences(Expect, original, saved);
            var render = original with
            {
                WindowMode = GameWindowMode.Windowed, AntiAliasing = AntiAliasingMode.Off,
                RenderScale = 1, Sharpen = false,
            };
            GraphicsSettings.Preview(render);
            _ui.HoldPointer(new Vector2(-10000, -10000));
            _ui.HoldEffects = false;
            _ui.Open(GroundScreen.Hud);
            _model.Prompt = null;
            _model.TargetHealth = null;
            if (_model.Death != null) _model.Respawn(null);
            _model.CloseLoot();
            _model.Crosshair = new();
            // Default viewer samples include a hit marker and damage arc. Let those expire before
            // freezing the UI clock; HoldEffects would instead hold their age at zero indefinitely.
            await ToSignal(GetTree().CreateTimer(1.6), SceneTreeTimer.SignalName.Timeout);
            _ui.SetProcess(false);

            // Keep the dot and bars clearly separated even at the smallest fractional scale, so
            // connected components diagnose duplicate markers rather than touching antialias fringes.
            var styleValues = new CrosshairValues { Opacity = 1, Gap = 6 };
            foreach ((Vector2I size, float scale) in new[]
            {
                (new Vector2I(1280, 720), .8125f),
                (new Vector2I(1281, 721), 1.25f),
            })
            {
                DisplayServer.WindowSetSize(size);
                await Frames(6);
                // Direct scale changes deliberately cover a fractional scale and a large preference,
                // independently of UiScaler's panel-fit limit. The HUD centre must remain exact.
                Ui.SetScale(scale);
                string sample = $"{size.X}x{size.Y}-scale{scale.ToString("0.####", CultureInfo.InvariantCulture)}";
                using Image off = await Capture(styleValues with { Style = CrosshairStyle.Off }, sample + "-off");
                Expect(off.GetWidth() == size.X && off.GetHeight() == size.Y,
                    $"native viewport is {size.X}×{size.Y}, UI scale {Ui.Scale:0.####}");
                foreach (CrosshairStyle style in new[]
                {
                    CrosshairStyle.Dot, CrosshairStyle.Cross, CrosshairStyle.CrossDot,
                    CrosshairStyle.Ring, CrosshairStyle.RingDot,
                })
                {
                    using Image shown = await Capture(styleValues with { Style = style }, sample + "-" + style.ToString().ToLowerInvariant());
                    CrosshairPixels ink = CrosshairDifference(off, shown);
                    int components = style switch
                    {
                        CrosshairStyle.Cross => 4, CrosshairStyle.CrossDot => 5,
                        CrosshairStyle.RingDot => 2, _ => 1,
                    };
                    Expect(ink.Count > 0 && ink.CentreError <= .75f &&
                        ink.Asymmetric <= Math.Max(4, ink.Count / 5) && ink.Components == components,
                        $"{sample} {style}: pixels={ink.Count}, centre error={ink.CentreError:0.###} px, " +
                        $"asymmetric={ink.Asymmetric}, components={ink.Components}/{components}");
                }
            }

            // These comparisons use the same captured background and frozen UI, so an extra legacy
            // marker, an ADS visibility leak or a menu crosshair cannot pass as scenery.
            _model.Crosshair = new();
            using Image blank = await Capture(styleValues with { Style = CrosshairStyle.Off });
            using Image dot = await Capture(styleValues);
            Expect(CrosshairDifference(blank, dot).Count > 0, "preview appears on the actual HUD immediately");
            _model.Crosshair = new(Aiming: true);
            using Image adsHidden = await Capture(styleValues with { ShowWhileAiming = false });
            Expect(CrosshairDifference(blank, adsHidden).Count == 0, "ADS preference hides the HUD crosshair");
            using Image adsShown = await Capture(styleValues with { ShowWhileAiming = true }, "ads-visible");
            Expect(CrosshairDifference(blank, adsShown).Count > 0, "ADS preference can retain the HUD crosshair");
            _model.Crosshair = new(Hidden: true);
            using Image hidden = await Capture(styleValues);
            Expect(CrosshairDifference(blank, hidden).Count == 0, "hidden camera/seat state draws no crosshair");

            _model.Crosshair = new(SpreadDegrees: 5, FieldOfView: 75);
            CrosshairValues cross = styleValues with { Style = CrosshairStyle.Cross };
            using Image fixedCross = await Capture(cross);
            using Image spreadCross = await Capture(cross with { DynamicSpread = true }, "dynamic-spread");
            CrosshairPixels fixedInk = CrosshairDifference(blank, fixedCross);
            CrosshairPixels spreadInk = CrosshairDifference(blank, spreadCross);
            Expect(spreadInk.Width > fixedInk.Width + 10 && spreadInk.CentreError <= .75f,
                "dynamic spread expands the same centred crosshair");
            _model.Crosshair = new(SpreadDegrees: 5, FieldOfView: 100);
            using Image wideFov = await Capture(cross with { DynamicSpread = true });
            Expect(CrosshairDifference(blank, wideFov).Width < spreadInk.Width,
                "spread projection follows camera field of view");
            _model.Crosshair = new(Aiming: true, SpreadDegrees: 5);
            using Image aimedCross = await Capture(cross with { DynamicSpread = true });
            Expect(CrosshairDifference(fixedCross, aimedCross).Count == 0,
                "ADS removes hip spread without introducing another crosshair");

            _model.Crosshair = new();
            foreach (GroundScreen screen in new[] { GroundScreen.Inventory, GroundScreen.Crafting, GroundScreen.Menu })
            {
                _ui.Open(screen);
                using Image menuOff = await Capture(styleValues with { Style = CrosshairStyle.Off });
                using Image menuOn = await Capture(styleValues);
                Expect(CrosshairDifference(menuOff, menuOn).Count == 0, $"{screen} suppresses the HUD crosshair");
            }
            _ui.Open(GroundScreen.Hud);

            // Exercise the actual settings keyboard route, including inactive controls and unsaved exit.
            _ui.ShowSettings(4);
            GraphicsValues beforeInactive = GraphicsSettings.Current;
            await Key(Godot.Key.Down);
            await Key(Godot.Key.Down); // Size has no effect on a dot.
            await Key(Godot.Key.Right);
            Expect(GraphicsSettings.Current == beforeInactive, "inactive dot size/arm control cannot change preferences");
            await Key(Godot.Key.Up);
            await Key(Godot.Key.Up);
            await Key(Godot.Key.Right);
            Expect(GraphicsSettings.Current.Crosshair.Style == CrosshairStyle.Cross,
                "CROSSHAIR settings keyboard changes Dot to Cross");
            await Key(Godot.Key.Down);
            await Key(Godot.Key.Right);
            Expect(GraphicsSettings.Current.Crosshair.Color == 1, "CROSSHAIR colour control selects Green");
            await Key(Godot.Key.Escape);
            Expect(GraphicsSettings.Current == saved && GraphicsSettings.Saved == saved,
                "leaving settings without Apply reverts the crosshair and does not write preferences");

            async Task<Image> Capture(CrosshairValues values, string? name = null)
            {
                GraphicsSettings.Preview(render with { Crosshair = values });
                _ui._Process(0); // redraw production controls without advancing the frozen effect clock
                await Frames(3);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Image image = GetViewport().GetTexture().GetImage();
                if (shots != null && name != null)
                    Expect(image.SavePng(Path.Combine(shots, name + ".png")) == Error.Ok, $"saved {name}.png");
                return image;
            }
        }
        catch (Exception exception)
        {
            Expect(false, exception.ToString());
        }
        finally
        {
            _model.Crosshair = originalState;
            _ui.HoldEffects = originalHold;
            _ui.SetProcess(originalProcessing);
            GraphicsSettings.Preview(original);
            DisplayServer.WindowSetMode(windowMode);
            if (windowMode is DisplayServer.WindowMode.Windowed or DisplayServer.WindowMode.Maximized)
            {
                DisplayServer.WindowSetSize(windowSize);
                DisplayServer.WindowSetPosition(windowPosition);
            }
            Ui.SetScale(originalScale);
            Expect(GraphicsSettings.Current == original, "original preview settings restored");
            Expect(GraphicsSettings.Saved == saved, "saved user preferences unchanged");
        }
        GD.Print($"crosshair-check: {checks - failures}/{checks} passed");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }

    private static void CheckCrosshairPreferences(Action<bool, string> expect, GraphicsValues original, GraphicsValues saved)
    {
        var configured = new CrosshairValues
        {
            Style = CrosshairStyle.RingDot, Color = 3, Size = 17, DotSize = 5, Thickness = 3,
            Gap = 7, Opacity = .65f, Outline = 2, ShowWhileAiming = false, DynamicSpread = true,
        };
        using var file = new ConfigFile();
        GraphicsSettings.WriteCrosshair(file, configured);
        using var parsed = new ConfigFile();
        expect(parsed.Parse(file.EncodeToText()) == Error.Ok && GraphicsSettings.ReadCrosshair(parsed) == configured,
            "every crosshair preference survives a ConfigFile text round trip in memory");
        using var absent = new ConfigFile();
        expect(GraphicsSettings.ReadCrosshair(absent) == new CrosshairValues(), "older settings files receive the default dot");
        parsed.SetValue("crosshair", "size", -99);
        parsed.SetValue("crosshair", "dot_size", 999);
        parsed.SetValue("crosshair", "thickness", "invalid");
        parsed.SetValue("crosshair", "opacity", double.NaN);
        CrosshairValues bounded = GraphicsSettings.ReadCrosshair(parsed);
        expect(bounded.Size == 2 && bounded.DotSize == 12 && bounded.Thickness == 2 && bounded.Opacity == .9f,
            "invalid saved dimensions and non-finite opacity are safely bounded/defaulted");
        foreach (QualityPreset preset in Enum.GetValues<QualityPreset>())
            expect(GraphicsSettings.WithPreset(original with { Crosshair = configured }, preset).Crosshair == configured,
                $"{preset} quality preserves the player's crosshair");
        GraphicsSettings.Preview(original with { Crosshair = configured });
        expect(GraphicsSettings.Current.Crosshair == configured && GraphicsSettings.Saved == saved,
            "preview changes the current crosshair without saving");
        GraphicsSettings.Revert();
        expect(GraphicsSettings.Current == saved && GraphicsSettings.Saved == saved, "revert restores the saved preferences");
    }

    private readonly record struct CrosshairPixels(int Count, float CentreError, int Asymmetric, int Components, int Width);

    private static CrosshairPixels CrosshairDifference(Image before, Image after)
    {
        if (before.GetSize() != after.GetSize()) return new(0, float.PositiveInfinity, 0, 0, 0);
        int width = after.GetWidth(), height = after.GetHeight();
        const int radius = 96;
        int left = Math.Max(0, width / 2 - radius), top = Math.Max(0, height / 2 - radius);
        int right = Math.Min(width, width - left), bottom = Math.Min(height, height - top);
        var pixels = new HashSet<Vector2I>();
        int minX = right, minY = bottom, maxX = left, maxY = top;
        for (int y = top; y < bottom; y++)
        for (int x = left; x < right; x++)
        {
            Color a = before.GetPixel(x, y), b = after.GetPixel(x, y);
            if (Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))) < .025f) continue;
            pixels.Add(new Vector2I(x, y));
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        if (pixels.Count == 0) return new(0, 0, 0, 0, 0);
        float centreError = Math.Max(Math.Abs((minX + maxX + 1) / 2f - width / 2f),
            Math.Abs((minY + maxY + 1) / 2f - height / 2f));
        int asymmetric = 0;
        foreach (Vector2I pixel in pixels)
            if (!pixels.Contains(new Vector2I(width - 1 - pixel.X, height - 1 - pixel.Y))) asymmetric++;
        var remaining = new HashSet<Vector2I>(pixels);
        var queue = new Queue<Vector2I>();
        int components = 0;
        foreach (Vector2I start in pixels)
        {
            if (!remaining.Remove(start)) continue;
            int count = 0;
            queue.Enqueue(start);
            while (queue.TryDequeue(out Vector2I at))
            {
                count++;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var next = at + new Vector2I(dx, dy);
                    if (remaining.Remove(next)) queue.Enqueue(next);
                }
            }
            if (count > 1) components++; // ignore isolated quantization noise
        }
        return new(pixels.Count, centreError, asymmetric, components, maxX - minX + 1);
    }
}
