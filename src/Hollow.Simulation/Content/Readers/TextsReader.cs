using System.Collections.Generic;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// texts.json: the player's lines (GROUND_SYSTEMS.md §3.2), read like the rest but outside the hash (a reworded line or
/// a translation refuses nobody). It has its own parse, since the loader parses only the hashed files.
/// </summary>
internal sealed class TextsReader : IContentReader
{
    public string File => Texts.File;

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        string text = r.Text(Texts.File);
        var names = new Dictionary<int, string>();
        foreach (ItemDef item in loaded.Items)
        {
            names.TryAdd(item.Id, item.Name);
        }
        string? NameOf(int id) => names.GetValueOrDefault(id);
        JsonElement root = default;
        if (text.Length == 0)
        {
            f.Problem(Texts.File, "missing or empty");
        }
        else
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                root = document.RootElement.Clone();
            }
            catch (JsonException e)
            {
                f.Problem(Texts.File, $"not valid JSON ({e.Message})");
            }
        }
        loaded.Texts = Texts.Read(f, root, NameOf);
    }

    public void Check(ContentChecking c)
    {
        // Checked as it is read: a line for every answer, no line for no answer, placeholders that are known.
    }
}
