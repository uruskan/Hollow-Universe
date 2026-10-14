using System.Collections.Generic;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// The readers in the order the loader runs them (GROUND_SYSTEMS.md §12.6): each file after the files it refers to
/// (items first, then what names items; the texts, the building and placement last, since a placeable names any of
/// them), and the checks across the files at the end. One reader per file: a test checks that every file in
/// <see cref="GameContent.Files"/> and the texts file has exactly one here.
/// </summary>
internal static class ContentReaders
{
    public static readonly IReadOnlyList<IContentReader> All = new IContentReader[]
    {
        new CapabilitiesReader(),
        new CategoriesReader(),
        new ItemsReader(),
        new RecipesReader(),
        new SurfacesReader(),
        new GatherReader(),
        new DeployablesReader(),
        new LootReader(),
        new SurvivalReader(),
        new InteractionsReader(),
        new WeaponsReader(),
        new ExplosivesReader(),
        new ShipsReader(),
        new WorldReader(),
        new AnimalsReader(),
        new TextsReader(),
        new BuildingReader(),
        new PlacementReader(),
        new CrossChecks(),
    };
}
