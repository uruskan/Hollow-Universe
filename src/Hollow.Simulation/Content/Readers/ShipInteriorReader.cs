using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Simulation.Ships;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// A ship's data file (IRON_WAKE.md §9.1, <c>game/data/ground/ships/</c>), as a ships.json entry's <c>interior</c> block
/// names it: what the rules take of it (the parts and how they move, the convex pieces, the boarding volume, the sockets,
/// the hull's sweep boxes and the landing pads, the centre of mass) and the block's own fields (the margin, the decks'
/// gravity, the parts' states as a ship is made, the boarding volume's conditions). The import writes the file; the rest
/// of it (rooms, stations, loaders, lights...) is the views' and the later packages'. Part of ShipsReader's file.
/// </summary>
internal static class ShipInteriorReader
{
    private const float Degrees = MathF.PI / 180;

    /// <summary>The interior a ships.json entry's <c>interior</c> block describes, or null with every problem noted.</summary>
    public static ShipInterior? Read(ContentReading r, JsonElement block, string where)
    {
        JsonFields f = r.Fields;
        f.OnlyKnown(block, where, "file", "margin", "gravity", "poses", "conditions");
        string file = f.Text(block, "file", where);
        if (file.Length == 0)
        {
            return null;
        }
        if (!r.Documents.TryGetValue(file, out JsonElement root) || root.ValueKind != JsonValueKind.Object)
        {
            f.Problem(where + ".file", $"no data file \"{file}\" (game/data/ground/{file})");
            return null;
        }
        string at = file;

        // The parts, the hull first (the import writes it first, "body", static).
        var parts = new List<ShipPartDef>();
        JsonElement[] partList = f.List(root, "parts", at);
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < partList.Length; i++)
        {
            names.TryAdd(f.Text(partList[i], "name", $"{at}: parts[{i}]"), i);
        }
        for (int i = 0; i < partList.Length; i++)
        {
            JsonElement p = partList[i];
            string name = f.Text(p, "name", $"{at}: parts[{i}]");
            string partAt = $"{at}: part {JsonFields.Quote(name)}";
            ShipPartKind kind = Kind(f, f.Text(p, "kind", partAt), partAt);
            int parent = -1;
            if (p.TryGetProperty("parent", out JsonElement parentName) && parentName.ValueKind == JsonValueKind.String)
            {
                parent = names.GetValueOrDefault(parentName.GetString() ?? "", -1);
                if (parent < 0 || parent >= i)
                {
                    f.Problem(partAt, $"its parent \"{parentName.GetString()}\" isn't a part listed before it");
                    parent = -1;
                }
            }
            bool moves = kind is not (ShipPartKind.Static or ShipPartKind.Loader);
            Vector3 axis = moves || f.Has(p, "axis") ? f.Vector(p, "axis", partAt) : Vector3.UnitY;
            if (moves && MathF.Abs(axis.Length() - 1) > 1e-3f)
            {
                f.Problem(partAt, "\"axis\" must be a unit vector");
                axis = Vector3.UnitY;
            }
            float scale = kind == ShipPartKind.Hinge ? Degrees : 1;
            parts.Add(new ShipPartDef
            {
                Name = name,
                Index = i,
                Parent = parent,
                Kind = kind,
                Pivot = f.Has(p, "pivot") ? f.Vector(p, "pivot", partAt) : Vector3.Zero,
                Axis = axis,
                Closed = kind is ShipPartKind.Hinge or ShipPartKind.Slide ? f.Number(p, "closed", partAt) * scale : 0,
                Open = kind is ShipPartKind.Hinge or ShipPartKind.Slide ? f.Number(p, "open", partAt) * scale : 0,
            });
        }
        if (parts.Count == 0 || parts[0].Kind != ShipPartKind.Static)
        {
            f.Problem(at, "the first part must be the static hull");
        }

        // The convex pieces by part.
        var pieces = new List<ShipPiece>();
        foreach (JsonElement p in f.List(root, "pieces", at))
        {
            string name = f.Text(p, "name", at + ": pieces", "");
            string pieceAt = $"{at}: piece {JsonFields.Quote(name)}";
            int part = names.GetValueOrDefault(f.Text(p, "part", pieceAt), -1);
            if (part < 0)
            {
                f.Problem(pieceAt, "its part isn't one of the parts");
                continue;
            }
            Vector3[] points = f.Vectors(p, "points", pieceAt);
            if (points.Length < 4)
            {
                f.Problem(pieceAt, "a convex piece needs at least four points");
                continue;
            }
            pieces.Add(new ShipPiece(part, name, points, f.Flag(p, "glass", pieceAt, false)));
        }

        // The block's own fields: the conditions (a boarding box's "when") name parts that must be open.
        var conditions = new Dictionary<string, int>(StringComparer.Ordinal);
        if (f.TryChild(block, "conditions", out JsonElement conditionBlock))
        {
            foreach (JsonProperty c in conditionBlock.EnumerateObject())
            {
                int part = c.Value.ValueKind == JsonValueKind.String ? names.GetValueOrDefault(c.Value.GetString() ?? "", -1) : -1;
                if (part < 0 || !parts[part].IsDoor)
                {
                    f.Problem($"{where}.conditions.{c.Name}", "must name a hinge or slide part of the data file");
                    continue;
                }
                conditions[c.Name] = part;
            }
        }

        // The boarding volume.
        var boarding = new List<BoardingBox>();
        foreach (JsonElement b in f.List(root, "boarding", at))
        {
            string name = f.Text(b, "name", at + ": boarding", "");
            string boxAt = $"{at}: boarding {JsonFields.Quote(name)}";
            Vector3 min = f.Vector(b, "min", boxAt), max = f.Vector(b, "max", boxAt);
            if (max.X < min.X || max.Y < min.Y || max.Z < min.Z)
            {
                f.Problem(boxAt, "min must be below max");
                continue;
            }
            int part = -1;
            if (f.Has(b, "part"))
            {
                part = names.GetValueOrDefault(f.Text(b, "part", boxAt), -1);
                if (part < 0)
                {
                    f.Problem(boxAt, "its part isn't one of the parts");
                }
            }
            int when = -1;
            if (f.Has(b, "when"))
            {
                string condition = f.Text(b, "when", boxAt);
                if (!conditions.TryGetValue(condition, out when))
                {
                    f.Problem(boxAt, $"its condition \"{condition}\" isn't in {where}.conditions");
                    when = -1;
                }
            }
            boarding.Add(new BoardingBox(min, max, part, when));
        }

        // The sockets, with what carries them.
        var sockets = new Dictionary<string, ShipSocket>(StringComparer.Ordinal);
        if (f.TryChild(root, "sockets", out JsonElement socketBlock))
        {
            foreach (JsonProperty s in socketBlock.EnumerateObject())
            {
                string socketAt = $"{at}: socket {JsonFields.Quote(s.Name)}";
                int part = f.Has(s.Value, "part") ? names.GetValueOrDefault(f.Text(s.Value, "part", socketAt), -1) : -1;
                sockets[s.Name] = new ShipSocket(f.Vector(s.Value, "position", socketAt), f.Vector(s.Value, "forward", socketAt),
                    f.Vector(s.Value, "up", socketAt), part);
            }
        }
        else
        {
            f.Problem(at, "\"sockets\" is missing");
        }

        // The hull's sweep boxes and the landing pads (IRON_WAKE.md §5.4).
        var hullBoxes = new List<HullBox>();
        var pads = new List<Vector3>();
        var padSizes = new List<Vector2>();
        if (f.TryChild(root, "hull", out JsonElement hull))
        {
            foreach (JsonElement b in f.List(hull, "boxes", at + ": hull"))
            {
                string boxAt = $"{at}: hull box {JsonFields.Quote(f.Text(b, "name", at + ": hull", ""))}";
                Vector3 min = f.Vector(b, "min", boxAt), max = f.Vector(b, "max", boxAt);
                hullBoxes.Add(new HullBox((min + max) / 2, (max - min) / 2, false));
            }
            foreach (JsonElement pad in f.List(hull, "pads", at + ": hull"))
            {
                string padAt = $"{at}: pad {JsonFields.Quote(f.Text(pad, "name", at + ": hull", ""))}";
                pads.Add(f.Vector(pad, "foot", padAt));
                double[]? size = f.Numbers(pad, "size", padAt, 2);
                padSizes.Add(size == null ? new Vector2(1, 1) : new Vector2((float)size[0], (float)size[1]));
            }
        }
        else
        {
            f.Problem(at, "\"hull\" is missing");
        }

        // The parts' states as a ship is made: by name, a door's progress (0 shut … 1 open), a turret's angle in degrees.
        float[] rest = new float[parts.Count];
        if (f.TryChild(block, "poses", out JsonElement poses))
        {
            foreach (JsonProperty pose in poses.EnumerateObject())
            {
                int part = names.GetValueOrDefault(pose.Name, -1);
                if (part < 0 || pose.Value.ValueKind != JsonValueKind.Number)
                {
                    f.Problem($"{where}.poses.{pose.Name}", "must name a part of the data file and give a number");
                    continue;
                }
                float value = (float)pose.Value.GetDouble();
                ShipPartDef def = parts[part];
                if (def.IsDoor && value is < 0 or > 1)
                {
                    f.Problem($"{where}.poses.{pose.Name}", "a door's state is 0 (shut) … 1 (open)");
                }
                rest[part] = def.Kind is ShipPartKind.Yaw or ShipPartKind.Pitch ? value * Degrees : value;
            }
        }

        // The stations' ids (a station's seat names one) and the lamps' fixtures (ships.json's lights), the drive glows left out.
        var stations = new HashSet<string>(StringComparer.Ordinal);
        var stationData = new List<StationGeometry>();
        foreach (JsonElement s in f.List(root, "stations", at, required: false))
        {
            string id = f.Text(s, "id", at + ": stations");
            stations.Add(id);
            if (Station(f, s, id, $"{at}: station {JsonFields.Quote(id)}", names, sockets, partList) is { } station)
            {
                stationData.Add(station);
            }
        }
        var fixtures = new List<ShipFixture>();
        foreach (JsonElement l in f.List(root, "lights", at, required: false))
        {
            string name = f.Text(l, "name", at + ": lights");
            if (f.Text(l, "use", at + ": lights", "") == "exhaust")
            {
                continue;
            }
            bool spot = f.Text(l, "kind", at + ": lights", "omni") == "spot";
            fixtures.Add(new ShipFixture(name, f.Vector(l, "position", $"{at}: light {name}"), spot ? f.Vector(l, "direction", $"{at}: light {name}") : Vector3.Zero, spot,
                f.Text(l, "room", at + ": lights", "")));
        }

        // Where things may be built aboard, and what is kept clear (IRON_WAKE.md §8): boxes of the ship's frame.
        var zones = new List<ShipBuildZone>();
        foreach (JsonElement z in f.List(root, "buildZones", at, required: false))
        {
            string id = f.Text(z, "id", at + ": buildZones");
            string zoneAt = $"{at}: build zone {JsonFields.Quote(id)}";
            Vector3 min = f.Vector(z, "min", zoneAt), max = f.Vector(z, "max", zoneAt);
            float floor = f.Number(z, "floor", zoneAt, min.Y), headroom = f.Number(z, "headroom", zoneAt, max.Y - min.Y);
            if (max.X < min.X || max.Y < min.Y || max.Z < min.Z || headroom <= 0)
            {
                f.Problem(zoneAt, "min must be below max, and its headroom above 0");
                continue;
            }
            zones.Add(new ShipBuildZone(id, f.Text(z, "name", zoneAt, id), min, max, floor, headroom));
        }
        var materials = new Dictionary<string, (string, float)>(StringComparer.Ordinal);
        if (f.TryChild(root, "materials", out JsonElement mats) && mats.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty m in mats.EnumerateObject())
            {
                string matAt = $"{at}: materials.{m.Name}";
                materials[m.Name] = (f.Text(m.Value, "material", matAt), f.Number(m.Value, "energy", matAt, 1f));
            }
        }
        var rooms = new List<ShipRoom>();
        foreach (JsonElement room in f.List(root, "rooms", at, required: false))
        {
            string id = f.Text(room, "id", at + ": rooms");
            string roomAt = $"{at}: room {JsonFields.Quote(id)}";
            Vector3 min = f.Vector(room, "min", roomAt), max = f.Vector(room, "max", roomAt);
            if (max.X < min.X || max.Y < min.Y || max.Z < min.Z)
            {
                f.Problem(roomAt, "min must be below max");
                continue;
            }
            rooms.Add(new ShipRoom(id, f.Text(room, "name", roomAt, id), min, max));
        }
        var exclusions = new List<ShipExclusion>();
        foreach (JsonElement x in f.List(root, "exclusions", at, required: false))
        {
            string name = f.Text(x, "name", at + ": exclusions");
            string exclusionAt = $"{at}: exclusion {JsonFields.Quote(name)}";
            Vector3 min = f.Vector(x, "min", exclusionAt), max = f.Vector(x, "max", exclusionAt);
            if (max.X < min.X || max.Y < min.Y || max.Z < min.Z)
            {
                f.Problem(exclusionAt, "min must be below max");
                continue;
            }
            exclusions.Add(new ShipExclusion(name, f.Text(x, "why", exclusionAt, ""), min, max));
        }

        var interior = new ShipInterior
        {
            BuildZones = zones,
            Rooms = rooms,
            Materials = materials,
            Exclusions = exclusions,
            File = file,
            Parts = parts,
            Pieces = pieces,
            Boarding = boarding,
            Sockets = sockets,
            HullBoxes = hullBoxes,
            Pads = pads,
            PadSizes = padSizes,
            CentreOfMass = f.Vector(root, "centreOfMass", at),
            Margin = f.Number(block, "margin", where, 0.3f),
            DeckGravity = f.Number(block, "gravity", where, 1f),
            Rest = rest,
            Stations = stations,
            StationData = stationData,
            Fixtures = fixtures,
        };
        interior.Finish();
        return interior;
    }

    // A station of the data file (IRON_WAKE.md §6.1): its parts by name, its barrels (their recoil parts, muzzle sockets,
    // breeches and sides; their lengths from the receiver), its camera socket, and its level arcs (its clearance's, else its
    // yaw part's).
    private static StationGeometry? Station(JsonFields f, JsonElement s, string id, string where, Dictionary<string, int> names,
        Dictionary<string, ShipSocket> sockets, JsonElement[] partList)
    {
        int yaw = names.GetValueOrDefault(f.Text(s, "yaw", where), -1), pitch = names.GetValueOrDefault(f.Text(s, "pitch", where), -1);
        if (yaw < 0 || pitch < 0)
        {
            f.Problem(where, "its yaw and pitch must be parts of the data file");
            return null;
        }
        var lengths = new Dictionary<string, float>(StringComparer.Ordinal);
        if (f.TryChild(s, "receiver", out JsonElement receiver))
        {
            foreach (JsonElement b in f.List(receiver, "barrels", where + ".receiver", required: false))
            {
                lengths[f.Text(b, "recoil", where + ".receiver", "")] = f.Number(b, "length", where + ".receiver", 0);
            }
        }
        var barrels = new List<StationBarrelData>();
        foreach (JsonElement b in f.List(s, "barrels", where))
        {
            string recoil = f.Text(b, "recoil", where + ".barrels");
            string muzzle = f.Text(b, "muzzle", where + ".barrels");
            if (!sockets.TryGetValue(muzzle, out ShipSocket muzzleSocket))
            {
                f.Problem(where + ".barrels", $"no socket \"{muzzle}\"");
                continue;
            }
            barrels.Add(new StationBarrelData(names.GetValueOrDefault(recoil, -1), muzzleSocket, f.Vector(b, "breech", where + ".barrels"),
                Side(f.Text(b, "mountSide", where + ".barrels", "centre")), lengths.GetValueOrDefault(recoil, 0)));
        }
        string camera = f.Text(s, "camera", where, "");
        sockets.TryGetValue(camera, out ShipSocket cameraSocket);
        var arcs = new List<(float, float)>();
        JsonElement arcList = default;
        if (f.TryChild(s, "clearance", out JsonElement clearance) && clearance.TryGetProperty("levelArcs", out JsonElement found))
        {
            arcList = found;
        }
        else if (partList[yaw].TryGetProperty("levelArcs", out JsonElement onYaw))
        {
            arcList = onYaw;
        }
        if (arcList.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement arc in arcList.EnumerateArray())
            {
                if (arc.ValueKind == JsonValueKind.Array && arc.GetArrayLength() == 2)
                {
                    arcs.Add(((float)arc[0].GetDouble(), (float)arc[1].GetDouble()));
                }
            }
        }
        return new StationGeometry(id, f.Text(s, "kind", where), Side(f.Text(s, "side", where, "centre")), yaw, pitch, barrels, cameraSocket, arcs);
    }

    // The data file's sides: port is +X, starboard −X.
    private static float Side(string side) => side switch
    {
        "port" => 1,
        "starboard" => -1,
        _ => 0,
    };

    private static ShipPartKind Kind(JsonFields f, string kind, string where)
    {
        switch (kind)
        {
            case "static":
                return ShipPartKind.Static;
            case "hinge":
                return ShipPartKind.Hinge;
            case "slide":
                return ShipPartKind.Slide;
            case "yaw":
                return ShipPartKind.Yaw;
            case "pitch":
                return ShipPartKind.Pitch;
            case "recoil":
                return ShipPartKind.Recoil;
            case "loader":
                return ShipPartKind.Loader;
            default:
                f.Problem(where, $"unknown kind \"{kind}\" (static, hinge, slide, yaw, pitch, recoil, loader)");
                return ShipPartKind.Static;
        }
    }
}
