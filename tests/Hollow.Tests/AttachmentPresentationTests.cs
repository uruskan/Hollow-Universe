using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;

namespace Hollow.Tests;

public static class AttachmentPresentationTests
{
    [Test]
    public static void HeldOpticAndLightReplicationFollowAttachDetachHolsterAndDeath()
    {
        using var rig = new LoopbackRig();
        GameClient owner = rig.Join("OpticOwner"), observer = rig.Join("OpticObserver");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport OpticOwner -2 0");
        rig.Server.Execute("teleport OpticObserver 2 0");
        GameContent content = owner.Content;
        ItemDef rifle = content.ItemByKey("rifle"), lamp = content.ItemByKey("weapon-light"), scope = content.ItemByKey("scope");
        byte lampBit = (byte)(1 << lamp.Attachment!.Index), scopeBit = (byte)(1 << scope.Attachment!.Index);
        rig.Server.Inspect(server =>
        {
            server.Zone.Players().TryFindPlayer(owner.EntityId, out _, out Player player);
            player.Inventory.Belt.Set(4, ItemStack.Of(rifle, 1));
            player.Inventory.Main.Set(20, ItemStack.Of(lamp, 1));
            player.Inventory.Main.Set(21, ItemStack.Of(scope, 1));
            return 0;
        });
        void Until(Func<bool> ready, string what)
        {
            var watch = Stopwatch.StartNew();
            while (!ready()) { Assert.True(watch.Elapsed.TotalSeconds < 8, what); rig.Run(.05, (_, _) => default); }
        }
        RemoteEntity? Seen() => observer.Entities.FirstOrDefault(entity => entity.NetId == owner.NetId);
        Until(() => owner.Local.Inventory.Belt[4].Item == rifle.Id && !owner.Local.Inventory.Main[21].IsEmpty, "attachment inventory");
        owner.Local.HeldSlot = 4;
        Until(() => owner.Local.Gun.Ready && Seen()?.Held == rifle.Id, "held rifle visible remotely");
        owner.Local.MoveAttachment(ContainerRef.Main, 20, -1, ContainerRef.Belt, 4, (int)AttachmentSlot.Rail);
        Until(() => owner.Local.PendingCount == 0 && Seen()?.HeldMods == lampBit, "one authoritative fitted light reaches remote view");
        Assert.True(owner.Local.Gun.Mods.Light, "local light capability matches the replicated attachment");
        owner.Local.MoveAttachment(ContainerRef.Main, 21, -1, ContainerRef.Belt, 4, (int)AttachmentSlot.Sight);
        Until(() => owner.Local.PendingCount == 0 && Seen()?.HeldMods == (lampBit | scopeBit), "optic and light coexist remotely");
        Assert.Near(scope.Attachment.Zoom, owner.Local.Gun.Zoom, 1e-6, "active optic replaces native zoom");
        owner.Local.HeldSlot = -1;
        Until(() => Seen() is { Held: 0, HeldMods: 0 }, "holster clears the visible beam and optic authority");
        owner.Local.HeldSlot = 4;
        Until(() => owner.Local.Gun.Ready && Seen()?.HeldMods == (lampBit | scopeBit), "drawing restores fitted capabilities");
        owner.Local.MoveAttachment(ContainerRef.Belt, 4, (int)AttachmentSlot.Rail, ContainerRef.Main, 22, -1);
        Until(() => owner.Local.PendingCount == 0 && Seen()?.HeldMods == scopeBit, "detaching the lamp removes only its effect");
        Assert.True(!owner.Local.Gun.Mods.Light && owner.Local.Inventory.Main[22].Item == lamp.Id, "one lamp returned without a live beam");
        rig.Server.Inspect(server =>
        {
            server.Zone.Players().TryFindPlayer(owner.EntityId, out Entity entity, out Player player);
            server.Zone.Players().Die(entity, player, "optic lifecycle test", EntityId.None, "");
            return 0;
        });
        Until(() => owner.Local.IsDead && Seen() is { Held: 0, HeldMods: 0 }, "death clears the held presentation state");
    }

    [Test]
    public static void EveryOpticHasARealLensAndAnUnobstructedNativeEyeAtEveryLod()
    {
        string game = Path.Combine(Fixtures.Root, "game");
        using JsonDocument catalogue = JsonDocument.Parse(File.ReadAllText(Path.Combine(game, "data/ground/models.json")));
        int optics = 0;
        foreach (JsonElement model in catalogue.RootElement.GetProperty("models").EnumerateArray())
        {
            if (!model.TryGetProperty("optic", out JsonElement optic)) continue;
            optics++;
            string key = model.GetProperty("key").GetString()!;
            string lens = optic.GetProperty("lens").GetString()!;
            byte[] bytes = File.ReadAllBytes(Path.Combine(game, model.GetProperty("file").GetString()![6..]));
            int jsonLength = BitConverter.ToInt32(bytes, 12), binary = 20 + jsonLength + 8;
            using JsonDocument glb = JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, 20, jsonLength));
            JsonElement root = glb.RootElement, nodes = root.GetProperty("nodes"), meshes = root.GetProperty("meshes");
            var parents = Enumerable.Repeat(-1, nodes.GetArrayLength()).ToArray();
            for (int i = 0; i < nodes.GetArrayLength(); i++)
                if (nodes[i].TryGetProperty("children", out JsonElement children))
                    foreach (JsonElement child in children.EnumerateArray()) parents[child.GetInt32()] = i;
            Matrix4x4 World(int index)
            {
                JsonElement node = nodes[index];
                Matrix4x4 local;
                if (node.TryGetProperty("matrix", out JsonElement matrix))
                    local = new Matrix4x4(matrix[0].GetSingle(), matrix[1].GetSingle(), matrix[2].GetSingle(), matrix[3].GetSingle(),
                        matrix[4].GetSingle(), matrix[5].GetSingle(), matrix[6].GetSingle(), matrix[7].GetSingle(),
                        matrix[8].GetSingle(), matrix[9].GetSingle(), matrix[10].GetSingle(), matrix[11].GetSingle(),
                        matrix[12].GetSingle(), matrix[13].GetSingle(), matrix[14].GetSingle(), matrix[15].GetSingle());
                else
                {
                    Vector3 scale = node.TryGetProperty("scale", out JsonElement s) ? Vector(s) : Vector3.One;
                    Quaternion rotation = node.TryGetProperty("rotation", out JsonElement r) ? new(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()) : Quaternion.Identity;
                    Vector3 translation = node.TryGetProperty("translation", out JsonElement t) ? Vector(t) : Vector3.Zero;
                    local = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
                }
                return parents[index] < 0 ? local : local * World(parents[index]);
            }
            int eyeIndex = Enumerable.Range(0, nodes.GetArrayLength()).Single(i => nodes[i].GetProperty("name").GetString() == "socket_" + optic.GetProperty("eye").GetString());
            Matrix4x4 eye = World(eyeIndex);
            Vector3 origin = eye.Translation, direction = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, eye));
            for (int lod = 0; lod < model.GetProperty("lods").GetInt32(); lod++)
            {
                bool foundLens = false;
                for (int i = 0; i < nodes.GetArrayLength(); i++)
                {
                    string name = nodes[i].GetProperty("name").GetString()!;
                    if (!name.EndsWith("_lod" + lod, StringComparison.Ordinal) || !nodes[i].TryGetProperty("mesh", out JsonElement meshId)) continue;
                    if (name.StartsWith(lens + "_", StringComparison.Ordinal)) { foundLens = true; continue; }
                    Matrix4x4 transform = World(i);
                    foreach (JsonElement primitive in meshes[meshId.GetInt32()].GetProperty("primitives").EnumerateArray())
                    {
                        JsonElement positions = root.GetProperty("accessors")[primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                        JsonElement indices = root.GetProperty("accessors")[primitive.GetProperty("indices").GetInt32()];
                        int count = indices.GetProperty("count").GetInt32();
                        Vector3 Point(int index)
                        {
                            int offset = AccessorOffset(root, positions, index, 12, binary);
                            return Vector3.Transform(new Vector3(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4), BitConverter.ToSingle(bytes, offset + 8)), transform);
                        }
                        int Index(int index)
                        {
                            int component = indices.GetProperty("componentType").GetInt32();
                            int size = component == 5121 ? 1 : component == 5123 ? 2 : 4;
                            int offset = AccessorOffset(root, indices, index, size, binary);
                            return size == 1 ? bytes[offset] : size == 2 ? BitConverter.ToUInt16(bytes, offset) : checked((int)BitConverter.ToUInt32(bytes, offset));
                        }
                        for (int n = 0; n < count; n += 3)
                            Assert.True(!Hit(origin, direction, Point(Index(n)), Point(Index(n + 1)), Point(Index(n + 2))),
                                $"{key}/{name}: opaque geometry blocks the actual optical eye");
                    }
                }
                Assert.True(foundLens, $"{key}/LOD{lod}: separate optical aperture exists");
            }
        }
        Assert.True(optics >= 2, "both supplied optic families are checked");
    }

    private static int AccessorOffset(JsonElement root, JsonElement accessor, int index, int packedStride, int binary)
    {
        JsonElement view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        return binary + (view.TryGetProperty("byteOffset", out JsonElement offset) ? offset.GetInt32() : 0) +
            (accessor.TryGetProperty("byteOffset", out JsonElement local) ? local.GetInt32() : 0) +
            index * (view.TryGetProperty("byteStride", out JsonElement stride) ? stride.GetInt32() : packedStride);
    }

    private static Vector3 Vector(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());

    private static bool Hit(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, p = Vector3.Cross(direction, ac);
        float determinant = Vector3.Dot(ab, p);
        if (MathF.Abs(determinant) < 1e-10f) return false;
        float inverse = 1f / determinant;
        Vector3 t = origin - a;
        float u = Vector3.Dot(t, p) * inverse;
        if (u < -1e-5f || u > 1 + 1e-5f) return false;
        Vector3 q = Vector3.Cross(t, ab);
        float v = Vector3.Dot(direction, q) * inverse;
        float distance = Vector3.Dot(ac, q) * inverse;
        return v >= -1e-5f && u + v <= 1 + 1e-5f && distance > 1e-4f && distance < 2;
    }
}
