using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Hollow.Net.Commands;

/// <summary>Where a console command runs: in the game (the view and its settings) or on the server.</summary>
public enum CommandSide : byte
{
    Client,
    Server,
}

/// <summary>What an argument takes, for Tab completion: a list to complete from, or none (numbers, text).</summary>
public enum ArgKind : byte
{
    Text,
    Number,

    /// <summary>A count (optional counts are skipped by completion when the word isn't a number).</summary>
    Amount,
    Item,
    Player,
    LootTable,
    Spawnable,
    Grade,
    Blueprint,
    OnOff,
    Command,

    /// <summary>A ship's kind (ships.json's keys: tin-finch, iron-wake, motorboat).</summary>
    Ship,
}

/// <summary>
/// A command of the F1 console (GROUND.md §8.2): its name and Rust-style aliases, how it is used, what it does, who may
/// run it and where it runs. <see cref="Args"/> says what each word after the name takes, for completion.
/// </summary>
public sealed record ConsoleCommandDef(string Name, string[] Usage, string Help, bool Admin, CommandSide Side, ArgKind[] Args, string[] Aliases)
{
    /// <summary>Optional counts in <see cref="Args"/> (completion skips them for words that aren't numbers).</summary>
    public bool OptionalCount(int index) => index < Args.Length && Args[index] is ArgKind.Amount or ArgKind.Number;
}

/// <summary>
/// The F1 console's commands (GROUND.md §8.2), shared by the server (which runs most of them and checks who may), the
/// game (which runs the view's own and completes words) and the tests. Engine-free.
/// </summary>
public static class ConsoleCatalog
{
    /// <summary>Every command, in the order <c>help</c> lists them: everyone's first, then the admins'.</summary>
    public static IReadOnlyList<ConsoleCommandDef> All { get; } = new ConsoleCommandDef[]
    {
        // ---------------------------------------------------------------- everyone, in the game
        Def("help", ["help [command]"], "Lists the commands, or how one is used.", false, CommandSide.Client, [ArgKind.Command]),
        Def("find", ["find <text>"], "Finds commands, items, loot tables and things to spawn by name.", false, CommandSide.Client, [ArgKind.Text]),
        Def("clear", ["clear"], "Empties the console.", false, CommandSide.Client, [], "cls", "console.clear"),
        Def("quit", ["quit"], "Leaves the game (a hosted game saves and stops).", false, CommandSide.Client, [], "exit", "global.quit"),
        Def("fps", ["fps"], "Shows or hides the frame rate.", false, CommandSide.Client, [], "perf", "global.fps"),
        Def("netgraph", ["netgraph"], "Shows or hides the network graph (F3).", false, CommandSide.Client, [], "global.netgraph"),
        Def("fov", ["fov [degrees]"], "The field of view, 60 to 100 degrees (75 by default); kept on this computer.", false, CommandSide.Client,
            [ArgKind.Number], "graphics.fov"),
        Def("sensitivity", ["sensitivity [x]"], "Mouse sensitivity, 0.1 to 5 (1 by default); kept on this computer.", false, CommandSide.Client,
            [ArgKind.Number], "input.sensitivity"),
        Def("bind", ["bind <key> [command]", "bind F6 \"noclip\""], "Assign a console command to a physical key or MWHEELUP/MWHEELDOWN; saved locally. Without a command, shows the binding.", false, CommandSide.Client, [ArgKind.Text, ArgKind.Command]),
        Def("unbind", ["unbind <key>"], "Remove a saved console-command binding.", false, CommandSide.Client, [ArgKind.Text]),
        Def("binds", ["binds"], "List saved console-command bindings.", false, CommandSide.Client, []),
        Def("admire", ["admire"], "Inspect the held item without using it. Default key N; use bind <key> admire to assign another key and unbind N to remove the default.", false, CommandSide.Client, []),
        Def("noclipspeed", ["noclipspeed [1..200]"], "Preferred noclip speed in m/s, sprint multiplies by four. Only affects players granted noclip by the server; wheel adjusts it while flying.", false, CommandSide.Client, [ArgKind.Number], "noclip.speed"),

        // ---------------------------------------------------------------- everyone, on the server
        Def("kill", ["kill", "kill <player>  (admin)"],
            "You die where you stand: your body keeps what you carried and the respawn screen opens (a bag in a base under attack, say).",
            false, CommandSide.Server, [ArgKind.Player], "global.kill", "suicide"),
        Def("players", ["players"], "Who is on the server.", false, CommandSide.Server, [], "global.players", "global.playerlist"),
        Def("whereami", ["whereami"], "Where you are: latitude and longitude, the height, the map cell (\"C K12\"), the way from the landing site, the biome, the local time.",
            false, CommandSide.Server, [], "pos", "global.whereami"),

        // ---------------------------------------------------------------- admins
        Def("thirdperson", ["thirdperson [on|off]"], "See your own character with a free orbit camera: hold right mouse to turn, mouse wheel to zoom. Toggles when no option is supplied; lasts for this session.",
            true, CommandSide.Client, [ArgKind.OnOff], "tps", "selfcamera"),
        Def("give", ["give <item> [amount] [player]"], "Puts items into an inventory (item keys as in items.json, <item>.blueprint for its blueprint; the rest drops at the feet).",
            true, CommandSide.Server, [ArgKind.Item, ArgKind.Amount, ArgKind.Player], "inventory.give", "inventory.giveto", "giveto", "inventory.giveid"),
        Def("kit", ["kit <loot table> [player]"], "Gives a loot table's items (loot.json), e.g. supply-crate or gunfight-kit.",
            true, CommandSide.Server, [ArgKind.LootTable, ArgKind.Player], "inventory.kit"),
        Def("learn", ["learn <item|all> [player]"], "Learns an item's blueprint, or every one.", true, CommandSide.Server,
            [ArgKind.Blueprint, ArgKind.Player], "blueprint.learn", "unlockall"),
        Def("teleport", ["teleport <player>", "teleport <cell>", "teleport <x> <z>", "teleport <latitude> <longitude>", "teleport <player> <target player>",
                "teleport <player> <cell>", "teleport <player> <x> <z>"],
            "Moves you (or a player) to a player, or to a point of the world: the middle of a map cell (\"C K12\", as the map names it), metres east and " +
            "south of the landing site along the ground, or a latitude and longitude (three decimals or more, or N, S, E, W).",
            true, CommandSide.Server, [ArgKind.Player, ArgKind.Player], "tp", "global.teleport", "teleportpos", "global.teleportpos"),
        Def("tp2me", ["tp2me <player>"], "Brings a player to you.", true, CommandSide.Server, [ArgKind.Player],
            "teleport2me", "global.teleport2me", "bring"),
        Def("tpmarker", ["tpmarker [marker]"], "Moves you to your map marker (the newest, or its number on the map).", true, CommandSide.Server,
            [ArgKind.Number], "teleport2marker", "global.teleport2marker"),
        Def("spawn", ["spawn <kind> [count] [player]", "spawn raidbase [grade]"],
            "Puts things in front of you: an animal herd, the Tin Finch, a barrel or crate, any deployable, or a claimed raid base.",
            true, CommandSide.Server, [ArgKind.Spawnable, ArgKind.Amount, ArgKind.Player], "entity.spawn", "ent.spawn"),
        Def("heal", ["heal [player]"], "Full health, food and water, and no bleeding.", true, CommandSide.Server, [ArgKind.Player], "global.heal"),
        Def("god", ["god [on|off] [player]"], "No damage of any kind.", true, CommandSide.Server, [ArgKind.OnOff, ArgKind.Player],
            "global.god", "godmode"),
        Def("freebuild", ["freebuild [on|off] [player]"], "Toggle free building and crafting for an admin; on by default. No ingredients, blueprints or workbenches required.",
            true, CommandSide.Server, [ArgKind.OnOff, ArgKind.Player]),
        Def("bgrade", ["bgrade [off|twig|wood|stone|metal|armoured] [player]"], "Choose the grade of newly placed building pieces, or off for normal twig placement.",
            true, CommandSide.Server, [ArgKind.Grade, ArgKind.Player]),
        Def("noclip", ["noclip [on|off] [player]"],
            "Flies through everything: W A S D along the view, Space up, Ctrl down, Shift fast.", true, CommandSide.Server,
            [ArgKind.OnOff, ArgKind.Player], "global.noclip", "fly"),
        Def("time", ["time", "time <hour>", "time reset"],
            "The zone's time of day (local solar time where you stand; at the server's console, the landing site): this zone's sun only; universe time stays as it is.",
            true, CommandSide.Server, [ArgKind.Number], "env.time"),
        Def("timescale", ["timescale [x]"], "How fast the zone's day runs: 1 the day of play, 0 stops the sun, 60 a minute an hour.",
            true, CommandSide.Server, [ArgKind.Number], "env.timescale"),
        Def("status", ["status"], "Players, pings, tick time and bandwidth.", true, CommandSide.Server, [], "global.status"),
        Def("kick", ["kick <player> [reason]"], "Disconnects a player.", true, CommandSide.Server, [ArgKind.Player, ArgKind.Text], "global.kick"),
        Def("say", ["say <text>"], "A server line in everyone's chat.", true, CommandSide.Server, [ArgKind.Text], "global.say"),
        Def("save", ["save"], "Saves the world now.", true, CommandSide.Server, [], "server.save"),
        Def("ents", ["ents"], "Counts what is in the zone: players, animals, things, building, ships, explosives.", true, CommandSide.Server, [],
            "entities", "entity.count", "ent.count"),
        Def("clearinv", ["clearinv [player]"], "Empties an inventory: main, belt and clothing.", true, CommandSide.Server, [ArgKind.Player],
            "inventory.clear", "strip"),
        Def("ship", ["ship [player] [kind]"], "A ship beside a player, theirs and fuelled, its way in toward them: a Tin Finch, or the kind named (iron-wake).",
            true, CommandSide.Server, [ArgKind.Player, ArgKind.Ship]),
        Def("raidbase", ["raidbase [grade] [player]", "raidbase hut [grade] [player]"], "A claimed 3×3 clan base 26 m ahead, with honeycomb, stairs, shooting floor and roof peeks. Choose twig, wood, stone, metal or armoured. Use 'hut' for the small test fixture.",
            true, CommandSide.Server, [ArgKind.Grade, ArgKind.Player]),
        Def("airlock", ["airlock [triangle|room] [layoutFirst] [layoutSecond] [open|shut] [player]"],
            "Two doors that can close the walk between them (docs/DOORS.md §7), 9 m ahead: a triangle with two single doors (layouts 1 and 2 close it) or a square room with a double door and a single door (0 and 1). A layout is 0 to 3: bit 0 hinges on the right, bit 1 swings out.",
            true, CommandSide.Server, [ArgKind.Text, ArgKind.Number, ArgKind.Number, ArgKind.Text, ArgKind.Player]),
        Def("admin", ["admin <name>"], "Gives a player admin rights until the server stops (admins.txt keeps them).", true, CommandSide.Server,
            [ArgKind.Player], "ownerid", "moderatorid"),
        Def("unadmin", ["unadmin <name>"], "Takes admin rights away until the server stops.", true, CommandSide.Server, [ArgKind.Player],
            "removeowner", "removemoderator"),
        Def("world", ["world"], "The world's props, teams and economy.", true, CommandSide.Server, []),
        Def("animals", ["animals"], "How many animals of each kind, and what they cost.", true, CommandSide.Server, []),
    };

    private static ConsoleCommandDef Def(string name, string[] usage, string help, bool admin, CommandSide side, ArgKind[] args, params string[] aliases) =>
        new(name, usage, help, admin, side, args, aliases);

    private static readonly Dictionary<string, ConsoleCommandDef> ByName = Build();

    private static Dictionary<string, ConsoleCommandDef> Build()
    {
        var names = new Dictionary<string, ConsoleCommandDef>(StringComparer.OrdinalIgnoreCase);
        foreach (ConsoleCommandDef c in All)
        {
            names.Add(c.Name, c);
            foreach (string alias in c.Aliases)
            {
                names.Add(alias, c);
            }
        }
        return names;
    }

    /// <summary>A command by its name or an alias (any case), or null.</summary>
    public static ConsoleCommandDef? Find(string word) => ByName.GetValueOrDefault(word.Trim());

    /// <summary>Every name and alias.</summary>
    public static IEnumerable<string> Names => ByName.Keys;

    /// <summary>
    /// The words of a line: split at spaces, with "double quotes" holding a word with spaces in it (a player's name).
    /// </summary>
    public static List<string> Split(string line)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    any = false;
                }
            }
            else
            {
                word.Append(c);
                any = true;
            }
        }
        if (any)
        {
            words.Add(word.ToString());
        }
        return words;
    }

    /// <summary>
    /// A line as a command and its words: the command found by name or alias (null if none is), the name typed, and
    /// the words after it.
    /// </summary>
    public static (ConsoleCommandDef? Command, string Typed, IReadOnlyList<string> Args) Parse(string line)
    {
        List<string> words = Split(line);
        if (words.Count == 0)
        {
            return (null, "", Array.Empty<string>());
        }
        // A leading slash works too (the chat's habit: "/give rifle").
        string typed = words[0].TrimStart('/');
        return (Find(typed), typed, words.Skip(1).ToArray());
    }

    /// <summary>Whether a word is a number (a count, a coordinate, an hour).</summary>
    public static bool IsNumber(string word) =>
        double.TryParse(word, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

    /// <summary>Quotes a word with spaces in it, so <see cref="Split"/> keeps it whole.</summary>
    public static string Quote(string word) => word.Any(char.IsWhiteSpace) ? $"\"{word}\"" : word;
}
