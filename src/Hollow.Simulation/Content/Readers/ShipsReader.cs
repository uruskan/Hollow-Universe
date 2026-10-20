using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// ships.json (G6): the kinds of ship: mass and inertia, hit points and damage multipliers, thrust and speeds, boost,
/// turn rates, drag, landing, skids, crash, fuel, cargo, hull boxes, sockets, nozzles, ceiling and wreck. Each hangs
/// off its item (<c>ship</c>).
/// </summary>
internal sealed class ShipsReader : IContentReader
{
    public string File => "ships.json";

    // An empty object, for the blocks a ship may leave out.
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement;

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        JsonElement root = r.Root("ships.json", "ships");
        JsonElement[] list = f.List(root, "ships", "ships.json");
        for (int i = 0; i < list.Length; i++)
        {
            JsonElement e = list[i];
            string key = f.Text(e, "key", $"ships.json: ships[{i}]");
            string where = $"ships.json: {JsonFields.Quote(key)}";
            f.OnlyKnown(e, where, "key", "item", "mass", "inertia", "centreOfMass", "health", "damage", "thrust", "speed", "boost",
                "rates", "drag", "landing", "skids", "crash", "fuel", "cargo", "hull", "seats", "doors", "parts", "nozzles", "lights", "ceiling",
                "wreck", "placement", "model", "interior", "water", "assist", "drive", "controls", "buoyancy");
            ItemDef? item = r.Resolve(f.Text(e, "item", where), where + ".item");
            // The inside people walk in (IRON_WAKE.md §4.3): its data file, read first, since the sockets and the hull may
            // name what it holds.
            Ships.ShipInterior? interior = f.TryChild(e, "interior", out JsonElement interiorBlock)
                ? ShipInteriorReader.Read(r, interiorBlock, where + ".interior")
                : null;
            var blocks = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (string name in new[] { "damage", "thrust", "speed", "rates", "drag", "landing", "skids", "crash", "fuel" })
            {
                if (f.TryChild(e, name, out JsonElement child) && child.ValueKind == JsonValueKind.Object)
                {
                    blocks[name] = child;
                }
                else
                {
                    f.Problem(where, $"needs \"{name}\" (an object)");
                }
            }
            if (blocks.Count < 9)
            {
                continue;
            }
            // A ship without a boost or nozzles (a boat) leaves them out: none, nothing drawn.
            JsonElement boost = f.TryChild(e, "boost", out JsonElement boostBlock) ? boostBlock : Empty;
            JsonElement nozzles = f.TryChild(e, "nozzles", out JsonElement nozzleBlock) ? nozzleBlock : Empty;
            bool boosts = boost.ValueKind == JsonValueKind.Object && boost.EnumerateObject().Any();
            JsonElement damage = blocks["damage"], thrust = blocks["thrust"], speed = blocks["speed"];
            JsonElement rates = blocks["rates"], drag = blocks["drag"], landing = blocks["landing"], skids = blocks["skids"];
            JsonElement crash = blocks["crash"], fuel = blocks["fuel"];
            f.OnlyKnown(damage, where + ".damage", "melee", "arrow", "bullet", "explosive");
            f.OnlyKnown(thrust, where + ".thrust", "main", "retro", "lateral", "lift", "down");
            f.OnlyKnown(speed, where + ".speed", "forward", "reverse", "lateral", "up", "down");
            f.OnlyKnown(boost, where + ".boost", "thrust", "speed", "seconds", "cooldown");
            f.OnlyKnown(rates, where + ".rates", "pitch", "yaw", "roll");
            f.OnlyKnown(drag, where + ".drag", "forward", "side", "top", "turn");
            f.OnlyKnown(landing, where + ".landing", "maxSlope", "assist");
            f.OnlyKnown(skids, where + ".skids", "points", "stiffness", "damping", "travel", "friction");
            f.OnlyKnown(crash, where + ".crash", "safe", "scale", "pilot");
            f.OnlyKnown(fuel, where + ".fuel", "item", "capacity", "idle", "full", "boost", "placed");
            var hull = new List<Ships.HullBox>();
            Vector3[] skidPoints;
            bool fromData = e.TryGetProperty("hull", out JsonElement hullValue) && hullValue.ValueKind == JsonValueKind.String;
            if (fromData)
            {
                // "hull": "data": the boxes the import fitted to the model for the world's sweeps, and the landing pads as
                // the skids (IRON_WAKE.md §5.4), each with a thin skid box a leg's travel above its foot, as the Tin Finch's
                // skid boxes sit: the spring takes the load through its travel, and the box stops it there.
                if (hullValue.GetString() != "data" || interior == null)
                {
                    f.Problem(where + ".hull", "a list of boxes, or \"data\" (the interior's data file's hull)");
                }
                hull.AddRange(interior?.HullBoxes ?? Array.Empty<Ships.HullBox>());
                if (interior != null)
                {
                    float travel = f.Number(skids, "travel", where + ".skids");
                    for (int p = 0; p < interior.Pads.Count; p++)
                    {
                        Vector2 size = interior.PadSizes[p];
                        hull.Add(new Ships.HullBox(interior.Pads[p] + new Vector3(0, travel + 0.15f, 0), new Vector3(size.X / 2, 0.15f, size.Y / 2), true));
                    }
                }
                skidPoints = interior?.Pads.ToArray() ?? Array.Empty<Vector3>();
            }
            else
            {
                foreach (JsonElement h in f.List(e, "hull", where))
                {
                    f.OnlyKnown(h, where + ".hull", "centre", "size", "skid");
                    hull.Add(new Ships.HullBox(f.Vector(h, "centre", where + ".hull"), f.Vector(h, "size", where + ".hull"), f.Flag(h, "skid", where + ".hull", false)));
                }
                skidPoints = f.Vectors(skids, "points", where + ".skids");
            }
            if (fromData && f.Has(skids, "points"))
            {
                f.Problem(where + ".skids", "\"points\" come from the data file's pads when the hull does");
            }
            // Flight assist's level hold (IW3) and the sea (IRON_WAKE.md §5.4), both optional.
            float levelPitch = 0, levelRoll = 0, levelReturn = 0.6f;
            if (f.TryChild(e, "assist", out JsonElement assistBlock))
            {
                f.OnlyKnown(assistBlock, where + ".assist", "level", "return");
                double[]? level = f.Numbers(assistBlock, "level", where + ".assist", 2);
                levelPitch = level == null ? 0 : (float)level[0] * MathF.PI / 180;
                levelRoll = level == null ? 0 : (float)level[1] * MathF.PI / 180;
                levelReturn = f.Number(assistBlock, "return", where + ".assist", 0.6f);
            }
            // A hull that floats on its own shape (docs/MOTORBOAT.md §3), optional.
            Ships.ShipBuoyancy? buoyancy = null;
            if (f.TryChild(e, "buoyancy", out JsonElement buoyancyBlock))
            {
                string bAt = where + ".buoyancy";
                f.OnlyKnown(buoyancyBlock, bAt, "points", "depth", "volume", "damping", "drag", "turn");
                buoyancy = new Ships.ShipBuoyancy
                {
                    Points = f.Vectors(buoyancyBlock, "points", bAt),
                    Depth = f.Number(buoyancyBlock, "depth", bAt),
                    Volume = f.Number(buoyancyBlock, "volume", bAt),
                    Damping = f.Vector(buoyancyBlock, "damping", bAt),
                    Drag = f.Vector(buoyancyBlock, "drag", bAt),
                    TurnDamping = f.Vector(buoyancyBlock, "turn", bAt),
                };
            }
            float waterBelow = 1.2f, waterAbove = 1.2f, waterVolume = 0;
            if (f.TryChild(e, "water", out JsonElement waterBlock))
            {
                f.OnlyKnown(waterBlock, where + ".water", "below", "above", "volume");
                waterBelow = f.Number(waterBlock, "below", where + ".water");
                waterAbove = f.Number(waterBlock, "above", where + ".water");
                waterVolume = f.Number(waterBlock, "volume", where + ".water", 0);
            }
            var wreck = new List<ItemAmount>();
            foreach (JsonElement w in f.List(e, "wreck", where, required: false))
            {
                if (f.Tuple(w, where + ".wreck", 1, out string wreckKey, out double[] values) && r.Resolve(wreckKey, where + ".wreck") is { } wreckItem)
                {
                    wreck.Add(new ItemAmount(wreckItem, (int)values[0]));
                }
            }
            float[] assist = f.List(landing, "assist", where + ".landing", required: false).Where(x => x.ValueKind == JsonValueKind.Number)
                .Select(x => (float)x.GetDouble()).ToArray();
            if (f.Has(landing, "assist") && assist.Length != 2)
            {
                f.Problem(where + ".landing", "\"assist\" is [least descent m/s, descent per metre of height]");
            }
            ItemDef? fuelItem = r.Resolve(f.Text(fuel, "item", where + ".fuel"), where + ".fuel.item");
            if (item == null || fuelItem == null)
            {
                continue;
            }
            // Its seats, doors and target parts (IRON_WAKE.md §5.1), its nozzles and lamps.
            (List<Ships.ShipSeatDef> seats, List<Ships.ShipDoorDef> doors, List<Ships.ShipTargetPart> parts) =
                ShipLayoutReader.Read(r, e, where, item.Name, interior);
            // Its crewed batteries (IRON_WAKE.md §6, IW3): the data file's stations that a seat works, each of a kind of batteries.json.
            List<Ships.ShipStationDef> stations = Stations(r, where, interior, seats, parts);
            // What its engines do (ships.json drive, a kind from Ships.ShipDrives with its own numbers; thrusters without one).
            Ships.IShipDrive drive = Ships.ShipDrives.Read(f, f.TryChild(e, "drive", out JsonElement driveBlock) ? driveBlock : null, where + ".drive");
            var def = new Ships.ShipDef
            {
                Key = key,
                Item = item,
                Drive = drive,
                Controls = f.Text(e, "controls", where, Ships.ShipDef.DefaultControls),
                Mass = f.Number(e, "mass", where),
                Inertia = f.Vector(e, "inertia", where),
                CentreOfMass = f.Vector(e, "centreOfMass", where),
                Health = f.Number(e, "health", where),
                MeleeDamage = f.Number(damage, "melee", where + ".damage"),
                ArrowDamage = f.Number(damage, "arrow", where + ".damage"),
                BulletDamage = f.Number(damage, "bullet", where + ".damage"),
                ExplosiveDamage = f.Number(damage, "explosive", where + ".damage"),
                MainThrust = f.Number(thrust, "main", where + ".thrust"),
                RetroThrust = f.Number(thrust, "retro", where + ".thrust"),
                // Axes a drive doesn't use may be left out (a boat's lift and strafe); its drive judges what it needs.
                LateralThrust = f.Number(thrust, "lateral", where + ".thrust", 0),
                LiftThrust = f.Number(thrust, "lift", where + ".thrust", 0),
                DownThrust = f.Number(thrust, "down", where + ".thrust", 0),
                ForwardSpeed = f.Number(speed, "forward", where + ".speed"),
                ReverseSpeed = f.Number(speed, "reverse", where + ".speed"),
                LateralSpeed = f.Number(speed, "lateral", where + ".speed", 0),
                UpSpeed = f.Number(speed, "up", where + ".speed", 0),
                DownSpeed = f.Number(speed, "down", where + ".speed", 0),
                BoostThrust = boosts ? f.Number(boost, "thrust", where + ".boost") : 1,
                BoostSpeed = boosts ? f.Number(boost, "speed", where + ".boost") : f.Number(speed, "forward", where + ".speed"),
                BoostSeconds = boosts ? f.Number(boost, "seconds", where + ".boost") : 0,
                BoostCooldownSeconds = boosts ? f.Number(boost, "cooldown", where + ".boost") : 0,
                Pitch = f.Has(rates, "pitch") ? f.Turn(rates, "pitch", where + ".rates") : default,
                Yaw = f.Turn(rates, "yaw", where + ".rates"),
                Roll = f.Has(rates, "roll") ? f.Turn(rates, "roll", where + ".rates") : default,
                DragForward = f.Number(drag, "forward", where + ".drag"),
                DragSide = f.Number(drag, "side", where + ".drag"),
                DragTop = f.Number(drag, "top", where + ".drag"),
                TurnDrag = f.Number(drag, "turn", where + ".drag"),
                MaxLandingSlope = f.Number(landing, "maxSlope", where + ".landing") * MathF.PI / 180,
                LandingDescent = assist.Length == 2 ? assist[0] : 1.5f,
                LandingDescentPerMetre = assist.Length == 2 ? assist[1] : 0.25f,
                LevelPitch = levelPitch,
                LevelRoll = levelRoll,
                LevelReturn = levelReturn,
                WaterBelow = waterBelow,
                WaterAbove = waterAbove,
                WaterVolume = waterVolume,
                Buoyancy = buoyancy,
                Model = f.Text(e, "model", where, key),
                Interior = interior,
                SkidPoints = skidPoints,
                SkidStiffness = f.Number(skids, "stiffness", where + ".skids"),
                SkidDamping = f.Number(skids, "damping", where + ".skids"),
                SkidTravel = f.Number(skids, "travel", where + ".skids"),
                SkidFriction = f.Number(skids, "friction", where + ".skids"),
                CrashSafeSpeed = f.Number(crash, "safe", where + ".crash"),
                CrashScale = f.Number(crash, "scale", where + ".crash"),
                CrashPilotShare = f.Number(crash, "pilot", where + ".crash", 0),
                Fuel = fuelItem,
                FuelCapacity = f.Number(fuel, "capacity", where + ".fuel"),
                FuelIdle = f.Number(fuel, "idle", where + ".fuel"),
                FuelFull = f.Number(fuel, "full", where + ".fuel"),
                FuelBoost = f.Number(fuel, "boost", where + ".fuel", 1),
                PlacedFuel = f.Number(fuel, "placed", where + ".fuel", 0),
                CargoSlots = f.Integer(e, "cargo", where, 0),
                Hull = hull,
                Seats = seats,
                Doors = doors,
                Parts = parts,
                Stations = stations,
                CargoPart = parts.FirstOrDefault(p => p.Capabilities.Has(r.Loaded.Capabilities?.Index("container") ?? -1))?.Index ?? 0,
                Nozzles = ShipLayoutReader.Nozzles(f, nozzles, where + ".nozzles", interior),
                Lights = ShipLayoutReader.Lights(f, e, where, interior),
                NavLights = !(f.TryChild(e, "lights", out JsonElement lightsBlock) && lightsBlock.ValueKind == JsonValueKind.Object &&
                    lightsBlock.TryGetProperty("nav", out JsonElement nav) && nav.ValueKind == JsonValueKind.False),
                Ceiling = f.Number(e, "ceiling", where),
                Wreck = wreck,
            };
            def.ComputeBounds();
            loaded.Ships.Add(def);
        }
        foreach (ItemDef item in loaded.Items)
        {
            if (item.ShipKey == null)
            {
                continue;
            }
            Ships.ShipDef? def = loaded.Ships.FirstOrDefault(s => s.Key == item.ShipKey);
            if (def == null)
            {
                f.Problem($"items.json: {JsonFields.Quote(item.Key)}.ship", $"no ship \"{item.ShipKey}\" in ships.json");
            }
            else if (def.Item != item)
            {
                f.Problem($"ships.json: {JsonFields.Quote(def.Key)}", $"its item is \"{def.Item.Key}\", but \"{item.Key}\" says it is that ship");
            }
            item.Ship = def;
        }
    }

    // The stations a ship's seats work (IRON_WAKE.md §6.1): the data file's geometry, the battery its kind names, the seat
    // and the target part (its health, its magazine) and the yaw's limits: its level arcs' span, and for a battery that
    // turns a set way from its beam, that much each way from the station's side within them.
    private static List<Ships.ShipStationDef> Stations(ContentReading r, string where, Ships.ShipInterior? interior,
        List<Ships.ShipSeatDef> seats, List<Ships.ShipTargetPart> parts)
    {
        var stations = new List<Ships.ShipStationDef>();
        if (interior == null)
        {
            return stations;
        }
        JsonFields f = r.Fields;
        const float Degrees = MathF.PI / 180;
        foreach (Ships.StationGeometry g in interior.StationData)
        {
            Ships.ShipSeatDef? seat = seats.FirstOrDefault(s => s.Role == Ships.SeatRole.Station && s.Station == g.Id);
            if (seat == null)
            {
                continue; // a station nobody can sit at: decoration
            }
            string at = $"{where}: station {JsonFields.Quote(g.Id)}";
            BatteryDef? battery = r.Loaded.Batteries.FirstOrDefault(b => b.Key == g.Kind);
            if (battery == null)
            {
                f.Problem(at, $"no battery \"{g.Kind}\" in batteries.json");
                continue;
            }
            Ships.ShipTargetPart? part = parts.FirstOrDefault(p => p.Seat == seat.Index);
            if (part == null)
            {
                f.Problem(at, $"no part names its seat \"{seat.Key}\"");
                continue;
            }
            if (g.Barrels.Count == 0 || g.Barrels.Count > 8)
            {
                f.Problem(at, "one to eight barrels");
                continue;
            }
            // The yaw's span: its arcs (an arc whose end is below its start runs through the stern), none a full circle.
            float lo = -180, hi = 180;
            if (g.Arcs.Count > 0)
            {
                lo = float.MaxValue;
                hi = float.MinValue;
                foreach ((float from, float to) in g.Arcs)
                {
                    float end = to < from ? to + 360 : to;
                    lo = MathF.Min(lo, from);
                    hi = MathF.Max(hi, end);
                }
            }
            if (battery.Mount.Outboard > 0 && g.Side != 0)
            {
                float beam = g.Side > 0 ? 90 : -90, each = battery.Mount.Outboard / Degrees;
                float best = -1, bestLo = lo, bestHi = hi;
                foreach (float turn in new[] { -360f, 0f, 360f })
                {
                    float a = MathF.Max(lo, beam - each + turn), b = MathF.Min(hi, beam + each + turn);
                    if (b - a > best)
                    {
                        (best, bestLo, bestHi) = (b - a, a, b);
                    }
                }
                if (best <= 0)
                {
                    f.Problem(at, "its beam's turn and its arcs don't meet");
                    continue;
                }
                (lo, hi) = (bestLo, bestHi);
            }
            if (hi - lo >= 360)
            {
                (lo, hi) = (-180, 180);
            }
            var barrels = new List<Ships.StationBarrel>();
            for (int i = 0; i < g.Barrels.Count; i++)
            {
                Ships.StationBarrelData b = g.Barrels[i];
                barrels.Add(new Ships.StationBarrel { Index = i, RecoilPart = b.RecoilPart, Muzzle = b.Muzzle, Breech = b.Breech, Side = b.Side, Length = b.Length });
            }
            var station = new Ships.ShipStationDef
            {
                Index = stations.Count,
                Id = g.Id,
                Battery = battery,
                Seat = seat.Index,
                Part = part.Index,
                YawPart = g.YawPart,
                PitchPart = g.PitchPart,
                Side = g.Side,
                YawMin = lo * Degrees,
                YawMax = hi * Degrees,
                Barrels = barrels,
                Camera = g.Camera,
            };
            part.Station = station.Index;
            stations.Add(station);
        }
        if (stations.Count > 15)
        {
            f.Problem(where, "at most 15 stations (a shot names its station in four bits)");
        }
        return stations;
    }

    // A ship people walk in (IRON_WAKE.md §4.3, §9.1): pieces to stand on, a boarding volume, sane margins, and the seat,
    // its eye and every exit inside the boarding volume (exits stand aboard: a pilot steps out onto the deck).
    private static void CheckInterior(ContentChecking c, Ships.ShipDef ship, Ships.ShipInterior interior, string where)
    {
        if (interior.Pieces.Count == 0 || interior.Boarding.Count == 0 || interior.Parts.Count == 0)
        {
            c.Problem(where + ".interior", $"{interior.File} needs convex pieces, a boarding volume and parts");
            return;
        }
        if (interior.Margin is < 0 or > 2 || interior.DeckGravity is < 0 or > 2)
        {
            c.Problem(where + ".interior", "margin 0 … 2 m, gravity 0 … 2 times a character's");
        }
        if (Vector3.Distance(ship.CentreOfMass, interior.CentreOfMass) > 0.001f)
        {
            // The ship's frame is placed by its centre of mass (ShipPose) and its parked body is built round the data
            // file's: they must be the same point.
            c.Problem(where + ".centreOfMass", $"must be the centre of mass of {interior.File} ({interior.CentreOfMass})");
        }
        float[] rest = interior.Rest;
        foreach (Ships.ShipSeatDef seat in ship.Seats)
        {
            foreach ((string name, Ships.ShipSocket point) in new[] { ("at", seat.At), ("eye", seat.Eye) }
                         .Concat(seat.Exits.Select((e, i) => ($"exits[{i}]", e))))
            {
                if (interior.OutsideBoarding(interior.SocketAt(point, rest), rest) > 0)
                {
                    c.Problem($"{where}.seats \"{seat.Key}\".{name}", $"must be inside the boarding volume of {interior.File} (aboard)");
                }
            }
        }
    }

    /// <summary>Keys unique; each one's item says it is that ship; the flight model's, skids', fuel's and hull's numbers in range (G6).</summary>
    public void Check(ContentChecking c)
    {
        GameContent.Loaded loaded = c.Loaded;
        c.Duplicates("ships.json", loaded.Ships.Select(x => x.Key));
        foreach (Ships.ShipDef ship in loaded.Ships)
        {
            string where = $"ships.json: \"{ship.Key}\"";
            if (ship.Item.Ship != ship)
            {
                c.Problem(where, $"item \"{ship.Item.Key}\" must say \"ship\": \"{ship.Key}\"");
            }
            if (ship.Mass <= 0 || ship.Inertia.X <= 0 || ship.Inertia.Y <= 0 || ship.Inertia.Z <= 0 || ship.Health <= 0)
            {
                c.Problem(where, "mass, inertia and health must be positive");
            }
            // Thrust, speeds, boost and rates are its drive's to judge (thrusters want every axis).
            foreach (string problem in ship.Drive.Problems(ship))
            {
                c.Problem(where, problem);
            }
            if (new[] { ship.DragForward, ship.DragSide, ship.DragTop, ship.TurnDrag }.Any(d => d < 0))
            {
                c.Problem(where, "drag can't be negative");
            }
            if (ship.SkidPoints.Count < 3 || ship.SkidStiffness <= 0 || ship.SkidDamping <= 0 || ship.SkidTravel is <= 0 or > 1 ||
                ship.SkidFriction is < 0 or > 2 || ship.MaxLandingSlope is <= 0 or > 0.8f)
            {
                c.Problem(where, "at least three skid points, positive springs, travel 0 … 1 m, friction 0 … 2, a landing slope up to 45°");
            }
            if (ship.CrashSafeSpeed <= 0 || ship.CrashScale <= 0 || ship.CrashPilotShare is < 0 or > 1)
            {
                c.Problem(where, "crash: a positive safe speed and scale, a pilot share of 0 … 1");
            }
            if (ship.FuelCapacity <= 0 || ship.FuelIdle < 0 || ship.FuelFull < ship.FuelIdle || ship.FuelBoost < 1)
            {
                c.Problem(where, "fuel: a tank, idle ≤ full burn, boost at least 1");
            }
            if (ship.Hull.Count == 0 || ship.Hull.Any(h => h.HalfExtents.X <= 0 || h.HalfExtents.Y <= 0 || h.HalfExtents.Z <= 0))
            {
                c.Problem(where, "hull: boxes of positive size");
            }
            if (ship.CargoSlots is < 0 or > 64 || ship.Ceiling is < 1 or > 3 ||
                new[] { ship.MeleeDamage, ship.ArrowDamage, ship.BulletDamage, ship.ExplosiveDamage }.Any(m => m is < 0 or > 10))
            {
                c.Problem(where, "cargo 0 … 64 slots, a ceiling of 1 … 3 world radii, damage multipliers 0 … 10");
            }
            ShipLayoutReader.Check(c, ship, where);
            foreach (string problem in ship.Buoyancy?.Problems(ship) ?? Enumerable.Empty<string>())
            {
                c.Problem(where, problem);
            }
            if (ship.WaterBelow <= 0 || ship.WaterAbove <= 0 || ship.WaterVolume < 0 || ship.LevelPitch is < 0 or > 1.05f ||
                ship.LevelRoll is < 0 or > 1.05f || ship.LevelReturn is < 0 or > 10)
            {
                c.Problem(where, "water: a positive band below and above the centre of mass, a volume; assist.level: 0 … 60° of pitch and roll");
            }
            if (ship.Interior is { } interior)
            {
                CheckInterior(c, ship, interior, where);
            }
        }
    }
}
