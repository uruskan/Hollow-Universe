using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using LiteNetLib;

namespace Hollow.Server;

public sealed partial class GameServer
{
    /// <summary>The admin commands (GROUND.md §4.2): what <c>help</c> prints.</summary>
    public const string CommandHelp =
        "status                        players, tick time, bandwidth\n" +
        "kick <name> [reason]          disconnect a player\n" +
        "teleport <name> <x> <z>       move a player to a point of the zone (metres east, south)\n" +
        "teleport <name> <other>       move a player next to another\n" +
        "say <text>                    a server line in everyone's chat\n" +
        "give <name> <item> [count]    items into a player's inventory (item keys as in items.json)\n" +
        "kit <name> <loot table>       a loot table's items (loot.json), e.g. supply-crate\n" +
        "heal <name>                   full health, food and water, no bleeding\n" +
        "kill <name>                   a player dies (a corpse with their things)\n" +
        "raidbase <name> [grade]       a claimed hut 8 m in front of a player, listing them (grade 0-4, stone by default)\n" +
        "learn <name> <item|all>       a player learns a blueprint (G7), or every one\n" +
        "world                         the world's props, teams and economy (G7)\n" +
        "save                          save the world now\n" +
        "quit                          save everyone and stop";

    private void RunCommand(string line)
    {
        string[] words = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return;
        }
        switch (words[0].ToLowerInvariant())
        {
            case "help":
            case "?":
                Log(CommandHelp);
                break;
            case "status":
                Log(Status());
                break;
            case "kick" when words.Length >= 2:
                if (Find(words[1]) is { } kicked)
                {
                    string reason = words.Length > 2 ? string.Join(' ', words.Skip(2)) : "Kicked by an admin.";
                    Kick(kicked, GoodbyeReason.Kicked, reason);
                    Log($"kicked {kicked.Name}: {reason}");
                }
                break;
            case "teleport" when words.Length == 3:
                if (Find(words[1]) is { Playing: true } mover && Find(words[2]) is { Playing: true } target)
                {
                    Vector3 to = Zone.Entities.Get<Character>(target.Entity).State.Position;
                    Vector3 beside = Zone.GroundAt(to.X + 1.5f, to.Z);
                    Zone.Teleport(mover.Entity, beside + Zone.Frame.UpAt(beside) * 0.05f);
                    Log($"teleported {mover.Name} to {target.Name}");
                }
                break;
            case "teleport" when words.Length == 4:
                if (Find(words[1]) is { Playing: true } moved && TryNumber(words[2], out float x) && TryNumber(words[3], out float z))
                {
                    Vector3 ground = Zone.GroundAt(x, z);
                    Zone.Teleport(moved.Entity, ground + Zone.Frame.UpAt(ground) * 0.05f);
                    Log($"teleported {moved.Name} to {x:0.#}, {z:0.#}");
                }
                break;
            case "say" when words.Length >= 2:
                string text = line.Trim()[4..].Trim();
                Log($"[server] {text}");
                Broadcast(new ChatLine { Kind = ChatKind.Server, From = "Server", Text = text });
                break;
            case "give" when words.Length >= 3:
                if (Find(words[1]) is { Playing: true } taker && Content.Item(words[2]) is { } item &&
                    Zone.PlayerOf(taker.Entity) is { Dead: false } receiver)
                {
                    int count = words.Length > 3 && int.TryParse(words[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? Math.Clamp(n, 1, 100_000) : 1;
                    int left = receiver.Inventory.Give(Content, item, count);
                    Log($"gave {taker.Name} {count - left} {item.Name}" + (left > 0 ? $" ({left} didn't fit)" : ""));
                }
                else
                {
                    Log("give <playing name> <item key> [count]");
                }
                break;
            case "kit" when words.Length >= 3:
                if (Find(words[1]) is { Playing: true } kitted && Content.Loot(words[2]) is { } table &&
                    Zone.PlayerOf(kitted.Entity) is { Dead: false } owner)
                {
                    GiveKit(owner, table);
                    Log($"gave {kitted.Name} the {table.Key} kit");
                }
                else
                {
                    Log("kit <playing name> <loot table key>");
                }
                break;
            case "heal" when words.Length >= 2:
                if (Find(words[1]) is { Playing: true } healed && Zone.PlayerOf(healed.Entity) is { Dead: false } patient)
                {
                    patient.Vitals.Health = Content.Survival.MaxHealth;
                    patient.Vitals.Calories = Content.Survival.MaxCalories;
                    patient.Vitals.Hydration = Content.Survival.MaxHydration;
                    patient.Vitals.Bleeding = 0;
                    patient.VitalsRevision++;
                    Zone.Entities.Get<Character>(healed.Entity).State.Health = (byte)Content.Survival.MaxHealth;
                    Log($"healed {healed.Name}");
                }
                break;
            case "kill" when words.Length >= 2:
                if (Find(words[1]) is { Playing: true } victim && Zone.PlayerOf(victim.Entity) is { Dead: false } dying)
                {
                    Zone.Hurt(victim.Entity, dying, 100_000, "Killed by an admin", Simulation.Entities.EntityId.None, "");
                    Log($"killed {victim.Name}");
                }
                break;
            case "raidbase" when words.Length >= 2:
                if (Find(words[1]) is { Playing: true } builder && Zone.PlayerOf(builder.Entity) is { } owner2)
                {
                    int grade = words.Length > 2 && int.TryParse(words[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int g) ? Math.Clamp(g, 0, 4) : 2;
                    ref Character c = ref Zone.Entities.Get<Character>(builder.Entity);
                    Vector3 up = Zone.Frame.UpAt(c.State.Position);
                    Vector3 ahead = c.State.Position + Simulation.Movement.CharacterMotor.ViewDirection(c.Input.Yaw, 0, up) * 8;
                    // The doorway faces the player.
                    Simulation.Raid.TestBases.Hut? hut = Simulation.Raid.TestBases.Build(Zone, Zone.GroundAt(ahead.X, ahead.Z),
                        Math.Atan2(c.State.Position.X - ahead.X, c.State.Position.Z - ahead.Z), grade, owner2.Id.Value);
                    Log(hut != null ? $"built a {Content.Building.Grades[grade].Name.ToLowerInvariant()} hut for {builder.Name}" : "no ground there");
                }
                break;
            case "save":
                SaveAll();
                Log("saving the world");
                break;
            case "quit":
            case "stop":
            case "exit":
                RequestStop();
                break;
            default:
                if (!RunWorldCommand(words))
                {
                    Log($"unknown command '{line.Trim()}' (help lists them)");
                }
                break;
        }
    }

    private PlayerSession? Find(string name)
    {
        PlayerSession? session = _sessions.Values.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (session == null)
        {
            Log($"no player called {name}");
        }
        return session;
    }

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
            string where = s.Playing ? Format(Zone.Entities.Get<Character>(s.Entity).State.Position) : "logging in";
            double onTime = s.InputsApplied + s.InputsMissing == 0 ? 100 : 100.0 * s.InputsApplied / (s.InputsApplied + s.InputsMissing);
            text.Append(CultureInfo.InvariantCulture,
                $"\n  {s.Name,-16} ping {s.Peer.RoundTripTime,4} ms  at {where}  sent {stats.BytesSent / 1024.0:0} KiB  " +
                $"received {stats.BytesReceived / 1024.0:0} KiB  inputs on time {onTime:0.0} %");
        }
        return text.ToString();
    }

    private static bool TryNumber(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
