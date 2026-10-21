using Godot;
using Hollow.Simulation.Interaction;
using HollowUniverse.Common;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.Ground;

// The interaction modal also accepts ordinary labels. It shares the game's existing input/cursor gate.
public partial class Keypad
{
    private LineEdit _textEntry = null!;
    private string _textError = "";
    public bool TextMode { get; private set; }

    private void PrepareTextEntry()
    {
        _textEntry = new LineEdit { Name = "TextEntry", MaxLength = Names.MaxCharacters, Visible = false };
        G.Field(_textEntry, 22, G.Entry, G.Entry);
        AddChild(_textEntry);
        _textEntry.TextChanged += _ => { _textError = ""; QueueRedraw(); };
        Resized += LayoutTextEntry;
    }

    public void AskText(string title, string initial = "")
    {
        TextMode = true;
        _title = title;
        _textError = "";
        _textEntry.Text = initial;
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        LayoutTextEntry();
        _textEntry.Show();
        _textEntry.GrabFocus();
        _textEntry.SelectAll();
        QueueRedraw();
    }

    private void LayoutTextEntry()
    {
        if (_textEntry == null) return;
        Rect2 d = Dialog;
        _textEntry.Position = d.Position + new Vector2(Ui.Pxf(26), Ui.Pxf(74));
        _textEntry.Size = new Vector2(d.Size.X - Ui.Pxf(52), Ui.Pxf(48));
    }

    private Rect2 TextButton(bool save)
    {
        Rect2 d = Dialog;
        return new Rect2(d.Position + new Vector2(Ui.Pxf(save ? 254 : 26), Ui.Pxf(168)), new Vector2(Ui.Pxf(200), Ui.Pxf(46))).Snapped();
    }

    private void DrawTextEntry(Rect2 d)
    {
        Text(_textError.Length == 0 ? $"UP TO {Names.MaxCharacters} CHARACTERS" : _textError,
            d.Position + new Vector2(Ui.Pxf(26), Ui.Pxf(146)), 16, _textError.Length == 0 ? G.Muted : G.Red, condensed: true);
        foreach (bool save in new[] { false, true })
        {
            Rect2 button = TextButton(save);
            DrawRect(button, save ? G.Green : G.Panel);
            Text(save ? "SAVE" : "CANCEL", button.GetCenter() + new Vector2(0, Ui.Pxf(8)), 22, G.Ink, condensed: true, centred: true);
        }
        Text("ENTER TO SAVE    ESC TO CANCEL", d.Position + new Vector2(Ui.Pxf(26), Ui.Pxf(236)), 14, G.Muted, condensed: true);
    }

    private void TextEntryInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
        if (TextButton(true).HasPoint(click.Position)) SubmitText();
        else if (TextButton(false).HasPoint(click.Position) || !Dialog.HasPoint(click.Position)) Close();
        AcceptEvent();
    }

    private void SubmitText()
    {
        if (!Names.TryNormalize(_textEntry.Text, out string value))
        {
            _textError = "ENTER A NAME WITHOUT CONTROL CHARACTERS";
            QueueRedraw();
            return;
        }
        Close();
        Entered?.Invoke(value);
    }
}
