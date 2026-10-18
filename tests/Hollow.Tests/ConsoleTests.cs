using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Server;
using Hollow.Net.Commands;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>The F1 console (GROUND.md §8.2): its words, completion, rights, commands over the network, and cheats.</summary>
public static class ConsoleTests
{
    /// <summary>
    /// Lines split into words (quotes keep a name with spaces whole), and commands are found by name, by Rust's
    /// aliases and with the chat's slash; every name and alias belongs to one command.
    /// </summary>
    [Test]
    public static void ConsoleParsesLinesAndRustAliases()
    {
        Assert.Equal("give|rifle|2|Big Ben", string.Join("|", ConsoleCatalog.Split("  give rifle   2 \"Big Ben\" ")), "words");
        foreach ((string line, string command) in new[]
                 {
                     ("give rifle", "give"), ("inventory.give rifle 2", "give"), ("/give rifle", "give"), ("GIVE rifle", "give"),
                     ("global.teleport Ada", "teleport"), ("tp 10 20", "teleport"), ("env.time 21", "time"), ("global.god", "god"),
                     ("global.noclip", "noclip"), ("entity.spawn wolf 2", "spawn"), ("server.save", "save"), ("global.kill", "kill"),
                     ("teleport2me Bo", "tp2me"), ("ownerid Bo", "admin"), ("cls", "clear"),
                 })
        {
            (ConsoleCommandDef? found, _, _) = ConsoleCatalog.Parse(line);
            Assert.Equal(command, found?.Name, $"'{line}'");
        }
        Assert.True(ConsoleCatalog.Parse("frobnicate 3").Command == null, "an unknown command is none");
        Assert.Equal(2, ConsoleCatalog.Parse("spawn wolf 2").Args.Count, "the words after the command");
        int names = ConsoleCatalog.All.Sum(c => 1 + c.Aliases.Length);
        Assert.Equal(names, ConsoleCatalog.Names.Count(), "every name and alias belongs to one command");
        Assert.True(ConsoleCatalog.All.All(c => c.Usage.Length > 0 && c.Help.Length > 0), "every command has a usage and a help line");
    }

    /// <summary>
    /// Tab completes the word being typed from what its place takes: commands (and Rust's dotted aliases once a dot is
    /// typed), item keys, player names (quoted when they have a space; past an optional count), loot tables and kinds
    /// to spawn; several matches extend to what they share and are listed.
    /// </summary>
    [Test]
    public static void ConsoleCompletesCommandsItemsPlayersAndKinds()
    {
        GameContent content = GameContent.LoadDefault();
        CompletionSources sources = CompletionSources.From(content, new[] { "Ada", "Bo", "Big Ben" });
        void Expect(string typed, string line, params string[] candidates)
        {
            Completion c = ConsoleCompletion.Complete(typed, sources);
            Assert.Equal(line, c.Line, $"'{typed}' completes to");
            foreach (string candidate in candidates)
            {
                Assert.True(c.Candidates.Contains(candidate), $"'{typed}' offers {candidate}: {string.Join(", ", c.Candidates)}");
            }
        }
        Expect("gi", "give ");
        Expect("give tin-g", "give tin-grenade ");
        Expect("give rif", "give rifle", "rifle", "rifle-ammo", "rifle-frame");
        Expect("give rifle 2 A", "give rifle 2 Ada ");
        Expect("give rifle A", "give rifle Ada ");
        Expect("teleport B", "teleport B", "Bo", "Big Ben");
        Expect("teleport Bi", "teleport \"Big Ben\" ");
        Expect("spawn wol", "spawn wolf ");
        Expect("spawn wo", "spawn wo", "wolf", "workbench-1");
        Expect("spawn b", "spawn b", "barrel", "bear", "bed", "boar");
        Expect("spawn tin", "spawn tin-finch ");
        Expect("kit supply", "kit supply-crate ");
        Expect("env.ti", "env.time", "env.time", "env.timescale");
        Expect("global.noc", "global.noclip ");
        Expect("help noc", "help noclip", "noclip", "noclipspeed");
        Expect("noclip o", "noclip o", "on", "off");
        Expect("frobnicate x", "frobnicate x");
        Assert.True(sources.Spawnables.Contains("raidbase") && sources.Spawnables.Contains("research-table") && sources.Spawnables.Contains("crate"),
            "spawn offers the raid base, deployables and props: " + string.Join(", ", sources.Spawnables));
    }

    private static void RunUntil(LoopbackRig rig, Func<bool> done, string what, double seconds = 6)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < seconds, $"timed out waiting for {what}");
            rig.Run(0.05, (_, _) => default);
        }
    }

    // Runs a console line as a client and waits for its first answer line; returns every line answered.
    private static List<ConsoleAnswer> Run(LoopbackRig rig, GameClient client, string line)
    {
        var answers = new List<ConsoleAnswer>();
        uint seq = client.Console().SendConsole(line);
        Assert.True(seq != 0, $"'{line}' was sent");
        void Collect(ConsoleAnswer a)
        {
            if (a.Seq == seq)
            {
                answers.Add(a);
            }
        }
        client.Console().ConsoleAnswered += Collect;
        try
        {
            RunUntil(rig, () => answers.Count > 0, $"the answer to '{line}'");
            rig.Run(0.15, (_, _) => default);
        }
        finally
        {
            client.Console().ConsoleAnswered -= Collect;
        }
        Fixtures.Report($"{client.Name}> {line}: " + string.Join(" / ", answers.Select(a => $"[{a.Kind}] {a.Text}")));
        return answers;
    }

    // How far a point is east and south of the landing site, along the ground (the console's metres).
    private static Vector2 FromLanding(LoopbackRig rig, Vector3 point) => rig.Server.Inspect(sv =>
    {
        (float east, float south) = sv.Zone.OffsetOf(point);
        return new Vector2(east, south);
    });

    private static Vector3 ServerPosition(LoopbackRig rig, GameClient client) => rig.Server.Inspect(sv =>
        sv.Zone.Players().TryFindPlayer(new EntityId(client.EntityId.Value), out Entity e, out _) ? sv.Zone.Entities.Get<Character>(e).State.Position : default);

    /// <summary>
    /// GROUND.md §8.2: a player who isn't an admin is told so, clearly, and nothing happens; everyone may still list the
    /// players, ask where they are, and kill themselves (Rust's F1 kill: dead, with the respawn screen's choices).
    /// </summary>
    [Test]
    public static void ANonAdminIsRefused()
    {
        using var rig = new LoopbackRig(admins: new[] { "Ada" });
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        RunUntil(rig, () => bo.Console().ServerPlayers.Any(p => p.Name == "Bo"), "the server's state");
        Assert.True(!bo.Console().IsAdmin, "Bo isn't an admin");
        ItemDef rifle = bo.Content.ItemByKey("rifle");
        foreach (string line in new[] { "give rifle", "teleport 10 10", "spawn wolf 2", "noclip", "god", "time 21", "kill Ada", "inventory.give rifle" })
        {
            List<ConsoleAnswer> answers = Run(rig, bo, line);
            Assert.True(answers.Count == 1 && answers[0].Kind == ConsoleReplyKind.Error && answers[0].Text.Contains("admin", StringComparison.Ordinal),
                $"'{line}' refused for want of admin rights: {answers.FirstOrDefault().Text}");
        }
        Assert.Equal(0, bo.Local.Inventory.Count(rifle), "no rifle given");
        Assert.True(bo.Console().Cheats == MoveCheats.None, "no cheats");
        Assert.True(Run(rig, bo, "players").Any(a => a.Text.Contains("Bo", StringComparison.Ordinal)), "players lists who is on");
        Assert.True(Run(rig, bo, "whereami").Any(a => a.Text.Contains("map cell", StringComparison.Ordinal)), "whereami says where");
        Assert.True(Run(rig, bo, "frobnicate").Single().Kind == ConsoleReplyKind.Error, "an unknown command is an error");

        List<ConsoleAnswer> died = Run(rig, bo, "kill");
        Assert.True(died.Any(a => a.Kind == ConsoleReplyKind.Success), "kill answered");
        RunUntil(rig, () => bo.Local.IsDead, "Bo dead");
        Assert.True(bo.Local.Life.Options.Any(o => o.IsBeach), "the respawn screen's beach");
        Assert.Equal("Suicide", bo.Local.Life.Cause, "how he died");
    }

    /// <summary>
    /// GROUND.md §8.2 over the network, as an admin: give (to herself and another), teleport (to a point, to a player,
    /// by latitude and longitude, a player to her), spawn (a wolf pack in front of her, a box), kill another player,
    /// and the zone's time of day, which reaches every client.
    /// </summary>
    [Test]
    public static void AdminCommandsWorkOverTheNetwork()
    {
        using var rig = new LoopbackRig(admins: new[] { "ada" });
        GameClient ada = rig.Join("Ada"), bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        RunUntil(rig, () => ada.Console().IsAdmin && ada.Console().ServerPlayers.Count == 2, "Ada an admin, both on");
        GameContent c = ada.Content;

        Run(rig, ada, "give rifle");
        Run(rig, ada, "inventory.give rifle-ammo 64");
        Run(rig, ada, "give wood 300 Bo");
        RunUntil(rig, () => ada.Local.Inventory.Count(c.ItemByKey("rifle")) == 1 && ada.Local.Inventory.Count(c.ItemByKey("rifle-ammo")) == 64 &&
            bo.Local.Inventory.Count(c.ItemByKey("wood")) == 300, "the items in their inventories");

        Assert.True(Run(rig, ada, "teleport 30 -40").Any(a => a.Kind == ConsoleReplyKind.Success), "teleported");
        RunUntil(rig, () => Vector2.Distance(FromLanding(rig, ServerPosition(rig, ada)), new Vector2(30, -40)) < 0.5f, "Ada at 30, -40");
        RunUntil(rig, () => Vector2.Distance(FromLanding(rig, ada.Character.Position), new Vector2(30, -40)) < 1.5f, "her client there too");
        Run(rig, bo, "teleport 0 0"); // refused: Bo isn't an admin
        Run(rig, ada, "tp2me Bo");
        RunUntil(rig, () => Vector3.Distance(ServerPosition(rig, bo), ServerPosition(rig, ada)) < 3, "Bo brought to Ada");
        Run(rig, ada, "teleport 5 5");
        Run(rig, ada, "global.teleport Bo");
        RunUntil(rig, () => Vector3.Distance(ServerPosition(rig, bo), ServerPosition(rig, ada)) < 3, "Ada went to Bo");

        // Latitude and longitude: the landing site.
        ZoneSpec spec = rig.Server.Spec;
        Run(rig, ada, FormattableString.Invariant($"teleport {spec.LatitudeDegrees:0.000000} {spec.LongitudeDegrees:0.000000}"));
        RunUntil(rig, () => FromLanding(rig, ServerPosition(rig, ada)).Length() < 1, "Ada at the landing site");

        // A map cell, named as the map names it (docs/WHOLE_WORLD.md §11): the one beside the landing site's.
        int cells = WorldGrid.CellsPerFace(spec.Radius, c.World.MapCell);
        WorldGrid.Cell landing = WorldGrid.CellAt(spec.LandingDirection, cells);
        var next = landing with { Column = landing.Column == 0 ? 1 : landing.Column - 1 };
        Assert.True(Run(rig, ada, "teleport " + WorldGrid.Name(next)).Any(a => a.Kind == ConsoleReplyKind.Success), "teleported to a cell");
        RunUntil(rig, () => ServerPosition(rig, ada) is var at && WorldGrid.Name(new Hollow.Core.Vector3D(at.X, at.Y, at.Z), cells) == WorldGrid.Name(next),
            "Ada in that cell (the zone's frame is the world's)");
        Assert.True(Run(rig, ada, "whereami").Any(a => a.Text.Contains("map cell " + WorldGrid.Name(next), StringComparison.Ordinal)), "whereami names it");
        Run(rig, ada, FormattableString.Invariant($"teleport {spec.LatitudeDegrees:0.000000} {spec.LongitudeDegrees:0.000000}"));
        RunUntil(rig, () => FromLanding(rig, ServerPosition(rig, ada)).Length() < 1, "Ada back at the landing site, where the clock is read below");

        // Spawning: a wolf pack 22 m in front of her, a box at her feet.
        List<ConsoleAnswer> wolves = Run(rig, ada, "spawn wolf 2");
        Assert.True(wolves.Any(a => a.Kind == ConsoleReplyKind.Success), "wolves spawned");
        RunUntil(rig, () => ada.Animals().All.Count(a => a.SpeciesKey == "wolf") == 2, "two wolves in her view");
        float ahead = ada.Animals().All.Where(a => a.SpeciesKey == "wolf").Min(a => Vector3.Distance(a.Position, ada.Character.Position));
        Assert.InRange(10, 35, ahead, "the pack in front of her (m)");
        List<ConsoleAnswer> usage = Run(rig, ada, "spawn");
        Assert.True(usage.All(a => a.Kind == ConsoleReplyKind.Error && a.Text.Length <= 360) && string.Concat(usage.Select(a => a.Text)).Contains("raidbase", StringComparison.Ordinal),
            "a long answer comes whole, in lines a reply holds");
        Run(rig, ada, "spawn small-box");
        RunUntil(rig, () => ada.Survival().Things.Any(t => t.Deployable(c)?.Key == "small-box"), "a small box in her view");
        Run(rig, ada, "spawn crate");
        RunUntil(rig, () => ada.Survival().Things.Any(t => t.Kind == Simulation.World.StaticKind.Prop), "a crate in her view");

        // Kill another player.
        Assert.True(Run(rig, ada, "kill Bo").Any(a => a.Kind == ConsoleReplyKind.Success), "killed");
        RunUntil(rig, () => bo.Local.IsDead, "Bo dead");

        // The zone's time of day: nine in the evening at the landing site, on every client's clock too.
        Fixtures.Report($"the planet's own clock: {rig.Server.Inspect(sv => sv.Console().ZoneHours ?? -1):0.00} h at the landing site");
        Assert.True(Run(rig, ada, "time 21").Any(a => a.Kind == ConsoleReplyKind.Success), "time set");
        RunUntil(rig, () => bo.Console().Day is { Overridden: true }, "the new clock at Bo's client");
        double hours = rig.Server.Inspect(sv => sv.Console().ZoneHours ?? -1);
        Assert.Near(21, hours, 0.05, "local solar time at the landing site (h)");
        Surface.SkyEphemeris sky = spec.CreateSky()!;
        ZoneDay day = bo.Console().Day!.Value;
        sky.SetSpin(day.BaseTime, day.BaseTurns, day.Scale);
        double clientHours = sky.LocalSolarHours(spec.LandingDirection, sky.Sun.Index, Core.UniverseTime.Now);
        Assert.Near(21, clientHours, 0.1, "the client's sky says the same (h)");
        Run(rig, ada, "timescale 0");
        Run(rig, ada, "time reset");
        RunUntil(rig, () => bo.Console().Day is { Overridden: false }, "the planet's own clock again");
    }

    /// <summary>
    /// GROUND.md §8.2: god mode and noclip run the same on the server and in the prediction. A character noclipping
    /// through the ground at the landing site (along the view, sprinting, up and down) is predicted exactly, tick
    /// for tick; out of noclip in god mode it falls 40 m and lands with no damage, also exactly as the server has it.
    /// Over the network the toggles reach the client's prediction, which then flies with no correction, and god mode
    /// takes no damage on the server.
    /// </summary>
    [Test]
    public static void GodAndNoclipMatchThePrediction()
    {
        ZoneSpec spec = ZoneSpec.FirstWorld(Fixtures.Galaxy, Surface.FirstWorld.LandingLatitude, Surface.FirstWorld.LandingLongitude)!;
        using (var zone = new Zone(spec.CreateSurface(diagnostic: true), spec))
        {
            Entity entity = zone.AddCharacter(new EntityId(7), "server", zone.NewCharacter(zone.SpawnPoint(1)));
            zone.SetCheats(entity, MoveCheats.Noclip | MoveCheats.God);
            using var predictor = new Predictor(spec, spec.CreateSurface(diagnostic: true), null);
            // The client's ground first, as its loading screen waits for it (GROUND.md §4.3: the prediction holds without it).
            predictor.World.EnsureGround(zone.Entities.Get<Character>(entity).State.Position);
            predictor.Reset(0, zone.Entities.Get<Character>(entity).State, 0);
            float lowest = float.MaxValue;
            for (uint tick = 1; tick <= 420; tick++)
            {
                Vector3 up = zone.Frame.UpAt(zone.Entities.Get<Character>(entity).State.Position);
                MoveInput input = MovementCheck.Script((int)tick, up);
                // Down through the ground for a while, then up again.
                input.View = Facing.View(input.YawAt(up), tick < 120 ? -0.9f : tick < 240 ? 0.6f : input.PitchAt(up), up);
                InputCommand command = NetConvert.ToCommand(input);
                zone.Entities.Get<Character>(entity).Input = NetConvert.ToMoveInput(command);
                zone.Step();
                predictor.RecordInput(tick, command);
                predictor.Predict(tick);
                CharacterState server = zone.Entities.Get<Character>(entity).State;
                lowest = MathF.Min(lowest, zone.HeightAboveGround(server.Position));
                Assert.True(predictor.Latest.Equals(server), $"tick {tick}: prediction {predictor.Latest} is the server's {server}");
            }
            Assert.True(lowest < -3, $"noclip went through the ground ({lowest:0.0} m)");

            // Out of noclip 40 m up, in god mode: the fall lands with no damage, on both sides.
            ref Character c = ref zone.Entities.Get<Character>(entity);
            Vector3 ground = zone.Settle(c.State.Position);
            zone.Teleport(entity, ground + zone.Frame.UpAt(ground) * 40);
            zone.SetCheats(entity, MoveCheats.God);
            uint at = 420;
            predictor.Reconcile(at, zone.Entities.Get<Character>(entity).State);
            for (uint tick = at + 1; tick <= at + 150; tick++)
            {
                InputCommand idle = NetConvert.ToCommand(new MoveInput { View = Facing.View(0.3f, 0, zone.Frame.UpAt(zone.Entities.Get<Character>(entity).State.Position)) });
                zone.Entities.Get<Character>(entity).Input = NetConvert.ToMoveInput(idle);
                zone.Step();
                predictor.RecordInput(tick, idle);
                predictor.Predict(tick);
                Assert.True(predictor.Latest.Equals(zone.Entities.Get<Character>(entity).State), $"tick {tick}: the fall predicted exactly");
            }
            CharacterState landed = zone.Entities.Get<Character>(entity).State;
            Assert.True((landed.Flags & MoveFlags.OnGround) != 0, "landed");
            Assert.Equal((byte)100, landed.Health, "no fall damage in god mode");
        }

        // Over the network: the toggles reach the prediction, which flies with no correction; god mode takes no damage.
        using var rig = new LoopbackRig(admins: new[] { "Ada" });
        GameClient ada = rig.Join("Ada");
        rig.WaitUntilPlaying();
        RunUntil(rig, () => ada.Console().IsAdmin, "Ada an admin");
        Run(rig, ada, "noclip");
        Run(rig, ada, "global.god on");
        RunUntil(rig, () => ada.Console().Cheats == (MoveCheats.Noclip | MoveCheats.God), "both cheats in her prediction");
        rig.Run(0.5, (_, _) => default);
        long corrections = ada.Stats.Corrections, teleports = ada.Stats.Teleports;
        Vector3 start = ada.Character.Position;
        rig.Run(2.0, (_, t) => new InputFrame { Forward = 1, Pitch = 0.4f, Yaw = (float)t * 0.5f, Sprint = t > 1 });
        Vector3 flown = ada.Character.Position - start;
        Fixtures.Report($"flew {flown.Length():0.0} m, {ada.Stats.Corrections - corrections} corrections, worst error {ada.Stats.PredictionErrorWorst * 1000:0.0} mm");
        Assert.True(flown.Length() > 25, $"flew ({flown.Length():0.0} m)");
        Assert.True(Vector3.Dot(flown, ada.Frame.UpAt(start)) > 5, "up along the view");
        Assert.Equal(corrections, ada.Stats.Corrections, "no corrections while flying");
        Assert.Equal(teleports, ada.Stats.Teleports, "no snaps while flying");
        float taken = rig.Server.Inspect(sv =>
        {
            sv.Zone.Players().TryFindPlayer(new EntityId(ada.EntityId.Value), out Entity e, out Player p);
            return sv.Zone.Players().Hurt(e, p, 500, "Test", EntityId.None, "");
        });
        Assert.Equal(0f, taken, "god mode takes no damage");
        Run(rig, ada, "noclip off");
        RunUntil(rig, () => ada.Console().Cheats == MoveCheats.God, "noclip off in her prediction");
    }
}
