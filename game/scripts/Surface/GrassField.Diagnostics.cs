using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    private bool _appearanceDiagnosticWritten;

    // Opt-in evidence from the actual field being rendered: --grass-diagnostics <absolute-json-path> beside a hosted
    // --shot or environment route writes, once the first tile is in, the species' levels as loaded (cards, triangles,
    // vertices), the distances they switch at for this field's reach, and that tile's instances by species. Screenshots
    // establish the blades' final look.
    private void WriteAppearanceDiagnostic(TileBuild first)
    {
        if (_appearanceDiagnosticWritten) return;
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--grass-diagnostics");
        if (at < 0) return;
        if (at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("--grass-diagnostics requires an output JSON path.");
        _appearanceDiagnosticWritten = true;
        var species = new List<object>();
        for (int s = 0; s < _species.Length; s++)
        {
            Species sp = _species[s];
            species.Add(new
            {
                sp.Key, sp.Litter, sp.Height, sp.Reach, cards = sp.Cards,
                levels = sp.Meshes.Select(m => { var (v, t) = GeometryCounts(m); return new { vertices = v, triangles = t }; }).ToArray(),
                switchMetres = sp.Switch, cardCurve = new[] { sp.CardCurve.X, sp.CardCurve.Y, sp.CardCurve.Z },
                thin = new[] { sp.Thin.X, sp.Thin.Y, sp.Thin.Z }, fade = new[] { sp.Fade.X, sp.Fade.Y },
                firstTileInstances = s < first.Species.Length ? first.Species[s].Count : 0,
            });
        }
        var report = new
        {
            kind = "grass-field", scope = "Loaded meshes, this field's distances and the first uploaded tile; not GPU cost or the final look.",
            distance = BuiltWith.Distance, density = BuiltWith.Density, far = _far, litterFar = _litterFar, tileCells = TileCells,
            placementLevel = _level, terrainLevel = _terrainLevel, firstTile = first.Key.ToString(), cells = first.Cells,
            clumps = first.Clumps, accents = first.Accents.Count, species,
        };
        string output = System.IO.Path.GetFullPath(args[at + 1]);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
        System.IO.File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"[grass-diagnostics] {first.Clumps} clumps in {first.Cells} cells of {first.Key}; {output}");
    }
}
