using System;
using System.Collections.Generic;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Construction-only owner. Its graded source, temporary plan and work tables never enter the result.</summary>
internal sealed class FormationFieldBuilder
{
    private readonly IGradedTerrain _terrain;
    private readonly TerrainLattice _lattice;
    private readonly FormationWorldSelection _selection;
    private readonly FormationFieldContract _contract;
    private readonly FormationFieldWork _work;
    private readonly HashSet<(string Key, int Revision)> _selected = new();

    private FormationFieldBuilder(IGradedTerrain terrain, TerrainLattice lattice, FormationWorldSelection selection,
        FormationFieldLimits limits, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(terrain); ArgumentNullException.ThrowIfNull(lattice);
        ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(limits);
        cancellation.ThrowIfCancellationRequested();
        _terrain = terrain; _lattice = lattice; _selection = selection;
        _contract = selection.FieldContract ?? throw new FormationValidationException("A sealed field requires an explicit output contract.");
        _contract.Validate(lattice);
        if (!terrain.IsFinalized || terrain.Radius != lattice.Radius)
            throw new FormationValidationException("Field construction requires matching finalized graded terrain.");
        if (selection.Settings.Region != null)
            throw new FormationValidationException("The initial sealed field requires a whole-world proposal plan.");
        _work = new FormationFieldWork(limits, cancellation);
        FormationRecipe recipe = selection.Catalog.RequireRecipe(selection.Settings.RecipeKey, selection.Settings.RecipeRevision);
        foreach (FormationRecipeSlot slot in recipe.Slots)
        foreach (FormationGeometryChoice choice in slot.Alternatives)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!_selected.Add((choice.Key, choice.Revision))) continue;
            CliffSupportDefinition definition = selection.Catalog.RequireQualified(choice.Key, choice.Revision);
            if (definition.ContactLoops.Length == 0)
                throw new FormationValidationException("Every selected physical alternative needs qualified closed contact loops.");
        }
    }

    internal static FormationField Build(IGradedTerrain terrain, TerrainLattice lattice, FormationWorldSelection selection,
        FormationFieldLimits limits, CancellationToken cancellation)
    {
        var builder = new FormationFieldBuilder(terrain, lattice, selection, limits, cancellation);
        FormationPlan plan = FormationPlanner.Build(terrain, selection.Catalog, selection.Settings, limits.Planner, cancellation);
        return builder.Complete(plan);
    }

    /// <summary>Internal stage boundary for deterministic focused fixtures; the public path always owns the planner.</summary>
    internal static FormationField BuildPlanned(IGradedTerrain terrain, TerrainLattice lattice, FormationWorldSelection selection,
        FormationFieldLimits limits, FormationPlan plan, CancellationToken cancellation = default) =>
        new FormationFieldBuilder(terrain, lattice, selection, limits, cancellation).Complete(plan);

    private FormationField Complete(FormationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var pieces = new List<FormationPiece>(); var cohorts = new List<FormationCohort>();
        var rejections = new List<FormationFieldRejection>(); int proposed = 0;
        foreach (FormationPlanEntry entry in plan.Entries)
        {
            _work.Check();
            if (entry.Record.RecipeKey != _selection.Settings.RecipeKey || entry.Record.RecipeRevision != _selection.Settings.RecipeRevision)
                throw new FormationValidationException("Proposal recipe does not match the selected field.");
            var admitted = new List<FormationPiece>(); var receipts = new List<FormationContactSealReceipt>();
            bool rejected = false;
            foreach (FormationPiece piece in entry.Record.Pieces)
            {
                if (proposed == _work.Limits.MaxPieces) throw new FormationCapacityException("Field proposal piece limit exceeded.");
                proposed++;
                if (!_selected.Contains((piece.Geometry.Key, piece.Geometry.Revision)))
                    throw new FormationValidationException("A proposed piece is not one of the selected alternatives.");
                CliffSupportDefinition definition = _selection.Catalog.RequireQualified(piece.Geometry.Key, piece.Geometry.Revision);
                if (!piece.Geometry.SameDefinition(definition.Geometry) || piece.Transform.Scale < definition.MinimumScale ||
                    piece.Transform.Scale > definition.MaximumScale)
                    throw new FormationValidationException("A proposed physical definition or scale differs from its qualification.");
                int receiptStart = receipts.Count; bool sealedPiece = true;
                foreach (FormationContactLoop loop in definition.ContactLoops)
                {
                    _work.Loop();
                    if (loop.VertexIndices.Length > _work.Limits.Seal.MaxLoopVertices)
                        throw new FormationCapacityException("Closed loop exceeds the supplied seal allowance.");
                    var points = new Vector3D[loop.VertexIndices.Length];
                    for (int i = 0; i < points.Length; i++)
                    {
                        _work.Check(); points[i] = piece.Transform.Transform(piece.Geometry.Vertices[loop.VertexIndices[i]]);
                        FormationFieldTerrain.ValidatePoint(points[i]);
                    }
                    var source = new FormationFieldTerrain(_terrain, _lattice, _work);
                    TerrainSealTriangle[] candidates = source.Candidates(points);
                    FormationTerrainSealResult seal = FormationTerrainSeal.Evaluate(points, candidates,
                        FormationFieldContract.MinimumPlaneBurialMetres, _work.RemainingSealLimits(), _work.Cancellation);
                    _work.ChargeSeal(seal.Diagnostics);
                    if (seal.Status == FormationQueryStatus.Cancelled) throw new OperationCanceledException(_work.Cancellation);
                    if (seal.Status == FormationQueryStatus.CapacityExceeded)
                        throw new FormationCapacityException("Continuous seal exceeded the per-loop or remaining field budget.");
                    if (!seal.IsSealed)
                    {
                        if (seal.Rejection != FormationTerrainSealRejection.IncompleteCoverage)
                            throw new FormationValidationException("Malformed field seal inputs: " + seal.Rejection);
                        Reject(piece.Id, loop.Key, FormationFieldRejectionReason.IncompleteSeal, seal.FailedSegment);
                        sealedPiece = false; rejected = true; break;
                    }
                    _work.Receipt();
                    _work.Dependencies(seal.Dependencies.Length);
                    var (pins, tiles) = source.Guards(seal.Dependencies);
                    receipts.Add(new FormationContactSealReceipt(piece, loop, _contract,
                        _selection.Identity.PhysicalContentDigest, _selection.Identity.GenerationDigest,
                        seal.Dependencies.ToArray(), pins, tiles, seal.Diagnostics));
                }
                if (sealedPiece) admitted.Add(piece);
                else receipts.RemoveRange(receiptStart, receipts.Count - receiptStart);
            }
            if (rejected && _contract.AdmissionPolicy == FormationFieldAdmissionPolicy.WholePlannedFormation)
            {
                foreach (FormationPiece piece in admitted)
                    Reject(piece.Id, "", FormationFieldRejectionReason.FormationPolicy, -1);
                continue;
            }
            if (admitted.Count == 0) continue;
            if (cohorts.Count == _work.Limits.MaxCohorts) throw new FormationCapacityException("Field cohort limit exceeded.");
            FormationCohort cohort = CreateCohort(entry.Record.Id, admitted, receipts);
            cohorts.Add(cohort); pieces.AddRange(admitted);
        }
        pieces.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        cohorts.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        rejections.Sort(static (a, b) => a.Piece.CompareTo(b.Piece));
        _work.Check();
        var support = new FormationSupportIndex(pieces, _work.Limits.Support, _work.Cancellation);
        _work.Check();
        return new FormationField(_contract, _selection.Identity.GenerationDigest, pieces.ToArray(), cohorts.ToArray(),
            rejections.ToArray(), support, plan.Diagnostics, _work.Diagnostics(proposed, pieces.Count, cohorts.Count, rejections.Count));

        void Reject(FormationPieceId piece, string loop, FormationFieldRejectionReason reason, int segment)
        {
            if (rejections.Count == _work.Limits.MaxRejections) throw new FormationCapacityException("Field rejection storage limit exceeded.");
            rejections.Add(new FormationFieldRejection(piece, loop, reason, segment));
        }
    }

    private FormationCohort CreateCohort(FormationId id, List<FormationPiece> pieces, List<FormationContactSealReceipt> receipts)
    {
        var witnesses = new Dictionary<(ulong Cell, int Half), TerrainLatticeTriangle>();
        var pins = new Dictionary<CubeVertexKey, Vector3D>(); var tiles = new HashSet<TileKey>();
        foreach (FormationContactSealReceipt receipt in receipts)
        {
            foreach (TerrainLatticeTriangle primitive in receipt.Witnesses)
            {
                _work.Check(); var key = (primitive.Cell.Packed, primitive.Half);
                if (witnesses.ContainsKey(key)) continue;
                _work.Dependencies(1); witnesses.Add(key, primitive);
            }
            foreach (FormationTerrainPin pin in receipt.Pins)
            {
                _work.Check();
                if (pins.TryGetValue(pin.Vertex, out Vector3D previous))
                {
                    if (pin.Position != previous) throw new FormationValidationException("Finalized terrain changed between seal receipts.");
                    continue;
                }
                _work.Pin(); pins.Add(pin.Vertex, pin.Position);
            }
            foreach (TileKey tile in receipt.DependencyTiles)
            {
                _work.Check(); if (tiles.Contains(tile)) continue;
                _work.Tile(); tiles.Add(tile);
            }
        }
        var orderedWitnesses = new TerrainLatticeTriangle[witnesses.Count]; witnesses.Values.CopyTo(orderedWitnesses, 0);
        Array.Sort(orderedWitnesses, FormationFieldTerrain.ComparePrimitive);
        return new FormationCohort(id, pieces.ToArray(), receipts.ToArray(), orderedWitnesses,
            FormationFieldTerrain.SortedPins(pins), FormationFieldTerrain.SortedTiles(tiles));
    }
}

/// <summary>One monotonic ledger shared by every candidate producer and exact seal call, including rejected work.</summary>
internal sealed class FormationFieldWork
{
    internal FormationFieldWork(FormationFieldLimits limits, CancellationToken cancellation)
    {
        Limits = limits; Cancellation = cancellation;
        (_resident, _scratch) = limits.Reserve();
        // Validate every supplied seal limit even when no planned pieces exist, without consuming build work.
        _ = FormationTerrainSeal.Evaluate(ReadOnlySpan<Vector3D>.Empty, ReadOnlySpan<TerrainSealTriangle>.Empty,
            FormationFieldContract.MinimumPlaneBurialMetres, limits.Seal, cancellation);
        Check();
    }
    internal FormationFieldLimits Limits { get; }
    internal CancellationToken Cancellation { get; }
    private readonly long _resident, _scratch;
    private int _loops, _receipts, _dependencies, _pins, _tiles;
    private long _cells, _samples, _candidateTests, _attempts, _operations;
    internal void Check() => Cancellation.ThrowIfCancellationRequested();
    internal void Loop() { Check(); Charge(ref _loops, 1, Limits.MaxLoopChecks, "loop checks"); }
    internal void Receipt() => Charge(ref _receipts, 1, Limits.MaxReceipts, "receipts");
    internal void Dependencies(int count) => Charge(ref _dependencies, count, Limits.MaxDependencyTriangles, "dependency triangles");
    internal void Pin() => Charge(ref _pins, 1, Limits.MaxPinnedVertices, "pinned vertices");
    internal void Tile() => Charge(ref _tiles, 1, Limits.MaxDependencyTiles, "dependency tiles");
    internal void Cell() { Check(); Charge(ref _cells, 1, Limits.MaxCandidateCellVisits, "candidate/guard cell visits"); }
    internal void Sample() { Check(); Charge(ref _samples, 1, Limits.MaxTerrainVertexSamples, "canonical height samples"); }
    internal void CandidateTest() { Check(); Charge(ref _candidateTests, 1, Limits.MaxCandidateTriangleTests, "candidate radial tests"); }
    internal FormationTerrainSealLimits RemainingSealLimits() => Limits.Seal with
    {
        MaxSegmentTriangleAttempts = (int)Math.Min(Limits.Seal.MaxSegmentTriangleAttempts, Limits.MaxSegmentTriangleAttempts - _attempts),
        MaxExactArithmeticOperations = (int)Math.Min(Limits.Seal.MaxExactArithmeticOperations, Limits.MaxExactArithmeticOperations - _operations),
        MaxDependencyTriangles = Math.Min(Limits.Seal.MaxDependencyTriangles, Limits.MaxDependencyTriangles - _dependencies)
    };
    internal void ChargeSeal(FormationTerrainSealDiagnostics diagnostics)
    {
        Charge(ref _attempts, diagnostics.SegmentTriangleAttempts, Limits.MaxSegmentTriangleAttempts, "seal attempts");
        Charge(ref _operations, diagnostics.ExactArithmeticOperations, Limits.MaxExactArithmeticOperations, "exact arithmetic");
    }
    internal FormationFieldDiagnostics Diagnostics(int proposed, int accepted, int cohorts, int rejected) =>
        new(proposed, accepted, _loops, _receipts, cohorts, rejected, _cells, _samples, _candidateTests, _attempts, _operations,
            _dependencies, _pins, _tiles, _resident, _scratch);
    private static void Charge(ref int used, int count, int maximum, string category)
    { if (count < 0 || count > maximum - used) throw new FormationCapacityException("Field aggregate " + category + " allowance exceeded."); used += count; }
    private static void Charge(ref long used, long count, long maximum, string category)
    { if (count < 0 || count > maximum - used) throw new FormationCapacityException("Field aggregate " + category + " allowance exceeded."); used += count; }
}
