using System.Collections.Generic;
using System.Globalization;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using HollowUniverse.UI.Model;
using ItemStack = Hollow.Simulation.Items.ItemStack;

namespace HollowUniverse.Ground;

/// <summary>
/// The gun in hand's rounds (GROUND_SYSTEMS.md §7), R held: every kind of round of its calibre the player carries, with
/// how many, then UNLOAD. Choosing a round reloads with it; UNLOAD empties the magazine into the inventory (on the gun's
/// own timeline). The current option is the round a tap of R reloads with: the loaded kind while there are more, else the
/// first kind carried (the gun's own rule, <see cref="FirearmRules.ReloadAmmo"/>). With none carried, the loaded kind
/// shows refused, so a tap says why.
/// </summary>
public sealed class AmmoRadial(HeldContext ctx) : IRadialSource
{
    private readonly List<RadialOption> _options = new();
    private readonly List<ItemDef?> _rounds = new();
    private int _current = -1;

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
        if (index < 0 || index >= _rounds.Count)
        {
            return;
        }
        LocalPlayer me = ctx.Me;
        if (_rounds[index] is { } round)
        {
            me.Reload(round);
            return;
        }
        // UNLOAD: the gun in hand's goes on its timeline (LocalPlayer routes it there).
        if (ctx.Content.ItemActions[ItemActions.UnloadKey] is { } unload)
        {
            me.ItemAction(ContainerRef.Belt, me.HeldSlot, unload.Id);
        }
    }

    // The options as things are now (the radial reads them every frame while it is open).
    private void Refresh()
    {
        _options.Clear();
        _rounds.Clear();
        _current = -1;
        LocalGun gun = ctx.Me.Gun;
        if (gun.Weapon is not { } weapon)
        {
            return;
        }
        PlayerInventory inventory = ctx.Me.Inventory;
        ItemStack stack = ctx.Me.HeldSlot is >= 0 and < PlayerInventory.BeltSlots ? inventory.Belt[ctx.Me.HeldSlot] : default;
        ushort loaded = gun.LoadedAmmo?.Id ?? 0;
        ushort next = FirearmRules.ReloadAmmo(stack with { LoadedAmmo = loaded }, weapon, loaded, id => InventoryRules.Count(inventory.Carried, id));
        foreach (ItemDef round in weapon.Ammo)
        {
            int carried = inventory.Count(round);
            if (carried == 0)
            {
                continue;
            }
            if (round.Id == next)
            {
                _current = _options.Count;
            }
            _options.Add(new RadialOption(RadialIcon.Item(round.Key), round.Name.ToUpperInvariant(), round.Description,
                carried.ToString(CultureInfo.InvariantCulture) + " CARRIED"));
            _rounds.Add(round);
        }
        if (_options.Count == 0)
        {
            // None carried: the loaded kind (or the calibre's first), refused, so a tap of R says why.
            ItemDef shown = gun.LoadedAmmo ?? weapon.Ammo[0];
            _current = 0;
            _options.Add(new RadialOption(RadialIcon.Item(shown.Key), shown.Name.ToUpperInvariant(), shown.Description, "0 CARRIED",
                Reason: ctx.Content.Texts.Line(OutcomeCode.NoAmmo)));
            _rounds.Add(shown);
        }
        string? why = gun.Loaded == 0 ? ctx.Content.Texts.Line(OutcomeCode.Empty) : gun.Reloading ? ctx.Content.Texts.Line(OutcomeCode.Reloading) : null;
        string unload = ctx.Content.ItemActions[ItemActions.UnloadKey]?.Label ?? "UNLOAD";
        _options.Add(new RadialOption(RadialIcon.Glyph("unload"), unload,
            gun.Loaded > 0 && gun.LoadedAmmo is { } inside ? $"Take the {gun.Loaded} {inside.Name.ToLowerInvariant()} out of the magazine" : "Take the rounds out of the magazine",
            Reason: why));
        _rounds.Add(null);
    }
}
