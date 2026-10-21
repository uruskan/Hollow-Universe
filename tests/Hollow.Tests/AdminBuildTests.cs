using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Players;

namespace Hollow.Tests;

/// <summary>Admin construction and crafting: privileges, prediction, resource accounting and save safety.</summary>
public static class AdminBuildTests
{
    private static GameContent C => ContentFixture.Content;

    private static void Until(LoopbackRig rig, Func<bool> done, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < 6, "timed out waiting for " + what);
            rig.Run(0.05, (_, _) => default);
        }
    }

    private static ConsoleAnswer Command(LoopbackRig rig, GameClient client, string command)
    {
        var answers = new List<ConsoleAnswer>();
        uint sequence = client.Console().SendConsole(command);
        void Receive(ConsoleAnswer answer)
        {
            if (answer.Seq == sequence) answers.Add(answer);
        }
        client.Console().ConsoleAnswered += Receive;
        try
        {
            Until(rig, () => answers.Count > 0, command);
            return answers[0];
        }
        finally
        {
            client.Console().ConsoleAnswered -= Receive;
        }
    }

    private static int Slot(GameClient client, string item) => Enumerable.Range(0, client.Local.Inventory.Main.Size)
        .First(i => client.Local.Inventory.Main[i].Item == client.Content.ItemByKey(item).Id);

    private static PlacementCandidate? Foundation(GameClient client, float east)
    {
        Vector3 feet = client.Character.Position;
        Vector3 eye = feet + client.Frame.UpAt(feet) * client.EyeHeight;
        Vector3 at = client.Frame.Beside(feet, east, 0, 0);
        Placeable placeable = client.Content.Placement.Of(client.Content.Building.Piece("foundation")!)!;
        return client.Placement().Plan(placeable, eye, Vector3.Normalize(at - eye), 0);
    }

    [Test]
    public static void AdminBuildCommandsRequireAdminAndReplicateRevocation()
    {
        using var rig = new LoopbackRig(admins: new[] { "Ada" });
        GameClient ada = rig.Join("Ada"), bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        Until(rig, () => ada.Console().IsAdmin && ada.Console().FreeBuild, "admin defaults");
        Assert.Equal(-1, ada.Console().BuildGrade, "BGrade starts off");
        Assert.True(!bo.Console().FreeBuild && bo.Console().BuildGrade == -1, "normal players start without construction privileges");

        foreach (string command in new[] { "freebuild on", "bgrade stone" })
        {
            Assert.Equal(ConsoleReplyKind.Error, Command(rig, bo, command).Kind, "a non-admin cannot use " + command);
        }
        Assert.Equal(ConsoleReplyKind.Error, Command(rig, ada, "freebuild on Bo").Kind, "freebuild cannot be assigned to a non-admin");
        Assert.Equal(ConsoleReplyKind.Error, Command(rig, ada, "bgrade stone Bo").Kind, "BGrade cannot be assigned to a non-admin");

        for (int grade = 0; grade < ada.Content.Building.Grades.Count; grade++)
        {
            Assert.Equal(ConsoleReplyKind.Success, Command(rig, ada, "bgrade " + ada.Content.Building.Grades[grade].Key).Kind, "select grade");
            Until(rig, () => ada.Console().BuildGrade == grade, "selected grade replicated");
        }
        int lastGrade = ada.Console().BuildGrade;
        Assert.Equal(ConsoleReplyKind.Error, Command(rig, ada, "bgrade 99").Kind, "invalid grade refused");
        Assert.Equal(lastGrade, ada.Console().BuildGrade, "invalid input leaves selected grade alone");
        Command(rig, ada, "bgrade off");
        Until(rig, () => ada.Console().BuildGrade == -1, "BGrade off");
        Command(rig, ada, "freebuild");
        Until(rig, () => !ada.Console().FreeBuild, "freebuild toggle off");

        Command(rig, ada, "admin Bo");
        Until(rig, () => bo.Console().IsAdmin && bo.Console().FreeBuild, "new admin gets freebuild");
        Command(rig, ada, "bgrade stone Bo");
        Until(rig, () => bo.Console().BuildGrade == 2, "targeted grade");
        Command(rig, ada, "unadmin Bo");
        Until(rig, () => !bo.Console().IsAdmin && !bo.Console().FreeBuild && bo.Console().BuildGrade == -1, "revocation clears construction privileges");
        Assert.True(rig.Server.Inspect(server =>
        {
            server.Zone.Players().TryFindPlayer(new EntityId(bo.EntityId.Value), out _, out Player p);
            return !p.FreeBuild && p.BuildGrade == -1;
        }), "revocation clears authoritative privileges too");
    }

    [Test]
    public static void AdminBuildDisconnectClearsSleeperAndReconnectUsesDefaults()
    {
        using var file = new TempDatabase();
        using var rig = new LoopbackRig(file.Path, admins: new[] { "Ada" });
        GameClient ada = rig.Join("Ada");
        rig.WaitUntilPlaying();
        Until(rig, () => ada.Console().IsAdmin && ada.Console().FreeBuild, "admin defaults before disconnect");
        Command(rig, ada, "bgrade armoured");
        Until(rig, () => ada.Console().BuildGrade == 4, "non-default grade before disconnect");
        EntityId id = ada.EntityId;
        Assert.True(rig.Server.Inspect(server => server.Zone.Players().TryFindPlayer(id, out _, out Player p) &&
            p.FreeBuild && p.BuildGrade == 4), "both construction privileges are active on the server");

        ada.Disconnect();
        rig.Clients.Remove(ada);
        ada.Dispose();
        Until(rig, () => rig.Server.Inspect(server => server.Zone.Players().TryFindPlayer(id, out _, out Player p) &&
            p.Sleeping && !p.FreeBuild && p.BuildGrade == -1), "disconnected sleeper loses both construction privileges");

        GameClient again = rig.Join("Ada");
        rig.WaitUntilPlaying();
        Until(rig, () => again.Console().IsAdmin && again.Console().FreeBuild && again.Console().BuildGrade == -1,
            "reconnected admin receives freebuild on and BGrade off");
        Assert.Equal(id, again.EntityId, "reconnect wakes the persistent character");
        Assert.True(rig.Server.Inspect(server => server.Zone.Players().TryFindPlayer(id, out _, out Player p) &&
            !p.Sleeping && p.FreeBuild && p.BuildGrade == -1), "authoritative state also resets to the admin defaults");
    }

    [Test]
    public static void AdminBuildGradePlacesFreeAndChargesChosenTierWhenDisabled()
    {
        using var rig = new LoopbackRig(admins: new[] { "Ada" });
        GameClient ada = rig.Join("Ada"), bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport Ada 0 0");
        rig.Server.Execute("teleport Bo -10 0");
        rig.Server.Execute("give Ada building-plan 1");
        rig.Server.Execute("give Bo building-plan 1");
        Until(rig, () => ada.Console().FreeBuild && ada.Local.Carries(BuildingRole.Plan) && bo.Local.Carries(BuildingRole.Plan), "plans and privileges");
        Command(rig, ada, "bgrade stone");
        Until(rig, () => ada.Console().BuildGrade == 2, "stone selected");
        rig.Run(0.5, (_, _) => default);

        var answers = new List<IntentAnswer>();
        ada.Local.IntentAnswered += answers.Add;
        PlacementCandidate? free = Foundation(ada, 3.5f);
        Assert.True(free is { Valid: true }, $"free stone ghost fits without materials: {free}");
        uint first = ada.Local.Place(free!, ContainerRef.Main, Slot(ada, "building-plan"));
        Until(rig, () => answers.Any(a => a.Seq == first) && ada.Building().World!.PieceCount == 1 && bo.Building().World!.PieceCount == 1, "free stone foundation on both clients");
        Assert.True(answers.Single(a => a.Seq == first).Done, "free foundation accepted");
        Piece placed = ada.Building().World!.AllPieces.Single();
        Assert.Equal(2, placed.Grade, "placed directly as stone");
        Assert.Near(placed.MaxHealth, placed.Health, 0.1, "stone starts at full health");
        Assert.Equal(2, bo.Building().World!.AllPieces.Single().Grade, "other player sees the chosen tier");
        Assert.Equal(0, ada.Local.Inventory.Count(C.ItemByKey("stone")), "no stone needed");

        Command(rig, ada, "freebuild off");
        Until(rig, () => !ada.Console().FreeBuild, "normal costs restored");
        PlacementCandidate? missing = Foundation(ada, -3.5f);
        Assert.True(missing is { Valid: false } && missing.Outcome.Code == OutcomeCode.NotEnough, $"chosen tier requires stone with freebuild off: {missing}");
        uint refused = ada.Local.Place(missing!, ContainerRef.Main, Slot(ada, "building-plan"));
        Until(rig, () => answers.Any(a => a.Seq == refused), "server refuses unpaid selected grade");
        Assert.Equal(OutcomeCode.NotEnough, answers.Single(a => a.Seq == refused).Code, "server also requires selected-tier resources");

        PieceDef foundation = ada.Content.Building.Piece("foundation")!;
        int price = ada.Content.Building.CostOf(foundation, 2);
        rig.Server.Execute($"give Ada stone {price + 17}");
        Until(rig, () => ada.Local.Inventory.Count(C.ItemByKey("stone")) == price + 17, "stone supplied");
        PlacementCandidate? paid = Foundation(ada, -3.5f);
        Assert.True(paid is { Valid: true }, $"paid selected-tier ghost fits: {paid}");
        uint second = ada.Local.Place(paid!, ContainerRef.Main, Slot(ada, "building-plan"));
        Assert.Equal(17, ada.Local.Inventory.Count(C.ItemByKey("stone")), "prediction pays the stone tier once");
        Until(rig, () => answers.Any(a => a.Seq == second) && ada.Building().World!.PieceCount == 2 && ada.Local.PendingCount == 0, "paid stone foundation");
        Assert.True(answers.Single(a => a.Seq == second).Done, "paid foundation accepted");
        Assert.Equal(17, ada.Local.Inventory.Count(C.ItemByKey("stone")), "server agrees with selected-tier payment");
        Assert.True(ada.Building().World!.AllPieces.All(p => p.Grade == 2), "both foundations stone");

        PlacementCandidate? ordinary = Foundation(bo, -3.5f);
        Assert.True(ordinary is { Valid: false } && ordinary.Outcome.Code == OutcomeCode.NotEnough, "ordinary player still needs twig resources");
    }

    [Test]
    public static void AdminBuildFreeCraftPredictionNeverRefundsUnspentIngredients()
    {
        using var rig = new LoopbackRig(admins: new[] { "Ada" });
        GameClient ada = rig.Join("Ada");
        rig.WaitUntilPlaying();
        Until(rig, () => ada.Console().FreeBuild, "free crafting enabled");
        Recipe recipe = ada.Content.Recipe(ada.Content.ItemByKey("metal-hatchet").Id)!;
        Assert.True(!ada.Local.Blueprints.Knows(recipe) && ada.Local.WorkbenchLevel < recipe.Workbench, "no blueprint or bench");
        Assert.Equal(OutcomeCode.Done, ada.Local.CanCraft(recipe), "UI allows admin crafting without requirements");
        ada.Local.Craft(recipe.Id);
        Until(rig, () => ada.Local.PendingCount == 0 && ada.Local.Crafting.Count == 1, "free job accepted and replicated");
        uint ticket = ada.Local.Crafting[0].Ticket;
        Command(rig, ada, "freebuild off");
        Until(rig, () => !ada.Console().FreeBuild, "freebuild disabled after job starts");
        Assert.Equal(OutcomeCode.UnknownRecipe, ada.Local.CanCraft(recipe), "normal blueprint restriction returns");
        ada.Local.CancelCraft(ticket);
        foreach (ItemAmount ingredient in recipe.Ingredients)
        {
            Assert.Equal(0, ada.Local.Inventory.Count(ingredient.Item), "predicted cancel does not invent " + ingredient.Item.Key);
        }
        Until(rig, () => ada.Local.PendingCount == 0 && ada.Local.Crafting.Count == 0, "free cancel accepted");
        foreach (ItemAmount ingredient in recipe.Ingredients)
        {
            Assert.Equal(0, ada.Local.Inventory.Count(ingredient.Item), "server cancel does not invent " + ingredient.Item.Key);
        }
    }

    [Test]
    public static void AdminBuildFreeCraftKeepsTimingLimitsAndNormalCraftRules()
    {
        var inventory = new PlayerInventory();
        var queue = new CraftQueue();
        var overflow = new List<ItemStack>();
        var finished = new List<ItemAmount>();
        Recipe recipe = C.Recipe(C.ItemByKey("metal-hatchet").Id)!;
        Assert.Equal(OutcomeCode.UnknownRecipe, queue.Start(C, inventory, recipe.Id, 1, 0, out _), "ordinary crafting still needs a blueprint");
        Assert.Equal(OutcomeCode.Done, queue.Start(C, inventory, recipe.Id, 2, 0, out uint ticket, free: true), "free craft needs no ingredients, blueprint or workbench");
        Assert.True(queue.Jobs.Single().Free, "free origin recorded per job");
        int ticks = queue.Jobs.Single().UnitTicks;
        Assert.Equal(recipe.UnitTicks, ticks, "normal crafting time without a workbench");
        for (int tick = 1; tick < ticks; tick++) queue.Advance(C, inventory, overflow, finished);
        Assert.Equal(0, inventory.Count(recipe.Output), "free does not complete instantly");
        queue.Advance(C, inventory, overflow, finished);
        Assert.Equal(recipe.Amount, inventory.Count(recipe.Output), "output arrives at its normal time");
        Assert.Equal(OutcomeCode.Done, queue.Cancel(C, inventory, ticket, overflow), "remaining free unit cancelled");
        Assert.True(overflow.Count == 0 && queue.IsEmpty, "no refund overflow");
        foreach (ItemAmount ingredient in recipe.Ingredients) Assert.Equal(0, inventory.Count(ingredient.Item), "free cancel has no refund");

        Assert.Equal(OutcomeCode.Invalid, queue.Start(C, inventory, recipe.Id, 0, 0, out _, free: true), "zero units remains invalid");
        Assert.Equal(OutcomeCode.Invalid, queue.Start(C, inventory, recipe.Id, CraftQueue.MaxCount + 1, 0, out _, free: true), "count limit remains");
        for (int i = 0; i < CraftQueue.MaxJobs; i++) queue.Start(C, inventory, recipe.Id, 1, 0, out _, free: true);
        Assert.Equal(OutcomeCode.QueueFull, queue.Start(C, inventory, recipe.Id, 1, 0, out _, free: true), "queue capacity remains");
        Assert.True(queue.TakeAll(C).Count == 0 && queue.IsEmpty, "death clears free jobs without inventing ingredients");

        Recipe ship = C.Recipe(C.ItemByKey("tin-finch").Id)!;
        Assert.Equal(OutcomeCode.Done, queue.Start(C, inventory, ship.Id, 1, 0, out _, free: true), "free ship needs no Vehicle Workbench");
        for (int tick = 0; tick < ship.UnitTicks; tick++) queue.Advance(C, inventory, overflow, finished);
        Assert.True(queue.IsEmpty && inventory.Count(ship.Output) == ship.Amount, "ship without a station becomes a placeable inventory item");
    }

    [Test]
    public static void AdminBuildPersistedFreeJobsCannotTurnIntoPaidRefunds()
    {
        var player = new Player(new EntityId(101), "Ada");
        Recipe paid = C.Recipe(C.ItemByKey("stone-hatchet").Id)!;
        Recipe free = C.Recipe(C.ItemByKey("metal-hatchet").Id)!;
        foreach (ItemAmount ingredient in paid.Ingredients) player.Inventory.Give(C, ingredient.Item, ingredient.Count);
        Assert.Equal(OutcomeCode.Done, player.Crafting.Start(C, player.Inventory, paid.Id, 1, 0, out uint paidTicket), "paid job takes its ingredients");
        Assert.Equal(OutcomeCode.Done, player.Crafting.Start(C, player.Inventory, free.Id, 2, 0, out uint freeTicket, free: true), "free job follows");
        Player restored = WorldCodec.DecodePlayer(WorldCodec.EncodePlayer(player), player.Id, player.Name, C);
        Assert.True(!restored.Crafting.Jobs[0].Free && restored.Crafting.Jobs[1].Free, "save preserves payment origin independently per job");
        var overflow = new List<ItemStack>();
        Assert.Equal(OutcomeCode.Done, restored.Crafting.Cancel(C, restored.Inventory, freeTicket, overflow), "restored free job cancels");
        Assert.True(!restored.Inventory.Stacks().Any() && overflow.Count == 0, "restart did not turn a free job into resources");
        Assert.Equal(OutcomeCode.Done, restored.Crafting.Cancel(C, restored.Inventory, paidTicket, overflow), "restored paid job cancels");
        foreach (ItemAmount ingredient in paid.Ingredients) Assert.Equal(ingredient.Count, restored.Inventory.Count(ingredient.Item), "normal paid refund survives a restart");

        restored = WorldCodec.DecodePlayer(WorldCodec.EncodePlayer(player), player.Id, player.Name, C);
        List<ItemStack> death = restored.Crafting.TakeAll(C);
        foreach (ItemAmount ingredient in paid.Ingredients)
        {
            Assert.Equal(ingredient.Count, death.Where(s => s.Item == ingredient.Item.Id).Sum(s => s.Count), "death returns only paid escrow");
        }
        Assert.Equal(paid.Ingredients.Sum(a => a.Count), death.Sum(s => s.Count), "no extra resources from free jobs after restart");

        // A ship already assigned to a real station retains its delivery destination as well as its payment origin.
        var shipOwner = new Player(new EntityId(102), "Bo");
        Recipe ship = C.Recipe(C.ItemByKey("tin-finch").Id)!;
        const ulong station = 0x1234_5678_9ABC;
        shipOwner.Crafting.Start(C, shipOwner.Inventory, ship.Id, 1, 0, out _, station: station, free: true);
        Player shipRestored = WorldCodec.DecodePlayer(WorldCodec.EncodePlayer(shipOwner), shipOwner.Id, shipOwner.Name, C);
        Assert.True(shipRestored.Crafting.Jobs.Single().Free && shipRestored.Crafting.Jobs.Single().Station == station,
            "free station-bound ship retains its delivery destination after restart");
    }

    [Test]
    public static void AdminBuildLegacyVersionSixJobsRemainPaid()
    {
        Recipe recipe = C.Recipe(C.ItemByKey("stone-hatchet").Id)!;
        // An independent version-6 fixture: one prepaid job, empty inventory, no optional world state.
        // This deliberately does not use the current encoder to construct the old save format.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)6); writer.Write(false);
            writer.Write(100f);
            for (int i = 0; i < 6; i++) writer.Write(0f);
            writer.Write(0L); writer.Write(""); writer.Write("");
            writer.Write(0f); writer.Write(false); writer.Write(""); writer.Write(""); writer.Write(0UL);
            foreach (int slots in new[] { PlayerInventory.MainSlots, PlayerInventory.BeltSlots, PlayerInventory.WearSlots })
            {
                writer.Write((ushort)slots);
                for (int i = 0; i < slots; i++) writer.Write((ushort)0);
            }
            writer.Write(2u); writer.Write((byte)1);
            writer.Write(1u); writer.Write(recipe.Id); writer.Write(2); writer.Write(recipe.UnitTicks / 2); writer.Write(recipe.UnitTicks);
            writer.Write((byte)0); writer.Write((ushort)0); writer.Write((byte)0); writer.Write(false);
            for (int i = 0; i < 3; i++) writer.Write(0f);
            writer.Write(3f);
        }
        Player restored = WorldCodec.DecodePlayer(stream.ToArray(), new EntityId(103), "Legacy", C);
        Assert.True(!restored.Crafting.Jobs.Single().Free && restored.Crafting.Jobs.Single().Station == 0, "legacy jobs remain paid without a station");
        Assert.Near(3, restored.Vitals.BreathUsed, 0.001, "fields after the legacy job remain aligned");
        Assert.True(!restored.FreeBuild && restored.BuildGrade == -1, "loading a save never grants admin privileges");
        restored.Crafting.Cancel(C, restored.Inventory, 1, new List<ItemStack>());
        foreach (ItemAmount ingredient in recipe.Ingredients)
        {
            Assert.Equal(ingredient.Count * 2, restored.Inventory.Count(ingredient.Item), "legacy prepaid ingredients are refunded");
        }
    }

    [Test]
    public static void AdminBuildEveryTierPlacesAtFullHealthAndHammerUsesFreeResources()
    {
        for (int grade = 0; grade < C.Building.Grades.Count; grade++)
        {
            using var rig = new GameRig();
            Entity admin = rig.Add("Ada", 0, 0);
            Player player = rig.P(admin);
            player.FreeBuild = true;
            player.BuildGrade = grade;
            rig.Give(admin, "building-plan", 1);
            Vector3 at = rig.Zone.GroundAt(3.5f, 0);
            rig.Clear(rig.Ch(admin).State.Position, at);
            Placeable placeable = C.Placement.Of(C.Building.Piece("foundation")!)!;
            Vector3 eye = rig.Zone.EyeOf(rig.Ch(admin).State);
            PlacementCandidate? ghost = rig.Zone.Placement().Aim(admin, placeable, Vector3.Normalize(at - eye));
            Assert.True(ghost is { Valid: true }, $"{C.Building.Grades[grade].Key} ghost valid without resources: {ghost}");
            Assert.Equal(OutcomeCode.Done, rig.Zone.Placement().Place(admin, ContainerRef.Main, 0, ghost!), "selected-tier foundation placed");
            Piece piece = rig.Zone.Building().World!.AllPieces.Single();
            Assert.Equal(grade, piece.Grade, "actual placed grade");
            Assert.Near(C.Building.Grades[grade].Health, piece.Health, 0.001, "new piece starts at full selected-tier health");
            if (grade != 0) continue;

            rig.Give(admin, "hammer", 1);
            Assert.Equal(OutcomeCode.Done, rig.Zone.Building().Hammer(admin, HammerAction.Upgrade, piece.Id, 2), "free hammer upgrade needs no stone");
            BuildingWorld building = rig.Zone.Building().World;
            double now = rig.Zone.Building().BuildingNow;
            DamageResult hit = building.Damage(piece, new DamageHit(Vector3.UnitX, Explosive: 100), now);
            Assert.True(hit.Loss > 0 && !hit.Destroyed, "piece damaged for repair");
            Assert.Equal(OutcomeCode.RecentlyDamaged, rig.Zone.Building().Hammer(admin, HammerAction.Repair, piece.Id, 0), "free repair keeps damage cooldown");
            rig.Zone.UnixMilliseconds += (long)((building.Data.RepairDelay + 1) * 1000);
            double damaged = piece.Health;
            player.FreeBuild = false;
            Assert.Equal(OutcomeCode.NotEnough, rig.Zone.Building().Hammer(admin, HammerAction.Repair, piece.Id, 0), "ordinary repair needs stone");
            player.FreeBuild = true;
            Assert.Equal(OutcomeCode.Done, rig.Zone.Building().Hammer(admin, HammerAction.Repair, piece.Id, 0), "free repair needs no stone");
            Assert.True(piece.Health > damaged && player.Inventory.Count(C.ItemByKey("stone")) == 0, "free repair restores health without resources");
        }
    }

    [Test]
    public static void AdminBuildFreeDeployablesStillRespectReachAndDoNotConsumeItems()
    {
        using var rig = new GameRig();
        Entity admin = rig.Add("Ada", 0, 0);
        Player p = rig.P(admin);
        p.FreeBuild = true;
        rig.Give(admin, "large-box", 1);
        Vector3 near = rig.Zone.GroundAt(2.5f, 0), far = rig.Zone.GroundAt(30, 0);
        rig.Clear(rig.Ch(admin).State.Position, far);
        Assert.Equal(OutcomeCode.OutOfReach, rig.Place(admin, 0, far, 0), "free placement still checks reach");
        Assert.Equal(OutcomeCode.Done, rig.Place(admin, 0, near, 0), "free deployable placed");
        Assert.Equal(1, p.Inventory.Count(C.ItemByKey("large-box")), "free deployable item retained");
        Assert.True(!rig.Place(admin, 0, near, 0).Ok, "free placement still refuses an occupied space");
        p.FreeBuild = false;
        Vector3 opposite = rig.Zone.GroundAt(-2.5f, 0);
        rig.Clear(rig.Ch(admin).State.Position, opposite);
        Assert.Equal(OutcomeCode.Done, rig.Place(admin, 0, opposite, 0), "ordinary deployable placed");
        Assert.Equal(0, p.Inventory.Count(C.ItemByKey("large-box")), "ordinary placement consumes its item");
    }
}
