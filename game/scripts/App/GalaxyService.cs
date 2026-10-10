using System;
using System.IO;
using Godot;
using Hollow.Galaxy;

namespace HollowUniverse.App;

/// <summary>Owns the one <see cref="Galaxy"/> of the client: loads the real-star catalogue and builds the model.</summary>
public static class GalaxyService
{
    public const string CataloguePath = "res://data/catalog/hyg41.bin.gz";

    private static Galaxy? _galaxy;

    public static Galaxy Galaxy => _galaxy ?? throw new InvalidOperationException("GalaxyService.Load() has not run.");

    public static bool IsLoaded => _galaxy != null;

    public static void Load()
    {
        if (_galaxy != null)
        {
            return;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        byte[] bytes = Godot.FileAccess.GetFileAsBytes(CataloguePath);
        StarCatalogue? catalogue = null;
        if (bytes.Length > 0)
        {
            using var stream = new MemoryStream(bytes);
            catalogue = StarCatalogue.Load(stream);
        }
        else
        {
            // The generator always leaves the region near Sol to the catalogue, so without it that region is empty.
            GD.PushError($"Star catalogue not found at {CataloguePath}: real stars near Sol will be missing. " +
                "When exporting, include data/*.gz in the export filter.");
        }
        _galaxy = new Galaxy(catalogue);
        GD.Print($"Galaxy ready: {catalogue?.Stars.Count ?? 0} catalogue stars, loaded in {watch.ElapsedMilliseconds} ms.");
    }
}
