using System;
using System.Collections.Generic;

namespace HollowUniverse.UI.Model;

// The F1 panel's values (GROUND.md §8.2): plain C#, no Godot types, as the rest of the view model. The game's adapter
// (ClientConsole) fills them from the client and the console's commands; MockConsole fakes them for the viewer.

/// <summary>The F1 panel's tabs, in Rust's order.</summary>
public enum ConsoleTab
{
    Console,
    Info,
    Net,
    Items,
    Tools,
}

/// <summary>What a console line is, for its colour.</summary>
public enum ConsoleLineKind : byte
{
    /// <summary>A line the player typed (or a button ran), shown after "> ".</summary>
    Input,

    /// <summary>The game's own answer (help, find, the view's settings).</summary>
    Output,

    /// <summary>The server's answer.</summary>
    Server,

    /// <summary>Something done.</summary>
    Success,

    /// <summary>A refusal or an error.</summary>
    Error,

    /// <summary>A note from the console itself (the greeting).</summary>
    System,
}

/// <summary>
/// A line of the console's log. A tab character splits it into two columns (a command's usage and what it does, a
/// label and its value).
/// </summary>
public readonly record struct ConsoleLine(string Text, ConsoleLineKind Kind);

/// <summary>A completed line, and the words it could go on as when there were several.</summary>
public readonly record struct ConsoleCompletionView(string Line, IReadOnlyList<string> Candidates);

/// <summary>A label and its value (the INFO and NET tabs).</summary>
public readonly record struct InfoRow(string Label, string Value);

/// <summary>A titled block of rows.</summary>
public sealed record InfoSection(string Title, IReadOnlyList<InfoRow> Rows);

/// <summary>The NET tab's graph: one sample a quarter of a second, the oldest at <see cref="Start"/>.</summary>
public sealed record NetGraphView(IReadOnlyList<float> RoundTrip, IReadOnlyList<float> KilobitsIn, IReadOnlyList<float> PredictionError, int Start)
{
    public static readonly NetGraphView Empty = new(Array.Empty<float>(), Array.Empty<float>(), Array.Empty<float>(), 0);
}

/// <summary>A button of the TOOLS tab: its label and the console line it runs.</summary>
public readonly record struct ToolButton(string Label, string Command);

/// <summary>A titled row of tool buttons (a kind of thing to spawn).</summary>
public sealed record ToolGroup(string Title, IReadOnlyList<ToolButton> Buttons);

/// <summary>What the TOOLS tab shows: the cheats, the zone's clock, the players to go to, what can be spawned.</summary>
public sealed record ToolsView(bool God, bool Noclip, double Hour, double TimeScale, bool ClockSet, IReadOnlyList<string> Players,
    IReadOnlyList<ToolGroup> Spawns)
{
    /// <summary>The admin's session-only camera shows their own full character.</summary>
    public bool ThirdPerson { get; init; }

    /// <summary>The server allows building and crafting without their resource costs or recipe prerequisites.</summary>
    public bool FreeBuild { get; init; }

    /// <summary>The selected building grade's catalogue index; -1 uses normal placement.</summary>
    public int BuildGrade { get; init; } = -1;

    /// <summary>The building catalogue's grades in index order, each with its bgrade command.</summary>
    public IReadOnlyList<ToolButton> BuildGrades { get; init; } = Array.Empty<ToolButton>();

    public static readonly ToolsView Empty = new(false, false, 12, 1, false, Array.Empty<string>(), Array.Empty<ToolGroup>());
}

/// <summary>
/// The F1 panel's model (GROUND.md §8.2): the console's log, history and completion; the INFO, NET, ITEMS and TOOLS
/// tabs' reads; the lines it runs. Reads are polled every frame.
/// </summary>
public interface IConsoleModel
{
    /// <summary>The log, oldest first (a few hundred lines at most).</summary>
    IReadOnlyList<ConsoleLine> Lines { get; }

    /// <summary>Rises with every line added or the log cleared (the log follows the newest line).</summary>
    int Revision { get; }

    /// <summary>The player may run the admin commands here (the server says).</summary>
    bool Admin { get; }

    /// <summary>Runs a typed line (the game's own commands at once, the rest on the server as this player).</summary>
    void Submit(string line);

    /// <summary>Runs a line for a button of a tab: it shows in the log as if typed.</summary>
    void Run(string line);

    /// <summary>
    /// The history, a step older (−1) or newer (+1) than the line shown last; <paramref name="typed"/> is what is in the
    /// field (kept to come back to). Null when there is nothing further that way.
    /// </summary>
    string? History(int step, string typed);

    /// <summary>Tab completion of a line.</summary>
    ConsoleCompletionView Complete(string line);

    /// <summary>The lines of the newest answer (the TOOLS tab shows them under its buttons).</summary>
    IReadOnlyList<ConsoleLine> LastAnswer { get; }

    /// <summary>The INFO tab: the game, this computer, the world, the server.</summary>
    IReadOnlyList<InfoSection> Info { get; }

    /// <summary>The NET tab's numbers, and its graph.</summary>
    IReadOnlyList<InfoSection> Net { get; }

    NetGraphView NetGraph { get; }

    /// <summary>Every item key, in the catalogue's order (the ITEMS tab; names and categories come from <see cref="IGroundUi.Item"/>).</summary>
    IReadOnlyList<string> ItemKeys { get; }

    /// <summary>Gives the player an item (an admin's: the ITEMS tab's click).</summary>
    void Give(string key, int amount);

    ToolsView Tools { get; }
}
