using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Hollow.Client;

namespace Hollow.Tests;

/// <summary>Transport stays alive through a graphical stall without running application callbacks on its workers.</summary>
public static class ClientTransportTests
{
    [Test]
    public static void BackgroundTransportSurvivesAFrameStallAndDispatchesOnTheCaller()
    {
        using var rig = new LoopbackRig();
        GameClient background = AddBackground(rig, "FrameStall");
        // The control deliberately uses the normal default, without opting into background transport.
        GameClient manual = rig.Join("ManualStall");
        rig.WaitUntilPlaying();
        rig.Run(0.2, (_, _) => default);

        const string duringStall = "queued during frame stall";
        const string afterStall = "sent after frame stall";
        int caller = Environment.CurrentManagedThreadId;
        var received = new ConcurrentQueue<(string Text, int Thread)>();
        background.ChatReceived += line => received.Enqueue((line.Text, Environment.CurrentManagedThreadId));
        long tickBefore = rig.Server.Inspect(server => server.Zone.Tick);
        long snapshotsBefore = background.Stats.Snapshots;
        rig.Server.Execute("say " + duringStall);

        // Neither client Update runs here. The same live server and its unchanged 10-second timeout exercise both
        // modes in one real-time window. Sleeping only this caller models a render/resource stall, not a dead server.
        var pause = Stopwatch.StartNew();
        Thread.Sleep(TimeSpan.FromSeconds(12));
        pause.Stop();
        Assert.True(pause.Elapsed.TotalSeconds > 10, "client updates stopped longer than the production timeout");
        (long tickAfter, bool backgroundPresent, bool manualPresent) = rig.Server.Inspect(server =>
            (server.Zone.Tick, server.Players.Any(p => p.Name == background.Name), server.Players.Any(p => p.Name == manual.Name)));
        Assert.True(tickAfter > tickBefore, "the server continued ticking while both clients withheld updates");
        Assert.True(!manualPresent, "manual default timed out: the regression reproduces the old transport starvation");
        Assert.True(backgroundPresent, "background transport kept the same server session alive beyond the timeout");
        Assert.True(received.IsEmpty, "no application callback ran while the caller withheld PollEvents");
        rig.Clients.Remove(manual);
        manual.Dispose();

        Until(rig, () => received.Any(line => line.Text == duringStall), "queued server chat after caller resumes");
        Assert.Equal(ClientState.Playing, background.State, "same client still playing after the stall");
        Assert.True(background.HasCharacter && background.Stats.Snapshots > snapshotsBefore, "world snapshots resume on the same client");
        background.SendChat(afterStall);
        Until(rig, () => received.Any(line => line.Text == afterStall), "client chat round trip after the stall");
        Assert.Equal(1, received.Count(line => line.Text == duringStall), "queued server chat delivered exactly once");
        Assert.Equal(1, received.Count(line => line.Text == afterStall), "resumed chat delivered exactly once");
        Assert.True(received.All(line => line.Thread == caller), "all queued gameplay callbacks ran on the polling thread");

        background.Disconnect();
        Until(rig, () => background.State == ClientState.Disconnected &&
            rig.Server.Inspect(server => server.Players.All(p => p.Name != background.Name)), "explicit disconnect removes the server session");
        rig.Clients.Remove(background);
        background.Dispose();
        background.Dispose();

        // Reuse the player's name after disconnect, then dispose while connected without another client update.
        // The server must observe the close; transport shutdown must not need one last rendered frame.
        GameClient replacement = AddBackground(rig, "FrameStall");
        rig.WaitUntilPlaying();
        rig.Clients.Remove(replacement);
        replacement.Dispose();
        replacement.Dispose();
        Until(rig, () => rig.Server.Inspect(server => server.Players.All(p => p.Name != replacement.Name)),
            "disposing a connected client closes its session without further client updates");
        Fixtures.Report($"{pause.Elapsed.TotalSeconds:0.00}s update stall; manual control timed out, background resumed; " +
            $"{received.Count} callbacks on caller {caller}; disconnect and connected disposal removed both sessions");
    }

    private static GameClient AddBackground(LoopbackRig rig, string name)
    {
        var client = new GameClient(new ClientOptions { Name = name, BackgroundTransport = true });
        // Register ownership before connecting so a failed connection still releases the transport and physics.
        rig.Clients.Add(client);
        client.Connect("127.0.0.1", rig.Server.Port);
        return client;
    }

    private static void Until(LoopbackRig rig, Func<bool> done, string reason)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < 5, reason);
            rig.Run(0.05, (_, _) => default);
        }
    }
}
