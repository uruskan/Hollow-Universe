using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class FormationTerrainSealTests
{
    [Test]
    public static void ContinuousSealCoversSharedFacetsAndIncludesTheBurialBoundaryExactly()
    {
        TerrainSealTriangle[] terrain = Rectangle(-5, 5, -5, 5);
        Vector3D[] loop = Loop(9);
        FormationTerrainSealResult sealedLoop = FormationTerrainSeal.Evaluate(loop, terrain, 1);
        Assert.True(sealedLoop.IsSealed, "The whole loop has the exact requested top-plane burial.");
        Assert.Equal(4, sealedLoop.Diagnostics.CompletedSegments, "All four closed-loop edges are covered.");
        Assert.Equal(8, sealedLoop.Diagnostics.SegmentTriangleAttempts, "Each edge tests both supplied triangles.");
        Assert.Equal(2, sealedLoop.Dependencies.Length, "Both halves are required to cover the square.");
        Assert.Equal(-1, sealedLoop.FailedSegment, "A complete seal has no failed edge.");

        // The entire closed line lies on the two triangles' shared radial facet, not just sampled endpoints.
        Vector3D[] onSharedFacet = { new(-1, 9, -1), new(0, 9, 0), new(1, 9, 1) };
        FormationTerrainSealResult shared = FormationTerrainSeal.Evaluate(onSharedFacet, terrain, 1);
        Assert.True(shared.IsSealed, "Closed half-spaces must meet without an epsilon crack.");
        Assert.Equal(1, shared.Dependencies.Length, "Equal coverage chooses one canonical primitive deterministically.");
        Assert.Equal(terrain[0].Primitive, shared.Dependencies[0], "The canonical first primitive wins an exact shared-facet tie.");
        Rejected(FormationTerrainSeal.Evaluate(Loop(Math.BitIncrement(9)), terrain, 1),
            FormationTerrainSealRejection.IncompleteCoverage);
    }

    [Test]
    public static void ContinuousSealRejectsTheGapHiddenBetweenVerticesAndMidpoints()
    {
        var terrain = new List<TerrainSealTriangle>();
        terrain.AddRange(Rectangle(-10, 2, -10, 10));
        terrain.AddRange(Rectangle(2.4, 10, -10, 10, firstPrimitive: 2));
        Vector3D[] loop = { new(-3, 5, -1), new(3, 5, -1), new(3, 5, 1), new(-3, 5, 1) };
        FormationTerrainSealResult result = FormationTerrainSeal.Evaluate(loop, terrain.ToArray(), 0);
        Rejected(result, FormationTerrainSealRejection.IncompleteCoverage);
        Assert.Equal(0, result.FailedSegment, "The first edge crosses the hidden gap.");
        Assert.Equal(0, result.Diagnostics.CompletedSegments, "The first edge cannot be admitted as covered.");
        // Radial projection doubles X on the y=10 top. Each old vertex/midpoint probe lies outside (2,2.4).
        for (int i = 0; i < loop.Length; i++)
        {
            double a = 2 * loop[i].X, midpoint = loop[i].X + loop[(i + 1) % loop.Length].X;
            Assert.True(a <= 2 || a >= 2.4, "Every old vertex probe misses the hidden gap.");
            Assert.True(midpoint <= 2 || midpoint >= 2.4, "Every old midpoint probe misses the hidden gap.");
        }
    }

    [Test]
    public static void ContinuousSealChecksTheClosingEdgeAndPublishesNoPartialDependencies()
    {
        var terrain = new List<TerrainSealTriangle>();
        terrain.AddRange(Rectangle(-6, -2, -6, 6));
        terrain.AddRange(Rectangle(-6, 6, -6, -2, firstPrimitive: 2));
        Vector3D[] loop = { new(-2, 5, 2), new(-2, 5, -2), new(2, 5, -2) };
        FormationTerrainSealResult result = FormationTerrainSeal.Evaluate(loop, terrain.ToArray(), 0);
        Rejected(result, FormationTerrainSealRejection.IncompleteCoverage);
        Assert.Equal(2, result.FailedSegment, "Only the implicit closing edge crosses the missing quadrant.");
        Assert.Equal(2, result.Diagnostics.CompletedSegments, "The first two edges were covered before the closing edge failed.");
        Assert.True(result.Diagnostics.DependencyTriangles > 0, "Provisional work was performed before rejection.");
        Assert.Equal(0, result.Dependencies.Length, "Provisional dependencies never escape as a usable result.");
    }

    [Test]
    public static void ContinuousSealRotatedPlanesUseTheDocumentedConservativeL1Burial()
    {
        var centre = new Vector3D(10, 10, 10);
        var u = new Vector3D(1, -1, 0);
        var v = new Vector3D(1, 1, -2);
        Vector3D a = centre - 4 * u - 4 * v, b = centre + 4 * u - 4 * v;
        Vector3D c = centre + 4 * u + 4 * v, d = centre - 4 * u + 4 * v;
        TerrainSealTriangle[] terrain = { Triangle(0, a, b, c), Triangle(1, a, c, d) };
        Vector3D[] PlaneLoop(double coordinate)
        {
            var p = new Vector3D(coordinate, coordinate, coordinate);
            return new[] { p - 0.125 * u - 0.125 * v, p + 0.125 * u - 0.125 * v,
                p + 0.125 * u + 0.125 * v, p - 0.125 * u + 0.125 * v };
        }
        Assert.True(FormationTerrainSeal.Evaluate(PlaneLoop(9), terrain, 1).IsSealed,
            "The L1 offset for an equal-component normal is exactly one metre along every coordinate.");
        Rejected(FormationTerrainSeal.Evaluate(PlaneLoop(9.25), terrain, 1), FormationTerrainSealRejection.IncompleteCoverage);
        Assert.True(0.75 * Math.Sqrt(3) > 1,
            "The rejected loop has over one metre of perpendicular clearance: L1 deliberately demands more on this rotation.");
    }

    [Test]
    public static void ContinuousSealRejectsInwardDegenerateAndOriginFacingFailures()
    {
        TerrainSealTriangle[] terrain = Rectangle(-5, 5, -5, 5);
        TerrainSealTriangle first = terrain[0];
        terrain[0] = first with { B = first.C, C = first.B };
        Rejected(FormationTerrainSeal.Evaluate(Loop(5), terrain, 0), FormationTerrainSealRejection.InvalidTriangle);
        terrain[0] = first with { B = first.A };
        Rejected(FormationTerrainSeal.Evaluate(Loop(5), terrain, 0), FormationTerrainSealRejection.InvalidTriangle);
        terrain[0] = Triangle(0, new(1, 0, 0), new(0, 1, 0), new(1, 1, 0));
        Rejected(FormationTerrainSeal.Evaluate(Loop(5), terrain, 0), FormationTerrainSealRejection.InvalidTriangle);
        terrain[0] = first with { Primitive = first.Primitive with { B = first.Primitive.A } };
        Rejected(FormationTerrainSeal.Evaluate(Loop(5), terrain, 0), FormationTerrainSealRejection.InvalidTriangle);
    }

    [Test]
    public static void ContinuousSealShuffledPrimitivesProduceOnlyTheSameUsedDependencies()
    {
        var terrain = new List<TerrainSealTriangle>(Rectangle(-5, 5, -5, 5));
        terrain.AddRange(Rectangle(20, 25, 20, 25, firstPrimitive: 2));
        terrain.AddRange(Rectangle(-5, 5, -5, 5, firstPrimitive: 4)); // Geometric ties with different valid identities.
        TerrainSealTriangle[] first = terrain.ToArray();
        FormationTerrainSealResult a = FormationTerrainSeal.Evaluate(Loop(5), first, 1);
        Array.Reverse(first);
        FormationTerrainSealResult b = FormationTerrainSeal.Evaluate(Loop(5), first, 1);
        Assert.True(a.IsSealed && b.IsSealed, "Both input orders produce a complete seal.");
        Assert.True(a.Dependencies.SequenceEqual(b.Dependencies), "Dependency order is independent of input order.");
        Assert.Equal(2, a.Dependencies.Length, "Unvisited far geometry and redundant ties are not dependencies.");
        Assert.Equal(terrain[0].Primitive, a.Dependencies[0], "The first canonical half wins its redundant geometric tie.");
        Assert.Equal(terrain[1].Primitive, a.Dependencies[1], "The second canonical half wins its redundant geometric tie.");
        Assert.Equal(a.Diagnostics, b.Diagnostics, "Input order cannot affect bounded arithmetic or dependency accounting.");
    }

    [Test]
    public static void ContinuousSealAllCapacityPathsReturnNoPartialDependencies()
    {
        TerrainSealTriangle[] terrain = Rectangle(-5, 5, -5, 5);
        var normal = new FormationTerrainSealLimits();
        FormationTerrainSealLimits[] small =
        {
            normal with { MaxLoopVertices = 2 }, normal with { MaxTerrainTriangles = 1 },
            normal with { MaxSegmentTriangleAttempts = 0 }, normal with { MaxExactArithmeticOperations = 0 },
            normal with { MaxIntegerBits = 1 }, normal with { MaxDependencyTriangles = 0 },
            normal with { MaxEstimatedScratchBytes = 0 }, normal with { MaxEstimatedResultBytes = 0 },
        };
        foreach (FormationTerrainSealLimits limits in small)
            Capacity(FormationTerrainSeal.Evaluate(Loop(5), terrain, 1, limits));
        FormationTerrainSealResult afterFirstEdge = FormationTerrainSeal.Evaluate(Loop(5), terrain, 1,
            normal with { MaxSegmentTriangleAttempts = 2 });
        Capacity(afterFirstEdge);
        Assert.Equal(1, afterFirstEdge.Diagnostics.CompletedSegments, "The attempt budget permits exactly the first edge.");
        Assert.Equal(2, afterFirstEdge.Diagnostics.SegmentTriangleAttempts, "Attempt accounting stops at the configured cap.");
        Assert.True(afterFirstEdge.Diagnostics.DependencyTriangles > 0, "A late capacity refusal discards provisional dependencies.");
        FormationTerrainSealResult exactBudget = FormationTerrainSeal.Evaluate(Loop(5), terrain, 1,
            normal with { MaxSegmentTriangleAttempts = 8 });
        Assert.True(exactBudget.IsSealed, "The exact final attempt is admitted; no off-by-one budget refusal.");
    }

    [Test]
    public static void ContinuousSealCancellationAndMalformedInputsFailClosed()
    {
        TerrainSealTriangle[] terrain = Rectangle(-5, 5, -5, 5);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        FormationTerrainSealResult stopped = FormationTerrainSeal.Evaluate(Loop(5), terrain, 1, cancellation: cancelled.Token);
        Assert.Equal(FormationQueryStatus.Cancelled, stopped.Status, "A cancelled request reports cancellation explicitly.");
        Assert.True(!stopped.IsSealed, "Cancellation cannot produce a seal.");
        Assert.Equal(0, stopped.Dependencies.Length, "Cancellation publishes no dependency set.");
        Assert.Equal(0, stopped.Diagnostics.ExactArithmeticOperations, "Pre-cancellation prevents arithmetic work.");
        Vector3D[] loop = Loop(5);
        loop[3] = loop[0];
        Rejected(FormationTerrainSeal.Evaluate(loop, terrain, 0), FormationTerrainSealRejection.InvalidLoop);
        loop[3] = new Vector3D(double.NaN, 1, 1);
        Rejected(FormationTerrainSeal.Evaluate(loop, terrain, 0), FormationTerrainSealRejection.InvalidLoop);
        loop[3] = new Vector3D(FormationTerrainSeal.MaximumCoordinateMetres + 1, 1, 1);
        Rejected(FormationTerrainSeal.Evaluate(loop, terrain, 0), FormationTerrainSealRejection.InvalidLoop);
        Rejected(FormationTerrainSeal.Evaluate(Loop(5), terrain, -1), FormationTerrainSealRejection.InvalidLoop);
        Rejected(FormationTerrainSeal.Evaluate(Loop(5), terrain, double.NaN), FormationTerrainSealRejection.InvalidLoop);
        Rejected(FormationTerrainSeal.Evaluate(Loop(5), Array.Empty<TerrainSealTriangle>(), 0),
            FormationTerrainSealRejection.IncompleteCoverage);
        terrain[1] = terrain[0];
        Rejected(FormationTerrainSeal.Evaluate(Loop(5), terrain, 0), FormationTerrainSealRejection.DuplicatePrimitive);
        Throws<FormationValidationException>(() => FormationTerrainSeal.Evaluate(Loop(5), terrain, 0,
            new FormationTerrainSealLimits { MaxSegmentTriangleAttempts = FormationTerrainSeal.MaximumAttempts + 1 }));
    }

    [Test]
    public static void ContinuousSealPreservesSubnormalWidthWithoutFloatingSidePlaneGaps()
    {
        double unit = double.Epsilon;
        TerrainSealTriangle[] terrain = Rectangle(-8 * unit, 8 * unit, -2, 2);
        Vector3D[] loop = { new(-unit, 5, -0.25), new(unit, 5, -0.25), new(unit, 5, 0.25), new(-unit, 5, 0.25) };
        FormationTerrainSealResult result = FormationTerrainSeal.Evaluate(loop, terrain, 1);
        Assert.True(result.IsSealed, "Exact dyadic predicates retain even subnormal-width positive radial tetrahedra.");
        Assert.True(result.Diagnostics.PeakIntegerBits > 2048, "The fixture exercises the bounded wide-integer path.");
        Assert.True(result.Diagnostics.PeakIntegerBits <= FormationTerrainSeal.MaximumIntegerBits,
            "Even subnormal-width exact geometry remains within the hard integer bit cap.");
        Capacity(FormationTerrainSeal.Evaluate(loop, terrain, 1, new FormationTerrainSealLimits { MaxIntegerBits = 2048 }));
    }

    private static Vector3D[] Loop(double y) => new[]
    { new Vector3D(-1, y, -1), new Vector3D(1, y, -1), new Vector3D(1, y, 1), new Vector3D(-1, y, 1) };

    // These predicate fixtures attach valid canonical labels to explicit synthetic top planes. Authenticating
    // the positions against a frozen world/lattice is intentionally the producing world's responsibility.
    private static TerrainSealTriangle Triangle(int primitive, Vector3D a, Vector3D b, Vector3D c)
    {
        var lattice = new TerrainLattice(10, 0);
        TerrainLatticeTriangle id = lattice.Triangle(new TileKey(2, lattice.VertexLevel, primitive / 2, 0), primitive & 1);
        return new TerrainSealTriangle(id, a, b, c);
    }

    private static TerrainSealTriangle[] Rectangle(double x0, double x1, double z0, double z1, int firstPrimitive = 0)
    {
        var a = new Vector3D(x0, 10, z0); var b = new Vector3D(x0, 10, z1);
        var c = new Vector3D(x1, 10, z1); var d = new Vector3D(x1, 10, z0);
        return new[] { Triangle(firstPrimitive, a, b, c), Triangle(firstPrimitive + 1, a, c, d) };
    }

    private static void Rejected(FormationTerrainSealResult result, FormationTerrainSealRejection reason)
    {
        Assert.Equal(FormationQueryStatus.Complete, result.Status, "The predicate completed with a geometric or input rejection.");
        Assert.Equal(reason, result.Rejection, "The rejection reason matches the failing condition.");
        Assert.True(!result.IsSealed, "A rejected predicate cannot publish a seal.");
        Assert.Equal(0, result.Dependencies.Length, "Rejection discards every provisional dependency.");
    }

    private static void Capacity(FormationTerrainSealResult result)
    {
        Assert.Equal(FormationQueryStatus.CapacityExceeded, result.Status, "The exhausted bound is reported as incomplete work.");
        Assert.True(!result.IsSealed, "Exhausting capacity cannot publish a seal.");
        Assert.Equal(0, result.Dependencies.Length, "Capacity exhaustion discards every provisional dependency.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
