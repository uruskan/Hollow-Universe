using System.Threading;
using System.Threading.Tasks;
using Hollow.Client.Streaming;
using Hollow.Surface;

namespace Hollow.Tests;

public static class SurfaceStreamingTests
{
    [Test]
    public static void FarScatterNeverSuppressesFullScatterAfterMovingAcrossFaces()
    {
        foreach (int level in new[] { 0, 5, TileKey.MaxLevel })
        {
            var requested = new ScatterRequestSet();
            int edge = (1 << level) - 1;
            // All far blocks arrive first. Full face0/1 used to alias far face4/5 because a mode bit overwrote face bits.
            for (int face = 0; face < CubeSphere.FaceCount; face++)
                Assert.True(requested.TryAdd(new TileKey(face, level, edge, edge), full: false), "first far request per face");
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                var tile = new TileKey(face, level, edge, edge);
                Assert.True(requested.TryAdd(tile, full: true), "near foliage remains requestable after far scatter: face " + face);
                Assert.True(!requested.TryAdd(tile, full: false), "far duplicate suppressed");
                Assert.True(!requested.TryAdd(tile, full: true), "near duplicate suppressed");
            }
        }
    }

    [Test]
    public static void ScatterRequestsDeduplicateConcurrentCallersWithoutMixingModes()
    {
        var requested = new ScatterRequestSet();
        var tile = new TileKey(5, 5, 17, 21);
        int full = 0, trees = 0;
        Parallel.For(0, 64, _ =>
        {
            if (requested.TryAdd(tile, full: true)) Interlocked.Increment(ref full);
            if (requested.TryAdd(tile, full: false)) Interlocked.Increment(ref trees);
        });
        Assert.Equal(1, full, "one full-scatter job");
        Assert.Equal(1, trees, "one trees-only job");
    }
}
