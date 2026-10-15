using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Persistence;
using Hollow.Server.Notifications;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Raid;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>
/// Raiding on the server (GROUND.md §6.6, G5): throw and place intents (a dud is lit again by a verb); explosives replicated to everyone in range
/// with their arc and fuse; detonations to everyone within hearing; claimed bases hurt by raids handed to the notification
/// service, which alerts the players they list in the game, or queues the alert for those offline and hands it over when
/// they log in; charges saved with their fuse left; and the raid load test's bases.
/// </summary>
public sealed class RaidModule : ServerModule, IPlayerPresence
{
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

    /// <summary>The explosives this player has, by ID, with the revision sent.</summary>
    private sealed class ExplosiveView
    {
        public readonly Dictionary<ulong, uint> Sent = new();
    }

    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        handlers.On<ThrowIntent>((PlayerSession session, in ThrowIntent message) =>
        {
            ThrowIntent m = message;
            Entity me = session.Entity;
            (float yaw, float pitch) = ViewAt(session, Quantize.FromOctahedral(m.ViewU, m.ViewV));
            Intent(session, m.Seq, () => Zone.Explosives().ThrowExplosive(me, m.Slot, Expected(m.Expected), yaw, pitch, m.Lob));
        });
        Zone.Events.On<Detonated>((in Detonated e) => SendDetonation(e));
    }

    protected internal override void Start()
    {
        RaidAlertSettings settings = Content.RaidAlerts;
        INotificationStore store = Persistence != null
            ? new DatabaseNotificationStore(Persistence, settings.MaxQueued)
            : new MemoryNotificationStore(settings.MaxQueued);
        _notifications = new NotificationService(this, store, settings, Options.Push);
        if (Options.RaidBases > 0)
        {
            // Players arriving on beaches anywhere on the world find the hut they raid beside their beach.
            bool beaches = !Options.SpawnAtOrigin && Server.Beaches is { Points.Count: > 0 };
            _arena = new RaidArena(Zone, Options.RaidBases, Options.RaidRadius, Options.RaidBasePrefix, beaches ? Server.ArrivalOf : null);
            Log($"raid arena: {_arena.Count} huts " + (beaches ? "beside the beaches where their raiders arrive" : $"{Options.RaidRadius:0} m around the landing site"));
        }
    }

    // ---------------------------------------------------------------- after each step

    // Raid hits to the notification service; once a second, the alerts that waited for their base's cooldown; queued
    // alerts handed over to players who just logged in; the arena's bases kept standing.
    protected internal override void AfterStep()
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
            if (!alerts.IsCompletedSuccessfully || alerts.Result.Count == 0 || !Players.Contains(session) || session.EntityId != player)
            {
                continue;
            }
            List<RaidAlert> all = alerts.Result.ToList();
            for (int start = Math.Max(0, all.Count - 64); start < all.Count; start += 64)
            {
                Writer.Reset();
                new RaidAlertList { Alerts = all.Skip(start).Take(64).Select(Net).ToArray() }.WriteMessage(Writer);
                Send(session);
            }
            Log($"{session.Name}: {all.Count} raid alert(s) waited while they were away");
        }
        _arena?.Tick();
    }

    /// <summary>A player joined: the raid alerts that waited for them are fetched, and sent when they arrive.</summary>
    protected internal override void Joined(PlayerSession session, bool newCharacter)
    {
        _alertDeliveries.Add((session, session.EntityId, _notifications!.TakeQueued(session.EntityId, Zone.UnixMilliseconds)));
        _arena?.Joined(Zone, session.Name, session.EntityId);
    }

    bool IPlayerPresence.Deliver(EntityId player, RaidAlert alert)
    {
        if (SessionOf(player) is not { } session || !session.Playing)
        {
            return false;
        }
        Writer.Reset();
        new RaidAlertMessage { Alert = Net(alert) }.WriteMessage(Writer);
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
    private void SendDetonation(in Detonated e)
    {
        Writer.Reset();
        new Detonation
        {
            Id = e.Explosive.Value,
            Item = e.Item,
            Owner = NetIdOf(e.Player),
            X = e.Position.X,
            Y = e.Position.Y,
            Z = e.Position.Z,
            NormalX = e.Normal.X,
            NormalY = e.Normal.Y,
            NormalZ = e.Normal.Z,
            Radius = Math.Clamp(e.Radius, 0, 20),
            Target = e.On.Family,
            Round = e.Round,
        }.WriteMessage(Writer);
        float hearing = 2 * Options.RelevanceRadius;
        DeliveryMethod method = e.Round ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered;
        foreach (PlayerSession session in Players)
        {
            if (session.Playing && Vector3.DistanceSquared(Zone.Entities.Get<Character>(session.Entity).State.Position, e.Position) <= hearing * hearing)
            {
                session.Peer.Send(Writer.Data, method);
            }
        }
    }

    // ---------------------------------------------------------------- replication

    protected internal override void Prepare() => PrepareExplosives();

    private void PrepareExplosives()
    {
        _explosivesNow.Clear();
        foreach (LiveExplosive e in Zone.Explosives().All)
        {
            _explosivesNow[e.Id.Value] = e;
        }
    }

    // The explosives within the player's range: each when it appears and whenever it changes; one gone or out of range
    // is said gone (a detonation has already said so to those near). Timed with the building's, as the profile has it.
    protected internal override void Replicate(PlayerSession session, Vector3 centre)
    {
        using (Profile.Time(ProfileSection.ReplicateBuilding))
        {
            ReplicateExplosives(session, centre);
        }
    }

    private void ReplicateExplosives(PlayerSession session, Vector3 centre)
    {
        Dictionary<ulong, uint> sent = session.State<ExplosiveView>().Sent;
        float radius = Options.RelevanceRadius;
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
            Writer.Reset();
            new ExplosiveGone { Id = id }.WriteMessage(Writer);
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
            Writer.Reset();
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
            }.WriteMessage(Writer);
            Send(session);
        }
    }

    // ---------------------------------------------------------------- saving and loading

    // Explosives go with the things: stuck and lying charges, duds, and those in the air (they carry on falling).
    protected internal override void Save(ZoneSave save, HashSet<EntityId> current)
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

    // A stored explosive comes back once the rest of the world is loaded (what it is stuck to must be there first).
    protected internal override void Restore(ZoneRecord stored, HashSet<EntityId> saved)
    {
        foreach (ThingRecord t in stored.Things)
        {
            if (t.Kind == ExplosiveCodec.ThingKind)
            {
                _storedExplosives.Add(t);
                saved.Add(t.Id);
            }
        }
        RestoreExplosives();
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
/// The raid load test's bases (GROUND.md §5.5): stone huts with sheet metal doors on a ring round the landing site, or,
/// when players arrive on beaches anywhere on the world, each beside the beach where the bot that raids it arrives (the
/// bot called <c>{prefix}{n:000}</c> raids hut <c>n mod huts</c>), its doorway facing that beach. The player called
/// <c>{prefix}{n:000}</c> is listed on hut <c>(n − 1) mod huts</c> once they join (so raids on it alert them; with half as
/// many huts as bots, two to a hut), and the huts are kept standing: whatever a raid broke is put back every 20 s, a hut
/// whose foundation went is built again.
/// </summary>
public sealed class RaidArena
{
    private const double RestoreSeconds = 20;

    /// <summary>
    /// A hut beside a beach stands this far inland of it, so the raider's furthest stand (its rockets', 16.7 m out from
    /// the hut's middle) stays on the shore.
    /// </summary>
    public const float BesideBeach = 20f;

    private readonly TestBases.Hut?[] _huts;
    private readonly (Vector3 At, double Heading)[] _sites;
    private readonly string _prefix;
    private readonly Zone _zone;
    private double _nextRestore;

    /// <param name="arrival">
    /// Where a player of a name first arrives, when players arrive on beaches anywhere on the world (the huts then stand
    /// beside their raiders' beaches); null puts them on a ring of <paramref name="radius"/> metres round the landing site.
    /// </param>
    public RaidArena(Zone zone, int count, float radius, string prefix, Func<string, Vector3>? arrival = null)
    {
        _zone = zone;
        _prefix = prefix;
        _huts = new TestBases.Hut?[count];
        _sites = new (Vector3, double)[count];
        var taken = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            // A ring round the landing site (docs/WHOLE_WORLD.md §6.7), or beside the beach of the bot that raids the hut.
            _sites[i] = arrival == null
                ? TestBases.Ring(zone.Ground, zone.Landing, i, count, radius)
                : TestBases.Beside(zone.Ground, arrival($"{prefix}{(i == 0 ? count : i):000}"), BesideBeach, taken, 9f);
            taken.Add(_sites[i].At);
            // Their ground, on worker threads all at once, before the huts are built on it.
            zone.Physics.PrefetchWithin(_sites[i].At, PhysicsWorld.EnsureReach);
        }
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
        (Vector3 at, double heading) = _sites[i];
        TestBases.Hut? hut = TestBases.Build(_zone, at, heading, grade: 2, owner: 0);
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

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): raiding, the notification service and the raid arena.</summary>
public static class RaidModuleAccess
{
    public static RaidModule Raid(this GameServer server) => server.Require<RaidModule>();
}
