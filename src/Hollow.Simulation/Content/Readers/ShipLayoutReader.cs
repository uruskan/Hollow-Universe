using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Ships;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// A ships.json entry's layout (IRON_WAKE.md §5.1): its <c>seats</c>, <c>doors</c>, target <c>parts</c>, <c>nozzles</c> and
/// <c>lights</c>. A point is a socket of the ship's data file by name (its position, orientation and the part that carries
/// it: the sockets come from the import, not hand-copied numbers) or three numbers in the ship's frame; a part of the data
/// file is named as the file names it. Every name is checked here, every number's range in <see cref="Check"/>. Part of
/// ShipsReader's file.
/// </summary>
internal static class ShipLayoutReader
{
    /// <summary>The seats, the doors and the parts, in that order (a part names its seat and door; a seat its door).</summary>
    /// <remarks>A module's (modules.json, VEHICLES.md §9.1) are read the same way, its seats optional.</remarks>
    public static (List<ShipSeatDef> Seats, List<ShipDoorDef> Doors, List<ShipTargetPart> Parts) Read(ContentReading r, JsonElement e, string where,
        string shipName, ShipInterior? interior, bool seatsOptional = false)
    {
        List<ShipDoorDef> doors = Doors(r, e, where, interior);
        List<ShipSeatDef> seats = Seats(r, e, where, shipName, interior, doors, seatsOptional);
        List<ShipTargetPart> parts = Parts(r, e, where, shipName, interior, seats, doors);
        return (seats, doors, parts);
    }

    // ---------------------------------------------------------------- points

    /// <summary>A point: a socket of the data file by its name, or three numbers (no orientation: +Z forward, +Y up, the hull).</summary>
    public static ShipSocket Point(JsonFields f, JsonElement v, string where, ShipInterior? interior)
    {
        if (v.ValueKind == JsonValueKind.String)
        {
            string name = v.GetString() ?? "";
            if (interior != null && interior.Sockets.TryGetValue(name, out ShipSocket socket))
            {
                return socket;
            }
            f.Problem(where, interior == null ? $"names a socket (\"{name}\") but the ship has no interior data file" : $"no socket \"{name}\" in {interior.File}");
            return new ShipSocket(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, -1);
        }
        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("at", out JsonElement numbers) && numbers.ValueKind == JsonValueKind.Array)
        {
            // Three numbers that face a way (the KA-FA's rear seat looks out to its right): + Z forward, + Y up unless named.
            ShipSocket point = Point(f, numbers, where, interior);
            Vector3 Axis(string name, Vector3 fallback)
            {
                if (!v.TryGetProperty(name, out JsonElement a))
                {
                    return fallback;
                }
                if (a.ValueKind == JsonValueKind.Array && a.GetArrayLength() == 3 && a.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number))
                {
                    var axis = new Vector3((float)a[0].GetDouble(), (float)a[1].GetDouble(), (float)a[2].GetDouble());
                    if (axis.LengthSquared() > 1e-6f)
                    {
                        return Vector3.Normalize(axis);
                    }
                }
                f.Problem(where, $"\"{name}\" is three numbers, not all 0");
                return fallback;
            }
            return point with { Forward = Axis("forward", Vector3.UnitZ), Up = Axis("up", Vector3.UnitY) };
        }
        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("at", out JsonElement at) && at.ValueKind == JsonValueKind.String)
        {
            // A socket moved by an offset in its part's frame, e.g. a gunner's eye raised to see over the gun: it still rides
            // the part (a turret's yaw) as the socket does.
            ShipSocket socket = Point(f, at, where, interior);
            if (v.TryGetProperty("offset", out JsonElement o) && o.ValueKind == JsonValueKind.Array && o.GetArrayLength() == 3
                && o.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number))
            {
                return socket with { Position = socket.Position + new Vector3((float)o[0].GetDouble(), (float)o[1].GetDouble(), (float)o[2].GetDouble()) };
            }
            f.Problem(where, "\"offset\" is three numbers");
            return socket;
        }
        if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 3 && v.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number))
        {
            return new ShipSocket(new Vector3((float)v[0].GetDouble(), (float)v[1].GetDouble(), (float)v[2].GetDouble()), Vector3.UnitZ, Vector3.UnitY, -1);
        }
        f.Problem(where, "a point is three numbers, a socket's name, { \"at\": a socket, \"offset\": three numbers }, or { \"at\": three numbers, \"forward\": three numbers }");
        return new ShipSocket(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, -1);
    }

    /// <summary>A named field holding a point.</summary>
    private static ShipSocket PointField(JsonFields f, JsonElement o, string name, string where, ShipInterior? interior)
    {
        if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out JsonElement v))
        {
            return Point(f, v, $"{where}.{name}", interior);
        }
        f.Problem(where, $"\"{name}\" is missing");
        return new ShipSocket(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, -1);
    }

    /// <summary>A list of points.</summary>
    private static ShipSocket[] PointList(JsonFields f, JsonElement o, string name, string where, ShipInterior? interior, bool required = true) =>
        f.List(o, name, where, required).Select(v => Point(f, v, $"{where}.{name}", interior)).ToArray();

    // The data file's parts named in a list.
    private static int[] DataParts(JsonFields f, JsonElement o, string name, string where, ShipInterior? interior)
    {
        var result = new List<int>();
        foreach (string part in f.Texts(o, name, where))
        {
            int index = interior?.PartIndex(part) ?? -1;
            if (index <= 0)
            {
                f.Problem(where, interior == null ? $"\"{name}\" names parts of a data file the ship hasn't" : $"no moving part \"{part}\" in {interior.File}");
                continue;
            }
            result.Add(index);
        }
        return result.ToArray();
    }

    // ---------------------------------------------------------------- doors

    private static List<ShipDoorDef> Doors(ContentReading r, JsonElement e, string where, ShipInterior? interior)
    {
        JsonFields f = r.Fields;
        var doors = new List<ShipDoorDef>();
        foreach (JsonElement d in f.List(e, "doors", where, required: false))
        {
            string key = f.Text(d, "key", where + ".doors");
            string at = $"{where}.doors {JsonFields.Quote(key)}";
            f.OnlyKnown(d, at, "key", "name", "parts", "clip", "seconds", "opens", "ease", "health");
            int[] parts = DataParts(f, d, "parts", at, interior);
            foreach (int part in parts)
            {
                if (interior != null && !interior.Parts[part].IsDoor)
                {
                    f.Problem(at, $"its part \"{interior.Parts[part].Name}\" is no hinge or slide");
                }
            }
            string clip = f.Text(d, "clip", at, "");
            if (parts.Length == 0 && clip.Length == 0)
            {
                f.Problem(at, "a door drives parts of the data file, or the views sample a clip of the model by its progress");
            }
            doors.Add(new ShipDoorDef
            {
                Index = doors.Count,
                Key = key,
                Name = f.Text(d, "name", at),
                Parts = parts,
                Clip = clip,
                Seconds = f.Number(d, "seconds", at),
                Opens = f.Text(d, "opens", at) switch
                {
                    "always" => DoorOpens.Always,
                    "still" => DoorOpens.Still,
                    "slow" => DoorOpens.Slow,
                    var other => Bad(f, at, $"\"opens\" is always, still or slow (not \"{other}\")", DoorOpens.Still),
                },
                Ease = f.Text(d, "ease", at, "linear") switch
                {
                    "linear" => DoorEase.Linear,
                    "inOut" => DoorEase.InOut,
                    var other => Bad(f, at, $"\"ease\" is linear or inOut (not \"{other}\")", DoorEase.Linear),
                },
                Health = Health(r, d, at),
            });
        }
        return doors;
    }

    private static T Bad<T>(JsonFields f, string where, string what, T fallback)
    {
        f.Problem(where, what);
        return fallback;
    }

    // A door's or a part's health: { "max": 1000, "protection": "armoured", "table": "doors", "repair": ["refined-metal", 10] }.
    private static ShipPartHealth? Health(ContentReading r, JsonElement o, string where)
    {
        JsonFields f = r.Fields;
        if (!f.TryChild(o, "health", out JsonElement h))
        {
            return null;
        }
        string at = where + ".health";
        f.OnlyKnown(h, at, "max", "protection", "table", "repair");
        ItemDef? material = null;
        int full = 0;
        if (h.TryGetProperty("repair", out JsonElement repair) && f.Tuple(repair, at + ".repair", 1, out string item, out double[] cost))
        {
            material = r.Resolve(item, at + ".repair");
            full = (int)cost[0];
        }
        else if (!h.TryGetProperty("repair", out _))
        {
            f.Problem(at, "\"repair\" is [material, what mending it from nothing costs]");
        }
        return new ShipPartHealth
        {
            Max = f.Number(h, "max", at),
            ProtectionKey = f.Text(h, "protection", at),
            DoorTable = f.Text(h, "table", at) switch
            {
                "doors" => true,
                "pieces" => false,
                var other => Bad(f, at, $"\"table\" is doors or pieces (not \"{other}\")", false),
            },
            RepairMaterial = material!,
            RepairFull = full,
        };
    }

    // ---------------------------------------------------------------- seats

    private static List<ShipSeatDef> Seats(ContentReading r, JsonElement e, string where, string shipName, ShipInterior? interior, List<ShipDoorDef> doors,
        bool optional)
    {
        JsonFields f = r.Fields;
        InteractionData? verbs = r.Loaded.Interactions;
        var seats = new List<ShipSeatDef>();
        foreach (JsonElement s in f.List(e, "seats", where, required: !optional))
        {
            string key = f.Text(s, "key", where + ".seats");
            string at = $"{where}.seats {JsonFields.Quote(key)}";
            f.OnlyKnown(s, at, "key", "name", "role", "at", "eye", "exits", "exposed", "sitters", "works", "verb", "door");
            string role = f.Text(s, "role", at);
            SeatRole kind = role == "helm" ? SeatRole.Helm : role == "passenger" ? SeatRole.Passenger : role.StartsWith("station:", StringComparison.Ordinal)
                ? SeatRole.Station : Bad(f, at, $"\"role\" is helm, passenger or station:<id> (not \"{role}\")", SeatRole.Passenger);
            string station = kind == SeatRole.Station ? role["station:".Length..] : "";
            if (kind == SeatRole.Station && (interior == null || !interior.Stations.Contains(station)))
            {
                f.Problem(at, interior == null ? "a station's seat needs the ship's data file" : $"no station \"{station}\" in {interior.File}");
            }
            SeatWorks works = SeatWorks.None;
            foreach (string w in f.Texts(s, "works", at))
            {
                works |= w switch
                {
                    "doors" => SeatWorks.Doors,
                    "lights" => SeatWorks.Lights,
                    _ => Bad(f, at, $"\"works\" lists doors and lights (not \"{w}\")", SeatWorks.None),
                };
            }
            string verb = f.Text(s, "verb", at);
            int door = -1;
            if (f.Has(s, "door"))
            {
                string doorKey = f.Text(s, "door", at);
                door = doors.FindIndex(d => d.Key == doorKey);
                if (door < 0)
                {
                    f.Problem(at, $"no door \"{doorKey}\" in the ship's doors");
                }
            }
            seats.Add(new ShipSeatDef
            {
                Index = seats.Count,
                Key = key,
                Name = f.Text(s, "name", at, shipName),
                Role = kind,
                Station = station,
                At = PointField(f, s, "at", at, interior),
                Eye = PointField(f, s, "eye", at, interior),
                Exits = PointList(f, s, "exits", at, interior),
                Exposed = f.Flag(s, "exposed", at, false),
                Sitters = f.Text(s, "sitters", at) switch
                {
                    "anyone" => SeatSitters.Anyone,
                    "unlocked" => SeatSitters.Unlocked,
                    "crew" => SeatSitters.Crew,
                    var other => Bad(f, at, $"\"sitters\" is anyone, unlocked or crew (not \"{other}\")", SeatSitters.Crew),
                },
                Works = works,
                Verb = verbs?.Index(verb) ?? Bad(f, at, "interactions.json isn't read", -1),
                VerbKey = verb,
                Door = door,
            });
            if (verbs != null && verbs.Index(verb) < 0 || Array.IndexOf(ShipSeatDef.Verbs, verb) < 0)
            {
                f.Problem(at, $"\"verb\" is one of the seat's verbs in interactions.json ({string.Join(", ", ShipSeatDef.Verbs)}), not \"{verb}\"");
            }
        }
        return seats;
    }

    // ---------------------------------------------------------------- parts

    private static List<ShipTargetPart> Parts(ContentReading r, JsonElement e, string where, string shipName, ShipInterior? interior,
        List<ShipSeatDef> seats, List<ShipDoorDef> doors)
    {
        JsonFields f = r.Fields;
        CapabilityData? capabilities = r.Loaded.Capabilities;
        var parts = new List<ShipTargetPart>();
        foreach (JsonElement p in f.List(e, "parts", where))
        {
            string key = f.Text(p, "key", where + ".parts");
            string at = $"{where}.parts {JsonFields.Quote(key)}";
            f.OnlyKnown(p, at, "key", "name", "at", "radius", "pieces", "capabilities", "seat", "door", "through", "health");
            CapabilitySet set = CapabilitySet.Empty;
            foreach (string capability in f.Texts(p, "capabilities", at))
            {
                int index = capabilities?.Index(capability) ?? -1;
                if (index < 0)
                {
                    f.Problem(at, $"no capability \"{capability}\" in capabilities.json");
                    continue;
                }
                set = set.With(index);
            }
            int Named(string field, IReadOnlyList<string> keys, string what)
            {
                if (!f.Has(p, field))
                {
                    return -1;
                }
                string name = f.Text(p, field, at);
                int i = keys.ToList().IndexOf(name);
                if (i < 0)
                {
                    f.Problem(at, $"no {what} \"{name}\" in the ship's {what}s");
                }
                return i;
            }
            string[] doorKeys = doors.Select(d => d.Key).ToArray();
            bool ball = f.Has(p, "at");
            parts.Add(new ShipTargetPart
            {
                Index = parts.Count,
                Key = key,
                Name = f.Text(p, "name", at, shipName),
                At = ball ? PointField(f, p, "at", at, interior) : default,
                Radius = ball ? f.Number(p, "radius", at) : 0,
                Pieces = DataParts(f, p, "pieces", at, interior),
                Capabilities = set,
                Seat = Named("seat", seats.Select(s => s.Key).ToArray(), "seat"),
                Door = Named("door", doorKeys, "door"),
                Through = Named("through", doorKeys, "door"),
                Health = Health(r, p, at),
            });
        }
        return parts;
    }

    // ---------------------------------------------------------------- nozzles and lights (drawn by the views; IW6 the lights)

    /// <summary>
    /// The nozzles: { "main": { "at": [points], "width": w, "length": l }, "aux": ..., "lift": ..., "rcs": ... }, each drive's
    /// points and its plume's width and length at full thrust.
    /// </summary>
    public static List<ShipNozzle> Nozzles(JsonFields f, JsonElement o, string where, ShipInterior? interior)
    {
        var nozzles = new List<ShipNozzle>();
        f.OnlyKnown(o, where, "main", "aux", "lift", "rcs");
        foreach ((string name, NozzleDrive drive) in new[] { ("main", NozzleDrive.Main), ("aux", NozzleDrive.Aux), ("lift", NozzleDrive.Lift), ("rcs", NozzleDrive.Rcs) })
        {
            if (!f.TryChild(o, name, out JsonElement d))
            {
                continue;
            }
            string at = $"{where}.{name}";
            f.OnlyKnown(d, at, "at", "width", "length");
            float width = f.Number(d, "width", at), length = f.Number(d, "length", at);
            foreach (ShipSocket point in PointList(f, d, "at", at, interior))
            {
                nozzles.Add(new ShipNozzle(point.Position, drive, width, length));
            }
        }
        return nozzles;
    }

    /// <summary>
    /// The lamps: { "lamps": [ { "key", "at", "direction"?, "spot"?, "colour", "energy", "range", "shadows"?, "power"? } ],
    /// "fixtures": { "colour", "energy", "range", "shadows": [names] } (the data file's fixtures, its drive glows left out),
    /// "emergency": { "colour", "energy", "range" } (the same fixtures, dim, without power) }.
    /// </summary>
    public static List<ShipLight> Lights(JsonFields f, JsonElement e, string where, ShipInterior? interior)
    {
        var lights = new List<ShipLight>();
        if (!f.TryChild(e, "lights", out JsonElement o))
        {
            return lights;
        }
        where += ".lights";
        f.OnlyKnown(o, where, "lamps", "fixtures", "emergency", "nav");
        foreach (JsonElement l in f.List(o, "lamps", where, required: false))
        {
            string key = f.Text(l, "key", where + ".lamps");
            string at = $"{where}.lamps {JsonFields.Quote(key)}";
            f.OnlyKnown(l, at, "key", "at", "direction", "spot", "colour", "energy", "range", "shadows", "power", "angle");
            bool spot = f.Flag(l, "spot", at, false);
            lights.Add(new ShipLight(key, PointField(f, l, "at", at, interior).Position, spot ? Vector3.Normalize(f.Vector(l, "direction", at) + new Vector3(0, 0, 1e-9f)) : Vector3.Zero,
                spot, f.Vector(l, "colour", at), f.Number(l, "energy", at), f.Number(l, "range", at), f.Flag(l, "shadows", at, false),
                f.Text(l, "power", at, "ship") == "emergency" ? LightPower.Emergency : LightPower.Ship, f.Number(l, "angle", at, 50f)));
        }
        if (f.TryChild(o, "fixtures", out JsonElement fixtures))
        {
            string at = where + ".fixtures";
            f.OnlyKnown(fixtures, at, "colour", "energy", "range", "shadows", "angle");
            if (interior == null)
            {
                f.Problem(at, "the fixtures are the data file's: the ship has none");
                return lights;
            }
            HashSet<string> shadows = f.Texts(fixtures, "shadows", at).ToHashSet(StringComparer.Ordinal);
            foreach (string name in shadows.Where(s => interior.Fixtures.All(x => x.Name != s)))
            {
                f.Problem(at, $"no fixture \"{name}\" in {interior.File}");
            }
            Vector3 colour = f.Vector(fixtures, "colour", at);
            float energy = f.Number(fixtures, "energy", at), range = f.Number(fixtures, "range", at), angle = f.Number(fixtures, "angle", at, 60f);
            foreach (ShipFixture x in interior.Fixtures)
            {
                lights.Add(new ShipLight(x.Name, x.Position, x.Direction, x.Spot, colour, energy, range, shadows.Contains(x.Name), LightPower.Ship, angle));
            }
            if (f.TryChild(o, "emergency", out JsonElement emergency))
            {
                string eAt = where + ".emergency";
                f.OnlyKnown(emergency, eAt, "colour", "energy", "range");
                Vector3 red = f.Vector(emergency, "colour", eAt);
                float dim = f.Number(emergency, "energy", eAt), near = f.Number(emergency, "range", eAt);
                foreach (ShipFixture x in interior.Fixtures)
                {
                    lights.Add(new ShipLight(x.Name + ":emergency", x.Position, x.Direction, x.Spot, red, dim, near, false, LightPower.Emergency, angle));
                }
            }
        }
        return lights;
    }

    // ---------------------------------------------------------------- checks (after every file is read)

    /// <summary>
    /// A ship's layout against the rest of the data: one helm; seats' exits; a station's seat exposed or not as data says;
    /// the parts' first is the hull, their keys unique, at most 255, each one naming what its capabilities need (a seat's
    /// verb is the seat capability's, a door part names its door); healths positive, their protection a building grade;
    /// doors' seconds sane; nozzles and lamps sane.
    /// </summary>
    public static void Check(ContentChecking c, ShipDef ship, string where)
    {
        GameContent.Loaded loaded = c.Loaded;
        CapabilityData? capabilities = loaded.Capabilities;
        // A kind that takes modules may leave its seats, the helm too, to them (a car's chassis: VEHICLES.md §3.3).
        bool modular = ship.Sockets.Count > 0;
        if (modular ? ship.Seats.Count(s => s.Flies) > 1 : ship.Seats.Count(s => s.Flies) != 1)
        {
            c.Problem(where + ".seats", modular ? "at most one seat flies the ship (role helm)" : "exactly one seat flies the ship (role helm)");
        }
        if (ship.Seats.Count > 31 || ship.Seats.Count == 0 && !modular)
        {
            c.Problem(where + ".seats", modular ? "at most 31 seats" : "1 … 31 seats");
        }
        foreach (ShipSeatDef seat in ship.Seats)
        {
            if (seat.Exits.Count == 0)
            {
                c.Problem($"{where}.seats \"{seat.Key}\"", "a seat needs a way out (exits)");
            }
            if (ship.Interior == null && seat.Door < 0 && !seat.Flies && !seat.Exposed)
            {
                // An open seat (exposed: an open boat's bench) is sat in from outside; a sheltered one through its door.
                c.Problem($"{where}.seats \"{seat.Key}\"", "a ship people only sit in is boarded through a door, unless the seat is in the open (exposed)");
            }
        }
        if (ship.Seats.Select(s => s.Key).Distinct().Count() != ship.Seats.Count || ship.Doors.Select(d => d.Key).Distinct().Count() != ship.Doors.Count ||
            ship.Parts.Select(p => p.Key).Distinct().Count() != ship.Parts.Count)
        {
            c.Problem(where, "seats', doors' and parts' keys are each unique");
        }
        if (ship.Parts.Count is 0 or > 255 || ship.Parts[0].HasBall || ship.Parts[0].Pieces.Count > 0 || !ship.Parts[0].Capabilities.IsEmpty)
        {
            c.Problem(where + ".parts", "1 … 255 parts, the first the hull: the rest of the ship, with no shape or capability of its own");
        }
        if (ship.Doors.Count > 16)
        {
            c.Problem(where + ".doors", "at most 16 doors");
        }
        foreach (ShipDoorDef door in ship.Doors)
        {
            if (door.Seconds is <= 0 or > 60)
            {
                c.Problem($"{where}.doors \"{door.Key}\"", "seconds 0 … 60");
            }
            CheckHealth(c, door.Health, $"{where}.doors \"{door.Key}\"");
        }
        int seatCapability = capabilities?.Index("seat") ?? -1, shipDoor = capabilities?.Index("ship-door") ?? -1;
        var claimed = new HashSet<int>();
        foreach (ShipTargetPart part in ship.Parts)
        {
            string at = $"{where}.parts \"{part.Key}\"";
            if (part.Radius < 0 || (part.Index > 0 && !part.HasBall && part.Pieces.Count == 0))
            {
                c.Problem(at, "a part is met by a ball (at, radius) or by pieces of the data file");
            }
            foreach (int piece in part.Pieces)
            {
                if (!claimed.Add(piece))
                {
                    c.Problem(at, "a part of the data file belongs to one target part at most");
                }
                if (ship.Interior != null && !ship.Interior.Parts[piece].HasPieces)
                {
                    c.Problem(at, $"\"{ship.Interior.Parts[piece].Name}\" has no pieces to meet");
                }
            }
            if (part.Capabilities.Has(seatCapability) != (part.Seat >= 0))
            {
                c.Problem(at, "a part with the seat capability names its seat, and only then");
            }
            if (part.Capabilities.Has(shipDoor) != (part.Door >= 0))
            {
                c.Problem(at, "a part with the ship-door capability names its door, and only then");
            }
            if (part.Door >= 0 && part.Health != null)
            {
                c.Problem(at, "a door's part takes its door's health");
            }
            CheckHealth(c, part.Health, at);
        }
        foreach (ShipSeatDef s in ship.Seats)
        {
            if (!ship.Parts.Any(p => p.Seat == s.Index))
            {
                c.Problem($"{where}.seats \"{s.Key}\"", "no part sits in it");
            }
        }
        foreach (ShipNozzle n in ship.Nozzles)
        {
            if (n.Width <= 0 || n.Length <= 0)
            {
                c.Problem(where + ".nozzles", "plumes have a positive width and length");
                break;
            }
        }
        foreach (ShipLight l in ship.Lights)
        {
            if (l.Energy < 0 || l.Range <= 0 || l.Colour.X < 0 || l.Colour.Y < 0 || l.Colour.Z < 0)
            {
                c.Problem(where + ".lights", $"\"{l.Key}\": a lamp's colour, energy and range are positive");
                break;
            }
        }

    }

    /// <summary>A door's or a part's health against the rest of the data: its numbers, its repair, its protection's grade (resolved here).</summary>
    internal static void CheckHealth(ContentChecking c, ShipPartHealth? health, string at)
    {
        if (health == null)
        {
            return;
        }
        if (health.Max <= 0 || health.RepairFull <= 0 || health.RepairMaterial == null)
        {
            c.Problem(at + ".health", "a positive most health, and a repair's material and cost");
        }
        if (c.Loaded.Building?.Grades.FirstOrDefault(g => g.Key == health.ProtectionKey) is { } grade)
        {
            health.Protection = grade;
        }
        else
        {
            c.Problem(at + ".health", $"no building grade \"{health.ProtectionKey}\" for its protection (building.json)");
        }
    }
}
