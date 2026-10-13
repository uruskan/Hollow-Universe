using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Common;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.Ground;

/// <summary>
/// The code lock's keypad (GROUND.md §6.5), in the RustMVP interface's manner: a dark dialog in the middle of the
/// screen, the code's four digits, and the keys 1–9, C, 0 and OK in condensed bold; the number keys, Backspace, Enter
/// and Esc work too. It asks for a code to open a door, or a new code for the lock's owner. Drawn on the 1600 × 900
/// design canvas through <see cref="Ui.Px"/>.
/// </summary>
public partial class Keypad : Control
{
    private const int Digits = 4;
    private static readonly string[] Keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "C", "0", "OK" };

    private string _code = "";
    private string _title = "";
    private int _hover = -1;

    /// <summary>A whole code was entered (four digits).</summary>
    public event Action<string>? Entered;

    /// <summary>A key was pressed (for its beep).</summary>
    public event Action? Pressed;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    /// <summary>Opens the keypad with a title ("ENTER CODE", "NEW CODE").</summary>
    public void Ask(string title)
    {
        _title = title;
        _code = "";
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        QueueRedraw();
    }

    public void Close()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private Rect2 Dialog
    {
        get
        {
            float w = Ui.Pxf(300), h = Ui.Pxf(410);
            return new Rect2((Size - new Vector2(w, h)) / 2, new Vector2(w, h)).Snapped();
        }
    }

    private Rect2 KeyRect(int i)
    {
        Rect2 d = Dialog;
        float size = Ui.Pxf(76), gap = Ui.Pxf(10);
        int row = i / 3, column = i % 3;
        float x0 = d.Position.X + (d.Size.X - 3 * size - 2 * gap) / 2;
        float y0 = d.Position.Y + Ui.Pxf(132);
        return new Rect2(new Vector2(x0 + column * (size + gap), y0 + row * (Ui.Pxf(60) + gap)), new Vector2(size, Ui.Pxf(60))).Snapped();
    }

    public override void _Draw()
    {
        if (!Visible)
        {
            return;
        }
        Rect2 d = Dialog;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.35f));
        DrawRect(d, G.Dialog);
        DrawRect(new Rect2(d.Position, new Vector2(Ui.Pxf(4), d.Size.Y)), G.Red);
        Text(_title, new Vector2(d.Position.X + Ui.Pxf(26), d.Position.Y + Ui.Pxf(48)), 32, G.Ink, condensed: true);
        // The code: four boxes, a dot for each digit entered.
        float box = Ui.Pxf(44), gap = Ui.Pxf(10);
        float x0 = d.Position.X + (d.Size.X - Digits * box - (Digits - 1) * gap) / 2;
        for (int i = 0; i < Digits; i++)
        {
            var r = new Rect2(new Vector2(x0 + i * (box + gap), d.Position.Y + Ui.Pxf(70)), new Vector2(box, Ui.Pxf(46))).Snapped();
            DrawRect(r, G.Entry);
            if (i < _code.Length)
            {
                DrawCircle(r.GetCenter(), Ui.Pxf(6), G.Ink);
            }
        }
        for (int i = 0; i < Keys.Length; i++)
        {
            Rect2 r = KeyRect(i);
            Color fill = Keys[i] == "OK" ? G.Green : i == _hover ? G.PanelHover : G.Panel;
            DrawRect(r, fill);
            Text(Keys[i], r.GetCenter() + new Vector2(0, Ui.Pxf(11)), 30, G.Ink, condensed: true, centred: true);
        }
        Text("ESC TO CLOSE", new Vector2(d.Position.X + Ui.Pxf(26), d.End.Y - Ui.Pxf(16)), 16, G.Muted, condensed: true);
    }

    private void Text(string text, Vector2 at, float size, Color colour, bool condensed = false, bool centred = false)
    {
        FontFile font = G.Font(size, bold: condensed, condensed: condensed);
        int px = Ui.Px(size);
        if (centred)
        {
            at.X -= MathF.Round(font.GetStringSize(text, HorizontalAlignment.Left, -1, px).X / 2);
        }
        DrawString(font, at.Round(), text, HorizontalAlignment.Left, -1, px, colour);
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion motion:
            {
                int hover = KeyAt(motion.Position);
                if (hover != _hover)
                {
                    _hover = hover;
                    QueueRedraw();
                }
                break;
            }
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
            {
                int key = KeyAt(click.Position);
                if (key >= 0)
                {
                    Press(Keys[key]);
                }
                else if (!Dialog.HasPoint(click.Position))
                {
                    Close();
                }
                AcceptEvent();
                break;
            }
        }
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        if (key.Keycode is >= Key.Key0 and <= Key.Key9)
        {
            Press(((int)(key.Keycode - Key.Key0)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (key.Keycode is >= Key.Kp0 and <= Key.Kp9)
        {
            Press(((int)(key.Keycode - Key.Kp0)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (key.Keycode == Key.Backspace)
        {
            _code = _code.Length > 0 ? _code[..^1] : "";
        }
        else if (key.Keycode is Key.Enter or Key.KpEnter)
        {
            Press("OK");
        }
        else if (key.Keycode == Key.Escape)
        {
            Close();
        }
        else
        {
            return;
        }
        QueueRedraw();
        GetViewport().SetInputAsHandled();
    }

    private int KeyAt(Vector2 point)
    {
        for (int i = 0; i < Keys.Length; i++)
        {
            if (KeyRect(i).HasPoint(point))
            {
                return i;
            }
        }
        return -1;
    }

    private void Press(string key)
    {
        Pressed?.Invoke();
        switch (key)
        {
            case "C":
                _code = "";
                break;
            case "OK":
                if (_code.Length == Digits)
                {
                    string code = _code;
                    Close();
                    Entered?.Invoke(code);
                }
                break;
            default:
                if (_code.Length < Digits)
                {
                    _code += key;
                }
                if (_code.Length == Digits)
                {
                    // Rust takes the fourth digit as the entry.
                    string code = _code;
                    Close();
                    Entered?.Invoke(code);
                }
                break;
        }
        QueueRedraw();
    }
}

/// <summary>
/// What holding the interact key offers on a door or the claim device (Rust's "more options"): a short list beside the
/// centre in the RustMVP interface's manner; moving the mouse picks one, letting go of the key chooses it.
/// </summary>
public partial class OptionsMenu : Control
{
    private IReadOnlyList<(string Label, Action Act)> _options = Array.Empty<(string, Action)>();
    private int _chosen;
    private float _travel;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public void Offer(IReadOnlyList<(string Label, Action Act)> options)
    {
        _options = options;
        _chosen = 0;
        _travel = 0;
        Visible = options.Count > 0;
        QueueRedraw();
    }

    /// <summary>The mouse moved while the key is held: the highlight follows.</summary>
    public void Move(float dy)
    {
        _travel += dy;
        int step = (int)(_travel / Ui.Pxf(40));
        if (step != 0)
        {
            _travel -= step * Ui.Pxf(40);
            _chosen = Math.Clamp(_chosen + step, 0, _options.Count - 1);
            QueueRedraw();
        }
    }

    /// <summary>The key let go: does the highlighted option and closes.</summary>
    public void Choose()
    {
        if (Visible && _chosen < _options.Count)
        {
            _options[_chosen].Act();
        }
        Visible = false;
    }

    public void Cancel() => Visible = false;

    public override void _Draw()
    {
        if (!Visible)
        {
            return;
        }
        float w = Ui.Pxf(260), h = Ui.Pxf(46), gap = Ui.Pxf(6);
        var origin = new Vector2(Size.X / 2 + Ui.Pxf(60), Size.Y / 2 - (_options.Count * (h + gap)) / 2);
        for (int i = 0; i < _options.Count; i++)
        {
            var r = new Rect2(origin + new Vector2(0, i * (h + gap)), new Vector2(w, h)).Snapped();
            DrawRect(r, i == _chosen ? G.Selected : G.Dialog);
            FontFile font = G.Condensed;
            int px = Ui.Px(26);
            DrawString(font, (r.Position + new Vector2(Ui.Pxf(16), Ui.Pxf(32))).Round(), _options[i].Label, HorizontalAlignment.Left, -1, px, G.Ink);
        }
    }
}

internal static class RectSnapping
{
    /// <summary>A rectangle on whole pixels.</summary>
    public static Rect2 Snapped(this Rect2 r) => new(r.Position.Round(), r.Size.Round());
}
