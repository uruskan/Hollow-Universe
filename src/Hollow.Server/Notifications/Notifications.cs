using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Hollow.Persistence;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Raid;

namespace Hollow.Server.Notifications;

/// <summary>
/// A raid alert as a player gets it (GROUND.md §6.5): which base (its claim device), where it stands (zone frame), when
/// it was first and last hit in this alert's window (Unix ms), how many pieces and doors were hit and broken, what (the
/// most telling first) and with what (the explosive or weapon's item).
/// </summary>
public sealed record RaidAlert
{
    public EntityId Base { get; init; }

    public Vector3 Where { get; init; }

    public long FirstUnixMs { get; init; }

    public long LastUnixMs { get; init; }

    public int Hits { get; init; }

    public int Destroyed { get; init; }

    public string What { get; init; } = "";

    public ushort Item { get; init; }
}

/// <summary>
/// The notification service (ARCHITECTURE.md §14): raid alerts now, "your miner is full" later; a phone app's push later
/// behind <see cref="IPushChannel"/>. An interface, so it can move to its own process.
/// </summary>
public interface INotificationService
{
    /// <summary>A claimed base was hurt by a raid: every player it lists is alerted (at most once a cooldown per base).</summary>
    void Raid(in RaidHit hit);

    /// <summary>Sends the alerts that waited for their base's cooldown (call about once a second).</summary>
    void Tick(long unixMs);

    /// <summary>The alerts that waited for a player while they were offline, oldest first, taken from the queue (at login).</summary>
    Task<IReadOnlyList<RaidAlert>> TakeQueued(EntityId player, long unixMs);
}

/// <summary>Who is playing now: an alert to one of them goes out at once, in the game.</summary>
public interface IPlayerPresence
{
    /// <summary>Sends the alert to the player if they are playing now; false if they are offline.</summary>
    bool Deliver(EntityId player, RaidAlert alert);
}

/// <summary>Where alerts wait for players who are offline (the database, or memory without one).</summary>
public interface INotificationStore
{
    void Queue(EntityId player, RaidAlert alert, long unixMs);

    /// <summary>A player's waiting alerts made at or after <paramref name="since"/>, oldest first; all of theirs are taken.</summary>
    Task<IReadOnlyList<RaidAlert>> Take(EntityId player, long since);
}

/// <summary>The seam for push notifications to a phone later (ARCHITECTURE.md §14): told of every alert queued for someone offline.</summary>
public interface IPushChannel
{
    void Push(EntityId player, RaidAlert alert);
}

/// <summary>No push yet.</summary>
public sealed class NoPush : IPushChannel
{
    public static readonly NoPush Instance = new();

    public void Push(EntityId player, RaidAlert alert)
    {
    }
}

/// <summary>What the notification service did (the load test and <c>status</c> report it).</summary>
public sealed class NotificationCounters
{
    /// <summary>Raid hits reported by the zone.</summary>
    public long Hits { get; internal set; }

    /// <summary>Alerts sent (one per base and window), delivered in the game, queued for players offline, pushed.</summary>
    public long Alerts { get; internal set; }

    public long Delivered { get; internal set; }

    public long Queued { get; internal set; }

    public long Pushed { get; internal set; }

    /// <summary>Alerts handed over at login (counted on the persistence worker's thread, hence the interlocked field).</summary>
    public long TakenAtLogin => System.Threading.Interlocked.Read(ref _takenAtLogin);

    internal long _takenAtLogin;

    public override string ToString() =>
        $"{Hits} raid hits made {Alerts} alerts: {Delivered} to players in the game, {Queued} queued for players offline ({TakenAtLogin} handed over at login)";
}

/// <summary>
/// Raid alerts (GROUND.md §6.5): a hit on a claimed base alerts every player its claim device lists at the moment of the
/// hit, rate-limited per base: the first hit sends an alert at once; hits within the cooldown are summed (hits, broken
/// pieces, what, the time of the last) into one alert sent when the cooldown is over. A player playing gets it in the
/// game; one offline gets it queued (and pushed, later), to be listed at login with the base, the time and what was hit.
/// Runs on the server's tick thread.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly Dictionary<EntityId, BaseState> _bases = new();
    private readonly IPlayerPresence _presence;
    private readonly INotificationStore _store;
    private readonly IPushChannel _push;
    private readonly RaidAlertSettings _settings;

    public NotificationService(IPlayerPresence presence, INotificationStore store, RaidAlertSettings settings, IPushChannel? push = null)
    {
        _presence = presence;
        _store = store;
        _settings = settings;
        _push = push ?? NoPush.Instance;
    }

    public NotificationCounters Counters { get; } = new();

    private long CooldownMs => (long)(_settings.CooldownSeconds * 1000);

    public void Raid(in RaidHit hit)
    {
        Counters.Hits++;
        if (!_bases.TryGetValue(hit.Base, out BaseState? state))
        {
            state = new BaseState();
            _bases.Add(hit.Base, state);
        }
        (state.Pending ??= new Window(hit)).Add(hit);
        if (hit.UnixMilliseconds - state.LastSentMs >= CooldownMs)
        {
            Send(state, hit.UnixMilliseconds);
        }
    }

    public void Tick(long unixMs)
    {
        foreach (BaseState state in _bases.Values)
        {
            if (state.Pending != null && unixMs - state.LastSentMs >= CooldownMs)
            {
                Send(state, unixMs);
            }
        }
        // Bases quiet for a long while are forgotten.
        if (_bases.Count > 256)
        {
            foreach (EntityId quiet in _bases.Where(b => b.Value.Pending == null && unixMs - b.Value.LastSentMs > 10 * CooldownMs).Select(b => b.Key).ToList())
            {
                _bases.Remove(quiet);
            }
        }
    }

    public async Task<IReadOnlyList<RaidAlert>> TakeQueued(EntityId player, long unixMs)
    {
        IReadOnlyList<RaidAlert> alerts = await _store.Take(player, unixMs - (long)(_settings.KeepDays * 86_400_000)).ConfigureAwait(false);
        System.Threading.Interlocked.Add(ref Counters._takenAtLogin, alerts.Count);
        return alerts;
    }

    private void Send(BaseState state, long unixMs)
    {
        Window window = state.Pending!;
        state.Pending = null;
        state.LastSentMs = unixMs;
        RaidAlert alert = window.Alert();
        Counters.Alerts++;
        foreach (ulong player in window.Recipients)
        {
            var id = new EntityId(player);
            if (_presence.Deliver(id, alert))
            {
                Counters.Delivered++;
            }
            else
            {
                _store.Queue(id, alert, unixMs);
                _push.Push(id, alert);
                Counters.Queued++;
                Counters.Pushed++;
            }
        }
    }

    private sealed class BaseState
    {
        public long LastSentMs = long.MinValue / 2;
        public Window? Pending;
    }

    // The hits on one base summed up for one alert.
    private sealed class Window(RaidHit first)
    {
        private readonly List<string> _destroyed = new();
        private readonly List<string> _hit = new();
        private long _first = first.UnixMilliseconds;
        private long _last = first.UnixMilliseconds;
        private int _hits;
        private int _broken;
        private ushort _item;
        private Vector3 _where = first.Where;

        public HashSet<ulong> Recipients { get; } = new();

        public void Add(in RaidHit hit)
        {
            _first = Math.Min(_first, hit.UnixMilliseconds);
            _last = Math.Max(_last, hit.UnixMilliseconds);
            _hits += hit.Hits;
            _broken += hit.Destroyed;
            _item = hit.Item;
            _where = hit.Where;
            foreach (ulong player in hit.Authorised ?? Array.Empty<ulong>())
            {
                Recipients.Add(player);
            }
            foreach (string part in (hit.What ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries))
            {
                List<string> into = part.EndsWith(" broken", StringComparison.Ordinal) ? _destroyed : _hit;
                if (!into.Contains(part))
                {
                    into.Add(part);
                }
            }
        }

        public RaidAlert Alert() => new()
        {
            Base = first.Base,
            Where = _where,
            FirstUnixMs = _first,
            LastUnixMs = _last,
            Hits = _hits,
            Destroyed = _broken,
            What = string.Join(", ", _destroyed.Concat(_hit.Where(h => !_destroyed.Contains(h + " broken"))).Take(4)),
            Item = _item,
        };
    }
}

/// <summary>Alerts waiting in memory (a server without a database: tests, the load test).</summary>
public sealed class MemoryNotificationStore(int keep = 50) : INotificationStore
{
    private readonly Dictionary<EntityId, List<(RaidAlert Alert, long At)>> _waiting = new();

    public int Count => _waiting.Values.Sum(l => l.Count);

    public void Queue(EntityId player, RaidAlert alert, long unixMs)
    {
        if (!_waiting.TryGetValue(player, out List<(RaidAlert, long)>? list))
        {
            list = new List<(RaidAlert, long)>();
            _waiting.Add(player, list);
        }
        list.Add((alert, unixMs));
        if (list.Count > keep)
        {
            list.RemoveRange(0, list.Count - keep);
        }
    }

    public Task<IReadOnlyList<RaidAlert>> Take(EntityId player, long since)
    {
        IReadOnlyList<RaidAlert> alerts = _waiting.Remove(player, out List<(RaidAlert Alert, long At)>? list)
            ? list.Where(x => x.At >= since).Select(x => x.Alert).ToList()
            : Array.Empty<RaidAlert>();
        return Task.FromResult(alerts);
    }
}

/// <summary>Alerts waiting in the database (the <c>notifications</c> table), through the persistence worker's thread.</summary>
public sealed class DatabaseNotificationStore(PersistenceWorker worker, int keep) : INotificationStore
{
    /// <summary>The kind of notification a raid alert is stored as.</summary>
    public const byte RaidAlertKind = 1;

    public void Queue(EntityId player, RaidAlert alert, long unixMs) =>
        _ = worker.QueueNotification(player.Value, RaidAlertKind, unixMs, RaidAlertCodec.Encode(alert), keep);

    public async Task<IReadOnlyList<RaidAlert>> Take(EntityId player, long since)
    {
        List<(byte Kind, long CreatedAt, byte[] Data)> rows = await worker.TakeNotifications(player.Value, since).ConfigureAwait(false);
        var alerts = new List<RaidAlert>();
        foreach ((byte kind, long _, byte[] data) in rows)
        {
            if (kind == RaidAlertKind && RaidAlertCodec.TryDecode(data, out RaidAlert? alert))
            {
                alerts.Add(alert!);
            }
        }
        return alerts;
    }
}

/// <summary>A raid alert's stored bytes (versioned; read as untrusted: one that doesn't read is left out).</summary>
public static class RaidAlertCodec
{
    public const byte Version = 1;

    public static byte[] Encode(RaidAlert a)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Version);
            w.Write(a.Base.Value);
            w.Write(a.Where.X);
            w.Write(a.Where.Y);
            w.Write(a.Where.Z);
            w.Write(a.FirstUnixMs);
            w.Write(a.LastUnixMs);
            w.Write(a.Hits);
            w.Write(a.Destroyed);
            w.Write(a.What);
            w.Write(a.Item);
        }
        return stream.ToArray();
    }

    public static bool TryDecode(byte[] data, out RaidAlert? alert)
    {
        alert = null;
        try
        {
            using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            if (r.ReadByte() is < 1 or > Version)
            {
                return false;
            }
            alert = new RaidAlert
            {
                Base = new EntityId(r.ReadUInt64()),
                Where = new Vector3(Finite(r.ReadSingle()), Finite(r.ReadSingle()), Finite(r.ReadSingle())),
                FirstUnixMs = r.ReadInt64(),
                LastUnixMs = r.ReadInt64(),
                Hits = Math.Max(0, r.ReadInt32()),
                Destroyed = Math.Max(0, r.ReadInt32()),
                What = r.ReadString(),
                Item = r.ReadUInt16(),
            };
            return true;
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or ArgumentException)
        {
            return false;
        }
    }

    private static float Finite(float value) => float.IsFinite(value) ? value : 0;
}
