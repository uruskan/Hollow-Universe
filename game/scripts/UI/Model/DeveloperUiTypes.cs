using System;
using System.Collections.Generic;

namespace HollowUniverse.UI.Model;

// The F2 developer panel's values (GROUND.md §8.5): plain C#, no Godot types, as the rest of the view model. The game's
// adapter (ClientDeveloper) fills them from the client's dev module; MockDeveloper fakes them for the viewer.

/// <summary>The F2 panel's tabs.</summary>
public enum DeveloperTab
{
    Server,
    Players,
    Look,
    Draw,
    Log,
}

/// <summary>
/// The SERVER tab's graph: the average and the longest tick of each half second, the oldest at <see cref="Start"/>,
/// <see cref="Count"/> samples, against the tick's budget.
/// </summary>
public sealed record TickGraphView(IReadOnlyList<float> Average, IReadOnlyList<float> Worst, int Start, int Count, float BudgetMs)
{
    public static readonly TickGraphView Empty = new(Array.Empty<float>(), Array.Empty<float>(), 0, 0, 1000f / 30);
}

/// <summary>A section of the tick's profile: its name, its depth in the profile's tree, its average and longest tick over the last half second.</summary>
public readonly record struct SectionRow(string Name, int Depth, float AverageMs, float WorstMs);

/// <summary>A player on the server, for the PLAYERS tab.</summary>
public readonly record struct PlayerRow(string Name, bool Admin, bool Me, string State, string Where, int PingMs, float KilobitsDown, float KilobitsUp,
    int OnTimePercent, int InView);

/// <summary>A toggle of the DRAW tab: its key, what it draws, a line on it, whether it is on, and whether the server must agree (an admin's).</summary>
public readonly record struct DrawToggle(string Key, string Label, string Help, bool On, bool NeedsAdmin);

/// <summary>
/// The F2 panel's model (GROUND.md §8.5): the server's tick and what it holds, everyone on, what the crosshair is on as
/// both ends see it, the drawings, the server's log. Reads are polled every frame.
/// </summary>
public interface IDeveloperModel
{
    /// <summary>The server answers this player (an admin's panel); without it only the DRAW tab's own toggles work.</summary>
    bool Admin { get; }

    /// <summary>The server's stats came lately.</summary>
    bool Connected { get; }

    /// <summary>The panel opened or closed: an open one is sent the server's stats.</summary>
    void Watch(bool on);

    /// <summary>The tab shown (the adapter asks the server only for what the tab needs).</summary>
    DeveloperTab Shown { get; set; }

    /// <summary>The line over the SERVER tab: the tick now, or why nothing shows.</summary>
    string Headline { get; }

    TickGraphView Ticks { get; }

    /// <summary>The profile's sections, in the tree's order.</summary>
    IReadOnlyList<SectionRow> Sections { get; }

    /// <summary>What the zone holds, the game's counters, the process: titled blocks of label and value.</summary>
    IReadOnlyList<InfoSection> Counts { get; }

    IReadOnlyList<PlayerRow> Players { get; }

    /// <summary>What the crosshair is on, as the LOOK tab titles it ("Wooden door, 2.4 m"), or why nothing shows.</summary>
    string LookTitle { get; }

    /// <summary>The client's own facts of it.</summary>
    IReadOnlyList<InfoRow> LookHere { get; }

    /// <summary>The server's description of it, as it last answered.</summary>
    IReadOnlyList<InfoRow> LookServer { get; }

    /// <summary>The last intents answered, oldest first: what, the outcome, how long ago; the label says whether it was refused.</summary>
    IReadOnlyList<InfoRow> Answers { get; }

    IReadOnlyList<DrawToggle> Draws { get; }

    void ToggleDraw(string key);

    /// <summary>The server's log, oldest first.</summary>
    IReadOnlyList<string> Log { get; }

    /// <summary>Rises with every line added (the log follows the newest line).</summary>
    int LogRevision { get; }
}
