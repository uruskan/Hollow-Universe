using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hollow.Persistence;

/// <summary>
/// Owns the <see cref="GameDatabase"/> on one background thread and runs every request in the order it was made
/// (ARCHITECTURE.md §13): the zone's tick never waits for the disk, and a login made after a save always reads what
/// that save wrote. Disposing writes everything still queued, then closes the database.
/// </summary>
public sealed class PersistenceWorker : IDisposable
{
    private readonly BlockingCollection<Action<GameDatabase>> _queue = new();
    private readonly Thread _thread;
    private readonly Func<long> _unixMilliseconds;
    private readonly Action<string> _log;
    private GameDatabase? _database;
    private bool _disposed;

    public PersistenceWorker(string path, Func<long> unixMilliseconds, Action<string>? log = null, TerrainIdentity? terrain = null)
    {
        _unixMilliseconds = unixMilliseconds;
        _log = log ?? (_ => { });
        // Open here, so an incompatible terrain, bad path or newer schema fails before the worker or server starts.
        _database = GameDatabase.Open(path, terrain);
        _thread = new Thread(Run) { IsBackground = true, Name = "Hollow persistence" };
        _thread.Start();
    }

    /// <summary>Characters written so far, and the time those writes took.</summary>
    public long CharactersSaved { get; private set; }

    public double SaveMilliseconds { get; private set; }

    /// <summary>The account of a name and its character (null when new).</summary>
    public Task<(long AccountId, CharacterRecord? Character)> Login(string name) =>
        Enqueue(db =>
        {
            long account = db.GetOrCreateAccount(name, _unixMilliseconds());
            return (account, db.LoadCharacter(account));
        });

    /// <summary>Queues characters to be written in one transaction.</summary>
    public Task Save(IReadOnlyList<CharacterRecord> records) =>
        Enqueue(db =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            db.SaveCharacters(records);
            CharactersSaved += records.Count;
            SaveMilliseconds += watch.Elapsed.TotalMilliseconds;
            return true;
        });

    /// <summary>Everything stored about a body's zone (read at start, before any player arrives).</summary>
    public Task<ZoneRecord> LoadZone(ulong systemAddress, uint bodyPath) => Enqueue(db => db.LoadZone(systemAddress, bodyPath));

    /// <summary>Queues a zone's save: one transaction.</summary>
    public Task SaveZone(ZoneSave save) =>
        Enqueue(db =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            db.SaveZone(save);
            CharactersSaved += save.Characters.Count;
            ThingsSaved += save.Things.Count;
            SaveMilliseconds += watch.Elapsed.TotalMilliseconds;
            return true;
        });

    /// <summary>Things written so far.</summary>
    public long ThingsSaved { get; private set; }

    /// <summary>Queues a notification for a player who is offline (G5: a raid alert), keeping at most <paramref name="keep"/> of theirs.</summary>
    public Task QueueNotification(ulong player, byte kind, long createdAt, byte[] data, int keep) =>
        Enqueue(db =>
        {
            db.QueueNotification(player, kind, createdAt, data, keep);
            return true;
        });

    /// <summary>A player's waiting notifications (made at or after <paramref name="since"/>), taken from the queue: at login.</summary>
    public Task<List<(byte Kind, long CreatedAt, byte[] Data)>> TakeNotifications(ulong player, long since) =>
        Enqueue(db => db.TakeNotifications(player, since));

    /// <summary>Completes once everything queued before it is written.</summary>
    public Task Flush() => Enqueue(_ => true);

    private Task<T> Enqueue<T>(Func<GameDatabase, T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(db =>
            {
                try
                {
                    done.SetResult(work(db));
                }
                catch (Exception e)
                {
                    _log($"persistence: {e.Message}");
                    done.SetException(e);
                }
            });
        }
        catch (InvalidOperationException)
        {
            done.SetException(new ObjectDisposedException(nameof(PersistenceWorker)));
        }
        return done.Task;
    }

    private void Run()
    {
        foreach (Action<GameDatabase> job in _queue.GetConsumingEnumerable())
        {
            job(_database!);
        }
        _database!.Dispose();
        _database = null;
    }

    /// <summary>Writes what is queued and closes the database (waits for it).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _queue.CompleteAdding();
        _thread.Join();
        _queue.Dispose();
    }
}
