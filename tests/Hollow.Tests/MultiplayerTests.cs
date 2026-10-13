using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Net.Transport;
using Hollow.Server;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;

namespace Hollow.Tests;

/// <summary>An in-process server on a background thread, and clients driven from the test's thread.</summary>
public sealed class LoopbackRig : IDisposable
{
    /// <summary>A server on the stand-in ground (plain and quick), or on the first world's real surface; no animals unless a test asks for them (G7).</summary>
    public LoopbackRig(string? database = null, bool planet = false, bool animals = false, IReadOnlyCollection<string>? admins = null)
    {
        Server = new GameServer(new ServerOptions
        {
            Port = 0, DatabasePath = database, StandIn = !planet, Animals = animals, Admins = admins ?? Array.Empty<string>(), Log = _ => { },
        }, Fixtures.Galaxy);
        Server.StartThread();
    }

    public GameServer Server { get; }

    public List<GameClient> Clients { get; } = new();

    public GameClient Join(string name, NetConditions? conditions = null)
    {
        var client = new GameClient(new ClientOptions { Name = name, Conditions = conditions ?? NetConditions.None });
        client.Connect("127.0.0.1", Server.Port);
        Clients.Add(client);
        return client;
    }

    // One clock for every run, so no time is lost between them: the clients' tick clocks follow real time.
    private readonly Stopwatch _time = Stopwatch.StartNew();
    private double _last;

    /// <summary>Updates every client at 60 Hz for a while, with each one's input from <paramref name="input"/>.</summary>
    public void Run(double seconds, Func<GameClient, double, InputFrame> input)
    {
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

    /// <summary>Runs until every client plays and has its character, or fails after a few seconds.</summary>
    public void WaitUntilPlaying()
    {
        var watch = Stopwatch.StartNew();
        while (!Clients.All(c => c.State == ClientState.Playing && c.HasCharacter))
        {
            Assert.True(watch.Elapsed.TotalSeconds < 10, "clients joined: " +
                string.Join(", ", Clients.Select(c => $"{c.Name} {c.State} {c.DisconnectReason}")));
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
        using var zone = new Zone(spec);
        Entity entity = zone.AddCharacter(new EntityId(7), "server", zone.NewCharacter(zone.SpawnPoint(1)));
        using var predictor = new Predictor(spec, spec.CreateSurface(), null);
        predictor.Reset(0, zone.Entities.Get<Character>(entity).State, 0);
        var commands = new InputCommand[601];
        for (uint tick = 1; tick <= 600; tick++)
        {
            commands[tick] = NetConvert.ToCommand(MovementCheck.Script((int)tick));
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
        using var server = new Zone(spec);
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
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo", new NetConditions { LatencyMs = 150, LossPercent = 5 });
        rig.WaitUntilPlaying();
        // Let the clocks settle, then measure.
        rig.Run(1.5, (_, _) => default);
        ada.Stats.ResetTotals();
        bo.Stats.ResetTotals();
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
            Assert.True(other.Character.Position.Length() > 3, $"{other.Name} moved");
        }
        Assert.True(ada.Stats.PredictionErrorMean < 0.001, "on a clean line prediction is exact");
        Assert.True(bo.Stats.PredictionErrorMean < 0.01, "at 150 ms and 5 % loss prediction holds");
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
            leftAt = ada.Character.Position;
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
        Assert.Near(40, ada.Character.Position.X, 0.05, "teleported east");
        Assert.Near(-25, ada.Character.Position.Z, 0.05, "teleported north");
        rig.Server.Execute("kick Ada testing");
        rig.Run(1.0, (_, _) => default);
        Assert.Equal(ClientState.Disconnected, ada.State, "kicked");
        Assert.True(ada.Goodbye == GoodbyeReason.Kicked && ada.DisconnectReason!.Contains("testing", StringComparison.Ordinal),
            $"told why: {ada.DisconnectReason}");
    }
}
