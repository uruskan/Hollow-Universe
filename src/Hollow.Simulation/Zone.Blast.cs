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
using Hollow.Simulation.World;

namespace Hollow.Simulation;

// The blast of a charge, grenade, rocket or explosive round (GROUND.md §6.6): what it reaches and sees, the damage to
// building pieces and doors (by grade and side, through the building rules), deployables and characters, and the raid
// hits on claimed bases it makes.
public sealed partial class Zone
{
    // ---------------------------------------------------------------- the blast

    private struct BlastTarget
    {
        public object Part;
        public Vector3 Point;
        public float Distance;
        public bool Visible;
        public ClaimDevice? Claim;
    }

    /// <summary>
    /// An explosion (GROUND.md §6.6) at <paramref name="at"/>, facing <paramref name="normal"/>: building pieces and doors
    /// within the radius that nothing of the building or the ground hides take the blast's table damage for their grade
    /// (and side) times the falloff; the one it was stuck to (or else the nearest in sight) whole, the others
    /// <see cref="BlastStats.Splash"/> of it. Deployables and characters in sight take their damage with the falloff
    /// (characters less what they wear on the body against projectiles; a lethal hit may knock down). Claimed bases hit
    /// are reported in <see cref="RaidHits"/>. Everything is looked at before anything is damaged, so a wall that breaks
    /// still shields what was behind it.
    /// </summary>
    public BlastReport Blast(BlastStats blast, Vector3 at, Vector3 normal, object? stuckTo, HitTarget target, EntityId attacker, string attackerName,
        ushort item, EntityId explosive = default, bool round = false)
    {
        var report = new BlastReport();
        float radius = (float)blast.Radius;
        GameContent content = Content!;
        _blastTargets.Clear();

        // Building pieces and their doors within reach.
        if (Building is { } building)
        {
            building.PiecesNear(at, at, radius + 3.6f, _blastPieces);
            foreach (Piece piece in _blastPieces)
            {
                if (piece.Gone || piece.Collapsing)
                {
                    continue;
                }
                Vector3 point = ClosestPoint(building.Data, piece, at);
                float d = Vector3.Distance(point, at);
                if (d <= radius || piece == stuckTo)
                {
                    _blastTargets.Add(new BlastTarget { Part = piece, Point = point, Distance = piece == stuckTo ? 0 : d, Claim = piece.Structure.Claim });
                }
                if (piece.Door is { Gone: false } door)
                {
                    (Vector3 doorPoint, Vector3 _) = ClosestPoint(door, at);
                    float dd = Vector3.Distance(doorPoint, at);
                    if (dd <= radius || door == stuckTo)
                    {
                        _blastTargets.Add(new BlastTarget { Part = door, Point = doorPoint, Distance = door == stuckTo ? 0 : dd, Claim = piece.Structure.Claim });
                    }
                }
            }
        }

        // Deployables within reach. A claim device's box stands for the claim device, which is the building's: its body
        // is what hides it, and its health is the building rules'.
        if (stuckTo is Deployable box && ClaimOf(box) is { } stuckClaim)
        {
            stuckTo = stuckClaim;
        }
        _blastThings.Clear();
        DeployablesNear(at, radius + 2f, _blastThings);
        foreach (Deployable d in _blastThings)
        {
            if (d.Health <= 0)
            {
                continue;
            }
            ClaimDevice? claim = ClaimOf(d);
            object part = (object?)claim ?? d;
            Vector3 point = ClosestPoint(d, at);
            float distance = Vector3.Distance(point, at);
            if (distance <= radius || part == stuckTo)
            {
                _blastTargets.Add(new BlastTarget { Part = part, Point = point, Distance = part == stuckTo ? 0 : distance, Claim = claim });
            }
        }

        // Characters within reach (a sleeper is a character too).
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        var people = new List<(Entity Entity, Player Player, float Distance)>();
        for (int i = 0; i < players.Count; i++)
        {
            Player p = players.Values[i].Player;
            if (p.Dead)
            {
                continue;
            }
            Entity entity = Entities.AtSlot(players.Slots[i]);
            CharacterState s = Entities.Get<Character>(entity).State;
            if (s.Stance == Stance.Seated)
            {
                continue; // a pilot (G6): the ship's cabin takes the blast (below)
            }
            Vector3 up = Frame.UpAt(s.Position);
            bool lying = p.Sleeping || p.Wounded;
            Vector3 chest = s.Position + up * (lying ? 0.25f : s.Stance == Stance.Crouching ? 0.7f : 1.15f);
            Vector3 head = s.Position + up * (lying ? 0.3f : s.Stance == Stance.Crouching ? 1.0f : 1.6f);
            float distance = MathF.Max(0, MathF.Min(Vector3.Distance(chest, at), Vector3.Distance(head, at)) - 0.3f);
            if (distance > radius)
            {
                continue;
            }
            if (Visible(at, chest, null) || Visible(at, head, null))
            {
                people.Add((entity, p, distance));
            }
            else
            {
                report.Shielded.Add(p);
            }
        }

        // What sees the blast: what it is stuck to does; anything else only if nothing stands in between.
        for (int i = 0; i < _blastTargets.Count; i++)
        {
            BlastTarget t = _blastTargets[i];
            t.Visible = t.Part == stuckTo || t.Distance < 0.05f || Visible(at, t.Point, t.Part) || Visible(at, CentreOf(t.Part), t.Part);
            _blastTargets[i] = t;
            if (!t.Visible)
            {
                report.Shielded.Add(t.Part);
            }
        }

        // The piece or door hit: the one it was stuck to, else the nearest in sight.
        object? primary = stuckTo is Piece or Door ? stuckTo : null;
        if (primary == null)
        {
            float nearest = float.MaxValue;
            foreach (BlastTarget t in _blastTargets)
            {
                if (t.Visible && t.Part is Piece or Door && t.Distance < nearest)
                {
                    nearest = t.Distance;
                    primary = t.Part;
                }
            }
        }

        // Building pieces and doors, through the building rules (grade, side, collapse).
        var raids = new Dictionary<ClaimDevice, RaidTally>(ReferenceEqualityComparer.Instance);
        double now = BuildingNow;
        foreach (BlastTarget t in _blastTargets)
        {
            if (!t.Visible || t.Part is not (Piece or Door) || Building == null)
            {
                continue;
            }
            double share = blast.Falloff(t.Distance) * (t.Part == primary ? 1 : blast.Splash);
            if (share <= 0)
            {
                continue;
            }
            Vector3 toward = t.Point - at;
            Vector3 direction = t.Part == stuckTo || toward.LengthSquared() < 1e-6f ? -normal : Vector3.Normalize(toward);
            double amount;
            string name;
            if (t.Part is Piece piece)
            {
                if (piece.Gone || piece.Collapsing)
                {
                    continue;
                }
                bool soft = piece.Def.Family == PieceFamily.Wall && BuildingWorld.SoftSide(piece, direction);
                GradeDef grade = Building.Data.Grades[piece.Grade];
                amount = blast.Piece(grade.Key, soft) * share;
                name = $"{grade.Name} {piece.Def.Name.ToLowerInvariant()}";
                report.PiecesHit++;
            }
            else
            {
                Door door = (Door)t.Part;
                if (door.Gone)
                {
                    continue;
                }
                amount = blast.Door(door.Def.Protection.Key) * share;
                name = door.Def.Name;
                report.DoorsHit++;
            }
            (double lost, bool destroyed) = DamageStructure(t.Part, amount, direction, now);
            report.Damage[t.Part] = amount;
            RaidStats.StructureDamage += (long)Math.Round(lost);
            if (destroyed)
            {
                if (t.Part is Piece)
                {
                    report.PiecesDestroyed++;
                    RaidStats.PiecesDestroyed++;
                }
                else
                {
                    report.DoorsDestroyed++;
                    RaidStats.DoorsDestroyed++;
                }
            }
            if (t.Claim is { } c)
            {
                if (!raids.TryGetValue(c, out RaidTally? tally))
                {
                    tally = new RaidTally(c);
                    raids.Add(c, tally);
                }
                tally.Add(name, destroyed, t.Part == primary);
            }
        }

        // Deployables; a claim device takes their damage through the building rules' explosive channel, as a raid on its base.
        foreach (BlastTarget t in _blastTargets)
        {
            if (!t.Visible)
            {
                continue;
            }
            float amount = (float)(blast.Deployable * blast.Falloff(t.Distance));
            if (amount <= 0)
            {
                continue;
            }
            if (t.Part is ClaimDevice { Gone: false } device && Building != null)
            {
                Vector3 toward = t.Point - at;
                Vector3 direction = toward.LengthSquared() < 1e-6f ? -normal : Vector3.Normalize(toward);
                (double lost, bool destroyed) = DamageStructure(device, amount, direction, now);
                report.Damage[device] = amount;
                report.DeployablesHit++;
                RaidStats.StructureDamage += (long)Math.Round(lost);
                if (!raids.TryGetValue(device, out RaidTally? tally))
                {
                    tally = new RaidTally(device);
                    raids.Add(device, tally);
                }
                tally.Add(content.ClaimDeployable.Item.Name, destroyed, primary: false);
            }
            else if (t.Part is Deployable d && d.Health > 0)
            {
                DamageDeployable(d, amount);
                report.Damage[d] = amount;
                report.DeployablesHit++;
            }
        }

        // Characters: the blast's damage on the body, less what is worn there against projectiles.
        TryFindPlayer(attacker, out _, out Player? by);
        BlastShips(blast, at, stuckTo, item, by, report);
        foreach ((Entity entity, Player p, float distance) in people)
        {
            float protection = p.Inventory.Protection(content, HitRegion.Body, projectile: true);
            float amount = (float)(blast.Flesh * blast.Falloff(distance)) * (1 - protection);
            if (amount <= 0 || p.Dead)
            {
                continue;
            }
            bool wasDown = p.Wounded;
            Hurt(entity, p, amount, "Blown up", attacker, attackerName, canWound: true);
            report.Damage[p] = amount;
            report.CharactersHit++;
            GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.DamageTaken, Player = p.Id, Other = attacker, Amount = amount, Region = HitRegion.Body, Position = at, Item = item });
            if (by != null && by != p)
            {
                GameEvents.Add(new ZoneEvent
                {
                    Kind = ZoneEventKind.HitConfirmed, Player = by.Id, Other = p.Id, Target = HitTarget.Character, Region = HitRegion.Body, Amount = amount,
                    Flag = p.Dead, Downed = p.Wounded && !wasDown, Position = at, Item = item,
                });
            }
        }

        // Animals in sight (G7).
        BlastAnimals(blast, at, radius, attacker, attackerName, item);

        // Claimed bases: one report each for the notification service.
        foreach (RaidTally tally in raids.Values)
        {
            RaidHits.Add(tally.Hit(attacker, attackerName, item, UnixMilliseconds));
            RaidStats.RaidHits++;
        }

        RaidStats.Detonations++;
        GameEvents.Add(new ZoneEvent
        {
            Kind = ZoneEventKind.Detonated, Player = attacker, Other = explosive, Position = at, Velocity = normal, Item = item, Amount = radius,
            Target = target, Flag = round,
        });
        return report;
    }

    // Damage to a piece, door or claim device, kept to the fraction: the building rules take whole points (rounded up
    // once, RustMVP's resolver), so the parts of a point are carried to the next hit; Rust's raid counts come out exactly
    // (a grenade's 8.99 against metal takes 112, not 100). The table's explosive damage goes in through the explosive
    // channel.
    private (double Lost, bool Destroyed) DamageStructure(object part, double amount, Vector3 direction, double now)
    {
        BuildingWorld building = Building!;
        double total = _blastResidue.GetValueOrDefault(part) + amount;
        double whole = Math.Floor(total + 1e-9);
        _blastResidue[part] = Math.Max(0, total - whole);
        if (whole <= 0)
        {
            return (0, false);
        }
        var hit = new DamageHit(direction, Explosive: whole);
        DamageResult result = part switch
        {
            Piece piece => building.Damage(piece, hit, now),
            Door door => building.Damage(door, hit, now),
            ClaimDevice device => building.Damage(device, hit, now),
            _ => default,
        };
        if (result.Destroyed)
        {
            _blastResidue.Remove(part);
        }
        return (result.Loss, result.Destroyed);
    }

    /// <summary>
    /// A blow, arrow or bullet on a piece, door or claim device (GROUND.md §6.3's hook) by a player: the building rules
    /// take it, and if the base is claimed and doesn't list the attacker, it is a raid (soft-siding, shooting a wooden
    /// door): reported in <see cref="RaidHits"/>. Returns the health lost.
    /// </summary>
    public float DamageBuildingBy(object part, Vector3 direction, float amount, bool projectile, Player? attacker, ushort weapon)
    {
        ClaimDevice? claim = part switch
        {
            Piece piece => piece.Structure?.Claim,
            Door door => door.Frame.Structure?.Claim,
            ClaimDevice device => device,
            _ => null,
        };
        float lost = DamageBuilding(part, direction, amount, projectile);
        if (lost > 0 && claim != null && (attacker == null || !claim.IsAuthorised(attacker.Id.Value)))
        {
            var tally = new RaidTally(claim);
            bool destroyed = part is Piece { Gone: true } or Door { Gone: true } or ClaimDevice { Gone: true };
            tally.Add(part switch
            {
                Piece piece => $"{Building!.Data.Grades[piece.Grade].Name} {piece.Def.Name.ToLowerInvariant()}",
                Door door => door.Def.Name,
                _ => Content!.ClaimDeployable.Item.Name,
            }, destroyed, primary: true);
            RaidHits.Add(tally.Hit(attacker?.Id ?? EntityId.None, attacker?.Name ?? "", weapon, UnixMilliseconds));
            RaidStats.RaidHits++;
        }
        return lost;
    }

    // What one blast did to one claimed base.
    private sealed class RaidTally(ClaimDevice claim)
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

    // Nothing of the building (but the target itself) nor the ground between a blast and a point.
    /// <summary>Whether a blast sees a point: no building piece or door and no ground in between (G6: ships' hulls).</summary>
    private bool BlastSees(Vector3 from, Vector3 to) => Visible(from, to, null);

    private bool Visible(Vector3 from, Vector3 to, object? target)
    {
        Vector3 d = to - from;
        float length = d.Length();
        if (length < 0.05f)
        {
            return true;
        }
        Vector3 direction = d / length;
        if (Building?.Physics is { } physics && physics.Ray(from, direction, length - 0.02f, out RayHit hit, buildingOnly: true))
        {
            object? owner = physics.OwnerOf(hit.Body);
            object? part = owner is DoorLeaf leaf ? leaf.Door : owner;
            if (part != target || target == null)
            {
                return false;
            }
        }
        return GroundCrossing(from, direction, length) < 0;
    }

    private static Vector3 ClosestPoint(BuildingData data, Piece piece, Vector3 at)
    {
        BuildingFrame frame = piece.Structure.Frame;
        Vector3D local = PieceGeometry.ClosestPoint(data, piece.Def, piece.Pose, frame.ToLocal(new Vector3D(at.X, at.Y, at.Z)));
        Vector3D zone = frame.ToZone(local);
        return new Vector3((float)zone.X, (float)zone.Y, (float)zone.Z);
    }

    // The nearest point of a door's leaves (boxes hanging from their hinges), and the leaf's centre.
    private static (Vector3 Point, Vector3 Centre) ClosestPoint(Door door, Vector3 at)
    {
        (Vector3 Point, Vector3 Centre) best = default;
        float nearest = float.MaxValue;
        for (int i = 0; i < door.Def.Leaves.Count; i++)
        {
            LeafDef leaf = door.Def.Leaves[i];
            (Vector3 position, Quaternion rotation) = door.Leaf(i, door.Open);
            Vector3 local = Vector3.Transform(at - position, Quaternion.Inverse(rotation));
            Vector3 size = leaf.Size;
            var min = new Vector3(MathF.Min(0, size.X), 0, -MathF.Abs(size.Z) / 2);
            var max = new Vector3(MathF.Max(0, size.X), size.Y, MathF.Abs(size.Z) / 2);
            Vector3 clamped = Vector3.Clamp(local, min, max);
            Vector3 point = position + Vector3.Transform(clamped, rotation);
            float d = Vector3.DistanceSquared(point, at);
            if (d < nearest)
            {
                nearest = d;
                best = (point, position + Vector3.Transform((min + max) / 2, rotation));
            }
        }
        return best;
    }

    private static Vector3 ClosestPoint(Deployable d, Vector3 at)
    {
        (Vector3 right, Vector3 forward) = Geometry.Axes(d.Up, d.Yaw);
        Vector3 local = at - d.Centre;
        Vector3 half = d.Def.HalfExtents;
        float x = Math.Clamp(Vector3.Dot(local, right), -half.X, half.X);
        float y = Math.Clamp(Vector3.Dot(local, d.Up), -half.Y, half.Y);
        float z = Math.Clamp(Vector3.Dot(local, forward), -half.Z, half.Z);
        return d.Centre + right * x + d.Up * y + forward * z;
    }

    // A second point to look at a target by: the middle of a piece, a door's leaf, a deployable, a claim device's box.
    private Vector3 CentreOf(object part)
    {
        switch (part)
        {
            case Piece piece:
            {
                double lift = piece.Def.Family is PieceFamily.Wall or PieceFamily.Stairs or PieceFamily.Roof ? piece.Def.Height / 2 : 0.05;
                Vector3D middle = PieceGeometry.ToFrame(piece.Pose, new Vector3D(0, lift, 0));
                Vector3D zone = piece.Structure.Frame.ToZone(middle);
                return new Vector3((float)zone.X, (float)zone.Y, (float)zone.Z);
            }
            case Door door:
                return ClosestPoint(door, door.Leaf(0, door.Open).Position).Centre;
            case Deployable d:
                return d.Centre;
            case ClaimDevice c:
                return DeployableOf(c)?.Centre ?? c.ZoneTransform.Position;
            default:
                return Vector3.Zero;
        }
    }
}
