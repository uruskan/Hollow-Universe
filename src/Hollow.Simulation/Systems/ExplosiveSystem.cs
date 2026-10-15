using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

// Raiding (GROUND.md §6.6, G5): explosives thrown, placed and lit again; their flight, bounces and sticking; fuses and
// duds; and the blast of a charge, a rocket or an explosive round: damage with falloff to building pieces (by grade and
// side, through BuildingWorld.Damage), doors, deployables and characters, each only if nothing of the building or the
// ground stands in between; the piece hit (or the nearest in sight) takes the table's damage, others in sight a share.
// Claimed bases hurt this way are reported (RaidHits) for the notification service. Server only: clients get the
// explosives' states and the detonations.
public sealed class ExplosiveSystem : ZoneSystem
{
    private ActionSystem? _actionsSystem;
    private ActionSystem Actions => _actionsSystem ??= Zone.Require<ActionSystem>();
    private DamageSystem? _damageSystem;
    private DamageSystem Damage => _damageSystem ??= Zone.Require<DamageSystem>();
    private HeldSystem? _heldSystem;
    private HeldSystem Held => _heldSystem ??= Zone.Require<HeldSystem>();
    private PlacementSystem? _placementSystem;
    private PlacementSystem Placement => _placementSystem ??= Zone.Require<PlacementSystem>();
    private PlayerSystem? _playersSystem;
    private PlayerSystem Players => _playersSystem ??= Zone.Require<PlayerSystem>();
    private SupportSystem? _supportsSystem;
    private SupportSystem Supports => _supportsSystem ??= Zone.Require<SupportSystem>();
    private ThingSystem? _thingsSystem;
    private ThingSystem Things => _thingsSystem ??= Zone.Require<ThingSystem>();

    /// <summary>At most this many explosives live in a zone at once (RustMVP allowed 1,000 charges); more are refused.</summary>
    public const int MaxExplosives = 1024;

    /// <summary>A thrown explosive stops bouncing after this many bounces and lies where the last left it.</summary>
    public const int MaxBounces = 12;

    /// <summary>An explosive in the air longer than this lands on the ground below it (it fell off the world's edge).</summary>
    public const int MaxAirborneTicks = 30 * CharacterMotor.TickRate;

    /// <summary>A stuck charge sits this far out from the surface (its blast starts there).</summary>
    public const float StuckOffset = 0.03f;

    private const int FlightSteps = 2;
    private const float FlightStepSeconds = CharacterMotor.DeltaTime / FlightSteps;

    private readonly List<LiveExplosive> _explosives = new();
    private readonly Dictionary<EntityId, LiveExplosive> _explosiveIds = new();
    internal readonly Dictionary<object, double> _blastResidue = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<EntityId, long> _nextThrowTick = new();
    internal readonly List<Piece> _blastPieces = new();
    internal readonly List<Deployable> _blastThings = new();
    private readonly List<NaturalObject> _blastNature = new();

    /// <summary>Explosives in the world (flying, stuck, resting, duds).</summary>
    public IReadOnlyList<LiveExplosive> All => _explosives;

    /// <summary>Claimed bases hurt by raids this tick: the server hands them to the notification service and clears the list.</summary>
    public List<RaidHit> RaidHits { get; } = new();

    /// <summary>What raiding has done so far (the load test reports it).</summary>
    public RaidCounters RaidStats { get; } = new();

    /// <summary>An explosive by its ID (the explosives' family's lookup), or null.</summary>
    public LiveExplosive? ExplosiveOf(EntityId id) => _explosiveIds.TryGetValue(id, out LiveExplosive? e) && !e.Gone ? e : null;

    // ---------------------------------------------------------------- the player's actions

    /// <summary>
    /// Throws the explosive in a belt slot along the view (a throw, or a gentle lob): it flies under the planet's gravity
    /// from in front of the eye, its fuse already burning; a charge sticks to the first surface it touches, a grenade
    /// bounces and comes to rest. The view is the intent's when it is within reach of the player's own (anti-cheat).
    /// </summary>
    public Outcome ThrowExplosive(Entity entity, int slot, ushort expected, float yaw, float pitch, bool lob)
    {
        if (!Actions.Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        Outcome check = CanThrow(p, slot, expected, out ThrowableStats? stats);
        if (check != OutcomeCode.Done)
        {
            return check;
        }
        ref Character c = ref Entities.Get<Character>(entity);
        // The view's heading and pitch are the thrower's frame's (aboard a ship, its deck's: IRON_WAKE.md §4.2); the throw
        // then flies in the zone's frame, with the ship's velocity added (§4.9).
        Vector3 up = c.State.Frame == 0 ? Frame.UpAt(c.State.Position) : Vector3.UnitY;
        (yaw, pitch) = TrustedView(c, up, yaw, pitch, Zone.Frames.ViewIn(c.Input.View, c.Input.Frame, c.State.Frame, Tick));
        Vector3 direction = Zone.Frames.DirectionToWorld(CharacterMotor.ViewDirection(yaw, pitch, up), c.State.Frame, Tick);
        Vector3 eye = EyeOf(c.State);
        // From a hand's length in front of the eye, unless something is right there.
        Vector3 start = eye + direction * 0.3f;
        if (Placement.World.Sweep(eye, start, 0, out _))
        {
            start = eye;
        }
        float speed = (float)(lob ? stats!.LobSpeed : stats!.ThrowSpeed);
        TakeOne(p, slot, stats);
        Vector3 walked = Zone.Frames.DirectionToWorld(c.State.Velocity, c.State.Frame, Tick);
        Vector3 carried = Zone.VelocityOf(c.State) - walked;
        LiveExplosive e = Spawn(stats, p.Id, p.Name, start, direction * speed + walked * 0.5f + carried);
        Arm(e);
        RaidStats.Thrown++;
        p.ActionCount++;
        p.LastAction = ActionKind.Throw;
        return OutcomeCode.Done;
    }

    /// <summary>
    /// A charge put on a surface by hand (the placement pipeline's charge kind, GROUND_SYSTEMS.md §6): stuck where the
    /// checked candidate says, held by what it touched, its fuse burning. No building rights are needed (raiding:
    /// RustMVP's rule). One throw or placement a cooldown, at most <see cref="MaxExplosives"/> in the zone.
    /// </summary>
    public Outcome PutCharge(ulong player, ThrowableStats stats, PlacementCandidate c)
    {
        if (!Players.TryFindPlayer(new EntityId(player), out _, out Player p))
        {
            return OutcomeCode.Dead;
        }
        if (Tick < _nextThrowTick.GetValueOrDefault(p.Id))
        {
            return OutcomeCode.Cooldown;
        }
        if (_explosives.Count >= MaxExplosives)
        {
            return OutcomeCode.NotAllowed;
        }
        _nextThrowTick[p.Id] = Tick + CharacterTicks.From(stats.CooldownSeconds);
        Vector3 surface = c.Detail is Vector3 point ? point : c.Position - c.Normal * StuckOffset;
        LiveExplosive e = Spawn(stats, p.Id, p.Name, surface, Vector3.Zero);
        Stick(e, new PlacementHit(0, surface, c.Normal, c.Support, c.Surface));
        Arm(e);
        RaidStats.Placed++;
        p.ActionCount++;
        p.LastAction = ActionKind.Throw;
        return OutcomeCode.Done;
    }

    /// <summary>Lights a dud again (the dud capability's verb, GROUND_SYSTEMS.md §5): a new fuse, which may go out again.</summary>
    internal Outcome Relight(LiveExplosive e)
    {
        if (!e.Dud)
        {
            return OutcomeCode.NotUsable;
        }
        Arm(e);
        RaidStats.Rearmed++;
        return OutcomeCode.Done;
    }

    // A throw or a placed charge: from the belt, checked by the held behaviour that sends it (GROUND_SYSTEMS.md §7).
    private Outcome CanThrow(Player p, int slot, ushort expected, out ThrowableStats? stats)
    {
        stats = null;
        Outcome holding = Held.ItemFor(p, ContainerRef.Belt, slot, expected, HeldUse.Throwable, out ItemDef item);
        if (!holding.Ok)
        {
            return holding;
        }
        ThrowableStats t = item.Throwable!;
        if (Tick < _nextThrowTick.GetValueOrDefault(p.Id))
        {
            return OutcomeCode.Cooldown;
        }
        if (_explosives.Count >= MaxExplosives)
        {
            return OutcomeCode.NotAllowed;
        }
        stats = t;
        return OutcomeCode.Done;
    }

    private void TakeOne(Player p, int slot, ThrowableStats stats)
    {
        ItemStack s = p.Inventory.Belt[slot];
        s.Count--;
        p.Inventory.Belt.Set(slot, s.Count > 0 ? s : default);
        _nextThrowTick[p.Id] = Tick + CharacterTicks.From(stats.CooldownSeconds);
    }

    // The view a throw goes along: the intent's (what the player saw when they clicked), unless it is far from the view
    // the server has for them (then theirs): a client can't throw behind its back.
    private static (float Yaw, float Pitch) TrustedView(in Character c, Vector3 up, float yaw, float pitch, Vector3 own)
    {
        float ownYaw = Facing.YawOf(own, up), ownPitch = Facing.PitchOf(own, up);
        if (!float.IsFinite(yaw) || !float.IsFinite(pitch) || MathF.Abs(pitch) > FirearmRules.MaxViewPitch)
        {
            return (ownYaw, ownPitch);
        }
        float dy = yaw - ownYaw;
        dy -= MathF.Round(dy / (2 * MathF.PI)) * 2 * MathF.PI;
        return MathF.Abs(dy) <= 0.6f && MathF.Abs(pitch - ownPitch) <= 0.6f ? (yaw, pitch) : (ownYaw, ownPitch);
    }

    // ---------------------------------------------------------------- explosives in the world

    /// <summary>
    /// A new explosive at a point with a velocity (flying), owned by someone: the server's own (admin, tests). Lit at once
    /// unless <paramref name="lit"/> is false; <paramref name="dud"/> forces whether this fuse goes out.
    /// </summary>
    public LiveExplosive SpawnExplosive(ThrowableStats stats, Vector3 at, Vector3 velocity, EntityId owner, string ownerName, bool lit = true,
        bool? dud = null)
    {
        LiveExplosive e = Spawn(stats, owner, ownerName, at, velocity);
        if (lit)
        {
            Arm(e, dud);
        }
        return e;
    }

    /// <summary>
    /// A new charge stuck where a ray from <paramref name="from"/> along <paramref name="direction"/> meets a surface
    /// within <paramref name="reach"/> (the server's own: admin, tests); null when it meets nothing.
    /// </summary>
    public LiveExplosive? StickExplosive(ThrowableStats stats, Vector3 from, Vector3 direction, float reach, EntityId owner, string ownerName,
        bool? dud = null)
    {
        if (!Placement.World.Sweep(from, from + Vector3.Normalize(direction) * reach, 0, out PlacementHit hit))
        {
            return null;
        }
        LiveExplosive e = Spawn(stats, owner, ownerName, hit.Point, Vector3.Zero);
        Stick(e, hit);
        Arm(e, dud);
        return e;
    }

    private LiveExplosive Spawn(ThrowableStats stats, EntityId owner, string ownerName, Vector3 at, Vector3 velocity)
    {
        var e = new LiveExplosive(NextId(), stats, owner, ownerName, Content.Survival.DudLifetime)
        {
            State = ExplosiveState.Flying,
            Position = at,
            Velocity = velocity,
            ArcStart = at,
            ArcVelocity = velocity,
            StateTick = Tick,
            Normal = Frame.UpAt(at),
        };
        _explosives.Add(e);
        _explosiveIds[e.Id] = e;
        return e;
    }

    // Lights the fuse: its length and whether it goes out come from a seed of the explosive and how often it was lit.
    private void Arm(LiveExplosive e, bool? dud = null)
    {
        e.Arms++;
        var rng = new Rng(Hash64.Of(e.Id.Value, (ulong)e.Arms, 0xB1A57));
        int fuse = e.Stats.FuseTicks(rng.NextDouble());
        bool willDud = rng.NextDouble() < e.Stats.DudChance;
        e.WillDud = dud ?? willDud;
        e.FuseTicks = fuse;
        e.FuseEndTick = Tick + fuse;
        if (e.Dud)
        {
            Things.Unloose(e); // a dud lit again lies loose no longer
        }
        e.Dud = false;
        e.DespawnTick = -1;
        e.Revision++;
    }

    // Every tick: explosives fly, follow or lose what they are on, and their fuses burn down.
    private void StepExplosives()
    {
        if (_explosives.Count == 0)
        {
            return;
        }
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < _explosives.Count; i++)
        {
            LiveExplosive e = _explosives[i];
            if (e.Gone)
            {
                continue;
            }
            if (e.State == ExplosiveState.Flying)
            {
                Fly(e);
            }
            else
            {
                Follow(e);
            }
            if (e.Burning && Tick >= e.FuseEndTick)
            {
                if (e.WillDud)
                {
                    // It lies there for its lifetime (the zone's one lifetime step ends it) unless someone lights it again.
                    e.Dud = true;
                    e.FuseEndTick = -1;
                    e.DespawnTick = Tick + e.Lifetime.Ticks;
                    e.Revision++;
                    RaidStats.Duds++;
                    Things.Loose(e);
                }
                else
                {
                    Explode(e);
                }
            }
        }
        for (int i = _explosives.Count - 1; i >= 0; i--)
        {
            if (_explosives[i].Gone)
            {
                Supports.Release(_explosives[i].Ref);
            }
        }
        for (int i = _explosives.Count - 1; i >= 0; i--)
        {
            if (_explosives[i].Gone)
            {
                if (_explosives[i].Dud)
                {
                    Things.Unloose(_explosives[i]);
                }
                _explosiveIds.Remove(_explosives[i].Id);
                _explosives.RemoveAt(i);
            }
        }
        if (Tick % CharacterMotor.TickRate == 0 && _blastResidue.Count > 0)
        {
            foreach (object part in _blastResidue.Keys.Where(Broken).ToList())
            {
                _blastResidue.Remove(part);
            }
        }
        RaidStats.Time += Stopwatch.GetElapsedTime(started);
    }

    private static bool Broken(object part) => part is not IBuildingPart { Gone: false };

    // Two steps a tick under the planet's gravity; a charge sticks where it touches, a grenade bounces off with its
    // restitution and some friction, and rests once slow on ground that is flat enough.
    private void Fly(LiveExplosive e)
    {
        ThrowableStats t = e.Stats;
        float gravity = (float)(Spec.SurfaceGravity * 9.80665);
        float radius = (float)t.Radius;
        Vector3 p = e.Position, v = e.Velocity;
        for (int s = 0; s < FlightSteps; s++)
        {
            Vector3 g = -Frame.UpAt(p) * gravity;
            float h = FlightStepSeconds;
            Vector3 next = p + v * h + g * (0.5f * h * h);
            if (Placement.World.Sweep(p, next, radius, out PlacementHit hit))
            {
                if (t.Sticky)
                {
                    Stick(e, hit);
                    return;
                }
                float fraction = Math.Clamp(hit.Distance / MathF.Max(1e-6f, Vector3.Distance(p, next)), 0, 1);
                // A centimetre clear of the surface, so the next step doesn't meet it again on the way off.
                Vector3 at = hit.Point + hit.Normal * (radius + 0.01f);
                Vector3 vh = v + g * (h * fraction);
                float vn = Vector3.Dot(vh, hit.Normal);
                Vector3 bounced = vn < 0 ? vh - hit.Normal * ((1 + (float)t.Bounce) * vn) : vh;
                float along = Vector3.Dot(bounced, hit.Normal);
                bounced = hit.Normal * along + (bounced - hit.Normal * along) * 0.7f;
                e.Bounces++;
                float speed = bounced.Length();
                if (speed < 0.3f || e.Bounces >= MaxBounces || (speed < 1.2f && Vector3.Dot(hit.Normal, Frame.UpAt(at)) > 0.6f))
                {
                    Rest(e, hit, at);
                    return;
                }
                e.Position = at;
                e.Velocity = bounced;
                e.ArcStart = at;
                e.ArcVelocity = bounced;
                e.StateTick = Tick;
                e.Revision++;
                return;
            }
            p = next;
            v += g * h;
        }
        e.Position = p;
        e.Velocity = v;
        // Off the world's edge or under the ground (it slipped past it): it lands on the ground below.
        if (++e.Airborne > MaxAirborneTicks || HeightAboveGround(p) < -0.5f)
        {
            Vector3 ground = Settle(p);
            Rest(e, new PlacementHit(0, ground, GroundNormal(ground).Normal, TargetRef.Ground, PlacementSurfaces.Terrain), ground + Frame.UpAt(ground) * radius);
        }
    }

    private void Stick(LiveExplosive e, in PlacementHit hit)
    {
        e.State = ExplosiveState.Stuck;
        Lay(e, hit, hit.Point + hit.Normal * StuckOffset);
    }

    private void Rest(LiveExplosive e, in PlacementHit hit, Vector3 at)
    {
        e.State = ExplosiveState.Resting;
        Lay(e, hit, at);
    }

    // Comes to lie or stick on what it touched (GROUND_SYSTEMS.md §3.6): linked to it where it is, its up the surface's
    // normal, so it moves with a door's leaf and goes with its support.
    private void Lay(LiveExplosive e, in PlacementHit hit, Vector3 at)
    {
        e.Position = at;
        e.Velocity = Vector3.Zero;
        e.ArcStart = at;
        e.ArcVelocity = Vector3.Zero;
        e.Normal = hit.Normal;
        e.StateTick = Tick;
        e.Airborne = 0;
        e.Link = Supports.LinkTo(hit.Target, new TargetPose(at, Geometry.Upright(hit.Normal, 0)));
        Supports.Hold(e.Ref, e.Link.Support);
        e.Revision++;
    }

    // A stuck or resting explosive follows what holds it when that moves (a door's leaf); when it goes, the support rule
    // (SupportSystem) drops it.
    private void Follow(LiveExplosive e)
    {
        if (e.Link.IsNone || !Supports.Moves(e.Link.Support) || !Supports.TryLinkPose(e.Link, out TargetPose pose))
        {
            return;
        }
        if (Vector3.DistanceSquared(pose.Position, e.Position) > 1e-6f)
        {
            e.Position = pose.Position;
            e.ArcStart = pose.Position;
            e.Normal = Vector3.Transform(Vector3.UnitY, pose.Rotation);
            e.StateTick = Tick;
            e.Revision++;
        }
    }

    /// <summary>An explosive whose support went (the explosive capability's consequence): it falls from where it is.</summary>
    internal void DropExplosive(LiveExplosive e)
    {
        if (e.Gone || e.State == ExplosiveState.Flying)
        {
            return;
        }
        Fall(e);
    }

    private void Fall(LiveExplosive e)
    {
        e.State = ExplosiveState.Flying;
        e.Bounces = 0;
        e.Velocity = Vector3.Zero;
        e.ArcStart = e.Position;
        e.ArcVelocity = Vector3.Zero;
        e.StateTick = Tick;
        Supports.Release(e.Ref);
        e.Link = SupportLink.None;
        e.Revision++;
    }

    // A fuse burnt down: the blast, from where it is; what it is stuck to takes the table's damage whole.
    private void Explode(LiveExplosive e)
    {
        e.Gone = true;
        TargetRef on = e.Link.Support;
        Supports.Release(e.Ref);
        Damage.Blast(e.Stats.Blast, e.Position, e.Normal, e.State == ExplosiveState.Stuck ? on : TargetRef.None, on.Family, e.Owner, e.OwnerName, e.Item.Id, e.Id);
    }

    // A rocket or an explosive round goes off a little short of what it hit (so its blast starts outside a wall).
    internal void BulletBlast(Bullet b, BlastStats blast, in TraceHit hit, Player? shooter)
    {
        Vector3 direction = b.Velocity.LengthSquared() > 1e-6f ? Vector3.Normalize(b.Velocity) : -Frame.UpAt(b.Position);
        Vector3 at = hit.Point - direction * 0.05f;
        // A round that met a ship where the shooter saw it bursts where that spot of the ship is now (IRON_WAKE.md §4.9).
        if (hit.Target.Family == TargetFamily.Ship && Zone.Frames.Ship(hit.Target.Id) is { } frame &&
            Zone.SeenPose(frame.Key, Tick - b.RewindTicks, out ShipPose seen) && frame.TryPose(Tick, out ShipPose now))
        {
            at = now.ToWorld(seen.ToLocal(at));
            direction = now.DirectionToWorld(seen.DirectionToLocal(direction));
        }
        Damage.Blast(blast, at, -direction, hit.Target, hit.Target.Family, b.Shooter, shooter?.Name ?? b.ShooterName, b.Ammo.Id, round: b.Damage > 0);
    }

    // ---------------------------------------------------------------- saving and loading

    /// <summary>An explosive's stored form (GROUND.md §6.6): its fuse as ticks left, which pause while the server is down.</summary>
    public ExplosiveSave SaveOf(LiveExplosive e) => new()
    {
        Id = e.Id,
        Item = e.Item.Id,
        Owner = e.Owner,
        OwnerName = e.OwnerName,
        State = e.State,
        Dud = e.Dud,
        Position = e.Position,
        Velocity = e.Velocity,
        Normal = e.Normal,
        FuseLeft = e.Burning ? Math.Max(0, e.FuseEndTick - Tick) : -1,
        FuseTicks = e.FuseTicks,
        WillDud = e.WillDud,
        Arms = e.Arms,
        Support = e.Link.Support,
        DespawnLeft = e.DespawnTick >= 0 ? Math.Max(0, e.DespawnTick - Tick) : -1,
    };

    /// <summary>
    /// Puts back a stored explosive (after the building and the deployables are loaded): its fuse goes on from where it
    /// was, linked again to what held it where it lies; if that is gone, it falls. False when it can't come back (an item
    /// the game no longer has, one already here).
    /// </summary>
    public bool RestoreExplosive(ExplosiveSave s)
    {
        if (Content.Item(s.Item)?.Throwable is not { } stats || ExplosiveOf(s.Id) != null || _explosives.Count >= MaxExplosives ||
            !float.IsFinite(s.Position.X) || !float.IsFinite(s.Position.Y) || !float.IsFinite(s.Position.Z))
        {
            return false;
        }
        LifetimeRule dudLifetime = Content.Survival.DudLifetime;
        var e = new LiveExplosive(s.Id, stats, s.Owner, s.OwnerName, dudLifetime)
        {
            State = s.State,
            Dud = s.Dud,
            Position = s.Position,
            Velocity = Finite(s.Velocity),
            ArcStart = s.Position,
            ArcVelocity = s.State == ExplosiveState.Flying ? Finite(s.Velocity) : Vector3.Zero,
            StateTick = Tick,
            Normal = s.Normal.LengthSquared() > 0.5f ? Vector3.Normalize(s.Normal) : Frame.UpAt(s.Position),
            FuseEndTick = s.FuseLeft >= 0 ? Tick + Math.Min(s.FuseLeft, CharacterTicks.From(stats.FuseMost) + 1) : -1,
            FuseTicks = Math.Max(1, s.FuseTicks),
            WillDud = s.WillDud,
            Arms = Math.Max(1, s.Arms),
            DespawnTick = s.DespawnLeft >= 0 ? Tick + s.DespawnLeft : (s.Dud ? Tick + dudLifetime.Ticks : -1),
            Revision = 1,
        };
        _explosives.Add(e);
        _explosiveIds[e.Id] = e;
        if (e.Dud)
        {
            Things.Loose(e);
        }
        if (e.State != ExplosiveState.Flying)
        {
            // What held it is asked of its family (an ID from storage: untrusted); gone, the explosive falls.
            e.Link = Supports.LinkTo(s.Support, new TargetPose(e.Position, Geometry.Upright(e.Normal, 0)));
            if (e.Link.IsNone)
            {
                e.State = ExplosiveState.Flying;
                e.Velocity = Vector3.Zero;
                e.ArcVelocity = Vector3.Zero;
            }
            else
            {
                Supports.Hold(e.Ref, e.Link.Support);
            }
        }
        return true;
    }

    private static Vector3 Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) ? v : Vector3.Zero;

    protected internal override void Step()
    {
        StepExplosives();
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class ExplosiveSystemAccess
{
    public static ExplosiveSystem Explosives(this Zone zone) => zone.Require<ExplosiveSystem>();
}
