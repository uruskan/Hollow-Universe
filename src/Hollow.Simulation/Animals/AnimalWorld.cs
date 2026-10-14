using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Animals;

/// <summary>A player as the animals see and hear them this tick (the zone fills the list).</summary>
public struct AnimalObserver
{
    public EntityId Id;
    public Vector3 Position;
    public Vector3 Velocity;

    /// <summary>How far their footsteps carry (0 standing still).</summary>
    public float Noise;

    public bool Crouching;

    /// <summary>Asleep or down: lying, easy prey, seen only close.</summary>
    public bool Lying;
}

/// <summary>A noise the animals may hear: a shot, a blast, a blow, chopping (the zone adds them as they happen).</summary>
public struct AnimalNoise
{
    public Vector3 Position;

    /// <summary>How far it carries, in metres, before a species' hearing.</summary>
    public float Radius;

    public long Tick;

    /// <summary>Who made it (a player), or none.</summary>
    public EntityId Source;

    /// <summary>A gunshot or a blast: it frightens.</summary>
    public bool Loud;
}

/// <summary>What the zone does for the animals: hurting players, telling them about walls and boxes.</summary>
public interface IAnimalHost
{
    /// <summary>The animal's attack lands on a player (armour applies); false if they are gone or dead.</summary>
    bool AnimalAttacksPlayer(Animal animal, EntityId player);

    /// <summary>The nearest building piece, door or deployable along a low ray from a point, within <paramref name="length"/>; −1 when none.</summary>
    float ObstacleAlong(Vector3 from, Vector3 direction, float length);

    /// <summary>Whether anything built or placed stands within <paramref name="radius"/> of a point (a cheap first test).</summary>
    bool ObstaclesNear(Vector3 point, float radius);
}

/// <summary>
/// Every animal of a zone and the systems that run them (GROUND.md §6.9): the AI on a slow tick staggered across the
/// animals (7.5 Hz engaged, 5 Hz calm), movement every tick along the ground, attacks with a wind-up, corpses with a
/// timer, spawn areas from the planet's biomes, and a second of poses for hits as the attacker saw them. Animals near no
/// player sleep. Server state, on the zone's thread; deterministic given the zone's inputs. On a whole world
/// (docs/WHOLE_WORLD.md §7.3): headings from local north, distances in three dimensions, steps along the ground.
/// </summary>
public sealed partial class AnimalWorld
{
    /// <summary>
    /// The AI thinks every so many ticks, staggered by animal: engaged (fleeing, chasing, alert) 7.5 times a second,
    /// calm 5 times; movement runs every tick.
    /// </summary>
    public const int EngagedThinkTicks = 4;

    public const int CalmThinkTicks = 6;

    /// <summary>Animals farther than this from every player sleep (beyond what anyone can see).</summary>
    public const float SleepDistance = 340f;

    /// <summary>A grid of animals for neighbour queries, rebuilt every tick.</summary>
    private const float CellSize = 32f;

    private readonly Zone _zone;
    private readonly IAnimalHost _host;
    private readonly List<Animal> _animals = new();
    private readonly Dictionary<EntityId, Animal> _byId = new();
    private readonly Dictionary<(int X, int Y, int Z), List<Animal>> _cells = new();
    private readonly Stack<List<Animal>> _spareCells = new();
    private readonly List<Animal> _removed = new();
    private readonly List<Animal> _near = new();
    private readonly List<Animal> _bodies = new();
    private readonly ulong _seed;
    private long _localIds = 1;

    internal AnimalWorld(Zone zone, IAnimalHost host, GameContent content, ulong seed)
    {
        _zone = zone;
        _host = host;
        Content = content;
        _seed = seed;
        Nav = new NavGrid(zone.Surface, zone.Frame, zone.Nature, content);
    }

    public GameContent Content { get; }

    /// <summary>The ground, water, slopes, trees and rocks as the animals know them.</summary>
    public NavGrid Nav { get; }

    /// <summary>Every animal now: alive, and corpses until they go.</summary>
    public IReadOnlyList<Animal> All => _animals;

    public int Count => _animals.Count;

    /// <summary>Players as the animals perceive them this tick, and recent noises (the zone fills both).</summary>
    public List<AnimalObserver> Observers { get; } = new();

    public List<AnimalNoise> Noises { get; } = new();

    /// <summary>
    /// Points that keep the animals near them awake without being seen or heard: a spectator's camera (the animal
    /// sandbox, an admin watching). Players keep them awake as observers.
    /// </summary>
    public List<Vector3> Watchers { get; } = new();

    /// <summary>One second of every animal's poses (feet, up, heading; lying when dead), for hits as the attacker saw them.</summary>
    public HitboxHistory History { get; } = new();

    /// <summary>Animals that appeared and went this tick (the server spawns and despawns them for clients; corpses saved).</summary>
    public List<Animal> Added { get; } = new();

    public List<Animal> Gone { get; } = new();

    /// <summary>Statistics: thinks run, animals moved, ticks stepped.</summary>
    public long Thinks { get; private set; }

    public long Moves { get; private set; }

    public long Steps { get; private set; }

    public int Awake { get; private set; }

    public Animal? Find(EntityId id) => _byId.GetValueOrDefault(id);

    private long Tick => _zone.Tick;

    private Vector3 UpAt(Vector3 p) => _zone.Frame.UpAt(p);

    // ---------------------------------------------------------------- adding and removing

    /// <summary>
    /// Puts a herd of <paramref name="count"/> animals of a species on the ground around a point (a spawn slot's, or by
    /// hand when <paramref name="slot"/> is null), roaming within <paramref name="range"/> metres of it, facing
    /// <paramref name="facing"/> (the characters' yaw; a little each way per animal) or any way.
    /// </summary>
    public Herd SpawnHerd(AnimalSpecies species, Vector3 at, int count, float range = 70, SpawnSlot? slot = null, float? facing = null)
    {
        var herd = new Herd(species, at, range) { Slot = slot, WanderGoal = at };
        for (int i = 0; i < count; i++)
        {
            Animal a = AddMember(herd, at, i);
            if (facing is { } yaw)
            {
                a.Yaw = WrapAngle(yaw + (float)(new Rng(a.Seed).NextDouble() - 0.5) * 0.8f);
                a.DesiredYaw = a.Yaw;
            }
        }
        return herd;
    }

    /// <summary>One more animal in a herd, near a point (a member coming back).</summary>
    internal Animal AddMember(Herd herd, Vector3 near, int index)
    {
        // Each animal its own seed: a counter, so animals added on one tick differ (KNOWN_PROBLEMS.md).
        ulong seed = Hash64.Of(_seed, (ulong)Tick, (ulong)_localIds++, 0xA111);
        var rng = new Rng(seed);
        float spread = 1.5f + herd.Species.Cohesion * 0.25f;
        Vector3 at = near;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            double angle = rng.NextDouble() * DMath.TwoPi;
            float distance = index == 0 && attempt == 0 ? 0 : (float)rng.Range(1.5, spread);
            Vector3 candidate = Along(near, angle, distance);
            if (Nav.Walkable(candidate, herd.Species.MaxSlope))
            {
                at = candidate;
                break;
            }
        }
        Vector3 position = Nav.Ground(at);
        var animal = new Animal(NewId(), herd.Species, herd, position, (float)(rng.NextDouble() * DMath.TwoPi), seed)
        {
            Slot = herd.Members.Count,
        };
        animal.NextThink = Tick + 1 + (long)(seed % CalmThinkTicks);
        animal.Until = Tick + (long)rng.Range(2, 8) * CharacterTicks.Rate;
        animal.State = rng.Chance(0.5) ? AnimalState.Graze : AnimalState.Idle;
        animal.StateSince = Tick;
        herd.Members.Add(animal);
        Add(animal);
        return animal;
    }

    /// <summary>Adds an animal (or a corpse restored from storage).</summary>
    public void Add(Animal animal)
    {
        _animals.Add(animal);
        _byId[animal.Id] = animal;
        Added.Add(animal);
        Nav.Prefetch(animal.Position, 96);
    }

    /// <summary>Takes an animal out of the world (its area emptied, its corpse gone).</summary>
    public void Remove(Animal animal)
    {
        if (animal.Removed)
        {
            return;
        }
        animal.Removed = true;
        _removed.Add(animal);
    }

    private EntityId NewId() => _zone.NextId();

    /// <summary>A corpse as stored (GROUND.md §6.9: corpses survive a restart while their timer runs).</summary>
    public Animal RestoreCorpse(EntityId id, AnimalSpecies species, Vector3 position, float yaw, float harvestLeft, long despawnTick,
        string killedByName = "")
    {
        var herd = new Herd(species, position, 0);
        var corpse = new Animal(id, species, herd, position, yaw, Hash64.Of(_seed, id.Value))
        {
            State = AnimalState.Dead,
            StateSince = Tick,
            Health = 0,
            HarvestLeft = Math.Clamp(harvestLeft, 0.01f, species.HarvestHealth),
            DespawnTick = despawnTick,
            KilledByName = killedByName,
        };
        herd.Members.Add(corpse);
        Add(corpse);
        _zone.Things().Loose(corpse);
        return corpse;
    }

    // ---------------------------------------------------------------- the tick

    /// <summary>
    /// One tick: the population once a second (areas in and out, the dead replaced, who sleeps), the AI of the animals
    /// whose turn it is, movement for every animal awake, attacks landing, corpses going, and the hit history.
    /// </summary>
    public void Step()
    {
        Steps++;
        Added.Clear();
        Gone.Clear();
        Nav.Adopt();
        if (Tick % CharacterTicks.Rate == 0)
        {
            Populate();
            Sleepers();
        }
        BuildCells();
        int awake = 0;
        for (int i = 0; i < _animals.Count; i++)
        {
            Animal a = _animals[i];
            if (a.Removed)
            {
                continue;
            }
            if (a.IsDead)
            {
                continue; // a carcass lies loose: the zone's lifetime step ends it (GROUND_SYSTEMS.md §3.7)
            }
            if (a.Asleep)
            {
                continue;
            }
            awake++;
            if (Tick >= a.NextThink)
            {
                Think(a);
                Thinks++;
                a.NextThink = Tick + (Engaged(a) ? EngagedThinkTicks : CalmThinkTicks);
            }
            if (a.StrikeTick == Tick)
            {
                Strike(a);
            }
            Move(a);
        }
        Awake = awake;
        foreach (Animal a in _removed)
        {
            _animals.Remove(a);
            _byId.Remove(a.Id);
            a.Herd.Members.Remove(a);
            History.Forget(a.Id);
            Gone.Add(a);
        }
        _removed.Clear();
        PruneNoises();
        Record();
        Nav.Release(60 * CharacterTicks.Rate);
    }

    private static bool Engaged(Animal a) => a.State is AnimalState.Flee or AnimalState.Chase or AnimalState.Attack or AnimalState.Alert;

    // Who sleeps: nobody within sight of any player.
    private void Sleepers()
    {
        foreach (Animal a in _animals)
        {
            if (a.IsDead || a.Removed)
            {
                continue;
            }
            float nearest = float.MaxValue;
            foreach (AnimalObserver o in Observers)
            {
                nearest = MathF.Min(nearest, Vector3.DistanceSquared(o.Position, a.Position));
            }
            foreach (Vector3 w in Watchers)
            {
                nearest = MathF.Min(nearest, Vector3.DistanceSquared(w, a.Position));
            }
            bool asleep = nearest > SleepDistance * SleepDistance;
            if (asleep && !a.Asleep)
            {
                a.Speed = 0;
                a.DesiredSpeed = 0;
                a.StrikeTick = -1;
                if (a.State is AnimalState.Chase or AnimalState.Flee or AnimalState.Attack)
                {
                    SetState(a, AnimalState.Idle);
                }
            }
            if (!asleep && a.Asleep)
            {
                a.NextThink = Tick + 1 + (long)(a.Seed % CalmThinkTicks);
                Nav.Prefetch(a.Position, 96);
            }
            a.Asleep = asleep;
        }
    }

    private void BuildCells()
    {
        foreach (List<Animal> cell in _cells.Values)
        {
            cell.Clear();
            _spareCells.Push(cell);
        }
        _cells.Clear();
        foreach (Animal a in _animals)
        {
            if (a.Removed)
            {
                continue;
            }
            (int, int, int) key = CellOf(a.Position);
            if (!_cells.TryGetValue(key, out List<Animal>? cell))
            {
                cell = _spareCells.Count > 0 ? _spareCells.Pop() : new List<Animal>();
                _cells.Add(key, cell);
            }
            cell.Add(a);
        }
    }

    private static (int X, int Y, int Z) CellOf(Vector3 p) =>
        ((int)MathF.Floor(p.X / CellSize), (int)MathF.Floor(p.Y / CellSize), (int)MathF.Floor(p.Z / CellSize));

    /// <summary>The animals within <paramref name="radius"/> of a point (from this tick's grid of cubes).</summary>
    public void Near(Vector3 point, float radius, List<Animal> into)
    {
        into.Clear();
        (int x0, int y0, int z0) = CellOf(point - new Vector3(radius));
        (int x1, int y1, int z1) = CellOf(point + new Vector3(radius));
        for (int z = z0; z <= z1; z++)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (_cells.TryGetValue((x, y, z), out List<Animal>? cell))
                    {
                        foreach (Animal a in cell)
                        {
                            if (Vector3.DistanceSquared(a.Position, point) <= radius * radius)
                            {
                                into.Add(a);
                            }
                        }
                    }
                }
            }
        }
    }

    private void PruneNoises()
    {
        long oldest = Tick - 2 * CalmThinkTicks;
        Noises.RemoveAll(n => n.Tick < oldest);
    }

    private void Record()
    {
        foreach (Animal a in _animals)
        {
            if (a.Asleep && !a.IsDead)
            {
                continue;
            }
            History.Record(a.Id, Tick, new HitPose
            {
                Feet = a.Position,
                Up = UpAt(a.Position),
                Yaw = a.Yaw,
                Pose = a.IsDead ? Pose.Lying : Pose.Standing,
            });
        }
    }

    // ---------------------------------------------------------------- movement

    // Turns towards the heading the AI chose and eases to its speed, then steps along the ground: never into water, a
    // trunk, a rock or ground too steep (it slides along, or stops), its feet on the grid's ground.
    private void Move(Animal a)
    {
        const float dt = CharacterMotor.DeltaTime;
        AnimalSpecies s = a.Species;
        float turn = s.TurnRate * dt * (a.Speed > s.TrotSpeed ? 0.7f : 1f);
        float delta = WrapAngle(a.DesiredYaw - a.Yaw);
        a.Yaw = WrapAngle(a.Yaw + Math.Clamp(delta, -turn, turn));
        float accel = (a.DesiredSpeed < a.Speed ? 1.6f : 1f) * s.Acceleration * dt;
        // Running straight into a sharp turn: slow down to make it.
        float target = MathF.Abs(delta) > 1.2f ? MathF.Min(a.DesiredSpeed, s.TrotSpeed) : a.DesiredSpeed;
        a.Speed += Math.Clamp(target - a.Speed, -accel, accel);
        if (a.Speed < 0.02f)
        {
            a.Speed = 0;
            return;
        }
        if (!Nav.IsReady(a.Position))
        {
            // Its ground isn't mapped yet (a worker builds it): wait a moment.
            Nav.Prefetch(a.Position, 64);
            return;
        }
        Moves++;
        Vector3 up = UpAt(a.Position);
        (Vector3 forward, _) = Facing.Basis(a.Yaw, up);
        Vector3 step = forward * (a.Speed * dt);
        Vector3 next = a.Position + step;
        if (!Nav.Walkable(next, s.MaxSlope))
        {
            // Slide along whichever of local east and north is free, or stop.
            (Vector3 north, Vector3 east) = Facing.Compass(up);
            Vector3 alongEast = a.Position + east * Vector3.Dot(step, east), alongNorth = a.Position + north * Vector3.Dot(step, north);
            if (Nav.Walkable(alongEast, s.MaxSlope))
            {
                next = alongEast;
            }
            else if (Nav.Walkable(alongNorth, s.MaxSlope))
            {
                next = alongNorth;
            }
            else
            {
                a.Speed = 0;
                return;
            }
        }
        // Bodies don't pass through each other: pushed out of any it would stand in.
        Near(next, 2.5f, _bodies);
        foreach (Animal other in _bodies)
        {
            if (other == a || other.Removed)
            {
                continue;
            }
            Vector3 d = OnGround(next - other.Position, up);
            float apart = d.Length(), room = a.Species.Radius + other.Species.Radius + 0.15f;
            if (apart < room && apart > 1e-4f)
            {
                Vector3 pushed = next + d / apart * (room - apart);
                if (Nav.Walkable(pushed, s.MaxSlope))
                {
                    next = pushed;
                }
            }
        }
        a.Position = Nav.Ground(next);
    }

    /// <summary>A point <paramref name="distance"/> metres along the ground from another, <paramref name="angle"/> radians anticlockwise from local east.</summary>
    private Vector3 Along(Vector3 from, double angle, float distance)
    {
        (Vector3 north, Vector3 east) = Facing.Compass(UpAt(from));
        return Nav.Ground(from + (east * (float)DMath.Cos(angle) + north * (float)DMath.Sin(angle)) * distance);
    }

    // ---------------------------------------------------------------- attacks

    private void StartAttack(Animal a, AnimalTarget target)
    {
        AttackStats attack = a.Species.Attack!;
        SetState(a, AnimalState.Attack);
        a.StrikeTick = Tick + attack.WindupTicks;
        a.StrikeTarget = target;
        a.AttackReady = Tick + attack.RateTicks;
        a.AttackCount++;
        a.LastAttack = attack.Kind;
    }

    // The attack lands if its target is still within reach in front: a step back or aside in the wind-up avoids it.
    private void Strike(Animal a)
    {
        a.StrikeTick = -1;
        AttackStats? attack = a.Species.Attack;
        if (attack == null)
        {
            return;
        }
        if (TryTarget(a.StrikeTarget, out Vector3 at, out bool _) && InReach(a, at, attack.Reach + 0.45f, 1.3f))
        {
            if (a.StrikeTarget.Kind == TargetKind.Player)
            {
                _host.AnimalAttacksPlayer(a, a.StrikeTarget.Id);
            }
            else if (Find(a.StrikeTarget.Id) is { IsDead: false } prey)
            {
                _zone.Animals().DamageAnimal(prey, attack.AnimalDamage, HitRegion.Body, a.Position, attacker: null, attackerId: a.Id, attackerName: a.Species.Name,
                    weapon: 0, by: a);
            }
        }
        if (a.State == AnimalState.Attack)
        {
            SetState(a, AnimalState.Chase);
        }
    }

    /// <summary>Whether a point is within <paramref name="reach"/> of the animal's mouth and within <paramref name="cone"/> radians of its heading.</summary>
    private bool InReach(Animal a, Vector3 point, float reach, float cone)
    {
        Vector3 up = UpAt(a.Position);
        (Vector3 forward, _) = CharacterMotor.HorizontalBasis(a.Yaw, up);
        Vector3 mouth = a.Position + forward * a.Species.HeadAhead;
        Vector3 d = point - mouth;
        Vector3 flat = d - up * Vector3.Dot(d, up);
        if (flat.Length() > reach || MathF.Abs(Vector3.Dot(d, up)) > 2.2f)
        {
            return false;
        }
        Vector3 fromBody = point - a.Position;
        Vector3 flatBody = fromBody - up * Vector3.Dot(fromBody, up);
        float length = flatBody.Length();
        return length < 0.3f || Vector3.Dot(flatBody / length, forward) >= (float)DMath.Cos(cone);
    }

    // Where a target is now (a player's feet, an animal's), and whether it is lying (asleep, down, dead).
    private bool TryTarget(AnimalTarget target, out Vector3 position, out bool lying)
    {
        position = default;
        lying = false;
        switch (target.Kind)
        {
            case TargetKind.Player:
                foreach (AnimalObserver o in Observers)
                {
                    if (o.Id == target.Id)
                    {
                        position = o.Position;
                        lying = o.Lying;
                        return true;
                    }
                }
                return false;
            case TargetKind.Animal:
                if (Find(target.Id) is { Removed: false } other && !other.IsDead)
                {
                    position = other.Position;
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- damage

    /// <summary>
    /// An animal takes a hit (the zone's combat calls this): its health falls, others see it flinch, and it reacts by
    /// its temperament (a deer runs, a boar charges whoever hurt it, a wolf or bear turns on them), raising its herd's
    /// alarm. At no health it dies into a corpse. Returns the health taken.
    /// </summary>
    internal float Hurt(Animal a, float amount, Vector3 from, AnimalTarget by, string byName)
    {
        if (a.IsDead || a.Removed || amount <= 0)
        {
            return 0;
        }
        float taken = MathF.Min(amount, a.Health);
        a.Health -= amount;
        a.HurtCount++;
        a.HurtBy = by;
        a.HurtTick = Tick;
        a.Asleep = false;
        if (a.Health <= 0)
        {
            Die(a, by, byName);
            return taken;
        }
        Raise(a.Herd, from, null);
        // React at once rather than at its next think.
        a.NextThink = Math.Min(a.NextThink, Tick + 1);
        return taken;
    }

    private void Die(Animal a, AnimalTarget by, string byName)
    {
        a.Health = 0;
        SetState(a, AnimalState.Dead);
        a.Speed = 0;
        a.DesiredSpeed = 0;
        a.StrikeTick = -1;
        a.Alert = false;
        a.HarvestLeft = a.Species.HarvestHealth;
        a.DespawnTick = Tick + a.Species.Carcass.Ticks;
        _zone.Things().Loose(a);
        a.KilledBy = by.Kind == TargetKind.Player ? by.Id : EntityId.None;
        a.KilledByName = byName;
        Raise(a.Herd, a.Position, null);
        a.Herd.Slot?.MemberDied(this, a);
    }

    // A herd's alarm: the rest run (or fight) at their next think; the one who raised it has answered already.
    private void Raise(Herd herd, Vector3 threat, Animal? by)
    {
        herd.AlarmTick = Tick;
        herd.ThreatPoint = threat;
        if (by != null)
        {
            by.AlarmSeen = Tick;
        }
    }

    /// <summary>
    /// A tool's hit on a corpse: takes the tool's rate of what is left to harvest and gives the share of the whole yield
    /// that was, in whole units; the last hit gives the rest (GROUND.md §6.9, as gathering, §6.1), and the carcass, emptied,
    /// goes by its lifetime's rule (the zone ends it at once, GROUND_SYSTEMS.md §3.7).
    /// </summary>
    internal List<ItemAmount> Harvest(Animal corpse, float rate)
    {
        var gained = new List<ItemAmount>();
        if (!corpse.IsDead || corpse.Removed || rate <= 0 || corpse.HarvestLeft <= 0)
        {
            return gained;
        }
        float max = corpse.Species.HarvestHealth;
        float before = corpse.HarvestLeft, after = MathF.Max(0, before - rate);
        foreach (ItemAmount y in corpse.Species.Harvest)
        {
            int n = Gathered(y.Count, max, after) - Gathered(y.Count, max, before);
            if (n > 0)
            {
                gained.Add(new ItemAmount(y.Item, n));
            }
        }
        corpse.HarvestLeft = after;
        return gained;
    }

    // Units gathered once health has fallen to `left` of `max`: in proportion, all of it at none.
    private static int Gathered(int total, float max, float left) =>
        left <= 0 ? total : (int)MathF.Floor(total * (1 - left / max) + 1e-4f);

    // ---------------------------------------------------------------- small helpers

    private void SetState(Animal a, AnimalState state)
    {
        if (a.State == state)
        {
            return;
        }
        a.State = state;
        a.StateSince = Tick;
    }

    private Rng RngFor(Animal a) => new(Hash64.Of(a.Seed, ++a.Draws, (ulong)Tick));

    internal static float WrapAngle(float a)
    {
        const float twoPi = MathF.PI * 2;
        a %= twoPi;
        if (a > MathF.PI)
        {
            a -= twoPi;
        }
        else if (a < -MathF.PI)
        {
            a += twoPi;
        }
        return a;
    }

    /// <summary>The heading of a direction at a point with this up (from local north, anticlockwise from above: <see cref="Facing"/>).</summary>
    internal static float YawOf(Vector3 direction, Vector3 up) => Facing.YawOf(direction, up);
}
