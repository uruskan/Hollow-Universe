using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class RenameTests
{
    [Test]
    public static void NamesNormalizeUnicodeWithoutInterpretingPaths()
    {
        Assert.True(Names.TryNormalize("  Cafe\u0301 東  ", out string name) && name == "Café 東", "trim and canonical Unicode");
        string astral = string.Concat(Enumerable.Repeat("🌙", 48));
        Assert.True(Names.TryNormalize(astral, out name) && name == astral, "48 Unicode scalars, 192 UTF8 bytes");
        Assert.True(!Names.TryNormalize(astral + "a", out _), "49 characters refused");
        foreach (string bad in new[] { "", "   ", "a\nb", "a\tb", "a\0b", "a\u202eb", "a\u2028b", "\ud800" })
            Assert.True(!Names.TryNormalize(bad, out _), "empty, control, format and malformed Unicode refused");
        Assert.True(Names.TryNormalize("../基地/bed", out name) && name == "../基地/bed", "path-looking text is an ordinary label");
    }

    [Test]
    public static void OnlyOwnersInReachRenameAndLabelsSurviveSaves()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        Entity ada = rig.Add("Ada", 20, 20), bo = rig.Add("Bo", 21, 20);
        Player owner = rig.P(ada);
        foreach (string kind in new[] { "sleeping-bag", "bed" })
        {
            Vector3 at = z.GroundAt(20, 21.5f);
            var bag = new Deployable(z.NextId(), rig.C.Deployable(kind)!, at, z.Frame.UpAt(at), 0, owner.Id);
            z.Things().AddStatic(bag);
            var options = new List<InteractOption>();
            Assert.True(z.Interaction().Options(ada, bag.Ref, options), "owner options");
            Assert.Equal(rig.C.Interactions.Index("rename"), options[InteractionRules.Primary(options)].Verb, "tap E names this respawn point");
            Assert.True(options.Any(o => o.Verb == rig.C.Interactions.Index("retrieve") && o.Enabled), "hold E includes real pick-up");
            uint mapBefore = owner.MapRevision, revision = bag.Revision;
            Assert.Equal(OutcomeCode.NotOwner, z.Interaction().Interact(bo, bag.Ref, "rename", text: "stolen").Code, "another player cannot rename");
            Assert.Equal(OutcomeCode.Invalid, z.Interaction().Interact(ada, bag.Ref, "rename", text: "bad\nname").Code, "server validates text");
            Assert.True(bag.Name == "" && bag.Revision == revision, "refusals leave target unchanged");
            Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, bag.Ref, "rename", text: "  Home  ").Code, "owner rename");
            Assert.True(bag.Name == "Home" && bag.Revision > revision && owner.MapRevision > mapBefore, "world and map update revisions");
            byte[] bytes = WorldCodec.EncodeThing(bag, z.Frame.ToBody(bag.Position), z.Tick);
            var restored = (Deployable)WorldCodec.DecodeThing(bag.Id, StaticKind.Deployable, bytes, rig.C, z.Frame.ToLocal, z.Frame.UpAt, z.Tick)!;
            Assert.Equal("Home", restored.Name, "name survives world save");
            // v5 has the identical prefix without v6's one-byte length + four ASCII label bytes.
            byte[] legacy = bytes[..^5]; legacy[0] = 5;
            var old = (Deployable)WorldCodec.DecodeThing(bag.Id, StaticKind.Deployable, legacy, rig.C, z.Frame.ToLocal, z.Frame.UpAt, z.Tick)!;
            Assert.Equal("", old.Name, "legacy v5 deployables still load unnamed");
            Raids.Stand(rig, ada, z.GroundAt(40, 40));
            Assert.Equal(OutcomeCode.OutOfReach, z.Interaction().Interact(ada, bag.Ref, "rename", text: "too far").Code, "submit rechecks reach");
            Raids.Stand(rig, ada, z.GroundAt(20, 20));
            int before = owner.Inventory.Count(bag.Def.Item);
            Assert.Equal(OutcomeCode.Done, z.Interaction().Interact(ada, bag.Ref, "retrieve").Code, "actual retrieve action");
            Assert.Equal(before + 1, owner.Inventory.Count(bag.Def.Item), "retrieved item is returned");
        }
        Assert.Equal((byte)6, WorldCodec.PlayerVersion, "breathing player-save version unchanged");
    }

    [Test]
    public static void RenameReplicatesToObserversMapAndRespawnList()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada"), bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        (EntityId id, Vector3 at) = rig.Server.Inspect(server =>
        {
            server.Zone.Players().TryFindPlayer(ada.EntityId, out _, out Player p);
            Vector3 position = server.Zone.GroundAt(0, -1.5f);
            var bag = new Deployable(server.Zone.NextId(), server.Content.Deployable("sleeping-bag")!, position,
                server.Zone.Frame.UpAt(position), 0, p.Id);
            server.Zone.Things().AddStatic(bag); p.MapRevision++;
            return (bag.Id, position);
        });
        rig.Teleport("Ada", at, south: 1.5f);
        rig.Teleport("Bo", at, east: 1, south: 1.5f);
        void Until(Func<bool> ready, string what)
        {
            var watch = Stopwatch.StartNew();
            while (!ready()) { Assert.True(watch.Elapsed.TotalSeconds < 8, "waiting for " + what); rig.Run(.05, (_, _) => default); }
        }
        Until(() => ada.Survival().ThingOf(id) != null && bo.Survival().ThingOf(id) != null, "initial world replication");
        var answers = new List<IntentAnswer>(); ada.Local.IntentAnswered += answers.Add;
        var target = new Hollow.Simulation.Targets.TargetRef(Hollow.Simulation.Targets.TargetFamily.Thing, id.Value);
        string name = string.Concat(Enumerable.Repeat("🌙", 48));
        uint seq = ada.Local.Interact(target, "rename", text: name);
        Until(() => answers.Any(a => a.Seq == seq), "rename acceptance");
        Assert.Equal(OutcomeCode.Done, answers.Single(a => a.Seq == seq).Code, "server accepted 192-byte Unicode label");
        Until(() => ada.Survival().ThingOf(id)?.Name == name && bo.Survival().ThingOf(id)?.Name == name &&
            ada.Local.RespawnPoints.Any(p => p.Id == id && p.Name == name), "ThingUpdate and live map label");
        GameClient late = rig.Join("Late"); rig.WaitUntilPlaying();
        Until(() => late.Survival().ThingOf(id)?.Name == name, "late ThingSpawn keeps full name");
        rig.Server.Execute("kill Ada");
        Until(() => ada.Local.IsDead && ada.Local.Life.Options.Any(b => b.Bag == id && b.Name == name), "respawn list label");
    }
}
