using System;
using System.Collections.Generic;
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
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation;

/// <summary>An arrow in flight (later bullets): simulated on the server only (ARCHITECTURE.md §11.5).</summary>
public sealed class Projectile
{
    public uint Id;
    public EntityId Shooter;
    public string ShooterName = "";
    public ItemDef Ammo = null!;
    public float Damage;
    public Vector3 Position;
    public Vector3 Velocity;
    public int AgeTicks;
    public int LifetimeTicks;

    /// <summary>Targets are tested where they were this many ticks before (what the shooter saw).</summary>
    public float RewindTicks;
}

/// <summary>
/// What a ray met first (GROUND_SYSTEMS.md §3.1): the thing, named by its family and ID (a body's part: the bone hit), the
/// body part's region, how far along and where. The family's receiver finds the thing itself.
/// </summary>
public struct TraceHit
{
    public TargetRef Target;
    public float Distance;
    public Vector3 Point;
    public HitRegion Region;

    public readonly bool Hit => Distance >= 0;
}

// Melee and the bow (GROUND.md §6): swings and shots from the held item, tested on the server against where others
// were when the attacker saw them (ARCHITECTURE.md §11.5); arrows fly with drop; damage by body part, armour, bleeding.
public sealed partial class Zone
{
    /// <summary>A melee swing is a ray thickened by this much (metres).</summary>
    public const float SweepRadius = 0.15f;

    /// <summary>Arrows are this thick.</summary>
    public const float ArrowRadius = 0.02f;

    private readonly List<Projectile> _projectiles = new();
    private readonly List<NaturalObject> _traceNature = new();
    private readonly List<Deployable> _traceThings = new();
    private uint _nextProjectile = 1;

    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    private float MaxRewindTicks => Content!.Survival.MaxRewindSeconds * CharacterMotor.TickRate;

    private void StepActions()
    {
        GameContent content = Content!;
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        _scratchEntities.Clear();
        for (int i = 0; i < players.Count; i++)
        {
            _scratchEntities.Add(Entities.AtSlot(players.Slots[i]));
        }
        foreach (Entity entity in _scratchEntities)
        {
            if (PlayerOf(entity) is not { IsAwakeAndAlive: true } p)
            {
                continue;
            }
            ref Character c = ref Entities.Get<Character>(entity);
            if (p.Wounded)
            {
                RefuseGunActions(p, OutcomeCode.Dead);
                continue;
            }
            if (Entities.Has<Ships.ShipSeat>(entity))
            {
                // In a ship's seat (G6): the hands are on the controls.
                RefuseGunActions(p, OutcomeCode.Lowered);
                continue;
            }
            ActionInput input = c.Actions;
            // Taking up another item (or another in the same slot) takes a moment and ends what was going on: the held
            // behaviour of what was in hand says what ends (GROUND_SYSTEMS.md §7); the gun's state follows the hands.
            ushort inSlot = input.HeldSlot < PlayerInventory.BeltSlots ? p.Inventory.Belt[input.HeldSlot].Item : (ushort)0;
            if (input.HeldSlot != p.HeldSlot || inSlot != p.HeldItem)
            {
                IHeldRules? putAway = HeldRulesOf(content.Item(p.HeldItem));
                p.HeldSlot = input.HeldSlot;
                p.HeldItem = inSlot;
                p.EquipReadyTick = Tick + CharacterTicks.From(content.Survival.EquipSeconds);
                putAway?.PutAway(p);
                EquipGun(p);
            }
            // The item in hand's buttons, by its held behaviour.
            if (p.Held(content) is { } held)
            {
                HeldRulesOf(held)?.Step(entity, ref c, p, held);
            }
            StepGun(entity, ref c, p);
        }
    }

    private void StepMelee(Entity entity, ref Character c, Player p, ItemDef item, MeleeStats melee)
    {
        if (p.StrikeTick < 0 && c.Actions.Has(ActionButtons.Primary) && Tick >= p.NextSwingTick && Tick >= p.EquipReadyTick)
        {
            p.StrikeTick = Tick + melee.StrikeTicks;
            p.NextSwingTick = Tick + melee.SwingTicks;
            p.ActionCount++;
            p.LastAction = ActionKind.Swing;
        }
        if (p.StrikeTick == Tick)
        {
            p.StrikeTick = -1;
            Strike(entity, ref c, p, item, melee);
        }
    }

    /// <summary>
    /// The swing lands: a thick ray from the eye along the view, out to the tool's reach; whatever it meets first (but the
    /// ground) takes a melee blow through the damage pipeline (GROUND.md §6.4.2): its damage to flesh (by the body part),
    /// its structure damage to deployables and the building, its damage to hulls; trees and outcrops are gathered, a
    /// corpse butchered.
    /// </summary>
    private void Strike(Entity entity, ref Character c, Player p, ItemDef item, MeleeStats melee)
    {
        GameContent content = Content!;
        Vector3 up = Frame.UpAt(c.State.Position);
        Vector3 eye = EyeOf(c.State);
        Vector3 direction = CharacterMotor.ViewDirection(c.Input.Yaw, c.Input.Pitch, up);
        float rewind = Math.Min(c.Actions.ViewDelay / 4f, MaxRewindTicks);
        TraceHit hit = Trace(eye, direction, melee.Reach, SweepRadius, p.Id, Tick - rewind);
        if (!hit.Hit || hit.Target.Family == TargetFamily.Ground)
        {
            return;
        }
        Damage(new DamageRequest
        {
            By = p,
            Attacker = p.Id,
            AttackerName = p.Name,
            Weapon = item.Id,
            Delivery = DamageDelivery.Melee,
            Channel = DamageType.Melee,
            Amounts = new DamageAmounts
            {
                Flesh = melee.Damage * content.Survival.RegionMultiplier(hit.Region),
                Structure = melee.Structure,
                Building = melee.Structure,
                Hull = melee.Damage,
            },
            Bleed = melee.Bleed,
            Cause = "Struck down",
            Direct = true,
            Hit = hit,
            From = eye,
            Direction = direction,
            Markers = TargetFamilies.All,
        });
        Wear(entity, p, p.HeldSlot);
    }

    private void Gather(Player p, Entity entity, in NaturalObject o, MeleeStats tool, Vector3 point, Vector3 eye)
    {
        // The swing's own line (the thick ray may have touched the object off it) and the tool (the last hit's bonus).
        ref Character c = ref Entities.Get<Character>(entity);
        Vector3 view = CharacterMotor.ViewDirection(c.Input.Yaw, c.Input.Pitch, Frame.UpAt(c.State.Position));
        GatherResult result = Natural!.Hit(o, new GatherSwing(p.Held(Content!), tool, point, eye, view, UnixMilliseconds, Tick));
        if (!result.Hit)
        {
            return;
        }
        if (Natural.IsDepleted(o.Id))
        {
            SupportGone(new TargetRef(TargetFamily.Natural, o.Id));
        }
        GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.HitConfirmed, Player = p.Id, Target = new TargetRef(TargetFamily.Natural, o.Id), Flag = result.WeakSpot, Position = result.Point });
        Give(entity, p, result.Yield, GainReason.Gathered, result.WeakSpot);
    }

    /// <summary>Gives items to a player (onto stacks, then empty slots; the rest at their feet) and says so.</summary>
    private void Give(Entity entity, Player p, List<ItemAmount> items, GainReason reason, bool flag = false)
    {
        foreach (ItemAmount a in items)
        {
            GiveOrDrop(entity, p, ItemStack.Of(a.Item, a.Count));
            GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.ItemsGained, Player = p.Id, Item = a.Item.Id, Count = a.Count, Reason = reason, Flag = flag });
        }
    }

    // A use costs the held tool one point of condition; at none it breaks.
    private void Wear(Entity entity, Player p, int beltSlot)
    {
        if (beltSlot >= PlayerInventory.BeltSlots)
        {
            return;
        }
        ItemStack s = p.Inventory.Belt[beltSlot];
        ItemDef? item = Content!.Item(s.Item);
        if (item == null || !item.HasCondition)
        {
            return;
        }
        if (s.Condition <= 1)
        {
            p.Inventory.Belt.Set(beltSlot, default);
            ReturnRounds(entity, p, s);
            GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.ItemsGained, Player = p.Id, Item = s.Item, Count = -1, Reason = GainReason.Broke });
            return;
        }
        s.Condition--;
        p.Inventory.Belt.Set(beltSlot, s);
    }

    private void StepBow(Entity entity, ref Character c, Player p, ItemDef item, BowStats bow)
    {
        ItemDef? ammo = null;
        foreach (ItemDef a in bow.Ammo)
        {
            if (p.Inventory.Count(a) > 0)
            {
                ammo = a;
                break;
            }
        }
        bool drawing = c.Actions.Has(ActionButtons.Secondary) && Tick >= p.EquipReadyTick && ammo != null &&
            (c.State.Flags & MoveFlags.Swimming) == 0;
        if (!drawing)
        {
            p.DrawStartTick = -1;
            return;
        }
        if (p.DrawStartTick < 0)
        {
            p.DrawStartTick = Tick;
            p.ActionCount++;
            p.LastAction = ActionKind.Draw;
        }
        if (!c.Actions.Has(ActionButtons.PrimaryPressed) || Tick - p.DrawStartTick < bow.DrawTicks)
        {
            return;
        }
        // Loose: an arrow from the inventory, from the eye along the view, at the arrow's speed.
        InventoryRules.Take(p.Inventory.Carried, ammo!.Id, 1);
        Vector3 up = Frame.UpAt(c.State.Position);
        Vector3 direction = CharacterMotor.ViewDirection(c.Input.Yaw, c.Input.Pitch, up);
        var projectile = new Projectile
        {
            Id = _nextProjectile++,
            Shooter = p.Id,
            ShooterName = p.Name,
            Ammo = ammo,
            Damage = ammo.Ammo!.Damage * bow.DamageScale,
            Position = EyeOf(c.State),
            Velocity = direction * (ammo.Ammo.Speed * bow.SpeedScale),
            LifetimeTicks = CharacterTicks.From(ammo.Ammo.LifetimeSeconds),
            RewindTicks = Math.Min(c.Actions.ViewDelay / 4f, MaxRewindTicks),
        };
        Launch(projectile);
        p.DrawStartTick = Tick; // drawing again while the button is held
        p.ActionCount++;
        p.LastAction = ActionKind.Shoot;
        Wear(entity, p, p.HeldSlot);
    }

    /// <summary>Starts a projectile and tells those near.</summary>
    public void Launch(Projectile projectile)
    {
        _projectiles.Add(projectile);
        GameEvents.Add(new ZoneEvent
        {
            Kind = ZoneEventKind.ProjectileLaunched,
            Player = projectile.Shooter,
            Projectile = projectile.Id,
            Item = projectile.Ammo.Id,
            Position = projectile.Position,
            Velocity = projectile.Velocity,
        });
    }

    // Arrows fly in two steps a tick, under the planet's gravity, tested against the world now and against characters
    // where the shooter saw them.
    private void StepProjectiles()
    {
        const int steps = 2;
        const float h = CharacterMotor.DeltaTime / steps;
        float gravity = (float)(Spec.SurfaceGravity * 9.80665);
        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            Projectile pr = _projectiles[i];
            bool ended = false;
            for (int s = 0; s < steps && !ended; s++)
            {
                Vector3 g = -Frame.UpAt(pr.Position) * (gravity * pr.Ammo.Ammo!.Gravity);
                Vector3 next = pr.Position + pr.Velocity * h + g * (0.5f * h * h);
                Vector3 path = next - pr.Position;
                float length = path.Length();
                if (length > 1e-6f)
                {
                    TraceHit hit = Trace(pr.Position, path / length, length, ArrowRadius, pr.Shooter, Tick - pr.RewindTicks);
                    if (hit.Hit)
                    {
                        ProjectileHit(pr, hit);
                        ended = true;
                        break;
                    }
                }
                pr.Position = next;
                pr.Velocity += g * h;
            }
            if (!ended && ++pr.AgeTicks >= pr.LifetimeTicks)
            {
                ended = true;
                GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.ProjectileEnded, Player = pr.Shooter, Projectile = pr.Id, Position = pr.Position });
            }
            if (ended)
            {
                _projectiles.RemoveAt(i);
            }
        }
    }

    // An arrow stops at what it met: an arrow request through the damage pipeline (GROUND.md §6.4.2); one in a body or a
    // hull stays there, one elsewhere is often left where it landed.
    private void ProjectileHit(Projectile pr, in TraceHit hit)
    {
        AmmoStats ammo = pr.Ammo.Ammo!;
        GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.ProjectileEnded, Player = pr.Shooter, Projectile = pr.Id, Position = hit.Point, Target = hit.Target });
        TryFindPlayer(pr.Shooter, out _, out Player? shooter);
        Damage(new DamageRequest
        {
            By = shooter,
            Attacker = shooter?.Id ?? EntityId.None,
            AttackerName = shooter?.Name ?? pr.ShooterName,
            Weapon = pr.Ammo.Id,
            Delivery = DamageDelivery.Arrow,
            Channel = DamageType.Bullet,
            Amounts = new DamageAmounts
            {
                Flesh = pr.Damage * Content!.Survival.RegionMultiplier(hit.Region),
                Structure = pr.Damage * ammo.Structure,
                Building = pr.Damage * ammo.Structure,
                Hull = pr.Damage,
            },
            Bleed = ammo.Bleed,
            Cause = "Shot",
            Direct = true,
            Hit = hit,
            From = pr.Position,
            Direction = Vector3.Normalize(pr.Velocity),
            Markers = TargetFamilies.All,
        });
        if (Keeps(hit.Target.Family))
        {
            return;
        }
        // A miss often leaves the arrow where it landed (in a bag, with others that landed close by).
        var rng = new Rng(Hash64.Of(pr.Id, (ulong)Tick, 0xA770));
        if (rng.NextDouble() < ammo.Recover)
        {
            Spill(hit.Point - Vector3.Normalize(pr.Velocity) * 0.1f, ItemStack.Of(pr.Ammo, 1));
        }
    }

    /// <summary>
    /// Damage to a character from a blow or an arrow (tests): a request through the characters' receiver, with the body
    /// part's multiplier (survival.json), then what is worn there takes its share (items.json); bleeding starts in
    /// proportion. Tells the victim and the attacker. Returns the health taken.
    /// </summary>
    public float DamageCharacter(Entity victimEntity, Player victim, float damage, HitRegion region, bool projectile, float bleed,
        Player? attacker, Vector3 from, ushort weapon) => Damage(new DamageRequest
        {
            By = attacker,
            Attacker = attacker?.Id ?? EntityId.None,
            AttackerName = attacker?.Name ?? "",
            Weapon = weapon,
            Delivery = projectile ? DamageDelivery.Arrow : DamageDelivery.Melee,
            Channel = projectile ? DamageType.Bullet : DamageType.Melee,
            Amounts = new DamageAmounts { Flesh = damage * Content!.Survival.RegionMultiplier(region) },
            Bleed = bleed,
            Cause = projectile ? "Shot" : "Struck down",
            Direct = true,
            Hit = new TraceHit { Target = victim.Ref, Region = region, Point = from },
            From = from,
            Markers = TargetFamilies.All,
        }).Taken;

    /// <summary>
    /// The characters' receiver (GROUND.md §6.4.2): players and sleepers, found where the attacker saw them (the hit
    /// history, lag compensation) by their hit shapes, and by chest and head in a blast's reach. Its defences: what is
    /// worn on the part hit (items.json, against blows or against projectiles and blasts). Its consequences: health (a
    /// lethal hit may knock down instead, except a projectile to the head), bleeding and burning, the victim's damage
    /// indicator.
    /// </summary>
    private sealed class CharacterReceiver(Zone zone) : IDamageReceiver
    {
        public TargetFamily[] Families { get; } = { TargetFamily.Character };

        public bool Keeps => true;

        public void Raycast(in RayQuery ray, ref TraceHit best)
        {
            // Characters, where the attacker saw them (those nowhere near now are skipped before their history is looked up).
            float length = ray.Length;
            foreach (EntityId id in zone.History.Characters)
            {
                if (id == ray.Ignore || (zone.History.TryNewest(id, out Vector3 now) && Vector3.DistanceSquared(now, ray.Origin) > (length + 8f) * (length + 8f)) ||
                    !zone.History.Sample(id, ray.ViewTick, out HitPose pose) ||
                    Vector3.DistanceSquared(pose.Feet, ray.Origin) > (length + 2.5f) * (length + 2.5f))
                {
                    continue;
                }
                if (!zone.TryFindPlayer(id, out _, out Player p) || p.Dead)
                {
                    continue;
                }
                float t = Hitboxes.Raycast(pose, ray.Origin, ray.Direction, length, ray.Thickness, out HitRegion region, out int shape);
                ray.Consider(ref best, t, new TargetRef(TargetFamily.Character, id.Value, (byte)(shape + 1)), region);
            }
        }

        public void InReach(in BlastQuery blast, List<BlastTarget> into)
        {
            // A sleeper and the downed lie; a pilot is the ship's cabin's to protect (G6).
            ComponentPool<PlayerLink> players = zone.Entities.Pool<PlayerLink>();
            for (int i = 0; i < players.Count; i++)
            {
                Player p = players.Values[i].Player;
                if (p.Dead)
                {
                    continue;
                }
                Entity entity = zone.Entities.AtSlot(players.Slots[i]);
                CharacterState s = zone.Entities.Get<Character>(entity).State;
                if (s.Stance == Stance.Seated)
                {
                    continue;
                }
                Vector3 up = zone.Frame.UpAt(s.Position);
                bool lying = p.Sleeping || p.Wounded;
                Vector3 chest = s.Position + up * (lying ? 0.25f : s.Stance == Stance.Crouching ? 0.7f : 1.15f);
                Vector3 head = s.Position + up * (lying ? 0.3f : s.Stance == Stance.Crouching ? 1.0f : 1.6f);
                float distance = MathF.Max(0, MathF.Min(Vector3.Distance(chest, blast.At), Vector3.Distance(head, blast.At)) - 0.3f);
                if (distance <= blast.Radius)
                {
                    into.Add(new BlastTarget
                    {
                        Hit = new TraceHit { Target = p.Ref, Region = HitRegion.Body, Point = chest }, Thing = p,
                        Sight = chest, Sight2 = head, Distance = distance,
                    });
                }
            }
        }

        public DamageOutcome Receive(in DamageRequest r)
        {
            if (!zone.TryFindPlayer(new EntityId(r.Hit.Target.Id), out Entity victimEntity, out Player victim))
            {
                return default;
            }
            HitRegion region = r.Hit.Region;
            float protection = victim.Inventory.Protection(zone.Content!, region, projectile: r.Channel != DamageType.Melee);
            float amount = r.Amounts.Flesh * (1 - protection);
            if (amount <= 0 || victim.Dead)
            {
                return default;
            }
            bool wasDown = victim.Wounded;
            victim.Vitals.Bleeding += amount * r.Bleed;
            victim.Vitals.Burning += r.Burn * (1 - protection);
            // Rust: a projectile to the head kills outright; anything else lethal may knock down instead (G4).
            float taken = zone.Hurt(victimEntity, victim, amount, r.Cause, r.Attacker, r.AttackerName,
                canWound: !(r.Channel == DamageType.Bullet && region == HitRegion.Head));
            zone.GameEvents.Add(new ZoneEvent
            {
                Kind = ZoneEventKind.DamageTaken, Player = victim.Id, Other = r.ByAnimal?.Id ?? r.Attacker, Amount = amount, Region = region, Position = r.From,
                Item = r.Weapon,
            });
            return new DamageOutcome(true, amount, taken, victim.Dead, victim.Wounded && !wasDown, victim.Id);
        }
    }

    /// <summary>Where a ray first goes below the ground (within <paramref name="length"/>), or −1.</summary>
    public float GroundCrossing(Vector3 origin, Vector3 direction, float length)
    {
        float previous = HeightAboveGround(origin);
        if (previous < 0)
        {
            return -1; // starting under the ground: the surface doesn't block (it would block everything)
        }
        int steps = Math.Max(1, (int)MathF.Ceiling(length / 0.5f));
        float t0 = 0;
        for (int i = 1; i <= steps; i++)
        {
            float t1 = length * i / steps;
            float height = HeightAboveGround(origin + direction * t1);
            if (height < 0)
            {
                // Bisect between the last point above and this one.
                float lo = t0, hi = t1;
                for (int k = 0; k < 8; k++)
                {
                    float mid = (lo + hi) / 2;
                    if (HeightAboveGround(origin + direction * mid) < 0)
                    {
                        hi = mid;
                    }
                    else
                    {
                        lo = mid;
                    }
                }
                return hi;
            }
            t0 = t1;
        }
        return -1;
    }

    /// <summary>
    /// Trees, outcrops and boulders (GROUND.md §6.4.2): found by their trunks and rocks along a ray (a boulder stops a
    /// swing or an arrow like the ground and gives nothing); blasts pass them by. A blow of a tool gathers (the gathering
    /// rules, §6.1, which confirm the hit with its weak spot); arrows and bullets do nothing to them.
    /// </summary>
    private sealed class NatureReceiver(Zone zone) : IDamageReceiver
    {
        public TargetFamily[] Families { get; } = { TargetFamily.Natural };

        public bool Keeps => false;

        public void Raycast(in RayQuery ray, ref TraceHit best)
        {
            Vector3 end = ray.Origin + ray.Direction * ray.Length;
            float margin = 3f;
            zone._traceNature.Clear();
            zone.Nature!.Query(MathF.Min(ray.Origin.X, end.X) - margin, MathF.Min(ray.Origin.Z, end.Z) - margin,
                MathF.Max(ray.Origin.X, end.X) + margin, MathF.Max(ray.Origin.Z, end.Z) + margin, zone._traceNature);
            foreach (NaturalObject o in zone._traceNature)
            {
                NaturalKind? kind = zone.Content!.Natural(o.Kind);
                if (kind == null || !kind.IsSolid || zone.Natural!.IsDepleted(o.Id))
                {
                    continue;
                }
                float t;
                if (kind.Class == NaturalClass.Tree)
                {
                    (Vector3 a, Vector3 b, float r) = NaturalShapes.Trunk(o, kind);
                    t = Geometry.RayCapsule(ray.Origin, ray.Direction, ray.Length, a, b, r + ray.Thickness);
                }
                else
                {
                    (Vector3 centre, float r) = NaturalShapes.Rock(o, kind);
                    t = Geometry.RaySphere(ray.Origin, ray.Direction, ray.Length, centre, r + ray.Thickness);
                }
                // A boulder stops the swing or the arrow like the ground, and gives nothing.
                ray.Consider(ref best, t, kind.IsHarvested ? new TargetRef(TargetFamily.Natural, o.Id) : TargetRef.Ground);
            }
        }

        public void InReach(in BlastQuery blast, List<BlastTarget> into)
        {
        }

        public DamageOutcome Receive(in DamageRequest r)
        {
            if (r.Channel == DamageType.Melee && r.By is { } p && zone.Content!.Item(r.Weapon)?.Melee is { } tool && zone.TryFindPlayer(p.Id, out Entity entity, out _) &&
                zone.Nature!.TryGet(r.Hit.Target.Id, out NaturalObject o))
            {
                zone.Gather(p, entity, o, tool, r.Hit.Point, r.From);
            }
            return default;
        }
    }

    /// <summary>The ground (GROUND.md §6.4.2): it hides all behind it, and takes nothing.</summary>
    private sealed class GroundReceiver(Zone zone) : IDamageReceiver
    {
        public TargetFamily[] Families { get; } = { TargetFamily.Ground };

        public bool Keeps => false;

        public void Raycast(in RayQuery ray, ref TraceHit best)
        {
            float length = ray.Upto(best);
            float ground = ray.Bullet ? zone.BulletGroundCrossing(ray.Origin, ray.Direction, length) : zone.GroundCrossing(ray.Origin, ray.Direction, length);
            if (ground >= 0)
            {
                ray.Consider(ref best, ground, TargetRef.Ground);
            }
        }

        public void InReach(in BlastQuery blast, List<BlastTarget> into)
        {
        }

        public DamageOutcome Receive(in DamageRequest r) => default;
    }
}
