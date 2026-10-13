using System;
using System.Collections.Generic;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Targets;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

// The interact key in the view model (GROUND_SYSTEMS.md §5, GROUND.md §8.4): every frame the one targeting query says
// what E acts on and the interaction rules what it offers; the prompt reads its primary verb and the target's name
// ("E  OPEN WOODEN DOOR"), with the hold hint when there is more than one option. E's radial binding (held 0.35 s) opens the
// options of what was aimed at the press; a tap does the primary one. A verb that needs a code asks for it on the keypad
// first; a verb that opens a panel (the tech tree) opens it; any other is the one interaction intent.
public sealed partial class ClientGroundUi
{
    private readonly List<InteractOption> _menu = new();
    private InteractAim? _aim;
    private TargetRef _aimed;
    private Num.Vector3 _eye;

    /// <summary>What the interact key acts on now (null: nothing there offers anything).</summary>
    public InteractAim? Aimed => _aim;

    /// <summary>The game's data (the verbs' labels and glyphs, the lines).</summary>
    internal GameContent Content => C;

    /// <summary>The target aimed at now (none: nothing).</summary>
    public TargetRef AimedTarget => _aim != null ? _aimed : TargetRef.None;

    /// <summary>
    /// Asks the player for a code (the keypad, its title), then sends what they entered: the view shows the keypad, and
    /// keeps the intent's number the answer comes back to (the lock's beep).
    /// </summary>
    public Action<string, Func<string, uint>>? AskCode { get; set; }

    /// <summary>Opens a panel of the interface by the name a verb gives it (the tech tree).</summary>
    public Action<string>? OpenPanel { get; set; }

    /// <summary>
    /// Every frame, from the eye along the view: what E acts on (none while <paramref name="can"/> is false: dead, down,
    /// flying), and the prompt for it.
    /// </summary>
    public void Aim(Num.Vector3 eye, Num.Vector3 direction, bool can)
    {
        _eye = eye;
        _aim = can ? _client.AimInteraction(eye, direction) : null;
        _aimed = _aim?.Target ?? TargetRef.None;
        Prompt = _aim != null ? PromptFor(_aim) : null;
    }

    // "E  OPEN WOODEN DOOR" (its primary verb and the target's name), or the reason it can't be done; just the name when a
    // tap does nothing; the hold hint when there is more to choose.
    private InteractionPrompt PromptFor(InteractAim aim)
    {
        int menu = 0;
        foreach (InteractOption option in aim.Options)
        {
            menu += C.Interactions.Verb(option.Verb)?.Menu == true ? 1 : 0;
        }
        string name = aim.Facts.Name ?? "";
        if (aim.Primary < 0 || C.Interactions.Verb(aim.Options[aim.Primary].Verb) is not { } verb)
        {
            return new InteractionPrompt("E", name.ToUpperInvariant(), MoreOptions: menu > 0);
        }
        InteractOption primary = aim.Options[aim.Primary];
        string action = primary.Enabled ? $"{verb.Label} {name}" : C.Texts.Line(primary.Reason);
        return new InteractionPrompt("E", action.ToUpperInvariant(), MoreOptions: menu > 1);
    }

    // E pressed: the options of what is aimed at now, held for the radial (none: E goes to the game).
    private IRadialSource? OpenInteraction() => _aim != null ? new InteractionRadial(this, _aimed) : null;

    /// <summary>The menu options of a target from where the player is now, ranked (the radial reads them every frame).</summary>
    internal IReadOnlyList<InteractOption> MenuOf(in TargetRef target, out int primary)
    {
        primary = -1;
        if (!_client.Options(target, _eye, _menu, out _))
        {
            _menu.Clear();
            return _menu;
        }
        for (int i = _menu.Count - 1; i >= 0; i--)
        {
            if (C.Interactions.Verb(_menu[i].Verb)?.Menu != true)
            {
                _menu.RemoveAt(i);
            }
        }
        primary = InteractionRules.Primary(_menu);
        return _menu;
    }

    /// <summary>
    /// Does a verb to a target: a code verb asks for the code first (the keypad), a panel's opens it, any other is sent.
    /// The radial passes enabled options only; a disabled one says why.
    /// </summary>
    internal void Do(in TargetRef target, InteractOption option)
    {
        if (C.Interactions.Verb(option.Verb) is not { } verb)
        {
            return;
        }
        if (!option.Enabled)
        {
            ShowNotice(C.Texts.Line(option.Reason));
            return;
        }
        TargetRef at = target;
        if (verb.Panel != null)
        {
            OpenPanel?.Invoke(verb.Panel);
        }
        else if (verb.Code)
        {
            AskCode?.Invoke(verb.Label.ToUpperInvariant(), code => Me.Interact(at, verb.Id, code: code));
        }
        else
        {
            Me.Interact(at, verb.Id);
        }
    }

    /// <summary>A device's switch in the loot panel: the verb of the looted thing that turns it on or off (light, turn on…).</summary>
    public void SetBurning(bool on)
    {
        if (Me.LootTarget.IsNone)
        {
            return;
        }
        var target = new TargetRef(TargetFamily.Thing, Me.LootTarget.Value);
        VerbSwitch wanted = on ? VerbSwitch.On : VerbSwitch.Off;
        foreach (InteractOption option in MenuOf(target, out _))
        {
            if (C.Interactions.Verb(option.Verb)?.Switch == wanted)
            {
                Do(target, option);
                return;
            }
        }
    }
}

/// <summary>
/// E's radial (GROUND_SYSTEMS.md §4, §5): the options of what was aimed at when E went down, read again every frame from
/// where the player is (they can walk while it is open), each with its verb's glyph, label and line, a disabled one with
/// the reason; its current option is the primary one, which a tap does.
/// </summary>
public sealed class InteractionRadial : IRadialSource
{
    private readonly ClientGroundUi _model;
    private readonly TargetRef _target;
    private readonly List<RadialOption> _options = new();
    private readonly List<InteractOption> _choices = new();
    private ulong _frame = ulong.MaxValue;
    private int _current = -1;

    public InteractionRadial(ClientGroundUi model, in TargetRef target)
    {
        _model = model;
        _target = target;
    }

    public IReadOnlyList<RadialOption> Options
    {
        get
        {
            Refresh();
            return _options;
        }
    }

    public int Current
    {
        get
        {
            Refresh();
            return _current;
        }
    }

    public void Choose(int index)
    {
        Refresh();
        if (index >= 0 && index < _choices.Count)
        {
            _model.Do(_target, _choices[index]);
        }
    }

    // Once a frame: the target's options now.
    private void Refresh()
    {
        ulong frame = Godot.Engine.GetProcessFrames();
        if (frame == _frame)
        {
            return;
        }
        _frame = frame;
        _options.Clear();
        _choices.Clear();
        GameContent content = _model.Content;
        IReadOnlyList<InteractOption> menu = _model.MenuOf(_target, out int primary);
        foreach (InteractOption option in menu)
        {
            VerbDef verb = content.Interactions.Verb(option.Verb)!;
            _choices.Add(option);
            _options.Add(new RadialOption(RadialIcon.Glyph(verb.Glyph), verb.Label.ToUpperInvariant(), verb.Description,
                Reason: option.Enabled ? null : content.Texts.Line(option.Reason)));
        }
        _current = primary;
    }
}
