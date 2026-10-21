using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

// What a blast sees, and the building's side of raiding (GROUND.md §6.6): the explosive channel's fractions of a point,
// carried to the next hit, and the raid hits on claimed bases, summed per base over a blast. The blast itself is
// DamageSystem's: every receiver finds its own things in its reach.
public sealed class BlastSystem : ZoneSystem
{
    private static readonly int Channels = Enum.GetValues<DamageType>().Length;

    private BuildingSystem? _buildingSystem;
    private BuildingSystem Building => _buildingSystem ??= Zone.Require<BuildingSystem>();
    private CombatSystem? _combatSystem;
    private CombatSystem Combat => _combatSystem ??= Zone.Require<CombatSystem>();
    private DamageSystem? _damageSystem;
    private DamageSystem Damage => _damageSystem ??= Zone.Require<DamageSystem>();
    private ExplosiveSystem? _explosivesSystem;
    private ExplosiveSystem Explosives => _explosivesSystem ??= Zone.Require<ExplosiveSystem>();

    // Damage to a piece, door or claim device, kept to the fraction: the building rules take whole points (rounded up
    // once, RustMVP's resolver), so the parts of a point are carried to the next hit, a channel's apart from another's;
    // Rust's raid counts come out exactly (a grenade's 8.99 against metal takes 112, not 100), and a fire's heat every half
    // second (IW4) adds up to what a second of it is. The damage goes in through its channel: a blast's table through the
    // explosive channel, a fire's through the heat channel (which the grade's or door's protection lets through or not).
    internal (double Lost, bool Destroyed) DamageStructure(IBuildingPart part, double amount, Vector3 direction, double now,
        DamageType channel = DamageType.Explosive)
    {
        BuildingWorld building = Building.World!;
        if (!Explosives._blastResidue.TryGetValue(part, out double[]? residue))
        {
            residue = new double[Channels];
            Explosives._blastResidue[part] = residue;
        }
        double total = residue[(int)channel] + amount;
        double whole = Math.Floor(total + 1e-9);
        residue[(int)channel] = Math.Max(0, total - whole);
        if (whole <= 0)
        {
            return (0, false);
        }
        DamageResult result = building.Damage(part, DamageHit.Of(channel, whole, direction), now);
        if (result.Destroyed)
        {
            Explosives._blastResidue.Remove(part);
        }
        return (result.Loss, result.Destroyed);
    }

    /// <summary>
    /// A blow, arrow or bullet on a piece, door or claim device (GROUND.md §6.3's hook) by a player, as a damage request
    /// through the building's receiver: the building rules take it, and if the base is claimed and doesn't list the
    /// attacker, it is a raid (soft-siding, shooting a wooden door): reported in <see cref="RaidHits"/>. Returns the health lost.
    /// </summary>
    public float DamageBuildingBy(object part, Vector3 direction, float amount, bool projectile, Player? attacker, ushort weapon) =>
        Damage.Damage(new DamageRequest
        {
            By = attacker,
            Attacker = attacker?.Id ?? EntityId.None,
            AttackerName = attacker?.Name ?? "",
            Weapon = weapon,
            Delivery = projectile ? DamageDelivery.Bullet : DamageDelivery.Melee,
            Channel = projectile ? DamageType.Bullet : DamageType.Melee,
            Amounts = new DamageAmounts { Building = amount },
            Direct = true,
            Hit = new TraceHit { Target = part is ITargetable thing ? thing.Ref : TargetRef.None, Distance = 0 },
            Direction = direction,
            Markers = TargetFamilies.None,
        }).Taken;

    /// <summary>What a raid alert calls a piece, door or claim device ("Stone wall", "Sheet Metal Door", "Claim device").</summary>
    internal string RaidName(object part) => part switch
    {
        Piece piece => $"{Building.World!.Data.Grades[piece.Grade].Name} {piece.Def.Name.ToLowerInvariant()}",
        Door door => door.Def.Name,
        _ => Content.ClaimDeployable.Item.Name,
    };

    /// <summary>A blast's raid hits, summed per claimed base (one alert each, in the order the bases were first hit).</summary>
    internal sealed class RaidTallies
    {
        private readonly Dictionary<ClaimDevice, RaidTally> _bases = new(ReferenceEqualityComparer.Instance);

        public void Add(ClaimDevice claim, string name, bool destroyed, bool primary)
        {
            if (!_bases.TryGetValue(claim, out RaidTally? tally))
            {
                tally = new RaidTally(claim);
                _bases.Add(claim, tally);
            }
            tally.Add(name, destroyed, primary);
        }

        public void Report(Zone zone, EntityId attacker, string attackerName, ushort item)
        {
            foreach (RaidTally tally in _bases.Values)
            {
                zone.Explosives().RaidHits.Add(tally.Hit(attacker, attackerName, item, zone.UnixMilliseconds));
                zone.Explosives().RaidStats.RaidHits++;
            }
        }
    }

    // What one blow or blast did to one claimed base.
    internal sealed class RaidTally(ClaimDevice claim)
    {
        private readonly List<string> _destroyed = new();
        private readonly List<string> _hit = new();
        private int _hits;

        public void Add(string name, bool destroyed, bool primary)
        {
            _hits++;
            if (destroyed)
            {
                _destroyed.Add(name);
            }
            else if (primary)
            {
                _hit.Insert(0, name);
            }
            else
            {
                _hit.Add(name);
            }
        }

        public RaidHit Hit(EntityId attacker, string attackerName, ushort item, long unixMs)
        {
            IEnumerable<string> what = _destroyed.Distinct().Select(n => n + " broken").Concat(_hit.Distinct()).Take(3);
            return new RaidHit
            {
                Base = claim.Id,
                Where = claim.ZoneTransform.Position,
                Authorised = claim.Authorised.ToArray(),
                Attacker = attacker,
                AttackerName = attackerName,
                Item = item,
                What = string.Join(", ", what),
                Hits = _hits,
                Destroyed = _destroyed.Count,
                UnixMilliseconds = unixMs,
            };
        }
    }

    // ---------------------------------------------------------------- what a blast sees

    /// <summary>Whether a blast sees a point: no building piece or door and no ground in between (G6: ships' hulls).</summary>
    private bool BlastSees(Vector3 from, Vector3 to) => Visible(from, to, null);

    // Nothing of the building (but the target itself), of a ship's pieces (IRON_WAKE.md §4.9: a burst on the deck sees
    // the deck, one against the hull's outside nobody inside) nor the ground between a blast and a point.
    internal bool Visible(Vector3 from, Vector3 to, object? target)
    {
        Vector3 d = to - from;
        float length = d.Length();
        if (length < 0.05f)
        {
            return true;
        }
        Vector3 direction = d / length;
        if (Building.World?.Physics is { } physics && physics.Ray(from, direction, length - 0.02f, out RayHit hit, buildingOnly: true))
        {
            object? owner = physics.OwnerOf(hit.Body);
            object? part = owner is DoorLeaf leaf ? leaf.Door : owner;
            if (part != target || target == null)
            {
                return false;
            }
        }
        if (Zone.Frames.Ships.Count > 0 && ShipQueries.Ray(Zone.Frames.Ships, from, direction, length - 0.02f, Tick, out _))
        {
            return false;
        }
        return Combat.GroundCrossing(from, direction, length) < 0;
    }

    internal static Vector3 ClosestPoint(BuildingData data, Piece piece, Vector3 at)
    {
        BuildingFrame frame = piece.Structure.Frame;
        Vector3D local = PieceGeometry.ClosestPoint(data, piece.Def, piece.Pose, frame.ToLocal(new Vector3D(at.X, at.Y, at.Z)));
        Vector3D zone = frame.ToZone(local);
        return new Vector3((float)zone.X, (float)zone.Y, (float)zone.Z);
    }

    // The nearest point of a door's leaves (boxes hanging from their hinges), and the leaf's centre.
    internal static (Vector3 Point, Vector3 Centre) ClosestPoint(Door door, Vector3 at) => DoorShapes.Nearest(door, at);

    internal static Vector3 ClosestPoint(Deployable d, Vector3 at)
    {
        (Vector3 right, Vector3 forward) = Geometry.Axes(d.Up, d.Yaw);
        Vector3 local = at - d.Centre;
        Vector3 half = d.Def.HalfExtents;
        float x = Math.Clamp(Vector3.Dot(local, right), -half.X, half.X);
        float y = Math.Clamp(Vector3.Dot(local, d.Up), -half.Y, half.Y);
        float z = Math.Clamp(Vector3.Dot(local, forward), -half.Z, half.Z);
        return d.Centre + right * x + d.Up * y + forward * z;
    }

    // A second point to look at a piece by: its middle (half its height for walls, stairs and roofs).
    internal static Vector3 CentreOf(Piece piece)
    {
        double lift = piece.Def.Family is PieceFamily.Wall or PieceFamily.Stairs or PieceFamily.Roof ? piece.Def.Height / 2 : 0.05;
        Vector3D middle = PieceGeometry.ToFrame(piece.Pose, new Vector3D(0, lift, 0));
        Vector3D zone = piece.Structure.Frame.ToZone(middle);
        return new Vector3((float)zone.X, (float)zone.Y, (float)zone.Z);
    }

    // A door's second point: its first leaf's centre.
    internal static Vector3 CentreOf(Door door) => ClosestPoint(door, door.LeafNow(0).Position).Centre;
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class BlastSystemAccess
{
    public static BlastSystem Blasts(this Zone zone) => zone.Require<BlastSystem>();
}
