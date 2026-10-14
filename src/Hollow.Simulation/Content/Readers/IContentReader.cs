using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// One data file's reader (GROUND_SYSTEMS.md §12.6, decision K12): every file in <see cref="GameContent.Files"/> and the
/// texts file has exactly one, found in <see cref="ContentReaders.All"/>, which reads the file into
/// <see cref="GameContent.Loaded"/> and then checks what it read. The loader runs every <see cref="Read"/> in the
/// list's order (a file after the files it refers to), then every <see cref="Check"/> in the same order; the checks
/// that span the files are <see cref="CrossChecks"/>, last. A test checks that the registry is complete.
/// </summary>
internal interface IContentReader
{
    /// <summary>The file it reads, as <see cref="GameContent.Files"/> names it (or the texts file); "" for the step across them all.</summary>
    string File { get; }

    /// <summary>Reads the file into the <see cref="ContentReading.Loaded"/>, noting every problem; a reference to another file resolves by key.</summary>
    void Read(ContentReading r);

    /// <summary>Checks what was read, once every file is (GROUND.md §7): IDs and keys unique, references resolved, numbers that make sense.</summary>
    void Check(ContentChecking c);
}

/// <summary>
/// What the readers read from in one load: the field reader that notes every problem, the files' parsed roots and
/// texts, the <see cref="GameContent.Loaded"/> they fill, and the items by key, so a file read after items.json can name
/// an item.
/// </summary>
internal sealed class ContentReading(JsonFields fields, IReadOnlyDictionary<string, JsonElement> documents, IReadOnlyDictionary<string, string> texts, GameContent.Loaded loaded)
{
    private readonly Dictionary<string, ItemDef> _items = new(StringComparer.Ordinal);

    /// <summary>Reads fields and notes every problem with where it is.</summary>
    public JsonFields Fields { get; } = fields;

    /// <summary>Each hashed file's parsed root by its name (undefined when the file is missing or isn't JSON).</summary>
    public IReadOnlyDictionary<string, JsonElement> Documents { get; } = documents;

    public GameContent.Loaded Loaded { get; } = loaded;

    /// <summary>A file's text as read ("" when it is missing), for a file with a parser of its own.</summary>
    public string Text(string file) => texts.GetValueOrDefault(file) ?? "";

    /// <summary>A file's root, its top-level fields checked against the known ones (<c>source</c> and <c>note</c> always are).</summary>
    public JsonElement Root(string file, params string[] known)
    {
        JsonElement root = Documents[file];
        if (root.ValueKind == JsonValueKind.Object)
        {
            Fields.OnlyKnown(root, file, known.Append("source").Append("note").ToArray());
        }
        return root;
    }

    /// <summary>An item named from any file, or null with the problem noted (items.json is read before every file that names one).</summary>
    public ItemDef? Resolve(string key, string where)
    {
        if (_items.TryGetValue(key, out ItemDef? item))
        {
            return item;
        }
        Fields.Problem(where, $"no item \"{key}\"");
        return null;
    }

    /// <summary>Makes an item resolvable by its key; false when the key is used already.</summary>
    public bool AddItem(ItemDef item) => _items.TryAdd(item.Key, item);
}

/// <summary>What the checks work on once every file is read: the <see cref="GameContent.Loaded"/>, and the problems they note.</summary>
internal sealed class ContentChecking(GameContent.Loaded loaded, List<string> problems)
{
    public GameContent.Loaded Loaded { get; } = loaded;

    public void Problem(string where, string what) => problems.Add($"{where}: {what}");

    /// <summary>Notes every key a file's list holds more than once.</summary>
    public void Duplicates(string file, IEnumerable<string> keys, string what = "is used twice")
    {
        foreach (string key in keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            Problem(file, $"\"{key}\" {what}");
        }
    }
}
