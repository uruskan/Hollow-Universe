using System.Collections.Generic;
using System.Numerics;
using System;
using Hollow.Core;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

// The survival game's players (GROUND.md §6): their lives, sleep, death into a corpse, and respawn at a bag or on the
// beach; and the order of the game's phases each tick.

// (from Zone.Admin.cs)
// What an admin's F1 console does to the zone (GROUND.md §8.2): world props and deployables placed by hand, and god mode
// and noclip on a character.
public sealed class PlayerSystem : ZoneSystem
{
    private SupportSystem? _supportsSystem;
    private SupportSystem Supports => _supportsSystem ??= Zone.Require<SupportSystem>();
    private ThingSystem? _thingsSystem;
    private ThingSystem Things => _thingsSystem ??= Zone.Require<ThingSystem>();
    private WorldSystem? _worldSystem;
    private WorldSystem World => _worldSystem ??= Zone.Require<WorldSystem>();
    private WoundedSystem? _woundedSystem;
    private WoundedSystem Wounded => _woundedSystem ??= Zone.Require<WoundedSystem>();

    internal readonly List<Entity> _scratchEntities = new();
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
        SurvivalTuning s = Content.Survival;
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
        GunSystem.ResetGun(p);
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
            GunSystem.ResetGun(p);
            p.Gun.LastSeq = 0;
            p.GunRefusedSeq = 0;
            p.HeldSlot = ActionInput.NoSlot;
            p.HeldItem = 0;
        }
    }

    /// <summary>Health as the character's movement state carries it (whole points; 0 is dead).</summary>
    internal static void SyncHealth(ref Character c, Player p)
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
        if (p.Dead || amount <= 0 || Entities.Get<Character>(entity).State.Has(MoveCheats.God))
        {
            // An admin's god mode (GROUND.md §8.2): nothing hurts.
            return 0;
        }
        float taken = Math.Min(amount, p.Vitals.Health);
        p.Vitals.Health -= amount;
        p.VitalsRevision++;
        ref Character c = ref Entities.Get<Character>(entity);
        if (p.Vitals.Health <= 0)
        {
            if (canWound && Wounded.CanBeWounded(p, amount) && !Entities.Has<Ships.ShipSeat>(entity))
            {
                Wounded.Wound(entity, p, cause, by, byName);
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
        GameContent content = Content;
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
                at, Frame.UpAt(at), c.Input.Yaw, content.Survival.BodyLifetime, Tick + content.Survival.BodyLifetime.Ticks);
            for (int i = 0; i < stacks.Count; i++)
            {
                corpse.Loot.Set(i, stacks[i]);
            }
            Things.AddStatic(corpse);
        }
        p.Dead = true;
        p.Wounded = false;
        GunSystem.ResetGun(p);
        p.DeathPoint = c.State.Position;
        p.MapRevision++;
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
        Supports.SupportGone(p.Ref);
        Events.Add(new Died(Player: p.Id, By: by, Cause: cause, Position: c.State.Position));
    }

    /// <summary>
    /// A respawn (GROUND.md §6): at one of the player's own sleeping bags (<paramref name="bag"/>) once it has cooled
    /// down, or on a random beach point (none), with a rock and a torch.
    /// </summary>
    public Outcome Respawn(Entity entity, EntityId bag) => Respawn(entity, bag, null);

    /// <summary>
    /// A respawn as <see cref="Respawn(Entity, EntityId)"/>, on the beach point <paramref name="beach"/> when it is
    /// given: the server picks it (<see cref="RespawnPoint"/>) when the player asks, has its ground built on worker
    /// threads and brings the player in on the tick it is ready (GROUND.md §4.6).
    /// </summary>
    public Outcome Respawn(Entity entity, EntityId bag, Vector3? beach)
    {
        using TickProfile.Scope timed = Profile.Time(ProfileSection.Respawn);
        if (PlayerOf(entity) is not { } p)
        {
            return OutcomeCode.Invalid;
        }
        if (!p.Dead)
        {
            return OutcomeCode.Dead;
        }
        Vector3 at;
        float yaw = 0;
        if (!bag.IsNone)
        {
            if (!Entities.TryFind(bag, out Entity bagEntity) || Things.StaticOf(bagEntity) is not Deployable d || !d.Def.IsRespawnPoint)
            {
                return OutcomeCode.NotFound;
            }
            if (d.Owner != p.Id)
            {
                return OutcomeCode.NotOwner;
            }
            if (UnixMilliseconds < d.ReadyAtUnixMs)
            {
                return OutcomeCode.Cooldown;
            }
            d.ReadyAtUnixMs = UnixMilliseconds + (long)(d.Def.RespawnSeconds * 1000);
            d.Revision++;
            at = BagPoint(d);
            yaw = d.Yaw;
        }
        else
        {
            if (UnixMilliseconds < p.BeachReadyAtUnixMs)
            {
                return OutcomeCode.Cooldown;
            }
            p.BeachReadyAtUnixMs = UnixMilliseconds + (long)(Content.Survival.BeachCooldownSeconds * 1000);
            at = beach ?? BeachPoint(Hash64.Of(p.Id.Value, (ulong)Tick, 0xBEAC));
        }
        StartLife(p);
        ref Character c = ref Entities.Get<Character>(entity);
        MoveCheats cheats = c.State.Cheats; // an admin's god mode and noclip last through a death
        c.State = NewCharacter(at);
        c.State.Cheats = cheats;
        c.Input = new MoveInput { Yaw = yaw };
        c.Actions = default;
        SyncHealth(ref c, p);
        Physics.Prefetch(at, 1);
        Physics.EnsureGround(at);
        return OutcomeCode.Done;
    }

    /// <summary>
    /// Where a respawn asked for now would bring the player in: in front of the bag, or the beach point this tick
    /// picks; null if there is no such player or bag (the respawn will be refused).
    /// </summary>
    public Vector3? RespawnPoint(Entity entity, EntityId bag)
    {
        if (PlayerOf(entity) is not { } p)
        {
            return null;
        }
        if (bag.IsNone)
        {
            return BeachPoint(Hash64.Of(p.Id.Value, (ulong)Tick, 0xBEAC));
        }
        return Entities.TryFind(bag, out Entity bagEntity) && Things.StaticOf(bagEntity) is Deployable { Def.IsRespawnPoint: true } d ? BagPoint(d) : null;
    }

    // In front of a sleeping bag, on the ground.
    private Vector3 BagPoint(Deployable d)
    {
        (Vector3 forward, _) = CharacterMotor.HorizontalBasis(d.Yaw, d.Up);
        return Settle(d.Position + forward * 0.3f) + d.Up * 0.05f;
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

    // ---------------------------------------------------------------- crafting

    // ---------------------------------------------------------------- history


    // ---------------------------------------------------------------- from Zone.Admin.cs

    // Props an admin placed: gone for good once broken or emptied (Zone.World.cs, PropGone).
    internal readonly HashSet<ulong> _oneOffProps = new();

    /// <summary>
    /// A world prop placed by an admin: a barrel, a crate or a recycler, with fresh loot, on the ground at a point and
    /// turned by <paramref name="yaw"/>. To everything else it is one of the world's (hits, looting, replication); broken
    /// or emptied it doesn't come back, and a restart doesn't keep it (its spot isn't one the world generates).
    /// </summary>
    public WorldProp SpawnProp(PropDef def, Vector3 ground, float yaw)
    {
        var spot = new LootSpot(NextId().Value, def, ground, Frame.UpAt(ground), yaw, LootSite.Open);
        World._spots[spot.Id] = spot;
        _oneOffProps.Add(spot.Id);
        return World.Spawn(spot, null);
    }

    /// <summary>
    /// A deployable placed by an admin, without the placing rules (reach, slope, room): on the ground at a point, turned
    /// by <paramref name="yaw"/>, owned by <paramref name="owner"/> (whose bag it is), a bag or bed ready at once.
    /// </summary>
    public Deployable SpawnDeployable(DeployableDef def, Vector3 ground, float yaw, EntityId owner)
    {
        var placed = new Deployable(NextId(), def, ground, Frame.UpAt(ground), yaw, owner);
        Things.AddStatic(placed);
        return placed;
    }


    protected internal override void Step()
    {
        TakeFallDamage();
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class PlayerSystemAccess
{
    public static PlayerSystem Players(this Zone zone) => zone.Require<PlayerSystem>();
}
