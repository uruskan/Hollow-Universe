using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Ships.Modules;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// modules.json (VEHICLES.md §9.1): the kinds of module a ship's sockets take. Each names its item, the socket tags it fits,
/// how many neighbouring sockets it takes, the levels it stands at, its height and whether its roof carries load, its mass
/// and centre of mass, its hull boxes, and the seats, doors and target parts a ship's own are read as (ShipLayoutReader; in
/// the module's frame, its first part its body), the fuel it adds and a hold.
/// </summary>
internal sealed class ModulesReader : IContentReader
{
    public string File => "modules.json";

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        JsonElement root = r.Root("modules.json", "modules");
        int container = loaded.Capabilities?.Index("container") ?? -1;
        JsonElement[] list = f.List(root, "modules", "modules.json");
        for (int i = 0; i < list.Length; i++)
        {
            JsonElement e = list[i];
            string key = f.Text(e, "key", $"modules.json: modules[{i}]");
            string where = $"modules.json: {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "key", "item", "fits", "span", "levels", "height", "carries", "mass", "centreOfMass", "hull", "seats", "doors",
                "parts", "fuel", "hold", "model", "wheels", "power", "boost", "drive");
            ItemDef? item = r.Resolve(f.Text(e, "item", where), where + ".item");
            if (item == null)
            {
                continue;
            }
            int[] levels = f.Integers(e, "levels", where);
            var hull = new List<HullBox>();
            foreach (JsonElement h in f.List(e, "hull", where))
            {
                f.OnlyKnown(h, where + ".hull", "centre", "size", "skid", "fragile");
                hull.Add(new HullBox(f.Vector(h, "centre", where + ".hull"), f.Vector(h, "size", where + ".hull"), f.Flag(h, "skid", where + ".hull", false),
                    f.Flag(h, "fragile", where + ".hull", false)));
            }
            (List<ShipSeatDef> seats, List<ShipDoorDef> doors, List<ShipTargetPart> parts) =
                ShipLayoutReader.Read(r, e, where, item.Name, interior: null, seatsOptional: true);
            loaded.Modules.Add(new ModuleDef
            {
                Key = key,
                Item = item,
                Fits = f.Texts(e, "fits", where),
                Span = f.Integer(e, "span", where, 1),
                Lower = levels.Contains(1),
                Upper = levels.Contains(2),
                Height = f.Number(e, "height", where),
                Carries = f.Flag(e, "carries", where, false),
                Mass = f.Number(e, "mass", where),
                CentreOfMass = f.Vector(e, "centreOfMass", where),
                Hull = hull,
                Seats = seats,
                Doors = doors,
                Parts = parts,
                Wheels = ShipsReader.Wheels(f, e, where),
                Power = f.Number(e, "power", where, 0),
                Boost = f.Number(e, "boost", where, 1),
                Drive = DriveNumbers(f, e, where),
                FuelCapacity = f.Number(e, "fuel", where, 0),
                Hold = f.Integer(e, "hold", where, 0),
                HoldPart = parts.FindIndex(p => p.Capabilities.Has(container)),
                Model = f.Text(e, "model", where, key),
            });
            if (levels.Any(l => l is not (1 or 2)) || levels.Length == 0)
            {
                f.Problem(where + ".levels", "lists level 1, level 2 or both");
            }
        }
        foreach (ItemDef item in loaded.Items)
        {
            if (item.ModuleKey == null)
            {
                continue;
            }
            ModuleDef? def = loaded.Modules.FirstOrDefault(m => m.Key == item.ModuleKey);
            if (def == null)
            {
                f.Problem($"items.json: {JsonFields.Quote(item.Key)}.module", $"no module \"{item.ModuleKey}\" in modules.json");
            }
            else if (def.Item != item)
            {
                f.Problem($"modules.json: {JsonFields.Quote(def.Key)}", $"its item is \"{def.Item.Key}\", but \"{item.Key}\" says it is that module");
            }
            item.Module = def;
        }
    }

    // A module's drive numbers: { "<name>": number, ... }, summed by name over a build.
    private static Dictionary<string, float> DriveNumbers(JsonFields f, JsonElement e, string where)
    {
        var numbers = new Dictionary<string, float>(StringComparer.Ordinal);
        if (!f.TryChild(e, "drive", out JsonElement drive))
        {
            return numbers;
        }
        foreach (JsonProperty p in drive.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Number && float.IsFinite((float)p.Value.GetDouble()))
            {
                numbers[p.Name] = (float)p.Value.GetDouble();
            }
            else
            {
                f.Problem(where + ".drive", $"\"{p.Name}\" is a number");
            }
        }
        return numbers;
    }

    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        c.Duplicates("modules.json", loaded.Modules.Select(m => m.Key));
        // Each kind's default build (ships.json build), resolved now the modules are read, and held to the sockets' rules.
        foreach (ShipDef kind in loaded.Ships.Where(s => s.DefaultBuildKeys.Count > 0))
        {
            string at = $"ships.json: {JsonFields.Quote(kind.Key)}.build";
            var entries = new List<Installed>();
            foreach ((string socket, int level, string module) in kind.DefaultBuildKeys)
            {
                int index = kind.Sockets.ToList().FindIndex(x => x.Key == socket);
                ModuleDef? def = loaded.Modules.FirstOrDefault(m => m.Key == module);
                if (index < 0 || def == null || level is not (1 or 2))
                {
                    c.Problem(at, $"\"{socket}\" level {level}: \"{module}\" names a socket of the kind, level 1 or 2, and a module of modules.json");
                    continue;
                }
                entries.Add(new Installed((byte)index, (byte)level, def.Id));
            }
            var build = new ShipBuild(entries);
            BuildProblem problem = ShipAssembler.Check(kind, build, id => loaded.Modules.FirstOrDefault(m => m.Id == id));
            if (problem != BuildProblem.None)
            {
                c.Problem(at, $"the default build can't be ({problem})");
            }
            else
            {
                kind.DefaultBuild = build;
            }
        }
        int seatCapability = loaded.Capabilities?.Index("seat") ?? -1, shipDoor = loaded.Capabilities?.Index("ship-door") ?? -1;
        foreach (ModuleDef m in loaded.Modules)
        {
            string where = $"modules.json: {JsonFields.Quote(m.Key)}";
            if (m.Item.Module != m)
            {
                c.Problem(where, $"item \"{m.Item.Key}\" must say \"module\": \"{m.Key}\"");
            }
            if (m.Item.Ship != null || m.Item.Deploys != null)
            {
                c.Problem(where, $"item \"{m.Item.Key}\" is a module and nothing else");
            }
            if (m.Fits.Count == 0 || m.Span is < 1 or > 8 || m.Height < 0 || m.Mass <= 0 || m.FuelCapacity < 0 || m.Hold is < 0 or > 48 || m.Power < 0 ||
                m.Boost is < 1 or > 3)
            {
                c.Problem(where, "fits a tag or more, spans 1 … 8 sockets, a height of 0 or more, a positive mass, fuel of 0 or more, a hold of 0 … 48 slots, power of 0 or more, an overboost of 1 … 3");
            }
            foreach (WheelDef w in m.Wheels.Where(w => w.Radius <= 0 || w.Width <= 0 || w.Travel <= 0 || w.Stiffness <= 0 || w.Damping <= 0 || w.Grip is <= 0 or > 2))
            {
                c.Problem(where + ".wheels", $"wheel \"{w.Key}\": a positive radius, width, travel, spring and damper, grip 0 … 2");
            }
            if (m.Hull.Count == 0 || m.Hull.Any(h => h.HalfExtents.X <= 0 || h.HalfExtents.Y <= 0 || h.HalfExtents.Z <= 0))
            {
                c.Problem(where + ".hull", "boxes of positive size (the module's body: what a shot meets)");
            }
            if (m.Parts.Count == 0 || m.Parts[0].Health == null || m.Parts.Count > 32)
            {
                c.Problem(where + ".parts", "1 … 32 parts, the first its body with health (the hammer mends it, a shot on its boxes takes it)");
            }
            if ((m.Hold > 0) != (m.HoldPart >= 0))
            {
                c.Problem(where, "a hold opens at a part with the container capability, and such a part opens a hold");
            }
            if (m.Seats.Count > 8 || m.Doors.Count > 4)
            {
                c.Problem(where, "at most 8 seats and 4 doors");
            }
            if (m.Seats.Select(s => s.Key).Distinct().Count() != m.Seats.Count || m.Doors.Select(d => d.Key).Distinct().Count() != m.Doors.Count ||
                m.Parts.Select(p => p.Key).Distinct().Count() != m.Parts.Count)
            {
                c.Problem(where, "seats', doors' and parts' keys are each unique");
            }
            foreach (ShipSeatDef seat in m.Seats)
            {
                string at = $"{where}.seats \"{seat.Key}\"";
                if (seat.Exits.Count == 0)
                {
                    c.Problem(at, "a seat needs a way out (exits)");
                }
                // A module's helm may ride as a passenger in a ship with a helm of its own, so this holds for every seat.
                if (seat.Door < 0 && !seat.Exposed)
                {
                    c.Problem(at, "a seat is reached through a door, unless it is in the open (exposed)");
                }
                if (seat.Role == SeatRole.Station)
                {
                    c.Problem(at, "a module's seat is a helm's or a passenger's");
                }
                if (!m.Parts.Any(p => p.Seat == seat.Index))
                {
                    c.Problem(at, "no part sits in it");
                }
            }
            foreach (ShipDoorDef door in m.Doors)
            {
                string at = $"{where}.doors \"{door.Key}\"";
                if (door.Seconds is <= 0 or > 60 || door.Clip.Length == 0)
                {
                    c.Problem(at, "seconds 0 … 60, and a clip of the module's model");
                }
                ShipLayoutReader.CheckHealth(c, door.Health, at);
            }
            foreach (ShipTargetPart part in m.Parts)
            {
                string at = $"{where}.parts \"{part.Key}\"";
                if (!part.HasBall)
                {
                    c.Problem(at, "a module's part is met by a ball (at, radius)");
                }
                if (part.Capabilities.Has(seatCapability) != (part.Seat >= 0) || part.Capabilities.Has(shipDoor) != (part.Door >= 0))
                {
                    c.Problem(at, "a part with the seat or ship-door capability names its seat or door, and only then");
                }
                if (part.Door >= 0 && part.Health != null)
                {
                    c.Problem(at, "a door's part takes its door's health");
                }
                ShipLayoutReader.CheckHealth(c, part.Health, at);
            }
        }
        // Every socket fits something, and every module some socket: a tag nothing uses is a typing error.
        var tags = new HashSet<string>(loaded.Ships.SelectMany(s => s.Sockets).SelectMany(s => s.Fits), StringComparer.Ordinal);
        foreach (ModuleDef m in loaded.Modules.Where(m => !m.Fits.Any(tags.Contains)))
        {
            c.Problem($"modules.json: {JsonFields.Quote(m.Key)}.fits", "no ship's socket fits any of its tags");
        }
    }
}
