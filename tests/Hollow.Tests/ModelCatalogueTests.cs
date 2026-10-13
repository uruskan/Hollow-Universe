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
        // G5: the raid package's keys.
        "bag-charge", "tin-grenade", "rocket-launcher", "rocket", "rocket-hv", "rifle-ammo-explosive",
    };

    // Models that replaced stand-ins: none of them may be marked as a stand-in again.
    private static readonly string[] NotStandIns = { "gunpowder", "metal-mask", "pistol-ammo-incendiary" };

    // World objects the gathering rules place (gather.json) and the ship.
    private static readonly string[] RequiredWorld =
    {
        "stone-node", "metal-node", "sulphur-node", "wood-pile", "stone-pile", "metal-pile", "sulphur-pile",
        "loot-sack", "dropped-bag", "tin-finch",
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
                (List<string> nodes, List<string> materials) = ReadGlb(path);
                List<string> parts = nodes.Where(n => n.EndsWith("_lod0", StringComparison.Ordinal)).Select(n => n[..^5]).ToList();
                Assert.True(parts.Count > 0, $"{key}: {res} has no _lod0 mesh");
                foreach (string part in parts)
                {
                    for (int lod = 1; lod < lods; lod++)
                    {
                        Assert.True(nodes.Contains($"{part}_lod{lod}"), $"{key}: {res} has no {part}_lod{lod}");
                    }
                }
                if (convex)
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
                CheckImport(game, key, path, materials);
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
