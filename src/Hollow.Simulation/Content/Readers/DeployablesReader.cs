using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// deployables.json: everything placed from the inventory (bags, beds, fires, furnaces, boxes, workbenches, the
/// research table, the vehicle workbench with its bay): hit points, half extents, slots, device slots and warmth,
/// workbench level. Links each to its item (<c>deploys</c>) and the recipes to their stations.
/// </summary>
internal sealed class DeployablesReader : IContentReader
{
    public string File => "deployables.json";

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        JsonElement root = r.Root("deployables.json", "deployables");
        JsonElement[] list = f.List(root, "deployables", "deployables.json");
        for (int i = 0; i < list.Length; i++)
        {
            JsonElement e = list[i];
            string key = f.Text(e, "key", $"deployables.json: deployables[{i}]");
            string where = $"deployables.json: {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "key", "item", "health", "size", "placement", "solid", "respawn", "slots", "device", "workbench", "bay", "pickup", "burns");
            ItemDef? item = r.Resolve(f.Text(e, "item", where), where + ".item");
            JsonElement[] size = f.List(e, "size", where);
            Vector3 half = Vector3.Zero;
            if (size.Length == 3 && size.All(s => s.ValueKind == JsonValueKind.Number))
            {
                half = new Vector3((float)size[0].GetDouble(), (float)size[1].GetDouble(), (float)size[2].GetDouble());
            }
            else
            {
                f.Problem(where, "\"size\" must be three numbers (half extents x, y, z)");
            }
            DeviceStats? device = null;
            if (f.TryChild(e, "device", out JsonElement d))
            {
                string dw = where + ".device";
                f.OnlyKnown(d, dw, "fuel", "input", "output", "cooks", "warmth");
                float radius = 0, celsius = 0;
                if (f.TryChild(d, "warmth", out JsonElement warmth))
                {
                    f.OnlyKnown(warmth, dw + ".warmth", "radius", "celsius");
                    radius = f.Number(warmth, "radius", dw + ".warmth");
                    celsius = f.Number(warmth, "celsius", dw + ".warmth");
                }
                device = new DeviceStats
                {
                    FuelSlots = f.Integers(d, "fuel", dw),
                    InputSlots = f.Integers(d, "input", dw),
                    OutputSlots = f.Integers(d, "output", dw),
                    Cooks = f.Text(d, "cooks", dw),
                    WarmthRadius = radius,
                    WarmthCelsius = celsius,
                };
            }
            BayStats? bay = null;
            if (f.TryChild(e, "bay", out JsonElement b))
            {
                string bw = where + ".bay";
                f.OnlyKnown(b, bw, "at", "clear", "maxSlope");
                bay = new BayStats
                {
                    At = f.Vector(b, "at", bw),
                    Clear = f.Vector(b, "clear", bw),
                    MaxSlopeDegrees = f.Number(b, "maxSlope", bw, 20),
                };
            }
            if (item == null)
            {
                continue;
            }
            var def = new DeployableDef
            {
                Key = key,
                Item = item,
                Health = f.Number(e, "health", where),
                HalfExtents = half,
                Solid = f.Flag(e, "solid", where, true),
                RespawnSeconds = f.Number(e, "respawn", where, 0),
                Slots = f.Integer(e, "slots", where, 0),
                Device = device,
                Workbench = f.Integer(e, "workbench", where, 0),
                Bay = bay,
                Pickup = f.Flag(e, "pickup", where, false),
                Burns = f.Flag(e, "burns", where, false),
            };
            loaded.Deployables.Add(def);
        }
        foreach (Recipe recipe in loaded.Recipes)
        {
            if (recipe.StationKey == null)
            {
                continue;
            }
            recipe.Station = loaded.Deployables.FirstOrDefault(d => d.Key == recipe.StationKey);
            if (recipe.Station == null)
            {
                f.Problem($"recipes.json: {JsonFields.Quote(recipe.Output.Key)}.station", $"no deployable \"{recipe.StationKey}\"");
            }
        }
        foreach (ItemDef item in loaded.Items)
        {
            if (item.DeploysKey == null)
            {
                continue;
            }
            DeployableDef? def = loaded.Deployables.FirstOrDefault(d => d.Key == item.DeploysKey);
            if (def == null)
            {
                f.Problem($"items.json: {JsonFields.Quote(item.Key)}.deploys", $"no deployable \"{item.DeploysKey}\"");
            }
            else if (def.Item != item)
            {
                f.Problem($"deployables.json: {JsonFields.Quote(def.Key)}", $"its item is \"{def.Item.Key}\", but \"{item.Key}\" deploys it");
            }
            item.Deploys = def;
        }
    }

    /// <summary>Keys unique; each one's item deploys it; health, size, slots, a device's slots and a bay's space in range.</summary>
    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        c.Duplicates("deployables.json", loaded.Deployables.Select(d => d.Key));
        foreach (DeployableDef d in loaded.Deployables)
        {
            string where = $"deployables.json: \"{d.Key}\"";
            if (d.Item.Deploys != d)
            {
                c.Problem(where, $"item \"{d.Item.Key}\" must say \"deploys\": \"{d.Key}\"");
            }
            if (d.Health <= 0 || d.HalfExtents.X <= 0 || d.HalfExtents.Y <= 0 || d.HalfExtents.Z <= 0)
            {
                c.Problem(where, "health and size must be positive");
            }
            if (d.Slots is < 0 or > 64 || d.Workbench is < 0 or > 3 || d.RespawnSeconds < 0)
            {
                c.Problem(where, "slots 0 … 64, workbench 0 … 3");
            }
            if (d.Device is { } device)
            {
                int[] all = device.FuelSlots.Concat(device.InputSlots).Concat(device.OutputSlots).ToArray();
                if (all.Any(s => s < 0 || s >= d.Slots) || all.Distinct().Count() != all.Length)
                {
                    c.Problem(where, "device slots must be distinct and within the slots");
                }
                if (device.FuelSlots.Count == 0 || device.OutputSlots.Count == 0)
                {
                    c.Problem(where, "a device needs fuel and output slots");
                }
            }
        }
        foreach (DeployableDef d in loaded.Deployables.Where(d => d.Bay != null))
        {
            BayStats bay = d.Bay!;
            if (bay.Clear.X <= 0 || bay.Clear.Y <= 0 || bay.Clear.Z <= 0 || bay.MaxSlopeDegrees is <= 0 or > 45 || bay.At.Length() > 30)
            {
                c.Problem($"deployables.json: \"{d.Key}\".bay", "a clear space of positive size, within 30 m, on ground of 0 … 45°");
            }
        }
    }
}
