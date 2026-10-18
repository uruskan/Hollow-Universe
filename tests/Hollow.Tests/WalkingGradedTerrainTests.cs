using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>Finite exclusion geometry and the finalized, raw-field-only walking adapter.</summary>
public static class WalkingGradedTerrainTests
{
    [Test]
    public static void RadialSiteProtectsTheWholeRayRatherThanOneHeightSlab()
    {
        FormationProtectedSite site = FormationProtectedSite.RadialFootprint("pad", Vector3D.UnitZ, 0.2);
        Vector3D inside = DirectionAtChord(Vector3D.UnitZ, Vector3D.UnitX, 0.15);
        Vector3D outside = DirectionAtChord(Vector3D.UnitZ, Vector3D.UnitX, 0.21);
        foreach (double radius in new[] { 0.01, 10.0, 1200.0, 1_000_000.0 })
        {
            Assert.True(site.Intersects(Point(inside * radius)), "The same footprint is protected at every positive radial height.");
            Assert.True(!site.Intersects(Point(outside * radius)), "A separated angular direction is not inside the footprint.");
            Assert.True(!site.Intersects(Point(-Vector3D.UnitZ * radius)), "A narrow cone never becomes an infinite cylinder behind the planet.");
        }
    }

    [Test]
    public static void RadialSiteUsesChordDistanceAndIncludesItsBoundary()
    {
        FormationProtectedSite site = FormationProtectedSite.RadialFootprint("chord", Vector3D.UnitZ, 1);
        // Angle 1.02 radians lies outside a mistaken one-radian arc cap, but its chord is less than one.
        var betweenArcAndChord = new Vector3D(Math.Sin(1.02), 0, Math.Cos(1.02));
        Assert.True((betweenArcAndChord - Vector3D.UnitZ).Length < 1, "The independent point lies inside the original chord comparison.");
        Assert.True(site.Intersects(Point(betweenArcAndChord * 100)), "Chord metres are not interpreted as angular radians.");
        Assert.True(site.Intersects(Point(new Vector3D(Math.Sqrt(3) * 0.5, 0, 0.5) * 100)), "The exact sixty-degree boundary is conservatively included.");
        Assert.True(!site.Intersects(Point(DirectionAtChord(Vector3D.UnitZ, Vector3D.UnitX, 1.000001) * 100)), "A resolvably separated chord is excluded.");
    }

    [Test]
    public static void RadialSiteHandlesApexTangentSphereAndInteriorOnlyBoxIntersection()
    {
        FormationProtectedSite site = FormationProtectedSite.RadialFootprint("narrow", Vector3D.UnitZ, 0.1);
        Assert.True(site.Intersects(Point(Vector3D.Zero)), "The apex is included.");
        Assert.True(site.Intersects(new FormationBounds(new Vector3D(-1, -1, -1), new Vector3D(1, 1, 1))), "An apex-straddling box is included.");
        Assert.True(!site.Intersects(new FormationBounds(new Vector3D(-0.1, -0.1, -10), new Vector3D(0.1, 0.1, -9))), "A separated box behind the apex is rejected.");

        var interior = new FormationBounds(new Vector3D(-1, -2, 9), new Vector3D(5, 2, 11));
        Assert.True(!site.Intersects(Point(interior.Centre)), "The box centre misses the narrow footprint.");
        for (int corner = 0; corner < 8; corner++)
        {
            var point = new Vector3D((corner & 1) == 0 ? interior.Min.X : interior.Max.X,
                (corner & 2) == 0 ? interior.Min.Y : interior.Max.Y,
                (corner & 4) == 0 ? interior.Min.Z : interior.Max.Z);
            Assert.True(!site.Intersects(Point(point)), "Every box corner misses, so corner sampling cannot certify separation.");
        }
        Assert.True(site.Intersects(interior), "The box contains part of the protected axis despite all sampled endpoints missing.");

        const double chord = 0.6;
        FormationProtectedSite tangent = FormationProtectedSite.RadialFootprint("tangent", Vector3D.UnitZ, chord);
        Vector3D boundary = DirectionAtChord(Vector3D.UnitZ, Vector3D.UnitX, chord);
        var outward = new Vector3D(boundary.Z, 0, -boundary.X);
        Vector3D centre = boundary * 100 + outward;
        // A Y-only box has enclosing sphere radius one. This intentionally checks the conservative sphere,
        // not a claim that the original degenerate box itself touches the cone.
        Assert.True(tangent.Intersects(new FormationBounds(centre - Vector3D.UnitY, centre + Vector3D.UnitY)), "Tangency of the enclosing sphere is retained.");
        centre += outward * 0.001;
        Assert.True(!tangent.Intersects(new FormationBounds(centre - Vector3D.UnitY, centre + Vector3D.UnitY)), "A sphere with positive separation is rejected.");
    }

    [Test]
    public static void RadialSiteSupportsZeroObtuseAndCompleteChordCaps()
    {
        FormationProtectedSite ray = FormationProtectedSite.RadialFootprint("ray", Vector3D.UnitZ, 0);
        Assert.True(ray.Intersects(Point(Vector3D.UnitZ * 100)), "A zero footprint still protects its forward ray.");
        Assert.True(!ray.Intersects(Point(new Vector3D(0.01, 0, 100))), "A separated point is outside the zero-width ray.");
        Assert.True(!ray.Intersects(Point(-Vector3D.UnitZ)), "The ray does not extend behind the apex.");

        FormationProtectedSite obtuse = FormationProtectedSite.RadialFootprint("obtuse", Vector3D.UnitZ, Math.Sqrt(3));
        Assert.True(obtuse.Intersects(Point(Vector3D.UnitX * 10)), "A 120-degree cone includes directions behind its tangent plane.");
        Assert.True(obtuse.Intersects(Point(new Vector3D(Math.Sqrt(3) * 0.5, 0, -0.5) * 10)), "The obtuse cone includes its backward-facing boundary.");
        Assert.True(!obtuse.Intersects(Point(-Vector3D.UnitZ * 10)), "The antipodal direction remains outside a 120-degree cone.");
        // Closest cone point is on a forward boundary ray, not the origin, for this backward sphere.
        Assert.True(obtuse.Intersects(new FormationBounds(new Vector3D(-9, 0, -10), new Vector3D(9, 0, -10))), "An enclosing sphere reaching the obtuse boundary is retained.");
        Assert.True(!obtuse.Intersects(new FormationBounds(new Vector3D(-8, 0, -10), new Vector3D(8, 0, -10))), "A smaller sphere is separated from the obtuse cone.");

        foreach (double chord in new[] { 2.0, 3.0, double.MaxValue })
        {
            FormationProtectedSite all = FormationProtectedSite.RadialFootprint("all", Vector3D.UnitZ, chord);
            foreach (Vector3D direction in new[] { Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, -Vector3D.UnitZ })
                Assert.True(all.Intersects(Point(direction * 1200)), "A chord cap spanning the sphere protects every direction.");
        }
    }

    [Test]
    public static void RadialSiteNormalizationDoesNotShrinkTheRepresentedGradeFootprint()
    {
        const double axisLength = 1 - 4e-11, chord = 0.8, radius = 100_000_000;
        Vector3D axis = Vector3D.UnitZ * axisLength;
        double z = (1 + axisLength * axisLength - chord * chord) / (2 * axisLength);
        var edge = new Vector3D(Math.Sqrt(1 - z * z), 0, z);
        Assert.Near(chord, (edge - axis).Length, 2e-15, "The point is on the original represented-axis chord boundary.");
        FormationProtectedSite represented = FormationProtectedSite.RadialFootprint("represented", axis, chord);
        FormationProtectedSite unexpanded = FormationProtectedSite.RadialFootprint("ideal", Vector3D.UnitZ, chord);
        Assert.True(represented.Intersects(Point(edge * radius)), "Normalization must conservatively preserve the original grading footprint.");
        Assert.True(!unexpanded.Intersects(Point(edge * radius)), "The fixture distinguishes expansion from silently normalizing the axis.");
    }

    [Test]
    public static void RadialSiteIsConservativeForIndependentInsidePointsAcrossAxesAndScales()
    {
        Vector3D[] axes = { Vector3D.UnitZ, Vector3D.UnitY, new Vector3D(1, -2, 3).Normalized };
        int checkedPoints = 0;
        foreach (Vector3D axis in axes)
        {
            Vector3D right = Vector3D.Cross(axis, Math.Abs(axis.Y) < 0.9 ? Vector3D.UnitY : Vector3D.UnitX).Normalized;
            Vector3D forward = Vector3D.Cross(axis, right).Normalized;
            foreach (double chord in new[] { 0.001, 0.5, 1.6, 1.99 })
            {
                FormationProtectedSite site = FormationProtectedSite.RadialFootprint("samples", axis, chord);
                foreach (Vector3D tangent in new[] { right, forward, -right, -forward })
                foreach (double fraction in new[] { 0.0, 0.5, 0.999999 })
                foreach (double radius in new[] { 0.01, 1200.0, 1_000_000.0 })
                {
                    Vector3D direction = DirectionAtChord(axis, tangent, chord * fraction);
                    Assert.True((direction - axis).Length <= chord + 1e-14, "The independent chord predicate confirms the sample is protected.");
                    Assert.True(site.Intersects(Point(direction * radius)), "No point inside the grade footprint may be missed by exclusion geometry.");
                    checkedPoints++;
                }
            }
        }
        Assert.Equal(432, checkedPoints, "The independent sample set has a fixed finite work bound.");
    }

    [Test]
    public static void ProtectedMeshAndRadialFootprintRemainAnExplicitUnion()
    {
        var mesh = new FormationBounds(new Vector3D(-10, -2, -2), new Vector3D(-8, 2, 2));
        var boxOnly = new FormationProtectedSite("mesh", mesh);
        var union = new FormationProtectedSite("site", mesh, Vector3D.UnitZ, 0.1);
        Assert.True(boxOnly.Intersects(Point(new Vector3D(-9, 0, 0))), "Existing box-only exclusion semantics are retained.");
        Assert.True(union.Intersects(Point(new Vector3D(-9, 0, 0))), "Mesh outside the cone is protected by the mesh branch.");
        Assert.True(!boxOnly.Intersects(Point(Vector3D.UnitZ * 1000)), "A finite mesh box cannot cover a distant grade ray.");
        Assert.True(union.Intersects(Point(Vector3D.UnitZ * 1000)), "The radial footprint survives independently of mesh bounds.");
        Assert.True(!union.Intersects(Point(Vector3D.UnitX * 1000)), "An unrelated separated point is outside both branches.");
    }

    [Test]
    public static void RadialSiteRejectsInvalidInputAndNeverLosesHugeFiniteAxisHits()
    {
        Reject<FormationValidationException>(() => FormationProtectedSite.RadialFootprint("bad", Vector3D.Zero, 0.1));
        Reject<FormationValidationException>(() => FormationProtectedSite.RadialFootprint("bad", Vector3D.UnitZ * 2, 0.1));
        foreach (double chord in new[] { -1.0, double.NaN, double.PositiveInfinity })
            Reject<FormationValidationException>(() => FormationProtectedSite.RadialFootprint("bad", Vector3D.UnitZ, chord));
        Reject<FormationValidationException>(() => Point(new Vector3D(double.NaN, 0, 0)));
        Reject<FormationValidationException>(() => Point(new Vector3D(1e200, 0, 0)));
        Reject<FormationValidationException>(() => new FormationBounds(new Vector3D(-1e154, 0, 0), new Vector3D(1e154, 0, 0)));
        FormationProtectedSite site = FormationProtectedSite.RadialFootprint("huge", Vector3D.UnitZ, 0.1);
        Assert.True(site.Intersects(Point(new Vector3D(0, 0, 1e154))), "Finite extreme coordinates cannot silently lose a protected point.");
        Assert.True(!site.Intersects(Point(new Vector3D(0, 0, -1e154))), "A representable extreme point behind the cone is still separated.");
    }

    [Test]
    public static void WalkingAdapterFinalizesActualEarthSitesAndPreservesRawFields()
    {
        var (system, earth) = StartingWorld.Find();
        // One actual world, no parallel builds, no scatter/collision generation, and a bounded 48-point survey.
        WalkingWorld world = WalkingWorld.For(system, earth, 1200);
        var adapter = new WalkingGradedTerrain(world, "earth-test");
        Assert.True(adapter.IsFinalized, "The adapter publishes only after the monument snapshot is complete.");
        Assert.Equal(world.Radius, adapter.Radius, "Body radius is preserved.");
        Assert.Equal(world.Monuments.All.Count, adapter.ProtectedSites.Length, "Every finalized monument has one immutable exclusion.");
        Assert.True(adapter.ProtectedSites.Length > 0, "The actual-world fixture exercises nonempty monument grading.");
        var savedSites = adapter.ProtectedSites.ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormationProtectedSite site in savedSites) Assert.True(keys.Add(site.Key), "Site keys are globally unique.");
        foreach (Vector3D direction in PlanetSurface.FibonacciSphere(48)) CheckRaw(direction);

        int changedByGrade = 0, meshVertices = 0;
        for (int i = 0; i < Math.Min(8, world.Monuments.All.Count); i++)
        {
            MonumentPlacement monument = world.Monuments.All[i];
            FormationProtectedSite exclusion = savedSites[i];
            Assert.Equal($"monument-{monument.Tile.Packed:x16}-{monument.Definition.Key}", exclusion.Key, "Exclusion identity follows canonical site ownership.");
            foreach (double distance in new[] { 0.0, monument.Definition.Radius * 0.5, monument.Definition.Radius + MonumentPlacements.Blend * 0.5 })
            {
                Vector3D direction = DirectionAtChord(monument.Up, monument.Right, distance / world.Radius);
                CheckRaw(direction);
                if (Math.Abs(world.Fields(direction).Height - adapter.HeightAt(direction)) > 1e-7) changedByGrade++;
                Assert.True(exclusion.Intersects(Point(direction * (world.Radius + adapter.HeightAt(direction)))), "The actual grade, including the blend ring, is protected.");
                Assert.True(exclusion.Intersects(Point(direction * (world.Radius * 0.5))), "The cone is not clipped to a finite mesh/plane height slab.");
                Assert.True(exclusion.Intersects(Point(direction * (world.Radius + 10000))), "The same ring remains protected above every monument mesh.");
            }
            // Each selected model has finite embedded collision geometry; inspect a bounded set of its vertices.
            int count = Math.Min(64, monument.Definition.Positions.Length);
            for (int vertex = 0; vertex < count; vertex++)
            {
                Assert.True(exclusion.Intersects(Point(monument.Transform(monument.Definition.Positions[vertex]))), "Actual transformed monument geometry is protected.");
                meshVertices++;
            }
        }
        Assert.True(changedByGrade > 0, "This fixture distinguishes finalized grading from raw height forwarding.");
        Assert.True(meshVertices > 0, "The union is checked against actual embedded monument vertices.");
        for (int i = 0; i < savedSites.Length; i++)
            Assert.True(ReferenceEquals(savedSites[i], adapter.ProtectedSites[i]), "Queries never replace the finalized site snapshot.");

        Reject<FormationCapacityException>(() => new WalkingGradedTerrain(world, "earth-test", maximumProtectedSites: 0));
        Reject<FormationValidationException>(() => new WalkingGradedTerrain(world, "earth-test", maximumProtectedSites: 1025));
        Reject<FormationValidationException>(() => new WalkingGradedTerrain(world, "  "));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Reject<OperationCanceledException>(() => new WalkingGradedTerrain(world, "earth-test", cancellation: cancelled.Token));
        Fixtures.Report($"{savedSites.Length} finalized Earth sites; {changedByGrade} probes differ from raw height; {meshVertices} transformed vertices checked");

        void CheckRaw(Vector3D direction)
        {
            WalkingSample raw = world.Fields(direction);
            ClimateSample climate = world.Climate(direction, raw);
            GradedTerrainFields fields = adapter.RawFieldsAt(direction);
            Assert.Equal(world.HeightAt(direction), adapter.HeightAt(direction), "Height follows finalized grading exactly.");
            Assert.Equal(world.WaterLevelAt(direction), adapter.WaterLevelAt(direction), "Water, including the dry sentinel, is unchanged.");
            Assert.Equal(new GradedTerrainFields("earth-test", climate.Temperature, climate.Moisture, world.ForestCover(direction, raw)), fields,
                "Climate and forest potential come from raw fields, without monument vegetation clearing or cover feedback.");
        }
    }

    [Test]
    public static void RadialProtectionIsIdenticalInFullAndRegionalPlannerRequests()
    {
        const int level = 4, n = 1 << level;
        FormationCatalog catalog = PlannerCatalog();
        FormationPlan control = FormationPlanner.Build(new PlannerTerrain(Array.Empty<FormationProtectedSite>()), catalog, PlannerSettings());
        FormationContourIdentity? accepted = null;
        foreach (FormationPlanEntry entry in control.Entries)
            if (entry.Record.Pieces.Length > 0) { accepted = entry.Identity; break; }
        Assert.True(accepted.HasValue, "The unprotected control has actual fitted geometry before any protected-site rejection.");
        FormationContourIdentity identity = accepted!.Value;
        var owner = new TileKey(identity.Face, identity.GridLevel, identity.X, identity.Y);
        // A contour's owner is its canonical first segment, potentially on another cube face from a
        // fitted section. Select that actual owner, not the +Z direction of the eligible physical wall.
        var region = new FormationSurveyRegion(level, new[] { owner }, 96, new FormationSurveyRegionLimits
        {
            MaxCoreCells = 1, MaxSurveyCells = 6 * n * n, MaxHaloRings = 96,
            MaxInputVisits = 1, MaxNeighborVisits = 20000
        });
        Assert.Equal(6 * n * n, region.Cells.Length, "The bounded halo supplies complete evidence while publishing only the actual contour owner.");
        FormationPlan regionalControl = FormationPlanner.Build(new PlannerTerrain(Array.Empty<FormationProtectedSite>()), catalog, PlannerSettings(region));
        int controlCompared = SameOwnedRecords(control, regionalControl);
        Assert.True(regionalControl.Diagnostics.AcceptedPieces > 0, "The single-owner regional control retains actual accepted physical output.");

        var sites = new[] { FormationProtectedSite.RadialFootprint("grade-ray", Vector3D.UnitZ, 1) };
        var terrain = new PlannerTerrain(sites);
        FormationPlan full = FormationPlanner.Build(terrain, catalog, PlannerSettings());
        FormationPlan regional = FormationPlanner.Build(terrain, catalog, PlannerSettings(region));
        int protectedCompared = SameOwnedRecords(full, regional);
        Assert.Equal(controlCompared, protectedCompared, "Site rejection preserves every preassigned section identity.");
        bool acceptedBecameProtected = false;
        foreach (FormationPlanEntry entry in full.Entries)
        {
            if (entry.Identity != identity) continue;
            Assert.Equal(0, entry.Record.Pieces.Length, "The formerly accepted wall cannot build through the grade ray.");
            foreach (FormationRejection rejection in entry.Record.Rejections)
                acceptedBecameProtected |= rejection.Reason == FormationRejectionReason.ProtectedSite;
        }
        Assert.True(acceptedBecameProtected, "The same formerly accepted section is explicitly refused by protected-site geometry.");
        Assert.True(full.Diagnostics.ConservativeSiteRejections > 0 && regional.Diagnostics.ConservativeSiteRejections > 0,
            "Both requests use the same radial protected-site predicate.");
        Assert.True(ReferenceEquals(sites[0], terrain.ProtectedSites[0]), "Fitting retains the same immutable global exclusion definition.");
        Fixtures.Report($"canonical owner {owner.Face}/{owner.Level}/{owner.X}/{owner.Y}; {controlCompared} sections; accepted control {regionalControl.Diagnostics.AcceptedPieces}; protected section {identity.Section}");

        int SameOwnedRecords(FormationPlan whole, FormationPlan part)
        {
            int compared = 0;
            foreach (FormationPlanEntry entry in whole.Entries)
            {
                var entryOwner = new TileKey(entry.Identity.Face, entry.Identity.GridLevel, entry.Identity.X, entry.Identity.Y);
                if (!entryOwner.Equals(owner)) continue;
                Assert.True(compared < part.Entries.Length, "A regional request preserves every core-owned section.");
                FormationPlanEntry actual = part.Entries[compared++];
                Assert.Equal(entry.Identity, actual.Identity, "Frozen identity does not depend on request partition.");
                FormationRecord expected = entry.Record, observed = actual.Record;
                Assert.Equal(expected.Id, observed.Id, "Physical record identity is stable.");
                Assert.Equal(expected.RecipeKey, observed.RecipeKey, "Recipe selection is stable.");
                Assert.Equal(expected.RecipeRevision, observed.RecipeRevision, "Recipe revision is stable.");
                Assert.Equal(expected.FamilyKey, observed.FamilyKey, "Physical family is stable.");
                Assert.True(expected.Crest.SequenceEqual(observed.Crest) && expected.Face.SequenceEqual(observed.Face) && expected.Toe.SequenceEqual(observed.Toe),
                    "The complete fitted guides are unchanged by request scope.");
                Assert.Equal(expected.InfluenceBounds, observed.InfluenceBounds, "The complete physical influence is stable.");
                Assert.Equal(expected.Coverage, observed.Coverage, "Coverage evidence is stable.");
                Assert.Equal(expected.Pieces.Length, observed.Pieces.Length, "Accepted physical slots are identical.");
                for (int piece = 0; piece < expected.Pieces.Length; piece++)
                {
                    FormationPiece a = expected.Pieces[piece], b = observed.Pieces[piece];
                    Assert.Equal(a.Id, b.Id, "Physical piece identity is stable.");
                    Assert.Equal(a.Role, b.Role, "Physical piece role is stable.");
                    Assert.True(ReferenceEquals(a.Geometry, b.Geometry), "Both builds select the same immutable catalog geometry.");
                    Assert.Equal(a.Transform, b.Transform, "The full physical transform is stable.");
                    Assert.Equal(a.Bounds, b.Bounds, "Physical piece bounds are stable.");
                }
                Assert.Equal(expected.Rejections.Length, observed.Rejections.Length, "Rejected slots retain their count and order.");
                for (int rejected = 0; rejected < expected.Rejections.Length; rejected++)
                {
                    FormationRejection a = expected.Rejections[rejected], b = observed.Rejections[rejected];
                    Assert.Equal(a.Slot, b.Slot, "Rejected slot identity is stable.");
                    Assert.Equal(a.Role, b.Role, "Rejected slot role is stable.");
                    Assert.Equal(a.Reason, b.Reason, "Rejection reason is stable.");
                    Assert.Equal(a.IntendedRegion, b.IntendedRegion, "Rejected footprint is stable.");
                }
            }
            Assert.True(compared > 0, "The comparison includes actual contour records, not an empty plan.");
            Assert.Equal(compared, part.Entries.Length, "Halo-only records remain unpublished.");
            return compared;
        }
    }

    private static FormationBounds Point(Vector3D point) => new(point, point);
    private static Vector3D DirectionAtChord(Vector3D axis, Vector3D tangent, double chord)
    {
        axis = axis.Normalized;
        tangent = (tangent - axis * Vector3D.Dot(axis, tangent)).Normalized;
        double axial = 1 - chord * chord * 0.5;
        return axis * axial + tangent * Math.Sqrt(Math.Max(0, 1 - axial * axial));
    }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }

    private sealed class PlannerTerrain(FormationProtectedSite[] sites) : IGradedTerrain
    {
        public bool IsFinalized => true;
        public double Radius => 128;
        public double HeightAt(Vector3D direction) => Math.Clamp(direction.X * Radius + 40, 0, 80);
        public double WaterLevelAt(Vector3D direction) => double.NegativeInfinity;
        public GradedTerrainFields RawFieldsAt(Vector3D direction) => new(direction.Z > 0.85 ? "test-ground" : "other", 15, 0.5, 0.5);
        public ReadOnlySpan<FormationProtectedSite> ProtectedSites => sites;
    }
    private static FormationPlannerSettings PlannerSettings(FormationSurveyRegion? region = null) =>
        new("recipe", 0, "test-ground", 2, 0.5, 10, 12, 2, 2, 100, Vector3D.UnitY, "crest", 3, region);
    private static FormationCatalog PlannerCatalog()
    {
        var geometry = new FormationGeometry("wall", 0, "test-family",
            new[] { new Vector3D(-32, 0, 0), new Vector3D(32, 0, 0), new Vector3D(32, -120, 120), new Vector3D(-32, -120, 120) },
            new[] { new GeometryTriangle(0, 1, 2, 0), new GeometryTriangle(0, 2, 3, 0) },
            new[] { new SupportPatch("face", "rock", SupportCapabilities.None) });
        const string digest = "0000000000000000000000000000000000000000000000000000000000000000";
        var maps = new[] { new CliffLodTriangleMap(0, 0), new CliffLodTriangleMap(1, 1) };
        var definition = new CliffSupportDefinition(geometry, digest, digest, 1, 1,
            new[] { new CliffPatchDomain("face", new[] { new CliffPatchBoundary(new[] { 0, 1, 2, 3 }) }) },
            new[] { new CliffContactDomain("anchor", "crest", "face", new[] { 0, 1 }, 0, 8, 8) },
            new[] { new CliffLodBinding("near", digest, geometry, maps, 0, 0.99), new CliffLodBinding("far", digest, geometry, maps, 0, 0.99) });
        var choice = new FormationGeometryChoice(definition.Key, definition.Revision, new[] { "anchor" }, 1, 1);
        var recipe = new FormationRecipe("recipe", 0, 17, "test-family",
            new[] { new FormationRecipeSlot(0, "wall", true, new[] { choice }) }, 24, 300, 0.5, 2, 1);
        return new FormationCatalog(new[] { definition }, new[] { new SubstrateProfile("rock", 1, 0, 0, 0, 0, 0, 0, 0) }, new[] { recipe });
    }
}
