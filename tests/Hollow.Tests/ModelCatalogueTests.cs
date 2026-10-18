using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hollow.Tests;

/// <summary>
/// The ground game's model catalogue (GROUND.md §9), checked without Godot: every model file is a GLB with its LOD
/// meshes, hulls and sockets, every material it uses maps to a shared Godot material, every icon is a square RGBA PNG
/// at each size, and every item has a model.
/// </summary>
public static class ModelCatalogueTests
{
    // The item keys of G2 (game/data/ground/items.json) and those coming in G3-G6.
    private static readonly string[] RequiredItems =
    {
        "wood", "stone", "metal-ore", "sulphur-ore", "high-grade-ore", "metal-fragments", "sulphur", "refined-metal",
        "charcoal", "fibre", "lamp-oil", "rock", "torch", "stone-hatchet", "stone-pickaxe", "metal-hatchet",
        "metal-pickaxe", "wooden-spear", "stone-spear", "machete", "bow", "arrow", "bandage", "medical-injector",
        "red-berries", "blue-berries", "wild-tuber", "roasted-tuber", "woven-hood", "woven-shirt", "woven-trousers",
        "woven-gloves", "work-boots", "chest-plate", "sleeping-bag", "campfire", "furnace", "small-box", "large-box",
        "workbench-1", "workbench-2", "workbench-3",
        "building-plan", "hammer", "claim-device", "key-lock", "code-lock", "wooden-door", "metal-door", "armoured-door",
        "double-door", "garage-door", "ladder", "pistol", "rifle", "pistol-ammo", "rifle-ammo", "timed-charge",
        "vehicle-workbench", "engineering-workbench", "bed",
        // G4: attachments and ammunition types.
        "holo-sight", "scope", "silencer", "weapon-light", "muzzle-brake", "pistol-ammo-hv", "rifle-ammo-hv",
        "rifle-ammo-incendiary", "pistol-ammo-incendiary", "gunpowder", "metal-mask",
        // G5: the raid package's keys; IW4's incendiary rocket.
        "bag-charge", "tin-grenade", "rocket-launcher", "rocket", "rocket-hv", "rifle-ammo-explosive", "rocket-incendiary",
    };

    // Models that replaced stand-ins: none of them may be marked as a stand-in again.
    private static readonly string[] NotStandIns = { "gunpowder", "metal-mask", "pistol-ammo-incendiary", "cloth", "sewing-kit", "metal-blade", "sign-plate", "receiver", "rifle-frame", "torn-schematic", "sealed-schematic", "salvage" };

    [Test]
    public static void ClothHasItsOwnModelAndRenderedIcon()
    {
        string path = Path.Combine(Fixtures.Root, "game", "data", "ground", "models.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement cloth = doc.RootElement.GetProperty("models").EnumerateArray()
            .Single(m => m.GetProperty("key").GetString() == "cloth");
        Assert.Equal("res://assets/items/cloth.glb", cloth.GetProperty("file").GetString(), "cloth model identity");
        Assert.True(!cloth.GetProperty("icon").TryGetProperty("key", out _), "cloth must render its own icon, not borrow another item's");
        Assert.True(!cloth.TryGetProperty("standIn", out _), "cloth must not return to the fibre stand-in");
    }

    [Test]
    public static void SewingKitHasItsOwnModelAndRenderedIcon()
    {
        string path = Path.Combine(Fixtures.Root, "game", "data", "ground", "models.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement kit = doc.RootElement.GetProperty("models").EnumerateArray()
            .Single(m => m.GetProperty("key").GetString() == "sewing-kit");
        Assert.Equal("res://assets/items/sewing-kit.glb", kit.GetProperty("file").GetString(), "sewing kit model identity");
        Assert.True(!kit.GetProperty("icon").TryGetProperty("key", out _), "sewing kit must render its own icon");
        Assert.True(!kit.TryGetProperty("standIn", out _), "sewing kit must not return to the fibre stand-in");
    }

    [Test]
    public static void MetalBladeHasItsOwnModelAndRenderedIcon()
    {
        string path = Path.Combine(Fixtures.Root, "game", "data", "ground", "models.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement blade = doc.RootElement.GetProperty("models").EnumerateArray()
            .Single(m => m.GetProperty("key").GetString() == "metal-blade");
        Assert.Equal("res://assets/items/metal-blade.glb", blade.GetProperty("file").GetString(), "metal blade model identity");
        Assert.True(!blade.GetProperty("icon").TryGetProperty("key", out _), "metal blade must render its own icon");
        Assert.True(!blade.TryGetProperty("standIn", out _), "metal blade must not return to the refined-metal stand-in");
    }

    [Test]
    public static void SignPlateHasItsOwnModelAndRenderedIcon()
    {
        string path = Path.Combine(Fixtures.Root, "game", "data", "ground", "models.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement sign = doc.RootElement.GetProperty("models").EnumerateArray()
            .Single(m => m.GetProperty("key").GetString() == "sign-plate");
        Assert.Equal("res://assets/items/sign-plate.glb", sign.GetProperty("file").GetString(), "sign plate model identity");
        Assert.True(!sign.GetProperty("icon").TryGetProperty("key", out _), "sign plate must render its own icon");
        Assert.True(!sign.TryGetProperty("standIn", out _), "sign plate must not return to the refined-metal stand-in");
    }

    [Test]
    public static void ReceiverHasItsOwnModelAndRenderedIcon()
    {
        string path = Path.Combine(Fixtures.Root, "game", "data", "ground", "models.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement item = doc.RootElement.GetProperty("models").EnumerateArray()
            .Single(m => m.GetProperty("key").GetString() == "receiver");
        Assert.Equal("res://assets/items/receiver.glb", item.GetProperty("file").GetString(), "receiver model identity");
        Assert.True(!item.GetProperty("icon").TryGetProperty("key", out _), "receiver must render its own icon");
        Assert.True(!item.TryGetProperty("standIn", out _), "receiver must not return to the refined-metal stand-in");
    }

    [Test]
    public static void RifleFrameHasItsOwnModelAndRenderedIcon()
    {
        string path = Path.Combine(Fixtures.Root, "game", "data", "ground", "models.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement item = doc.RootElement.GetProperty("models").EnumerateArray()
            .Single(m => m.GetProperty("key").GetString() == "rifle-frame");
        Assert.Equal("res://assets/items/rifle-frame.glb", item.GetProperty("file").GetString(), "rifle frame model identity");
        Assert.True(!item.GetProperty("icon").TryGetProperty("key", out _), "rifle frame must render its own icon");
        Assert.True(!item.TryGetProperty("standIn", out _), "rifle frame must not return to the refined-metal stand-in");
    }

    [Test]
    public static void TornSchematicHasItsOwnModelAndRenderedIcon()
    {
        string path = Path.Combine(Fixtures.Root, "game", "data", "ground", "models.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement item = doc.RootElement.GetProperty("models").EnumerateArray()
            .Single(m => m.GetProperty("key").GetString() == "torn-schematic");
        Assert.Equal("res://assets/items/torn-schematic.glb", item.GetProperty("file").GetString(), "torn schematic model identity");
        Assert.True(!item.GetProperty("icon").TryGetProperty("key", out _), "torn schematic must render its own icon");
        Assert.True(!item.TryGetProperty("standIn", out _), "torn schematic must not return to the building-plan stand-in");
    }

    [Test]
    public static void SealedSchematicHasItsOwnModelAndRenderedIcon()
    {
        string path = Path.Combine(Fixtures.Root, "game", "data", "ground", "models.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement item = doc.RootElement.GetProperty("models").EnumerateArray()
            .Single(m => m.GetProperty("key").GetString() == "sealed-schematic");
        Assert.Equal("res://assets/items/sealed-schematic.glb", item.GetProperty("file").GetString(), "sealed schematic model identity");
        Assert.True(!item.GetProperty("icon").TryGetProperty("key", out _), "sealed schematic must render its own icon");
        Assert.True(!item.TryGetProperty("standIn", out _), "sealed schematic must not return to the building-plan stand-in");
    }

    [Test]
    public static void SalvageHasItsOwnModelAndRenderedIcon()
    {
        string path = Path.Combine(Fixtures.Root, "game", "data", "ground", "models.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement item = doc.RootElement.GetProperty("models").EnumerateArray()
            .Single(m => m.GetProperty("key").GetString() == "salvage");
        Assert.Equal("res://assets/items/salvage.glb", item.GetProperty("file").GetString(), "salvage model identity");
        Assert.True(!item.GetProperty("icon").TryGetProperty("key", out _), "salvage must render its own icon");
        Assert.True(!item.TryGetProperty("standIn", out _), "salvage must not return to the metal-fragments stand-in");
    }

    // World objects the gathering rules place (gather.json) and the ship.
    private static readonly string[] RequiredWorld =
    {
        "stone-node", "metal-node", "sulphur-node", "wood-pile", "stone-pile", "metal-pile", "sulphur-pile",
        "loot-sack", "dropped-bag", "tin-finch", "iron-wake",
        // G7 props.
        "loot-barrel-1", "loot-barrel-2", "loot-crate", "military-crate", "recycler",
    };

    [Test]
    public static void ModelCatalogueIsComplete()
    {
        string game = Path.Combine(Fixtures.Root, "game");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(game, "data", "ground", "models.json")));
        JsonElement icons = doc.RootElement.GetProperty("icons");
        string iconPattern = icons.GetProperty("path").GetString()!;
        int[] sizes = icons.GetProperty("sizes").EnumerateArray().Select(s => s.GetInt32()).ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var slots = new HashSet<string>(StringComparer.Ordinal);
        var mounts = new HashSet<string>(StringComparer.Ordinal);
        int files = 0, iconFiles = 0;
        foreach (JsonElement m in doc.RootElement.GetProperty("models").EnumerateArray())
        {
            string key = m.GetProperty("key").GetString()!;
            Assert.True(keys.Add(key), $"{key}: listed twice");
            Assert.True(!NotStandIns.Contains(key) || !m.TryGetProperty("standIn", out _), $"{key}: still marked as a stand-in");
            var paths = new List<string>();
            if (m.TryGetProperty("file", out JsonElement file))
            {
                paths.Add(file.GetString()!);
            }
            if (m.TryGetProperty("stages", out JsonElement stages))
            {
                paths.AddRange(stages.EnumerateArray().Select(s => s.GetString()!));
            }
            Assert.True(paths.Count > 0, $"{key}: no file");
            if (m.GetProperty("kind").GetString() == "kit")
            {
                // A kit (GROUND.md §9.4): the building's or doors' art, drawn by the building package by the kit's own names;
                // listed for its parts' gibs (GibTests).
                foreach (string res in paths)
                {
                    Assert.True(File.Exists(Local(game, res)), $"{key}: {res} is missing");
                    files++;
                }
                continue;
            }
            int lods = m.GetProperty("lods").GetInt32();
            bool convex = m.TryGetProperty("collision", out JsonElement col) && col.GetString() == "convex";
            foreach (string res in paths)
            {
                string path = Local(game, res);
                Assert.True(File.Exists(path), $"{key}: {res} is missing");
                bool nativeScene = path.EndsWith(".tscn", StringComparison.Ordinal);
                (List<string> nodes, List<string> materials) = nativeScene ? ReadScene(game, path, convex) : ReadGlb(path);
                List<string> parts = nodes.Where(n => n.EndsWith("_lod0", StringComparison.Ordinal)).Select(n => n[..^5]).ToList();
                Assert.True(parts.Count > 0, $"{key}: {res} has no _lod0 mesh");
                foreach (string part in parts)
                {
                    for (int lod = 1; lod < lods; lod++)
                    {
                        Assert.True(nodes.Contains($"{part}_lod{lod}"), $"{key}: {res} has no {part}_lod{lod}");
                    }
                }
                if (convex && !nativeScene)
                {
                    Assert.True(nodes.Any(n => n.EndsWith("-convcolonly", StringComparison.Ordinal)), $"{key}: {res} has no convex hull");
                }
                if (m.TryGetProperty("held", out JsonElement held))
                {
                    // The character package holds items by their palm sockets.
                    Assert.True(nodes.Contains("socket_grip_r") || nodes.Contains("socket_grip_l"), $"{key}: held, but no socket_grip_r or socket_grip_l");
                    if (held.TryGetProperty("offHand", out JsonElement off))
                    {
                        Assert.True(nodes.Contains("socket_" + off.GetString()), $"{key}: no socket_{off.GetString()} for the other hand");
                    }
                    if (held.TryGetProperty("grip", out JsonElement grip))
                    {
                        CheckGrip(key, grip);
                    }
                    if (held.TryGetProperty("supportHand", out JsonElement support))
                    {
                        CheckGrip(key + " support hand", support);
                        double[] pole = support.GetProperty("pole").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                        Assert.True(pole.Length == 3 && pole.All(double.IsFinite) && pole.Sum(v => v * v) > 0.1,
                            $"{key}: support hand needs a finite elbow direction");
                    }
                }
                // A gun's attachment sockets are nodes of its file.
                if (m.TryGetProperty("sockets", out JsonElement sockets))
                {
                    foreach (JsonProperty socket in sockets.EnumerateObject())
                    {
                        Assert.True(nodes.Contains(socket.Value.GetString()!), $"{key}: no {socket.Value.GetString()} node for the {socket.Name} slot");
                        mounts.Add(socket.Name);
                    }
                }
                if (!nativeScene) CheckImport(game, key, path, materials);
                files++;
            }
            if (m.TryGetProperty("slot", out JsonElement slot))
            {
                slots.Add(slot.GetString()!);
            }
            if (m.TryGetProperty("icon", out JsonElement icon) && !icon.TryGetProperty("key", out _))
            {
                foreach (int size in sizes)
                {
                    string res = iconPattern.Replace("{size}", size.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("{key}", key);
                    string path = Local(game, res);
                    Assert.True(File.Exists(path), $"{key}: icon {res} is missing");
                    (int w, int h, int colourType) = PngHeader(path);
                    Assert.True(w == size && h == size && colourType == 6, $"{key}: icon {res} is {w}x{h} (colour type {colourType}), not {size}x{size} RGBA");
                    iconFiles++;
                }
            }
        }
        foreach (string slot in slots)
        {
            Assert.True(mounts.Contains(slot), $"no gun has a socket for the {slot} slot");
        }
        foreach (string key in RequiredItems.Concat(RequiredWorld))
        {
            Assert.True(keys.Contains(key), $"{key}: no model in the catalogue");
        }
        // With the survival package's items.json beside it, every item must have a model too.
        string items = Path.Combine(game, "data", "ground", "items.json");
        if (File.Exists(items))
        {
            using JsonDocument itemDoc = JsonDocument.Parse(File.ReadAllText(items));
            foreach (JsonElement item in itemDoc.RootElement.GetProperty("items").EnumerateArray())
            {
                string key = item.GetProperty("key").GetString()!;
                Assert.True(keys.Contains(key), $"items.json: {key} has no model");
            }
        }
        Console.WriteLine($"      {keys.Count} models, {files} files, {iconFiles} icons");
    }

    private static string Local(string game, string res)
    {
        Assert.True(res.StartsWith("res://", StringComparison.Ordinal), $"{res}: not a res:// path");
        return Path.Combine(game, res["res://".Length..].Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Native packages embed the authored LOD meshes and supply their own collision/material wrappers.</summary>
    private static (List<string> Nodes, List<string> Materials) ReadScene(string game, string path, bool convex)
    {
        string scene = File.ReadAllText(path);
        Assert.True(scene.StartsWith("[gd_scene", StringComparison.Ordinal), $"{path}: not a Godot scene");
        var nodes = Regex.Matches(scene, "\\[node name=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.True(scene.Contains("type=\"ArrayMesh\"", StringComparison.Ordinal), $"{path}: has no embedded mesh");
        if (convex)
        {
            Assert.True(scene.Contains("type=\"StaticBody3D\"", StringComparison.Ordinal) &&
                scene.Contains("type=\"CollisionShape3D\"", StringComparison.Ordinal) &&
                Regex.IsMatch(scene, "type=\"(ConvexPolygon|Box|Cylinder)Shape3D\""), $"{path}: missing convex collision");
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void CheckReferences(string resource)
        {
            if (!seen.Add(resource)) return;
            foreach (Match match in Regex.Matches(File.ReadAllText(resource), "path=\"(res://[^\"]+)\""))
            {
                string dependency = Local(game, match.Groups[1].Value);
                Assert.True(File.Exists(dependency), $"{resource}: missing {dependency}");
                if (dependency.EndsWith(".tres", StringComparison.Ordinal) || dependency.EndsWith(".tscn", StringComparison.Ordinal))
                    CheckReferences(dependency);
            }
        }
        CheckReferences(path);
        Assert.True(seen.Count > 1, $"{path}: no external PBR material");
        return (nodes, new List<string>());
    }

    /// <summary>Node and material names from a GLB's JSON chunk.</summary>
    private static (List<string> Nodes, List<string> Materials) ReadGlb(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        Assert.True(data.Length > 20 && Encoding.ASCII.GetString(data, 0, 4) == "glTF", $"{path}: not a GLB");
        int length = BitConverter.ToInt32(data, 12);
        Assert.True(Encoding.ASCII.GetString(data, 16, 4) == "JSON", $"{path}: first chunk isn't JSON");
        using JsonDocument json = JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, length));
        JsonElement root = json.RootElement;
        Assert.True(!root.TryGetProperty("images", out JsonElement images) || images.GetArrayLength() == 0,
            $"{path}: carries images (textures belong to the shared materials)");
        var nodes = root.GetProperty("nodes").EnumerateArray()
            .Select(n => n.TryGetProperty("name", out JsonElement name) ? name.GetString()! : "").ToList();
        var materials = root.TryGetProperty("materials", out JsonElement mats)
            ? mats.EnumerateArray().Select(mm => mm.GetProperty("name").GetString()!).ToList()
            : new List<string>();
        return (nodes, materials);
    }

    /// <summary>
    /// A held item's own grip (GROUND.md §4.8): fingers and thumb are two directions far from parallel (the hand's frame
    /// is made from them), and what the hand closes round, how far it sits from the palm and where a swing turns are
    /// within a hand's size.
    /// </summary>
    private static void CheckGrip(string key, JsonElement grip)
    {
        static double[] Vec(JsonElement g, string name, double[] fallback) =>
            g.TryGetProperty(name, out JsonElement v) ? v.EnumerateArray().Select(e => e.GetDouble()).ToArray() : fallback;
        static double Length(double[] v) => Math.Sqrt(v.Sum(c => c * c));
        double[] fingers = Vec(grip, "fingers", new[] { 1.0, 0, 0 }), thumb = Vec(grip, "thumb", new[] { 0.0, 1, 0 });
        Assert.True(fingers.Length == 3 && thumb.Length == 3 && Length(fingers) > 0.5 && Length(thumb) > 0.5, $"{key}: grip fingers and thumb are directions");
        double cos = Math.Abs(fingers.Zip(thumb, (a, b) => a * b).Sum()) / (Length(fingers) * Length(thumb));
        Assert.True(cos < 0.5, $"{key}: grip fingers and thumb are nearly parallel");
        double radius = grip.TryGetProperty("radius", out JsonElement r) ? r.GetDouble() : 0.018;
        Assert.InRange(0, 0.07, radius, $"{key}: grip radius (0: an open hand)");
        if (grip.TryGetProperty("seat", out JsonElement seat))
        {
            Assert.InRange(0, 0.07, seat.GetDouble(), $"{key}: grip seat");
        }
        Assert.InRange(0, 0.4, Length(Vec(grip, "pivot", new double[3])), $"{key}: swing pivot");
        Assert.InRange(0, 0.3, Length(Vec(grip, "at", new double[3])), $"{key}: grip point");
    }

    /// <summary>The GLB's .import maps every material to a shared .tres that exists.</summary>
    private static void CheckImport(string game, string key, string glb, List<string> materials)
    {
        string import = glb + ".import";
        Assert.True(File.Exists(import), $"{key}: {Path.GetFileName(import)} is missing");
        string text = File.ReadAllText(import);
        foreach (string material in materials)
        {
            Assert.True(text.Contains($"\"{material}\": {{", StringComparison.Ordinal), $"{key}: material {material} isn't mapped in {Path.GetFileName(import)}");
        }
        foreach (Match match in Regex.Matches(text, "\"use_external/path\": \"(res://[^\"]+)\""))
        {
            Assert.True(File.Exists(Local(game, match.Groups[1].Value)), $"{key}: {match.Groups[1].Value} is missing");
        }
    }

    private static (int Width, int Height, int ColourType) PngHeader(string path)
    {
        byte[] head = new byte[26];
        using (FileStream s = File.OpenRead(path))
        {
            s.ReadExactly(head);
        }
        int Big(int at) => (head[at] << 24) | (head[at + 1] << 16) | (head[at + 2] << 8) | head[at + 3];
        return (Big(16), Big(20), head[25]);
    }
}
