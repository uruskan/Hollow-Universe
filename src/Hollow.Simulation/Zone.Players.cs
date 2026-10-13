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

// The survival game's players (GROUND.md §6): their lives, sleep, death into a corpse, and respawn at a bag or on the
// beach; and the order of the game's phases each tick.
public sealed partial class Zone
{
    private readonly List<ItemStack> _overflow = new();
    private readonly List<ItemAmount> _finished = new();
    private readonly List<Entity> _scratchEntities = new();
    private long _localIds = 1;

    /// <summary>The game's numbers; null in a movement-only zone (G1).</summary>
    public GameContent? Content { get; private set; }

    /// <summary>Where the trees, outcrops, pickups and bushes are, and what players did to them.</summary>
    public INaturalObjects? Nature { get; private set; }

    public NaturalState? Natural { get; private set; }

    /// <summary>The air temperature (the stand-in until the planet package's climate).</summary>
    public IClimate? Climate { get; set; }

    /// <summary>The real clock (Unix ms), set by the server before every step: respawn timers and bag cooldowns.</summary>
    public long UnixMilliseconds { get; set; }

    /// <summary>Hands out IDs for new persistent entities (the server's allocator); a counter if not set.</summary>
    public Func<EntityId>? NewEntityId { get; set; }

    /// <summary>A beach point for a respawn (the server sets the planet's real coast, <see cref="Simulation.Beaches"/>); the stand-in ring if not set.</summary>
    public Func<ulong, Vector3>? Beaches { get; set; }

    /// <summary>One second of poses of every character, for hits as the attacker saw them.</summary>
    public HitboxHistory History { get; } = new();

    /// <summary>What happened this tick that players should hear about; the server sends and clears it.</summary>
    public List<ZoneEvent> GameEvents { get; } = new();

    private void InitGame(GameContent content)
    {
        Content = content;
        Nature = Spec.CreateNature(Surface, Frame, content);
        Natural = new NaturalState(content, Nature);
        _ = new NaturalBodies(Physics, Natural);
        Climate = new StandInClimate(content.Survival, Frame, Surface.Radius);
        InitBuilding(content);
        InitAnimals(content);
    }

    // After movement: GROUND.md §4.2's order.
    private void StepGame()
    {
        GameContent content = Content!;
        TakeFallDamage();
        StepActions();
        StepProjectiles();
        StepBullets();
        StepExplosives();
        StepWounded();
        StepAnimals();
        if (Tick % CharacterMotor.TickRate == 0)
        {
            StepVitals();
            Natural!.Respawn(UnixMilliseconds);
            StepDespawns();
            CheckLoot();
        }
        StepDevices();
        StepCrafting(content);
        StepBuilding();
        RecordHistory();
    }

    // ---------------------------------------------------------------- players

    /// <summary>The survival state of a character, or null (not a player, or a G1 zone).</summary>
    public Player? PlayerOf(Entity entity) => Entities.Has<PlayerLink>(entity) ? Entities.Get<PlayerLink>(entity).Player : null;

    public bool TryFindPlayer(EntityId id, out Entity entity, out Player player)
    {
        player = null!;
        if (Entities.TryFind(id, out entity) && PlayerOf(entity) is { } p)
        {
            player = p;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Adds a player's character: <paramref name="player"/> restored from storage, or a new life (spawn vitals and the
    /// respawn kit) when null. A dead one is added dead, waiting for a respawn.
    /// </summary>
    public Entity AddPlayer(EntityId id, string name, in CharacterState state, Player? player = null)
    {
        Entity entity = AddCharacter(id, name, state);
        if (player == null)
        {
            player = new Player(id, name);
            StartLife(player);
        }
        Entities.Add(entity, new PlayerLink { Player = player });
        ref Character c = ref Entities.Get<Character>(entity);
        if (player.Dead)
        {
            c.State.Health = 0;
            c.State.Flags = MoveFlags.Dead;
        }
        else if (player.Wounded)
        {
            // Saved while down (a server stopped): the player was gone meanwhile, and a wounded player who is gone dies.
            player.Wounded = false;
            Die(entity, player, player.WoundedCause.Length > 0 ? player.WoundedCause : "Bled out", player.WoundedBy, player.WoundedByName);
        }
        else
        {
            SyncHealth(ref c, player);
        }
        return entity;
    }

    /// <summary>A fresh life: spawn vitals (survival.json) and the respawn kit (a rock and a torch).</summary>
    private void StartLife(Player p)
    {
        SurvivalTuning s = Content!.Survival;
        p.Dead = false;
        p.Vitals = new Vitals
        {
            Health = s.SpawnHealth,
            Calories = s.SpawnCalories,
            Hydration = s.SpawnHydration,
            Temperature = s.ClimateMeanCelsius,
        };
        p.HeldSlot = ActionInput.NoSlot;
        p.StrikeTick = -1;
        p.DrawStartTick = -1;
        p.VitalsRevision++;
        ResetGun(p);
        p.Wounded = false;
        if (Content.Loot(s.RespawnKitKey) is { } kit)
        {
            var rng = new Rng(Hash64.Of(p.Id.Value, (ulong)Tick));
            // On the belt, as in Rust: a new life starts with the rock and the torch in the hotbar.
            var beltFirst = new[] { p.Inventory.Belt, p.Inventory.Main };
            foreach (ItemAmount a in kit.Roll(ref rng))
            {
                InventoryRules.AddTo(Content, beltFirst, ItemStack.Of(a.Item, a.Count));
            }
        }
    }

    /// <summary>A player logged off: their character stays, asleep, lootable and killable (GROUND.md §6).</summary>
    public void Sleep(Entity entity)
    {
        if (PlayerOf(entity) is not { } p)
        {
            return;
        }
        if (p.Wounded)
        {
            // Down and gone: Rust's wounded who log off die.
            Die(entity, p, p.WoundedCause.Length > 0 ? p.WoundedCause : "Bled out", p.WoundedBy, p.WoundedByName);
        }
        p.Sleeping = true;
        p.Looting = EntityId.None;
        p.LootRevision++;
        p.StrikeTick = -1;
        p.DrawStartTick = -1;
        ref Character c = ref Entities.Get<Character>(entity);
        c.Input = new MoveInput { Yaw = c.Input.Yaw };
        c.Actions = default;
    }

    /// <summary>The player logged back in: the sleeper wakes.</summary>
    public void Wake(Entity entity)
    {
        if (PlayerOf(entity) is { } p)
        {
            p.Sleeping = false;
            p.VitalsRevision++;
            // A new connection numbers its gun actions from 1 again, and takes up what it holds afresh.
            ResetGun(p);
            p.Gun.LastSeq = 0;
            p.GunRefusedSeq = 0;
            p.HeldSlot = ActionInput.NoSlot;
            p.HeldItem = 0;
        }
    }

    /// <summary>Health as the character's movement state carries it (whole points; 0 is dead).</summary>
    private static void SyncHealth(ref Character c, Player p)
    {
        c.State.Health = (byte)Math.Clamp(Math.Ceiling(p.Vitals.Health), 0, 255);
        if (c.State.Health == 0 && !p.Dead)
        {
            c.State.Health = 1; // death happens in Hurt, which sets it to 0
        }
    }

    // Fall damage was taken off the movement state by the motor: take it off the vitals too.
    private void TakeFallDamage()
    {
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        ReadOnlySpan<int> slots = players.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            Entity entity = Entities.AtSlot(slots[i]);
            Player p = players.Values[i].Player;
            ref Character c = ref Entities.Get<Character>(entity);
            if (c.Events.FallDamage > 0 && !p.Dead)
            {
                Hurt(entity, p, c.Events.FallDamage, "Fell", EntityId.None, "");
            }
        }
    }

    /// <summary>
    /// Takes health; at 0 the character dies, or is knocked down (wounded, G4) when <paramref name="canWound"/> and the
    /// hit is small enough (Zone.Wounded.cs). Returns the health taken.
    /// </summary>
    public float Hurt(Entity entity, Player p, float amount, string cause, EntityId by, string byName, bool canWound = true)
    {
        if (p.Dead || amount <= 0)
        {
            return 0;
        }
        float taken = Math.Min(amount, p.Vitals.Health);
        p.Vitals.Health -= amount;
        p.VitalsRevision++;
        ref Character c = ref Entities.Get<Character>(entity);
        if (p.Vitals.Health <= 0)
        {
            if (canWound && CanBeWounded(p, amount))
            {
                Wound(entity, p, cause, by, byName);
            }
            else
            {
                Die(entity, p, cause, by, byName);
            }
        }
        else
        {
            SyncHealth(ref c, p);
        }
        return taken;
    }

    /// <summary>
    /// A death (GROUND.md §6): everything the character carried and its crafting queue's ingredients go into a corpse
    /// where it fell, which anyone may loot until it despawns; the character waits, dead, for a respawn.
    /// </summary>
    public void Die(Entity entity, Player p, string cause, EntityId by, string byName)
    {
        if (p.Dead)
        {
            return;
        }
        GameContent content = Content!;
        ref Character c = ref Entities.Get<Character>(entity);
        // The body keeps the player's layout (main 0–23, belt 24–29, clothing 30–36, as the loot panel shows a body);
        // the crafting queue's ingredients fill empty main slots, then go after the clothing.
        var stacks = new List<ItemStack>();
        foreach (Container part in new[] { p.Inventory.Main, p.Inventory.Belt, p.Inventory.Wear })
        {
            for (int i = 0; i < part.Size; i++)
            {
                stacks.Add(part[i]);
            }
        }
        foreach (ItemStack refund in p.Crafting.TakeAll(content))
        {
            int free = stacks.FindIndex(0, PlayerInventory.MainSlots, s => s.IsEmpty);
            if (free >= 0)
            {
                stacks[free] = refund;
            }
            else
            {
                stacks.Add(refund);
            }
        }
        bool any = stacks.Exists(s => !s.IsEmpty);
        p.Inventory.Clear();
        if (any)
        {
            Vector3 at = Settle(c.State.Position);
            var corpse = new Corpse(NextId(), p.Name, p.Id, stacks.Count,
                at, Frame.UpAt(at), c.Input.Yaw, Tick + (long)(content.Survival.CorpseSeconds * CharacterMotor.TickRate));
            for (int i = 0; i < stacks.Count; i++)
            {
                corpse.Loot.Set(i, stacks[i]);
            }
            AddStatic(corpse);
        }
        p.Dead = true;
        p.Wounded = false;
        ResetGun(p);
        p.DeathCause = cause;
        p.KilledBy = byName;
        p.Vitals = default;
        p.Looting = EntityId.None;
        p.LootRevision++;
        p.StrikeTick = -1;
        p.DrawStartTick = -1;
        p.VitalsRevision++;
        c.State.Health = 0;
        c.State.Velocity = Vector3.Zero;
        c.State.Flags = MoveFlags.Dead;
        History.Forget(p.Id);
        GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.Died, Player = p.Id, Other = by, Text = cause, Position = c.State.Position });
    }

    /// <summary>
    /// A respawn (GROUND.md §6): at one of the player's own sleeping bags (<paramref name="bag"/>) once it has cooled
    /// down, or on a random beach point (none), with a rock and a torch.
    /// </summary>
    public ActionResult Respawn(Entity entity, EntityId bag)
    {
        if (PlayerOf(entity) is not { } p)
        {
            return ActionResult.Invalid;
        }
        if (!p.Dead)
        {
            return ActionResult.Dead;
        }
        Vector3 at;
        float yaw = 0;
        if (!bag.IsNone)
        {
            if (!Entities.TryFind(bag, out Entity bagEntity) || StaticOf(bagEntity) is not Deployable d || !d.Def.IsRespawnPoint)
            {
                return ActionResult.NotFound;
            }
            if (d.Owner != p.Id)
            {
                return ActionResult.NotAllowed;
            }
            if (UnixMilliseconds < d.ReadyAtUnixMs)
            {
                return ActionResult.Cooldown;
            }
            d.ReadyAtUnixMs = UnixMilliseconds + (long)(d.Def.RespawnSeconds * 1000);
            d.Revision++;
            (Vector3 forward, _) = CharacterMotor.HorizontalBasis(d.Yaw, d.Up);
            at = Settle(d.Position + forward * 0.3f) + d.Up * 0.05f;
            yaw = d.Yaw;
        }
        else
        {
            if (UnixMilliseconds < p.BeachReadyAtUnixMs)
            {
                return ActionResult.Cooldown;
            }
            p.BeachReadyAtUnixMs = UnixMilliseconds + (long)(Content!.Survival.BeachCooldownSeconds * 1000);
            at = BeachPoint(Hash64.Of(p.Id.Value, (ulong)Tick, 0xBEAC));
        }
        StartLife(p);
        ref Character c = ref Entities.Get<Character>(entity);
        c.State = NewCharacter(at);
        c.Input = new MoveInput { Yaw = yaw };
        c.Actions = default;
        SyncHealth(ref c, p);
        Physics.Prefetch(at, 1);
        Physics.EnsureGround(at);
        return ActionResult.Accepted;
    }

    /// <summary>A random beach point: the planet's coast (<see cref="Beaches"/>), or on the stand-in ground a ring 15–60 m around the origin.</summary>
    public Vector3 BeachPoint(ulong salt)
    {
        if (Beaches != null)
        {
            return Beaches(salt);
        }
        var rng = new Rng(Hash64.Of(salt, 0xBEAC4));
        double angle = rng.NextDouble() * DMath.TwoPi;
        double radius = rng.Range(15, 60);
        Vector3 ground = GroundAt((float)(radius * DMath.Cos(angle)), (float)(radius * DMath.Sin(angle)));
        return ground + Frame.UpAt(ground) * 0.05f;
    }

    /// <summary>A new persistent entity ID.</summary>
    public EntityId NextId() => NewEntityId?.Invoke() ?? new EntityId(0x7000_0000_0000_0000UL + (ulong)_localIds++);

    /// <summary>The point on the ground below (or above) a point.</summary>
    public Vector3 Settle(Vector3 point)
    {
        Vector3 ground = GroundAt(point.X, point.Z);
        return ground;
    }

    /// <summary>How far a point is above the ground, in metres (negative below).</summary>
    public float HeightAboveGround(Vector3 point)
    {
        Vector3D body = Frame.ToBody(point);
        double r = body.Length;
        return (float)(r - (Surface.Radius + Surface.HeightAt(body / r)));
    }

    /// <summary>The ground's up and its slope (radians) under a point, from the surface by finite differences.</summary>
    public (Vector3 Normal, float Slope) GroundNormal(Vector3 point)
    {
        const float d = 0.35f;
        Vector3 a = GroundAt(point.X - d, point.Z), b = GroundAt(point.X + d, point.Z);
        Vector3 c = GroundAt(point.X, point.Z - d), e = GroundAt(point.X, point.Z + d);
        Vector3 normal = Vector3.Normalize(Vector3.Cross(e - c, b - a));
        Vector3 up = Frame.UpAt(point);
        if (Vector3.Dot(normal, up) < 0)
        {
            normal = -normal;
        }
        float cos = Math.Clamp(Vector3.Dot(normal, up), -1, 1);
        return (normal, MathF.Acos(cos));
    }

    /// <summary>The eye of a character now.</summary>
    public Vector3 EyeOf(in CharacterState state) => state.Position + Frame.UpAt(state.Position) * Tuning.EyeHeight(state.Stance);

    // ---------------------------------------------------------------- crafting

    private void StepCrafting(GameContent content)
    {
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        ReadOnlySpan<int> slots = players.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            Player p = players.Values[i].Player;
            if (!p.IsAwakeAndAlive || p.Crafting.IsEmpty)
            {
                continue;
            }
            _overflow.Clear();
            _finished.Clear();
            p.Crafting.Advance(content, p.Inventory, _overflow, _finished);
            foreach (ItemAmount a in _finished)
            {
                GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.CraftFinished, Player = p.Id, Item = a.Item.Id, Count = a.Count });
                GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.ItemsGained, Player = p.Id, Item = a.Item.Id, Count = a.Count, Reason = GainReason.Crafted });
            }
            if (_overflow.Count > 0)
            {
                Entity entity = Entities.AtSlot(slots[i]);
                DropAll(entity, _overflow);
            }
        }
    }

    // ---------------------------------------------------------------- history

    private void RecordHistory()
    {
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        ReadOnlySpan<int> slots = players.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            Player p = players.Values[i].Player;
            if (p.Dead)
            {
                continue;
            }
            ref Character c = ref Entities.Get<Character>(Entities.AtSlot(slots[i]));
            History.Record(p.Id, Tick, new HitPose
            {
                Feet = c.State.Position,
                Up = Frame.UpAt(c.State.Position),
                Yaw = c.Input.Yaw,
                Pose = p.Sleeping || p.Wounded ? Pose.Lying : c.State.Stance == Stance.Crouching ? Pose.Crouching : Pose.Standing,
            });
        }
    }
}
