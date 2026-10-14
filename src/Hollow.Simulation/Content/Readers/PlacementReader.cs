using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Simulation.Building;
using Hollow.Simulation.Placement;

namespace Hollow.Simulation.Content.Readers;

/// <summary>
/// placement.json (GROUND_SYSTEMS.md §6): the defaults every placement rule starts from, each kind's block over them, the
/// append-only list of placeables (the wire's numbers), and the names of the solvers, rotations, checks and kinds; each
/// placeable's own <c>placement</c> block comes from next to its definition (its kind knows where). Read after every other
/// file, since a placeable names their things; the handlers are sealed against the names here, and the building's reach,
/// sight slack and claim padding are the piece rule's.
/// </summary>
internal sealed class PlacementReader : IContentReader
{
    private static readonly string[] RuleFields =
    {
        "surfaces", "solver", "footprint", "support", "supportStep", "maxSlope", "maxStep", "water", "align", "rotation", "turn", "reach",
        "sight", "privilege", "padding", "limits", "tolerance",
    };

    private static readonly Dictionary<string, PlacementSurfaces> SurfaceNames = new(StringComparer.Ordinal)
    {
        ["terrain"] = PlacementSurfaces.Terrain, ["floor"] = PlacementSurfaces.Floor, ["wall"] = PlacementSurfaces.Wall,
        ["roof"] = PlacementSurfaces.Roof, ["opening"] = PlacementSurfaces.Opening, ["door"] = PlacementSurfaces.Door,
        ["thing"] = PlacementSurfaces.Thing, ["natural"] = PlacementSurfaces.Natural, ["ship"] = PlacementSurfaces.Ship,
        ["any"] = PlacementSurfaces.Any,
    };

    public string File => PlacementData.File;

    public void Read(ContentReading r)
    {
        JsonFields f = r.Fields;
        GameContent.Loaded loaded = r.Loaded;
        IReadOnlyDictionary<string, JsonElement> documents = r.Documents;
        const string file = PlacementData.File;
        JsonElement root = documents.GetValueOrDefault(file);
        if (root.ValueKind != JsonValueKind.Object)
        {
            loaded.Placement = PlacementData.Empty;
            return;
        }
        f.OnlyKnown(root, file, "source", "note", "defaults", "kinds", "pieceSurfaces", "solvers", "rotations", "checks", "placeables");
        string[] solvers = f.Texts(root, "solvers", file);
        string[] rotations = f.Texts(root, "rotations", file);
        string[] checks = f.Texts(root, "checks", file);
        JsonElement defaults = f.TryChild(root, "defaults", out JsonElement d) ? d : default;
        if (defaults.ValueKind != JsonValueKind.Object)
        {
            f.Problem(file, "\"defaults\" is missing");
        }
        var kindBlocks = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (f.TryChild(root, "kinds", out JsonElement kinds))
        {
            foreach (JsonProperty kind in kinds.EnumerateObject())
            {
                kindBlocks[kind.Name] = kind.Value;
            }
        }
        else
        {
            f.Problem(file, "\"kinds\" is missing");
        }
        var problems = new List<string>();
        PlacementHandlers? handlers = PlacementHandlers.Create(solvers, rotations, checks, kindBlocks.Keys.ToArray(), problems);
        foreach (string problem in problems)
        {
            f.Problem(file, problem);
        }
        if (handlers == null)
        {
            // Nothing below can be read until the names are right (every placeable would say so again).
            loaded.Placement = PlacementData.Empty;
            return;
        }

        var pieceSurfaces = new Dictionary<PieceFamily, PlacementSurfaces>();
        if (f.TryChild(root, "pieceSurfaces", out JsonElement families))
        {
            foreach (JsonProperty family in families.EnumerateObject())
            {
                if (!Enum.TryParse(family.Name, ignoreCase: true, out PieceFamily pieceFamily))
                {
                    f.Problem($"{file}: pieceSurfaces", $"no piece family \"{family.Name}\"");
                    continue;
                }
                pieceSurfaces[pieceFamily] = Surfaces(f, family.Value, $"{file}: pieceSurfaces.{family.Name}");
            }
        }
        foreach (PieceFamily family in Enum.GetValues<PieceFamily>().Where(x => !pieceSurfaces.ContainsKey(x)))
        {
            f.Problem($"{file}: pieceSurfaces", $"the {family.ToString().ToLowerInvariant()} family offers no surface");
        }

        var sources = new PlacementSources(loaded, documents);
        var placeables = new List<Placeable>();
        JsonElement[] list = f.List(root, "placeables", file);
        var seen = new HashSet<(string, string)>();
        for (int i = 0; i < list.Length; i++)
        {
            JsonElement e = list[i];
            string where = $"{file}: placeables[{i}]";
            f.OnlyKnown(e, where, "kind", "key");
            string kindName = f.Text(e, "kind", where), key = f.Text(e, "key", where);
            where = $"{file}: {kindName} {JsonFields.Quote(key)}";
            if (!seen.Add((kindName, key)))
            {
                f.Problem(where, "is listed twice");
            }
            if (handlers.Kinds.Find(handlers.Kinds.IndexOf(kindName)) is not { } kind)
            {
                f.Problem(where, $"no kind \"{kindName}\" in \"kinds\"");
                placeables.Add(Unknown(i, key));
                continue;
            }
            if (kind.Resolve(key, sources) is not { } resolved)
            {
                f.Problem(where, $"names no {kindName} in the game data");
                placeables.Add(Unknown(i, key));
                continue;
            }
            PlacementRule rule = Rule(f, where, solvers, rotations, defaults, kindBlocks.GetValueOrDefault(kindName), kind.Own(key, sources));
            Footprint footprint = rule.HalfExtents is { } half ? new BoxFootprint(half) : kind.OwnFootprint(resolved.Definition);
            if (rule.Hull && footprint is not HullFootprint)
            {
                f.Problem(where, "only a ship has a hull for its footprint");
            }
            var placeable = new Placeable
            {
                Index = i,
                Key = key,
                Kind = kind,
                Rule = rule,
                Definition = resolved.Definition,
                Item = resolved.Item,
                Tool = resolved.Tool,
                Costs = resolved.Costs,
                Footprint = footprint,
                ModelKey = resolved.ModelKey,
                Name = resolved.Name,
            };
            placeables.Add(placeable);
            if (resolved.Item is { } item)
            {
                if (item.Places != null)
                {
                    f.Problem(where, $"its item \"{item.Key}\" places {item.Places} already");
                }
                item.Places = placeable;
            }
            if (resolved.Item == null && resolved.Tool == null)
            {
                f.Problem(where, "nothing places it: no item is used up placing it, and no tool needed");
            }
        }
        loaded.Placement = new PlacementData(placeables, solvers, rotations, checks, kindBlocks.Keys.ToArray(), pieceSurfaces) { Handlers = handlers };
        Complete(f, loaded);
        // The building's reach (the hammer's too), sight slack and claim padding are the piece rule's.
        if (loaded.Placement.Placeables.FirstOrDefault(p => p.Kind.Name == "piece") is { } piece)
        {
            loaded.Building?.UsePlacement(piece.Rule.Reach, piece.Rule.Sight, piece.Rule.Padding);
        }
    }

    public void Check(ContentChecking c)
    {
        // Checked as it is read: the names against the handlers, each placeable by its kind, and that everything placeable is listed.
    }

    // A placeable that names nothing keeps its number (the list is the wire's), but nobody can place it.
    private static Placeable Unknown(int index, string key) =>
        new() { Index = index, Key = key, Kind = Placement.Kinds.All[0], Rule = new PlacementRule(), Definition = new object() };

    // Everything that can be placed is listed: each deployable, door with an item, lock, the claim device, charge, ship.
    private static void Complete(JsonFields f, GameContent.Loaded loaded)
    {
        PlacementData data = loaded.Placement!;
        void Expect(object? definition, string what)
        {
            if (definition != null && data.Of(definition) == null)
            {
                f.Problem(PlacementData.File, $"{what} can't be placed: \"placeables\" doesn't list it");
            }
        }
        foreach (DeployableDef d in loaded.Deployables)
        {
            Expect(d, $"the deployable \"{d.Key}\"");
        }
        foreach (ItemDef item in loaded.Items)
        {
            Expect(item.Door, $"the door \"{item.Door?.Key}\"");
            Expect(item.Throwable is { Sticky: true } ? item.Throwable : null, $"the charge \"{item.Key}\"");
        }
        foreach (Ships.ShipDef ship in loaded.Ships)
        {
            Expect(ship, $"the ship \"{ship.Key}\"");
        }
        if (loaded.Building is { } building)
        {
            foreach (LockDef l in building.Locks)
            {
                Expect(l, $"the lock \"{l.Key}\"");
            }
            foreach (PieceDef piece in building.Pieces)
            {
                Expect(piece, $"the piece \"{piece.Key}\"");
            }
            Expect(loaded.ClaimDeployable, "the claim device");
        }
    }

    // One rule from its layers (defaults, its kind's block, its own block), each field from the last layer that has it.
    private static PlacementRule Rule(JsonFields f, string where, string[] solvers, string[] rotations, params JsonElement[] layers)
    {
        var fields = new Dictionary<string, (JsonElement Value, string Where)>(StringComparer.Ordinal);
        string[] names = { "defaults", "kind", "own" };
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i].ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }
            string at = $"{where} ({names[Math.Min(i, names.Length - 1)]} placement)";
            if (layers[i].ValueKind != JsonValueKind.Object)
            {
                f.Problem(at, "a placement block must be an object");
                continue;
            }
            f.OnlyKnown(layers[i], at, RuleFields);
            foreach (JsonProperty p in layers[i].EnumerateObject())
            {
                if (p.Name == "limits" && p.Value.ValueKind == JsonValueKind.Object)
                {
                    f.OnlyKnown(p.Value, at + ".limits", "structure", "player");
                    foreach (JsonProperty limit in p.Value.EnumerateObject())
                    {
                        fields["limits." + limit.Name] = (limit.Value, at);
                    }
                    continue;
                }
                fields[p.Name] = (p.Value, at);
            }
        }
        float Number(string name, float fallback, float min, float max)
        {
            if (!fields.TryGetValue(name, out (JsonElement Value, string Where) v))
            {
                return fallback;
            }
            if (v.Value.ValueKind != JsonValueKind.Number || !v.Value.TryGetDouble(out double x) || !double.IsFinite(x) || x < min || x > max)
            {
                f.Problem(v.Where, $"\"{name}\" must be a number from {min} to {max}");
                return fallback;
            }
            return (float)x;
        }
        string Text(string name, string fallback, IReadOnlyCollection<string> allowed)
        {
            if (!fields.TryGetValue(name, out (JsonElement Value, string Where) v))
            {
                return fallback;
            }
            string text = v.Value.ValueKind == JsonValueKind.String ? v.Value.GetString() ?? "" : "";
            if (!allowed.Contains(text))
            {
                f.Problem(v.Where, $"\"{name}\" must be one of {string.Join(", ", allowed)}");
                return fallback;
            }
            return text;
        }

        PlacementSurfaces surfaces = fields.TryGetValue("surfaces", out (JsonElement Value, string Where) s)
            ? Surfaces(f, s.Value, s.Where)
            : PlacementSurfaces.Terrain | PlacementSurfaces.Floor;
        string solver = Text("solver", solvers.FirstOrDefault() ?? "free", solvers);

        Vector3? half = null;
        bool hull = false;
        if (fields.TryGetValue("footprint", out (JsonElement Value, string Where) fp))
        {
            if (fp.Value.ValueKind == JsonValueKind.String && fp.Value.GetString() is "own" or "hull")
            {
                hull = fp.Value.GetString() == "hull";
            }
            else if (fp.Value.ValueKind == JsonValueKind.Array && fp.Value.GetArrayLength() == 3 &&
                     fp.Value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Number && x.GetDouble() > 0 && x.GetDouble() < 20))
            {
                double[] xyz = fp.Value.EnumerateArray().Select(x => x.GetDouble()).ToArray();
                half = new Vector3((float)xyz[0], (float)xyz[1], (float)xyz[2]);
            }
            else
            {
                f.Problem(fp.Where, "\"footprint\" must be \"own\", \"hull\" or half extents [x, y, z]");
            }
        }

        string rotation = "free";
        float stepDegrees = 0;
        if (fields.TryGetValue("rotation", out (JsonElement Value, string Where) r))
        {
            if (r.Value.ValueKind == JsonValueKind.Number && r.Value.GetDouble() is > 0 and <= 180)
            {
                rotation = "steps";
                stepDegrees = (float)r.Value.GetDouble();
            }
            else
            {
                rotation = Text("rotation", "free", rotations.Where(n => n != "steps").ToArray());
            }
        }
        if (Array.IndexOf(rotations, rotation) < 0)
        {
            f.Problem(where, $"its rotation \"{rotation}\" isn't in \"rotations\"");
        }

        float sight = 0.1f;
        if (fields.TryGetValue("sight", out (JsonElement Value, string Where) sv))
        {
            sight = sv.Value.ValueKind == JsonValueKind.False ? -1 : Number("sight", 0.1f, 0, 2);
        }

        return new PlacementRule
        {
            Surfaces = surfaces,
            Solver = solver,
            SolverIndex = Math.Max(0, Array.IndexOf(solvers, solver)),
            HalfExtents = half,
            Hull = hull,
            Support = Text("support", "corners", new[] { "centre", "corners" }) == "corners" ? SupportMode.Corners : SupportMode.Centre,
            SupportStep = Number("supportStep", 0.15f, 0, 2),
            MaxSlopeDegrees = Number("maxSlope", 30, 0, 180),
            MaxStep = Number("maxStep", 0.6f, 0, 3),
            Water = fields.TryGetValue("water", out (JsonElement Value, string Where) w) && w.Value.ValueKind == JsonValueKind.True,
            Align = Text("align", "up", new[] { "up", "normal" }) == "normal" ? AlignMode.Normal : AlignMode.Up,
            Rotation = rotation,
            RotationIndex = Math.Max(0, Array.IndexOf(rotations, rotation)),
            StepDegrees = stepDegrees,
            TurnDegrees = Number("turn", 90, 0, 360),
            Reach = Number("reach", 3, 0.5f, 30),
            Sight = sight,
            Privilege = Text("privilege", "blocked", new[] { "ignored", "blocked", "needed" }) switch
            {
                "ignored" => PrivilegeMode.Ignored,
                "needed" => PrivilegeMode.Needed,
                _ => PrivilegeMode.Blocked,
            },
            Padding = Number("padding", 0, 0, 10),
            PerStructure = (int)Number("limits.structure", 0, 0, 10_000),
            PerPlayer = (int)Number("limits.player", 0, 0, 10_000),
            Tolerance = Number("tolerance", 0.3f, 0.01f, 2),
        };
    }

    private static PlacementSurfaces Surfaces(JsonFields f, JsonElement value, string where)
    {
        IEnumerable<JsonElement> names = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : new[] { value };
        PlacementSurfaces surfaces = PlacementSurfaces.None;
        foreach (JsonElement name in names)
        {
            if (name.ValueKind == JsonValueKind.String && SurfaceNames.TryGetValue(name.GetString() ?? "", out PlacementSurfaces surface))
            {
                surfaces |= surface;
            }
            else
            {
                f.Problem(where, $"unknown surface {name} (known: {string.Join(", ", SurfaceNames.Keys)})");
            }
        }
        return surfaces;
    }
}
