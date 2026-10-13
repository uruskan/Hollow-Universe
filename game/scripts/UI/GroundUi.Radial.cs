using System;
using System.Collections.Generic;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The radial menu (GROUND_SYSTEMS.md §4; RustMVP's wheels, from SBareRoot.cpp): one control for every source of options.
// An ivory ring of radius 200–300 with the pointed sector in brick red (197–318), each option's outline glyph or item
// icon at radius 250 (257 when pointed at), and the pointed option's large icon, title, description, cost and reason in
// the middle. The pointer selects 95–430 px from the centre. A held input opens it (RadialBinding): pressed, its binding
// gives a source; held for the binding's time, the radial opens with the pointer in the middle; released, the option
// under the pointer is chosen, or after a tap the source's current one. Esc or the right mouse cancels; a left click
// chooses.
public partial class GroundUi
{
    // The inputs a binding may name, as Rust binds them, and how the radial's footer names them.
    private static readonly Dictionary<RadialInput, Trigger> Triggers = new()
    {
        [RadialInput.Aim] = Trigger.Mouse(MouseButton.Right, "RIGHT MOUSE"),
        [RadialInput.Interact] = Trigger.Key(Key.E, "E"),
        [RadialInput.Reload] = Trigger.Key(Key.R, "R"),
    };

    /// <summary>The radial open now, if any.</summary>
    public IRadialSource? Radial { get; private set; }

    private RadialInput _radialInput;

    // The held input that may open a radial: its binding, the source it gave on the press and when; done once the radial
    // it opened has closed, or it was cancelled, so that its release does nothing more.
    private RadialBinding? _holding;
    private IRadialSource? _holdSource;
    private double _holdSince;
    private bool _holdDone;

    /// <summary>
    /// Opens a radial on a source over the HUD, as if <paramref name="input"/> held it open (the viewer, the build
    /// sandbox, scripted runs); a left click chooses, Esc or the right mouse cancels.
    /// </summary>
    public void OpenRadial(IRadialSource source, RadialInput input)
    {
        Open(GroundScreen.Hud);
        ShowRadial(source, input);
    }

    /// <summary>Closes the radial, choosing the option under the pointer when <paramref name="apply"/> is set.</summary>
    public void CloseRadial(bool apply)
    {
        if (Radial is not { } source)
        {
            return;
        }
        int choice = apply ? RadialSelection(source.Options.Count) : -1;
        Radial = null;
        _holdDone = true;
        Layout();
        if (!Apply(source, choice))
        {
            source.Cancel();
        }
    }

    /// <summary>Where an option's sector of the open radial is on the screen, in pixels (scripted runs point at it).</summary>
    public Vector2 RadialPoint(int index)
    {
        int count = Radial?.Options.Count ?? 0;
        double a = StartAngle(Radial, count) + Math.Tau / Math.Max(1, count) * index;
        return CentreFrame + new Vector2(800 + (float)Math.Cos(a) * 250, 450 + (float)Math.Sin(a) * 250) * Ui.Scale;
    }

    private void ShowRadial(IRadialSource source, RadialInput input)
    {
        Radial = source;
        _radialInput = input;
        Layout();
    }

    // Chooses an option: an enabled one is chosen; one that can't be says why instead (RustMVP's status line). False when
    // nothing was chosen.
    private bool Apply(IRadialSource source, int index)
    {
        IReadOnlyList<RadialOption> options = source.Options;
        if (index < 0 || index >= options.Count)
        {
            return false;
        }
        if (options[index].Reason is { } reason)
        {
            OnNotice(reason);
            return false;
        }
        source.Choose(index);
        return true;
    }

    // Esc, the right mouse, another screen, death or a release the window never saw: the radial, or the hold before it
    // opens, ends without a choice.
    private void CancelRadial()
    {
        if (Radial != null)
        {
            CloseRadial(apply: false);
        }
        else if (_holding != null && !_holdDone)
        {
            _holdDone = true;
            _holdSource?.Cancel();
        }
    }

    // ---------------------------------------------------------------- input

    // A radial's input event: a binding's press (asks it for a source and starts holding), the held input's release
    // (chooses), Esc and the right mouse (cancel), the left mouse (chooses). True when the event was the radial's.
    private bool RadialEvent(InputEvent e)
    {
        if (_holding is { } held && Triggers[held.Input].Event.IsMatch(e, false))
        {
            if (!e.IsPressed())
            {
                ReleaseHold();
            }
            return true;
        }
        if (Radial != null || (_holding != null && !_holdDone))
        {
            switch (e)
            {
                case InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape }:
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                    CancelRadial();
                    return true;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when Radial != null:
                    CloseRadial(apply: true);
                    return true;
            }
        }
        if (Radial != null || _holding != null || !e.IsPressed() || e.IsEcho() || Screen != GroundScreen.Hud || IsChatting || Model.Piloting)
        {
            return false;
        }
        foreach (RadialBinding binding in Model.Radials)
        {
            if (Triggers[binding.Input].Event.IsMatch(e, false) && binding.Open() is { } source)
            {
                _holding = binding;
                _holdSource = source;
                _holdSince = _now;
                _holdDone = false;
                if (binding.HoldSeconds <= 0)
                {
                    OpenHeld();
                }
                return true;
            }
        }
        return false;
    }

    // Each frame: a hold long enough opens its radial; a held input the window no longer sees down (its release was
    // lost, the window lost focus) ends without a choice.
    private void UpdateRadial()
    {
        if (_holding is not { } held)
        {
            return;
        }
        if (!Triggers[held.Input].Held())
        {
            CancelRadial();
            _holding = null;
            _holdSource = null;
        }
        else if (!_holdDone && Radial == null && _now - _holdSince >= held.HoldSeconds)
        {
            OpenHeld();
        }
    }

    // The hold time passed: the radial opens with the pointer in the middle (RustMVP puts the cursor there).
    private void OpenHeld()
    {
        ShowRadial(_holdSource!, _holding!.Input);
        Vector2 middle = (Size / 2).Floor();
        _pointer = middle;
        _pointerDesign = null;
        GetViewport().WarpMouse(middle);
    }

    // The held input let go: an open radial chooses what the pointer is on; a tap (let go before it opened) chooses the
    // source's current option: E's primary action, R's reload.
    private void ReleaseHold()
    {
        IRadialSource source = _holdSource!;
        bool open = Radial == source, done = _holdDone;
        _holding = null;
        _holdSource = null;
        if (open)
        {
            CloseRadial(apply: true);
        }
        else if (!done && !Apply(source, source.Current))
        {
            source.Cancel();
        }
    }

    /// <summary>An input that opens radials: the event it matches (any modifiers), whether it is down now, its name.</summary>
    private sealed record Trigger(InputEvent Event, Func<bool> Held, string Label)
    {
        public static Trigger Key(Key key, string label) =>
            new(new InputEventKey { PhysicalKeycode = key }, () => Input.IsPhysicalKeyPressed(key), label);

        public static Trigger Mouse(MouseButton button, string label) =>
            new(new InputEventMouseButton { ButtonIndex = button }, () => Input.IsMouseButtonPressed(button), label);
    }

    // ---------------------------------------------------------------- drawing

    // Where the first option's sector is centred: twelve o'clock, or half a sector on when the top falls between the last
    // option and the first (the plan's).
    private static double StartAngle(IRadialSource? source, int count) =>
        -Math.PI / 2 + (source?.SplitAtTop == true && count > 0 ? Math.PI / count : 0);

    // The option under the pointer, or −1 in the middle and far out.
    private int RadialSelection(int count)
    {
        _o = CentreFrame;
        Vector2 delta = DesignPointer - new Vector2(800, 450);
        float distance = delta.Length();
        if (count == 0 || distance < 95 || distance > 430)
        {
            return -1;
        }
        double sector = Math.Tau / count;
        double angle = Math.Atan2(delta.Y, delta.X) - StartAngle(Radial, count) + sector / 2 + 2 * Math.Tau;
        return (int)Math.Floor(angle % Math.Tau / sector) % count;
    }

    private void PaintRadial()
    {
        if (Radial is not { } source)
        {
            return;
        }
        _p.DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.025f, 0.035f, 0.03f, 0.26f));
        _o = CentreFrame;
        IReadOnlyList<RadialOption> options = source.Options;
        int count = options.Count;
        int selection = RadialSelection(count);
        int shown = selection >= 0 ? selection : source.Current;
        double sector = Math.Tau / Math.Max(1, count), start = StartAngle(source, count);

        Annulus(0, Math.Tau, 200, 300, G.Ivory);
        if (shown >= 0 && shown < count)
        {
            double a = start + sector * shown;
            Annulus(a - sector / 2, a + sector / 2, 197, 318, G.Brick);
        }
        for (int i = 0; i < count; i++)
        {
            double a = start + sector * i;
            float radius = i == shown ? 257 : 250;
            OptionIcon(options[i].Icon, new Vector2(800 + (float)Math.Cos(a) * radius, 450 + (float)Math.Sin(a) * radius),
                i == shown ? G.Ivory : G.Brick, 29);
        }
        if (shown >= 0 && shown < count)
        {
            RadialOption option = options[shown];
            OptionIcon(option.Icon, new Vector2(800, 345), G.Brick, 64);
            float title = 40;
            while (title > 22 && Measure(option.Title, title, true, true) > 355)
            {
                title--;
            }
            CentreText(option.Title, 431, title, G.Ivory);
            float description = 20;
            while (description > 15 && Measure(option.Description, description, false, true) > 355)
            {
                description--;
            }
            CentreText(option.Description, 480, description, G.Ivory, bold: false);
            if (option.Cost != null)
            {
                CentreText(option.Cost, 568, 20, G.Ivory, bold: false);
            }
            if ((option.Reason ?? option.Warning) is { } why)
            {
                CentreText(why, 601, 17, G.Warning, bold: false);
            }
        }
        CentreText($"RELEASE {Triggers[_radialInput].Label} TO SELECT   ·   ESC TO CANCEL", 792, 17, G.Ivory, bold: false);
    }

    // A filled ring sector, with antialiased edges.
    private void Annulus(double from, double to, float inner, float outer, Color colour)
    {
        Vector2 centre = Pt(800, 450);
        float r0 = Ui.Pxf(inner), r1 = Ui.Pxf(outer);
        int pieces = Math.Max(1, (int)Math.Ceiling((to - from) / (Math.PI / 2)));
        for (int p = 0; p < pieces; p++)
        {
            double a0 = from + (to - from) * p / pieces, a1 = from + (to - from) * (p + 1) / pieces;
            int steps = Math.Max(8, (int)Math.Ceiling((a1 - a0) / Math.Tau * 256));
            var points = new Vector2[2 * (steps + 1)];
            for (int i = 0; i <= steps; i++)
            {
                double a = a0 + (a1 - a0) * i / steps;
                var direction = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                points[i] = centre + direction * r1;
                points[2 * steps + 1 - i] = centre + direction * r0;
            }
            _p.DrawColoredPolygon(points, colour);
        }
        int arcPoints = Math.Max(16, (int)Math.Ceiling((to - from) / Math.Tau * 256));
        _p.DrawArc(centre, r1, (float)from, (float)to, arcPoints, colour, 1.2f, true);
        _p.DrawArc(centre, r0, (float)from, (float)to, arcPoints, colour, 1.2f, true);
        if (to - from < Math.Tau - 1e-3)
        {
            foreach (double a in new[] { from, to })
            {
                var direction = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                _p.DrawLine(centre + direction * r0, centre + direction * r1, colour, 1.2f, true);
            }
        }
    }

    // An option's icon centred on a design point, size being a glyph's half width: its outline glyph stroked about 7.5 % of
    // the size wide, or the item's icon in a square a little wider than a glyph (an icon's picture has a margin).
    private void OptionIcon(RadialIcon icon, Vector2 at, Color colour, float size)
    {
        if (icon.IsItem)
        {
            float side = size * 2.2f;
            Icon(icon.Name, at.X - side / 2, at.Y - side / 2, side, side);
            return;
        }
        float width = Math.Max(1f / Ui.Scale, size * 0.075f);
        foreach (Vector2[] stroke in RadialGlyphs.Strokes(icon.Name))
        {
            var design = new Vector2[stroke.Length];
            for (int i = 0; i < stroke.Length; i++)
            {
                design[i] = at + stroke[i] * size;
            }
            Line(colour, width, design);
        }
    }
}
