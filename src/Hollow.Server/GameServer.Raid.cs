using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Persistence;
using Hollow.Server.Notifications;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Raid;
using LiteNetLib;

namespace Hollow.Server;

// Raiding on the server (GROUND.md §6.6, G5): throw and place intents (a dud is lit again by a verb); explosives replicated to everyone in range
// with their arc and fuse; detonations to everyone within hearing; claimed bases hurt by raids handed to the notification
// service, which alerts the players they list in the game, or queues the alert for those offline and hands it over when
// they log in; charges saved with their fuse left; and the raid load test's bases.
public sealed partial class GameServer : IPlayerPresence
{
    private readonly ConditionalWeakTable<PlayerSession, Dictionary<ulong, uint>> _explosivesSent = new();
    private readonly Dictionary<ulong, LiveExplosive> _explosivesNow = new();
    private readonly List<ulong> _explosivesLeaving = new();
    private readonly List<(PlayerSession Session, EntityId Player, Task<IReadOnlyList<RaidAlert>> Alerts)> _alertDeliveries = new();
    private readonly List<ThingRecord> _storedExplosives = new();
    private NotificationService? _notifications;
    private RaidArena? _arena;

    /// <summary>The notification service: raid alerts (ARCHITECTURE.md §14).</summary>
    public NotificationService Notifications => _notifications!;

    /// <summary>The raid load test's bases (<see cref="ServerOptions.RaidBases"/>), or null.</summary>
    public RaidArena? Arena => _arena;

    private void StartRaid()
    {
        RaidAlertSettings settings = Content.RaidAlerts;
        INotificationStore store = _persistence != null
            ? new DatabaseNotificationStore(_persistence, settings.MaxQueued)
            : new MemoryNotificationStore(settings.MaxQueued);
        _notifications = new NotificationService(this, store, settings, _options.Push);
        if (_options.RaidBases > 0)
        {
            _arena = new RaidArena(Zone, _options.RaidBases, _options.RaidRadius, _options.RaidBasePrefix);
            Log($"raid arena: {_arena.Count} huts {_options.RaidRadius:0} m around the origin");
        }
    }

    // ---------------------------------------------------------------- intents

    private bool ReceiveRaidIntent(PlayerSession session, uint type)
    {
        Entity me = session.Entity;
        switch (type)
        {
            case ThrowIntent.TypeId:
            {
                ThrowIntent m = ThrowIntent.Read(_reader);
                return !_reader.Failed && Intent(session, m.Seq,
                    () => Zone.Explosives().ThrowExplosive(me, m.Slot, Expected(m.Expected), Quantize.FromAngle16(m.Yaw), Quantize.FromAngle16(m.Pitch), m.Lob));
            }
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- after each step

    // Raid hits to the notification service; once a second, the alerts that waited for their base's cooldown; queued
    // alerts handed over to players who just logged in; the arena's bases kept standing.
    private void AfterRaid()
    {
        foreach (RaidHit hit in Zone.Explosives().RaidHits)
        {
            _notifications!.Raid(hit);
        }
        Zone.Explosives().RaidHits.Clear();
        if (Zone.Tick % TickRate == 0)
        {
            _notifications!.Tick(Zone.UnixMilliseconds);
        }
        for (int i = _alertDeliveries.Count - 1; i >= 0; i--)
        {
            (PlayerSession session, EntityId player, Task<IReadOnlyList<RaidAlert>> alerts) = _alertDeliveries[i];
            if (!alerts.IsCompleted)
            {
                continue;
            }
            _alertDeliveries.RemoveAt(i);
            if (!alerts.IsCompletedSuccessfully || alerts.Result.Count == 0 || !_sessions.ContainsValue(session) || session.EntityId != player)
            {
                continue;
            }
            List<RaidAlert> all = alerts.Result.ToList();
            for (int start = Math.Max(0, all.Count - 64); start < all.Count; start += 64)
            {
                _writer.Reset();
                new RaidAlertList { Alerts = all.Skip(start).Take(64).Select(Net).ToArray() }.WriteMessage(_writer);
                Send(session);
            }
            Log($"{session.Name}: {all.Count} raid alert(s) waited while they were away");
        }
        _arena?.Tick();
    }

    /// <summary>A player joined: the raid alerts that waited for them are fetched, and sent when they arrive.</summary>
    private void QueueAlertDelivery(PlayerSession session)
    {
        _alertDeliveries.Add((session, session.EntityId, _notifications!.TakeQueued(session.EntityId, Zone.UnixMilliseconds)));
        _arena?.Joined(Zone, session.Name, session.EntityId);
    }

    bool IPlayerPresence.Deliver(EntityId player, RaidAlert alert)
    {
        if (!_sessionOf.TryGetValue(player, out PlayerSession? session) || !session.Playing)
        {
            return false;
        }
        _writer.Reset();
        new RaidAlertMessage { Alert = Net(alert) }.WriteMessage(_writer);
        Send(session);
        return true;
    }

    private static RaidAlertNet Net(RaidAlert a) => new()
    {
        Base = a.Base.Value,
        X = a.Where.X,
        Y = a.Where.Y,
        Z = a.Where.Z,
        FirstUnixMs = (ulong)Math.Max(0, a.FirstUnixMs),
        LastUnixMs = (ulong)Math.Max(0, a.LastUnixMs),
        Hits = (uint)Math.Max(0, a.Hits),
        Destroyed = (uint)Math.Max(0, a.Destroyed),
        What = a.What.Length > 150 ? a.What[..150] : a.What,
        Item = a.Item,
    };

    // An explosion, to everyone within hearing (twice the relevance radius): reliable, but an explosive round's (many,
    // and only cosmetic) unreliable.
    private void SendDetonation(in ZoneEvent e)
    {
        _writer.Reset();
        new Detonation
        {
            Id = e.Other.Value,
            Item = e.Item,
            Owner = NetIdOf(e.Player),
            X = e.Position.X,
            Y = e.Position.Y,
            Z = e.Position.Z,
            NormalX = e.Velocity.X,
            NormalY = e.Velocity.Y,
            NormalZ = e.Velocity.Z,
            Radius = Math.Clamp(e.Amount, 0, 20),
            Target = e.Target.Family,
            Round = e.Flag,
        }.WriteMessage(_writer);
        float hearing = 2 * _options.RelevanceRadius;
        DeliveryMethod method = e.Flag ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered;
        foreach (PlayerSession session in _sessions.Values)
        {
            if (session.Playing && Vector3.DistanceSquared(Zone.Entities.Get<Character>(session.Entity).State.Position, e.Position) <= hearing * hearing)
            {
                session.Peer.Send(_writer.Data, method);
            }
        }
    }

    private uint NetIdOf(EntityId id) =>
        !id.IsNone && Zone.Entities.TryFind(id, out Entity entity) && Zone.Entities.Has<NetPresence>(entity) ? Zone.Entities.Get<NetPresence>(entity).NetId : 0;

    // ---------------------------------------------------------------- replication

    private void PrepareExplosives()
    {
        _explosivesNow.Clear();
        foreach (LiveExplosive e in Zone.Explosives().All)
        {
            _explosivesNow[e.Id.Value] = e;
        }
    }

    // The explosives within the player's range: each when it appears and whenever it changes; one gone or out of range
    // is said gone (a detonation has already said so to those near).
    private void ReplicateExplosives(PlayerSession session, Vector3 centre)
    {
        Dictionary<ulong, uint> sent = _explosivesSent.GetValue(session, _ => new Dictionary<ulong, uint>());
        float radius = _options.RelevanceRadius;
        _explosivesLeaving.Clear();
        foreach (ulong id in sent.Keys)
        {
            if (!_explosivesNow.TryGetValue(id, out LiveExplosive? e) || Vector3.DistanceSquared(e.Position, centre) > (radius + 20) * (radius + 20))
            {
                _explosivesLeaving.Add(id);
            }
        }
        foreach (ulong id in _explosivesLeaving)
        {
            sent.Remove(id);
            _writer.Reset();
            new ExplosiveGone { Id = id }.WriteMessage(_writer);
            Send(session);
        }
        foreach (LiveExplosive e in _explosivesNow.Values)
        {
            if (Vector3.DistanceSquared(e.Position, centre) > radius * radius ||
                (sent.TryGetValue(e.Id.Value, out uint revision) && revision == e.Revision))
            {
                continue;
            }
            sent[e.Id.Value] = e.Revision;
            _writer.Reset();
            new ExplosiveUpdate
            {
                Id = e.Id.Value,
                Item = e.Item.Id,
                Owner = NetIdOf(e.Owner),
                State = e.State,
                Dud = e.Dud,
                X = e.ArcStart.X,
                Y = e.ArcStart.Y,
                Z = e.ArcStart.Z,
                VelocityX = e.ArcVelocity.X,
                VelocityY = e.ArcVelocity.Y,
                VelocityZ = e.ArcVelocity.Z,
                NormalX = e.Normal.X,
                NormalY = e.Normal.Y,
                NormalZ = e.Normal.Z,
                StateTick = (uint)Math.Max(0, e.StateTick),
                FuseEndTick = e.Burning ? (uint)Math.Max(1, e.FuseEndTick) : 0,
                FuseTicks = (uint)Math.Max(0, e.FuseTicks),
            }.WriteMessage(_writer);
            Send(session);
        }
    }

    // ---------------------------------------------------------------- saving and loading

    // Explosives go with the things: stuck and lying charges, duds, and those in the air (they carry on falling).
    private void AddExplosivesToSave(ZoneSave save, HashSet<EntityId> current)
    {
        foreach (LiveExplosive e in Zone.Explosives().All)
        {
            if (e.Gone)
            {
                continue;
            }
            save.Things.Add(new ThingRecord
            {
                Id = e.Id,
                SystemAddress = Spec.SystemAddress,
                BodyPath = Spec.BodyPath,
                Kind = ExplosiveCodec.ThingKind,
                Data = ExplosiveCodec.Encode(Zone.Explosives().SaveOf(e), Zone.Frame.ToBody),
            });
            current.Add(e.Id);
        }
    }

    // A stored explosive waits until the rest of the world is loaded (what it is stuck to must be there first).
    private bool StoredExplosive(ThingRecord t)
    {
        if (t.Kind != ExplosiveCodec.ThingKind)
        {
            return false;
        }
        _storedExplosives.Add(t);
        _savedThings.Add(t.Id);
        return true;
    }

    private void RestoreExplosives()
    {
        int restored = 0;
        foreach (ThingRecord t in _storedExplosives)
        {
            try
            {
                if (Zone.Explosives().RestoreExplosive(ExplosiveCodec.Decode(t.Id, t.Data, Zone.Frame.ToLocal)))
                {
                    restored++;
                }
            }
            catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException)
            {
                Log($"explosive {t.Id} doesn't load ({e.Message}); left out");
            }
        }
        if (_storedExplosives.Count > 0)
        {
            Log($"restored {restored} of {_storedExplosives.Count} explosives with their fuses");
        }
        _storedExplosives.Clear();
    }
}

/// <summary>
/// The raid load test's bases (GROUND.md §5.5): stone huts with sheet metal doors on a ring around the zone's origin; the
/// player called <c>{prefix}{n:000}</c> is listed on hut <c>(n − 1) mod huts</c> once they join (so raids on it alert them;
/// with half as many huts as bots, two to a hut), and they are kept standing: whatever a raid broke is put back every
/// 20 s, a hut whose foundation went is built again.
/// </summary>
public sealed class RaidArena
{
    private const double RestoreSeconds = 20;

    private readonly TestBases.Hut?[] _huts;
    private readonly float _radius;
    private readonly string _prefix;
    private readonly Zone _zone;
    private double _nextRestore;

    public RaidArena(Zone zone, int count, float radius, string prefix)
    {
        _zone = zone;
        _radius = radius;
        _prefix = prefix;
        _huts = new TestBases.Hut?[count];
        for (int i = 0; i < count; i++)
        {
            _huts[i] = Build(i);
        }
        _nextRestore = zone.Building().BuildingNow + RestoreSeconds;
    }

    public int Count => _huts.Count(h => h != null);

    /// <summary>Huts rebuilt or repaired so far.</summary>
    public int Restored { get; private set; }

    public TestBases.Hut? this[int index] => _huts[index];

    private TestBases.Hut? Build(int i)
    {
        (float x, float z, double heading) = TestBases.Ring(i, _huts.Length, _radius);
        TestBases.Hut? hut = TestBases.Build(_zone, _zone.GroundAt(x, z), heading, grade: 2, owner: 0);
        if (hut?.Claim is { } claim)
        {
            // Nobody's yet: the bots are listed as they join.
            _zone.Building().World!.Deauthorise(0, claim);
        }
        return hut;
    }

    /// <summary>A player joined: if they are a hut's bot, the hut lists them.</summary>
    public void Joined(Zone zone, string name, EntityId player)
    {
        if (name.StartsWith(_prefix, StringComparison.Ordinal) && int.TryParse(name[_prefix.Length..], out int n) && n >= 1 &&
            _huts[(n - 1) % _huts.Length]?.Claim is { Gone: false } claim)
        {
            zone.Building().World!.Authorise(player.Value, claim);
        }
    }

    public void Tick()
    {
        if (_zone.Building().BuildingNow < _nextRestore)
        {
            return;
        }
        _nextRestore = _zone.Building().BuildingNow + RestoreSeconds;
        for (int i = 0; i < _huts.Length; i++)
        {
            TestBases.Hut? hut = _huts[i];
            if (hut != null && hut.Whole)
            {
                continue;
            }
            if (hut != null && TestBases.Restore(_zone, hut))
            {
                if (hut.Claim is { } restored)
                {
                    _zone.Building().World!.Deauthorise(0, restored);
                }
            }
            else
            {
                IReadOnlyList<ulong> listed = hut?.Claim?.Authorised ?? Array.Empty<ulong>();
                _huts[i] = Build(i);
                if (_huts[i]?.Claim is { } claim)
                {
                    foreach (ulong player in listed)
                    {
                        _zone.Building().World!.Authorise(player, claim);
                    }
                }
            }
            Restored++;
        }
    }
}
