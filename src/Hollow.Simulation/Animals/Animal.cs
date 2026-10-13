using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;

namespace Hollow.Simulation.Animals;

/// <summary>What an animal is doing (GROUND.md §6.9); others see it (4 bits) and animate by it.</summary>
public enum AnimalState : byte
{
    /// <summary>Standing, looking about.</summary>
    Idle,

    /// <summary>Head down, eating grass (grazers) or rooting (boars).</summary>
    Graze,

    /// <summary>Walking somewhere in its home range, with its herd.</summary>
    Wander,

    /// <summary>Noticed something: stands and faces it (a warning growl or snort from those that fight).</summary>
    Alert,

    /// <summary>Running from a threat.</summary>
    Flee,

    /// <summary>Running at a target (a hunt, a charge, a defence).</summary>
    Chase,

    /// <summary>A bite, gore or swipe on its way: the wind-up others can read.</summary>
    Attack,

    /// <summary>Feeding at a kill.</summary>
    Eat,

    /// <summary>A corpse, to harvest until it goes.</summary>
    Dead,
}

/// <summary>What an animal is after or running from.</summary>
public enum TargetKind : byte
{
    None,
    Player,
    Animal,
}

/// <summary>A player (their character's ID) or another animal (its ID).</summary>
public readonly record struct AnimalTarget(TargetKind Kind, EntityId Id)
{
    public static readonly AnimalTarget None = default;

    public bool IsNone => Kind == TargetKind.None;

    public static AnimalTarget Player(EntityId id) => new(TargetKind.Player, id);

    public static AnimalTarget Animal(EntityId id) => new(TargetKind.Animal, id);
}

/// <summary>
/// A herd (deer, boars) or pack (wolves): its members keep together around the leader, share an alarm (one runs, all
/// run) and a hunt (a pack's prey). It lives in a spawn slot, which brings back its dead.
/// </summary>
public sealed class Herd
{
    private static int _next;

    public Herd(AnimalSpecies species, Vector3 home, float range)
    {
        Id = System.Threading.Interlocked.Increment(ref _next);
        Species = species;
        Home = home;
        Range = range;
    }

    public int Id { get; }

    public AnimalSpecies Species { get; }

    public List<Animal> Members { get; } = new();

    /// <summary>Where it lives and how far from there it roams (metres).</summary>
    public Vector3 Home { get; set; }

    public float Range { get; set; }

    /// <summary>The spawn slot that keeps its numbers up (null for animals placed by hand).</summary>
    public SpawnSlot? Slot { get; set; }

    /// <summary>The first living member leads.</summary>
    public Animal? Leader
    {
        get
        {
            foreach (Animal a in Members)
            {
                if (!a.IsDead && !a.Removed)
                {
                    return a;
                }
            }
            return null;
        }
    }

    public int Alive
    {
        get
        {
            int n = 0;
            foreach (Animal a in Members)
            {
                n += !a.IsDead && !a.Removed ? 1 : 0;
            }
            return n;
        }
    }

    /// <summary>An alarm: a member saw or heard a threat at <see cref="ThreatPoint"/> on this tick; the rest run too.</summary>
    public long AlarmTick { get; set; } = Animal.Never;

    public Vector3 ThreatPoint { get; set; }

    /// <summary>A pack's prey (wolves hunt together), and when it was last seen.</summary>
    public AnimalTarget Prey { get; set; }

    public long PreySeenTick { get; set; }

    /// <summary>Where the leader is heading while the herd wanders, and until when.</summary>
    public Vector3 WanderGoal { get; set; }

    public long WanderUntil { get; set; }

    /// <summary>The middle of its living members.</summary>
    public Vector3 Centre()
    {
        Vector3 sum = Vector3.Zero;
        int n = 0;
        foreach (Animal a in Members)
        {
            if (!a.IsDead && !a.Removed)
            {
                sum += a.Position;
                n++;
            }
        }
        return n > 0 ? sum / n : Home;
    }
}

/// <summary>
/// One animal (GROUND.md §6.9): where it is and which way it faces, how fast it goes, its health, what it is doing and
/// why, its herd, its attack in progress, and after death its corpse. Server state: animals are never stored, except a
/// corpse while its timer runs.
/// </summary>
public sealed class Animal
{
    public Animal(EntityId id, AnimalSpecies species, Herd herd, Vector3 position, float yaw, ulong seed)
    {
        Id = id;
        Species = species;
        Herd = herd;
        Position = position;
        Yaw = yaw;
        DesiredYaw = yaw;
        Health = species.Health;
        Seed = seed;
        Hunger = species.HuntHunger * 0.5f;
    }

    public EntityId Id { get; }

    public AnimalSpecies Species { get; }

    public Herd Herd { get; internal set; }

    /// <summary>Its feet on the ground, in the zone frame, and its heading (the characters' yaw: 0 faces −Z, anticlockwise from above).</summary>
    public Vector3 Position { get; internal set; }

    public float Yaw { get; internal set; }

    /// <summary>Speed along its heading now (m/s), and what it steers for.</summary>
    public float Speed { get; internal set; }

    public float DesiredSpeed { get; internal set; }

    public float DesiredYaw { get; internal set; }

    public float Health { get; internal set; }

    public float HealthFraction => Math.Clamp(Health / Species.Health, 0, 1);

    public AnimalState State { get; internal set; }

    /// <summary>The tick it entered its state.</summary>
    public long StateSince { get; internal set; }

    /// <summary>What it is after, or running from, and where that was last noticed.</summary>
    public AnimalTarget Target { get; internal set; }

    public Vector3 TargetPoint { get; internal set; }

    /// <summary>The tick the target was last seen or heard.</summary>
    public long TargetSeen { get; internal set; }

    /// <summary>Aware of a threat (ears up, head up): others see it.</summary>
    public bool Alert { get; internal set; }

    /// <summary>0 (fed) … 1 (starving): hunters hunt when hungry.</summary>
    public float Hunger { get; internal set; }

    /// <summary>When it thinks next (the AI runs on a slow, staggered tick) and thought last, and when its chase began and where.</summary>
    internal long NextThink;

    internal long LastThink;

    internal long ChaseStart;

    internal Vector3 ChaseOrigin;

    /// <summary>Until when it keeps doing what it does (an idle spell, a graze, a flight's least length).</summary>
    internal long Until;

    /// <summary>The herd alarm it last answered (one alarm, one reaction).</summary>
    internal long AlarmSeen = Never;

    /// <summary>A provocation: who hurt it last, and when.</summary>
    internal AnimalTarget HurtBy;

    internal long HurtTick = Never;

    /// <summary>A tick long before any zone's first: "never" that still subtracts without overflowing.</summary>
    internal const long Never = -1L << 40;

    /// <summary>A hunter that gave up a hunt waits until this tick before it hunts again.</summary>
    internal long HuntAfter;

    /// <summary>Whom it is warning off (they came too close), and since when: the charge comes after the warning.</summary>
    internal AnimalTarget Warned;

    internal long WarnStart = Never;

    // The attack under way: the tick it lands and on whom; the next may start at AttackReady.
    internal long StrikeTick = -1;

    internal AnimalTarget StrikeTarget;

    internal long AttackReady;

    /// <summary>Attacks begun and hits taken, counted (others see the counts and animate each), and the last attack's kind.</summary>
    public byte AttackCount { get; internal set; }

    public byte HurtCount { get; internal set; }

    public AttackKind LastAttack { get; internal set; }

    // Steering: a path's next point on the navigation grid and how fresh it is; progress, to notice being stuck.
    internal Vector3 Waypoint;

    internal bool HasWaypoint;

    internal long WaypointTick;

    internal Vector3 LastThinkPosition;

    internal float Stuck;

    /// <summary>Where it stands around its pack's prey (wolves take turns: some attack, the rest circle).</summary>
    internal int Slot;

    /// <summary>Asleep: no player near enough to see it, so it neither thinks nor moves.</summary>
    public bool Asleep { get; internal set; }

    /// <summary>Taken out of the world (its area emptied, its corpse gone): references to it are stale.</summary>
    public bool Removed { get; internal set; }

    public bool IsDead => State == AnimalState.Dead;

    /// <summary>A corpse: the harvest left (of <see cref="AnimalSpecies.HarvestHealth"/>), when it goes (world ticks) and who killed it.</summary>
    public float HarvestLeft { get; internal set; }

    public long DespawnTick { get; internal set; }

    public EntityId KilledBy { get; internal set; }

    public string KilledByName { get; internal set; } = "";

    /// <summary>Its own random stream's seed.</summary>
    public ulong Seed { get; }

    internal ulong Draws;

    /// <summary>A fraction of the corpse left to harvest (1 whole), for drawing it.</summary>
    public float HarvestFraction => Species.HarvestHealth > 0 ? Math.Clamp(HarvestLeft / Species.HarvestHealth, 0, 1) : 0;

    public override string ToString() => $"{Species.Key} {Id} {State} at ({Position.X:0.0}, {Position.Z:0.0})";
}
