using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

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

/// <summary>What a ray met first.</summary>
public struct TraceHit
{
    public HitTarget Target;
    public float Distance;
    public Vector3 Point;
    public Entity Character;
    public HitRegion Region;
    public NaturalObject Natural;
    public Deployable? Deployable;

    /// <summary>The building piece, door or claim device hit.</summary>
    public object? Building;

    /// <summary>The ship hit (G6).</summary>
    public Ships.Ship? Ship;

    /// <summary>The animal hit (alive or a corpse), G7.</summary>
    public Animals.Animal? Animal;

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
                RefuseGunActions(p, GunResult.NotAlive);
                continue;
            }
            if (Entities.Has<Ships.ShipSeat>(entity))
            {
                // In a ship's seat (G6): the hands are on the controls.
                RefuseGunActions(p, GunResult.NotReady);
                continue;
            }
            ActionInput input = c.Actions;
            // Taking up another item (or another in the same slot) takes a moment and ends what was going on.
            ushort inSlot = input.HeldSlot < PlayerInventory.BeltSlots ? p.Inventory.Belt[input.HeldSlot].Item : (ushort)0;
            if (input.HeldSlot != p.HeldSlot || inSlot != p.HeldItem)
            {
                p.HeldSlot = input.HeldSlot;
                p.HeldItem = inSlot;
                p.EquipReadyTick = Tick + CharacterTicks.From(content.Survival.EquipSeconds);
                p.StrikeTick = -1;
                p.DrawStartTick = -1;
                EquipGun(p);
            }
            ItemDef? held = p.Held(content);
            if (held?.Melee is { } melee)
            {
                StepMelee(entity, ref c, p, held, melee);
            }
            else if (held?.Bow is { } bow)
            {
                StepBow(entity, ref c, p, held, bow);
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

    /// <summary>The swing lands: a thick ray from the eye along the view, out to the tool's reach.</summary>
    private void Strike(Entity entity, ref Character c, Player p, ItemDef item, MeleeStats melee)
    {
        GameContent content = Content!;
        Vector3 up = Frame.UpAt(c.State.Position);
        Vector3 eye = EyeOf(c.State);
        Vector3 direction = CharacterMotor.ViewDirection(c.Input.Yaw, c.Input.Pitch, up);
        float rewind = Math.Min(c.Actions.ViewDelay / 4f, MaxRewindTicks);
        TraceHit hit = Trace(eye, direction, melee.Reach, SweepRadius, p.Id, Tick - rewind);
        if (!hit.Hit || hit.Target == HitTarget.Ground)
        {
            return;
        }
        switch (hit.Target)
        {
            case HitTarget.Character:
                if (PlayerOf(hit.Character) is { } victim)
                {
                    DamageCharacter(hit.Character, victim, melee.Damage, hit.Region, projectile: false, melee.Bleed, p, eye, item.Id);
                }
                break;
            case HitTarget.Natural:
                Gather(p, entity, hit.Natural, melee, hit.Point, eye);
                break;
            case HitTarget.Deployable:
                DamageDeployable(hit.Deployable!, melee.Structure);
                GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.HitConfirmed, Player = p.Id, Other = hit.Deployable!.Id, Target = HitTarget.Deployable, Amount = melee.Structure, Position = hit.Point });
                break;
            case HitTarget.Building:
            {
                float lost = DamageBuildingBy(hit.Building!, direction, melee.Structure, projectile: false, p, item.Id);
                GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.HitConfirmed, Player = p.Id, Target = HitTarget.Building, Amount = lost, Position = hit.Point });
                break;
            }
            case HitTarget.Ship:
            {
                float lost = DamageShip(hit.Ship!, melee.Damage, ShipDamage.Melee, p);
                GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.HitConfirmed, Player = p.Id, Other = hit.Ship!.Id, Target = HitTarget.Ship, Amount = lost, Position = hit.Point });
                break;
            }
            case HitTarget.Animal when hit.Animal is { } animal:
                // G7: a blow on an animal; a tool on a corpse harvests it (GROUND.md §6.9).
                if (animal.IsDead)
                {
                    HarvestAnimal(p, entity, animal, item, hit.Point);
                }
                else
                {
                    DamageAnimal(animal, melee.Damage * content.Survival.RegionMultiplier(hit.Region), hit.Region, eye, p, p.Id, p.Name, item.Id);
                }
                break;
        }
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
        GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.HitConfirmed, Player = p.Id, Target = HitTarget.Natural, Flag = result.WeakSpot, Position = result.Point });
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

    private void ProjectileHit(Projectile pr, in TraceHit hit)
    {
        AmmoStats ammo = pr.Ammo.Ammo!;
        GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.ProjectileEnded, Player = pr.Shooter, Projectile = pr.Id, Position = hit.Point, Target = hit.Target });
        TryFindPlayer(pr.Shooter, out _, out Player? shooter);
        switch (hit.Target)
        {
            case HitTarget.Character:
                if (PlayerOf(hit.Character) is { } victim)
                {
                    DamageCharacter(hit.Character, victim, pr.Damage, hit.Region, projectile: true, ammo.Bleed, shooter, pr.Position, pr.Ammo.Id);
                }
                return;
            case HitTarget.Deployable:
                DamageDeployable(hit.Deployable!, pr.Damage * ammo.Structure);
                if (shooter != null)
                {
                    GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.HitConfirmed, Player = shooter.Id, Other = hit.Deployable!.Id, Target = HitTarget.Deployable, Amount = pr.Damage * ammo.Structure, Position = hit.Point });
                }
                break;
            case HitTarget.Building:
            {
                float lost = DamageBuildingBy(hit.Building!, Vector3.Normalize(pr.Velocity), pr.Damage * ammo.Structure, projectile: true, shooter, pr.Ammo.Id);
                if (shooter != null)
                {
                    GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.HitConfirmed, Player = shooter.Id, Target = HitTarget.Building, Amount = lost, Position = hit.Point });
                }
                break;
            }
            case HitTarget.Ship:
            {
                float lost = DamageShip(hit.Ship!, pr.Damage, ShipDamage.Arrow, shooter);
                if (shooter != null)
                {
                    GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.HitConfirmed, Player = shooter.Id, Other = hit.Ship!.Id, Target = HitTarget.Ship, Amount = lost, Position = hit.Point });
                }
                return; // an arrow on a hull is lost
            }
            case HitTarget.Animal when hit.Animal is { } animal:
                // G7: an arrow in an animal stays in it; one in a corpse is lost too.
                if (!animal.IsDead)
                {
                    DamageAnimal(animal, pr.Damage * Content!.Survival.RegionMultiplier(hit.Region), hit.Region, pr.Position, shooter, pr.Shooter,
                        pr.ShooterName, pr.Ammo.Id);
                }
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
    /// Damage to a character from an attack: the body part's multiplier, then what is worn there takes its share
    /// (survival.json, items.json); bleeding starts in proportion. Tells the victim and the attacker.
    /// </summary>
    public float DamageCharacter(Entity victimEntity, Player victim, float damage, HitRegion region, bool projectile, float bleed,
        Player? attacker, Vector3 from, ushort weapon)
    {
        GameContent content = Content!;
        float amount = damage * content.Survival.RegionMultiplier(region) * (1 - victim.Inventory.Protection(content, region, projectile));
        if (amount <= 0 || victim.Dead)
        {
            return 0;
        }
        victim.Vitals.Bleeding += amount * bleed;
        bool wasDown = victim.Wounded;
        // Rust: a projectile to the head kills outright; anything else lethal may knock down instead (G4).
        float taken = Hurt(victimEntity, victim, amount, projectile ? "Shot" : "Struck down", attacker?.Id ?? EntityId.None, attacker?.Name ?? "",
            canWound: !(projectile && region == HitRegion.Head));
        GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.DamageTaken, Player = victim.Id, Other = attacker?.Id ?? EntityId.None, Amount = amount, Region = region, Position = from, Item = weapon });
        if (attacker != null)
        {
            GameEvents.Add(new ZoneEvent
            {
                Kind = ZoneEventKind.HitConfirmed, Player = attacker.Id, Other = victim.Id, Target = HitTarget.Character, Region = region,
                Amount = amount, Flag = victim.Dead, Downed = victim.Wounded && !wasDown, Position = from, Item = weapon,
            });
        }
        return taken;
    }

    /// <summary>
    /// The first thing a ray (thickened by <paramref name="thickness"/>) meets: a character as it was at
    /// <paramref name="viewTick"/> (interpolated from the history), a tree or outcrop, a deployable, or the ground.
    /// <paramref name="ignore"/> is the attacker.
    /// </summary>
    public TraceHit Trace(Vector3 origin, Vector3 direction, float length, float thickness, EntityId ignore, double viewTick, bool bullet = false)
    {
        var best = new TraceHit { Distance = -1 };
        void Consider(float t, HitTarget target, ref TraceHit candidate)
        {
            if (t >= 0 && (best.Distance < 0 || t < best.Distance))
            {
                candidate.Distance = t;
                candidate.Target = target;
                candidate.Point = origin + direction * t;
                best = candidate;
            }
        }

        // Characters, where the attacker saw them (those nowhere near now are skipped before their history is looked up).
        foreach (EntityId id in History.Characters)
        {
            if (id == ignore || (History.TryNewest(id, out Vector3 now) && Vector3.DistanceSquared(now, origin) > (length + 8f) * (length + 8f)) ||
                !History.Sample(id, viewTick, out HitPose pose) ||
                Vector3.DistanceSquared(pose.Feet, origin) > (length + 2.5f) * (length + 2.5f))
            {
                continue;
            }
            if (!TryFindPlayer(id, out Entity e, out Player p) || p.Dead)
            {
                continue;
            }
            float t = Hitboxes.Raycast(pose, origin, direction, length, thickness, out HitRegion region);
            var candidate = new TraceHit { Character = e, Region = region };
            Consider(t, HitTarget.Character, ref candidate);
        }

        // Animals (G7), where the attacker saw them; corpses as they lie.
        if (Animals != null && TraceAnimal(origin, direction, length, thickness, viewTick, out float animalAt, out Animals.Animal? animal, out HitRegion animalRegion))
        {
            var candidate = new TraceHit { Animal = animal, Region = animalRegion };
            Consider(animalAt, HitTarget.Animal, ref candidate);
        }

        // Trees and outcrops.
        Vector3 end = origin + direction * length;
        float margin = 3f;
        _traceNature.Clear();
        Nature!.Query(MathF.Min(origin.X, end.X) - margin, MathF.Min(origin.Z, end.Z) - margin,
            MathF.Max(origin.X, end.X) + margin, MathF.Max(origin.Z, end.Z) + margin, _traceNature);
        foreach (NaturalObject o in _traceNature)
        {
            NaturalKind? kind = Content!.Natural(o.Kind);
            if (kind == null || !kind.IsSolid || Natural!.IsDepleted(o.Id))
            {
                continue;
            }
            float t;
            if (kind.Class == NaturalClass.Tree)
            {
                (Vector3 a, Vector3 b, float r) = NaturalShapes.Trunk(o, kind);
                t = Geometry.RayCapsule(origin, direction, length, a, b, r + thickness);
            }
            else
            {
                (Vector3 centre, float r) = NaturalShapes.Rock(o, kind);
                t = Geometry.RaySphere(origin, direction, length, centre, r + thickness);
            }
            if (!kind.IsHarvested)
            {
                // A boulder: stops the swing or the arrow like the ground, and gives nothing.
                var stone = new TraceHit();
                Consider(t, HitTarget.Ground, ref stone);
                continue;
            }
            var candidate = new TraceHit { Natural = o };
            Consider(t, HitTarget.Natural, ref candidate);
        }

        // Deployables.
        _traceThings.Clear();
        DeployablesNear((origin + end) / 2, length / 2 + 2f, _traceThings);
        foreach (Deployable d in _traceThings)
        {
            (Vector3 right, Vector3 forward) = Geometry.Axes(d.Up, d.Yaw);
            float t = Geometry.RayBox(origin, direction, length, d.Centre, d.Def.HalfExtents + new Vector3(thickness), right, d.Up, forward);
            var candidate = new TraceHit { Deployable = d };
            Consider(t, HitTarget.Deployable, ref candidate);
        }

        // Ships' hulls (G6), as they are now.
        if (_ships.Count > 0)
        {
            float t = TraceShips(origin, direction, length, thickness, out Ships.Ship? ship);
            var candidate = new TraceHit { Ship = ship };
            Consider(t, HitTarget.Ship, ref candidate);
        }

        // Building pieces, doors and claim devices: walls stop swings, arrows and bullets.
        if (Building != null && TraceBuilding(origin, direction, best.Distance >= 0 ? best.Distance : length, out float wall, out object? part))
        {
            var candidate = new TraceHit { Building = part };
            Consider(wall, HitTarget.Building, ref candidate);
        }

        // The ground, which hides all behind it.
        float ground = bullet
            ? BulletGroundCrossing(origin, direction, best.Distance >= 0 ? best.Distance : length)
            : GroundCrossing(origin, direction, best.Distance >= 0 ? best.Distance : length);
        if (ground >= 0)
        {
            var candidate = new TraceHit();
            Consider(ground, HitTarget.Ground, ref candidate);
        }
        return best;
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
}
