using System;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;

namespace Hollow.Simulation.Ships;

/// <summary>
/// A tank's unopened fuel units and the fraction already drawn into its engine. Only whole, unspent units can be
/// moved through inventory. The reserve is never converted back into a withdrawable item or added twice on load.
/// </summary>
public sealed class ShipFuel
{
    private readonly ItemDef _item;
    private readonly float _capacity;

    public ShipFuel(ShipDef def)
    {
        _item = def.Fuel;
        _capacity = def.FuelCapacity;
        Tank = new Container(ContainerKind.Box, 1);
        RefreshLimit();
    }

    public Container Tank { get; }
    public float Reserve { get; private set; }
    public int Units => Tank[0].IsEmpty ? 0 : Tank[0].Count;
    public float Amount => Units + Reserve;

    /// <summary>Initial fuel and migration from the old float-only save.</summary>
    public void Load(float amount)
    {
        amount = Math.Clamp(float.IsFinite(amount) ? amount : 0, 0, _capacity);
        int whole = (int)MathF.Floor(amount);
        Restore(whole, amount - whole);
    }

    /// <summary>The saved tank and reserve are the authority; the legacy cached total is not added to them.</summary>
    public void Restore(int units, float reserve)
    {
        Reserve = float.IsFinite(reserve) ? Math.Clamp(reserve, 0, MathF.BitDecrement(1f)) : 0;
        RefreshLimit();
        Tank.Set(0, ItemStack.Of(_item, Math.Clamp(units, 0, Tank.StackLimit)));
    }

    /// <summary>Burns the current reserve first, opening whole units only as needed, with no full-tank float drift.</summary>
    public void Consume(float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0) return;
        int units = Units;
        float reserve = Reserve;
        Burn(ref units, ref reserve, amount);
        Tank.Set(0, ItemStack.Of(_item, units));
        Reserve = reserve;
        RefreshLimit();
    }

    // The motor and server tank use the same arithmetic. The snapshot carries the reserve, preserving prediction
    // exactly even when adding a tiny fraction to several thousand whole units rounds the displayed total.
    internal static void Split(float total, float reserve, out int units, out float fraction)
    {
        fraction = reserve;
        units = Math.Max(0, (int)MathF.Round(total - reserve));
        if (total != units + reserve || reserve is < 0 or >= 1 || !float.IsFinite(reserve))
        {
            units = Math.Max(0, (int)MathF.Floor(total));
            fraction = Math.Max(0, total - units);
        }
    }

    internal static void Burn(ref int units, ref float reserve, float amount)
    {
        amount = Math.Min(amount, units + reserve);
        if (amount <= reserve)
        {
            reserve -= amount;
        }
        else
        {
            float needed = amount - reserve;
            int opened = Math.Min(units, (int)MathF.Ceiling(needed));
            units -= opened;
            reserve = Math.Max(0, opened - needed);
        }
    }

    private void RefreshLimit() => Tank.SetConstraints(_item.Id, (int)Math.Floor((double)_capacity - Reserve));
}
