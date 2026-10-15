using System;
using System.Linq;
using Hollow.Galaxy;
using Hollow.Galaxy.Systems;

namespace Hollow.Tests;

public static class PlayableGalaxyTests
{
    private static readonly Lazy<PlayableGalaxy> Full = new(() => new PlayableGalaxy(new Galaxy.Galaxy(null)));

    [Test]
    public static void StarBudgetIncludesEveryCompanion()
    {
        PlayableGalaxy galaxy = Full.Value;
        Assert.Equal(1000, galaxy.StarCount, "default total stellar-body budget");
        Assert.Equal(1000, galaxy.Systems.Sum(entry => entry.StarCount), "companions count toward cap");
        Assert.True(galaxy.Systems.Count <= 1000, "system count cannot exceed star count");
        Assert.Equal(galaxy.Systems.Count, galaxy.Systems.Select(entry => entry.SourceStar.Address).Distinct().Count(), "unique identities");
        Assert.Equal(galaxy.Systems.Count, galaxy.Systems.Select(entry => entry.MapStar.Position).Distinct().Count(), "distinct diagram positions");
        Assert.True(galaxy.Systems.All(entry => entry.MapStar.Position.Y == 0), "compact planar map");
        foreach (PlayableGalaxy.Entry entry in galaxy.Systems.Take(24))
        {
            StarSystem system = galaxy.GetSystem(entry.SourceStar.Address)!;
            Assert.Equal(entry.StarCount, system.Bodies.Count(body => body.Kind == BodyKind.Star), "budget reflects actual generated stars");
        }
        Fixtures.Report($"{galaxy.Systems.Count} systems contain exactly {galaxy.StarCount} stars, including companions");
    }

    [Test]
    public static void RegistryIsStableWithoutCatalogueAndKeepsOriginalGeneration()
    {
        var withoutCatalogue = new PlayableGalaxy(new Galaxy.Galaxy(null), 32);
        var withCatalogue = new PlayableGalaxy(Fixtures.Galaxy, 32);
        Assert.Equal(withoutCatalogue.Systems.Count, withCatalogue.Systems.Count, "optional catalogue does not change count");
        for (int i = 0; i < withoutCatalogue.Systems.Count; i++)
        {
            PlayableGalaxy.Entry a = withoutCatalogue.Systems[i], b = withCatalogue.Systems[i];
            Assert.Equal(a.SourceStar.Address, b.SourceStar.Address, "stable selection");
            Assert.Equal(a.MapStar.Position, b.MapStar.Position, "stable diagram");
            if (a.SourceStar.Address.Equals(SolSystem.Address)) continue;
            StarSystem expected = Fixtures.Galaxy.GetSystem(a.SourceStar.Address)!;
            StarSystem actual = withoutCatalogue.GetSystem(a.SourceStar.Address)!;
            Assert.Equal(expected.Name, actual.Name, "original system identity");
            Assert.Equal(expected.Bodies.Count, actual.Bodies.Count, "original system bodies");
            for (int body = 0; body < expected.Bodies.Count; body++)
            {
                Assert.Equal(expected.Bodies[body].Radius, actual.Bodies[body].Radius, "original physical radius");
                Assert.Equal(expected.Bodies[body].Mass, actual.Bodies[body].Mass, "original physical mass");
                Assert.Equal(expected.Bodies[body].Orbit, actual.Bodies[body].Orbit, "original orbit");
            }
        }
    }

    [Test]
    public static void SearchAndLookupStayInsideThePlayableRegistry()
    {
        var galaxy = new PlayableGalaxy(new Galaxy.Galaxy(null), 1);
        Assert.Equal(1, galaxy.Systems.Count, "one-star budget includes only Sol");
        Assert.Equal(SolSystem.Address, galaxy.Search("Earth", 8).Single().Address, "Earth search opens Sol");
        Assert.True(galaxy.GetSystem(SolSystem.Address)!.Bodies.Any(body => body.Name == "Earth"), "Sol resolves Earth");
        var excluded = Full.Value.Systems[1].SourceStar.Address;
        Assert.True(!galaxy.Contains(excluded) && galaxy.GetSystem(excluded) == null && !galaxy.TryGetStar(excluded, out _),
            "existing source systems outside the selected population are inaccessible");
        Assert.Equal(0, galaxy.Search("Sol", 0).Count, "zero results requested");
        foreach (int bad in new[] { 0, -1, 1001 })
        {
            bool rejected = false;
            try { _ = new PlayableGalaxy(new Galaxy.Galaxy(null), bad); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert.True(rejected, "invalid star budget rejected");
        }
    }
}
