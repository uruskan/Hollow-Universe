using System;
using System.Collections.Generic;

namespace HollowUniverse.Common;

/// <summary>
/// Admission metadata for one warm-up catalogue. At most one prefix window is admitted, and its unique file
/// dependencies remain counted until their items are consumed, including files already loaded. Completed cache
/// pins are deliberately not evicted here: this bounds startup lookahead, not total resource residency.
/// </summary>
internal sealed class WarmupAssetAdmission
{
    internal const int DefaultLookahead = 24;
    internal const int MaximumLookahead = 64;
    internal const int MaximumPendingFiles = 64;
    internal const int LoadOnlyChunkFiles = 32;
    private readonly List<WarmItem> _waiting = new();
    private readonly Dictionary<string, int> _pending = new(StringComparer.Ordinal);
    private readonly Queue<string> _queued = new();
    private readonly HashSet<string> _queuedSet = new(StringComparer.Ordinal);
    private readonly HashSet<string> _requested = new(StringComparer.Ordinal);
    private readonly int _lookahead;
    private bool _stopped;

    internal WarmupAssetAdmission(IEnumerable<WarmItem> items, int lookahead = DefaultLookahead)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (lookahead < 1 || lookahead > MaximumLookahead) throw new ArgumentOutOfRangeException(nameof(lookahead));
        _lookahead = lookahead;
        var allFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (WarmItem item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(item.Files);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var files = new List<string>();
            foreach (string file in item.Files)
            {
                if (string.IsNullOrWhiteSpace(file)) throw new ArgumentException("Warm-up dependency paths must not be empty: " + item.Name, nameof(items));
                if (unique.Add(file)) files.Add(file);
                allFiles.Add(file);
            }
            if (!item.IsLoadOnly && files.Count > MaximumPendingFiles)
                throw new ArgumentException($"Warm-up item '{item.Name}' needs {files.Count} distinct files; a draw item may require at most {MaximumPendingFiles}. Split its content explicitly.", nameof(items));
            if (item.IsLoadOnly && files.Count > LoadOnlyChunkFiles)
            {
                int chunks = (files.Count + LoadOnlyChunkFiles - 1) / LoadOnlyChunkFiles;
                for (int offset = 0, part = 1; offset < files.Count; offset += LoadOnlyChunkFiles, part++)
                    _waiting.Add(WarmItem.Load($"{item.Name} ({part}/{chunks})", files.GetRange(offset, Math.Min(LoadOnlyChunkFiles, files.Count - offset)).ToArray()));
            }
            else _waiting.Add(item with { Files = files.ToArray() });
        }
        TotalItems = _waiting.Count;
        TotalFiles = allFiles.Count;
    }

    internal IReadOnlyList<WarmItem> Waiting => _waiting;
    internal int TotalItems { get; }
    internal int TotalFiles { get; }
    internal int AdmittedItems { get; private set; }
    internal int PendingFiles => _pending.Count;
    internal int QueuedAdmittedFiles => _queued.Count;
    internal int RequestedFiles => _requested.Count;
    internal int UnrequestedFiles => TotalFiles - RequestedFiles;

    internal void Admit()
    {
        if (_stopped) return;
        while (AdmittedItems < _lookahead && AdmittedItems < _waiting.Count)
        {
            WarmItem item = _waiting[AdmittedItems];
            int additional = 0;
            foreach (string file in item.Files) if (!_pending.ContainsKey(file)) additional++;
            if (_pending.Count + additional > MaximumPendingFiles) break;
            foreach (string file in item.Files)
            {
                _pending.TryGetValue(file, out int references);
                _pending[file] = references + 1;
                if (!_requested.Contains(file) && _queuedSet.Add(file)) _queued.Enqueue(file);
            }
            AdmittedItems++;
        }
    }

    internal bool TryRequest(out string file)
    {
        if (_stopped || !_queued.TryDequeue(out string? next)) { file = ""; return false; }
        _queuedSet.Remove(next);
        _requested.Add(next);
        file = next;
        return true;
    }

    internal bool TryTakeReady(Func<string, bool> ready, out WarmItem? item)
    {
        if (!_stopped)
            for (int i = 0; i < AdmittedItems; i++)
            {
                WarmItem candidate = _waiting[i];
                bool complete = true;
                foreach (string file in candidate.Files) if (!ready(file)) { complete = false; break; }
                if (!complete) continue;
                _waiting.RemoveAt(i);
                AdmittedItems--;
                foreach (string file in candidate.Files)
                {
                    int references = _pending[file] - 1;
                    if (references == 0) _pending.Remove(file); else _pending[file] = references;
                }
                item = candidate;
                return true;
            }
        item = null;
        return false;
    }

    /// <summary>The owner must drain previous in-flight requests before admitting a replacement prefix.</summary>
    internal void Requeue(IReadOnlyList<WarmItem> items)
    {
        if (_stopped || items.Count == 0) return;
        _waiting.InsertRange(0, items);
        AdmittedItems = 0;
        _pending.Clear();
        _queued.Clear();
        _queuedSet.Clear();
        // Requests already started remain known; completed/error cache entries are still owned by RenderWarmup.
    }

    internal void Stop()
    {
        _stopped = true;
        _queued.Clear();
        _queuedSet.Clear();
    }

    internal void Clear()
    {
        Stop();
        _waiting.Clear();
        _pending.Clear();
        _requested.Clear();
        AdmittedItems = 0;
    }
}
