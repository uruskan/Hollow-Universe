using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The F2 panel (GROUND.md §8.5): the developer's window on the game while playing it, in the F1 panel's shape and
// style. Five tabs: SERVER (the tick's graph and its sections, what the zone holds, the game's counters, the process),
// PLAYERS (everyone on and how their connection does), LOOK (what the crosshair is on as this client and the server see
// it, and the last intents answered), DRAW (what is drawn in the world: hitboxes, ground tiles, the relevance radius,
// the nav grid, the spawn areas) and LOG (the server's lines).
public partial class GroundUi
{
    private static readonly Color DevAverage = new(0.55f, 0.86f, 0.45f);
    private static readonly Color DevWorst = new(1.00f, 0.62f, 0.35f);
    private static readonly Color DevBudget = new(0.76f, 0.235f, 0.17f);
    private static readonly Color DevRule = new(1f, 1f, 1f, 0.08f);

    // The log's lines between the bar and the panel's bottom (no input line here).
    private const float DevLogTop = 92, DevLogBottom = 744;

    private int _devLogScroll, _devPlayersScroll, _devLookScroll;

    /// <summary>The F2 panel's tab shown.</summary>
    public DeveloperTab DeveloperTab
    {
        get => Model.Developer?.Shown ?? DeveloperTab.Server;
        set
        {
            if (Model.Developer is { } dev)
            {
                dev.Shown = value;
            }
            _devLookScroll = 0;
        }
    }

    /// <summary>F2: opens the panel (on the tab shown last), or closes it.</summary>
    public void ToggleDeveloper()
    {
        if (Model.Developer == null)
        {
            return;
        }
        Open(Screen == GroundScreen.Developer ? (Model.Death != null ? GroundScreen.Death : GroundScreen.Hud) : GroundScreen.Developer);
    }

    // F2 opens and closes the panel anywhere but the menu; while it is open, Esc closes it and Page Up and Page Down
    // scroll the tab's list. True when the key was the panel's.
    private bool DeveloperKey(InputEventKey key)
    {
        if (Model.Developer == null)
        {
            return false;
        }
        if (key.PhysicalKeycode == Key.F2 && !key.Echo && Screen != GroundScreen.Menu)
        {
            ToggleDeveloper();
            return true;
        }
        if (Screen != GroundScreen.Developer)
        {
            return false;
        }
        switch (key.PhysicalKeycode)
        {
            case Key.Escape when !key.Echo:
                ToggleDeveloper();
                return true;
            case Key.Pageup:
                ScrollDeveloper(-8);
                return true;
            case Key.Pagedown:
                ScrollDeveloper(8);
                return true;
            case Key.Tab:
                return true;
        }
        return false;
    }

    // The wheel and the page keys scroll the tab's list: the log and the server's description back in time, the players down.
    private bool ScrollDeveloper(int steps)
    {
        if (Screen != GroundScreen.Developer)
        {
            return false;
        }
        switch (DeveloperTab)
        {
            case DeveloperTab.Log:
                _devLogScroll = Math.Max(0, _devLogScroll - steps * 3);
                break;
            case DeveloperTab.Players:
                _devPlayersScroll = Math.Max(0, _devPlayersScroll + steps);
                break;
            case DeveloperTab.Look:
                _devLookScroll = Math.Max(0, _devLookScroll + steps);
                break;
        }
        return true;
    }

    // ---------------------------------------------------------------- painting

    private void PaintDeveloperScreen()
    {
        if (Model.Developer is not { } dev)
        {
            return;
        }
        _o = TopFrame;
        Rect(PanelX, PanelY, PanelW, PanelH, ConsolePanel);
        PanelArea(PanelX, PanelY, PanelW, PanelH);
        Rect(PanelX, PanelY, PanelW, BarH, ConsoleBar);
        Rect(PanelX, PanelY + PanelH - 2, PanelW, 2, new Color(G.Brick, 0.9f));
        string[] names = { "SERVER", "PLAYERS", "LOOK", "DRAW", "LOG" };
        for (int i = 0; i < names.Length; i++)
        {
            var tab = (DeveloperTab)i;
            Button(names[i], PanelX + i * 150, PanelY, 148, BarH, () => DeveloperTab = tab, 24, tab == dev.Shown ? G.Selected : Transparent);
        }
        if (dev.Admin)
        {
            Rect(1210, 36, 92, 24, new Color(G.Green, 0.8f));
            TextIn("ADMIN", 1210, 37, 92, 17, new Color(0.05f, 0.07f, 0.04f), HorizontalAlignment.Center, bold: true);
        }
        TextIn("F2 / ESC TO CLOSE", 1310, 38, 250, 18, G.Muted, HorizontalAlignment.Right, bold: true);
        switch (dev.Shown)
        {
            case DeveloperTab.Server:
                PaintDevServer(dev);
                break;
            case DeveloperTab.Players:
                PaintDevPlayers(dev);
                break;
            case DeveloperTab.Look:
                PaintDevLook(dev);
                break;
            case DeveloperTab.Draw:
                PaintDevDraw(dev);
                break;
            case DeveloperTab.Log:
                PaintDevLog(dev);
                break;
        }
    }

    // SERVER: the tick's graph (the last minute, the average and the longest of each half second, against the budget),
    // the profile's sections as its tree in two columns, and on the right what the zone holds, the game's counters and
    // the process.
    private void PaintDevServer(IDeveloperModel dev)
    {
        Text(Fit(dev.Headline, 740, 20, bold: true), 44, 94, 20, dev.Connected ? G.Ink : G.Muted, bold: true);
        const float gx = 44, gy = 128, gw = 740, gh = 190;
        TickGraphView ticks = dev.Ticks;
        Rect(gx, gy, gw, gh, new Color(0, 0, 0, 0.3f));
        float top = ticks.BudgetMs * 1.25f;
        for (int i = 0; i < ticks.Count; i++)
        {
            float v = ticks.Worst[(ticks.Start + i) % ticks.Worst.Count];
            top = Math.Max(top, float.IsFinite(v) ? v : 0);
        }
        float budgetY = gy + gh - 2 - (gh - 4) * (ticks.BudgetMs / top);
        Rect(gx, budgetY, gw, 1, new Color(DevBudget, 0.9f));
        TextIn(string.Create(CultureInfo.InvariantCulture, $"budget {ticks.BudgetMs:0.0} ms"), gx, budgetY - 18, gw - 8, 14, DevBudget, HorizontalAlignment.Right, bold: true);
        DevGraphLine(ticks.Worst, ticks.Start, ticks.Count, top, gx, gy, gw, gh, DevWorst);
        DevGraphLine(ticks.Average, ticks.Start, ticks.Count, top, gx, gy, gw, gh, DevAverage);
        float lx = gx;
        foreach ((string label, Color colour) in new[] { ("Average tick", DevAverage), ("Longest tick", DevWorst) })
        {
            Rect(lx, gy + gh + 13, 12, 12, colour);
            lx += 18 + Text(label, lx + 18, gy + gh + 9, 16, G.Ink) + 24;
        }
        Text(string.Create(CultureInfo.InvariantCulture, $"The last minute, a sample each half second; the scale tops at {top:0} ms."), lx + 4, gy + gh + 9, 16, G.Muted);

        IReadOnlyList<SectionRow> sections = dev.Sections;
        const float sy = 356, rowH = 22, colW = 370;
        int perColumn = Math.Max(1, (sections.Count + 1) / 2);
        for (int c = 0; c < 2; c++)
        {
            float x = 44 + c * (colW + 20);
            Text("SECTION", x, sy, 14, G.Muted, bold: true);
            TextIn("AVERAGE", x, sy, colW - 90, 14, G.Muted, HorizontalAlignment.Right, bold: true);
            TextIn("LONGEST", x, sy, colW, 14, G.Muted, HorizontalAlignment.Right, bold: true);
            Rect(x, sy + 20, colW, 1, DevRule);
        }
        for (int i = 0; i < sections.Count; i++)
        {
            SectionRow row = sections[i];
            int c = Math.Min(1, i / perColumn), r = i - c * perColumn;
            float x = 44 + c * (colW + 20), y = sy + 26 + r * rowH;
            Text(Fit(row.Name, colW - 180 - row.Depth * 14, 16, bold: row.Depth == 0), x + row.Depth * 14, y, 16, row.Depth == 0 ? G.Ink : G.Muted, bold: row.Depth == 0);
            TextIn(DevMs(row.AverageMs), x, y, colW - 90, 16, G.Ink, HorizontalAlignment.Right);
            TextIn(DevMs(row.WorstMs), x, y, colW, 16, row.WorstMs >= ticks.BudgetMs ? G.Warning : G.Ink, HorizontalAlignment.Right);
        }
        if (sections.Count == 0)
        {
            Text(dev.Admin ? "No sections yet." : "The server sends its tick to admins only.", 44, sy + 28, 16, G.Muted);
        }
        PaintDevCounts(dev.Counts);
    }

    // One line of the tick graph: the ring's samples from its start, the scale from zero to `top`.
    private void DevGraphLine(IReadOnlyList<float> values, int start, int count, float top, float x, float y, float w, float h, Color colour)
    {
        if (count < 2 || values.Count < 2)
        {
            return;
        }
        var points = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            float v = values[(start + i) % values.Count];
            points[i] = new Vector2(x + w * i / (values.Count - 1), y + h - 2 - (h - 4) * Math.Clamp((float.IsFinite(v) ? v : 0) / top, 0, 1));
        }
        Line(colour, 1.5f, points);
    }

    // The counts: what the zone holds in one column, the game's counters and the process in the other.
    private void PaintDevCounts(IReadOnlyList<InfoSection> counts)
    {
        float[] ys = { 94, 94 };
        float[] xs = { 820, 1200 };
        float[] ws = { 350, 346 };
        for (int i = 0; i < counts.Count; i++)
        {
            int c = i == 0 ? 0 : 1;
            InfoSection section = counts[i];
            Text(section.Title, xs[c], ys[c], 20, G.Ink, bold: true);
            ys[c] += 30;
            foreach (InfoRow row in section.Rows)
            {
                Text(Fit(row.Label, 190, 15), xs[c], ys[c], 15, G.Muted);
                TextIn(Fit(row.Value, ws[c] - 150, 15), xs[c] + 140, ys[c], ws[c] - 140, 15, G.Ink, HorizontalAlignment.Right);
                ys[c] += 21;
            }
            ys[c] += 16;
        }
    }

    // PLAYERS: everyone on, a row each; the player's own row marked.
    private void PaintDevPlayers(IDeveloperModel dev)
    {
        IReadOnlyList<PlayerRow> players = dev.Players;
        const float rowH = 26;
        const int visible = 23;
        float y = 98;
        (string Title, float X, float W, HorizontalAlignment Align)[] columns =
        {
            ("NAME", 44, 300, HorizontalAlignment.Left), ("STATE", 354, 110, HorizontalAlignment.Left), ("WHERE (ZONE METRES)", 474, 300, HorizontalAlignment.Left),
            ("PING", 784, 80, HorizontalAlignment.Right), ("DOWN", 874, 110, HorizontalAlignment.Right), ("UP", 994, 110, HorizontalAlignment.Right),
            ("INPUTS ON TIME", 1114, 150, HorizontalAlignment.Right), ("IN VIEW", 1274, 110, HorizontalAlignment.Right),
        };
        foreach ((string title, float x, float w, HorizontalAlignment align) in columns)
        {
            TextIn(title, x, y, w, 14, G.Muted, align, bold: true);
        }
        Rect(44, y + 20, 1512, 1, DevRule);
        _devPlayersScroll = Math.Clamp(_devPlayersScroll, 0, Math.Max(0, players.Count - visible));
        y += 30;
        for (int i = _devPlayersScroll; i < players.Count && i < _devPlayersScroll + visible; i++, y += rowH)
        {
            PlayerRow p = players[i];
            if (p.Me)
            {
                Rect(40, y - 3, 1520, rowH - 2, new Color(G.Selected, 0.25f));
            }
            string name = Fit(p.Name, 220, 17, bold: true);
            float used = Text(name, 44, y, 17, p.Me ? G.Short : G.Ink, bold: true);
            if (p.Admin)
            {
                Text("ADMIN", 44 + used + 10, y + 3, 12, G.Green, bold: true);
            }
            Text(p.State, 354, y, 17, p.State == "dead" ? G.Warning : p.State == "wounded" ? G.Short : G.Ink);
            Text(Fit(p.Where, 290, 17), 474, y, 17, G.Muted);
            TextIn($"{p.PingMs} ms", 784, y, 80, 17, G.Ink, HorizontalAlignment.Right);
            TextIn(string.Create(CultureInfo.InvariantCulture, $"{p.KilobitsDown:0.0} kbit/s"), 874, y, 110, 17, G.Ink, HorizontalAlignment.Right);
            TextIn(string.Create(CultureInfo.InvariantCulture, $"{p.KilobitsUp:0.0} kbit/s"), 994, y, 110, 17, G.Ink, HorizontalAlignment.Right);
            TextIn($"{p.OnTimePercent} %", 1114, y, 150, 17, p.OnTimePercent < 95 ? G.Warning : G.Ink, HorizontalAlignment.Right);
            TextIn(p.InView.ToString(CultureInfo.InvariantCulture), 1274, y, 110, 17, G.Ink, HorizontalAlignment.Right);
        }
        if (players.Count == 0)
        {
            Text(!dev.Admin ? "The server lists its players for admins only." : dev.Connected ? "Nobody is on." : "Waiting for the server.", 44, 134, 17, G.Muted);
        }
        if (players.Count > visible)
        {
            TextIn($"ROWS {_devPlayersScroll + 1}–{Math.Min(players.Count, _devPlayersScroll + visible)} OF {players.Count}  ·  the wheel scrolls",
                44, 742, 1512, 14, G.Muted, HorizontalAlignment.Right, bold: true);
        }
    }

    // LOOK: what the crosshair is on. Left, this client's facts and the last intents answered; right, the server's
    // description, scrolled.
    private void PaintDevLook(IDeveloperModel dev)
    {
        Text(Fit(dev.LookTitle, 1500, 24, bold: true), 44, 94, 24, G.Ink, bold: true);
        float y = 140;
        Text("THIS CLIENT", 44, y, 14, G.Muted, bold: true);
        y += 24;
        foreach (InfoRow row in dev.LookHere)
        {
            Text(Fit(row.Label, 170, 16), 44, y, 16, G.Muted);
            Text(Fit(row.Value, 380, 16), 224, y, 16, G.Ink);
            y += 22;
        }
        if (dev.LookHere.Count == 0)
        {
            Text("Nothing under the crosshair.", 44, y, 16, G.Muted);
            y += 22;
        }
        y = Math.Max(y + 24, 470);
        Text("THE LAST INTENTS ANSWERED", 44, y, 14, G.Muted, bold: true);
        y += 24;
        foreach (InfoRow row in dev.Answers)
        {
            Text(row.Label, 44, y, 16, row.Label == "refused" ? G.Warning : G.Green, bold: true);
            Text(Fit(row.Value, 470, 16), 134, y, 16, G.Ink);
            y += 22;
        }
        if (dev.Answers.Count == 0)
        {
            Text("None yet: do something (open a door, craft) and its answer shows here.", 44, y, 16, G.Muted);
        }

        const float rx = 640, rw = 912, top = 140, rowH = 21;
        const int visible = 27;
        Text("THE SERVER SAYS", rx, top, 14, G.Muted, bold: true);
        IReadOnlyList<InfoRow> lines = dev.LookServer;
        _devLookScroll = Math.Clamp(_devLookScroll, 0, Math.Max(0, lines.Count - visible));
        float ly = top + 24;
        for (int i = _devLookScroll; i < lines.Count && i < _devLookScroll + visible; i++, ly += rowH)
        {
            InfoRow line = lines[i];
            bool rules = line.Label is "Capabilities" or "Verbs for you" or "Flags for you";
            Text(Fit(line.Label, 190, 15, bold: true), rx, ly, 15, G.Muted, bold: true);
            Text(Fit(line.Value, rw - 210, 15), rx + 200, ly, 15, rules ? ServerReply : G.Ink);
        }
        if (lines.Count == 0)
        {
            Text(dev.Admin ? "Nothing described yet: the server answers within a quarter of a second." : "The server describes things for admins only.", rx, top + 24, 15, G.Muted);
        }
        if (lines.Count > visible)
        {
            TextIn($"LINES {_devLookScroll + 1}–{Math.Min(lines.Count, _devLookScroll + visible)} OF {lines.Count}  ·  the wheel scrolls", rx, 742, rw, 14, G.Muted,
                HorizontalAlignment.Right, bold: true);
        }
    }

    // DRAW: the toggles, each with a line on what it draws; those the server must agree to say so.
    private void PaintDevDraw(IDeveloperModel dev)
    {
        Text("DRAWN IN THE WORLD WHILE ON; THEY STAY ON WHEN THE PANEL CLOSES", 44, 94, 14, G.Muted, bold: true);
        float y = 130;
        foreach (DrawToggle toggle in dev.Draws)
        {
            string key = toggle.Key;
            bool can = !toggle.NeedsAdmin || dev.Admin;
            Button(toggle.Label + (toggle.On ? "  ON" : "  OFF"), 44, y, 300, 40, can ? () => dev.ToggleDraw(key) : null, 20, toggle.On ? ToolOn : G.Panel, enabled: can);
            Paragraph(toggle.Help + (toggle.NeedsAdmin ? (can ? "  From the server." : "  From the server: admins only.") : ""), 364, y + 1, 1160, 17, can ? G.Ink : G.Muted, 2);
            y += 60;
        }
    }

    // LOG: the server's lines, the newest at the bottom, wrapped; the wheel and the page keys scroll back.
    private void PaintDevLog(IDeveloperModel dev)
    {
        IReadOnlyList<string> lines = dev.Log;
        int visible = (int)((DevLogBottom - DevLogTop) / LogLine);
        var shown = new List<string>();
        int wanted = visible + _devLogScroll;
        for (int i = lines.Count - 1; i >= 0 && shown.Count < wanted; i--)
        {
            string[] parts = Wrap(lines[i], LogW - 12, LogSize);
            for (int p = parts.Length - 1; p >= 0; p--)
            {
                shown.Add(parts[p]);
            }
        }
        _devLogScroll = Math.Clamp(_devLogScroll, 0, Math.Max(0, shown.Count - visible));
        float y = DevLogBottom - LogLine;
        for (int i = _devLogScroll; i < shown.Count && y >= DevLogTop - 1; i++, y -= LogLine)
        {
            Text(shown[i], LogX, y, LogSize, ServerReply);
        }
        if (_devLogScroll > 0)
        {
            TextIn($"{_devLogScroll} lines below  ·  Page Down", LogX, DevLogBottom - LogLine, LogW, 16, G.Muted, HorizontalAlignment.Right, bold: true);
        }
        if (lines.Count == 0)
        {
            Text(dev.Admin ? "No lines yet: the server sends its log to an admin whose panel is open." : "The server sends its log to admins only.",
                LogX, DevLogTop + 4, LogSize, G.Muted);
        }
    }

    private static string DevMs(float ms) => string.Create(CultureInfo.InvariantCulture, $"{ms:0.00}");
}
