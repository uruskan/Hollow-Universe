using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Server;
using Hollow.Simulation;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>G7 World over the network and through a restart (GROUND.md §6.8): protocol 9, schema 6.</summary>
public static class WorldNetTests
{
    private static void RunUntil(LoopbackRig rig, Func<bool> done, string what, double seconds = 6)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < seconds, $"timed out waiting for {what}");
            rig.Run(0.05, (_, _) => default);
        }
    }

    private static int SlotOf(Container c, string key) =>
        Enumerable.Range(0, c.Size).First(i => c[i].Item == ContentFixture.Item(key).Id);

    private static T OnServer<T>(LoopbackRig rig, Func<GameServer, T> look) => rig.Server.Inspect(look);

    // Teleports a player next to the world prop of a kind nearest the landing site, on its side toward the landing site,
    // and waits until their client has them there.
    private static Vector3 NextTo(LoopbackRig rig, GameClient client, WorldPropKind kind, out EntityId prop)
    {
        (EntityId id, Vector3 at, Vector3 stand) = OnServer(rig, sv =>
        {
            WorldProp p = sv.Zone.World().Props.Where(x => x.Prop.Kind == kind).OrderBy(x => Vector3.Distance(x.Position, sv.Zone.Landing)).First();
            Vector3 toward = Vector3.Normalize(sv.Zone.Frame.Flat(p.Position, sv.Zone.Landing - p.Position));
            return (p.Id, p.Position, sv.Zone.Settle(p.Position + toward * 1.4f));
        });
        prop = id;
        rig.Teleport(client.Name, stand);
        RunUntil(rig, () => Vector3.Distance(client.Character.Position, stand) < 0.5f, "the teleport");
        return at;
    }

    // The ground so many metres east and south of the landing site, as a client finds it.
    private static Vector3 FromLanding(GameClient client, float east, float south)
    {
        GroundProbe ground = client.Placement().Ground!.Value;
        return ground.Offset(ground.On(client.Zone!.LandingDirection), east, south);
    }

    /// <summary>
    /// GROUND.md §6.8 over the network: the world's props reach clients as things of kind Prop, solid in their prediction (a crate opens take only
    /// and goes once emptied); a recycler started from the client runs with its state shown to the looter; a tech tree
    /// node unlocked shows at once and is confirmed; a team made, an invite sent and accepted, team chat to members
    /// alone, teammates' positions; a marker on the map.
    /// </summary>
    [Test]
    public static void WorldOverTheNetwork()
    {
        using var rig = new LoopbackRig();
        GameContent c = ContentFixture.Content;
        GameClient ada = rig.Join("Ada"), bo = rig.Join("Bo"), cy = rig.Join("Cy");
        rig.WaitUntilPlaying();
        LocalPlayer me = ada.Local;

        // Props in range arrive as things.
        NextTo(rig, ada, WorldPropKind.Crate, out EntityId crate);
        RunUntil(rig, () => ada.Survival().ThingOf(crate) is { Kind: StaticKind.Prop }, "the crate");
        RemoteThing seen = ada.Survival().ThingOf(crate)!;
        Fixtures.Report($"Ada sees {ada.World().Props.Count()} props near the crate: " +
            string.Join(", ", ada.World().Props.GroupBy(p => p.Prop(c)!.Name).Select(g => $"{g.Count()} {g.Key}")));
        Assert.Equal(WorldPropKind.Crate, seen.Prop(c)!.Kind, "a crate");
        Assert.True(ada.Physics!.HasProp(PropLayer.Deployable, crate.Value), "solid in her prediction too");
        me.Interact(new TargetRef(TargetFamily.Thing, crate.Value), "open");
        RunUntil(rig, () => me.LootTarget == crate && me.Loot != null, "the crate's contents");
        Assert.Equal(LootKind.Crate, me.LootKind, "as a crate");
        Assert.True(!me.LootTakesDeposits, "take only");
        for (int i = 0; i < me.Loot!.Size; i++)
        {
            if (!me.Loot[i].IsEmpty)
            {
                me.QuickMove(ContainerRef.Loot, i);
            }
        }
        RunUntil(rig, () => ada.Survival().ThingOf(crate) == null, "the emptied crate to go", 4);
        Assert.True(!ada.Physics!.HasProp(PropLayer.Deployable, crate.Value), "and gone from her prediction");
        Assert.True(me.Inventory.Count(c.ItemByKey("salvage")) >= 4, "its salvage taken");

        // The recycler.
        Vector3 machine = NextTo(rig, ada, WorldPropKind.Recycler, out EntityId recycler);
        // Whatever the crate gave her goes; then two gears.
        OnServer(rig, sv =>
        {
            sv.Zone.Players().TryFindPlayer(ada.EntityId, out _, out Player p);
            p.Inventory.Main.Clear();
            p.Inventory.Belt.Clear();
            return 0;
        });
        rig.Server.Execute("give Ada gears 2");
        RunUntil(rig, () => ada.Survival().ThingOf(recycler) != null && me.Inventory.Count(c.ItemByKey("gears")) == 2 &&
            me.Inventory.Count(c.ItemByKey("salvage")) == 0, "the recycler and the gears");
        me.Interact(new TargetRef(TargetFamily.Thing, recycler.Value), "open");
        RunUntil(rig, () => me.LootTarget == recycler && me.Loot != null, "the recycler open");
        me.MoveItem(ContainerRef.Main, SlotOf(me.Inventory.Main, "gears"), ContainerRef.Loot, 0);
        me.Interact(new TargetRef(TargetFamily.Thing, recycler.Value), "turn-on");
        RunUntil(rig, () => me.Machine is { Running: true }, "it to run");
        // The thing's lamp comes with the things' own updates, not always in the same packet as the machine's state.
        RunUntil(rig, () => ada.Survival().ThingOf(recycler)!.Lit, "others to see it running", 2);
        RunUntil(rig, () => me.Loot!.Slots.ToArray().Any(s => s.Item == c.ItemByKey("salvage").Id) && me.Machine is { Running: false }, "the salvage", 8);
        Assert.Equal(20, me.Loot!.Slots.ToArray().Where(s => s.Item == c.ItemByKey("salvage").Id).Sum(s => s.Count), "two gears' salvage");
        me.Close();

        // A tech tree node: at a workbench, for salvage; shown at once, confirmed after.
        rig.Server.Execute("give Ada salvage 100");
        rig.Server.Execute("give Ada workbench-1 1");
        RunUntil(rig, () => me.Inventory.Count(c.ItemByKey("workbench-1")) == 1 && me.Inventory.Count(c.ItemByKey("salvage")) >= 100, "the bench and salvage");
        Vector3 feet = ada.Character.Position;
        Vector3 away = Vector3.Normalize(ada.Frame.Flat(machine, FromLanding(ada, 0, 0) - machine));
        Simulation.Placement.PlacementCandidate? bench = ada.Placement().PlanAt(c.ItemByKey("workbench-1").Places!, ada.Placement().Ground!.Value.Settle(feet + away * 2.2f), 0);
        Assert.True(bench is { Valid: true }, $"the bench's ghost fits ({bench})");
        me.Place(bench!, ContainerRef.Main, SlotOf(me.Inventory.Main, "workbench-1"));
        RunUntil(rig, () => me.WorkbenchLevel == 1, "the bench in reach");
        int salvage = me.Inventory.Count(c.ItemByKey("salvage"));
        Recipe hatchet = c.Recipe(c.ItemByKey("metal-hatchet").Id)!;
        Assert.True(!me.Knows(hatchet) && me.CanUnlock(c.World.NodeOf(hatchet.Id)!) == OutcomeCode.Done, "unknown, and can be unlocked");
        var learnt = new List<Recipe>();
        var refused = new List<IntentAnswer>();
        me.BlueprintLearned += learnt.Add;
        me.IntentRejected += refused.Add;
        me.Unlock(ada.Interaction().BenchNear(), hatchet.Id);
        Assert.True(me.Knows(hatchet) && me.Inventory.Count(c.ItemByKey("salvage")) == salvage - 75, "shown at once");
        // (The client has its own copy of the game data: recipes compare by ID.)
        RunUntil(rig, () => (learnt.Any(r => r.Id == hatchet.Id) && me.PendingCount == 0) || refused.Count > 0, "the server's word");
        Assert.True(refused.Count == 0, "refused: " + string.Join(", ", refused));
        Assert.True(me.Knows(hatchet) && me.CanCraft(hatchet) != OutcomeCode.UnknownRecipe, "known, and crafts");

        // A team: made, an invite to a player close by, accepted.
        rig.Server.Execute("teleport Bo Ada");
        rig.Server.Execute("teleport Cy 60 60");
        me.CreateTeam();
        RunUntil(rig, () => me.Team != null && me.LeadsTeam, "the team");
        RunUntil(rig, () => bo.Entities.Any(e => e.Name == "Ada") && ada.Entities.Any(e => e.Name == "Bo"), "each other");
        var invites = new List<TeamInviteView>();
        bo.Local.TeamInviteReceived += invites.Add;
        RemoteEntity boSeen = ada.Entities.First(e => e.Name == "Bo");
        me.Interact(new TargetRef(TargetFamily.Character, boSeen.Id.Value), "invite");
        RunUntil(rig, () => invites.Count == 1 && bo.Local.TeamInvites.Count == 1, "Bo's invite");
        Assert.Equal("Ada", invites[0].From, "from Ada");
        bo.Local.AcceptInvite(invites[0].Team);
        RunUntil(rig, () => bo.Local.Team?.Members.Count == 2 && me.Team?.Members.Count == 2, "both in the team");
        Assert.True(ada.World().IsTeammate(bo.EntityId) && bo.World().IsTeammate(ada.EntityId) && !ada.World().IsTeammate(cy.EntityId), "teammates");
        Assert.True(me.Team!.Members.All(m => m.Online), "both online");
        RunUntil(rig, () => bo.Local.Teammates.Any(t => t.Id == ada.EntityId && Vector3.Distance(t.Position, ada.Character.Position) < 2), "Ada's position at Bo's");
        Assert.Equal("Ada", bo.Local.Teammates.Single().Name, "with her name");

        // Team chat: members only.
        var heard = new Dictionary<string, List<ChatLine>> { ["Ada"] = new(), ["Bo"] = new(), ["Cy"] = new() };
        foreach (GameClient client in new[] { ada, bo, cy })
        {
            client.ChatReceived += line => heard[client.Name].Add(line);
        }
        ada.World().SendTeamChat("meet at the recycler");
        RunUntil(rig, () => heard["Bo"].Any(l => l.Kind == ChatKind.Team), "the team line");
        rig.Run(0.3, (_, _) => default);
        Assert.True(heard["Ada"].Any(l => l.Kind == ChatKind.Team && l.Text == "meet at the recycler") && heard["Cy"].Count == 0, "to the team alone");

        // The map: a marker, then gone.
        Vector3 basePoint = FromLanding(ada, 200, -300);
        me.PlaceMarker(basePoint, 2, "base");
        RunUntil(rig, () => me.Markers.Count == 1, "the marker");
        Assert.True(me.Markers[0].Label == "base" && me.Markers[0].Colour == 2 && Vector3.Distance(me.Markers[0].Position, basePoint) < 0.05f, "as placed");
        me.RemoveMarker(0);
        RunUntil(rig, () => me.Markers.Count == 0, "the marker gone");
        string cell = ada.World().Map!.GridName(ada.Character.Position);
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(cell, "^[A-F] [A-Y][0-9]{1,2}$"), $"the map names a grid cell ({cell})");
    }

    /// <summary>
    /// GROUND.md §6.8 through a restart: blueprints learnt, markers and where the player died; a team; a barrel broken
    /// (it stays gone until the same time) and a crate partly looted (it keeps what was left), all as they were.
    /// </summary>
    [Test]
    public static void WorldSurvivesARestart()
    {
        using var file = new TempDatabase();
        GameContent c = ContentFixture.Content;
        ulong team;
        EntityId barrel = default, crate = default;
        long barrelBack;
        ItemStack[] left;
        using (var rig = new LoopbackRig(file.Path))
        {
            GameClient ada = rig.Join("Ada");
            GameClient bo = rig.Join("Bo");
            rig.WaitUntilPlaying();
            rig.Server.Execute("teleport Bo Ada");
            rig.Server.Execute("learn Ada metal-hatchet");
            rig.Server.Execute("learn Ada pistol");
            RunUntil(rig, () => ada.Local.Blueprints.Learnt.Count == 2, "two blueprints");
            ada.Local.PlaceMarker(FromLanding(ada, -150, 80), 4, "lake");
            ada.Local.CreateTeam();
            RunUntil(rig, () => ada.Local.LeadsTeam && ada.Local.Markers.Count == 1 && ada.Entities.Any(e => e.Name == "Bo"), "a team and a marker");
            ada.Local.Interact(new TargetRef(TargetFamily.Character, bo.EntityId.Value), "invite");
            RunUntil(rig, () => bo.Local.TeamInvites.Count == 1, "the invite");
            bo.Local.AcceptInvite(bo.Local.TeamInvites[0].Team);
            RunUntil(rig, () => ada.Local.Team?.Members.Count == 2, "Bo in");
            team = ada.Local.Team!.Id;
            (barrel, crate, barrelBack, left) = OnServer(rig, sv =>
            {
                Zone z = sv.Zone;
                WorldProp b = z.World().Props.First(p => p.Prop.Kind == WorldPropKind.Barrel);
                WorldProp k = z.World().Props.First(p => p.Prop.Kind == WorldPropKind.Crate && p.Storage!.Slots.ToArray().Count(s => !s.IsEmpty) >= 2);
                z.Things().DamageDeployable(b, 1000);
                int first = Enumerable.Range(0, k.Storage!.Size).First(i => !k.Storage[i].IsEmpty);
                k.Storage.Set(first, default);
                return (b.Id, k.Id, z.World().RespawnOf(b.Id.Value)!.Value, k.Storage.Slots.ToArray());
            });
            rig.Server.Execute("kill Bo");
            RunUntil(rig, () => bo.Local.IsDead, "Bo dead");
        }
        using (var rig = new LoopbackRig(file.Path))
        {
            (bool gone, long back, ItemStack[] kept, ulong teamId, int members) = OnServer(rig, sv => (sv.Zone.World().PropAt(barrel.Value) == null,
                sv.Zone.World().RespawnOf(barrel.Value) ?? 0, sv.Zone.World().PropAt(crate.Value)!.Storage!.Slots.ToArray(),
                sv.Zone.World().Teams.All.Single().Id, sv.Zone.World().Teams.All.Single().Members.Count));
            Assert.True(gone && back == barrelBack, "the barrel stays gone until the same time");
            Assert.True(kept.SequenceEqual(left), "the crate keeps what was left");
            Assert.True(teamId == team && members == 2, "the team");
            GameClient ada = rig.Join("Ada");
            GameClient bo = rig.Join("Bo");
            rig.WaitUntilPlaying();
            RunUntil(rig, () => ada.Local.Blueprints.Learnt.Count == 2 && ada.Local.Markers.Count == 1 && ada.Local.Team?.Members.Count == 2 &&
                bo.Local.DeathPoint != null, "the player's world state");
            Assert.True(ada.Local.Knows(c.Recipe(c.ItemByKey("pistol").Id)!) && ada.Local.Markers[0].Label == "lake", "blueprints and markers");
            Assert.True(bo.Local.Team?.Id == team, "Bo's team too");
            Fixtures.Report($"after the restart: barrel back in {(back - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000.0:0} s, crate with " +
                $"{kept.Count(s => !s.IsEmpty)} stacks, team of {members}");
        }
    }
}
