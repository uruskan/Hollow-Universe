using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading;
using Hollow.Client;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Net.Transport;
using Hollow.Server;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;

namespace Hollow.Tests;

/// <summary>
/// An in-process server and clients on loopback. By default the server runs on a background thread and the clients are
/// driven from the test's thread on the wall clock, as a game would be; a busy PC (a collection, paging, other processes)
/// then changes how many ticks and inputs each side gets, which is right for a smoke test and wrong for an assertion
/// about distances, counts or what a lossy line delivered. With <c>simulated: true</c> nothing waits for the PC: the rig
/// steps the server and every client itself, on one thread, in fixed steps of a simulated clock, and a client behind a
/// bad line (<see cref="Join"/>) goes through a <see cref="SimulatedRelay"/> that delays and drops datagrams on that
/// clock. Who gets which tick, input and datagram then depends on the code and the seeds, not on how long a step takes;
/// only what LiteNetLib reads from the wall clock (the delay before it resends a lost reliable packet) still does.
/// </summary>
public sealed class LoopbackRig : IDisposable
{
    /// <summary>The simulated clock's steps: a client frame each, 60 Hz as the game draws; the server ticks on every second.</summary>
    public const int StepsPerSecond = 60;

    private const int StepsPerServerTick = StepsPerSecond / GameServer.TickRate;

    /// <summary>
    /// A server on the stand-in ground (plain and quick; players arrive at the landing site, where the tests lay out their
    /// places), or on the first world's real surface (on its beaches); no animals unless a test asks for them (G7).
    /// </summary>
    public LoopbackRig(string? database = null, bool planet = false, bool animals = false, IReadOnlyCollection<string>? admins = null,
        bool simulated = false)
    {
        Simulated = simulated;
        Server = new GameServer(new ServerOptions
        {
            Port = 0, DatabasePath = database, StandIn = !planet, SpawnAtOrigin = !planet, Animals = animals, Admins = admins ?? Array.Empty<string>(),
            Log = _ => { },
        }, Fixtures.Galaxy);
        if (simulated)
        {
            // No server thread: Run steps it. Sleeps of 1 ms, so waiting for the wall clock (below) costs little.
            TimerResolution.Begin();
            _time.Restart();
        }
        else
        {
            Server.StartThread();
        }
    }

    public GameServer Server { get; }

    public List<GameClient> Clients { get; } = new();

    /// <summary>Whether the rig steps everything itself on a simulated clock.</summary>
    public bool Simulated { get; }

    /// <summary>Seeds the loss and jitter of each simulated bad line (with the client's name); any value, the same every run.</summary>
    public ulong LineSeed { get; set; } = 1;

    /// <summary>Seconds since the rig began: simulated ones, or wall-clock ones when it runs on the wall clock.</summary>
    public double Elapsed => Simulated ? _steps / (double)StepsPerSecond : _time.Elapsed.TotalSeconds;

    public GameClient Join(string name, NetConditions? conditions = null)
    {
        conditions ??= NetConditions.None;
        GameClient client;
        if (Simulated && !conditions.IsNone)
        {
            var relay = new SimulatedRelay(new IPEndPoint(IPAddress.Loopback, Server.Port), conditions, Hash64.Of(LineSeed, Hash64.Of(name)));
            _relays.Add(relay);
            client = new GameClient(new ClientOptions { Name = name });
            client.Connect("127.0.0.1", relay.Port);
        }
        else
        {
            client = new GameClient(new ClientOptions { Name = name, Conditions = conditions });
            client.Connect("127.0.0.1", Server.Port);
        }
        Clients.Add(client);
        return client;
    }

    // One clock for every run, so no time is lost between them: the clients' tick clocks follow real time.
    private readonly Stopwatch _time = Stopwatch.StartNew();
    private readonly List<SimulatedRelay> _relays = new();
    private double _last;
    private long _steps;
    private long _stepDue;

    /// <summary>Updates every client at 60 Hz for a while, with each one's input from <paramref name="input"/>.</summary>
    public void Run(double seconds, Func<GameClient, double, InputFrame> input)
    {
        if (Simulated)
        {
            RunSimulated(seconds, input);
            return;
        }
        TimerResolution.Begin();
        try
        {
            double start = _time.Elapsed.TotalSeconds;
            while (_time.Elapsed.TotalSeconds - start < seconds)
            {
                double now = _time.Elapsed.TotalSeconds;
                foreach (GameClient client in Clients)
                {
                    client.Update(now - _last, input(client, now - start));
                }
                _last = now;
                Thread.Sleep(15);
            }
        }
        finally
        {
            TimerResolution.End();
        }
    }

    // A step: the lines deliver what is due, the server ticks on every second step, each client has its frame (and what it
    // sent goes into its line at once). A step takes at least its own length of the wall clock, and a late one is not made
    // up for: whatever in the game runs on real time (LiteNetLib resends a lost reliable packet after a delay it reads from the
    // wall clock, ground tiles are built by worker threads, a client's ground is waited for) then sees no faster a clock than
    // its own, and a stall of the PC stretches the run instead of changing what happens in it.
    private void RunSimulated(double seconds, Func<GameClient, double, InputFrame> input)
    {
        long first = _steps;
        long last = first + (long)Math.Round(seconds * StepsPerSecond);
        long stepLength = Stopwatch.Frequency / StepsPerSecond;
        while (_steps < last)
        {
            double t = (_steps - first) / (double)StepsPerSecond;
            _steps++;
            double now = _steps / (double)StepsPerSecond;
            TimerResolution.WaitUntil(_stepDue);
            _stepDue = Math.Max(_stepDue, Stopwatch.GetTimestamp()) + stepLength;
            Pump(now);
            if (_steps % StepsPerServerTick == 0)
            {
                Server.Tick();
                Pump(now);
            }
            foreach (GameClient client in Clients)
            {
                client.Update(1.0 / StepsPerSecond, input(client, t));
                Pump(now);
            }
        }
    }

    private void Pump(double now)
    {
        foreach (SimulatedRelay relay in _relays)
        {
            relay.Pump(now);
        }
    }

    /// <summary>
    /// The console's teleport of a player onto the ground under a zone point (and so many metres further east and south):
    /// the point in the console's terms, metres east and south of the landing site along the ground.
    /// </summary>
    public void Teleport(string name, Vector3 at, float east = 0, float south = 0)
    {
        (float e, float s) = Server.Inspect(sv => sv.Zone.OffsetOf(at));
        Server.Execute(FormattableString.Invariant($"teleport {name} {e + east:0.00} {s + south:0.00}"));
    }

    /// <summary>Runs until every client plays and has its character, or fails after a few seconds.</summary>
    public void WaitUntilPlaying()
    {
        double start = Elapsed;
        while (!Clients.All(c => c.State == ClientState.Playing && c.HasCharacter))
        {
            Assert.True(Elapsed - start < 10, "clients joined: " +
                string.Join(", ", Clients.Select(c => $"{c.Name} {c.State} {c.DisconnectReason}")));
            Run(0.1, (_, _) => default);
        }
    }

    /// <summary>
    /// Runs until every client has settled in: its tick clock is steady against the server's (no clock jump for a while, and
    /// inputs reaching the server in time without much to spare) and its prediction stands on its own ground (no tick held for
    /// ground lately). A client starts its clock from how long its handshake took, on the wall clock, so a lost connection
    /// request or a busy PC sets it tens of ticks ahead, and the server's reports then pause it for as long
    /// (<c>GameClient.SteerClock</c>); it holds its character at the start until worker threads have built the ground
    /// around it (<c>Predictor.Step</c>), which a busy PC makes take seconds, and the server's states correct every tick
    /// of the wait. A test that counts ticks, inputs, shots or metres must begin after both.
    /// </summary>
    public void WaitUntilSettled()
    {
        const double Quiet = 1.2; // seconds: twice the steering's cooldown, plus a margin report's half second
        var jumps = Clients.ToDictionary(c => c, c => c.Stats.ClockJumps);
        var held = Clients.ToDictionary(c => c, c => c.Prediction?.HeldForGround ?? 0);
        double changed = Elapsed;
        var watch = Stopwatch.StartNew();
        while (true)
        {
            foreach (GameClient client in Clients)
            {
                long heldNow = client.Prediction?.HeldForGround ?? 0;
                if (client.Stats.ClockJumps != jumps[client] || heldNow != held[client])
                {
                    jumps[client] = client.Stats.ClockJumps;
                    held[client] = heldNow;
                    changed = Elapsed;
                }
            }
            bool inTime = Clients.All(c => c.Stats.InputMargin is >= 0 and <= GameClient.TargetInputMargin + 10);
            if (inTime && Elapsed - changed >= Quiet)
            {
                return;
            }
            Assert.True(watch.Elapsed.TotalSeconds < 30, "clients settled: " +
                string.Join(", ", Clients.Select(c => $"{c.Name} margin {c.Stats.InputMargin}, {c.Stats.ClockJumps} jumps, {c.Prediction?.HeldForGround} ticks held")));
            Run(0.1, (_, _) => default);
        }
    }

    public void Dispose()
    {
        foreach (GameClient client in Clients)
        {
            client.Dispose();
        }
        Server.Stop();
        Server.Dispose();
        foreach (SimulatedRelay relay in _relays)
        {
            relay.Dispose();
        }
        if (Simulated)
        {
            TimerResolution.End();
        }
    }
}

public static class MultiplayerTests
{
    /// <summary>
    /// GROUND.md §4.3: replaying a recorded input stream through client prediction gives exactly the server's result.
    /// The server's zone steps a character with 600 ticks of input as it would receive them; a predictor steps the same
    /// commands on its own ground. Every state matches to the bit. Then the predictor, 8 ticks ahead, is handed a
    /// different server state (a correction), rewinds and replays: it lands exactly where the server goes from there.
    /// </summary>
    [Test]
    public static void PredictionReplaysToTheServersResult()
    {
        // On the first world's real ground at the landing site: hills, a forest edge's slopes (GROUND.md §3).
        ZoneSpec spec = ZoneSpec.FirstWorld(Fixtures.Galaxy, Surface.FirstWorld.LandingLatitude, Surface.FirstWorld.LandingLongitude)!;
        using var zone = new Zone(spec.CreateSurface(diagnostic: true), spec);
        Entity entity = zone.AddCharacter(new EntityId(7), "server", zone.NewCharacter(zone.SpawnPoint(1)));
        using var predictor = new Predictor(spec, spec.CreateSurface(diagnostic: true), null);
        // The client's ground first, as its loading screen waits for it (GROUND.md §4.3: the prediction holds without it).
        predictor.World.EnsureGround(zone.Entities.Get<Character>(entity).State.Position);
        predictor.Reset(0, zone.Entities.Get<Character>(entity).State, 0);
        var commands = new InputCommand[601];
        for (uint tick = 1; tick <= 600; tick++)
        {
            commands[tick] = NetConvert.ToCommand(MovementCheck.Script((int)tick, zone.Frame.UpAt(zone.Entities.Get<Character>(entity).State.Position)));
            ref Character c = ref zone.Entities.Get<Character>(entity);
            c.Input = NetConvert.ToMoveInput(commands[tick]);
            zone.Step();
            predictor.RecordInput(tick, commands[tick]);
            predictor.Predict(tick);
            Assert.True(predictor.Latest.Equals(zone.Entities.Get<Character>(entity).State), $"tick {tick}: prediction {predictor.Latest} is the server's {zone.Entities.Get<Character>(entity).State}");
        }

        // A correction: the server's character was pushed (as by an admin teleport) at tick 592; the predictor is at 600.
        ref Character character = ref zone.Entities.Get<Character>(entity);
        Assert.Equal(600L, zone.Tick, "server tick");
        // Rebuild the server's history from tick 592 with a different state.
        using var server = new Zone(spec.CreateSurface(diagnostic: true), spec);
        CharacterState pushed = predictor.TryGetState(592, out CharacterState at592) ? at592 : default;
        pushed.Position += new Vector3(0.7f, 0.3f, -0.4f);
        Entity again = server.AddCharacter(new EntityId(8), "server", pushed);
        for (uint tick = 593; tick <= 600; tick++)
        {
            server.Entities.Get<Character>(again).Input = NetConvert.ToMoveInput(commands[tick]);
            server.Step();
        }
        float? error = predictor.Reconcile(592, pushed);
        Fixtures.Report($"correction of {error:0.000} m at tick 592 replayed through 8 ticks: {predictor.Latest}");
        Assert.True(error > 0.5f, "the correction was seen");
        Assert.True(predictor.Latest.Equals(server.Entities.Get<Character>(again).State), "after replay, exactly the server's state");
    }

    /// <summary>
    /// GROUND.md §5 G1: a server and two headless clients on loopback, one of them through a relay adding 150 ms of
    /// ping and 5 % loss each way. Once joined and settled, both walk, then stop; each sees the other where the server
    /// has them, and both predictions held.
    /// </summary>
    [Test]
    public static void TwoClientsSeeEachOther()
    {
        // On the simulated clock: how far Bo gets in two seconds of strafing must not depend on what else the PC is doing.
        using var rig = new LoopbackRig(simulated: true);
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo", new NetConditions { LatencyMs = 150, LossPercent = 5 });
        rig.WaitUntilPlaying();
        // Let the clocks settle (not for a fixed time: a lost packet at the handshake sets Bo's clock tens of ticks ahead, and
        // the server's reports then pause it for as long), then measure.
        rig.WaitUntilSettled();
        ada.Stats.ResetTotals();
        bo.Stats.ResetTotals();
        var starts = new Dictionary<GameClient, Vector3> { [ada] = ada.Character.Position, [bo] = bo.Character.Position };
        rig.Run(3.0, (client, t) => client == ada
            ? new InputFrame { Forward = t < 2.0 ? 1 : 0, Yaw = 0.5f, Sprint = true }
            : new InputFrame { Right = t < 2.0 ? 1 : 0, Yaw = -1f, JumpPressed = t is > 0.5 and < 0.53, Jump = t is > 0.5 and < 0.6 });
        rig.Run(1.0, (_, _) => default);

        foreach ((GameClient viewer, GameClient other) in new[] { (ada, bo), (bo, ada) })
        {
            RemoteEntity? seen = viewer.Entities.FirstOrDefault(e => e.Name == other.Name);
            Assert.True(seen != null, $"{viewer.Name} sees {other.Name}");
            float gap = Vector3.Distance(seen!.Position, other.Character.Position);
            Fixtures.Report($"{viewer.Name} sees {other.Name} {gap * 1000:0.0} mm from where {other.Name} is; " +
                $"{viewer.Name}: {viewer.Stats.Snapshots} snapshots, prediction error mean {viewer.Stats.PredictionErrorMean * 1000:0.00} mm, " +
                $"worst {viewer.Stats.PredictionErrorWorst * 1000:0.0} mm, {viewer.Stats.Corrections} corrections, ping {viewer.Stats.RoundTripMs} ms");
            Assert.True(gap < 0.02f, $"{viewer.Name} sees {other.Name} within 2 cm");
            Assert.True(Vector3.Distance(other.Character.Position, starts[other]) > 3, $"{other.Name} moved");
        }
        Assert.True(ada.Stats.PredictionErrorMean < 0.001, "on a clean line prediction is exact");
        Assert.True(bo.Stats.PredictionErrorMean < 0.01, "at 150 ms and 5 % loss prediction holds");
    }

    /// <summary>
    /// GROUND.md §4.4: a client's tick clock starts from how long its handshake took, so a slow join (a lost packet, a busy PC)
    /// starts it tens of ticks ahead of where it should be, and the server's margin reports then steer it back. At a
    /// ping of 150 ms those reports still described the old clock when the cooldown ended, so the clock was moved twice for one
    /// error, overshot, and swung by tens of ticks every second or two without ever settling (the first test of this
    /// file failed on it, and a player on such a line would have been paused again and again). The first move now waits for
    /// the server's window and the trip back; the clock settles with a jump or two.
    /// </summary>
    [Test]
    public static void TickClockSettlesAfterASlowHandshakeAtALongPing()
    {
        using var rig = new LoopbackRig(simulated: true);
        GameClient bo = rig.Join("Bo", new NetConditions { LatencyMs = 150 });
        // Nothing runs while the connection is made: its handshake takes the wall clock's 700 ms and the line's 150.
        Thread.Sleep(700);
        rig.WaitUntilPlaying();
        rig.WaitUntilSettled();
        Fixtures.Report($"settled after {rig.Elapsed:0.0} s with {bo.Stats.ClockJumps} clock jumps, margin {bo.Stats.InputMargin}, ping {bo.Stats.RoundTripMs} ms");
        Assert.True(bo.Stats.ClockJumps <= 3, $"the clock is moved to where it belongs in a jump or two, not {bo.Stats.ClockJumps}");
    }

    /// <summary>
    /// GROUND.md §4.5: free look (Alt) turns the head, not the view. Ada walks while looking back over her shoulder,
    /// further than a neck goes: her turn is held at RustMVP's 120° and her view within ±85°; her view, so her aim and
    /// her walk, stays where it was (Bo sees it unchanged and her prediction holds) while Bo sees her head turned. Letting
    /// go turns it back at once. When her input stops for longer than 0.8 s the server turns her head back by itself.
    /// </summary>
    [Test]
    public static void FreeLookTurnsTheHeadOthersSee()
    {
        var look = new FreeLook();
        look.Hold(true);
        look.Turn(3f, 2f, viewPitch: 0.3f);
        Assert.Near(FreeLook.MaxYaw, look.Yaw, 1e-6, "the turn stops at 120°");
        Assert.Near(FreeLook.MaxViewPitch - 0.3f, look.Pitch, 1e-6, "the view looks no higher than 85°");
        Assert.Equal((sbyte)80, NetConvert.BoundLook(127, FreeLook.MaxYaw), "a turn sent too far is held at 120° by the server");

        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Run(1.0, (_, _) => default);
        ada.Stats.ResetTotals();
        InputFrame walking = new() { Forward = 1, Yaw = 0.5f, Pitch = 0.3f, LookYaw = look.Yaw, LookPitch = look.Pitch };
        rig.Run(1.2, (client, _) => client == ada ? walking : default);
        RemoteEntity seen = bo.Entities.First(e => e.Name == "Ada");
        Fixtures.Report($"Bo sees Ada's view at {seen.Yaw:0.000}, {seen.Pitch:0.000} rad and her head turned {seen.LookYaw:0.000}, " +
            $"{seen.LookPitch:0.000} rad; her prediction error {ada.Stats.PredictionErrorMean * 1000:0.000} mm");
        Assert.Near(look.Yaw, seen.LookYaw, NetConvert.LookStep, "Bo sees her head turned");
        Assert.Near(look.Pitch, seen.LookPitch, NetConvert.LookStep, "and nodded");
        Assert.Near(0.5, seen.Yaw, 0.001, "her view stays where she walks and aims");
        Assert.True(ada.Stats.PredictionErrorMean < 0.001, "her movement is predicted as before");

        look.Hold(false);
        Assert.True(look.Yaw == 0 && look.Pitch == 0, "letting go turns the camera back at once");
        rig.Run(0.5, (client, _) => client == ada ? walking with { LookYaw = 0, LookPitch = 0 } : default);
        Assert.Near(0, seen.LookYaw, 1e-6, "and others see the head back");

        // Her client stops sending (a stall): the server keeps her last input a while, then turns the head back.
        rig.Run(0.5, (client, _) => client == ada ? walking : default);
        rig.Clients.Remove(ada);
        rig.Run(0.5, (_, _) => default);
        Assert.Near(walking.LookYaw, seen.LookYaw, NetConvert.LookStep, "half a second without input: still turned");
        rig.Run(0.9, (_, _) => default);
        Assert.Near(0, seen.LookYaw, 1e-6, "after 0.8 s the server turns it back");
        rig.Clients.Add(ada);
    }

    /// <summary>GROUND.md §5 G1: after a server restart you're where you left off (saved on disconnect, restored).</summary>
    [Test]
    public static void CharacterReturnsAfterServerRestart()
    {
        using var file = new TempDatabase();
        Vector3 leftAt;
        using (var rig = new LoopbackRig(file.Path))
        {
            GameClient ada = rig.Join("Ada");
            rig.WaitUntilPlaying();
            rig.Run(1.5, (_, t) => new InputFrame { Forward = t < 1.0 ? 1 : 0, Yaw = 2.0f });
            // The server saves the state it has stepped, which on a busy PC can be some ticks behind the client's
            // prediction: stand still until the two agree, then remember where the server has the character.
            var settle = Stopwatch.StartNew();
            while (settle.Elapsed.TotalSeconds < 5 && Vector3.Distance(ServerPosition(rig, ada), ada.Character.Position) > 0.005f)
            {
                rig.Run(0.1, (_, _) => new InputFrame { Yaw = 2.0f });
            }
            leftAt = ServerPosition(rig, ada);
        }
        using (var rig = new LoopbackRig(file.Path))
        {
            GameClient ada = rig.Join("ada");
            rig.WaitUntilPlaying();
            rig.Run(0.3, (_, _) => default);
            float gap = Vector3.Distance(leftAt, ada.Character.Position);
            Fixtures.Report($"left at ({leftAt.X:0.00}, {leftAt.Y:0.00}, {leftAt.Z:0.00}), back {gap * 100:0.0} cm from there, facing {ada.InitialYaw:0.00} rad");
            Assert.True(gap < 0.05f, "restored where it left off");
            Assert.Near(2.0, ada.InitialYaw, 0.001, "and facing the same way");
        }
    }

    // Where the server's zone has a client's character now (read across threads: fine for a test's comparison).
    private static Vector3 ServerPosition(LoopbackRig rig, GameClient client) =>
        rig.Server.Zone.Players().TryFindPlayer(client.EntityId, out Entity entity, out _)
            ? rig.Server.Zone.Entities.Get<Character>(entity).State.Position
            : client.Character.Position;

    /// <summary>
    /// GROUND.md §4.2: admin commands and chat. A teleport reaches the client as a correction; a player's line and a
    /// server line reach everyone; a kick ends the connection with its reason.
    /// </summary>
    [Test]
    public static void AdminCommandsAndChat()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        var lines = new List<string>();
        ada.ChatReceived += line => lines.Add($"{line.From}: {line.Text}");
        rig.WaitUntilPlaying();
        ada.SendChat("hello");
        rig.Server.Execute("say welcome");
        rig.Server.Execute("teleport Ada 40 -25");
        rig.Run(1.0, (_, _) => default);
        Fixtures.Report($"chat: {string.Join(" | ", lines)}; after teleport at {ada.Character.Position}");
        Assert.True(lines.Contains("Ada: hello") && lines.Contains("Server: welcome"), "both chat lines arrived");
        (float east, float south) = rig.Server.Inspect(sv => sv.Zone.OffsetOf(ada.Character.Position));
        Assert.Near(40, east, 0.05, "teleported east");
        Assert.Near(-25, south, 0.05, "teleported north");
        rig.Server.Execute("kick Ada testing");
        rig.Run(1.0, (_, _) => default);
        Assert.Equal(ClientState.Disconnected, ada.State, "kicked");
        Assert.True(ada.Goodbye == GoodbyeReason.Kicked && ada.DisconnectReason!.Contains("testing", StringComparison.Ordinal),
            $"told why: {ada.DisconnectReason}");
    }
}
