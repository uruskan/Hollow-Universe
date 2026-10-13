using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BuildingData = Hollow.Simulation.Building.BuildingData;

namespace Hollow.Simulation.Content;

// building.json joins the game's data (GROUND.md §6.3): the zone's building rules and G5's raiding read it. Phase 2a of
// the building work links its items (doors, locks, the claim device) here as well; this is the part raiding needs.
public sealed partial class GameContent
{
    private static void ReadBuilding(string text, JsonElement document, Loaded loaded, List<string> problems)
    {
        if (document.ValueKind != JsonValueKind.Object)
        {
            loaded.Building = BuildingData.Parse("{}");
            return;
        }
        BuildingData data;
        try
        {
            data = BuildingData.Parse(text);
        }
        catch (JsonException e)
        {
            problems.Add($"building.json: not valid JSON ({e.Message})");
            data = BuildingData.Parse("{}");
        }
        problems.AddRange(data.Problems.Select(p => "building.json: " + p));
        loaded.Building = data;
    }
}
