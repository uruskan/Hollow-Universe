using System;
using System.Linq;
using Hollow.Surface;

namespace Hollow.Tests;

public static class BiomePartitionTests
{
    [Test]
    public static void IceAndAlpineKeepTheirIntendedSharesAcrossClimateAndHeightBoundaries()
    {
        // Explicit partition expectations, including overlapping transition midpoints. Renormalizing
        // all outputs would satisfy the total but incorrectly reduce the intended ice/alpine shares.
        var cases = new (string Name, double Temperature, double TreeLine, double Height,
            double IceTemperature, double Ice, double Alpine, double Land)[]
        {
            ("below cold edge", -2, 0, 1000, 0, 1, 0, 0),
            ("cold edge", -1.5, 0, 1000, 0, 1, 0, 0),
            ("below alpine rise", 0, 0.5, 599, 0, 0.5, 0, 0.5),
            ("alpine rise begins", 0, 0.5, 600, 0, 0.5, 0, 0.5),
            ("both midpoints", 0, 0.5, 1000, 0, 0.5, 0.125, 0.375),
            ("alpine rise ends", 0, 0.5, 1400, 0, 0.5, 0.25, 0.25),
            ("above alpine rise", 0, 0.5, 1401, 0, 0.5, 0.25, 0.25),
            ("warm edge", 1.5, 1, 1000, 0, 0, 0, 1),
            ("above warm edge", 2, 1, 1000, 0, 0, 0, 1),
            ("treeless ice overlap", -1.5, 0, 1000, -1.5, 0.5, 0.25, 0.25),
            ("ice and alpine fill ground", -1.5, 0, 1400, -1.5, 0.5, 0.5, 0),
            ("alpine without ice", -1.5, 0, 1400, -3, 0, 1, 0),
            ("partial alpine without ice", 0, 0.5, 1000, -3, 0, 0.25, 0.75),
        };
        foreach (var c in cases)
        {
            double[] weights = Weights(new ClimateSample(c.Temperature, 0.5, c.TreeLine), c.Height, c.IceTemperature);
            AssertPartition(weights, c.Ice, c.Alpine, c.Land, c.Name);
        }
        Fixtures.Report($"{cases.Length} finite normalized climate/height partitions with fixed ice/alpine shares");
    }

    [Test]
    public static void OrdinaryWarmClimateKeepsItsMixtureAcrossAltitudeAndIceThresholds()
    {
        // At the grassland climate centre, full tree-line support prevents alpine ground. All
        // thresholds below this temperature leave ice absent, preserving the ordinary mixture.
        var climate = new ClimateSample(12, 0.4, 1);
        double[] control = Weights(climate, 0, -20);
        AssertPartition(control, 0, 0, 1, "warm lowland control");
        Assert.Equal(Biome.Grassland, BiomeTable.Dominant(control), "ordinary warm grassland stays dominant");
        for (int b = (int)Biome.Tundra; b <= (int)Biome.TropicalForest; b++)
            Assert.True(control[b] > 0, "ordinary climate keeps its blended biome distribution");

        foreach (double height in new[] { 0.0, 600, 1000, 1400, 2000 })
        foreach (double iceTemperature in new[] { -20.0, 0 })
        {
            double[] actual = Weights(climate, height, iceTemperature);
            AssertPartition(actual, 0, 0, 1, "ordinary warm control");
            Assert.True(control.SequenceEqual(actual), "absent ice/alpine leave every ordinary biome weight unchanged");
        }
    }

    private static double[] Weights(ClimateSample climate, double height, double iceTemperature)
    {
        var weights = new double[BiomeTable.Count];
        Array.Fill(weights, double.NaN); // the shared function must write its complete output
        BiomeTable.Weights(climate, height, iceTemperature, weights);
        return weights;
    }

    private static void AssertPartition(double[] weights, double ice, double alpine, double land, string context)
    {
        foreach (double weight in weights)
            Assert.True(double.IsFinite(weight) && weight >= 0 && weight <= 1, context + ": finite bounded weights");
        Assert.Near(1, weights.Sum(), 1e-12, context + ": normalized total");
        Assert.Near(ice, weights[(int)Biome.IceSheet], 1e-12, context + ": intended ice share");
        Assert.Near(alpine, weights[(int)Biome.Alpine], 1e-12, context + ": intended alpine share");
        double ordinary = 0;
        for (int b = (int)Biome.Tundra; b <= (int)Biome.TropicalForest; b++) ordinary += weights[b];
        Assert.Near(land, ordinary, 1e-12, context + ": remaining ordinary-land share");
        Assert.Equal(0.0, weights[(int)Biome.Ocean], context + ": land table does not add ocean");
        Assert.Equal(0.0, weights[(int)Biome.Beach], context + ": caller owns the beach transition");
    }
}
