using System.Linq;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using HollowUniverse.UI.Model;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

// The held controls (GROUND_SYSTEMS.md §7), one per held behaviour; registered in HeldControls.Create.

/// <summary>Melee: the attack button held swings, in the tool's rhythm (the server's); the arms swing with it.</summary>
public sealed class MeleeControl : IHeldControl
{
    private double _swing = -1;
    private int _equipped = -1;

    public void Hands(HeldContext ctx, ref InputFrame input, in HandButtons buttons)
    {
        input.Primary = buttons.Attack;
        input.PrimaryPressed = buttons.Clicked;
    }

    // Swings follow the server's rhythm: one every swing time while the attack is held, once the item is ready.
    public void Arms(HeldContext ctx, double delta, in InputFrame input)
    {
        MeleeStats melee = ctx.Item.Melee!;
        if (_equipped != ctx.Arms.Equipped)
        {
            _equipped = ctx.Arms.Equipped;
            _swing = -1;
        }
        if (ctx.Arms.SinceEquip <= 0.3)
        {
            return;
        }
        bool swingNow = false;
        if (_swing < 0 && input.Primary)
        {
            _swing = 0;
            swingNow = true;
        }
        else if (_swing >= 0)
        {
            _swing += delta;
            if (_swing >= melee.SwingSeconds)
            {
                _swing = input.Primary ? 0 : -1;
                swingNow = input.Primary;
            }
        }
        if (swingNow)
        {
            ctx.Arms.Swing(ctx.Item);
        }
    }
}

/// <summary>The bow: the aim button draws, a press of the attack button looses; the string sounds as it is drawn.</summary>
public sealed class BowControl : IHeldControl
{
    private bool _drawing;

    public void Hands(HeldContext ctx, ref InputFrame input, in HandButtons buttons)
    {
        input.Primary = buttons.Attack;
        input.Secondary = buttons.Aim;
        input.PrimaryPressed = buttons.Clicked;
    }

    public HeldAmmo? Ammo(HeldContext ctx) => new HeldAmmo(0, ctx.Item.Bow!.Ammo.Sum(a => ctx.Me.Inventory.Count(a)));

    public void Arms(HeldContext ctx, double delta, in InputFrame input)
    {
        bool drawing = input.Secondary;
        if (drawing && !_drawing)
        {
            Sounds.Flat(Sounds.Item(ctx.Item.Key, "draw") ?? "bow.draw");
        }
        _drawing = drawing;
    }
}

/// <summary>
/// A firearm: the attack button fires (the predicted gun, <see cref="LocalGun"/>, reads the tick's buttons), the aim
/// button aims down the sights; R tapped reloads with the loaded kind and held opens its rounds (<see cref="AmmoRadial"/>).
/// </summary>
public sealed class FirearmControl : IHeldControl
{
    private AmmoRadial? _rounds;

    public void Hands(HeldContext ctx, ref InputFrame input, in HandButtons buttons)
    {
        input.Primary = buttons.Attack;
        input.Secondary = buttons.Aim;
        input.PrimaryPressed = buttons.Clicked;
    }

    public IRadialSource? Radial(HeldContext ctx, RadialInput input) =>
        input == RadialInput.Reload && ctx.Me.Gun.Weapon != null ? _rounds ??= new AmmoRadial(ctx) : null;

    public HeldAmmo? Ammo(HeldContext ctx) => ctx.Me.Gun is { Weapon: not null } gun ? new HeldAmmo(gun.Loaded, gun.Reserve, gun.Reloading) : null;

    // The gun's attachments as its stack has them (sight, muzzle part, light), drawn with the arms.
    public void Arms(HeldContext ctx, double delta, in InputFrame input) => ctx.Arms.DressGun();
}

/// <summary>
/// An explosive: the attack button throws it; the aim button puts a charge on the surface aimed at within reach, or lobs a
/// grenade gently. One every cooldown.
/// </summary>
public sealed class ThrowableControl : IHeldControl
{
    private double _thrownAt = -10;

    public void Hands(HeldContext ctx, ref InputFrame input, in HandButtons buttons)
    {
        if (!buttons.Clicked && !buttons.AimClicked)
        {
            return;
        }
        ThrowableStats throwable = ctx.Item.Throwable!;
        if (ctx.Client.ClockSeconds - _thrownAt < throwable.CooldownSeconds)
        {
            return;
        }
        _thrownAt = ctx.Client.ClockSeconds;
        bool secondary = buttons.AimClicked && !buttons.Clicked;
        if (secondary && throwable.Placeable)
        {
            ctx.Me.PlaceCharge(ctx.Slot, ctx.Yaw, ctx.Pitch);
            return;
        }
        ctx.Me.Throw(ctx.Slot, ctx.Yaw, ctx.Pitch, lob: secondary);
        ctx.Arms.Throw(ctx.Item.Key);
    }
}

/// <summary>
/// A placeable (a deployable, a door, a lock, the tool cupboard, a ship): the attack button places it where the aim is, R
/// turns it. Placing is package P2's system, behind <see cref="IPlacement"/>.
/// </summary>
public class PlacerControl : IHeldControl
{
    public void Hands(HeldContext ctx, ref InputFrame input, in HandButtons buttons)
    {
        if (buttons.Clicked)
        {
            ctx.Placement.Place(ctx);
        }
        if (buttons.Reload)
        {
            ctx.Placement.Turn(ctx);
        }
    }

    public void Aim(HeldContext ctx, Num.Vector3 eye, Num.Vector3 forward) => ctx.Placement.Aim(ctx, eye, forward);
}

/// <summary>The building plan: places the piece chosen in its radial (the aim button opens it), R turning it.</summary>
public sealed class PlannerControl : PlacerControl, IHeldControl
{
    public IRadialSource? Radial(HeldContext ctx, RadialInput input) => input == RadialInput.Aim ? ctx.Build.Plan : null;
}

/// <summary>The hammer: the attack button repairs the piece aimed at; the aim button opens its radial (upgrade, turn, demolish).</summary>
public sealed class HammerControl : IHeldControl
{
    public void Hands(HeldContext ctx, ref InputFrame input, in HandButtons buttons)
    {
        if (buttons.Clicked)
        {
            ctx.Build.Use();
        }
    }

    public IRadialSource? Radial(HeldContext ctx, RadialInput input) => input == RadialInput.Aim ? ctx.Build.HammerRadial : null;
}

/// <summary>
/// Food, medicine and blueprints: a click does what the held part's first inventory button does (EAT, USE, LEARN), through
/// the same item action as the inventory's; a refused one says why.
/// </summary>
public sealed class UsesItsPartControl : IHeldControl
{
    public void Hands(HeldContext ctx, ref InputFrame input, in HandButtons buttons)
    {
        if (!buttons.Clicked || ctx.Item.HeldPart is not { } part)
        {
            return;
        }
        if (ctx.Me.FirstAction(ContainerRef.Belt, ctx.Slot, part) is { Refusal: null } option)
        {
            Sounds.Flat(Sounds.Item(ctx.Item.Key, option.Action.Key) ?? Sounds.Item(ctx.Item.Key, "use"));
        }
        ctx.Me.UsePart(ContainerRef.Belt, ctx.Slot, part);
    }
}
