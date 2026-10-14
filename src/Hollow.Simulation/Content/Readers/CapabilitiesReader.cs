using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>capabilities.json (GROUND_SYSTEMS.md §2.1): the capabilities' keys, in their append-only order (at most 64: a thing's set is one word).</summary>
internal sealed class CapabilitiesReader : IContentReader
{
    public string File => CapabilityData.File;

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        const string file = CapabilityData.File;
        JsonElement root = r.Root(file, "capabilities");
        var keys = new List<string>();
        foreach ((JsonElement e, int i) in f.List(root, "capabilities", file).Select((e, i) => (e, i)))
        {
            string where = $"{file}: capabilities[{i}]";
            f.OnlyKnown(e, where, "key", "note");
            string key = f.Text(e, "key", where);
            if (keys.Contains(key))
            {
                f.Problem(where, $"\"{key}\" is in the list twice");
            }
            keys.Add(key);
        }
        if (keys.Count > Hollow.Simulation.Capabilities.CapabilitySet.Most)
        {
            f.Problem(file, $"at most {Hollow.Simulation.Capabilities.CapabilitySet.Most} capabilities");
        }
        r.Loaded.Capabilities = new CapabilityData(keys);
    }

    public void Check(ContentChecking c)
    {
        // Checked as it is read: a key twice, too many for one word.
    }
}
