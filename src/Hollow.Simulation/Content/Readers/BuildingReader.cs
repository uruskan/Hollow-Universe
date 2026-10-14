using System;
using System.Collections.Generic;
using System.Text.Json;
using Hollow.Simulation.Building;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// building.json (GROUND.md §6.3) has a parser of its own (<see cref="BuildingData"/>); its items are linked here: a door
/// item to its door, and the building's own item keys (claim device, locks, key, materials) to items that say what they
/// are. The claim device becomes the deployable the zone keeps it as.
/// </summary>
internal sealed class BuildingReader : IContentReader
{
    public string File => "building.json";

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        string text = r.Text("building.json");
        JsonElement document = r.Documents["building.json"];
        BuildingData data;
        try
        {
            data = BuildingData.Parse(document.ValueKind == JsonValueKind.Object ? text : "{}");
        }
        catch (JsonException)
        {
            data = BuildingData.Parse("{}");
        }
        if (document.ValueKind == JsonValueKind.Object)
        {
            foreach (string problem in data.Problems)
            {
                f.Problem("building.json", problem);
            }
        }
        loaded.Building = data;
        var byKey = new Dictionary<string, ItemDef>(StringComparer.Ordinal);
        foreach (ItemDef item in loaded.Items)
        {
            byKey.TryAdd(item.Key, item);
        }
        foreach (ItemDef item in loaded.Items)
        {
            string where = $"items.json: {JsonFields.Quote(item.Key)}";
            if (item.Building == BuildingRole.Door)
            {
                item.Door = item.DoorKey != null ? data.Door(item.DoorKey) : null;
                if (item.Door == null)
                {
                    f.Problem(where + ".door", $"no door \"{item.DoorKey}\" in building.json");
                }
            }
            else if (item.DoorKey != null)
            {
                f.Problem(where + ".door", "only a door item names a door");
            }
        }
        void Expect(string key, BuildingRole role, string what)
        {
            if (!byKey.TryGetValue(key, out ItemDef? item) || item.Building != role)
            {
                f.Problem("building.json", $"{what} \"{key}\" must be an item with \"building\": \"{role}\"");
            }
        }
        Expect(data.ClaimItem, BuildingRole.Claim, "the claim device");
        Expect(data.KeyLockItem, BuildingRole.KeyLock, "the key lock");
        Expect(data.CodeLockItem, BuildingRole.CodeLock, "the code lock");
        Expect(data.KeyItem, BuildingRole.Key, "the key");
        foreach (string material in data.Materials)
        {
            if (byKey.TryGetValue(material, out ItemDef? item))
            {
                data.LinkMaterial(material, item.Id);
            }
            else
            {
                f.Problem("building.json", $"material \"{material}\" is not an item");
            }
        }
        if (byKey.TryGetValue(data.ClaimItem, out ItemDef? claim))
        {
            loaded.ClaimDeployable = new DeployableDef
            {
                Key = data.ClaimItem,
                Item = claim,
                Health = (float)data.ClaimHealth,
                HalfExtents = data.ClaimSize / 2,
                // The building's own body blocks; the device is placed by the building rules, never on the ground.
                Solid = false,
                Slots = data.ClaimSlots,
            };
        }
    }

    public void Check(ContentChecking c)
    {
        // The building's own checks are its parser's (BuildingData.Problems), noted as it is read.
    }
}
