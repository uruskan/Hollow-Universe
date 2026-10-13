using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using Hollow.Simulation.Capabilities;

namespace Hollow.Simulation;

// Raiding (GROUND.md §6.6, G5): explosives thrown, placed and lit again; their flight, bounces and sticking; fuses and
// duds; and the blast of a charge, a rocket or an explosive round: damage with falloff to building pieces (by grade and
// side, through BuildingWorld.Damage), doors, deployables and characters, each only if nothing of the building or the
// ground stands in between; the piece hit (or the nearest in sight) takes the table's damage, others in sight a share.
// Claimed bases hurt this way are reported (RaidHits) for the notification service. Server only: clients get the
// explosives' states and the detonations.
public sealed partial class Zone
{
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
    private readonly Dictionary<object, double> _blastResidue = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<EntityId, long> _nextThrowTick = new();
    private readonly List<Piece> _blastPieces = new();
    private readonly List<Deployable> _blastThings = new();
    private readonly List<NaturalObject> _blastNature = new();

    /// <summary>Explosives in the world (flying, stuck, resting, duds).</summary>
    public IReadOnlyList<LiveExplosive> Explosives => _explosives;

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
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        Outcome check = CanThrow(p, slot, expected, out ThrowableStats? stats);
        if (check != OutcomeCode.Done)
        {
            return check;
        }
        ref Character c = ref Entities.Get<Character>(entity);
        Vector3 up = Frame.UpAt(c.State.Position);
        (yaw, pitch) = TrustedView(c, yaw, pitch);
        Vector3 direction = CharacterMotor.ViewDirection(yaw, pitch, up);
        Vector3 eye = EyeOf(c.State);
        // From a hand's length in front of the eye, unless something is right there.
        Vector3 start = eye + direction * 0.3f;
        if (Sweep(eye, start, 0, out _))
        {
            start = eye;
        }
        float speed = (float)(lob ? stats!.LobSpeed : stats!.ThrowSpeed);
        TakeOne(p, slot, stats);
        LiveExplosive e = Spawn(stats, p.Id, p.Name, start, direction * speed + c.State.Velocity * 0.5f);
        Arm(e);
        RaidStats.Thrown++;
        p.ActionCount++;
        p.LastAction = ActionKind.Throw;
        return OutcomeCode.Done;
    }

    /// <summary>
    /// Places the charge in a belt slot on the surface the view meets within reach (a wall, a door, a floor, a box, the
    /// ground): it sticks there with its fuse burning. No building rights are needed (raiding: RustMVP's rule).
    /// </summary>
    public Outcome PlaceCharge(Entity entity, int slot, ushort expected, float yaw, float pitch)
    {
        if (!Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        Outcome check = CanThrow(p, slot, expected, out ThrowableStats? stats);
        if (check != OutcomeCode.Done)
        {
            return check;
        }
        if (!stats!.Placeable)
        {
            return OutcomeCode.NotUsable;
        }
        ref Character c = ref Entities.Get<Character>(entity);
        (yaw, pitch) = TrustedView(c, yaw, pitch);
        Vector3 eye = EyeOf(c.State);
        Vector3 direction = CharacterMotor.ViewDirection(yaw, pitch, Frame.UpAt(c.State.Position));
        if (!Sweep(eye, eye + direction * Content!.PlacementReach, 0, out SurfaceHit hit))
        {
            return OutcomeCode.OutOfReach;
        }
        TakeOne(p, slot, stats);
        LiveExplosive e = Spawn(stats, p.Id, p.Name, hit.Point, Vector3.Zero);
        Stick(e, hit);
        Arm(e);
        RaidStats.Placed++;
        p.ActionCount++;
        p.LastAction = ActionKind.Throw;
        return OutcomeCode.Done;
    }

    /// <summary>Lights a dud again (the interact key on it, within reach): a new fuse, which may go out again.</summary>
    public Outcome Rearm(Entity entity, EntityId explosive)
    {
        if (!Acting(entity, out Player _))
        {
            return OutcomeCode.Dead;
        }
        if (ExplosiveOf(explosive) is not { } e)
        {
            return OutcomeCode.NotFound;
        }
        if (!e.Dud)
        {
            return OutcomeCode.NotUsable;
        }
        Outcome reach = Reach(EyeOf(Entities.Get<Character>(entity).State), e.Ref, Content!.Survival.InteractReach);
        if (!reach.Ok)
        {
            return reach;
        }
        Arm(e);
        RaidStats.Rearmed++;
        return OutcomeCode.Done;
    }

    // A throw or a placed charge: from the belt, checked by the held behaviour that sends it (GROUND_SYSTEMS.md §7).
    private Outcome CanThrow(Player p, int slot, ushort expected, out ThrowableStats? stats)
    {
        stats = null;
        Outcome holding = ItemFor(p, ContainerRef.Belt, slot, expected, HeldUse.Throwable, out ItemDef item);
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
    private static (float Yaw, float Pitch) TrustedView(in Character c, float yaw, float pitch)
    {
        if (!float.IsFinite(yaw) || !float.IsFinite(pitch) || MathF.Abs(pitch) > FirearmRules.MaxViewPitch)
        {
            return (c.Input.Yaw, c.Input.Pitch);
        }
        float dy = yaw - c.Input.Yaw;
        dy -= MathF.Round(dy / (2 * MathF.PI)) * 2 * MathF.PI;
        return MathF.Abs(dy) <= 0.6f && MathF.Abs(pitch - c.Input.Pitch) <= 0.6f ? (yaw, pitch) : (c.Input.Yaw, c.Input.Pitch);
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
        if (!Sweep(from, from + Vector3.Normalize(direction) * reach, 0, out SurfaceHit hit))
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
        var e = new LiveExplosive(NextId(), stats, owner, ownerName, Content!.Survival.DudLifetime)
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
            Unloose(e); // a dud lit again lies loose no longer
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
                    Loose(e);
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
                Release(_explosives[i].Ref);
            }
        }
        for (int i = _explosives.Count - 1; i >= 0; i--)
        {
            if (_explosives[i].Gone)
            {
                if (_explosives[i].Dud)
                {
                    Unloose(_explosives[i]);
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
            if (Sweep(p, next, radius, out SurfaceHit hit))
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
            Rest(e, new SurfaceHit { Point = ground, Normal = GroundNormal(ground).Normal, Support = TargetRef.Ground }, ground + Frame.UpAt(ground) * radius);
        }
    }

    private void Stick(LiveExplosive e, in SurfaceHit hit)
    {
        e.State = ExplosiveState.Stuck;
        Lay(e, hit, hit.Point + hit.Normal * StuckOffset);
    }

    private void Rest(LiveExplosive e, in SurfaceHit hit, Vector3 at)
    {
        e.State = ExplosiveState.Resting;
        Lay(e, hit, at);
    }

    // Comes to lie or stick on what it touched (GROUND_SYSTEMS.md §3.6): linked to it where it is, its up the surface's
    // normal, so it moves with a door's leaf and goes with its support.
    private void Lay(LiveExplosive e, in SurfaceHit hit, Vector3 at)
    {
        e.Position = at;
        e.Velocity = Vector3.Zero;
        e.ArcStart = at;
        e.ArcVelocity = Vector3.Zero;
        e.Normal = hit.Normal;
        e.StateTick = Tick;
        e.Airborne = 0;
        e.Link = LinkTo(hit.Support, new TargetPose(at, Geometry.Upright(hit.Normal, 0)));
        Hold(e.Ref, e.Link.Support);
        e.Revision++;
    }

    // A stuck or resting explosive follows what holds it when that moves (a door's leaf); when it goes, the support rule
    // (Zone.Supports.cs) drops it.
    private void Follow(LiveExplosive e)
    {
        if (e.Link.IsNone || !Moves(e.Link.Support) || !TryPose(e.Link, out TargetPose pose))
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
    internal void Drop(LiveExplosive e)
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
        Release(e.Ref);
        e.Link = SupportLink.None;
        e.Revision++;
    }

    // A fuse burnt down: the blast, from where it is; what it is stuck to takes the table's damage whole.
    private void Explode(LiveExplosive e)
    {
        e.Gone = true;
        TargetRef on = e.Link.Support;
        Release(e.Ref);
        Blast(e.Stats.Blast, e.Position, e.Normal, e.State == ExplosiveState.Stuck ? on : TargetRef.None, on.Family, e.Owner, e.OwnerName, e.Item.Id, e.Id);
    }

    // A rocket or an explosive round goes off a little short of what it hit (so its blast starts outside a wall).
    private void BulletBlast(Bullet b, BlastStats blast, in TraceHit hit, Player? shooter)
    {
        Vector3 direction = b.Velocity.LengthSquared() > 1e-6f ? Vector3.Normalize(b.Velocity) : -Frame.UpAt(b.Position);
        Vector3 at = hit.Point - direction * 0.05f;
        Blast(blast, at, -direction, hit.Target, hit.Target.Family, b.Shooter, shooter?.Name ?? b.ShooterName, b.Ammo.Id, round: b.Damage > 0);
    }

    // ---------------------------------------------------------------- what a flying explosive meets

    private struct SurfaceHit
    {
        public float Distance;
        public Vector3 Point;
        public Vector3 Normal;

        /// <summary>What was touched (a door's leaf as its part): what holds the explosive if it sticks or rests there.</summary>
        public TargetRef Support;
    }

    // The first surface a ball of a radius meets from one point to the next: building pieces, doors and claim devices,
    // deployables, trees and outcrops, the ground (characters are passed through).
    private bool Sweep(Vector3 from, Vector3 to, float radius, out SurfaceHit best)
    {
        best = new SurfaceHit { Distance = float.MaxValue };
        Vector3 d = to - from;
        float length = d.Length();
        if (length < 1e-6f)
        {
            return false;
        }
        Vector3 direction = d / length;

        if (Building?.Physics is { } physics && physics.Ray(from, direction, length + radius, out RayHit rh, buildingOnly: true))
        {
            float t = MathF.Max(0, rh.Distance - radius);
            if (t <= length)
            {
                // A piece, a door's leaf (its part), the claim device: whatever owns the body says how it is named.
                best = new SurfaceHit
                {
                    Distance = t, Point = rh.Point, Normal = Facing(rh.Normal, direction),
                    Support = physics.OwnerOf(rh.Body) is ITargetable owner ? owner.Ref : TargetRef.None,
                };
            }
        }

        float reach = MathF.Min(length, best.Distance);
        _blastThings.Clear();
        DeployablesNear((from + to) / 2, length / 2 + 2f, _blastThings);
        foreach (Deployable thing in _blastThings)
        {
            (Vector3 right, Vector3 forward) = Geometry.Axes(thing.Up, thing.Yaw);
            Vector3 half = thing.Def.HalfExtents + new Vector3(radius);
            float t = Geometry.RayBox(from, direction, reach, thing.Centre, half, right, thing.Up, forward);
            if (t >= 0 && t < best.Distance)
            {
                Vector3 point = from + direction * t;
                Vector3 local = point - thing.Centre;
                float x = Vector3.Dot(local, right) / half.X, y = Vector3.Dot(local, thing.Up) / half.Y, z = Vector3.Dot(local, forward) / half.Z;
                Vector3 normal = MathF.Abs(x) >= MathF.Abs(y) && MathF.Abs(x) >= MathF.Abs(z) ? right * MathF.Sign(x)
                    : MathF.Abs(y) >= MathF.Abs(z) ? thing.Up * MathF.Sign(y) : forward * MathF.Sign(z);
                if (t <= 0 && Vector3.Dot(direction, normal) >= 0)
                {
                    continue; // touching it and moving away
                }
                best = new SurfaceHit { Distance = t, Point = point - normal * radius, Normal = normal, Support = thing.Ref };
                reach = t;
            }
        }

        if (Nature != null && Natural != null)
        {
            _blastNature.Clear();
            float margin = 3f;
            Nature.Query(MathF.Min(from.X, to.X) - margin, MathF.Min(from.Z, to.Z) - margin, MathF.Max(from.X, to.X) + margin,
                MathF.Max(from.Z, to.Z) + margin, _blastNature);
            foreach (NaturalObject o in _blastNature)
            {
                NaturalKind? kind = Content!.Natural(o.Kind);
                if (kind == null || !kind.IsHarvested || Natural.IsDepleted(o.Id))
                {
                    continue;
                }
                float t;
                Vector3 normal;
                if (kind.Class == NaturalClass.Tree)
                {
                    (Vector3 a, Vector3 b, float r) = NaturalShapes.Trunk(o, kind);
                    t = Geometry.RayCapsule(from, direction, reach, a, b, r + radius);
                    Vector3 point = from + direction * MathF.Max(0, t);
                    Vector3 axis = Vector3.Normalize(b - a);
                    Vector3 onAxis = a + axis * Math.Clamp(Vector3.Dot(point - a, axis), 0, Vector3.Distance(a, b));
                    normal = SafeNormal(point - onAxis, -direction);
                }
                else
                {
                    (Vector3 centre, float r) = NaturalShapes.Rock(o, kind);
                    t = Geometry.RaySphere(from, direction, reach, centre, r + radius);
                    normal = SafeNormal(from + direction * MathF.Max(0, t) - centre, -direction);
                }
                if (t >= 0 && t < best.Distance && !(t <= 0 && Vector3.Dot(direction, normal) >= 0))
                {
                    best = new SurfaceHit
                    {
                        Distance = t, Point = from + direction * t - normal * radius, Normal = normal, Support = new TargetRef(TargetFamily.Natural, o.Id),
                    };
                    reach = t;
                }
            }
        }

        float ground = GroundCrossing(from, direction, reach);
        if (ground >= 0 && ground < best.Distance)
        {
            Vector3 point = from + direction * ground;
            best = new SurfaceHit { Distance = ground, Point = point, Normal = GroundNormal(point).Normal, Support = TargetRef.Ground };
        }
        return best.Distance <= length;
    }

    private static Vector3 Facing(Vector3 normal, Vector3 direction)
    {
        if (!float.IsFinite(normal.X) || normal.LengthSquared() < 1e-6f)
        {
            return -direction;
        }
        normal = Vector3.Normalize(normal);
        return Vector3.Dot(normal, direction) > 0 ? -normal : normal;
    }

    private static Vector3 SafeNormal(Vector3 v, Vector3 fallback) => v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : fallback;

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
        if (Content!.Item(s.Item)?.Throwable is not { } stats || ExplosiveOf(s.Id) != null || _explosives.Count >= MaxExplosives ||
            !float.IsFinite(s.Position.X) || !float.IsFinite(s.Position.Y) || !float.IsFinite(s.Position.Z))
        {
            return false;
        }
        LifetimeRule dudLifetime = Content!.Survival.DudLifetime;
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
            Loose(e);
        }
        if (e.State != ExplosiveState.Flying)
        {
            // What held it is asked of its family (an ID from storage: untrusted); gone, the explosive falls.
            e.Link = LinkTo(s.Support, new TargetPose(e.Position, Geometry.Upright(e.Normal, 0)));
            if (e.Link.IsNone)
            {
                e.State = ExplosiveState.Flying;
                e.Velocity = Vector3.Zero;
                e.ArcVelocity = Vector3.Zero;
            }
            else
            {
                Hold(e.Ref, e.Link.Support);
            }
        }
        return true;
    }

    private static Vector3 Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) ? v : Vector3.Zero;
}
