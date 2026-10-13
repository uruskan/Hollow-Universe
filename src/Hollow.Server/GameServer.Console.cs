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

// The F1 console on the server (GROUND.md §8.2): who is an admin, the console's messages (a line in, the answer's lines
// out, at most five a second), the state the console shows (rights, who is on), the zone's clock, and the commands
// that move, spawn, cheat, set the time and count.
public sealed partial class GameServer
{
    private readonly HashSet<string> _admins = new(StringComparer.OrdinalIgnoreCase);

    // The zone's sky: its spin is the zone's clock, which admins set (time, timescale); the climate follows it.
    private SkyEphemeris? _sky;
    private bool _clockOverridden;

    // ---------------------------------------------------------------- admins

    /// <summary>
    /// The admins: <see cref="ServerOptions.Admins"/> (the hosting player, <c>--admin</c>) and the names in
    /// <see cref="ServerOptions.AdminsFile"/> (one a line, # for comments), read at start.
    /// </summary>
    private void LoadAdmins()
    {
        foreach (string name in _options.Admins)
        {
            _admins.Add(name.Trim());
        }
        if (_options.AdminsFile is { } file && File.Exists(file))
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
        if (_options.EveryoneAdmin)
        {
            Log("every player is an admin (--dev)");
        }
        else if (_admins.Count > 0)
        {
            Log($"admins: {string.Join(", ", _admins.Order(StringComparer.OrdinalIgnoreCase))}");
        }
    }

    /// <summary>Whether a player may run the admin commands (GROUND.md §8.2).</summary>
    public bool IsAdmin(PlayerSession session) => _options.EveryoneAdmin || _admins.Contains(session.Name);

    private void SetAdmin(CommandContext ctx, string name, bool admin)
    {
        if (!GameServer.IsValidName(name))
        {
            ctx.Error($"'{name}' isn't a player's name.");
            return;
        }
        bool changed = admin ? _admins.Add(name) : _admins.Remove(name);
        ctx.Done(changed
            ? $"{name} {(admin ? "is an admin now" : "isn't an admin any more")}, until the server stops" +
              (_options.AdminsFile != null ? $" ({Path.GetFileName(_options.AdminsFile)} keeps admins for good)." : ".")
            : $"{name} {(admin ? "was an admin already" : "wasn't an admin")}.");
        if (changed)
        {
            SendServerStateToAll();
        }
    }

    // ---------------------------------------------------------------- the console's messages

    // A line from a player's F1 console: run it as them, the answer back to them, line by line.
    private void ReceiveConsole(PlayerSession session)
    {
        ConsoleRun run = ConsoleRun.Read(_reader);
        if (_reader.Failed)
        {
            session.BadPackets++;
            return;
        }
        uint seq = run.Seq;
        var ctx = new CommandContext
        {
            Caller = session,
            Admin = IsAdmin(session),
            Reply = (kind, text) => SendConsoleReply(session, seq, kind, text),
        };
        if (!session.SpendConsoleToken(Zone.Tick))
        {
            session.ConsoleRateLimited++;
            ctx.Error("Slow down: the console takes five commands a second.");
            return;
        }
        string line = (run.Text ?? "").Trim();
        if (line.Length == 0)
        {
            return;
        }
        Log($"[console] {session.Name}: {line}");
        RunCommand(line, ctx);
    }

    private void SendConsoleReply(PlayerSession session, uint seq, ConsoleReplyKind kind, string text)
    {
        foreach (string line in text.Split('\n'))
        {
            _writer.Reset();
            new ConsoleReply { Seq = seq, Kind = kind, Text = line.TrimEnd('\r') }.WriteMessage(_writer);
            session.Peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
        }
    }

    // The console's view of the server: this player's rights and who is on.
    private void SendServerState(PlayerSession session)
    {
        _writer.Reset();
        new ServerState
        {
            Admin = IsAdmin(session),
            MaxPlayers = (uint)Math.Max(0, _options.MaxPlayers),
            Players = _sessions.Values.Where(s => s.Playing).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Take(256).Select(s => new ServerPlayerNet { Name = s.Name, Admin = IsAdmin(s) }).ToArray(),
        }.WriteMessage(_writer);
        session.Peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
    }

    private void SendServerStateToAll()
    {
        foreach (PlayerSession session in _sessions.Values.Where(s => s.Playing))
        {
            SendServerState(session);
        }
    }

    // ---------------------------------------------------------------- the zone's clock

    /// <summary>Universe time now (seconds since J2000), as the zone's climate counts it.</summary>
    private double UniverseSeconds => Zone.UnixMilliseconds / 1000.0 - PlanetClimate.UnixJ2000;

    private ZoneClock ClockMessage()
    {
        (double time, double turns, double scale) = _sky?.Spin ?? (0, 0, Spec.RotationTimeScale);
        return new ZoneClock { BaseTime = time, BaseTurns = turns, Scale = scale, Overridden = _clockOverridden };
    }

    private void SendClock(PlayerSession session)
    {
        _writer.Reset();
        ClockMessage().WriteMessage(_writer);
        session.Peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
    }

    private void SendClockToAll()
    {
        foreach (PlayerSession session in _sessions.Values.Where(s => s.Playing))
        {
            SendClock(session);
        }
    }

    /// <summary>The local solar time (hours) at the zone's origin now, by the zone's clock; null without a sky.</summary>
    public double? ZoneHours => _sky?.LocalSolarHours(Zone.Frame.Origin.Normalized, _sky.Sun.Index, UniverseSeconds);

    // time; time <hour>; time reset.
    private void Time(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (_sky == null)
        {
            ctx.Error("This zone's body has no sky to set.");
            return;
        }
        double now = UniverseSeconds;
        Vector3D origin = Zone.Frame.Origin.Normalized;
        if (args.Count == 0)
        {
            ctx.Out(ClockLine(ctx));
            return;
        }
        if (args[0].ToLowerInvariant() is "reset" or "default" or "real")
        {
            _sky.SetSpin(0, 0, Spec.RotationTimeScale);
            _clockOverridden = false;
            SendClockToAll();
            ctx.Done($"The zone's day is the planet's own again: {Hours(_sky.LocalSolarHours(origin, _sky.Sun.Index, now))} at its origin.");
            return;
        }
        if (!TryHours(args[0], out double hours))
        {
            ctx.Error("Usage: time <hour, 0 to 24, e.g. 21 or 21:30>  |  time reset");
            return;
        }
        _sky.SetLocalSolarHours(origin, _sky.Sun.Index, now, hours);
        _clockOverridden = true;
        SendClockToAll();
        ctx.Done($"It is {Hours(_sky.LocalSolarHours(origin, _sky.Sun.Index, now))} at the zone's origin now: this zone's sun only, universe time unchanged.");
    }

    // timescale; timescale <x> (x times the day of play; 0 stops the sun).
    private void TimeScale(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (_sky == null)
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
        _sky.SetSpin(now, _sky.TurnsAt(now), Spec.RotationTimeScale * x);
        _clockOverridden = true;
        SendClockToAll();
        double day = Math.Abs(_sky.Planet.RotationPeriod) / Math.Max(1e-9, Spec.RotationTimeScale * x);
        ctx.Done(x == 0
            ? "The zone's sun stands still (timescale 1 lets it go on)."
            : string.Create(CultureInfo.InvariantCulture, $"The zone's day runs ×{x:0.###} the day of play: a day lasts {Duration(day)}."));
    }

    private string ClockLine(CommandContext ctx)
    {
        double now = UniverseSeconds;
        string line = $"It is {Hours(_sky!.LocalSolarHours(Zone.Frame.Origin.Normalized, _sky.Sun.Index, now))} at the zone's origin";
        if (ctx.Caller is { Playing: true } me)
        {
            Vector3D here = Zone.Frame.ToBody(Zone.Entities.Get<Character>(me.Entity).State.Position).Normalized;
            line += $", {Hours(_sky.LocalSolarHours(here, _sky.Sun.Index, now))} where you stand";
        }
        double factor = _sky.RotationTimeScale / Spec.RotationTimeScale;
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
    private void Teleport(CommandContext ctx, IReadOnlyList<string> args)
    {
        switch (args.Count)
        {
            case 1:
                if (ctx.Caller == null)
                {
                    ctx.Error("Usage here: teleport <player> <target player> or teleport <player> <x> <z>");
                }
                else if (Target(ctx, args[0]) is { } to && Beside(ctx, to) is { } there)
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
                if (Target(ctx, args[0]) is { } mover && Target(ctx, args[1]) is { } target && Beside(ctx, target) is { } beside)
                {
                    MoveTo(ctx, mover, beside, target.Name);
                }
                return;
            case 3:
                if (Target(ctx, args[0]) is { } moved && Point(ctx, args[1], args[2]) is var (spot, place))
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
    private void BringToMe(CommandContext ctx, IReadOnlyList<string> args)
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
        if (Target(ctx, string.Join(' ', args)) is not { } whom)
        {
            return;
        }
        ref Character c = ref Zone.Entities.Get<Character>(me.Entity);
        Vector3 up = Zone.Frame.UpAt(c.State.Position);
        Vector3 at = c.State.Position + CharacterMotor.ViewDirection(c.Input.Yaw, 0, up) * 2;
        MoveTo(ctx, whom, Zone.GroundAt(at.X, at.Z), "you");
    }

    // tpmarker [n]: the caller's newest map marker, or the one with that number.
    private void TeleportToMarker(CommandContext ctx, IReadOnlyList<string> args)
    {
        if (ctx.Caller is not { Playing: true } me || Zone.PlayerOf(me.Entity) is not { } p)
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
        MoveTo(ctx, me, Zone.GroundAt(marker.Position.X, marker.Position.Z),
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
        Vector3 to = Zone.Entities.Get<Character>(target.Entity).State.Position;
        return Zone.GroundAt(to.X + 1.5f, to.Z);
    }

    // A point of the zone from two words: metres east and south of its origin, or a latitude and longitude: marked with
    // N, S, E or W (or a degree sign), or both with three decimals or more and inside the zone as degrees. Null, said
    // why, when it isn't in the zone.
    private (Vector3 Ground, string Where)? Point(CommandContext ctx, string a, string b)
    {
        if (!TryNumber(Degrees(a), out float first) || !TryNumber(Degrees(b), out float second))
        {
            ctx.Error("Two numbers: metres east and south of the zone's origin, or a latitude and longitude.");
            return null;
        }
        float reach = Simulation.Physics.PhysicsWorld.Extent - 2 * Simulation.Physics.PhysicsWorld.TileSize;
        double latitude = first * Sign(a), longitude = second * Sign(b);
        bool marked = Marked(a) || Marked(b);
        Vector3? asDegrees = null;
        if (Math.Abs(latitude) <= 90 && Math.Abs(longitude) <= 180)
        {
            Vector3D direction = ZoneFrame.Direction(latitude * DMath.DegToRad, longitude * DMath.DegToRad);
            Vector3 local = Zone.Frame.ToLocal(direction * (Zone.Surface.Radius + Zone.Surface.HeightAt(direction)));
            asDegrees = Vector3D.Dot(direction, Zone.Frame.Origin.Normalized) > 0 ? local : new Vector3(float.MaxValue);
        }
        bool inZone = asDegrees is { } d && Math.Abs(d.X) <= reach && Math.Abs(d.Z) <= reach;
        if (marked || (inZone && Decimals(a) >= 3 && Decimals(b) >= 3))
        {
            string where = string.Create(CultureInfo.InvariantCulture,
                $"{Math.Abs(latitude):0.0000}° {(latitude >= 0 ? 'N' : 'S')}, {Math.Abs(longitude):0.0000}° {(longitude >= 0 ? 'E' : 'W')}");
            if (!inZone)
            {
                ctx.Error(string.Create(CultureInfo.InvariantCulture,
                    $"{where} isn't in this zone: it reaches {reach / 1000:0.0} km from its origin ({Spec.LatitudeDegrees:0.0000}°, {Spec.LongitudeDegrees:0.0000}°)."));
                return null;
            }
            return (Zone.GroundAt(asDegrees!.Value.X, asDegrees.Value.Z), where);
        }
        if (Math.Abs(first) > reach || Math.Abs(second) > reach)
        {
            ctx.Error(string.Create(CultureInfo.InvariantCulture, $"{first:0}, {second:0} is outside the zone: it reaches {reach:0} m from its origin."));
            return null;
        }
        return (Zone.GroundAt(first, second), string.Create(CultureInfo.InvariantCulture, $"{first:0.#}, {second:0.#}"));
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
        if (!Alive(ctx, whom, out _))
        {
            return;
        }
        if (Zone.Entities.Get<Character>(whom.Entity).State.Stance == Stance.Seated)
        {
            ctx.Error($"{(IsMe(ctx, whom) ? "You are" : whom.Name + " is")} in a ship's seat: step out first (F).");
            return;
        }
        Zone.Teleport(whom.Entity, ground + Zone.Frame.UpAt(ground) * 0.05f);
        Log($"teleported {whom.Name} to {where} {Format(ground)}");
        ctx.Done($"Teleported {Whom(ctx, whom)} to {where}" + (where.Contains(',', StringComparison.Ordinal) ? "" : $" {Format(ground)}") + ".");
    }

    // ---------------------------------------------------------------- spawning

    // spawn <kind> [count] [player]; spawn raidbase [grade]; the older spawn <species> <player> [count] [metres].
    private void SpawnCommand(CommandContext ctx, IReadOnlyList<string> args)
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
        if (Content.Animal(kind) != null && args.Count >= 2 && !ConsoleCatalog.IsNumber(args[1]) && Target(ctx, args[1], quiet: true) != null)
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
        PlayerSession? whom = TargetOrMe(ctx, args, playerAt);
        if (!Alive(ctx, whom, out Player p))
        {
            return;
        }
        ref Character c = ref Zone.Entities.Get<Character>(whom!.Entity);
        Vector3 feet = c.State.Position;
        Vector3 up = Zone.Frame.UpAt(feet);
        float yaw = c.Input.Yaw;
        (Vector3 forward, Vector3 right) = CharacterMotor.HorizontalBasis(yaw, up);
        string front = IsMe(ctx, whom) ? "you" : whom.Name;

        if (Content.Animal(kind) is { } species)
        {
            if (Zone.Animals is not { } animals)
            {
                ctx.Error("This zone has no animals.");
                return;
            }
            count = Math.Clamp(count, 1, 12);
            float ahead = metres ?? (species.Key is "bear" or "wolf" ? 22 : 18);
            Vector3 at = feet + forward * ahead;
            // A herd of its own: not refilled when killed, as the world's are.
            animals.SpawnHerd(species, Zone.GroundAt(at.X, at.Z), count, facing: yaw + MathF.PI);
            ctx.Done($"{count} {(count == 1 ? species.Name.ToLowerInvariant() : Plural(species.Name.ToLowerInvariant()))} {ahead:0} m in front of {front}.");
            return;
        }
        if (Content.Ships.FirstOrDefault(s => s.Key == kind) is { } shipDef)
        {
            if (GiveShip(whom) != null)
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
            Vector3 ground = Zone.GroundAt(at.X, at.Z);
            if (prop != null)
            {
                Zone.SpawnProp(prop, ground, yaw);
            }
            else
            {
                Zone.SpawnDeployable(deployable, ground, yaw, p.Id);
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
    private void RaidBase(CommandContext ctx, IReadOnlyList<string> args)
    {
        int grade = 2;
        PlayerSession? whom;
        if (args.Count > 0 && Grade(args[0]) is { } g)
        {
            grade = g;
            whom = TargetOrMe(ctx, args, 1);
        }
        else if (args.Count > 0)
        {
            whom = Target(ctx, args[0]);
            if (args.Count > 1)
            {
                grade = Grade(args[1]) ?? 2;
            }
        }
        else
        {
            whom = TargetOrMe(ctx, args, 0);
        }
        if (!Alive(ctx, whom, out Player owner))
        {
            return;
        }
        if (Zone.Building == null)
        {
            ctx.Error("This zone has no building.");
            return;
        }
        ref Character c = ref Zone.Entities.Get<Character>(whom!.Entity);
        Vector3 up = Zone.Frame.UpAt(c.State.Position);
        Vector3 ahead = c.State.Position + CharacterMotor.ViewDirection(c.Input.Yaw, 0, up) * 8;
        TestBases.Hut? hut = TestBases.Build(Zone, Zone.GroundAt(ahead.X, ahead.Z),
            Math.Atan2(c.State.Position.X - ahead.X, c.State.Position.Z - ahead.Z), grade, owner.Id.Value);
        if (hut != null)
        {
            ctx.Done($"A claimed {Content.Building.Grades[grade].Name.ToLowerInvariant()} hut 8 m in front of {Whom(ctx, whom)}, listing {Whom(ctx, whom)}.");
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
    private void Cheat(CommandContext ctx, IReadOnlyList<string> args, MoveCheats cheat, string name)
    {
        bool? wanted = null;
        int playerAt = 0;
        if (args.Count > 0 && OnOff(args[0]) is { } on)
        {
            wanted = on;
            playerAt = 1;
        }
        PlayerSession? whom = TargetOrMe(ctx, args, playerAt);
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
        ctx.Done($"{name} {(now ? "on" : "off")}{(IsMe(ctx, whom) ? "" : " for " + whom.Name)}." +
            (now && cheat == MoveCheats.Noclip ? " W A S D along the view, Space up, Ctrl down, Shift fast." : ""));
    }

    private static bool? OnOff(string word) => word.ToLowerInvariant() switch
    {
        "on" or "1" or "true" or "yes" => true,
        "off" or "0" or "false" or "no" => false,
        _ => null,
    };

    // ---------------------------------------------------------------- what is where

    private void ListPlayers(CommandContext ctx)
    {
        PlayerSession[] on = _sessions.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        ctx.Out($"{on.Length} of {_options.MaxPlayers} players on:");
        foreach (PlayerSession s in on)
        {
            string state = !s.Playing ? "joining" : Zone.PlayerOf(s.Entity) is { } p ? (p.Dead ? "dead" : p.Wounded ? "wounded" : "alive") : "in the world";
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
    private void WhereAmI(CommandContext ctx)
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
        string time = _sky != null ? $"{Hours(_sky.LocalSolarHours(direction, _sky.Sun.Index, UniverseSeconds))} local solar time" : "no sky here";
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
    private void CountEntities(CommandContext ctx)
    {
        Player[] players = Zone.Entities.Pool<PlayerLink>().Values.ToArray().Select(l => l.Player).ToArray();
        ctx.Out($"Players: {_sessions.Count} on; {players.Length} characters in the zone ({players.Count(p => p.Sleeping)} asleep, " +
            $"{players.Count(p => p.Dead)} dead, {players.Count(p => p.Wounded)} wounded).");
        if (Zone.Animals is { } animals)
        {
            Animal[] all = animals.All.Where(a => !a.Removed).ToArray();
            string kinds = string.Join(", ", all.Where(a => !a.IsDead).GroupBy(a => a.Species.Key).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Count()} {g.Key}"));
            ctx.Out($"Animals: {all.Count(a => !a.IsDead)} alive ({(kinds.Length > 0 ? kinds : "none")}), {all.Count(a => a.IsDead)} corpses, {animals.Awake} awake.");
        }
        WorldObject[] statics = Zone.Statics().ToArray();
        ctx.Out($"Things: {statics.Count(s => s.Kind == StaticKind.Deployable)} deployables, {statics.Count(s => s.Kind == StaticKind.Prop)} world props, " +
            $"{statics.Count(s => s.Kind == StaticKind.DroppedItem)} dropped items, {statics.Count(s => s.Kind == StaticKind.Corpse)} bodies.");
        if (Zone.Building is { } building)
        {
            ctx.Out($"Building: {building.PieceCount} pieces, {building.Doors.Count()} doors, {building.Claims.Count()} claim devices" +
                (building.Collapsing > 0 ? $", {building.Collapsing} collapsing." : "."));
        }
        ctx.Out($"Ships: {Zone.Ships.Count}; explosives: {Zone.Explosives.Count}; natural objects changed: {Zone.Natural?.Count ?? 0}; " +
            $"ground tiles: {Zone.Physics.TileCount}; net entities: {_netStates.Count}.");
    }
}
