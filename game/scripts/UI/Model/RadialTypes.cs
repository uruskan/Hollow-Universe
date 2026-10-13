using System;
using System.Collections.Generic;

namespace HollowUniverse.UI.Model;

// The radial menu's contract (GROUND_SYSTEMS.md §4, GROUND.md §8): one control, drawn in RustMVP's look, on any source
// of options. The control never knows what opened it or what its options mean; a source says what to show and what a
// choice does. Plain C#, no Godot types.

/// <summary>
/// What an option shows in the ring and, larger, in the middle: one of RustMVP's outline glyphs by name (the radial's
/// glyph table: a building piece's key in building.json, <c>upgrade</c>, <c>rotate</c>, <c>demolish</c>, <c>door</c>,
/// <c>lock</c>, <c>unlock</c>, <c>keypad</c>, <c>pick-up</c>), or an item's icon by the item's key (rounds in the ammo
/// radial).
/// </summary>
public readonly record struct RadialIcon(string Name, bool IsItem = false)
{
    public static RadialIcon Glyph(string name) => new(name);

    public static RadialIcon Item(string key) => new(key, true);
}

/// <summary>
/// One option of a radial: its icon, title, a line of description and a cost line ("25 x WOOD (915)"). An option with
/// a <see cref="Reason"/> can't be chosen: the middle says why, and choosing it shows the reason instead of acting. A
/// <see cref="Warning"/> shows in the same place but doesn't stop the choice (the plan's "Not enough wood": the piece is
/// still chosen, and its ghost refuses).
/// </summary>
public sealed record RadialOption(RadialIcon Icon, string Title, string Description, string? Cost = null, string? Reason = null,
    string? Warning = null)
{
    public bool Enabled => Reason == null;
}

/// <summary>
/// What a radial offers and does (GROUND_SYSTEMS.md §4): the building plan's pieces, the hammer's actions, the interaction
/// verbs on what is aimed at (P1), a gun's rounds (P3). The radial reads <see cref="Options"/> every frame while it is
/// open, so they always show the state now.
/// </summary>
public interface IRadialSource
{
    /// <summary>The options, clockwise.</summary>
    IReadOnlyList<RadialOption> Options { get; }

    /// <summary>
    /// The option shown while the pointer rests in the middle, which is also what a tap of a held trigger chooses (the
    /// plan's piece, the primary interaction, the loaded round), or −1. RustMVP's wheels show their first.
    /// </summary>
    int Current => 0;

    /// <summary>
    /// Twelve o'clock falls between the last option and the first (the building plan's 20, as in Rust) rather than on the
    /// first option's middle.
    /// </summary>
    bool SplitAtTop => false;

    /// <summary>Chooses an option (the radial only passes enabled ones).</summary>
    void Choose(int index);

    /// <summary>The radial closed without a choice (Esc, the right mouse, another screen).</summary>
    void Cancel()
    {
    }
}

/// <summary>
/// The held inputs that open radials, named as Rust binds them: the aim button (the right mouse: the building plan's and
/// the hammer's radials), interact (E: the options on what is aimed at) and reload (R: a gun's rounds). The interface
/// maps each to its key or button in one table.
/// </summary>
public enum RadialInput : byte
{
    Aim,
    Interact,
    Reload,
}

/// <summary>
/// A held input that opens a radial (GROUND_SYSTEMS.md §4). Pressed, it asks <see cref="Open"/> for a source; none means
/// the input isn't the radial's and goes on to the game. Held for <see cref="HoldSeconds"/> (0: at once), the radial
/// opens on that source with the pointer in the middle; released, the option under the pointer is chosen. Released
/// sooner, it was a tap: the source's <see cref="IRadialSource.Current"/> option is chosen (E's primary action, R's
/// reload). Esc or the right mouse cancels, and the release after that does nothing.
/// </summary>
public sealed record RadialBinding(RadialInput Input, double HoldSeconds, Func<IRadialSource?> Open);

/// <summary>
/// A radial source made of options that each carry what choosing them does, listed again by <c>fill</c> whenever they are
/// read, so they always show the state now (the hammer's actions on the piece aimed at, a sample door's options).
/// </summary>
public sealed class RadialMenu : IRadialSource
{
    private readonly Action<RadialMenu> _fill;
    private readonly Func<int>? _current;
    private readonly Action? _cancel;
    private readonly List<RadialOption> _options = new();
    private readonly List<Action> _choices = new();

    public RadialMenu(Action<RadialMenu> fill, Func<int>? current = null, bool splitAtTop = false, Action? cancel = null)
    {
        _fill = fill;
        _current = current;
        _cancel = cancel;
        SplitAtTop = splitAtTop;
    }

    public IReadOnlyList<RadialOption> Options
    {
        get
        {
            Refill();
            return _options;
        }
    }

    public int Current => _current?.Invoke() ?? 0;

    public bool SplitAtTop { get; }

    /// <summary>Adds an option and what choosing it does (called by the fill).</summary>
    public void Add(RadialOption option, Action choose)
    {
        _options.Add(option);
        _choices.Add(choose);
    }

    public void Choose(int index)
    {
        Refill();
        if (index >= 0 && index < _choices.Count)
        {
            _choices[index]();
        }
    }

    public void Cancel() => _cancel?.Invoke();

    private void Refill()
    {
        _options.Clear();
        _choices.Clear();
        _fill(this);
    }
}
