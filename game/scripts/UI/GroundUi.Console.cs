using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using HollowUniverse.Common;
using HollowUniverse.UI.Model;
using G = HollowUniverse.Common.Ui.Ground;

namespace HollowUniverse.UI;

// The F1 panel (GROUND.md §8.2): Rust's F1 menu in the ground interface's style, our own drawing. One dark translucent
// panel hanging from the top edge (the belt stays in sight below it) with five tabs: CONSOLE (the log, the input line
// with history and Tab completion), INFO (the game, this computer, the world, the server), NET (the connection and its
// graph), ITEMS (every item; an admin's click gives it) and TOOLS (an admin's buttons, each running a console command).
public partial class GroundUi
{
    private static readonly Color ConsolePanel = new(0.025f, 0.035f, 0.04f, 0.9f);
    private static readonly Color ConsoleBar = new(0.075f, 0.09f, 0.095f, 0.96f);
    private static readonly Color ConsoleField = new(0.10f, 0.13f, 0.14f, 1f);
    private static readonly Color ConsoleFieldFocus = new(0.12f, 0.155f, 0.165f, 1f);
    private static readonly Color ServerReply = new(0.62f, 0.80f, 0.94f);
    private static readonly Color ToolOn = new(0.31f, 0.44f, 0.18f, 0.85f);

    // The panel on the design canvas (from the top edge): the tabs' bar, the content under it.
    private const float PanelX = 24, PanelY = 24, PanelW = 1552, PanelH = 736, BarH = 48, ContentY = 84;

    // The log: lines of 18 px, 24 apart, between the bar and the input line.
    private const float LogX = 44, LogW = 1512, LogTop = 86, LogBottom = 686, LogLine = 24, LogSize = 18, LogColumn = 360;
    private const float InputY = 696, InputH = 48;

    private static readonly int[] GiveAmounts = { 1, 10, 100, 1000 };

    private LineEdit _consoleInput = null!;
    private LineEdit _itemSearch = null!;
    private int _consoleScroll;
    private int _consoleSeen = -1;
    private IReadOnlyList<string> _candidates = Array.Empty<string>();
    private string _itemCategory = "ALL";
    private int _itemScroll;
    private int _giveAmount = 1;
    private string? _itemSelected;
    private string? _itemHovered;
    private bool _sliderHeld;
    private double _sliderHours;
    private Rect2 _sliderTrack;
    private bool _wasDeadForConsole;
    private readonly Dictionary<(string Text, float Width, float Scale), string[]> _wrapped = new();

    /// <summary>The F1 panel's tab shown.</summary>
    public ConsoleTab ConsoleTab
    {
        get => _consoleTab;
        set
        {
            _consoleTab = value;
            _candidates = Array.Empty<string>();
            Layout();
        }
    }

    private ConsoleTab _consoleTab = ConsoleTab.Console;

    private void CreateConsoleFields()
    {
        _consoleInput = Field("ConsoleInput", 20, ConsoleField, ConsoleFieldFocus);
        _consoleInput.MaxLength = 200;
        _consoleInput.PlaceholderText = "Type a command (help lists them; Tab completes; Up and Down go through what you typed)";
        _consoleInput.KeepEditingOnTextSubmit = true;
        _consoleInput.TextSubmitted += text =>
        {
            if (Model.Console is { } console && text.Trim().Length > 0)
            {
                console.Submit(text);
            }
            _consoleInput.Text = "";
            _candidates = Array.Empty<string>();
            _consoleScroll = 0;
        };
        _consoleInput.TextChanged += _ => _candidates = Array.Empty<string>();
        _itemSearch = Field("ItemSearch", 20, G.Panel, new Color(0.76f, 0.74f, 0.70f, 0.22f));
        _itemSearch.PlaceholderText = "Search items...";
        _itemSearch.TextChanged += _ => _itemScroll = 0;
    }

    /// <summary>F1: opens the panel (on the tab shown last), or closes it.</summary>
    public void ToggleConsole()
    {
        if (Model.Console == null)
        {
            return;
        }
        if (Screen == GroundScreen.Console)
        {
            Open(Model.Death != null ? GroundScreen.Death : GroundScreen.Hud);
            return;
        }
        Open(GroundScreen.Console);
        _consoleScroll = 0;
        FocusConsole();
    }

    private void FocusConsole()
    {
        if (Screen == GroundScreen.Console && _consoleTab == ConsoleTab.Console && _consoleInput.Visible && !_consoleInput.HasFocus())
        {
            _consoleInput.GrabFocus();
            _consoleInput.Edit();
        }
    }

    /// <summary>Shows an item on the ITEMS tab, as a click does for a player who isn't an admin (scripted runs).</summary>
    public void SelectConsoleItem(string key) => _itemSelected = key;

    /// <summary>Puts a line in the input, as typed (scripted runs).</summary>
    public void TypeInConsole(string text)
    {
        _consoleInput.Text = text;
        _consoleInput.CaretColumn = text.Length;
    }

    // The F1 panel's keys, before the fields and the game get them: F1 and Esc close it, Tab completes, Up and Down go
    // through the history, Page Up and Page Down scroll the log. True when the key was the panel's.
    private bool ConsoleKey(InputEventKey key)
    {
        if (Model.Console is not { } console)
        {
            return false;
        }
        if (key.PhysicalKeycode == Key.F1 && !key.Echo && Screen != GroundScreen.Menu)
        {
            ToggleConsole();
            return true;
        }
        if (Screen != GroundScreen.Console)
        {
            return false;
        }
        bool log = _consoleTab == ConsoleTab.Console;
        switch (key.PhysicalKeycode)
        {
            case Key.Escape when !key.Echo:
                ToggleConsole();
                return true;
            case Key.Tab when log:
                ConsoleCompletionView done = console.Complete(_consoleInput.Text);
                _consoleInput.Text = done.Line;
                _consoleInput.CaretColumn = done.Line.Length;
                _candidates = done.Candidates;
                return true;
            case Key.Tab:
                return true;
            case Key.Up when log:
            case Key.Down when log:
                if (console.History(key.PhysicalKeycode == Key.Up ? -1 : 1, _consoleInput.Text) is { } line)
                {
                    _consoleInput.Text = line;
                    _consoleInput.CaretColumn = line.Length;
                    _candidates = Array.Empty<string>();
                }
                return true;
            case Key.Pageup when log:
                _consoleScroll += 20;
                return true;
            case Key.Pagedown when log:
                _consoleScroll = Math.Max(0, _consoleScroll - 20);
                return true;
            case Key.Pageup when _consoleTab == ConsoleTab.Items:
                _itemScroll = Math.Max(0, _itemScroll - 4);
                return true;
            case Key.Pagedown when _consoleTab == ConsoleTab.Items:
                _itemScroll += 4;
                return true;
        }
        return false;
    }

    // The mouse wheel over the log or the item grid scrolls it.
    private bool ScrollConsole(int steps)
    {
        if (Screen != GroundScreen.Console)
        {
            return false;
        }
        if (_consoleTab == ConsoleTab.Console)
        {
            _consoleScroll = Math.Max(0, _consoleScroll - steps * 3);
        }
        else if (_consoleTab == ConsoleTab.Items)
        {
            _itemScroll = Math.Max(0, _itemScroll + steps);
        }
        return true;
    }

    // The time slider: dragged with the left button held, set on release.
    private void DragConsoleSlider()
    {
        if (_sliderHeld)
        {
            _sliderHours = SliderHours();
        }
    }

    private void ReleaseConsole(MouseButton button)
    {
        if (!_sliderHeld || button != MouseButton.Left)
        {
            return;
        }
        _sliderHeld = false;
        _sliderHours = SliderHours();
        Model.Console?.Run(string.Create(CultureInfo.InvariantCulture, $"time {_sliderHours:0.##}"));
    }

    private double SliderHours() =>
        _sliderTrack.Size.X <= 0 ? 12 : Math.Clamp((Pointer.X - _sliderTrack.Position.X) / _sliderTrack.Size.X, 0, 1) * 24;

    // ---------------------------------------------------------------- painting

    private void PaintConsoleScreen()
    {
        if (Model.Console is not { } console)
        {
            return;
        }
        _o = TopFrame;
        Rect(PanelX, PanelY, PanelW, PanelH, ConsolePanel);
        PanelArea(PanelX, PanelY, PanelW, PanelH);
        Rect(PanelX, PanelY, PanelW, BarH, ConsoleBar);
        Rect(PanelX, PanelY + PanelH - 2, PanelW, 2, new Color(G.Brick, 0.9f));
        string[] names = { "CONSOLE", "INFO", "NET", "ITEMS", "TOOLS" };
        for (int i = 0; i < names.Length; i++)
        {
            var tab = (ConsoleTab)i;
            Button(names[i], PanelX + i * 150, PanelY, 148, BarH, () => ConsoleTab = tab, 24, tab == _consoleTab ? G.Selected : Transparent);
        }
        if (console.Admin)
        {
            Rect(1210, 36, 92, 24, new Color(G.Green, 0.8f));
            TextIn("ADMIN", 1210, 37, 92, 17, new Color(0.05f, 0.07f, 0.04f), HorizontalAlignment.Center, bold: true);
        }
        TextIn("F1 / ESC TO CLOSE", 1310, 38, 250, 18, G.Muted, HorizontalAlignment.Right, bold: true);
        switch (_consoleTab)
        {
            case ConsoleTab.Console:
                PaintConsoleLog(console);
                FocusConsole();
                break;
            case ConsoleTab.Info:
                PaintSections(console.Info, 4);
                break;
            case ConsoleTab.Net:
                PaintNet(console);
                break;
            case ConsoleTab.Items:
                PaintItems(console);
                break;
            case ConsoleTab.Tools:
                PaintTools(console);
                break;
        }
    }

    private void PaintConsoleLog(IConsoleModel console)
    {
        IReadOnlyList<ConsoleLine> lines = console.Lines;
        if (console.Revision != _consoleSeen)
        {
            _consoleSeen = console.Revision;
            if (_wrapped.Count > 4000)
            {
                _wrapped.Clear();
            }
        }
        int visible = (int)((LogBottom - LogTop) / LogLine);
        // The newest lines that fit, wrapped, from the bottom up; scrolled back by whole display lines.
        var shown = new List<(string Left, string Right, ConsoleLineKind Kind, bool First, float Indent)>();
        int wanted = visible + _consoleScroll;
        for (int i = lines.Count - 1; i >= 0 && shown.Count < wanted; i--)
        {
            ConsoleLine line = lines[i];
            int tab = line.Text.IndexOf('	', StringComparison.Ordinal);
            string left = tab >= 0 ? line.Text[..tab] : "", right = tab >= 0 ? line.Text[(tab + 1)..] : line.Text;
            float indent = tab >= 0 ? LogColumn : line.Kind == ConsoleLineKind.Input ? 22 : 0;
            string[] parts = Wrap(right, LogW - indent - 12, LogSize);
            for (int p = parts.Length - 1; p >= 0; p--)
            {
                shown.Add((p == 0 ? left : "", parts[p], line.Kind, p == 0, indent));
            }
        }
        _consoleScroll = Math.Clamp(_consoleScroll, 0, Math.Max(0, shown.Count - visible));
        float y = LogBottom - LogLine;
        for (int i = _consoleScroll; i < shown.Count && y >= LogTop - 1; i++, y -= LogLine)
        {
            (string left, string right, ConsoleLineKind kind, bool first, float indent) = shown[i];
            Color colour = kind switch
            {
                ConsoleLineKind.Input => G.Ink,
                ConsoleLineKind.Server => ServerReply,
                ConsoleLineKind.Success => G.Green,
                ConsoleLineKind.Error => G.Warning,
                ConsoleLineKind.System => new Color(G.Muted, 0.8f),
                _ => G.Muted,
            };
            if (kind == ConsoleLineKind.Input && first)
            {
                Text(">", LogX, y, LogSize, G.Brick, bold: true);
            }
            if (left.Length > 0)
            {
                Text(Fit(left, LogColumn - 14, LogSize, bold: true), LogX, y, LogSize, kind is ConsoleLineKind.Output or ConsoleLineKind.Server ? G.Ink : colour,
                    bold: true);
            }
            Text(right, LogX + indent, y, LogSize, colour);
        }
        if (_consoleScroll > 0)
        {
            TextIn($"{_consoleScroll} lines below  ·  Page Down", LogX, LogBottom - LogLine, LogW, 16, G.Muted, HorizontalAlignment.Right, bold: true);
        }
        if (lines.Count == 0)
        {
            Text("The console is empty. Type help.", LogX, LogTop, LogSize, G.Muted);
        }

        // Tab's candidates, over the bottom of the log.
        if (_candidates.Count > 0)
        {
            Rect(LogX, LogBottom - 2, LogW, 30, new Color(0.055f, 0.07f, 0.075f, 0.97f));
            string all = string.Join("    ", _candidates);
            Text(Fit(all, LogW - 24, 17), LogX + 12, LogBottom + 3, 17, ServerReply);
        }
        Rect(LogX, InputY, LogW, InputH, ConsoleField);
        Text(">", LogX + 10, InputY + 10, 24, G.Brick, bold: true);
    }

    // A text broken into lines no wider than the width (design px) at a size, at spaces where it can.
    private string[] Wrap(string text, float width, float size)
    {
        var key = (text, width, Ui.Scale);
        if (_wrapped.TryGetValue(key, out string[]? cached))
        {
            return cached;
        }
        var parts = new List<string>();
        string rest = text;
        while (rest.Length > 0 && Measure(rest, size) > width)
        {
            int cut = rest.Length;
            // The longest head that fits, ending at a space if there is one.
            int low = 1, high = rest.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (Measure(rest[..mid], size) <= width)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }
            cut = low;
            int space = rest.LastIndexOf(' ', Math.Max(0, cut - 1));
            if (space > cut / 2)
            {
                cut = space;
            }
            parts.Add(rest[..cut].TrimEnd());
            rest = rest[cut..].TrimStart();
        }
        parts.Add(rest);
        string[] result = parts.ToArray();
        _wrapped[key] = result;
        return result;
    }

    // A text cut to a width (design px), with an ellipsis when it had to be.
    private static string Fit(string text, float width, float size, bool bold = false)
    {
        if (Measure(text, size, bold) <= width)
        {
            return text;
        }
        int n = text.Length;
        while (n > 1 && Measure(text[..n] + "…", size, bold) > width)
        {
            n--;
        }
        return text[..n].TrimEnd() + "…";
    }

    // INFO (and NET's numbers): sections in two columns, a title and its rows of label and value.
    private void PaintSections(IReadOnlyList<InfoSection> sections, int perColumn, float left = 56, float right = 820, float top = 100)
    {
        float[] ys = { top, top };
        for (int i = 0; i < sections.Count; i++)
        {
            int column = sections.Count <= 1 ? 0 : i < (sections.Count + 1) / 2 ? 0 : 1;
            float x = column == 0 ? left : right;
            InfoSection section = sections[i];
            Text(section.Title, x, ys[column], 26, G.Ink, bold: true);
            ys[column] += 38;
            foreach (InfoRow row in section.Rows)
            {
                Text(Fit(row.Label, 230, 18), x, ys[column], 18, G.Muted);
                Text(Fit(row.Value, 470, 18), x + 240, ys[column], 18, G.Ink);
                ys[column] += 26;
            }
            ys[column] += 20;
        }
    }

    private void PaintNet(IConsoleModel console)
    {
        PaintSections(console.Net, 2, right: 9999);
        // The graph: the last 30 s of round trip, bandwidth down and prediction error, each on its own scale.
        const float gx = 820, gy = 112, gw = 720, gh = 300;
        Text("THE LAST 30 SECONDS", gx, 100 - 2, 26, G.Ink, bold: true);
        Rect(gx, gy + 28, gw, gh, new Color(0, 0, 0, 0.3f));
        for (int i = 1; i < 4; i++)
        {
            Rect(gx, gy + 28 + gh * i / 4, gw, 1, new Color(1, 1, 1, 0.06f));
        }
        NetGraphView graph = console.NetGraph;
        float trip = GraphLine(graph.RoundTrip, graph.Start, 100, gx, gy + 28, gw, gh, NetRoundTrip);
        float down = GraphLine(graph.KilobitsIn, graph.Start, 200, gx, gy + 28, gw, gh, NetDown);
        float error = GraphLine(graph.PredictionError, graph.Start, 0.05f, gx, gy + 28, gw, gh, NetError);
        float lx = gx;
        foreach ((string label, Color colour) in new[]
                 {
                     (string.Create(CultureInfo.InvariantCulture, $"Round trip (to {trip:0} ms)"), NetRoundTrip),
                     (string.Create(CultureInfo.InvariantCulture, $"Down (to {down:0} kbit/s)"), NetDown),
                     (string.Create(CultureInfo.InvariantCulture, $"Prediction error (to {error * 1000:0} mm)"), NetError),
                 })
        {
            Rect(lx, gy + gh + 44, 14, 14, colour);
            lx += 22 + Text(label, lx + 22, gy + gh + 39, 18, G.Ink) + 28;
        }
        Text("Each line on its own scale, from zero to the most it shows. F3 shows the small graph in play.", gx, gy + gh + 72, 16, G.Muted);
    }

    private static readonly Color NetRoundTrip = new(0.42f, 0.80f, 1.00f);
    private static readonly Color NetDown = new(0.55f, 0.86f, 0.45f);
    private static readonly Color NetError = new(1.00f, 0.62f, 0.35f);

    // One line of the graph, on its own scale (from zero to the most it shows, at least the floor); returns that top.
    private float GraphLine(IReadOnlyList<float> history, int start, float floor, float x, float y, float w, float h, Color colour)
    {
        int n = history.Count;
        if (n < 2)
        {
            return floor;
        }
        float top = floor;
        foreach (float v in history)
        {
            top = Math.Max(top, float.IsFinite(v) ? v : 0);
        }
        var points = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float v = history[(start + i) % n];
            points[i] = new Vector2(x + w * i / (n - 1), y + h - 2 - (h - 4) * Math.Clamp(float.IsFinite(v) ? v / top : 0, 0, 1));
        }
        Line(colour, 1.5f, points);
        return top;
    }

    // ITEMS: categories on the left, the search and the amount above a grid of every item, the item under the pointer
    // (or the one chosen) on the right. An admin's click gives the amount chosen (Rust's item list).
    private void PaintItems(IConsoleModel console)
    {
        IReadOnlyList<string> keys = console.ItemKeys;
        var categories = new List<string> { "ALL" };
        foreach (string key in keys)
        {
            string category = Model.Item(key).Category;
            if (!categories.Contains(category))
            {
                categories.Add(category);
            }
        }
        for (int i = 0; i < categories.Count; i++)
        {
            string category = categories[i];
            int count = category == "ALL" ? keys.Count : keys.Count(k => Model.Item(k).Category == category);
            Button($"{category}  {count}", 44, ContentY + 4 + i * 41, 236, 40, () =>
            {
                _itemCategory = category;
                _itemScroll = 0;
            }, 18, _itemCategory == category ? G.Category : Transparent, left: true);
        }
        if (console.Admin)
        {
            Text("GIVE", 760, 96, 22, G.Ink, bold: true);
            for (int i = 0; i < GiveAmounts.Length; i++)
            {
                int amount = GiveAmounts[i];
                Button(amount.ToString(CultureInfo.InvariantCulture), 830 + i * 88, 88, 84, 38, () => _giveAmount = amount, 20,
                    _giveAmount == amount ? G.Selected : G.Panel);
            }
        }
        else
        {
            Text("Browse only: giving items needs admin rights.", 760, 97, 18, G.Muted);
        }

        string search = _itemSearch.Text.Trim();
        List<string> items = keys.Where(k => (_itemCategory == "ALL" || Model.Item(k).Category == _itemCategory) &&
            (search.Length == 0 || Model.Item(k).Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             k.Contains(search, StringComparison.OrdinalIgnoreCase))).ToList();
        const int columns = 10, rows = 5;
        const float cell = 88, cellH = 122, gx = 300, gy = 140;
        int totalRows = (items.Count + columns - 1) / columns;
        _itemScroll = Math.Clamp(_itemScroll, 0, Math.Max(0, totalRows - rows));
        _itemHovered = null;
        for (int i = _itemScroll * columns; i < items.Count && i < (_itemScroll + rows) * columns; i++)
        {
            int index = i - _itemScroll * columns;
            string key = items[i];
            float x = gx + index % columns * cell, y = gy + index / columns * cellH;
            bool chosen = key == _itemSelected;
            Rect2 area = Area(x, y, cell - 6, cellH - 8);
            if (Under(area))
            {
                _itemHovered = key;
            }
            Button("", x, y, cell - 6, cellH - 8, () =>
            {
                _itemSelected = key;
                if (Model.Console is { Admin: true } admin)
                {
                    admin.Give(key, _giveAmount);
                }
            }, fill: chosen ? new Color(G.Selected, 0.55f) : G.Panel, tip: key);
            Icon(key, x + 7, y + 6, cell - 20, cell - 20);
            TextIn(Fit(Model.Item(key).Name, cell - 12, 13), x + 3, y + cell - 10, cell - 12, 13, G.Ink, HorizontalAlignment.Center);
        }
        if (items.Count == 0)
        {
            Text("No item matches.", gx, gy + 10, 20, G.Muted);
        }
        if (totalRows > rows)
        {
            Text($"ROWS {_itemScroll + 1}–{Math.Min(totalRows, _itemScroll + rows)} OF {totalRows}", 44, 690, 16, G.Muted, bold: true);
            Text("The wheel scrolls", 44, 712, 16, G.Muted);
        }

        // The item shown: under the pointer, else the one chosen last.
        const float px = 1196, pw = 360;
        Rect(px, 88, pw, 650, G.Panel);
        if ((_itemHovered ?? _itemSelected) is { } shown)
        {
            ItemInfo info = Model.Item(shown);
            Icon(shown, px + (pw - 140) / 2, 104, 140, 140);
            TextIn(Fit(info.Name.ToUpperInvariant(), pw - 24, 28, bold: true), px, 256, pw, 28, G.Ink, HorizontalAlignment.Center, bold: true);
            TextIn(info.Key, px, 294, pw, 17, G.Muted, HorizontalAlignment.Center);
            TextIn($"{info.Category}  ·  STACKS TO {info.StackSize}", px, 318, pw, 16, G.Muted, HorizontalAlignment.Center, bold: true);
            Paragraph(info.Description, px + 20, 352, pw - 40, 18, G.Ink, 9);
            float y = 560;
            foreach (ItemStat stat in info.Stats.Take(5))
            {
                Text(stat.Label, px + 20, y, 16, G.Muted);
                TextIn(stat.Value, px + 20, y, pw - 40, 16, stat.Positive ? G.Ink : G.Warning, HorizontalAlignment.Right, bold: true);
                y += 22;
            }
            if (console.Admin)
            {
                TextIn($"A click gives {_giveAmount}", px, 704, pw, 18, G.Green, HorizontalAlignment.Center, bold: true);
            }
        }
        else
        {
            TextIn("Point at an item to see it.", px, 380, pw, 18, G.Muted, HorizontalAlignment.Center);
        }
    }

    // TOOLS: an admin's buttons, each running a console command (what it answered shows at the bottom right).
    private void PaintTools(IConsoleModel console)
    {
        ToolsView tools = console.Tools;
        if (!console.Admin)
        {
            Text("The tools need admin rights on this server.", 56, 104, 24, G.Ink, bold: true);
            Paragraph("Hosting a game makes you its admin; a dedicated server names its admins with --admin <name> or in admins.txt, " +
                "and --dev makes everyone one. Everyone may still use kill (the respawn screen), players, whereami and the view's commands.",
                56, 146, 760, 18, G.Muted, 4);
            Button("KILL MYSELF", 56, 250, 220, 44, () => Model.Console?.Run("kill"), 22);
            PaintAnswer(console, 56, 330, 760);
            return;
        }

        // The player: the cheats, health.
        float x = 56, y = 98;
        Section("PLAYER", x, y);
        y += 40;
        ToolToggle("GOD", tools.God, "god", x, y);
        ToolToggle("NOCLIP", tools.Noclip, "noclip", x + 116, y);
        ToolButtonAt("HEAL", "heal", x + 232, y, 108);
        ToolButtonAt("KILL", "kill", x + 348, y, 108);
        Text("Noclip: W A S D along the view, Space up, Ctrl down, Shift fast.", x, y + 48, 16, G.Muted);

        // The zone's time of day and how fast it runs.
        y += 92;
        Section("TIME OF DAY", x, y);
        double hours = _sliderHeld ? _sliderHours : tools.Hour;
        int minutes = (int)Math.Round(hours * 60) % (24 * 60);
        TextIn(string.Create(CultureInfo.InvariantCulture, $"{minutes / 60:00}:{minutes % 60:00}") + (tools.ClockSet ? "  SET" : ""), x, y + 4, 456, 24,
            tools.ClockSet ? G.Short : G.Ink, HorizontalAlignment.Right, bold: true);
        y += 46;
        _sliderTrack = Area(x, y + 6, 456, 10);
        Rect(x, y + 8, 456, 6, new Color(1, 1, 1, 0.12f));
        // Night, dawn, day and dusk along the track.
        for (int h = 0; h < 24; h++)
        {
            float shade = Math.Clamp(1 - Math.Abs(h - 12.5f) / 7.5f, 0.05f, 1);
            Rect(x + h * 19, y + 8, 19, 6, new Color(0.25f + 0.55f * shade, 0.30f + 0.45f * shade, 0.45f + 0.25f * shade, 0.75f));
        }
        Rect(x + (float)(hours / 24 * 456) - 3, y, 6, 22, G.Ink);
        Button("", x - 6, y - 4, 468, 30, () =>
        {
            _sliderHeld = true;
            _sliderHours = SliderHours();
        }, fill: Transparent);
        y += 36;
        foreach ((string label, string command, float at) in new[] { ("06:00", "time 6", 0f), ("12:00", "time 12", 1f), ("18:00", "time 18", 2f), ("00:00", "time 0", 3f) })
        {
            ToolButtonAt(label, command, x + at * 92, y, 86);
        }
        ToolButtonAt("RESET", "time reset", x + 368, y, 88);
        y += 58;
        Section("TIME SCALE", x, y);
        TextIn(string.Create(CultureInfo.InvariantCulture, $"×{tools.TimeScale:0.##}"), x, y + 4, 456, 24, G.Ink, HorizontalAlignment.Right, bold: true);
        y += 42;
        foreach ((string label, string command, float at) in new[] { ("STOP", "timescale 0", 0f), ("×1", "timescale 1", 1f), ("×10", "timescale 10", 2f), ("×60", "timescale 60", 3f) })
        {
            ToolToggleAt(label, command, Math.Abs(tools.TimeScale - (at switch { 0 => 0, 1 => 1, 2 => 10, _ => 60 })) < 1e-6, x + at * 115, y, 108);
        }

        // Building preferences are server-authorised, and every choice runs the same console command as typing it.
        y += 58;
        Section("BUILDING", x, y);
        y += 40;
        ToolToggleAt("FREE BUILD" + (tools.FreeBuild ? "  ON" : "  OFF"), tools.FreeBuild ? "freebuild off" : "freebuild on",
            tools.FreeBuild, x, y, 184);
        Text("No building resource costs.", x + 196, y + 10, 16, G.Muted);
        Text("Crafting needs no ingredients, blueprints or workbench.", x, y + 45, 16, G.Muted);
        Text("BGRADE · NEW BUILDING TIER", x, y + 99, 16, G.Muted, bold: true);
        for (int i = -1; i < tools.BuildGrades.Count; i++)
        {
            ToolButton grade = i < 0 ? new ToolButton("OFF", "bgrade off") : tools.BuildGrades[i];
            int at = i + 1;
            ToolToggleAt(grade.Label, grade.Command, tools.BuildGrade == i, x + at % 3 * 154, y + 123 + at / 3 * 40, 148);
        }
        Text("OFF uses normal building placement.", x, y + 209, 16, G.Muted);

        // Spawning: what each kind puts in front of the player.
        float sx = 548, sy = 98;
        Section("SPAWN IN FRONT OF YOU", sx, sy);
        sy += 40;
        foreach (ToolGroup group in tools.Spawns)
        {
            Text(group.Title, sx, sy + 2, 16, G.Muted, bold: true);
            sy += 24;
            for (int i = 0; i < group.Buttons.Count; i++)
            {
                if (i > 0 && i % 4 == 0)
                {
                    sy += 40;
                }
                ToolButtonAt(group.Buttons[i].Label, group.Buttons[i].Command, sx + i % 4 * 122, sy, 116, 18);
            }
            sy += 46;
        }

        // Teleporting to a player, and the server.
        float tx = 1060, ty = 98;
        Section("TELEPORT TO", tx, ty);
        ty += 40;
        IReadOnlyList<string> players = tools.Players;
        if (players.Count == 0)
        {
            Text("Nobody else is on.", tx, ty + 6, 18, G.Muted);
            ty += 42;
        }
        for (int i = 0; i < players.Count && i < 8; i++)
        {
            ToolButtonAt(players[i].ToUpperInvariant(), "teleport " + (players[i].Contains(' ', StringComparison.Ordinal) ? $"\"{players[i]}\"" : players[i]), tx + i % 2 * 246, ty + i / 2 * 40, 240, 18);
        }
        if (players.Count > 0)
        {
            ty += (Math.Min(players.Count, 8) + 1) / 2 * 40 + 2;
        }
        ToolButtonAt("MY MAP MARKER", "tpmarker", tx, ty, 240, 18);
        ty += 58;
        Section("SERVER", tx, ty);
        ty += 40;
        ToolButtonAt("ENTITY COUNTS", "ents", tx, ty, 240, 18);
        ToolButtonAt("SAVE", "save", tx + 246, ty, 240, 18);
        ty += 40;
        ToolButtonAt("STATUS", "status", tx, ty, 240, 18);
        ToolButtonAt("PLAYERS", "players", tx + 246, ty, 240, 18);
        ty += 52;

        Section("CAMERA", tx, ty);
        ty += 40;
        ToolToggleAt("THIRD PERSON" + (tools.ThirdPerson ? "  ON" : "  OFF"), tools.ThirdPerson ? "thirdperson off" : "thirdperson on",
            tools.ThirdPerson, tx, ty, 240);
        Text("Hold right mouse to orbit · Mouse wheel to zoom", tx, ty + 45, 16, G.Muted);
        Text("Close F1 to control the camera.", tx, ty + 66, 16, G.Muted);
        ty += 96;
        PaintAnswer(console, tx, ty, 486);
    }

    // The newest answer, under the buttons that asked for it.
    private void PaintAnswer(IConsoleModel console, float x, float y, float w)
    {
        IReadOnlyList<ConsoleLine> answer = console.LastAnswer;
        if (answer.Count == 0)
        {
            return;
        }
        float bottom = PanelY + PanelH - 16;
        Rect(x - 8, y - 6, w + 16, bottom - y + 6, new Color(0, 0, 0, 0.28f));
        foreach (ConsoleLine line in answer)
        {
            Color colour = line.Kind switch
            {
                ConsoleLineKind.Error => G.Warning,
                ConsoleLineKind.Success => G.Green,
                ConsoleLineKind.Input => G.Ink,
                _ => ServerReply,
            };
            foreach (string part in Wrap((line.Kind == ConsoleLineKind.Input ? "> " : "") + line.Text.Replace('\t', ' '), w, 16))
            {
                if (y > bottom - 22)
                {
                    return;
                }
                Text(part, x, y, 16, colour);
                y += 21;
            }
        }
    }

    private void ToolButtonAt(string label, string command, float x, float y, float w, float size = 20)
    {
        // A long label takes a smaller size rather than running out of its button.
        while (size > 13 && Measure(label, size, bold: true, condensed: true) > w - 12)
        {
            size--;
        }
        Button(label, x, y, w, 36, () => Model.Console?.Run(command), size);
    }

    private void ToolToggle(string label, bool on, string command, float x, float y) =>
        ToolToggleAt(label + (on ? "  ON" : "  OFF"), command, on, x, y, 108);

    private void ToolToggleAt(string label, string command, bool on, float x, float y, float w) =>
        Button(label, x, y, w, 36, () => Model.Console?.Run(command), 20, on ? ToolOn : G.Panel);
}
