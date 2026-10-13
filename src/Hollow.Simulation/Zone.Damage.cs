using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;

namespace Hollow.Simulation;

/// <summary>
/// A ray's test for what it meets first (a swing, an arrow's or a bullet's step): from where, which way, how far, how
/// thick; who swung or shot (never hit); the moment the attacker saw others at; a bullet (its ground test steps by height).
/// </summary>
internal readonly record struct RayQuery(Vector3 Origin, Vector3 Direction, float Length, float Thickness, EntityId Ignore, double ViewTick, bool Bullet)
{
    /// <summary>Puts a candidate at <paramref name="t"/> along the ray into <paramref name="best"/> when nearer (a tie keeps the first found).</summary>
    public void Consider(ref TraceHit best, float t, HitTarget target, TraceHit candidate)
    {
        if (t >= 0 && (best.Distance < 0 || t < best.Distance))
        {
            candidate.Distance = t;
            candidate.Target = target;
            candidate.Point = Origin + Direction * t;
            best = candidate;
        }
    }

    /// <summary>How far a test needs to look that may stop at the nearest found so far (the building's, the ground's).</summary>
    public float Upto(in TraceHit best) => best.Distance >= 0 ? best.Distance : Length;
}

/// <summary>An explosion's reach, for the receivers to find their things in: where, how far, and what the explosive was on.</summary>
internal readonly record struct BlastQuery(Vector3 At, float Radius, object? StuckTo);

/// <summary>
/// A thing within an explosion's reach, as its receiver found it: what it is (a trace's hit: its kind, the thing, the
/// body part, its nearest point), the object the blast's report names, one or two points the blast must see of it, its
/// own building body (which doesn't hide it), how far, whether the explosive is on it, and whether it takes the
/// explosive's own table (building pieces and doors: the one hit whole, the rest a splash share).
/// </summary>
internal struct BlastTarget
{
    public TraceHit Hit;
    public object Thing;
    public Vector3 Sight;
    public Vector3 Sight2;
    public object? Self;
    public float Distance;
    public bool Stuck;
    public bool Table;
    public bool Visible;
}

// The damage pipeline (GROUND.md §6.4.2, RustMVP's damage plan: source → delivery → receiver → resolver → consequences).
// Every delivery (a swing, an arrow, a bullet, a blast, an animal's attack) builds a DamageRequest and calls Damage, the
// one entry point; Damage hands it to the receiver registered for that kind of thing, which applies its own defences and
// then its own consequences; the shared step after it gives the attacker their hit marker. The receivers also find their
// things along a ray (Trace) and in a blast's reach (Blast), so a new kind of damageable thing (a vehicle, a monument's
// door) is one more receiver, registered in InitDamage: no delivery changes.
public sealed partial class Zone
{
    /// <summary>
    /// A kind of thing blows can land on (RustMVP's receiver capability): it finds its things along a ray and within a
    /// blast's reach, and takes a request against one of them: its defences first (armour, a grade, a hull's share), then
    /// its consequences (health, death or breaking, what spills, what is reported).
    /// </summary>
    private interface IDamageReceiver
    {
        HitTarget Kind { get; }

        /// <summary>A projectile that lands in one of its things stays there (an arrow in a body or a hull is lost); elsewhere it may be picked up again.</summary>
        bool Keeps { get; }

        /// <summary>The nearest of its things along the ray, into <paramref name="best"/> if nearer than what is there.</summary>
        void Raycast(in RayQuery ray, ref TraceHit best);

        /// <summary>Its things within an explosion's reach, with the points the blast must see of each.</summary>
        void InReach(in BlastQuery blast, List<BlastTarget> into);

        /// <summary>A blow against one of its things: its defences, then its consequences.</summary>
        DamageOutcome Receive(in DamageRequest request);
    }

    private readonly IDamageReceiver?[] _receivers = new IDamageReceiver?[(int)Enum.GetValues<HitTarget>().Max() + 1];
    private readonly List<IDamageReceiver> _receiverOrder = new();
    private readonly List<BlastTarget> _blastTargets = new();
    private int _blasts;
    private Blow _blow;

    // The one place receivers are registered, in the order a ray tests them: everything that is found along the whole
    // ray first, then the building and the ground, which look only as far as the nearest found so far. A blast takes
    // its targets in the same order.
    private void InitDamage()
    {
        Register(new CharacterReceiver(this));
        Register(new AnimalReceiver(this));
        Register(new NatureReceiver(this));
        Register(new DeployableReceiver(this));
        Register(new ShipReceiver(this));
        Register(new BuildingReceiver(this));
        Register(new GroundReceiver(this));
    }

    private void Register(IDamageReceiver receiver)
    {
        _receivers[(int)receiver.Kind] = receiver;
        _receiverOrder.Add(receiver);
    }

    /// <summary>Whether a projectile that hit this kind of thing stays in it (it can't be picked up again).</summary>
    private bool Keeps(HitTarget kind) => _receivers[(int)kind]?.Keeps == true;

    /// <summary>
    /// The first thing a ray (thickened by <paramref name="thickness"/>) meets: a character as it was at
    /// <paramref name="viewTick"/> (interpolated from the history), an animal, a tree or outcrop, a deployable, a ship, a
    /// building piece, door or claim device, or the ground; each receiver tests its own things. <paramref name="ignore"/>
    /// is the attacker.
    /// </summary>
    public TraceHit Trace(Vector3 origin, Vector3 direction, float length, float thickness, EntityId ignore, double viewTick, bool bullet = false)
    {
        var ray = new RayQuery(origin, direction, length, thickness, ignore, viewTick, bullet);
        var best = new TraceHit { Distance = -1 };
        foreach (IDamageReceiver receiver in _receiverOrder)
        {
            receiver.Raycast(ray, ref best);
        }
        return best;
    }

    /// <summary>
    /// A blow against one thing (GROUND.md §6.4.2), the one entry point every delivery uses: the receiver of the thing's
    /// kind applies its defences and then its consequences (and reports anything it destroyed, <see cref="Destroyed"/>);
    /// then the attacker gets their hit marker when the blow landed on a kind the request confirms. Returns what it came to.
    /// </summary>
    public DamageOutcome Damage(in DamageRequest request)
    {
        if (_receivers[(int)request.Hit.Target] is not { } receiver)
        {
            return default;
        }
        Blow outer = _blow;
        _blow = Blow.Of(request);
        DamageOutcome result;
        try
        {
            result = receiver.Receive(request);
        }
        finally
        {
            _blow = outer;
        }
        if (result.Landed && request.By is { } by && result.Victim != by.Id &&
            (request.Markers & (HitTargets)(1 << (int)request.Hit.Target)) != 0)
        {
            GameEvents.Add(new ZoneEvent
            {
                Kind = ZoneEventKind.HitConfirmed, Player = by.Id, Other = result.Victim, Target = request.Hit.Target, Region = request.Hit.Region,
                Amount = result.Dealt, Flag = result.Killed, Downed = result.Downed, Position = request.Hit.Point, Item = request.Weapon,
            });
        }
        return result;
    }

    // The blow being applied (Damage sets it around its receiver): what anything it breaks is reported with.
    private struct Blow
    {
        public bool Active;
        public DestroyCause Cause;
        public Vector3 Direction;
        public EntityId Attacker;
        public ushort Weapon;

        public static Blow Of(in DamageRequest r) => new()
        {
            Active = true,
            Cause = r.Delivery switch
            {
                DamageDelivery.Arrow => DestroyCause.Arrow,
                DamageDelivery.Bullet => DestroyCause.Bullet,
                DamageDelivery.Blast => DestroyCause.Explosion,
                _ => DestroyCause.Melee,
            },
            Direction = r.Direction,
            Attacker = r.By?.Id ?? r.Attacker,
            Weapon = r.Weapon,
        };
    }

    /// <summary>
    /// The one place the <see cref="ZoneEventKind.Destroyed"/> event is made (GROUND.md §6.4.2): the receivers'
    /// consequences end here when the thing broke, and so do the building's own removals (collapse, decay, demolition)
    /// and a ship's crash. The blow being applied gives the cause (unless <paramref name="own"/> names one), its
    /// direction, who and with what.
    /// </summary>
    private void Destroyed(HitTarget receiver, DestroyedKind kind, EntityId id, string key, int grade, Vector3 position, Quaternion rotation,
        DestroyCause? own = null, bool open = false)
    {
        Blow blow = _blow;
        GameEvents.Add(new ZoneEvent
        {
            Kind = ZoneEventKind.Destroyed,
            Player = blow.Active ? blow.Attacker : EntityId.None,
            Other = id,
            Target = receiver,
            Part = kind,
            Text = key,
            Grade = (byte)Math.Clamp(grade, 0, 255),
            Position = position,
            Rotation = rotation,
            Velocity = own == null && blow.Active ? blow.Direction : Vector3.Zero,
            Cause = own ?? (blow.Active ? blow.Cause : DestroyCause.Melee),
            Item = blow.Active ? blow.Weapon : (ushort)0,
            Flag = open,
        });
    }

    // ---------------------------------------------------------------- the blast

    /// <summary>
    /// An explosion (GROUND.md §6.6) at <paramref name="at"/>, facing <paramref name="normal"/>: every receiver finds its
    /// things within the radius; each is looked at before anything is damaged (a wall that breaks still shields what was
    /// behind it) and, if nothing of the building and no ground stands between (what the explosive is on always sees it),
    /// takes a blast request with the falloff for its distance: building pieces and doors the explosive's own table for
    /// their grade (and side), the one it was stuck to (or else the nearest in sight) whole and the others
    /// <see cref="BlastStats.Splash"/> of it; deployables, claim devices and hulls its damage to deployables; characters
    /// and animals its damage to flesh (characters less what they wear on the body against projectiles; a lethal hit may
    /// knock down). Claimed bases hit are reported in <see cref="RaidHits"/>, one alert a base.
    /// </summary>
    public BlastReport Blast(BlastStats blast, Vector3 at, Vector3 normal, object? stuckTo, HitTarget target, EntityId attacker, string attackerName,
        ushort item, EntityId explosive = default, bool round = false)
    {
        // (A blast set off by another's consequences gets a list of its own.)
        List<BlastTarget> targets = _blasts++ == 0 ? _blastTargets : new List<BlastTarget>();
        try
        {
            return Blast(targets, blast, at, normal, stuckTo, target, attacker, attackerName, item, explosive, round);
        }
        finally
        {
            _blasts--;
        }
    }

    private BlastReport Blast(List<BlastTarget> targets, BlastStats blast, Vector3 at, Vector3 normal, object? stuckTo, HitTarget target,
        EntityId attacker, string attackerName, ushort item, EntityId explosive, bool round)
    {
        var report = new BlastReport();
        float radius = (float)blast.Radius;
        targets.Clear();
        var reach = new BlastQuery(at, radius, stuckTo);
        foreach (IDamageReceiver receiver in _receiverOrder)
        {
            receiver.InReach(reach, targets);
        }

        // What sees the blast, all of it before anything is damaged.
        for (int i = 0; i < targets.Count; i++)
        {
            BlastTarget t = targets[i];
            t.Visible = t.Stuck || Visible(at, t.Sight, t.Self) || (t.Sight2 != t.Sight && Visible(at, t.Sight2, t.Self));
            targets[i] = t;
            if (!t.Visible)
            {
                report.Shielded.Add(t.Thing);
            }
        }

        // The one it hit, which takes the explosive's table whole: what it was stuck to, else the nearest in sight.
        int primary = targets.FindIndex(t => t.Stuck && t.Table);
        if (primary < 0)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < targets.Count; i++)
            {
                BlastTarget t = targets[i];
                if (t.Table && t.Visible && t.Distance < nearest)
                {
                    nearest = t.Distance;
                    primary = i;
                }
            }
        }

        TryFindPlayer(attacker, out _, out Player? by);
        var raids = new RaidTallies();
        for (int i = 0; i < targets.Count; i++)
        {
            BlastTarget t = targets[i];
            if (!t.Visible)
            {
                continue;
            }
            double falloff = blast.Falloff(t.Distance);
            Vector3 toward = t.Hit.Point - at;
            var request = new DamageRequest
            {
                By = by,
                Attacker = attacker,
                AttackerName = attackerName,
                Weapon = item,
                Delivery = DamageDelivery.Blast,
                Channel = DamageType.Explosive,
                Amounts = new DamageAmounts
                {
                    Flesh = (float)(blast.Flesh * falloff),
                    Structure = (float)(blast.Deployable * falloff),
                    Building = (float)(blast.Deployable * falloff),
                    Hull = (float)(blast.Deployable * falloff),
                },
                Cause = "Blown up",
                Blast = blast,
                Falloff = falloff,
                Direct = i == primary,
                Raids = raids,
                Hit = t.Hit,
                From = at,
                Direction = t.Stuck || toward.LengthSquared() < 1e-6f ? -normal : Vector3.Normalize(toward),
                Markers = HitTargets.Living,
            };
            DamageOutcome result = Damage(request);
            if (result.Landed)
            {
                report.Damage[t.Thing] = result.Dealt;
                report.Count(t.Hit.Target, result.Killed);
            }
        }

        // Claimed bases: one report each for the notification service.
        raids.Report(this, attacker, attackerName, item);
        RaidStats.Detonations++;
        GameEvents.Add(new ZoneEvent
        {
            Kind = ZoneEventKind.Detonated, Player = attacker, Other = explosive, Position = at, Velocity = normal, Item = item, Amount = radius,
            Target = target, Flag = round,
        });
        return report;
    }
}
