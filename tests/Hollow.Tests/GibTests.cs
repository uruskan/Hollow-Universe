using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>
/// Gibs (GROUND.md §9.4): everything that can be destroyed has pieces to break into, declared beside its model in the
/// catalogue and built by art/gibs/build.py; the zone reports every destruction once, as the model that stood there and
/// the blow; and the fact reaches the clients near.
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

        // Every set: its file, its pieces, its materials.
        var sets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        int pieces = 0;
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
            (HashSet<string> parts, int count, long tris) = ReadSet(game, key, path, kit);
            sets[key] = parts;
            if (files.Add(path))
            {
                pieces += count;
                triangles += tris;
                bytes += new FileInfo(path).Length;
            }
        }

        // What can be destroyed, all of it.
        GameContent c = ContentFixture.Content;
        foreach (DeployableDef d in c.Deployables)
        {
            Assert.True(sets.ContainsKey(d.Key), $"deployable {d.Key} has no gibs");
        }
        foreach (PropDef p in c.World.Props.Where(p => p.Breakable))
        {
            Assert.True(sets.ContainsKey(p.Key), $"prop {p.Key} has no gibs");
        }
        Assert.True(sets.ContainsKey(c.Building.ClaimItem), "the claim device has no gibs");
        foreach (GradeDef grade in c.Building.Grades)
        {
            Assert.True(sets.TryGetValue(grade.Model, out HashSet<string>? parts), $"grade {grade.Key}: no gib set {grade.Model}");
            foreach (PieceDef piece in c.Building.Pieces)
            {
                Assert.True(parts!.Contains(piece.Key), $"{grade.Model}: no pieces for {piece.Key}");
            }
        }
        foreach (DoorDef door in c.Building.Doors)
        {
            Assert.True(sets.TryGetValue(door.Model, out HashSet<string>? parts) && parts.Contains(door.Key), $"door {door.Key}: no gibs in {door.Model}");
        }
        foreach (NaturalKind kind in c.NaturalKinds.Where(k => k.Class == NaturalClass.Node))
        {
            Assert.True(sets.ContainsKey(kind.Key), $"outcrop {kind.Key} has no gibs");
        }
        Fixtures.Report($"{sets.Count} gib sets in {files.Count} files: {pieces} pieces, {triangles:N0} triangles, {bytes / 1e6:0.0} MB");
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

    /// <summary>
    /// The zone reports each destruction once, as the model that stood there and the blow: a barrel broken by a blow; a
    /// stone hut whose foundation is blown up (the foundation and the claim device on it by the blast, then the walls, the
    /// door and the roof brought down); a wooden hut's claim device picked up (nothing broke) and a wall demolished.
    /// </summary>
    [Test]
    public static void BrokenThingsAreReportedOnce()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, -10);
        Player a = rig.P(ada);
        z.GameEvents.Clear();

        WorldProp barrel = z.SpawnProp(c.World.Prop("loot-barrel-1")!, z.GroundAt(4, 0), 0.6f);
        z.DamageDeployable(barrel, 1000, Blow.Along(DestroyCause.Melee, new Vector3(1, 0, 0), a.Id));
        ZoneEvent broken = Single(z, e => e.Other == barrel.Id);
        Assert.True(broken.Target == HitTarget.Deployable && broken.Model.Model == "loot-barrel-1" && broken.Model.Part == "" &&
            broken.Cause == DestroyCause.Melee && broken.Player == a.Id && Vector3.Distance(broken.Model.Position, barrel.Position) < 1e-4f,
            $"the barrel: {broken.Model} by {broken.Cause}");
        Quaternion expected = ModelPlacement.Upright(barrel.Up, barrel.Yaw);
        Assert.True(MathF.Abs(Quaternion.Dot(expected, broken.Model.Rotation)) > 0.9999f, "turned as it stood");

        TestBases.Hut stone = TestBases.Build(z, z.GroundAt(-8, 6), 0, 2, a.Id.Value)!;
        Door door = stone.Door!;
        ClaimDevice claim = stone.Claim!;
        int pieces = z.Building!.AllPieces.Count();
        z.DamageBuilding(stone.Foundation, -Vector3.UnitY, 100_000, projectile: false, Blow.Along(DestroyCause.Explosion, -Vector3.UnitY, a.Id));
        rig.Step(90);
        ZoneEvent foundation = Single(z, e => e.Other == stone.Foundation.Id);
        Assert.True(foundation.Model.Model == "building-stone" && foundation.Model.Part == "foundation" && foundation.Cause == DestroyCause.Explosion,
            $"the foundation: {foundation.Model.Model}/{foundation.Model.Part} by {foundation.Cause}");
        ZoneEvent device = Single(z, e => e.Other == claim.Id);
        Assert.True(device.Model.Model == c.Building.ClaimItem && device.Cause == DestroyCause.Explosion, $"the claim device: {device.Model.Model} by {device.Cause}");
        foreach (Piece wall in stone.Walls)
        {
            ZoneEvent fell = Single(z, e => e.Other == wall.Id);
            Assert.True(fell.Model.Model == "building-stone" && fell.Model.Part == wall.Def.Key && fell.Cause == DestroyCause.Collapse,
                $"{wall.Def.Key}: {fell.Model.Part} by {fell.Cause}");
        }
        ZoneEvent leaf = Single(z, e => e.Other == door.Id);
        Assert.True(leaf.Model.Model == door.Def.Model && leaf.Model.Part == door.Def.Key && leaf.Cause == DestroyCause.Collapse,
            $"the door: {leaf.Model.Model}/{leaf.Model.Part} by {leaf.Cause}");
        int reported = z.GameEvents.Count(e => e.Kind == ZoneEventKind.Destroyed);

        TestBases.Hut wood = TestBases.Build(z, z.GroundAt(10, 8), 0, 1, a.Id.Value)!;
        z.GameEvents.Clear();
        Assert.True(z.Building.PickUpClaim(a.Id.Value, wood.Claim!).Ok, "the claim device picked up");
        Piece demolished = wood.Walls[2];
        (Vector3 at, _) = demolished.ZoneTransform;
        Assert.True(z.Building.Demolish(a.Id.Value, demolished, at + Vector3.UnitY * 1.5f, z.BuildingNow).Ok, "a wall demolished");
        rig.Step(30);
        ZoneEvent hammered = Single(z, e => e.Other == demolished.Id);
        Assert.True(hammered.Model.Model == "building-wood" && hammered.Model.Part == demolished.Def.Key && hammered.Cause == DestroyCause.Demolish,
            $"the demolished wall: {hammered.Model.Model}/{hammered.Model.Part} by {hammered.Cause}");
        Assert.True(!z.GameEvents.Any(e => e.Kind == ZoneEventKind.Destroyed && e.Other == wood.Claim!.Id), "nothing broke when it was picked up");
        Fixtures.Report($"a barrel, then a stone hut of {pieces} pieces blown at its foundation: {reported} things broke apart; " +
            $"a wooden hut's claim device picked up (not broken), a wall demolished");
    }

    private static ZoneEvent Single(Zone z, Func<ZoneEvent, bool> which)
    {
        var found = z.GameEvents.Where(e => e.Kind == ZoneEventKind.Destroyed && which(e)).ToList();
        Assert.Equal(1, found.Count, "reported once");
        return found[0];
    }

    /// <summary>The fact over the network: a barrel broken on the server reaches another client as the model that stood there.</summary>
    [Test]
    public static void BreakingReachesTheClients()
    {
        using var rig = new LoopbackRig();
        GameContent c = ContentFixture.Content;
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport Ada 0 -6");
        rig.Server.Execute("teleport Bo 0 6");
        rig.Run(0.5, (_, _) => default);
        var seen = new List<DestructionEvent>();
        bo.Destroyed += seen.Add;
        (EntityId id, Vector3 at, Quaternion turn) = rig.Server.Inspect(sv =>
        {
            Assert.True(sv.Zone.TryFindPlayer(ada.EntityId, out _, out Player a), "Ada's character");
            WorldProp prop = sv.Zone.SpawnProp(c.World.Prop("loot-barrel-2")!, sv.Zone.GroundAt(3, 0), 1.1f);
            sv.Zone.DamageDeployable(prop, 1000, Blow.Along(DestroyCause.Bullet, new Vector3(0, 0, 1), a.Id));
            return (prop.Id, prop.Position, prop.Model.Rotation);
        });
        var watch = Stopwatch.StartNew();
        while (seen.Count == 0)
        {
            Assert.True(watch.Elapsed.TotalSeconds < 5, "the destruction reaches Bo");
            rig.Run(0.05, (_, _) => default);
        }
        DestructionEvent e = seen.Single();
        Assert.True(e.Id == id && e.Receiver == HitTarget.Deployable && e.Model.Model == "loot-barrel-2" && e.Cause == DestroyCause.Bullet,
            $"what broke: {e.Model.Model} ({e.Receiver}) by {e.Cause}");
        Assert.True(Vector3.Distance(e.Model.Position, at) < 0.001f && MathF.Abs(Quaternion.Dot(e.Model.Rotation, turn)) > 0.9999f &&
            Vector3.Distance(e.Blow, new Vector3(0, 0, 1)) < 0.01f, "where, how it stood and the blow");
        Fixtures.Report($"{e.Model.Model} broken on the server by a bullet reached Bo's client as the model that stood there, turned and hit as it was");
    }
}
