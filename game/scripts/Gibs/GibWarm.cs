using System;
using System.Collections.Generic;
using System.Linq;
using HollowUniverse.Common;
using HollowUniverse.Items;

namespace HollowUniverse.Gibs;

/// <summary>
/// The gibs' part of the renderer's warm-up (GROUND.md §4.3.1, §9.4): every gib file the catalogue names, read, and a
/// piece of each of its sets drawn as the pieces are (one multi-mesh per piece mesh), so a first break never waits on a
/// file or a pipeline.
/// </summary>
public static class GibWarm
{
    public static IEnumerable<WarmItem> Items()
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (ModelEntry entry in ModelCatalog.Entries)
        {
            if (entry.Gibs is not { } gibs || !files.Add(gibs.File))
            {
                continue;
            }
            yield return new WarmItem($"gibs of {entry.Key}", new[] { gibs.File }, _ =>
            {
                IEnumerable<string> parts = entry.IsKit ? GibLibrary.Parts(entry.Key) : new[] { "" };
                List<GibSet> sets = parts.Select(p => GibLibrary.Get(entry.Key, p)).OfType<GibSet>().ToList();
                // RenderWarmup owns the preview and frees it with its holder. Do not create the live gib world:
                // its deferred insertion has not entered the scene tree when this callback first runs.
                return GibWorld.CreateWarmup(sets);
            });
        }
    }
}
