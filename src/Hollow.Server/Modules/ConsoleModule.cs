using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Net.Commands;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.World;
using Hollow.Surface;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>
/// The F1 console on the server (GROUND.md §8.2): who is an admin, the console's messages (a line in, the answer's lines
/// out, at most five a second), the state the console shows (rights, who is on), the zone's clock, and the commands
/// that move, spawn, cheat, set the time and count.
/// </summary>
public sealed class ConsoleModule : ServerModule
{
    private readonly HashSet<string> _admins = new(StringComparer.OrdinalIgnoreCase);

    // Whether an admin set the zone's clock (time, timescale); the sky it spins is the server's.
    private bool _clockOverridden;

    /// <summary>
    /// The F1 console's budget (GROUND.md §8.2): ten commands at once, five a second; a line over it is refused with a
    /// reply, not run; and the lines refused for coming too fast.
    /// </summary>
    private sealed class ConsoleBudget
    {
        private double _tokens = 10;
        private long _tick;

        public long RateLimited;

        public bool Spend(long tick)
        {
            _tokens = Math.Min(10, _tokens + (tick - _tick) * 5.0 / Simulation.Movement.CharacterMotor.TickRate);
            _tick = tick;
            if (_tokens < 1)
            {
                return false;
            }
            _tokens -= 1;
            return true;
        }
    }

    protected internal override void Init(MessageHandlers<PlayerSession> handlers)
    {
        LoadAdmins();
        // A line from a player's F1 console: run it as them, the answer back to them, line by line.
        handlers.On<ConsoleRun>((PlayerSession session, in ConsoleRun run) =>
        {
            uint seq = run.Seq;
            var ctx = new CommandContext
            {
                Caller = session,
                Admin = IsAdmin(session),
                Reply = (kind, text) => SendConsoleReply(session, seq, kind, text),
            };
            ConsoleBudget budget = session.State<ConsoleBudget>();
            if (!budget.Spend(Zone.Tick))
            {
                budget.RateLimited++;
                ctx.Error("Slow down: the console takes five commands a second.");
                return;
            }
            string line = (run.Text ?? "").Trim();
            if (line.Length == 0)
            {
                return;
            }
            Log($"[console] {session.Name}: {line}");
            Server.RunCommand(line, ctx);
        });
    }

    /// <summary>A player joined: the zone's clock for them, and who is on for everyone (the F1 console's view, GROUND.md §8.2).</summary>
    protected internal override void Joined(PlayerSession session, bool newCharacter)
    {
        SendClock(session);
        SendServerStateToAll();
    }

    protected internal override void Left(PlayerSession session) => SendServerStateToAll();

    // ---------------------------------------------------------------- admins

    /// <summary>
    /// The admins: <see cref="ServerOptions.Admins"/> (the hosting player, <c>--admin</c>) and the names in
    /// <see cref="ServerOptions.AdminsFile"/> (one a line, # for comments), read at start.
    /// </summary>
    private void LoadAdmins()
    {
        foreach (string name in Options.Admins)
        {
            _admins.Add(name.Trim());
        }
        if (Options.AdminsFile is { } file && File.Exists(file))
        {
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Split('#')[0].Trim();
                if (line.Length > 0)
                {
                    _admins.Add(line);
                }
            }
        }
        _admins.RemoveWhere(string.IsNullOrWhiteSpace);
        if (Options.EveryoneAdmin)
        {
            Log("every player is an admin (--dev)");
        }
        else if (_admins.Count > 0)
        {
            Log($"admins: {string.Join(", ", _admins.Order(StringComparer.OrdinalIgnoreCase))}");
        }
    }

    /// <summary>Whether a player may run the admin commands (GROUND.md §8.2).</summary>
    public bool IsAdmin(PlayerSession session) => Options.EveryoneAdmin || _admins.Contains(session.Name);

    internal void SetAdmin(CommandContext ctx, string name, bool admin)
    {
        if (!GameServer.IsValidName(name))
        {
            ctx.Error($"'{name}' isn't a player's name.");
            return;
        }
        bool changed = admin ? _admins.Add(name) : _admins.Remove(name);
        ctx.Done(changed
            ? $"{name} {(admin ? "is an admin now" : "isn't an admin any more")}, until the server stops" +
              (Options.AdminsFile != null ? $" ({Path.GetFileName(Options.AdminsFile)} keeps admins for good)." : ".")
            : $"{name} {(admin ? "was an admin already" : "wasn't an admin")}.");
        if (changed)
        {
            SendServerStateToAll();
        }
    }

    // ---------------------------------------------------------------- the console's messages

    private void SendConsoleReply(PlayerSession session, uint seq, ConsoleReplyKind kind, string text)
    {
        foreach (string raw in text.Split('\n'))
        {
            // A line longer than a reply carries goes as several, broken at spaces.
            string line = raw.TrimEnd('\r');
            do
            {
                int cut = line.Length;
                if (cut > ConsoleLineChars)
                {
                    int space = line.LastIndexOf(' ', ConsoleLineChars);
                    cut = space > ConsoleLineChars / 2 ? space : ConsoleLineChars;
                }
                Writer.Reset();
                new ConsoleReply { Seq = seq, Kind = kind, Text = line[..cut] }.WriteMessage(Writer);
                session.Peer.Send(Writer.Data, DeliveryMethod.ReliableOrdered);
                line = line[cut..].TrimStart();
            }
            while (line.Length > 0);
        }
    }

    // A reply's line holds 400 bytes: a little less in characters, for the odd degree sign.
    private const int ConsoleLineChars = 360;

    // The console's view of the server: this player's rights and who is on.
    private void SendServerState(PlayerSession session)
    {
        Writer.Reset();
        new ServerState
        {
            Admin = IsAdmin(session),
            MaxPlayers = (uint)Math.Max(0, Options.MaxPlayers),
            Players = Players.Where(s => s.Playing).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Take(256).Select(s => new ServerPlayerNet { Name = s.Name, Admin = IsAdmin(s) }).ToArray(),
        }.WriteMessage(Writer);
        session.Peer.Send(Writer.Data, DeliveryMethod.ReliableOrdered);
    }

    private void SendServerStateToAll()
    {
        foreach (PlayerSession session in Players.Where(s => s.Playing))
        {
            SendServerState(session);
        }
    }

    // ---------------------------------------------------------------- the zone's clock

    /// <summary>Universe time now (seconds since J2000), as the zone's climate counts it.</summary>
    private double UniverseSeconds => Zone.UnixMilliseconds / 1000.0 - PlanetClimate.UnixJ2000;

    private ZoneClock ClockMessage()
    {
        (double time, double turns, double scale) = Sky?.Spin ?? (0, 0, Spec.RotationTimeScale);
        return new ZoneClock { BaseTime = time, BaseTurns = turns, Scale = scale, Overridden = _clockOverridden };
    }

    private void SendClock(PlayerSession session)
    {
        Writer.Reset();
        ClockMessage().WriteMessage(Writer);
        session.Peer.Send(Writer.Data, DeliveryMethod.ReliableOrdered);
    }

    private void SendClockToAll()
    {
        foreach (PlayerSession session in Players.Where(s => s.Playing))
        {
            SendClock(session);
        }
    }

    /// <summary>The local solar time (hours) at the landing site now, by the zone's clock; null without a sky.</summary>
    public double? ZoneHours => Sky?.LocalSolarHours(Spec.LandingDirection, Sky.Sun.Index, UniverseSeconds);

    // Where a command's clock is read and set (docs/WHOLE_WORLD.md decision 11): where the caller stands, or at the
    // server's console the landing site.
    private Vector3D ClockPoint(CommandContext ctx) => ctx.Caller is { Playing: true } me
        ? Zone.Frame.ToBody(Zone.Entities.Get<Character>(me.Entity).State.Position).Normalized
        : Spec.LandingDirection;

    private static string ClockPlace(CommandContext ctx) => ctx.Caller is { Playing: true } ? "where you stand" : "at the landing site";

    // time; time <hour>; time reset.
    internal void Time(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (Sky == null)
        {
            ctx.Error("This zone's body has no sky to set.");
            return;
        }
        double now = UniverseSeconds;
        Vector3D origin = ClockPoint(ctx);
        if (args.Count == 0)
        {
            ctx.Out(ClockLine(ctx));
            return;
        }
        if (args[0].ToLowerInvariant() is "reset" or "default" or "real")
        {
            Sky.SetSpin(0, 0, Spec.RotationTimeScale);
            _clockOverridden = false;
            SendClockToAll();
            ctx.Done($"The zone's day is the planet's own again: {Hours(Sky.LocalSolarHours(origin, Sky.Sun.Index, now))} {ClockPlace(ctx)}.");
            return;
        }
        if (!TryHours(args[0], out double hours))
        {
            ctx.Error("Usage: time <hour, 0 to 24, e.g. 21 or 21:30>  |  time reset");
            return;
        }
        Sky.SetLocalSolarHours(origin, Sky.Sun.Index, now, hours);
        _clockOverridden = true;
        SendClockToAll();
        ctx.Done($"It is {Hours(Sky.LocalSolarHours(origin, Sky.Sun.Index, now))} {ClockPlace(ctx)} now: this zone's sun only, universe time unchanged.");
    }

    // timescale; timescale <x> (x times the day of play; 0 stops the sun).
    internal void TimeScale(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (Sky == null)
        {
            ctx.Error("This zone's body has no sky to set.");
            return;
        }
        double now = UniverseSeconds;
        if (args.Count == 0)
        {
            ctx.Out(ClockLine(ctx));
            return;
        }
        if (!double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) || !(x >= 0) || x > 1000)
        {
            ctx.Error("Usage: timescale <x>: 1 the day of play, 0 stops the sun, up to 1000.");
            return;
        }
        Sky.SetSpin(now, Sky.TurnsAt(now), Spec.RotationTimeScale * x);
        _clockOverridden = true;
        SendClockToAll();
        double day = Math.Abs(Sky.Planet.RotationPeriod) / Math.Max(1e-9, Spec.RotationTimeScale * x);
        ctx.Done(x == 0
            ? "The zone's sun stands still (timescale 1 lets it go on)."
            : string.Create(CultureInfo.InvariantCulture, $"The zone's day runs ×{x:0.###} the day of play: a day lasts {Duration(day)}."));
    }

    private string ClockLine(CommandContext ctx)
    {
        double now = UniverseSeconds;
        string line = $"It is {Hours(Sky!.LocalSolarHours(Spec.LandingDirection, Sky.Sun.Index, now))} at the landing site";
        if (ctx.Caller is { Playing: true } me)
        {
            Vector3D here = Zone.Frame.ToBody(Zone.Entities.Get<Character>(me.Entity).State.Position).Normalized;
            line += $", {Hours(Sky.LocalSolarHours(here, Sky.Sun.Index, now))} where you stand";
        }
        double factor = Sky.RotationTimeScale / Spec.RotationTimeScale;
        return line + string.Create(CultureInfo.InvariantCulture, $" (local solar time); the day runs ×{factor:0.###} the day of play") +
            (_clockOverridden ? ", as an admin set it (time reset goes back)." : ".");
    }

    private static bool TryHours(string text, out double hours)
    {
        hours = 0;
        string[] parts = text.Split(':');
        if (parts.Length > 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double h))
        {
            return false;
        }
        double minutes = 0;
        if (parts.Length == 2 && !(double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out minutes) && minutes is >= 0 and < 60))
        {
            return false;
        }
        hours = h + minutes / 60;
        if (!(hours >= 0 && hours <= 24))
        {
            return false;
        }
        hours %= 24;
        return true;
    }

    private static string Hours(double hours)
    {
        int minutes = (int)Math.Round(hours * 60) % (24 * 60);
        return string.Create(CultureInfo.InvariantCulture, $"{minutes / 60:00}:{minutes % 60:00}");
    }

    private static string Duration(double seconds) =>
        seconds >= 3600 ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600:0.#} h")
        : seconds >= 60 ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:0.#} min")
        : string.Create(CultureInfo.InvariantCulture, $"{seconds:0.#} s");

    // ---------------------------------------------------------------- moving

    // teleport <player> | <x> <z> | <latitude> <longitude> | <player> <target> | <player> <x> <z> | <player> <lat> <lon>.
    internal void Teleport(CommandContext ctx, IReadOnlyList<string> args)
    {
        switch (args.Count)
        {
            case 1:
                if (ctx.Caller == null)
                {
                    ctx.Error("Usage here: teleport <player> <target player> or teleport <player> <x> <z>");
                }
                else if (Server.Target(ctx, args[0]) is { } to && Beside(ctx, to) is { } there)
                {
                    MoveTo(ctx, ctx.Caller, there, to.Name);
                }
                return;
            case 2 when ConsoleCatalog.IsNumber(Degrees(args[0])) && ConsoleCatalog.IsNumber(Degrees(args[1])):
                if (ctx.Caller == null)
                {
                    ctx.Error("Name a player: the server's console has no 'me'.");
                }
                else if (Point(ctx, args[0], args[1]) is var (point, where))
                {
                    MoveTo(ctx, ctx.Caller, point, where);
                }
                return;
            case 2:
                if (Server.Target(ctx, args[0]) is { } mover && Server.Target(ctx, args[1]) is { } target && Beside(ctx, target) is { } beside)
                {
                    MoveTo(ctx, mover, beside, target.Name);
                }
                return;
            case 3:
                if (Server.Target(ctx, args[0]) is { } moved && Point(ctx, args[1], args[2]) is var (spot, place))
                {
                    MoveTo(ctx, moved, spot, place);
                }
                return;
            default:
                ctx.Error("Usage: teleport <player> | <x> <z> | <latitude> <longitude> | <player> <target> | <player> <x> <z>");
                return;
        }
    }

    // tp2me <player>: two metres in front of the caller.
    internal void BringToMe(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (ctx.Caller is not { Playing: true } me)
        {
            ctx.Error("Only a player in the world can bring someone to them.");
            return;
        }
        if (args.Count == 0)
        {
            ctx.Error("Usage: tp2me <player>");
            return;
        }
        if (Server.Target(ctx, string.Join(' ', args)) is not { } whom)
        {
            return;
        }
        ref Character c = ref Zone.Entities.Get<Character>(me.Entity);
        Vector3 up = Zone.Frame.UpAt(c.State.Position);
        Vector3 at = c.State.Position + Facing.Basis(c.Input.View, up).Forward * 2;
        MoveTo(ctx, whom, Zone.Settle(at), "you");
    }

    // tpmarker [n]: the caller's newest map marker, or the one with that number.
    internal void TeleportToMarker(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (ctx.Caller is not { Playing: true } me || Zone.Players().PlayerOf(me.Entity) is not { } p)
        {
            ctx.Error("Only a player in the world has map markers.");
            return;
        }
        if (p.Markers.Count == 0)
        {
            ctx.Error("You have no marker on the map (G opens it; a click puts one down).");
            return;
        }
        int index = p.Markers.Count - 1;
        if (args.Count > 0 && (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1 || n > p.Markers.Count))
        {
            ctx.Error($"Your markers are 1 to {p.Markers.Count}.");
            return;
        }
        if (args.Count > 0)
        {
            index = int.Parse(args[0], CultureInfo.InvariantCulture) - 1;
        }
        MapMarker marker = p.Markers[index];
        MoveTo(ctx, me, Zone.Settle(marker.Position),
            $"marker {index + 1}" + (string.IsNullOrWhiteSpace(marker.Label) ? "" : $" ({marker.Label})"));
    }

    // Beside a player (1.5 m east of them), on the ground.
    private Vector3? Beside(CommandContext ctx, PlayerSession target)
    {
        if (!target.Playing)
        {
            ctx.Error($"{target.Name} isn't in the world yet.");
            return null;
        }
        ref Character beside = ref Zone.Entities.Get<Character>(target.Entity);
        Vector3 to = beside.State.Position;
        // 1.5 m to their right.
        return Zone.Settle(to + Facing.Basis(beside.Input.View, Zone.Frame.UpAt(to)).Right * 1.5f);
    }

    // A point of the world from two words: a latitude and longitude, marked with N, S, E or W (or a degree sign), or both
    // with three decimals or more; else metres east and south of the landing site, along the ground (docs/WHOLE_WORLD.md
    // §6.7: the whole world is the zone). Null, said why, when the words aren't numbers.
    private (Vector3 Ground, string Where)? Point(CommandContext ctx, string a, string b)
    {
        if (!TryNumber(Degrees(a), out float first) || !TryNumber(Degrees(b), out float second))
        {
            ctx.Error("Two numbers: a latitude and longitude, or metres east and south of the landing site.");
            return null;
        }
        double latitude = first * Sign(a), longitude = second * Sign(b);
        bool marked = Marked(a) || Marked(b);
        bool degrees = Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180;
        if (degrees && (marked || (Decimals(a) >= 3 && Decimals(b) >= 3)))
        {
            Vector3D direction = ZoneFrame.Direction(latitude * DMath.DegToRad, longitude * DMath.DegToRad);
            string where = string.Create(CultureInfo.InvariantCulture,
                $"{Math.Abs(latitude):0.0000}° {(latitude >= 0 ? 'N' : 'S')}, {Math.Abs(longitude):0.0000}° {(longitude >= 0 ? 'E' : 'W')}");
            return (Zone.Ground.On(direction), where);
        }
        if (marked)
        {
            ctx.Error("A latitude is −90 … 90 and a longitude −180 … 180.");
            return null;
        }
        return (Zone.GroundAt(first, second), string.Create(CultureInfo.InvariantCulture, $"{first:0.#} m east, {second:0.#} m south of the landing site"));
    }

    private static bool Marked(string w) => w.Length > 0 && "NSEWnsew°".Contains(w[^1], StringComparison.Ordinal);

    private static int Decimals(string w)
    {
        string number = Degrees(w);
        int dot = number.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? 0 : number.Length - dot - 1;
    }

    // The number in "44.8683N", "170.001°", "-170.001".
    private static string Degrees(string word) => word.TrimEnd('N', 'S', 'E', 'W', 'n', 's', 'e', 'w', '°');

    private static int Sign(string word) => word.Length > 0 && "SWsw".Contains(word[^1], StringComparison.Ordinal) ? -1 : 1;

    // Moves a player onto a point of the ground (not one in a ship's seat).
    private void MoveTo(CommandContext ctx, PlayerSession whom, Vector3 ground, string where)
    {
        if (!Server.Alive(ctx, whom, out _))
        {
            return;
        }
        if (Zone.Entities.Get<Character>(whom.Entity).State.Stance == Stance.Seated)
        {
            ctx.Error($"{(GameServer.IsMe(ctx, whom) ? "You are" : whom.Name + " is")} in a ship's seat: step out first (F).");
            return;
        }
        Zone.Teleport(whom.Entity, ground + Zone.Frame.UpAt(ground) * 0.05f);
        Log($"teleported {whom.Name} to {where} {Format(ground)}");
        ctx.Done($"Teleported {GameServer.Whom(ctx, whom)} to {where}" + (where.Contains(',', StringComparison.Ordinal) ? "" : $" {Format(ground)}") + ".");
    }

    // ---------------------------------------------------------------- spawning

    // spawn <kind> [count] [player]; spawn raidbase [grade]; the older spawn <species> <player> [count] [metres].
    internal void SpawnCommand(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            ctx.Error("Usage: spawn <kind> [count] [player]: " + string.Join(", ", Spawning.Kinds(Content)));
            return;
        }
        string kind = args[0].ToLowerInvariant();
        if (kind == Spawning.RaidBase)
        {
            RaidBase(ctx, args.Skip(1).ToArray());
            return;
        }
        int count = 1, playerAt = 1;
        float? metres = null;
        if (Content.Animal(kind) != null && args.Count >= 2 && !ConsoleCatalog.IsNumber(args[1]) && Server.Target(ctx, args[1], quiet: true) != null)
        {
            // The older form: the player second, then the count and how far.
            count = args.Count > 2 && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int herd) ? herd : Content.Animal(kind)!.GroupMost;
            metres = args.Count > 3 && TryNumber(args[3], out float m) ? Math.Clamp(m, 2, 200) : 25;
            args = new[] { args[0], args[1] };
            playerAt = 1;
        }
        else if (args.Count > 1 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
        {
            count = n;
            playerAt = 2;
        }
        PlayerSession? whom = Server.TargetOrMe(ctx, args, playerAt);
        if (!Server.Alive(ctx, whom, out Player p))
        {
            return;
        }
        ref Character c = ref Zone.Entities.Get<Character>(whom!.Entity);
        Vector3 feet = c.State.Position;
        Vector3 up = Zone.Frame.UpAt(feet);
        float yaw = c.Input.YawAt(up);
        (Vector3 forward, Vector3 right) = CharacterMotor.HorizontalBasis(yaw, up);
        string front = GameServer.IsMe(ctx, whom) ? "you" : whom.Name;

        if (Content.Animal(kind) is { } species)
        {
            if (Zone.Animals().World is not { } animals)
            {
                ctx.Error("This zone has no animals.");
                return;
            }
            count = Math.Clamp(count, 1, 12);
            float ahead = metres ?? (species.Key is "bear" or "wolf" ? 22 : 18);
            Vector3 at = feet + forward * ahead;
            // A herd of its own: not refilled when killed, as the world's are.
            animals.SpawnHerd(species, Zone.Settle(at), count, facing: yaw + MathF.PI);
            ctx.Done($"{count} {(count == 1 ? species.Name.ToLowerInvariant() : Plural(species.Name.ToLowerInvariant()))} {ahead:0} m in front of {front}.");
            return;
        }
        if (Content.Ships.FirstOrDefault(s => s.Key == kind) is { } shipDef)
        {
            if (Server.Ships().GiveShip(whom) != null)
            {
                ctx.Done($"A {shipDef.Name} beside {front}, fuelled, its hatch open.");
            }
            else
            {
                ctx.Error($"No room for a {shipDef.Name} beside {front}.");
            }
            return;
        }
        PropDef? prop = Spawning.Prop(Content, kind);
        DeployableDef? deployable = prop?.Deployable ?? Content.Deployable(kind);
        if (deployable == null)
        {
            ctx.Error($"Nothing called '{args[0]}' to spawn: " + string.Join(", ", Spawning.Kinds(Content)));
            return;
        }
        count = Math.Clamp(count, 1, 10);
        Vector3 half = deployable.HalfExtents;
        float width = 2 * MathF.Max(half.X, half.Z) + 0.8f, depth = 2.2f + MathF.Max(half.X, half.Z);
        for (int i = 0; i < count; i++)
        {
            Vector3 at = feet + forward * depth + right * ((i - (count - 1) / 2f) * width);
            Vector3 ground = Zone.Settle(at);
            if (prop != null)
            {
                Zone.Players().SpawnProp(prop, ground, yaw);
            }
            else
            {
                Zone.Players().SpawnDeployable(deployable, ground, yaw, p.Id);
            }
        }
        string name = prop?.Name ?? deployable.Item.Name;
        ctx.Done($"{(count == 1 ? "A" : count.ToString(CultureInfo.InvariantCulture))} {(count == 1 ? name.ToLowerInvariant() : Plural(name.ToLowerInvariant()))} in front of {front}.");
    }

    private static string Plural(string name) =>
        name.EndsWith('s') || name.EndsWith("deer", StringComparison.Ordinal) ? name
        : name.EndsWith('x') || name.EndsWith("ch", StringComparison.Ordinal) ? name + "es"
        : name.EndsWith("f", StringComparison.Ordinal) ? name[..^1] + "ves"
        : name + "s";

    // raidbase [grade] [player], or the older raidbase <player> [grade]: a claimed hut 8 m in front, its doorway facing them.
    internal void RaidBase(CommandContext ctx, IReadOnlyList<string> args)
    {
        int grade = 2;
        PlayerSession? whom;
        if (args.Count > 0 && Grade(args[0]) is { } g)
        {
            grade = g;
            whom = Server.TargetOrMe(ctx, args, 1);
        }
        else if (args.Count > 0)
        {
            whom = Server.Target(ctx, args[0]);
            if (args.Count > 1)
            {
                grade = Grade(args[1]) ?? 2;
            }
        }
        else
        {
            whom = Server.TargetOrMe(ctx, args, 0);
        }
        if (!Server.Alive(ctx, whom, out Player owner))
        {
            return;
        }
        if (Zone.Building().World == null)
        {
            ctx.Error("This zone has no building.");
            return;
        }
        ref Character c = ref Zone.Entities.Get<Character>(whom!.Entity);
        Vector3 up = Zone.Frame.UpAt(c.State.Position);
        Vector3 ahead = Zone.Settle(c.State.Position + Facing.Basis(c.Input.View, up).Forward * 8);
        // Its doorway toward the player.
        Vector3 aheadUp = Zone.Frame.UpAt(ahead), back = c.State.Position - ahead;
        back -= aheadUp * Vector3.Dot(back, aheadUp);
        TestBases.Hut? hut = TestBases.Build(Zone, ahead,
            BuildingFrame.HeadingOf(new Vector3D(back.X, back.Y, back.Z), new Vector3D(aheadUp.X, aheadUp.Y, aheadUp.Z)), grade, owner.Id.Value);
        if (hut != null)
        {
            ctx.Done($"A claimed {Content.Building.Grades[grade].Name.ToLowerInvariant()} hut 8 m in front of {GameServer.Whom(ctx, whom)}, listing {GameServer.Whom(ctx, whom)}.");
        }
        else
        {
            ctx.Error("No room or no ground there for a hut.");
        }
    }

    private static int? Grade(string word) =>
        int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? Math.Clamp(n, 0, 4)
        : word.ToLowerInvariant() == "armored" ? 4
        : Array.IndexOf(CompletionSources.Grades, word.ToLowerInvariant()) is var i and >= 0 ? i : null;

    // ---------------------------------------------------------------- cheats

    // god|noclip [on|off] [player]: a toggle without on or off.
    internal void Cheat(CommandContext ctx, IReadOnlyList<string> args, MoveCheats cheat, string name)
    {
        bool? wanted = null;
        int playerAt = 0;
        if (args.Count > 0 && OnOff(args[0]) is { } on)
        {
            wanted = on;
            playerAt = 1;
        }
        PlayerSession? whom = Server.TargetOrMe(ctx, args, playerAt);
        if (whom == null)
        {
            return;
        }
        if (!whom.Playing)
        {
            ctx.Error($"{whom.Name} isn't in the world yet.");
            return;
        }
        ref Character c = ref Zone.Entities.Get<Character>(whom.Entity);
        bool now = wanted ?? !c.State.Has(cheat);
        Zone.SetCheats(whom.Entity, now ? c.State.Cheats | cheat : c.State.Cheats & ~cheat);
        Log($"{name.ToLowerInvariant()} {(now ? "on" : "off")} for {whom.Name}");
        ctx.Done($"{name} {(now ? "on" : "off")}{(GameServer.IsMe(ctx, whom) ? "" : " for " + whom.Name)}." +
            (now && cheat == MoveCheats.Noclip ? " W A S D along the view, Space up, Ctrl down, Shift fast." : ""));
    }

    private static bool? OnOff(string word) => word.ToLowerInvariant() switch
    {
        "on" or "1" or "true" or "yes" => true,
        "off" or "0" or "false" or "no" => false,
        _ => null,
    };

    // ---------------------------------------------------------------- what is where

    internal void ListPlayers(CommandContext ctx)
    {
        PlayerSession[] on = Players.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        ctx.Out($"{on.Length} of {Options.MaxPlayers} players on:");
        foreach (PlayerSession s in on)
        {
            string state = !s.Playing ? "joining" : Zone.Players().PlayerOf(s.Entity) is { } p ? (p.Dead ? "dead" : p.Wounded ? "wounded" : "alive") : "in the world";
            string ping = ctx.Admin ? $", ping {s.Peer.RoundTripTime} ms" : "";
            ctx.Out($"  {s.Name}{(IsAdmin(s) ? " (admin)" : "")}: {state}{ping}");
        }
        int sleepers = Zone.Entities.Pool<PlayerLink>().Values.ToArray().Count(l => l.Player.Sleeping);
        if (sleepers > 0)
        {
            ctx.Out($"  and {sleepers} asleep in the world (logged off).");
        }
    }

    // whereami: latitude and longitude, the zone position and map cell, the biome, the local time, the air.
    internal void WhereAmI(CommandContext ctx)
    {
        if (ctx.Caller is not { Playing: true } me)
        {
            ctx.Error("Only a player in the world is somewhere.");
            return;
        }
        Vector3 at = Zone.Entities.Get<Character>(me.Entity).State.Position;
        Vector3D body = Zone.Frame.ToBody(at);
        Vector3D direction = body.Normalized;
        double latitude = DMath.Asin(direction.Y) * DMath.RadToDeg, longitude = DMath.Atan2(-direction.Z, direction.X) * DMath.RadToDeg;
        ctx.Out(string.Create(CultureInfo.InvariantCulture,
            $"{Math.Abs(latitude):0.00000}° {(latitude >= 0 ? 'N' : 'S')}, {Math.Abs(longitude):0.00000}° {(longitude >= 0 ? 'E' : 'W')} " +
            $"(teleport {latitude:0.00000} {longitude:0.00000})"));
        double sea = Zone.Surface.SeaRadius > 0 ? body.Length - Zone.Surface.SeaRadius : body.Length - Zone.Surface.Radius;
        ctx.Out(string.Create(CultureInfo.InvariantCulture,
            $"Zone position {at.X:0.0} m east, {-at.Z:0.0} m north of its origin, {at.Y:0.0} m up (teleport {at.X:0.0} {at.Z:0.0}); " +
            $"{sea:0} m above the sea; map cell {MapCell(at)}."));
        string biome = Zone.Surface is PlanetSurface planet ? Words(planet.BiomeAt(direction).ToString()) : "the stand-in ground";
        string time = Sky != null ? $"{Hours(Sky.LocalSolarHours(direction, Sky.Sun.Index, UniverseSeconds))} local solar time" : "no sky here";
        float air = Zone.Climate?.TemperatureAt(at, Zone.UnixMilliseconds / 1000.0) ?? float.NaN;
        ctx.Out(string.Create(CultureInfo.InvariantCulture, $"{biome}; {time}; the air {air:0} °C."));
    }

    // A map cell's name, as the map draws its grid: columns lettered from the west, rows numbered from the north.
    private string MapCell(Vector3 at)
    {
        double metres = Content.World.MapMetres;
        int cells = Math.Max(1, (int)Math.Round(metres / Content.World.MapCell));
        int column = Math.Clamp((int)Math.Floor((at.X + metres / 2) / metres * cells), 0, cells - 1);
        int row = Math.Clamp((int)Math.Floor((at.Z + metres / 2) / metres * cells), 0, cells - 1);
        return Column(column) + row.ToString(CultureInfo.InvariantCulture);
    }

    private static string Column(int index) =>
        index < 26 ? ((char)('A' + Math.Max(0, index))).ToString() : Column(index / 26 - 1) + (char)('A' + index % 26);

    // "TemperateForest" → "temperate forest".
    private static string Words(string name) =>
        string.Concat(name.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));

    // ents: what is in the zone.
    internal void CountEntities(CommandContext ctx)
    {
        Player[] players = Zone.Entities.Pool<PlayerLink>().Values.ToArray().Select(l => l.Player).ToArray();
        ctx.Out($"Players: {Players.Count} on; characters in the zone: {players.Length} ({players.Count(p => p.Sleeping)} asleep, " +
            $"{players.Count(p => p.Dead)} dead, {players.Count(p => p.Wounded)} wounded).");
        if (Zone.Animals().World is { } animals)
        {
            Animal[] all = animals.All.Where(a => !a.Removed).ToArray();
            string kinds = string.Join(", ", all.Where(a => !a.IsDead).GroupBy(a => a.Species.Key).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Count()} {g.Key}"));
            ctx.Out($"Animals: {all.Count(a => !a.IsDead)} alive ({(kinds.Length > 0 ? kinds : "none")}), {animals.Awake} awake; dead ones lying: {all.Count(a => a.IsDead)}.");
        }
        WorldObject[] statics = Zone.Things().Statics().ToArray();
        ctx.Out($"Things: {statics.Count(s => s.Kind == StaticKind.Deployable)} deployables, {statics.Count(s => s.Kind == StaticKind.Prop)} world props, " +
            $"{statics.Count(s => s.Kind == StaticKind.DroppedBag)} loot bags, {statics.Count(s => s.Kind == StaticKind.Corpse)} bodies.");
        if (Zone.Building().World is { } building)
        {
            ctx.Out($"Building: {building.PieceCount} pieces, {building.Doors.Count()} doors, {building.Claims.Count()} claim devices" +
                (building.Collapsing > 0 ? $", {building.Collapsing} collapsing." : "."));
        }
        ctx.Out($"Ships: {Zone.Ships().All.Count}; explosives: {Zone.Explosives().All.Count}; natural objects changed: {Zone.Natural?.Count ?? 0}; " +
            $"ground tiles: {Zone.Physics.TileCount}; net entities: {Server.NetEntityCount}.");
    }

    private static bool TryNumber(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}

/// <summary>The accessor on the server (GROUND_SYSTEMS.md §12.5): the F1 console's rights, messages, clock and commands.</summary>
public static class ConsoleModuleAccess
{
    public static ConsoleModule Console(this GameServer server) => server.Require<ConsoleModule>();
}
