using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation;

/// <summary>What fires have done so far (the load test reports it).</summary>
public sealed class FireCounters
{
    /// <summary>Blasts that would leave a fire, fires lit, and those that came to nothing (no surface near enough below).</summary>
    public long Bursts { get; internal set; }

    public long Lit { get; internal set; }

    public long Fizzled { get; internal set; }

    private readonly long[] _out = new long[Enum.GetValues<FireEnd>().Length];

    /// <summary>Fires that went out, by why (fires that never lit in water count with <see cref="FireEnd.Doused"/>).</summary>
    public long Out(FireEnd why) => _out[(int)why];

    internal void WentOut(FireEnd why) => _out[(int)why]++;

    public long BurntOut => Out(FireEnd.BurntOut);

    public long Doused => Out(FireEnd.Doused);

    /// <summary>Fires that fell onto something else when what held them went.</summary>
    public long Moved { get; internal set; }

    /// <summary>Times a fire hurt what was in it, and the requests that made.</summary>
    public long Hurts { get; internal set; }

    public long Requests { get; internal set; }

    /// <summary>Time spent on fires.</summary>
    public TimeSpan Time { get; internal set; }

    public override string ToString() =>
        $"{Lit} fires lit of {Bursts} bursts ({Fizzled} with nothing to burn on), {Doused} in water, {BurntOut} burnt out, {Out(FireEnd.Replaced)} replaced, " +
        $"{Out(FireEnd.Fell)} fell to nothing, {Moved} fell onto something else, {Out(FireEnd.PutOut)} put out; {Hurts} hurts, {Requests} damage requests";
}

// Fires (IRON_WAKE.md §7, IW4): a blast whose data names a fire (explosives.json: the incendiary rocket's) leaves a pool of
// burning fuel where it went off, wherever it was delivered from (the shoulder launcher today, the Iron Wake's batteries
// with IW3: the Detonated event is all it listens to). The fuel rests on the surface the blast met when that faces up
// enough, or runs and falls straight down to the first one that does; a fire is a world thing of its own (the fire family,
// GROUND_SYSTEMS.md §3.1) held by what it burns on through a support link (§3.6), so a fire on a ship's deck rides the ship,
// and when what holds it goes it falls to what is below. Every so often it hurts what is within its radius and in its sight
// (the one reach and sight rule, §3.4, which enters ships' spaces, §3.8), by distance from its middle, through the damage
// pipeline's heat delivery (GROUND.md §6.4.2): people (less what they wear, setting them burning), animals, deployables that
// burn, and the building through its rules' heat channel (twig and wood burn, stone and up don't). A fire in water goes out
// at once; at most the rules' number burn in a zone, the oldest going first; fires don't spread and aren't saved.
public sealed class FireSystem : ZoneSystem
{
    private DamageSystem? _damageSystem;
    private DamageSystem Damage => _damageSystem ??= Zone.Require<DamageSystem>();
    private PlacementSystem? _placementSystem;
    private PlacementSystem Placement => _placementSystem ??= Zone.Require<PlacementSystem>();
    private PlayerSystem? _playersSystem;
    private PlayerSystem Players => _playersSystem ??= Zone.Require<PlayerSystem>();
    private SupportSystem? _supportsSystem;
    private SupportSystem Supports => _supportsSystem ??= Zone.Require<SupportSystem>();
    private TargetSystem? _targetsSystem;
    private TargetSystem Targets => _targetsSystem ??= Zone.Require<TargetSystem>();

    /// <summary>
    /// A fire looks for what it burns from this far above its surface (its flames' feet): what stands or lies on the surface
    /// is in it at once, and the surface it burns on, which holds it, is a few tenths of a metre away and in its sight.
    /// </summary>
    public const float Lift = 0.3f;

    /// <summary>How far into the surface a blast met it looks for what that surface is (its point, its facing, its thing).</summary>
    private const float Contact = 0.6f;

    /// <summary>A fire's things are looked for this much beyond its radius (a person's chest is found by its own measure).</summary>
    private const float Margin = 1.5f;

    private readonly List<Fire> _fires = new();
    private readonly Dictionary<EntityId, Fire> _ids = new();
    private readonly List<BlastTarget> _targets = new();
    private long _serial;
    private int _burning;
    private bool _stepping;

    /// <summary>The fires burning, the oldest first (one that went out during a step leaves the list at its end).</summary>
    public IReadOnlyList<Fire> All => _fires;

    /// <summary>How many fires are burning.</summary>
    public int Burning => _burning;

    /// <summary>A burning fire by its ID (the fire family's lookup), or null.</summary>
    public Fire? FireOf(EntityId id) => _ids.TryGetValue(id, out Fire? fire) && !fire.Out ? fire : null;

    public FireCounters Stats { get; } = new();

    protected internal override void Init() => Events.On<Detonated>((in Detonated e) => OnDetonated(e));

    // A blast whose data names a fire leaves it where it went off (the rocket's, the battery's, whatever sent it).
    private void OnDetonated(in Detonated e)
    {
        if (e.Blast?.Fire is { } kind && Content.Item(e.Item) is { } item)
        {
            Ignite(kind, e.Position, e.Normal, item, e.Player, e.PlayerName);
        }
    }

    // ---------------------------------------------------------------- lighting

    /// <summary>
    /// Lights a fire of a kind where burning fuel thrown out at <paramref name="at"/> comes to rest (IRON_WAKE.md §7): on the
    /// surface just behind the point (the one it faces out of, <paramref name="facing"/>) when that faces up within the rules'
    /// steepest, else straight down to the first surface that does, within the rules' reach; owned by whoever fired what left
    /// it. Null when nothing below holds the fuel, or it lands in water (it goes out at once). With the zone's most burning,
    /// the oldest goes out first.
    /// </summary>
    public Fire? Ignite(FireDef kind, Vector3 at, Vector3 facing, ItemDef item, EntityId owner, string ownerName)
    {
        Stats.Bursts++;
        if (!Settle(at, facing, out FireSpot spot))
        {
            Stats.Fizzled++;
            return null;
        }
        if (Ground.InWater(spot.Point))
        {
            Stats.WentOut(FireEnd.Doused);
            return null;
        }
        int most = Math.Max(1, Content.FireRules.Most);
        while (_burning >= most && Oldest() is { } oldest)
        {
            PutOut(oldest, FireEnd.Replaced);
        }
        var fire = new Fire(NextId(), kind, item, owner, ownerName, ++_serial)
        {
            StartTick = Tick,
            EndTick = Tick + kind.Ticks,
            NextHurtTick = Tick + kind.EveryTicks,
        };
        Place(fire, spot);
        _fires.Add(fire);
        _ids.Add(fire.Id, fire);
        _burning++;
        Stats.Lit++;
        Events.Add(new FireLit(Fire: fire.Id, Player: owner, Item: item.Id, Position: spot.Point, Normal: spot.Normal, Radius: (float)kind.Radius, On: fire.Link.Support));
        return fire;
    }

    // The fire lit longest ago that still burns.
    private Fire? Oldest()
    {
        foreach (Fire fire in _fires)
        {
            if (!fire.Out)
            {
                return fire;
            }
        }
        return null;
    }

    /// <summary>Puts a fire out at once (an admin, a test; and the zone's own reasons: its time up, water, too many fires).</summary>
    public void PutOut(Fire fire, FireEnd why = FireEnd.PutOut)
    {
        if (fire.Out)
        {
            return;
        }
        fire.Out = true;
        _ids.Remove(fire.Id);
        _burning--;
        Supports.Release(fire.Ref);
        Stats.WentOut(why);
        Events.Add(new FireOut(Fire: fire.Id, Position: fire.LastPose.Position, Why: why));
        if (!_stepping)
        {
            _fires.Remove(fire);
        }
    }

    /// <summary>
    /// What a fire burnt on went (the fire capability's consequence, GROUND_SYSTEMS.md §3.6): the fuel falls from where the
    /// fire was to what is below and burns on there for the time it had left; with nothing near enough below, or into
    /// water, it goes out.
    /// </summary>
    internal void SupportLost(Fire fire)
    {
        if (fire.Out)
        {
            return;
        }
        Vector3 from = fire.LastPose.Position;
        Vector3 up = Frame.UpAt(from);
        if (!Settle(from + up * 0.1f, up, out FireSpot spot))
        {
            PutOut(fire, FireEnd.Fell);
            return;
        }
        if (Ground.InWater(spot.Point))
        {
            PutOut(fire, FireEnd.Doused);
            return;
        }
        Place(fire, spot);
        Stats.Moved++;
        Events.Add(new FireLit(Fire: fire.Id, Player: fire.Owner, Item: fire.Item.Id, Position: spot.Point, Normal: spot.Normal,
            Radius: (float)fire.Kind.Radius, On: fire.Link.Support, Moved: true));
    }

    // Holds the fire on what it rests on, its middle on the surface and its +Y the surface's normal (held by the ground when
    // what it found doesn't answer for itself).
    private void Place(Fire fire, in FireSpot spot)
    {
        var at = new TargetPose(spot.Point, Geometry.Upright(spot.Normal, 0));
        SupportLink link = Supports.LinkTo(spot.Support, at);
        if (link.IsNone)
        {
            link = SupportLink.Of(TargetRef.Ground, new TargetPose(Vector3.Zero, Quaternion.Identity), at);
        }
        fire.Link = link;
        fire.LastPose = at;
        Supports.Hold(fire.Ref, link.Support);
        fire.Revision++;
    }

    /// <summary>Where a fire comes to rest: its middle, the surface's normal there and what the surface is.</summary>
    private readonly record struct FireSpot(Vector3 Point, Vector3 Normal, TargetRef Support);

    // Where burning fuel thrown out at a point comes to rest: on the surface just behind it (the one it faces out of) when
    // that faces up enough to hold it; else (a wall, a hull's side, a trunk, a body, the air) it runs off the face and falls
    // straight down to the first surface that does, within the rules' reach, sliding off steep ones on the way.
    private bool Settle(Vector3 at, Vector3 facing, out FireSpot spot)
    {
        FireRules rules = Content.FireRules;
        float holds = MathF.Cos((float)(rules.SteepestDegrees * Math.PI / 180));
        Vector3 n = facing.LengthSquared() > 1e-8f ? Vector3.Normalize(facing) : Frame.UpAt(at);
        Vector3 from = at;
        if (SurfaceAlong(at + n * 0.05f, -n, Contact, out spot))
        {
            if (Holds(spot, holds))
            {
                return true;
            }
            from = spot.Point + spot.Normal * 0.1f;
        }
        float left = (float)rules.RunsDown;
        for (int i = 0; i < 4 && left > 0.05f; i++)
        {
            if (!SurfaceAlong(from, -Frame.UpAt(from), left, out spot))
            {
                return false;
            }
            if (Holds(spot, holds))
            {
                return true;
            }
            left -= Vector3.Distance(from, spot.Point);
            from = spot.Point + spot.Normal * 0.1f;
        }
        return false;
    }

    private bool Holds(in FireSpot spot, float holds) => Vector3.Dot(spot.Normal, Frame.UpAt(spot.Point)) >= holds;

    // The first surface along a segment: the building, deployables and props, trees and outcrops and the ground (the
    // placement world's sweep, which a thrown charge's flight uses), or a ship's space (IRON_WAKE.md §4.9: a deck).
    private bool SurfaceAlong(Vector3 from, Vector3 direction, float length, out FireSpot spot)
    {
        spot = default;
        float best = float.MaxValue;
        if (Placement.World.Sweep(from, from + direction * length, 0, out PlacementHit hit))
        {
            best = hit.Distance;
            spot = new FireSpot(hit.Point, Vector3.Normalize(hit.Normal), hit.Target);
        }
        if (Zone.Frames.Ships.Count > 0 && ShipQueries.Ray(Zone.Frames.Ships, from, direction, MathF.Min(length, best), Tick, out ShipQueryHit met) &&
            met.Distance < best && met.Ship.TryPose(Tick, out ShipPose pose))
        {
            Vector3 normal = pose.DirectionToWorld(met.Hit.Normal);
            normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : -direction;
            best = met.Distance;
            spot = new FireSpot(from + direction * met.Distance, Vector3.Dot(normal, direction) > 0 ? -normal : normal, new TargetRef(TargetFamily.Ship, met.Key));
        }
        return best < float.MaxValue;
    }

    // ---------------------------------------------------------------- burning

    // Every tick: fires on what moves follow it; each hurts what is in it when its time comes, and goes out when its time
    // is up.
    private void StepFires()
    {
        if (_fires.Count == 0)
        {
            return;
        }
        long started = Stopwatch.GetTimestamp();
        _stepping = true;
        try
        {
            for (int i = 0; i < _fires.Count; i++)
            {
                Fire fire = _fires[i];
                if (fire.Out)
                {
                    continue;
                }
                if (Supports.Moves(fire.Link.Support) && Supports.TryLinkPose(fire.Link, out TargetPose pose))
                {
                    fire.LastPose = pose;
                }
                if (Tick >= fire.NextHurtTick)
                {
                    fire.NextHurtTick += fire.Kind.EveryTicks;
                    Hurt(fire);
                }
                if (!fire.Out && Tick >= fire.EndTick)
                {
                    PutOut(fire, FireEnd.BurntOut);
                }
            }
        }
        finally
        {
            _stepping = false;
        }
        _fires.RemoveAll(f => f.Out);
        Stats.Time += Stopwatch.GetElapsedTime(started);
    }

    // What is within the fire's radius and in its sight takes its heat by distance from its middle: each receiver's things
    // near it (the damage pipeline's reach), measured by the one reach and sight rule from just above the fire's surface to
    // the thing's nearest surface, each through one heat request. A fire in water goes out instead.
    private void Hurt(Fire fire)
    {
        if (Supports.TryLinkPose(fire.Link, out TargetPose pose))
        {
            fire.LastPose = pose;
        }
        else
        {
            pose = fire.LastPose;
        }
        Vector3 centre = pose.Position;
        if (Ground.InWater(centre))
        {
            PutOut(fire, FireEnd.Doused);
            return;
        }
        FireDef kind = fire.Kind;
        Vector3 normal = Vector3.Transform(Vector3.UnitY, pose.Rotation);
        Vector3 eye = centre + normal * Lift;
        float radius = (float)kind.Radius;
        Damage.InReach(new BlastQuery(eye, radius + Margin, TargetRef.None), _targets);
        Players.TryFindPlayer(fire.Owner, out _, out Player? by);
        Stats.Hurts++;
        for (int i = 0; i < _targets.Count; i++)
        {
            TargetRef target = _targets[i].Hit.Target;
            if (!Targets.TryNearest(target, eye, out Vector3 point))
            {
                continue;
            }
            float distance = Vector3.Distance(point, eye);
            if (distance > radius || !Targets.InSight(eye, point, target))
            {
                continue;
            }
            double share = kind.Falloff(distance) * kind.Every;
            if (share <= 0)
            {
                continue;
            }
            Vector3 toward = point - eye;
            Stats.Requests++;
            Damage.Damage(new DamageRequest
            {
                By = by,
                Attacker = fire.Owner,
                AttackerName = fire.OwnerName,
                Weapon = fire.Item.Id,
                Delivery = DamageDelivery.Heat,
                Channel = DamageType.Heat,
                Amounts = new DamageAmounts
                {
                    Flesh = (float)(kind.Flesh * share),
                    Structure = (float)(kind.Deployables * share),
                    Building = (float)(kind.Building * share),
                },
                Burn = (float)(kind.Burning * kind.Every),
                Cause = "Burned",
                Outright = true,
                Direct = target.Whole == fire.Link.Support.Whole,
                Hit = new TraceHit { Target = target, Region = HitRegion.Body, Point = point, Distance = distance },
                From = centre,
                Direction = toward.LengthSquared() > 1e-6f ? Vector3.Normalize(toward) : -normal,
                Markers = TargetFamilies.None,
            });
        }
    }

    // ---------------------------------------------------------------- the fire family

    /// <summary>
    /// Fires (GROUND_SYSTEMS.md §3.1, IW4), by their IDs: each where what holds it is now (a ship's deck carries it), its
    /// nearest point on its pool; it hides nothing from the aim and offers nothing; when what holds it goes, it falls
    /// (its capability's consequence, §3.6).
    /// </summary>
    internal sealed class FireTargets(Zone zone) : ITargetFamily
    {
        public TargetFamily Family => TargetFamily.Fire;

        public bool Moves => true;

        public bool Exists(ulong id) => zone.Fires().FireOf(new EntityId(id)) != null;

        public object? Resolve(ulong id) => zone.Fires().FireOf(new EntityId(id));

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (zone.Fires().FireOf(new EntityId(target.Id)) is not { } fire)
            {
                return false;
            }
            if (!zone.Supports().TryLinkPose(fire.Link, out pose))
            {
                pose = fire.LastPose;
            }
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (zone.Fires().FireOf(new EntityId(target.Id)) is not { } fire || !TryPose(target, out TargetPose pose))
            {
                return false;
            }
            point = Fire.NearestOnPool(pose, (float)fire.Kind.Radius, from);
            return true;
        }

        public string ModelKey(ulong id) => "";

        public CapabilitySet Capabilities(ulong id) => zone.Fires().FireOf(new EntityId(id)) != null ? zone.Content!.Capabilities.Fire : CapabilitySet.Empty;

        // Flames hide nothing behind them and offer nothing to the interact key.
        public void Aim(in AimRay ray, ref AimHit best)
        {
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (zone.Fires().FireOf(new EntityId(target.Id)) is not { } fire)
            {
                return false;
            }
            facts = new TargetFacts { Capabilities = Capabilities(target.Id), Name = "Fire", Position = fire.Position };
            return true;
        }

        public bool Remove(ulong id)
        {
            if (zone.Fires().FireOf(new EntityId(id)) is not { } fire)
            {
                return false;
            }
            zone.Fires().PutOut(fire);
            return true;
        }
    }

    protected internal override void Step() => StepFires();
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class FireSystemAccess
{
    public static FireSystem Fires(this Zone zone) => zone.Require<FireSystem>();
}
