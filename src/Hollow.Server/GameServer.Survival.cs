using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;
using LiteNetLib;

namespace Hollow.Server;

// The survival game on the server (GROUND.md §4.2, §6): the world loaded and saved, players' intents (with their
// budget) applied to the zone, each player's own state (containers, vitals, crafting, life) sent to them alone, events
// routed to whom they concern, and static things and natural object changes replicated by relevance.
public sealed partial class GameServer
{
    /// <summary>A player hears of at most this many new static things per snapshot, so a big base streams in.</summary>
    public const int ThingSpawnsPerSnapshot = 48;

    private readonly RelevanceGrid _thingGrid;
    private readonly Dictionary<EntityId, long> _accountOf = new();
    private readonly Dictionary<EntityId, PlayerSession> _sessionOf = new();
    private readonly Dictionary<EntityId, uint> _thingNetIds = new();
    private readonly Dictionary<uint, WorldObject> _things = new();
    private readonly HashSet<uint> _thingsPresent = new();
    private readonly List<uint> _thingsGone = new();
    private readonly List<int> _thingIndices = new();
    private readonly HashSet<uint> _thingsRelevant = new();
    private readonly List<uint> _thingsLeaving = new();
    private readonly HashSet<EntityId> _savedThings = new();
    private readonly HashSet<ulong> _savedNaturals = new();
    private readonly HashSet<ulong> _naturalsToSave = new();
    private readonly HashSet<ulong> _naturalsToSend = new();
    private readonly Dictionary<(int X, int Z), HashSet<ulong>> _overrideCells = new();
    private readonly List<NaturalNet> _naturalBatch = new();
    private readonly List<(int X, int Z)> _cellsLeaving = new();
    private readonly Dictionary<Container, List<(PlayerSession Session, ContainerRef Which)>> _watchers = new();
    private readonly List<int> _dirty = new();
    private readonly List<SlotNet> _slots = new();

    /// <summary>The game's data, as every client must have it too.</summary>
    public GameContent Content { get; }

    private bool _deathToSave;
    private long _lastDeathSave;

    /// <summary>What happened in the game so far, for the load test and <c>status</c>.</summary>
    public GameCounters Counters { get; } = new();

    // ---------------------------------------------------------------- loading and saving the world

    // Everything stored about this body: characters come back asleep (or dead), things where they were, natural
    // objects as players left them.
    private void LoadWorld()
    {
        if (_persistence == null)
        {
            return;
        }
        ZoneRecord stored = _persistence.LoadZone(Spec.SystemAddress, Spec.BodyPath).GetAwaiter().GetResult();
        int sleepers = 0, things = 0;
        foreach (CharacterRecord r in stored.Characters)
        {
            if (!Restorable(r, out Vector3 local) || Zone.Entities.TryFind(r.Id, out _))
            {
                continue;
            }
            CharacterState state = Zone.NewCharacter(local + Zone.Frame.UpAt(local) * 0.02f);
            state.Stance = (Stance)Math.Min(r.Stance, (byte)Stance.Crouching);
            Entity entity = Zone.AddPlayer(r.Id, r.Name, state, DecodePlayer(r));
            Zone.Entities.Get<Character>(entity).Input.Yaw = r.Yaw;
            Zone.Sleep(entity);
            Zone.Entities.Add(entity, new NetPresence { NetId = _nextNetId++, Yaw = Quantize.Angle16(r.Yaw), Pitch = Quantize.Angle16(r.Pitch) });
            _accountOf[r.Id] = r.AccountId;
            sleepers++;
        }
        foreach (ThingRecord t in stored.Things)
        {
            if (StoredExplosive(t))
            {
                continue;
            }
            try
            {
                WorldObject? thing = WorldCodec.DecodeThing(t.Id, (StaticKind)t.Kind, t.Data, Content, Zone.Frame.ToLocal, Zone.Frame.UpAt, Zone.Tick);
                if (thing != null && !Zone.Entities.TryFind(thing.Id, out _))
                {
                    Zone.AddStatic(thing);
                    things++;
                }
                _savedThings.Add(t.Id);
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException)
            {
                Log($"thing {t.Id} doesn't load ({e.Message}); left out");
            }
        }
        foreach (NaturalRecord n in stored.Naturals)
        {
            var loaded = new NaturalOverride { Health = n.Health, Depleted = n.Depleted, RespawnAtUnixMs = n.RespawnAtUnixMilliseconds };
            loaded.UnpackWeakSpot(n.WeakSpot);
            Zone.Natural!.Apply(n.ObjectId, loaded);
            _savedNaturals.Add(n.ObjectId);
            IndexOverride(n.ObjectId);
        }
        LoadBuilding(stored);
        LoadShips(stored);
        RestoreExplosives();
        LoadWorldState(stored);
        Log($"loaded {sleepers} sleepers, {things} things and {stored.Naturals.Count} changed natural objects");
    }

    private Player? DecodePlayer(CharacterRecord record)
    {
        if (record.State == null)
        {
            return null; // a G1 character: a new life (with the respawn kit) where it stood
        }
        try
        {
            return WorldCodec.DecodePlayer(record.State, record.Id, record.Name, Content);
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException)
        {
            Log($"{record.Name}'s survival state doesn't load ({e.Message}); a new life");
            return null;
        }
    }

    private CharacterRecord RecordOf(Entity entity, Player p)
    {
        ref Character c = ref Zone.Entities.Get<Character>(entity);
        Vector3D body = Zone.Frame.ToBody(c.State.Position);
        float yaw = c.Input.Yaw, pitch = c.Input.Pitch;
        if (Zone.Entities.Has<NetPresence>(entity))
        {
            ref NetPresence presence = ref Zone.Entities.Get<NetPresence>(entity);
            yaw = Quantize.FromAngle16(presence.Yaw);
            pitch = Quantize.FromAngle16(presence.Pitch);
        }
        return new CharacterRecord
        {
            Id = p.Id,
            AccountId = _accountOf.GetValueOrDefault(p.Id),
            Name = p.Name,
            SystemAddress = Spec.SystemAddress,
            BodyPath = Spec.BodyPath,
            BodyX = body.X,
            BodyY = body.Y,
            BodyZ = body.Z,
            Yaw = yaw,
            Pitch = pitch,
            Stance = (byte)c.State.Stance,
            Health = c.State.Health,
            SavedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Dead = p.Dead,
            State = WorldCodec.EncodePlayer(p),
        };
    }

    // The whole zone in one transaction: every character (players and sleepers), every static thing (and those gone
    // since), and the natural objects that changed.
    private ZoneSave BuildSave()
    {
        var save = new ZoneSave
        {
            SystemAddress = Spec.SystemAddress,
            BodyPath = Spec.BodyPath,
            SavedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        ComponentPool<PlayerLink> players = Zone.Entities.Pool<PlayerLink>();
        for (int i = 0; i < players.Count; i++)
        {
            Player p = players.Values[i].Player;
            if (_accountOf.GetValueOrDefault(p.Id) != 0)
            {
                save.Characters.Add(RecordOf(Zone.Entities.AtSlot(players.Slots[i]), p));
            }
        }
        var current = new HashSet<EntityId>();
        foreach (WorldObject thing in Zone.Statics())
        {
            if (thing is Deployable deployable && Zone.ClaimOf(deployable) != null)
            {
                continue; // saved with the building
            }
            save.Things.Add(new ThingRecord
            {
                Id = thing.Id,
                SystemAddress = Spec.SystemAddress,
                BodyPath = Spec.BodyPath,
                Kind = (byte)thing.Kind,
                Data = WorldCodec.EncodeThing(thing, Zone.Frame.ToBody(thing.Position), Zone.Tick),
            });
            current.Add(thing.Id);
        }
        AddExplosivesToSave(save, current);
        foreach (EntityId id in _savedThings)
        {
            if (!current.Contains(id))
            {
                save.RemovedThings.Add(id);
            }
        }
        _savedThings.Clear();
        _savedThings.UnionWith(current);
        foreach (ulong id in _naturalsToSave)
        {
            if (Zone.Natural!.TryGetOverride(id, out NaturalOverride o))
            {
                save.Naturals.Add(new NaturalRecord(id, o.Health, o.PackedWeakSpot, o.Depleted, o.RespawnAtUnixMs));
                _savedNaturals.Add(id);
            }
            else if (_savedNaturals.Remove(id))
            {
                save.RemovedNaturals.Add(id);
            }
        }
        _naturalsToSave.Clear();
        AddBuildingToSave(save);
        AddShipsToSave(save);
        AddWorldToSave(save);
        return save;
    }

    // ---------------------------------------------------------------- intents

    // Every intent spends from the player's budget (a bad one too), is applied to the zone, and is answered after this
    // tick's changes have gone out.
    private bool ReceiveIntent(PlayerSession session, uint type)
    {
        Entity me = session.Entity;
        switch (type)
        {
            case MoveItemIntent.TypeId:
            {
                MoveItemIntent m = MoveItemIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq,
                    () => Zone.MoveItem(me, m.From, m.FromSlot, m.To, m.ToSlot, Amount(m.Amount), Expected(m.Expected)));
            }
            case QuickMoveIntent.TypeId:
            {
                QuickMoveIntent m = QuickMoveIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq, () => Zone.QuickMove(me, m.From, m.Slot, Expected(m.Expected)));
            }
            case DropIntent.TypeId:
            {
                DropIntent m = DropIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq, () => Zone.DropItem(me, m.From, m.Slot, Amount(m.Amount), Expected(m.Expected)));
            }
            case UseIntent.TypeId:
            {
                UseIntent m = UseIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq, () => Zone.UseItem(me, m.From, m.Slot, Expected(m.Expected)));
            }
            case CraftIntent.TypeId:
            {
                CraftIntent m = CraftIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq, () => Zone.Craft(me, Amount(m.Recipe), Amount(m.Count), out _));
            }
            case CancelCraftIntent.TypeId:
            {
                CancelCraftIntent m = CancelCraftIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq, () => Zone.CancelCraft(me, m.Ticket));
            }
            case OpenIntent.TypeId:
            {
                OpenIntent m = OpenIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq, () => Zone.Open(me, new EntityId(m.Target)));
            }
            case CloseIntent.TypeId:
            {
                CloseIntent m = CloseIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq, () => Zone.Close(me));
            }
            case PickUpIntent.TypeId:
            {
                PickUpIntent m = PickUpIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq,
                    () => m.Natural ? Zone.PickUpNatural(me, m.Target) : Zone.PickUpItem(me, new EntityId(m.Target)));
            }
            case PlaceIntent.TypeId:
            {
                PlaceIntent m = PlaceIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq,
                    () => Zone.Place(me, m.From, m.Slot, Expected(m.Expected), new Vector3(m.X, m.Y, m.Z), Quantize.FromAngle16(m.Yaw)));
            }
            case ToggleIntent.TypeId:
            {
                ToggleIntent m = ToggleIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq, () => Zone.Toggle(me, new EntityId(m.Target)));
            }
            case RespawnIntent.TypeId:
            {
                RespawnIntent m = RespawnIntent.Read(_reader);
                if (_reader.Failed)
                {
                    return false;
                }
                session.SentDead = null; // the answer comes with fresh respawn choices
                return Intent(session, m.Seq, () =>
                {
                    ActionResult result = Zone.Respawn(me, new EntityId(m.Bag));
                    if (result == ActionResult.Accepted && _options.KitEveryLife && _options.JoinKit != null &&
                        Content.Loot(_options.JoinKit) is { } kit && Zone.PlayerOf(me) is { } p)
                    {
                        GiveKit(p, kit);
                    }
                    return result;
                }, whileDead: true);
            }
            case FireIntent.TypeId:
            {
                FireIntent m = FireIntent.Read(_reader);
                return !_reader.Failed && GunIntent(session, new GunAction
                {
                    Seq = m.Seq, Tick = m.Tick, Kind = GunActionKind.Fire, Slot = m.Slot, Item = Expected(m.Item),
                    Yaw = Quantize.FromAngle16(m.Yaw), Pitch = Quantize.FromAngle16(m.Pitch), Aimed = m.Aimed, ViewDelay = m.ViewDelay,
                });
            }
            case ReloadIntent.TypeId:
            {
                ReloadIntent m = ReloadIntent.Read(_reader);
                return !_reader.Failed && GunIntent(session, new GunAction
                {
                    Seq = m.Seq, Tick = m.Tick, Kind = GunActionKind.Reload, Slot = m.Slot, Item = Expected(m.Item), Ammo = Expected(m.Ammo),
                });
            }
            case AttachIntent.TypeId:
            {
                AttachIntent m = AttachIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq, () => Zone.Attach(me, m.From, m.Slot, m.WeaponIn, m.WeaponSlot, Expected(m.Expected)));
            }
            case DetachIntent.TypeId:
            {
                DetachIntent m = DetachIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq, () => Zone.Detach(me, m.WeaponIn, m.WeaponSlot, m.Mod));
            }
            case HelpUpIntent.TypeId:
            {
                HelpUpIntent m = HelpUpIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq, () => Zone.HelpUp(me, new EntityId(m.Target)));
            }
            default:
                return ReceiveBuildIntent(session, type) || ReceiveRaidIntent(session, type) || ReceiveWorldIntent(session, type);
        }
    }

    private bool Intent(PlayerSession session, uint seq, Func<ActionResult> act, bool whileDead = false)
    {
        SurvivalTuning s = Content.Survival;
        ActionResult result;
        if (!session.SpendIntentToken(Zone.Tick, s.IntentBurst, s.IntentsPerSecond))
        {
            result = ActionResult.RateLimited;
            session.IntentsRateLimited++;
        }
        else if (!session.Playing)
        {
            result = ActionResult.Dead;
        }
        else
        {
            result = act();
        }
        if (result == ActionResult.Accepted)
        {
            session.IntentsAccepted++;
        }
        else
        {
            session.IntentsRefused++;
        }
        session.PendingResults.Add(new IntentResult { Seq = seq, Result = result });
        session.LastIntentSeq = Math.Max(session.LastIntentSeq, seq);
        return true;
    }

    // A shot or reload: from its own budget (a refused one is still answered in order), queued for its tick in the zone.
    private bool GunIntent(PlayerSession session, GunAction action)
    {
        SurvivalTuning s = Content.Survival;
        session.GunActions++;
        if (!session.SpendGunToken(Zone.Tick, s.GunBurst, s.GunActionsPerSecond))
        {
            action.Refused = GunResult.RateLimited;
            session.GunRateLimited++;
        }
        if (session.Playing)
        {
            Zone.QueueGunAction(session.Entity, action);
        }
        return true;
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

    private static int Amount(uint value) => (int)Math.Min(value, int.MaxValue);

    // An item ID from outside: one that can't be is "expect nothing", which then can't match.
    private static ushort Expected(uint value) => value > ushort.MaxValue ? ushort.MaxValue : (ushort)value;

    // ---------------------------------------------------------------- after each step

    private void AfterStep()
    {
        NaturalState natural = Zone.Natural!;
        foreach (ulong id in natural.Changed)
        {
            _naturalsToSave.Add(id);
            _naturalsToSend.Add(id);
            IndexOverride(id);
        }
        natural.Changed.Clear();
        RouteEvents();
        RouteShipEvents();
        AfterRaid();
        AfterWorld();
        foreach (PlayerSession session in _sessions.Values)
        {
            if (session.Playing && Zone.PlayerOf(session.Entity) is { } p)
            {
                SendOwnState(session, p);
            }
        }
        SendContainerChanges();
        foreach (PlayerSession session in _sessions.Values)
        {
            foreach (IntentResult result in session.PendingResults)
            {
                _writer.Reset();
                result.WriteMessage(_writer);
                Send(session);
            }
            session.PendingResults.Clear();
        }
        SendBuildResults();
    }

    private void Send(PlayerSession session) => session.Peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);

    // What happened this tick, to whom it concerns.
    private void RouteEvents()
    {
        foreach (ZoneEvent e in Zone.GameEvents)
        {
            Counters.Count(e);
            PlayerSession? to = _sessionOf.GetValueOrDefault(e.Player);
            _writer.Reset();
            switch (e.Kind)
            {
                case ZoneEventKind.HitConfirmed when to != null:
                    new HitMarker
                    {
                        Target = e.Target,
                        Region = e.Region,
                        Damage = Tenths(e.Amount),
                        Killed = e.Target == HitTarget.Character && e.Flag,
                        WeakSpot = e.Target == HitTarget.Natural && e.Flag,
                        Downed = e.Downed,
                    }.WriteMessage(_writer);
                    Send(to);
                    break;
                case ZoneEventKind.DamageTaken when to != null:
                    new DamageTaken { Damage = Tenths(e.Amount), Region = e.Region, FromX = e.Position.X, FromY = e.Position.Y, FromZ = e.Position.Z, Weapon = e.Item }
                        .WriteMessage(_writer);
                    Send(to);
                    break;
                case ZoneEventKind.ItemsGained when to != null:
                    new ItemsChanged { Item = e.Item, Count = e.Count, Reason = e.Reason, WeakSpot = e.Flag }.WriteMessage(_writer);
                    Send(to);
                    break;
                case ZoneEventKind.CraftFinished when to != null:
                    new CraftFinished { Item = e.Item, Count = (uint)Math.Max(0, e.Count) }.WriteMessage(_writer);
                    Send(to);
                    break;
                case ZoneEventKind.Died:
                    Log($"{NameOf(e.Player)} died: {e.Text}" + (e.Other.IsNone ? "" : $", by {NameOf(e.Other)}"));
                    if (_persistence != null)
                    {
                        // Deaths commit at once (ARCHITECTURE.md §13): within a second, several deaths in one save.
                        _deathToSave = true;
                    }
                    break;
                case ZoneEventKind.ProjectileLaunched:
                    uint shooter = Zone.Entities.TryFind(e.Player, out Entity s) && Zone.Entities.Has<NetPresence>(s) ? Zone.Entities.Get<NetPresence>(s).NetId : 0;
                    new ProjectileLaunch
                    {
                        Id = e.Projectile, Shooter = shooter, Ammo = e.Item, X = e.Position.X, Y = e.Position.Y, Z = e.Position.Z,
                        VelocityX = e.Velocity.X, VelocityY = e.Velocity.Y, VelocityZ = e.Velocity.Z, Seq = e.Seq, Silenced = e.Flag && e.Seq != 0,
                    }.WriteMessage(_writer);
                    // Bullets are cosmetic to others and many: unreliable (ARCHITECTURE.md §11.3); arrows stay reliable.
                    SendNear(e.Position, e.Seq != 0 ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneEventKind.ProjectileEnded:
                    new ProjectileEnd { Id = e.Projectile, X = e.Position.X, Y = e.Position.Y, Z = e.Position.Z, Target = e.Target }.WriteMessage(_writer);
                    SendNear(e.Position, e.Seq != 0 ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneEventKind.Notice:
                    Notify(e.Player, e.Text ?? "");
                    break;
                case ZoneEventKind.Wounded:
                    Log($"{NameOf(e.Player)} is down: {e.Text}" + (e.Other.IsNone ? "" : $", by {NameOf(e.Other)}"));
                    break;
                case ZoneEventKind.Recovered:
                    Log($"{NameOf(e.Player)} is up again" + (e.Other.IsNone ? "" : $", helped by {NameOf(e.Other)}"));
                    break;
                case ZoneEventKind.Detonated:
                    SendDetonation(e);
                    break;
            }
        }
        Zone.GameEvents.Clear();
    }

    private string NameOf(EntityId id) => Zone.TryFindPlayer(id, out _, out Player p) ? p.Name : id.ToString();

    private void SendNear(Vector3 point, DeliveryMethod method = DeliveryMethod.ReliableOrdered)
    {
        float radius = _options.RelevanceRadius;
        foreach (PlayerSession session in _sessions.Values)
        {
            if (session.Playing && Vector3.DistanceSquared(Zone.Entities.Get<Character>(session.Entity).State.Position, point) <= radius * radius)
            {
                session.Peer.Send(_writer.Data, method);
            }
        }
    }

    private static uint Tenths(float amount) => (uint)Math.Clamp(Math.Round(amount * 10), 0, uint.MaxValue);

    // The player's own containers (in full at joining), what they loot, vitals, crafting and life, when changed.
    private void SendOwnState(PlayerSession session, Player p)
    {
        if (!session.OwnContainersSent)
        {
            session.OwnContainersSent = true;
            SendContents(session, ContainerRef.Main, p.Inventory.Main);
            SendContents(session, ContainerRef.Belt, p.Inventory.Belt);
            SendContents(session, ContainerRef.Wear, p.Inventory.Wear);
        }
        if (p.LootRevision != session.SentLoot)
        {
            session.SentLoot = p.LootRevision;
            (WorldObject? thing, Player? sleeper, _) = Zone.LootTarget(p);
            _writer.Reset();
            Simulation.Ships.Ship? ship = Zone.LootShip(p);
            new LootState
            {
                LastSeq = session.LastIntentSeq, Target = thing != null || sleeper != null || ship != null ? p.Looting.Value : 0, Kind = Zone.LootKindOf(p),
            }.WriteMessage(_writer);
            Send(session);
            if (ship != null)
            {
                SendContents(session, ContainerRef.Loot, ship.Cargo); // a ship's cargo (G6)
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
        if (p.VitalsRevision != session.SentVitals)
        {
            session.SentVitals = p.VitalsRevision;
            Vitals v = p.Vitals;
            _writer.Reset();
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
            }.WriteMessage(_writer);
            Send(session);
        }
        if (p.Crafting.Revision != session.SentCraft)
        {
            session.SentCraft = p.Crafting.Revision;
            var jobs = new CraftJobNet[p.Crafting.Jobs.Count];
            for (int i = 0; i < jobs.Length; i++)
            {
                CraftJob j = p.Crafting.Jobs[i];
                jobs[i] = new CraftJobNet { Ticket = j.Ticket, Recipe = j.Recipe, Count = (uint)j.Count, TicksLeft = (uint)j.TicksLeft, UnitTicks = (uint)j.UnitTicks };
            }
            _writer.Reset();
            new CraftQueueState { LastSeq = session.LastIntentSeq, ServerTick = (uint)Zone.Tick, NextTicket = p.Crafting.NextTicket, Jobs = jobs }.WriteMessage(_writer);
            Send(session);
        }
        if (session.SentDead != p.Dead || session.SentWounded != p.Wounded)
        {
            session.SentDead = p.Dead;
            session.SentWounded = p.Wounded;
            SendLife(session, p);
        }
        if (p.GunRevision != session.SentGun)
        {
            session.SentGun = p.GunRevision;
            SendGun(session, p);
        }
    }

    // The gun in hand as the server has it, after the last action it applied: the client starts its prediction again here.
    private void SendGun(PlayerSession session, Player p)
    {
        GunState g = p.Gun;
        ItemStack weapon = g.Slot < PlayerInventory.BeltSlots ? p.Inventory.Belt[g.Slot] : default;
        bool held = g.Item != 0 && weapon.Item == g.Item;
        _writer.Reset();
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
        }.WriteMessage(_writer);
        Send(session);
    }

    private void SendLife(PlayerSession session, Player p)
    {
        var bags = new List<BagNet>();
        if (p.Dead)
        {
            foreach (WorldObject thing in Zone.Statics())
            {
                if (thing is Deployable { Def.IsRespawnPoint: true } bag && bag.Owner == p.Id && bags.Count < 32)
                {
                    bags.Add(new BagNet
                    {
                        Id = bag.Id.Value,
                        X = bag.Position.X,
                        Z = bag.Position.Z,
                        ReadySeconds = (uint)Math.Max(0, Math.Ceiling((bag.ReadyAtUnixMs - Zone.UnixMilliseconds) / 1000.0)),
                    });
                }
            }
        }
        _writer.Reset();
        new LifeState
        {
            Dead = p.Dead,
            Wounded = p.Wounded && !p.Dead,
            WoundedSeconds = p.Wounded ? (uint)Math.Max(0, Math.Ceiling((p.WoundedUntilTick - Zone.Tick) / (double)TickRate)) : 0,
            Cause = p.Dead ? p.DeathCause : "",
            KilledBy = p.Dead ? p.KilledBy : "",
            BeachReadySeconds = (uint)Math.Max(0, Math.Ceiling((p.BeachReadyAtUnixMs - Zone.UnixMilliseconds) / 1000.0)),
            Bags = bags.ToArray(),
        }.WriteMessage(_writer);
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
        _writer.Reset();
        new ContainerContents { LastSeq = session.LastIntentSeq, Which = which, Size = (byte)Math.Min(255, container.Size), Slots = _slots.ToArray() }.WriteMessage(_writer);
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
        foreach (PlayerSession session in _sessions.Values)
        {
            if (!session.Playing || Zone.PlayerOf(session.Entity) is not { } p)
            {
                continue;
            }
            Watch(p.Inventory.Main, session, ContainerRef.Main);
            Watch(p.Inventory.Belt, session, ContainerRef.Belt);
            Watch(p.Inventory.Wear, session, ContainerRef.Wear);
            if (!p.Looting.IsNone)
            {
                Watch(Zone.Resolve(p, ContainerRef.Loot), session, ContainerRef.Loot);
                Watch(Zone.Resolve(p, ContainerRef.LootBelt), session, ContainerRef.LootBelt);
                Watch(Zone.Resolve(p, ContainerRef.LootWear), session, ContainerRef.LootWear);
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
                _writer.Reset();
                new ContainerSlots { LastSeq = session.LastIntentSeq, Which = which, Slots = slots }.WriteMessage(_writer);
                Send(session);
            }
        }
    }

    // ---------------------------------------------------------------- clothing others see

    private void SendLook(PlayerSession session, uint netId, Entity entity)
    {
        if (Zone.PlayerOf(entity) is not { } p)
        {
            return;
        }
        ulong hash = Hash64.Combine(0x10C, p.Body);
        for (int i = 0; i < p.Inventory.Wear.Size; i++)
        {
            hash = Hash64.Combine(hash, p.Inventory.Wear[i].Item);
        }
        if (session.LooksSent.TryGetValue(netId, out ulong sent) && sent == hash)
        {
            return;
        }
        session.LooksSent[netId] = hash;
        var wear = new List<WornItem>();
        for (int i = 0; i < p.Inventory.Wear.Size; i++)
        {
            if (!p.Inventory.Wear[i].IsEmpty)
            {
                wear.Add(new WornItem { Item = p.Inventory.Wear[i].Item });
            }
        }
        _writer.Reset();
        new CharacterLook { NetId = netId, Wear = wear.ToArray(), Body = p.Body }.WriteMessage(_writer);
        Send(session);
    }

    // ---------------------------------------------------------------- static things

    // Once per snapshot: every static thing gets a net ID and goes into the relevance grid; those gone are noted.
    private void PrepareThings()
    {
        _thingGrid.Clear();
        _thingsPresent.Clear();
        foreach (WorldObject thing in Zone.Statics())
        {
            if (!_thingNetIds.TryGetValue(thing.Id, out uint netId))
            {
                netId = _nextNetId++;
                _thingNetIds.Add(thing.Id, netId);
            }
            _things[netId] = thing;
            _thingsPresent.Add(netId);
            _thingGrid.Add(netId, thing.Position);
        }
        _thingsGone.Clear();
        foreach ((uint netId, WorldObject thing) in _things)
        {
            if (!_thingsPresent.Contains(netId))
            {
                _thingsGone.Add(netId);
                _thingNetIds.Remove(thing.Id);
            }
        }
        foreach (uint netId in _thingsGone)
        {
            _things.Remove(netId);
        }
    }

    private void ReplicateThings(PlayerSession session, Vector3 centre)
    {
        foreach (uint netId in _thingsGone)
        {
            if (session.ThingsSent.Remove(netId))
            {
                Despawned(session, netId);
            }
        }
        _thingsLeaving.Clear();
        foreach (uint netId in session.ThingsSent.Keys)
        {
            _thingsLeaving.Add(netId);
        }
        _thingsRelevant.Clear();
        _thingsRelevant.UnionWith(_thingsLeaving);
        _thingGrid.Query(centre, _thingsRelevant, _thingIndices);
        _thingsRelevant.Clear();
        int spawned = 0;
        foreach (int index in _thingIndices)
        {
            uint netId = _thingGrid[index].NetId;
            WorldObject thing = _things[netId];
            if (session.ThingsSent.TryGetValue(netId, out uint revision))
            {
                _thingsRelevant.Add(netId);
                if (revision != thing.Revision)
                {
                    session.ThingsSent[netId] = thing.Revision;
                    (byte health, uint count, ThingFlags flags) = Look(thing, session);
                    _writer.Reset();
                    new ThingUpdate { NetId = netId, Health = health, Count = count, Flags = flags }.WriteMessage(_writer);
                    Send(session);
                }
            }
            else if (spawned < ThingSpawnsPerSnapshot)
            {
                spawned++;
                _thingsRelevant.Add(netId);
                session.ThingsSent[netId] = thing.Revision;
                (byte health, uint count, ThingFlags flags) = Look(thing, session);
                _writer.Reset();
                new ThingSpawn
                {
                    NetId = netId,
                    EntityId = thing.Id.Value,
                    Kind = thing.Kind,
                    Look = thing.Look,
                    X = thing.Position.X,
                    Y = thing.Position.Y,
                    Z = thing.Position.Z,
                    Yaw = Quantize.Angle16(thing.Yaw),
                    Health = health,
                    Count = count,
                    Flags = flags,
                    Name = thing is Corpse corpse ? corpse.Name : "",
                }.WriteMessage(_writer);
                Send(session);
            }
        }
        foreach (uint netId in _thingsLeaving)
        {
            if (!_thingsRelevant.Contains(netId) && session.ThingsSent.Remove(netId))
            {
                Despawned(session, netId);
            }
        }
    }

    private void Despawned(PlayerSession session, uint netId)
    {
        _writer.Reset();
        new Despawn { NetId = netId }.WriteMessage(_writer);
        Send(session);
    }

    private static (byte Health, uint Count, ThingFlags Flags) Look(WorldObject thing, PlayerSession session) => thing switch
    {
        Deployable d => ((byte)Math.Clamp(Math.Ceiling(d.Health / d.Def.Health * 255), 0, 255), 0u,
            (d.Lit ? ThingFlags.Lit : ThingFlags.None) | (d.Owner == session.EntityId ? ThingFlags.Mine : ThingFlags.None)),
        DroppedItem item => (255, (uint)Math.Max(0, item.Stack.Count), ThingFlags.None),
        _ => (255, 0u, ThingFlags.None),
    };

    private void FinishThings()
    {
        _thingsGone.Clear();
        _naturalsToSend.Clear();
    }

    // ---------------------------------------------------------------- natural objects

    private static (int X, int Z) NatureCell(Vector3 p) =>
        ((int)MathF.Floor(p.X / PhysicsWorld.TileSize), (int)MathF.Floor(p.Z / PhysicsWorld.TileSize));

    // Keeps the index of overrides by area up to date for an object that changed.
    private void IndexOverride(ulong id)
    {
        if (!Zone.Nature!.TryGet(id, out NaturalObject o))
        {
            return;
        }
        (int, int) cell = NatureCell(o.Position);
        if (Zone.Natural!.TryGetOverride(id, out _))
        {
            if (!_overrideCells.TryGetValue(cell, out HashSet<ulong>? set))
            {
                set = new HashSet<ulong>();
                _overrideCells.Add(cell, set);
            }
            set.Add(id);
        }
        else if (_overrideCells.TryGetValue(cell, out HashSet<ulong>? set))
        {
            set.Remove(id);
        }
    }

    // The changed natural objects of every area that came into range, then those that changed since the last snapshot.
    private void ReplicateNature(PlayerSession session, Vector3 centre)
    {
        _naturalBatch.Clear();
        float radius = _options.RelevanceRadius;
        int reach = (int)MathF.Ceiling(radius / PhysicsWorld.TileSize);
        (int cx, int cz) = NatureCell(centre);
        for (int z = cz - reach; z <= cz + reach; z++)
        {
            for (int x = cx - reach; x <= cx + reach; x++)
            {
                float dx = (x + 0.5f) * PhysicsWorld.TileSize - centre.X, dz = (z + 0.5f) * PhysicsWorld.TileSize - centre.Z;
                if (dx * dx + dz * dz > (radius + PhysicsWorld.TileSize) * (radius + PhysicsWorld.TileSize) || !session.NatureCells.Add((x, z)))
                {
                    continue;
                }
                if (_overrideCells.TryGetValue((x, z), out HashSet<ulong>? set))
                {
                    foreach (ulong id in set)
                    {
                        _naturalBatch.Add(NaturalOf(id));
                    }
                }
            }
        }
        _cellsLeaving.Clear();
        float far = radius + 3 * PhysicsWorld.TileSize;
        foreach ((int x, int z) in session.NatureCells)
        {
            float dx = (x + 0.5f) * PhysicsWorld.TileSize - centre.X, dz = (z + 0.5f) * PhysicsWorld.TileSize - centre.Z;
            if (dx * dx + dz * dz > far * far)
            {
                _cellsLeaving.Add((x, z));
            }
        }
        foreach ((int, int) cell in _cellsLeaving)
        {
            session.NatureCells.Remove(cell);
        }
        foreach (ulong id in _naturalsToSend)
        {
            if (Zone.Nature!.TryGet(id, out NaturalObject o) && session.NatureCells.Contains(NatureCell(o.Position)))
            {
                _naturalBatch.Add(NaturalOf(id));
            }
        }
        for (int start = 0; start < _naturalBatch.Count; start += 64)
        {
            int count = Math.Min(64, _naturalBatch.Count - start);
            _writer.Reset();
            new NaturalStates { States = _naturalBatch.GetRange(start, count).ToArray() }.WriteMessage(_writer);
            Send(session);
        }
    }

    private NaturalNet NaturalOf(ulong id)
    {
        if (Zone.Natural!.TryGetOverride(id, out NaturalOverride o) && Zone.Nature!.TryGet(id, out NaturalObject obj) &&
            Content.Natural(obj.Kind) is { } kind)
        {
            float max = kind.IsHarvested ? NaturalShapes.MaxHealth(obj, kind) : 1;
            byte health = kind.IsHarvested ? (byte)Math.Clamp(Math.Round(o.Health / max * 255), 0, 255) : (byte)(o.Depleted ? 0 : 255);
            return new NaturalNet
            {
                Id = id, Health = health, HasWeakSpot = o.HasWeakSpot, WeakSpotYaw = o.WeakSpotYaw, WeakSpotLift = o.WeakSpotLift, Depleted = o.Depleted,
            };
        }
        return new NaturalNet { Id = id, Health = 255 };
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

    public void Count(in ZoneEvent e)
    {
        switch (e.Kind)
        {
            case ZoneEventKind.ItemsGained when e.Reason == GainReason.Gathered:
                Gathered += e.Count;
                break;
            case ZoneEventKind.ItemsGained when e.Reason == GainReason.PickedUp:
                PickedUp += e.Count;
                break;
            case ZoneEventKind.HitConfirmed when e.Target == HitTarget.Natural:
                GatherHits++;
                WeakSpotHits += e.Flag ? 1 : 0;
                break;
            case ZoneEventKind.HitConfirmed when e.Target == HitTarget.Character:
                CharacterHits++;
                Kills += e.Flag ? 1 : 0;
                Downed += e.Downed ? 1 : 0;
                break;
            case ZoneEventKind.Recovered:
                Recovered++;
                break;
            case ZoneEventKind.CraftFinished:
                Crafted += e.Count;
                break;
            case ZoneEventKind.Died:
                Deaths++;
                break;
            case ZoneEventKind.ProjectileLaunched when e.Seq != 0:
                Shots++;
                break;
            case ZoneEventKind.ProjectileLaunched:
                Arrows++;
                break;
            case ZoneEventKind.ProjectileEnded when e.Seq != 0 && e.Target == HitTarget.Character:
                BulletHits++;
                break;
        }
    }

    public override string ToString() =>
        $"{GatherHits} gathering hits ({WeakSpotHits} on the weak spot) for {Gathered} items, {PickedUp} items picked up, " +
        $"{Crafted} crafted, {Arrows} arrows, {Shots} shots ({BulletHits} hit someone), {CharacterHits} hits on players, {Kills} kills, " +
        $"{Downed} knocked down ({Recovered} up again), {Deaths} deaths";
}
