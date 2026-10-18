using System;
using Godot;
using HollowUniverse.Settings;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The settings panel (GROUND.md §8.6), opened by the menu's SETTINGS (the main menu and the Esc menu): five tabs,
// a row per setting with its value and a line saying what it does. Changes show at once; APPLY saves
// them, REVERT goes back to the saved ones, DEFAULTS to this computer's, and Esc or BACK closes, dropping what was not
// applied. Keys: Up and Down choose a row, Left and Right change it, Enter flips or steps it, Tab the next tab.
public partial class GroundUi
{
    private const float SetX = 470, SetY = 70, SetW = 1070, SetH = 750;
    private const float SetColumn = 475, SetGap = 48, SetRowHeight = 48, SetFirstRow = 100;
    private const int SetRowsPerColumn = 12;

    private bool _settingsOpen;
    private int _settingsTab;
    private int _settingsRow;
    private int _settingsDrag = -1;
    private float _settingsDragX, _settingsDragW;

    private SettingRow[] SettingsRows => _settingsTab switch
    {
        0 => SettingRows.Graphics, 1 => SettingRows.Interface, 2 => SettingRows.Display,
        3 => SettingRows.Shadows, _ => SettingRows.Crosshair,
    };

    /// <summary>Opens a settings tab, also used by reproducible UI captures.</summary>
    public void ShowSettings(int tab = 0)
    {
        ShowMenu(_inGame);
        OpenSettings();
        _settingsTab = Math.Clamp(tab, 0, 4);
    }

    private void OpenSettings()
    {
        _settingsOpen = true;
        _settingsTab = 0;
        _settingsRow = 0;
        _menuEntry = false;
        Layout();
    }

    /// <summary>Closes the panel; what was shown but not applied goes back to what is saved.</summary>
    private void CloseSettings()
    {
        if (!_settingsOpen)
        {
            return;
        }
        _settingsOpen = false;
        _settingsDrag = -1;
        if (GraphicsSettings.Dirty)
        {
            GraphicsSettings.Revert();
        }
        _menuEntry = !_inGame && Screen == GroundScreen.Menu;
        Layout();
    }

    private void ChangeSetting(int row, Func<SettingRow, GraphicsValues, GraphicsValues> change)
    {
        SettingRow[] rows = SettingsRows;
        if (row < 0 || row >= rows.Length)
        {
            return;
        }
        _settingsRow = row;
        if (!rows[row].IsActive(GraphicsSettings.Current)) return;
        GraphicsSettings.Preview(change(rows[row], GraphicsSettings.Current));
    }

    private void PaintSettings()
    {
        Rect(SetX, SetY, SetW, SetH, G.Dialog);
        Rect(SetX, SetY, 4, SetH, G.Red);
        PanelArea(SetX, SetY, SetW, SetH);
        Text("SETTINGS", SetX + 36, SetY + 22, 43, G.Ink, bold: true);
        string[] tabs = { "GRAPHICS", "INTERFACE", "DISPLAY", "SHADOWS", "CROSSHAIR" };
        for (int t = 0; t < tabs.Length; t++)
        {
            int tab = t;
            Button(tabs[t], SetX + SetW - 36 - (tabs.Length - t) * 140, SetY + 30, 134, 44, () =>
            {
                _settingsTab = tab;
                _settingsRow = 0;
            }, 20, _settingsTab == t ? G.Red : G.Panel);
        }

        GraphicsValues values = GraphicsSettings.Current;
        SettingRow[] rows = SettingsRows;
        _settingsRow = Math.Clamp(_settingsRow, 0, rows.Length - 1);
        for (int i = 0; i < rows.Length; i++)
        {
            float x = SetX + 36 + i / SetRowsPerColumn * (SetColumn + SetGap);
            float y = SetY + SetFirstRow + i % SetRowsPerColumn * SetRowHeight;
            PaintSettingRow(rows[i], i, x, y, values);
        }
        if (_settingsTab == 4)
        {
            PaintCrosshairPreview(values.Crosshair);
        }

        // The foot: what is shown against what is saved, this computer's card, and the buttons.
        float foot = SetY + SetH - 66;
        Rect(SetX + 36, foot - 12, SetW - 72, 1, new Color(0.8f, 0.82f, 0.75f, 0.18f));
        bool dirty = GraphicsSettings.Dirty;
        const float buttonGap = 10;
        float bx = SetX + SetW - 36;
        void FootButton(string label, float w, Action click, Color? fill = null, bool enabled = true)
        {
            bx -= w;
            Button(label, bx, foot, w, 44, click, 22, fill, enabled);
            bx -= buttonGap;
        }
        FootButton("BACK", 120, CloseSettings);
        FootButton("DEFAULTS", 150, () => GraphicsSettings.Preview(GraphicsSettings.Defaults()));
        FootButton("REVERT", 130, GraphicsSettings.Revert, enabled: dirty);
        FootButton("APPLY", 140, GraphicsSettings.Save, dirty ? G.Red : G.Panel, dirty);
        float textX = SetX + 36, textWidth = bx + buttonGap - 16 - textX;
        Text(dirty ? "NOT APPLIED: APPLY saves; Esc reverts." : "Saved. Changes show at once.",
            textX, foot + 2, 16, dirty ? G.Warning : G.Muted);
        Text(Fit($"{RenderingServer.GetVideoAdapterName()}: {GraphicsSettings.DefaultPreset.ToString().ToUpperInvariant()} by default.",
            textWidth, 15), textX, foot + 26, 15, G.Muted);
    }

    private void PaintCrosshairPreview(CrosshairValues values)
    {
        const float x = SetX + 36 + SetColumn + SetGap;
        const float y = SetY + SetFirstRow;
        Text("LIVE PREVIEW", x, y, 25, G.Ink, bold: true, condensed: true);
        Text("Shown at the size you will see in game.", x, y + 34, 17, G.Muted);

        const float top = y + 80, height = 280;
        Rect(x, top, SetColumn / 2, height, new Color(0.065f, 0.08f, 0.085f));
        Rect(x + SetColumn / 2, top, SetColumn / 2, height, new Color(0.43f, 0.46f, 0.41f));
        Text("DARK", x + 16, top + 15, 15, G.Muted, bold: true, condensed: true);
        TextIn("LIGHT", x + SetColumn / 2, top + 15, SetColumn / 2 - 16, 15,
            new Color(0.9f, 0.9f, 0.85f), HorizontalAlignment.Right, bold: true, condensed: true);
        CrosshairPainter.Draw(_p, Pt(x + SetColumn / 2, top + height / 2), values);

        Text("Always at the exact center of your screen.", x, top + height + 25, 18, G.Ink);
        Text("Choose a style, then adjust its details on the left.", x, top + height + 55, 17, G.Muted);
        Text("Use arrow keys or drag the sliders.", x, top + height + 80, 17, G.Muted);
    }

    private void PaintSettingRow(SettingRow row, int index, float x, float y, GraphicsValues values)
    {
        bool active = row.IsActive(values);
        if (index == _settingsRow)
        {
            Rect(x - 14, y - 6, SetColumn + 28, SetRowHeight - 2, new Color(1, 1, 1, 0.05f));
            Rect(x - 14, y - 6, 3, SetRowHeight - 2, G.Red);
        }
        // The whole row chooses it (the control's own buttons, painted after, come first).
        Button("", x - 14, y - 6, SetColumn + 28, SetRowHeight - 2, () => _settingsRow = index, fill: Transparent);
        Text(row.Label, x, y, 19, active ? G.Ink : G.Muted, bold: true, condensed: true);
        Text(row.Help, x, y + 22, 15, G.Muted);

        const float ControlW = 230;
        float cx = x + SetColumn - ControlW;
        string text = row.Text(values);
        switch (row.Kind)
        {
            case SettingKind.Switch:
            {
                bool on = row.Get(values) > 0.5f;
                Button(text, cx + ControlW - 96, y - 3, 96, 28, () => ChangeSetting(index, (r, v) => r.Move(v, 1)), 18,
                    on ? G.Red : G.Panel, enabled: active);
                break;
            }
            case SettingKind.Choice:
                Button("<", cx, y - 3, 30, 28, () => ChangeSetting(index, (r, v) => r.Move(v, -1)), 18, enabled: active);
                Button(text, cx + 34, y - 3, ControlW - 68, 28, () => ChangeSetting(index, (r, v) => r.Move(v, 1)), 18, Transparent,
                    enabled: active, muted: !active);
                Button(">", cx + ControlW - 30, y - 3, 30, 28, () => ChangeSetting(index, (r, v) => r.Move(v, 1)), 18, enabled: active);
                break;
            default:
            {
                const float TrackW = 150;
                float fraction = row.Fraction(values);
                // The track takes a press (and a drag) a little above and below it.
                Button("", cx - 6, y - 4, TrackW + 12, 30, () =>
                {
                    _settingsRow = index;
                    _settingsDrag = index;
                    _settingsDragX = cx;
                    _settingsDragW = TrackW;
                    DragSetting();
                }, fill: Transparent, enabled: active);
                Rect(cx, y + 8, TrackW, 6, G.Panel);
                Rect(cx, y + 8, TrackW * fraction, 6, active ? G.Red : G.Muted);
                Rect(cx + TrackW * fraction - 4, y + 2, 8, 18, active ? G.Ink : G.Muted);
                TextIn(text, cx + TrackW + 8, y, ControlW - TrackW - 8, 18, active ? G.Ink : G.Muted, HorizontalAlignment.Right,
                    bold: true, condensed: true);
                break;
            }
        }
    }

    // A slider follows the pointer while the left button is held.
    private void DragSetting()
    {
        if (_settingsDrag < 0)
        {
            return;
        }
        if (!_settingsOpen || Screen != GroundScreen.Menu || !Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _settingsDrag = -1;
            return;
        }
        float fraction = (DesignPointer.X - _settingsDragX) / _settingsDragW;
        ChangeSetting(_settingsDrag, (r, v) => r.SetFraction(v, fraction));
    }

    /// <summary>The panel's keys; true when one was used.</summary>
    private bool SettingsKey(InputEventKey key)
    {
        if (!_settingsOpen || Screen != GroundScreen.Menu)
        {
            return false;
        }
        int count = SettingsRows.Length;
        switch (key.PhysicalKeycode)
        {
            case Key.Escape:
                CloseSettings();
                return true;
            case Key.Up:
                _settingsRow = (_settingsRow - 1 + count) % count;
                return true;
            case Key.Down:
                _settingsRow = (_settingsRow + 1) % count;
                return true;
            case Key.Left:
                ChangeSetting(_settingsRow, (r, v) => r.Move(v, -1));
                return true;
            case Key.Right:
                ChangeSetting(_settingsRow, (r, v) => r.Move(v, 1));
                return true;
            case Key.Enter or Key.KpEnter or Key.Space:
                ChangeSetting(_settingsRow, (r, v) => r.Move(v, 1));
                return true;
            case Key.Tab:
                _settingsTab = (_settingsTab + 1) % 5;
                _settingsRow = 0;
                return true;
            default:
                return false;
        }
    }
}
