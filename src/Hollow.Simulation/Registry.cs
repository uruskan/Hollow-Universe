using System;
using System.Collections.Generic;
using System.Linq;

namespace Hollow.Simulation;

/// <summary>
/// Handlers found by a list's index (GROUND_SYSTEMS.md §3.5): the list comes from the data (capabilities.json's keys)
/// or, where the wire numbers it already, from an enum (the target families); each entry gets its handler once, at
/// start-up, and <see cref="Seal"/> refuses to go on while an entry has none (when every entry must have one), a handler
/// names an entry that doesn't exist, or two handlers take one entry. After that a lookup is an array index: no switch,
/// no dictionary.
/// </summary>
public sealed class Registry<T> where T : class
{
    private readonly string[] _keys;
    private readonly T?[] _handlers;
    private readonly List<string> _problems = new();
    private readonly bool _complete;
    private readonly bool[] _unused;
    private bool _sealed;

    /// <param name="what">What the handlers are, for the problems ("capabilities").</param>
    /// <param name="keys">The list, in its order: an entry's index is its number.</param>
    /// <param name="complete">Whether every entry must have a handler (else some may have none).</param>
    public Registry(string what, IReadOnlyList<string> keys, bool complete = true)
        : this(what, keys, complete, new bool[keys.Count])
    {
    }

    private Registry(string what, IReadOnlyList<string> keys, bool complete, bool[] unused)
    {
        What = what;
        _keys = keys.ToArray();
        _handlers = new T?[_keys.Length];
        _complete = complete;
        _unused = unused;
        foreach (IGrouping<string, string> twice in _keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            _problems.Add($"{what}: \"{twice.Key}\" is in the list twice");
        }
    }

    /// <summary>A registry over an enum's members (their values are the indices), leaving out <paramref name="except"/>.</summary>
    public static Registry<T> ForEnum<TEnum>(string what, bool complete, params TEnum[] except) where TEnum : struct, Enum
    {
        TEnum[] values = Enum.GetValues<TEnum>();
        int count = values.Max(v => Convert.ToInt32(v)) + 1;
        var keys = new string[count];
        var unused = new bool[count];
        for (int i = 0; i < count; i++)
        {
            keys[i] = $"#{i}";
            unused[i] = true;
        }
        foreach (TEnum v in values)
        {
            int i = Convert.ToInt32(v);
            keys[i] = v.ToString();
            unused[i] = Array.IndexOf(except, v) >= 0;
        }
        return new Registry<T>(what, keys, complete, unused);
    }

    public string What { get; }

    /// <summary>Entries in the list.</summary>
    public int Count => _keys.Length;

    /// <summary>The list's keys, by index.</summary>
    public IReadOnlyList<string> Keys => _keys;

    /// <summary>The index of a key, or −1.</summary>
    public int IndexOf(string key) => Array.IndexOf(_keys, key);

    /// <summary>Gives the entry <paramref name="key"/> its handler (a problem if there is no such entry or it has one).</summary>
    public void Add(string key, T handler) => Add(IndexOf(key), handler, key);

    public void Add(int index, T handler) => Add(index, handler, index.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void Add(int index, T handler, string name)
    {
        if (_sealed)
        {
            throw new InvalidOperationException($"{What}: the registry is sealed; handlers are added at start-up");
        }
        if (index < 0 || index >= _handlers.Length || _unused[index])
        {
            _problems.Add($"{What}: a handler ({handler.GetType().Name}) for \"{name}\", which isn't in the list");
            return;
        }
        if (_handlers[index] is { } other)
        {
            _problems.Add($"{What}: \"{_keys[index]}\" has two handlers ({other.GetType().Name}, {handler.GetType().Name})");
            return;
        }
        _handlers[index] = handler;
    }

    /// <summary>The handler of an entry; null when it has none or the index is outside the list (an untrusted number).</summary>
    public T? Find(int index) => (uint)index < (uint)_handlers.Length ? _handlers[index] : null;

    /// <summary>The handler of an entry that must have one.</summary>
    public T this[int index] => Find(index) ?? throw new KeyNotFoundException($"{What}: no handler for #{index}");

    /// <summary>Everything wrong: entries without a handler (when every one must have one), handlers for no entry, two for one.</summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            var all = new List<string>(_problems);
            if (_complete)
            {
                for (int i = 0; i < _handlers.Length; i++)
                {
                    if (_handlers[i] == null && !_unused[i])
                    {
                        all.Add($"{What}: \"{_keys[i]}\" has no handler");
                    }
                }
            }
            return all;
        }
    }

    /// <summary>Ends start-up: throws listing every problem, else no handler can be added any more.</summary>
    public Registry<T> Seal()
    {
        IReadOnlyList<string> problems = Problems;
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", problems));
        }
        _sealed = true;
        return this;
    }

    /// <summary>Every handler, once each, in the list's order.</summary>
    public IEnumerable<T> Handlers => _handlers.Where(h => h != null).Distinct().Select(h => h!);
}
