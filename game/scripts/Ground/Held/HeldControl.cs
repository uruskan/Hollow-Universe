using System;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>Buttons pressed for the hands since the last frame: attack (left mouse), aim (right mouse), R.</summary>
[Flags]
public enum HandPress
{
    None = 0,
    Attack = 1,
    Aim = 2,
    Reload = 4,
}

/// <summary>The hands' buttons this frame: attack and aim as held, and what was pressed since the last frame.</summary>
public readonly record struct HandButtons(bool Attack, bool Aim, HandPress Pressed)
{
    public bool Clicked => (Pressed & HandPress.Attack) != 0;

    public bool AimClicked => (Pressed & HandPress.Aim) != 0;

    public bool Reload => (Pressed & HandPress.Reload) != 0;
}

/// <summary>
/// What a held control sees (GROUND_SYSTEMS.md §7): the client, the item in hand and its belt slot, the view, and what it
/// may act through (the building's tools and radials, the first-person arms, placing, notices). One per game view, updated
/// each frame.
/// </summary>
public sealed class HeldContext
{
    public required GameClient Client { get; init; }

    public required BuildView Build { get; init; }

    public required FirstPersonView Arms { get; init; }

    /// <summary>Placing what is held (package P2's system behind its interface).</summary>
    public required IPlacement Placement { get; init; }

    /// <summary>A short notice for the player.</summary>
    public required Action<string> Say { get; init; }

    public LocalPlayer Me => Client.Local;

    public GameContent Content => Client.Content;

    /// <summary>The item in hand and its belt slot, this frame.</summary>
    public ItemDef Item { get; internal set; } = null!;

    public int Slot { get; internal set; }

    /// <summary>The view, this frame (radians, the movement code's convention).</summary>
    public float Yaw { get; internal set; }

    public float Pitch { get; internal set; }

    /// <summary>A refusal's line (texts.json), as a notice.</summary>
    public void Refuse(Outcome outcome) => Say(Content.Texts.Line(outcome));
}

/// <summary>
/// What an item in hand does on this client (GROUND_SYSTEMS.md §7): one control per held behaviour (<see cref="HeldUse"/>,
/// which the item's part gives), found in a registry. The ground view asks the item in hand's control what the mouse
/// buttons and R do, which radial a held input opens, what the HUD's ammo counter shows and how the first-person arms
/// move; nothing else in the game asks what kind of item is held. Free look, death and the pilot's seat keep the hands
/// still in one place, the view's, before any control is asked.
/// </summary>
public interface IHeldControl
{
    /// <summary>
    /// This frame's buttons for the item in hand. <paramref name="input"/> comes in with no attack or aim for the tick, and
    /// leaves with what the tick sends (a swing held, a bow drawn, a trigger pressed); what acts at once (placing, throwing,
    /// eating) acts here.
    /// </summary>
    void Hands(HeldContext ctx, ref InputFrame input, in HandButtons buttons);

    /// <summary>The radial a held input opens with it in hand (the aim button: the plan's pieces, the hammer's work; R: a gun's rounds), or null: the input goes to <see cref="Hands"/>.</summary>
    IRadialSource? Radial(HeldContext ctx, RadialInput input) => null;

    /// <summary>The HUD's ammo counter while it is held, or null.</summary>
    HeldAmmo? Ammo(HeldContext ctx) => null;

    /// <summary>Each frame, with the view: where what it places would go (its ghost).</summary>
    void Aim(HeldContext ctx, Num.Vector3 eye, Num.Vector3 forward)
    {
    }

    /// <summary>Each frame after the tick's input: the first-person arms (a swing in the server's rhythm, a bow drawn, a gun dressed).</summary>
    void Arms(HeldContext ctx, double delta, in InputFrame input)
    {
    }
}

/// <summary>The held controls by held behaviour (every one but <see cref="HeldUse.None"/> has its control, or the game doesn't start).</summary>
public static class HeldControls
{
    /// <summary>How long R is held before a gun's rounds open (GROUND_SYSTEMS.md §11: RustMVP's 0.35 s, as E's).</summary>
    public const double RadialHoldSeconds = 0.35;

    public static Registry<IHeldControl> Create()
    {
        var registry = Registry<IHeldControl>.ForEnum<HeldUse>("held controls", complete: true, HeldUse.None);
        registry.Add((int)HeldUse.Melee, new MeleeControl());
        registry.Add((int)HeldUse.Bow, new BowControl());
        registry.Add((int)HeldUse.Firearm, new FirearmControl());
        registry.Add((int)HeldUse.Throwable, new ThrowableControl());
        registry.Add((int)HeldUse.Placer, new PlacerControl());
        registry.Add((int)HeldUse.Planner, new PlannerControl());
        registry.Add((int)HeldUse.Hammer, new HammerControl());
        // Eating and learning go through the inventory's actions: a click does the held part's first button.
        var usesItsPart = new UsesItsPartControl();
        registry.Add((int)HeldUse.Consumable, usesItsPart);
        registry.Add((int)HeldUse.Blueprint, usesItsPart);
        return registry.Seal();
    }
}
