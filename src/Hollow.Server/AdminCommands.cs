using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Hollow.Net.Commands;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Players;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>
/// Who runs a console command and where its answer goes (GROUND.md §8.2): a player through the F1 console (their own
/// rights, "me" the default target, the answer back to them), or the server's own console (all rights, no "me", the
/// answer in the log).
/// </summary>
public sealed class CommandContext
{
    /// <summary>The player who typed it; null for the server's console and the hosting game's own calls.</summary>
    public PlayerSession? Caller { get; init; }

    public bool Admin { get; init; }

    /// <summary>Where each line of the answer goes.</summary>
    public Action<ConsoleReplyKind, string> Reply { get; init; } = (_, _) => { };

    public void Out(string line) => Reply(ConsoleReplyKind.Output, line);

    public void Done(string line) => Reply(ConsoleReplyKind.Success, line);

    public void Error(string line) => Reply(ConsoleReplyKind.Error, line);
}

// The console's commands (GROUND.md §8.2) for the server's own console and every player's F1 console: who may run
// what, who a command means, and the commands that change inventories and health. The rest are the console module's
// (Modules/ConsoleModule.cs): rights, the console's messages, the zone's clock, moving, spawning, cheats, what is where.
public sealed partial class GameServer
{
    /// <summary>The commands (GROUND.md §8.2) as <c>help</c> at the server's console prints them.</summary>
    public static string CommandHelp { get; } = BuildHelp();

    private static string BuildHelp()
    {
        var text = new StringBuilder();
        foreach (ConsoleCommandDef c in ConsoleCatalog.All.Where(c => c.Side == CommandSide.Server))
        {
            for (int i = 0; i < c.Usage.Length; i++)
            {
                text.Append(CultureInfo.InvariantCulture, $"{c.Usage[i],-36}{(i == 0 ? c.Help : "")}\n");
            }
        }
        text.Append(CultureInfo.InvariantCulture, $"{"quit",-36}Saves everyone and stops the server.\n");
        text.Append("Here a command names its player: \"give rifle 1 Ada\" (or the older \"give Ada rifle 1\").");
        return text.ToString();
    }

    // The server's own console (standard input, and the hosting game's calls): every right, the answer in the log.
    private CommandContext ServerConsole => _serverConsole ??= new CommandContext { Admin = true, Reply = (_, line) => Log(line) };

    private CommandContext? _serverConsole;

    private void RunCommand(string line) => RunCommand(line, ServerConsole);

    /// <summary>Runs a console line for whoever typed it (GROUND.md §8.2), checking their rights.</summary>
    internal void RunCommand(string line, CommandContext ctx)
    {
        (ConsoleCommandDef? command, string typed, IReadOnlyList<string> args) = ConsoleCatalog.Parse(line);
        if (typed.Length == 0)
        {
            return;
        }
        if (ctx.Caller == null && typed.ToLowerInvariant() is "quit" or "stop" or "exit")
        {
            RequestStop();
            return;
        }
        if (command == null)
        {
            ctx.Error($"Unknown command '{typed}' (help lists them, find searches them).");
            return;
        }
        if (command.Side == CommandSide.Client)
        {
            if (ctx.Caller == null && command.Name == "help")
            {
                Log(CommandHelp);
            }
            else
            {
                ctx.Error($"'{command.Name}' runs in the game, not on the server.");
            }
            return;
        }
        if (command.Admin && !ctx.Admin)
        {
            ctx.Error($"'{command.Name}' needs admin rights on this server, which you don't have.");
            return;
        }
        switch (command.Name)
        {
            case "give":
                Give(ctx, args);
                break;
            case "kit":
                Kit(ctx, args);
                break;
            case "learn":
                Learn(ctx, args);
                break;
            case "teleport":
                this.Console().Teleport(ctx, args);
                break;
            case "tp2me":
                this.Console().BringToMe(ctx, args);
                break;
            case "tpmarker":
                this.Console().TeleportToMarker(ctx, args);
                break;
            case "spawn":
                this.Console().SpawnCommand(ctx, args);
                break;
            case "heal":
                Heal(ctx, args);
                break;
            case "kill":
                Kill(ctx, args);
                break;
            case "god":
                this.Console().Cheat(ctx, args, Simulation.Movement.MoveCheats.God, "God mode");
                break;
            case "noclip":
                this.Console().Cheat(ctx, args, Simulation.Movement.MoveCheats.Noclip, "Noclip");
                break;
            case "time":
                this.Console().Time(ctx, args);
                break;
            case "timescale":
                this.Console().TimeScale(ctx, args);
                break;
            case "players":
                this.Console().ListPlayers(ctx);
                break;
            case "whereami":
                this.Console().WhereAmI(ctx);
                break;
            case "status":
                foreach (string row in Status().Split('\n'))
                {
                    ctx.Out(row);
                }
                break;
            case "kick" when args.Count >= 1:
                if (Target(ctx, args[0]) is { } kicked)
                {
                    string reason = args.Count > 1 ? string.Join(' ', args.Skip(1)) : "Kicked by an admin.";
                    Kick(kicked, GoodbyeReason.Kicked, reason);
                    Log($"kicked {kicked.Name}: {reason}");
                    if (ctx.Caller != null)
                    {
                        ctx.Done($"Kicked {kicked.Name}: {reason}");
                    }
                }
                break;
            case "say" when args.Count >= 1:
                string text = string.Join(' ', args);
                Log($"[server] {text}");
                Broadcast(new ChatLine { Kind = ChatKind.Server, From = "Server", Text = text });
                if (ctx.Caller != null)
                {
                    ctx.Done($"Said: {text}");
                }
                break;
            case "save":
                SaveAll();
                ctx.Done("Saving the world.");
                break;
            case "ents":
                this.Console().CountEntities(ctx);
                break;
            case "clearinv":
                ClearInventory(ctx, args);
                break;
            case "ship":
                // ship [player] [kind] (IRON_WAKE.md §5.6): the kind is the last word when it names one (a Tin Finch without).
                Simulation.Ships.ShipDef? kind = args.Count > 0 ? Content.Ship(args[^1]) : null;
                if (TargetOrMe(ctx, kind != null ? args.Take(args.Count - 1).ToList() : args, 0) is { } pilot && Alive(ctx, pilot, out _))
                {
                    if (this.Ships().GiveShip(pilot, kind) is { } given)
                    {
                        ctx.Done($"A {given.Def.Name} for {pilot.Name} ({given.Id}), beside them, fuelled, its hatch open.");
                    }
                    else
                    {
                        ctx.Error($"No room for a ship beside {pilot.Name}.");
                    }
                }
                break;
            case "raidbase":
                this.Console().RaidBase(ctx, args);
                break;
            case "admin" when args.Count >= 1:
                this.Console().SetAdmin(ctx, string.Join(' ', args), true);
                break;
            case "unadmin" when args.Count >= 1:
                this.Console().SetAdmin(ctx, string.Join(' ', args), false);
                break;
            case "world":
                ctx.Out(this.World().WorldStatus());
                break;
            case "animals":
                ctx.Out(this.Animals().AnimalStatus());
                break;
            default:
                Usage(ctx, command);
                break;
        }
    }

    private static void Usage(CommandContext ctx, ConsoleCommandDef command) =>
        ctx.Error("Usage: " + string.Join("  |  ", command.Usage));

    // ---------------------------------------------------------------- players: who a command means

    /// <summary>
    /// A connected player by name: exactly (any case), else the one whose name starts with it, else the one whose name
    /// holds it. Says why not when none or several fit (unless <paramref name="quiet"/>).
    /// </summary>
    internal PlayerSession? Target(CommandContext ctx, string name, bool quiet = false)
    {
        PlayerSession[] all = _sessions.Values.ToArray();
        PlayerSession? exact = all.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }
        foreach (Func<PlayerSession, bool> fits in new Func<PlayerSession, bool>[]
                 {
                     s => s.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase),
                     s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase),
                 })
        {
            PlayerSession[] found = all.Where(fits).ToArray();
            if (found.Length == 1)
            {
                return found[0];
            }
            if (found.Length > 1)
            {
                if (!quiet)
                {
                    ctx.Error($"'{name}' could be {string.Join(", ", found.Select(s => s.Name))}.");
                }
                return null;
            }
        }
        if (!quiet)
        {
            ctx.Error($"No player called '{name}' is on.");
        }
        return null;
    }

    /// <summary>The player named from <paramref name="index"/> on, or the caller when no name is given there.</summary>
    internal PlayerSession? TargetOrMe(CommandContext ctx, IReadOnlyList<string> args, int index)
    {
        if (index < args.Count)
        {
            return Target(ctx, string.Join(' ', args.Skip(index)));
        }
        if (ctx.Caller == null)
        {
            ctx.Error("Name a player: the server's console has no 'me'.");
        }
        return ctx.Caller;
    }

    /// <summary>A player in the world and alive, and their survival state; says why not otherwise.</summary>
    internal bool Alive(CommandContext ctx, PlayerSession? session, out Player player)
    {
        player = null!;
        if (session == null)
        {
            return false;
        }
        if (!session.Playing || Zone.Players().PlayerOf(session.Entity) is not { } p)
        {
            ctx.Error($"{session.Name} isn't in the world yet.");
            return false;
        }
        if (p.Dead)
        {
            ctx.Error($"{(IsMe(ctx, session) ? "You are" : session.Name + " is")} dead: respawn first.");
            return false;
        }
        player = p;
        return true;
    }

    internal static bool IsMe(CommandContext ctx, PlayerSession session) => ctx.Caller == session;

    internal static string Whom(CommandContext ctx, PlayerSession session) => IsMe(ctx, session) ? "you" : session.Name;

    // ---------------------------------------------------------------- inventory

    // An item key, or "<item>.blueprint": a blueprint of that item's recipe (G7's blueprint item, its condition naming the
    // recipe; P3 needed one to show its LEARN).
    private Simulation.Items.ItemStack? Giveable(string key)
    {
        const string suffix = ".blueprint";
        if (Content.Item(key) is { } item)
        {
            return Simulation.Items.ItemStack.Of(item, 1);
        }
        return key.EndsWith(suffix, StringComparison.Ordinal) && Content.World.Blueprint != null && Content.RecipeByKey(key[..^suffix.Length]) is { } recipe
            ? Simulation.Economy.TechTrees.BlueprintOf(Content, recipe)
            : null;
    }

    // give <item> [amount] [player], or the older give <player> <item> [amount]; <item>.blueprint gives its blueprint.
    private void Give(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            ctx.Error("Usage: give <item> [amount] [player] (find <text> lists item keys; <item>.blueprint its blueprint)");
            return;
        }
        Simulation.Items.ItemStack? giving = Giveable(args[0]);
        PlayerSession? whom;
        int count = 1;
        if (giving == null && args.Count >= 2 && Giveable(args[1]) is { } older && Target(ctx, args[0], quiet: true) is { } named)
        {
            giving = older;
            whom = named;
            if (args.Count > 2 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            {
                count = n;
            }
        }
        else if (giving == null)
        {
            ctx.Error($"No item called '{args[0]}' (find {args[0]} lists those like it).");
            return;
        }
        else
        {
            int playerAt = 1;
            if (args.Count > 1 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            {
                count = n;
                playerAt = 2;
            }
            whom = TargetOrMe(ctx, args, playerAt);
        }
        if (!Alive(ctx, whom, out Player p))
        {
            return;
        }
        count = Math.Clamp(count, 1, 100_000);
        Simulation.Items.ItemStack stack = giving.Value;
        ItemDef item = Content.Item(stack.Item)!;
        int left = p.Inventory.Give(Content, stack.WithCount(count));
        int given = count - left;
        // What doesn't fit drops at their feet, in a bag as a finished craft's does (two dozen stacks at most).
        int dropped = Math.Min(left, 24 * Math.Max(1, item.StackSize));
        Zone.Spill().Drop(whom!.Entity, stack.WithCount(dropped));
        left -= dropped;
        if (given > 0)
        {
            // The pickup feed's "+N" for the receiver.
            Zone.Events.Add(new ItemsGained(Player: p.Id, Item: item.Id, Count: given, Reason: GainReason.Looted));
        }
        ctx.Done($"Gave {Whom(ctx, whom!)} {given + dropped} {item.Name}" + (dropped > 0 ? $" ({dropped} on the ground: no room)" : "") +
            (left > 0 ? $"; {left} more didn't fit" : "") + ".");
    }

    // kit <loot table> [player], or the older kit <player> <table>.
    private void Kit(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            ctx.Error("Usage: kit <loot table> [player]: " + string.Join(", ", Content.LootTables.Select(t => t.Key)));
            return;
        }
        LootTable? table = Content.Loot(args[0]);
        PlayerSession? whom;
        if (table == null && args.Count >= 2 && Content.Loot(args[1]) is { } older)
        {
            table = older;
            whom = Target(ctx, args[0]);
        }
        else if (table == null)
        {
            ctx.Error($"No loot table called '{args[0]}': " + string.Join(", ", Content.LootTables.Select(t => t.Key)));
            return;
        }
        else
        {
            whom = TargetOrMe(ctx, args, 1);
        }
        if (Alive(ctx, whom, out Player p))
        {
            this.Survival().GiveKit(p, table);
            ctx.Done($"Gave {Whom(ctx, whom!)} the {table.Key} kit.");
        }
    }

    // learn <item|all> [player], or the older learn <player> <item|all>.
    private void Learn(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            ctx.Error("Usage: learn <item|all> [player]");
            return;
        }
        bool Teaches(string key) => key == "all" || Content.Recipes.Any(r => r.Output.Key == key);
        string what = args[0];
        PlayerSession? whom;
        if (!Teaches(what) && args.Count >= 2 && Teaches(args[1]))
        {
            what = args[1];
            whom = Target(ctx, args[0]);
        }
        else if (!Teaches(what))
        {
            ctx.Error($"No recipe makes '{what}'.");
            return;
        }
        else
        {
            whom = TargetOrMe(ctx, args, 1);
        }
        if (whom == null || !whom.Playing || Zone.Players().PlayerOf(whom.Entity) is not { } p)
        {
            return;
        }
        int before = p.Blueprints.Learnt.Count;
        foreach (Recipe r in Content.Recipes.Where(r => !r.Known && (what == "all" || r.Output.Key == what)))
        {
            p.Blueprints.Learn(r);
        }
        int learnt = p.Blueprints.Learnt.Count - before;
        string who = IsMe(ctx, whom) ? "You" : whom.Name;
        if (learnt > 0)
        {
            ctx.Done($"{who} learnt {learnt} blueprint{(learnt == 1 ? "" : "s")}.");
        }
        else
        {
            ctx.Out($"{who} knew {(what == "all" ? "every blueprint" : "that one")} already.");
        }
    }

    private void ClearInventory(CommandContext ctx, IReadOnlyList<string> args)
    {
        PlayerSession? whom = TargetOrMe(ctx, args, 0);
        if (Alive(ctx, whom, out Player p))
        {
            p.Inventory.Clear();
            ctx.Done($"Emptied {(IsMe(ctx, whom!) ? "your" : whom!.Name + "'s")} inventory.");
        }
    }

    // ---------------------------------------------------------------- health

    private void Heal(CommandContext ctx, IReadOnlyList<string> args)
    {
        PlayerSession? whom = TargetOrMe(ctx, args, 0);
        if (!Alive(ctx, whom, out Player p))
        {
            return;
        }
        p.Vitals.Health = Content.Survival.MaxHealth;
        p.Vitals.Calories = Content.Survival.MaxCalories;
        p.Vitals.Hydration = Content.Survival.MaxHydration;
        p.Vitals.Bleeding = 0;
        p.Vitals.Burning = 0;
        p.VitalsRevision++;
        Zone.Entities.Get<Character>(whom!.Entity).State.Health = (byte)Content.Survival.MaxHealth;
        ctx.Done($"Healed {Whom(ctx, whom)}: full health, food and water.");
    }

    // kill: anyone, themselves (Rust's F1 kill: the respawn screen, a bag in a base under attack; GAME_DESIGN.md §5);
    // kill <player>: admins only.
    private void Kill(CommandContext ctx, IReadOnlyList<string> args)
    {
        PlayerSession? whom;
        if (args.Count > 0)
        {
            string name = string.Join(' ', args);
            if (!ctx.Admin && (ctx.Caller == null || Target(ctx, name, quiet: true) != ctx.Caller))
            {
                ctx.Error("Killing another player needs admin rights; 'kill' alone kills you.");
                return;
            }
            whom = Target(ctx, name);
        }
        else
        {
            whom = TargetOrMe(ctx, args, 0);
        }
        if (!Alive(ctx, whom, out Player p))
        {
            return;
        }
        bool me = IsMe(ctx, whom!);
        // Straight to death (not knocked down), whatever god mode says: the body keeps what was carried.
        Zone.Players().Die(whom!.Entity, p, me ? "Suicide" : "Killed by an admin", EntityId.None, "");
        Log($"{whom.Name} died: {(me ? "killed themselves (console)" : "killed by an admin")}");
        ctx.Done(me ? "You died. Choose where to respawn." : $"Killed {whom.Name}.");
    }

    // ---------------------------------------------------------------- the server

    /// <summary>What <c>status</c> prints.</summary>
    public string Status()
    {
        (double average, double max) = Ticks.Recent;
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture,
            $"tick {Zone.Tick} at {TickRate} Hz: {average:0.00} ms average, {max:0.00} ms most (last 10 s); " +
            $"{_sessions.Count} players, {Zone.Physics.TileCount} ground tiles");
        foreach (PlayerSession s in _sessions.Values)
        {
            NetStatistics stats = s.Peer.Statistics;
            string where = s.Playing ? Format(Zone.WorldOf(Zone.Entities.Get<Character>(s.Entity).State)) : "logging in";
            double onTime = s.InputsApplied + s.InputsMissing == 0 ? 100 : 100.0 * s.InputsApplied / (s.InputsApplied + s.InputsMissing);
            text.Append(CultureInfo.InvariantCulture,
                $"\n  {s.Name,-16} ping {s.Peer.RoundTripTime,4} ms  at {where}  sent {stats.BytesSent / 1024.0:0} KiB  " +
                $"received {stats.BytesReceived / 1024.0:0} KiB  inputs on time {onTime:0.0} %");
        }
        return text.ToString();
    }
}
