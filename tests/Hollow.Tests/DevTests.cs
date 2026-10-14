using System;
using System.Linq;
using Hollow.Client;
using Hollow.Net.Protocol;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>The F2 developer panel's server side (GROUND.md §8.5), over the network.</summary>
public static class DevTests
{
    /// <summary>
    /// A watching admin gets the stats every half second (the tick rate, every section of the profile, everyone on), a
    /// target asked about described (the server's facts, then the object's own members), and the log; a player who
    /// isn't an admin asks the same and gets nothing.
    /// </summary>
    [Test]
    public static void TheServerAnswersAWatchingAdmin()
    {
        using var rig = new LoopbackRig(admins: new[] { "Ada" });
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        foreach (GameClient client in new[] { ada, bo })
        {
            client.Dev().WantLog = true;
            client.Dev().Watch(true);
        }
        RunUntil(rig, () => ada.Dev().Server != null, "stats at Ada");
        DevStats stats = ada.Dev().Server!.Value;
        Assert.True(stats.TickRate == 30 && stats.Players.Length == 2, "the stats say the tick rate and both players");
        Assert.True(ada.Dev().Sections.Count == TickProfile.Tree.Length, "every section of the profile is in the stats");
        Assert.True(ada.Dev().Players.Any(p => p.Name == "Bo" && p.State == DevPlayerState.Alive && !p.Admin), "Bo is listed, alive, no admin");
        Assert.True(ada.Dev().Players.Any(p => p.Name == "Ada" && p.Admin), "Ada is listed as the admin");
        Assert.True(stats.Characters == 2 && stats.Tiles > 0, "the counts: two characters in the zone, ground under them");

        ada.Dev().Inspect(new TargetRef(TargetFamily.Character, bo.EntityId.Value));
        RunUntil(rig, () => ada.Dev().Inspected.Family == TargetFamily.Character, "Bo described");
        Assert.True(ada.Dev().Lines.Any(l => l.Label == "Object" && l.Value == "Player"), "the description names the object's type");
        Assert.True(ada.Dev().Lines.Any(l => l.Label == "Name" && l.Value == "Bo"), "and his name, from the object's own members");
        Assert.True(ada.Dev().Lines.Any(l => l.Label == "Verbs for you"), "and the verbs the rules offer Ada on him");
        RunUntil(rig, () => ada.Dev().Log.Count > 0, "the log at Ada");
        Assert.True(ada.Dev().Log.Any(l => l.Contains("watches the server", StringComparison.Ordinal)), "the log has the watch line");

        rig.Run(1.5, (_, _) => default);
        Assert.True(bo.Dev().Server == null && bo.Dev().Log.Count == 0, "Bo, no admin, gets nothing");
        Fixtures.Report($"stats: tick {stats.Tick}, {stats.AverageMs:0.00} ms average, {stats.Sections.Length} sections, {stats.Players.Length} players; " +
            $"{ada.Dev().Lines.Count} lines on Bo; {ada.Dev().Log.Count} log lines");
    }

    private static void RunUntil(LoopbackRig rig, Func<bool> done, string what, double seconds = 6)
    {
        double left = seconds;
        while (!done() && left > 0)
        {
            rig.Run(0.1, (_, _) => default);
            left -= 0.1;
        }
        Assert.True(done(), what);
    }
}
