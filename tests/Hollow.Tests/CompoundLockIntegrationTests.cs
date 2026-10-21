using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Hollow.Client;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class CompoundLockIntegrationTests
{
    [Test]
    public static void CompoundLocksAndWallSupportsSurviveTheThingCodec()
    {
        using var rig = new GameRig();
        Zone zone = rig.Zone;
        BuildingWorld building = zone.Building().World;
        const ulong owner = 17, friend = 19;
        Deployable Make(string key) => new(zone.NextId(), rig.C.Deployable(key)!, zone.Landing,
            zone.Frame.UpAt(zone.Landing), 0, new EntityId(owner));
        Deployable RoundTrip(Deployable source) => (Deployable)WorldCodec.DecodeThing(source.Id, source.Kind,
            WorldCodec.EncodeThing(source, zone.Frame.ToBody(source.Position), zone.Tick), rig.C,
            zone.Frame.ToLocal, zone.Frame.UpAt, zone.Tick)!;

        Deployable keyGate = Make("wood-compound-gate"), codeGate = Make("stone-compound-gate");
        Assert.True(building.AttachKeyLock(owner, keyGate, out KeyLock? key).Ok && building.SetLocked(owner, keyGate, true).Ok,
            "compound host uses the existing key lock rules");
        KeyLock originalKey = key!;
        Assert.True(RoundTrip(keyGate).Lock is KeyLock restoredKey && restoredKey.Id == originalKey.Id && restoredKey.Owner == owner &&
            restoredKey.Locked && restoredKey.KeyCode == originalKey.KeyCode, "key identity, owner, key number and locked state survive");

        Assert.True(building.AttachCodeLock(owner, codeGate, out CodeLock? code).Ok && building.SetCode(owner, codeGate, "2468").Ok &&
            building.EnterCode(friend, codeGate, "2468", zone.Building().BuildingNow).Ok, "the same code rules authorise a friend");
        CodeLock originalCode = code!;
        Deployable restored = RoundTrip(codeGate);
        Assert.True(restored.Lock is CodeLock back && back.Id == originalCode.Id && back.Owner == owner && back.Locked &&
            back.CodeHash == originalCode.CodeHash && back.IsAuthorised(owner) && back.IsAuthorised(friend), "code hash and authorisation survive");
        Assert.True(building.EnterCode(23, restored, "2468", zone.Building().BuildingNow).Ok, "restored hash still accepts the original code");

        Deployable ladder = Make("wooden-ladder");
        ladder.Support = keyGate.Ref;
        Assert.Equal(keyGate.Ref, RoundTrip(ladder).Support, "wall-mounted ladder keeps its deployable support after restart");

        // Version 8 ended after utility.Open; the repair timestamp and lock payload begin together in version 9.
        Deployable source = Make("windmill");
        source.LastDamagedAtUnixMs = zone.UnixMilliseconds;
        byte[] latest = WorldCodec.EncodeThing(source, zone.Frame.ToBody(source.Position), zone.Tick);
        byte[] previous = latest[..^9]; previous[0] = 8;
        var legacy = (Deployable)WorldCodec.DecodeThing(source.Id, source.Kind, previous, rig.C,
            zone.Frame.ToLocal, zone.Frame.UpAt, zone.Tick)!;
        Assert.True(legacy.LastDamagedAtUnixMs == long.MinValue && legacy.Lock == null,
            "version 8 utilities retain the never-damaged default and no lock without version 9 fields");
    }

    [Test]
    public static void CompoundLockChangesReachExistingClientsAndUseOrdinaryIntents()
    {
        using var rig = new LoopbackRig();
        GameClient owner = rig.Join("LockOwner"), guest = rig.Join("LockGuest");
        rig.WaitUntilPlaying();
        (EntityId id, Vector3 at) = rig.Server.Inspect(server =>
        {
            Zone zone = server.Zone;
            Deployable gate = zone.Players().SpawnDeployable(server.Content.Deployable("wood-compound-gate")!,
                zone.GroundAt(0, -4), 0, owner.EntityId);
            return (gate.Id, gate.Position);
        });
        rig.Teleport("LockOwner", at, south: 1.7f);
        rig.Teleport("LockGuest", at, east: 1, south: 1.7f);
        void Until(Func<bool> ready, string what)
        {
            var watch = Stopwatch.StartNew();
            while (!ready()) { Assert.True(watch.Elapsed.TotalSeconds < 8, "waiting for " + what); rig.Run(.05, (_, _) => default); }
        }
        RemoteThing View(GameClient client) => client.Survival().ThingOf(id)!;
        Until(() => owner.Survival().ThingOf(id) != null && guest.Survival().ThingOf(id) != null, "initial unlocked gate");
        ulong initial = View(owner).Capabilities.Bits;
        rig.Server.Inspect(server =>
        {
            var gate = (Deployable)server.Zone.Things().StaticOf(id)!;
            Assert.True(server.Zone.Building().World.AttachCodeLock(owner.EntityId.Value, gate, out _).Ok, "fit the ordinary code lock");
            return true;
        });
        Until(() => View(owner).Utility.Lock == LockKind.Code && View(guest).Utility.Lock == LockKind.Code &&
            View(owner).Capabilities.Bits != initial && View(guest).Capabilities.Bits != initial, "lock kind and newly added capability on existing clients");

        var answers = new Dictionary<uint, IntentAnswer>();
        owner.Local.IntentAnswered += answer => answers[answer.Seq] = answer;
        TargetRef target = new(TargetFamily.Thing, id.Value);
        void Ask(uint seq, string what)
        {
            Until(() => answers.ContainsKey(seq), what);
            Assert.Equal(OutcomeCode.Done, answers[seq].Code, what);
        }
        Ask(owner.Local.Interact(target, "set-code", code: "2468"), "set gate code through the existing interaction intent");
        Until(() => (View(guest).Flags & ViewerFlags.Locked) != 0 && (View(owner).Flags & ViewerFlags.HasCode) != 0, "viewer flags after setting code");
        Assert.True((View(guest).Flags & ViewerFlags.CanUse) == 0, "unlisted guest cannot open the locked gate");
        Ask(owner.Local.Interact(target, "code-unlock"), "unlock gate");
        Ask(owner.Local.Interact(target, "code-remove"), "remove lock");
        Until(() => View(owner).Utility.Lock == LockKind.None && View(guest).Utility.Lock == LockKind.None &&
            View(owner).Capabilities.Bits == initial && View(guest).Capabilities.Bits == initial, "removed capability on both clients");
    }
}
