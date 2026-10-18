using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Settings;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    /// <summary>Isolated native lifecycle evidence; no production asset, field publication or visual-quality claim.</summary>
    private async Task CheckTerrainCohorts()
    {
        var terrain = new TerrainLod { Name = "CohortFixtureTerrain", Hurry = true };
        var ancestor = new Node3D { Name = "CohortFixtureAncestor" };
        AddChild(ancestor);
        ancestor.AddChild(terrain);
        var sources = new List<CohortProbeSurface>();
        var retired = new List<Task>();
        using var shader = new Shader { Code = "shader_type spatial; void fragment() { ALBEDO = vec3(0.3,0.4,0.2); }" };
        using var material = new ShaderMaterial { Shader = shader };
        var frame = new RenderFrame(Vector3D.Zero);
        Vector3D distant = new(100_000, 0, 0);
        TerrainViewLimits limits = CohortProbeLimits();
        int maximumOutstanding = 0, maximumNodes = 0;
        bool allReservationsFit = true;
        try
        {
            var source = NewSource(120, 'a');
            terrain.Setup(source, frame, material, null, limits);
            await Settle(distant);
            source.Block = true; source.Entered.Reset();
            TerrainCohortTicket first = Acquire(source.First);
            Pump(distant);
            await Until(() => source.Entered.IsSet, "cohort child build starts and blocks");
            Expect("cohort blocked work keeps parent fallback and stays waiting",
                terrain.GetCohortState(first) == TerrainCohortState.Waiting && terrain.DrawnTiles == 6 &&
                terrain.CohortDiagnostics.OutstandingJobsIncludingRetired == 1 && terrain.CohortDiagnostics.DeferredByCapacity > 0,
                "one outstanding slot covers queued, running and completed work; every root remains visible");
            source.Block = false; source.Continue.Set();
            await Until(() => terrain.CohortDiagnostics.CompletedJobs == 1, "cohort result completes before upload");
            Expect("cohort completed worker output is not native readiness",
                terrain.GetCohortState(first) == TerrainCohortState.Waiting && terrain.CohortDiagnostics.UploadedNodes == 6,
                "no terrain update has consumed the completed result");
            Pump(distant);
            Expect("cohort uploaded hidden sibling cannot satisfy readiness",
                terrain.GetCohortState(first) == TerrainCohortState.Waiting && terrain.CohortDiagnostics.UploadedNodes == 7 && terrain.DrawnTiles == 6,
                "one child is uploaded while its sibling quartet is incomplete; parent remains drawn");
            await Until(() => { Pump(distant); return terrain.GetCohortState(first) == TerrainCohortState.Ready; }, "cohort finest support is shown");
            var records = new List<DrawnTerrainTile>();
            TileKey[] expected = CohortIncidentLeaves(source);
            bool copied = terrain.CopyContactTiles(distant, 200_000, records);
            Expect("cohort closure includes every cube-corner face copy",
                expected.Length == 3 && expected.Select(key => key.Face).Distinct().Count() == 3 && copied &&
                expected.All(key => records.Any(tile => tile.Key.Equals(key))) && terrain.CohortDiagnostics.FinestTiles == 3,
                "the synthetic field deliberately declares one tile; exact incident pins recover all three finest face copies");
            TerrainCohortDiagnostics one = terrain.CohortDiagnostics;
            TerrainCohortTicket second = Acquire(source.Second);
            Expect("two cohort tickets share all terrain reservations",
                terrain.GetCohortState(second) == TerrainCohortState.Ready && terrain.CohortDiagnostics.Tickets == 2 &&
                terrain.CohortDiagnostics.ForcedNodes == one.ForcedNodes && terrain.CohortDiagnostics.ReservedNodes == one.ReservedNodes,
                "different formation identities hold the same canonical terrain closure without duplicating nodes");
            terrain.Visible = false;
            Expect("hidden terrain cannot provide ready cohort support", terrain.GetCohortState(first) == TerrainCohortState.Waiting,
                "local child visibility and retained uploaded meshes do not imply effective visibility");
            terrain.Visible = true;
            Expect("restored terrain recovers existing cohort readiness", terrain.GetCohortState(first) == TerrainCohortState.Ready,
                "the same current epoch and finest instances are visible again");
            ancestor.Visible = false;
            Expect("hidden ancestor cannot provide ready cohort support", terrain.GetCohortState(first) == TerrainCohortState.Waiting,
                "visibility is checked through the entire scene ancestry");
            ancestor.Visible = true;
            Expect("restored ancestor recovers existing cohort readiness", terrain.GetCohortState(first) == TerrainCohortState.Ready,
                "no ticket replacement or rescheduling is needed after visibility restoration");
            bool hideThrew = false;
            try { terrain.ReleaseCohort(first, () => throw new InvalidOperationException("Expected synthetic hide failure.")); }
            catch (InvalidOperationException) { hideThrew = true; }
            Expect("failed hide retains the complete cohort reservation", hideThrew && terrain.CohortDiagnostics.Tickets == 2 &&
                terrain.GetCohortState(first) == TerrainCohortState.Ready, "references are unchanged when the caller cannot hide its formation");
            bool heldWhileHiding = false;
            terrain.ReleaseCohort(first, () => heldWhileHiding = terrain.GetCohortState(first) == TerrainCohortState.Ready);
            Expect("hide precedes release and shared ticket stays ready", heldWhileHiding &&
                terrain.GetCohortState(first) == TerrainCohortState.Retired && terrain.GetCohortState(second) == TerrainCohortState.Ready &&
                terrain.CohortDiagnostics.ForcedNodes == one.ForcedNodes, "releasing the first consumer cannot prune the second consumer's support");
            float detail = GraphicsSettings.Current.TerrainDetail;
            try
            {
                foreach (float value in new[] { 0.5f, 2f })
                {
                    GraphicsSettings.Current.TerrainDetail = value;
                    Pump(new Vector3D(-121.7, 0, 0)); Pump(distant);
                    Expect($"cohort survives camera and terrain-detail {value} pruning",
                        terrain.GetCohortState(second) == TerrainCohortState.Ready,
                        "forced descendants ignore ordinary camera merge and graphics-detail changes");
                }
            }
            finally { GraphicsSettings.Current.TerrainDetail = detail; }
            frame.Move(new Vector3D(7, -3, 2)); terrain.Reanchor();
            Expect("cohort body-space ownership survives reanchor", terrain.GetCohortState(second) == TerrainCohortState.Ready,
                "instances are reanchored while the immutable support identity remains current");
            terrain.ReleaseCohort(second, () => { });
            await Settle(distant);
            Expect("last cohort release permits ordinary pruning and reservation recovery",
                terrain.DrawnTiles == 6 && terrain.CohortDiagnostics.ForcedNodes == 0 && terrain.CohortDiagnostics.ReservedNodes == 6,
                "no whole-world finest preload remains after the last visible formation retires");
            ulong nativeEpoch = terrain.CohortDiagnostics.Epoch;
            bool nativeRefused = false;
            try
            {
                terrain.Setup(source, frame, material, null, limits with
                {
                    MaxTotalNodes = 6,
                    MaxEstimatedResidentBytes = 2L * 1024 * 1024 + 4096L * 6 + 6L * TerrainLod.EstimatedResidentBytesPerNode,
                });
            }
            catch (InvalidOperationException) { nativeRefused = true; }
            Expect("native instance retirement is reserved before destructive replacement", nativeRefused &&
                terrain.CohortDiagnostics.Epoch == nativeEpoch && terrain.DrawnTiles == 6,
                "a roots-only byte allowance cannot hide the previous native instance pool until end-of-frame disposal");
            await Retire();
            Expect("queued native retirement remains visible in stopped diagnostics",
                terrain.CohortDiagnostics.EstimatedResidentBytes >= terrain.CohortDiagnostics.RetiringNativeInstances * TerrainLod.EstimatedPoolInstanceBytes,
                "empty retiring instance handles retain their accounting until observed freed; material references were cleared synchronously");

            var failed = NewSource(130, 'c');
            terrain.Setup(failed, frame, material, null, limits);
            await Settle(distant);
            failed.Fault = true;
            TerrainCohortTicket broken = Acquire(failed.First);
            await Until(() => { Pump(distant); return terrain.GetCohortState(broken) == TerrainCohortState.Failed; }, "required cohort node failure is published");
            Expect("failed cohort node never becomes ready through a parent fallback",
                terrain.GetCohortState(broken) == TerrainCohortState.Failed && terrain.FailedBuilds > 0 && terrain.DrawnTiles >= 6,
                "the injected worker exception is expected; uploaded parents still cover the affected region");
            failed.Fault = false;
            terrain.ReleaseCohort(broken, () => { });
            await Settle(distant);
            Expect("released failed cohort stops poisoning unrelated readiness", terrain.FailedBuilds == 0 && terrain.DrawnTiles == 6,
                "normal pruning retires the failed descendants without endless automatic retry");
            await Retire();

            foreach (var scenario in new (string Name, TerrainViewLimits Limits)[]
            {
                ("ticket count", limits with { MaxTickets = 0 }),
                ("incident tile count", limits with { MaxTilesPerCohort = 2 }),
                ("distinct tile count", limits with { MaxDistinctCohortTiles = 2 }),
                ("forced path and siblings", limits with { MaxForcedNodes = 5 }),
                ("forced resident bytes", limits with { MaxEstimatedForcedResidentBytes = 0 }),
                ("overall camera and cohort nodes", limits with { MaxTotalNodes = 6 }),
                ("overall resident bytes", limits with { MaxEstimatedResidentBytes = 2L * 1024 * 1024 + 4096L * limits.MaxTotalNodes + 6L * TerrainLod.EstimatedResidentBytesPerNode }),
            })
            {
                var capped = NewSource(140, 'e');
                terrain.Setup(capped, frame, material, null, scenario.Limits);
                await Settle(distant);
                TerrainCohortDiagnostics before = terrain.CohortDiagnostics;
                TerrainCohortAcquireStatus status = terrain.TryAcquireCohort(capped.First, out TerrainCohortTicket? rejected);
                TerrainCohortDiagnostics after = terrain.CohortDiagnostics;
                Expect($"cohort {scenario.Name} capacity rolls back completely", status == TerrainCohortAcquireStatus.CapacityExceeded && rejected == null &&
                    before.Tickets == after.Tickets && before.ForcedNodes == after.ForcedNodes && before.FinestTiles == after.FinestTiles &&
                    before.LiveNodes == after.LiveNodes && before.ReservedNodes == after.ReservedNodes && before.OutstandingJobsIncludingRetired == after.OutstandingJobsIncludingRetired,
                    "no ticket, node, partial demand or queued job survives refusal");
                if (scenario.Name.StartsWith("overall", StringComparison.Ordinal))
                {
                    terrain.UpdateLod(new Vector3D(141.7, 0, 0));
                    Expect($"{scenario.Name} capacity preserves camera parent coverage",
                        terrain.DrawnTiles == 6 && !terrain.IsIdle && terrain.CohortDiagnostics.DeferredByCapacity > 0 && terrain.CohortDiagnostics.LiveNodes == 6,
                        "ordinary camera splitting shares the same total reservation; all six roots remain drawn");
                }
                await Retire();
            }

            var worldA = NewSource(150, '1');
            var worldB = NewSource(250, '2');
            var worldC = NewSource(350, '3');
            TerrainViewLimits replacementLimits = limits with { MaxRetiringSessions = 1 };
            worldA.Block = true; worldA.Entered.Reset();
            terrain.Setup(worldA, frame, material, null, replacementLimits);
            TerrainCohortTicket ticketA = Acquire(worldA.First);
            Pump(distant);
            await Until(() => worldA.Entered.IsSet, "world A's reserved terrain work blocks");
            Task stoppedA = terrain.WorkersStopped; retired.Add(stoppedA);
            terrain.Setup(worldB, frame, material, null, replacementLimits);
            TerrainCohortTicket ticketB = Acquire(worldB.First);
            Pump(distant);
            ulong epochB = terrain.CohortDiagnostics.Epoch;
            Expect("replacement shares the retiring world's work reservation", !stoppedA.IsCompleted &&
                terrain.GetCohortState(ticketA) == TerrainCohortState.Retired && terrain.GetCohortState(ticketB) == TerrainCohortState.Waiting &&
                terrain.CohortDiagnostics.OutstandingJobsIncludingRetired == 1 && terrain.Pending == 0,
                "B cannot submit extra jobs while A owns the one configured slot; no old epoch is ready");
            bool setupRefused = false;
            try { terrain.Setup(worldC, frame, material, null, replacementLimits); }
            catch (InvalidOperationException) { setupRefused = true; }
            Expect("retiring-session capacity refuses before destroying current setup", setupRefused &&
                terrain.CohortDiagnostics.Epoch == epochB && terrain.CohortDiagnostics.Tickets == 1 && terrain.GetCohortState(ticketB) == TerrainCohortState.Waiting,
                "C admission fails while B keeps its identity, ticket and deferred work");
            bool wrongWorldRefused = false;
            try { terrain.TryAcquireCohort(worldA.First, out _); }
            catch (InvalidOperationException) { wrongWorldRefused = true; }
            Expect("equal formation IDs cannot cross world ownership", wrongWorldRefused && terrain.CohortDiagnostics.Tickets == 1,
                "reference identity authenticates the immutable cohort against the selected field");
            worldA.Block = false; worldA.Continue.Set();
            await Until(() => stoppedA.IsCompleted, "world A releases its retiring reservation");
            await Until(() => { Pump(distant); return terrain.GetCohortState(ticketB) == TerrainCohortState.Ready; }, "world B retries and becomes ready");
            var stateB = terrain.CaptureDiagnostics();
            Expect("replacement publishes only its own radius and pin field", stateB.MinimumRadius == worldB.Radius && stateB.MaximumRadius == worldB.Radius &&
                terrain.GetCohortState(ticketA) == TerrainCohortState.Retired,
                "old completed work cannot publish into the replacement tree");
            terrain.ReleaseCohort(ticketB, () => { });
            await Retire();
            terrain.Setup(worldA, frame, material, null, replacementLimits);
            TerrainCohortTicket newA = Acquire(worldA.First);
            await Until(() => { Pump(distant); return terrain.GetCohortState(newA) == TerrainCohortState.Ready; }, "world A returns under a new epoch");
            Expect("A B A retirement never revives an earlier ticket", terrain.GetCohortState(ticketA) == TerrainCohortState.Retired &&
                terrain.GetCohortState(ticketB) == TerrainCohortState.Retired && terrain.GetCohortState(newA) == TerrainCohortState.Ready,
                "even the identical field reference needs a fresh view-epoch ticket");
            Expect("cohort fixture stayed within configured outstanding and node reservations", allReservationsFit && maximumOutstanding <= limits.MaxOutstandingJobs && maximumNodes <= limits.MaxTotalNodes,
                $"observed outstanding={maximumOutstanding}, reserved nodes={maximumNodes}; these are lifecycle counts, not performance timings");
        }
        finally
        {
            foreach (CohortProbeSurface source in sources) { source.Block = false; source.Continue.Set(); }
            retired.Add(terrain.WorkersStopped); terrain.Stop(); terrain.QueueFree();
            ancestor.QueueFree();
            Task drained = Task.WhenAll(retired);
            await Task.WhenAny(drained, Task.Delay(TimeSpan.FromSeconds(25)));
            Expect("cohort fixture drains current and retired sessions", drained.IsCompletedSuccessfully,
                "world fixtures outlive their worker completion tasks");
            if (drained.IsCompleted) foreach (CohortProbeSurface source in sources) source.Dispose();
        }

        CohortProbeSurface NewSource(double radius, char identity)
        { var value = new CohortProbeSurface(radius, identity); sources.Add(value); return value; }
        TerrainCohortTicket Acquire(FormationCohort cohort)
        {
            if (terrain.TryAcquireCohort(cohort, out TerrainCohortTicket? ticket) != TerrainCohortAcquireStatus.Accepted || ticket == null)
                throw new InvalidOperationException("Synthetic cohort was unexpectedly refused.");
            return ticket;
        }
        void Pump(Vector3D camera)
        {
            terrain.UpdateLod(camera);
            TerrainCohortDiagnostics state = terrain.CohortDiagnostics;
            maximumOutstanding = Math.Max(maximumOutstanding, state.OutstandingJobsIncludingRetired);
            maximumNodes = Math.Max(maximumNodes, state.ReservedNodes);
            allReservationsFit &= state.EstimatedResidentBytes <= limits.MaxEstimatedResidentBytes && state.ReservedWorkBytes <= limits.MaxEstimatedWorkBytes;
        }
        async Task Settle(Vector3D camera) => await Until(() => { Pump(camera); return terrain.IsIdle; }, "bounded cohort fixture terrain settles");
        async Task Retire()
        {
            Task completion = terrain.WorkersStopped; retired.Add(completion); terrain.Stop();
            await Until(() => completion.IsCompleted, "cohort fixture setup retires");
            // This fixture's tight byte-limit cases deliberately start after deferred native disposal.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private static TerrainViewLimits CohortProbeLimits() => new()
    {
        MaxTickets = 4, MaxTilesPerCohort = 8, MaxDistinctCohortTiles = 16, MaxForcedNodes = 64, MaxTotalNodes = 64,
        MaxOutstandingJobs = 1, MaxRetiringSessions = 2,
        MaxEstimatedForcedResidentBytes = 32L * 1024 * 1024, MaxEstimatedResidentBytes = 40L * 1024 * 1024,
        MaxEstimatedWorkBytes = TerrainLod.EstimatedWorkBytesPerJob,
    };

    private static TileKey[] CohortIncidentLeaves(CohortProbeSurface source)
    {
        var found = new HashSet<TileKey>();
        Span<TileKey> cells = stackalloc TileKey[4];
        int count = source.TerrainLattice.IncidentCells(source.Corner.Vertex, cells);
        for (int i = 0; i < count; i++)
        {
            TileKey key = cells[i];
            while (key.Level > source.TerrainLattice.FinestTileLevel) key = key.Parent;
            found.Add(key);
        }
        return found.OrderBy(key => key.Packed).ToArray();
    }

    private sealed class CohortProbeSurface : ISurface, IGradedTerrain, ITerrainLatticeSurface, ITerrainPinnedSurface, IFormationSurface, IDisposable
    {
        public double Radius { get; }
        public double SeaRadius => 0;
        public bool IsFinalized => true;
        public TerrainLattice TerrainLattice { get; }
        public TerrainPinSet TerrainPins { get; }
        public FormationField Formations { get; }
        public FormationCohort First { get; }
        public FormationCohort Second { get; }
        public FormationTerrainPin Corner { get; }
        public readonly ManualResetEventSlim Entered = new(false), Continue = new(false);
        public volatile bool Block, Fault;
        public CohortProbeSurface(double radius, char identity)
        {
            Radius = radius; TerrainLattice = new TerrainLattice(radius, 2);
            CubeVertexKey vertex = TerrainLattice.VertexKey(0, 0, 0);
            Corner = new FormationTerrainPin(vertex, TerrainLattice.Direction(vertex) * radius);
            TileKey[] declared = { new(0, 2, 0, 0) };
            First = FixtureConstruct<FormationCohort>(new FormationId(1, 0), Array.Empty<FormationPiece>(),
                Array.Empty<FormationContactSealReceipt>(), Array.Empty<TerrainLatticeTriangle>(), new[] { Corner }, declared);
            Second = FixtureConstruct<FormationCohort>(new FormationId(2, 0), Array.Empty<FormationPiece>(),
                Array.Empty<FormationContactSealReceipt>(), Array.Empty<TerrainLatticeTriangle>(), new[] { Corner }, declared);
            string digest = new(identity, 64);
            var contract = new FormationFieldContract(digest, radius, 2, FormationFieldAdmissionPolicy.IndependentPieces);
            Formations = FixtureConstruct<FormationField>(contract, digest, Array.Empty<FormationPiece>(), new[] { First, Second },
                Array.Empty<FormationFieldRejection>(), new FormationSupportIndex(Array.Empty<FormationPiece>()),
                default(FormationPlanDiagnostics), default(FormationFieldDiagnostics));
            TerrainPins = TerrainPinSet.Build(this, TerrainLattice, digest, Formations, new TerrainPinSetLimits
            {
                MaxVertices = 4, MaxInputPins = 4, MaxNormalTriangles = 32, MaxAdditionalSamples = 32,
                MaxEstimatedResidentBytes = 8192, MaxEstimatedScratchBytes = 32768,
            });
        }
        public double HeightAt(Vector3D direction)
        {
            Entered.Set();
            if (Block && !Continue.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Cohort fixture did not release its worker.");
            if (Fault) throw new InvalidOperationException("Expected cohort-fixture terrain failure.");
            return 0;
        }
        public double WaterLevelAt(Vector3D direction) => double.NegativeInfinity;
        public GradedTerrainFields RawFieldsAt(Vector3D direction) => new("fixture", 10, 0.5, 0.5);
        public ReadOnlySpan<FormationProtectedSite> ProtectedSites => ReadOnlySpan<FormationProtectedSite>.Empty;
        public void Dispose() { Entered.Dispose(); Continue.Dispose(); }
    }

    // Deliberately isolated diagnostic construction of internal immutable records. This does not bypass the
    // production builder or qualify an asset; it allows native scheduling failures without loading any art.
    private static T FixtureConstruct<T>(params object[] arguments) where T : class =>
        (T)(Activator.CreateInstance(typeof(T), BindingFlags.Instance | BindingFlags.NonPublic, null, arguments, null)
            ?? throw new InvalidOperationException("Missing synthetic cohort fixture constructor: " + typeof(T).Name));
}
