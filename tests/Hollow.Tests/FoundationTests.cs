using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hollow.Simulation;
using Hollow.Simulation.Content;

namespace Hollow.Tests;

/// <summary>The ground game's shared foundations (GROUND_SYSTEMS.md §3, package P0), engine-free.</summary>
public static class FoundationTests
{
    /// <summary>
    /// GROUND_SYSTEMS.md §3.2: one answer vocabulary. Every code has a line in texts.json, a line names its answer's
    /// detail (an item, a number), and a texts file that misses a code, names one that doesn't exist or uses a placeholder
    /// nobody fills is reported, while it stays out of the data's hash.
    /// </summary>
    [Test]
    public static void EveryAnswerHasALine()
    {
        GameContent c = ContentFixture.Content;
        Texts texts = c.Texts;
        foreach (OutcomeCode code in Enum.GetValues<OutcomeCode>().Where(code => code != OutcomeCode.Done))
        {
            Assert.True(texts.Line(code).Length > 0, $"{code} has a line");
        }
        Assert.Equal("", texts.Line(Outcome.Done), "done says nothing");
        Assert.Equal("Not enough wood", texts.Line(OutcomeCode.NotEnough.With(c.ItemByKey("wood").Id)), "an item, in lower case");
        Assert.Equal("Not enough resources", texts.Line(OutcomeCode.NotEnough), "the word for none");
        Assert.Equal("You need a hammer", texts.Line(OutcomeCode.NoTool.With(c.ItemByKey("hammer").Id)), "a tool");
        Assert.Equal("Wait 5 s before trying another code", texts.Line(OutcomeCode.WaitForCode.With(5)), "a number");
        Assert.Equal("Needs a level 2 workbench nearby", texts.Line(OutcomeCode.NeedsWorkbench.With(2)), "a level");
        Assert.True(OutcomeCode.Stale == new Outcome(OutcomeCode.Stale, 3) && !new Outcome(OutcomeCode.Stale).Ok && Outcome.Done.Ok,
            "an answer is its code, whatever its detail");
        Assert.True((int)Enum.GetValues<OutcomeCode>().Max() < 128, "the codes fit IntentResult's 7 bits");

        Dictionary<string, string> files = GameContent.Files.Concat(GameContent.Unhashed)
            .ToDictionary(f => f, f => File.ReadAllText(Path.Combine(ContentFixture.Folder, f)));
        ulong hash = GameContent.FromTexts(files).Hash;
        files[Texts.File] = files[Texts.File]
            .Replace("\"Stale\": \"That item has moved\",", "\"Stail\": \"That item has moved\",", StringComparison.Ordinal)
            .Replace("\"Wait {n} s before", "\"Wait {seconds} s before", StringComparison.Ordinal);
        GameContent broken = GameContent.FromTexts(files);
        string all = string.Join("\n", broken.Problems);
        Fixtures.Report($"{broken.Problems.Count} problems, e.g. {broken.Problems.FirstOrDefault()}");
        Assert.True(all.Contains("outcomes.Stail: no such answer code", StringComparison.Ordinal), "a line for no code");
        Assert.True(all.Contains("the answer Stale has no line", StringComparison.Ordinal), "a code without a line");
        Assert.True(all.Contains("unknown placeholder {seconds}", StringComparison.Ordinal), "a placeholder nobody fills");
        Assert.Equal(hash, broken.Hash, "texts aren't in the data's hash: a translated client may join any server");
    }
}
