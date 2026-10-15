using System;
using Godot;
using HollowUniverse.Common;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// A small main menu for playtests, in RustMVP's menu style: the title and a column of big condensed items on the
// left over a dark left-weighted vignette, and the connect panel (name, address, port) on the right.
public partial class GroundUi
{
    private bool _menuEntry;
    private string _menuStatus = "";

    private void PaintMenu()
    {
        // The world behind, darkened from the left (RustMVP's vignette, alpha 0.58 to 0.06).
        Color left = new(0, 0, 0, 0.58f), right = new(0, 0, 0, 0.06f);
        _p.DrawPolygon(
            new[] { Vector2.Zero, new Vector2(Size.X, 0), Size, new Vector2(0, Size.Y) },
            new[] { left, right, right, left });

        _o = LeftFrame;
        Rect(104, 92, 52, 5, G.Red);
        Text("HOLLOW", 96, 101, 112, G.Ink, bold: true);
        Text("UNIVERSE", 106, 247, 30, G.Muted);
        Button(_inGame ? "RESUME" : "PLAY", 90, 385, 360, 80, _inGame ? () => Open(GroundScreen.Hud) : Host, 55, Transparent, left: true);
        Button("CONNECT", 90, 499, 360, 65, () =>
        {
            CloseSettings();
            _menuEntry = !_menuEntry;
            Layout();
        }, 39, Transparent, left: true);
        // The settings panel (GROUND.md §8.6), in the main menu and the Esc menu alike.
        Button("SETTINGS", 90, 591, 360, 65, () =>
        {
            if (_settingsOpen)
            {
                CloseSettings();
            }
            else
            {
                OpenSettings();
            }
        }, 39, _settingsOpen ? G.Panel : Transparent, left: true);
        Button("QUIT", 90, 684, 360, 65, Quit, 39, Transparent, left: true);
        Rect(105, 824, 1388, 1, new Color(0.8f, 0.82f, 0.75f, 0.18f));
        Text("EARTH  /  SOL", 105, 837, 19, G.Muted);
        TextIn("HOLLOW UNIVERSE", 1293, 837, 200, 18, G.Muted, HorizontalAlignment.Right);
        PaintCrashNotice();

        if (_settingsOpen)
        {
            PaintSettings();
            return;
        }
        if (!_menuEntry)
        {
            return;
        }
        Rect(880, 295, 610, 470, G.Dialog);
        Rect(880, 295, 4, 470, G.Red);
        PanelArea(880, 295, 610, 470);
        Text("JOIN A GAME", 916, 320, 43, G.Ink, bold: true);
        Text("Host one on this computer, or connect to a server.", 916, 383, 23, G.Muted);
        Text("PLAYER NAME", 916, 440, 19, G.Muted, bold: true);
        Text("BODY", 1306, 440, 19, G.Muted, bold: true);
        Button(FemaleBody ? "FEMALE" : "MALE", 1306, 468, 144, 45, () =>
        {
            FemaleBody = !FemaleBody;
            SavePreferences();
        }, 21);
        Text("SERVER ADDRESS", 916, 533, 19, G.Muted, bold: true);
        Text("PORT", 1337, 533, 19, G.Muted, bold: true);
        Button("HOST SOLO", 916, 640, 300, 53, Host, 27, G.Red);
        Button("CONNECT     ", 1232, 640, 218, 53, Connect, 27);
        Arrow(1390, 666);
        Text(_menuStatus, 916, 706, 18, G.Muted);
    }

    // A crash report the menu has not shown yet (GROUND.md §4.3): small, under the menu's column, with its folder.
    private void PaintCrashNotice()
    {
        if (HollowUniverse.App.CrashReporter.Notice is not { } notice)
        {
            return;
        }
        bool crash = notice.Kind.StartsWith("crash", StringComparison.Ordinal);
        float w = Text(crash ? "THE LAST RUN CRASHED" : "THE LAST RUN HIT AN ERROR", 105, 764, 18, G.Warning, bold: true, condensed: true);
        Button("OPEN FOLDER", 105 + w + 16, 760, 120, 26, () => OS.ShellOpen(HollowUniverse.App.CrashReporter.Folder), 16);
        Button("DISMISS", 105 + w + 144, 760, 90, 26, HollowUniverse.App.CrashReporter.Dismiss, 16);
        Text(notice.Path, 105, 792, 15, G.Muted);
    }

    private string PlayerName => _name.Text.Trim().Length > 0 ? _name.Text.Trim() : "Survivor";

    private void Host()
    {
        SavePreferences();
        _menuStatus = "Starting a game on this computer...";
        HostRequested?.Invoke(PlayerName);
    }

    private void Connect()
    {
        string host = _host.Text.Trim();
        if (host.Length == 0)
        {
            _menuStatus = "Fill in the server address.";
            return;
        }
        SavePreferences();
        int port = MenuPort();
        _menuStatus = $"Connecting to {host}:{port}...";
        ConnectRequested?.Invoke(PlayerName, host, port);
    }

    private void Quit()
    {
        if (QuitRequested != null)
        {
            QuitRequested();
        }
        else
        {
            GetTree().Quit();
        }
    }

    /// <summary>A line under the connect panel's buttons ("Connecting...", or why it failed).</summary>
    public void SetMenuStatus(string text) => _menuStatus = text;
}
