using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Experimental finite producer for one explicitly selected qualified recipe. No world integration or asset loading.
/// Survey/contour identities are frozen before fitting. All failure/cancellation paths publish no partial plan.
/// Coverage is a declared finite lattice estimate against physical triangles, not an area-containment certificate.
/// Independently fitted records use one-hop canonical priority, including halo-owned conflict evidence.
/// </summary>
public static class FormationPlanner
{
    /// <summary>Revision 2 replaces request-local greedy packing with independent proposal priority.</summary>
    public const int GenerationRevision = 2;
    public const string ConflictPolicy = "independent-proposal-lower-id-v1";
    public static FormationPlan Build(IGradedTerrain terrain, FormationCatalog catalog, FormationPlannerSettings settings,
        FormationPlannerLimits? limits = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(terrain); ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(settings);
        return new Builder(terrain, catalog, settings, limits ?? new FormationPlannerLimits(), cancellation).Build();
    }

    private sealed class Builder
    {
        private readonly IGradedTerrain _terrain;
        private readonly FormationCatalog _catalog;
        private readonly FormationPlannerSettings _settings;
        private readonly FormationPlannerLimits _limits;
        private readonly CancellationToken _cancellation;
        private readonly FormationRecipe _recipe;
        private readonly FormationProtectedSite[] _sites;
        private readonly int _level, _n, _stride;
        private readonly Sample[] _survey;
        private readonly bool[] _sampled;
        private readonly Dictionary<CubeVertexKey, Sample>? _regionalSurvey;
        private readonly FormationSurveyRegion? _region;
        private readonly double[] _regionWarp;
        private readonly HashSet<TileKey> _sourceEvidenceInterior = new();
        private readonly int _regionalVertexLimit;
        private readonly List<Link> _links = new();
        private readonly Dictionary<CubeEdgeKey, int> _linkIds = new();
        private readonly List<Segment> _segments = new();
        private readonly List<Candidate> _candidates = new();
        private readonly List<FormationPlanEntry> _proposals = new();
        private readonly List<FormationPlanEntry> _entries = new();
        private readonly List<FormationPiece> _accepted = new();
        private int _vertices, _cells, _contours, _contacts, _coverage, _rejections, _siteRejections, _conflictRejections;
        private long _terrainSamples, _triangles, _geometryVertices, _conflicts, _owned = 4096;
        private readonly long _scratch;
        private long _regionCellTests;
        private bool _fitting;

        internal Builder(IGradedTerrain terrain, FormationCatalog catalog, FormationPlannerSettings settings,
            FormationPlannerLimits limits, CancellationToken cancellation)
        {
            _terrain = terrain; _catalog = catalog; _settings = settings; _limits = limits; _cancellation = cancellation;
            cancellation.ThrowIfCancellationRequested();
            if (!terrain.IsFinalized || !double.IsFinite(terrain.Radius) || terrain.Radius <= 0 ||
                !double.IsFinite(terrain.Radius * terrain.Radius))
                throw new FormationValidationException("A finalized finite positive-radius graded terrain is required.");
            if (limits.MaxRecords < 0 || limits.MaxRecords > 4096 || limits.MaxContourSegments < 0 || limits.MaxContourSegments > 196608 ||
                limits.MaxProtectedSites < 0 || limits.MaxContactAndCoverageSamplesPerRecord < 1 ||
                limits.MaxContactAndCoverageSamplesPerRecord > 48 || limits.MaxTerrainSamples < 0 ||
                limits.MaxTriangleTests < 0 || limits.MaxGeometryVertexVisits < 0 || limits.MaxConflictTests < 0 || limits.MaxRegionCellTests < 0 || limits.MaxEstimatedOwnedBytes < 0 || limits.MaxEstimatedScratchBytes < 0)
                throw new FormationValidationException("Planner limits exceed the explicit experimental ceilings.");
            _recipe = catalog.RequireRecipe(settings.RecipeKey, settings.RecipeRevision);
            if (_recipe.MaximumGuideStations > 9 || _recipe.MaximumFitProbes > 3 || _recipe.Slots.Length > 12)
                throw new FormationValidationException("Recipe exceeds the planner's 9-station/3-probe/12-slot implementation.");
            _level = settings.SurveyLevel + 1; _n = 1 << _level; _stride = _n + 1;
            _region = settings.Region;
            long vertexSlots = _region == null ? 6L * _stride * _stride : 4L * _region.Cells.Length;
            _regionalVertexLimit = checked((int)vertexSlots);
            // Includes survey arrays, bounded hash/list overhead, candidate copies, sorting/BVH scratch and result construction.
            // Regional dictionaries include capacity growth/rehash; no face-sized survey arrays are allocated.
            _scratch = 65536L + (_region == null ? 128L : 320L) * vertexSlots + 768L * limits.MaxContourSegments +
                2048L * limits.MaxRecords + 1024L * limits.MaxProtectedSites +
                (_region?.Diagnostics.EstimatedOwnedBytes ?? 0) + 512L * (_region == null ? 0 : _region.Cells.Length) + 8L * (_n + 1);
            if (_scratch > limits.MaxEstimatedScratchBytes || _owned > limits.MaxEstimatedOwnedBytes)
                throw new FormationCapacityException("Planner survey/construction reservation exceeds its byte budget.");
            if (terrain.ProtectedSites.Length > limits.MaxProtectedSites)
                throw new FormationCapacityException("Protected-site snapshot exceeds its limit.");
            _sites = terrain.ProtectedSites.ToArray();
            foreach (FormationProtectedSite site in _sites) if (site == null) throw new FormationValidationException("Null protected site.");
            Array.Sort(_sites, static (a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
            for (int i = 1; i < _sites.Length; i++) if (_sites[i - 1].Key == _sites[i].Key) throw new FormationValidationException("Duplicate protected-site key.");
            _survey = _region == null ? new Sample[(int)vertexSlots] : Array.Empty<Sample>();
            _sampled = _region == null ? new bool[(int)vertexSlots] : Array.Empty<bool>();
            _regionalSurvey = _region == null ? null : new Dictionary<CubeVertexKey, Sample>();
            // Whole-planet proposals use the same explicit provenance gate as regional ones.
            _regionWarp = new double[_n + 1];
            {
                // Face endpoints are exact dominant-axis planes. Interior lines use represented Warp values;
                // never invert them with an approximate Unwarp to decide domain membership.
                _regionWarp[0] = -1; _regionWarp[_n] = 1;
                for (int i = 1; i < _n; i++) _regionWarp[i] = CubeSphere.Warp(-1 + 2.0 * i / _n);
                for (int i = 1; i <= _n; i++)
                    if (!double.IsFinite(_regionWarp[i]) || _regionWarp[i] <= _regionWarp[i - 1])
                        throw new FormationValidationException("Regional face grid is not a finite increasing partition.");
            }
        }

        internal FormationPlan Build()
        {
            Survey(); TraceContours();
            // Every contour/section identity exists before the first acceptance decision.
            _candidates.Sort(static (a, b) => a.Identity.Id.CompareTo(b.Identity.Id));
            foreach (Candidate candidate in _candidates) { _cancellation.ThrowIfCancellationRequested(); Fit(candidate); }
            PublishIndependentProposals();
            var diagnostics = new FormationPlanDiagnostics(_vertices, _cells, _segments.Count, _contours, _entries.Count,
                _accepted.Count, _rejections, _terrainSamples, _contacts, _coverage, _triangles, _geometryVertices, _conflicts,
                _siteRejections, _conflictRejections, _owned, _scratch, _regionCellTests);
            return new FormationPlan(_entries.ToArray(), diagnostics, _cancellation);
        }

        private void Survey()
        {
            var keys = new CubeVertexKey[4]; var edges = new int[4];
            if (_region != null)
            {
                foreach (TileKey cell in _region.Cells) Cell(cell.Face, cell.X, cell.Y);
            }
            else
            {
                // Four level+1 cells are each coarse cell's 2x2 subcells. Shared vertices use exact cube keys.
                for (int face = 0; face < 6; face++)
                for (int y = 0; y < _n; y++)
                for (int x = 0; x < _n; x++)
                    Cell(face, x, y);
            }
            void Cell(int face, int x, int y)
            {
                _cancellation.ThrowIfCancellationRequested(); _cells++;
                keys[0] = CubeTopology.VertexKey(face, _level, x, y); keys[1] = CubeTopology.VertexKey(face, _level, x + 1, y);
                keys[2] = CubeTopology.VertexKey(face, _level, x + 1, y + 1); keys[3] = CubeTopology.VertexKey(face, _level, x, y + 1);
                int mask = 0;
                for (int i = 0; i < 4; i++) if (Vertex(keys[i]).Slope >= _settings.MinimumRiseRun) mask |= 1 << i;
                if (mask == 0 || mask == 15) return;
                int count = 0;
                for (int e = 0; e < 4; e++) if (((mask >> e) & 1) != ((mask >> ((e + 1) % 4)) & 1)) edges[count++] = e;
                if (count == 2) Add(edges[0], edges[1]);
                else
                {
                    Sample centre = SampleAt(CubeSphere.Direction(face, -1 + 2.0 * (x + 0.5) / _n, -1 + 2.0 * (y + 0.5) / _n));
                    // Equality belongs to high, consistently at vertices and at the saddle centre.
                    bool centreHigh = centre.Slope >= _settings.MinimumRiseRun;
                    if ((mask == 5) == centreHigh) { Add(0, 1); Add(2, 3); }
                    else { Add(3, 0); Add(1, 2); }
                }
                void Add(int edgeA, int edgeB)
                {
                    if (_segments.Count == _limits.MaxContourSegments) throw new FormationCapacityException("Contour segment limit exceeded.");
                    int a = LinkFor(CubeTopology.EdgeKey(keys[edgeA], keys[(edgeA + 1) % 4]));
                    int b = LinkFor(CubeTopology.EdgeKey(keys[edgeB], keys[(edgeB + 1) % 4]));
                    int index = _segments.Count;
                    _segments.Add(new Segment(face, x, y, Math.Min(edgeA, edgeB), Math.Max(edgeA, edgeB), a, b));
                    _links[a].Segments.Add(index); _links[b].Segments.Add(index);
                }
            }
        }

        private int LinkFor(CubeEdgeKey key)
        {
            if (_linkIds.TryGetValue(key, out int existing)) return existing;
            Vector3D a = Direction(key.First), b = Direction(key.Second);
            bool high = Vertex(key.First).Slope >= _settings.MinimumRiseRun;
            for (int i = 0; i < 4; i++)
            {
                Vector3D middle = Unit(a + b);
                if ((SampleAt(middle).Slope >= _settings.MinimumRiseRun) == high) a = middle; else b = middle;
            }
            Vector3D point = Point(Unit(a + b));
            int index = _links.Count; _links.Add(new Link(key, point)); _linkIds.Add(key, index); return index;
        }

        private void TraceContours()
        {
            bool[]? admitted = _region == null ? null : CompleteRegionalComponents();
            var order = new int[_links.Count]; for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => _links[a].Key.CompareTo(_links[b].Key));
            var used = new bool[_segments.Count];
            // Branches/endpoints split contours; remaining degree-two components are closed loops.
            foreach (int node in order) if (_links[node].Segments.Count != 2)
                foreach (int segment in _links[node].Segments) if (!used[segment] && (admitted == null || admitted[segment])) Trace(node, segment);
            foreach (int node in order)
                foreach (int segment in _links[node].Segments) if (!used[segment] && (admitted == null || admitted[segment])) Trace(node, segment);

            void Trace(int start, int first)
            {
                _contours++; Segment owner = _segments[first]; int section = 0, current = start, selected = first;
                var points = new List<Vector3D> { _links[start].Point }; double length = 0;
                while (true)
                {
                    _cancellation.ThrowIfCancellationRequested(); used[selected] = true;
                    Segment segment = _segments[selected]; int next = segment.A == current ? segment.B : segment.A;
                    Vector3D target = _links[next].Point, previous = points[points.Count - 1];
                    // A complete loop cannot be one fitted chord. Split its closing edge while identities are still being assigned.
                    if (next == start && points.Count > 2 && points[0].Equals(target)) Flush();
                    double edgeLength = (target - previous).Length;
                    if (!double.IsFinite(edgeLength)) throw new FormationValidationException("Nonfinite contour length.");
                    // Subdivide long contour edges deterministically before any fit, within the global record ceiling.
                    double required = Math.Ceiling(edgeLength / _recipe.MaximumSectionLengthMetres);
                    if (!double.IsFinite(required) || required > _limits.MaxRecords + 1L)
                        throw new FormationCapacityException("Contour subdivision exceeds the bounded record namespace.");
                    int divisions = Math.Max(1, (int)required);
                    for (int part = 1; part <= divisions; part++)
                    {
                        Vector3D point = part == divisions ? target : Point(Unit(previous * (1.0 - (double)part / divisions) + target * ((double)part / divisions)));
                        double distance = (point - points[points.Count - 1]).Length;
                        if (points.Count >= 2 && (points.Count == _recipe.MaximumGuideStations || length + distance > _recipe.MaximumSectionLengthMetres))
                            Flush();
                        length += (point - points[points.Count - 1]).Length; points.Add(point);
                    }
                    if (_links[next].Segments.Count != 2) break;
                    int following = _links[next].Segments[0] == selected ? _links[next].Segments[1] : _links[next].Segments[0];
                    if (used[following]) break;
                    current = next; selected = following;
                }
                if (points.Count > 1) Flush();
                void Flush()
                {
                    if (_candidates.Count == _limits.MaxRecords) throw new FormationCapacityException("Formation record limit exceeded.");
                    var identity = new FormationContourIdentity(owner.Face, _level, owner.X, owner.Y, owner.EdgeA, owner.EdgeB,
                        _recipe.IdentityDomain, _recipe.Revision, section++);
                    // The last guide is either this segment's incident endpoint or an interpolation on it.
                    // Preserve that actual source cell, not the (possibly distant) contour identity owner.
                    Segment source = _segments[selected];
                    _candidates.Add(new Candidate(identity, points.ToArray(), length > _recipe.MaximumSectionLengthMetres,
                        new TileKey(source.Face, _level, source.X, source.Y)));
                    Vector3D last = points[points.Count - 1]; points.Clear(); points.Add(last); length = 0;
                }
            }
        }

        private bool[] CompleteRegionalComponents()
        {
            var visited = new bool[_segments.Count]; var admitted = new bool[_segments.Count];
            var queue = new int[_segments.Count];
            for (int first = 0; first < _segments.Count; first++)
            {
                if (visited[first]) continue;
                int head = 0, count = 1;
                queue[0] = first; visited[first] = true;
                bool incomplete = false;
                while (head < count)
                {
                    _cancellation.ThrowIfCancellationRequested();
                    Segment segment = _segments[queue[head++]];
                    Visit(segment.A); Visit(segment.B);
                }
                // Halo contours are rival evidence, never missing/empty competitors. Conservatively require
                // every component, even a remote halo-only one, to be complete before publishing anything.
                if (incomplete)
                    throw new FormationIncompleteRegionException("A survey contour reaches the boundary or has incomplete adjacency; neighbor-conflict evidence requires a larger explicit halo.");
                for (int i = 0; i < count; i++) admitted[queue[i]] = true;

                void Visit(int node)
                {
                    Link link = _links[node];
                    incomplete |= link.Segments.Count != 2 || _region!.IsBoundaryEdge(link.Key);
                    foreach (int neighbor in link.Segments)
                        if (!visited[neighbor]) { visited[neighbor] = true; queue[count++] = neighbor; }
                }
            }
            return admitted;
        }

        private void Fit(Candidate candidate)
        {
            _fitting = true;
            Vector3D[] crest = candidate.Crest, toe = new Vector3D[crest.Length], face = new Vector3D[crest.Length];
            FormationRejectionReason? failure = candidate.TooLong ? FormationRejectionReason.Extent : null;
            Vector3D sourcePoint = crest[crest.Length - 1];
            // This is a generation admission rule in every scope, not an assumed floating-point lemma.
            // Even a numerically displaced guide cannot authorize a piece unless its actual source cell
            // belongs to the same exact two-ring shadow closure used by regional neighbor evidence.
            if (!ShadowCells(new FormationBounds(sourcePoint, sourcePoint), candidate.SourceCell))
                failure = FormationRejectionReason.Extent;
            for (int i = 0; i < crest.Length; i++)
            {
                Sample top = SampleAt(Unit(crest[i])); toe[i] = crest[i]; bool found = false, entered = false;
                Vector3D prior = Unit(crest[i]);
                if (!top.Eligible) failure ??= FormationRejectionReason.Contact;
                for (int step = 1; step <= _settings.CrossSectionSteps; step++)
                {
                    double distance = step * _settings.CrossSectionStepMetres;
                    if (distance > _recipe.MaximumExtentMetres) break;
                    Vector3D direction = Unit(Unit(crest[i]) + top.Downhill * (distance / _terrain.Radius));
                    Sample sample = SampleAt(direction); toe[i] = sample.Point;
                    if (!sample.Eligible) { failure ??= FormationRejectionReason.Contact; break; }
                    if (sample.Slope >= _settings.MinimumRiseRun) entered = true;
                    else if (entered)
                    {
                        for (int refine = 0; refine < 4; refine++)
                        {
                            Vector3D middle = Unit(prior + direction);
                            if (SampleAt(middle).Slope >= _settings.MinimumRiseRun) prior = middle; else direction = middle;
                        }
                        toe[i] = Point(Unit(prior + direction)); found = true; break;
                    }
                    else break; // This is a lower boundary or an unresolved band, not an invented crest.
                    prior = direction;
                }
                if (!found) failure ??= FormationRejectionReason.Contact;
                face[i] = (crest[i] + toe[i]) * 0.5;
            }
            FormationBounds intended = Bounds(crest).Union(Bounds(toe));
            RequireRegion(intended);
            if (!FitsExtent(intended)) failure = FormationRejectionReason.Extent;
            if (Protected(intended)) { failure = FormationRejectionReason.ProtectedSite; _siteRejections++; }
            double area = StripArea(crest, toe);
            if (area <= 0) failure ??= FormationRejectionReason.Coverage;
            var pieces = new List<FormationPiece>(); var rejected = new List<FormationRejection>();
            int samples = 0;
            int reservedCoverage = checked((crest.Length - 1) * _settings.CoverageAlong * _settings.CoverageDown);
            if (reservedCoverage > _limits.MaxContactAndCoverageSamplesPerRecord)
                throw new FormationCapacityException("Declared coverage lattice exceeds the per-record sample budget.");
            bool requiredFailed = false;
            foreach (FormationRecipeSlot slot in _recipe.Slots)
            {
                _cancellation.ThrowIfCancellationRequested(); FormationPiece? accepted = null;
                FormationRejectionReason reason = failure ?? FormationRejectionReason.Contact;
                if (failure == null)
                {
                    foreach (FormationGeometryChoice choice in slot.Alternatives)
                    {
                        CliffSupportDefinition definition = _catalog.RequireQualified(choice.Key, choice.Revision);
                        CliffContactDomain? anchor = null; int anchors = 0;
                        foreach (string contactKey in choice.ContactKeys)
                        {
                            CliffContactDomain contact = definition.RequireContact(contactKey);
                            if (contact.Role == _settings.AnchorContactRole) { anchor = contact; anchors++; }
                        }
                        if (anchors != 1 || anchor == null) { reason = FormationRejectionReason.Contact; continue; }
                        for (int probe = 0; probe < _recipe.MaximumFitProbes; probe++)
                        {
                            _cancellation.ThrowIfCancellationRequested();
                            if (!TryTransform(definition, choice, anchor, crest, probe, out FormationTransform transform)) continue;
                            // FormationPiece validates every vertex and triangle even if a later gate rejects this attempt.
                            if (definition.Geometry.VertexCount > _limits.MaxGeometryVertexVisits - _geometryVertices ||
                                definition.Geometry.TriangleCount > _limits.MaxTriangleTests - _triangles)
                                throw new FormationCapacityException("Physical candidate validation work budget exceeded.");
                            _geometryVertices += definition.Geometry.VertexCount; _triangles += definition.Geometry.TriangleCount;
                            FormationPiece piece;
                            try { piece = new FormationPiece(new FormationPieceId(candidate.Identity.Id, slot.Slot), slot.Role, definition.Geometry, transform); }
                            catch (FormationValidationException) { reason = FormationRejectionReason.Extent; continue; }
                            FormationBounds combined = intended.Union(piece.Bounds);
                            foreach (FormationPiece prior in pieces) combined = combined.Union(prior.Bounds);
                            if (!FitsExtent(combined))
                            { reason = FormationRejectionReason.Extent; continue; }
                            RequireRegion(combined);
                            if (Protected(piece.Bounds)) { reason = FormationRejectionReason.ProtectedSite; _siteRejections++; continue; }
                            if (ConflictsWithinRecord(piece.Bounds, pieces)) { reason = FormationRejectionReason.Clearance; _conflictRejections++; continue; }
                            bool contacts = FormationTransitions.ContactsMatch(definition, choice, transform,
                                p => p.Length - (_terrain.Radius + Height(Unit(p))), ChargeContact, _cancellation);
                            if (!contacts) { reason = FormationRejectionReason.Contact; continue; }
                            accepted = piece; break;
                        }
                        if (accepted != null) break;
                    }
                }
                if (accepted != null) pieces.Add(accepted);
                else { rejected.Add(new FormationRejection(slot.Slot, slot.Role, reason, intended)); requiredFailed |= slot.Required; }
            }
            double covered = 0;
            if (pieces.Count > 0 && !requiredFailed) covered = MeasureCoverage(crest, toe, pieces, ref samples);
            if (pieces.Count > 0 && (requiredFailed || area <= 0 || covered / area < _recipe.MinimumCoverage))
            {
                foreach (FormationPiece piece in pieces)
                    rejected.Add(new FormationRejection(piece.Id.Slot, piece.Role,
                        requiredFailed ? FormationRejectionReason.Contact : FormationRejectionReason.Coverage, intended.Union(piece.Bounds)));
                pieces.Clear(); covered = 0;
            }
            FormationBounds influence = intended;
            foreach (FormationPiece piece in pieces) influence = influence.Union(piece.Bounds);
            foreach (FormationRejection rejection in rejected) influence = influence.Union(rejection.IntendedRegion);
            ChargeRecord(crest.Length, pieces.Count, rejected.Count);
            var record = new FormationRecord(candidate.Identity.Id, _recipe.Key, _recipe.Revision, _recipe.FamilyKey,
                crest, face, toe, pieces, rejected, influence, new FormationCoverage(area, Math.Min(area, covered), 0));
            _proposals.Add(new FormationPlanEntry(candidate.Identity, record));
            _fitting = false;
            void ChargeContact()
            {
                if (samples + reservedCoverage == _limits.MaxContactAndCoverageSamplesPerRecord)
                    throw new FormationCapacityException("Authored chain vertices/midpoints and fit probes exceed the aggregate record sample budget.");
                samples++; _contacts++;
            }
        }

        private void PublishIndependentProposals()
        {
            // One-hop local priority has no transitive acceptance dependency: a lower proposal remains
            // evidence even if another proposal suppresses it. Never retry alternatives after this phase.
            for (int i = 0; i < _proposals.Count; i++)
            {
                _cancellation.ThrowIfCancellationRequested();
                FormationPlanEntry proposal = _proposals[i]; FormationContourIdentity identity = proposal.Identity;
                if (_region != null && !_region.HasCore(new TileKey(identity.Face, identity.GridLevel, identity.X, identity.Y))) continue;
                FormationRecord record = proposal.Record;
                if (_region != null && _region.Cells.Length != 6 * _n * _n && record.Pieces.Length > 0)
                {
                    FormationBounds physical = record.Pieces[0].Bounds;
                    foreach (FormationPiece piece in record.Pieces) physical = physical.Union(piece.Bounds);
                    // A rival that passes the combined guide+physical extent gate has every crest point
                    // within E in each coordinate of its physical box. An overlapping rival therefore has
                    // a crest in this envelope. The scope-independent provenance gate below binds its
                    // source cell to a two-ring shadow closure; require that closure as evidence too.
                    double e = _recipe.MaximumExtentMetres;
                    var low = new Vector3D(Math.BitDecrement(physical.Min.X - e), Math.BitDecrement(physical.Min.Y - e), Math.BitDecrement(physical.Min.Z - e));
                    var high = new Vector3D(Math.BitIncrement(physical.Max.X + e), Math.BitIncrement(physical.Max.Y + e), Math.BitIncrement(physical.Max.Z + e));
                    if (!double.IsFinite(low.LengthSquared) || !double.IsFinite(high.LengthSquared))
                        throw new FormationIncompleteRegionException("Neighbor-conflict envelope is not finitely representable.");
                    RequireRegion(new FormationBounds(low, high), sourceEvidence: true);
                }
                bool suppressed = false;
                for (int j = 0; record.Pieces.Length > 0 && j < i && !suppressed; j++)
                {
                    ChargeConflict(); // Proposal visits, including empty or already-suppressed rivals, are work.
                    foreach (FormationPiece piece in record.Pieces)
                    {
                        foreach (FormationPiece rival in _proposals[j].Record.Pieces)
                        {
                            ChargeConflict();
                            if (piece.Bounds.Intersects(rival.Bounds)) { suppressed = true; break; }
                        }
                        if (suppressed) break;
                    }
                }
                if (suppressed)
                {
                    var rejected = new List<FormationRejection>();
                    foreach (FormationRejection rejection in record.Rejections) rejected.Add(rejection);
                    foreach (FormationPiece piece in record.Pieces)
                        rejected.Add(new FormationRejection(piece.Id.Slot, piece.Role, FormationRejectionReason.Clearance, record.InfluenceBounds));
                    ChargeRecord(record.Crest.Length, 0, rejected.Count);
                    record = new FormationRecord(record.Id, record.RecipeKey, record.RecipeRevision, record.FamilyKey,
                        record.Crest.ToArray(), record.Face.ToArray(), record.Toe.ToArray(), Array.Empty<FormationPiece>(), rejected,
                        record.InfluenceBounds, new FormationCoverage(record.Coverage.IntendedArea, 0, 0));
                    proposal = new FormationPlanEntry(identity, record); _conflictRejections++;
                }
                _entries.Add(proposal);
                foreach (FormationPiece piece in record.Pieces) _accepted.Add(piece);
                _rejections += record.Rejections.Length;
            }
        }

        private void ChargeRecord(int guidePoints, int pieces, int rejected)
        {
            // Includes all independent halo proposals and any suppressed publication copy at construction
            // peak. This deliberately overestimates final retained output after halo evidence is released.
            long bytes = 2304L + 192L * guidePoints + 768L * pieces + 512L * rejected;
            if (bytes > _limits.MaxEstimatedOwnedBytes - _owned) throw new FormationCapacityException("Proposal/output record byte budget exceeded.");
            _owned += bytes;
        }

        private bool FitsExtent(FormationBounds bounds)
        {
            // Outward coordinate subtraction makes the neighbor-envelope proof independent of rounding in
            // the diagonal length calculation: every exact represented coordinate span is at most E.
            double e = _recipe.MaximumExtentMetres;
            return Math.BitIncrement(bounds.Max.X - bounds.Min.X) <= e &&
                Math.BitIncrement(bounds.Max.Y - bounds.Min.Y) <= e &&
                Math.BitIncrement(bounds.Max.Z - bounds.Min.Z) <= e &&
                (bounds.Max - bounds.Min).Length <= e;
        }

        private bool TryTransform(CliffSupportDefinition definition, FormationGeometryChoice choice, CliffContactDomain anchor,
            Vector3D[] crest, int probe, out FormationTransform transform)
        {
            transform = default;
            Vector3D first = definition.Geometry.Vertices[anchor.VertexIndices[0]], last = definition.Geometry.Vertices[anchor.VertexIndices[anchor.VertexIndices.Length - 1]];
            Vector3D localUp = Unit(_settings.GeometryUp), localChord = last - first;
            localChord = Vector3D.Cross(localUp, Vector3D.Cross(localChord, localUp));
            Vector3D worldUp = Unit(crest[0] + crest[crest.Length - 1]), worldChord = crest[crest.Length - 1] - crest[0];
            worldChord = Vector3D.Cross(worldUp, Vector3D.Cross(worldChord, worldUp));
            if (localChord.LengthSquared <= 0 || worldChord.LengthSquared <= 0) return false;
            Vector3D localRight = Unit(localChord), localForward = Vector3D.Cross(localRight, localUp);
            Vector3D right = Unit(worldChord), forward = Vector3D.Cross(right, worldUp);
            if (Vector3D.Dot(forward, SampleAt(Unit(crest[0] + crest[crest.Length - 1])).Downhill) < 0)
            { right = -right; forward = -forward; }
            Vector3D Rotate(Vector3D point) => right * Vector3D.Dot(point, localRight) + worldUp * Vector3D.Dot(point, localUp) + forward * Vector3D.Dot(point, localForward);
            double scale = Math.Clamp(worldChord.Length / localChord.Length, choice.MinimumScale, choice.MaximumScale);
            double fraction = _recipe.MaximumFitProbes == 1 ? 0.5 : (double)probe / (_recipe.MaximumFitProbes - 1);
            double burial = (anchor.MinimumBurialMetres + (anchor.MaximumBurialMetres - anchor.MinimumBurialMetres) * fraction) * scale;
            Vector3D target = (crest[0] + crest[crest.Length - 1]) * 0.5 - worldUp * burial;
            try
            {
                transform = new FormationTransform(target - Rotate((first + last) * 0.5) * scale,
                    Rotate(Vector3D.UnitX), Rotate(Vector3D.UnitY), Rotate(Vector3D.UnitZ), scale);
                return true;
            }
            catch (FormationValidationException) { return false; }
        }

        private double MeasureCoverage(Vector3D[] crest, Vector3D[] toe, List<FormationPiece> pieces, ref int samples)
        {
            double covered = 0;
            for (int station = 0; station < crest.Length - 1; station++)
            {
                double area = QuadArea(crest[station], crest[station + 1], toe[station], toe[station + 1]);
                Vector3D cross = Vector3D.Cross(crest[station + 1] - crest[station], toe[station] - crest[station]);
                if (area <= 0 || cross.LengthSquared <= 0) continue;
                Vector3D normal = Unit(cross);
                for (int along = 0; along < _settings.CoverageAlong; along++)
                for (int down = 0; down < _settings.CoverageDown; down++)
                {
                    _cancellation.ThrowIfCancellationRequested();
                    if (samples == _limits.MaxContactAndCoverageSamplesPerRecord) throw new FormationCapacityException("Aggregate contact/coverage sample budget exceeded.");
                    samples++; _coverage++;
                    double u = (along + 0.5) / _settings.CoverageAlong, v = (down + 0.5) / _settings.CoverageDown;
                    Vector3D top = crest[station] * (1 - u) + crest[station + 1] * u;
                    Vector3D bottom = toe[station] * (1 - u) + toe[station + 1] * u;
                    Vector3D target = top * (1 - v) + bottom * v;
                    if (Covered(target, normal, pieces)) covered += area / (_settings.CoverageAlong * _settings.CoverageDown);
                }
            }
            return covered;
        }

        private bool Covered(Vector3D target, Vector3D normal, List<FormationPiece> pieces)
        {
            Vector3D origin = target - normal * _settings.CoverageReachMetres;
            double distance = 2 * _settings.CoverageReachMetres;
            // Enclose the same represented ray used below, not a separately rounded target + normal*reach.
            // Two operations require outward bounds for both multiplication and addition.
            (double x0, double x1) = Endpoint(origin.X, normal.X);
            (double y0, double y1) = Endpoint(origin.Y, normal.Y);
            (double z0, double z1) = Endpoint(origin.Z, normal.Z);
            var rayBounds = new FormationBounds(FormationBounds.Lower(origin, new Vector3D(x0, y0, z0)),
                FormationBounds.Upper(origin, new Vector3D(x1, y1, z1)));
            RequireRegion(rayBounds);
            foreach (FormationPiece piece in pieces)
            foreach (GeometryTriangle triangle in piece.Geometry.Triangles)
            {
                _cancellation.ThrowIfCancellationRequested();
                if (_triangles == _limits.MaxTriangleTests) throw new FormationCapacityException("Physical coverage triangle-test budget exceeded.");
                _triangles++;
                Vector3D a = piece.Transform.Transform(piece.Geometry.Vertices[triangle.A]);
                Vector3D b = piece.Transform.Transform(piece.Geometry.Vertices[triangle.B]);
                Vector3D c = piece.Transform.Transform(piece.Geometry.Vertices[triangle.C]);
                if (TriangleRay(origin, normal, distance, a, b, c)) return true;
            }
            return false;
            (double Low, double High) Endpoint(double start, double direction)
            {
                double product = direction * distance;
                return (Math.BitDecrement(start + Math.BitDecrement(product)),
                    Math.BitIncrement(start + Math.BitIncrement(product)));
            }
        }

        private bool Protected(FormationBounds bounds)
        {
            foreach (FormationProtectedSite site in _sites)
            {
                ChargeConflict(); if (site.Intersects(bounds)) return true;
            }
            return false;
        }
        private bool ConflictsWithinRecord(FormationBounds bounds, List<FormationPiece> provisional)
        {
            foreach (FormationPiece piece in provisional)
            { ChargeConflict(); if (bounds.Intersects(piece.Bounds)) return true; }
            return false;
        }
        private void ChargeConflict()
        {
            _cancellation.ThrowIfCancellationRequested();
            if (_conflicts == _limits.MaxConflictTests) throw new FormationCapacityException("Conservative site/conflict comparison budget exceeded.");
            _conflicts++;
        }

        private Sample Vertex(CubeVertexKey key)
        {
            if (_regionalSurvey != null)
            {
                if (_regionalSurvey.TryGetValue(key, out Sample existing)) return existing;
                if (_regionalSurvey.Count == _regionalVertexLimit)
                    throw new FormationCapacityException("Regional survey vertex reservation exceeded.");
                Sample sample = SampleAt(Direction(key)); _regionalSurvey.Add(key, sample); _vertices++; return sample;
            }
            (int face, int x, int y) = CanonicalCoordinates(key, _level);
            int index = face * _stride * _stride + y * _stride + x;
            if (!_sampled[index]) { _survey[index] = SampleAt(Direction(key)); _sampled[index] = true; _vertices++; }
            return _survey[index];
        }
        private Sample SampleAt(Vector3D direction)
        {
            _cancellation.ThrowIfCancellationRequested();
            double height = Height(direction); Vector3D point = direction * (_terrain.Radius + height);
            double ax = Math.Abs(direction.X), ay = Math.Abs(direction.Y), az = Math.Abs(direction.Z);
            Vector3D axis = ax <= ay && ax <= az ? Vector3D.UnitX : ay <= az ? Vector3D.UnitY : Vector3D.UnitZ;
            Vector3D right = Unit(Vector3D.Cross(axis, direction)), forward = Vector3D.Cross(direction, right);
            double step = _settings.GradientProbeMetres / _terrain.Radius;
            Vector3D rp = Unit(direction + right * step), rm = Unit(direction - right * step);
            Vector3D fp = Unit(direction + forward * step), fm = Unit(direction - forward * step);
            double runR = (rp - rm).Length * _terrain.Radius, runF = (fp - fm).Length * _terrain.Radius;
            if (runR <= 0 || runF <= 0) throw new FormationValidationException("Gradient probe is below representable radial resolution.");
            double dr = (Height(rp) - Height(rm)) / runR, df = (Height(fp) - Height(fm)) / runF;
            double slope = Math.Sqrt(dr * dr + df * df);
            if (!double.IsFinite(slope)) throw new FormationValidationException("Nonfinite measured terrain rise/run.");
            Vector3D downhill = slope > 0 ? -(right * dr + forward * df) / slope : right;
            ChargeTerrain(); double water = _terrain.WaterLevelAt(direction);
            ChargeTerrain(); GradedTerrainFields fields = _terrain.RawFieldsAt(direction);
            FormationInput.Key(fields.ProfileKey, nameof(fields));
            if (double.IsNaN(water) || double.IsPositiveInfinity(water) || !double.IsFinite(fields.TemperatureC) || !double.IsFinite(fields.Moisture) ||
                fields.Moisture < 0 || fields.Moisture > 1 || !double.IsFinite(fields.ForestPotential) || fields.ForestPotential < 0 || fields.ForestPotential > 1)
                throw new FormationValidationException("Malformed finalized raw terrain fields.");
            return new Sample(point, downhill, slope, height > water && fields.ProfileKey == _settings.TerrainProfileKey);
        }
        private Vector3D Point(Vector3D direction) => direction * (_terrain.Radius + Height(direction));
        private double Height(Vector3D direction)
        {
            // Survey stencils are finite explicit probes around admitted vertices, including the outer fringe.
            // Missing fit evidence is never a rejected rival that a larger request might accept instead.
            if (_fitting) RequireRegion(new FormationBounds(direction, direction));
            ChargeTerrain(); double height = _terrain.HeightAt(direction), radius = _terrain.Radius + height;
            if (!double.IsFinite(height) || !double.IsFinite(radius) || radius <= 0 || !double.IsFinite(radius * radius))
                throw new FormationValidationException("Graded height must produce a finite positive radial point.");
            return height;
        }

        private void RequireRegion(FormationBounds bounds, bool sourceEvidence = false)
        {
            if (!RegionContains(bounds, sourceEvidence))
                throw new FormationIncompleteRegionException("The explicit survey region does not contain complete fitting or neighbor-conflict evidence.");
        }

        /// <summary>
        /// Conservative continuous coverage of the entire AABB's radial shadow by admitted face cells.
        /// Unlike corner lookup, this tests every covered cell and therefore cannot bridge a region hole.
        /// This is a regional domain gate, not a terrain-contact or obstruction-clearance certificate.
        /// </summary>
        private bool RegionContains(FormationBounds bounds, bool sourceEvidence = false)
        {
            if (_region == null) return true;
            // Valid unique cells covering every face leave no excluded angular domain. This also preserves
            // the unrestricted whole-planet contract for a canonical full-region control.
            if (_region.Cells.Length == 6 * _n * _n) return true;
            return ShadowCells(bounds, requiredSource: null, sourceEvidence: sourceEvidence);
        }

        private bool ShadowCells(FormationBounds bounds, TileKey? requiredSource, bool sourceEvidence = false)
        {
            if (bounds.Min.X <= 0 && bounds.Max.X >= 0 && bounds.Min.Y <= 0 && bounds.Max.Y >= 0 &&
                bounds.Min.Z <= 0 && bounds.Max.Z >= 0) return false;
            bool coveredFace = false;
            for (int face = 0; face < 6; face++)
            {
                _cancellation.ThrowIfCancellationRequested();
                (double n0, double n1) = Interval(CubeSphere.Normal(face));
                (double u0, double u1) = Interval(CubeSphere.AxisU(face));
                (double v0, double v1) = Interval(CubeSphere.AxisV(face));
                double uMinAbs = MinAbs(u0, u1), vMinAbs = MinAbs(v0, v1);
                // Keep equality: a face seam belongs to both possible dominant faces for this proof.
                if (n1 <= 0 || n1 < uMinAbs || n1 < vMinAbs) continue;
                double denominator = Math.Max(n0, Math.Max(uMinAbs, vMinAbs));
                if (denominator <= 0 || !double.IsFinite(denominator)) return false;
                if (!Ratios(u0, u1, denominator, n1, out double uLow, out double uHigh) ||
                    !Ratios(v0, v1, denominator, n1, out double vLow, out double vHigh)) return false;
                uLow = Math.Max(-1, uLow); uHigh = Math.Min(1, uHigh);
                vLow = Math.Max(-1, vLow); vHigh = Math.Min(1, vHigh);
                if (uLow > uHigh || vLow > vHigh) continue;
                coveredFace = true;
                (int x0, int x1) = Cells(uLow, uHigh);
                (int y0, int y1) = Cells(vLow, vHigh);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    ChargeRegionCell();
                    var cell = new TileKey(face, _level, x, y);
                    if (requiredSource.HasValue)
                    {
                        if (SourceInClosure(cell, requiredSource.Value)) return true;
                    }
                    else if (!_region!.ContainsCell(cell) || (sourceEvidence && !EvidenceClosure(cell))) return false;
                }
            }
            return !requiredSource.HasValue && coveredFace;

            (double Low, double High) Interval(Vector3D axis)
            {
                double sign = axis.X != 0 ? axis.X : axis.Y != 0 ? axis.Y : axis.Z;
                double low = axis.X != 0 ? bounds.Min.X : axis.Y != 0 ? bounds.Min.Y : bounds.Min.Z;
                double high = axis.X != 0 ? bounds.Max.X : axis.Y != 0 ? bounds.Max.Y : bounds.Max.Z;
                return sign > 0 ? (low, high) : (-high, -low);
            }
            (int First, int Last) Cells(double low, double high)
            {
                // First upper grid line >= low and last lower grid line <= high. Inclusive touching
                // requires both adjacent cells, with division rounded outward by Ratios below.
                int left = 0, right = _n + 1;
                while (left < right)
                { int middle = left + (right - left) / 2; if (_regionWarp[middle] < low) left = middle + 1; else right = middle; }
                int first = Math.Clamp(left - 1, 0, _n - 1);
                left = 0; right = _n + 1;
                while (left < right)
                { int middle = left + (right - left) / 2; if (_regionWarp[middle] <= high) left = middle + 1; else right = middle; }
                return (first, Math.Clamp(left - 1, 0, _n - 1));
            }
            static double MinAbs(double low, double high) => low <= 0 && high >= 0 ? 0 : Math.Min(Math.Abs(low), Math.Abs(high));
            static bool Ratios(double a, double b, double lowDenominator, double highDenominator, out double low, out double high)
            {
                double aa = a / lowDenominator, ab = a / highDenominator;
                double ba = b / lowDenominator, bb = b / highDenominator;
                low = Math.BitDecrement(Math.Min(Math.Min(aa, ab), Math.Min(ba, bb)));
                high = Math.BitIncrement(Math.Max(Math.Max(aa, ab), Math.Max(ba, bb)));
                return double.IsFinite(low) && double.IsFinite(high);
            }
        }

        private bool SourceInClosure(TileKey cell, TileKey source)
        {
            if (cell.Equals(source)) return true;
            for (int edge = 0; edge < 4; edge++)
            {
                ChargeRegionCell(); TileKey one = CubeTopology.Neighbor(cell, (CubeEdge)edge);
                if (one.Equals(source)) return true;
                for (int second = 0; second < 4; second++)
                {
                    ChargeRegionCell();
                    if (CubeTopology.Neighbor(one, (CubeEdge)second).Equals(source)) return true;
                }
            }
            return false;
        }

        private bool EvidenceClosure(TileKey cell)
        {
            // Per-build proof reuse only; no proposals, priorities or world state are cached here.
            if (_sourceEvidenceInterior.Contains(cell)) return true;
            for (int edge = 0; edge < 4; edge++)
            {
                ChargeRegionCell(); TileKey one = CubeTopology.Neighbor(cell, (CubeEdge)edge);
                if (!_region!.ContainsCell(one)) return false;
                for (int second = 0; second < 4; second++)
                {
                    ChargeRegionCell();
                    if (!_region.ContainsCell(CubeTopology.Neighbor(one, (CubeEdge)second))) return false;
                }
            }
            if (_sourceEvidenceInterior.Count == _region!.Cells.Length)
                throw new FormationCapacityException("Regional source-evidence cache exceeds its admitted cell count.");
            _sourceEvidenceInterior.Add(cell); return true;
        }

        private void ChargeRegionCell()
        {
            _cancellation.ThrowIfCancellationRequested();
            if (_regionCellTests == _limits.MaxRegionCellTests)
                throw new FormationCapacityException("Angular containment/source-evidence cell budget exceeded.");
            _regionCellTests++;
        }
        private void ChargeTerrain()
        {
            _cancellation.ThrowIfCancellationRequested();
            if (_terrainSamples == _limits.MaxTerrainSamples) throw new FormationCapacityException("Finalized terrain query budget exceeded.");
            _terrainSamples++;
        }
    }

    private static (int Face, int X, int Y) CanonicalCoordinates(CubeVertexKey key, int gridLevel)
    {
        int scale = 1 << (gridLevel - key.Level), n = 1 << gridLevel;
        var cube = new Vector3D(key.X * scale, key.Y * scale, key.Z * scale);
        for (int face = 0; face < 6; face++) if (Vector3D.Dot(cube, CubeSphere.Normal(face)) == n)
            return (face, ((int)Vector3D.Dot(cube, CubeSphere.AxisU(face)) + n) / 2,
                ((int)Vector3D.Dot(cube, CubeSphere.AxisV(face)) + n) / 2);
        throw new FormationValidationException("Invalid canonical cube vertex.");
    }
    private static Vector3D Direction(CubeVertexKey key)
    {
        // Normalized keys are cube coordinates, not necessarily vertices of a grid at key.Level.
        // For example (0,0,1,level0) is the +Z face centre; integer grid division would move it.
        int n = 1 << key.Level;
        var cube = new Vector3D(key.X, key.Y, key.Z);
        for (int face = 0; face < 6; face++)
            if (Vector3D.Dot(cube, CubeSphere.Normal(face)) == n)
                return CubeSphere.Direction(face, Vector3D.Dot(cube, CubeSphere.AxisU(face)) / n,
                    Vector3D.Dot(cube, CubeSphere.AxisV(face)) / n);
        throw new FormationValidationException("Invalid canonical cube direction.");
    }
    private static Vector3D Unit(Vector3D value)
    {
        double scale = Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
        if (!double.IsFinite(scale) || scale <= 0) throw new FormationValidationException("A planner direction is zero or nonfinite.");
        return (value / scale).Normalized;
    }
    private static FormationBounds Bounds(Vector3D[] points)
    {
        Vector3D low = points[0], high = low;
        foreach (Vector3D point in points) { low = FormationBounds.Lower(low, point); high = FormationBounds.Upper(high, point); }
        return new FormationBounds(low, high);
    }
    private static double StripArea(Vector3D[] crest, Vector3D[] toe)
    {
        double area = 0;
        for (int i = 0; i < crest.Length - 1; i++) area += QuadArea(crest[i], crest[i + 1], toe[i], toe[i + 1]);
        if (!double.IsFinite(area)) throw new FormationValidationException("Nonfinite intended strip area.");
        return area;
    }
    private static double QuadArea(Vector3D a, Vector3D b, Vector3D c, Vector3D d) =>
        0.5 * (Vector3D.Cross(b - a, c - a).Length + Vector3D.Cross(d - b, c - b).Length);
    private static bool TriangleRay(Vector3D origin, Vector3D direction, double length, Vector3D a, Vector3D b, Vector3D c)
    {
        Vector3D normal = FormationGeometryIndex.WindingNormal(a, b, c);
        double denominator = Vector3D.Dot(normal, direction);
        if (denominator == 0) return false;
        double distance = Vector3D.Dot(normal, a - origin) / denominator;
        if (!double.IsFinite(distance) || distance < 0 || distance > length) return false;
        Vector3D p = origin + direction * distance;
        // Dominant-axis barycentrics retain the actual transformed triangle, including its authored winding.
        double ax = Math.Abs(normal.X), ay = Math.Abs(normal.Y), az = Math.Abs(normal.Z);
        int axis = ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
        double Orient(Vector3D x, Vector3D y, Vector3D z)
        {
            double xu = axis == 0 ? x.Y : x.X, xv = axis == 2 ? x.Y : x.Z;
            double yu = axis == 0 ? y.Y : y.X, yv = axis == 2 ? y.Y : y.Z;
            double zu = axis == 0 ? z.Y : z.X, zv = axis == 2 ? z.Y : z.Z;
            return (yu - xu) * (zv - xv) - (yv - xv) * (zu - xu);
        }
        double area = Orient(a, b, c), u = Orient(b, c, p), v = Orient(c, a, p), w = Orient(a, b, p);
        return double.IsFinite(area) && double.IsFinite(u) && double.IsFinite(v) && double.IsFinite(w) &&
            (area > 0 ? u >= 0 && v >= 0 && w >= 0 : area < 0 && u <= 0 && v <= 0 && w <= 0);
    }
    private readonly record struct Sample(Vector3D Point, Vector3D Downhill, double Slope, bool Eligible);
    private sealed class Link(CubeEdgeKey key, Vector3D point)
    { internal CubeEdgeKey Key { get; } = key; internal Vector3D Point { get; } = point; internal List<int> Segments { get; } = new(); }
    private readonly record struct Segment(int Face, int X, int Y, int EdgeA, int EdgeB, int A, int B);
    private sealed record Candidate(FormationContourIdentity Identity, Vector3D[] Crest, bool TooLong, TileKey SourceCell);
}
