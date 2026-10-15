using System.Collections.Generic;
using System.Numerics;
using System;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

using Hollow.Simulation.Movement;

namespace Hollow.Simulation;

// Static things of the survival game (GROUND.md §6): deployables and their devices, dropped items, corpses; warmth and
// workbench reach; and the one lifetime step (GROUND_SYSTEMS.md §3.7) for every loose thing of any family (bags, bodies,
// duds, carcasses). The zone keeps the loose things (those with a lifetime) and the working ones (burners and machines)
// in lists of their own, so neither the lifetime step nor the devices' step looks at every static thing.
public sealed class ThingSystem : ZoneSystem
{
    private BuildingSystem? _buildingSystem;
    private BuildingSystem Building => _buildingSystem ??= Zone.Require<BuildingSystem>();
    private DamageSystem? _damageSystem;
    private DamageSystem Damage => _damageSystem ??= Zone.Require<DamageSystem>();
    private PlacementSystem? _placementSystem;
    private PlacementSystem Placement => _placementSystem ??= Zone.Require<PlacementSystem>();
    private SpillSystem? _spillSystem;
    private SpillSystem Spill => _spillSystem ??= Zone.Require<SpillSystem>();
    private SupportSystem? _supportsSystem;
    private SupportSystem Supports => _supportsSystem ??= Zone.Require<SupportSystem>();
    private TargetSystem? _targetsSystem;
    private TargetSystem Targets => _targetsSystem ??= Zone.Require<TargetSystem>();
    private WorldSystem? _worldSystem;
    private WorldSystem World => _worldSystem ??= Zone.Require<WorldSystem>();

    /// <summary>Side of the cells deployables are indexed by, for placement checks and hit tests.</summary>
    private const float ThingCell = 16f;

    private readonly Dictionary<(int X, int Y, int Z), List<Deployable>> _deployableCells = new();
    internal readonly List<ILifetime> _loose = new();
    private readonly List<ILifetime> _over = new();
    private readonly List<Deployable> _workers = new();

    /// <summary>The object of a static entity, or null.</summary>
    public WorldObject? StaticOf(Entity entity) => Entities.Has<StaticLink>(entity) ? Entities.Get<StaticLink>(entity).Object : null;

    public WorldObject? StaticOf(EntityId id) => Entities.TryFind(id, out Entity e) ? StaticOf(e) : null;

    /// <summary>Adds a static object (placed, dropped, a corpse, or one restored from storage).</summary>
    public Entity AddStatic(WorldObject thing)
    {
        Entity entity = Entities.Create(thing.Id);
        Entities.Add(entity, new StaticLink { Object = thing });
        CapabilitySet capabilities = Content is { } content ? thing.CapabilitiesIn(content.Capabilities) : CapabilitySet.Empty;
        if (thing is ILifetime loose && capabilities.Has(_lifetimeCapability))
        {
            Loose(loose);
        }
        if (thing is Deployable worker && (capabilities.Has(_burnerCapability) || capabilities.Has(_machineCapability)))
        {
            _workers.Add(worker);
        }
        if (thing is Deployable d)
        {
            Cell(d.Position).Add(d);
            if (d.Def.Solid)
            {
                Physics.AddProp(PropLayer.Deployable, d.Id.Value, PropShape.Box(d.Centre, d.Def.HalfExtents, d.Up, d.Yaw));
            }
            World.RespawnPointChanged(d);
            Placement.CountOwned(d, +1);
            // Standing on a piece, it goes with it (GROUND_SYSTEMS.md §3.6; restored, its piece may come back after it).
            Supports.Hold(d.Ref, d.Support);
        }
        thing.Revision++;
        return entity;
    }

    public void RemoveStatic(WorldObject thing)
    {
        if (thing is ILifetime loose)
        {
            Unloose(loose);
        }
        if (thing is Deployable worker)
        {
            _workers.Remove(worker);
        }
        if (thing is Deployable d)
        {
            Cell(d.Position).Remove(d);
            Physics.RemoveProp(PropLayer.Deployable, d.Id.Value);
            Placement.CountOwned(d, -1);
            Supports.Release(d.Ref);
        }
        if (Entities.TryFind(thing.Id, out Entity entity))
        {
            Entities.Destroy(entity);
        }
        if (thing is Deployable gone)
        {
            World.RespawnPointChanged(gone);
        }
        Supports.SupportGone(thing.Ref);
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
        (int, int, int) key = ThingCellOf(position);
        if (!_deployableCells.TryGetValue(key, out List<Deployable>? cell))
        {
            cell = new List<Deployable>();
            _deployableCells.Add(key, cell);
        }
        return cell;
    }

    // Deployables are indexed by cubes of the world's frame (docs/WHOLE_WORLD.md §3.3).
    private static (int X, int Y, int Z) ThingCellOf(Vector3 p) =>
        ((int)MathF.Floor(p.X / ThingCell), (int)MathF.Floor(p.Y / ThingCell), (int)MathF.Floor(p.Z / ThingCell));

    /// <summary>Deployables whose base is within <paramref name="radius"/> of a point (straight-line, by cubes).</summary>
    public void DeployablesNear(Vector3 point, float radius, List<Deployable> into)
    {
        (int x0, int y0, int z0) = ThingCellOf(point - new Vector3(radius));
        (int x1, int y1, int z1) = ThingCellOf(point + new Vector3(radius));
        void Take(List<Deployable> cell, List<Deployable> found)
        {
            foreach (Deployable d in cell)
            {
                if (Vector3.DistanceSquared(d.Position, point) <= radius * radius)
                {
                    found.Add(d);
                }
            }
        }
        long volume = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
        if (volume > _deployableCells.Count)
        {
            foreach (((int X, int Y, int Z) key, List<Deployable> cell) in _deployableCells)
            {
                if (key.X >= x0 && key.X <= x1 && key.Y >= y0 && key.Y <= y1 && key.Z >= z0 && key.Z <= z1)
                {
                    Take(cell, into);
                }
            }
            return;
        }
        for (int z = z0; z <= z1; z++)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (_deployableCells.TryGetValue((x, y, z), out List<Deployable>? cell))
                    {
                        Take(cell, into);
                    }
                }
            }
        }
    }

    internal readonly List<Deployable> _near = new();

    /// <summary>
    /// The best workbench level within reach of an eye (0: none): a bench counts when its nearest surface is within
    /// survival.json's <c>reach.station</c> (GROUND_SYSTEMS.md §3.4; no sight needed, as Rust's).
    /// </summary>
    public int WorkbenchLevel(Vector3 eye)
    {
        float reach = Content.Survival.StationReach;
        _near.Clear();
        DeployablesNear(eye, reach + 3f, _near);
        int best = 0;
        foreach (Deployable d in _near)
        {
            if (d.Def.Workbench > best && Vector3.DistanceSquared(d.Nearest(eye), eye) <= reach * reach)
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

    // ---------------------------------------------------------------- deployables

    /// <summary>
    /// Damage to a deployable (tests, the admin's tools): a blow of <paramref name="amount"/> through the deployables'
    /// receiver, with no hit marker; at 0 it breaks and its contents spill, in one bag.
    /// </summary>
    public void DamageDeployable(Deployable d, float amount) => Damage.Damage(new DamageRequest
    {
        AttackerName = "",
        Delivery = DamageDelivery.Melee,
        Channel = DamageType.Melee,
        Amounts = new DamageAmounts { Structure = amount },
        Direct = true,
        Hit = new TraceHit { Target = d.Ref, Point = d.Centre },
        Markers = TargetFamilies.None,
    });

    /// <summary>
    /// The deployables' receiver (GROUND.md §6.4.2): placed things and the world's props, found by their boxes. No
    /// defences but this: crates and recyclers can't be broken (Rust's). Every blow takes its structure damage off their
    /// health (a claim device's box passes it to the building rules); at 0 one breaks: what it held spills in one bag,
    /// a research table gives back its work, a barrel goes until its respawn, and it is reported destroyed.
    /// </summary>
    internal sealed class DeployableReceiver(Zone zone) : DamageSystem.IDamageReceiver
    {
        public TargetFamily[] Families { get; } = { TargetFamily.Thing };

        public void Raycast(in RayQuery ray, ref TraceHit best)
        {
            Vector3 end = ray.Origin + ray.Direction * ray.Length;
            zone.Combat()._traceThings.Clear();
            zone.Things().DeployablesNear((ray.Origin + end) / 2, ray.Length / 2 + 2f, zone.Combat()._traceThings);
            foreach (Deployable d in zone.Combat()._traceThings)
            {
                (Vector3 right, Vector3 forward) = Geometry.Axes(d.Up, d.Yaw);
                float t = Geometry.RayBox(ray.Origin, ray.Direction, ray.Length, d.Centre, d.Def.HalfExtents + new Vector3(ray.Thickness), right, d.Up, forward);
                ray.Consider(ref best, t, d.Ref);
            }
        }

        public void InReach(in BlastQuery blast, List<BlastTarget> into)
        {
            zone.Explosives()._blastThings.Clear();
            zone.Things().DeployablesNear(blast.At, blast.Radius + 2f, zone.Explosives()._blastThings);
            foreach (Deployable d in zone.Explosives()._blastThings)
            {
                // A claim device's box is the building's (its receiver finds it).
                if (d.Health <= 0 || zone.Building().ClaimOf(d) != null)
                {
                    continue;
                }
                Vector3 point = BlastSystem.ClosestPoint(d, blast.At);
                float distance = Vector3.Distance(point, blast.At);
                bool stuck = blast.IsStuckTo(d.Ref);
                if (distance <= blast.Radius || stuck)
                {
                    into.Add(new BlastTarget
                    {
                        Hit = new TraceHit { Target = d.Ref, Point = point }, Thing = d, Sight = point, Sight2 = d.Centre,
                        Self = d, Distance = stuck ? 0 : distance, Stuck = stuck,
                    });
                }
            }
        }

        public DamageOutcome Receive(in DamageRequest r)
        {
            if (zone.Things().StaticOf(new EntityId(r.Hit.Target.Id)) is not Deployable d)
            {
                return default;
            }
            // A fire burns only what its data says burns (IW4: wood and cloth; stone and metal don't).
            if (r.Channel == DamageType.Heat && !d.Def.Burns)
            {
                return default;
            }
            float amount = r.Amounts.Structure;
            float before = d.Health;
            bool broke = zone.Things().HurtDeployable(d, amount);
            return new DamageOutcome(true, amount, Math.Max(0, before - Math.Max(0, d.Health)), broke, false, d.Id);
        }
    }

    // The deployables' consequences: true when it broke.
    internal bool HurtDeployable(Deployable d, float amount)
    {
        if (amount <= 0 || d.Health <= 0 || d is WorldProp { Prop.Breakable: false })
        {
            return false; // crates and recyclers can't be broken (Rust's)
        }
        if (Building.ClaimOf(d) is { } claim)
        {
            // A claim device takes its damage through the building rules (BuildingSystem).
            Building.DamageBuilding(claim, Vector3.Zero, amount, projectile: false);
            return false;
        }
        d.Health -= amount;
        d.Revision++;
        if (d.Health > 0)
        {
            return false;
        }
        Break(d);
        return true;
    }

    /// <summary>
    /// A deployable breaks (its health gone, or what it stood on gone: <paramref name="cause"/>): its body goes first, so
    /// what it held comes to rest where it stood (on the floor under a box, not its lid), in one bag; a research table gives
    /// back its work; it is reported destroyed.
    /// </summary>
    internal void Break(Deployable d, DestroyCause? cause = null)
    {
        if (StaticOf(d.Id) != d)
        {
            return;
        }
        RemoveStatic(d);
        Vector3 at = d.Position + d.Up * 0.3f;
        if (d.Storage != null)
        {
            Spill.Spill(at, d.Storage, d.Yaw);
        }
        World.SpillResearch(d, at);
        if (d is WorldProp prop)
        {
            World.PropGone(prop); // a barrel's loot lies on the ground; the barrel comes back on its timer (G7)
        }
        Damage.Destroyed(d.Ref, d.ModelKey, 0, d.Position, Geometry.Upright(Frame.UpAt(d.Position), d.Yaw), cause);
    }

    // Campfires and furnaces (GROUND.md §6): a lit one burns a unit of fuel every fuel.seconds, leaving its by-product
    // (charcoal) in an output slot, and cooks every input slot in parallel into the outputs; it goes out without fuel.
    // Only the things that can work (the burners and machines) are looked at, not every static thing.
    private void StepDevices()
    {
        for (int i = 0; i < _workers.Count; i++)
        {
            Deployable d = _workers[i];
            if (d is { Lit: true, Def.Device: { } device, Storage: { } storage })
            {
                StepDevice(d, device, storage);
            }
            else if (d is { Lit: true, Def.IsMachine: true })
            {
                World.StepMachine(d); // G7: a recycler or research table at work
            }
        }
    }

    private void StepDevice(Deployable d, DeviceStats device, Container storage)
    {
        GameContent content = Content;
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

    // ---------------------------------------------------------------- lifetimes (GROUND_SYSTEMS.md §3.7)

    private int _lifetimeCapability = -1, _burnerCapability = -1, _machineCapability = -1;

    private void InitThings(GameContent content)
    {
        _lifetimeCapability = content.Capabilities.Index("lifetime");
        _burnerCapability = content.Capabilities.Index("burner");
        _machineCapability = content.Capabilities.Index("machine");
    }

    private ILifetimeCapability Lifetimes => (ILifetimeCapability)Targets.Capabilities[_lifetimeCapability];

    /// <summary>How many loose things there are (things with a lifetime running), for statistics and tests.</summary>
    public int LooseCount => _loose.Count;

    /// <summary>
    /// A thing with a lifetime starts lying loose (a bag or body added, a charge gone out, an animal dead): the lifetime
    /// step watches it from now until it goes.
    /// </summary>
    internal void Loose(ILifetime thing) => _loose.Add(thing);

    /// <summary>It lies loose no longer (gone from the world, or a dud lit again).</summary>
    internal void Unloose(ILifetime thing) => _loose.Remove(thing);

    // The one lifetime step, once a second, on world time (it pauses while the server is down): every loose thing whose
    // time is up, or that was emptied when its rule ends it so, goes; its family takes it out of the world.
    private void StepLifetimes()
    {
        if (_loose.Count == 0)
        {
            return;
        }
        ILifetimeCapability lifetimes = Lifetimes;
        _over.Clear();
        foreach (ILifetime thing in _loose)
        {
            if (lifetimes.Over(thing, Tick))
            {
                _over.Add(thing);
            }
        }
        foreach (ILifetime thing in _over)
        {
            lifetimes.End(Zone, thing.Ref, thing);
            Unloose(thing);
        }
    }

    /// <summary>A loose thing's time starts again if its rule restarts on <paramref name="why"/> (something put in a bag).</summary>
    internal void Restart(ILifetime thing, LifetimeRestart why) => Lifetimes.Restart(thing, why, Tick);

    /// <summary>A loose thing just emptied (looted, harvested out) goes at once if its rule ends it so; true when it went.</summary>
    internal bool EndIfEmptied(ILifetime thing)
    {
        ILifetimeCapability lifetimes = Lifetimes;
        if (!lifetimes.Over(thing, Tick))
        {
            return false;
        }
        lifetimes.End(Zone, thing.Ref, thing);
        Unloose(thing);
        return true;
    }

    public override int InitOrder => 10;

    protected internal override void Init() => InitThings(Content);

    protected internal override void Step()
    {
        if (Tick % CharacterMotor.TickRate == 0)
        {
            StepLifetimes();
        }
        StepDevices();
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class ThingSystemAccess
{
    public static ThingSystem Things(this Zone zone) => zone.Require<ThingSystem>();
}
