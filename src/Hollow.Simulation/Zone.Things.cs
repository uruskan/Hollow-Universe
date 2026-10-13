using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Physics;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

// Static things of the survival game (GROUND.md §6): deployables and their devices, dropped items, corpses; their
// timers, warmth and workbench reach.
public sealed partial class Zone
{
    /// <summary>Side of the cells deployables are indexed by, for placement checks and hit tests.</summary>
    private const float ThingCell = 16f;

    /// <summary>A workbench counts for crafting within this many metres (Rust).</summary>
    public const float WorkbenchReach = 4f;

    private readonly Dictionary<(int X, int Z), List<Deployable>> _deployableCells = new();
    private readonly List<WorldObject> _expiredThings = new();

    /// <summary>The object of a static entity, or null.</summary>
    public WorldObject? StaticOf(Entity entity) => Entities.Has<StaticLink>(entity) ? Entities.Get<StaticLink>(entity).Object : null;

    public WorldObject? StaticOf(EntityId id) => Entities.TryFind(id, out Entity e) ? StaticOf(e) : null;

    /// <summary>Adds a static object (placed, dropped, a corpse, or one restored from storage).</summary>
    public Entity AddStatic(WorldObject thing)
    {
        Entity entity = Entities.Create(thing.Id);
        Entities.Add(entity, new StaticLink { Object = thing });
        if (thing is Deployable d)
        {
            Cell(d.Position).Add(d);
            if (d.Def.Solid)
            {
                Physics.AddProp(PropLayer.Deployable, d.Id.Value, PropShape.Box(d.Centre, d.Def.HalfExtents, d.Up, d.Yaw));
            }
            RespawnPointChanged(d);
        }
        thing.Revision++;
        return entity;
    }

    public void RemoveStatic(WorldObject thing)
    {
        if (thing is Deployable d)
        {
            Cell(d.Position).Remove(d);
            Physics.RemoveProp(PropLayer.Deployable, d.Id.Value);
        }
        if (Entities.TryFind(thing.Id, out Entity entity))
        {
            Entities.Destroy(entity);
        }
        if (thing is Deployable gone)
        {
            RespawnPointChanged(gone);
        }
    }

    /// <summary>Every static object, for saving and replication.</summary>
    public IEnumerable<WorldObject> Statics()
    {
        ComponentPool<StaticLink> pool = Entities.Pool<StaticLink>();
        for (int i = 0; i < pool.Count; i++)
        {
            yield return pool.Values[i].Object;
        }
    }

    private List<Deployable> Cell(Vector3 position)
    {
        (int, int) key = ((int)MathF.Floor(position.X / ThingCell), (int)MathF.Floor(position.Z / ThingCell));
        if (!_deployableCells.TryGetValue(key, out List<Deployable>? cell))
        {
            cell = new List<Deployable>();
            _deployableCells.Add(key, cell);
        }
        return cell;
    }

    /// <summary>Deployables whose base is within <paramref name="radius"/> of a point (horizontally, by cells).</summary>
    public void DeployablesNear(Vector3 point, float radius, List<Deployable> into)
    {
        int x0 = (int)MathF.Floor((point.X - radius) / ThingCell), x1 = (int)MathF.Floor((point.X + radius) / ThingCell);
        int z0 = (int)MathF.Floor((point.Z - radius) / ThingCell), z1 = (int)MathF.Floor((point.Z + radius) / ThingCell);
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (_deployableCells.TryGetValue((x, z), out List<Deployable>? cell))
                {
                    foreach (Deployable d in cell)
                    {
                        if (Vector3.DistanceSquared(d.Position, point) <= radius * radius)
                        {
                            into.Add(d);
                        }
                    }
                }
            }
        }
    }

    private readonly List<Deployable> _near = new();

    /// <summary>The best workbench level within <see cref="WorkbenchReach"/> of a point (0: none).</summary>
    public int WorkbenchLevel(Vector3 point)
    {
        _near.Clear();
        DeployablesNear(point, WorkbenchReach + 1.5f, _near);
        int best = 0;
        foreach (Deployable d in _near)
        {
            if (d.Def.Workbench > best && Vector3.Distance(d.Centre, point) <= WorkbenchReach + d.Def.HalfExtents.X)
            {
                best = d.Def.Workbench;
            }
        }
        return best;
    }

    /// <summary>Warmth from lit fires and furnaces at a point, in °C (the best one counts).</summary>
    public float Warmth(Vector3 point)
    {
        _near.Clear();
        DeployablesNear(point, 6f, _near);
        float best = 0;
        foreach (Deployable d in _near)
        {
            if (d.Lit && d.Def.Device is { } device && device.WarmthRadius > 0)
            {
                float distance = Vector3.Distance(d.Position, point);
                if (distance < device.WarmthRadius)
                {
                    best = Math.Max(best, device.WarmthCelsius * (1 - distance / device.WarmthRadius * 0.5f));
                }
            }
        }
        return best;
    }

    // ---------------------------------------------------------------- dropped items

    /// <summary>
    /// Drops a stack in front of a character: it lands on the ground there (GROUND.md §6) and stays until picked up or
    /// <c>droppedItemSeconds</c> pass.
    /// </summary>
    public DroppedItem Drop(Entity by, ItemStack stack)
    {
        ref Character c = ref Entities.Get<Character>(by);
        Vector3 up = Frame.UpAt(c.State.Position);
        (Vector3 forward, Vector3 right) = CharacterMotor.HorizontalBasis(c.Input.Yaw, up);
        // A little scatter so several drops don't stack on one point.
        var rng = new Rng(Hash64.Of((ulong)Tick, (ulong)_localIds, 0xD0));
        Vector3 at = c.State.Position + forward * 0.7f + right * (float)rng.Range(-0.25, 0.25);
        return DropAt(at, stack, c.Input.Yaw);
    }

    /// <summary>A stack lying on the ground at a point.</summary>
    public DroppedItem DropAt(Vector3 point, ItemStack stack, float yaw = 0)
    {
        Vector3 ground = Settle(point);
        var item = new DroppedItem(NextId(), stack, ground, Frame.UpAt(ground), yaw,
            Tick + (long)(Content!.Survival.DroppedItemSeconds * CharacterMotor.TickRate));
        AddStatic(item);
        return item;
    }

    private void DropAll(Entity by, List<ItemStack> stacks)
    {
        foreach (ItemStack s in stacks)
        {
            if (!s.IsEmpty)
            {
                Drop(by, s);
            }
        }
    }

    // ---------------------------------------------------------------- deployables

    /// <summary>
    /// Damage to a deployable (melee, arrows, bullets, blasts): at 0 it breaks, its contents spill and it is reported
    /// destroyed by <paramref name="blow"/> (GROUND.md §9.4).
    /// </summary>
    public void DamageDeployable(Deployable d, float amount, in Blow blow = default)
    {
        if (amount <= 0 || d.Health <= 0 || d is WorldProp { Prop.Breakable: false })
        {
            return; // crates and recyclers can't be broken (Rust's)
        }
        if (ClaimOf(d) is { } claim)
        {
            // A claim device takes its damage through the building rules (Zone.Building.cs).
            DamageBuilding(claim, blow.Direction, amount, projectile: false, blow);
            return;
        }
        d.Health -= amount;
        d.Revision++;
        if (d.Health > 0)
        {
            return;
        }
        if (d.Storage != null)
        {
            for (int i = 0; i < d.Storage.Size; i++)
            {
                if (!d.Storage[i].IsEmpty)
                {
                    DropAt(d.Position + d.Up * 0.3f, d.Storage[i]);
                }
            }
        }
        SpillResearch(d);
        ReportDestroyed(HitTarget.Deployable, d.Id, d.Model, blow);
        RemoveStatic(d);
        if (d is WorldProp prop)
        {
            PropGone(prop); // a barrel's loot lies on the ground; the barrel comes back on its timer (G7)
        }
    }

    // Campfires and furnaces (GROUND.md §6): a lit one burns a unit of fuel every fuel.seconds, leaving its by-product
    // (charcoal) in an output slot, and cooks every input slot in parallel into the outputs; it goes out without fuel.
    private void StepDevices()
    {
        ComponentPool<StaticLink> pool = Entities.Pool<StaticLink>();
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool.Values[i].Object is Deployable { Lit: true, Def.Device: { } device, Storage: { } storage } d)
            {
                StepDevice(d, device, storage);
            }
            else if (pool.Values[i].Object is Deployable { Lit: true, Def.IsMachine: true } machine)
            {
                StepMachine(machine); // G7: a recycler or research table at work
            }
        }
    }

    private void StepDevice(Deployable d, DeviceStats device, Container storage)
    {
        GameContent content = Content!;
        if (--d.BurnTicksLeft <= 0)
        {
            int fuelSlot = -1;
            foreach (int s in device.FuelSlots)
            {
                if (content.Item(storage[s].Item)?.Fuel != null)
                {
                    fuelSlot = s;
                    break;
                }
            }
            if (fuelSlot < 0)
            {
                d.Lit = false;
                d.Revision++;
                return;
            }
            ItemDef fuel = content.Item(storage[fuelSlot].Item)!;
            ItemStack left = storage[fuelSlot];
            left.Count--;
            storage.Set(fuelSlot, left);
            d.BurnTicksLeft = fuel.Fuel!.Ticks;
            if (fuel.Fuel.Byproduct is { } byproduct)
            {
                PutOutput(storage, device, byproduct);
            }
        }
        foreach (int s in device.InputSlots)
        {
            ItemStack input = storage[s];
            ItemDef? item = content.Item(input.Item);
            if (item?.Cook is not { Into: { } into } cook || cook.In != device.Cooks)
            {
                d.CookProgress[s] = 0;
                continue;
            }
            if (++d.CookProgress[s] < cook.Ticks)
            {
                continue;
            }
            if (!PutOutput(storage, device, into))
            {
                d.CookProgress[s] = cook.Ticks; // no room: wait
                continue;
            }
            input.Count--;
            storage.Set(s, input);
            d.CookProgress[s] = 0;
        }
        // G7: food left on the fire goes on cooking where it lies: cooked meat burns if nobody takes it out (Rust).
        foreach (int s in device.OutputSlots)
        {
            ItemStack output = storage[s];
            ItemDef? item = content.Item(output.Item);
            if (item?.Cook is not { Into: { } into } cook || cook.In != device.Cooks)
            {
                d.CookProgress[s] = 0;
                continue;
            }
            if (++d.CookProgress[s] < cook.Ticks)
            {
                continue;
            }
            output.Count--;
            storage.Set(s, output);
            if (!PutOutput(storage, device, into))
            {
                output.Count++;
                storage.Set(s, output);
                d.CookProgress[s] = cook.Ticks; // no room: wait
                continue;
            }
            d.CookProgress[s] = 0;
        }
    }

    // One unit into the device's outputs: onto a stack of it, else an empty output slot.
    private bool PutOutput(Container storage, DeviceStats device, ItemDef item)
    {
        foreach (int s in device.OutputSlots)
        {
            ItemStack o = storage[s];
            if (o.Item == item.Id && o.Count < item.StackSize)
            {
                o.Count++;
                storage.Set(s, o);
                return true;
            }
        }
        foreach (int s in device.OutputSlots)
        {
            if (storage[s].IsEmpty)
            {
                storage.Set(s, ItemStack.Of(item, 1));
                return true;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------- timers

    // Dropped items and corpses despawn on world time (it pauses while the server is down); empty corpses go at once.
    private void StepDespawns()
    {
        _expiredThings.Clear();
        ComponentPool<StaticLink> pool = Entities.Pool<StaticLink>();
        for (int i = 0; i < pool.Count; i++)
        {
            switch (pool.Values[i].Object)
            {
                case DroppedItem item when Tick >= item.DespawnTick || item.Stack.IsEmpty:
                    _expiredThings.Add(item);
                    break;
                case Corpse corpse when Tick >= corpse.DespawnTick || corpse.Loot.IsEmpty:
                    _expiredThings.Add(corpse);
                    break;
            }
        }
        foreach (WorldObject thing in _expiredThings)
        {
            RemoveStatic(thing);
        }
    }
}
