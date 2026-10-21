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
    public void Hands(HeldContext ctx, ref InputFrame input, in HandButtons buttons)
    {
        input.Primary = buttons.Attack;
        input.PrimaryPressed = buttons.Clicked;
    }

    // LocalMelee starts the arms on the same input tick as the simulated action; there is no frame-rate timer here.
}

/// <summary>The bow: the aim button draws, a press of the attack button looses; the string sounds as it is drawn.</summary>
public sealed class BowControl : IHeldControl
{
    private bool _drawing;
    private int _equipped = -1;

    public void Hands(HeldContext ctx, ref InputFrame input, in HandButtons buttons)
    {
        input.Primary = buttons.Attack;
        input.Secondary = buttons.Aim;
        input.PrimaryPressed = buttons.Clicked;
    }

    public HeldAmmo? Ammo(HeldContext ctx) => new HeldAmmo(0, ctx.Item.Bow!.Ammo.Sum(a => ctx.Me.Inventory.Count(a)));

    public void Arms(HeldContext ctx, double delta, in InputFrame input)
    {
        if (_equipped != ctx.Arms.Equipped)
        {
            _equipped = ctx.Arms.Equipped;
            _drawing = false;
        }
        bool drawing = input.Secondary && ctx.Arms.SinceEquip >= HeldActionTiming.Quantize(ctx.Content.Survival.EquipSeconds) &&
            (ctx.Client.Character.Flags & Hollow.Simulation.Movement.MoveFlags.Swimming) == 0 &&
            ctx.Item.Bow!.Ammo.Any(a => ctx.Me.Inventory.Count(a) > 0);
        ctx.Arms.Aim(drawing);
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
/// An explosive: the attack button throws it; the aim button puts a charge where its ghost shows it (placement.json lists
/// the charges, GROUND_SYSTEMS.md §6), or lobs a grenade gently. One every cooldown.
/// </summary>
public sealed class ThrowableControl : IHeldControl
{
    // A charge's ghost on the surface the view meets.
    public void Aim(HeldContext ctx, Num.Vector3 eye, Num.Vector3 forward)
    {
        if (ctx.Item.Places != null)
        {
            ctx.Placement.Aim(ctx, eye, forward);
        }
    }

    public void Hands(HeldContext ctx, ref InputFrame input, in HandButtons buttons)
    {
        if (!buttons.Clicked && !buttons.AimClicked)
        {
            return;
        }
        bool secondary = buttons.AimClicked && !buttons.Clicked;
        if (secondary && ctx.Item.Places != null)
        {
            ctx.Arms.BeginAction(ctx.Item, "throw", () => ctx.Placement.Place(ctx));
            return;
        }
        ItemDef item = ctx.Item;
        ctx.Arms.BeginAction(item, "throw", () =>
        {
            uint intent = ctx.Me.Throw(ctx.Slot, ctx.Yaw, ctx.Pitch, lob: secondary);
            if (intent != 0) Sounds.Flat(Sounds.Item(item.Key, "throw"));
            return intent;
        });
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
            ctx.Arms.BeginAction(ctx.Item, "use", () => ctx.Placement.Place(ctx));
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
            ctx.Arms.BeginAction(ctx.Item, "repair", () => ctx.Build.Use());
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
        ItemDef item = ctx.Item;
        if (ctx.Me.FirstAction(ContainerRef.Belt, ctx.Slot, part) is not { Refusal: null } option)
        {
            ctx.Me.UsePart(ContainerRef.Belt, ctx.Slot, part); // ordinary refusal message, with no visual action
            return;
        }
        ctx.Arms.BeginAction(item, "use", () =>
        {
            uint intent = ctx.Me.UsePart(ContainerRef.Belt, ctx.Slot, part);
            if (intent != 0) Sounds.Flat(Sounds.Item(item.Key, option.Action.Key) ?? Sounds.Item(item.Key, "use"));
            return intent;
        });
    }
}
