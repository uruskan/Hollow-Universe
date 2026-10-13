using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;

namespace Hollow.Tests;

/// <summary>
/// Gibs (GROUND.md §9.4): everything that can be destroyed has pieces to break into, declared beside its model in the
/// catalogue and built by art/gibs/build.py, found by what the damage pipeline's Destroyed event names it with (its key and
/// a piece's grade; DamageTests checks the event itself).
/// </summary>
public static class GibTests
{
    /// <summary>
    /// The data: every deployable, breakable prop, the claim device, every building piece in every grade, every door and
    /// every outcrop names a gib set; each set's file is there, carries no images, maps its materials to shared Godot
    /// materials, holds 2–16 pieces per part; every material and cause the runtime looks up is in gibs.json, and every
    /// effect and sound it names exists.
    /// </summary>
    [Test]
    public static void EverythingThatBreaksHasGibs()
    {
        string game = Path.Combine(Fixtures.Root, "game");
        string data = Path.Combine(game, "data", "ground");
        using JsonDocument models = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "models.json")));
        using JsonDocument gibs = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "gibs.json")));
        using JsonDocument fx = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "fx.json")));
        using JsonDocument sounds = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "sounds.json")));
        var effects = fx.RootElement.GetProperty("effects").EnumerateArray().Select(e => e.GetProperty("key").GetString()!).ToHashSet();
        var soundKeys = sounds.RootElement.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("key").GetString()!).ToHashSet();

        JsonElement materials = gibs.RootElement.GetProperty("materials");
        foreach (JsonProperty m in materials.EnumerateObject())
        {
            string dust = m.Value.GetProperty("dust").GetString()!;
            Assert.True(effects.Contains(dust), $"gibs.json: material {m.Name}'s dust {dust} isn't in fx.json");
            string sound = m.Value.GetProperty("sound").GetString()!;
            Assert.True(soundKeys.Contains(sound), $"gibs.json: material {m.Name}'s sound {sound} isn't in sounds.json");
            Assert.Equal(3, m.Value.GetProperty("colour").GetArrayLength(), $"gibs.json: material {m.Name}'s colour");
        }
        JsonElement causes = gibs.RootElement.GetProperty("causes");
        foreach (DestroyCause cause in Enum.GetValues<DestroyCause>())
        {
            string name = cause.ToString().ToLowerInvariant();
            Assert.True(causes.TryGetProperty(name, out JsonElement profile), $"gibs.json: no cause {name}");
            if (profile.TryGetProperty("sound", out JsonElement sound))
            {
                Assert.True(soundKeys.Contains(sound.GetString()!), $"gibs.json: cause {name}'s sound isn't in sounds.json");
            }
        }

        // Every set: its file, its pieces, its materials; indexed as the runtime finds them (GibLibrary.Find): a whole model
        // by its key at any grade, a kit's part by its key at the kit's grade (a grade's kit) or any (a door kit).
        var whole = new HashSet<string>(StringComparer.Ordinal);
        var parts = new HashSet<(string Key, int Grade)>();
        int sets = 0, pieces = 0;
        long triangles = 0, bytes = 0;
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement m in models.RootElement.GetProperty("models").EnumerateArray())
        {
            if (!m.TryGetProperty("gibs", out JsonElement g))
            {
                continue;
            }
            string key = m.GetProperty("key").GetString()!;
            string material = g.GetProperty("material").GetString()!;
            Assert.True(materials.TryGetProperty(material, out _), $"{key}: gib material {material} isn't in gibs.json");
            string res = g.GetProperty("file").GetString()!;
            string path = Path.Combine(game, res["res://".Length..].Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{key}: {res} is missing");
            bool kit = m.GetProperty("kind").GetString() == "kit";
            (HashSet<string> named, int count, long tris) = ReadSet(game, key, path, kit);
            int grade = g.TryGetProperty("grade", out JsonElement gg) ? gg.GetInt32() : -1;
            if (kit)
            {
                foreach (string part in named)
                {
                    Assert.True(parts.Add((part, grade)), $"{key}: {part} at grade {grade} is in two kits");
                }
            }
            else
            {
                whole.Add(key);
            }
            sets += kit ? named.Count : 1;
            if (files.Add(path))
            {
                pieces += count;
                triangles += tris;
                bytes += new FileInfo(path).Length;
            }
        }
        bool Breaks(string key, int grade) => whole.Contains(key) || parts.Contains((key, grade)) || parts.Contains((key, -1));

        // What can be destroyed (the Destroyed event's kinds, and an outcrop used up), all of it.
        GameContent c = ContentFixture.Content;
        foreach (DeployableDef d in c.Deployables)
        {
            Assert.True(Breaks(d.Key, 0), $"deployable {d.Key} has no gibs");
        }
        foreach (PropDef p in c.World.Props.Where(p => p.Breakable))
        {
            Assert.True(Breaks(p.Key, 0), $"prop {p.Key} has no gibs");
        }
        Assert.True(Breaks(c.Building.ClaimItem, 0), "the claim device has no gibs");
        foreach (GradeDef grade in c.Building.Grades)
        {
            foreach (PieceDef piece in c.Building.Pieces)
            {
                Assert.True(Breaks(piece.Key, grade.Index), $"{grade.Key} {piece.Key} has no gibs");
            }
        }
        foreach (DoorDef door in c.Building.Doors)
        {
            Assert.True(Breaks(door.Key, 0), $"door {door.Key} has no gibs");
        }
        foreach (NaturalKind kind in c.NaturalKinds.Where(k => k.Gather?.Look.Depleted?.Gibs == true))
        {
            Assert.True(Breaks(kind.Key, 0), $"{kind.Key} crumbles into gibs (gather.json) but has none");
        }
        Assert.True(c.NaturalKinds.Count(k => k.Class == NaturalClass.Node && k.Gather?.Look.Depleted?.Gibs == true) >= 3,
            "the outcrops' last remnants crumble");
        Fixtures.Report($"{sets} gib sets in {files.Count} files: {pieces} pieces, {triangles:N0} triangles, {bytes / 1e6:0.0} MB");
    }

    // A gib file: no images, its materials mapped, 2–16 pieces per part (a kit's parts are its top nodes, a whole model's
    // pieces are). Returns the parts, the pieces and their triangles.
    private static (HashSet<string> Parts, int Pieces, long Triangles) ReadSet(string game, string key, string path, bool kit)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 20 && Encoding.ASCII.GetString(bytes, 0, 4) == "glTF", $"{key}: not a GLB");
        int length = BitConverter.ToInt32(bytes, 12);
        using JsonDocument json = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, 20, length));
        JsonElement root = json.RootElement;
        Assert.True(!root.TryGetProperty("images", out JsonElement images) || images.GetArrayLength() == 0, $"{key}: its gibs carry images");
        JsonElement nodes = root.GetProperty("nodes");
        JsonElement meshes = root.GetProperty("meshes");
        JsonElement accessors = root.GetProperty("accessors");
        long Triangles(JsonElement node)
        {
            long t = 0;
            foreach (JsonElement prim in meshes[node.GetProperty("mesh").GetInt32()].GetProperty("primitives").EnumerateArray())
            {
                t += accessors[prim.GetProperty("indices").GetInt32()].GetProperty("count").GetInt64() / 3;
            }
            return t;
        }
        var parts = new HashSet<string>(StringComparer.Ordinal);
        int count = 0;
        long tris = 0;
        int[] top = root.GetProperty("scenes")[0].GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        if (kit)
        {
            foreach (int index in top)
            {
                JsonElement group = nodes[index];
                string name = group.GetProperty("name").GetString()!;
                bool isGroup = !group.TryGetProperty("mesh", out _) & group.TryGetProperty("children", out JsonElement children);
                Assert.True(isGroup, $"{key}: {name} isn't a part");
                int n = children.GetArrayLength();
                Assert.True(n is >= 2 and <= 16, $"{key}/{name}: {n} pieces");
                foreach (JsonElement child in children.EnumerateArray())
                {
                    tris += Triangles(nodes[child.GetInt32()]);
                }
                count += n;
                parts.Add(name);
            }
        }
        else
        {
            Assert.True(top.Length is >= 2 and <= 16, $"{key}: {top.Length} pieces");
            foreach (int index in top)
            {
                Assert.True(nodes[index].TryGetProperty("mesh", out _), $"{key}: a top node that isn't a piece");
                tris += Triangles(nodes[index]);
            }
            count = top.Length;
        }
        // The .import maps every material to a shared .tres that exists (the pieces are drawn as the model is).
        string import = path + ".import";
        Assert.True(File.Exists(import), $"{key}: {Path.GetFileName(import)} is missing");
        string text = File.ReadAllText(import);
        foreach (JsonElement mat in root.TryGetProperty("materials", out JsonElement mats) ? mats.EnumerateArray() : Enumerable.Empty<JsonElement>())
        {
            string name = mat.GetProperty("name").GetString()!;
            Assert.True(text.Contains($"\"{name}\": {{", StringComparison.Ordinal), $"{key}: material {name} isn't mapped");
        }
        foreach (Match match in Regex.Matches(text, "\"use_external/path\": \"(res://[^\"]+)\""))
        {
            string res = match.Groups[1].Value;
            Assert.True(File.Exists(Path.Combine(game, res["res://".Length..].Replace('/', Path.DirectorySeparatorChar))), $"{key}: {res} is missing");
        }
        return (parts, count, tris);
    }
}
