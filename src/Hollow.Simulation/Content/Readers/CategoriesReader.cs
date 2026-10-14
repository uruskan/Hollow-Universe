using System.Linq;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>categories.json: the crafting tabs, each a key and a name; an item's category is one of them.</summary>
internal sealed class CategoriesReader : IContentReader
{
    public string File => "categories.json";

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        JsonElement root = r.Root("categories.json", "categories");
        foreach ((JsonElement e, int i) in f.List(root, "categories", "categories.json").Select((e, i) => (e, i)))
        {
            string where = $"categories.json: categories[{i}]";
            f.OnlyKnown(e, where, "key", "name");
            r.Loaded.Categories.Add(new Category(f.Text(e, "key", where), f.Text(e, "name", where)));
        }
    }

    public void Check(ContentChecking c) => c.Duplicates("categories.json", c.Loaded.Categories.Select(x => x.Key));
}
