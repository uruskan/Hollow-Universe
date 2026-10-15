using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class AttachmentTransferTests
{
    [Test]
    public static void ExplicitSocketsSwapDetachAndPreserveTheWholeWeapon()
    {
        GameContent c = ContentFixture.Content;
        var slots = new Container(ContainerKind.Main, 8);
        var storage = new Container(ContainerKind.Box, 4);
        ItemStack gun = ItemStack.Of(c.ItemByKey("rifle"), 1);
        gun.Condition = 123; gun.Loaded = 17; gun.LoadedAmmo = c.ItemByKey("rifle-ammo-hv").Id;
        slots.Set(0, gun);
        slots.Set(1, ItemStack.Of(c.ItemByKey("scope"), 1));
        slots.Set(2, ItemStack.Of(c.ItemByKey("holo-sight"), 1));
        Outcome Move(Container from, int i, int a, Container to, int j, int b) =>
            AttachmentTransfer.Move(c, from, i, a, to, j, b, from[i], to[j]);
        Assert.True(Move(slots, 1, -1, slots, 0, 0).Ok, "scope into sight socket");
        Assert.True(slots[1].IsEmpty && slots[0].Sight == c.ItemByKey("scope").Id, "one part moved");
        Assert.True(Move(slots, 2, -1, slots, 0, 0).Ok, "swap filled socket");
        Assert.Equal(c.ItemByKey("scope").Id, slots[2].Item, "old scope goes to exact source slot");
        Assert.True(Move(slots, 0, 0, storage, 3, -1).Ok, "detach into chosen storage slot");
        Assert.True(slots[0] == gun && storage[3].Item == c.ItemByKey("holo-sight").Id, "gun condition and magazine untouched");
        Assert.True(Move(storage, 3, -1, slots, 0, 0).Ok, "reattach from storage");
        storage.Set(0, ItemStack.Of(c.ItemByKey("rifle"), 1));
        Assert.True(Move(slots, 0, 0, storage, 0, 0).Ok, "socket to another weapon's socket");
        Assert.True(slots[0] == gun && storage[0].Sight == c.ItemByKey("holo-sight").Id, "both weapons retained");
    }

    [Test]
    public static void RejectionsAreAtomicAndCheckEveryAffectedStack()
    {
        GameContent c = ContentFixture.Content;
        var a = new Container(ContainerKind.Main, 4);
        var takeOnly = new Container(ContainerKind.Crate, 3);
        ItemStack rifle = ItemStack.Of(c.ItemByKey("rifle"), 1);
        rifle.Sight = c.ItemByKey("scope").Id;
        a.Set(0, rifle); a.Set(1, ItemStack.Of(c.ItemByKey("silencer"), 1));
        a.Set(2, ItemStack.Of(c.ItemByKey("wood"), 100));
        void Rejected(int from, int mod, Container to, int target, int targetMod, OutcomeCode code, bool deposit = true)
        {
            ItemStack beforeFrom = a[from], beforeTo = to[target];
            Outcome result = AttachmentTransfer.Move(c, a, from, mod, to, target, targetMod, beforeFrom, beforeTo, true, deposit);
            Assert.Equal(code, result.Code, "correct rejection");
            Assert.True(a[from] == beforeFrom && to[target] == beforeTo, "neither side changed");
        }
        Rejected(1, -1, a, 0, 0, OutcomeCode.NotAllowed); // muzzle part on sight
        Rejected(0, 0, a, 2, -1, OutcomeCode.NotUsable); // occupied by resources
        Rejected(0, 0, takeOnly, 0, -1, OutcomeCode.NotAllowed);
        Rejected(0, 0, a, 3, -1, OutcomeCode.NotAllowed, false); // take-only sleeper policy
        ItemStack observed = a[0];
        rifle.Loaded = 1; a.Set(0, rifle);
        Assert.Equal(OutcomeCode.Stale, AttachmentTransfer.Move(c, a, 0, 0, a, 3, -1, observed, a[3]).Code, "changed magazine invalidates captured weapon");
        Assert.True(a[0] == rifle && a[3].IsEmpty, "stale move is unchanged");
        a.Set(3, ItemStack.Of(c.ItemByKey("pistol"), 1));
        Rejected(0, 0, a, 3, 0, OutcomeCode.NotAllowed); // pistol does not take scope
        takeOnly.Set(0, ItemStack.Of(c.ItemByKey("holo-sight"), 1));
        ItemStack before = a[0], loot = takeOnly[0];
        Assert.Equal(OutcomeCode.NotAllowed, AttachmentTransfer.Move(c, takeOnly, 0, -1, a, 0, 0, loot, before, false, true).Code,
            "swap cannot deposit replaced part into a take-only source");
        Assert.True(a[0] == before && takeOnly[0] == loot, "take-only swap loses nothing");
    }

    [Test]
    public static void ServerStoragePermissionsAndExistingSavesKeepAttachments()
    {
        using var rig = new GameRig();
        var z = rig.Zone;
        Entity entity = rig.Add("Ada", 0, 0);
        Player player = rig.P(entity);
        ItemStack gun = ItemStack.Of(rig.C.ItemByKey("rifle"), 1);
        gun.Sight = rig.C.ItemByKey("scope").Id;
        player.Inventory.Belt.Set(1, gun);
        var at = z.GroundAt(1, 0);
        var box = new Deployable(z.NextId(), rig.C.Deployable("large-box")!, at, z.Frame.UpAt(at), 0, player.Id);
        z.Things().AddStatic(box);
        Assert.Equal(OutcomeCode.NotOpen, z.Guns().MoveAttachment(entity, ContainerRef.Belt, 1, 0, ContainerRef.Loot, 7, -1, gun, default).Code,
            "cannot name storage that is not open");
        player.Looting = box.Id;
        Assert.True(z.Guns().MoveAttachment(entity, ContainerRef.Belt, 1, 0, ContainerRef.Loot, 7, -1, gun, default).Ok, "explicit open storage destination");
        ItemStack stored = box.Container![7];
        Assert.Equal(rig.C.ItemByKey("scope").Id, stored.Item, "part is in slot7");
        Assert.True(z.Guns().MoveAttachment(entity, ContainerRef.Loot, 7, -1, ContainerRef.Belt, 1, 0, stored, player.Inventory.Belt[1]).Ok,
            "storage part can return to weapon");
        Player saved = WorldCodec.DecodePlayer(WorldCodec.EncodePlayer(player), player.Id, player.Name, rig.C);
        Assert.True(saved.Inventory.Belt[1] == gun, "existing player save format preserves assembled weapon");
        Assert.True(z.Guns().MoveAttachment(entity, ContainerRef.Belt, 1, 0, ContainerRef.Loot, 7, -1, gun, default).Ok, "detach again for storage save");
        WorldObject? restored = WorldCodec.DecodeThing(box.Id, StaticKind.Deployable,
            WorldCodec.EncodeThing(box, z.Frame.ToBody(box.Position), z.Tick), rig.C, z.Frame.ToLocal, z.Frame.UpAt, z.Tick);
        Assert.True(restored?.Container?[7] == stored, "existing world save preserves detached part in exact storage slot");
    }

    [Test]
    public static void AttachmentIntentsAreAcceptedOrRolledBackWithoutLoss()
    {
        using var rig = new LoopbackRig();
        GameClient client = rig.Join("Attacher");
        rig.WaitUntilPlaying();
        var me = client.Local;
        var answers = new List<IntentAnswer>();
        me.IntentAnswered += answers.Add;
        void Until(Func<bool> ready, string what)
        {
            var watch = Stopwatch.StartNew();
            while (!ready()) { Assert.True(watch.Elapsed.TotalSeconds < 8, what); rig.Run(.05, (_, _) => default); }
        }
        rig.Server.Inspect(server =>
        {
            server.Zone.Players().TryFindPlayer(client.EntityId, out _, out Player p);
            ItemStack gun = ItemStack.Of(client.Content.ItemByKey("rifle"), 1);
            gun.Loaded = 17; gun.LoadedAmmo = client.Content.ItemByKey("rifle-ammo").Id;
            p.Inventory.Belt.Set(4, gun);
            p.Inventory.Main.Set(20, ItemStack.Of(client.Content.ItemByKey("scope"), 1));
            p.Inventory.Main.Set(21, ItemStack.Of(client.Content.ItemByKey("holo-sight"), 1));
            return 0;
        });
        Until(() => me.Inventory.Belt[4].Loaded == 17 && !me.Inventory.Main[21].IsEmpty, "initial inventory");
        uint seq = me.MoveAttachment(ContainerRef.Main, 20, -1, ContainerRef.Belt, 4, 0);
        Assert.True(me.Inventory.Main[20].IsEmpty && me.Inventory.Belt[4].Sight == client.Content.ItemByKey("scope").Id, "optimistic attach");
        Until(() => answers.Any(a => a.Seq == seq), "attach response");
        Assert.Equal(OutcomeCode.Done, answers.Single(a => a.Seq == seq).Code, "server accepted explicit socket");
        seq = me.MoveAttachment(ContainerRef.Main, 21, -1, ContainerRef.Belt, 4, 0);
        Until(() => answers.Any(a => a.Seq == seq), "swap response");
        Assert.True(me.Inventory.Main[21].Item == client.Content.ItemByKey("scope").Id && me.Inventory.Belt[4].Sight == client.Content.ItemByKey("holo-sight").Id,
            "swap agrees over the wire");
        rig.Server.Inspect(server =>
        {
            server.Zone.Players().TryFindPlayer(client.EntityId, out _, out Player p);
            ItemStack changed = p.Inventory.Belt[4]; changed.Loaded = 16; p.Inventory.Belt.Set(4, changed); return 0;
        });
        seq = me.MoveAttachment(ContainerRef.Belt, 4, 0, ContainerRef.Main, 23, -1);
        Until(() => answers.Any(a => a.Seq == seq), "stale response");
        Assert.Equal(OutcomeCode.Stale, answers.Single(a => a.Seq == seq).Code, "changed weapon refused");
        Until(() => me.PendingCount == 0 && me.Inventory.Belt[4].Loaded == 16, "authoritative rollback");
        Assert.True(me.Inventory.Main[23].IsEmpty && me.Inventory.Belt[4].Sight == client.Content.ItemByKey("holo-sight").Id,
            "rollback restores exactly one attachment to the changed weapon");
    }
}
