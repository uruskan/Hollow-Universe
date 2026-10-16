using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>
/// The survival game on the server (GROUND.md §4.2, §6): players' intents (inventory, crafting, interaction, closing,
/// respawn; each from the player's budget, answered in order) applied to the zone, each player's own state (containers,
/// what they loot, vitals, crafting, life, the gun in hand) sent to them alone when it changed, what others wear sent
/// to those who see them, the zone's events routed to whom they concern, the respawns that wait for their ground, the
/// kits, and the counters of what happened.
/// </summary>
public sealed class SurvivalModule : ServerModule
{
    private readonly Dictionary<Container, List<(PlayerSession Session, ContainerRef Which)>> _watchers = new();
    private readonly List<int> _dirty = new();
    private readonly List<SlotNet> _slots = new();

    /// <summary>What happened in the game so far, for the load test and <c>status</c>.</summary>
    public GameCounters Counters { get; } = new();

    /// <summary>What of the player's own state was sent: the revisions, and whether their containers went in full.</summary>
    private sealed class OwnState
    {
        public bool ContainersSent;
        public uint Loot = uint.MaxValue;
        public uint Vitals = uint.MaxValue;
        public uint Craft = uint.MaxValue;
        public bool? Dead;

        /// <summary>Whether the player was down (wounded) when their life was last sent.</summary>
        public bool? Wounded;

        public uint Gun = uint.MaxValue;
    }

    /// <summary>What each relevant character was last told to wear (a hash of the items).</summary>
    private sealed class Looks
    {
        public readonly Dictionary<uint, ulong> Sent = new();
    }

    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        // Every intent spends from the player's budget (a bad one too), is applied to the zone, and is answered after
        // this tick's changes have gone out.
        handlers.On<MoveItemIntent>((PlayerSession session, in MoveItemIntent message) =>
        {
            MoveItemIntent m = message;
            Entity me = session.Entity;
            Intent(session, m.Seq, () => Zone.Actions().MoveItem(me, m.From, m.FromSlot, m.To, m.ToSlot, Amount(m.Amount), Expected(m.Expected)));
        });
        handlers.On<QuickMoveIntent>((PlayerSession session, in QuickMoveIntent message) =>
        {
            QuickMoveIntent m = message;
            Entity me = session.Entity;
            Intent(session, m.Seq, () => Zone.Actions().QuickMove(me, m.From, m.Slot, Expected(m.Expected)));
        });
        handlers.On<DropIntent>((PlayerSession session, in DropIntent message) =>
        {
            DropIntent m = message;
            Entity me = session.Entity;
            Intent(session, m.Seq, () => Zone.Actions().DropItem(me, m.From, m.Slot, Amount(m.Amount), Expected(m.Expected)));
        });
        handlers.On<ItemActionIntent>((PlayerSession session, in ItemActionIntent message) =>
        {
            ItemActionIntent m = message;
            Entity me = session.Entity;
            Intent(session, m.Seq, () => Zone.Actions().ItemAction(me, m.From, m.Slot, Expected(m.Expected), Amount(m.Action), Amount(m.Arg)));
        });
        handlers.On<CraftIntent>((PlayerSession session, in CraftIntent message) =>
        {
            CraftIntent m = message;
            Entity me = session.Entity;
            Intent(session, m.Seq, () => Zone.Actions().Craft(me, Amount(m.Recipe), Amount(m.Count), out _));
        });
        handlers.On<CancelCraftIntent>((PlayerSession session, in CancelCraftIntent message) =>
        {
            CancelCraftIntent m = message;
            Entity me = session.Entity;
            Intent(session, m.Seq, () => Zone.Actions().CancelCraft(me, m.Ticket));
        });
        handlers.On<InteractIntent>((PlayerSession session, in InteractIntent message) =>
        {
            // GROUND_SYSTEMS.md §5: every verb, one path (Zone.Interaction().Interact).
            InteractIntent m = message;
            Entity me = session.Entity;
            Intent(session, m.Seq, () => Zone.Interaction().Interact(me, NetConvert.ToRef(m.Target), (int)Math.Min(m.Verb, int.MaxValue), m.Arg, m.Code, m.Text));
        });
        handlers.On<CloseIntent>((PlayerSession session, in CloseIntent message) =>
        {
            CloseIntent m = message;
            Entity me = session.Entity;
            Intent(session, m.Seq, () => Zone.Actions().Close(me));
        });
        handlers.On<RespawnIntent>((PlayerSession session, in RespawnIntent message) =>
        {
            RespawnIntent m = message;
            Entity me = session.Entity;
            session.State<OwnState>().Dead = null; // the answer comes with fresh respawn choices
            var bag = new EntityId(m.Bag);
            // The ground where the player comes in is built first, on worker threads (GROUND.md §4.6): the
            // respawn happens on the tick it is ready (FinishRespawns), on the beach point picked now.
            if (session.Playing && Zone.Players().RespawnPoint(me, bag) is { } at && !Zone.Physics.GroundReady(at))
            {
                if (TakeIntent(session, out Outcome refused))
                {
                    _respawns.Add(new PendingRespawn(session, me, m.Seq, bag, at, Zone.Tick));
                }
                else
                {
                    Answer(session, m.Seq, refused);
                }
                return;
            }
            Intent(session, m.Seq, () => RespawnNow(me, bag, null), whileDead: true);
        });

        // What happens in the zone, to whom it concerns, as it happens (GROUND_SYSTEMS.md §12.3): one subscription per
        // kind of event, made once at start; the handlers run on the tick thread, inside the zone's step.
        ZoneEvents events = Zone.Events;
        Counters.Listen(events);
        events.On<HitConfirmed>((in HitConfirmed e) =>
        {
            if (SessionOf(e.By) is { } to)
            {
                Writer.Reset();
                new HitMarker
                {
                    Weapon = e.Weapon,
                    ActionTick = Zone.Players().PlayerOf(to.Entity)?.LastActionTick ?? 0,
                    PointX = e.Point.X,
                    PointY = e.Point.Y,
                    PointZ = e.Point.Z,
                    Target = e.Target.Family,
                    Region = e.Region,
                    Damage = Tenths(e.Amount),
                    Killed = e.Target.Family is TargetFamily.Character or TargetFamily.Animal && e.Flag,
                    WeakSpot = e.Target.Family == TargetFamily.Natural && e.Flag,
                    Downed = e.Downed,
                }.WriteMessage(Writer);
                Send(to);
            }
        });
        events.On<Hurt>((in Hurt e) =>
        {
            if (SessionOf(e.Player) is { } to)
            {
                Writer.Reset();
                new DamageTaken { Damage = Tenths(e.Amount), Region = e.Region, FromX = e.From.X, FromY = e.From.Y, FromZ = e.From.Z, Weapon = e.Weapon }
                    .WriteMessage(Writer);
                Send(to);
            }
        });
        events.On<ItemsGained>((in ItemsGained e) =>
        {
            if (SessionOf(e.Player) is { } to)
            {
                Writer.Reset();
                new ItemsChanged { Item = e.Item, Count = e.Count, Reason = e.Reason, WeakSpot = e.WeakSpot }.WriteMessage(Writer);
                Send(to);
            }
        });
        events.On<Crafted>((in Crafted e) =>
        {
            if (SessionOf(e.Player) is { } to)
            {
                Writer.Reset();
                new CraftFinished { Item = e.Item, Count = (uint)Math.Max(0, e.Count) }.WriteMessage(Writer);
                Send(to);
            }
        });
        events.On<Died>((in Died e) =>
        {
            Log($"{NameOf(e.Player)} died: {e.Cause}" + (e.By.IsNone ? "" : $", by {NameOf(e.By)}"));
            if (Persistence != null)
            {
                // Deaths commit at once (ARCHITECTURE.md §13): within a second, several deaths in one save.
                Server.SaveSoon();
            }
        });
        events.On<ProjectileLaunched>((in ProjectileLaunched e) =>
        {
            uint shooter = Zone.Entities.TryFind(e.Shooter, out Entity s) && Zone.Entities.Has<NetPresence>(s) ? Zone.Entities.Get<NetPresence>(s).NetId : 0;
            Writer.Reset();
            new ProjectileLaunch
            {
                Id = e.Projectile, Shooter = shooter, Ammo = e.Ammo, X = e.Position.X, Y = e.Position.Y, Z = e.Position.Z,
                VelocityX = e.Velocity.X, VelocityY = e.Velocity.Y, VelocityZ = e.Velocity.Z, Seq = e.Seq, Silenced = e.Silenced && e.Seq != 0,
                Battery = (byte)Math.Clamp(e.Battery, 0, 15),
            }.WriteMessage(Writer);
            // Bullets are cosmetic to others and many: unreliable (ARCHITECTURE.md §11.3); arrows and batteries' rockets stay reliable.
            SendNear(e.Position, e.Seq != 0 && e.Battery == 0 ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered);
        });
        events.On<ProjectileEnded>((in ProjectileEnded e) =>
        {
            Writer.Reset();
            new ProjectileEnd { Id = e.Projectile, X = e.Position.X, Y = e.Position.Y, Z = e.Position.Z, Target = e.Target.Family }.WriteMessage(Writer);
            SendNear(e.Position, e.Seq != 0 ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered);
        });
        events.On<Notice>((in Notice e) => Notify(e.Player, e.Text));
        events.On<WentDown>((in WentDown e) => Log($"{NameOf(e.Player)} is down: {e.Cause}" + (e.By.IsNone ? "" : $", by {NameOf(e.By)}")));
        events.On<GotUp>((in GotUp e) => Log($"{NameOf(e.Player)} is up again" + (e.Helper.IsNone ? "" : $", helped by {NameOf(e.Helper)}")));
    }

    /// <summary>A new character gets the join kit (the load test's full inventories, <see cref="ServerOptions.JoinKit"/>).</summary>
    protected internal override void Joined(PlayerSession session, bool newCharacter)
    {
        if (newCharacter && Options.JoinKit != null && Content.Loot(Options.JoinKit) is { } kit && Zone.Players().PlayerOf(session.Entity) is { } fresh)
        {
            GiveKit(fresh, kit);
        }
    }

    protected internal override void BeforeStep() => FinishRespawns();

    // ---------------------------------------------------------------- respawns waiting for their ground

    /// <summary>A respawn waits at most this long for its ground, then builds what is missing itself.</summary>
    private const int RespawnWaitTicks = PlayerSession.ArrivalWaitTicks;

    private sealed record PendingRespawn(PlayerSession Session, Entity Player, uint Seq, EntityId Bag, Vector3 At, long Since);

    private readonly List<PendingRespawn> _respawns = new();

    private Outcome RespawnNow(Entity me, EntityId bag, Vector3? beach)
    {
        Outcome result = Zone.Players().Respawn(me, bag, beach);
        if (result == OutcomeCode.Done && Options.KitEveryLife && Options.JoinKit != null &&
            Content.Loot(Options.JoinKit) is { } kit && Zone.Players().PlayerOf(me) is { } p)
        {
            GiveKit(p, kit);
        }
        return result;
    }

    // Respawns whose ground is ready (or that waited long enough) happen now, in the order they were asked for.
    private void FinishRespawns()
    {
        for (int i = 0; i < _respawns.Count; i++)
        {
            PendingRespawn r = _respawns[i];
            bool gone = !r.Session.Playing || r.Session.Entity != r.Player;
            if (!gone && !Zone.Physics.GroundReady(r.At) && Zone.Tick - r.Since < RespawnWaitTicks)
            {
                continue;
            }
            _respawns.RemoveAt(i--);
            if (!gone)
            {
                Answer(r.Session, r.Seq, RespawnNow(r.Player, r.Bag, r.At));
            }
        }
    }

    /// <summary>Gives a loot table's items to a player (admin kits, the load test); what doesn't fit is left out.</summary>
    public void GiveKit(Player p, LootTable table)
    {
        var rng = new Rng(Hash64.Of((ulong)Zone.Tick, p.Id.Value, 0x417));
        foreach (ItemAmount a in table.Roll(ref rng))
        {
            p.Inventory.Give(Content, a.Item, a.Count);
        }
    }

    // ---------------------------------------------------------------- after each step

    protected internal override void AfterStep()
    {
        ReportFalls();
        foreach (PlayerSession session in Players)
        {
            if (session.Playing && Zone.Players().PlayerOf(session.Entity) is { } p)
            {
                SendOwnState(session, p);
            }
        }
        SendContainerChanges();
    }

    private void ReportFalls()
    {
        foreach (PlayerSession session in Players)
        {
            if (session.Playing && Zone.Entities.Get<Character>(session.Entity).Events.FallDamage > 0)
            {
                ref Character c = ref Zone.Entities.Get<Character>(session.Entity);
                Log($"{session.Name} fell: {c.Events.LandingSpeed:0.0} m/s, {c.Events.FallDamage} damage, {c.State.Health} health left");
            }
        }
    }

    private static uint Tenths(float amount) => (uint)Math.Clamp(Math.Round(amount * 10), 0, uint.MaxValue);

    // The player's own containers (in full at joining), what they loot, vitals, crafting and life, when changed.
    private void SendOwnState(PlayerSession session, Player p)
    {
        OwnState sent = session.State<OwnState>();
        if (!sent.ContainersSent)
        {
            sent.ContainersSent = true;
            SendContents(session, ContainerRef.Main, p.Inventory.Main);
            SendContents(session, ContainerRef.Belt, p.Inventory.Belt);
            SendContents(session, ContainerRef.Wear, p.Inventory.Wear);
        }
        if (p.LootRevision != sent.Loot)
        {
            sent.Loot = p.LootRevision;
            (WorldObject? thing, Player? sleeper, _) = Zone.Actions().LootTarget(p);
            Writer.Reset();
            Simulation.Ships.Ship? ship = Zone.Ships().LootShip(p);
            new LootState
            {
                LastSeq = session.LastIntentSeq, Target = thing != null || sleeper != null || ship != null ? p.Looting.Value : 0, Kind = Zone.Actions().LootKindOf(p),
            }.WriteMessage(Writer);
            Send(session);
            if (ship != null)
            {
                SendContents(session, ContainerRef.Loot, Zone.Ships().LootContainer(p, ship)); // a ship's cargo (G6), a station's magazine (IW3)
            }
            else if (thing?.Container is { } container)
            {
                SendContents(session, ContainerRef.Loot, container);
            }
            else if (sleeper != null)
            {
                SendContents(session, ContainerRef.Loot, sleeper.Inventory.Main);
                SendContents(session, ContainerRef.LootBelt, sleeper.Inventory.Belt);
                SendContents(session, ContainerRef.LootWear, sleeper.Inventory.Wear);
            }
        }
        if (p.VitalsRevision != sent.Vitals)
        {
            sent.Vitals = p.VitalsRevision;
            Vitals v = p.Vitals;
            Writer.Reset();
            new VitalsUpdate
            {
                Health = (uint)Math.Max(0, Math.Ceiling(v.Health * 10)),
                Calories = (uint)Math.Max(0, v.Calories),
                Hydration = (uint)Math.Max(0, v.Hydration),
                Temperature = (sbyte)Math.Clamp(Math.Round(v.Temperature), -127, 127),
                Bleeding = (byte)Math.Clamp(Math.Ceiling(v.Bleeding), 0, 255),
                Healing = (byte)Math.Clamp(Math.Ceiling(v.PendingHeal), 0, 255),
                Burning = (byte)Math.Clamp(Math.Ceiling(v.Burning), 0, 255),
                Cold = (byte)Math.Clamp(v.Cold * 255, 0, 255),
                Comfort = v.Comfort,
                Wet = v.WetSeconds > 0,
                Breath = (byte)Math.Clamp(MathF.Round(255 * (1 - v.BreathUsed / Content.Survival.BreathSeconds)), 0, 255),
                Suffocating = v.Suffocating,
            }.WriteMessage(Writer);
            Send(session);
        }
        if (p.Crafting.Revision != sent.Craft)
        {
            sent.Craft = p.Crafting.Revision;
            var jobs = new CraftJobNet[p.Crafting.Jobs.Count];
            for (int i = 0; i < jobs.Length; i++)
            {
                CraftJob j = p.Crafting.Jobs[i];
                jobs[i] = new CraftJobNet { Ticket = j.Ticket, Recipe = j.Recipe, Count = (uint)j.Count, TicksLeft = (uint)j.TicksLeft, UnitTicks = (uint)j.UnitTicks, Free = j.Free };
            }
            Writer.Reset();
            new CraftQueueState { LastSeq = session.LastIntentSeq, ServerTick = (uint)Zone.Tick, NextTicket = p.Crafting.NextTicket, Jobs = jobs }.WriteMessage(Writer);
            Send(session);
        }
        if (sent.Dead != p.Dead || sent.Wounded != p.Wounded)
        {
            sent.Dead = p.Dead;
            sent.Wounded = p.Wounded;
            SendLife(session, p);
        }
        if (p.GunRevision != sent.Gun)
        {
            sent.Gun = p.GunRevision;
            SendGun(session, p);
        }
    }

    // The gun in hand as the server has it, after the last action it applied: the client starts its prediction again here.
    private void SendGun(PlayerSession session, Player p)
    {
        GunState g = p.Gun;
        ItemStack weapon = g.Slot < PlayerInventory.BeltSlots ? p.Inventory.Belt[g.Slot] : default;
        bool held = g.Item != 0 && weapon.Item == g.Item;
        Writer.Reset();
        new GunUpdate
        {
            LastSeq = g.LastSeq,
            RefusedSeq = p.GunRefusedSeq,
            Refused = p.GunRefused,
            Slot = g.Slot,
            Item = g.Item,
            Loaded = held ? weapon.Loaded : 0u,
            LoadedAmmo = held ? weapon.LoadedAmmo : 0u,
            EquipReadyTick = (ulong)Math.Max(0, g.EquipReadyTick),
            NextFire16 = (ulong)Math.Max(0, g.NextFire16),
            ReloadStartTick = g.Reloading ? (ulong)Math.Max(0, g.ReloadStartTick) : 0,
            ReloadEndTick = g.Reloading ? (ulong)Math.Max(1, g.ReloadEndTick) : 0,
            ReloadAmmo = g.ReloadAmmo,
            ShotTick = g.Recoil.ShotTick < 0 ? 0 : (ulong)Math.Max(1, g.Recoil.ShotTick),
            RecoilPitch = g.Recoil.Pitch,
            RecoilYaw = g.Recoil.Yaw,
            StartPitch = g.Recoil.StartPitch,
            StartYaw = g.Recoil.StartYaw,
            Bloom = g.Recoil.Bloom,
            RecoilIndex = (ushort)Math.Min((int)g.Recoil.Index, 63),
        }.WriteMessage(Writer);
        Send(session);
    }

    private void SendLife(PlayerSession session, Player p)
    {
        var bags = new List<BagNet>();
        if (p.Dead)
        {
            foreach (WorldObject thing in Zone.Things().Statics())
            {
                if (thing is Deployable { Def.IsRespawnPoint: true } bag && bag.Owner == p.Id && bags.Count < 32)
                {
                    bags.Add(new BagNet
                    {
                        Id = bag.Id.Value,
                        X = bag.Position.X,
                        Y = bag.Position.Y,
                        Z = bag.Position.Z,
                        ReadySeconds = (uint)Math.Max(0, Math.Ceiling((bag.ReadyAtUnixMs - Zone.UnixMilliseconds) / 1000.0)),
                        Name = bag.Name.Length > 0 ? bag.Name : bag.Def.Item.Name,
                    });
                }
            }
        }
        Writer.Reset();
        new LifeState
        {
            Dead = p.Dead,
            Wounded = p.Wounded && !p.Dead,
            WoundedSeconds = p.Wounded ? (uint)Math.Max(0, Math.Ceiling((p.WoundedUntilTick - Zone.Tick) / (double)TickRate)) : 0,
            Cause = p.Dead ? p.DeathCause : "",
            KilledBy = p.Dead ? p.KilledBy : "",
            BeachReadySeconds = (uint)Math.Max(0, Math.Ceiling((p.BeachReadyAtUnixMs - Zone.UnixMilliseconds) / 1000.0)),
            Bags = bags.ToArray(),
        }.WriteMessage(Writer);
        Send(session);
    }

    private void SendContents(PlayerSession session, ContainerRef which, Container container)
    {
        _slots.Clear();
        for (int i = 0; i < container.Size && i < 256; i++)
        {
            if (!container[i].IsEmpty)
            {
                _slots.Add(Slot(container, i));
            }
        }
        Writer.Reset();
        new ContainerContents { LastSeq = session.LastIntentSeq, Which = which, Size = (byte)Math.Min(255, container.Size), Slots = _slots.ToArray(),
            AcceptedItem = container.AcceptedItem, StackLimit = (uint)container.StackLimit }.WriteMessage(Writer);
        Send(session);
    }

    private static SlotNet Slot(Container container, int index)
    {
        ItemStack s = container[index];
        return new SlotNet
        {
            Index = (byte)index, Item = s.Item, Count = (uint)Math.Max(0, s.Count), Condition = s.Condition,
            Gun = s.HasWeaponState ? new[] { new GunNet { Loaded = s.Loaded, LoadedAmmo = s.LoadedAmmo, Sight = s.Sight, Muzzle = s.Muzzle, Rail = s.Rail } } : null!,
        };
    }

    // Changed slots go to everyone who sees the container: its owner, and whoever is looting it.
    private void SendContainerChanges()
    {
        _watchers.Clear();
        void Watch(Container? container, PlayerSession session, ContainerRef which)
        {
            if (container == null)
            {
                return;
            }
            if (!_watchers.TryGetValue(container, out List<(PlayerSession, ContainerRef)>? list))
            {
                list = new List<(PlayerSession, ContainerRef)>();
                _watchers.Add(container, list);
            }
            list.Add((session, which));
        }
        foreach (PlayerSession session in Players)
        {
            if (!session.Playing || Zone.Players().PlayerOf(session.Entity) is not { } p)
            {
                continue;
            }
            Watch(p.Inventory.Main, session, ContainerRef.Main);
            Watch(p.Inventory.Belt, session, ContainerRef.Belt);
            Watch(p.Inventory.Wear, session, ContainerRef.Wear);
            if (!p.Looting.IsNone)
            {
                Watch(Zone.Actions().ResolveContainer(p, ContainerRef.Loot), session, ContainerRef.Loot);
                Watch(Zone.Actions().ResolveContainer(p, ContainerRef.LootBelt), session, ContainerRef.LootBelt);
                Watch(Zone.Actions().ResolveContainer(p, ContainerRef.LootWear), session, ContainerRef.LootWear);
            }
        }
        foreach ((Container container, List<(PlayerSession Session, ContainerRef Which)> list) in _watchers)
        {
            if (!container.HasDirty)
            {
                continue;
            }
            _dirty.Clear();
            container.TakeDirty(_dirty);
            _slots.Clear();
            foreach (int index in _dirty)
            {
                if (index < 256)
                {
                    _slots.Add(Slot(container, index));
                }
            }
            SlotNet[] slots = _slots.ToArray();
            foreach ((PlayerSession session, ContainerRef which) in list)
            {
                Writer.Reset();
                new ContainerSlots { LastSeq = session.LastIntentSeq, Which = which, Slots = slots,
                    AcceptedItem = container.AcceptedItem, StackLimit = (uint)container.StackLimit }.WriteMessage(Writer);
                Send(session);
            }
        }
    }

    // ---------------------------------------------------------------- clothing others see

    /// <summary>A character in this player's range: what they wear, when it changed or the character just came into range.</summary>
    protected internal override void Seen(PlayerSession session, uint netId, Entity entity, bool arrived)
    {
        if (Zone.Players().PlayerOf(entity) is not { } p)
        {
            return;
        }
        ulong hash = Hash64.Combine(0x10C, p.Body);
        for (int i = 0; i < p.Inventory.Wear.Size; i++)
        {
            hash = Hash64.Combine(hash, p.Inventory.Wear[i].Item);
        }
        Dictionary<uint, ulong> looks = session.State<Looks>().Sent;
        if (!arrived && looks.TryGetValue(netId, out ulong sent) && sent == hash)
        {
            return;
        }
        looks[netId] = hash;
        var wear = new List<WornItem>();
        for (int i = 0; i < p.Inventory.Wear.Size; i++)
        {
            if (!p.Inventory.Wear[i].IsEmpty)
            {
                wear.Add(new WornItem { Item = p.Inventory.Wear[i].Item });
            }
        }
        Writer.Reset();
        new CharacterLook { NetId = netId, Wear = wear.ToArray(), Body = p.Body }.WriteMessage(Writer);
        Send(session);
    }
}

/// <summary>Counts of what happened in the survival game (from the zone's events).</summary>
public sealed class GameCounters
{
    public long Gathered { get; private set; }
    public long GatherHits { get; private set; }
    public long WeakSpotHits { get; private set; }
    public long PickedUp { get; private set; }
    public long Crafted { get; private set; }
    public long CharacterHits { get; private set; }
    public long Kills { get; private set; }
    public long Deaths { get; private set; }
    public long Arrows { get; private set; }

    /// <summary>G4: bullets fired, their hits on characters, players knocked down and up again.</summary>
    public long Shots { get; private set; }
    public long BulletHits { get; private set; }
    public long Downed { get; private set; }
    public long Recovered { get; private set; }

    /// <summary>G7: hits on animals (and harvesting blows), animals killed by players.</summary>
    public long AnimalHits { get; private set; }
    public long AnimalKills { get; private set; }

    /// <summary>Subscribes to the zone's events (GROUND_SYSTEMS.md §12.3): each kind counted as it happens.</summary>
    public void Listen(ZoneEvents events)
    {
        events.On<ItemsGained>((in ItemsGained e) =>
        {
            if (e.Reason == GainReason.Gathered)
            {
                Gathered += e.Count;
            }
            else if (e.Reason == GainReason.PickedUp)
            {
                PickedUp += e.Count;
            }
        });
        events.On<HitConfirmed>((in HitConfirmed e) =>
        {
            // A tally by what was hit.
            if (e.Target.Family == TargetFamily.Natural)
            {
                GatherHits++;
                WeakSpotHits += e.Flag ? 1 : 0;
            }
            else if (e.Target.Family == TargetFamily.Character)
            {
                CharacterHits++;
                Kills += e.Flag ? 1 : 0;
                Downed += e.Downed ? 1 : 0;
            }
            else if (e.Target.Family == TargetFamily.Animal)
            {
                AnimalHits++;
                AnimalKills += e.Flag ? 1 : 0;
            }
        });
        events.On<GotUp>((in GotUp e) => Recovered++);
        events.On<Crafted>((in Crafted e) => Crafted += e.Count);
        events.On<Died>((in Died e) => Deaths++);
        events.On<ProjectileLaunched>((in ProjectileLaunched e) =>
        {
            if (e.Seq != 0)
            {
                Shots++;
            }
            else
            {
                Arrows++;
            }
        });
        events.On<ProjectileEnded>((in ProjectileEnded e) =>
        {
            if (e.Seq != 0 && e.Target.Family == TargetFamily.Character)
            {
                BulletHits++;
            }
        });
    }

    public override string ToString() =>
        $"{GatherHits} gathering hits ({WeakSpotHits} on the weak spot) for {Gathered} items, {PickedUp} items picked up, " +
        $"{Crafted} crafted, {Arrows} arrows, {Shots} shots ({BulletHits} hit someone), {CharacterHits} hits on players, {Kills} kills, " +
        $"{Downed} knocked down ({Recovered} up again), {Deaths} deaths; {AnimalHits} hits on animals, {AnimalKills} animals killed";
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): the survival game's intents, own state and counters.</summary>
public static class SurvivalModuleAccess
{
    public static SurvivalModule Survival(this GameServer server) => server.Require<SurvivalModule>();
}
