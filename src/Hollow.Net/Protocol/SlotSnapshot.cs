using System;
using Hollow.Simulation.Items;

namespace Hollow.Net.Protocol;

/// <summary>Exact inventory preconditions for an atomic attachment transaction.</summary>
public static class SlotSnapshot
{
    public static SlotNet Of(in ItemStack stack) => new()
    {
        Item = stack.Item, Count = (uint)Math.Max(0, stack.Count), Condition = stack.Condition,
        Gun = stack.HasWeaponState ? new[] { new GunNet { Loaded = stack.Loaded, LoadedAmmo = stack.LoadedAmmo,
            Sight = stack.Sight, Muzzle = stack.Muzzle, Rail = stack.Rail } } : Array.Empty<GunNet>(),
    };

    public static bool TryRead(in SlotNet value, out ItemStack stack)
    {
        stack = default;
        if (value.Item > ushort.MaxValue || value.Count > int.MaxValue || value.Condition > ushort.MaxValue || value.Gun?.Length > 1)
            return false;
        stack = new ItemStack((ushort)value.Item, (int)value.Count, (ushort)value.Condition);
        if (value.Gun is { Length: 1 })
        {
            GunNet gun = value.Gun[0];
            if (gun.Loaded > ushort.MaxValue || gun.LoadedAmmo > ushort.MaxValue || gun.Sight > ushort.MaxValue ||
                gun.Muzzle > ushort.MaxValue || gun.Rail > ushort.MaxValue) return false;
            stack.Loaded = (ushort)gun.Loaded; stack.LoadedAmmo = (ushort)gun.LoadedAmmo;
            stack.Sight = (ushort)gun.Sight; stack.Muzzle = (ushort)gun.Muzzle; stack.Rail = (ushort)gun.Rail;
        }
        return true;
    }
}
