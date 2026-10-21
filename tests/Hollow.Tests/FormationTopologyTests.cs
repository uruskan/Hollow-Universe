using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationTopologyTests
{
    // Independently read from CubeSphere's six N/U/V bases. Columns are UMin, UMax, VMin, VMax;
    // an edge parameter increases along +V on U edges and +U on V edges.
    private static readonly (int Face, CubeEdge Edge, bool Reversed)[,] Expected =
    {
        { (4, CubeEdge.UMax, false), (5, CubeEdge.UMin, false), (3, CubeEdge.UMax, true),  (2, CubeEdge.UMax, false) },
        { (5, CubeEdge.UMax, false), (4, CubeEdge.UMin, false), (3, CubeEdge.UMin, false), (2, CubeEdge.UMin, true) },
        { (1, CubeEdge.VMax, true),  (0, CubeEdge.VMax, false), (4, CubeEdge.VMax, false), (5, CubeEdge.VMax, true) },
        { (1, CubeEdge.VMin, false), (0, CubeEdge.VMin, true),  (5, CubeEdge.VMin, true),  (4, CubeEdge.VMin, false) },
        { (1, CubeEdge.UMax, false), (0, CubeEdge.UMin, false), (3, CubeEdge.VMax, false), (2, CubeEdge.VMin, false) },
        { (0, CubeEdge.UMax, false), (1, CubeEdge.UMin, false), (3, CubeEdge.VMin, true),  (2, CubeEdge.VMax, true) },
    };

    [Test]
    public static void AllDirectedFaceEdgesHaveReciprocalOrientedTransitions()
    {
        for (int face = 0; face < 6; face++)
        for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
        {
            CubeEdge edge = (CubeEdge)edgeIndex;
            CubeEdgeTransition actual = CubeTopology.Transition(face, edge);
            var expected = Expected[face, edgeIndex];
            Assert.True(actual.IsValid, "every directed edge has a real transition");
            Assert.Equal(expected, (actual.Face, actual.Edge, actual.Reversed), $"face {face} edge {edge}: basis orientation");
            CubeEdgeTransition back = CubeTopology.Transition(actual.Face, actual.Edge);
            Assert.Equal((face, edge, actual.Reversed), (back.Face, back.Edge, back.Reversed), "reciprocal seam orientation");
            foreach (int coordinate in new[] { 0, 1, 4, 7, 8 })
            {
                int mapped = actual.Reversed ? 8 - coordinate : coordinate;
                Assert.Equal(BoundaryVertex(face, 3, edge, coordinate),
                    BoundaryVertex(actual.Face, 3, actual.Edge, mapped), "shared vertex and endpoint orientation agree");
            }
        }
    }

    [Test]
    public static void CompleteTileGraphsHaveReciprocalNeighborsAndNoAliasedEdges()
    {
        foreach (int level in new[] { 0, 1, 3 })
        {
            int n = 1 << level;
            var edgeUses = new Dictionary<CubeEdgeKey, int>();
            var vertexUses = new Dictionary<CubeVertexKey, int>();
            for (int face = 0; face < 6; face++)
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var tile = new TileKey(face, level, x, y);
                for (int corner = 0; corner < 4; corner++)
                {
                    CubeVertexKey vertex = CubeTopology.VertexKey(face, level, x + (corner & 1), y + (corner >> 1));
                    vertexUses[vertex] = vertexUses.GetValueOrDefault(vertex) + 1;
                }
                for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
                {
                    CubeEdge edge = (CubeEdge)edgeIndex;
                    TileKey neighbor = CubeTopology.Neighbor(tile, edge);
                    bool boundary = edge switch
                    {
                        CubeEdge.UMin => x == 0, CubeEdge.UMax => x == n - 1,
                        CubeEdge.VMin => y == 0, _ => y == n - 1,
                    };
                    CubeEdge returnEdge = boundary ? Expected[face, edgeIndex].Edge : (CubeEdge)(edgeIndex ^ 1);
                    Assert.Equal(tile, CubeTopology.Neighbor(neighbor, returnEdge), "crossing a shared edge and returning restores the tile");
                    Assert.True(neighbor != tile, "no edge is self-adjacent, including level zero");
                    if (!boundary)
                    {
                        int expectedX = x + (edge == CubeEdge.UMin ? -1 : edge == CubeEdge.UMax ? 1 : 0);
                        int expectedY = y + (edge == CubeEdge.VMin ? -1 : edge == CubeEdge.VMax ? 1 : 0);
                        Assert.Equal(new TileKey(face, level, expectedX, expectedY), neighbor, "interior adjacency is an exact one-cell step");
                    }
                    CubeEdgeKey key = CubeTopology.EdgeKey(tile, edge);
                    Assert.Equal(key, CubeTopology.EdgeKey(neighbor, returnEdge), "both owners name exactly the same undirected edge");
                    edgeUses[key] = edgeUses.GetValueOrDefault(key) + 1;
                }
            }
            Assert.Equal(12 * n * n, edgeUses.Count, "closed quad cube has exactly two owners per edge without aliasing");
            Assert.True(edgeUses.Values.All(uses => uses == 2), "every unique edge has exactly two incident tiles");
            Assert.Equal(6 * n * n + 2, vertexUses.Count, "Euler vertex count includes all face interiors, seams and corners");
            Assert.Equal(8, vertexUses.Values.Count(uses => uses == 3), "only the eight cube corners have three incident tiles");
            Assert.True(vertexUses.Values.All(uses => uses is 3 or 4), "all other vertices have four incident tiles");
        }
    }

    [Test]
    public static void BoundaryNeighborsRemainExactAtMaximumLevel()
    {
        foreach (int level in new[] { TileKey.MaxLevel, 1, 6, 0 })
        {
            int n = 1 << level;
            foreach (int along in new[] { 0, n / 2, n - 1 }.Distinct())
            for (int face = 0; face < 6; face++)
            for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
            {
                CubeEdge edge = (CubeEdge)edgeIndex;
                var expected = Expected[face, edgeIndex];
                TileKey tile = BoundaryTile(face, level, edge, along);
                int mapped = expected.Reversed ? n - 1 - along : along;
                TileKey neighbor = CubeTopology.Neighbor(tile, edge);
                Assert.Equal(BoundaryTile(expected.Face, level, expected.Edge, mapped), neighbor,
                    "boundary cell mapping uses n-1-index, even at maximum integer resolution");
                Assert.Equal(CubeTopology.EdgeKey(tile, edge), CubeTopology.EdgeKey(neighbor, expected.Edge),
                    "maximum-level seam endpoints do not overflow or alias");
                Assert.Equal(tile, CubeTopology.Neighbor(neighbor, expected.Edge), "maximum-level seam is reciprocal");
            }
        }
    }

    [Test]
    public static void VertexKeysUnifyAllEightCornersAndCoarseFineVertices()
    {
        var corners = new Dictionary<CubeVertexKey, int>();
        foreach (int level in new[] { 28, 0, 6, 1 })
        {
            int n = 1 << level;
            for (int face = 0; face < 6; face++)
            foreach (int x in new[] { 0, n })
            foreach (int y in new[] { 0, n })
            {
                CubeVertexKey key = CubeTopology.VertexKey(face, level, x, y);
                Assert.Equal(0, key.Level, "cube corners reduce to the same level-zero coordinates");
                Assert.True(Math.Abs(key.X) == 1 && Math.Abs(key.Y) == 1 && Math.Abs(key.Z) == 1,
                    "corner identities retain their three signs");
                corners[key] = corners.GetValueOrDefault(key) + 1;
            }
        }
        Assert.Equal(8, corners.Count, "all 24 face corners collapse to exactly eight cube corners at every level");
        Assert.True(corners.Values.All(uses => uses == 12), "each corner has three face representations at each of four levels");

        for (int face = 0; face < 6; face++)
        for (int y = 0; y <= 8; y++)
        for (int x = 0; x <= 8; x++)
        {
            CubeVertexKey coarse = CubeTopology.VertexKey(face, 3, x, y);
            foreach (int fineLevel in new[] { 5, 28, 3 })
            {
                int shift = fineLevel - 3;
                Assert.Equal(coarse, CubeTopology.VertexKey(face, fineLevel, x << shift, y << shift),
                    "refinement preserves every existing vertex's identity");
            }
        }
        CubeVertexKey north = CubeTopology.VertexKey(2, 28, 1 << 27, 1 << 27);
        CubeVertexKey south = CubeTopology.VertexKey(3, 1, 1, 1);
        Assert.Equal((0, 1, 0, 0), (north.X, north.Y, north.Z, north.Level), "north pole keeps its exact canonical axis");
        Assert.Equal((0, -1, 0, 0), (south.X, south.Y, south.Z, south.Level), "south pole does not alias north");

        CubeEdgeKey coarseEdge = CubeTopology.EdgeKey(new TileKey(0, 2, 0, 1), CubeEdge.UMin);
        CubeEdgeKey sameSpan = CubeTopology.EdgeKey(CubeTopology.VertexKey(0, 4, 0, 4), CubeTopology.VertexKey(0, 4, 0, 8));
        Assert.Equal(coarseEdge, sameSpan, "the same endpoints canonicalize independently of their input level");
        Assert.True(coarseEdge != CubeTopology.EdgeKey(new TileKey(0, 4, 0, 4), CubeEdge.UMin),
            "a refined subsegment does not alias the complete coarse edge");
    }

    [Test]
    public static void CanonicalOrderingIsIndependentOfQueryOrderAndThreads()
    {
        var queries = new List<(int Face, int Level, int X, int Y)>();
        foreach (int level in new[] { 0, 5, 1, 28, 2 })
        {
            int n = 1 << level;
            for (int face = 0; face < 6; face++)
            foreach (int x in new[] { 0, n / 2, n })
            foreach (int y in new[] { 0, n / 2, n }) queries.Add((face, level, x, y));
        }
        CubeVertexKey[] baseline = queries.Select(q => CubeTopology.VertexKey(q.Face, q.Level, q.X, q.Y)).ToArray();
        int[] order = Enumerable.Range(0, queries.Count).ToArray();
        var random = new Rng(0x544F504F);
        for (int i = order.Length - 1; i > 0; i--)
        {
            int swap = random.NextInt(i + 1);
            (order[i], order[swap]) = (order[swap], order[i]);
        }
        var parallel = new CubeVertexKey[queries.Count];
        Parallel.For(0, order.Length, new ParallelOptions { MaxDegreeOfParallelism = 2 }, i =>
        {
            int index = order[i];
            var query = queries[index];
            parallel[index] = CubeTopology.VertexKey(query.Face, query.Level, query.X, query.Y);
        });
        Assert.True(baseline.SequenceEqual(parallel), "shuffled concurrent vertex queries preserve exact values");
        CubeVertexKey[] forward = baseline.Distinct().OrderBy(key => key).ToArray();
        CubeVertexKey[] reversed = parallel.Reverse().Distinct().OrderBy(key => key).ToArray();
        Assert.True(forward.SequenceEqual(reversed), "canonical sorting is independent of insertion order");
        for (int i = 1; i < forward.Length; i++)
        {
            Assert.True(forward[i - 1].CompareTo(forward[i]) < 0 && forward[i].CompareTo(forward[i - 1]) > 0,
                "distinct vertices have an antisymmetric total integer order");
            CubeEdgeKey edge = CubeTopology.EdgeKey(forward[i - 1], forward[i]);
            CubeEdgeKey reverse = CubeTopology.EdgeKey(forward[i], forward[i - 1]);
            Assert.Equal(edge, reverse, "undirected endpoints sort canonically");
            Assert.Equal(edge.GetHashCode(), reverse.GetHashCode(), "equal edge values have equal dictionary hashes");
            Assert.True(edge.First.CompareTo(edge.Second) < 0, "edge endpoint order is explicit");
        }
    }

    [Test]
    public static void InvalidTopologyInputsAndDefaultKeysAreRejected()
    {
        foreach (int face in new[] { -1, 6, int.MaxValue })
        {
            Rejected(() => CubeTopology.Transition(face, CubeEdge.UMin));
            Rejected(() => CubeTopology.VertexKey(face, 0, 0, 0));
        }
        foreach (int level in new[] { -1, TileKey.MaxLevel + 1, int.MaxValue })
            Rejected(() => CubeTopology.VertexKey(0, level, 0, 0));
        foreach (int coordinate in new[] { -1, 9, int.MinValue, int.MaxValue })
        {
            Rejected(() => CubeTopology.VertexKey(0, 3, coordinate, 0));
            Rejected(() => CubeTopology.VertexKey(0, 3, 0, coordinate));
        }
        Rejected(() => CubeTopology.Transition(0, (CubeEdge)255));
        Rejected(() => CubeTopology.Neighbor(new TileKey(0, 0, 0, 0), (CubeEdge)255));
        Rejected(() => CubeTopology.EdgeKey(new TileKey(0, 0, 0, 0), (CubeEdge)255));
        CubeVertexKey valid = CubeTopology.VertexKey(0, 0, 0, 0);
        Rejected(() => CubeTopology.EdgeKey(default, valid));
        Rejected(() => CubeTopology.EdgeKey(valid, default));
        Rejected(() => CubeTopology.EdgeKey(valid, valid));
        Assert.True(!default(CubeVertexKey).IsValid && !default(CubeEdgeKey).IsValid && !default(CubeEdgeTransition).IsValid,
            "default value structs cannot masquerade as validated topology values");
        Assert.True(valid.IsValid && valid != default, "a real corner never aliases the default vertex");
    }

    private static CubeVertexKey BoundaryVertex(int face, int level, CubeEdge edge, int along)
    {
        int n = 1 << level;
        return edge switch
        {
            CubeEdge.UMin => CubeTopology.VertexKey(face, level, 0, along),
            CubeEdge.UMax => CubeTopology.VertexKey(face, level, n, along),
            CubeEdge.VMin => CubeTopology.VertexKey(face, level, along, 0),
            CubeEdge.VMax => CubeTopology.VertexKey(face, level, along, n),
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };
    }

    private static TileKey BoundaryTile(int face, int level, CubeEdge edge, int along)
    {
        int last = (1 << level) - 1;
        return edge switch
        {
            CubeEdge.UMin => new TileKey(face, level, 0, along),
            CubeEdge.UMax => new TileKey(face, level, last, along),
            CubeEdge.VMin => new TileKey(face, level, along, 0),
            CubeEdge.VMax => new TileKey(face, level, along, last),
            _ => throw new ArgumentOutOfRangeException(nameof(edge)),
        };
    }

    private static void Rejected(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new TestFailure("invalid topology input must fail explicitly");
    }
}
